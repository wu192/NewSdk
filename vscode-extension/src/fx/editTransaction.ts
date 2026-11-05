import { XmlDocument, XmlElement, XmlAttribute, parseXml } from './xmlDocument';

export interface TextPatch { start: number; end: number; text: string; expected: string }
export interface AttributeEditIntent {
    nodePath: string; attribute: string; value: string; expectedValue?: string;
    /** The text version observed at gesture start, required for all committed edits. */
    documentVersion: number;
}
export class EditConflictError extends Error {
    constructor(message = '内容已更新，请重新应用。') { super(message); this.name = 'EditConflictError'; }
}
function validateAttributeValue(value: string): void {
    if (value.length > 65536) throw new Error('属性值超过编辑预算。');
    for (const character of value) {
        const point = character.codePointAt(0)!;
        if ((point < 32 && point !== 9 && point !== 10 && point !== 13) ||
            point >= 0xD800 && point <= 0xDFFF || point === 0xFFFE || point === 0xFFFF) throw new Error('属性值包含 XML 非法字符。');
    }
}
function escapeAttribute(value: string, quote: '"' | "'"): string {
    return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/\r/g, '&#13;').replace(/\n/g, '&#10;').replace(/\t/g, '&#9;')
        .replace(quote === '"' ? /"/g : /'/g, quote === '"' ? '&quot;' : '&apos;');
}
/** Only an existing token may be replaced; structure edits use explicit schema ordering. */
export function createAttributePatch(document: XmlDocument, intent: AttributeEditIntent): TextPatch {
    if (!document.valid) throw new EditConflictError('XML 尚未完整，修复语法后再应用参数。');
    if (intent.documentVersion !== document.version) throw new EditConflictError();
    const element = document.find(intent.nodePath); const token = element?.attributeTokens[intent.attribute];
    if (!token || (intent.expectedValue !== undefined && token.value !== intent.expectedValue)) throw new EditConflictError();
    validateAttributeValue(intent.value);
    return { start: token.valueStart, end: token.valueEnd, text: intent.value === token.value ? token.rawValue : escapeAttribute(intent.value, token.quote), expected: token.rawValue };
}
/** Atomic preflight; callers add every returned range to one WorkspaceEdit. */
export function prepareTransaction(document: XmlDocument, intents: readonly AttributeEditIntent[]): TextPatch[] {
    if (!intents.length || intents.length > 512) throw new Error('编辑事务为空或超过预算。');
    const patches = intents.map(intent => createAttributePatch(document, intent));
    const ordered = [...patches].sort((a, b) => a.start - b.start);
    for (let i = 1; i < ordered.length; i++) if (ordered[i].start < ordered[i - 1].end || ordered[i].start === ordered[i - 1].start) throw new EditConflictError('事务包含重复或重叠字段。');
    return patches.filter(patch => patch.expected !== patch.text);
}
/** Schema-default insertion: callers must authorize the missing field from their IR. */
export function createAttributeInsertionPatch(document: XmlDocument, intent: AttributeEditIntent): TextPatch {
    if (!document.valid || intent.documentVersion !== document.version || intent.expectedValue !== undefined) throw new EditConflictError();
    const element = document.find(intent.nodePath);
    if (!element || Object.hasOwn(element.attributes, intent.attribute)) throw new EditConflictError();
    if (!/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(intent.attribute) || intent.attribute === 'xmlns') throw new Error('新增属性名称不合法。');
    validateAttributeValue(intent.value);
    const tokens = Object.values(element.attributeTokens).sort((a, b) => a.end - b.end);
    const last = tokens[tokens.length - 1];
    const offset = last?.end ?? element.start + 1 + element.name.length;
    const quote = last?.quote ?? '"';
    return { start: offset, end: offset, expected: '', text: ` ${intent.attribute}=${quote}${escapeAttribute(intent.value, quote)}${quote}` };
}
/**
 * Host-only field edits may materialize known XSD defaults. Missing fields require
 * expectedValue=undefined; unknown/default-looking fields must never be authorized
 * merely because a webview sends their names.
 */
export function prepareFieldTransaction(document: XmlDocument, intents: readonly AttributeEditIntent[]): TextPatch[] {
    if (!intents.length || intents.length > 512) throw new Error('编辑事务为空或超过预算。');
    const identities = new Set<string>(); const patches: TextPatch[] = [];
    for (const intent of intents) {
        const identity = `${intent.nodePath}\0${intent.attribute}`;
        if (identities.has(identity)) throw new EditConflictError('事务包含重复字段。');
        identities.add(identity);
        const node = document.find(intent.nodePath);
        const patch = node && Object.hasOwn(node.attributes, intent.attribute)
            ? createAttributePatch(document, intent) : createAttributeInsertionPatch(document, intent);
        if (patch.text === patch.expected) continue;
        const sameInsertion = patch.start === patch.end && patches.find(existing => existing.start === patch.start && existing.end === patch.end);
        if (sameInsertion) sameInsertion.text += patch.text;
        else patches.push(patch);
    }
    const ordered = [...patches].sort((a, b) => a.start - b.start);
    for (let i = 1; i < ordered.length; i++) if (ordered[i].start < ordered[i - 1].end) throw new EditConflictError('事务包含重叠字段。');
    return patches;
}

