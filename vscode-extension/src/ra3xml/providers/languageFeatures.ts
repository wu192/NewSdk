import type { XmlDocument, XmlElement } from '../core/xmlDocument';
import type { AssetRecord } from '../index/assetIndex';
import type { XsdModel } from '../model/xsdModel';
import type { AssetService, SemanticEditIntent } from '../services/assetService';
import type { TextPatch } from '../core/editTransaction';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { pathIdentity } from '../../project';

export interface SourceIssue { uri: string; start: number; end: number; code: string; message: string; severity: 'error' | 'warning' | 'information'; nodePath?: string; attribute?: string; defaultValue?: string }
export interface ValueContext { node: XmlElement; attribute?: string; value: string; start: number; end: number }
export interface CompletionContext { kind: 'element' | 'attribute' | 'value'; node?: XmlElement; parent?: XmlElement; prefix: string; start: number; value?: ValueContext }

export function unresolvedReferenceSeverity(completeness: 'partial' | 'complete', setting: string): SourceIssue['severity'] | undefined {
    if (completeness === 'partial') return 'information';
    return setting === 'off' ? undefined : setting === 'information' ? 'information' : 'warning';
}

export function valueAt(document: XmlDocument, offset: number): ValueContext | undefined {
    const node = document.elementAt(offset);
    if (!node) return undefined;
    for (const token of Object.values(node.attributeTokens)) if (token.valueStart <= offset && offset <= token.valueEnd) {
        return {node, attribute: token.name, value: token.value, start: token.valueStart, end: token.valueEnd};
    }
    const token = node.textTokens.find(token => token.start <= offset && offset <= token.end);
    if (!token) return undefined;
    if (!token.value.trim()) return {node, value: '', start: offset, end: offset};
    const before = /^\s*/.exec(token.value)![0].length;
    const after = /\s*$/.exec(token.value)![0].length;
    return {node, value: token.value.trim(), start: token.start + before, end: token.end - after};
}

