import { test } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { AssetIndex } from '../src/fx/assetIndex';
import { AssetResolver } from '../src/fx/assetResolver';
import { compileParticle, ParticleWorld, type FxNode, type ParticleConfig, type Range, type WorldEventList, type WorldSystem } from '../webview/fx/runtime';
const rv = (low: number, high = low): Range => ({ low, high, type: low === high ? 'CONSTANT' : 'UNIFORM' });
const node = (name: string, attributes: Record<string, string> = {}, children: FxNode[] = []): FxNode => ({ name, attributes, children });
function config(cpu = true, life = 6): ParticleConfig {
    const result = compileParticle(node('FXParticleSystemTemplate', { Type: cpu ? 'PARTICLE' : 'GPU_PARTICLE', SystemLifetime: '1' }, [
        node('Lifetime', { Low: String(life), High: String(life) }), node('BurstCount', { Low: '1', High: '1' }),
        node('Physics', {}, [node('Default')]), node('Draw', {}, [node(cpu ? 'Default' : 'Gpu', cpu ? {} : { EnableSoftParticles: 'false' })]),
        node('Volume', {}, [node('Point')]), node('Velocity', {}, [node('Ortho', {}, [node('Position', {}, [node('x', { Low: '1', High: '1' })])])]),
        node('Update', {}, [node('Default')]),
    ])); assert.ok(result.config, JSON.stringify(result.issues)); return result.config;
}
function list(systems: WorldSystem[], extra: Partial<WorldEventList> = {}): WorldEventList {
    return { key: 'event', systems, cullTrackingFrames: 0, cullTrackingMin: 0, cullTrackingMax: 0, triggered: true, ...extra };
}
const state = (world: ParticleWorld): string => JSON.stringify({ frame: world.frame, handles: [...world.attachmentHandles],
    systems: world.systems.map(s => ({ id: s.id, parent: s.parent, sourceId: s.sourceId, depth: s.depth, state: s.runtime.captureState() })) });

test('EventFX Life creates independent systems at current position through a nonrecursive manager boundary', () => {
    const parent = config(true, 9); parent.events = [{ type: 'Life', fx: 'event', perParticle: true, kill: true, eventTime: rv(1) }];
    const child = config(false, 4);
    const world = new ParticleWorld([{ id: 'parent', config: parent }], 1, 100, 100, {
        fxLists: [list([{ id: 'call', sourceId: 'event-call', templateKey: 'child', config: child, options: { offset: [10, 0, 0] } }])],
    });
    world.seek(1); assert.equal(world.systems.length, 2); assert.equal(world.systems[0].runtime.particles.length, 0);
    const queued = world.systems[1]; assert.deepEqual(queued.runtime.options.offset, [12, 0, 0]); assert.equal(queued.runtime.particles.length, 0);
    assert.equal(queued.parent, undefined); assert.equal(queued.sourceId, 'event-call');
    world.seek(2); assert.equal(queued.runtime.particles.length, 1); assert.equal(queued.runtime.particles[0].creationFrame, 2);
    world.seek(5); assert.equal(world.systems.length, 2); world.seek(7); assert.equal(world.systems.length, 1);
});

test('CPU attached parent death immediately unbinds and stops births while child stock drains', () => {
    const parent = config(true, 3); parent.attachedSystem = 'child';
    const child = config(false, 4); child.systemLifetime = 0;
    const world = new ParticleWorld([{ id: 'parent', config: parent }], 1, 100, 100, {
        templates: [{ id: 'child-def', templateKey: 'child', config: child }],
    });
    world.seek(0); assert.equal(world.systems.length, 2); assert.equal(world.systems[1].runtime.particles.length, 0);
    world.seek(1); const attached = world.systems[1]; assert.deepEqual(attached.runtime.particles[0].position, [2, 0, 0]);
    world.seek(2); assert.equal(attached.parent, undefined); assert.equal(attached.runtime.emissionStopped, true);
    assert.equal(attached.runtime.particles.length, 1); assert.equal(world.attachmentHandles.size, 0);
    world.seek(5); assert.equal(world.systems.length, 2); world.seek(6); assert.equal(world.systems.length, 1);
});

test('attached child death invalidates its parent handle before parent cleanup', () => {
    const parent = config(true, 10); parent.attachedSystem = 'child';
    const world = new ParticleWorld([{ id: 'parent', config: parent }], 1, 100, 100, {
        templates: [{ id: 'child', config: config(true, 1) }],
    });
    world.seek(1); assert.equal(world.systems.length, 1); assert.deepEqual([...world.attachmentHandles.values()], [null]);
    world.seek(9); assert.equal(world.attachmentHandles.size, 0);
});

