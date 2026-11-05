import type { Vec3, Vec4 } from '../runtime/types';

/** Fixed high quality preview profiles: CommonParticle / CPUParticle / CommonGeometryParticle. */
export type GeometryType = 'SIMPLE_QUAD' | 'CENTERED_QUAD' | 'TWO_CONCENTRIC_QUADS' | 'TWO_QUADS';
export type ShaderProfile = 'GPU_STANDARD' | 'GPU_PERPENDICULAR_BOTTOM' | 'CPU_DEFAULT';
export type BlendMode = 'ADDITIVE' | 'ALPHA' | 'MULTIPLY';
export type GeometryRotation = 'ROTATION_OFF' | 'ROTATE_AROUND_VELOCITY' | 'ROTATE_IN_WORLD_SPACE';
export interface GeometryContract {
    /** Interleaved local xyz and UV, five floats per vertex. */
    readonly vertices: Float32Array;
    readonly indices: Uint16Array;
}
const standardCorners = [
    [-0.5, -0.5], [0.5, -0.5], [0.5, 0.5], [-0.5, 0.5], [0, 0],
    [-0.25, -0.25], [0.25, -0.25], [0.25, 0.25], [-0.25, 0.25],
];
const standard = (count: number, indices: number[]): GeometryContract => ({
    vertices: new Float32Array(standardCorners.slice(0, count).flatMap(([x, y]) => [x, y, 0, x + 0.5, 0.5 - y])),
    indices: new Uint16Array(indices),
});
// Native shared index constructor 0x6CDC30, research 42. No repeated sprite expansion.
const geometries: Record<GeometryType, GeometryContract> = {
    SIMPLE_QUAD: standard(4, [0, 1, 2, 0, 2, 3]),
    CENTERED_QUAD: standard(5, [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4]),
    TWO_CONCENTRIC_QUADS: standard(9, [0, 1, 5, 1, 6, 5, 1, 7, 6, 1, 2, 7, 2, 3, 7, 3, 8, 7,
        3, 5, 8, 3, 0, 5, 5, 6, 4, 6, 7, 4, 7, 8, 4, 8, 5, 4]),
    TWO_QUADS: {
        // GpuParticlePerpendicularBottom.fixup(p) = -p + (0.5, 0, 0).
        vertices: new Float32Array([
            1, 0.5, 0, 0, 0, 1, -0.5, 0, 0, 1, 0, -0.5, 0, 1, 1, 0, 0.5, 0, 1, 0,
            1, 0, 0.5, 0, 0, 1, 0, -0.5, 0, 1, 0, 0, -0.5, 1, 1, 0, 0, 0.5, 1, 0,
        ]),
        indices: new Uint16Array([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7]),
    },
};

export function geometryContract(type: GeometryType = 'SIMPLE_QUAD', profile: ShaderProfile = 'GPU_STANDARD'): GeometryContract {
    if (!Object.prototype.hasOwnProperty.call(geometries, type)) throw new Error(`未知粒子几何：${String(type)}`);
    if (profile === 'CPU_DEFAULT' && type !== 'SIMPLE_QUAD') throw new Error('CPU Default 仅支持 SIMPLE_QUAD。');
    if ((type === 'TWO_QUADS') !== (profile === 'GPU_PERPENDICULAR_BOTTOM')) {
        throw new Error('TWO_QUADS 必须搭配 GpuParticlePerpendicularBottom 和显式 GeometryUpdate。');
    }
    return geometries[type];
}

export type BlendFactor = 'ONE' | 'ZERO' | 'SRC_ALPHA' | 'ONE_MINUS_SRC_ALPHA' | 'ONE_MINUS_SRC_COLOR' | 'DST_COLOR';
export type ColorOrder = 'TEXTURE_THEN_COLOR_THEN_LINEAR' | 'TEXTURE_THEN_LINEAR_THEN_COLOR' | 'TEXTURE_THEN_COLOR';
export interface MaterialProfile {
    source: BlendFactor;
    destination: BlendFactor;
    depthTest: true;
    depthWrite: false;
    depthFunction: 'LEQUAL';
    cull: 'NONE';
    alphaTest: { enabled: false; comparison: 'GEQUAL'; reference: number };
    sortParticles: boolean;
    colorOrder: ColorOrder;
    softTarget: 'RGB' | 'ALPHA' | 'DISABLED';
    volumeScale: number;
}
export function materialProfile(backend: 'GPU' | 'CPU', shader: BlendMode, sortParticles = false,
    profile: ShaderProfile = backend === 'CPU' ? 'CPU_DEFAULT' : 'GPU_STANDARD'): MaterialProfile {
    if (backend === 'CPU' && profile !== 'CPU_DEFAULT' || backend === 'GPU' && profile === 'CPU_DEFAULT') {
        throw new Error('粒子数据路径与材质 profile 不匹配。');
    }
    let source: BlendFactor, destination: BlendFactor;
    if (shader === 'ADDITIVE') { source = 'ONE'; destination = 'ONE'; }
    else if (shader === 'ALPHA') { source = 'SRC_ALPHA'; destination = 'ONE_MINUS_SRC_ALPHA'; }
    else if (shader === 'MULTIPLY') {
        source = backend === 'CPU' ? 'DST_COLOR' : 'ZERO';
        destination = backend === 'CPU' ? 'ZERO' : 'ONE_MINUS_SRC_COLOR';
    } else throw new Error(`未知粒子混合模式：${String(shader)}`);
    return {
        source, destination, depthTest: true, depthWrite: false, depthFunction: 'LEQUAL', cull: 'NONE',
        // The three supported modes explicitly disable alpha testing. The fixed reference is retained for diagnostics.
        alphaTest: { enabled: false, comparison: 'GEQUAL', reference: 96 / 255 },
        sortParticles: sortParticles || shader === 'ALPHA',
        colorOrder: backend === 'CPU' ? 'TEXTURE_THEN_LINEAR_THEN_COLOR'
            : profile === 'GPU_PERPENDICULAR_BOTTOM' ? 'TEXTURE_THEN_COLOR' : 'TEXTURE_THEN_COLOR_THEN_LINEAR',
        softTarget: backend === 'CPU' ? 'DISABLED' : shader === 'ALPHA' ? 'ALPHA' : 'RGB',
        volumeScale: profile === 'GPU_PERPENDICULAR_BOTTOM' ? 1.5 : 5,
    };
}

