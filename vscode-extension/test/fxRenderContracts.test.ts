import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cameraMatrices, project } from '../webview/fx/render/camera';
import { geometryContract, geometryCornerPosition, geometryEulerRotation, geometryObjectLookAt,
    linearDepthFromWindow, linearViewDepth, materialColor, materialProfile, softParticleFade,
    windowDepthFromLinear, type GeometryType } from '../webview/fx/render/contracts';
import type { Vec3, Vec4 } from '../webview/fx/runtime/types';

const close = (actual: number, expected: number, tolerance = 1e-12) => assert.ok(Math.abs(actual - expected) <= tolerance,
    `${actual} differs from ${expected} by more than ${tolerance}`);
const vectorClose = (actual: Vec3, expected: Vec3) => actual.forEach((value, i) => close(value, expected[i]));

test('native standard geometries are a single unit square with 4/5/9 vertices and 2/4/12 triangles', () => {
    for (const [type, vertices, triangles] of [['SIMPLE_QUAD', 4, 2], ['CENTERED_QUAD', 5, 4],
        ['TWO_CONCENTRIC_QUADS', 9, 12]] as [GeometryType, number, number][]) {
        const geometry = geometryContract(type);
        assert.equal(geometry.vertices.length, vertices * 5);
        assert.equal(geometry.indices.length, triangles * 3);
        const edgeUses = new Map<string, number>();
        let area = 0;
        for (let offset = 0; offset < geometry.indices.length; offset += 3) {
            const ids = Array.from(geometry.indices.subarray(offset, offset + 3));
            const points = ids.map(id => Array.from(geometry.vertices.subarray(id * 5, id * 5 + 3)));
            const [[ax, ay], [bx, by], [cx, cy]] = points;
            const triangleArea = ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)) / 2;
            assert.ok(triangleArea > 0, `${type} triangle ${offset / 3} must preserve winding`);
            area += triangleArea;
            for (let edge = 0; edge < 3; edge++) {
                const key = [ids[edge], ids[(edge + 1) % 3]].sort((a, b) => a - b).join(':');
                edgeUses.set(key, (edgeUses.get(key) ?? 0) + 1);
            }
        }
        close(area, 1);
        assert.equal([...edgeUses.values()].filter(uses => uses === 1).length, 4, `${type} has four boundary edges`);
        assert.ok([...edgeUses.values()].every(uses => uses === 1 || uses === 2));
    }
    assert.deepEqual(Array.from(geometryContract('CENTERED_QUAD').indices), [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4]);
    assert.deepEqual(Array.from(geometryContract('TWO_CONCENTRIC_QUADS').indices), [
        0, 1, 5, 1, 6, 5, 1, 7, 6, 1, 2, 7, 2, 3, 7, 3, 8, 7,
        3, 5, 8, 3, 0, 5, 5, 6, 4, 6, 7, 4, 7, 8, 4, 8, 5, 4,
    ]);
});

test('standard UVs retain the top-left texture origin, center and concentric positions', () => {
    const vertices = geometryContract('TWO_CONCENTRIC_QUADS').vertices;
    const vertex = (index: number) => Array.from(vertices.subarray(index * 5, index * 5 + 5));
    assert.deepEqual(vertex(0), [-0.5, -0.5, 0, 0, 1]);
    assert.deepEqual(vertex(2), [0.5, 0.5, 0, 1, 0]);
    assert.deepEqual(vertex(4), [0, 0, 0, 0.5, 0.5]);
    assert.deepEqual(vertex(5), [-0.25, -0.25, 0, 0.25, 0.75]);
    assert.deepEqual(vertex(7), [0.25, 0.25, 0, 0.75, 0.25]);
});

test('TWO_QUADS requires its explicit bottom-pivot shader and preserves two independent UV squares', () => {
    assert.throws(() => geometryContract('TWO_QUADS'), /PerpendicularBottom/);
    assert.throws(() => geometryContract('SIMPLE_QUAD', 'GPU_PERPENDICULAR_BOTTOM'), /PerpendicularBottom/);
    assert.throws(() => geometryContract('CENTERED_QUAD', 'CPU_DEFAULT'), /CPU Default/);
    const geometry = geometryContract('TWO_QUADS', 'GPU_PERPENDICULAR_BOTTOM');
    assert.deepEqual(Array.from(geometry.indices), [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]);
    const v = (index: number) => Array.from(geometry.vertices.subarray(index * 5, index * 5 + 5));
    assert.deepEqual(v(0), [1, 0.5, 0, 0, 0]);
    assert.deepEqual(v(3), [0, 0.5, 0, 1, 0]);
    assert.deepEqual(v(4), [1, 0, 0.5, 0, 0]);
    assert.deepEqual(v(7), [0, 0, 0.5, 1, 0]);
    for (let i = 0; i < 4; i++) assert.deepEqual(v(i).slice(3), v(i + 4).slice(3));
});

