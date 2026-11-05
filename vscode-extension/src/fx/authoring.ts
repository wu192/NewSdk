import { parseXml, XmlElement } from './xmlDocument';
import { applyTextPatches, prepareFieldTransaction, TextPatch } from './editTransaction';
import { FxValidationReport } from './skillIntent';

export interface AuthoringTarget {type: string; id: string;}
export interface AuthoringPlan {patches: TextPatch[]; text: string; changes: string[]; target?: AuthoringTarget; additions?:AuthoringTarget[];}
const assetNamespace = 'uri:ea.com:eala:asset';
const limit = 4 * 1024 * 1024;
function escapedText(value: string): string { return value.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/\r/g,'&#13;'); }
function binding(node:XmlElement,prefix:string):string|undefined {let current:XmlElement|undefined=node;while(current){const found=current.attributes[`xmlns:${prefix}`];if(found!==undefined)return found;current=current.parent;}return prefix==='xml'?'http://www.w3.org/XML/1998/namespace':undefined;}
function simplePath(node: XmlElement, root: XmlElement): string { return node.path.slice(root.path.length); }
/** AI XML is a proposal. Only fields of the uniquely selected asset become edits. */
export function prepareAuthoringPlan(original: string, proposed: string, version: number, mode: 'new' | 'edit', target?: AuthoringTarget): AuthoringPlan {
    if (Buffer.byteLength(original) > limit || Buffer.byteLength(proposed) > limit) throw new Error('XML 超过 4 MiB 创作预算。');
    const source = parseXml(original, '', version), incoming = parseXml(proposed);
    if (!source.valid || !incoming.valid) throw new Error('原 XML 和建议 XML 必须具有有效格式。');
    const root = source.roots[0], proposedRoot = incoming.roots[0];
    if (!root || root.localName !== 'AssetDeclaration' || root.namespaceUri !== assetNamespace || proposedRoot?.localName !== 'AssetDeclaration' || proposedRoot.namespaceUri !== assetNamespace)
        throw new Error('需要 uri:ea.com:eala:asset 命名空间的 AssetDeclaration。');
    const changes: string[] = []; let patches: TextPatch[] = [];let newIdentities:AuthoringTarget[]|undefined;
    if (mode === 'new') {
        const additions = proposedRoot.children;newIdentities=additions.filter(node=>node.attributes.id).map(node=>({type:node.localName,id:node.attributes.id}));
        const identities=new Set<string>();for(const node of additions){if(!node.attributes.id)continue;const key=`${node.localName}:${node.attributes.id}`;if(identities.has(key))throw new Error(`建议包含重复资产：${key}`);identities.add(key);}
        const preview=additions.find(node=>node.localName==='FXList')??additions.find(node=>node.localName==='FXParticleSystemTemplate');if(preview?.attributes.id)target={type:preview.localName,id:preview.attributes.id};
        if (!additions.length || additions.some(node => !['FXList','FXParticleSystemTemplate','Texture','Include','Define'].includes(node.localName)))
            throw new Error('新建结果只能包含特效、纹理、Include 或 Define 声明。');
        for (const node of additions) if (node.attributes.id && source.elements.some(existing => existing.parent === root && existing.localName === node.localName && existing.attributes.id === node.attributes.id))
            throw new Error(`资产身份重复：${node.localName}:${node.attributes.id}`);
        const newline = original.includes('\r\n') ? '\r\n' : '\n';
        const declarations = Object.entries(proposedRoot.attributes).filter(([key]) => key === 'xmlns' || key.startsWith('xmlns:'));
        const fragments = additions.map(node => {
            let text = proposed.slice(node.start, node.end);
            // Standalone declarations keep prefixes/default namespace from the proposal.
            const attrs = declarations.filter(([key]) => !Object.hasOwn(node.attributes,key)).map(([key,value]) => ` ${key}="${value.replace(/&/g,'&amp;').replace(/"/g,'&quot;')}"`).join('');
            text = text.slice(0,1+node.name.length) + attrs + text.slice(1+node.name.length);
            changes.push(`新增 ${node.localName}${node.attributes.id ? `:${node.attributes.id}` : ''}`); return text;
        });
        patches = root.selfClosing?[{start:root.openEnd-2,end:root.openEnd,expected:'/>' ,text:`>${newline}${fragments.join(newline)}${newline}</${root.name}>`}]:[{start:root.closeStart,end:root.closeStart,expected:'',text:`${newline}${fragments.join(newline)}${newline}`}];
    } else {
        if (!target) throw new Error('先唯一选择要修改的资产。');
        const identity=target;
        const oldMatches = source.elements.filter(node => node.parent === root && node.localName === identity.type && node.attributes.id === identity.id);
        const newMatches = incoming.elements.filter(node => node.parent === proposedRoot && node.localName === identity.type && node.attributes.id === identity.id);
        if (oldMatches.length !== 1 || newMatches.length !== 1) throw new Error('建议与当前 XML 必须唯一包含相同类型和 ID 的目标。');
        const oldRoot = oldMatches[0], newRoot = newMatches[0];
        const oldNodes = new Map(source.elements.filter(node => node === oldRoot || node.path.startsWith(`${oldRoot.path}/`)).map(node => [simplePath(node,oldRoot),node]));
        const intents: {nodePath:string;attribute:string;value:string;expectedValue?:string;documentVersion:number}[] = [];
        for (const node of incoming.elements.filter(node => node === newRoot || node.path.startsWith(`${newRoot.path}/`))) {
            const previous = oldNodes.get(simplePath(node,newRoot));
            if (!previous || previous.localName !== node.localName || previous.namespaceUri !== node.namespaceUri) throw new Error('此建议包含结构或家族变更；请在 XML 编辑器中核对后手动处理。');
            if(previous.attributes.Frame!==node.attributes.Frame&&previous.parent&&previous.parent.children.filter(child=>child.localName===previous.localName).length>1)throw new Error('关键帧身份或顺序发生变化；请在 XML 编辑器中手动确认。');
            if(previous.attributes.id!==node.attributes.id)throw new Error('子节点身份或顺序发生变化；请在 XML 编辑器中手动确认。');
            for (const [attribute,value] of Object.entries(node.attributes)) {
                if(attribute.includes(':')&&!attribute.startsWith('xmlns:')&&binding(previous,attribute.split(':')[0])!==binding(node,attribute.split(':')[0]))throw new Error('属性命名空间发生变化；请在 XML 编辑器中手动确认。');
                if (attribute === 'id' || attribute === 'xmlns' || attribute.startsWith('xmlns:')) continue;
                if (previous.attributes[attribute] !== value) {intents.push({nodePath:previous.path,attribute,value,expectedValue:previous.attributes[attribute],documentVersion:version});changes.push(`${target.type}:${target.id}${simplePath(previous,oldRoot)}/@${attribute}: ${previous.attributes[attribute] ?? '(未声明)'} → ${value}`);}
            }
            if (!node.children.length && node.textTokens.some(token => token.value.trim())) {
                const next = node.textTokens.map(token => token.value).join(''); const old = previous.textTokens.map(token => token.value).join('');
                if (next !== old) {
                    if (previous.textTokens.length !== 1 || previous.children.length || previous.textTokens[0].cdata) throw new Error('混合内容需要在 XML 编辑器中手动确认。');
                    const token = previous.textTokens[0]; patches.push({start:token.start,end:token.end,expected:original.slice(token.start,token.end),text:escapedText(next)});changes.push(`${target.type}:${target.id}${simplePath(previous,oldRoot)}/text(): ${old.trim()} → ${next.trim()}`);
                }
            }
        }
        if (intents.length) patches.push(...prepareFieldTransaction(source,intents));
    }
    const text = applyTextPatches(original,patches);
    if (!parseXml(text).valid) throw new Error('最小修改会产生无效 XML。');
    return {patches,text,changes,target,additions:newIdentities};
}
export function localAuthoringChecks(xml: string): FxValidationReport {
    const parsed = parseXml(xml); const validNamespace = parsed.roots[0]?.localName === 'AssetDeclaration' && parsed.roots[0]?.namespaceUri === assetNamespace;
    return {checkedAt:new Date().toISOString(),levels:[
        {level:'V0',status:'UNKNOWN',summary:'未运行 Skill 字段检查；类型、单位和范围尚未核对。',rules:[]},
        {level:'V1',status:parsed.valid && validNamespace?'PASS':'FAIL',summary:parsed.valid && validNamespace?'XML 格式和资产命名空间有效。':'XML 格式或命名空间无效。',rules:['共享 XML 解析器','AssetDeclaration 资产命名空间'],diagnostics:parsed.issues.map(issue=>({message:issue.message}))},
        {level:'V2',status:'SKIPPED',summary:'未提供指定 XSD。',rules:[]},
        {level:'V3',status:'UNKNOWN',summary:'尚未运行项目依赖检查；不能据此判断纹理、shader 或 Include 已存在。',rules:[]}
    ]};
}
export function parseAssistantResult(text: string): {xml: string; intent?: unknown} {
    if (text.length > limit) throw new Error('AI 响应超过创作预算。');
    try {const json = JSON.parse(text.replace(/^```json\s*/,'').replace(/\s*```$/,'')) as {xml?:unknown;intent?:unknown};if(typeof json.xml==='string')return {xml:json.xml,intent:json.intent};}catch { /* A fenced XML response is also a supported manual result. */ }
    const xml = /```xml\s*([\s\S]*?)```/i.exec(text)?.[1] ?? text.trim();
    return {xml};
}
