import type { XmlDocument, XmlElement } from '../core/xmlDocument';
import type { AssetRecord, IncludeEdge } from '../index/assetIndex';
import type { ReferenceRecord } from '../index/referenceIndex';
import type { XsdField, XsdModel } from '../model/xsdModel';
import type { CompletionContext } from './languageFeatures';
import { pathIdentity } from '../../project';

export interface DefineRecord {name: string; value?: string; uri: string; start: number; end: number; node: XmlElement}
export interface SemanticRange {start: number; end: number; type: 'newsdkAsset' | 'newsdkReference' | 'newsdkDefine' | 'newsdkInclude' | 'newsdkResource'; declaration?: boolean}
export interface OutlineEntry {name: string; detail: string; start: number; end: number; selectionStart: number; selectionEnd: number; kind: 'asset' | 'define'; type?: string}
const documentDefines = new WeakMap<XmlDocument, DefineRecord[]>();

/** Only outgoing Includes contribute names. Unrelated entries in a project graph do not. */
export function visibleDefineDocuments(sourceUri: string, documents: Iterable<XmlDocument>, includes: readonly IncludeEdge[]): XmlDocument[] {
    const byUri = new Map([...documents].map(document => [pathIdentity(document.uri), document]));
    const outgoing = new Map<string, string[]>();
    for (const edge of includes) {const from = pathIdentity(edge.from), targets = outgoing.get(from) ?? []; targets.push(pathIdentity(edge.to)); outgoing.set(from, targets);}
    const pending = [pathIdentity(sourceUri)], seen = new Set<string>();
    while (pending.length) {
        const current = pending.pop()!; if (seen.has(current)) continue; seen.add(current);
        for (const target of outgoing.get(current) ?? []) if (!seen.has(target)) pending.push(target);
    }
    return [...seen].flatMap(uri => byUri.has(uri) ? [byUri.get(uri)!] : []);
}

export function defineDeclarations(documents: Iterable<XmlDocument>): DefineRecord[] {
    const result: DefineRecord[] = [], seen = new Set<string>();
    for (const document of documents) {
        let declarations = documentDefines.get(document);
        if (!declarations) {
            declarations = [];
            for (const node of document.elements) {
                if (node.localName !== 'Define' || node.parent?.localName !== 'Defines') continue;
                const name = node.attributeTokens.name; if (!name?.value) continue;
                declarations.push({name: name.value, value: node.attributes.value ?? node.textTokens.map(token => token.value).join('').trim(), uri: document.uri,
                    start: name.valueStart, end: name.valueEnd, node});
            }
            documentDefines.set(document, declarations);
        }
        for (const declaration of declarations) {
            const key = `${pathIdentity(declaration.uri)}:${declaration.start}`; if (seen.has(key)) continue; seen.add(key); result.push(declaration);
        }
    }
    return result;
}

export function defineNameAt(document: XmlDocument, offset: number): {name: string; start: number; end: number} | undefined {
    for (const declaration of defineDeclarations([document])) if (declaration.start <= offset && offset <= declaration.end) return declaration;
    const node = document.elementAt(offset); if (!node) return;
    const tokens = [...Object.values(node.attributeTokens).map(token => ({start: token.valueStart, end: token.valueEnd})), ...node.textTokens];
    for (const token of tokens) {
        if (offset < token.start || offset > token.end) continue;
        for (const match of document.text.slice(token.start, token.end).matchAll(/\$([A-Za-z_][A-Za-z0-9_]*)/g)) {
            const start = token.start + match.index!, end = start + match[0].length;
            if (start <= offset && offset <= end) return {name: match[1], start, end};
        }
    }
    return;
}

export function completionField(schema: XsdModel | undefined, context: CompletionContext): XsdField | undefined {
    if (!schema || !context.node || !context.value) return;
    return context.value.attribute ? schema.getAttributes(context.node).find(field => field.name === context.value!.attribute)
        : schema.getElement(context.node);
}

