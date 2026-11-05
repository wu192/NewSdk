import type { BirthRecord, ParticleConfig, ParticleSample, TrailPoint, Vec2, Vec3, Vec4 } from '../runtime/types';

export interface SpecialCamera { right: Vec3; up: Vec3; eye: Vec3; backward: Vec3; }
export interface SpecialGeometry { vertices: Float32Array; particles: number; }
/** World xyz, texture uv, straight RGBA and environment uv. */
export const specialVertexFloats = 11;
const epsilon = 1e-8;
const dot = (a: Vec3, b: Vec3): number => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const subtract = (a: Vec3, b: Vec3): Vec3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cross = (a: Vec3, b: Vec3): Vec3 => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const scale = (v: Vec3, amount: number): Vec3 => v.map(x => x * amount) as Vec3;
const add = (a: Vec3, b: Vec3): Vec3 => a.map((x, i) => x + b[i]) as Vec3;
const clamp = (value: number): number => Math.max(0, Math.min(1, value));
const finite = (values: readonly number[]): void => {
    if (values.some(value => !Number.isFinite(value) || !Number.isFinite(Math.fround(value)))) {
        throw new Error('特殊粒子几何包含非有限或超出 float32 范围的状态。');
    }
};
const normalize = (value: Vec3, fallback: Vec3): Vec3 => {
    const length = Math.hypot(...value);
    return length > epsilon ? scale(value, 1 / length) : [...fallback];
};

class VertexWriter {
    private data: Float32Array;
    private used = 0;
    constructor(readonly maximum: number) { this.data = new Float32Array(Math.min(maximum * specialVertexFloats, 4096)); }
    get count(): number { return this.used / specialVertexFloats; }
    reserve(count: number): void {
        const wanted = this.used + count * specialVertexFloats;
        if (wanted > this.maximum * specialVertexFloats) throw new Error('特殊粒子几何超过本帧顶点预算；请减少粒子数或拖尾寿命。');
        if (wanted > this.data.length) {
            const expanded = new Float32Array(Math.min(this.maximum * specialVertexFloats, Math.max(wanted, this.data.length * 2)));
            expanded.set(this.data); this.data = expanded;
        }
    }
    vertex(position: Vec3, uv: Vec2, color: Vec4, environment: Vec2 = [0, 0]): void {
        const values = [...position, ...uv, ...color, ...environment]; finite(values);
        this.data.set(values, this.used); this.used += specialVertexFloats;
    }
    result(): Float32Array { return this.data.slice(0, this.used); }
}

interface RibbonPoint { position: Vec3; size: number; color: Vec4; u: number; }
/** Shared edges at each center avoid cracks between neighboring strip segments. */
function ribbon(writer: VertexWriter, points: RibbonPoint[], camera: SpecialCamera, vTile: number, vScroll: number): void {
    if (points.length < 2) return;
    const edges: [Vec3, Vec3][] = [];
    let previousSide: Vec3 | undefined;
    for (let index = 0; index < points.length; index++) {
        const point = points[index], before = points[Math.max(0, index - 1)], after = points[Math.min(points.length - 1, index + 1)];
        const tangent = normalize(subtract(after.position, before.position), camera.up);
        let side = cross(tangent, subtract(camera.eye, point.position));
        if (Math.hypot(...side) < epsilon) side = cross(tangent, camera.up);
        side = normalize(side, camera.right);
        if (previousSide && dot(previousSide, side) < 0) side = scale(side, -1);
        previousSide = side;
        const half = scale(side, point.size * 0.5);
        edges.push([subtract(point.position, half), add(point.position, half)]);
    }
    for (let index = 1; index < points.length; index++) {
        const a = points[index - 1], b = points[index];
        if (Math.hypot(...subtract(a.position, b.position)) < epsilon || a.size === 0 && b.size === 0) continue;
        writer.reserve(6);
        const [al, ar] = edges[index - 1], [bl, br] = edges[index];
        const ua: Vec2 = [a.u, vScroll], va: Vec2 = [a.u, vTile + vScroll];
        const ub: Vec2 = [b.u, vScroll], vb: Vec2 = [b.u, vTile + vScroll];
        writer.vertex(al, ua, a.color); writer.vertex(ar, va, a.color); writer.vertex(br, vb, b.color);
        writer.vertex(al, ua, a.color); writer.vertex(br, vb, b.color); writer.vertex(bl, ub, b.color);
    }
}

