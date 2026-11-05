import { createAttributePatch, TextPatch } from './editTransaction';
import { XmlDocument, XmlElement } from './xmlDocument';
import { escapeXml } from './templates';

export interface CopyDependency { type: 'Texture' | 'FXParticleSystemTemplate'; id: string; namespace: string; }
export interface AssetCopyOptions {
    source: XmlDocument; sourcePath: string; target: XmlDocument; reservedIds?: string[];
    visibleAssets: { type: string; id: string; namespace: string }[];
    reference?: { nodePath: string; attribute: string; expectedValue: string };
}
export interface AssetCopyPlan {
    assetId: string; copied: {sourceId: string; id: string}[];
    patches: TextPatch[]; dependencies: CopyDependency[]; preview: string;
}

function addNamespaces(fragment: string, node: XmlElement): string {
    const bindings = new Map<string, string>();
    for (let parent: XmlElement | undefined = node; parent; parent = parent.parent) {
        for (const [name, value] of Object.entries(parent.attributes)) if ((name === 'xmlns' || name.startsWith('xmlns:')) && !bindings.has(name)) bindings.set(name, value);
    }
    const attributes = [...bindings].filter(([name]) => node.attributes[name] === undefined);
    const boundary = /[\s/>]/.exec(fragment.slice(1));
    if (!attributes.length || !boundary) return fragment;
    const at = boundary.index + 1;
    return fragment.slice(0, at) + attributes.map(([name, value]) => ` ${name}="${escapeXml(value)}"`).join('') + fragment.slice(at);
}
function replaceTokens(source: XmlDocument, node: XmlElement, replacements: [string, string][]): string {
    let result = source.text.slice(node.start, node.end);
    const edits = replacements.map(([name, value]) => {
        const token = node.attributeTokens[name]; if (!token) throw new Error(`复制来源缺少 ${name} token。`);
        return { start: token.valueStart - node.start, end: token.valueEnd - node.start, text: escapeXml(value) };
    }).sort((a, b) => b.start - a.start);
    for (const edit of edits) result = result.slice(0, edit.start) + edit.text + result.slice(edit.end);
    return addNamespaces(result, node);
}

