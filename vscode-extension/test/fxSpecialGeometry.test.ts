import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildSpecialGeometry, specialVertexFloats, type SpecialCamera } from '../webview/fx/render/specialGeometry';
import type { BirthRecord, ParticleConfig, ParticleSample, Vec3, Vec4 } from '../webview/fx/runtime/types';

const camera: SpecialCamera = { right: [1, 0, 0], up: [0, 1, 0], eye: [0, 0, 20], backward: [0, 0, 1] };
const birth = (position: Vec3 = [0, 0, 0], velocity: Vec3 = [0, 0, 0]): BirthRecord => ({ id: 0, position, velocity, life: 30, creationFrame: 0, seed: 1 });
const sample = (particle: BirthRecord): ParticleSample => ({ alive: true, position: particle.position, size: 2, angleZ: 0,
    color: [1, 0.5, 0.25, 1], atlas: { current: [0, 0], next: [0, 0], blend: 0, scale: 1 } });
const cfg = (values: Partial<ParticleConfig>): ParticleConfig => values as ParticleConfig;
const vertex = (data: Float32Array, index: number): number[] => Array.from(data.subarray(index * specialVertexFloats, (index + 1) * specialVertexFloats));
const close = (actual: number, expected: number): void => assert.ok(Math.abs(actual - expected) < 1e-5, `${actual} != ${expected}`);
const trailConfig = cfg({ drawModule: 'Trail', trail: { life: 4, uTile: 1, vTile: 2, uScrollRate: 3, vScrollRate: 6 } });
function trailBirth(): BirthRecord {
    return { ...birth(), trail: [0, 1, 2].map(frame => ({ frame, position: [frame * 4, 0, 0] as Vec3, size: 2, color: [1, 1, 1, 1] as Vec4 })) };
}

test('Trail builds joined camera-facing triangles, fades historical points and drains after death', () => {
    const particle = trailBirth();
    const geometry = buildSpecialGeometry(trailConfig, [particle], 2, camera, () => ({ ...sample(particle), alive: false }));
    assert.equal(geometry.particles, 1); assert.equal(geometry.vertices.length, 12 * specialVertexFloats);
    close(vertex(geometry.vertices, 0)[8], 0.5); close(vertex(geometry.vertices, 2)[8], 0.75);
    // Both segments share exactly the same two edge positions at their join.
    assert.deepEqual(vertex(geometry.vertices, 2).slice(0, 3), vertex(geometry.vertices, 7).slice(0, 3));
    assert.deepEqual(vertex(geometry.vertices, 5).slice(0, 3), vertex(geometry.vertices, 6).slice(0, 3));
    close(vertex(geometry.vertices, 1)[4] - vertex(geometry.vertices, 0)[4], 2);
    assert.equal(buildSpecialGeometry(trailConfig, [particle], 7, camera, sample).vertices.length, 0);
});

test('Trail UV scrolling is time-based, duplicate points remain finite and camera changes face the strip', () => {
    const particle = trailBirth(), before = JSON.stringify(particle);
    particle.trail!.splice(1, 0, structuredClone(particle.trail![0]));
    const first = buildSpecialGeometry(trailConfig, [particle], 2, camera, sample);
    const next = buildSpecialGeometry(trailConfig, [particle], 3, camera, sample);
    close(vertex(next.vertices, 0)[3] - vertex(first.vertices, 0)[3], 3 / 30);
    close(vertex(next.vertices, 0)[4] - vertex(first.vertices, 0)[4], 6 / 30);
    const turned = buildSpecialGeometry(trailConfig, [particle], 2,
        { right: [1, 0, 0], up: [0, 0, 1], eye: [0, 20, 0], backward: [0, 1, 0] }, sample);
    assert.notDeepEqual(Array.from(first.vertices.subarray(0, 3)), Array.from(turned.vertices.subarray(0, 3)));
    assert.ok(Array.from(turned.vertices).every(Number.isFinite));
    particle.trail!.splice(1, 1); assert.equal(JSON.stringify(particle), before);
});

