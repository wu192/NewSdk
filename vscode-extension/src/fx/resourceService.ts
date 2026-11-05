import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { Worker } from 'node:worker_threads';
import { canonicalPath, containsPath, pathIdentity } from '../project';
import { DecodedTexture, TextureDecodeError, TextureDecodeOptions, estimateTextureMemory } from './textureDecoder';
import type { TextureDecodeJob, TextureDecodeReply } from './textureWorker';

export interface ResourceServiceOptions {
    roots: readonly string[];
    /** Entire retained CPU pixel/block cache, including referenced old generations. */
    cacheBytes?: number;
    decodeBudgetBytes?: number;
    sourceBytes?: number;
    /** Source bytes plus one in-progress decoded image; separate from the retained LRU. */
    transientBytes?: number;
    maxDimension?: number;
    maxPending?: number;
    /** Bounds metadata even when thousands of tiny textures fit the pixel budget. */
    cacheEntries?: number;
}
export interface ResourceReady {
    status: 'ready'; key: string; path: string; generation: number; texture: DecodedTexture;
    source: 'source-texture';
}
export type ResourceFailureStatus = 'missing' | 'empty' | 'unsupported' | 'decode-error' | 'outside-roots' | 'budget' | 'cancelled';
export interface ResourceFailure { status: ResourceFailureStatus; path: string; message: string; generation: number }
export type ResourceResult = ResourceReady | ResourceFailure;
interface Entry {
    result: ResourceReady; identity: string; mtimeMs: number; size: number; refs: number; touched: number; stale: boolean;
}
class TextureWorkerFailure extends Error {}

