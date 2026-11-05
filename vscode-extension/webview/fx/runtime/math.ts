import type { AlphaKey, AtlasConfig, BirthRecord, ParticleConfig, ParticleSample, Vec2, Vec3, Vec4 } from './types';
import { tableSample, tableValue } from './random';

export function integrateDamping(damping: number, age: number): number {
    const d = damping === 1 ? 1.0001 : damping;
    return (Math.pow(d, age) - 1) / Math.log(d);
}
export function atlasFrame(config: AtlasConfig, seed: number, age: number): ParticleSample['atlas'] {
    const n = Math.max(1, Math.trunc(config.framesPerRow));
    const total = Math.max(1, Math.trunc(config.totalFrames));
    const ageFrame = age * config.speedMultiplier;
    const randomRow = Math.trunc(n * tableValue(seed, 2));
    const randomCol = Math.trunc(n * tableValue(seed, 7));
    let current: Vec2, next: Vec2, blend: number;
    const rowUV = (column: number, row: number): Vec2 => [Math.floor(column) / n, row / n];
    if (config.singleRow > 0 && config.randTex === 0 || config.singleRow === -1 && config.randTex === 0) {
        const col = Math.min(ageFrame, n - 1);
        const row = config.singleRow > 0 ? config.singleRow - 1 : randomRow;
        current = rowUV(col, row); next = rowUV(Math.min(ageFrame + 1, n - 1), row);
        blend = col - Math.floor(col);
    } else if (config.randTex !== 0) {
        current = rowUV(randomCol, randomRow); next = [...current]; blend = 0;
    } else {
        const get = (frame: number): Vec2 => {
            const index = frame % total;
            return [Math.floor(index % n) / n, Math.floor(index / n) / n];
        };
        current = get(ageFrame); next = get(ageFrame + 1); blend = ageFrame - Math.floor(ageFrame);
    }
    return { current, next, blend, scale: 1 / n };
}

export function curveColor(config: ParticleConfig, relativeAge: number, seed: number, alphaSamples?: readonly number[]): Vec4 {
    const times = config.colors?.slice(0, 4).map(key => key.time) ?? [0, 0, 0, 0];
    const segment = relativeAge < times[1] ? 0 : relativeAge < times[2] ? 1 : 2;
    const a = segment, b = segment + 1;
    // Binder's near-zero denominator guard retains the slot order, including missing slots.
    const delta = times[b] - times[a];
    const amount = Math.abs(delta) <= 1e-6 ? 0 : (relativeAge - times[a]) / delta;
    const key = (index: number): Vec4 => {
        const rgb = config.colors?.[index]?.value ?? [1, 1, 1];
        const alpha = alphaSamples?.[index] ?? (config.alphas ? config.alphas[index].range.low : config.colors && config.shader === 'MULTIPLY' ? 0 : 1);
        return [...rgb.map(c => config.colors && config.shader === 'MULTIPLY' ? 1 - c : c), alpha] as Vec4;
    };
    const x = key(a), y = key(b);
    const scale = tableSample(config.colorScale, seed, 4);
    return x.map((v, i) => (v + (y[i] - v) * amount) * scale) as Vec4;
}

export function sampleParticle(birth: BirthRecord, config: ParticleConfig, ageFrames: number, alphaSamples?: readonly number[]): ParticleSample {
    const s = birth.seed, t = ageFrames;
    const vd = tableSample(config.velocityDamping, s, 13);
    const distance = integrateDamping(vd, t);
    const move: Vec3 = birth.velocity.map((v, i) => v * distance + config.drift[i] * t) as Vec3;
    move[2] += 0.5 * config.gravity * t * t;
    const angleXY = tableSample(config.angleXY, s, 9, 0) + tableSample(config.angularRateXY, s, 9, 1)
        * integrateDamping(tableSample(config.angularDampingXY, s, 9, 2), t);
    const c = Math.cos(angleXY), sn = Math.sin(angleXY);
    const position: Vec3 = [birth.position[0] + move[0] * c + move[1] * sn,
        birth.position[1] - move[0] * sn + move[1] * c, birth.position[2] + move[2]];
    return {
        alive: t >= 0 && t <= birth.life,
        position,
        size: tableSample(config.size, s, 3, 0) + tableSample(config.sizeRate, s, 3, 1)
            * integrateDamping(tableSample(config.sizeRateDamping, s, 3, 2), t),
        angleZ: tableSample(config.angleZ, s, 2, 0) + tableSample(config.angularRateZ, s, 2, 1)
            * integrateDamping(tableSample(config.angularDamping, s, 2, 2), t),
        color: curveColor(config, t / birth.life, s, alphaSamples),
        atlas: atlasFrame(config.atlas, s, t),
    };
}

/**
 * Position-trigger transform profile: Rx * Ry * Rz (column vectors).
 * RA3 6f50d090 native evidence establishes X->Y->Z setter calls. Their bodies are absent.
 * Multiplication/sign contract comes from EA's same-family GeneralsMD ParticleSys.cpp
 * rotateLocalTransform{X,Y,Z} and WWMath/matrix3d.h Rotate_{X,Y,Z} post-multiplication.
 * This explicit profile still needs an RA3 game-frame comparison; it is not a byte-equivalence claim.
 */
export function localRotationMatrix(angles: Vec3): readonly number[] {
    const matrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    for (let axis = 0; axis < 3; axis++) {
        const s = Math.sin(angles[axis]), c = Math.cos(angles[axis]);
        const a = axis === 0 ? 1 : 0, b = axis === 2 ? 1 : 2;
        for (let row = 0; row < 3; row++) {
            const x = matrix[row * 3 + a], y = matrix[row * 3 + b];
            matrix[row * 3 + a] = axis === 1 ? c * x - s * y : c * x + s * y;
            matrix[row * 3 + b] = axis === 1 ? s * x + c * y : -s * x + c * y;
        }
    }
    return matrix;
}
export function transformVector(vector: Vec3, matrix: readonly number[]): Vec3 {
    return [matrix[0] * vector[0] + matrix[1] * vector[1] + matrix[2] * vector[2],
        matrix[3] * vector[0] + matrix[4] * vector[1] + matrix[5] * vector[2],
        matrix[6] * vector[0] + matrix[7] * vector[1] + matrix[8] * vector[2]];
}
