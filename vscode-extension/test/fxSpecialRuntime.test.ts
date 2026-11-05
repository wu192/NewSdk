import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ParticleRuntime, ParticleWorld, particleStateBytes, runtimeSnapshotBytes, type ParticleConfig, type Range } from '../webview/fx/runtime';

const rv = (low: number, high = low): Range => ({ low, high, type: low === high ? 'CONSTANT' : 'UNIFORM' });
function config(): ParticleConfig {
    return {
        family: 'TRAIL', backend: 'CPU', drawModule: 'Trail', id: 'special', texture: '', shader: 'ADDITIVE',
        lifetime: rv(10), initialDelay: rv(0), burstDelay: rv(0), burstCount: rv(1), size: rv(2), startSizeRate: rv(0),
        systemLifetime: 1, isOneShot: false, velocityDamping: rv(1), gravity: 0, drift: [0, 0, 0],
        sizeRate: rv(0), sizeRateDamping: rv(1), angleZ: rv(0), angularRateZ: rv(0), angularDamping: rv(1),
        angleXY: rv(0), angularRateXY: rv(0), angularDampingXY: rv(1), colorScale: rv(1),
        atlas: { framesPerRow: 1, totalFrames: 1, singleRow: 0, randTex: 0, speedMultiplier: 1 },
        volume: { type: 'Point', hollow: false, start: [0, 0, 0], end: [0, 0, 0], halfSize: [0, 0, 0], radius: 0, radiusRate: 0, length: 0, offset: [0, 0, 0] },
        velocity: { type: 'Ortho', x: rv(1), y: rv(0), z: rv(0), radial: rv(0), normal: rv(0), speed: rv(0), otherSpeed: rv(0) },
        groundAligned: false, sortParticles: false, sortLevel: 0,
        trail: { life: 3, uTile: 1, vTile: 1, uScrollRate: 0, vScrollRate: 0 },
    };
}
const state = (runtime: ParticleRuntime): string => JSON.stringify(runtime.captureState());

test('Trail records independent finite samples, retires once and drains after the head dies', () => {
    const c = config(); c.lifetime = rv(3);
    const runtime = new ParticleRuntime(c); let retired = 0; runtime.onDeath = () => retired++;
    runtime.seek(2);
    assert.equal(retired, 1); assert.equal(runtime.particles.length, 1);
    const head = runtime.particles[0];
    assert.equal(runtime.sample(head).alive, false);
    assert.deepEqual(head.trail!.map(point => point.position[0]), [0, 1, 2, 3]);
    assert.deepEqual(head.trail!.map(point => point.frame), [-1, 0, 1, 2]);
    assert.ok(head.trail!.every(point => point.color[3] === 1));
    assert.notEqual(head.trail![0].position, head.position);
    runtime.seek(4); assert.deepEqual(head.trail!.map(point => point.frame), [1, 2]);
    runtime.seek(6); assert.equal(runtime.particles.length, 0); assert.equal(retired, 1); assert.equal(runtime.finished, true);
});

test('Trail ring and snapshots stay bounded, cloned snapshots preserve all replay state', async () => {
    const c = config(); c.lifetime = rv(400); c.trail!.life = 7;
    const runtime = new ParticleRuntime(c, 71, { snapshotBytes: 32 * 1024 }); runtime.seek(100);
    assert.equal(runtime.particles[0].trail!.length, 8);
    const expected = state(runtime), snapshot = runtime.captureState();
    assert.ok(runtimeSnapshotBytes(snapshot) <= runtime.particleBytes + 640);
    snapshot.particles[0].trail![0].position[0] = 9999;
    assert.notEqual(runtime.particles[0].trail![0].position[0], 9999);
    runtime.seek(5); await runtime.seekAsync(100); assert.equal(state(runtime), expected);
    assert.ok(runtime.snapshotBytes <= 32 * 1024);
    const calls = runtime.random.calls; for (let i = 0; i < 50; i++) runtime.sample(runtime.particles[0]);
    assert.equal(runtime.random.calls, calls);
});

test('Lightning birth parameters and rotated world endpoints replay without rendering random draws', () => {
    const c = config(); c.trail = undefined; c.family = 'STREAK'; c.drawModule = 'Lightning';
    c.velocity.x = rv(0);
    const set = { startAmplitude: rv(2, 5), endAmplitudeChange: rv(-1, 1), startFrequency: rv(3, 7),
        endFrequencyChange: rv(-0.1, 0.1), startPhase: rv(0, 5), endPhaseChange: rv(0, 0.05) };
    c.lightning = { start: [1, 0, 0], end: [3, 0, 0], randomSets: [set, set] };
    const runtime = new ParticleRuntime(c, 91, { offset: [10, 20, 30], rotation: Math.PI / 2 });
    runtime.seek(0); const lightning = runtime.particles[0].lightning!;
    assert.deepEqual(lightning.start, [10, 21, 30]); assert.deepEqual(lightning.end, [10, 23, 30]);
    assert.equal(lightning.randomSets.length, 2); assert.notDeepEqual(lightning.randomSets[0], lightning.randomSets[1]);
    const calls = runtime.random.calls; for (let i = 0; i < 100; i++) runtime.sample(runtime.particles[0]);
    assert.equal(runtime.random.calls, calls);
    runtime.seek(6); const expected = state(runtime); runtime.seek(0); runtime.seek(6); assert.equal(state(runtime), expected);
});

