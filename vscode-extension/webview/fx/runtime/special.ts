import type { FxRandom } from './random';
import { transformVector } from './math';
import type { BirthRecord, ParticleConfig, ParticleSample, Vec3 } from './types';

/** Reconstructed special-family profile. It is deterministic preview behavior, not a native-engine contract. */
export const isSpecialParticle = (config: ParticleConfig): boolean =>
    config.family === 'TRAIL' || config.family === 'STREAK' || config.family === 'SWARM'
    || !!(config.trail || config.lightning || config.swarm || config.swarmPhysics);

export const TRAIL_POINT_BYTES = 320;
export const LIGHTNING_STATE_BYTES = 1024;

const trailFrames = (life: number): number => Number.isFinite(life) ? Math.min(600, Math.max(0, Math.trunc(life))) : 0;

/** Reserve the entire bounded ring before a birth so extending a tail cannot overrun World memory. */
export function specialParticleCapacity(config: ParticleConfig): number {
    return (config.trail ? 64 + (trailFrames(config.trail.life) + 1) * TRAIL_POINT_BYTES : 0)
        + (config.lightning ? LIGHTNING_STATE_BYTES : 0);
}
export function storedSpecialParticleBytes(birth: BirthRecord): number {
    return (birth.trail ? 64 + birth.trail.length * TRAIL_POINT_BYTES : 0)
        + (birth.lightning ? LIGHTNING_STATE_BYTES : 0);
}

/** Consume the runtime random stream only at birth. Rendering and seeking between frames never sample it. */
export function createSpecialState(birth: BirthRecord, config: ParticleConfig, random: FxRandom, rotation: readonly number[]): void {
    if (config.trail) birth.trail = [];
    if (config.lightning) {
        const worldPoint = (local: Vec3): Vec3 => transformVector(local, rotation).map((v, i) => Math.fround(v + birth.position[i])) as Vec3;
        birth.lightning = {
            start: worldPoint(config.lightning.start), end: worldPoint(config.lightning.end),
            randomSets: config.lightning.randomSets.map(set => ({
                startAmplitude: random.sample(set.startAmplitude), endAmplitudeChange: random.sample(set.endAmplitudeChange),
                startFrequency: random.sample(set.startFrequency), endFrequencyChange: random.sample(set.endFrequencyChange),
                startPhase: random.sample(set.startPhase), endPhaseChange: random.sample(set.endPhaseChange),
            })),
        };
    }
}

/** Draw/Swarm and Physics/Swarm are independent modules; both contribute to the reconstructed spring field. */
export function specialAcceleration(birth: BirthRecord, config: ParticleConfig, emitter: Vec3): Vec3 | undefined {
    if (!config.swarmPhysics && !config.swarm) return undefined;
    const strength = (config.swarmPhysics?.attractStrength ?? 0) + (config.swarm?.attractStrength ?? 0);
    return birth.position.map((v, i) => Math.fround(Math.fround(emitter[i] - v) * strength)) as Vec3;
}

export function expireTrail(birth: BirthRecord, config: ParticleConfig, frame: number): void {
    if (!birth.trail || !config.trail) return;
    const cutoff = frame - trailFrames(config.trail.life);
    let expired = 0;
    while (expired < birth.trail.length && birth.trail[expired].frame < cutoff) expired++;
    if (expired) birth.trail.splice(0, expired);
}
export function recordTrail(birth: BirthRecord, config: ParticleConfig, frame: number, sample: ParticleSample): void {
    if (!birth.trail || !config.trail) return;
    expireTrail(birth, config, frame);
    while (birth.trail.length >= trailFrames(config.trail.life) + 1) birth.trail.shift();
    birth.trail.push({ frame, position: [...sample.position], size: sample.size, color: [...sample.color] });
}
