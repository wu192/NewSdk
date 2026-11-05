import { test, type TestContext } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { AssetIndex } from '../src/fx/assetIndex';
import { AssetResolver } from '../src/fx/assetResolver';
import { applyTextPatches, prepareFieldTransaction } from '../src/fx/editTransaction';
import { ParticleWorld, type WorldSystem } from '../webview/fx/runtime';
import type { FxSystem, PreviewIR } from '../src/fx/previewProtocol';

const fixtureRoot = path.resolve(__dirname, '../../test/fixtures/fx');
const ids = ['SpecialTrail', 'SpecialAttractedTrail', 'SpecialLightning', 'SpecialSwarm'];
function definition(system: FxSystem): WorldSystem {
    return { id: system.key, config: system.config!, options: { offset: system.offset, rotationMatrix: system.rotationMatrix } };
}
async function fixture(t: TestContext, writable = false): Promise<{index: AssetIndex; file: string}> {
    let root = fixtureRoot;
    if (writable) {
        root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-special-')));
        t.after(() => fs.rm(root, { recursive: true, force: true }));
        await fs.mkdir(path.join(root, 'Data'));
        await Promise.all(['special.xml', 'texture.xml', 'particle.tga'].map(file => fs.copyFile(path.join(fixtureRoot, 'Data', file), path.join(root, 'Data', file))));
    }
    const index = new AssetIndex({ roots: [root], modRoot: root });
    t.after(() => index.dispose());
    const file = path.join(root, 'Data', 'special.xml'); await index.load(file);
    return { index, file };
}
async function resolve(index: AssetIndex, id: string): Promise<PreviewIR> {
    const asset = index.listAssets().find(asset => asset.id === id); assert.ok(asset, id);
    const ir = await new AssetResolver(index).resolve(asset.key, { source: [0, 0, 3], seed: 41 });
    assert.equal(ir.issues.filter(issue => issue.severity === 'error').length, 0, `${id}: ${JSON.stringify(ir.issues)}`);
    assert.ok(ir.systems[0]?.config, `${id}: ${JSON.stringify(ir.issues)}`);
    return ir;
}
function state(world: ParticleWorld): string {
    return JSON.stringify(world.systems.map(system => ({ id: system.id, state: system.runtime.captureState() })));
}

test('special fixtures resolve modular SDK combinations, source fields, texture references and explicit reconstruction warnings', async t => {
    const { index } = await fixture(t);
    for (const [id, draw] of ids.map((id, index) => [id, ['Trail', 'Trail', 'Lightning', 'Swarm'][index]])) {
        const ir = await resolve(index, id), system = ir.systems[0];
        assert.equal(system.config!.drawModule, draw);
        assert.ok(system.fields.some(field => field.nodePath.includes(`/Draw[1]/${draw}[1]`)), `${id}: missing editable Draw parameters`);
        assert.ok(ir.textures.some(texture => texture.file?.endsWith('particle.tga')));
        assert.ok(ir.issues.some(issue => issue.severity === 'warning'), `${id}: reconstructed preview must disclose its scope`);
    }
    const attracted = await resolve(index, 'SpecialAttractedTrail');
    assert.equal(attracted.systems[0].config!.swarmPhysics?.attractStrength, .04);
    const lightning = await resolve(index, 'SpecialLightning');
    assert.equal(lightning.systems[0].config!.family, 'STREAK');
    assert.equal(lightning.systems[0].config!.volume.type, 'Lightning');
    assert.equal(lightning.systems[0].config!.alphaTest, true);
    const swarm = await resolve(index, 'SpecialSwarm');
    assert.equal(swarm.systems[0].config!.swarm?.environmentTexture, 'FxFixtureTexture');
});

