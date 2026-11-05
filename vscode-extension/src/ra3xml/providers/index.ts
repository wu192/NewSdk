import * as vscode from 'vscode';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import type { AssetService } from '../services/assetService';
import type { AssetGraph, AssetRecord } from '../index/assetIndex';
import type { ReferenceRecord } from '../index/referenceIndex';
import { createAttributeInsertionPatch, createAttributePatch } from '../core/editTransaction';
import { completionAt, completePaths, assetAt, schemaIssues, defineIssues, prepareAssetRename, type SourceIssue } from './languageFeatures';
import { canonicalPath, containsPath, pathIdentity } from '../../project';
import { planCopyToMod } from '../core/copyAsset';
import { applyTextPatches } from '../core/editTransaction';
import { parseXml } from '../core/xmlDocument';

export interface XmlProviderServices { getService(document: vscode.TextDocument): AssetService | undefined }
export interface XmlProviders extends vscode.Disposable { refresh(): void }
const selector: vscode.DocumentSelector = [{scheme: 'file', pattern: '**/*.{xml,w3x}'}];
function eligible(document: vscode.TextDocument): boolean { return document.uri.scheme === 'file' && /\.(?:xml|w3x)$/i.test(document.uri.fsPath); }
function uri(file: string): vscode.Uri { return file.startsWith('file:') ? vscode.Uri.parse(file) : vscode.Uri.file(file); }
function range(document: vscode.TextDocument, start: number, end: number): vscode.Range { return new vscode.Range(document.positionAt(start), document.positionAt(Math.max(start, end))); }
function fieldDetail(field: {type: string; required: boolean; default?: string}): string {
    return `${field.type}${field.required ? ' · 必填' : ''}${field.default !== undefined ? ` · 默认 ${field.default}` : ''}`;
}
async function checkedDocument(service: AssetService, raw: string): Promise<string> {
    const normalized = service.documents.normalize(raw);
    const physical = normalized !== path.resolve(raw) ? normalized : await canonicalPath(raw);
    const checked = await service.index.checkedFile(physical);
    service.documents.alias(raw, checked);
    return checked;
}
function includeValue(node: {localName: string; namespaceUri: string; parent?: {localName: string}}, attribute?: string): boolean {
    return node.localName === 'Include' && node.parent?.localName === 'Includes' && attribute === 'source'
        || node.localName === 'include' && node.namespaceUri === 'http://www.w3.org/2001/XInclude' && attribute === 'href';
}

