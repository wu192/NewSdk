import { FxRandom } from './random';
import type { ParticleConfig, Vec3, VolumeConfig } from './types';
const multiply = (v: Vec3, s: number): Vec3 => v.map(x => Math.fround(x * s)) as Vec3;
const normalize = (v: Vec3): Vec3 => { const n = Math.hypot(...v); return n === 0 ? [0, 0, 0] : multiply(v, Math.fround(1 / n)); };
export function sampleVolume(v: VolumeConfig, rng: FxRandom, runtimeRadius = v.radius): Vec3 {
    switch (v.type) {
        case 'Point': return [0, 0, 0];
        case 'Line': {
            const q = rng.range(0, 1);
            return v.start.map((a, i) => Math.fround(a + (v.end[i] - a) * q)) as Vec3;
        }
        case 'Box': {
            const h = v.halfSize;
            if (!v.hollow) return h.map(x => rng.range(-x, x)) as Vec3;
            const k = rng.integer(0, 6), axis = k % 3;
            const side = k < 3 ? -1 : 1;
            if (axis === 0) return [rng.range(-h[0], h[0]), rng.range(-h[1], h[1]), side * h[2]];
            if (axis === 1) return [side * h[0], rng.range(-h[1], h[1]), rng.range(-h[2], h[2])];
            return [rng.range(-h[0], h[0]), side * h[1], rng.range(-h[2], h[2])];
        }
        case 'Sphere': {
            const radius = v.hollow ? v.radius : rng.range(0, v.radius);
            return multiply(rng.direction(), radius);
        }
        case 'Cylinder': {
            const angle = rng.range(0, Math.fround(2 * Math.PI));
            const radius = v.hollow ? runtimeRadius : rng.range(0, runtimeRadius);
            return [Math.fround(radius * Math.cos(angle) + v.offset[0]), Math.fround(radius * Math.sin(angle) + v.offset[1]),
                Math.fround(rng.range(-v.length / 2, v.length / 2) + v.offset[2])];
        }
    }
}
export function sampleVelocity(config: ParticleConfig, p: Vec3, rng: FxRandom): Vec3 {
    const v = config.velocity;
    switch (v.type) {
        case 'Ortho': return [rng.sample(v.x), rng.sample(v.y), rng.sample(v.z)];
        case 'Cylindrical': {
            const radial = rng.sample(v.radial), angle = rng.range(0, Math.fround(2 * Math.PI)), normal = rng.sample(v.normal);
            return [Math.fround(radial * Math.cos(angle)), Math.fround(radial * Math.sin(angle)), normal];
        }
        case 'Spherical': { const speed = rng.sample(v.speed); return multiply(rng.direction(), speed); }
        case 'Outward': {
            const speed = rng.sample(v.speed), other = rng.sample(v.otherSpeed);
            switch (config.volume.type) {
                case 'Point': return multiply(rng.direction(), speed);
                case 'Box': case 'Sphere': return multiply(normalize(p), speed);
                case 'Cylinder': {
                    const length2 = p[0] * p[0] + p[1] * p[1];
                    const scale = length2 > Math.fround(0.0001) ? Math.fround(1 / Math.sqrt(length2)) : 1;
                    return [Math.fround(speed * p[0] * scale), Math.fround(speed * p[1] * scale), other];
                }
                case 'Line': {
                    let d = config.volume.end.map((x, i) => x - config.volume.start[i]) as Vec3;
                    if (d.reduce((sum, x) => sum + x * x, 0) > Math.fround(0.0001)) d = normalize(d);
                    const a: Vec3 = [-d[1], d[0], 0];
                    const b: Vec3 = [-d[2] * d[0], -d[2] * d[1], d[0] * d[0] + d[1] * d[1]];
                    return a.map((x, i) => Math.fround(speed * x + other * b[i])) as Vec3;
                }
            }
        }
    }
}