function lightningBirth(): BirthRecord {
    return { ...birth(), lightning: { start: [0, 0, 0], end: [30, 0, 0], randomSets: [{ startAmplitude: 4,
        endAmplitudeChange: 2, startFrequency: 2, endFrequencyChange: 1, startPhase: 0, endPhaseChange: 2 }] } };
}
test('Lightning pins endpoints, changes with sampled animation and repeats deterministically without mutating state', () => {
    const particle = lightningBirth(), before = JSON.stringify(particle), config = cfg({ drawModule: 'Lightning' });
    const first = buildSpecialGeometry(config, [particle], 5, camera, sample);
    assert.equal(first.vertices.length, 64 * 6 * specialVertexFloats);
    const startLeft = vertex(first.vertices, 0), startRight = vertex(first.vertices, 1);
    const endLeft = vertex(first.vertices, 64 * 6 - 1), endRight = vertex(first.vertices, 64 * 6 - 4);
    for (let axis = 0; axis < 3; axis++) {
        close((startLeft[axis] + startRight[axis]) / 2, particle.lightning!.start[axis]);
        close((endLeft[axis] + endRight[axis]) / 2, particle.lightning!.end[axis]);
    }
    assert.deepEqual(first.vertices, buildSpecialGeometry(config, [particle], 5, camera, sample).vertices);
    assert.notDeepEqual(first.vertices, buildSpecialGeometry(config, [particle], 20, camera, sample).vertices);
    assert.equal(JSON.stringify(particle), before);
    particle.lightning!.end = [0, 0, 0];
    assert.equal(buildSpecialGeometry(config, [particle], 5, camera, sample).vertices.length, 0);
});

const swarmConfig = cfg({ drawModule: 'Swarm', shader: 'ALPHA', swarm: { opaqueSpeed: 0, transparentSpeed: 8,
    speedStretchAmount: 2, attractStrength: 0, environmentTexture: 'environment' } });
test('Swarm stretches along projected velocity, maps speed opacity and supplies changing finite environment coordinates', () => {
    const particle = birth([0, 0, 0], [4, 0, 0]);
    const mesh = buildSpecialGeometry(swarmConfig, [particle], 0, camera, sample);
    assert.equal(mesh.particles, 1); assert.equal(mesh.vertices.length, 6 * specialVertexFloats);
    const vertices = Array.from({ length: 6 }, (_, i) => vertex(mesh.vertices, i));
    close(Math.max(...vertices.map(v => v[0])) - Math.min(...vertices.map(v => v[0])), 10);
    close(Math.max(...vertices.map(v => v[1])) - Math.min(...vertices.map(v => v[1])), 2);
    assert.ok(vertices.every(v => v[8] === 0.5));
    assert.ok(vertices.every(v => v.slice(9).every(x => Number.isFinite(x) && x >= 0 && x <= 1)));
    assert.ok(new Set(vertices.map(v => v.slice(9).join(':'))).size > 1);
    particle.velocity = [0, 4, 0];
    const turned = buildSpecialGeometry(swarmConfig, [particle], 0, camera, sample);
    assert.notDeepEqual(mesh.vertices, turned.vertices);
    particle.velocity = [0, 0, 4];
    assert.ok(Array.from(buildSpecialGeometry(swarmConfig, [particle], 0, camera, sample).vertices).every(Number.isFinite));
});

test('Swarm sorts transparent particles back to front and equal speed thresholds preserve visibility', () => {
    const near = birth([0, 0, 5]), far = { ...birth([0, 0, -5]), id: 1 };
    const config = cfg({ ...swarmConfig, swarm: { ...swarmConfig.swarm!, transparentSpeed: 0 } });
    const mesh = buildSpecialGeometry(config, [near, far], 0, camera, sample);
    assert.equal(mesh.particles, 2); close(vertex(mesh.vertices, 0)[2], -5); close(vertex(mesh.vertices, 6)[2], 5);
    assert.equal(vertex(mesh.vertices, 0)[8], 1);
});

test('special geometry rejects overflow and enforces aggregate vertex budgets before returning a mesh', () => {
    assert.throws(() => buildSpecialGeometry(trailConfig, [trailBirth()], 2, camera, sample, 6), /顶点预算/);
    assert.throws(() => buildSpecialGeometry(cfg({ drawModule: 'Lightning' }), [lightningBirth()], 2, camera, sample, 383), /顶点预算/);
    assert.throws(() => buildSpecialGeometry(swarmConfig, [birth()], 2, camera, sample, 0), /顶点预算/);
    assert.throws(() => buildSpecialGeometry(swarmConfig, [], Infinity, camera, sample), /非有限/);
    const invalid = trailBirth(); invalid.trail![1].position[0] = Infinity;
    assert.throws(() => buildSpecialGeometry(trailConfig, [invalid], 2, camera, sample), /非有限/);
    const huge = birth([0, 0, 0], [1e40, 0, 0]);
    assert.throws(() => buildSpecialGeometry(swarmConfig, [huge], 2, camera, sample), /float32/);
});
