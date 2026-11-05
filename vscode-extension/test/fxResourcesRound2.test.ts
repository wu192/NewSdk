import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { ResourceReady, ResourceService } from '../src/fx/resourceService';
import { decodeTexture, estimateTextureMemory } from '../src/fx/textureDecoder';

const MiB = 1024 * 1024;
function fullDds(width: number, height: number): { bytes: Buffer; rgba: number; compressed: number; levels: number } {
    let rgba = 0, compressed = 0, levels = 0;
    for (let w = width, h = height; ; w = Math.max(1, w >>> 1), h = Math.max(1, h >>> 1)) {
        rgba += w * h * 4; compressed += Math.ceil(w / 4) * Math.ceil(h / 4) * 8; levels++;
        if (w === 1 && h === 1) break;
    }
    const bytes = Buffer.alloc(128 + compressed); bytes.write('DDS ');
    for (const [offset, value] of [[4, 124], [8, 0x21007], [12, height], [16, width], [28, levels], [76, 32], [80, 4], [108, 0x401008]]) bytes.writeUInt32LE(value, offset);
    bytes.write('DXT1', 84);
    for (let at = 128; at < bytes.length; at += 8) bytes.writeUInt16LE(0xf800, at);
    return { bytes, rgba, compressed, levels };
}
function tga(width: number, height: number): Buffer {
    const bytes = Buffer.alloc(18 + width * height * 4, 0x7f); bytes.fill(0, 0, 18);
    bytes[2] = 2; bytes.writeUInt16LE(width, 12); bytes.writeUInt16LE(height, 14); bytes[16] = 32; bytes[17] = 0x28;
    return bytes;
}
async function fixture(run: (root: string) => Promise<void>): Promise<void> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-resources-round2-')));
    try { await run(root); } finally { await fs.rm(root, { recursive: true, force: true }); }
}
function ready(value: Awaited<ReturnType<ResourceService['load']>>): ResourceReady {
    assert.equal(value.status, 'ready', value.status === 'ready' ? undefined : value.message); return value as ResourceReady;
}

test('real 4k DDS full mip payload charges RGBA and compressed copies and loads only under an explicit increased budget', async () => fixture(async root => {
    const image = fullDds(4096, 4096), file = path.join(root, '4k.dds'); await fs.writeFile(file, image.bytes);
    assert.equal(image.rgba, 89478484); assert.equal(image.compressed, 11184824); assert.equal(image.levels, 13);
    const limited = new ResourceService({ roots: [root] });
    try {
        const rejected = await limited.load(file); assert.equal(rejected.status, 'budget');
        assert.equal(limited.stats.workerStarts, 0, 'header/mip preflight blocks the oversized allocation before creating a worker');
        assert.equal(limited.stats.bytes, 0); assert.equal(limited.stats.transientBytes, 0);
    } finally { limited.dispose(); }
    const budget = 128 * MiB;
    const resources = new ResourceService({ roots: [root], cacheBytes: budget, decodeBudgetBytes: budget, transientBytes: budget * 2, sourceBytes: budget * 2 });
    try {
        const result = ready(await resources.load(file));
        assert.equal(result.texture.width, 4096); assert.equal(result.texture.height, 4096); assert.equal(result.texture.mips.length, 13);
        assert.deepEqual([...result.texture.pixels.subarray(0, 4)], [255, 0, 0, 255]);
        assert.equal(result.texture.byteLength, image.rgba + image.compressed);
        assert.equal(resources.stats.bytes, result.texture.byteLength);
        assert.equal(resources.stats.peakTransientBytes, image.bytes.byteLength + result.texture.byteLength);
        assert.equal(resources.stats.peakControlledBytes, resources.stats.peakTransientBytes);
        assert.ok(resources.stats.peakTransientBytes <= resources.stats.transientBudgetBytes);
        assert.equal(resources.stats.transientBytes, 0); resources.release(result); resources.clear();
        assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.entries, 0); assert.equal(resources.stats.workerCount, 0);
    } finally { resources.dispose(); }
}));

test('real 4k TGA retains alpha under increased source/decode budgets and accounts source plus decode staging', async () => fixture(async root => {
    const file = path.join(root, '4k.tga'), image = tga(4096, 4096); await fs.writeFile(file, image);
    const resources = new ResourceService({ roots: [root], cacheBytes: 128 * MiB, decodeBudgetBytes: 128 * MiB,
        sourceBytes: 256 * MiB, transientBytes: 256 * MiB });
    try {
        const result = ready(await resources.load(file));
        assert.equal(result.texture.byteLength, 64 * MiB); assert.equal(result.texture.pixels[result.texture.pixels.length - 1], 0x7f);
        assert.equal(resources.stats.peakTransientBytes, image.byteLength + 64 * MiB);
        assert.equal(resources.stats.transientBytes, 0); resources.release(result);
    } finally { resources.dispose(); }
    assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.references, 0); assert.equal(resources.stats.pending, 0);
}));

