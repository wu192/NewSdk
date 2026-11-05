/** Lossless source model: original XML stays authoritative; ranges use UTF-16 offsets. */
export interface XmlAttribute {
    name: string; value: string; rawValue: string;
    start: number; end: number; valueStart: number; valueEnd: number; quote: '"' | "'";
}
export interface XmlTextToken { start: number; end: number; value: string; cdata?: boolean }
export interface XmlElement {
    name: string; localName: string; namespaceUri: string;
    attributes: Record<string, string>; attributeTokens: Record<string, XmlAttribute>;
    children: XmlElement[]; textTokens: XmlTextToken[]; parent?: XmlElement;
    /** Same-qualified-name sibling indexes make paths independent of whitespace/comments. */
    path: string; start: number; end: number; openEnd: number; closeStart: number; selfClosing: boolean;
}
export interface XmlIssue { message: string; start: number; end: number; code: string }
export interface XmlDocument {
    shallow?:boolean; sourceBytes?:number;
    uri: string; version: number; text: string; roots: XmlElement[]; elements: XmlElement[]; issues: XmlIssue[]; valid: boolean;
    find(path: string): XmlElement | undefined;
    elementAt(offset: number): XmlElement | undefined;
}
export interface PlainXmlNode { name: string; attributes: Record<string, string>; children: PlainXmlNode[]; text?: string }

const namePattern = /^[A-Za-z_\u0080-\uFFFF][A-Za-z0-9_.:\-\u0080-\uFFFF]*/;
const xmlNamespace = 'http://www.w3.org/XML/1998/namespace';
const xmlnsNamespace = 'http://www.w3.org/2000/xmlns/';
const predefined: Record<string, string> = { amp: '&', lt: '<', gt: '>', apos: "'", quot: '"' };
function xmlCharacter(n: number): boolean {
    return n === 9 || n === 10 || n === 13 || (n >= 32 && n <= 0xD7FF) || (n >= 0xE000 && n <= 0xFFFD) || (n >= 0x10000 && n <= 0x10FFFF);
}
function decodeEntities(raw: string, offset: number, issues: XmlIssue[], attribute = false): string {
    let cursor = 0; let result = '';
    while (cursor < raw.length) {
        const amp = raw.indexOf('&', cursor);
        if (amp === -1) { result += raw.slice(cursor); break; }
        result += raw.slice(cursor, amp);
        const semi = raw.indexOf(';', amp + 1);
        if (semi === -1) {
            issues.push({ message: '实体引用缺少分号。', start: offset + amp, end: offset + amp + 1, code: 'xml.entity' });
            result += raw.slice(amp); break;
        }
        const name = raw.slice(amp + 1, semi);
        let value = predefined[name];
        if (/^#(?:[0-9]+|x[0-9a-fA-F]+)$/.test(name)) {
            const n = name[1] === 'x' ? Number.parseInt(name.slice(2), 16) : Number.parseInt(name.slice(1), 10);
            if (xmlCharacter(n)) value = String.fromCodePoint(n);
        }
        if (value === undefined) {
            issues.push({ message: `不展开未声明或不受支持的实体 &${name};。`, start: offset + amp, end: offset + semi + 1, code: 'xml.entity' });
            value = raw.slice(amp, semi + 1);
        }
        result += value; cursor = semi + 1;
    }
    // XML attributes normalize literal whitespace, but character references retain it.
    // Keeping lexical whitespace here is intentional: editors display the exact value;
    // consumers needing XSD whitespace facets apply those facets separately.
    if (attribute && raw.includes('<')) issues.push({ message: '属性值不能包含未转义的 <。', start: offset, end: offset + raw.length, code: 'xml.attribute' });
    return result;
}