function trail(writer: VertexWriter, config: ParticleConfig, birth: BirthRecord, frame: number, camera: SpecialCamera): void {
    const settings = config.trail;
    if (!settings || !birth.trail?.length || settings.life <= 0) return;
    finite([settings.life, settings.uTile, settings.vTile, settings.uScrollRate, settings.vScrollRate]);
    // Runtime retains drained tails; rendering intentionally does not require a live particle sample.
    const retained: TrailPoint[] = [];
    let lastFrame = -Infinity;
    for (const point of birth.trail) {
        finite([point.frame, ...point.position, point.size, ...point.color]);
        if (point.frame < lastFrame) throw new Error('拖尾历史时间顺序无效。');
        lastFrame = point.frame;
        if (point.frame < frame - settings.life || point.frame > frame) continue;
        if (retained.length && Math.hypot(...subtract(point.position, retained[retained.length - 1].position)) < epsilon) retained[retained.length - 1] = point;
        else retained.push(point);
    }
    if (retained.length < 2) return;
    let distance = 0;
    const width = Math.max(Math.abs(retained[retained.length - 1].size), 0.001);
    const scroll = settings.uScrollRate * frame / 30;
    const points = retained.map((point, index): RibbonPoint => {
        if (index) distance += Math.hypot(...subtract(point.position, retained[index - 1].position));
        const color = [...point.color] as Vec4;
        color[3] *= clamp(1 - (frame - point.frame) / settings.life);
        return { position: point.position, size: point.size, color, u: distance / width * settings.uTile + scroll };
    });
    ribbon(writer, points, camera, settings.vTile, settings.vScrollRate * frame / 30);
}

function lightning(writer: VertexWriter, birth: BirthRecord, sample: ParticleSample, frame: number, camera: SpecialCamera): void {
    const state = birth.lightning;
    if (!state || !sample.alive || sample.size === 0) return;
    finite([...state.start, ...state.end, sample.size, ...sample.color, birth.life, birth.creationFrame]);
    const displacement = subtract(state.end, state.start), length = Math.hypot(...displacement);
    if (length < epsilon) return;
    const axis = scale(displacement, 1 / length);
    const first = normalize(cross(axis, Math.abs(axis[2]) < 0.9 ? [0, 0, 1] : [0, 1, 0]), camera.right);
    const second = normalize(cross(axis, first), camera.up);
    const age = clamp((frame - birth.creationFrame) / Math.max(1, birth.life));
    const waves = state.randomSets.slice(0, 2).map(wave => {
        finite(Object.values(wave));
        return { amplitude: wave.startAmplitude + wave.endAmplitudeChange * age,
            frequency: wave.startFrequency + wave.endFrequencyChange * age, phase: wave.startPhase + wave.endPhaseChange * age };
    });
    const segments = 64, points: RibbonPoint[] = [];
    // Geometry is a reconstructed profile: sampled random state is immutable and rendering never consumes RNG.
    for (let index = 0; index <= segments; index++) {
        const t = index / segments;
        let position = add(state.start, scale(displacement, t));
        if (index > 0 && index < segments) for (let waveIndex = 0; waveIndex < waves.length; waveIndex++) {
            const wave = waves[waveIndex], amount = Math.sin(Math.PI * t) * wave.amplitude * Math.sin(t * wave.frequency * Math.PI * 2 + wave.phase);
            position = add(position, scale(waveIndex ? second : first, amount));
        }
        points.push({ position, size: sample.size, color: sample.color, u: t });
    }
    ribbon(writer, points, camera, 1, 0);
}

