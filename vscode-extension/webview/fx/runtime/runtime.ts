import { FxRandom } from './random';
import { localRotationMatrix, transformVector } from './math';
import { sampleVelocity, sampleVolume } from './emitter';
import type { BirthRecord, ParticleConfig, RuntimeOptions, Vec3 } from './types';

interface Snapshot {
    frame: number; words: number[]; calls: number; initial: number; burst: number; remaining: number;
    radius: number; accumulator: number; nextId: number; alpha: number[]; particles: BirthRecord[];
}

/** Local 30 Hz replay. Binder sampling is scheduled once after each logical update with live particles. */
export class ParticleRuntime {
    frame = -1;
    readonly particles: BirthRecord[] = [];
    alphaSamples: number[] = [1, 1, 1, 1];
    budgetExceeded: string | undefined;
    readonly random: FxRandom;
    private initial = 0;
    private burst = 0;
    private remaining = 0;
    private radius = 0;
    private accumulator = 0;
    private nextId = 0;
    private snapshots: Snapshot[] = [];
    private request = 0;
    private readonly rotationMatrix: readonly number[];
    private readonly initialWords: Uint32Array;
    private readonly initialCalls: number;
    readonly options: Required<Omit<RuntimeOptions, 'systemLifeFrames' | 'rotationXYZ' | 'random' | 'initialDelayOverrideMs'>> & Pick<RuntimeOptions, 'systemLifeFrames' | 'rotationXYZ' | 'initialDelayOverrideMs'>;
    constructor(readonly config: ParticleConfig, readonly seed = 1, options: RuntimeOptions = {}) {
        this.options = { maxParticles: options.maxParticles ?? 100_000, maxFrames: options.maxFrames ?? 18_000,
            snapshotBytes: options.snapshotBytes ?? 16 * 1024 * 1024, offset: options.offset ?? [0, 0, 0],
            rotation: options.rotation ?? options.rotationXYZ?.[2] ?? 0, rotationXYZ: options.rotationXYZ, startFrame: options.startFrame ?? 0,
            systemLifeFrames: options.systemLifeFrames, initialDelayOverrideMs: options.initialDelayOverrideMs };
        this.rotationMatrix = localRotationMatrix(options.rotationXYZ ?? [0, 0, this.options.rotation]);
        this.random = options.random ?? new FxRandom(seed);
        this.initialWords = this.random.words.slice(); this.initialCalls = this.random.calls;
        this.reset();
    }
    private reset(): void {
        this.frame = -1; this.particles.length = 0; this.snapshots.length = 0; this.budgetExceeded = undefined;
        this.random.words.set(this.initialWords); this.random.calls = this.initialCalls;
        this.initial = Math.trunc(this.random.sample(this.config.initialDelay));
        if (this.options.initialDelayOverrideMs) {
            // FXList normal GameLogic/rate=1 branch. Preserve template-constructor sampling first.
            const milliseconds = this.random.sample(this.options.initialDelayOverrideMs);
            const seconds = Math.fround(milliseconds * Math.fround(0.001));
            this.initial = Math.ceil(Math.fround(seconds * 30));
        }
        this.burst = 0;
        this.remaining = this.config.systemLifetime; this.radius = this.config.volume.radius;
        this.accumulator = 0; this.nextId = 0;
        this.alphaSamples = this.config.alphas || this.config.colors && this.config.shader === 'MULTIPLY' ? [0, 0, 0, 0] : [1, 1, 1, 1];
    }
    step(): void { this.advance(true); }
    private advance(allowSnapshot: boolean): void {
        if (this.budgetExceeded) return;
        if (this.frame >= this.options.maxFrames) { this.budgetExceeded = '时间轴达到预览重放预算'; return; }
        const frame = ++this.frame, c = this.config;
        // Expiration is strict: the shader still retains age == lifetime.
        let write = 0;
        for (let i = 0; i < this.particles.length; i++) {
            const p = this.particles[i]; if (frame - p.creationFrame <= p.life) this.particles[write++] = p;
        }
        this.particles.length = write;
        if (frame < this.options.startFrame) return;
        if (this.initial !== 0) { this.initial--; return; }
        const elapsed = frame - this.options.startFrame;
        const systemLifeOpen = this.options.systemLifeFrames === undefined || elapsed < this.options.systemLifeFrames;
        if (systemLifeOpen && (c.systemLifetime === 0 || this.remaining > 0)) {
            if (this.burst === 0) {
                const count = Math.trunc(this.random.sample(c.burstCount));
                if (this.particles.length + count > this.options.maxParticles) {
                    this.budgetExceeded = '预览预算已达上限；系统暂停（未修改 XML）'; return;
                }
                for (let i = 0; i < count; i++) {
                    const localPosition = sampleVolume(c.volume, this.random, this.radius);
                    const localVelocity = sampleVelocity(c, localPosition, this.random);
                    const life = Math.trunc(this.random.sample(c.lifetime));
                    this.accumulator = Math.min(50, Math.fround(this.accumulator + this.random.sample(c.startSizeRate)));
                    const position = transformVector(localPosition, this.rotationMatrix), velocity = transformVector(localVelocity, this.rotationMatrix);
                    for (let axis = 0; axis < 3; axis++) position[axis] += this.options.offset[axis];
                    const seed = this.random.range(0, 15);
                    this.particles.push({ id: this.nextId++, position, velocity, life, creationFrame: frame, seed });
                }
                this.burst = Math.trunc(this.random.sample(c.burstDelay));
            } else if (c.isOneShot) this.burst = 1;
            else this.burst--;
        }
        if (c.volume.type === 'Cylinder') this.radius = Math.max(0.1, Math.fround(this.radius + c.volume.radiusRate));
        if (c.systemLifetime !== 0 && this.remaining !== 0) this.remaining--;
        if (c.alphas && this.particles.length > 0) {
            for (let i = 0; i < 4; i++) this.alphaSamples[i] = this.random.sample(c.alphas[i].range);
        }
        if (allowSnapshot && frame % 30 === 0) this.saveSnapshot();
    }
    private capture(): Snapshot {
        return { frame: this.frame, words: Array.from(this.random.words), calls: this.random.calls,
            initial: this.initial, burst: this.burst, remaining: this.remaining, radius: this.radius,
            accumulator: this.accumulator, nextId: this.nextId, alpha: this.alphaSamples.slice(), particles: this.particles.slice() };
    }
    private restore(s: Snapshot): void {
        this.frame = s.frame; this.random.words.set(s.words); this.random.calls = s.calls;
        this.initial = s.initial; this.burst = s.burst; this.remaining = s.remaining; this.radius = s.radius;
        this.accumulator = s.accumulator; this.nextId = s.nextId; this.alphaSamples = s.alpha.slice();
        this.particles.length = 0; for (const p of s.particles) this.particles.push(p); this.budgetExceeded = undefined;
    }
    private saveSnapshot(): void {
        const s = this.capture(), cost = (v: Snapshot) => 256 + 192 * v.particles.length;
        if (cost(s) > this.options.snapshotBytes) return;
        this.snapshots.push(s);
        let bytes = this.snapshots.reduce((sum, v) => sum + cost(v), 0);
        while (this.snapshots.length > 1 && bytes > this.options.snapshotBytes) bytes -= cost(this.snapshots.shift()!);
    }
    get startSizeAccumulator(): number { return this.accumulator; }
    get snapshotCount(): number { return this.snapshots.length; }
    get snapshotBytes(): number { return this.snapshots.reduce((sum, v) => sum + 256 + 192 * v.particles.length, 0); }
    private prepareSeek(frame: number): number {
        if (!Number.isFinite(frame) || frame < 0 || frame > this.options.maxFrames) throw new Error('时间轴目标超出预览预算');
        const target = Math.trunc(frame);
        if (target < this.frame || this.budgetExceeded) {
            const snapshot = this.snapshots.filter(s => s.frame <= target).at(-1);
            if (snapshot) this.restore(snapshot); else this.reset();
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
        const request = ++this.request, target = this.prepareSeek(frame);
        while (this.frame < target && !this.budgetExceeded) {
            if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
            const deadline = performance.now() + 4;
            while (this.frame < target && !this.budgetExceeded && performance.now() < deadline) this.advance(true);
            if (this.frame < target && !this.budgetExceeded) await new Promise<void>(resolve => setTimeout(resolve, 0));
        }
    }
    dispose(): void { this.request++; this.particles.length = 0; this.snapshots.length = 0; }
}
