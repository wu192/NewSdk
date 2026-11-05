import * as path from 'node:path';
import {containsPath, pathIdentity} from '../../project';
import type {AssetService} from '../services/assetService';
import type {AssetRecord} from '../index/assetIndex';
import type {ReferenceRecord} from '../index/referenceIndex';
import {applyTextPatches, createAttributePatch, type TextPatch} from './editTransaction';
import {parseXml, type XmlDocument, type XmlElement} from './xmlDocument';

export interface CopyToModOptions {
    service: AssetService; source: AssetRecord; target: XmlDocument; newId: string;
    targetIsNew?: boolean; reference?: ReferenceRecord; includeIn?: XmlDocument;
}
export interface CopyDocumentChange {document: XmlDocument; patches: TextPatch[]; isNew: boolean}
export interface CopyToModPlan {assetId: string; changes: Map<string, CopyDocumentChange>; source: XmlDocument; preview: string}
const escape = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
const same = (a: string, b: string) => pathIdentity(a) === pathIdentity(b);

/** Copy only verified source XML. Referenced SDK definitions stay read-only imports. */
export async function planCopyToMod(options: CopyToModOptions): Promise<CopyToModPlan> {
    const {service, source, target, newId} = options;
    const physicalTarget = await service.index.checkedFile(service.documents.normalize(target.uri));
    if (!service.options.modRoot || !containsPath(service.options.modRoot, physicalTarget) || service.index.isReadonly(physicalTarget)) throw new Error('复制目标必须位于当前 MOD 内。');
    if (!source.readonly || !service.index.isReadonly(source.uri)) throw new Error('请选择已解析到 XML 定义的 SDK 资产。');
    if (!/^[0-9A-Za-z_.-]+$/.test(newId) || newId.length > 512) throw new Error('新 ID 必须符合 SDK InstanceId 规则。');
    if (service.completeness !== 'complete') throw new Error('索引尚未完整，不能确认复制依赖和新 ID。');
    if (service.findAssets({type: source.type, id: newId}).length || service.manifests?.find(source.type, newId).length) throw new Error('该类型已有同名资产，请换一个新 ID。');
    const original = service.getDocument(source.uri);
    const node = original?.find(source.node.path), root = target.roots[0];
    if (!original?.valid || original.shallow || !node?.attributeTokens.id || !target.valid || target.shallow || root?.localName !== 'AssetDeclaration') throw new Error('复制需要完整有效的来源 XML 和 AssetDeclaration 目标。');
    if (root.namespaceUri !== source.namespace) throw new Error('来源和目标资产命名空间不同。');
    const inspect = (element: XmlElement): void => {
        if (element.localName === 'include' && element.namespaceUri === 'http://www.w3.org/2001/XInclude') throw new Error('含 xi:include 的子树需要先明确相对文件范围，不能自动移动。');
        for (const [name, value] of Object.entries(element.attributes)) {
            if (name.startsWith('xmlns')) continue;
            if (/[$=]/.test(value)) throw new Error('含宏或动态表达式的 SDK 资产不能自动跨文件复制，请先明确目标宏范围。');
            const field = service.schema?.getAttributes(element).find(field => field.name === name);
            if (value && /(?:file|path)/i.test(field?.type ?? '') && !/^[A-Za-z]+:/.test(value) && !path.isAbsolute(value)) throw new Error('资产包含相对资源路径，移动前需要在 XML 中明确资源搜索前缀。');
        }
        for (const token of element.textTokens) if (/[$=]/.test(token.value)) throw new Error('含宏或动态表达式的文本不能自动跨文件复制。');
        element.children.forEach(inspect);
    };
    inspect(node);
    const aliases = service.findAssets({type: source.type, id: source.id}).filter(asset => same(asset.uri, source.uri) && asset.node.path === source.node.path);
    const aliasKeys = new Set(aliases.map(asset => asset.key));
    for (const reference of service.getReferences().filter(reference => reference.ownerKey && aliasKeys.has(reference.ownerKey))) {
        if (reference.dynamic || reference.ambiguous || reference.targets.length !== 1 && !reference.manifestTargets?.length) throw new Error(`无法确认 SDK 资产的依赖 ${reference.type}:${reference.value}。`);
    }
    const idPatch = createAttributePatch(original, {nodePath: node.path, attribute: 'id', expectedValue: source.id, value: newId, documentVersion: original.version});
    let fragment = applyTextPatches(original.text.slice(node.start, node.end), [{...idPatch, start: idPatch.start - node.start, end: idPatch.end - node.start}]);
    const namespaces = new Map<string, string>();
    for (let ancestor: XmlElement | undefined = node; ancestor; ancestor = ancestor.parent) for (const [name, value] of Object.entries(ancestor.attributes)) {
        if ((name === 'xmlns' || name.startsWith('xmlns:')) && !namespaces.has(name)) namespaces.set(name, value);
    }
    const additions = [...namespaces].filter(([name]) => !Object.hasOwn(node.attributes, name)).map(([name, value]) => ` ${name}="${escape(value)}"`).join('');
    fragment = fragment.slice(0, 1 + node.name.length) + additions + fragment.slice(1 + node.name.length);
    const changes = new Map<string, CopyDocumentChange>();
    const add = (document: XmlDocument, patches: TextPatch[], isNew = false) => {
        const existing = changes.get(document.uri); if (existing) existing.patches.push(...patches); else changes.set(document.uri, {document, patches, isNew});
    };
    if (root.selfClosing) {
        const prefix = root.name.includes(':') ? root.name.slice(0, root.name.indexOf(':') + 1) : '';
        add(target, [appendFragment(target, `<${prefix}Includes><${prefix}Include type="reference" source="${escape(source.uri)}"/></${prefix}Includes>\n${fragment}`)], !!options.targetIsNew);
    } else {
        add(target, await includePatch(service, target, source.uri, 'reference'), !!options.targetIsNew);
        add(target, [appendFragment(target, fragment)], !!options.targetIsNew);
    }
    if (options.reference) {
        const reference = options.reference;
        if (reference.dynamic || reference.ambiguous || reference.targets.length !== 1 || !aliasKeys.has(reference.targets[0]) || service.index.isReadonly(reference.uri)) throw new Error('只允许修改当前唯一确认的 MOD 引用。');
        const document = service.getDocument(reference.uri);
        if (!document || document.version !== reference.documentVersion) throw new Error('引用内容已更新，请重试。');
        if (!same(document.uri, target.uri)) add(document, await includePatch(service, document, target.uri, 'all'));
        const prefix = reference.value.includes(':') ? reference.value.slice(0, reference.value.indexOf(':') + 1) : '';
        add(document, [{start: reference.start, end: reference.end, expected: document.text.slice(reference.start, reference.end), text: prefix + newId}]);
    }
    if (options.includeIn && !same(options.includeIn.uri, target.uri)) {
        const physicalEntry = await service.index.checkedFile(service.documents.normalize(options.includeIn.uri));
        if (!containsPath(service.options.modRoot, physicalEntry) || service.index.isReadonly(physicalEntry)) throw new Error('资产入口必须位于当前 MOD 内。');
        add(options.includeIn, await includePatch(service, options.includeIn, target.uri, 'all'));
    }
    for (const change of changes.values()) {
        change.patches = mergeInsertions(change.patches);
        if (!parseXml(applyTextPatches(change.document.text, change.patches), change.document.uri).valid) throw new Error('复制计划会产生无效 XML。');
        if (!change.isNew) service.prepareEdits(change.patches.map(patch => ({uri: change.document.uri, documentVersion: change.document.version, ...patch})));
    }
    return {assetId: newId, changes, source: original, preview: fragment};
}
function appendFragment(document: XmlDocument, fragment: string): TextPatch {
    const root = document.roots[0];
    if (root.selfClosing) return {start: root.openEnd - 2, end: root.openEnd, expected: '/>', text: `>\n${fragment}\n</${root.name}>`};
    return {start: root.closeStart, end: root.closeStart, expected: '', text: `\n${fragment}\n`};
}
async function includePatch(service: AssetService, document: XmlDocument, source: string, mode: 'all' | 'reference'): Promise<TextPatch[]> {
    const root = document.roots[0]; if (!root || root.localName !== 'AssetDeclaration') throw new Error('引用文件需要 AssetDeclaration 根元素。');
    const includes = root.children.find(child => child.localName === 'Includes');
    for (const include of includes?.children.filter(child => child.localName === 'Include' && child.attributes.source) ?? []) {
        let target: string; try {target = await service.index.resolveFile(document.uri, include.attributes.source);} catch {continue;}
        if (!same(target, source)) continue;
        if (mode === 'reference' || include.attributes.type === 'all') return [];
        return [createAttributePatch(document, {nodePath: include.path, attribute: 'type', expectedValue: include.attributes.type, value: 'all', documentVersion: document.version})];
    }
    const prefix = root.name.includes(':') ? root.name.slice(0, root.name.indexOf(':') + 1) : '';
    const xml = `<${prefix}Include type="${mode}" source="${escape(source)}"/>`;
    if (includes) return [includes.selfClosing ? {start: includes.openEnd - 2, end: includes.openEnd, expected: '/>', text: `>${xml}</${includes.name}>`}
        : {start: includes.closeStart, end: includes.closeStart, expected: '', text: xml}];
    const insertion = `<${prefix}Includes>${xml}</${prefix}Includes>`;
    if (root.selfClosing) return [{start: root.openEnd - 2, end: root.openEnd, expected: '/>', text: `>\n${insertion}\n</${root.name}>`}];
    return [{start: root.openEnd, end: root.openEnd, expected: '', text: `\n${insertion}\n`}];
}
function mergeInsertions(patches: TextPatch[]): TextPatch[] {
    const result: TextPatch[] = [];
    for (const patch of patches) {
        const previous = result.find(value => value.start === patch.start && value.end === patch.end);
        if (previous && patch.start === patch.end) previous.text += patch.text;
        else result.push({...patch});
    }
    return result;
}
