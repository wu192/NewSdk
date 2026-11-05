import { test } from 'node:test';
import assert from 'node:assert/strict';
import { compileParticle, FxRandom, ParticleRuntime, rebuildCpuCurve, updateCpuCurve,
    type CpuCurveState, type FxNode, type ParticleConfig, type Range } from '../webview/fx/runtime';
const rv = (low: number, high = low): Range => ({ low, high, type: low === high ? 'CONSTANT' : 'UNIFORM' });
const node = (name: string, attributes: Record<string, string> = {}, children: FxNode[] = []): FxNode => ({ name, attributes, children });
export function cpuConfig(): ParticleConfig {
    return { id: 'cpu', backend: 'CPU', texture: '', shader: 'ALPHA', lifetime: rv(4), initialDelay: rv(0), burstDelay: rv(0), burstCount: rv(1),
        size: rv(2), startSizeRate: rv(1), systemLifetime: 1, isOneShot: false, velocityDamping: rv(0.5), gravity: -1, drift: [10, 20, 30],
        sizeRate: rv(2), sizeRateDamping: rv(0.5), angleZ: rv(0.25), angularRateZ: rv(0.5), angularDamping: rv(0.1),
        angleXY: rv(0.75), angularRateXY: rv(1), angularDampingXY: rv(0.5), colorScale: rv(1),
        atlas: { framesPerRow: 1, totalFrames: 1, singleRow: 0, randTex: 0, speedMultiplier: 1 },
        volume: { type: 'Point', hollow: false, start: [0, 0, 0], end: [0, 0, 0], halfSize: [0, 0, 0], radius: 0, radiusRate: 0, length: 0, offset: [0, 0, 0] },
        velocity: { type: 'Ortho', x: rv(2), y: rv(3), z: rv(4), radial: rv(0), normal: rv(0), speed: rv(0), otherSpeed: rv(0) },
        groundAligned: false, sortParticles: false, sortLevel: 0 };
}
const cpuNode = (): FxNode => node('FXParticleSystemTemplate', { Type: 'PARTICLE' }, [
    node('Lifetime', { Low: '30', High: '30' }), node('BurstCount', { Low: '1', High: '1' }),
    node('Physics', {}, [node('Default')]), node('Draw', {}, [node('Default')]), node('Volume', {}, [node('Point')]),
    node('Velocity', {}, [node('Ortho')]), node('Update', {}, [node('Default')]),
]);

test('CPU birth updates Default before Physics, includes birth accumulator, and has no shader seed sample', () => {
    const r = new ParticleRuntime(cpuConfig(), 9); r.step(); const b = r.particles[0], p = r.sample(b);
    assert.deepEqual(b.velocity, [1, 1.5, 1.5]); assert.deepEqual(p.position, [11, 21.5, 31.5]);
    assert.equal(p.size, 5); assert.equal(p.angleZ, 0.75); assert.equal(b.cpu!.angleXY, 1.75);
    assert.equal(b.cpu!.sizeRate, 1); assert.equal(b.cpu!.angleRateZ, 0.25);
    assert.equal(r.random.calls, 0); assert.equal(p.color[3], 0); assert.deepEqual(p.atlas.current, [0, 0]);
    r.step(); assert.deepEqual(r.particles[0].velocity, [0.5, 0.75, 0.25]);
    assert.deepEqual(r.sample(b).position, [21.5, 42.25, 61.75]); assert.equal(r.sample(b).size, 6);
});

test('CPU positive lifetime retires after exactly life updates including birth; stock drains after emission closes', () => {
    const c = cpuConfig(); const r = new ParticleRuntime(c); r.seek(2); assert.equal(r.particles.length, 1);
    assert.equal(r.particles[0].cpu!.remaining, 1); r.seek(3); assert.equal(r.particles.length, 0); assert.equal(r.finished, true);
    c.lifetime = rv(1); const one = new ParticleRuntime(c); one.step(); assert.equal(one.particles.length, 0);
    assert.equal(one.births.length, 1); assert.equal(one.deaths.length, 1);
});

test('CPU eight-slot RGB/Alpha curves keep independent times and advance at most one segment, strictly after key time', () => {
    const make = (next: number): CpuCurveState => ({ index: 1, values: Array.from({ length: 8 }, (_, i) => [i]),
        times: [0, next, 0.5, 0.6, 0.7, 0.8, 0.9, 1], current: [0], delta: [0] });
    const color = make(0.25), alpha = make(0.5); rebuildCpuCurve(color, 8, false); rebuildCpuCurve(alpha, 8, true);
    updateCpuCurve(color, 8, 6, false); updateCpuCurve(alpha, 8, 6, true);
    assert.equal(color.index, 1); assert.equal(alpha.index, 1); assert.deepEqual(color.current, [0.5]); assert.deepEqual(alpha.current, [0.25]);
    updateCpuCurve(color, 8, 5, false); updateCpuCurve(alpha, 8, 5, true);
    assert.equal(color.index, 2); assert.deepEqual(color.current, [1]); assert.equal(alpha.index, 1);
    updateCpuCurve(color, 8, 1, false); assert.equal(color.index, 3);
    for (let i = 0; i < 4; i++) updateCpuCurve(color, 8, 1, false);
    assert.equal(color.index, 6); // the next slot at .9 is still future at relative age .875
    const zero = make(0); rebuildCpuCurve(zero, 8, false); updateCpuCurve(zero, 8, 1, false);
    assert.equal(zero.index, 1); assert.deepEqual(zero.delta, [0]);
    const eight = make(0.1); eight.times = Array.from({ length: 8 }, (_, i) => i / 10); rebuildCpuCurve(eight, 100, false);
    for (let i = 0; i < 7; i++) updateCpuCurve(eight, 100, 1, false);
    assert.equal(eight.index, 8); assert.deepEqual(eight.delta, [0]);
});

