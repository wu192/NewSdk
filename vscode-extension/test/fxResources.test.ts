import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { decodeTexture, TextureDecodeError } from '../src/fx/textureDecoder';
import { ResourceReady, ResourceService } from '../src/fx/resourceService';

function tga(width: number, height: number, payload: number[], type = 2, bits = 24, origin = 0x20): Uint8Array {
    const bytes = new Uint8Array(18 + payload.length), view = new DataView(bytes.buffer);
    bytes[2] = type; bytes[16] = bits; bytes[17] = origin;
    view.setUint16(12, width, true); view.setUint16(14, height, true); bytes.set(payload, 18);
    return bytes;
}
function dds(width: number, height: number, payload: number[], fourCC = 'DXT1', mipCount = 1): Uint8Array {
    const bytes = new Uint8Array(128 + payload.length), view = new DataView(bytes.buffer);
    bytes.set([0x44, 0x44, 0x53, 0x20]);
    for (const [offset, value] of [[4, 124], [8, 0x21007], [12, height], [16, width], [28, mipCount], [76, 32], [80, 4], [108, 0x1000]]) {
        view.setUint32(offset, value, true);
    }
    bytes.set([...fourCC].map(char => char.charCodeAt(0)), 84); bytes.set(payload, 128);
    return bytes;
}
function rawDds(width: number, height: number, payload: number[], bits: number, masks: number[], mipCount = 1, pitch?: number): Uint8Array {
    const bytes = dds(width, height, payload, '\0\0\0\0', mipCount), view = new DataView(bytes.buffer);
    view.setUint32(80, 0x41, true); view.setUint32(88, bits, true);
    masks.forEach((mask, i) => view.setUint32(92 + i * 4, mask, true));
    if (pitch !== undefined) { view.setUint32(8, view.getUint32(8, true) | 8, true); view.setUint32(20, pitch, true); }
    return bytes;
}
function errorCode(bytes: Uint8Array, code: string, options?: Parameters<typeof decodeTexture>[1]): void {
    assert.throws(() => decodeTexture(bytes, options), (error: unknown) => error instanceof TextureDecodeError && error.code === code);
}

test('TGA canonicalizes all four origins and preserves BGRA alpha independently of extension', () => {
    const colors = [[0, 0, 255], [0, 255, 0], [255, 0, 0], [255, 255, 255]];
    const expected = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
    for (const origin of [0, 0x10, 0x20, 0x30]) {
        const indices: number[] = [];
        for (let index = 0; index < 4; index++) {
            const x = origin & 0x10 ? 1 - index % 2 : index % 2;
            const y = origin & 0x20 ? Math.floor(index / 2) : 1 - Math.floor(index / 2);
            indices.push(y * 2 + x);
        }
        assert.deepEqual([...decodeTexture(tga(2, 2, indices.flatMap(i => colors[i]), 2, 24, origin)).pixels], expected);
    }
    assert.deepEqual([...decodeTexture(tga(1, 1, [4, 3, 2, 1], 2, 32)).pixels], [2, 3, 4, 1]);
    assert.deepEqual([...decodeTexture(tga(2, 1, [0, 0x7c, 0xe0, 0x83], 2, 16, 0x21)).pixels], [255, 0, 0, 0, 0, 255, 0, 255]);
    assert.deepEqual([...decodeTexture(tga(1, 1, [0, 0x7c], 2, 15)).pixels], [255, 0, 0, 255]);
});

test('TGA RLE mixed packets, grayscale and grayscale-alpha have exact independent pixel expectations', () => {
    const mixed = tga(3, 1, [0x81, 0, 0, 255, 0, 0, 255, 0], 10);
    assert.deepEqual([...decodeTexture(mixed).pixels], [255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255]);
    assert.deepEqual([...decodeTexture(tga(2, 1, [0x81, 80], 11, 8)).pixels], [80, 80, 80, 255, 80, 80, 80, 255]);
    assert.deepEqual([...decodeTexture(tga(2, 1, [22, 17, 44, 99], 3, 16)).pixels], [22, 22, 22, 17, 44, 44, 44, 99]);
    errorCode(tga(1, 1, [0x81, 0, 0, 255], 10), 'invalid-header');
    errorCode(tga(2, 1, [0x81, 0], 10), 'truncated');
});

test('DDS DXT1 decodes endpoint interpolation and the transparent fourth entry', () => {
    // Red > green: index sequence 0,1,2,3 in the first row.
    const image = decodeTexture(dds(4, 4, [0, 0xf8, 0xe0, 7, 0xe4, 0, 0, 0]));
    assert.deepEqual([...image.pixels.subarray(0, 16)], [255, 0, 0, 255, 0, 255, 0, 255, 170, 85, 0, 255, 85, 170, 0, 255]);
    assert.equal(image.compressedMips?.[0].bytes.length, 8);
    const transparent = decodeTexture(dds(1, 1, [0, 0, 255, 255, 3, 0, 0, 0]));
    assert.deepEqual([...transparent.pixels], [0, 0, 0, 0]);
    assert.equal(decodeTexture(dds(1, 1, [0, 0, 255, 255, 3, 0, 0, 0]), { keepCompressed: false }).compressedMips, undefined);
});