export interface DocumentTransaction { document: XmlDocument; documentVersion: number; intents: readonly AttributeEditIntent[] }
/** Validate every document before constructing any edits. No partial multi-file result. */
export function prepareMultiDocumentTransaction(transactions: readonly DocumentTransaction[]): Map<string, TextPatch[]> {
    const result = new Map<string, TextPatch[]>();
    for (const transaction of transactions) {
        if (result.has(transaction.document.uri) || transaction.document.version !== transaction.documentVersion) throw new EditConflictError();
        result.set(transaction.document.uri, prepareTransaction(transaction.document, transaction.intents));
    }
    return result;
}
export function applyTextPatches(text: string, patches: readonly TextPatch[]): string {
    const ordered = [...patches].sort((a, b) => b.start - a.start); let lastStart = text.length + 1;
    for (const patch of ordered) {
        if (patch.start < 0 || patch.end < patch.start || patch.end > text.length || patch.end > lastStart || text.slice(patch.start, patch.end) !== patch.expected) throw new EditConflictError();
        text = text.slice(0, patch.start) + patch.text + text.slice(patch.end); lastStart = patch.start;
    }
    return text;
}

export interface GestureState { id: string; documentUri: string; documentVersion: number; intents: readonly AttributeEditIntent[] }
/** Drafts have no document side effects. A gesture commits once, with the original version. */
export class EditGesture {
    private state?: GestureState;
    begin(id: string, document: XmlDocument): void { this.state = { id, documentUri: document.uri, documentVersion: document.version, intents: [] }; }
    update(id: string, intents: readonly Omit<AttributeEditIntent, 'documentVersion'>[]): void {
        if (!this.state || this.state.id !== id) throw new EditConflictError();
        this.state = { ...this.state, intents: intents.map(intent => ({ ...intent, documentVersion: this.state!.documentVersion })) };
    }
    commit(id: string, document: XmlDocument): TextPatch[] {
        const state = this.state; this.state = undefined;
        if (!state || state.id !== id || state.documentUri !== document.uri || state.documentVersion !== document.version) throw new EditConflictError();
        return prepareTransaction(document, state.intents);
    }
    cancel(): void { this.state = undefined; }
    get draft(): GestureState | undefined { return this.state; }
}

/** Insert a child at its XSD sequence position, preserving surrounding source. */
export function createChildInsertion(document: XmlDocument, parentPath: string, localName: string, xml: string, sequence: readonly string[], documentVersion: number): TextPatch {
    if (!document.valid || document.version !== documentVersion) throw new EditConflictError();
    const parent = document.find(parentPath); const ordinal = sequence.indexOf(localName);
    if (!parent || ordinal < 0) throw new Error('未声明该节点的 XSD 顺序。');
    // Validate a standalone wrapped fragment; namespace declarations are inherited explicitly.
    const inheritedDeclarations: Record<string, string> = {};
    let ancestor: XmlElement | undefined = parent;
    while (ancestor) {
        for (const [key, value] of Object.entries(ancestor.attributes)) if ((key === 'xmlns' || key.startsWith('xmlns:')) && !Object.hasOwn(inheritedDeclarations, key)) inheritedDeclarations[key] = value;
        ancestor = ancestor.parent;
    }
    const declarations = Object.entries(inheritedDeclarations).map(([key, value]) => ` ${key}="${escapeAttribute(value, '"')}"`).join('');
    const fragment = parseXml(`<fragment${declarations}>${xml}</fragment>`);
    if (!fragment.valid || fragment.roots[0].children.length !== 1 || fragment.roots[0].children[0].localName !== localName) throw new Error('新增节点 XML 无效。');
    const lineStart = document.text.lastIndexOf('\n', parent.start - 1) + 1;
    const parentIndent = /^[\t ]*/.exec(document.text.slice(lineStart, parent.start))![0];
    const first = parent.children[0];
    const firstLineStart = first ? document.text.lastIndexOf('\n', first.start - 1) + 1 : 0;
    const childIndent = first ? /^[\t ]*/.exec(document.text.slice(firstLineStart, first.start))![0] : parentIndent + (document.text.includes('\t') ? '\t' : '    ');
    const newline = document.text.includes('\r\n') ? '\r\n' : '\n';
    if (parent.selfClosing) {
        return { start: parent.openEnd - 2, end: parent.openEnd, expected: '/>', text: `>${newline}${childIndent}${xml}${newline}${parentIndent}</${parent.name}>` };
    }
    const next = parent.children.find(node => { const index = sequence.indexOf(node.localName); return index >= 0 && index > ordinal; });
    if (next) return { start: next.start, end: next.start, expected: '', text: `${xml}${newline}${childIndent}` };
    // Close tag already has its own indentation; add before that line if it is whitespace only.
    const closingLineStart = document.text.lastIndexOf('\n', parent.closeStart - 1) + 1;
    const emptyClosingPrefix = /^\s*$/.test(document.text.slice(closingLineStart, parent.closeStart));
    return emptyClosingPrefix
        ? { start: closingLineStart, end: closingLineStart, expected: '', text: `${childIndent}${xml}${newline}` }
        : { start: parent.closeStart, end: parent.closeStart, expected: '', text: `${newline}${childIndent}${xml}${newline}${parentIndent}` };
}

export function attributeToken(element: XmlElement, name: string): XmlAttribute | undefined { return element.attributeTokens[name]; }
