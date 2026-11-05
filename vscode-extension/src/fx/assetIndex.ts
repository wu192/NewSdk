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
}
export interface IncludeEdge { from: string; to: string; mode: IncludeMode; source: SourceLocation; }
export interface AssetGraph {
    entry: string; generation: number; assets: Map<string, AssetRecord>;
    documents: Map<string, XmlDocument>; issues: FxIssue[]; dependencies: string[];
    includes: IncludeEdge[];
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
    isReadonly(uri: string): boolean {
        const absolute = filePath(uri); const current = this.physicalPaths.get(absolute) ?? absolute;
        const sdk = this.options.sdkRoot ? this.physicalPaths.get(filePath(this.options.sdkRoot)) ?? filePath(this.options.sdkRoot) : undefined;
        const mod = this.options.modRoot ? this.physicalPaths.get(filePath(this.options.modRoot)) ?? filePath(this.options.modRoot) : undefined;
        return !!sdk && contains(sdk, current) && !(mod && contains(mod, current));
    }
    /** Lexical and physical checks both run before any external file reader. */
    async checkedFile(uri: string): Promise<string> {
        const candidate = filePath(uri);
        const roots = await Promise.all(this.options.roots.map(async root => {
            const absolute = filePath(root); let physicalRoot: string; try { physicalRoot = await fs.realpath(absolute); } catch { physicalRoot = absolute; }
            this.physicalPaths.set(absolute, physicalRoot); return physicalRoot;
        }));
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
        if (/^(?:https?|ftp|data):/i.test(value) || value.includes('\0')) throw new Error('FX 仅允许 MOD/SDK 内本地资源');
        const prefixed = /^([a-zA-Z]+):(.*)$/.exec(value);
        const candidates: string[] = [];
        if (prefixed && !/^[a-zA-Z]:\//.test(value)) {
            const suffix = prefixed[2].replace(/^\/+/, '');
            const prefix = prefixed[1].toUpperCase();
            if (prefix === 'DATA') {
                if (this.options.modRoot) candidates.push(path.join(this.options.modRoot, 'Data', suffix));
                if (this.options.sdkRoot) candidates.push(path.join(this.options.sdkRoot, 'SageXml', suffix), path.join(this.options.sdkRoot, suffix));
            } else if (prefix === 'ART' || prefix === 'AUDIO') {
                const folder = prefix === 'ART' ? 'Art' : 'Audio';
                if (this.options.modRoot) candidates.push(path.join(this.options.modRoot, folder, suffix));
                if (this.options.sdkRoot) candidates.push(path.join(this.options.sdkRoot, folder, suffix));
            } else throw new Error(`尚不支持资源前缀 ${prefix}:`);
            if (!candidates.length) throw new Error(`资源前缀 ${prefix}: 需要配置 MOD/SDK 根`);
        } else candidates.push(path.isAbsolute(value) ? value : path.resolve(path.dirname(filePath(fromUri)), value));
        for (const candidate of candidates) {
            const checked = await this.checkedFile(candidate);
            try { await fs.access(checked); return checked; }
            catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
        }
        return this.checkedFile(candidates[0]);
    }
    /** Rebuild only this include closure. Dirty TextDocument snapshots take precedence. */
    async load(entryUri: string, signal?: AbortSignal): Promise<AssetGraph> {
        if (this.disposed) throw new Error('AssetIndex disposed');
        const entry = await this.checkedFile(entryUri); stopped(signal);
        const request = ++this.epoch;
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
                const isXmlInclude = node.localName === 'include' && (node.namespaceUri === XML_INCLUDE_NAMESPACE || node.name === 'xi:include');
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
        this.active = graph; this.graphs.clear(); this.graphs.set(entry, graph); this.invalidated.delete(entry);
        this.rebuildReverseAssets();
        return graph;
    }
    /** Resolve a typed id within the active entry, never across Base/Global/APT graphs. */
    candidates(type: string, reference: string, namespace?: string): AssetRecord[] {
        const [prefix, id] = reference.includes(':') ? [reference.slice(0, reference.indexOf(':')), reference.slice(reference.indexOf(':') + 1)] : ['', reference];
        const expected = prefix === 'FXParticleSystem' ? 'FXParticleSystemTemplate' : prefix || type;
        if (expected !== type) return [];
        return [...this.active.assets.values()].filter(a => a.type === type && a.id === id && (namespace === undefined || a.namespace === namespace));
    }
    private rebuildReverseAssets(): void {
        this.reverseAssets.clear();
        for (const asset of this.active.assets.values()) {
            const refs: [string, string][] = [];
            if (asset.node.attributes.inheritFrom) refs.push([asset.type, asset.node.attributes.inheritFrom]);
            if (asset.type === 'FXParticleSystemTemplate' && asset.node.attributes.ParticleTexture) refs.push(['Texture', asset.node.attributes.ParticleTexture]);
            const walk = (node: XmlElement): void => {
                if (node.localName === 'ParticleSystem' && node.attributes.Particle) refs.push(['FXParticleSystemTemplate', node.attributes.Particle]);
                for (const child of node.children) walk(child);
            }; walk(asset.node);
            for (const [type, ref] of refs) for (const target of this.candidates(type, ref, asset.namespace)) {
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
        const absolute = filePath(uri); const file = this.physicalPaths.get(absolute) ?? absolute; this.versions.set(file, (this.versions.get(file) ?? 0) + 1);
        const affected = new Set<string>();
        for (const [entry, graph] of this.graphs) if (graph.dependencies.includes(file) || graph.includes.some(edge => edge.to === file)) {
            this.invalidated.add(entry); affected.add(entry);
        }
        ++this.epoch;
        return affected;
    }
    dispose(): void { this.disposed = true; ++this.epoch; this.graphs.clear(); this.reverseAssets.clear(); this.versions.clear(); this.invalidated.clear(); this.physicalPaths.clear();
        this.active = {entry: '', generation: this.epoch, assets: new Map(), documents: new Map(), issues: [], dependencies: [], includes: []}; }
}
