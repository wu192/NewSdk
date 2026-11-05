import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { FxCompiledTextureService, FxTextureInputVersion } from '../src/fx/compiledTextureService';
import { defaultScene, FxTexture, PreviewIR } from '../src/fx/previewProtocol';
import { ResourceService } from '../src/fx/resourceService';
import { prepareTextureState, streamPreviewTextures, UploadedTexture } from '../src/fx/texturePipeline';

function dds(): Buffer {
    const bytes = Buffer.alloc(136); bytes.write('DDS ');
    for (const [offset, value] of [[4, 124], [8, 0x1007], [12, 1], [16, 1], [76, 32], [80, 4], [108, 0x1000]]) bytes.writeUInt32LE(value, offset);
    bytes.write('DXT1', 84); bytes.writeUInt16LE(0xf800, 128);
    return bytes;
}
function texture(file: string): FxTexture { return { key: 'Texture:smoke', name: 'smoke', file, source: 'source', status: 'pending' }; }
function preview(value: FxTexture): PreviewIR {
    return { schemaVersion: 1, entry: 'Mod.xml', assetKey: 'FX:test', name: 'test', documentUri: 'Effect.xml', documentVersion: 7,
        generation: 4, systems: [], fields: [], textures: [value], issues: [], dependencies: [], scene: defaultScene };
}
const input: FxTextureInputVersion = { documentUri: 'Effect.xml', documentVersion: 7, savedDocumentVersion: 7, inputGeneration: 4 };
async function fixture(run: (root: string) => Promise<void>): Promise<void> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-compiled-')));
    try { await run(root); } finally { await fs.rm(root, { recursive: true, force: true }); }
}

test('imported DDS provenance never claims current output and detects unsaved versions, saved versions and resource events', async () => fixture(async root => {
    const file = path.join(root, 'source.tga'), compiled = path.join(root, 'result.dds'); await fs.writeFile(compiled, dds());
    const resources = new ResourceService({ roots: [root] }), service = new FxCompiledTextureService(resources);
    try {
        assert.equal((await service.importResult(texture(file), compiled, input)).status, 'imported');
        assert.equal(resources.stats.references, 0, 'import only retains metadata');
        const ir = preview(texture(file)); service.applySelection(ir, input);
        assert.equal(ir.textures[0].source, 'compiled'); assert.equal(ir.textures[0].file, compiled);
        assert.equal(ir.textures[0].sourceFile, file); assert.equal(ir.textures[0].compiled?.state, 'unverified');
        assert.deepEqual(ir.textures[0].compiled?.input, input);
        const newer = { ...input, documentVersion: 8, savedDocumentVersion: undefined }; service.applySelection(ir, newer);
        assert.equal(ir.textures[0].compiled?.state, 'stale'); assert.match(ir.textures[0].compiled!.message, /旧结果/);
        service.applySelection(ir, { ...input, inputGeneration: 5 }); assert.equal(ir.textures[0].compiled?.state, 'stale');
        service.applySelection(ir, { ...input, savedDocumentVersion: undefined }); assert.equal(ir.textures[0].compiled?.state, 'stale');
        service.applySelection(ir, input); assert.equal(ir.textures[0].compiled?.state, 'unverified', 'returning to import stamp still cannot prove compilation inputs');
        assert.ok(service.select(ir.textures[0], 'source')); service.applySelection(ir, input);
        assert.equal(ir.textures[0].source, 'source'); assert.equal(ir.textures[0].file, file);
        assert.ok(service.select(ir.textures[0], 'compiled')); service.applySelection(ir, input);
        assert.equal(ir.textures[0].source, 'compiled');
        service.clear(); service.applySelection(ir, input); assert.equal(ir.textures[0].source, 'source');
        assert.equal(ir.textures[0].compiled, undefined); assert.equal(service.stats.entries, 0);
    } finally { service.dispose(); resources.dispose(); }
}));