/** No network, DTD, custom entities, or executable expressions are evaluated. */
export function parseXml(text: string, uri = '', version = 0): XmlDocument {
    const roots: XmlElement[] = []; const elements: XmlElement[] = []; const issues: XmlIssue[] = [];
    const stack: { element: XmlElement; namespaces: Record<string, string>; counts: Record<string, number> }[] = [];
    const rootCounts: Record<string, number> = Object.create(null) as Record<string, number>;
    let pos = text.charCodeAt(0) === 0xFEFF ? 1 : 0;
    const issue = (message: string, start: number, end = start + 1, code = 'xml.syntax'): void => { issues.push({ message, start, end: Math.min(text.length, end), code }); };
    const consumeSpace = (): void => { while (/[\x20\t\r\n]/.test(text[pos] ?? '') && pos < text.length) pos++; };
    while (pos < text.length) {
        if (text[pos] !== '<') {
            const start = pos; const next = text.indexOf('<', pos); pos = next === -1 ? text.length : next;
            const raw = text.slice(start, pos);
            if (raw.includes(']]>')) issue('普通文本不能包含 ]]>。', start, pos);
            const value = decodeEntities(raw, start, issues);
            if (stack.length) stack[stack.length - 1].element.textTokens.push({ start, end: pos, value });
            else if (raw.trim()) issue('根元素之外不能包含文本。', start, pos);
            continue;
        }
        if (text.startsWith('<!--', pos)) {
            const start = pos; const end = text.indexOf('-->', pos + 4);
            if (end < 0) { issue('注释尚未闭合。', start, text.length); break; }
            if (text.slice(pos + 4, end).includes('--')) issue('XML 注释内部不能包含 --。', start, end + 3);
            pos = end + 3; continue;
        }
        if (text.startsWith('<![CDATA[', pos)) {
            const start = pos; const end = text.indexOf(']]>', pos + 9);
            if (end < 0) { issue('CDATA 尚未闭合。', start, text.length); break; }
            if (!stack.length) issue('CDATA 必须位于根元素内。', start, end + 3);
            else stack[stack.length - 1].element.textTokens.push({ start: pos + 9, end, value: text.slice(pos + 9, end), cdata: true });
            pos = end + 3; continue;
        }
        if (text.startsWith('<?', pos)) {
            const end = text.indexOf('?>', pos + 2);
            if (end < 0) { issue('处理指令尚未闭合。', pos, text.length); break; }
            const target = namePattern.exec(text.slice(pos + 2))?.[0];
            if (!target) issue('处理指令缺少目标名称。', pos, end + 2);
            else if (target.toLowerCase() === 'xml') {
                const declaration = text.slice(pos, end + 2);
                if (target !== 'xml' || pos !== (text.charCodeAt(0) === 0xFEFF ? 1 : 0)) issue('XML 声明必须位于文档开头。', pos, end + 2);
                if (!/^<\?xml\s+version\s*=\s*(["'])1\.0\1(?:\s+encoding\s*=\s*(["'])[A-Za-z][A-Za-z0-9._-]*\2)?(?:\s+standalone\s*=\s*(["'])(?:yes|no)\3)?\s*\?>$/.test(declaration)) issue('XML 声明无效；预览支持 XML 1.0。', pos, end + 2);
            }
            pos = end + 2; continue;
        }
        if (text.startsWith('<!', pos)) { issue('出于安全原因不支持 DTD 或实体声明。', pos, text.length, 'xml.dtd'); break; }
        if (text.startsWith('</', pos)) {
            const start = pos; pos += 2;
            const match = namePattern.exec(text.slice(pos));
            if (!match) { issue('结束标签缺少名称。', start, pos + 1); break; }
            const name = match[0]; pos += name.length; consumeSpace();
            if (text[pos] !== '>') { issue('结束标签格式不正确。', start, pos + 1); break; }
            pos++;
            const entry = stack.pop();
            if (!entry) issue(`没有与 </${name}> 对应的开始标签。`, start, pos);
            else { entry.element.closeStart = start; entry.element.end = pos;
                if (entry.element.name !== name) issue(`预期 </${entry.element.name}>，实际为 </${name}>。`, start, pos); }
            continue;
        }
        const start = pos++;
        const match = namePattern.exec(text.slice(pos));
        if (!match) { issue('开始标签缺少名称。', start, pos + 1); break; }
        const name = match[0]; pos += name.length;
        const attributes: Record<string, string> = Object.create(null) as Record<string, string>;
        const attributeTokens: Record<string, XmlAttribute> = Object.create(null) as Record<string, XmlAttribute>;
        let selfClosing = false; let complete = false;
        while (pos < text.length) {
            const beforeSpace = pos; consumeSpace();
            if (text[pos] === '>') { pos++; complete = true; break; }
            if (text.startsWith('/>', pos)) { pos += 2; selfClosing = true; complete = true; break; }
            if (beforeSpace === pos) issue('属性之间必须有空白。', pos, pos + 1);
            const attributeStart = pos; const attrMatch = namePattern.exec(text.slice(pos));
            if (!attrMatch) { issue('属性名称或开始标签不完整。', attributeStart, pos + 1); break; }
            const attrName = attrMatch[0]; pos += attrName.length; consumeSpace();
            if (text[pos] !== '=') { issue(`属性 ${attrName} 缺少等号。`, attributeStart, pos + 1); break; }
            pos++; consumeSpace(); const quote = text[pos];
            if (quote !== '"' && quote !== "'") { issue(`属性 ${attrName} 需要引号。`, attributeStart, pos + 1); break; }
            const valueStart = ++pos; const valueEnd = text.indexOf(quote, pos);
            if (valueEnd < 0) { issue(`属性 ${attrName} 的引号尚未闭合。`, valueStart - 1, text.length); pos = text.length; break; }
            const rawValue = text.slice(valueStart, valueEnd); const value = decodeEntities(rawValue, valueStart, issues, true);
            pos = valueEnd + 1;
            if (Object.hasOwn(attributes, attrName)) issue(`重复属性 ${attrName}。`, attributeStart, pos);
            attributes[attrName] = value;
            attributeTokens[attrName] = { name: attrName, value, rawValue, start: attributeStart, end: pos, valueStart, valueEnd, quote };
        }
        if (!complete) { issue(`标签 <${name}> 尚未闭合。`, start, pos); break; }
        const parent = stack[stack.length - 1];
        const namespaces = Object.assign(Object.create(null) as Record<string, string>, parent?.namespaces ?? { xml: xmlNamespace });
        for (const [key, value] of Object.entries(attributes)) {
            if (key === 'xmlns') namespaces[''] = value;
            else if (key.startsWith('xmlns:')) namespaces[key.slice(6)] = value;
            if ((key === 'xmlns' || key.startsWith('xmlns:')) &&
                (value === xmlnsNamespace || value === xmlNamespace && key !== 'xmlns:xml' || key.startsWith('xmlns:') && !value)) {
                issue('保留命名空间绑定或空前缀绑定无效。', attributeTokens[key].start, attributeTokens[key].end);
            }
        }
        const qname = name.split(':');
        if (qname.length > 2 || qname.some(x => !x)) issue(`限定名称 ${name} 不合法。`, start, start + name.length + 1);
        const prefix = qname.length === 2 ? qname[0] : '';
        if (prefix && !namespaces[prefix]) issue(`未声明命名空间前缀 ${prefix}。`, start, start + name.length + 1);
        const expandedAttributes = new Set<string>();
        for (const [key, token] of Object.entries(attributeTokens)) {
            if (key === 'xmlns' || key.startsWith('xmlns:')) continue;
            const segments = key.split(':'); const attrPrefix = segments.length === 2 ? segments[0] : '';
            if (segments.length > 2 || segments.some(x => !x)) issue(`限定属性名称 ${key} 不合法。`, token.start, token.end);
            if (attrPrefix && !namespaces[attrPrefix]) issue(`未声明命名空间前缀 ${attrPrefix}。`, token.start, token.end);
            const expanded = `${attrPrefix ? namespaces[attrPrefix] : ''}\0${segments[segments.length - 1]}`;
            if (expandedAttributes.has(expanded)) issue(`重复的展开属性名称 ${key}。`, token.start, token.end);
            expandedAttributes.add(expanded);
        }
        if (namespaces.xml !== xmlNamespace || namespaces.xmlns || namespaces[''] === xmlnsNamespace) issue('保留命名空间绑定无效。', start, pos);
        const counts = parent?.counts ?? rootCounts; const ordinal = (counts[name] ?? 0) + 1; counts[name] = ordinal;
        const element: XmlElement = {
            name, localName: qname[qname.length - 1], namespaceUri: namespaces[prefix] ?? '', attributes, attributeTokens,
            children: [], textTokens: [], parent: parent?.element, path: `${parent?.element.path ?? ''}/${name}[${ordinal}]`,
            start, openEnd: pos, closeStart: selfClosing ? pos - 2 : text.length, end: selfClosing ? pos : text.length, selfClosing,
        };
        elements.push(element); if (parent) parent.element.children.push(element); else roots.push(element);
        if (!selfClosing) stack.push({ element, namespaces, counts: Object.create(null) as Record<string, number> });
        if (stack.length > 256) { issue('XML 嵌套超过 256 层预览预算。', start, pos, 'xml.budget'); break; }
    }
    for (const entry of stack) issue(`标签 <${entry.element.name}> 尚未闭合。`, entry.element.start, entry.element.openEnd);
    if (roots.length !== 1) issue(roots.length === 0 ? '文档缺少根元素。' : 'XML 文档只能有一个根元素。', 0, Math.min(text.length, 1));
    for (let i = 0; i < text.length; i++) {
        const point = text.codePointAt(i)!;
        if (!xmlCharacter(point)) issue('XML 包含非法字符。', i, i + 1);
        if (point > 0xFFFF) i++;
    }
    const byPath = new Map(elements.map(element => [element.path, element]));
    return { uri, version, text, roots, elements, issues, valid: issues.length === 0,
        find: nodePath => byPath.get(nodePath),
        elementAt: offset => {
            let found: XmlElement | undefined;
            for (const element of elements) if (element.start <= offset && offset < element.end && (!found || element.start >= found.start)) found = element;
            return found;
        } };
}

export function toPlainXmlNode(element: XmlElement): PlainXmlNode {
    return { name: element.localName, attributes: { ...element.attributes }, children: element.children.map(toPlainXmlNode),
        ...(element.textTokens.length ? { text: element.textTokens.map(token => token.value).join('') } : {}) };
}
export function child(element: XmlElement, localName: string): XmlElement | undefined { return element.children.find(node => node.localName === localName); }
export function children(element: XmlElement, localName: string): XmlElement[] { return element.children.filter(node => node.localName === localName); }
export function textContent(element: XmlElement): string {
    const tokens: { start: number; value: string }[] = [...element.textTokens, ...element.children.map(node => ({ start: node.start, value: textContent(node) }))];
    return tokens.sort((a, b) => a.start - b.start).map(token => token.value).join('');
}

