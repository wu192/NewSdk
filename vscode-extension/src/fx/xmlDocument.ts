/** FX expression semantics over the shared lossless XML source model. */
export * from '../ra3xml/core/xmlDocument';
export { toPlainXmlNode as toFxNode } from '../ra3xml/core/xmlDocument';

export type ExpressionResult = { status: 'evaluated'; value: number } | { status: 'unsupported' | 'invalid'; reason: string };
/** Small arithmetic parser. Unknown symbols/functions are visible, never coerced to zero. */
export function evaluateExpression(source: string, symbols: Readonly<Record<string, number>> = {}): ExpressionResult {
    if (source.length > 65536) return { status: 'unsupported', reason: '表达式超过求值预算。' };
    let text = source.trim(); if (text.startsWith('=')) text = text.slice(1).trim();
    let pos = 0; let operations = 0;
    class ExpressionError extends Error { constructor(message: string, readonly status: 'unsupported' | 'invalid') { super(message); } }
    const space = (): void => { while (/\s/.test(text[pos] ?? '') && pos < text.length) pos++; };
    const finite = (value: number): number => { if (!Number.isFinite(value)) throw new ExpressionError('结果不是有限数。', 'invalid'); return value; };
    const atom = (): number => {
        if (++operations > 256) throw new ExpressionError('表达式超过求值预算。', 'unsupported');
        space();
        if (text[pos] === '+' || text[pos] === '-') { const sign = text[pos++]; return (sign === '-' ? -1 : 1) * atom(); }
        if (text[pos] === '(') { pos++; const value = sum(); space(); if (text[pos++] !== ')') throw new ExpressionError('缺少右括号。', 'invalid'); return value; }
        const number = /^(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?/.exec(text.slice(pos));
        if (number) { pos += number[0].length; return finite(Number(number[0])); }
        const symbol = /^(?:\$[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*)/.exec(text.slice(pos));
        if (symbol) {
            pos += symbol[0].length;
            if (!Object.hasOwn(symbols, symbol[0])) throw new ExpressionError(`尚未求值：${symbol[0]}`, 'unsupported');
            return finite(symbols[symbol[0]]);
        }
        throw new ExpressionError('不支持此表达式语法。', 'unsupported');
    };
    const product = (): number => {
        let value = atom(); space();
        while (text[pos] === '*' || text[pos] === '/') { const operator = text[pos++]; const right = atom(); value = finite(operator === '*' ? value * right : value / right); space(); }
        return value;
    };
    const sum = (): number => {
        let value = product(); space();
        while (text[pos] === '+' || text[pos] === '-') { const operator = text[pos++]; const right = product(); value = finite(operator === '+' ? value + right : value - right); space(); }
        return value;
    };
    try { const value = sum(); space(); if (pos !== text.length) return { status: 'unsupported', reason: '尚未求值：不支持的表达式后缀。' }; return { status: 'evaluated', value: finite(value) }; }
    catch (error) { return error instanceof ExpressionError ? { status: error.status, reason: error.message } : { status: 'invalid', reason: '表达式无效。' }; }
}

export type ScalarKind = 'number' | 'time' | 'angle';
export type ScalarResult = { status: 'evaluated'; value: number; unit: string; lexical: string } | { status: 'unsupported' | 'invalid'; reason: string };
/** Time is seconds, angle is radians; bare numeric values retain their native unit. */
export function parseScalar(source: string, kind: ScalarKind = 'number', symbols: Readonly<Record<string, number>> = {}): ScalarResult {
    const lexical = source; const text = source.trim();
    let expression = text; let unit = '';
    // Recognize only lexical suffixes declared in Includes/Base.xsd. A known
    // arithmetic symbol ending in s/d/r is still a complete expression.
    const direct = evaluateExpression(text, symbols);
    if (direct.status === 'evaluated') return { ...direct, unit: '', lexical };
    const suffix = /^(.*?)(ms|s|d|r)$/i.exec(text);
    if (suffix && kind !== 'number') { expression = suffix[1].trim(); unit = suffix[2].toLowerCase(); }
    const value = evaluateExpression(expression, symbols);
    if (value.status !== 'evaluated') return value;
    let scale = 1;
    if (unit) {
        if (kind === 'time') {
            if (unit === 'ms') scale = 0.001;
            else if (unit !== 's') return { status: 'invalid', reason: 'Time 字段单位无效。' };
        } else if (kind === 'angle') {
            if (unit === 'd') scale = Math.PI / 180;
            else if (unit !== 'r') return { status: 'invalid', reason: 'Angle 字段单位无效。' };
        }
    }
    return { status: 'evaluated', value: value.value * scale, unit, lexical };
}