/** Pixel math companion for the three independent high quality shader paths. */
export function materialColor(texture: Vec4, color: Vec4, order: ColorOrder): Vec4 {
    return texture.map((value, i) => i === 3 ? value * color[i]
        : order === 'TEXTURE_THEN_COLOR_THEN_LINEAR' ? Math.pow(value * color[i], 2.2)
            : order === 'TEXTURE_THEN_LINEAR_THEN_COLOR' ? Math.pow(value, 2.2) * color[i] : value * color[i]) as Vec4;
}

/** Positive view distance and WebGL window depth use the same near/far projection as cameraMatrices. */
export function linearDepthFromWindow(depth: number, near = 0.1, far = 100000): number {
    return near * far / (far - depth * (far - near));
}
export function windowDepthFromLinear(depth: number, near = 0.1, far = 100000): number {
    return far / (far - near) - far * near / ((far - near) * depth);
}
export function linearViewDepth(position: Vec3, eye: Vec3, forward: Vec3): number {
    return (position[0] - eye[0]) * forward[0] + (position[1] - eye[1]) * forward[1] + (position[2] - eye[2]) * forward[2];
}
/** Official signed size is intentional: negative sizes invert the fade, zero remains a degenerate particle. */
export function softParticleFade(particleDepth: number, backgroundDepth: number, size: number, volumeScale = 5): number {
    const amount = (backgroundDepth - particleDepth) * volumeScale / size;
    // At exactly coincident zero size the rasterized quad has zero area; choose zero instead of leaking NaN.
    return Number.isNaN(amount) ? 0 : Math.max(0, Math.min(1, amount));
}

/** CommonGeometryParticle.Matrix33FromEulerXYZ and HLSL mul(rowVector, matrix). */
export function geometryEulerRotation(point: Vec3, angles: Vec3): Vec3 {
    const [si, sj, sh] = angles.map(Math.sin), [ci, cj, ch] = angles.map(Math.cos);
    const cc = ci * ch, cs = ci * sh, sc = si * ch, ss = si * sh;
    return [
        point[0] * cj * ch + point[1] * (sj * sc - cs) + point[2] * (sj * cc + ss),
        point[0] * cj * sh + point[1] * (sj * ss + cc) + point[2] * (sj * cs - sc),
        -point[0] * sj + point[1] * cj * si + point[2] * cj * ci,
    ];
}
/** ObjectLookAt uses zero roll; the local x axis points along direction, including vertical directions. */
export function geometryObjectLookAt(point: Vec3, direction: Vec3): Vec3 {
    const length = Math.hypot(...direction), xyLength = Math.hypot(direction[0], direction[1]);
    const sinPitch = length ? direction[2] / length : 0, cosPitch = length ? xyLength / length : 1;
    const sinYaw = xyLength ? direction[1] / xyLength : 0, cosYaw = xyLength ? direction[0] / xyLength : 1;
    // Matrix33FromEulerSinCosXYZ((0,-sinPitch,sinYaw),(1,cosPitch,cosYaw)).
    return [
        point[0] * cosPitch * cosYaw - point[1] * sinYaw - point[2] * sinPitch * cosYaw,
        point[0] * cosPitch * sinYaw + point[1] * cosYaw - point[2] * sinPitch * sinYaw,
        point[0] * sinPitch + point[2] * cosPitch,
    ];
}
export function geometryCornerPosition(corner: Vec3, size: Vec3, angles: Vec3, rotation: GeometryRotation,
    finalVelocity: Vec3 = [0, 0, 0], startVelocity: Vec3 = [0, 0, 0]): Vec3 {
    let point = corner.map((value, i) => value * size[i]) as Vec3;
    if (rotation === 'ROTATION_OFF') return point;
    point = geometryEulerRotation(point, angles);
    if (rotation === 'ROTATE_AROUND_VELOCITY') {
        point = geometryObjectLookAt(point, Math.hypot(...finalVelocity) < 0.001 ? startVelocity : finalVelocity);
    } else if (rotation !== 'ROTATE_IN_WORLD_SPACE') throw new Error(`未知几何旋转模式：${String(rotation)}`);
    return point;
}