test('Collision checks flat ground before applying HeightOffset to event point and optional velocity heading', () => {
    const parent = config(true, 3); parent.events = [{ type: 'Collision', fx: 'event', perParticle: true, kill: true, heightOffset: rv(5), orientToTerrain: true }];
    parent.velocity.x = rv(0); parent.velocity.y = rv(1); parent.velocity.z = rv(-1);
    const world = new ParticleWorld([{ id: 'parent', config: parent, options: { offset: [0, 0, 1] } }], 1, 100, 100, {
        groundHeight: 0, fxLists: [list([{ id: 'call', config: config(false), options: { offset: [1, 0, 0] } }])],
    });
    world.seek(0); const point = world.systems[1].runtime.options.offset;
    assert.ok(Math.abs(point[0]) < 1e-10); assert.equal(point[1], 2); assert.equal(point[2], 5);
});

test('CullTracking registers permitted attempts before visibility and uses integer quotient, including degenerate RNG draws', () => {
    const c = config(true, 3); c.burstCount = rv(5); c.events = [{ type: 'Life', fx: 'event', perParticle: true, kill: false, eventTime: rv(0) }];
    const world = new ParticleWorld([{ id: 'parent', config: c }], 1, 100, 100, {
        fxLists: [list([], { triggered: false, cullTrackingFrames: 100, cullTrackingMin: 1, cullTrackingMax: 4 })],
    });
    world.seek(0); assert.equal(world.systems.length, 1); assert.equal(world.systems[0].runtime.random.calls, 3);
    // First two allowed attempts registered despite no dispatch; n=2 yields q=0 for the last three attempts.
    const replay = state(world); world.seek(1); world.seek(0); assert.equal(state(world), replay);
});

test('recursive local events stop at finite depth, event attempts and system allocation budgets', () => {
    const c = config(true, 30); c.events = [{ type: 'Life', fx: 'event', perParticle: true, kill: false, eventTime: rv(0) }];
    const graph = [list([{ id: 'recursive', config: c }])];
    const depth = new ParticleWorld([{ id: 'root', config: c }], 1, 100, 100, { fxLists: graph, maxDepth: 2 });
    depth.seek(100); assert.match(depth.budgetExceeded!, /层级/); assert.equal(depth.frame, 2); assert.ok(depth.systems.length <= 3);
    c.burstCount = rv(5);
    const events = new ParticleWorld([{ id: 'root', config: c }], 1, 100, 100, { fxLists: graph, maxEventsPerFrame: 2 });
    events.seek(10); assert.match(events.budgetExceeded!, /EventFX/); assert.ok(events.pendingCount <= 2);
    const systems = new ParticleWorld([{ id: 'root', config: c }], 1, 100, 100, { fxLists: graph, maxSystems: 2 });
    systems.seek(10); assert.match(systems.budgetExceeded!, /实例/); assert.ok(systems.systems.length + systems.pendingCount <= 2);
});

test('whole-world snapshots restore shared RNG, dynamic graph, cull records, particle state and attachment handles', async () => {
    const root = config(true, 90); root.attachedSystem = 'child'; root.size = rv(1, 2);
    root.events = [{ type: 'Life', fx: 'event', perParticle: true, kill: false, eventTime: rv(3, 5) }];
    const child = config(false, 6); child.systemLifetime = 0;
    const world = new ParticleWorld([{ id: 'root', config: root }], 7, 100, 500, {
        templates: [{ id: 'child', config: child }], fxLists: [list([{ id: 'call', config: config(false, 15) }],
            { cullTrackingFrames: 10, cullTrackingMin: 2, cullTrackingMax: 4 })], snapshotBytes: 40_000,
    });
    world.seek(80); const expected = state(world); assert.ok(world.snapshotCount > 1); assert.ok(world.snapshotBytes <= 40_000);
    world.seek(40); await world.seekAsync(80); assert.equal(state(world), expected);
    assert.equal(world.systems[0].runtime.random, world.systems[1].runtime.random);
    const abort = new AbortController(); abort.abort(); await assert.rejects(world.seekAsync(20, abort.signal), /取消/); assert.equal(state(world), expected);
    world.dispose(); assert.equal(world.snapshotBytes, 0); assert.equal(world.attachmentHandles.size, 0); assert.equal(world.pendingCount, 0);
});

test('a newer seek cancels an in-flight replay at the next yield', async () => {
    const c = config(true, 100); c.systemLifetime = 0;
    const world = new ParticleWorld([{ id: 'root', config: c }], 1, 1000, 18_000, { snapshotBytes: 32_000 });
    const pending = world.seekAsync(10_000); world.seek(2); await assert.rejects(pending, /取消/); assert.equal(world.frame, 2);
});

