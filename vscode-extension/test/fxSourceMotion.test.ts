import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compileParticle, integrateDamping, parseSourceMotion, ParticleWorld, sampleSourceMotion, type FxNode, type ParticleConfig,
    type Range, type SourceMotion, type Vec3, type WorldEventList, type WorldSystem } from '../webview/fx/runtime';

const rv = (low: number, high = low): Range => ({ low, high, type: low === high ? 'CONSTANT' : 'UNIFORM' });
const node = (name: string, attributes: Record<string, string> = {}, children: FxNode[] = []): FxNode => ({ name, attributes, children });
function config(cpu = false, count = 4, velocity = 0): ParticleConfig {
    const result = compileParticle(node('FXParticleSystemTemplate', { Type: cpu ? 'PARTICLE' : 'GPU_PARTICLE' }, [
        node('Lifetime', { Low: '200', High: '200' }), node('BurstCount', { Low: String(count), High: String(count) }),
        node('Physics', {}, [node('Default')]), node('Draw', {}, [node(cpu ? 'Default' : 'Gpu', cpu ? {} : { EnableSoftParticles: 'false' })]),
        node('Volume', {}, [node('Point')]), node('Velocity', {}, [node('Ortho', {}, [node('Position', {}, [node('x', { Low: String(velocity), High: String(velocity) })])])]),
        node('Update', {}, [node('Default')]),
    ]));
    assert.ok(result.config, JSON.stringify(result.issues));
    return result.config;
}
const path = (playback: SourceMotion['playback'] = 'once'): SourceMotion => ({ playback,
    points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 100, offset: [800, 0, 0] }] });
const eventList = (systems: WorldSystem[]): WorldEventList => ({ key: 'event', systems,
    cullTrackingFrames: 0, cullTrackingMin: 0, cullTrackingMax: 0, triggered: true });
const state = (world: ParticleWorld): string => JSON.stringify({ frame: world.frame, handles: [...world.attachmentHandles],
    systems: world.systems.map(s => ({ id: s.id, dynamic: s.dynamic, parent: s.parent, state: s.runtime.captureState() })) });

test('source motion validates bounded points, times, coordinates and continuous loop closure, and owns its data', () => {
    const input = path(), parsed = parseSourceMotion(input)!;
    assert.deepEqual(parsed, input); assert.notEqual(parsed, input); assert.notEqual(parsed.points, input.points);
    assert.notEqual(parsed.points[1], input.points[1]); assert.notEqual(parsed.points[1].offset, input.points[1].offset);
    input.points[1].offset[0] = 90; assert.equal(parsed.points[1].offset[0], 800);
    const bad: unknown[] = [undefined, null, [], 5, {}, { ...path(), playback: 'repeat' }, { ...path(), points: [] },
        { ...path(), points: [path().points[0]] }, { ...path(), points: Array.from({ length: 65 }, (_, frame) => ({ frame, offset: [0, 0, 0] })) },
        { ...path(), points: [undefined, path().points[1]] }, { ...path(), points: new Array(2) },
        { ...path(), points: [{ frame: 0, offset: new Array(3) }, path().points[1]] },
        { ...path(), points: [{ frame: 1, offset: [0, 0, 0] }, path().points[1]] },
        { ...path(), points: [{ frame: 0, offset: [0, 0, 1] }, path().points[1]] }, path('loop')];
    for (const frame of [-1, 0, 0.5, 18_001, NaN, Infinity, '1']) bad.push({ ...path(), points: [path().points[0], { frame, offset: [0, 0, 0] }] });
    for (const offset of [[0, 0], [0, 0, 0, 0], [NaN, 0, 0], [0, Infinity, 0], [0, 0, 1_000_001], ['1', 0, 0]])
        bad.push({ ...path(), points: [path().points[0], { frame: 1, offset }] });
    bad.push({ ...path(), points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 2, offset: [0, 0, 0] }, { frame: 1, offset: [0, 0, 0] }] });
    for (const value of bad) assert.equal(parseSourceMotion(value), undefined, JSON.stringify(value));
    assert.ok(parseSourceMotion({ playback: 'once', points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 18_000, offset: [1e6, -1e6, 0] }] }));
    assert.ok(parseSourceMotion({ playback: 'loop', points: Array.from({ length: 64 }, (_, frame) => ({ frame, offset: [0, 0, 0] })) }));
});

