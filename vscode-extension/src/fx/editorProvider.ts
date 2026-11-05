import * as vscode from 'vscode';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { randomBytes } from 'node:crypto';
import { AssetService, getAssetService } from '../ra3xml/services/assetService';
import { AssetIndex, AssetRecord } from './assetIndex';
import { AssetResolver } from './assetResolver';
import { ResourceReady, ResourceService } from './resourceService';
import { parseXml, XmlElement } from './xmlDocument';
import { createAttributePatch, prepareFieldTransaction, prepareTransaction } from './editTransaction';
import { ActivePreview, PreviewSession, parseFxScene } from './previewSession';
import { FxDiagnostics } from './diagnostics';
import { FxTreeProvider } from './treeProvider';
import { FxClientMessage, FxEnvelope, FxField, FxHostMessage, FxScene, PreviewIR, defaultScene } from './previewProtocol';
import { canonicalPath, containsPath, DiscoveredProject, pathIdentity, ResolvedProjectSettings } from '../project';
import { createFxTemplate, escapeXml, FxPreset, fxPresets } from './templates';
import { prepareTextureState, streamPreviewTextures, UploadedTexture } from './texturePipeline';
import { planAssetCopy } from './copyAsset';
import { FxCompiledTextureService, FxTextureInputVersion } from './compiledTextureService';

const viewType = 'newsdk.fxEditor';
const sourceLimit = 4 * 1024 * 1024;
const defaultTextureBudget = 32 * 1024 * 1024;
const textureChunkBytes = 256 * 1024;
type HostPayload = { [K in FxHostMessage['type']]: Omit<Extract<FxHostMessage, { type: K }>, keyof FxEnvelope> }[FxHostMessage['type']];
interface FxServices {
    getProject(): DiscoveredProject | undefined;
    getConfiguration(): ResolvedProjectSettings | undefined;
    saveAndBuild(uris?: vscode.Uri[]): Promise<unknown>;
}
interface OpenRequest { assetKey?: string; entry?: string; previewKey?: string; scene?: FxScene; }
interface EditorState {
    document: vscode.TextDocument; panel: vscode.WebviewPanel; session: PreviewSession;
    index: AssetIndex; resolver: AssetResolver; entry: string; assetKey?: string; previewKey?: string;
    ir?: PreviewIR; scene: FxScene; timer?: ReturnType<typeof setTimeout>; disposed: boolean;
    followCursor: boolean; uploadedTextures: Map<string, UploadedTexture>; subscriptions: vscode.Disposable[];
    indexReleased: boolean; inputFiles: string[]; textureInputs: number; compiled: FxCompiledTextureService;
    s3tc: boolean; transfer?: {key: string; transferId: string; level: number; offset: number; resolve: (accepted: boolean) => void};
    state: Record<string, unknown>;
}
export interface FxApi extends vscode.Disposable {
    refreshProject(): void;
    open(uri: vscode.Uri, assetKey?: string): Promise<void>;
    openShared(uri: string, systemKey: string): Promise<void>;
    copy(uri: string, assetKey: string, systemKey?: string): Promise<void>;
    applyEdits(uri: string, edits: { key: string; value: string; expectedValue: string }[], version: number): Promise<boolean>;
    getSnapshot(uri?: string): { sessionCount: number; activeCount: number; dependencies: string[]; ir?: PreviewIR; documentVersion?: number; sessionId?: string };
}

function asUri(file: string): vscode.Uri { return file.startsWith('file:') ? vscode.Uri.parse(file) : vscode.Uri.file(file); }
function err(error: unknown): string { return error instanceof Error ? error.message : String(error); }
function sameFile(a: string, b: string): boolean { return pathIdentity(asUri(a).fsPath) === pathIdentity(asUri(b).fsPath); }
function systems(ir: PreviewIR) { return [...ir.systems, ...(ir.dynamic?.templates ?? []), ...(ir.dynamic?.fxLists.flatMap(list => list.systems) ?? [])]; }
function fields(ir: PreviewIR): FxField[] {
    const map = new Map<string, FxField>();
    for (const field of [...ir.fields, ...ir.systems.flatMap(system => system.fields), ...(ir.dynamic?.templates.flatMap(system => system.fields) ?? []), ...(ir.dynamic?.fxLists.flatMap(list => list.systems.flatMap(system => system.fields)) ?? [])]) map.set(field.key, field);
    return [...map.values()];
}
function safeUiState(value: unknown): Record<string, unknown> {
    const result: Record<string, unknown> = {};
    if (!value || typeof value !== 'object' || Array.isArray(value)) return result;
    const v = value as Record<string, unknown>;
    for (const key of ['playing', 'followCursor', 'showGround', 'ground', 'lowResolution', 'softParticles']) if (typeof v[key] === 'boolean') result[key] = v[key];
    for (const key of ['time', 'timeSeconds', 'speed', 'zoom', 'frame', 'endFrame']) if (typeof v[key] === 'number' && Number.isFinite(v[key]) && Math.abs(v[key] as number) <= 100000) result[key] = v[key];
    for (const key of ['selectedSystem', 'soloKey', 'background', 'selected', 'solo']) if (typeof v[key] === 'string' && (v[key] as string).length <= 256) result[key] = v[key];
    if (Array.isArray(v.hiddenKeys)) result.hiddenKeys = v.hiddenKeys.filter(x => typeof x === 'string' && x.length <= 256).slice(0, 128);
    if (Array.isArray(v.hidden)) result.hidden = v.hidden.filter(x => typeof x === 'string' && x.length <= 256).slice(0, 128);
    if (v.camera && typeof v.camera === 'object') {
        const camera = v.camera as Record<string, unknown>;
        if (['yaw', 'pitch', 'distance'].every(k => typeof camera[k] === 'number' && Number.isFinite(camera[k]) && Math.abs(camera[k] as number) <= 1e6)
            && Array.isArray(camera.target) && camera.target.length === 3 && camera.target.every(n => typeof n === 'number' && Number.isFinite(n) && Math.abs(n) <= 1e6)) {
            result.camera = { yaw: camera.yaw, pitch: camera.pitch, distance: camera.distance, target: camera.target.slice() };
        }
    }
    const scene = parseFxScene(v.scene); if (scene) result.scene = scene;
    return result;
}

