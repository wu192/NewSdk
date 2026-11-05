import { FxRandom } from './random';
import { ParticleRuntime } from './runtime';
import type { ParticleConfig, RuntimeOptions } from './types';
export interface WorldSystem { id: string; config: ParticleConfig; options?: RuntimeOptions; }
/** Fixed system order and a shared FX stream; solo/hide belongs to rendering, never this scheduler. */
export class ParticleWorld {
    frame = -1;
    systems: { id: string; runtime: ParticleRuntime }[] = [];
    budgetExceeded: string | undefined;
    private request = 0;
    constructor(readonly definitions: readonly WorldSystem[], readonly seed = 1,
        readonly maxParticles = 100_000, readonly maxFrames = 18_000) { this.reset(); }
    private reset(): void {
        for (const system of this.systems) system.runtime.dispose();
        const random = new FxRandom(this.seed);
        this.systems = this.definitions.map(d => ({ id: d.id, runtime: new ParticleRuntime(d.config, this.seed,
            { ...d.options, random, maxFrames: this.maxFrames, maxParticles: this.maxParticles, snapshotBytes: 0 }) }));
        this.frame = -1; this.budgetExceeded = undefined;
    }
    step(): void {
        if (this.budgetExceeded) return;
        if (this.frame >= this.maxFrames) { this.budgetExceeded = '时间轴达到预览重放预算'; return; }
        let total = this.systems.reduce((sum, s) => sum + s.runtime.particles.length, 0);
        for (const s of this.systems) {
            const before = s.runtime.particles.length;
            s.runtime.options.maxParticles = Math.max(0, this.maxParticles - total + before);
            s.runtime.step(); total += s.runtime.particles.length - before;
            if (s.runtime.budgetExceeded) { this.budgetExceeded = s.runtime.budgetExceeded; return; }
        }
        this.frame++;
    }
    private target(frame: number): number {
        if (!Number.isFinite(frame) || frame < 0 || frame > this.maxFrames) throw new Error('时间轴目标超出预览预算');
        const target = Math.trunc(frame);
        if (target < this.frame || this.budgetExceeded) this.reset();
        return target;
    }
    seek(frame: number): void {
        this.request++; const target = this.target(frame);
        while (this.frame < target && !this.budgetExceeded) this.step();
    }
    async seekAsync(frame: number, signal?: AbortSignal): Promise<void> {
        const request = ++this.request, target = this.target(frame);
        while (this.frame < target && !this.budgetExceeded) {
            if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
            const deadline = performance.now() + 4;
            while (this.frame < target && !this.budgetExceeded && performance.now() < deadline) this.step();
            if (this.frame < target && !this.budgetExceeded) await new Promise<void>(resolve => setTimeout(resolve, 0));
        }
    }
    dispose(): void { this.request++; for (const s of this.systems) s.runtime.dispose(); this.systems.length = 0; }
}
