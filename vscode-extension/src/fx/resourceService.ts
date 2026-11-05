import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { Worker } from 'node:worker_threads';
import { canonicalPath, containsPath, pathIdentity } from '../project';
import { DecodedTexture, TextureDecodeError, TextureDecodeOptions } from './textureDecoder';
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
    private readonly generations = new Map<string, number>();
    private readonly aliases = new Map<string, string>();
    private queue: Promise<void> = Promise.resolve();
    private pending = 0;
    private tick = 0;
    private epoch = 0;
    private resetVersion = 0;
    private disposed = false;
    private bytes = 0;
    private worker?: Worker;
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
        workerCount: number; workerStarts: number; workerRestarts: number; disposed: boolean } {
        return { bytes: this.bytes, entries: this.entries.size,
            references: [...this.entries.values()].reduce((sum, entry) => sum + entry.refs, 0),
            pending: this.pending, generation: this.epoch, workerCount: this.worker ? 1 : 0,
            workerStarts: this.workerStarts, workerRestarts: this.workerRestarts, disposed: this.disposed };
    }
    load(file: string, options: { signal?: AbortSignal } = {}): Promise<ResourceResult> {
        if (this.disposed || options.signal?.aborted) return Promise.resolve(this.failure('cancelled', file, '资源任务已取消。'));
        if (this.pending >= (this.options.maxPending ?? 8)) return Promise.resolve(this.failure('budget', file, '资源任务队列已满，请稍后重试。'));
        this.pending++;
        const run = this.queue.then(() => this.loadNow(file, options.signal));
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
        this.generations.set(identity, ++this.epoch);
        this.latest.delete(identity);
        for (const [key, entry] of this.entries) if (entry.identity === identity) {
            entry.stale = true;
            if (!entry.refs) this.remove(key);
        }
    }
    clear(): void {
        ++this.epoch;
        ++this.resetVersion;
        if (this.worker) { void this.worker.terminate(); this.worker = undefined; }
        this.latest.clear();
        for (const [key, entry] of this.entries) {
            this.generations.set(entry.identity, this.epoch);
            entry.stale = true;
            if (!entry.refs) this.remove(key);
        }
    }
    dispose(): void {
        this.disposed = true;
        ++this.epoch;
        ++this.resetVersion;
        this.entries.clear(); this.latest.clear(); this.aliases.clear(); this.generations.clear(); this.bytes = 0;
        if (this.worker) { void this.worker.terminate(); this.worker = undefined; }
    }
    private failure(status: ResourceFailureStatus, file: string, message: string, identity?: string): ResourceFailure {
        return { status, path: file, message, generation: this.generations.get(identity ?? file) ?? this.epoch };
    }
    private remove(key: string): void {
        const entry = this.entries.get(key);
        if (!entry) return;
        this.bytes -= entry.result.texture.byteLength;
        this.entries.delete(key);
        if (this.latest.get(entry.identity) === key) this.latest.delete(entry.identity);
    }
    private evict(required: number): boolean {
        const candidates = [...this.entries.entries()].filter(([, entry]) => entry.refs === 0)
            .sort((a, b) => a[1].touched - b[1].touched);
        for (const [key] of candidates) {
            if (this.bytes + required <= this.capacity) break;
            this.remove(key);
        }
        return this.bytes + required <= this.capacity;
    }
    /** The service's serial queue owns one reusable worker. Large decodes do not block the extension host. */
    private decode(bytes: Uint8Array, options: TextureDecodeOptions, signal?: AbortSignal): Promise<DecodedTexture> {
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
            const failed = (error: Error): void => { cleanup(); reject(new TextureWorkerFailure(`贴图后台任务异常：${error.message}`)); };
            const exited = (): void => { cleanup(); reject(new TextureWorkerFailure('贴图后台任务意外结束。')); };
            const aborted = (): void => {
                cleanup(); if (this.worker === worker) this.worker = undefined;
                void worker.terminate();
                const error = new Error('贴图任务已取消。'); error.name = 'AbortError'; reject(error);
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
        if (typeof file !== 'string' || !file.trim() || file.includes('\0') || !path.isAbsolute(file)) {
            return this.failure('outside-roots', file, '贴图必须是已解析的 MOD/SDK 内绝对路径。');
        }
        let physical = file, identity = '';
        try {
            const roots = await this.roots;
            physical = await canonicalPath(file);
            identity = pathIdentity(physical);
            if (!roots.some(root => containsPath(root, physical))) return this.failure('outside-roots', file, '贴图位于允许的 MOD/SDK 资源根之外。', identity);
            this.aliases.set(pathIdentity(path.resolve(file)), identity);
            const stat = await fs.stat(physical);
            if (!stat.isFile()) return this.failure('missing', physical, '贴图路径不是文件。', identity);
            if (stat.size === 0) return this.failure('empty', physical, '贴图文件为空。', identity);
            const transientLimit = this.options.transientBytes ?? 64 * 1024 * 1024;
            const sourceLimit = Math.min(this.options.sourceBytes ?? 64 * 1024 * 1024, transientLimit);
            if (stat.size >= sourceLimit) return this.failure('budget', physical, '源贴图文件超过读取/暂存预算。', identity);
            const cachedKey = this.latest.get(identity), cached = cachedKey ? this.entries.get(cachedKey) : undefined;
            if (cached && cached.mtimeMs === stat.mtimeMs && cached.size === stat.size && !cached.stale) {
                if (cancelled()) return this.failure('cancelled', file, '资源任务已取消。', identity);
                cached.refs++; cached.touched = ++this.tick;
                return cached.result;
            }
            if (cached) this.invalidate(physical);
            const generation = this.generations.get(identity) ?? 0;
            const resetVersion = this.resetVersion;
            const handle = await fs.open(physical, 'r');
            let source: Uint8Array;
            try {
                const opened = await handle.stat();
                if (!opened.isFile() || opened.size !== stat.size || opened.mtimeMs !== stat.mtimeMs) {
                    this.invalidate(physical);
                    return this.failure('cancelled', file, '资源在打开时变化，请重试。', identity);
                }
                // A bounded read cannot follow file growth into an unbounded readFile allocation.
                source = new Uint8Array(opened.size);
                let read = 0;
                while (read < source.length) {
                    if (cancelled()) return this.failure('cancelled', file, '资源任务已取消。', identity);
                    const chunk = await handle.read(source, read, Math.min(1024 * 1024, source.length - read), read);
                    if (chunk.bytesRead === 0) return this.failure('cancelled', file, '资源在读取时被截断，请重试。', identity);
                    read += chunk.bytesRead;
                }
            } finally { await handle.close(); }
            if (cancelled() || resetVersion !== this.resetVersion || generation !== (this.generations.get(identity) ?? 0)) {
                return this.failure('cancelled', file, '资源在加载时变化，请重试。', identity);
            }
            // Re-check a symlink/rename after I/O before accepting bytes for an allowed resource.
            if (pathIdentity(await fs.realpath(file)) !== identity) return this.failure('cancelled', file, '资源路径在加载时变化，请重试。', identity);
            const after = await fs.stat(physical);
            if (after.size !== stat.size || after.mtimeMs !== stat.mtimeMs) {
                this.invalidate(physical);
                return this.failure('cancelled', file, '资源内容在加载时变化，请重试。', identity);
            }
            if (source.byteLength >= sourceLimit) return this.failure('budget', physical, '源贴图文件超过读取/暂存预算。', identity);
            const limit = Math.min(this.options.decodeBudgetBytes ?? 32 * 1024 * 1024, this.capacity, transientLimit - source.byteLength);
            // Reserve against active old generations before allocating another decoded image.
            const available = this.capacity - [...this.entries.values()].filter(entry => entry.refs > 0)
                .reduce((sum, entry) => sum + entry.result.texture.byteLength, 0);
            if (available <= 0) return this.failure('budget', physical, '已引用贴图占满 CPU 缓存预算。', identity);
            const texture = await this.decode(source, { maxBytes: Math.min(limit, available), maxDimension: this.options.maxDimension }, signal);
            if (cancelled() || resetVersion !== this.resetVersion || generation !== (this.generations.get(identity) ?? 0)) {
                return this.failure('cancelled', file, '资源任务已取消。', identity);
            }
            if (!this.evict(texture.byteLength)) return this.failure('budget', physical, '贴图缓存预算不足；请释放其他预览。', identity);
            const key = `${identity}@${generation}`;
            const ready: ResourceReady = { status: 'ready', key, path: physical, generation, texture, source: 'source-texture' };
            this.entries.set(key, { result: ready, identity, refs: 1, touched: ++this.tick, stale: false, mtimeMs: stat.mtimeMs, size: stat.size });
            this.latest.set(identity, key); this.bytes += texture.byteLength;
            return ready;
        } catch (error) {
            if (cancelled() || operationReset !== this.resetVersion || error instanceof Error && error.name === 'AbortError') {
                return this.failure('cancelled', file, '资源任务已取消。', identity);
            }
            if (error instanceof TextureWorkerFailure && workerRetry === 0) {
                // The source buffer was transferred, so re-read once instead of retaining a duplicate for retries.
                if (this.worker) { void this.worker.terminate(); this.worker = undefined; }
                this.workerRestarts++;
                return this.loadNow(file, signal, 1);
            }
            if (error instanceof TextureDecodeError) return this.failure(error.code === 'budget' ? 'budget'
                : error.code === 'unsupported' ? 'unsupported' : 'decode-error', physical, error.message, identity);
            if (error && typeof error === 'object' && 'code' in error && ['ENOENT', 'ENOTDIR'].includes(String(error.code))) {
                return this.failure('missing', physical, '贴图文件不存在。', identity);
            }
            return this.failure('decode-error', physical, error instanceof Error ? error.message : String(error), identity);
        }
    }
}