test('source motion samples fractions, segment boundaries, held endpoints, loop seams and ping-pong reversal', () => {
    const motion: SourceMotion = { playback: 'once', points: [{ frame: 0, offset: [0, 0, 0] },
        { frame: 2, offset: [8, 4, -4] }, { frame: 4, offset: [12, 0, 4] }] };
    assert.deepEqual(sampleSourceMotion(undefined, 10), [0, 0, 0]);
    assert.deepEqual(sampleSourceMotion(motion, -1), [0, 0, 0]);
    assert.deepEqual(sampleSourceMotion(motion, 1), [4, 2, -2]);
    assert.deepEqual(sampleSourceMotion(motion, 2), [8, 4, -4]);
    assert.deepEqual(sampleSourceMotion(motion, 3.5), [11, 1, 2]);
    assert.deepEqual(sampleSourceMotion(motion, 10_000), [12, 0, 4]);
    const endpoint = sampleSourceMotion(motion, 4); endpoint[0] = 999; assert.equal(motion.points[2].offset[0], 12);
    const pingPong = { ...motion, playback: 'pingPong' as const };
    for (const frame of [0, 1, 2, 3.5, 4]) assert.deepEqual(sampleSourceMotion(pingPong, 8 - frame), sampleSourceMotion(motion, frame));
    assert.deepEqual(sampleSourceMotion(pingPong, 12), [12, 0, 4]);
    const loop: SourceMotion = { playback: 'loop', points: [motion.points[0], motion.points[1], { frame: 4, offset: [0, 0, 0] }] };
    for (const frame of [0, 1, 2, 3.5]) assert.deepEqual(sampleSourceMotion(loop, frame + 8), sampleSourceMotion(loop, frame));
    assert.deepEqual(sampleSourceMotion(loop, 4), [0, 0, 0]);
    assert.ok(sampleSourceMotion(loop, 4 - 1e-6).every(v => Math.abs(v) < 1e-5));
    assert.deepEqual(sampleSourceMotion(motion, NaN), [0, 0, 0]);
});

test('world clones its path, adds each root definition offset without accumulation, and preserves ground placement', () => {
    const motion: SourceMotion = { playback: 'once', points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 3, offset: [30, 60, 90] }] };
    const base: Vec3 = [7, 8, 9], definitions: WorldSystem[] = [
        { id: 'free', config: config(), options: { offset: base } },
        { id: 'ground', config: config(), options: { offset: base }, createAtGroundHeight: true },
        { id: 'ground-option', config: config(), options: { offset: base, createAtGroundHeight: true, groundHeight: 23 } },
    ];
    const world = new ParticleWorld(definitions, 1, 100, 100, { sourceMotion: motion, groundHeight: 42 });
    motion.points[1].offset[0] = 999; assert.equal(world.scheduling.sourceMotion!.points[1].offset[0], 30);
    world.seek(1); assert.deepEqual(world.systems.map(s => s.runtime.options.offset), [[17, 28, 39], [17, 28, 42], [17, 28, 42]]);
    world.seek(2); assert.deepEqual(world.systems[0].runtime.options.offset, [27, 48, 69]);
    assert.deepEqual(base, [7, 8, 9]);
    world.seek(0); assert.deepEqual(world.systems[0].runtime.options.offset, base); assert.notEqual(world.systems[0].runtime.options.offset, base);
    world.dispose();
});

test('absent motion preserves existing runtime offset behavior and does not change particle or random state', () => {
    const definitions = [{ id: 'root', config: config(), options: { offset: [5, 6, 7] as Vec3 } }];
    const unchanged = new ParticleWorld(definitions, 19, 1000), stationary = new ParticleWorld(definitions, 19, 1000,
        18_000, { sourceMotion: { playback: 'once', points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 1, offset: [0, 0, 0] }] } });
    unchanged.seek(10); stationary.seek(10); assert.equal(state(unchanged), state(stationary));
    unchanged.systems[0].runtime.options.offset = [50, 60, 70]; unchanged.seek(11);
    assert.deepEqual(unchanged.systems[0].runtime.options.offset, [50, 60, 70]);
    unchanged.dispose(); stationary.dispose();
});

