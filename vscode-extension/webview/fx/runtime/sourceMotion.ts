import type { Vec3 } from './types';

/** Explicit preview input, measured in logical 30 Hz frames relative to the scene source. */
export interface SourceMotion {
    playback: 'once' | 'loop' | 'pingPong';
    points: { frame: number; offset: Vec3 }[];
}

/** Validate and own the small path instead of retaining caller-owned arrays. */
export function parseSourceMotion(value: unknown): SourceMotion | undefined {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
    const input = value as Record<string, unknown>;
    if (input.playback !== 'once' && input.playback !== 'loop' && input.playback !== 'pingPong') return undefined;
    if (!Array.isArray(input.points) || input.points.length < 2 || input.points.length > 64) return undefined;
    const points: SourceMotion['points'] = [];
    for (const entry of input.points) {
        if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return undefined;
        const point = entry as Record<string, unknown>, frame = point.frame, offset = point.offset;
        if (typeof frame !== 'number' || !Number.isInteger(frame) || frame < 0 || frame > 18_000
            || (points.length > 0 && frame <= points[points.length - 1].frame)) return undefined;
        if (!Array.isArray(offset) || offset.length !== 3
            || ![0, 1, 2].every(axis => typeof offset[axis] === 'number' && Number.isFinite(offset[axis]) && Math.abs(offset[axis]) <= 1e6)) return undefined;
        points.push({ frame, offset: [offset[0], offset[1], offset[2]] });
    }
    if (points[0].frame !== 0 || points[0].offset.some(v => v !== 0)) return undefined;
    if (input.playback === 'loop' && points.at(-1)!.offset.some((v, axis) => v !== points[0].offset[axis])) return undefined;
    return { playback: input.playback, points };
}

/** Sample relative translation; fractions support display between fixed simulation ticks. */
export function sampleSourceMotion(motion: SourceMotion | undefined, frame: number): Vec3 {
    if (!motion || !Number.isFinite(frame)) return [0, 0, 0];
    const points = motion.points, end = points[points.length - 1], duration = end.frame;
    let time = Math.max(0, frame);
    if (motion.playback === 'once') time = Math.min(time, duration);
    else if (motion.playback === 'loop') time %= duration;
    else { time %= duration * 2; if (time > duration) time = duration * 2 - time; }
    if (time >= duration) return [...end.offset];
    let index = 1;
    while (points[index].frame < time) index++;
    const left = points[index - 1], right = points[index], amount = (time - left.frame) / (right.frame - left.frame);
    return left.offset.map((value, axis) => value + (right.offset[axis] - value) * amount) as Vec3;
}
