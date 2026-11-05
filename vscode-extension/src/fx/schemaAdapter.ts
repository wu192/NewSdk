import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import {createHash, randomUUID} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {canonicalPath} from '../project';
import {parseXml} from '../ra3xml/core/xmlDocument';
import {applyTextPatches, createAttributePatch, TextPatch} from '../ra3xml/core/editTransaction';

const XSD = 'http://www.w3.org/2001/XMLSchema';
const adapterVersion = 1;
const qnames = new Set(['type', 'base', 'ref', 'itemType', 'substitutionGroup']);
const dependencies = new Set(['include', 'import', 'redefine']);
const digest = (value: string | Buffer): string => createHash('sha256').update(value).digest('hex');

export interface SchemaAdapterLimits {maxFiles?: number; maxBytes?: number;}
export interface PreparedSchema {
    /** Local derived entry accepted by the imported Skill's restricted resolver. */
    entryPath: string;
    /** Actual selected SDK/user source, shown in reports instead of a cache path. */
    sourcePath: string;
    version: string;
    files: number;
    adjustments: {schemaLocations: number; qnames: number};
    normalized: boolean;
}
interface SourceFile {file: string; relative: string; original: Buffer; text: string;}
interface CacheManifest {adapterVersion: number; version: string; files: {path: string; hash: string}[];}

function inside(root: string, candidate: string): boolean {
    const relative = path.relative(root, candidate);
    return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}