/** Replacing one word keeps already chosen flags, and never suggests them twice. */
export function valueCompletionState(context: CompletionContext, offset: number, field?: XsdField): {start: number; end: number; prefix: string; used: Set<string>; values: string[]} {
    const list = !!field?.list;
    const prefix = list ? /[^\s]*$/.exec(context.prefix)![0] : context.prefix;
    const start = list ? offset - prefix.length : context.start;
    const whole = context.value?.value ?? context.prefix, relativeStart = start - (context.value?.start ?? context.start);
    const relativeEnd = offset - (context.value?.start ?? context.start);
    const end = list ? offset + /^\S*/.exec(whole.slice(relativeEnd))![0].length : context.value?.end ?? offset;
    const remaining = list ? `${whole.slice(0, relativeStart)} ${whole.slice(relativeEnd).replace(/^\S*/, '')}` : '';
    const used = new Set(remaining.trim().split(/\s+/).filter(Boolean));
    return {start, end, prefix, used, values: (field?.enumValues ?? []).filter(value => !list || !used.has(value))};
}

export function documentOutline(document: XmlDocument): OutlineEntry[] {
    const result: OutlineEntry[] = [];
    for (const node of document.elements) {
        const id = node.attributeTokens.id;
        if (!id || node.parent && node.parent.localName !== 'AssetDeclaration') continue;
        result.push({name: `${node.localName}: ${id.value}`, detail: node.localName, type: node.localName, kind: 'asset',
            start: node.start, end: node.end, selectionStart: id.valueStart, selectionEnd: id.valueEnd});
    }
    for (const record of defineDeclarations([document])) result.push({name: `$${record.name}`, detail: record.value ?? '', kind: 'define',
        start: record.node.start, end: record.node.end, selectionStart: record.start, selectionEnd: record.end});
    return result.sort((a, b) => a.start - b.start);
}

/** Source ranges derive from the shared CST and reference records, never another parser. */
export function semanticRanges(document: XmlDocument, assets: readonly AssetRecord[], references: readonly ReferenceRecord[]): SemanticRange[] {
    const result: SemanticRange[] = [], add = (start: number, end: number, type: SemanticRange['type'], declaration = false) => {
        if (end > start) result.push({start, end, type, ...(declaration ? {declaration} : {})});
    };
    for (const asset of assets) if (pathIdentity(asset.uri) === pathIdentity(document.uri)) {
        const token = asset.node.attributeTokens.id; if (token) add(token.valueStart, token.valueEnd, 'newsdkAsset', true);
    }
    for (const reference of references) if (!reference.dynamic && pathIdentity(reference.uri) === pathIdentity(document.uri)) add(reference.start, reference.end, 'newsdkReference');
    for (const record of defineDeclarations([document])) add(record.start, record.end, 'newsdkDefine', true);
    for (const node of document.elements) {
        const tokens = [...Object.values(node.attributeTokens).map(token => ({name: token.name, start: token.valueStart, end: token.valueEnd})),
            ...node.textTokens.map(token => ({name: undefined, start: token.start, end: token.end}))];
        for (const token of tokens) {
            const raw = document.text.slice(token.start, token.end);
            const include = node.localName === 'Include' && node.parent?.localName === 'Includes' && token.name === 'source'
                || node.localName === 'include' && node.namespaceUri === 'http://www.w3.org/2001/XInclude' && token.name === 'href';
            if (include && !raw.includes('$')) add(token.start, token.end, 'newsdkInclude');
            else if (/^\s*(?:DATA|ART|AUDIO):/i.test(raw) && !raw.includes('$')) add(token.start, token.end, 'newsdkResource');
            for (const match of raw.matchAll(/\$[A-Za-z_][A-Za-z0-9_]*/g)) add(token.start + match.index!, token.start + match.index! + match[0].length, 'newsdkDefine');
        }
    }
    // Aliased xi:include records can identify one source token several times.
    const unique = [...new Map(result.map(token => [`${token.start}:${token.end}:${token.type}`, token])).values()];
    const sorted = unique.sort((a, b) => a.start - b.start || a.end - b.end);
    const nonOverlapping: SemanticRange[] = []; let previousEnd = -1;
    for (const token of sorted) if (token.start >= previousEnd) {nonOverlapping.push(token); previousEnd = token.end;}
    return nonOverlapping;
}
