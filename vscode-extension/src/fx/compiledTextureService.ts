import * as path from 'node:path';
import { pathIdentity } from '../project';
import type { FxTexture, PreviewIR } from './previewProtocol';
import type { ResourceFailure, ResourceService } from './resourceService';

export interface FxTextureInputVersion {
    documentUri: string;
    documentVersion: number;
    savedDocumentVersion?: number;
    /** Raised by input file events/reload, rather than by a new parse or decoded cache entry. */
    inputGeneration: number;
}
export interface FxCompiledTextureMetadata {
    path: string;
    importedAt: number;
    input: FxTextureInputVersion;
    state: 'unverified' | 'stale';
    message: string;
}
interface ImportedTexture {
    metadata: FxCompiledTextureMetadata;
    selected: boolean;
    touched: number;
}
export type FxCompiledTextureImport = { status: 'imported'; compiled: FxCompiledTextureMetadata } | ResourceFailure;

function sourceFile(texture: FxTexture): string | undefined { return texture.sourceFile ?? texture.file; }
function identity(file: string): string { return pathIdentity(path.resolve(file)); }
function validInput(input: FxTextureInputVersion): boolean {
    return typeof input.documentUri === 'string' && input.documentUri.length > 0
        && Number.isSafeInteger(input.documentVersion) && input.documentVersion >= 0
        && Number.isSafeInteger(input.inputGeneration) && input.inputGeneration >= 0
        && (input.savedDocumentVersion === undefined || Number.isSafeInteger(input.savedDocumentVersion)
            && input.savedDocumentVersion >= 0 && input.savedDocumentVersion <= input.documentVersion);
}
function sameInput(a: FxTextureInputVersion, b: FxTextureInputVersion): boolean {
    return a.documentUri === b.documentUri && a.documentVersion === b.documentVersion
        && a.savedDocumentVersion === b.savedDocumentVersion && a.inputGeneration === b.inputGeneration;
}
function copyMetadata(value: FxCompiledTextureMetadata): FxCompiledTextureMetadata {
    return { ...value, input: { ...value.input } };
}

/** A session-local, bounded registry of explicitly imported DDS results. It never writes source/cache files. */
export class FxCompiledTextureService {
    private readonly imports = new Map<string, ImportedTexture>();
    private readonly pending = new Map<string, { id: number; abort: AbortController }>();
    private tick = 0;
    private epoch = 0;
    private disposed = false;
    private readonly maxEntries: number;
    constructor(private readonly resources: Pick<ResourceService, 'load' | 'release'>, options: { maxEntries?: number } = {}) {
        this.maxEntries = options.maxEntries ?? 256;
        if (!Number.isSafeInteger(this.maxEntries) || this.maxEntries < 1) throw new Error('编译贴图记录上限必须是正整数。');
    }
    get stats(): { entries: number; pending: number; disposed: boolean } {
        return { entries: this.imports.size, pending: this.pending.size, disposed: this.disposed };
    }
    async importResult(texture: FxTexture, compiledFile: string, input: FxTextureInputVersion,
        options: { signal?: AbortSignal } = {}): Promise<FxCompiledTextureImport> {
        const file = sourceFile(texture);
        const failure = (status: ResourceFailure['status'], message: string): ResourceFailure =>
            ({ status, path: compiledFile, message, generation: this.epoch });
        if (this.disposed || options.signal?.aborted) return failure('cancelled', '编译贴图导入已取消。');
        if (!file || !path.isAbsolute(file) || !validInput(input)) return failure('decode-error', '贴图来源或输入版本无效。');
        const source = identity(file), operation = ++this.epoch, version = { ...input };
        if (this.pending.size >= 8 && !this.pending.has(source)) return failure('budget', '编译贴图导入队列已满。');
        this.pending.get(source)?.abort.abort();
        const abort = new AbortController(), cancelled = (): void => abort.abort();
        options.signal?.addEventListener('abort', cancelled, { once: true });
        this.pending.set(source, { id: operation, abort });
        try {
            // Uses exactly the same canonical-root, symlink, file-change and byte-budget checks as source loading.
            const loaded = await this.resources.load(compiledFile, { signal: abort.signal });
            try {
                if (this.disposed || abort.signal.aborted || this.pending.get(source)?.id !== operation) {
                    return failure('cancelled', '编译贴图导入已取消或已被较新的选择替换。');
                }
                if (loaded.status !== 'ready') return loaded;
                if (loaded.texture.format === 'TGA') return failure('unsupported', '编译结果导入需要可读取的 DDS 文件；源 TGA 不能作为编译结果。');
                const metadata: FxCompiledTextureMetadata = { path: loaded.path, importedAt: Date.now(), input: version,
                    state: 'unverified', message: '已导入 DDS 编译结果；原始编译输入未核实，不能视为当前预览的编译结果。' };
                this.imports.delete(source);
                while (this.imports.size >= this.maxEntries) {
                    const oldest = [...this.imports].sort((a, b) => a[1].touched - b[1].touched)[0];
                    this.imports.delete(oldest[0]);
                }
                this.imports.set(source, { metadata, selected: true, touched: ++this.tick });
                return { status: 'imported', compiled: copyMetadata(metadata) };
            } finally { if (loaded.status === 'ready') this.resources.release(loaded); }
        } finally {
            options.signal?.removeEventListener('abort', cancelled);
            if (this.pending.get(source)?.id === operation) this.pending.delete(source);
        }
    }
    select(texture: FxTexture, source: 'source' | 'compiled'): boolean {
        const file = sourceFile(texture), entry = file && this.imports.get(identity(file));
        if (this.disposed || !entry) return source === 'source' && !this.disposed;
        entry.selected = source === 'compiled'; entry.touched = ++this.tick;
        return true;
    }
    /** Called before prepareTextureState: source and compiled files therefore get distinct GPU upload identities. */
    applySelection(ir: PreviewIR, input: FxTextureInputVersion): void {
        if (this.disposed || !validInput(input)) return;
        for (const texture of ir.textures) {
            const file = sourceFile(texture);
            if (!file) continue;
            const entry = this.imports.get(identity(file));
            if (!entry) {
                if (texture.sourceFile && texture.source === 'compiled') {
                    texture.file = texture.sourceFile; texture.source = 'source'; texture.status = 'pending';
                    delete texture.width; delete texture.height; delete texture.message;
                }
                delete texture.compiled;
                continue;
            }
            entry.touched = ++this.tick;
            const metadata = copyMetadata(entry.metadata);
            if (!sameInput(metadata.input, input)) {
                metadata.state = 'stale';
                metadata.message = '导入后文档、保存版本或资源输入已变化；此 DDS 是旧结果，不能代表当前预览。';
            }
            texture.sourceFile = file; texture.compiled = metadata;
            texture.source = entry.selected ? 'compiled' : 'source';
            const selected = entry.selected ? metadata.path : file;
            if (texture.file !== selected) {
                texture.file = selected; texture.status = 'pending';
                delete texture.width; delete texture.height; delete texture.message;
                delete texture.pixels; delete texture.pixelsBase64;
            }
        }
    }
    clear(): void {
        ++this.epoch;
        for (const operation of this.pending.values()) operation.abort.abort();
        this.pending.clear(); this.imports.clear();
    }
    dispose(): void { this.disposed = true; this.clear(); }
}