test('Swarm physics works independently with Trail and Draw/Swarm contributes its own field', () => {
    const c = config(); c.volume.type = 'Line'; c.volume.start = [10, 0, 0]; c.volume.end = [10, 0, 0]; c.velocity.x = rv(0);
    c.swarmPhysics = { attractStrength: 0.1 };
    const physics = new ParticleRuntime(c); physics.seek(0);
    assert.equal(physics.particles[0].position[0], 9); assert.equal(physics.particles[0].velocity[0], -1);
    assert.equal(physics.particles[0].trail!.at(-1)!.position[0], 9);
    const draw = structuredClone(c); draw.trail = undefined; draw.family = 'SWARM'; draw.drawModule = 'Swarm'; draw.swarmPhysics = undefined;
    draw.swarm = { opaqueSpeed: 0, transparentSpeed: 100, speedStretchAmount: 1, attractStrength: 0.1, environmentTexture: '' };
    const shader = new ParticleRuntime(draw); shader.seek(0); assert.equal(shader.particles[0].position[0], 9);
    const combined = structuredClone(draw); combined.swarmPhysics = { attractStrength: 0.1 };
    const both = new ParticleRuntime(combined); both.seek(0); assert.equal(both.particles[0].position[0], 8);
});

test('special alpha interpolation guards equal integer intervals and animates additive tails', () => {
    const c = config(); c.lifetime = rv(3);
    c.alphas = Array.from({ length: 8 }, (_, index) => ({ time: index < 3 ? index * 0.01 : 0, range: rv(index === 0 ? 1 : 0) }));
    const runtime = new ParticleRuntime(c); runtime.seek(2);
    assert.equal(runtime.budgetExceeded, undefined);
    assert.ok(runtime.particles[0].trail!.every(point => point.color.every(Number.isFinite)));
    assert.equal(runtime.particles[0].trail![1].color[3], 0);
});

test('World reserves full tail capacity before births and restores moving-source special state', () => {
    const c = config(); c.lifetime = rv(100); c.trail!.life = 600;
    const oversized = new ParticleWorld([{ id: 'tail', config: c }], 1, 100, 1000, { maxStateBytes: 16 * 1024 });
    oversized.seek(0); assert.match(oversized.budgetExceeded!, /预算/); assert.equal(oversized.stats.particles, 0);
    assert.ok(oversized.stats.stateBytes <= 16 * 1024);
    c.trail!.life = 5; c.systemLifetime = 0; c.burstDelay = rv(5); c.swarmPhysics = { attractStrength: 0.02 };
    const world = new ParticleWorld([{ id: 'tail', config: c }], 23, 100, 200, { sourceMotion: { playback: 'pingPong',
        points: [{ frame: 0, offset: [0, 0, 0] }, { frame: 50, offset: [50, 0, 0] }] } });
    world.seek(80); const expected = JSON.stringify(world.systems.map(s => s.runtime.captureState()));
    world.seek(3); world.seek(80); assert.equal(JSON.stringify(world.systems.map(s => s.runtime.captureState())), expected);
    assert.equal(world.budgetExceeded, undefined);
    assert.equal(world.stats.particleBytes, world.stats.particles * particleStateBytes(c));
    world.dispose(); assert.equal(world.stats.stateBytes, 0);
});


test('one-frame Trail heads leave a segment and death detaches attached systems before the tail drains', () => {
    const c = config(); c.lifetime = rv(1); c.attachedSystem = 'child';
    const child = config(); child.trail = undefined; child.family = 'PARTICLE'; child.drawModule = 'Default';
    const world = new ParticleWorld([{ id: 'head', config: c }], 1, 100, 100,
        { templates: [{ id: 'child', config: child }] });
    world.seek(0);
    const head = world.systems[0].runtime.particles[0];
    assert.deepEqual(head.trail!.map(point => point.position[0]), [0, 1]);
    assert.equal(head.cpu!.remaining, 0);
    assert.equal(world.attachmentHandles.size, 0);
    assert.ok(world.systems.filter(system => system.dynamic).every(system => !system.parent && system.runtime.emissionStopped));
    world.seek(4); assert.equal(world.systems[0].runtime.particles.length, 0); assert.equal(world.systems.length, 1);
    world.dispose();
});