/** One bounded decode queue, reference-counted LRU and event-driven generations. No disk cache is written. */
export class ResourceService {
    private readonly roots: Promise<string[]>;
    private readonly entries = new Map<string, Entry>();
    private readonly latest = new Map<string, string>();
    private readonly aliases = new Map<string, string>();
    private inflight?: { identity: string; invalidated: boolean };
    private queue: Promise<void> = Promise.resolve();
    private pending = 0;
    private tick = 0;
    private epoch = 0;
    private resetVersion = 0;
    private disposed = false;
    private bytes = 0;
    private activeSourceBytes = 0;
    private activeDecodeBytes = 0;
    private peakTransientBytes = 0;
    private peakControlledBytes = 0;
    private worker?: Worker;
    private workerTermination: Promise<void> = Promise.resolve();
    private terminatingWorkers = 0;
    private workerStarts = 0;
    private workerRestarts = 0;
    private readonly capacity: number;
    constructor(private readonly options: ResourceServiceOptions) {
        this.capacity = options.cacheBytes ?? 32 * 1024 * 1024;
        for (const [name, value] of Object.entries(options)) {
            if (name !== 'roots' && value !== undefined && (!Number.isSafeInteger(value) || (value as number) <= 0)) {
                throw new Error(`贴图 ${name} 预算必须是正整数。`);
            }
        }
        this.roots = Promise.all(options.roots.map(root => canonicalPath(root)));
        // A missing root is reported on load instead of becoming an unhandled rejection.
        void this.roots.catch(() => undefined);
    }
    get stats(): { bytes: number; entries: number; references: number; pending: number; generation: number;
        trackedFiles: number; aliases: number; workerCount: number; workerStarts: number; workerRestarts: number; disposed: boolean;
        sourceBytes: number; decodeBytes: number; transientBytes: number; peakTransientBytes: number; peakControlledBytes: number;
        cacheBudgetBytes: number; transientBudgetBytes: number; terminatingWorkers: number } {
        return { bytes: this.bytes, entries: this.entries.size,
            references: [...this.entries.values()].reduce((sum, entry) => sum + entry.refs, 0),
            pending: this.pending, generation: this.epoch,
            trackedFiles: new Set([...this.entries.values()].map(entry => entry.identity).concat(this.inflight?.identity ?? [])).size,
            aliases: this.aliases.size, workerCount: this.worker ? 1 : 0,
            workerStarts: this.workerStarts, workerRestarts: this.workerRestarts, disposed: this.disposed,
            sourceBytes: this.activeSourceBytes, decodeBytes: this.activeDecodeBytes,
            transientBytes: this.activeSourceBytes + this.activeDecodeBytes,
            peakTransientBytes: this.peakTransientBytes, peakControlledBytes: this.peakControlledBytes,
            cacheBudgetBytes: this.capacity, transientBudgetBytes: this.options.transientBytes ?? 64 * 1024 * 1024,
            terminatingWorkers: this.terminatingWorkers };
    }
    /** Source is transferred to the worker; decoded RGBA and retained compressed blocks are charged once each. */
    private accountTransient(source: number, decoded = 0): void {
        this.activeSourceBytes = source; this.activeDecodeBytes = decoded;
        this.peakTransientBytes = Math.max(this.peakTransientBytes, source + decoded);
        this.peakControlledBytes = Math.max(this.peakControlledBytes, this.bytes + source + decoded);
    }
    private stopWorker(worker: Worker): void {
        if (this.worker === worker) this.worker = undefined;
        this.terminatingWorkers++;
        const stopped = worker.terminate().then(() => undefined, () => undefined).finally(() => { this.terminatingWorkers--; });
        this.workerTermination = Promise.all([this.workerTermination, stopped]).then(() => undefined);
    }
    /** Awaitable cleanup boundary for host shutdown/stress tests; no new work should be submitted while waiting. */
    async whenIdle(): Promise<void> { await this.queue; await this.workerTermination; }
    load(file: string, options: { signal?: AbortSignal } = {}): Promise<ResourceResult> {
        if (this.disposed || options.signal?.aborted) return Promise.resolve(this.failure('cancelled', file, '资源任务已取消。'));
        if (this.pending >= (this.options.maxPending ?? 8)) return Promise.resolve(this.failure('budget', file, '资源任务队列已满，请稍后重试。'));
        this.pending++;
        const queuedReset = this.resetVersion;
        const run = this.queue.then(() => queuedReset === this.resetVersion ? this.loadNow(file, options.signal)
            : this.failure('cancelled', file, '资源任务所属的旧加载队列已取消。'));
        this.queue = run.then(() => undefined, () => undefined);
        return run.finally(() => { this.pending--; });
    }
    /** Each successful load acquires exactly one reference; release it when a page/resource is replaced. */
    release(handle: ResourceReady | string): void {
        const key = typeof handle === 'string' ? handle : handle.key, entry = this.entries.get(key);
        if (!entry) return;
        if (entry.refs > 0) entry.refs--;
        entry.touched = ++this.tick;
        if (!entry.refs && entry.stale) this.remove(key);
    }
    /** File-watch events and explicit reloads call this even if size and mtime are unchanged. */
    invalidate(file: string): void {
        const alias = pathIdentity(path.resolve(file)), identity = this.aliases.get(alias) ?? alias;
        ++this.epoch;
        if (this.inflight?.identity === identity) this.inflight.invalidated = true;
        this.retire(identity);
    }
    private retire(identity: string): void {
        this.latest.delete(identity);
        for (const [key, entry] of this.entries) if (entry.identity === identity) {
            entry.stale = true;
            if (!entry.refs) this.remove(key);
        }
    }
    clear(): void {
        ++this.epoch;
        ++this.resetVersion;
        if (this.inflight) this.inflight.invalidated = true;
        if (this.worker) this.stopWorker(this.worker);
        this.latest.clear();
        for (const [key, entry] of this.entries) {
            entry.stale = true;
            if (!entry.refs) this.remove(key);
        }
    }
    dispose(): void {
        this.disposed = true;
        ++this.epoch;
        ++this.resetVersion;
        if (this.inflight) this.inflight.invalidated = true;
        this.entries.clear(); this.latest.clear(); this.aliases.clear(); this.bytes = 0;
        this.accountTransient(0);
        if (this.worker) this.stopWorker(this.worker);
    }
    private failure(status: ResourceFailureStatus, file: string, message: string, identity?: string): ResourceFailure {
        return { status, path: file, message, generation: this.epoch };
    }
    private trackAlias(alias: string, identity: string): boolean {
        if (alias === identity) return true;
        if (this.aliases.get(alias) === identity) return true;
        // Refuse excess aliases explicitly rather than silently losing event invalidation for an older alias.
        if ([...this.aliases.values()].filter(value => value === identity).length >= 16) return false;
        this.aliases.set(alias, identity);
        return true;
    }
    private pruneIdentity(identity: string): void {
        if (this.inflight?.identity === identity || [...this.entries.values()].some(entry => entry.identity === identity)) return;
        for (const [alias, value] of this.aliases) if (value === identity) this.aliases.delete(alias);
    }
    private remove(key: string): void {
        const entry = this.entries.get(key);
        if (!entry) return;
        this.bytes -= entry.result.texture.byteLength;
        this.entries.delete(key);
        if (this.latest.get(entry.identity) === key) this.latest.delete(entry.identity);
        this.pruneIdentity(entry.identity);
    }
    private evict(required: number): boolean {
        const candidates = [...this.entries.entries()].filter(([, entry]) => entry.refs === 0)
            .sort((a, b) => a[1].touched - b[1].touched);
        for (const [key] of candidates) {
            if (this.bytes + required <= this.capacity && this.entries.size < (this.options.cacheEntries ?? 256)) break;
            this.remove(key);
        }
        return this.bytes + required <= this.capacity && this.entries.size < (this.options.cacheEntries ?? 256);
    }
    /** The service's serial queue owns one reusable worker. Large decodes do not block the extension host. */
    private async decode(bytes: Uint8Array, options: TextureDecodeOptions, signal?: AbortSignal): Promise<DecodedTexture> {
        // A cancelled large worker must finish terminating before the next source/decode can overlap it.
        await this.workerTermination;
        if (signal?.aborted || this.disposed) { const error = new Error('贴图任务已取消。'); error.name = 'AbortError'; throw error; }
        if (!this.worker) {
            const worker = new Worker(path.join(__dirname, 'textureWorker.js'));
            this.workerStarts++;
            worker.unref();
            // A failure while idle must not become an uncaught EventEmitter error.
            worker.on('error', () => { if (this.worker === worker) this.worker = undefined; });
            worker.on('exit', () => { if (this.worker === worker) this.worker = undefined; });
            this.worker = worker;
        }
        const worker = this.worker;
        return new Promise((resolve, reject) => {
            const cleanup = (): void => {
                worker.off('message', message); worker.off('error', failed); worker.off('exit', exited);
                signal?.removeEventListener('abort', aborted);
            };
            const message = (reply: TextureDecodeReply): void => {
                cleanup();
                if ('error' in reply) reject(new TextureDecodeError(reply.error.code, reply.error.message));
                else resolve(reply.texture);
            };
            const failed = (error: Error): void => { cleanup(); this.stopWorker(worker); reject(new TextureWorkerFailure(`贴图后台任务异常：${error.message}`)); };
            const exited = (): void => { cleanup(); reject(new TextureWorkerFailure('贴图后台任务意外结束。')); };
            const aborted = (): void => {
                cleanup(); this.stopWorker(worker);
                const error = new Error('贴图任务已取消。'); error.name = 'AbortError';
                void this.workerTermination.then(() => reject(error));
            };
            worker.once('message', message); worker.once('error', failed); worker.once('exit', exited);
            signal?.addEventListener('abort', aborted, { once: true });
            if (signal?.aborted || this.disposed) { aborted(); return; }
            const job: TextureDecodeJob = { bytes, options };
            try { worker.postMessage(job, [bytes.buffer as ArrayBuffer]); } catch (error) { failed(error as Error); }
        });
    }
    private async loadNow(file: string, signal?: AbortSignal, workerRetry = 0): Promise<ResourceResult> {
        const operationReset = this.resetVersion;
        const cancelled = (): boolean => this.disposed || !!signal?.aborted;
        if (cancelled()) return this.failure('cancelled', file, '资源任务已取消。');
        await this.workerTermination;
        if (cancelled() || operationReset !== this.resetVersion) return this.failure('cancelled', file, '资源任务已取消。');
        if (typeof file !== 'string' || !file.trim() || file.includes('\0') || !path.isAbsolute(file)) {
            return this.failure('outside-roots', file, '贴图必须是已解析的 MOD/SDK 内绝对路径。');
        }
        let physical = file, identity = '';
        let operation: { identity: string; invalidated: boolean } | undefined;
        try {
            const roots = await this.roots;
            physical = await canonicalPath(file);
            identity = pathIdentity(physical);
            if (!roots.some(root => containsPath(root, physical))) return this.failure('outside-roots', file, '贴图位于允许的 MOD/SDK 资源根之外。', identity);
            operation = { identity, invalidated: false }; this.inflight = operation;
            if (!this.trackAlias(pathIdentity(path.resolve(file)), identity)) return this.failure('budget', file, '同一贴图的路径别名超过 16 个，请使用规范路径。');
            const stat = await fs.stat(physical);
            if (!stat.isFile()) return this.failure('missing', physical, '贴图路径不是文件。', identity);
            if (stat.size === 0) return this.failure('empty', physical, '贴图文件为空。', identity);
            const transientLimit = this.options.transientBytes ?? 64 * 1024 * 1024;
            const sourceLimit = Math.min(this.options.sourceBytes ?? 64 * 1024 * 1024, transientLimit);
            if (stat.size > sourceLimit) return this.failure('budget', physical, '源贴图文件超过读取/暂存预算；可调整贴图预算或选择较小素材。', identity);
            const cachedKey = this.latest.get(identity), cached = cachedKey ? this.entries.get(cachedKey) : undefined;
            if (cached && cached.mtimeMs === stat.mtimeMs && cached.size === stat.size && !cached.stale) {
                if (cancelled() || operation.invalidated || operationReset !== this.resetVersion) return this.failure('cancelled', file, '资源任务已取消。', identity);
                cached.refs++; cached.touched = ++this.tick;
                return cached.result;
            }
            if (cached) this.retire(identity);
            // Every newly decoded upload has a unique generation, even after its CPU entry was evicted.
            // A GPU texture or host.sentTextures may still remember that previous key.
            const generation = ++this.epoch;
            const handle = await fs.open(physical, 'r');
            let source: Uint8Array;
            try {
                const opened = await handle.stat();
                if (!opened.isFile() || opened.size !== stat.size || opened.mtimeMs !== stat.mtimeMs) {
                    this.invalidate(physical);
                    return this.failure('cancelled', file, '资源在打开时变化，请重试。', identity);
                }
                // A bounded read cannot follow file growth into an unbounded readFile allocation.
                this.accountTransient(opened.size);
                source = new Uint8Array(opened.size);
                let read = 0;
                while (read < source.length) {
                    if (cancelled()) return this.failure('cancelled', file, '资源任务已取消。', identity);
                    const chunk = await handle.read(source, read, Math.min(1024 * 1024, source.length - read), read);
                    if (chunk.bytesRead === 0) return this.failure('cancelled', file, '资源在读取时被截断，请重试。', identity);
                    read += chunk.bytesRead;
                }
            } finally { await handle.close(); }
            if (cancelled() || operation.invalidated || operationReset !== this.resetVersion) {
                return this.failure('cancelled', file, '资源在加载时变化，请重试。', identity);
            }
            // Re-check a symlink/rename after I/O before accepting bytes for an allowed resource.
            if (pathIdentity(await fs.realpath(file)) !== identity) return this.failure('cancelled', file, '资源路径在加载时变化，请重试。', identity);
            const after = await fs.stat(physical);
            if (after.size !== stat.size || after.mtimeMs !== stat.mtimeMs) {
                this.invalidate(physical);
                return this.failure('cancelled', file, '资源内容在加载时变化，请重试。', identity);
            }
            if (source.byteLength > sourceLimit) return this.failure('budget', physical, '源贴图文件超过读取/暂存预算。', identity);
            const limit = Math.min(this.options.decodeBudgetBytes ?? 32 * 1024 * 1024, this.capacity, transientLimit - source.byteLength);
            // Reserve against active old generations before allocating another decoded image.
            const available = this.capacity - [...this.entries.values()].filter(entry => entry.refs > 0)
                .reduce((sum, entry) => sum + entry.result.texture.byteLength, 0);
            if (available <= 0) return this.failure('budget', physical, '已引用贴图占满 CPU 缓存预算。', identity);
            const decodeOptions = { maxBytes: Math.min(limit, available), maxDimension: this.options.maxDimension };
            const estimate = estimateTextureMemory(source, decodeOptions);
            // Evict before allocating: unused cached pixels must not overlap another full decode allocation.
            if (!this.evict(estimate.decodedBytes)) return this.failure('budget', physical, '贴图缓存预算不足；请释放其他预览或提高贴图预算。', identity);
            this.accountTransient(source.byteLength, estimate.decodedBytes);
            const texture = await this.decode(source, decodeOptions, signal);
            this.accountTransient(0, texture.byteLength);
            if (cancelled() || operation.invalidated || operationReset !== this.resetVersion) {
                return this.failure('cancelled', file, '资源任务已取消。', identity);
            }
            if (!this.evict(texture.byteLength)) return this.failure('budget', physical, '贴图缓存预算不足；请释放其他预览。', identity);
            const key = `${identity}@${generation}`;
            const ready: ResourceReady = { status: 'ready', key, path: physical, generation, texture, source: 'source-texture' };
            this.entries.set(key, { result: ready, identity, refs: 1, touched: ++this.tick, stale: false, mtimeMs: stat.mtimeMs, size: stat.size });
            this.latest.set(identity, key); this.bytes += texture.byteLength;
            this.accountTransient(0);
            return ready;
        } catch (error) {
            if (cancelled() || operationReset !== this.resetVersion || error instanceof Error && error.name === 'AbortError') {
                return this.failure('cancelled', file, '资源任务已取消。', identity);
            }
            if (error instanceof TextureWorkerFailure && workerRetry === 0) {
                // The source buffer was transferred, so re-read once instead of retaining a duplicate for retries.
                if (this.worker) this.stopWorker(this.worker);
                this.workerRestarts++;
                return this.loadNow(file, signal, 1);
            }
            if (error instanceof TextureDecodeError) return this.failure(error.code === 'budget' ? 'budget'
                : error.code === 'unsupported' ? 'unsupported' : 'decode-error', physical,
                error.code === 'budget' ? `${error.message} 可调整 newsdk.fxTextureBudgetMiB 或选择较小素材。` : error.message, identity);
            if (error && typeof error === 'object' && 'code' in error && ['ENOENT', 'ENOTDIR'].includes(String(error.code))) {
                return this.failure('missing', physical, '贴图文件不存在。', identity);
            }
            return this.failure('decode-error', physical, error instanceof Error ? error.message : String(error), identity);
        } finally {
            this.accountTransient(0);
            if (operation && this.inflight === operation) this.inflight = undefined;
            if (identity) this.pruneIdentity(identity);
        }
    }
}