test('moving roots reuse native first-candidate history reset and subsequent i/N birth interpolation', () => {
    const world = new ParticleWorld([{ id: 'root', config: config(), options: { offset: [5, 0, 0] } }], 1, 100, 100, { sourceMotion: path() });
    world.seek(0); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [5, 5, 5, 5]);
    world.seek(1); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [5, 7, 9, 11]);
    world.seek(2); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [13, 15, 17, 19]);
    assert.deepEqual(world.systems[0].runtime.options.offset, [21, 0, 0]); world.dispose();
});

test('delayed first emission starts at the current path point without connecting to pre-emission history', () => {
    const delayed = config(); delayed.initialDelay = rv(2);
    const world = new ParticleWorld([{ id: 'root', config: delayed, options: { startFrame: 2 } }], 1, 100, 100, { sourceMotion: path() });
    world.seek(3); assert.equal(world.systems[0].runtime.particles.length, 0);
    assert.deepEqual(world.systems[0].runtime.options.offset, [24, 0, 0]);
    world.seek(4); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [32, 32, 32, 32]);
    world.seek(5); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [32, 34, 36, 38]); world.dispose();
});

test('quiet burst-delay ticks advance source history, so later bursts interpolate only the last logical step', () => {
    const delayed = config(); delayed.burstDelay = rv(2);
    const world = new ParticleWorld([{ id: 'root', config: delayed }], 1, 100, 100, { sourceMotion: path(), snapshotBytes: 0 });
    world.seek(2); assert.equal(world.systems[0].runtime.births.length, 0);
    world.seek(3); assert.deepEqual(world.systems[0].runtime.births.map(p => p.position[0]), [16, 18, 20, 22]);
    world.seek(10); const expected = state(world); assert.equal(world.snapshotCount, 0);
    world.seek(1); world.seek(10); assert.equal(state(world), expected); world.dispose();
});

test('CPU and GPU stock retain world positions and their own velocities while the source moves', () => {
    for (const cpu of [false, true]) {
        const world = new ParticleWorld([{ id: 'root', config: config(cpu, 2, 1) }], 1, 100, 100, { sourceMotion: path() });
        world.seek(0); const runtime = world.systems[0].runtime, first = runtime.particles[0];
        assert.equal(first.position[0], cpu ? 1 : 0);
        world.seek(1); assert.deepEqual(runtime.births.map(p => p.position[0]), cpu ? [1, 5] : [0, 4]);
        assert.equal(first.position[0], cpu ? 2 : 0); assert.equal(runtime.sample(first).position[0], cpu ? 2 : integrateDamping(1, 1));
        assert.ok(runtime.particles.every(p => p.velocity[0] === 1));
        world.seek(2); assert.equal(runtime.sample(first).position[0], cpu ? 3 : integrateDamping(1, 2)); world.dispose();
    }
});

test('system EventFX starts at the moved source while particle EventFX remains at particle position; instances stay independent', () => {
    for (const perParticle of [false, true]) {
        const root = config(true, 1, 2); root.systemLifetime = 1;
        root.events = [{ type: 'Life', fx: 'event', perParticle, kill: false, eventTime: rv(1) }];
        const child = config(false, 1);
        const world = new ParticleWorld([{ id: 'root', config: root, options: { offset: [3, 0, 0] } }], 1, 100, 100, {
            sourceMotion: path(), fxLists: [eventList([{ id: 'event-child', config: child, options: { offset: [2, 0, 0] } }])],
        });
        world.seek(1); const dynamic = world.systems.find(s => s.dynamic)!;
        const expected = perParticle ? 9 : 13;
        assert.deepEqual(dynamic.runtime.options.offset, [expected, 0, 0]); assert.equal(dynamic.parent, undefined);
        world.seek(3); assert.deepEqual(dynamic.runtime.options.offset, [expected, 0, 0]);
        assert.ok(dynamic.runtime.particles.every(p => p.position[0] === expected)); world.dispose();
    }
});