function environmentUv(normal: Vec3, position: Vec3, camera: SpecialCamera): Vec2 {
    const view = normalize(subtract(camera.eye, position), camera.backward);
    const reflection = subtract(scale(normal, 2 * dot(normal, view)), view);
    return [0.5 + Math.atan2(reflection[1], reflection[0]) / (Math.PI * 2), 0.5 - Math.asin(Math.max(-1, Math.min(1, reflection[2]))) / Math.PI];
}

function swarm(writer: VertexWriter, config: ParticleConfig, birth: BirthRecord, sample: ParticleSample, camera: SpecialCamera): void {
    const settings = config.swarm;
    if (!settings || !sample.alive || sample.size === 0) return;
    finite([...sample.position, sample.size, sample.angleZ, ...sample.color, ...birth.velocity,
        settings.opaqueSpeed, settings.transparentSpeed, settings.speedStretchAmount]);
    const speed = Math.hypot(...birth.velocity);
    const opacity = settings.opaqueSpeed === settings.transparentSpeed ? 1
        : clamp((settings.transparentSpeed - speed) / (settings.transparentSpeed - settings.opaqueSpeed));
    if (opacity <= 0) return;
    const x = dot(birth.velocity, camera.right), y = dot(birth.velocity, camera.up);
    const screenSpeed = Math.hypot(x, y);
    const along = screenSpeed > epsilon ? add(scale(camera.right, x / screenSpeed), scale(camera.up, y / screenSpeed))
        : add(scale(camera.right, Math.cos(sample.angleZ)), scale(camera.up, Math.sin(sample.angleZ)));
    const across = normalize(cross(camera.backward, along), camera.up);
    const halfWidth = Math.abs(sample.size) * 0.5;
    const halfLength = Math.max(0, Math.abs(sample.size) + speed * settings.speedStretchAmount) * 0.5;
    const color = [...sample.color] as Vec4; color[3] *= opacity;
    const corners = [[-1, -1], [1, -1], [1, 1], [-1, 1]];
    const positions = corners.map(([a, b]) => add(sample.position, add(scale(along, a * halfLength), scale(across, b * halfWidth))));
    const uvs: Vec2[] = [[0, 1], [1, 1], [1, 0], [0, 0]];
    const environments = corners.map(([a, b]) => environmentUv(normalize(add(scale(camera.backward, Math.SQRT1_2),
        add(scale(along, a * 0.5), scale(across, b * 0.5))), camera.backward), sample.position, camera));
    writer.reserve(6);
    for (const index of [0, 1, 2, 0, 2, 3]) writer.vertex(positions[index], uvs[index], color, environments[index]);
}

/** Build bounded dynamic triangles for special families without mutating particle or random state. */
export function buildSpecialGeometry(config: ParticleConfig, births: readonly BirthRecord[], frame: number,
    camera: SpecialCamera, sample: (birth: BirthRecord, frame: number) => ParticleSample, maxVertices = 500_000): SpecialGeometry {
    finite([frame, ...camera.right, ...camera.up, ...camera.eye, ...camera.backward]);
    if (!Number.isSafeInteger(maxVertices) || maxVertices < 0 || maxVertices > 1_000_000) throw new Error('特殊粒子顶点预算无效。');
    const writer = new VertexWriter(maxVertices);
    let particles = 0;
    let ordered = births;
    const samples = new Map<BirthRecord, ParticleSample>();
    if (config.drawModule === 'Swarm' && (config.shader === 'ALPHA' || config.sortParticles)) {
        for (const birth of births) samples.set(birth, sample(birth, frame));
        const depth = (birth: BirthRecord) => dot(subtract(samples.get(birth)!.position, camera.eye), camera.backward);
        ordered = births.slice().sort((a, b) => depth(a) - depth(b) || a.creationFrame - b.creationFrame || a.id - b.id);
    }
    for (const birth of ordered) {
        const before = writer.count;
        if (config.drawModule === 'Trail') trail(writer, config, birth, frame, camera);
        else if (config.drawModule === 'Lightning') lightning(writer, birth, sample(birth, frame), frame, camera);
        else if (config.drawModule === 'Swarm') swarm(writer, config, birth, samples.get(birth) ?? sample(birth, frame), camera);
        if (writer.count > before) particles++;
    }
    return { vertices: writer.result(), particles };
}
