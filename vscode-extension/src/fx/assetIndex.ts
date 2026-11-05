import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseXml, XmlDocument, XmlElement } from './xmlDocument';
import type { FxIssue, SourceLocation } from './previewProtocol';

export type IncludeMode = 'all' | 'instance' | 'reference' | 'xml';
export interface TextSnapshot { text: string; version: number; }
export interface AssetIndexOptions {
    roots: string[]; modRoot?: string; sdkRoot?: string;
    readText?: (uri: string) => Promise<TextSnapshot | string>;
    maxSourceBytes?: number; maxDocuments?: number;
}
export interface AssetRecord {
    key: string; id: string; type: string; namespace: string; uri: string; entry: string;
    node: XmlElement; readonly: boolean; includeType: IncludeMode;
    /** xi:include is expanded before compiler instance collection. Keep the
     * physical URI for edits, and this logical document for reference visibility. */
    scopeUri?: string;
}
export interface SourceDataProfile { source: string; dataRoot?: string; dataPaths: string[]; artPaths: string[]; audioPaths: string[]; }
interface AssetScope { self: AssetRecord[]; all: AssetRecord[]; tentative: AssetRecord[]; reference: AssetRecord[]; }
export interface IncludeEdge { from: string; to: string; mode: IncludeMode; source: SourceLocation; }
export interface AssetGraph {
    entry: string; generation: number; assets: Map<string, AssetRecord>;
    documents: Map<string, XmlDocument>; issues: FxIssue[]; dependencies: string[];
    includes: IncludeEdge[]; sourceDataProfile?: SourceDataProfile;
}
const assetTypes = new Set(['FXList', 'FXParticleSystemTemplate', 'Texture', 'OnDemandTexture']);
const ASSET_NAMESPACE = 'uri:ea.com:eala:asset';
const XML_INCLUDE_NAMESPACE = 'http://www.w3.org/2001/XInclude';
function localName(name: string): string { return name.includes(':') ? name.slice(name.indexOf(':') + 1) : name; }
export function filePath(uri: string): string { return path.resolve(uri.startsWith('file:') ? fileURLToPath(uri) : uri); }
export function sourceOf(uri: string, node: XmlElement): SourceLocation { return { uri, nodePath: node.path, start: node.start, end: node.openEnd }; }
function contains(root: string, file: string): boolean {
    const relative = path.relative(root, file); return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}
function identity(value: string): string { return process.platform === 'win32' ? value.toLowerCase() : value; }
function stopped(signal?: AbortSignal): void { if (signal?.aborted) throw new Error('FX asset request cancelled'); }
function issue(code: string, message: string, uri: string, node?: XmlElement, severity: FxIssue['severity'] = 'error'): FxIssue {
    return { code, message, severity, uri, nodePath: node?.path ?? '', start: node?.start ?? 0, end: node?.openEnd ?? 1 };
}

