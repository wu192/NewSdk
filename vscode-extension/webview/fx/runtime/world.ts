import { FxRandom } from './random';
import { localRotationMatrix, transformVector } from './math';
import { ParticleRuntime, runtimeSnapshotBytes, type RuntimeSnapshot } from './runtime';
import type { BirthRecord, ParticleConfig, RuntimeEventTrigger, RuntimeOptions, Vec3 } from './types';
export interface WorldSystem { id: string; config: ParticleConfig; options?: RuntimeOptions; templateKey?: string; sourceId?: string; createAtGroundHeight?: boolean; orientToObject?: boolean; }
export interface WorldEventList {
    key: string; systems: readonly WorldSystem[]; cullTrackingFrames: number; cullTrackingMin: number; cullTrackingMax: number; triggered: boolean;
}
export interface WorldSchedulingOptions {
    templates?: readonly WorldSystem[]; fxLists?: readonly WorldEventList[];
    initialFxLists?: readonly string[];
    maxSystems?: number; maxEventsPerFrame?: number; maxDepth?: number; maxCullRecords?: number; snapshotBytes?: number; maxStateBytes?: number; groundHeight?: number;
}
export interface WorldActiveSystem {
    id: string; runtime: ParticleRuntime; templateKey?: string; sourceId?: string; depth: number; dynamic: boolean;
    parent?: { systemId: string; particleId: number };
}
interface SystemState { definition: WorldSystem; id: string; depth: number; dynamic: boolean; parent?: WorldActiveSystem['parent']; state: RuntimeSnapshot; }
interface WorldSnapshot {
    frame: number; words: number[]; calls: number; nextId: number; systems: SystemState[]; pending: SystemState[];
    cull: [string, number[]][]; handles: [string, string | null][];
}
const clone = <T>(value: T): T => structuredClone(value);
const multiply = (a: readonly number[], b: readonly number[]): number[] => Array.from({ length: 9 }, (_, i) =>
    a[Math.trunc(i / 3) * 3] * b[i % 3] + a[Math.trunc(i / 3) * 3 + 1] * b[3 + i % 3] + a[Math.trunc(i / 3) * 3 + 2] * b[6 + i % 3]);
const handleKey = (systemId: string, particleId: number): string => `${systemId}\0${particleId}`;
const systemStateBytes = (config: ParticleConfig): number => 1536 + (config.events?.filter(e => !e.perParticle).length ?? 0) * 96;
const snapshotCost = (s: WorldSnapshot): number => 320 + [...s.systems, ...s.pending].reduce((n, v) => n + 256 + runtimeSnapshotBytes(v.state), 0)
    + s.cull.reduce((n, [key, times]) => n + key.length * 2 + 64 + times.length * 8, 0)
    + s.handles.reduce((n, [key, value]) => n + 64 + key.length * 2 + (value?.length ?? 0) * 2, 0);