test('attached systems follow CPU parent stock instead of the source path and never attach to GPU stock', () => {
    for (const cpu of [false, true]) {
        const root = config(cpu, 1, 1); root.systemLifetime = 1; root.attachedSystem = 'child';
        const world = new ParticleWorld([{ id: 'root', config: root, options: { offset: [3, 0, 0] } }], 1, 100, 100, {
            sourceMotion: path(), templates: [{ id: 'child', config: config(false, 1) }],
        });
        world.seek(2);
        if (cpu) {
            const attached = world.systems.find(s => s.parent)!;
            assert.deepEqual(attached.runtime.options.offset, [6, 0, 0]);
            assert.deepEqual(attached.runtime.particles.map(p => p.position[0]), [5, 5]);
        } else assert.equal(world.systems.length, 1);
        world.dispose();
    }
});

test('world snapshots restore source history, random stream, events and attachments across playback boundaries', async () => {
    for (const playback of ['once', 'loop', 'pingPong'] as const) {
        const root = config(true, 1, 1); root.lifetime = rv(20); root.initialDelay = rv(2); root.size = rv(1, 2); root.attachedSystem = 'attached';
        root.events = [{ type: 'Life', fx: 'event', perParticle: true, kill: false, eventTime: rv(3, 5) }];
        const attached = config(false, 1); attached.lifetime = rv(7); attached.systemLifetime = 1;
        const event = config(false, 1); event.lifetime = rv(5); event.systemLifetime = 1;
        const motion: SourceMotion = { playback, points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 18, offset: [54, 18, 9] },
            { frame: 37, offset: playback === 'loop' ? [0, 0, 0] : [111, 0, 37] }] };
        const world = new ParticleWorld([{ id: 'root', config: root }], 19, 1000, 500, { sourceMotion: motion,
            templates: [{ id: 'attached', config: attached }], fxLists: [eventList([{ id: 'event', config: event }])], snapshotBytes: 512_000 });
        world.seek(90); assert.equal(world.budgetExceeded, undefined); assert.ok(world.snapshotCount > 1);
        const expected = state(world); world.seek(35); await world.seekAsync(90); assert.equal(state(world), expected, playback);
        world.seek(0); await world.seekAsync(90); assert.equal(state(world), expected, playback);
        world.dispose(); assert.equal(world.stats.stateBytes, 0);
    }
});

test('cancelled motion replay resumes deterministically and an already aborted replay changes no state', async () => {
    const definition = [{ id: 'root', config: config(false, 4) }];
    const world = new ParticleWorld(definition, 1, 1000, 18_000, { sourceMotion: path('pingPong'), snapshotBytes: 32_000 });
    const pending = world.seekAsync(10_000); world.seek(2); await assert.rejects(pending, /取消/); assert.equal(world.frame, 2);
    const before = state(world), controller = new AbortController(); controller.abort();
    await assert.rejects(world.seekAsync(5, controller.signal), /取消/); assert.equal(state(world), before);
    const direct = new ParticleWorld(definition, 1, 1000, 18_000, { sourceMotion: path('pingPong'), snapshotBytes: 32_000 });
    world.seek(80); direct.seek(80); assert.equal(state(world), state(direct)); world.dispose(); direct.dispose();
});

test('world rejects invalid motion and charges bounded path state before allocating systems or particles', () => {
    assert.throws(() => new ParticleWorld([], 1, 100, 100, { sourceMotion: path('loop') }), /轨迹/);
    const motion: SourceMotion = { playback: 'once', points: Array.from({ length: 64 }, (_, frame) => ({ frame, offset: [0, 0, 0] })) };
    const root = config(false, 0), definitions = [{ id: 'root', config: root }];
    const plain = new ParticleWorld(definitions), moving = new ParticleWorld(definitions, 1, 100, 100, { sourceMotion: motion });
    const overhead = moving.stats.stateBytes - plain.stats.stateBytes;
    assert.equal(overhead, 128 + 64 * 128);
    const insufficient = new ParticleWorld(definitions, 1, 100, 100, { sourceMotion: motion, maxStateBytes: moving.stats.stateBytes - 1 });
    assert.match(insufficient.budgetExceeded!, /预算/); assert.equal(insufficient.systems.length, 0); assert.equal(insufficient.stats.stateBytes, 0);
    const exact = new ParticleWorld(definitions, 1, 100, 100, { sourceMotion: motion, maxStateBytes: moving.stats.stateBytes });
    exact.seek(1); assert.equal(exact.budgetExceeded, undefined);
    for (const world of [plain, moving, insufficient, exact]) { world.dispose(); assert.equal(world.stats.stateBytes, 0); }
});