/** Entry-scoped metadata. No texture decoding, directory walking or source writes. */
export class AssetIndex {
    private readonly graphs = new Map<string, AssetGraph>();
    private readonly invalidated = new Set<string>();
    private readonly versions = new Map<string, number>();
    private readonly reverseAssets = new Map<string, Set<string>>();
    private readonly physicalPaths = new Map<string, string>();
    private readonly scopes = new Map<string, AssetScope>();
    private profileRequest?: Promise<SourceDataProfile>;
    private profile?: SourceDataProfile;
    private readonly profileDependencies = new Set<string>();
    private epoch = 0;
    private disposed = false;
    private active: AssetGraph = {entry: '', generation: 0, assets: new Map(), documents: new Map(), issues: [], dependencies: [], includes: []};
    constructor(readonly options: AssetIndexOptions) {
        if (!options.roots.length) throw new Error('FX asset roots must be explicit');
    }
    get graph(): AssetGraph { return this.active; }
    getDocument(uri: string): XmlDocument | undefined { return this.active.documents.get(filePath(uri)); }
    getAsset(key: string): AssetRecord | undefined { return this.active.assets.get(key); }
    listAssets(query = ''): AssetRecord[] {
        const q = query.toLowerCase();
        return [...this.active.assets.values()].filter(a => !q || `${a.type} ${a.id} ${a.uri}`.toLowerCase().includes(q))
            .sort((a, b) => a.type.localeCompare(b.type) || a.id.localeCompare(b.id) || a.uri.localeCompare(b.uri));
    }
    get generation(): number { return this.active.generation; }
    get sourceDataProfile(): SourceDataProfile | undefined { return this.profile; }
    isReadonly(uri: string): boolean {
        const absolute = filePath(uri); const current = this.physicalPaths.get(absolute) ?? absolute;
        const sdk = this.options.sdkRoot ? this.physicalPaths.get(filePath(this.options.sdkRoot)) ?? filePath(this.options.sdkRoot) : undefined;
        const mod = this.options.modRoot ? this.physicalPaths.get(filePath(this.options.modRoot)) ?? filePath(this.options.modRoot) : undefined;
        // Only an explicitly selected MOD strictly inside the SDK is writable.
        return !!sdk && contains(sdk, current) && !(mod && identity(mod) !== identity(sdk) && contains(sdk, mod) && contains(mod, current));
    }
    /** Lexical and physical checks both run before any external file reader. */
    async checkedFile(uri: string): Promise<string> {
        const candidate = filePath(uri);
        // SDK/MOD selectors may use Windows short names or symlink aliases even
        // when the allowed roots are canonical. Resolve selectors for readonly
        // classification without granting them any additional filesystem access.
        const selectors = [...new Set([...this.options.roots, this.options.modRoot, this.options.sdkRoot].filter((root): root is string => !!root))];
        await Promise.all(selectors.map(async root => {
            const absolute = filePath(root); let physicalRoot: string; try { physicalRoot = await fs.realpath(absolute); } catch { physicalRoot = absolute; }
            this.physicalPaths.set(absolute, physicalRoot);
        }));
        const roots = this.options.roots.map(root => this.physicalPaths.get(filePath(root))!);
        if (!this.options.roots.some(root => contains(filePath(root), candidate)) && !roots.some(root => contains(root, candidate))) throw new Error(`文件位于 MOD/SDK 资源根之外：${candidate}`);
        let physical: string;
        try { physical = await fs.realpath(candidate); }
        catch (error) {
            if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
            // Walk to a real ancestor, so a missing child under a symlink cannot escape.
            let parent = path.dirname(candidate); const suffix = [path.basename(candidate)];
            for (;;) {
                try { physical = path.join(await fs.realpath(parent), ...suffix); break; }
                catch (parentError) {
                    if ((parentError as NodeJS.ErrnoException).code !== 'ENOENT' || path.dirname(parent) === parent) throw parentError;
                    suffix.unshift(path.basename(parent)); parent = path.dirname(parent);
                }
            }
        }
        if (!roots.some(root => contains(root, physical!))) throw new Error(`资源符号链接指向 MOD/SDK 根之外：${candidate}`);
        this.physicalPaths.set(candidate, physical);
        return physical;
    }
    async resolveFile(fromUri: string, reference: string): Promise<string> {
        if (!reference.trim()) throw new Error('资源文件引用为空');
        const value = reference.replace(/\\/g, '/');
        if (/^(?:https?|ftp):/i.test(value) || value.includes('\0')) throw new Error('FX 仅允许 MOD/SDK 内本地资源');
        const prefixed = /^([a-zA-Z]+):(.*)$/.exec(value);
        if (value.includes(':') && !prefixed && !/^[a-zA-Z]:\//.test(value)) throw new Error('资源路径包含非法别名或文件流语法');
        const candidates: string[] = [];
        if (prefixed && !/^[a-zA-Z]:\//.test(value)) {
            const suffix = prefixed[2].replace(/^\/+/, '');
            if (suffix.includes(':')) throw new Error('资源别名不能包含第二个冒号');
            const prefix = prefixed[1].toUpperCase();
            const profile = await this.readSourceProfile();
            const search = (paths: string[], art = false): void => {
                if (art && !suffix.includes('/') && suffix.length < 2) throw new Error('ART 文件名不足两字符，无法进行 bucket 查找');
                for (const root of paths) {
                    const relative = art && root !== '*' && !suffix.includes('/') ? path.join(suffix.slice(0,2),suffix) : suffix;
                    candidates.push(path.join(root === '*' ? path.dirname(filePath(fromUri)) : root,relative));
                }
            };
            if (prefix === 'DATA') search(profile.dataPaths);
            else if (prefix === 'ART') search(profile.artPaths,true);
            else if (prefix === 'AUDIO') search(profile.audioPaths);
            else if (prefix === 'ROOT' && profile.dataRoot) candidates.push(path.join(profile.dataRoot,suffix));
            else throw new Error(`尚不支持资源前缀 ${prefix}:`);
            if (!candidates.length) throw new Error(`资源前缀 ${prefix}: 需要配置 MOD/SDK 根`);
        } else candidates.push(path.isAbsolute(value) ? value : path.resolve(path.dirname(filePath(fromUri)), value));
        for (const candidate of candidates) {
            const checked = await this.checkedFile(candidate);
            const existing = await this.existingCaseInsensitive(checked);
            if (existing) return this.checkedFile(existing);
        }
        return this.checkedFile(candidates[0]);
    }
    /** Current CLI a75840e CoreStreamCompiler.cs:237,244-246 prepends MOD
     * roots to SDK assetbuilder defaults. SettingsLoader.ProcessPaths anchors
     * relative settings at sdkRoot; FileNameResolver preserves the exact order.
     */
    private readSourceProfile(): Promise<SourceDataProfile> {
        if (this.profileRequest) return this.profileRequest;
        this.profileRequest = (async () => {
            const sdk = this.options.sdkRoot ? filePath(this.options.sdkRoot) : undefined;
            let configured: Record<string,string> = sdk ? {defaultDataPaths:'.;.\\Mods;.\\SAGEXML',defaultArtPaths:'.\\Art',defaultAudioPaths:'.\\Audio'} : {};
            let source = sdk ? 'NewModCompiler a75840e SDK default profile (SDK configuration unavailable)' : 'Standalone MOD source profile';
            if (sdk) {
                const requested = await this.checkedFile(path.join(sdk,'tools','BinaryAssetBuilder.exe.config'));
                this.profileDependencies.add(requested);
                const config = await this.existingCaseInsensitive(requested);
                if (config) {
                    const checked = await this.checkedFile(config); this.profileDependencies.add(checked);
                    if ((await fs.stat(checked)).size > 512*1024) throw new Error('SDK 搜索路径配置超过 512 KiB 读取预算');
                    const document = parseXml(await fs.readFile(checked,'utf8'),checked);
                    const settings = document.elements.find(node => node.localName === 'assetbuilder' && node.parent?.localName === 'configuration');
                    if (!document.valid || !settings) throw new Error('SDK BinaryAssetBuilder.exe.config 缺少合法 assetbuilder 搜索路径配置');
                    configured = settings.attributes;
                    source = `NewModCompiler a75840e mod-prepend+sdk-config: ${checked}`;
                }
            }
            const expand = (name: string, folder: string): string[] => {
                const paths = this.options.modRoot ? [path.join(filePath(this.options.modRoot),folder)] : [];
                if (sdk) for (const value of (configured[name] ?? '').split(';').filter(value => value.length > 0)) {
                    if (!value.trim()) throw new Error(`SDK ${name} 包含空搜索项`);
                    if (value === '*') paths.push('*');
                    else paths.push(path.resolve(sdk,value.replace(/\\/g,'/')));
                }
                return paths;
            };
            const profile = {source,dataRoot:sdk ?? this.options.modRoot,dataPaths:expand('defaultDataPaths','Data'),artPaths:expand('defaultArtPaths','Art'),audioPaths:expand('defaultAudioPaths','Audio')};
            this.profile = profile; this.active.sourceDataProfile = profile;
            for (const file of this.profileDependencies) if (this.active.entry && !this.active.dependencies.includes(file)) this.active.dependencies.push(file);
            return profile;
        })();
        return this.profileRequest;
    }
    /** Windows filename semantics for Linux contract tests; never scan subtrees. */
    private async existingCaseInsensitive(candidate: string): Promise<string | undefined> {
        try { await fs.access(candidate); return candidate; }
        catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
        const suffix: string[] = []; let parent = candidate;
        for (;;) {
            try { await fs.access(parent); break; }
            catch (error) {
                if ((error as NodeJS.ErrnoException).code !== 'ENOENT' || path.dirname(parent) === parent) throw error;
                suffix.unshift(path.basename(parent)); parent = path.dirname(parent);
            }
        }
        for (const segment of suffix) {
            parent = await this.checkedFile(parent);
            let match: string | undefined; let visited = 0;
            try {
                const entries = await fs.opendir(parent);
                for await (const entry of entries) {
                    if (++visited > 16384) throw new Error('资源目录名称查找超过 16384 项预算');
                    if (entry.name.toLowerCase() !== segment.toLowerCase()) continue;
                    if (match && match !== entry.name) throw new Error(`资源大小写名称存在歧义：${segment}`);
                    match = entry.name;
                }
            } catch (error) {
                if (['ENOENT','ENOTDIR'].includes((error as NodeJS.ErrnoException).code ?? '')) return undefined;
                throw error;
            }
            if (!match) return undefined;
            parent = path.join(parent,match);
        }
        return parent;
    }
    /** Rebuild only this include closure. Dirty TextDocument snapshots take precedence. */
    async load(entryUri: string, signal?: AbortSignal): Promise<AssetGraph> {
        if (this.disposed) throw new Error('AssetIndex disposed');
        const entry = await this.checkedFile(entryUri); stopped(signal);
        const request = ++this.epoch;
        this.profileRequest = undefined; this.profile = undefined; this.profileDependencies.clear();
        const graph: AssetGraph = { entry, generation: request, assets: new Map(), documents: new Map(), issues: [], dependencies: [], includes: [] };
        let sourceBytes = 0;
        const visiting: string[] = [];
        const modes = new Map<string, IncludeMode>();
        const rank: Record<IncludeMode, number> = {reference: 0, instance: 1, all: 2, xml: 3};
        const visit = async (file: string, mode: IncludeMode, origin?: XmlElement, from?: string): Promise<void> => {
            stopped(signal);
            if (this.disposed || request !== this.epoch) throw new Error('FX asset request superseded');
            const cycle = visiting.indexOf(identity(file));
            if (cycle >= 0) {
                graph.issues.push(issue('include-cycle', `Include 循环：${[...visiting.slice(cycle), file].join(' → ')}`, from ?? file, origin)); return;
            }
            const previous = modes.get(file);
            if (previous !== undefined) {
                if (rank[mode] > rank[previous]) {
                    modes.set(file, mode);
                    for (const asset of graph.assets.values()) if (asset.uri === file) asset.includeType = mode;
                    // A later concrete import upgrades the same canonical subtree;
                    // reference-only children remain reference-stream definitions.
                    for (const edge of graph.includes.filter(edge => edge.from === file && edge.mode !== 'reference')) {
                        await visit(edge.to, mode === 'reference' ? 'reference' : edge.mode, graph.documents.get(file)?.find(edge.source.nodePath), file);
                    }
                }
                return;
            }
            if (graph.documents.size >= (this.options.maxDocuments ?? 5000)) {
                graph.issues.push(issue('source-budget', 'FX 资产文档预算已达上限，停止展开', from ?? file, origin)); return;
            }
            modes.set(file, mode); visiting.push(identity(file));
            let document: XmlDocument;
            try {
                await this.checkedFile(file); stopped(signal);
                const remaining = (this.options.maxSourceBytes ?? 48 * 1024 * 1024) - sourceBytes;
                try { if ((await fs.stat(file)).size > remaining) throw new Error('源码/CST 预算已达上限（读取前拦截）'); }
                catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT' || !this.options.readText) throw error; }
                const snapshot = this.options.readText ? await this.options.readText(file) : {text: await fs.readFile(file, 'utf8'), version: this.versions.get(file) ?? 0};
                const normalized = typeof snapshot === 'string' ? {text: snapshot, version: this.versions.get(file) ?? 0} : snapshot;
                sourceBytes += Buffer.byteLength(normalized.text, 'utf8');
                if (sourceBytes > (this.options.maxSourceBytes ?? 48 * 1024 * 1024)) throw new Error('源码/CST 预算已达 48 MiB 上限');
                document = parseXml(normalized.text, file, normalized.version);
                graph.documents.set(file, document); graph.dependencies.push(file);
            } catch (error) {
                graph.issues.push(issue('include-read', error instanceof Error ? error.message : String(error), from ?? file, origin)); visiting.pop(); return;
            }
            if (!document.valid) {
                for (const diagnostic of document.issues) graph.issues.push({uri: file, nodePath: '', start: diagnostic.start, end: diagnostic.end,
                    severity: 'error', code: 'xml-syntax', message: diagnostic.message});
                visiting.pop(); return;
            }
            for (const node of document.elements) {
                if (assetTypes.has(node.localName) && node.attributes.id !== undefined && (!node.parent || node.parent.localName === 'AssetDeclaration')) {
                    const namespace = node.namespaceUri ?? '';
                    if (namespace && namespace !== ASSET_NAMESPACE) {
                        graph.issues.push(issue('asset-namespace', `未支持资产命名空间 ${namespace}`, file, node)); continue;
                    }
                    const key = JSON.stringify([entry, namespace, node.localName, node.attributes.id, file, node.path]);
                    graph.assets.set(key, { key, id: node.attributes.id, type: node.localName, namespace, uri: file, entry,
                        node, readonly: this.isReadonly(file), includeType: mode });
                }
                const isAssetInclude = node.localName === 'Include' && node.parent?.localName === 'Includes';
                const isXmlInclude = node.localName === 'include' && node.namespaceUri === XML_INCLUDE_NAMESPACE;
                if (!isAssetInclude && !isXmlInclude) continue;
                const reference = isXmlInclude ? node.attributes.href : node.attributes.source;
                const childMode = isXmlInclude ? 'xml' : node.attributes.type;
                if (!reference || !['all', 'instance', 'reference', 'xml'].includes(childMode)) {
                    graph.issues.push(issue('include-contract', 'Include 需要明确 source/href 与合法 type', file, node)); continue;
                }
                if (isXmlInclude && (node.attributes.parse && node.attributes.parse !== 'xml' || node.attributes.xpointer)) {
                    graph.issues.push(issue('xinclude-contract', `尚不支持 xi:include ${node.attributes.xpointer ? 'xpointer 分支' : '非 XML parse'}`, file, node)); continue;
                }
                try {
                    const target = await this.resolveFile(file, reference);
                    graph.includes.push({from: file, to: target, mode: childMode as IncludeMode, source: sourceOf(file, node)});
                    await visit(target, mode === 'reference' ? 'reference' : childMode as IncludeMode, node, file);
                } catch (error) {
                    graph.issues.push(issue('include-path', error instanceof Error ? error.message : String(error), file, node));
                }
            }
            visiting.pop();
        };
        await visit(entry, 'all'); stopped(signal);
        if (request !== this.epoch || this.disposed) throw new Error('FX asset request superseded');
        // Compiler AssetDeclarationDocument.LoadXml loads an XIncludingReader
        // before collecting instances. Inline fragments belong to the including
        // definition document, while source tokens retain their physical URI.
        const rawAssets = new Map([...graph.assets.values()].map(asset=>[asset.node,asset])); graph.assets.clear();
        const assetDocuments = new Set([entry,...graph.includes.filter(edge=>edge.mode!=='xml').map(edge=>edge.to)]);
        const insert = (scopeUri:string,uri:string,node:XmlElement,stack:string[],inlinePath=node.path):void => {
            const edge=graph.includes.find(value=>value.mode==='xml'&&value.from===uri&&value.source.nodePath===node.path);
            if(edge){
                if(stack.includes(edge.to))return;
                for(const root of graph.documents.get(edge.to)?.roots??[]) {
                    if(root.localName==='AssetDeclaration') {graph.issues.push(issue('xinclude-nested-declaration','xi:include 内联整个根元素；AssetDeclaration 中不能嵌套 AssetDeclaration，请包含单资产 XML 片段',uri,node));continue;}
                    insert(scopeUri,edge.to,root,[...stack,edge.to],`${inlinePath}/${root.path}`);
                }
                return;
            }
            const original=rawAssets.get(node);if(!original)return;
            const record=scopeUri===uri?original:{...original,scopeUri,key:JSON.stringify([entry,original.namespace,original.type,original.id,uri,node.path,scopeUri,inlinePath]),includeType:modes.get(scopeUri)??'all'};
            graph.assets.set(record.key,record);
        };
        for(const uri of assetDocuments)for(const root of graph.documents.get(uri)?.roots??[]) {
            if(root.localName==='AssetDeclaration')for(const child of root.children)insert(uri,uri,child,[uri]);
            else insert(uri,uri,root,[uri]);
        }
        graph.sourceDataProfile = this.profile;
        for (const file of this.profileDependencies) if (!graph.dependencies.includes(file)) graph.dependencies.push(file);
        this.active = graph; this.scopes.clear(); this.graphs.clear(); this.graphs.set(entry, graph); this.invalidated.delete(entry);
        this.rebuildReverseAssets();
        return graph;
    }
    /** Release a75840e DocumentProcessor.cs:1006-1034, FindInstance:1798-1823.
     * Keep Self / All / Tentative / External visibility per definition document.
     * Instance imports intentionally do not propagate the child's reference stream.
     */
    private documentScope(uri: string): AssetScope {
        const file = filePath(uri); const cached = this.scopes.get(file); if (cached) return cached;
        const scope: AssetScope = {self:[...this.active.assets.values()].filter(asset => (asset.scopeUri??asset.uri) === file),all:[],tentative:[],reference:[]};
        this.scopes.set(file,scope); // Include cycles are separately diagnosed and bounded.
        const add = (target: AssetRecord[], values: AssetRecord[]): void => {
            for (const asset of values) if (!target.some(existing => existing.key === asset.key)) target.push(asset);
        };
        for (const edge of this.active.includes.filter(edge => edge.from === file)) {
            const child = this.documentScope(edge.to); const instances = [...child.self,...child.all];
            if (edge.mode === 'reference') { add(scope.reference,child.reference); add(scope.reference,instances); }
            else if (edge.mode === 'all') { add(scope.reference,child.reference); add(scope.all,instances); add(scope.tentative,child.tentative); }
            else if (edge.mode === 'instance') { add(scope.tentative,instances); add(scope.tentative,child.tentative); }
            else { add(scope.self,child.self); add(scope.all,child.all); add(scope.tentative,child.tentative); add(scope.reference,child.reference); }
        }
        const externalKeys = new Set(scope.reference.map(asset => asset.key));
        scope.all = scope.all.filter(asset => !externalKeys.has(asset.key));
        scope.tentative = scope.tentative.filter(asset => !externalKeys.has(asset.key));
        return scope;
    }
    candidates(type: string, reference: string, namespace?: string, fromUri = this.active.entry): AssetRecord[] {
        const [prefix, id] = reference.includes(':') ? [reference.slice(0, reference.indexOf(':')), reference.slice(reference.indexOf(':') + 1)] : ['', reference];
        const expected = prefix === 'FXParticleSystem' ? 'FXParticleSystemTemplate' : prefix || type;
        if (expected !== type) return [];
        const scope = this.documentScope(fromUri);
        for (const level of [scope.self,scope.all,scope.tentative,scope.reference]) {
            const candidates = level.filter(asset => asset.type === type && asset.id === id && (namespace === undefined || asset.namespace === namespace));
            if (candidates.length) return candidates;
        }
        return [];
    }
    includedDocuments(fromUri: string): Set<string> {
        const result = new Set<string>(); const pending = [filePath(fromUri)];
        while (pending.length) { const file = pending.pop()!; if (result.has(file)) continue; result.add(file);
            for (const edge of this.active.includes) if (edge.from === file) pending.push(edge.to);
        }
        return result;
    }
    private rebuildReverseAssets(): void {
        this.reverseAssets.clear();
        for (const asset of this.active.assets.values()) {
            const refs: [string, string][] = [];
            if (asset.node.attributes.inheritFrom) refs.push([asset.type, asset.node.attributes.inheritFrom]);
            if (asset.type === 'FXParticleSystemTemplate' && asset.node.attributes.ParticleTexture) refs.push(['Texture', asset.node.attributes.ParticleTexture]);
            const walk = (node: XmlElement, uri=asset.uri, stack: string[]=[]): void => {
                const edge=this.active.includes.find(edge=>edge.mode==='xml'&&edge.from===uri&&edge.source.nodePath===node.path);
                if(edge){if(!stack.includes(edge.to))for(const root of this.getDocument(edge.to)?.roots??[])walk(root,edge.to,[...stack,edge.to]);return;}
                if (node.localName === 'ParticleSystem' && node.attributes.Particle) refs.push(['FXParticleSystemTemplate', node.attributes.Particle]);
                if (node.attributes.PerParticleAttachedSystem) refs.push(['FXParticleSystemTemplate',node.attributes.PerParticleAttachedSystem]);
                if (node.attributes.EventFX) refs.push(['FXList',node.attributes.EventFX]);
                if (node.localName === 'FXListAtBonePos' && node.attributes.FX) refs.push(['FXList',node.attributes.FX]);
                for (const child of node.children) walk(child,uri,stack);
            }; walk(asset.node);
            for (const [type, ref] of refs) for (const target of this.candidates(type, ref, asset.namespace, asset.scopeUri??asset.uri)) {
                const dependents = this.reverseAssets.get(target.key) ?? new Set<string>(); dependents.add(asset.key); this.reverseAssets.set(target.key, dependents);
            }
        }
    }
    dependentsOf(assetKey: string): Set<string> {
        const result = new Set([assetKey]); const pending = [assetKey];
        while (pending.length) for (const key of this.reverseAssets.get(pending.pop()!) ?? []) if (!result.has(key)) { result.add(key); pending.push(key); }
        return result;
    }
    invalidate(uri: string): Set<string> {
        const absolute = filePath(uri); const file = this.physicalPaths.get(absolute) ?? absolute;
        if (this.profileDependencies.has(file)) { this.profileRequest = undefined; this.profile = undefined; }
        this.versions.set(file, (this.versions.get(file) ?? 0) + 1);
        const affected = new Set<string>();
        for (const [entry, graph] of this.graphs) if (graph.dependencies.includes(file) || graph.includes.some(edge => edge.to === file)) {
            this.invalidated.add(entry); affected.add(entry);
        }
        ++this.epoch;
        return affected;
    }
    dispose(): void { this.disposed = true; ++this.epoch; this.graphs.clear(); this.reverseAssets.clear(); this.versions.clear(); this.invalidated.clear(); this.physicalPaths.clear(); this.scopes.clear(); this.profileDependencies.clear(); this.profileRequest = undefined; this.profile = undefined;
        this.active = {entry: '', generation: this.epoch, assets: new Map(), documents: new Map(), issues: [], dependencies: [], includes: []}; }
}