/** Shared stream, bounded manager queue, and whole-world snapshots. New systems never tick recursively. */
export class ParticleWorld {
    frame = -1;
    systems: WorldActiveSystem[] = [];
    budgetExceeded: string | undefined;
    /** Null preserves the distinction between an invalidated child handle and no handle. */
    readonly attachmentHandles = new Map<string, string | null>();
    private request = 0;
    private random = new FxRandom();
    private pending: WorldActiveSystem[] = [];
    private definitionsById = new Map<string, WorldSystem>();
    private cull = new Map<string, number[]>();
    private nextId = 0;
    private eventsThisFrame = 0;
    private snapshots: WorldSnapshot[] = [];
    readonly scheduling: Required<Pick<WorldSchedulingOptions, 'maxSystems' | 'maxEventsPerFrame' | 'maxDepth' | 'maxCullRecords' | 'snapshotBytes' | 'maxStateBytes' | 'groundHeight'>> & WorldSchedulingOptions;
    constructor(readonly definitions: readonly WorldSystem[], readonly seed = 1,
        readonly maxParticles = 100_000, readonly maxFrames = 18_000, scheduling: WorldSchedulingOptions = {}) {
        this.scheduling = { ...scheduling, maxSystems: scheduling.maxSystems ?? Math.max(128, definitions.length),
            maxEventsPerFrame: scheduling.maxEventsPerFrame ?? 256, maxDepth: scheduling.maxDepth ?? 16,
            maxCullRecords: scheduling.maxCullRecords ?? 4096, snapshotBytes: scheduling.snapshotBytes ?? 16 * 1024 * 1024,
            maxStateBytes: scheduling.maxStateBytes ?? 16 * 1024 * 1024, groundHeight: scheduling.groundHeight ?? 0 };
        for (const key of ['maxSystems', 'maxEventsPerFrame', 'maxDepth', 'maxCullRecords', 'snapshotBytes', 'maxStateBytes'] as const)
            if (!Number.isFinite(this.scheduling[key]) || this.scheduling[key] < 0) throw new Error('World 调度预算必须是有限非负数');
        this.reset();
    }
    private reset(): void {
        for (const s of [...this.systems, ...this.pending]) s.runtime.dispose();
        this.random = new FxRandom(this.seed); this.systems = []; this.pending = []; this.definitionsById.clear(); this.cull.clear();
        this.attachmentHandles.clear(); this.nextId = 0; this.frame = -1; this.budgetExceeded = undefined; this.snapshots = [];
        if (this.definitions.length > this.scheduling.maxSystems || this.definitions.reduce((n, d) => n + systemStateBytes(d.config), 0) > this.scheduling.maxStateBytes) { this.limit('系统实例超过 World 预览预算'); return; }
        this.frame = 0;
        for (const key of this.scheduling.initialFxLists ?? []) {
            const list = this.scheduling.fxLists?.find(list => list.key === key);
            if (list) this.permitted(list);
        }
        this.frame = -1;
        for (const d of this.definitions) this.systems.push(this.create(d, d.id, 0, false));
    }
    private create(d: WorldSystem, id: string, depth: number, dynamic: boolean, parent?: WorldActiveSystem['parent'], restoring?: RuntimeSnapshot): WorldActiveSystem {
        const runtime = new ParticleRuntime(d.config, this.seed, { ...d.options,
            random: restoring ? new FxRandom(this.seed) : this.random, maxFrames: this.maxFrames, maxParticles: this.maxParticles,
            groundHeight: d.options?.groundHeight ?? this.scheduling.groundHeight, snapshotBytes: 0 });
        if (restoring) {
            (runtime as { random: FxRandom }).random = this.random;
            runtime.restoreState(restoring);
        }
        const s: WorldActiveSystem = { id, runtime, templateKey: d.templateKey, sourceId: d.sourceId ?? d.id, depth, dynamic, parent };
        this.definitionsById.set(id, d);
        runtime.onBirth = p => { this.attach(s, p); if (this.budgetExceeded) runtime.budgetExceeded = this.budgetExceeded; };
        runtime.onDeath = p => this.detachParticle(s.id, p.id);
        runtime.onEvent = e => { this.dispatch(s, e); if (this.budgetExceeded) runtime.budgetExceeded = this.budgetExceeded; };
        runtime.canAllocate = bytes => this.stats.stateBytes + bytes <= this.scheduling.maxStateBytes;
        return s;
    }
    private limit(message: string): void { this.budgetExceeded = message; }
    private queue(d: WorldSystem, depth: number, parent?: WorldActiveSystem['parent']): WorldActiveSystem | undefined {
        if (depth > this.scheduling.maxDepth) { this.limit('局部事件/附属层级超过预览预算'); return; }
        if (this.systems.length + this.pending.length >= this.scheduling.maxSystems) { this.limit('动态系统实例超过预览预算'); return; }
        if (this.stats.stateBytes + systemStateBytes(d.config) + (parent ? 256 : 0) > this.scheduling.maxStateBytes) { this.limit('动态系统状态超过 World 内存预算'); return; }
        const queued = { ...d, options: { ...d.options, creationFrame: this.frame } };
        const s = this.create(queued, `dynamic:${this.nextId++}`, depth, true, parent);
        s.runtime.frame = this.frame; this.pending.push(s); return s;
    }
    private attach(parent: WorldActiveSystem, particle: BirthRecord): void {
        const key = parent.runtime.config.attachedSystem;
        if (!key || !particle.cpu || this.budgetExceeded) return;
        const d = this.scheduling.templates?.find(d => (d.templateKey ?? d.id) === key);
        if (!d) { this.limit(`附属模板未解析：${key}`); return; }
        const child = this.queue({ ...d, options: { ...d.options, offset: [...particle.position], startFrame: this.frame + 1 } },
            parent.depth + 1, { systemId: parent.id, particleId: particle.id });
        if (child) this.attachmentHandles.set(handleKey(parent.id, particle.id), child.id);
    }
    private detachParticle(systemId: string, particleId: number): void {
        const key = handleKey(systemId, particleId), childId = this.attachmentHandles.get(key);
        if (childId) {
            const child = [...this.systems, ...this.pending].find(s => s.id === childId);
            if (child) { child.parent = undefined; child.runtime.stopEmission(); }
        }
        this.attachmentHandles.delete(key);
    }
    private destroy(s: WorldActiveSystem): void {
        if (s.parent) this.attachmentHandles.set(handleKey(s.parent.systemId, s.parent.particleId), null);
        s.parent = undefined; s.runtime.dispose(); this.definitionsById.delete(s.id);
    }
    private permitted(list: WorldEventList): boolean {
        if (list.cullTrackingFrames === 0) return true;
        if (!this.cull.has(list.key) && this.stats.stateBytes + 128 > this.scheduling.maxStateBytes) {
            this.limit('EventFX 状态超过 World 内存预算'); return false;
        }
        const times = this.cull.get(list.key) ?? [], cutoff = Math.fround(this.frame) - list.cullTrackingFrames;
        let expired = 0; while (expired < times.length && times[expired] < cutoff) expired++;
        if (expired) times.splice(0, expired); this.cull.set(list.key, times);
        const n = times.length;
        if (n > list.cullTrackingMax) return false;
        if (n > list.cullTrackingMin) {
            const divisor = list.cullTrackingMax - list.cullTrackingMin;
            if (divisor === 0) { this.limit('EventFX CullTracking 阈值导致整数除零'); return false; }
            if (this.random.integer(0, Math.trunc((n - list.cullTrackingMin) / divisor)) === 0) return false;
        }
        if ([...this.cull.values()].reduce((sum, v) => sum + v.length, 0) >= this.scheduling.maxCullRecords) {
            this.limit('EventFX 时间窗记录超过预览预算'); return false;
        }
        if (this.stats.stateBytes + 144 > this.scheduling.maxStateBytes) { this.limit('EventFX 状态超过 World 内存预算'); return false; }
        times.push(this.frame); return true;
    }
    private dispatch(source: WorldActiveSystem, event: RuntimeEventTrigger): void {
        if (this.budgetExceeded) return;
        if (++this.eventsThisFrame > this.scheduling.maxEventsPerFrame) { this.limit('单逻辑帧 EventFX 尝试超过预览预算'); return; }
        const list = this.scheduling.fxLists?.find(list => list.key === event.fx);
        if (!list) { this.limit(`事件 FXList 未解析：${event.fx}`); return; }
        if (!this.permitted(list) || !list.triggered) return;
        const parentRotation = localRotationMatrix(event.rotationXYZ);
        for (const d of list.systems) {
            if (this.budgetExceeded) break;
            const local = d.options?.rotationMatrix ?? localRotationMatrix(d.options?.rotationXYZ ?? [0, 0, d.options?.rotation ?? 0]);
            const offset = transformVector(d.options?.offset ?? [0, 0, 0], parentRotation);
            const position = event.position.map((v, i) => v + offset[i]) as Vec3;
            if (d.createAtGroundHeight || d.options?.createAtGroundHeight) position[2] = this.scheduling.groundHeight;
            this.queue({ ...d, options: { ...d.options, offset: position, rotationMatrix: d.orientToObject ? multiply(parentRotation, local) : local,
                startFrame: this.frame + 1 + (d.options?.startFrame ?? 0) } }, source.depth + 1);
        }
    }
    step(): void {
        if (this.budgetExceeded) return;
        if (this.frame >= this.maxFrames) { this.limit('时间轴达到预览重放预算'); return; }
        this.frame++; this.eventsThisFrame = 0;
        let total = this.systems.reduce((sum, s) => sum + s.runtime.particles.length, 0);
        for (const s of this.systems.slice()) {
            if (s.parent) {
                const parent = this.systems.find(p => p.id === s.parent!.systemId)?.runtime.particles.find(p => p.id === s.parent!.particleId);
                if (parent) s.runtime.options.offset = [...parent.position];
                else { s.parent = undefined; s.runtime.stopEmission(); }
            }
            const before = s.runtime.particles.length;
            s.runtime.options.maxParticles = Math.max(0, this.maxParticles - total + before);
            s.runtime.options.maxParticleBytes = Math.max(0, this.scheduling.maxStateBytes - this.stats.stateBytes + s.runtime.particleBytes);
            s.runtime.step(); total += s.runtime.particles.length - before;
            if (s.runtime.budgetExceeded) this.limit(s.runtime.budgetExceeded);
            if (this.budgetExceeded) return;
        }
        // Explicit manager boundary: queued systems are visible now and first update next logical call.
        this.systems.push(...this.pending); this.pending = [];
        this.systems = this.systems.filter(s => { if (!s.dynamic || !s.runtime.finished) return true; this.destroy(s); return false; });
        if (this.frame % 30 === 0) this.saveSnapshot();
    }
    private captureSystem(s: WorldActiveSystem): SystemState {
        return { definition: this.definitionsById.get(s.id)!, id: s.id, depth: s.depth, dynamic: s.dynamic,
            parent: s.parent ? { ...s.parent } : undefined, state: s.runtime.captureState() };
    }
    private saveSnapshot(): void {
        if (this.scheduling.snapshotBytes <= 0) return;
        const snapshot: WorldSnapshot = { frame: this.frame, words: Array.from(this.random.words), calls: this.random.calls, nextId: this.nextId,
            systems: this.systems.map(s => this.captureSystem(s)), pending: this.pending.map(s => this.captureSystem(s)),
            cull: clone([...this.cull]), handles: [...this.attachmentHandles] };
        if (snapshotCost(snapshot) > this.scheduling.snapshotBytes) return;
        this.snapshots.push(snapshot);
        let bytes = this.snapshotBytes;
        while (this.snapshots.length > 1 && bytes > this.scheduling.snapshotBytes) bytes -= snapshotCost(this.snapshots.shift()!);
    }
    private restore(snapshot: WorldSnapshot): void {
        for (const s of [...this.systems, ...this.pending]) s.runtime.dispose();
        this.definitionsById.clear(); this.random = new FxRandom(this.seed);
        const restore = (s: SystemState): WorldActiveSystem => this.create(s.definition, s.id, s.depth, s.dynamic, s.parent ? { ...s.parent } : undefined, s.state);
        this.systems = snapshot.systems.map(restore); this.pending = snapshot.pending.map(restore);
        this.random.words.set(snapshot.words); this.random.calls = snapshot.calls;
        this.frame = snapshot.frame; this.nextId = snapshot.nextId; this.cull = new Map(clone(snapshot.cull));
        this.attachmentHandles.clear(); for (const [key, value] of snapshot.handles) this.attachmentHandles.set(key, value);
        this.budgetExceeded = undefined;
    }
    get snapshotCount(): number { return this.snapshots.length; }
    get snapshotBytes(): number { return this.snapshots.reduce((n, s) => n + snapshotCost(s), 0); }
    get pendingCount(): number { return this.pending.length; }
    get stats(): { particles: number; particleBytes: number; stateBytes: number; snapshotBytes: number; systems: number; pending: number; attachmentHandles: number; cullRecords: number } {
        const all = [...this.systems, ...this.pending];
        const particleBytes = all.reduce((n, s) => n + s.runtime.particleBytes, 0);
        const cullRecords = [...this.cull.values()].reduce((n, times) => n + times.length, 0);
        return { particles: all.reduce((n, s) => n + s.runtime.particles.length, 0), particleBytes,
            stateBytes: particleBytes + all.reduce((n, s) => n + systemStateBytes(s.runtime.config) + s.runtime.events.length * 160, 0)
                + this.attachmentHandles.size * 256 + cullRecords * 16 + this.cull.size * 128,
            snapshotBytes: this.snapshotBytes, systems: this.systems.length, pending: this.pending.length, attachmentHandles: this.attachmentHandles.size, cullRecords };
    }
    private target(frame: number): number {
        if (!Number.isFinite(frame) || frame < 0 || frame > this.maxFrames) throw new Error('时间轴目标超出预览预算');
        const target = Math.trunc(frame);
        if (target < this.frame || this.budgetExceeded) {
            const snapshot = this.snapshots.filter(s => s.frame <= target).at(-1);
            if (snapshot) this.restore(snapshot); else this.reset();
            this.snapshots = this.snapshots.filter(s => s.frame <= this.frame);
        }
        return target;
    }
    seek(frame: number): void {
        this.request++; const target = this.target(frame);
        while (this.frame < target && !this.budgetExceeded) this.step();
    }
    async seekAsync(frame: number, signal?: AbortSignal): Promise<void> {
        if (signal?.aborted) throw new Error('预览重放已取消');
        const request = ++this.request, target = this.target(frame);
        while (this.frame < target && !this.budgetExceeded) {
            if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
            const deadline = performance.now() + 4;
            while (this.frame < target && !this.budgetExceeded && performance.now() < deadline) this.step();
            if (this.frame < target && !this.budgetExceeded) await new Promise<void>(resolve => setTimeout(resolve, 0));
        }
        if (signal?.aborted || request !== this.request) throw new Error('预览重放已取消');
    }
    dispose(): void {
        this.request++; for (const s of [...this.systems, ...this.pending]) s.runtime.dispose();
        this.systems.length = this.pending.length = this.snapshots.length = 0; this.definitionsById.clear(); this.cull.clear(); this.attachmentHandles.clear();
    }
}
