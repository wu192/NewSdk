import { FxRandom } from './random';
import { localRotationMatrix, sampleParticle, transformVector } from './math';
import { sampleVelocity, sampleVolume } from './emitter';
import { createCpuState, sampleCpuParticle, updateCpuParticle } from './cpu';
import { createSpecialState, expireTrail, isSpecialParticle, recordTrail, specialAcceleration, specialParticleCapacity, storedSpecialParticleBytes } from './special';
import type { BirthRecord, ParticleConfig, ParticleEventState, ParticleSample, RuntimeEventTrigger, RuntimeOptions, Vec3 } from './types';

export interface RuntimeSnapshot {
    frame: number; words: number[]; calls: number; initial: number; burst: number; remaining: number;
    radius: number; accumulator: number; nextId: number; alpha: number[]; particles: BirthRecord[];
    stopped: boolean; activeStart: number; eventStates: ParticleEventState[]; offset: Vec3; previousOffset: Vec3; hasEmitted: boolean;
}
const clone = <T>(value: T): T => structuredClone(value);
/** Conservative JS state accounting includes the nested eight-slot arrays, objects and event records. */
export const particleStateBytes = (config: ParticleConfig): number => (config.backend === 'CPU'
    ? 640 + (config.colors ? 1152 : 0) + (config.alphas || isSpecialParticle(config) ? 1024 : 0) + (config.events?.filter(e => e.perParticle).length ?? 0) * 96 : 192)
    + specialParticleCapacity(config);
const storedParticleBytes = (p: BirthRecord): number => (p.cpu
    ? 640 + (p.cpu.color ? 1152 : 0) + (p.cpu.alpha ? 1024 : 0) + (p.events?.length ?? 0) * 96 : 192) + storedSpecialParticleBytes(p);
export const runtimeSnapshotBytes = (s: RuntimeSnapshot): number => 640 + s.eventStates.length * 96
    + s.particles.reduce((n, p) => n + storedParticleBytes(p), 0);