test('mip allocation preflight is exact and malformed lower mips fail before any decode worker starts', async () => fixture(async root => {
    const image = fullDds(16, 8), estimate = estimateTextureMemory(image.bytes, { maxBytes: MiB });
    assert.deepEqual(estimate, { decodedBytes: image.rgba + image.compressed, rgbaBytes: image.rgba, compressedBytes: image.compressed, mipCount: image.levels });
    assert.equal(decodeTexture(image.bytes, { maxBytes: MiB }).byteLength, estimate.decodedBytes);
    assert.equal(estimateTextureMemory(image.bytes, { maxBytes: MiB, keepCompressed: false }).decodedBytes, image.rgba);
    const file = path.join(root, 'truncated.dds'); await fs.writeFile(file, image.bytes.subarray(0, image.bytes.length - 1));
    const resources = new ResourceService({ roots: [root] });
    try {
        assert.equal((await resources.load(file)).status, 'decode-error'); assert.equal(resources.stats.workerStarts, 0);
        assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.transientBytes, 0); assert.equal(resources.stats.trackedFiles, 0);
    } finally { resources.dispose(); }
}));

test('100 actual texture switches bound cache pixels, file metadata, worker count and staging; close leaves no resources', async () => fixture(async root => {
    const resources = new ResourceService({ roots: [root], cacheBytes: 2 * MiB, cacheEntries: 3, transientBytes: 4 * MiB });
    try {
        for (let index = 0; index < 100; index++) {
            const file = path.join(root, `texture-${index}.tga`); await fs.writeFile(file, tga(256, 256));
            const result = ready(await resources.load(file)); resources.release(result);
            assert.ok(resources.stats.bytes <= 2 * MiB); assert.ok(resources.stats.entries <= 3);
            assert.equal(resources.stats.entries, resources.stats.trackedFiles); assert.equal(resources.stats.references, 0);
            assert.equal(resources.stats.workerCount, 1); assert.equal(resources.stats.transientBytes, 0);
        }
        assert.equal(resources.stats.workerStarts, 1); assert.equal(resources.stats.aliases, 0);
        assert.ok(resources.stats.peakControlledBytes <= resources.stats.cacheBudgetBytes + resources.stats.transientBudgetBytes);
        resources.clear(); await resources.whenIdle(); assert.equal(resources.stats.entries, 0); assert.equal(resources.stats.trackedFiles, 0);
        assert.equal(resources.stats.workerCount, 0); assert.equal(resources.stats.pending, 0);
        assert.equal(resources.stats.terminatingWorkers, 0);
        assert.equal((await fs.readdir(root)).length, 100, 'memory cache creates no additional files or directories');
    } finally { resources.dispose(); }
    assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.transientBytes, 0); assert.equal(resources.stats.aliases, 0);
}));

test('cancelling a real 4k decode terminates the worker and releases staging before a fresh request recovers', async () => fixture(async root => {
    const large = path.join(root, 'large.dds'), small = path.join(root, 'small.tga');
    await Promise.all([fs.writeFile(large, fullDds(4096, 4096).bytes), fs.writeFile(small, tga(1, 1))]);
    const resources = new ResourceService({ roots: [root], cacheBytes: 128 * MiB, decodeBudgetBytes: 128 * MiB, transientBytes: 256 * MiB });
    const abort = new AbortController();
    try {
        const pending = resources.load(large, { signal: abort.signal });
        for (let attempt = 0; attempt < 10000 && !resources.stats.workerCount; attempt++) await new Promise<void>(resolve => setImmediate(resolve));
        assert.equal(resources.stats.workerCount, 1); abort.abort();
        assert.equal((await pending).status, 'cancelled'); assert.equal(resources.stats.workerCount, 0);
        assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.transientBytes, 0);
        const fresh = ready(await resources.load(small)); resources.release(fresh);
        assert.equal(resources.stats.workerStarts, 2); assert.equal(resources.stats.references, 0);
    } finally { resources.dispose(); await resources.whenIdle(); }
    assert.equal(resources.stats.pending, 0); assert.equal(resources.stats.workerCount, 0); assert.equal(resources.stats.trackedFiles, 0);
    assert.equal(resources.stats.terminatingWorkers, 0);
}));

test('clear cancels both active and queued old loads and has an awaitable zero-worker cleanup boundary', async () => fixture(async root => {
    const large = path.join(root, 'large.dds'), small = path.join(root, 'small.tga');
    await Promise.all([fs.writeFile(large, fullDds(2048, 2048).bytes), fs.writeFile(small, tga(1, 1))]);
    const resources = new ResourceService({ roots: [root] });
    try {
        const active = resources.load(large), queued = resources.load(small);
        for (let attempt = 0; attempt < 10000 && !resources.stats.workerCount; attempt++) await new Promise<void>(resolve => setImmediate(resolve));
        assert.equal(resources.stats.workerCount, 1); resources.clear();
        assert.equal((await active).status, 'cancelled'); assert.equal((await queued).status, 'cancelled');
        await resources.whenIdle();
        assert.equal(resources.stats.pending, 0); assert.equal(resources.stats.workerCount, 0); assert.equal(resources.stats.terminatingWorkers, 0);
        assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.transientBytes, 0); assert.equal(resources.stats.trackedFiles, 0);
        resources.release(ready(await resources.load(small)));
    } finally { resources.dispose(); await resources.whenIdle(); }
}));