test('high quality material states explicitly separate GPU and CPU multiply and color conversion', () => {
    for (const backend of ['GPU', 'CPU'] as const) for (const blend of ['ADDITIVE', 'ALPHA', 'MULTIPLY'] as const) {
        const profile = materialProfile(backend, blend);
        assert.equal(profile.depthTest, true); assert.equal(profile.depthWrite, false);
        assert.equal(profile.depthFunction, 'LEQUAL'); assert.equal(profile.cull, 'NONE');
        assert.deepEqual(profile.alphaTest, { enabled: false, comparison: 'GEQUAL', reference: 96 / 255 });
        assert.equal(profile.sortParticles, blend === 'ALPHA');
        if (blend === 'ADDITIVE') assert.deepEqual([profile.source, profile.destination], ['ONE', 'ONE']);
        else if (blend === 'ALPHA') assert.deepEqual([profile.source, profile.destination], ['SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA']);
        else assert.deepEqual([profile.source, profile.destination], backend === 'CPU' ? ['DST_COLOR', 'ZERO'] : ['ZERO', 'ONE_MINUS_SRC_COLOR']);
        assert.equal(profile.softTarget, backend === 'CPU' ? 'DISABLED' : blend === 'ALPHA' ? 'ALPHA' : 'RGB');
    }
    assert.equal(materialProfile('GPU', 'ADDITIVE', true).sortParticles, true);
    assert.throws(() => materialProfile('CPU', 'ALPHA', false, 'GPU_STANDARD'), /profile/);
    assert.throws(() => materialProfile('GPU', 'ALPHA', false, 'CPU_DEFAULT'), /profile/);
    assert.equal(materialProfile('GPU', 'ALPHA', false, 'GPU_PERPENDICULAR_BOTTOM').volumeScale, 1.5);
    const texture: Vec4 = [0.5, 0.4, 0.25, 0.2], color: Vec4 = [0.5, 0.25, 0.75, 0.6];
    const gpu = materialColor(texture, color, materialProfile('GPU', 'ALPHA').colorOrder);
    const cpu = materialColor(texture, color, materialProfile('CPU', 'ALPHA').colorOrder);
    const geometry = materialColor(texture, color, materialProfile('GPU', 'ALPHA', false, 'GPU_PERPENDICULAR_BOTTOM').colorOrder);
    close(gpu[0], Math.pow(0.25, 2.2)); close(cpu[0], Math.pow(0.5, 2.2) * 0.5); close(geometry[0], 0.25);
    assert.notEqual(gpu[0], cpu[0]);
    assert.equal(gpu[3], 0.12); assert.equal(cpu[3], 0.12); assert.equal(geometry[3], 0.12);
});

test('perspective depth reconstruction agrees with projected world space across camera positions', () => {
    for (const camera of [
        { yaw: -0.7, pitch: 0.65, distance: 160, target: [0, 0, 10] as Vec3 },
        { yaw: 1.3, pitch: 0.3, distance: 47, target: [20, -4, 0] as Vec3 },
    ]) {
        const matrices = cameraMatrices(camera, 16 / 9);
        const forward = camera.target.map((value, i) => (value - matrices.eye[i]) / camera.distance) as Vec3;
        for (const point of [[0, 0, 0], [10, 4, 12], [-20, 2, 8]] as Vec3[]) {
            const depth = linearViewDepth(point, matrices.eye, forward);
            const projected = project(point, matrices.matrix, 1280, 720);
            close(linearDepthFromWindow(projected[2] * 0.5 + 0.5), depth, 0.015);
        }
    }
    for (const depth of [0.1, 1, 20, 160, 10000, 100000]) close(linearDepthFromWindow(windowDepthFromLinear(depth)), depth, depth * 1e-9);
});

test('soft intersection uses per-fragment view separation divided by signed size, not uniform alpha', () => {
    close(softParticleFade(100, 100, 10), 0);
    close(softParticleFade(100, 101, 10), 0.5);
    close(softParticleFade(100, 103, 10), 1);
    close(softParticleFade(102, 101, 10), 0);
    close(softParticleFade(100, 101, 20), 0.25);
    close(softParticleFade(100, 101, 10, 1.5), 0.15);
    close(softParticleFade(100, 101, -10), 0); // Preserve the official signed-size expression.
    close(softParticleFade(102, 101, -10), 0.5);
    close(softParticleFade(100, 100, 0), 0);
    const near = windowDepthFromLinear(100), far = windowDepthFromLinear(101);
    assert.ok(far - near < 0.00001, 'nonlinear depth difference cannot be used as the soft distance');
    close(softParticleFade(linearDepthFromWindow(near), linearDepthFromWindow(far), 10), 0.5, 1e-10);
});

test('geometry Euler XYZ, velocity look-at and bottom-pivot scale follow the public row-vector math', () => {
    vectorClose(geometryEulerRotation([1, 0, 0], [0, 0, Math.PI / 2]), [0, 1, 0]);
    vectorClose(geometryEulerRotation([0, 1, 0], [Math.PI / 2, 0, 0]), [0, 0, 1]);
    vectorClose(geometryEulerRotation([1, 0, 0], [0, Math.PI / 2, 0]), [0, 0, -1]);
    const point: Vec3 = [2, -3, 4], angles: Vec3 = [0.3, -1, 0.8];
    close(Math.hypot(...geometryEulerRotation(point, angles)), Math.hypot(...point));
    for (const direction of [[2, 3, 4], [0, 0, 4], [0, 0, -4], [-4, 0, 0]] as Vec3[]) {
        vectorClose(geometryObjectLookAt([1, 0, 0], direction), direction.map(value => value / Math.hypot(...direction)) as Vec3);
        close(Math.hypot(...geometryObjectLookAt(point, direction)), Math.hypot(...point));
    }
    vectorClose(geometryObjectLookAt(point, [0, 0, 0]), point);
    vectorClose(geometryCornerPosition([1, 0.5, 0], [10, 20, 30], angles, 'ROTATION_OFF'), [10, 10, 0]);
    vectorClose(geometryCornerPosition([1, 0, 0], [10, 20, 30], [0, 0, 0], 'ROTATE_AROUND_VELOCITY', [0, 0, 0], [0, 3, 0]), [0, 10, 0]);
    vectorClose(geometryCornerPosition([1, 0, 0], [10, 20, 30], [0, 0, 0], 'ROTATE_AROUND_VELOCITY', [0, 0, 5], [0, 3, 0]), [0, 0, 10]);
});
