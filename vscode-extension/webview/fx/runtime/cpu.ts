import type { FxRandom } from './random';
import type { BirthRecord, CpuCurveState, CpuParticleState, ParticleConfig, ParticleSample, Range, Vec3 } from './types';

const f = Math.fround;

/** Native CPU keyframe helper: slot order is retained; a zero next time stops the slope. */
export function rebuildCpuCurve(curve: CpuCurveState, life: number, alpha: boolean): void {
    const i = curve.index;
    if (i >= 8 || !curve.times[i] || life <= 0) { curve.delta.fill(0); return; }
    const frames = Math.trunc(life * curve.times[i]) - Math.trunc(life * Math.max(curve.times[i - 1], 0));
    const denominator = f(alpha ? frames >>> 0 : Math.max(1, frames >>> 0));
    curve.current = curve.values[i - 1].slice();
    const inverse = f(1 / denominator);
    curve.delta = curve.current.map((v, axis) => alpha ? f(f(curve.values[i][axis] - v) / denominator)
        : f(f(curve.values[i][axis] - v) * inverse));
}

export function updateCpuCurve(curve: CpuCurveState, life: number, remaining: number, alpha: boolean): void {
    for (let i = 0; i < curve.current.length; i++) curve.current[i] = f(curve.current[i] + curve.delta[i]);
    if (curve.index < 8 && curve.times[curve.index] !== 0 && remaining > 0 && life > 0
        && 1 - remaining / life > curve.times[curve.index]) {
        curve.index++;
        rebuildCpuCurve(curve, life, alpha);
    } else if (curve.index >= 8 || !curve.times[curve.index] || remaining <= 0) curve.delta.fill(0);
    for (let i = 0; i < curve.current.length; i++) curve.current[i] = alpha ? Math.min(1, Math.max(0, curve.current[i])) : Math.max(0, curve.current[i]);
}

export function createCpuState(config: ParticleConfig, life: number, accumulator: number, random: FxRandom): CpuParticleState {
    const sample = (range: Range): number => f(random.sample(range));
    let color: CpuCurveState | undefined, alpha: CpuCurveState | undefined;
    // FXParticle ctor builds Color, Alpha, Update, Physics in this order (6CC6E6…6CC73D).
    if (config.colors) {
        const scale = sample(config.colorScale);
        const values = config.colors.map(key => key.value.map(v => f(f(v) * scale)));
        color = { index: 1, values, times: config.colors.map(key => f(key.time)), current: values[0].slice(), delta: [0, 0, 0] };
        rebuildCpuCurve(color, life, false);
    }
    if (config.alphas) {
        const values = config.alphas.map(key => [sample(key.range)]);
        alpha = { index: 1, values, times: config.alphas.map(key => f(key.time)), current: values[0].slice(), delta: [0] };
        rebuildCpuCurve(alpha, life, true);
    }
    const sizeDamping = sample(config.sizeRateDamping);
    const size = f(sample(config.size) + accumulator);
    const sizeRate = sample(config.sizeRate);
    const angleZ = sample(config.angleZ);
    const angleRateZ = sample(config.angularRateZ);
    // Native CPU samples AngularDampingXY twice, independently; AngularDamping is GPU-only here.
    const angleDampingZ = sample(config.angularDampingXY);
    const angleXY = sample(config.angleXY);
    const angleRateXY = sample(config.angularRateXY);
    const angleDampingXY = sample(config.angularDampingXY);
    const velocityDamping = sample(config.velocityDamping);
    return { remaining: life, size, sizeRate, sizeDamping, angleZ, angleRateZ, angleDampingZ,
        angleXY, angleRateXY, angleDampingXY, velocityDamping, color, alpha };
}

/** Update Default before Physics Default, then retire in the same update that reaches remaining=0. */
export function updateCpuParticle(birth: BirthRecord, config: ParticleConfig): boolean {
    const p = birth.cpu!;
    const size = f(p.size + p.sizeRate), sizeRate = f(p.sizeRate * p.sizeDamping);
    const angleZ = f(p.angleZ + p.angleRateZ), angleRateZ = f(p.angleRateZ * p.angleDampingZ);
    const angleXY = f(p.angleXY + p.angleRateXY), angleRateXY = f(p.angleRateXY * p.angleDampingXY);
    const vx = f(f(birth.velocity[0] + 0) * p.velocityDamping), vy = f(f(birth.velocity[1] + 0) * p.velocityDamping);
    const vz = f(f(birth.velocity[2] + f(config.gravity)) * p.velocityDamping);
    const px = f(birth.position[0] + f(vx + f(config.drift[0]))), py = f(birth.position[1] + f(vy + f(config.drift[1])));
    const pz = f(birth.position[2] + f(vz + f(config.drift[2])));
    // This is a preview arithmetic budget, not an invented native NaN/overflow policy.
    // Keep the last finite state visible when otherwise finite input overflows float32 recurrence.
    if (!Number.isFinite(size) || !Number.isFinite(sizeRate) || !Number.isFinite(angleZ) || !Number.isFinite(angleRateZ)
        || !Number.isFinite(angleXY) || !Number.isFinite(angleRateXY) || !Number.isFinite(vx) || !Number.isFinite(vy)
        || !Number.isFinite(vz) || !Number.isFinite(px) || !Number.isFinite(py) || !Number.isFinite(pz)
        || p.color && !p.color.delta.every(Number.isFinite) || p.alpha && !p.alpha.delta.every(Number.isFinite)) return false;
    if (p.color) updateCpuCurve(p.color, birth.life, p.remaining, false);
    // Shader enum 1/2 (ADDITIVE / ADDITIVE_ALPHA_TEST) skips the native Alpha update.
    if (p.alpha && config.shader !== 'ADDITIVE') updateCpuCurve(p.alpha, birth.life, p.remaining, true);
    p.size = size; p.sizeRate = sizeRate; p.angleZ = angleZ; p.angleRateZ = angleRateZ; p.angleXY = angleXY; p.angleRateXY = angleRateXY;
    birth.velocity[0] = vx; birth.velocity[1] = vy; birth.velocity[2] = vz;
    birth.position[0] = px; birth.position[1] = py; birth.position[2] = pz;
    return true;
}

export function sampleCpuParticle(birth: BirthRecord): ParticleSample {
    const p = birth.cpu!;
    const rgb = p.color?.current ?? [1, 1, 1];
    // DefaultDraw's missing Alpha module fallback is zero (6B7470), not GPU's one.
    return { alive: p.remaining > 0, position: birth.position.slice() as Vec3, size: p.size, angleZ: p.angleZ,
        color: [rgb[0], rgb[1], rgb[2], p.alpha?.current[0] ?? 0],
        atlas: { current: [0, 0], next: [0, 0], blend: 0, scale: 1 } };
}
