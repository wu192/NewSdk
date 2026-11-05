import type { FxIssue, FxTexture, PreviewIR } from './previewProtocol';
import type { ResourceReady, ResourceService } from './resourceService';

export interface UploadedTexture { key: string; width: number; height: number; }
export interface TexturePipelineOptions {
    uploaded: Map<string, UploadedTexture>;
    signal?: AbortSignal;
    maxBytes: number;
    current(): boolean;
    publish(previousKey: string, texture: FxTexture, issue?: FxIssue): Promise<void>;
    transfer(key: string, loaded: ResourceReady): Promise<boolean>;
}

function bindKey(ir: PreviewIR, texture: FxTexture, key: string): string {
    const previous = texture.key; texture.key = key;
    for (const system of ir.systems) if (system.textureKey === previous) system.textureKey = key;
    return previous;
}

/** Only lightweight metadata survives parameter edits; pixel caches can evict freely. */
export function prepareTextureState(ir: PreviewIR, uploaded: Map<string, UploadedTexture>): void {
    const files = new Set(ir.textures.flatMap(texture => texture.file ? [texture.file] : []));
    for (const file of uploaded.keys()) if (!files.has(file)) uploaded.delete(file);
    for (const texture of ir.textures) {
        const retained = texture.file && uploaded.get(texture.file);
        if (!retained) continue;
        bindKey(ir, texture, retained.key);
        texture.width = retained.width; texture.height = retained.height;
        texture.status = 'ready'; delete texture.message;
    }
}

function textureProblem(texture: FxTexture): FxIssue | undefined {
    if (!texture.location || texture.status === 'ready') return undefined;
    return { ...texture.location, code: 'texture-resource', severity: 'warning',
        message: `贴图 ${texture.name}（${texture.status}）：${texture.message ?? '资源暂不可用'}` };
}
function recordProblem(ir: PreviewIR, texture: FxTexture): FxIssue | undefined {
    const problem = textureProblem(texture);
    if (!problem) return undefined;
    const at = ir.issues.findIndex(issue => issue.code === problem.code && issue.uri === problem.uri && issue.start === problem.start);
    if (at < 0) ir.issues.push(problem); else ir.issues[at] = problem;
    return problem;
}

/**
 * Decode and transfer each file under one acquired handle, then release it before
 * touching the next file. Total active GPU textures may exceed the CPU LRU cache.
 */
export async function streamPreviewTextures(ir: PreviewIR, resources: Pick<ResourceService, 'load' | 'release'>,
    options: TexturePipelineOptions): Promise<void> {
    for (const texture of ir.textures) {
        if (!texture.file) continue;
        if (!options.current() || options.signal?.aborted) return;
        const retained = options.uploaded.get(texture.file);
        if (retained) {
            if (texture.key !== retained.key || texture.status !== 'ready') {
                const previousKey = bindKey(ir, texture, retained.key);
                texture.width = retained.width; texture.height = retained.height; texture.status = 'ready'; delete texture.message;
                await options.publish(previousKey, { ...texture });
            }
            continue;
        }
        const loaded = await resources.load(texture.file, { signal: options.signal });
        try {
            if (!options.current() || options.signal?.aborted) return;
            const previousKey = texture.key;
            texture.status = loaded.status;
            if (loaded.status === 'ready') {
                bindKey(ir, texture, `gpu:${loaded.key}`);
                texture.width = loaded.texture.width; texture.height = loaded.texture.height;
                delete texture.message;
                if (loaded.texture.byteLength > options.maxBytes) {
                    texture.status = 'budget'; texture.message = '单贴图超过 CPU 预览预算，请选择低分辨率源图。';
                }
            } else texture.message = loaded.message;
            const issue = recordProblem(ir, texture);
            await options.publish(previousKey, { ...texture }, issue);
            if (!options.current() || options.signal?.aborted) return;
            if (loaded.status !== 'ready' || texture.status !== 'ready') continue;
            if (await options.transfer(texture.key, loaded)) {
                if (options.current() && !options.signal?.aborted) options.uploaded.set(texture.file,
                    { key: texture.key, width: loaded.texture.width, height: loaded.texture.height });
            } else if (options.current() && !options.signal?.aborted) {
                texture.status = 'transfer-error'; texture.message = '贴图传输未完成，请重载资源后重试。';
                const transferIssue = recordProblem(ir, texture);
                await options.publish(texture.key, { ...texture }, transferIssue);
            }
        } finally { if (loaded.status === 'ready') resources.release(loaded); }
    }
}