test('DDS DXT3 alpha nibbles and both DXT5 alpha modes retain exact coverage', () => {
    const color = [0, 0xf8, 0, 0, 0, 0, 0, 0];
    const dxt3 = decodeTexture(dds(4, 4, [0xf0, 0x81, 0, 0, 0, 0, 0, 0, ...color], 'DXT3'));
    assert.deepEqual([dxt3.pixels[3], dxt3.pixels[7], dxt3.pixels[11], dxt3.pixels[15]], [0, 255, 17, 136]);
    // index 6 then 7, with alpha0 <= alpha1: fixed 0 and 255 entries.
    const dxt5 = decodeTexture(dds(4, 4, [10, 20, 0x3e, 0, 0, 0, 0, 0, ...color], 'DXT5'));
    assert.deepEqual([dxt5.pixels[3], dxt5.pixels[7]], [0, 255]);
    const interpolated = decodeTexture(dds(4, 4, [255, 0, 2, 0, 0, 0, 0, 0, ...color], 'DXT5'));
    assert.equal(interpolated.pixels[3], 218);
});

test('DDS RGB masks, padded rows, 565 and mip chains decode without assuming RGBA byte order', () => {
    const rgba = decodeTexture(rawDds(1, 1, [10, 20, 30, 40], 32, [0xff0000, 0xff00, 0xff, 0xff000000]));
    assert.deepEqual([...rgba.pixels], [30, 20, 10, 40]);
    const rgb565 = decodeTexture(rawDds(1, 1, [0xe0, 7], 16, [0xf800, 0x7e0, 31, 0]));
    assert.deepEqual([...rgb565.pixels], [0, 255, 0, 255]);
    const padded = decodeTexture(rawDds(1, 2, [1, 2, 3, 99, 4, 5, 6, 99], 24, [0xff, 0xff00, 0xff0000, 0], 1, 4));
    assert.deepEqual([...padded.pixels], [1, 2, 3, 255, 4, 5, 6, 255]);
});

test('DDS mip preflight validates every level and total allocation before decoding', () => {
    const complete = rawDds(1, 2, [1, 2, 3, 99, 4, 5, 6, 99, 7, 8, 9, 99], 24, [0xff, 0xff00, 0xff0000, 0], 2, 4);
    const image = decodeTexture(complete);
    assert.equal(image.mips.length, 2);
    assert.deepEqual([...image.pixels], [1, 2, 3, 255, 4, 5, 6, 255]);
    assert.deepEqual([...image.mips[1].pixels], [7, 8, 9, 255]);
    errorCode(complete.subarray(0, complete.length - 1), 'truncated');
    errorCode(complete, 'budget', { maxBytes: 8 });
    const blocks = dds(4, 4, [0, 0xf8, 0, 0, 0, 0, 0, 0, 0xe0, 7, 0, 0, 0, 0, 0, 0], 'DXT1', 2);
    assert.deepEqual(decodeTexture(blocks).mips.map(mip => [mip.width, mip.height]), [[4, 4], [2, 2]]);
});

test('texture headers reject distinct unsupported families, corrupt masks, truncated blocks and oversized images', () => {
    errorCode(dds(4, 4, [], 'DX10'), 'unsupported');
    errorCode(dds(4, 4, [], 'ATI2'), 'unsupported');
    for (const [caps2, expected] of [[0x200, /Cube/], [0x200000, /Volume/]] as const) {
        const bytes = dds(4, 4, []); new DataView(bytes.buffer).setUint32(112, caps2, true);
        assert.throws(() => decodeTexture(bytes), expected);
    }
    errorCode(rawDds(1, 1, [0, 0, 0, 0], 32, [0xff, 0xff, 0xff0000, 0]), 'invalid-header');
    errorCode(dds(4, 4, [0]), 'truncated');
    errorCode(tga(16384, 16384, []), 'budget');
    errorCode(new Uint8Array(4), 'truncated');
    errorCode(tga(1, 1, [0], 1, 8), 'unsupported');
});

async function fixture(run: (root: string) => Promise<void>): Promise<void> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-resource-')));
    try { await run(root); } finally { await fs.rm(root, { recursive: true, force: true }); }
}
function ready(result: Awaited<ReturnType<ResourceService['load']>>): ResourceReady {
    assert.equal(result.status, 'ready', JSON.stringify(result));
    return result as ResourceReady;
}