function relativeUri(from: string, to: string): string {
    return path.relative(path.dirname(from), to).split(path.sep).map(component => encodeURIComponent(component)).join('/');
}
function dependencyPath(from: string, value: string): string {
    const normalized = value.trim().replace(/\\/g, '/');
    if (!normalized || normalized.includes('\0') || normalized.includes('?') || normalized.includes('#')) throw new Error('XSD 依赖路径无效。');
    if (/^[A-Za-z]:\//.test(normalized)) return path.resolve(normalized);
    if (/^[A-Za-z][A-Za-z0-9+.-]*:/.test(normalized)) {
        const url = new URL(normalized);
        if (url.protocol !== 'file:' || url.host && url.host !== 'localhost') throw new Error('XSD 校验不下载远程依赖。');
        return fileURLToPath(url);
    }
    let decoded: string;
    try {decoded = decodeURIComponent(normalized);} catch {throw new Error('XSD 依赖路径编码无效。');}
    return path.resolve(path.dirname(from), decoded);
}

/**
 * A derived XSD compatibility snapshot, not another asset index. All field rules,
 * constraints, annotations and comments come from the selected local schemas.
 * Only dependency URI spelling and QName whitespace are normalized, using the
 * shared lossless XML parser/patcher. Source SDK files are never written.
 */
export class SchemaAdapter {
    private readonly maxFiles: number;
    private readonly maxBytes: number;
    private pending: Promise<unknown> = Promise.resolve();
    constructor(readonly storageDirectory: string, limits: SchemaAdapterLimits = {}) {
        this.maxFiles = limits.maxFiles ?? 4096;
        this.maxBytes = limits.maxBytes ?? 32 * 1024 * 1024;
    }
    prepare(entryPath: string): Promise<PreparedSchema> {
        const result = this.pending.then(() => this.prepareSnapshot(entryPath));
        this.pending = result.catch(() => undefined);
        return result;
    }
    private async prepareSnapshot(entryPath: string): Promise<PreparedSchema> {
        const source = await this.canonicalAbsolute(entryPath);
        const root = path.dirname(source);
        // The Skill validates this named wrapper against its original provenance
        // manifest. Rewriting it would invalidate hashes and weaken that contract.
        if (path.basename(source) === 'public-fx-types-wrapper.xsd') {
            const bytes = await fs.readFile(source);
            if (bytes.length > this.maxBytes) throw new Error('XSD 超过大小预算。');
            return {entryPath: source, sourcePath: source, version: digest(bytes), files: 1,
                adjustments: {schemaLocations: 0, qnames: 0}, normalized: false};
        }
        const names = new Map<string, string[]>();
        const canonical = async (candidate: string): Promise<string> => {
            const absolute = path.resolve(candidate);
            if (!inside(root, absolute)) throw new Error('XSD 依赖越出选定入口目录。');
            let current = root;
            for (const component of path.relative(root, absolute).split(path.sep).filter(Boolean)) {
                let entries = names.get(current);
                if (!entries) {entries = await fs.readdir(current); names.set(current, entries);}
                const matches = entries.filter(name => name.toLowerCase() === component.toLowerCase());
                if (matches.length !== 1) throw new Error(`XSD 依赖缺失或存在大小写歧义：${path.relative(root, absolute)}`);
                current = path.join(current, matches[0]);
            }
            const physical = await fs.realpath(current);
            if (!inside(root, physical)) throw new Error('XSD 符号链接越出选定入口目录。');
            if (!/\.xsd$/i.test(physical)) throw new Error('XSD 依赖必须是本地 .xsd 文件。');
            return physical;
        };
        const files = new Map<string, SourceFile>();
        const edges = new Map<string, Map<number, string>>();
        let totalBytes = 0;
        const adjustments = {schemaLocations: 0, qnames: 0};
        const visit = async (input: string): Promise<void> => {
            const file = await canonical(input);
            if (files.has(file)) return;
            if (files.size >= this.maxFiles) throw new Error('XSD 依赖超过文件数量预算。');
            const info = await fs.stat(file);
            if (!info.isFile() || info.size > this.maxBytes - totalBytes) throw new Error('XSD 依赖超过大小预算。');
            const original = await fs.readFile(file);
            totalBytes += original.length;
            if (totalBytes > this.maxBytes) throw new Error('XSD 依赖超过大小预算。');
            const text = original.toString('utf8');
            if (!Buffer.from(text, 'utf8').equals(original)) throw new Error('XSD 需要有效的 UTF-8 编码。');
            const document = parseXml(text, file);
            if (!document.valid || document.roots[0]?.namespaceUri !== XSD || document.roots[0]?.localName !== 'schema') throw new Error(`XSD XML 格式无效：${path.relative(root, file)}`);
            const record: SourceFile = {file, relative: path.relative(root, file), original, text};
            files.set(file, record);
            const resolved = new Map<number, string>(); edges.set(file, resolved);
            for (const node of document.elements) {
                if (Object.hasOwn(node.attributes, 'xml:base')) throw new Error('XSD xml:base 尚未支持；请使用本地相对依赖路径。');
                if (node.namespaceUri !== XSD || !dependencies.has(node.localName)) continue;
                const location = node.attributeTokens.schemaLocation;
                if (!location) continue;
                const target = await canonical(dependencyPath(file, location.value));
                resolved.set(location.valueStart, target);
                await visit(target);
            }
        };
        await visit(source);
        const ordered = [...files.values()].sort((a, b) => a.relative.localeCompare(b.relative));
        const versionHash = createHash('sha256').update(`newsdk-xsd-adapter-${adapterVersion}\0`).update(path.relative(root, source)).update('\0');
        const normalized = new Map<string, string>();
        for (const record of ordered) {
            versionHash.update(record.relative.replace(/\\/g, '/')).update('\0').update(record.original).update('\0');
            const document = parseXml(record.text, record.file);
            const patches: TextPatch[] = [];
            for (const node of document.elements) {
                if (node.namespaceUri !== XSD) continue;
                const location = node.attributeTokens.schemaLocation;
                const target = location && edges.get(record.file)?.get(location.valueStart);
                if (location && target) {
                    const value = relativeUri(record.file, target);
                    if (value !== location.value) {patches.push(createAttributePatch(document, {nodePath: node.path, attribute: 'schemaLocation', value, documentVersion: 0})); adjustments.schemaLocations++;}
                }
                for (const attribute of qnames) {
                    const token = node.attributeTokens[attribute];
                    if (token && token.value !== token.value.trim()) {patches.push(createAttributePatch(document, {nodePath: node.path, attribute, value: token.value.trim(), documentVersion: 0})); adjustments.qnames++;}
                }
            }
            normalized.set(record.relative, applyTextPatches(record.text, patches));
        }
        const version = versionHash.digest('hex');
        const storage = await canonicalPath(this.storageDirectory);
        if (inside(root, storage)) throw new Error('派生 XSD 缓存不能写入 SDK schema 来源目录。');
        await fs.mkdir(storage, {recursive: true});
        const directory = path.join(storage, version);
        const manifest: CacheManifest = {adapterVersion, version, files: [...normalized].map(([file, text]) => ({path: file, hash: digest(text)}))};
        if (!await this.validCache(directory, manifest)) {
            const staging = path.join(storage, `.${version}.${randomUUID()}.tmp`);
            await fs.mkdir(staging);
            try {
                for (const [file, text] of normalized) {
                    const destination = path.join(staging, file);
                    await fs.mkdir(path.dirname(destination), {recursive: true});
                    await fs.writeFile(destination, text, {flag: 'wx'});
                }
                await fs.writeFile(path.join(staging, 'newsdk-schema-snapshot.json'), JSON.stringify(manifest), {flag: 'wx'});
                // Only generated content-hash cache directories are replaced.
                await fs.rm(directory, {recursive: true, force: true});
                await fs.rename(staging, directory);
            } finally {await fs.rm(staging, {recursive: true, force: true});}
        }
        return {entryPath: path.join(directory, path.relative(root, source)), sourcePath: source, version, files: files.size, adjustments, normalized: true};
    }
    private async canonicalAbsolute(input: string): Promise<string> {
        if (!input || input.includes('\0')) throw new Error('XSD 入口路径无效。');
        const absolute = path.resolve(input);
        let current = path.parse(absolute).root;
        for (const component of absolute.slice(current.length).split(path.sep).filter(Boolean)) {
            const matches = (await fs.readdir(current)).filter(name => name.toLowerCase() === component.toLowerCase());
            if (matches.length !== 1) throw new Error('XSD 入口缺失或存在大小写歧义。');
            current = path.join(current, matches[0]);
        }
        const physical = await fs.realpath(current);
        if (!/\.xsd$/i.test(physical) || !(await fs.stat(physical)).isFile()) throw new Error('XSD 入口必须是本地 .xsd 文件。');
        return physical;
    }
    private async validCache(directory: string, expected: CacheManifest): Promise<boolean> {
        try {
            const info = await fs.lstat(directory);
            if (!info.isDirectory() || info.isSymbolicLink()) return false;
            const saved = JSON.parse(await fs.readFile(path.join(directory, 'newsdk-schema-snapshot.json'), 'utf8')) as CacheManifest;
            if (JSON.stringify(saved) !== JSON.stringify(expected)) return false;
            const expectedFiles = new Set([...saved.files.map(file => file.path), 'newsdk-schema-snapshot.json']);
            let seen = 0;
            const inspect = async (current: string): Promise<boolean> => {
                for (const entry of await fs.readdir(current, {withFileTypes: true})) {
                    const file = path.join(current, entry.name);
                    if (entry.isSymbolicLink()) return false;
                    if (entry.isDirectory()) {if (!await inspect(file)) return false;}
                    else if (!entry.isFile() || !expectedFiles.has(path.relative(directory, file)) || ++seen > expectedFiles.size) return false;
                }
                return true;
            };
            if (!await inspect(directory) || seen !== expectedFiles.size) return false;
            for (const file of saved.files) {
                const candidate = path.join(directory, file.path);
                const info = await fs.lstat(candidate);
                const physical = await fs.realpath(candidate);
                if (!info.isFile() || info.isSymbolicLink() || !inside(directory, physical) || digest(await fs.readFile(candidate)) !== file.hash) return false;
            }
            return true;
        } catch {return false;}
    }
}