/** Every feature resolves through the same service used by FX; this object owns no asset index. */
export function registerXmlProviders(context: vscode.ExtensionContext, services: XmlProviderServices): XmlProviders {
    const subscriptions: vscode.Disposable[] = [];
    const diagnostics = vscode.languages.createDiagnosticCollection('newsdk-xml');
    subscriptions.push(diagnostics);
    const lensesChanged = new vscode.EventEmitter<void>(); subscriptions.push(lensesChanged);
    const timers = new Map<string, ReturnType<typeof setTimeout>>();
    const generations = new Map<string, number>();
    const observedServices = new Map<AssetService, vscode.Disposable>();
    const snapshots = new Map<string, {service: AssetService; version: number}>();
    const scopes = new Map<string, AssetGraph>();
    let disposed = false;
    const openKnownDocument = (file: string, service: AssetService): Thenable<vscode.TextDocument> | Promise<vscode.TextDocument> => {
        const identity = pathIdentity(service.documents.normalize(file));
        const open = vscode.workspace.textDocuments.find(document => document.uri.scheme === 'file'
            && pathIdentity(service.documents.normalize(document.uri.fsPath)) === identity);
        return open ? Promise.resolve(open) : vscode.workspace.openTextDocument(uri(file));
    };
    const serviceFor = async (document: vscode.TextDocument, token?: vscode.CancellationToken): Promise<AssetService | undefined> => {
        if (disposed || !vscode.workspace.isTrusted || !eligible(document) || token?.isCancellationRequested) return undefined;
        const service = services.getService(document); if (!service) return undefined;
        let currentFile: string;
        try {currentFile = await checkedDocument(service, document.uri.fsPath);} catch {return undefined;}
        if (!observedServices.has(service)) {
            const owned: vscode.Disposable[] = [service.onDidChange(() => {
                lensesChanged.fire();
                for (const open of vscode.workspace.textDocuments) if (eligible(open) && services.getService(open) === service) schedule(open);
            })];
            for (const root of new Set(service.options.roots)) {
                const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, '**/*.{xml,w3x,xsd,manifest,config}'));
                const changed = (uri: vscode.Uri) => {void fileChanged(uri, service);};
                owned.push(watcher, watcher.onDidChange(changed), watcher.onDidCreate(changed), watcher.onDidDelete(changed));
            }
            observedServices.set(service, {dispose() {owned.forEach(subscription => subscription.dispose());}});
        }
        const version = document.version;
        // Recovered disk records must never hide another already-open dirty document.
        const openDocuments = new Set([...vscode.workspace.textDocuments.filter(eligible), document]);
        for (const open of openDocuments) {
            if (services.getService(open) !== service) continue;
            const previous = snapshots.get(open.uri.toString());
            if (previous?.service === service && previous.version === open.version) continue;
            let checked: string;
            try {checked = open === document ? currentFile : await checkedDocument(service, open.uri.fsPath);} catch {continue;}
            service.setDocument(checked, open.getText(), open.version);
            snapshots.set(open.uri.toString(), {service, version: open.version});
        }
        const graph = await service.getScope(currentFile, version);
        scopes.set(document.uri.toString(), graph);
        return token?.isCancellationRequested || document.version !== version ? undefined : service;
    };
    const definitionLocation = async (asset: AssetRecord, service: AssetService): Promise<vscode.Location> => {
        const target = await openKnownDocument(asset.uri, service);
        const token = asset.node.attributeTokens.id;
        return new vscode.Location(target.uri, range(target, token?.valueStart ?? asset.node.start, token?.valueEnd ?? asset.node.openEnd));
    };
    const referenceLocation = async (reference: ReferenceRecord, service: AssetService): Promise<vscode.Location> => {
        const target = await openKnownDocument(reference.uri, service);
        return new vscode.Location(target.uri, range(target, reference.start, reference.end));
    };
    const selection = (service: AssetService, document: vscode.TextDocument, offset: number): {asset?: AssetRecord; reference?: ReferenceRecord} => {
        const reference = service.resolveReference(document.uri.fsPath, offset);
        const assets = service.findAssets({});
        const declared = assetAt(assets, service.documents.normalize(document.uri.fsPath), offset);
        const resolved = reference?.targets.length === 1 ? assets.find(asset => asset.key === reference.targets[0]) : undefined;
        return {asset: declared ?? resolved, reference};
    };

    subscriptions.push(vscode.languages.registerCompletionItemProvider(selector, {
        async provideCompletionItems(document, position, token) {
            const service = await serviceFor(document, token); if (!service) return undefined;
            const source = service.getDocument(document.uri.fsPath); if (!source) return undefined;
            const offset = document.offsetAt(position); const current = completionAt(source, offset); if (!current) return undefined;
            const schema = service.schema; const items: vscode.CompletionItem[] = [];
            let replacementStart = current.start;
            const add = (label: string, kind: vscode.CompletionItemKind, detail?: string, insertText?: string) => {
                const item = new vscode.CompletionItem(label, kind); item.detail = detail; item.insertText = insertText ?? label;
                item.range = range(document, replacementStart, offset); items.push(item); return item;
            };
            if (current.kind === 'element' && schema) {
                const fields = current.parent ? schema.getChildren(current.parent) : [...schema.elements.values()];
                for (const field of fields) {
                    const name = field.name;
                    const item = add(name, vscode.CompletionItemKind.Class, 'type' in field ? field.type : 'SDK XSD');
                    item.insertText = new vscode.SnippetString(`${name}$0`);
                }
            } else if (current.kind === 'attribute' && current.node && schema) {
                for (const field of schema.getAttributes(current.node)) if (!Object.hasOwn(current.node.attributes, field.name)) {
                    const item = add(field.name, vscode.CompletionItemKind.Property, fieldDetail(field));
                    const value = field.default ?? field.enumValues[0] ?? '';
                    item.insertText = new vscode.SnippetString(`${field.name}="\${1:${value.replace(/[\\$}]/g, '\\$&')}}"$0`);
                    item.documentation = field.documentation;
                    item.sortText = `${field.required ? '0' : '1'}${field.name}`;
                }
            } else if (current.kind === 'value' && current.value && current.node) {
                const field = current.value.attribute ? schema?.getAttributes(current.node).find(field => field.name === current.value!.attribute)
                    : schema?.getChildren(current.node.parent ?? current.node).find(field => field.name === current.node!.localName);
                const list = field?.list || schema?.isReferenceList(current.node, current.value.attribute);
                const valuePrefix = list ? /[^\s]*$/.exec(current.prefix)![0] : current.prefix;
                if (list) replacementStart = offset - valuePrefix.length;
                for (const value of field?.enumValues ?? []) add(value, vscode.CompletionItemKind.EnumMember, field?.type);
                if (field?.default !== undefined && !field.enumValues.includes(field.default)) add(field.default, vscode.CompletionItemKind.Value, 'XSD 默认值');
                const own = service.findAssets({}).find(asset => pathIdentity(asset.uri) === pathIdentity(service.documents.normalize(document.uri.fsPath)) && asset.node.start <= offset && offset < asset.node.end);
                const type = current.value.attribute === 'inheritFrom' ? own?.type : schema?.getReferenceType(current.node, current.value.attribute);
                if (type) {
                    const seen = new Set<string>();
                    for (const asset of service.findAssets({scopeUri: document.uri.fsPath}).filter(asset => asset.type === type || schema?.isAssignable(asset.type, type))) {
                        if (asset.key === own?.key && current.value.attribute === 'inheritFrom') continue;
                        const value = valuePrefix.includes(':') ? `${valuePrefix.split(':')[0]}:${asset.id}` : asset.id;
                        if (seen.has(value)) continue; seen.add(value);
                        add(value, vscode.CompletionItemKind.Reference, `${asset.type} · ${asset.readonly ? 'SDK · ' : ''}${path.basename(asset.uri)}`);
                    }
                    for (const asset of (service.manifests?.list() ?? []).filter(asset => asset.type === type || schema?.isAssignable(asset.type, type))) if (!seen.has(asset.id)) {
                        seen.add(asset.id); add(asset.id, vscode.CompletionItemKind.Reference, `${asset.type} · SDK manifest（只读）`);
                    }
                }
                if (/^(?:DATA|ART|AUDIO):/i.test(current.prefix) || includeValue(current.node, current.value.attribute)) {
                    for (const item of await completePaths(service, document.uri.fsPath, current.prefix)) add(item.value,
                        item.directory ? vscode.CompletionItemKind.Folder : vscode.CompletionItemKind.File, 'MOD / SDK 资源路径');
                } else if (field && /(?:file|path)/i.test(field.type) && (!current.prefix || /^[A-Z]*:?$/i.test(current.prefix))) {
                    for (const prefix of ['DATA:', 'ART:', 'AUDIO:']) add(prefix, vscode.CompletionItemKind.Folder, '资源搜索路径');
                }
            }
            return items.slice(0, 512);
        },
    }, '<', ' ', '"', "'", ':', '/', '\\'));

    subscriptions.push(vscode.languages.registerDefinitionProvider(selector, {
        async provideDefinition(document, position, token) {
            const service = await serviceFor(document, token); if (!service) return undefined;
            const source = service.getDocument(document.uri.fsPath); if (!source) return undefined;
            const offset = document.offsetAt(position); const current = completionAt(source, offset);
            if (current?.value && includeValue(current.value.node, current.value.attribute)) {
                try { const target = await service.index.resolveFile(document.uri.fsPath, current.value.value); return new vscode.Location(uri(target), new vscode.Position(0, 0)); }
                catch { return undefined; }
            }
            const selected = selection(service, document, offset);
            if (selected.reference) {
                const assets = service.findAssets({});
                return Promise.all(assets.filter(asset => selected.reference!.targets.includes(asset.key)).map(asset => definitionLocation(asset, service)));
            }
            return selected.asset ? definitionLocation(selected.asset, service) : undefined;
        },
    }));

    subscriptions.push(vscode.languages.registerHoverProvider(selector, {
        async provideHover(document, position, token) {
            const service = await serviceFor(document, token); if (!service) return undefined;
            const offset = document.offsetAt(position); const selected = selection(service, document, offset);
            const markdown = new vscode.MarkdownString();
            if (selected.asset) {
                const asset = selected.asset;
                markdown.appendCodeblock(`${asset.type} ${asset.id}`, 'text');
                markdown.appendText(`${asset.readonly ? 'SDK（只读）' : 'MOD'} · ${asset.uri}\n\n`);
                const ancestors = service.getInheritance(asset).filter(parent => parent.key !== asset.key);
                if (ancestors.length) markdown.appendText(`继承：${ancestors.map(parent => `${parent.type}:${parent.id}`).join(' → ')}\n\n`);
                const fields = service.getEffectiveFields(asset).filter(field => field.inherited).slice(0, 16);
                for (const field of fields) markdown.appendText(`${field.name} = ${field.value} ← ${field.source.id}${field.certainty === 'source' ? '（来源值，类型合并由编译器核实）' : ''}\n\n`);
                markdown.appendText(`已确认引用：${service.findReferences(asset).length}；可能受影响资产：${service.getPotentialDependents(asset).length}。${service.completeness === 'partial' ? '索引尚未完整，数量为当前已知结果。' : ''}`);
            } else if (selected.reference?.manifestTargets?.length) {
                for (const asset of selected.reference.manifestTargets) {
                    markdown.appendText(`${asset.type}:${asset.id} · SDK manifest（只读，无 XML 源位置）\n\n`);
                    markdown.appendMarkdown(`[打开 manifest 文件](<${uri(asset.uri).toString()}>)`);
                }
            } else if (selected.reference) {
                markdown.appendText(selected.reference.dynamic ? '动态引用：无法静态确认目标。' : selected.reference.ambiguous ? '存在多个可见目标，请先消除歧义。' : '尚未确认引用目标。');
            } else {
                const source = service.getDocument(document.uri.fsPath); const node = source?.elementAt(offset);
                if (!node) return undefined;
                const attribute = Object.values(node.attributeTokens).find(token => token.start <= offset && offset <= token.end)?.name;
                const field = attribute ? service.schema?.getAttributes(node).find(field => field.name === attribute) : undefined;
                if (field) {markdown.appendCodeblock(`${field.name}: ${fieldDetail(field)}`, 'text'); if (field.documentation) markdown.appendText(field.documentation);}
                else return undefined;
            }
            return new vscode.Hover(markdown);
        },
    }));

    subscriptions.push(vscode.languages.registerReferenceProvider(selector, {
        async provideReferences(document, position, options, token) {
            const service = await serviceFor(document, token); if (!service) return undefined;
            const selected = selection(service, document, document.offsetAt(position)); if (!selected.asset) return [];
            const locations = await Promise.all(service.findReferences(selected.asset).map(reference => referenceLocation(reference, service)));
            if (options.includeDeclaration) locations.unshift(await definitionLocation(selected.asset, service));
            return locations;
        },
    }));

    const renameTarget = (service: AssetService, document: vscode.TextDocument, offset: number): AssetRecord => {
        const selected = selection(service, document, offset);
        if (!selected.asset || selected.reference?.dynamic || selected.reference?.ambiguous) throw new Error('只能重命名已唯一解析到定义的资产；动态或有歧义的引用不参与。');
        if (selected.asset.readonly || service.index.isReadonly(selected.asset.uri)) throw new Error('SDK 资产只读；请先复制到 MOD 再修改 MOD 引用。');
        if (service.completeness !== 'complete') throw new Error('项目索引尚未完整，无法安全预览跨文件重命名。');
        return selected.asset;
    };
    subscriptions.push(vscode.languages.registerRenameProvider(selector, {
        async prepareRename(document, position, token) {
            const service = await serviceFor(document, token); if (!service) throw new Error('请先选择 MOD 和 SDK。');
            const offset = document.offsetAt(position); const asset = renameTarget(service, document, offset);
            const reference = service.resolveReference(document.uri.fsPath, offset);
            const id = asset.node.attributeTokens.id;
            return {range: range(document, reference?.start ?? id.valueStart, reference?.end ?? id.valueEnd), placeholder: asset.id};
        },
        async provideRenameEdits(document, position, newName, token) {
            const service = await serviceFor(document, token); if (!service) return undefined;
            const asset = renameTarget(service, document, document.offsetAt(position));
            const prepared = prepareAssetRename(service, asset, newName);
            const change = new vscode.WorkspaceEdit();
            for (const [file, patches] of prepared) {
                const target = await openKnownDocument(file, service);
                const expected = service.getDocument(file);
                if (!expected || target.getText() !== expected.text) throw new Error('文档在预览前已更新，请重新重命名。');
                const replacements = patches.map(patch => ({range: range(target, patch.start, patch.end), newText: patch.text}));
                const metadata: vscode.WorkspaceEditEntryMetadata = {label: `重命名 ${asset.type}:${asset.id} → ${newName}`, needsConfirmation: true,
                    description: '仅包含可唯一确认的 MOD 定义和引用；SDK、动态及有歧义的引用已排除。'};
                change.set(target.uri, replacements.map((replacement): [vscode.TextEdit, vscode.WorkspaceEditEntryMetadata] =>
                    [new vscode.TextEdit(replacement.range, replacement.newText), metadata]));
            }
            return change;
        },
    }));

    subscriptions.push(vscode.languages.registerCodeLensProvider(selector, {
        onDidChangeCodeLenses: lensesChanged.event,
        async provideCodeLenses(document, token) {
            const service = await serviceFor(document, token); if (!service) return [];
            return service.findAssets({}).filter(asset => pathIdentity(asset.uri) === pathIdentity(service.documents.normalize(document.uri.fsPath))).slice(0, 256).flatMap(asset => {
                const id = asset.node.attributeTokens.id; const at = range(document, id.valueStart, id.valueEnd);
                const references = service.findReferences(asset); const dependents = service.getPotentialDependents(asset);
                const suffix = service.completeness === 'partial' ? '（已知）' : '';
                return [new vscode.CodeLens(at, {title: `${references.length} 个引用${suffix}`, command: 'newsdk.xml.references', arguments: [document.uri, id.valueStart]}),
                    new vscode.CodeLens(at, {title: `${dependents.length} 个可能受影响资产${suffix}`, command: 'newsdk.xml.impact', arguments: [document.uri, id.valueStart]})];
            });
        },
    }));

    function commandInput(target?: vscode.Uri, offset?: number): {target: vscode.Uri; offset: number} | undefined {
        if (target instanceof vscode.Uri && typeof offset === 'number' && Number.isSafeInteger(offset) && offset >= 0) return {target, offset};
        const active = vscode.window.activeTextEditor;
        if (!active || !eligible(active.document)) {void vscode.window.showInformationMessage('请先打开 MOD XML，并将光标放在资产 ID 或引用上。'); return undefined;}
        return {target: active.document.uri, offset: active.document.offsetAt(active.selection.active)};
    }
    subscriptions.push(vscode.commands.registerCommand('newsdk.xml.references', async (target?: vscode.Uri, offset?: number) => {
        const input = commandInput(target, offset); if (!input) return; target = input.target; offset = input.offset;
        const document = await vscode.workspace.openTextDocument(target); const service = await serviceFor(document); if (!service) return;
        const asset = selection(service, document, offset).asset; if (!asset) return;
        const locations = await Promise.all(service.findReferences(asset).map(reference => referenceLocation(reference, service)));
        await vscode.commands.executeCommand('editor.action.showReferences', target, document.positionAt(offset), locations);
    }));
    subscriptions.push(vscode.commands.registerCommand('newsdk.xml.impact', async (target?: vscode.Uri, offset?: number) => {
        const input = commandInput(target, offset); if (!input) return; target = input.target; offset = input.offset;
        const document = await vscode.workspace.openTextDocument(target); const service = await serviceFor(document); if (!service) return;
        const asset = selection(service, document, offset).asset; if (!asset) return;
        const items = service.getPotentialDependents(asset).filter(dependent => dependent.key !== asset.key).map(dependent => ({label: `${dependent.type}:${dependent.id}`, description: path.basename(dependent.uri), detail: dependent.uri, asset: dependent}));
        const picked = await vscode.window.showQuickPick(items, {title: `修改 ${asset.type}:${asset.id} 的已知影响`, placeHolder: '源码关系仅提示潜在影响；实际重编译由编译器缓存决定'});
        if (picked) {const location = await definitionLocation(picked.asset, service); await vscode.window.showTextDocument(location.uri, {selection: location.range, preview: true});}
    }));
    subscriptions.push(vscode.commands.registerCommand('newsdk.xml.copyToMod', async (target?: vscode.Uri, offset?: number) => {
        try {
            const input = commandInput(target, offset); if (!input) return;
            const document = await vscode.workspace.openTextDocument(input.target); const service = await serviceFor(document); if (!service) return;
            const selected = selection(service, document, input.offset);
            if (!selected.asset?.readonly || selected.reference?.dynamic || selected.reference?.ambiguous) throw new Error('请选择唯一解析到 SDK XML 定义的引用或资产；仅 manifest 声明没有可复制的 XML 源码。');
            const source = selected.asset, modRoot = service.options.modRoot; if (!modRoot) throw new Error('请先选择 MOD。');
            const sourceEditor = await openKnownDocument(source.uri, service);
            service.setDocument(source.uri, sourceEditor.getText(), sourceEditor.version); await service.getScope(source.uri);
            const newId = await vscode.window.showInputBox({title: `复制 ${source.type}:${source.id} 到 MOD`, value: `${source.id}_Copy`,
                prompt: '新资产 ID；复制后可在 MOD XML 中修改。', validateInput: value => !/^[0-9A-Za-z_.-]{1,512}$/.test(value)
                    ? '仅支持英文字母、数字、下划线、点和连字符。' : service.findAssets({type: source.type, id: value}).length || service.manifests?.find(source.type, value).length ? '该类型已有同名资产。' : undefined});
            if (!newId) return;
            const destinations = new Map<string, vscode.Uri>();
            for (const open of vscode.workspace.textDocuments) if (eligible(open) && containsPath(modRoot, open.uri.fsPath) && !service.index.isReadonly(open.uri.fsPath)) destinations.set(open.uri.toString(), open.uri);
            const entry = vscode.Uri.file(path.join(modRoot, 'Data', 'Mod.xml'));
            try {await fs.access(entry.fsPath); destinations.set(entry.toString(), entry);} catch { /* A new MOD may not yet have an entry. */ }
            const choices = [...destinations.values()].map(target => ({label: path.basename(target.fsPath), description: target.fsPath, mode: 'existing', target}));
            const choice = await vscode.window.showQuickPick([...choices, {label: '选择已有 MOD XML…', description: '', mode: 'pick', target: undefined},
                {label: '新建 MOD XML…', description: '', mode: 'new', target: undefined}], {title: '选择复制目标'});
            if (!choice) return;
            let destination = choice.target, isNew = choice.mode === 'new';
            if (choice.mode === 'pick') destination = (await vscode.window.showOpenDialog({title: '选择已有 MOD XML', canSelectMany: false,
                filters: {XML: ['xml']}, defaultUri: vscode.Uri.file(modRoot)}))?.[0];
            else if (isNew) destination = await vscode.window.showSaveDialog({title: '新建资产 XML', filters: {XML: ['xml']},
                defaultUri: vscode.Uri.file(path.join(modRoot, 'Data', `${newId}.xml`))});
            if (!destination || destination.scheme !== 'file') return;
            const physical = await checkedDocument(service, destination.fsPath);
            if (!containsPath(modRoot, physical) || service.index.isReadonly(physical)) throw new Error('目标必须位于当前 MOD 的物理路径内。');
            let targetDocument: vscode.TextDocument | undefined;
            if (isNew) {
                try {await fs.access(physical); throw new Error('该文件已存在，请选择“已有 MOD XML”。');} catch (error) {if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;}
            } else targetDocument = await openKnownDocument(physical, service);
            if (targetDocument) {await serviceFor(targetDocument);}
            const model = targetDocument ? service.getDocument(physical) : parseXml(`<AssetDeclaration xmlns="${source.namespace}"></AssetDeclaration>`, physical, 0);
            if (!model) throw new Error('目标 XML 尚未完成索引。');
            const reference = selected.reference && !service.index.isReadonly(selected.reference.uri) ? selected.reference : undefined;
            let includeIn;
            if (!reference && pathIdentity(physical) !== pathIdentity(service.documents.normalize(entry.fsPath))) {
                let entryDocument: vscode.TextDocument;
                try {entryDocument = await openKnownDocument(entry.fsPath, service);} catch {throw new Error('当前 MOD 缺少入口，请将 Data/Mod.xml 选为复制目标。');}
                await serviceFor(entryDocument); includeIn = service.getDocument(entry.fsPath);
                if (!includeIn) throw new Error('MOD 入口尚未完成索引。');
            }
            const plan = await planCopyToMod({service, source, target: model, targetIsNew: isNew, newId, reference, includeIn});
            const confirmed = await vscode.window.showInformationMessage(`复制 ${source.type}:${source.id} 为 ${newId}，写入 ${path.basename(physical)}${reference ? '并修改当前 MOD 引用' : ''}`,
                {modal: true, detail: `SDK 依赖通过 reference Include 保留。\n\n${plan.preview}`}, '复制');
            if (confirmed !== '复制') return;
            const opened = new Map<string, vscode.TextDocument>();
            const sourceDocument = await openKnownDocument(plan.source.uri, service);
            for (const [file, change] of plan.changes) if (!change.isNew) opened.set(file, await openKnownDocument(file, service));
            for (const file of plan.changes.keys()) {
                const checked = await checkedDocument(service, file);
                if (!containsPath(modRoot, checked) || service.index.isReadonly(checked)) throw new Error('复制目标的物理路径已变化，请重试。');
            }
            // All asynchronous reads are complete before this atomic preflight.
            if (sourceDocument.version !== plan.source.version || sourceDocument.getText() !== plan.source.text) throw new Error('SDK 来源已更新，请重新复制。');
            for (const [file, current] of opened) {
                const expected = plan.changes.get(file)!.document;
                if (current.version !== expected.version || current.getText() !== expected.text) throw new Error('目标或引用文档已更新，请重新复制。');
            }
            const preflight = [...plan.changes].filter(([, change]) => !change.isNew).flatMap(([file, change]) => change.patches.map(patch => ({uri: file,
                documentVersion: change.document.version, ...patch})));
            if (preflight.length) service.prepareEdits(preflight);
            const edit = new vscode.WorkspaceEdit();
            for (const [file, change] of plan.changes) {
                if (change.isNew) {edit.createFile(uri(file), {overwrite: false, ignoreIfExists: false}); edit.insert(uri(file), new vscode.Position(0, 0), applyTextPatches(change.document.text, change.patches));}
                else for (const patch of change.patches) edit.replace(opened.get(file)!.uri, range(opened.get(file)!, patch.start, patch.end), patch.text);
            }
            if (!await vscode.workspace.applyEdit(edit, {isRefactoring: true})) throw new Error('复制未能应用。');
            await vscode.window.showTextDocument(uri(physical), {preview: false});
        } catch (error) {void vscode.window.showErrorMessage(`NewSdk XML：${error instanceof Error ? error.message : String(error)}`);}
    }));

    subscriptions.push(vscode.languages.registerCodeActionsProvider(selector, {
        async provideCodeActions(document, _selection, actionContext, token) {
            const service = await serviceFor(document, token); if (!service) return [];
            const source = service.getDocument(document.uri.fsPath); if (!source?.valid || !service.schema) return [];
            const actions: vscode.CodeAction[] = [];
            const selected = selection(service, document, document.offsetAt(_selection.start));
            if (selected.asset?.readonly && !selected.reference?.dynamic && !selected.reference?.ambiguous) {
                const action = new vscode.CodeAction('复制 SDK 资产到 MOD 并修改当前引用…', vscode.CodeActionKind.RefactorRewrite);
                action.command = {title: action.title, command: 'newsdk.xml.copyToMod', arguments: [document.uri, document.offsetAt(_selection.start)]}; actions.push(action);
            }
            if (service.index.isReadonly(document.uri.fsPath)) return actions;
            for (const diagnostic of actionContext.diagnostics.filter(diagnostic => diagnostic.source === 'NewSdk XML' && diagnostic.code === 'xml.required-attribute')) {
                const issue = schemaIssues(source, service.schema).find(issue => issue.code === 'xml.required-attribute' && issue.defaultValue !== undefined && range(document, issue.start, issue.end).isEqual(diagnostic.range));
                if (!issue?.attribute || issue.defaultValue === undefined || !issue.nodePath) continue;
                const patch = createAttributeInsertionPatch(source, {nodePath: issue.nodePath, attribute: issue.attribute, value: issue.defaultValue, documentVersion: source.version});
                const edit = new vscode.WorkspaceEdit(); edit.replace(document.uri, range(document, patch.start, patch.end), patch.text);
                const action = new vscode.CodeAction(`添加 ${issue.attribute} 的 XSD 默认值`, vscode.CodeActionKind.QuickFix);
                action.edit = edit; action.diagnostics = [diagnostic]; actions.push(action);
            }
            for (const diagnostic of actionContext.diagnostics.filter(diagnostic => diagnostic.source === 'NewSdk XML' && diagnostic.code === 'xml.enum')) {
                const offset = document.offsetAt(diagnostic.range.start); const node = source.elementAt(offset); if (!node) continue;
                const attribute = Object.values(node.attributeTokens).find(token => token.valueStart === offset); if (!attribute) continue;
                const field = service.schema.getAttributes(node).find(field => field.name === attribute.name);
                for (const value of field?.enumValues.slice(0, 8) ?? []) {
                    const patch = createAttributePatch(source, {nodePath: node.path, attribute: attribute.name, value,
                        expectedValue: attribute.value, documentVersion: source.version});
                    const edit = new vscode.WorkspaceEdit(); edit.replace(document.uri, range(document, patch.start, patch.end), patch.text);
                    const action = new vscode.CodeAction(`将 ${attribute.name} 改为 ${value}`, vscode.CodeActionKind.QuickFix);
                    action.edit = edit; action.diagnostics = [diagnostic]; actions.push(action);
                }
            }
            const selectedNode = source.elementAt(document.offsetAt(_selection.start));
            if (selectedNode) for (const field of service.schema.getAttributes(selectedNode).filter(field => field.default !== undefined
                && !Object.hasOwn(selectedNode.attributes, field.name)).slice(0, 16)) {
                const patch = createAttributeInsertionPatch(source, {nodePath: selectedNode.path, attribute: field.name,
                    value: field.default!, documentVersion: source.version});
                const edit = new vscode.WorkspaceEdit(); edit.replace(document.uri, range(document, patch.start, patch.end), patch.text);
                const action = new vscode.CodeAction(`显式设置 ${field.name}="${field.default}"（XSD 默认值）`, vscode.CodeActionKind.RefactorRewrite);
                action.edit = edit; actions.push(action);
            }
            return actions;
        },
    }, {providedCodeActionKinds: [vscode.CodeActionKind.QuickFix, vscode.CodeActionKind.RefactorRewrite]}));

    async function updateDiagnostics(document: vscode.TextDocument): Promise<void> {
        const key = document.uri.toString(); const generation = (generations.get(key) ?? 0) + 1; generations.set(key, generation);
        try {
            const version = document.version; const service = await serviceFor(document); if (!service) {diagnostics.delete(document.uri); return;}
            const source = service.getDocument(document.uri.fsPath); if (!source) return;
            const graph = scopes.get(key);
            const problems: SourceIssue[] = [...schemaIssues(source, service.schema), ...defineIssues(source, graph?.documents.values() ?? [source])];
            for (const issue of scopes.get(key)?.issues ?? []) if (issue.uri && pathIdentity(issue.uri) === pathIdentity(source.uri)) problems.push({uri: source.uri, start: issue.start ?? 0, end: issue.end ?? (issue.start ?? 0) + 1,
                code: issue.code, message: issue.message, severity: issue.severity});
            for (const reference of service.getReferences(document.uri.fsPath)) {
                if (reference.dynamic || !reference.ambiguous && (reference.targets.length || reference.manifestTargets?.length)) continue;
                const pending = service.completeness !== 'complete';
                problems.push({uri: reference.uri, start: reference.start, end: reference.end, code: reference.ambiguous ? 'xml.ambiguous-reference' : 'xml.unresolved-reference',
                    message: reference.ambiguous ? `引用 ${reference.type}:${reference.value} 有多个可见定义。` : `${pending ? '待确认：' : ''}尚未找到 ${reference.type}:${reference.value}。`,
                    severity: pending ? 'information' : 'warning'});
            }
            const declarations = service.findAssets({}).filter(asset => pathIdentity(asset.uri) === pathIdentity(source.uri));
            const seen = new Map<string, AssetRecord>();
            for (const asset of declarations) {
                const identity = JSON.stringify([asset.scopeUri ?? asset.uri, asset.namespace, asset.type, asset.id.toLowerCase()]);
                if (seen.has(identity)) {const id = asset.node.attributeTokens.id; problems.push({uri: asset.uri, start: id.valueStart, end: id.valueEnd, code: 'xml.duplicate-id', message: `同一文档作用域中重复定义 ${asset.type}:${asset.id}。`, severity: 'warning'});}
                else seen.set(identity, asset);
            }
            if (disposed || generations.get(key) !== generation || document.version !== version || services.getService(document) !== service) return;
            const unique = new Set<string>();
            diagnostics.set(document.uri, problems.filter(issue => {const key = `${issue.start}:${issue.end}:${issue.code}:${issue.message}`; if (unique.has(key)) return false; unique.add(key); return true;}).slice(0, 256).map(issue => {
                const diagnostic = new vscode.Diagnostic(range(document, issue.start, issue.end), issue.message,
                    issue.severity === 'error' ? vscode.DiagnosticSeverity.Error : issue.severity === 'information' ? vscode.DiagnosticSeverity.Information : vscode.DiagnosticSeverity.Warning);
                diagnostic.code = issue.code; diagnostic.source = 'NewSdk XML'; return diagnostic;
            }));
        } catch { /* A superseded service request is retried by the next editor event. */ }
    }
    function schedule(document: vscode.TextDocument): void {
        if (disposed || !eligible(document)) return;
        const key = document.uri.toString(); const previous = timers.get(key); if (previous) clearTimeout(previous);
        timers.set(key, setTimeout(() => {timers.delete(key); void updateDiagnostics(document);}, 180));
    }
    subscriptions.push(vscode.workspace.onDidOpenTextDocument(schedule));
    subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
        if (!eligible(event.document)) return;
        schedule(event.document);
    }));
    subscriptions.push(vscode.workspace.onDidCloseTextDocument(document => {
        const key = document.uri.toString(); const timer = timers.get(key); if (timer) clearTimeout(timer); timers.delete(key); generations.delete(key);
        const tracked = snapshots.get(key); snapshots.delete(key);
        scopes.delete(key);
        tracked?.service.closeDocument(document.uri.fsPath); diagnostics.delete(document.uri);
    }));
    async function fileChanged(changed: vscode.Uri, onlyService?: AssetService): Promise<void> {
        const servicesSeen = new Set<AssetService>();
        if (onlyService) {
            try {const checked = await checkedDocument(onlyService, changed.fsPath); onlyService.invalidate(checked); servicesSeen.add(onlyService);} catch {return;}
        }
        for (const document of vscode.workspace.textDocuments.filter(eligible)) {
            const service = services.getService(document); if (!service || onlyService && service !== onlyService) continue;
            if (!servicesSeen.has(service)) {
                try {const checked = await checkedDocument(service, changed.fsPath); service.invalidate(checked);} catch {continue;}
                servicesSeen.add(service);
            }
            schedule(document);
        }
    }
    const api: XmlProviders = {
        refresh() {
            for (const timer of timers.values()) clearTimeout(timer); timers.clear(); generations.clear();
            observedServices.forEach(subscription => subscription.dispose()); observedServices.clear();
            diagnostics.clear(); snapshots.clear(); scopes.clear(); lensesChanged.fire();
            for (const document of vscode.workspace.textDocuments) schedule(document);
        },
        dispose() {disposed = true; for (const timer of timers.values()) clearTimeout(timer); timers.clear(); generations.clear(); snapshots.clear(); scopes.clear();
            observedServices.forEach(subscription => subscription.dispose()); observedServices.clear(); subscriptions.forEach(subscription => subscription.dispose());},
    };
    context.subscriptions.push(api); api.refresh(); return api;
}