test('second-round CPU/geometry/EventFX/attached fixture graph compiles and runs through the same scheduler', async () => {
    const fixture = path.resolve(__dirname, '../../test/fixtures/fx');
    const index = new AssetIndex({ roots: [fixture] });
    try {
        await index.load(path.join(fixture, 'Data/Round2Mod.xml'));
        const selected = index.listAssets().find(a => a.id === 'Round2Acceptance')!; assert.ok(selected);
        const ir = await new AssetResolver(index).resolve(selected.key);
        assert.ok(!ir.issues.some(i => i.severity === 'error'), JSON.stringify(ir.issues));
        const definition = (s: typeof ir.systems[number]): WorldSystem => ({ id: s.key, sourceId: s.key, templateKey: s.templateKey, config: s.config!,
            options: { offset: s.offset, rotationXYZ: s.rotationXYZ, rotationMatrix: s.rotationMatrix, startFrame: s.startFrame,
                systemLifeFrames: s.systemLifeFrames, initialDelayOverrideMs: s.initialDelayOverrideMs, createAtGroundHeight: s.createAtGroundHeight } });
        const world = new ParticleWorld(ir.systems.filter(s => s.triggered && s.config).map(definition), 1, 1000, 200,
            { ...ir.dynamic, templates: ir.dynamic!.templates.filter(s => s.config).map(definition),
                fxLists: ir.dynamic!.fxLists.map(l => ({ ...l, systems: l.systems.filter(s => s.config && s.triggered).map(definition) })) });
        world.seek(12); assert.equal(world.budgetExceeded, undefined); assert.ok(world.systems.some(s => s.dynamic));
        const expected = state(world); world.seek(0); world.seek(12); assert.equal(state(world), expected); world.dispose();
    } finally { index.dispose(); }
});

test('attached translation interpolates later birth candidates after first-candidate history reset', () => {
    const parent = config(true, 6); parent.attachedSystem = 'child';
    const child = config(false, 20); child.systemLifetime = 0; child.burstCount = rv(2);
    const world = new ParticleWorld([{ id: 'parent', config: parent }], 1, 100, 100, { templates: [{ id: 'child', config: child }] });
    world.seek(2); const births = world.systems[1].runtime.particles;
    assert.deepEqual(births.filter(b => b.creationFrame === 1).map(b => b.position[0]), [2, 2]);
    assert.deepEqual(births.filter(b => b.creationFrame === 2).map(b => b.position[0]), [2, 2.5]);
});

test('CPU nested curve states count against a separate whole-world memory budget before allocation', () => {
    const c = config(true, 30); c.burstCount = rv(100);
    c.colors = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, value: [1, 1, 1] }));
    c.alphas = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, range: rv(1) }));
    const world = new ParticleWorld([{ id: 'root', config: c }], 1, 1000, 100, { maxStateBytes: 32_000, snapshotBytes: 16_000 });
    world.seek(0); assert.match(world.budgetExceeded!, /预算/); assert.equal(world.stats.particles, 0);
    assert.ok(world.stats.stateBytes <= 32_000); assert.ok(world.stats.snapshotBytes <= 16_000);
    const small = new ParticleWorld([{ id: 'root', config: { ...c, burstCount: rv(2) } }], 1, 1000, 100,
        { maxStateBytes: 32_000, snapshotBytes: 16_000 });
    small.seek(0); assert.equal(small.stats.particleBytes, 2 * (640 + 1152 + 1024)); assert.equal(small.stats.systems, 1);
    small.dispose(); assert.deepEqual(small.stats, { particles: 0, particleBytes: 0, stateBytes: 0, snapshotBytes: 0,
        systems: 0, pending: 0, attachmentHandles: 0, cullRecords: 0 });
});

test('system Life events run after emission, reset age at InitialDelay expiry, and Kill writes remaining without changing infinite flag', () => {
    const finite = config(true, 30); finite.systemLifetime = 20; finite.initialDelay = rv(2);
    finite.events = [{ type: 'Life', fx: 'event', perParticle: false, kill: true, eventTime: rv(1) }];
    const world = new ParticleWorld([{ id: 'root', config: finite }], 1, 100, 100, { fxLists: [list([])] });
    world.seek(2); assert.equal(world.systems[0].runtime.particles.length, 1); assert.equal(world.systems[0].runtime.events.length, 1);
    world.seek(3); assert.equal(world.systems[0].runtime.particles.length, 1);
    const infinite = { ...finite, systemLifetime: 0, initialDelay: rv(0) };
    const forever = new ParticleWorld([{ id: 'root', config: infinite }], 1, 100, 100, { fxLists: [list([])] });
    forever.seek(3); assert.equal(forever.systems[0].runtime.particles.length, 4); assert.equal(forever.systems[0].runtime.emissionStopped, false);
});
