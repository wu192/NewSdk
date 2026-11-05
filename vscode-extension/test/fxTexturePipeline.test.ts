import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { ResourceReady, ResourceService } from '../src/fx/resourceService';
import { prepareTextureState, streamPreviewTextures, UploadedTexture } from '../src/fx/texturePipeline';
import { defaultScene, FxTexture, PreviewIR } from '../src/fx/previewProtocol';

function preview(files: string[]): PreviewIR {
    const textures: FxTexture[] = files.map((file, index) => ({ key: `declaration:${index}`, name: `Texture${index}`, file,
        status: 'pending', source: 'source', location: { uri: path.join(path.dirname(file), 'Effect.xml'), nodePath: `Texture[${index}]`, start: index * 20, end: index * 20 + 10 } }));
    return { schemaVersion: 1, entry: 'Effect.xml', assetKey: 'List', name: 'List', documentUri: 'Effect.xml', documentVersion: 1,
        generation: 1, textures, issues: [], fields: [], dependencies: [], scene: defaultScene,
        systems: textures.map((texture, index) => ({ key: `system:${index}`, name: `System${index}`, templateKey: `template:${index}`,
            source: { uri: 'Effect.xml', nodePath: `ParticleSystem[${index}]`, start: 0, end: 1 }, fields: [], issues: [],
            offset: [0, 0, 0], rotation: 0, startFrame: 0, textureKey: texture.key, triggered: true })) };
}

test('sequential uploads may exceed the CPU LRU, and parameter edits retain GPU metadata without reloading', async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-texture-pipeline-'));
    const resources = new ResourceService({ roots: [root], cacheBytes: 32 * 1024 * 1024 });
    try {
        const files = [0, 1, 2].map(index => path.join(root, `${index}.tga`));
        for (let index = 0; index < files.length; index++) {
            const tga = Buffer.alloc(18 + 2048 * 2048 * 4, 20 + index);
            tga.fill(0, 0, 18); tga[2] = 2; tga.writeUInt16LE(2048, 12); tga.writeUInt16LE(2048, 14); tga[16] = 32; tga[17] = 0x28;
            await fs.writeFile(files[index], tga);
        }
        let loads = 0; let uploads = 0;
        const service = { load: (...args: Parameters<ResourceService['load']>) => { loads++; return resources.load(...args); }, release: resources.release.bind(resources) };
        const uploaded = new Map<string, UploadedTexture>();
        const first = preview(files);
        prepareTextureState(first, uploaded);
        await streamPreviewTextures(first, service, { uploaded, maxBytes: 32 * 1024 * 1024, current: () => true,
            publish: async (_previous, texture, issue) => { assert.equal(issue, undefined); assert.equal(texture.status, 'ready'); assert.equal(texture.pixels, undefined); },
            transfer: async (_key, handle) => { uploads++; assert.equal(resources.stats.references, 1); assert.equal(handle.texture.pixels.length, 2048 * 2048 * 4); return true; } });
        assert.equal(loads, 3); assert.equal(uploads, 3); assert.equal(uploaded.size, 3);
        assert.ok(resources.stats.bytes <= 32 * 1024 * 1024); assert.equal(resources.stats.references, 0);
        assert.equal(resources.stats.entries, 2, 'first texture was evicted from the CPU LRU');
        const second = preview(files); second.documentVersion = 2;
        prepareTextureState(second, uploaded);
        await streamPreviewTextures(second, service, { uploaded, maxBytes: 32 * 1024 * 1024, current: () => true,
            publish: async () => assert.fail('unchanged textures do not need metadata messages'),
            transfer: async () => { assert.fail('unchanged textures must not be uploaded'); return false; } });
        assert.equal(loads, 3, 'parameter edits must not decode an evicted CPU texture still on the GPU');
        assert.deepEqual(second.systems.map(system => system.textureKey), first.systems.map(system => system.textureKey));
        const third = preview([files[2]]); prepareTextureState(third, uploaded);
        assert.equal(uploaded.size, 1, 'inactive metadata is pruned when selecting another effect');
    } finally { resources.dispose(); await fs.rm(root, { recursive: true, force: true }); }
});

test('resource failures publish exact source diagnostics', async () => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-texture-problems-'));
    const resources = new ResourceService({ roots: [root] });
    try {
        const ir = preview([path.join(root, 'missing.tga')]); const uploaded = new Map<string, UploadedTexture>();
        await streamPreviewTextures(ir, resources, { uploaded, maxBytes: 32 * 1024 * 1024, current: () => true,
            publish: async (_previous, texture, issue) => { assert.equal(texture.status, 'missing'); assert.equal(issue?.code, 'texture-resource'); assert.equal(issue?.start, 0); },
            transfer: async () => { assert.fail('missing resources are not transferred'); return false; } });
        assert.equal(ir.issues.length, 1); assert.equal(uploaded.size, 0); assert.equal(resources.stats.references, 0);
    } finally { resources.dispose(); await fs.rm(root, { recursive: true, force: true }); }
});

test('cancellation between metadata and upload releases the acquired image', async () => {
    const pixels = new Uint8Array([1, 2, 3, 4]); const abort = new AbortController();
    const handle: ResourceReady = { status: 'ready', key: 'generation:1', path: '/Mod/Texture.tga', generation: 1, source: 'source-texture',
        texture: { width: 1, height: 1, pixels, mips: [{ width: 1, height: 1, pixels }], format: 'TGA', byteLength: 4, source: 'source-texture' } };
    let released = 0; const uploaded = new Map<string, UploadedTexture>();
    await streamPreviewTextures(preview([handle.path]), { load: async () => handle, release: () => { released++; } },
        { uploaded, maxBytes: 100, signal: abort.signal, current: () => true, publish: async () => { abort.abort(); },
            transfer: async () => { assert.fail('superseded data must not reach the GPU'); return false; } });
    assert.equal(released, 1); assert.equal(uploaded.size, 0);
});