/** Completion recovery is lexical only; authoritative queries still use the shared CST. */
export function completionAt(document: XmlDocument, offset: number): CompletionContext | undefined {
    const text = document.text; const before = text.slice(0, offset);
    if (before.lastIndexOf('<!--') > before.lastIndexOf('-->') || before.lastIndexOf('<![CDATA[') > before.lastIndexOf(']]>')) return undefined;
    const value = valueAt(document, offset);
    if (value) return {kind: 'value', node: value.node, prefix: text.slice(value.start, offset), start: value.start, value};
    const opening = before.lastIndexOf('<'); const closing = before.lastIndexOf('>');
    if (opening <= closing) return undefined;
    const fragment = before.slice(opening + 1);
    if (/^[!?/]/.test(fragment)) return undefined;
    const elementName = /^([\w.:-]*)$/.exec(fragment);
    if (elementName) {
        const container = document.elementAt(opening);
        return {kind: 'element', parent: container?.start === opening ? container.parent : container, prefix: elementName[1], start: opening + 1};
    }
    const tag = /^([\w.:-]+)/.exec(fragment)?.[1];
    if (!tag) return undefined;
    const existing = document.elements.find(node => node.start === opening);
    const parent = document.elementAt(opening);
    const node = existing ?? {
        name: tag, localName: tag.split(':').pop()!, namespaceUri: parent?.namespaceUri ?? '', attributes: {}, attributeTokens: {}, children: [], textTokens: [],
        parent, path: `${parent?.path ?? ''}/${tag}[1]`, start: opening, openEnd: offset, closeStart: offset, end: offset, selfClosing: false,
    };
    const partialValue = /([\w.:-]+)\s*=\s*(["'])([^"']*)$/.exec(fragment);
    if (partialValue) {
        const start = offset - partialValue[3].length;
        const partial: ValueContext = {node, attribute: partialValue[1], value: partialValue[3], start, end: offset};
        return {kind: 'value', node, prefix: partial.value, start, value: partial};
    }
    const attribute = /(?:\s)([\w.:-]*)$/.exec(fragment);
    return attribute ? {kind: 'attribute', node, prefix: attribute[1], start: offset - attribute[1].length} : undefined;
}

export function assetAt(assets: readonly AssetRecord[], uri: string, offset: number): AssetRecord | undefined {
    return assets.find(asset => pathIdentity(asset.uri) === pathIdentity(uri) && asset.node.attributeTokens.id && asset.node.attributeTokens.id.valueStart <= offset && offset <= asset.node.attributeTokens.id.valueEnd);
}

/** No unresolved-reference errors are emitted here: only the shared resolver knows visibility. */
export function schemaIssues(document: XmlDocument, schema?: XsdModel): SourceIssue[] {
    const issues: SourceIssue[] = document.issues.map(issue => ({...issue, uri: document.uri, severity: 'error'}));
    if (!document.valid || !schema) return issues;
    for (const node of document.elements) {
        const declaration = schema.getElement(node);
        if (!declaration) {
            const parentType = node.parent && schema.getType(schema.getElement(node.parent)?.type ?? '');
            const knownNamespace = !node.namespaceUri || node.namespaceUri === 'uri:ea.com:eala:asset';
            if (knownNamespace && (parentType && !parentType.allowAnyChild || !node.parent && node.localName !== 'AssetDeclaration')) issues.push({uri: document.uri,
                start: node.start + 1, end: node.start + 1 + node.name.length, severity: schema.complete ? 'warning' : 'information', code: 'xml.unknown-element',
                message: `${schema.complete ? '' : '待确认：'}XSD 中未声明元素 ${node.localName}。`, nodePath: node.path});
            continue;
        }
        const nodeType = schema.getType(declaration.type);
        const fields = schema.getAttributes(node);
        if (nodeType && !nodeType.allowAnyAttribute) for (const token of Object.values(node.attributeTokens)) {
            if (token.name === 'xmlns' || token.name.includes(':') || fields.some(field => field.name === token.name)) continue;
            issues.push({uri: document.uri, start: token.start, end: token.start + token.name.length,
                severity: schema.complete ? 'warning' : 'information', code: 'xml.unknown-attribute', message: `${schema.complete ? '' : '待确认：'}${node.localName} 未声明属性 ${token.name}。`, nodePath: node.path, attribute: token.name});
        }
        let inherited = false;
        for (let ancestor: XmlElement | undefined = node; ancestor; ancestor = ancestor.parent) if (ancestor.attributes.inheritFrom) {inherited = true; break;}
        for (const field of fields) {
            const token = node.attributeTokens[field.name];
            if (!token && field.required && (!inherited || field.name === 'id' && !!node.attributes.inheritFrom)) issues.push({uri: document.uri, start: node.start, end: node.openEnd,
                severity: 'warning', code: 'xml.required-attribute', message: `缺少必填属性 ${field.name}。`, nodePath: node.path, attribute: field.name,
                ...(field.default !== undefined ? {defaultValue: field.default} : {})});
            if (token && field.name === 'id' && !token.value.trim()) issues.push({uri: document.uri, start: token.valueStart, end: token.valueEnd,
                severity: 'warning', code: 'xml.empty-id', message: '资产 ID 不能为空。', nodePath: node.path, attribute: field.name});
            const values = field.list ? token?.value.trim().split(/\s+/) ?? [] : [token?.value ?? ''];
            if (token && field.enumValues.length && !/[=$]/.test(token.value) && values.some(value => !field.enumValues.includes(value))) {
                issues.push({uri: document.uri, start: token.valueStart, end: token.valueEnd, severity: 'warning', code: 'xml.enum', message: `${field.name} 的值应为：${field.enumValues.join('、')}。`, nodePath: node.path, attribute: field.name});
            }
            if (token && !/[=$]/.test(token.value)) {
                const mismatch = primitiveMismatch(schema, field.type, token.value, !!field.list);
                if (mismatch) issues.push({uri: document.uri, start: token.valueStart, end: token.valueEnd, severity: 'warning', code: 'xml.type',
                    message: `${field.name}：${mismatch}`, nodePath: node.path, attribute: field.name});
                if (field.fixed !== undefined && token.value !== field.fixed) issues.push({uri: document.uri, start: token.valueStart, end: token.valueEnd,
                    severity: 'warning', code: 'xml.fixed', message: `${field.name} 的 XSD 固定值为 ${field.fixed}。`, nodePath: node.path, attribute: field.name});
            }
        }
        if (document.shallow) continue;
        const element = schema.getElement(node);
        if (element?.enumValues.length && !node.children.length) for (const token of node.textTokens) {
            const value = token.value.trim(), values = element.list ? value.split(/\s+/) : [value];
            if (value && !/[=$]/.test(value) && values.some(value => !element.enumValues.includes(value))) issues.push({uri: document.uri,
                start: token.start, end: token.end, severity: 'warning', code: 'xml.enum-text', message: `${node.localName} 的值应为：${element.enumValues.join('、')}。`, nodePath: node.path});
        }
        if (element && !node.children.length) for (const token of node.textTokens) {
            if (!token.value.trim() || /[=$]/.test(token.value)) continue;
            const mismatch = primitiveMismatch(schema, element.type, token.value, !!element.list);
            if (mismatch) issues.push({uri: document.uri, start: token.start, end: token.end, severity: 'warning', code: 'xml.type-text',
                message: `${node.localName}：${mismatch}`, nodePath: node.path});
        }
        for (const field of schema.getChildren(node)) if (!inherited && field.required && !node.children.some(child => child.localName === field.name)) {
            issues.push({uri: document.uri, start: node.start, end: node.openEnd, severity: 'warning', code: 'xml.required-element', message: `缺少必填子元素 ${field.name}。`, nodePath: node.path});
        }
    }
    return issues;
}

/** Check only XSD primitive rules this editor can prove; expressions stay compiler-owned. */
function primitiveMismatch(schema: XsdModel, type: string, raw: string, list: boolean): string | undefined {
    if (list) return;
    const seen = new Set<string>(); let name: string | undefined = type;
    while (name && !seen.has(name)) {
        seen.add(name); const local = name.slice(name.lastIndexOf(':') + 1), value = raw.trim();
        if (local === 'boolean') return /^(?:true|false|0|1)$/.test(value) ? undefined : '应为布尔值 true、false、0 或 1。';
        const bounds: Record<string, [bigint | undefined, bigint | undefined]> = {
            integer: [undefined, undefined], nonNegativeInteger: [0n, undefined], positiveInteger: [1n, undefined], nonPositiveInteger: [undefined, 0n], negativeInteger: [undefined, -1n],
            byte: [-128n, 127n], short: [-32768n, 32767n], int: [-2147483648n, 2147483647n], long: [-9223372036854775808n, 9223372036854775807n],
            unsignedByte: [0n, 255n], unsignedShort: [0n, 65535n], unsignedInt: [0n, 4294967295n], unsignedLong: [0n, 18446744073709551615n],
        };
        if (Object.hasOwn(bounds, local)) {
            if (!/^[+-]?\d+$/.test(value)) return `应为 ${local} 整数。`;
            const parsed = BigInt(value), [min, max] = bounds[local];
            return min !== undefined && parsed < min || max !== undefined && parsed > max ? `超出 ${local} 的数值范围。` : undefined;
        }
        if (local === 'decimal') return /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(value) ? undefined : '应为 decimal 数值。';
        if (local === 'float' || local === 'double') return /^(?:[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?|-?INF|NaN)$/.test(value) ? undefined : `应为 ${local} 数值。`;
        name = schema.getType(name)?.base;
    }
    return;
}

export function defineIssues(document: XmlDocument, visible: Iterable<XmlDocument>): SourceIssue[] {
    if (!document.valid || document.shallow) return [];
    const names = new Set<string>();
    for (const source of visible) for (const node of source.elements) if (node.localName === 'Define' && node.parent?.localName === 'Defines' && node.attributes.name) names.add(node.attributes.name);
    const issues: SourceIssue[] = [], local = new Set<string>();
    for (const node of document.elements) {
        if (node.localName === 'Define' && node.parent?.localName === 'Defines' && node.attributeTokens.name) {
            const token = node.attributeTokens.name;
            if (local.has(token.value)) issues.push({uri: document.uri, start: token.valueStart, end: token.valueEnd, code: 'xml.duplicate-define',
                severity: 'warning', message: `当前文档重复定义 $${token.value}。`});
            local.add(token.value);
        }
        const tokens = [...Object.values(node.attributeTokens).map(token => ({value: token.rawValue, start: token.valueStart})), ...node.textTokens];
        for (const token of tokens) for (const match of token.value.matchAll(/\$([A-Za-z_][A-Za-z0-9_]*)/g)) if (!names.has(match[1])) issues.push({uri: document.uri,
            start: token.start + match.index!, end: token.start + match.index! + match[0].length, code: 'xml.unresolved-define', severity: 'information',
            message: `待确认：当前 Include 数据中尚未找到 ${match[0]}；表达式仍由编译器核实。`});
    }
    return issues;
}

/** Read each configured search root in compiler order, with bounded metadata only. */
export async function completePaths(service: AssetService, from: string, prefix: string): Promise<{value: string; directory: boolean}[]> {
    const normalized = prefix.replace(/\\/g, '/');
    const alias = /^(DATA|ART|AUDIO):/i.exec(normalized)?.[0] ?? '';
    const suffix = normalized.slice(alias.length), slash = suffix.lastIndexOf('/');
    const relativeDirectory = slash >= 0 ? suffix.slice(0, slash + 1) : '';
    const fragment = suffix.slice(relativeDirectory.length).toLowerCase();
    const head = alias + relativeDirectory;
    let roots: string[];
    if (alias) {
        try {await service.index.resolveFile(from, `${alias}./__newsdk_completion__`);} catch {return [];}
        const profile = service.index.sourceDataProfile; if (!profile) return [];
        const kind = alias.slice(0, -1).toUpperCase();
        roots = kind === 'DATA' ? profile.dataPaths : kind === 'ART' ? profile.artPaths : profile.audioPaths;
        roots = roots.map(root => root === '*' ? path.dirname(from) : root).map(root => path.resolve(root,
            kind === 'ART' && !relativeDirectory && fragment.length >= 2 ? fragment.slice(0, 2) : relativeDirectory || '.'));
    } else roots = [path.resolve(path.dirname(from), relativeDirectory || '.')];
    const result = new Map<string, {value: string; directory: boolean}>();
    for (const root of [...new Set(roots)].slice(0, 32)) {
        let visited = 0;
        try {
            const directory = await fs.opendir(await service.index.checkedFile(root));
            for await (const entry of directory) {
                if (++visited > 2048 || result.size >= 256) break;
                if (!entry.name.toLowerCase().startsWith(fragment) || !entry.isDirectory() && !entry.isFile()) continue;
                await service.index.checkedFile(path.join(root, entry.name));
                const value = `${head}${entry.name}${entry.isDirectory() ? '/' : ''}`;
                if (!result.has(value.toLowerCase())) result.set(value.toLowerCase(), {value, directory: entry.isDirectory()});
            }
        } catch { /* Missing paths are diagnosed through Include resolution. */ }
    }
    return [...result.values()];
}

export function escapeText(value: string): string { return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
export function renamedReference(value: string, newName: string): string {
    const colon = value.indexOf(':'); return colon < 0 ? newName : value.slice(0, colon + 1) + newName;
}
export function validAssetName(value: string): boolean { return value.length <= 512 && /^[0-9A-Za-z_.-]+$/.test(value); }

/** Plan the complete, version-checked change before a host constructs its preview. */
export function prepareAssetRename(service: AssetService, asset: AssetRecord, newName: string): Map<string, TextPatch[]> {
    if (!validAssetName(newName)) throw new Error('资产 ID 仅支持英文字母、数字、下划线、点和连字符（SDK InstanceId 规则）。');
    if (asset.readonly || service.index.isReadonly(asset.uri)) throw new Error('SDK 资产只读；请先复制到 MOD 再修改 MOD 引用。');
    if (service.completeness !== 'complete') throw new Error('项目索引尚未完整，无法安全预览跨文件重命名。');
    if (newName === asset.id) return new Map();
    // xi:include may expose one physical definition through several logical scopes.
    // Changing that source declaration changes every alias, so gather all bindings.
    const aliases = service.findAssets({type: asset.type, id: asset.id}).filter(other => pathIdentity(other.uri) === pathIdentity(asset.uri)
        && other.node.path === asset.node.path && other.namespace === asset.namespace);
    const aliasKeys = new Set(aliases.map(alias => alias.key));
    const references = aliases.flatMap(alias => service.findReferences(alias)).filter(reference => !reference.dynamic && !reference.ambiguous
        && reference.targets.length === 1 && !service.index.isReadonly(reference.uri));
    for (const scopeUri of new Set([...aliases.map(alias => alias.scopeUri ?? alias.uri), ...references.map(reference => reference.scopeUri)])) {
        const collisions = service.findAssets({type: asset.type, id: newName, scopeUri}).filter(other => !aliasKeys.has(other.key) && other.namespace === asset.namespace);
        if (collisions.length || service.manifests?.find(asset.type, newName).length) throw new Error('该类型和作用域已有同名资产；重命名会改变引用绑定。');
    }
    const declaration = asset.node.attributeTokens.id;
    const source = service.getDocument(asset.uri);
    if (!declaration || !source) throw new Error('资产定义已更新，请重新重命名。');
    const edits: SemanticEditIntent[] = [{uri: asset.uri, documentVersion: source.version, start: declaration.valueStart,
        end: declaration.valueEnd, expected: declaration.rawValue, text: newName}];
    for (const reference of references) {
        const document = service.getDocument(reference.uri); if (!document) throw new Error('引用文档已更新，请重新重命名。');
        edits.push({uri: reference.uri, documentVersion: reference.documentVersion, start: reference.start, end: reference.end,
            expected: document.text.slice(reference.start, reference.end), text: renamedReference(reference.value, newName)});
    }
    const unique = new Map(edits.map(edit => [`${edit.uri}:${edit.start}:${edit.end}`, edit]));
    return service.prepareEdits([...unique.values()]);
}