/** Fixed 30 Hz preview schedule: one shared binder sample after each logical system update with live stock. */
export class ParticleRuntime {
    frame = -1;
    readonly particles: BirthRecord[] = [];
    readonly births: BirthRecord[] = [];
    readonly deaths: BirthRecord[] = [];
    readonly events: RuntimeEventTrigger[] = [];
    alphaSamples: number[] = [1, 1, 1, 1];
    budgetExceeded: string | undefined;
    readonly random: FxRandom;
    onBirth?: (particle: BirthRecord) => void;
    onDeath?: (particle: BirthRecord) => void;
    onEvent?: (event: RuntimeEventTrigger) => void;
    canAllocate?: (bytes: number) => boolean;
    private initial = 0;
    private burst = 0;
    private remaining = 0;
    private radius = 0;
    private accumulator = 0;
    private nextId = 0;
    private trackedParticleBytes = 0;
    private snapshots: RuntimeSnapshot[] = [];
    private request = 0;
    private stopped = false;
    private activeStart = -1;
    private previousOffset: Vec3 = [0, 0, 0];
    private hasEmitted = false;
    private eventStates: ParticleEventState[] = [];
    private readonly rotationMatrix: readonly number[];
    private readonly initialWords: Uint32Array;
    private readonly initialCalls: number;
    readonly options: RuntimeOptions & { maxParticles: number; maxFrames: number; snapshotBytes: number; maxParticleBytes: number; offset: Vec3; rotation: number; startFrame: number; groundHeight: number };
    constructor(readonly config: ParticleConfig, readonly seed = 1, options: RuntimeOptions = {}) {
        this.options = { ...options, maxParticles: options.maxParticles ?? 100_000, maxFrames: options.maxFrames ?? 18_000,
            snapshotBytes: options.snapshotBytes ?? 16 * 1024 * 1024, maxParticleBytes: options.maxParticleBytes ?? 16 * 1024 * 1024, offset: [...(options.offset ?? [0, 0, 0])],
            rotation: options.rotation ?? options.rotationXYZ?.[2] ?? 0, startFrame: options.startFrame ?? 0,
            groundHeight: options.groundHeight ?? 0 };
        this.rotationMatrix = options.rotationMatrix ?? localRotationMatrix(options.rotationXYZ ?? [0, 0, this.options.rotation]);
        this.random = options.random ?? new FxRandom(seed);
        this.initialWords = this.random.words.slice(); this.initialCalls = this.random.calls;
        this.reset();
    }
    private makeEvents(perParticle: boolean): ParticleEventState[] {
        return (this.config.events ?? []).flatMap((event, index) => event.perParticle !== perParticle ? [] : [{ index, armed: true,
            value: event.type === 'Life' ? Math.trunc(this.random.sample(event.eventTime)) : perParticle ? this.random.sample(event.heightOffset) : 0 }]);
    }
    private reset(): void {
        this.frame = -1; this.particles.length = 0; this.snapshots.length = 0; this.budgetExceeded = undefined;
        this.births.length = this.deaths.length = this.events.length = 0;
        this.random.words.set(this.initialWords); this.random.calls = this.initialCalls;
        this.initial = Math.trunc(this.random.sample(this.config.initialDelay));
        if (this.options.initialDelayOverrideMs) {
            const milliseconds = this.random.sample(this.options.initialDelayOverrideMs);
            const seconds = Math.fround(milliseconds * Math.fround(0.001));
            this.initial = Math.ceil(Math.fround(seconds * 30));
        }
        this.burst = 0; this.remaining = this.config.systemLifetime; this.radius = this.config.volume.radius;
        this.accumulator = 0; this.nextId = 0; this.trackedParticleBytes = 0; this.stopped = false;
        this.activeStart = this.options.creationFrame ?? this.options.startFrame;
        this.previousOffset = [...this.options.offset]; this.hasEmitted = false;
        this.eventStates = this.makeEvents(false);
        this.alphaSamples = this.config.alphas || this.config.colors && this.config.shader === 'MULTIPLY' ? [0, 0, 0, 0] : [1, 1, 1, 1];
    }
    sample(particle: BirthRecord, frame = this.frame): ParticleSample {
        return particle.cpu ? sampleCpuParticle(particle) : sampleParticle(particle, this.config, frame - particle.creationFrame, this.alphaSamples);
    }
    stopEmission(): void { this.stopped = true; }
    get emissionStopped(): boolean { return this.stopped; }
    get finished(): boolean {
        const windowClosed = this.options.systemLifeFrames !== undefined && this.frame - this.options.startFrame >= this.options.systemLifeFrames;
        return this.particles.length === 0 && (this.stopped || windowClosed || this.config.systemLifetime > 0 && this.remaining <= 0);
    }
    private retire(particle: BirthRecord): void { this.deaths.push(particle); this.onDeath?.(particle); }
    private updateEvents(states: ParticleEventState[], position: Vec3, age: number, particle?: BirthRecord): void {
        for (const state of states) {
            const event = this.config.events![state.index];
            if (!state.armed || !event.fx) continue;
            if (event.type === 'Life' ? age < state.value : position[2] > this.options.groundHeight) continue;
            state.armed = false;
            const point = position.slice() as Vec3;
            if (event.type === 'Collision' && particle) point[2] = Math.fround(point[2] + state.value);
            // Life passes NULL matrix. Collision's normal profile is identity with optional XY velocity heading.
            const rotationXYZ: Vec3 = [0, 0, event.type === 'Collision' && event.orientToTerrain && particle
                ? Math.atan2(particle.velocity[1], particle.velocity[0]) : 0];
            const trigger = { fx: event.fx, position: point, rotationXYZ, particleId: particle?.id };
            if (this.canAllocate && !this.canAllocate(160)) { this.budgetExceeded = '事件状态超过 World 内存预算'; return; }
            this.events.push(trigger); this.onEvent?.(trigger);
            if (this.budgetExceeded) return;
            // System events write remaining=1; they do not set the stop-emission flag.
            // In particular, the SystemLifetime=0 infinite flag bypasses the normal decrement.
            if (event.kill) { if (particle?.cpu) particle.cpu.remaining = 1; else this.remaining = 1; }
        }
    }
    step(): void { this.advance(true); }
    private advance(allowSnapshot: boolean): void {
        if (this.budgetExceeded) return;
        if (this.frame >= this.options.maxFrames) { this.budgetExceeded = '时间轴达到预览重放预算'; return; }
        this.births.length = this.deaths.length = this.events.length = 0;
        this.trackedParticleBytes = this.particles.length * particleStateBytes(this.config);
        const frame = ++this.frame, c = this.config;
        let write = 0;
        for (const p of this.particles) {
            if (!p.cpu && frame - p.creationFrame > p.life) this.retire(p); else this.particles[write++] = p;
        }
        this.particles.length = write;
        if (frame < this.options.startFrame) return;
        if (this.initial !== 0) { this.initial--; if (this.initial === 0) this.activeStart = frame; return; }
        const elapsed = frame - this.options.startFrame;
        const systemLifeOpen = this.options.systemLifeFrames === undefined || elapsed < this.options.systemLifeFrames;
        if (!this.stopped && systemLifeOpen && (c.systemLifetime === 0 || this.remaining > 0)) {
            if (this.burst === 0) {
                const count = Math.max(0, Math.trunc(this.random.sample(c.burstCount)));
                if (this.particles.length + count > this.options.maxParticles || this.particleBytes + count * particleStateBytes(c) > this.options.maxParticleBytes) {
                    this.budgetExceeded = '预览预算已达上限；系统暂停（未修改 XML）'; return;
                }
                for (let i = 0; i < count; i++) {
                    if (this.canAllocate && !this.canAllocate(particleStateBytes(c))) {
                        this.budgetExceeded = '粒子状态超过 World 内存预算'; return;
                    }
                    const localPosition = sampleVolume(c.volume, this.random, this.radius);
                    const localVelocity = sampleVelocity(c, localPosition, this.random);
                    const life = Math.trunc(this.random.sample(c.lifetime));
                    this.accumulator = Math.min(50, Math.fround(this.accumulator + this.random.sample(c.startSizeRate)));
                    const position = transformVector(localPosition, this.rotationMatrix), velocity = transformVector(localVelocity, this.rotationMatrix);
                    // Constructor flag 0x40 resets history on the first candidate. Later batches
                    // interpolate the emitter translation with u=1-i/N (6BEEA0), retaining candidate index.
                    if (!this.hasEmitted) { this.previousOffset = [...this.options.offset]; this.hasEmitted = true; }
                    const u = Math.fround(1 - Math.fround(i / count));
                    for (let axis = 0; axis < 3; axis++) {
                        position[axis] += this.options.offset[axis];
                        const delta = Math.fround(this.options.offset[axis] - this.previousOffset[axis]);
                        if (delta !== 0) position[axis] = Math.fround(position[axis] - Math.fround(u * delta));
                    }
                    if (c.backend === 'CPU') for (let axis = 0; axis < 3; axis++) { position[axis] = Math.fround(position[axis]); velocity[axis] = Math.fround(velocity[axis]); }
                    const cpu = c.backend === 'CPU' ? createCpuState(c, life, this.accumulator, this.random) : undefined;
                    const particle: BirthRecord = { id: this.nextId++, position, velocity, life, creationFrame: frame,
                        seed: cpu ? 0 : this.random.range(0, 15), ...(cpu ? { cpu, events: this.makeEvents(true) } : {}) };
                    createSpecialState(particle, c, this.random, this.rotationMatrix);
                    // The birth-to-first-update interval is visible even for a one-frame moving head.
                    if (particle.cpu && particle.trail) recordTrail(particle, c, frame - 1, sampleCpuParticle(particle));
                    this.trackedParticleBytes += particleStateBytes(c);
                    this.particles.push(particle); this.births.push(particle); this.onBirth?.(particle);
                    if (this.budgetExceeded) return;
                }
                this.burst = Math.trunc(this.random.sample(c.burstDelay));
            } else if (c.isOneShot) this.burst = 1;
            else this.burst--;
        }
        if (c.volume.type === 'Cylinder') this.radius = Math.max(0.1, Math.fround(this.radius + c.volume.radiusRate));
        // Core 6D1AFC…6D1B62 emits, updates system modules/events, then updates particle storage.
        this.updateEvents(this.eventStates, this.options.offset, frame - this.activeStart);
        if (this.budgetExceeded) return;
        write = 0;
        for (const p of this.particles) {
            if (p.cpu) {
                expireTrail(p, c, frame);
                if (p.cpu.remaining <= 0) {
                    if (p.trail?.length) this.particles[write++] = p;
                    continue;
                }
                if (!updateCpuParticle(p, c, specialAcceleration(p, c, this.options.offset))) { this.budgetExceeded = 'CPU 递推超过有限 float32 运算预算；保留上一有效状态'; return; }
                this.updateEvents(p.events ?? [], p.position, frame - p.creationFrame, p);
                if (this.budgetExceeded) return;
                recordTrail(p, c, frame, sampleCpuParticle(p));
                p.cpu.remaining--;
                if (p.cpu.remaining <= 0) { this.retire(p); if (!p.trail?.length) continue; }
            }
            this.particles[write++] = p;
        }
        this.particles.length = write;
        if (isSpecialParticle(c)) this.trackedParticleBytes = this.particles.length * particleStateBytes(c);
        if (c.systemLifetime !== 0 && this.remaining !== 0) this.remaining--;
        this.previousOffset = [...this.options.offset];
        if (c.alphas && this.particles.length > 0) for (let i = 0; i < 4; i++) this.alphaSamples[i] = this.random.sample(c.alphas[i].range);
        if (allowSnapshot && frame % 30 === 0) this.saveSnapshot();
    }
    captureState(): RuntimeSnapshot {
        return { frame: this.frame, words: Array.from(this.random.words), calls: this.random.calls,
            initial: this.initial, burst: this.burst, remaining: this.remaining, radius: this.radius,
            accumulator: this.accumulator, nextId: this.nextId, alpha: this.alphaSamples.slice(), particles: clone(this.particles),
            stopped: this.stopped, activeStart: this.activeStart, eventStates: clone(this.eventStates), offset: [...this.options.offset],
            previousOffset: [...this.previousOffset], hasEmitted: this.hasEmitted };
    }
    restoreState(s: RuntimeSnapshot): void {
        this.frame = s.frame; this.random.words.set(s.words); this.random.calls = s.calls;
        this.initial = s.initial; this.burst = s.burst; this.remaining = s.remaining; this.radius = s.radius;
        this.accumulator = s.accumulator; this.nextId = s.nextId; this.alphaSamples = s.alpha.slice();
        this.stopped = s.stopped; this.activeStart = s.activeStart; this.eventStates = clone(s.eventStates); this.options.offset = [...s.offset];
        this.previousOffset = [...s.previousOffset]; this.hasEmitted = s.hasEmitted;
        this.particles.length = 0; for (const p of clone(s.particles)) this.particles.push(p);
        this.trackedParticleBytes = s.particles.length * particleStateBytes(this.config);
        this.births.length = this.deaths.length = this.events.length = 0; this.budgetExceeded = undefined;
    }
    private saveSnapshot(): void {
        if (this.options.snapshotBytes <= 0) return;
        const s = this.captureState();
        if (runtimeSnapshotBytes(s) > this.options.snapshotBytes) return;
        this.snapshots.push(s);
        let bytes = this.snapshotBytes;
        while (this.snapshots.length > 1 && bytes > this.options.snapshotBytes) bytes -= runtimeSnapshotBytes(this.snapshots.shift()!);
    }
    get startSizeAccumulator(): number { return this.accumulator; }
    get particleBytes(): number { return this.trackedParticleBytes; }
    get snapshotCount(): number { return this.snapshots.length; }
    get snapshotBytes(): number { return this.snapshots.reduce((sum, s) => sum + runtimeSnapshotBytes(s), 0); }
    private prepareSeek(frame: number): number {
        if (!Number.isFinite(frame) || frame < 0 || frame > this.options.maxFrames) throw new Error('时间轴目标超出预览预算');
        const target = Math.trunc(frame);
        if (target < this.frame || this.budgetExceeded) {
            const snapshot = this.snapshots.filter(s => s.frame <= target).at(-1);
            if (snapshot) this.restoreState(snapshot); else this.reset();
            this.snapshots = this.snapshots.filter(s => s.frame <= this.frame);
        }
        return target;
    }
    seek(frame: number): void {
        this.request++;
        const target = this.prepareSeek(frame);
        while (this.frame < target && !this.budgetExceeded) this.advance(true);
    }
    async seekAsync(frame: number, signal?: AbortSignal): Promise<void> {
        if (signal?.aborted) throw new Error('预览重放已取消');
        const request = ++this.request, target = this.prepareSeek(frame);
        while (this.frame < target && !this.budgetExceeded) {
            if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
            const deadline = performance.now() + 4;
            while (this.frame < target && !this.budgetExceeded && performance.now() < deadline) this.advance(true);
            if (this.frame < target && !this.budgetExceeded) await new Promise<void>(resolve => setTimeout(resolve, 0));
        }
        if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
    }
    dispose(): void { this.request++; this.particles.length = 0; this.snapshots.length = 0; this.births.length = this.deaths.length = this.events.length = 0; this.trackedParticleBytes = 0; }
}