test('special parameter edits use the existing atomic XML transaction and survive disk save/reload', async t => {
    const { index, file } = await fixture(t, true);
    const edits = [
        { id: 'SpecialTrail', module: '/Draw[1]/Trail[1]', attribute: 'TrailLife', value: '6', expected: (ir: PreviewIR) => ir.systems[0].config!.trail!.life === 6 },
        { id: 'SpecialAttractedTrail', module: '/Physics[1]/Swarm[1]', attribute: 'AttractStrength', value: '0.2', expected: (ir: PreviewIR) => ir.systems[0].config!.swarmPhysics!.attractStrength === .2 },
        { id: 'SpecialLightning', module: '/Draw[1]/Lightning[1]/EndPoint[1]', attribute: 'x', value: '40', expected: (ir: PreviewIR) => ir.systems[0].config!.lightning!.end[0] === 40 },
        { id: 'SpecialSwarm', module: '/Draw[1]/Swarm[1]', attribute: 'SpeedStretchAmount', value: '4', expected: (ir: PreviewIR) => ir.systems[0].config!.swarm!.speedStretchAmount === 4 },
    ];
    for (const edit of edits) {
        const ir = await resolve(index, edit.id), field = ir.systems[0].fields.find(field => field.nodePath.includes(edit.module) && field.attribute === edit.attribute);
        assert.ok(field, JSON.stringify(edit));
        const document = index.getDocument(field.uri)!;
        const patches = prepareFieldTransaction(document, [{ nodePath: field.nodePath, attribute: field.attribute, expectedValue: field.defaulted ? undefined : field.value, value: edit.value, documentVersion: document.version }]);
        assert.equal(patches.length, 1);
        const original = document.text, saved = applyTextPatches(original, patches);
        await fs.writeFile(file, saved); index.invalidate(file); await index.load(file);
        assert.equal(edit.expected(await resolve(index, edit.id)), true, `${edit.id}: saved parameter lost`);
        const inverse = patches.map(patch => ({ start: patch.start, end: patch.start + patch.text.length, text: patch.expected, expected: patch.text }));
        assert.equal(applyTextPatches(saved, inverse), original, 'single transaction remains reversible');
    }
});

test('special worlds reproduce simulated state after snapshot seek and source motion without renderer RNG', async t => {
    const { index } = await fixture(t);
    for (const id of ids) {
        const ir = await resolve(index, id), world = new ParticleWorld(ir.systems.map(definition), ir.scene.seed, 2048, 100, {
            sourceMotion: { playback: 'once', points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 60, offset: [12, 4, 0] }] },
        });
        try {
            world.seek(35); assert.equal(world.budgetExceeded, undefined, id);
            const expected = state(world); assert.ok(world.snapshotCount > 0, id);
            world.seek(31); world.seek(35); assert.equal(state(world), expected, `${id}: snapshot replay`);
            world.seek(4); world.seek(35); assert.equal(state(world), expected, `${id}: full replay`);
            assert.ok(world.stats.stateBytes > world.stats.particles * 192, `${id}: mutable state must count toward World budget`);
            assert.ok(world.stats.stateBytes <= world.scheduling.maxStateBytes);
        } finally { world.dispose(); }
        assert.equal(world.stats.stateBytes, 0); assert.equal(world.snapshotBytes, 0);
    }
});

test('dead Trail keeps its bounded tail while draining, restores it on seek, and eventually releases it', async t => {
    const { index } = await fixture(t), ir = await resolve(index, 'SpecialTrail'), config = ir.systems[0].config!;
    config.systemLifetime = 1; config.lifetime = { low: 4, high: 4, type: 'CONSTANT' }; config.trail!.life = 6;
    const world = new ParticleWorld([{ id: 'tail', config }], 9, 100, 100);
    try {
        world.seek(5); const tail = world.systems[0].runtime.particles[0];
        assert.ok(tail?.trail && tail.trail.length > 1, 'tail must remain visible after logical particle death');
        assert.equal(world.systems[0].runtime.finished, false);
        const expected = state(world), bytes = world.stats.particleBytes;
        world.seek(1); world.seek(5); assert.equal(state(world), expected, 'dead tail must survive replay');
        assert.equal(world.stats.particleBytes, bytes);
        world.seek(12); assert.equal(world.stats.particles, 0); assert.equal(world.stats.particleBytes, 0);
        assert.equal(world.systems[0].runtime.finished, true);
    } finally { world.dispose(); }
});