/** Preserve each original subtree; only ids, local inheritance and the chosen call change. */
export function planAssetCopy(options: AssetCopyOptions): AssetCopyPlan {
    const { source, target } = options;
    if (!source.valid || !target.valid) throw new Error('请修复来源和目标 XML 后再复制。');
    const selected = source.find(options.sourcePath);
    const root = target.roots.find(node => node.localName === 'AssetDeclaration');
    if (!selected || !root || !['FXParticleSystemTemplate', 'FXList'].includes(selected.localName) || !selected.attributes.id) throw new Error('复制需要明确的 FX 模板或组合，以及 AssetDeclaration 目标。');
    if (root.namespaceUri !== selected.namespaceUri) throw new Error('来源与目标资产命名空间不同，不能自动复制。');
    const chain: XmlElement[] = []; const visiting = new Set<string>();
    const collect = (node: XmlElement): void => {
        const id = node.attributes.id;
        if (visiting.has(id)) throw new Error(`继承循环：${[...visiting, id].join(' → ')}`);
        if (chain.length + visiting.size >= 128) throw new Error('继承链超过复制预算（128）。');
        visiting.add(id);
        const parentId = node.attributes.inheritFrom;
        if (parentId) {
            const parents = source.elements.filter(candidate => candidate.localName === selected.localName && candidate.namespaceUri === selected.namespaceUri && candidate.attributes.id === parentId);
            if (parents.length !== 1) throw new Error(`父定义 ${parentId} 必须在同一来源文档中唯一存在；不能留下跨文件继承。`);
            collect(parents[0]);
        }
        visiting.delete(id); chain.push(node);
    };
    collect(selected);
    const dependencies = new Map<string, CopyDependency>();
    const moving = source.uri !== target.uri;
    const inspect = (node: XmlElement): void => {
        if (moving) for (const value of Object.values(node.attributes)) if (value.trim().startsWith('=')) throw new Error('跨文件复制含表达式的定义前，需要先明确目标文档的宏范围；请使用 XML。');
        for (const name of ['SlaveSystem', 'PerParticleAttachedSystem', 'Drawable', 'EmitterSound']) if (node.attributes[name]) throw new Error(`${name} 的附属资源复制尚未实现，请先在 XML 中明确依赖。`);
        if (node.localName === 'Event' && node.children.length) throw new Error('带 Event 子系统的依赖复制尚未实现，请使用 XML。');
        if (selected.localName === 'FXList' && node.localName === 'NuggetList' && node.children.some(child => child.localName !== 'ParticleSystem')) throw new Error('此组合含普通 ParticleSystem 之外的依赖，暂不能自动复制。');
        for (const name of ['ParticleTexture', 'DetailTexture', 'EnvironmentTexture']) if (node.attributes[name]) {
            const dependency: CopyDependency = { type: 'Texture', id: node.attributes[name], namespace: selected.namespaceUri };
            dependencies.set(`${dependency.type}:${dependency.id}`, dependency);
        }
        if (node.localName === 'ParticleSystem' && node.attributes.Particle) {
            const dependency: CopyDependency = { type: 'FXParticleSystemTemplate', id: node.attributes.Particle, namespace: selected.namespaceUri };
            dependencies.set(`${dependency.type}:${dependency.id}`, dependency);
        }
        for (const child of node.children) inspect(child);
    };
    chain.forEach(inspect);
    for (const dependency of dependencies.values()) {
        const visible = options.visibleAssets.filter(asset => asset.type === dependency.type && asset.id === dependency.id && asset.namespace === dependency.namespace);
        if (visible.length !== 1) throw new Error(`目标入口需要唯一可见的 ${dependency.type}:${dependency.id}；请先 Include 相应声明，贴图文件不会被复制。`);
    }
    const reserved = new Set([...(options.reservedIds ?? []), ...target.elements.map(node => node.attributes.id).filter(Boolean)].map(id => id.toLowerCase()));
    const ids = new Map<string, string>();
    for (const node of chain) {
        const stem = `${node.attributes.id}_Copy`; let id = stem;
        for (let n = 2; reserved.has(id.toLowerCase()); n++) id = `${stem}${n}`;
        reserved.add(id.toLowerCase()); ids.set(node.attributes.id, id);
    }
    const fragments = chain.map(node => {
        const replacements: [string, string][] = [['id', ids.get(node.attributes.id)!]];
        if (node.attributes.inheritFrom) replacements.push(['inheritFrom', ids.get(node.attributes.inheritFrom)!]);
        return replaceTokens(source, node, replacements);
    });
    const insertion = `\n${fragments.join('\n')}\n`;
    const patches: TextPatch[] = [];
    if (root.selfClosing) patches.push({ start: root.openEnd - 2, end: root.openEnd, expected: '/>', text: `>${insertion}</${root.name}>` });
    else patches.push({ start: root.closeStart, end: root.closeStart, expected: '', text: insertion });
    const assetId = ids.get(selected.attributes.id)!;
    if (options.reference) {
        if (options.reference.attribute !== 'Particle' || options.reference.expectedValue !== selected.attributes.id) throw new Error('复制只允许替换已确认的当前 Particle 调用。');
        patches.push(createAttributePatch(target, { ...options.reference, value: assetId, documentVersion: target.version }));
    }
    return { assetId, copied: chain.map(node => ({ sourceId: node.attributes.id, id: ids.get(node.attributes.id)! })), patches,
        dependencies: [...dependencies.values()], preview: `${options.reference ? `${options.reference.expectedValue} → ${assetId}\n` : ''}${fragments.join('\n')}` };
}