test('source and compiled selections upload distinct images without losing provenance or retaining decoded references', async () => fixture(async root => {
    const source = path.join(root, 'source.tga'), compiled = path.join(root, 'result.dds');
    const tga = Buffer.alloc(21); tga[2] = 2; tga.writeUInt16LE(1, 12); tga.writeUInt16LE(1, 14); tga[16] = 24; tga[17] = 0x20; tga[19] = 255;
    await Promise.all([fs.writeFile(source, tga), fs.writeFile(compiled, dds())]);
    const resources = new ResourceService({ roots: [root], cacheBytes: 16 }), service = new FxCompiledTextureService(resources);
    const uploaded = new Map<string, UploadedTexture>(), uploads: string[] = [];
    const show = async (): Promise<PreviewIR> => {
        const ir = preview(texture(source)); service.applySelection(ir, input); prepareTextureState(ir, uploaded);
        await streamPreviewTextures(ir, resources, { uploaded, maxBytes: 16, current: () => true, publish: async () => undefined,
            transfer: async (key, loaded) => { uploads.push(key); assert.equal(loaded.texture.pixels[3], 255); return true; } });
        assert.equal(resources.stats.references, 0); return ir;
    };
    try {
        const original = await show(); assert.equal(original.textures[0].source, 'source');
        assert.equal((await service.importResult(texture(source), compiled, input)).status, 'imported');
        const result = await show(); assert.equal(result.textures[0].compiled?.state, 'unverified');
        assert.notEqual(uploads[0], uploads[1]); assert.equal(uploaded.size, 1);
        service.select(result.textures[0], 'source'); await show(); assert.equal(uploads.length, 3);
        assert.equal(uploaded.size, 1); assert.ok(resources.stats.bytes <= 16);
    } finally { service.dispose(); resources.dispose(); }
}));

test('DDS import rejects corrupt, source-disguised and escaped files and never writes to MOD or SDK roots', async () => fixture(async root => {
    const mod = path.join(root, 'mod'), sdk = path.join(root, 'sdk'), outside = path.join(root, 'outside');
    await Promise.all([fs.mkdir(mod), fs.mkdir(sdk), fs.mkdir(outside)]);
    const valid = path.join(sdk, 'ready.dds'); await fs.writeFile(valid, dds());
    await fs.writeFile(path.join(mod, 'broken.dds'), Buffer.from('DDS '));
    const tga = Buffer.alloc(21); tga[2] = 2; tga.writeUInt16LE(1, 12); tga.writeUInt16LE(1, 14); tga[16] = 24;
    await fs.writeFile(path.join(mod, 'fake.dds'), tga); await fs.writeFile(path.join(outside, 'escape.dds'), dds());
    const alias = path.join(mod, 'alias'); await fs.symlink(outside, alias, process.platform === 'win32' ? 'junction' : 'dir');
    const before = await Promise.all([fs.readdir(mod), fs.readdir(sdk)]);
    const resources = new ResourceService({ roots: [mod, sdk] }), service = new FxCompiledTextureService(resources);
    try {
        const value = texture(path.join(mod, 'source.tga'));
        assert.equal((await service.importResult(value, valid, input)).status, 'imported');
        assert.equal((await service.importResult(value, path.join(mod, 'broken.dds'), input)).status, 'decode-error');
        assert.equal((await service.importResult(value, path.join(mod, 'fake.dds'), input)).status, 'unsupported');
        assert.equal((await service.importResult(value, path.join(alias, 'escape.dds'), input)).status, 'outside-roots');
        assert.equal((await service.importResult(value, path.join(mod, 'missing.dds'), input)).status, 'missing');
        const ir = preview(value); service.applySelection(ir, input); assert.equal(ir.textures[0].file, valid, 'bad imports preserve the previous usable selection');
        assert.equal(resources.stats.references, 0); assert.equal(service.stats.pending, 0);
        assert.deepEqual(await Promise.all([fs.readdir(mod), fs.readdir(sdk)]), before);
        assert.deepEqual(await fs.readFile(valid), dds());
    } finally { service.dispose(); resources.dispose(); }
}));

test('100 import and selection cycles bound provenance metadata; clearing cancels queued imports', async () => fixture(async root => {
    const file = path.join(root, 'result.dds'); await fs.writeFile(file, dds());
    const resources = new ResourceService({ roots: [root], cacheEntries: 2 }), service = new FxCompiledTextureService(resources, { maxEntries: 4 });
    try {
        for (let i = 0; i < 100; i++) {
            const value = texture(path.join(root, `source-${i}.tga`));
            assert.equal((await service.importResult(value, file, { ...input, inputGeneration: i })).status, 'imported');
            assert.ok(service.select(value, i % 2 ? 'source' : 'compiled'));
            const ir = preview(value); service.applySelection(ir, input);
            assert.ok(service.stats.entries <= 4); assert.equal(resources.stats.references, 0);
        }
        const pending = service.importResult(texture(path.join(root, 'pending.tga')), file, input); service.clear();
        assert.equal((await pending).status, 'cancelled'); assert.equal(service.stats.pending, 0); assert.equal(service.stats.entries, 0);
    } finally { service.dispose(); resources.dispose(); }
    assert.equal(resources.stats.bytes, 0); assert.equal(resources.stats.workerCount, 0); assert.equal(service.stats.disposed, true);
}));