test('CPU RGB has no upper saturation, ColorScale affects RGB only, additive alpha skips updates', () => {
    const c = cpuConfig(); c.lifetime = rv(10); c.colorScale = rv(2);
    c.colors = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, value: [1, 2, 3] }));
    c.alphas = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, range: rv(i === 0 ? 0.25 : 0.75) }));
    c.shader = 'ADDITIVE'; const additive = new ParticleRuntime(c); additive.step();
    assert.deepEqual(additive.sample(additive.particles[0]).color, [2, 4, 6, 0.25]);
    c.shader = 'ALPHA'; const alpha = new ParticleRuntime(c); alpha.step();
    assert.equal(alpha.sample(alpha.particles[0]).color[3], 0.75);
});

test('CPU constructor preserves Color, eight Alpha, Update and Physics sampling order and independent XY damping samples', () => {
    const c = cpuConfig(); c.alphas = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, range: rv(100 + i) }));
    c.colors = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, value: [1, 1, 1] })); c.colorScale = rv(50);
    const trace: Range[] = [];
    class TraceRandom extends FxRandom { override sample(range: Range): number { trace.push(range); return range.low; } }
    const random = new TraceRandom(); const r = new ParticleRuntime(c, 1, { random }); r.step();
    const start = trace.indexOf(c.colorScale);
    assert.deepEqual(trace.slice(start, start + 8 + 1).map(range => range.low), [50, 100, 101, 102, 103, 104, 105, 106, 107]);
    assert.deepEqual(trace.slice(start + 9, start + 19), [c.sizeRateDamping, c.size, c.sizeRate, c.angleZ, c.angularRateZ,
        c.angularDampingXY, c.angleXY, c.angularRateXY, c.angularDampingXY, c.velocityDamping]);
    assert.equal(trace.filter(range => range === c.angularDamping).length, 0);
    assert.equal(trace.filter(range => range === c.angularDampingXY).length, 2);
});

test('CPU compile admits eight independent slots, zero damping, and reports unproven allocator-dependent alpha initialization', () => {
    const root = cpuNode(); root.children.push(node('Colors', {}, [node('Color', { Frame: '8', RelativeAge: '100' }, [node('Color', { r: '3', g: '2', b: '1' })])]),
        node('Alphas', {}, [node('Alpha', { Frame: '1', Low: '1', High: '1' }), node('Alpha', { Frame: '2', RelativeAge: '100', Low: '0', High: '0' })]));
    root.children.find(n => n.name === 'Physics')!.children[0].children.push(node('VelocityDamping', { Low: '0', High: '0' }));
    root.children.find(n => n.name === 'Update')!.children[0].children.push(node('SizeRateDamping', { Low: '0', High: '0' }));
    const result = compileParticle(root); assert.ok(result.config, JSON.stringify(result.issues));
    assert.equal(result.config.backend, 'CPU'); assert.equal(result.config.shaderProfile, 'CPU_DEFAULT');
    assert.equal(result.config.velocityDamping.low, 0); assert.equal(result.config.sizeRateDamping.low, 0);
    assert.equal(result.config.colors![7].value[0], 3); assert.ok(!result.issues.some(i => i.code === 'slot' || i.code === 'alpha-time'));
    root.children.find(n => n.name === 'Alphas')!.children[1].attributes.RelativeAge = '0';
    const unknown = compileParticle(root); assert.equal(unknown.config, undefined); assert.ok(unknown.issues.some(i => i.code === 'cpu-alpha-initial'));
});

test('CPU runtime snapshots are deep and replay CPU state/RNG/binder state with bounded storage', async () => {
    const c = cpuConfig(); c.lifetime = rv(90, 100); c.systemLifetime = 0; c.burstDelay = rv(3); c.size = rv(1, 5);
    c.alphas = Array.from({ length: 8 }, (_, i) => ({ time: i / 7, range: rv(0.2, 0.8) }));
    const r = new ParticleRuntime(c, 22, { snapshotBytes: 200_000 }); r.seek(120);
    const expected = JSON.stringify(r.captureState()); r.seek(70); await r.seekAsync(120);
    assert.equal(JSON.stringify(r.captureState()), expected); assert.ok(r.snapshotBytes <= 200_000);
    const before = Array.from(r.random.words); for (let i = 0; i < 100; i++) r.sample(r.particles[0]); assert.deepEqual(Array.from(r.random.words), before);
});

test('finite XML values that overflow CPU float recurrence pause while preserving the last finite state', () => {
    const c = cpuConfig(); c.lifetime = rv(100); c.sizeRate = rv(1e12); c.sizeRateDamping = rv(1e12);
    const r = new ParticleRuntime(c); r.seek(20); assert.match(r.budgetExceeded!, /有限 float32/);
    const sample = r.sample(r.particles[0]); assert.ok([sample.size, ...sample.position, ...sample.color].every(Number.isFinite));
});