test('resource cache generations invalidate same-size/same-mtime updates and retain active old references safely', async () => fixture(async root => {
    const file = path.join(root, 'named.dds'), image = tga(1, 1, [0, 0, 255]);
    await fs.writeFile(file, image);
    const stat = await fs.stat(file), service = new ResourceService({ roots: [root], cacheBytes: 8 });
    try {
        const first = ready(await service.load(file)), duplicate = ready(await service.load(file));
        assert.equal(first.texture, duplicate.texture); assert.equal(service.stats.references, 2);
        service.release(duplicate);
        await fs.writeFile(file, tga(1, 1, [0, 255, 0])); await fs.utimes(file, stat.atime, stat.mtime);
        service.invalidate(file);
        const next = ready(await service.load(file));
        assert.notEqual(next.key, first.key); assert.equal(next.generation, 1);
        assert.deepEqual([...first.texture.pixels], [255, 0, 0, 255]);
        assert.deepEqual([...next.texture.pixels], [0, 255, 0, 255]);
        assert.equal(service.stats.bytes, 8);
        service.release(first); assert.equal(service.stats.bytes, 4);
        service.release(next); service.clear(); assert.equal(service.stats.entries, 0);
    } finally { service.dispose(); }
    assert.equal(service.stats.bytes, 0); assert.equal(service.stats.references, 0);
}));

test('resource LRU evicts only unreferenced pixels and reports budgets rather than oversubscribing', async () => fixture(async root => {
    const a = path.join(root, 'a'), b = path.join(root, 'b');
    await Promise.all([fs.writeFile(a, tga(1, 1, [1, 2, 3])), fs.writeFile(b, tga(1, 1, [4, 5, 6]))]);
    const service = new ResourceService({ roots: [root], cacheBytes: 4 });
    try {
        const first = ready(await service.load(a));
        assert.equal((await service.load(b)).status, 'budget');
        service.release(first);
        const second = ready(await service.load(b));
        assert.equal(service.stats.entries, 1); assert.equal(service.stats.bytes, 4);
        service.release(second);
    } finally { service.dispose(); }
}));

test('the real decode worker transfers pixels, cancels active work and is terminated on clear/dispose', async () => fixture(async root => {
    const large = path.join(root, 'large'), small = path.join(root, 'small');
    const packets: number[] = [];
    for (let i = 0; i < 2048 * 2048 / 128; i++) packets.push(255, 99);
    await Promise.all([fs.writeFile(large, tga(2048, 2048, packets, 11, 8)), fs.writeFile(small, tga(1, 1, [1, 2, 3]))]);
    const service = new ResourceService({ roots: [root] }), abort = new AbortController();
    try {
        assert.equal(service.stats.workerCount, 0, 'worker is lazy');
        const pending = service.load(large, { signal: abort.signal });
        for (let i = 0; i < 10000 && !service.stats.workerCount; i++) await new Promise<void>(resolve => setImmediate(resolve));
        assert.equal(service.stats.workerCount, 1);
        abort.abort();
        assert.equal((await pending).status, 'cancelled'); assert.equal(service.stats.workerCount, 0);
        const recovered = ready(await service.load(small));
        assert.deepEqual([...recovered.texture.pixels], [3, 2, 1, 255]); assert.equal(service.stats.workerStarts, 2);
        service.release(recovered); service.clear();
        assert.equal(service.stats.workerCount, 0); assert.equal(service.stats.bytes, 0);
    } finally { service.dispose(); }
    assert.equal(service.stats.pending, 0); assert.equal(service.stats.workerCount, 0);
}));

test('resource roots reject traversal and symlink escapes, while missing/empty/cancelled states stay distinct', async () => fixture(async root => {
    const allowed = path.join(root, 'allowed'), outside = path.join(root, 'outside');
    await Promise.all([fs.mkdir(allowed), fs.mkdir(outside)]);
    const outsideFile = path.join(outside, 'image'); await fs.writeFile(outsideFile, tga(1, 1, [1, 2, 3]));
    const alias = path.join(allowed, 'alias'); await fs.symlink(outside, alias, process.platform === 'win32' ? 'junction' : 'dir');
    await fs.writeFile(path.join(allowed, 'empty'), '');
    const service = new ResourceService({ roots: [allowed], maxPending: 1 });
    try {
        assert.equal((await service.load(outsideFile)).status, 'outside-roots');
        assert.equal((await service.load(path.join(alias, 'image'))).status, 'outside-roots');
        assert.equal((await service.load('../outside/image')).status, 'outside-roots');
        assert.equal((await service.load(path.join(allowed, 'missing'))).status, 'missing');
        assert.equal((await service.load(path.join(allowed, 'empty'))).status, 'empty');
        const abort = new AbortController(); abort.abort();
        assert.equal((await service.load(path.join(allowed, 'empty'), { signal: abort.signal })).status, 'cancelled');
        const pending = service.load(path.join(allowed, 'empty'));
        assert.equal((await service.load(path.join(allowed, 'empty'))).status, 'budget'); await pending;
    } finally { service.dispose(); }
    assert.equal((await service.load(outsideFile)).status, 'cancelled');
}));