class FxEditorProvider implements vscode.CustomTextEditorProvider, FxApi {
    private readonly editors = new Set<EditorState>();
    private readonly pending = new Map<string, OpenRequest>();
    private readonly active = new ActivePreview();
    private readonly diagnostics = new FxDiagnostics();
    private readonly tree = new FxTreeProvider();
    private readonly subscriptions: vscode.Disposable[] = [];
    private resources?: ResourceService;
    private semantic?:AssetService;
    private rootKey = '';
    private textureBudget = defaultTextureBudget;
    private roots: string[] = [];
    private compiledRoot?: string;
    private watchers: vscode.Disposable[] = [];
    private closed = false;
    constructor(private readonly context: vscode.ExtensionContext, private readonly services: FxServices) {
        this.subscriptions.push(vscode.window.registerCustomEditorProvider(viewType, this, {
            webviewOptions: { retainContextWhenHidden: false }, supportsMultipleEditorsPerDocument: false,
        }));
        this.subscriptions.push(vscode.window.createTreeView('newsdk.fx', { treeDataProvider: this.tree, showCollapseAll: true }));
        const command = (id: string, callback: (...args: unknown[]) => unknown): void => {
            this.subscriptions.push(vscode.commands.registerCommand(id, async (...args: unknown[]) => {
                try { return await callback(...args); } catch (error) { void vscode.window.showErrorMessage(`NewSdk FX：${err(error)}`); return undefined; }
            }));
        };
        command('newsdk.previewFx', (uri, key) => this.open(uri instanceof vscode.Uri ? uri : vscode.window.activeTextEditor?.document.uri,
            typeof key === 'string' ? key : undefined));
        command('newsdk.selectFxSystem', async (uri, key, systemKey) => {
            if (!(uri instanceof vscode.Uri) || typeof key !== 'string' || typeof systemKey !== 'string') return;
            await this.open(uri, key);
            const state = [...this.editors].find(editor => sameFile(editor.document.uri.fsPath, uri.fsPath));
            if (state?.ir && systems(state.ir).some(system => system.key === systemKey)) await this.post(state, {type:'focusSystem', key:systemKey});
        });
        command('newsdk.searchFx', () => this.search());
        command('newsdk.reloadFx', () => this.reload());
        command('newsdk.newFx', () => this.newPreset(this.current()));
        command('newsdk.openFxXml', () => { const state = this.current(); return state ? this.locate(state) : undefined; });
        this.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
            if (!event.contentChanges.length) return;
            const file=event.document.uri.fsPath;
            const normalized=this.semantic?.documents.normalize(file);
            if(normalized&&this.roots.some(root=>containsPath(root,normalized)))this.semantic?.setDocument(normalized,event.document.getText(),event.document.version);
            this.changed(file);
            void this.primeDocument(event.document).then(physical=>{if(physical&&!sameFile(file,physical))this.changed(physical);});
        }));
        this.subscriptions.push(vscode.workspace.onDidCloseTextDocument(document=>{
            if(document.uri.scheme==='file'&&this.semantic?.documents.hasOverlay(document.uri.fsPath))this.semantic.closeDocument(document.uri.fsPath);
        }));
        this.subscriptions.push(vscode.window.onDidChangeTextEditorSelection(event => {
            for (const state of this.editors) if (state.followCursor && state.panel.visible && sameFile(state.document.uri.fsPath, event.textEditor.document.uri.fsPath)) {
                const offset = event.textEditor.document.offsetAt(event.selections[0].active);
                const field = state.ir && fields(state.ir).filter(f => sameFile(f.uri, event.textEditor.document.uri.fsPath) && offset >= f.start && offset <= f.end).sort((a, b) => (a.end - a.start) - (b.end - b.start))[0];
                if (field) { void this.post(state, { type: 'focusField', key: field.key }); continue; }
                const asset = state.index.listAssets().find(a => sameFile(a.uri, state.document.uri.fsPath) && offset >= a.node.start && offset <= a.node.end);
                if (asset && asset.key !== state.assetKey && !state.previewKey) { state.assetKey = asset.key; this.schedule(state, 0); }
            }
        }));
    }
    private current(): EditorState | undefined { return [...this.editors].find(s => s.panel.active) ?? [...this.editors].find(s => s.panel.visible); }

    private async configureRoots(uri?: vscode.Uri): Promise<void> {
        const project = this.services.getProject(); const config = this.services.getConfiguration();
        let mod = project?.root ?? config?.root;
        if (!mod && uri) mod = vscode.workspace.getWorkspaceFolder(uri)?.uri.fsPath;
        if (!mod) throw new Error('请先选择 MOD 目录，或打开 MOD 工作区中的 XML。');
        const roots = await Promise.all([mod, config?.sdkRoot].filter((x): x is string => !!x).map(x => canonicalPath(x)));
        const compiledRoot = config?.outputDirectory ? await canonicalPath(config.outputDirectory) : undefined;
        const resourceRoots = [...new Set([...roots, ...(compiledRoot ? [compiledRoot] : [])])];
        const configuredBudget = vscode.workspace.getConfiguration('newsdk').get<number>('fxTextureBudgetMiB', 32);
        this.textureBudget = (Number.isSafeInteger(configuredBudget) ? Math.max(16, Math.min(256, configuredBudget)) : 32) * 1024 * 1024;
        const key = `${resourceRoots.map(x => pathIdentity(x)).join('|')}|${this.textureBudget}`;
        if (key === this.rootKey) return;
        if (this.rootKey) for (const state of [...this.editors]) { state.panel.dispose(); this.close(state); }
        this.rootKey = key; this.roots = roots; this.compiledRoot = compiledRoot;
        this.resources?.dispose();
        this.resources = new ResourceService({ roots: resourceRoots, cacheBytes: this.textureBudget, decodeBudgetBytes: this.textureBudget * 2, transientBytes: this.textureBudget * 2, sourceBytes: this.textureBudget * 2 });
        for (const watcher of this.watchers) watcher.dispose(); this.watchers = [];
        for (const root of resourceRoots) {
            const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, '**/*'));
            const changed = (file: vscode.Uri): void => {
                if (/[\\/](?:SuperCompilerCache|node_modules|\.git)[\\/]/i.test(file.fsPath)) return;
                for (const state of this.editors) for (const resource of state.uploadedTextures.keys()) if (sameFile(resource, file.fsPath)) state.uploadedTextures.delete(resource);
                this.resources?.invalidate(file.fsPath); this.changed(file.fsPath);
            };
            this.watchers.push(watcher, watcher.onDidChange(changed), watcher.onDidCreate(changed), watcher.onDidDelete(changed));
        }
    }
    private async allowed(file: string, editable = false): Promise<string> {
        const physical = await canonicalPath(asUri(file).fsPath);
        if (!this.roots.some(root => containsPath(root, physical))) throw new Error('文件不在当前 MOD / SDK 资源根中。');
        const sdk = this.services.getConfiguration()?.sdkRoot;
        const mod = this.services.getProject()?.root ?? this.roots[0];
        if (editable && sdk) {
            const sdkRoot = await canonicalPath(sdk); const modRoot = await canonicalPath(mod);
            const nestedMod = pathIdentity(sdkRoot) !== pathIdentity(modRoot) && containsPath(sdkRoot, modRoot);
            if (containsPath(sdkRoot, physical) && !(nestedMod && containsPath(modRoot, physical))) throw new Error('SDK 来源只读，请复制为 MOD 独立资源后编辑。');
        }
        if (editable && !vscode.workspace.isTrusted) throw new Error('信任此工作区后才能修改特效。');
        return physical;
    }
    private async allowedCompiled(file: string): Promise<string> {
        const physical = await canonicalPath(asUri(file).fsPath);
        if (![...this.roots, ...(this.compiledRoot ? [this.compiledRoot] : [])].some(root => containsPath(root, physical)))
            throw new Error('编译贴图不在当前 MOD / SDK / 已配置输出目录中。');
        return physical;
    }
    private async newIndex(): Promise<AssetIndex> {
        const semantic=getAssetService({ storagePath:this.context.globalStorageUri.fsPath, roots: this.roots, modRoot: this.services.getProject()?.root ?? this.roots[0],
            sdkRoot: this.services.getConfiguration()?.sdkRoot,
            readText: async file => {
                const physical = await this.allowed(file);
                let open = vscode.workspace.textDocuments.find(d => d.uri.scheme === 'file' && sameFile(d.uri.fsPath, physical));
                if (!open) for (const candidate of vscode.workspace.textDocuments) {
                    if (candidate.uri.scheme !== 'file' || !/\.xml$/i.test(candidate.uri.fsPath)) continue;
                    try { if (sameFile(await canonicalPath(candidate.uri.fsPath), physical)) { open = candidate; break; } } catch { /* A deleted unrelated document is not an alias. */ }
                }
                if (open) {
                    if (Buffer.byteLength(open.getText(), 'utf8') > sourceLimit) throw new Error('XML 超过单文档 4 MiB 解析预算。');
                    return { text: open.getText(), version: open.version };
                }
                const stat = await fs.stat(physical); if (stat.size > sourceLimit) throw new Error('XML 超过单文档 4 MiB 解析预算。');
                return { text: await fs.readFile(physical, 'utf8'), version: 0 };
            } });
        this.semantic=semantic;
        await Promise.all(vscode.workspace.textDocuments.map(document=>this.primeDocument(document)));
        if(this.semantic!==semantic)throw new Error('MOD 项目已切换，请重新打开特效。');
        return semantic.createView();
    }
    private async primeDocument(document:vscode.TextDocument):Promise<string|undefined>{
        const semantic=this.semantic;
        if(!semantic||document.isClosed||document.uri.scheme!=='file'||!/\.xml$/i.test(document.uri.fsPath))return;
        try{
            const physical=await semantic.index.checkedFile(await canonicalPath(document.uri.fsPath));
            semantic.documents.alias(document.uri.fsPath,physical);
            if(document.isClosed||this.semantic!==semantic)return;
            semantic.setDocument(physical,document.getText(),document.version);return physical;
        }catch{return;}
    }
    async open(uri?: vscode.Uri, assetKey?: string): Promise<void> {
        if (!uri || uri.scheme !== 'file' || !/\.xml$/i.test(uri.fsPath)) throw new Error('请打开本地 XML 文件，或从特效树选择资产。');
        await this.configureRoots(uri);
        const file = await this.allowed(uri.fsPath); const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
        const requestedKey = assetKey;
        const suppliedAsset = requestedKey ? [...this.editors].map(s => s.index.getAsset(requestedKey)).find((asset): asset is AssetRecord => !!asset) : undefined;
        const existing = [...this.editors].find(s => sameFile(s.document.uri.fsPath, file));
        if (existing) {
            if (assetKey) { existing.assetKey = assetKey; existing.previewKey = undefined; if (suppliedAsset) existing.entry = suppliedAsset.entry; }
            existing.panel.reveal(); await this.rebuild(existing); return;
        }
        let entry = suppliedAsset?.entry ?? file;
        const mod = this.services.getProject()?.root ?? this.roots[0];
        const candidate = path.join(mod, 'Data', 'Mod.xml');
        try { if (!suppliedAsset) { await fs.access(candidate); entry = candidate; } } catch { /* Standalone XML remains a useful explicit entry. */ }
        const probe = await this.newIndex();
        try {
            await probe.load(entry);
            let assets = probe.listAssets().filter(a => sameFile(a.uri, file) && (a.type === 'FXList' || a.type === 'FXParticleSystemTemplate'));
            if (!assets.length && !sameFile(entry, file)) { entry = file; await probe.load(entry); assets = probe.listAssets().filter(a => sameFile(a.uri, file) && (a.type === 'FXList' || a.type === 'FXParticleSystemTemplate')); }
            if (!assetKey) {
                const native = vscode.window.activeTextEditor;
                const offset = native && sameFile(native.document.uri.fsPath, file) ? document.offsetAt(native.selection.active) : -1;
                assetKey = assets.find(a => offset >= a.node.start && offset <= a.node.end)?.key;
                if (!assetKey && assets.length === 1) assetKey = assets[0].key;
                if (!assetKey && assets.length > 1) assetKey = (await vscode.window.showQuickPick(assets.map(a => ({ label: a.id, description: a.type, key: a.key })), { title: '选择要预览的特效' }))?.key;
                if (!assetKey && assets.length > 1) return;
            }
        } finally { probe.dispose(); }
        this.pending.set(document.uri.toString(), { assetKey, entry });
        await vscode.commands.executeCommand('vscode.openWith', document.uri, viewType, { viewColumn: vscode.ViewColumn.One, preview: false });
        const state = [...this.editors].find(s => sameFile(s.document.uri.fsPath, file));
        if (state) await this.locate(state);
    }

    async resolveCustomTextEditor(document: vscode.TextDocument, panel: vscode.WebviewPanel, token: vscode.CancellationToken): Promise<void> {
        await this.configureRoots(document.uri); await this.allowed(document.uri.fsPath);
        if (token.isCancellationRequested || this.closed) return;
        const request = this.pending.get(document.uri.toString()); this.pending.delete(document.uri.toString());
        const session = new PreviewSession(document.version); const index = await this.newIndex();
        if(token.isCancellationRequested||this.closed){session.dispose();index.dispose();return;}
        const stored = safeUiState(this.context.workspaceState.get(`newsdk.fx:${document.uri.toString()}`, {}));
        const state: EditorState = { document, panel, session, index, resolver: new AssetResolver(index),
            entry: request?.entry ?? document.uri.fsPath, assetKey: request?.assetKey, previewKey: request?.previewKey,
            scene: request?.scene ?? parseFxScene(stored.scene) ?? { ...defaultScene, source: [...defaultScene.source], target: [...defaultScene.target] },
            disposed: false, followCursor: stored.followCursor === true, state: stored, uploadedTextures: new Map(), subscriptions: [], s3tc: false, indexReleased: false, inputFiles: [], textureInputs: 0, compiled: new FxCompiledTextureService(this.resources!) };
        this.editors.add(state);
        panel.webview.options = { enableScripts: true, localResourceRoots: [vscode.Uri.joinPath(this.context.extensionUri, 'media')] };
        panel.webview.html = this.html(panel.webview, session, stored);
        state.subscriptions.push(panel.webview.onDidReceiveMessage((value: unknown) => { void this.dispatch(state, value); }));
        state.subscriptions.push(panel.onDidChangeViewState(() => {
            if (panel.visible && panel.active) {
                this.active.activate(session, () => this.stopCanvas(state));
                if (session.signal.aborted || state.indexReleased || !state.ir) this.schedule(state, 0);
                void this.post(state, { type: 'active', active: true });
            } else if (!panel.visible) {
                this.active.release(session); state.uploadedTextures.clear();
                if (state.timer) { clearTimeout(state.timer); state.timer = undefined; }
            }
        }));
        state.subscriptions.push(panel.onDidDispose(() => this.close(state)));
        if (panel.active) this.active.activate(session, () => this.stopCanvas(state));
        // Returning promptly lets VS Code finish the editor before webview handshakes.
        void this.rebuild(state).catch(error => { if (!state.disposed) void this.status(state, err(error), !!state.ir); });
    }
    private stopCanvas(state: EditorState): void {
        void this.post(state, { type: 'active', active: false });
        state.uploadedTextures.clear();
        state.index.dispose(); state.indexReleased = true; state.ir = undefined;
        this.diagnostics.remove(state.session.id);
        // No host decoder worker remains alive while every canvas is suspended.
        if (![...this.editors].some(editor => editor.session.visible && !editor.session.signal.aborted)) this.resources?.clear();
    }
    private async post(state: EditorState, message: HostPayload): Promise<void> {
        if (state.disposed) return;
        await state.panel.webview.postMessage({ ...message, sessionId: state.session.id,
            documentVersion: state.document.version, requestId: state.session.requestId });
    }
    private status(state: EditorState, message: string, stale = false): Promise<void> { return this.post(state, { type: 'status', message, stale }); }
    private schedule(state: EditorState, delay = 150): void {
        if (state.disposed) return;
        state.session.renew(state.document.version);
        if (state.timer) clearTimeout(state.timer);
        if (!state.panel.visible || (this.active.hasActive && !this.active.owns(state.session))) return;
        state.timer = setTimeout(() => { state.timer = undefined; void this.rebuild(state).catch(error => this.status(state, err(error), !!state.ir)); }, delay);
    }
    private changed(file: string): void {
        for (const state of this.editors) {
            // Keep only bounded input identities when a hidden page releases its graph.
            const relevant = sameFile(file, state.document.uri.fsPath) || state.inputFiles.some(uri => sameFile(uri, file))
                || (!state.indexReleased && (state.index.graph.dependencies.some(uri => sameFile(uri, file))
                    || state.index.graph.includes.some(edge => sameFile(edge.to, file))));
            if (!relevant) continue;
            state.textureInputs++;
            void this.post(state, { type: 'invalidateDraft', message: '内容已更新，临时调节已取消；请依据当前文档重新应用。' });
            if (!state.indexReleased) state.index.invalidate(file);
            this.schedule(state);
        }
    }
    private async rebuild(state: EditorState): Promise<void> {
        if (state.disposed || !state.panel.visible || (this.active.hasActive && !this.active.owns(state.session))) return;
        if (state.indexReleased) { const index=await this.newIndex();if(state.disposed){index.dispose();return;}state.index=index;state.resolver = new AssetResolver(state.index); state.indexReleased = false; }
        const { requestId, signal } = state.session.renew(state.document.version);
        try {
        if(Buffer.byteLength(state.document.getText(),'utf8')>sourceLimit)throw new Error('XML 超过单文档 4 MiB FX 解析预算。');
        state.index.documentStore.set(state.document.uri.fsPath,state.document.getText(),state.document.version);
        const model = await state.index.documentStore.read(state.document.uri.fsPath);
        if (!model.valid) {
            await this.diagnostics.update(state.session.id, model.issues.map(issue => ({ ...issue, uri: state.document.uri.fsPath, severity: 'error' })));
            await this.status(state, `XML 尚未完整；保留上一有效画面（文档版本 ${state.ir?.documentVersion ?? '—'}）。`, true); return;
        }
        await state.index.load(state.entry, signal); if (!state.session.current(requestId)) return;
        const assets = state.index.listAssets().filter(a => a.type === 'FXList' || a.type === 'FXParticleSystemTemplate');
        const selected = state.index.getAsset(state.previewKey ?? state.assetKey ?? '')
            ?? assets.find(a => sameFile(a.uri, state.document.uri.fsPath)) ?? assets[0];
        this.tree.update(assets, state.ir);
        if (!selected) { await this.status(state, '此入口未找到 FXList 或 FXParticleSystemTemplate。'); return; }
        if (!state.previewKey) state.assetKey = selected.key;
        const ir = await state.resolver.resolve(selected.key, state.scene, signal); if (!state.session.current(requestId)) return;
        state.compiled.applySelection(ir, this.textureInput(state));
        state.inputFiles = [...new Set([...ir.dependencies, ...ir.textures.flatMap(texture => [texture.file, texture.sourceFile, texture.compiled?.path].filter((file): file is string => !!file))])].slice(0, 16384);
        prepareTextureState(ir, state.uploadedTextures);
        state.ir = ir; state.scene = ir.scene;
        state.panel.title = state.previewKey ? `特效 · 编辑 ${path.basename(state.document.uri.fsPath)} / 预览 ${ir.name}` : `特效 · ${ir.name}`;
        this.tree.update(assets, ir);
        await this.diagnostics.update(state.session.id, [...ir.issues, ...systems(ir).flatMap(system => system.issues)]);
        if (!state.session.current(requestId)) return;
        await this.post(state, { type: 'preview', ir, assets: assets.map(a => ({ key: a.key, name: a.id, type: a.type })), editingUri: state.document.uri.fsPath, textureBudgetBytes: this.textureBudget });
        const resources = this.resources;
        if (resources) await streamPreviewTextures(ir, resources, { uploaded: state.uploadedTextures, signal, maxBytes: this.textureBudget,
            current: () => state.session.current(requestId),
            transfer: (key, loaded) => this.sendTexture(state, key, loaded, requestId, signal),
            publish: async (previousKey, texture, issue) => {
                if (!state.session.current(requestId)) return;
                await this.post(state, { type: 'textureState', previousKey, texture, issue });
                if (issue && state.session.current(requestId)) await this.diagnostics.update(state.session.id, [...ir.issues, ...systems(ir).flatMap(system => system.issues)]);
            } });
        } catch (error) {
            // A later ready/change/activation owns the state; cancellation is normal.
            if (!state.session.current(requestId)) return;
            throw error;
        }
    }
    private textureInput(state: EditorState): FxTextureInputVersion {
        return {documentUri: state.document.uri.toString(), documentVersion: state.document.version,
            savedDocumentVersion: state.document.isDirty ? undefined : state.document.version, inputGeneration: state.textureInputs};
    }
    private async importCompiled(state: EditorState, key: string): Promise<void> {
        const texture = state.ir?.textures.find(item => item.key === key);
        if (!texture?.file) return;
        const input = this.textureInput(state); const {requestId, signal} = state.session;
        const files = await vscode.window.showOpenDialog({title:'从 MOD / SDK / 已配置输出目录导入 DDS 编译结果', canSelectMany:false,
            defaultUri:asUri(texture.compiled?.path ?? texture.file), filters:{DDS:['dds']}});
        if (!files?.[0] || !state.session.current(requestId)) return;
        const file = await this.allowedCompiled(files[0].fsPath);
        const result = await state.compiled.importResult(texture, file, input, {signal});
        if (!state.session.current(requestId)) return;
        if (result.status !== 'imported') { await this.status(state, result.message); return; }
        await this.rebuild(state);
    }
    private async sendTexture(state: EditorState, key: string, loaded: ResourceReady, requestId: number, signal: AbortSignal): Promise<boolean> {
        const image = loaded.texture;
        const transferId = randomBytes(12).toString('hex');
        const compressed = state.s3tc && image.compressedMips && (image.format === 'DXT1' || image.format === 'DXT3' || image.format === 'DXT5');
        const levels = compressed ? image.compressedMips!.map(mip => ({ width: mip.width, height: mip.height, bytes: mip.bytes }))
            : image.mips.map(mip => ({ width: mip.width, height: mip.height, bytes: mip.pixels }));
        if (levels.reduce((n, level) => n + level.bytes.byteLength, 0) > this.textureBudget) { await this.status(state, `贴图传输超过 ${this.textureBudget / 1048576} MiB 预览预算；可调整 newsdk.fxTextureBudgetMiB。`); return false; }
        await this.post(state, { type: 'textureBegin', texture: { key, transferId, width: image.width, height: image.height,
            format: compressed ? image.format as 'DXT1' | 'DXT3' | 'DXT5' : 'RGBA8', levels: levels.map(level => ({ width: level.width, height: level.height, byteLength: level.bytes.byteLength })) } });
        for (let level = 0; level < levels.length; level++) {
            const bytes = levels[level].bytes;
            for (let offset = 0; offset < bytes.byteLength; offset += textureChunkBytes) {
                if (!state.session.current(requestId)) return false;
                const end = Math.min(offset + textureChunkBytes, bytes.byteLength);
                const accepted = this.awaitTextureAck(state, key, transferId, level, end, signal);
                await this.post(state, { type: 'textureChunk', key, transferId, level, offset, total: bytes.byteLength,
                    pixelsBase64: Buffer.from(bytes.buffer, bytes.byteOffset + offset, end - offset).toString('base64') });
                if (!await accepted) { if (!signal.aborted) await this.status(state, '贴图传输已暂停，请重载资源后重试。'); return false; }
            }
        }
        if (!state.session.current(requestId)) return false;
        await this.post(state, { type: 'textureEnd', key, transferId }); return true;
    }
    private awaitTextureAck(state: EditorState, key: string, transferId: string, level: number, offset: number, signal: AbortSignal): Promise<boolean> {
        return new Promise(resolve => {
            const finish = (accepted: boolean): void => { clearTimeout(timer); signal.removeEventListener('abort', cancelled); state.transfer = undefined; resolve(accepted); };
            const cancelled = (): void => finish(false);
            const timer = setTimeout(() => finish(false), 10000);
            state.transfer = { key, transferId, level, offset, resolve: finish };
            signal.addEventListener('abort', cancelled, { once: true });
            if (signal.aborted) finish(false);
        });
    }
    private dispatch(state: EditorState, value: unknown): Promise<void> {
        const type = value && typeof value === 'object' ? (value as Record<string, unknown>).type : undefined;
        const run = (): Promise<void> => this.receive(state, value).catch(error => this.status(state, err(error), type === 'edit' || type === 'overrideField'));
        // Texture ACKs bypass document actions, which may themselves await uploads.
        return ['edit','save','saveAndBuild','undo','redo','new','copy','overrideField','select','openShared','openFieldSource'].includes(String(type))
            ? state.session.enqueueDocumentAction(run) : run();
    }
    private async receive(state: EditorState, value: unknown): Promise<void> {
        const ready = !!value && typeof value === 'object' && (value as Record<string, unknown>).type === 'ready';
        const type = value && typeof value === 'object' ? (value as Record<string, unknown>).type : undefined;
        const requiresVersion = !ready && !['save','saveAndBuild','undo','redo','state','textureAck'].includes(String(type));
        if (!state.session.accepts(value, requiresVersion)) {
            if (value && typeof value === 'object' && (value as Record<string, unknown>).sessionId === state.session.id) await this.status(state, '内容已更新，请重新应用。', !!state.ir);
            return;
        }
        const message = value as FxClientMessage;
        switch (message.type) {
            case 'ready':
                if (!state.panel.visible) return;
                state.s3tc = message.s3tc === true;
                if (state.panel.active || !this.active.hasActive || this.active.owns(state.session)) this.active.activate(state.session, () => this.stopCanvas(state));
                else { state.session.suspend(); this.stopCanvas(state); return; }
                state.uploadedTextures.clear(); await this.rebuild(state); break;
            case 'reload': await this.reload(state, message.resetRenderer === true); break;
            case 'undo': case 'redo':
                if (!state.session.acceptMutation(message.requestId)) return;
                if (state.panel.active) await vscode.commands.executeCommand(message.type); break;
            case 'save':
                if (!state.session.acceptMutation(message.requestId)) return;
                if (!await state.document.save()) await this.status(state, '保存失败或已取消；修改仍保留在脏文档中。');
                else await this.status(state, `已保存 ${path.basename(state.document.uri.fsPath)}。`);
                break;
            case 'saveAndBuild':
                if (!state.session.acceptMutation(message.requestId)) return;
                await this.services.saveAndBuild(state.ir?.dependencies.map(asUri)); break;
            case 'edit':
                if (!state.session.acceptMutation(message.requestId)) return;
                await this.apply(state, message.edits, message.documentVersion); break;
            case 'locate':
                if (state.ir && typeof message.key === 'string') {
                    if (/^issue:\d+$/.test(message.key)) await this.locate(state, state.ir.issues[Number(message.key.slice(6))]);
                    else if (message.key.startsWith('issue:')) {
                        const at = message.key.lastIndexOf(':'); const system = systems(state.ir).find(s => s.key === message.key.slice(6, at));
                        const index = Number(message.key.slice(at + 1)); if (system && Number.isSafeInteger(index) && index >= 0) await this.locate(state, system.issues[index]);
                    } else await this.locate(state, fields(state.ir).find(f => f.key === message.key));
                }
                break;
            case 'select':
                if (typeof message.assetKey !== 'string' || !state.index.getAsset(message.assetKey)) return;
                {
                    const asset = state.index.getAsset(message.assetKey)!;
                    if (!sameFile(asset.uri, state.document.uri.fsPath)) await this.openSharedAsset(state, asset, false);
                    else { state.assetKey = asset.key; state.previewKey = undefined; await this.rebuild(state); }
                }
                break;
            case 'scene': {
                const scene = parseFxScene(message.scene);
                if (scene) {
                    state.scene = scene;
                    // Cancel old scene resolves before persistence yields to them.
                    state.session.renew(state.document.version);
                    await this.context.workspaceState.update(`newsdk.fx:${state.document.uri.toString()}`, { ...state.state, scene });
                    await this.rebuild(state);
                }
                break;
            }
            case 'openShared': {
                const system = state.ir && systems(state.ir).find(s => s.key === message.systemKey); const asset = system && state.index.getAsset(system.templateKey);
                if (asset) await this.openSharedAsset(state, asset, true); break;
            }
            case 'openFieldSource': {
                const field = state.ir && fields(state.ir).find(f => f.key === message.key);
                const asset = field?.sourceAsset && state.index.getAsset(field.sourceAsset);
                if (asset) await this.openSharedAsset(state, asset, true); break;
            }
            case 'overrideField':
                if (typeof message.key === 'string' && typeof message.value === 'string' && typeof message.expectedValue === 'string'
                    && message.value.length <= 4096 && state.session.acceptMutation(message.requestId)) await this.overrideField(state, message.key, message.value, message.expectedValue);
                break;
            case 'new': if (fxPresets.includes(message.preset as FxPreset)) await this.newPreset(state, message.preset as FxPreset); break;
            case 'copy': if (typeof message.assetKey === 'string') await this.copyAsset(state, message.assetKey, typeof message.systemKey === 'string' ? message.systemKey : undefined); break;
            case 'state':
                state.state = safeUiState(message.state); state.followCursor = state.state.followCursor === true;
                await this.context.workspaceState.update(`newsdk.fx:${state.document.uri.toString()}`, { ...state.state, scene: state.scene }); break;
            case 'textureSource': {
                const texture = state.ir?.textures.find(t => t.key === message.key);
                if (texture && (message.source === 'source' || message.source === 'compiled') && state.compiled.select(texture, message.source)) await this.rebuild(state);
                break;
            }
            case 'importCompiledTexture': await this.importCompiled(state, message.key); break;
            case 'textureAck': {
                const pending = state.transfer;
                if (pending && pending.key === message.key && pending.transferId === message.transferId && pending.level === message.level && pending.offset === message.offset) pending.resolve(true);
                break;
            }
            default: break;
        }
    }
    async applyEdits(uri: string, edits: { key: string; value: string; expectedValue: string }[], version: number): Promise<boolean> {
        const state = [...this.editors].find(s => sameFile(s.document.uri.fsPath, uri));
        if (!state) return false; return this.apply(state, edits, version);
    }
    async openShared(uri: string, systemKey: string): Promise<void> {
        const state = [...this.editors].find(s => sameFile(s.document.uri.fsPath, uri));
        const system = state?.ir && systems(state.ir).find(s => s.key === systemKey);
        const asset = state && system && state.index.getAsset(system.templateKey);
        if (state && asset) await this.openSharedAsset(state, asset, true);
    }
    private async apply(state: EditorState, edits: { key: string; value: string; expectedValue: string }[], version: number): Promise<boolean> {
        if (!state.ir || !Array.isArray(edits) || !edits.length || edits.length > 64) return false;
        await this.allowed(state.document.uri.fsPath, true);
        if (state.document.version !== version) { await this.status(state, '内容已更新，请重新应用。', true); return false; }
        const registered = new Map(fields(state.ir).map(f => [f.key, f]));
        const intents = [];
        for (const edit of edits) {
            if (!edit || typeof edit.key !== 'string' || typeof edit.value !== 'string' || typeof edit.expectedValue !== 'string' || edit.value.length > 4096) return false;
            const field = registered.get(edit.key);
            if (!field || field.readonly || !sameFile(field.uri, state.document.uri.fsPath)) { await this.status(state, '此字段来自共享或只读定义，请先打开实际定义。', true); return false; }
            if (field.value !== edit.expectedValue) { await this.status(state, '内容已更新，请重新应用。', true); return false; }
            intents.push({ nodePath: field.nodePath, attribute: field.attribute, value: edit.value, expectedValue: field.defaulted ? undefined : edit.expectedValue, documentVersion: version });
        }
        const model = parseXml(state.document.getText(), state.document.uri.fsPath, state.document.version);
        const patches = prepareFieldTransaction(model, intents);
        const workspaceEdit = new vscode.WorkspaceEdit();
        for (const patch of patches) workspaceEdit.replace(state.document.uri,
            new vscode.Range(state.document.positionAt(patch.start), state.document.positionAt(patch.end)), patch.text);
        if (state.document.version !== version) return false;
        const applied = await vscode.workspace.applyEdit(workspaceEdit);
        if (!applied) await this.status(state, '未能应用字段修改；文档内容保留。', true);
        return applied;
    }
    private async locate(state: EditorState, field?: Pick<FxField, 'uri' | 'start' | 'end'> & Partial<Pick<FxField, 'nodePath' | 'attribute' | 'value' | 'sourceAsset'>>): Promise<void> {
        const file = field?.uri ?? state.document.uri.fsPath; await this.allowed(file);
        const document = await vscode.workspace.openTextDocument(asUri(file));
        const editor = await vscode.window.showTextDocument(document, { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true, preview: false });
        if (field) {
            let start = field.start, end = field.end;
            if (field.nodePath) {
                const model = parseXml(document.getText(), document.uri.fsPath, document.version), node = model.find(field.nodePath);
                if (!node) { await this.status(state, '源码节点已更新，请等待面板同步后重新定位。', true); this.schedule(state, 0); return; }
                const sourceAsset = field.sourceAsset && state.index.getAsset(field.sourceAsset);
                let owning = node; while (owning.parent && !owning.attributes.id) owning = owning.parent;
                if (sourceAsset && owning.attributes.id !== sourceAsset.id) { await this.status(state, '源码身份已更新，请等待面板同步后重新定位。', true); this.schedule(state, 0); return; }
                const token = field.attribute && node.attributeTokens[field.attribute];
                if (token && field.value !== undefined && token.value !== field.value) { await this.status(state, '字段已更新，请等待面板同步后重新定位。', true); this.schedule(state, 0); return; }
                start = token ? token.valueStart : node.start; end = token ? token.valueEnd : node.openEnd;
            }
            const range = new vscode.Range(document.positionAt(start), document.positionAt(end)); editor.selection = new vscode.Selection(range.start, range.end); editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
        }
    }
    private async openSharedAsset(state: EditorState, asset: AssetRecord, keepRoot: boolean): Promise<void> {
        await this.allowed(asset.uri);
        const uri = asUri(asset.uri);
        const existing = [...this.editors].find(s => sameFile(s.document.uri.fsPath, asset.uri));
        if (existing) { existing.entry = state.entry; existing.assetKey = asset.key; existing.previewKey = keepRoot ? state.ir?.assetKey : undefined; if (keepRoot) existing.scene = structuredClone(state.scene); existing.panel.reveal(); await this.rebuild(existing); await this.locate(existing); return; }
        this.pending.set(uri.toString(), { entry: state.entry, assetKey: asset.key, previewKey: keepRoot ? state.ir?.assetKey : undefined, scene: keepRoot ? structuredClone(state.scene) : undefined });
        await vscode.commands.executeCommand('vscode.openWith', uri, viewType, { viewColumn: vscode.ViewColumn.One, preview: false });
        const opened = [...this.editors].find(s => sameFile(s.document.uri.fsPath, asset.uri)); if (opened) await this.locate(opened);
    }
    private async reload(state = this.current(), resetRenderer = true): Promise<void> {
        if (!state) { await this.search(); return; }
        if (resetRenderer) await this.post(state, { type: 'resetRenderer' });
        this.resources?.clear();
        for (const editor of this.editors) { editor.textureInputs++; editor.uploadedTextures.clear(); }
        for (const file of state.ir?.dependencies ?? [state.entry]) state.index.invalidate(file);
        await this.rebuild(state);
    }
    private async search(): Promise<void> {
        const current = this.current();
        if (current) {
            const selected = await vscode.window.showQuickPick(current.index.listAssets().filter(a => a.type === 'FXList' || a.type === 'FXParticleSystemTemplate')
                .map(a => ({ label: a.id, description: `${a.type} · ${path.basename(a.uri)}`, asset: a })), { title: '搜索当前入口的特效', matchOnDescription: true });
            if (selected) await this.openSharedAsset(current, selected.asset, false); return;
        }
        const uri = vscode.window.activeTextEditor?.document.uri;
        await this.configureRoots(uri);
        const entry = path.join(this.services.getProject()?.root ?? this.roots[0], 'Data', 'Mod.xml');
        const index = await this.newIndex();
        try {
            await index.load(entry); const assets = index.listAssets().filter(a => a.type === 'FXList' || a.type === 'FXParticleSystemTemplate'); this.tree.update(assets);
            const selected = await vscode.window.showQuickPick(assets.map(asset => ({ label: asset.id, description: asset.type, asset })), { title: '搜索当前入口的特效' });
            if (selected) { this.pending.set(asUri(selected.asset.uri).toString(), { entry, assetKey: selected.asset.key }); await vscode.commands.executeCommand('vscode.openWith', asUri(selected.asset.uri), viewType, { preview: false }); }
        } finally { index.dispose(); }
    }
    private async newPreset(state = this.current(), preset?: FxPreset): Promise<void> {
        const document = state?.document ?? vscode.window.activeTextEditor?.document;
        if (!document || document.uri.scheme !== 'file' || !/\.xml$/i.test(document.uri.fsPath)) throw new Error('请先打开要插入资产的 MOD XML。');
        await this.configureRoots(document.uri); await this.allowed(document.uri.fsPath, true);
        const model = parseXml(document.getText(), document.uri.fsPath, document.version);
        const root = model.roots.find(r => r.localName === 'AssetDeclaration');
        if (!model.valid || !root || root.selfClosing) throw new Error('需要完整的、带结束标签的 AssetDeclaration 文档。');
        preset ??= await vscode.window.showQuickPick([...fxPresets], { title: '新建特效' }) as FxPreset | undefined;
        if (!preset) return;
        const id = await vscode.window.showInputBox({ title: '新特效 id', value: `New${['Flash', 'Smoke', 'Spark', 'Halo', 'Trail', 'Lightning', 'Swarm'][fxPresets.indexOf(preset)]}`,
            validateInput: value => !/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(value) ? '使用字母、数字、下划线、点或连字符。' : model.elements.some(e => e.attributes.id === value) ? '此 id 已存在。' : undefined });
        if (!id) return;
        const textures = state?.index.listAssets().filter(a => a.type === 'Texture') ?? [];
        const texture = await vscode.window.showQuickPick([{ label: '稍后选择贴图', id: '' }, ...textures.map(a => ({ label: a.id, id: a.id }))], { title: '选择贴图；暂缺素材将明确标为未选择' });
        if (!texture) return;
        const prefix = root.name.includes(':') ? `${root.name.split(':')[0]}:` : '';
        const xml = createFxTemplate(preset, id, texture.id || undefined, prefix);
        const version = document.version;
        const confirmed = await vscode.window.showInformationMessage(`向 ${path.basename(document.uri.fsPath)} 插入 ${id}${texture.id ? `，引用 ${texture.id}` : '（尚未选择贴图）'}`, { modal: true, detail: xml }, '插入');
        if (confirmed !== '插入') return;
        if (state?.disposed || version !== document.version) throw new Error('内容已更新，请重新新建。');
        await this.allowed(document.uri.fsPath, true);
        if (state?.disposed || version !== document.version) throw new Error('内容已更新，请重新新建。');
        const edit = new vscode.WorkspaceEdit(); edit.insert(document.uri, document.positionAt(root.closeStart), `\n${xml}\n`);
        if (!await vscode.workspace.applyEdit(edit)) throw new Error('未能插入特效，文档保留。');
        if (state) { state.previewKey = undefined; state.assetKey = undefined; await this.rebuild(state); }
        else await this.open(document.uri);
    }
    private async overrideField(state: EditorState, key: string, value: string, expectedValue: string): Promise<void> {
        if (!state.ir) return;
        const field = fields(state.ir).find(f => f.key === key);
        const selected = state.index.getAsset(state.assetKey ?? state.ir.assetKey);
        if (!field || !field.inherited || field.value !== expectedValue || !selected || !sameFile(selected.uri, state.document.uri.fsPath)) throw new Error('字段或编辑目标已更新，请重载后重试。');
        if (selected.type !== 'FXParticleSystemTemplate') throw new Error('图形覆盖当前支持普通粒子模板字段；请在 XML 中明确组合继承的覆盖规则。');
        await this.allowed(state.document.uri.fsPath, true); await this.allowed(field.uri);
        const model = parseXml(state.document.getText(), state.document.uri.fsPath, state.document.version);
        const targetAsset = model.find(selected.node.path);
        const sourceDocument = await vscode.workspace.openTextDocument(asUri(field.uri));
        const sourceModel = parseXml(sourceDocument.getText(), sourceDocument.uri.fsPath, sourceDocument.version);
        const sourceNode = sourceModel.find(field.nodePath);
        if (!model.valid || !sourceModel.valid || !targetAsset || !sourceNode) throw new Error('文档正在变化，请修复 XML 后重试。');
        const sourceAsset = state.index.getAsset(field.sourceAsset ?? '');
        const sourceAssetNode = sourceAsset && sourceModel.find(sourceAsset.node.path);
        if (!sourceAssetNode) throw new Error('未找到继承字段的实际来源。');
        const chain: XmlElement[] = []; let item: XmlElement | undefined = sourceNode;
        while (item && item !== sourceAssetNode) { chain.unshift(item); item = item.parent; }
        if (!item) throw new Error('字段不属于已确认的继承来源。');
        let target = targetAsset; let missing: XmlElement | undefined;
        for (const child of chain) {
            const match = target.children.find(c => c.localName === child.localName
                && (child.attributes.Frame === undefined || c.attributes.Frame === child.attributes.Frame)
                && (child.attributes.id === undefined || c.attributes.id === child.attributes.id));
            if (match) target = match; else { missing = child; break; }
        }
        const edit = new vscode.WorkspaceEdit(); let detail: string;
        if (!missing) {
            if (target.attributes[field.attribute] !== undefined) throw new Error('本资产已有局部字段，请重载后直接编辑。');
            const patches = prepareFieldTransaction(model, [{ nodePath: target.path, attribute: field.attribute, value, expectedValue: undefined, documentVersion: model.version }]);
            for (const patch of patches) edit.replace(state.document.uri, new vscode.Range(state.document.positionAt(patch.start), state.document.positionAt(patch.end)), patch.text);
            detail = `在 ${target.name} 中新增 ${field.attribute}="${escapeXml(value)}"\n继承来源：${field.uri}\n原值：${expectedValue}`;
        } else {
            const choice = new Set(['Physics', 'Draw', 'Volume', 'Velocity', 'Update']);
            if (choice.has(target.localName) && target.children.length) throw new Error('当前模块选择与父定义不兼容，请在 XML 中明确切换模块。');
            const token = sourceNode.attributeTokens[field.attribute]; if (!token) throw new Error('继承来源没有可复制的属性 token。');
            let fragment = sourceDocument.getText().slice(missing.start, missing.end);
            const replacement = createAttributePatch(sourceModel, {nodePath:sourceNode.path, attribute:field.attribute,
                value, expectedValue:token.value, documentVersion:sourceModel.version});
            fragment = fragment.slice(0, token.valueStart - missing.start) + replacement.text + fragment.slice(token.valueEnd - missing.start);
            fragment = this.withNamespaces(fragment, missing);
            const sequence: Record<string, string[]> = {
                FXParticleSystemTemplate: ['SlavePosOffset','Lifetime','Size','StartSizeRate','BurstDelay','BurstCount','InitialDelay','Alphas','Colors','Wind','Physics','Draw','Volume','Velocity','Event','Update'],
                Alphas: ['Alpha'], Colors: ['Color','ColorScale'], Color: ['Color'],
                Default: target.parent?.localName === 'Physics' ? ['VelocityDamping','DriftVelocity']
                    : target.parent?.localName === 'Update' ? ['SizeRate','SizeRateDamping','AngleZ','AngularRateZ','AngularDamping','AngleXY','AngularRateXY','AngularDampingXY'] : [],
                Point: [], Line: ['StartPoint','EndPoint'], Box: ['HalfSize'], Cylinder: ['Offset'],
                Ortho: ['Position'], Position: ['x','y','z'], Cylindrical: ['Radial','Normal'], Spherical: ['Speed'], Outward: ['Speed','OtherSpeed'],
                Physics: ['Default'], Draw: ['Gpu','Default'], Volume: ['Point','Line','Box','Cylinder','Sphere'], Velocity: ['Ortho','Cylindrical','Spherical','Outward'], Update: ['Default'],
            };
            const order = sequence[target.localName];
            if (!order?.includes(missing.localName)) throw new Error(`尚未实现 ${target.localName}/${missing.localName} 的安全插入顺序，请使用 XML。`);
            if (target.selfClosing) {
                const at = target.openEnd - 2;
                edit.replace(state.document.uri, new vscode.Range(state.document.positionAt(at), state.document.positionAt(target.openEnd)), `>\n${fragment}\n</${target.name}>`);
            } else {
                const rank = order.indexOf(missing.localName);
                const later = target.children.find(child => order.indexOf(child.localName) > rank || (child.localName === missing.localName && Number(child.attributes.Frame) > Number(missing!.attributes.Frame)));
                edit.insert(state.document.uri, state.document.positionAt(later?.start ?? target.closeStart), `\n${fragment}\n`);
            }
            detail = `在本资产中覆盖 ${missing.name}；共享父定义保持不变。\n${fragment}`;
        }
        const version = state.document.version; const sourceVersion = sourceDocument.version;
        const confirmation = await vscode.window.showInformationMessage(`在 ${selected.id} 中覆盖 ${field.label}`, { modal: true, detail }, '应用覆盖');
        if (confirmation !== '应用覆盖') return;
        if (state.disposed || state.document.version !== version || sourceDocument.version !== sourceVersion) throw new Error('内容已更新，请重新应用覆盖。');
        await this.allowed(state.document.uri.fsPath, true);
        if (state.disposed || state.document.version !== version || sourceDocument.version !== sourceVersion) throw new Error('内容已更新，请重新应用覆盖。');
        if (!await vscode.workspace.applyEdit(edit)) throw new Error('覆盖未能应用，文档保持原状。');
    }
    private withNamespaces(fragment: string, node: XmlElement): string {
        const declarations = new Map<string, string>(); let ancestor: XmlElement | undefined = node;
        while (ancestor) { for (const [key, value] of Object.entries(ancestor.attributes)) if ((key === 'xmlns' || key.startsWith('xmlns:')) && !declarations.has(key)) declarations.set(key, value); ancestor = ancestor.parent; }
        const missing = [...declarations].filter(([key]) => node.attributes[key] === undefined);
        if (!missing.length) return fragment;
        const match = /[\s/>]/.exec(fragment.slice(1)); if (!match) return fragment;
        const at = match.index + 1;
        return fragment.slice(0, at) + missing.map(([key, value]) => ` ${key}="${escapeXml(value)}"`).join('') + fragment.slice(at);
    }
    async copy(uri: string, assetKey: string, systemKey?: string): Promise<void> {
        const state = [...this.editors].find(editor => sameFile(editor.document.uri.fsPath, uri));
        if (state) await this.copyAsset(state, assetKey, systemKey);
    }
    private async copyAsset(state: EditorState, assetKey: string, systemKey?: string): Promise<void> {
        const asset = state.index.getAsset(assetKey); if (!asset || !state.ir) return;
        const rootIr = state.ir;
        await this.allowed(asset.uri);
        const source = await vscode.workspace.openTextDocument(asUri(asset.uri));
        const sourceModel = parseXml(source.getText(), source.uri.fsPath, source.version);
        const node = sourceModel.find(asset.node.path); if (!node || !sourceModel.valid) throw new Error('共享源已更新，请重载。');
        if (node.attributes.id !== asset.id) throw new Error('共享源身份已更新，请重载。');
        const systems = rootIr.systems.filter(system => system.templateKey === asset.key);
        const calls = [...new Map(systems.map(system => [`${system.source.uri}\0${system.source.nodePath}`, system])).values()];
        let system = systemKey ? systems.find(candidate => candidate.key === systemKey) : calls[0];
        if (systemKey && !system) throw new Error('所选调用已更新，请重载。');
        if (!systemKey && calls.length > 1) system = (await vscode.window.showQuickPick(calls.map(candidate => ({ label: candidate.name, description: path.basename(candidate.source.uri), system: candidate })), { title: '选择要改为独立模板的调用' }))?.system;
        if (!systemKey && calls.length > 1 && !system) return;
        let reference: {nodePath: string; attribute: string; expectedValue: string} | undefined;
        let targetUri: vscode.Uri | undefined;
        if (system) {
            const caller = await vscode.workspace.openTextDocument(asUri(system.source.uri));
            const snapshot = state.index.getDocument(system.source.uri);
            if (!snapshot?.valid || snapshot.text !== caller.getText()) throw new Error('调用文档已更新，请重载后重新复制。');
            const call = parseXml(caller.getText(), caller.uri.fsPath, caller.version).find(system.source.nodePath);
            if (call?.localName === 'ParticleSystem' && call.attributes.Particle === asset.id) {
                try {
                    await this.allowed(caller.uri.fsPath, true);
                    targetUri = caller.uri; reference = { nodePath: call.path, attribute: 'Particle', expectedValue: asset.id };
                } catch { /* SDK calls stay read-only; choose a MOD XML for a new independent template. */ }
            }
        }
        if (!targetUri) {
            const preferred = state.previewKey ? asUri(rootIr.documentUri) : state.document.uri;
            try { await this.allowed(preferred.fsPath, true); targetUri = preferred; } catch { /* A standalone SDK source needs a MOD destination. */ }
        }
        if (!targetUri) {
            const mod = this.services.getProject()?.root ?? this.roots[0];
            const candidates = new Map<string, vscode.Uri>();
            for (const document of vscode.workspace.textDocuments) if (document.uri.scheme === 'file' && /\.xml$/i.test(document.uri.fsPath)) {
                try { await this.allowed(document.uri.fsPath, true); candidates.set(document.uri.toString(), document.uri); } catch { /* SDK text remains read-only. */ }
            }
            const entry = vscode.Uri.file(path.join(mod, 'Data', 'Mod.xml'));
            try { await fs.access(entry.fsPath); await this.allowed(entry.fsPath, true); candidates.set(entry.toString(), entry); } catch { /* Allow choosing a different MOD XML. */ }
            const choices = [...candidates.values()].map(uri => ({ label: path.basename(uri.fsPath), description: uri.fsPath, uri }));
            const choice = await vscode.window.showQuickPick([...choices, { label: '选择其他 MOD XML…', description: '', uri: undefined }], { title: '复制 SDK 模板到哪个 MOD XML？' });
            if (!choice) return;
            targetUri = choice.uri ?? (await vscode.window.showOpenDialog({ title: '选择 MOD XML', canSelectMany: false, filters: { XML: ['xml'] }, defaultUri: vscode.Uri.file(mod) }))?.[0];
            if (!targetUri) return;
        }
        await this.allowed(targetUri.fsPath, true);
        const target = await vscode.workspace.openTextDocument(targetUri);
        const targetModel = parseXml(target.getText(), target.uri.fsPath, target.version);
        const targetIndex = await this.newIndex();
        let entry = rootIr.entry;
        try {
            if (!containsPath(this.services.getProject()?.root ?? this.roots[0], entry)) entry = path.join(this.services.getProject()?.root ?? this.roots[0], 'Data', 'Mod.xml');
            try { await fs.access(entry); } catch { entry = target.uri.fsPath; }
            await targetIndex.load(entry);
            if (!targetIndex.getDocument(target.uri.fsPath)) { entry = target.uri.fsPath; await targetIndex.load(entry); }
            const visible = targetIndex.listAssets();
            const plan = planAssetCopy({ source: sourceModel, sourcePath: node.path, target: targetModel,
                reservedIds: visible.map(candidate => candidate.id), visibleAssets: visible, reference });
            const version = target.version; const sourceVersion = source.version;
            const confirmed = await vscode.window.showInformationMessage(`复制 ${asset.id} 为 ${plan.assetId}，写入 ${path.basename(target.uri.fsPath)}${reference ? '并修改当前调用' : ''}`,
                { modal: true, detail: `${plan.copied.map(item => `${item.sourceId} → ${item.id}`).join('\n')}\n依赖保留引用，不复制贴图文件。\n\n${plan.preview}` }, '复制');
            if (confirmed !== '复制') return;
            if (state.disposed || target.version !== version || source.version !== sourceVersion) throw new Error('内容已更新，请重新复制。');
            await this.allowed(target.uri.fsPath, true);
            if (state.disposed || target.version !== version || source.version !== sourceVersion) throw new Error('内容已更新，请重新复制。');
            const edit = new vscode.WorkspaceEdit();
            for (const patch of plan.patches) edit.replace(target.uri, new vscode.Range(target.positionAt(patch.start), target.positionAt(patch.end)), patch.text);
            if (!await vscode.workspace.applyEdit(edit)) throw new Error('复制未应用，源文档保留。');
            targetIndex.invalidate(target.uri.fsPath); await targetIndex.load(entry);
            const selected = reference ? targetIndex.listAssets().find(candidate => candidate.type === 'FXList' && sameFile(candidate.uri, target.uri.fsPath)
                && !!targetIndex.getDocument(target.uri.fsPath)?.find(reference!.nodePath)?.path.startsWith(`${candidate.node.path}/`))
                : targetIndex.listAssets().find(candidate => candidate.id === plan.assetId && sameFile(candidate.uri, target.uri.fsPath));
            await this.open(target.uri, selected?.key);
            const editing = [...this.editors].find(editor => sameFile(editor.document.uri.fsPath, target.uri.fsPath));
            if (editing) await this.locate(editing);
        } finally { targetIndex.dispose(); }
    }
    getSnapshot(uri?: string): ReturnType<FxApi['getSnapshot']> {
        const state = uri ? [...this.editors].find(s => sameFile(s.document.uri.fsPath, uri)) : this.current();
        return { sessionCount: this.editors.size, activeCount: [...this.editors].filter(s => s.session.visible && !s.session.signal.aborted).length,
            dependencies: state?.ir?.dependencies ?? [], ir: state?.ir, documentVersion: state?.document.version, sessionId: state?.session.id };
    }
    refreshProject(): void {
        this.rootKey = ''; this.semantic=undefined; this.resources?.dispose(); this.resources = undefined;
        for (const watcher of this.watchers) watcher.dispose(); this.watchers = [];
        for (const state of [...this.editors]) { state.panel.dispose(); this.close(state); }
        this.tree.update([]); this.diagnostics.clear();
    }
    private close(state: EditorState): void {
        if (state.disposed) return; state.disposed = true;
        if (state.timer) clearTimeout(state.timer);
        this.active.release(state.session); state.session.dispose(); state.index.dispose();
        for (const subscription of state.subscriptions) subscription.dispose(); state.subscriptions = [];
        state.compiled.dispose(); state.uploadedTextures.clear(); state.ir = undefined; this.diagnostics.remove(state.session.id); this.editors.delete(state);
        if (!this.editors.size) this.resources?.clear();
    }
    private html(webview: vscode.Webview, session: PreviewSession, state: Record<string, unknown>): string {
        const nonce = randomBytes(24).toString('base64');
        const js = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, 'media', 'fx-editor.js'));
        const css = webview.asWebviewUri(vscode.Uri.joinPath(this.context.extensionUri, 'media', 'fx.css'));
        const bootstrap = JSON.stringify({ sessionId: session.id, documentVersion: session.documentVersion, requestId: session.requestId, state }).replace(/</g, '\\u003c');
        return `<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-${nonce}'; style-src ${webview.cspSource}; img-src ${webview.cspSource} data:; worker-src blob:; connect-src 'none';"><link rel="stylesheet" href="${css}"><title>NewSdk 特效工作区</title></head><body><div id="app"></div><script nonce="${nonce}">window.__FX_BOOTSTRAP__=${bootstrap};</script><script nonce="${nonce}" src="${js}"></script></body></html>`;
    }
    dispose(): void {
        this.closed = true; for (const state of [...this.editors]) { state.panel.dispose(); this.close(state); }
        this.active.dispose(); this.resources?.dispose(); this.diagnostics.dispose(); this.tree.dispose();
        for (const watcher of this.watchers) watcher.dispose(); for (const subscription of this.subscriptions) subscription.dispose();
        this.pending.clear();
    }
}

export function registerFx(context: vscode.ExtensionContext, services: FxServices): FxApi { return new FxEditorProvider(context, services); }
