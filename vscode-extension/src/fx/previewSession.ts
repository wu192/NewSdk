import { randomBytes } from 'node:crypto';
import type { FxScene } from './previewProtocol';
import { parseSourceMotion } from '../../webview/fx/runtime/sourceMotion';

export function parseFxScene(value: unknown): FxScene | undefined {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
    const o = value as Record<string, unknown>;
    const vec = (v: unknown): v is [number, number, number] => Array.isArray(v) && v.length === 3 && v.every(n => typeof n === 'number' && Number.isFinite(n) && Math.abs(n) <= 1e6);
    if (!vec(o.source) || !vec(o.target) || (o.terrain !== 'land' && o.terrain !== 'water')
        || typeof o.ground !== 'number' || !Number.isFinite(o.ground) || Math.abs(o.ground) > 1e6
        || !Number.isSafeInteger(o.seed) || (o.seed as number) < 0 || (o.seed as number) > 0xffffffff) return undefined;
    const scene: FxScene = { source: [...o.source], target: [...o.target], terrain: o.terrain, ground: o.ground, seed: o.seed as number };
    if (o.sourceMotion !== undefined) {
        const motion = parseSourceMotion(o.sourceMotion);
        if (!motion) return undefined;
        scene.sourceMotion = motion;
    }
    const words=(value:unknown):value is string[]=>Array.isArray(value)&&value.length<=128&&value.every(s=>typeof s==='string'&&s.length<=128&&/^[A-Za-z0-9_]+$/.test(s));
    if(o.dispatch!==undefined){if(o.dispatch!=='position'&&o.dispatch!=='object')return undefined;scene.dispatch=o.dispatch;}
    if(o.sourceRotation!==undefined){if(!vec(o.sourceRotation))return undefined;scene.sourceRotation=[...o.sourceRotation];}
    for(const key of ['sourceModelConditions','targetModelConditions','sourceDisabledTypesThisFrame'] as const)if(o[key]!==undefined){if(!words(o[key]))return undefined;scene[key]=[...o[key] as string[]];}
    if(o.targetObject!==undefined){if(typeof o.targetObject!=='boolean')return undefined;scene.targetObject=o.targetObject;}
    if(o.weather!==undefined){if(typeof o.weather!=='string'||o.weather.length>128||!/^[A-Za-z0-9_]*$/.test(o.weather))return undefined;scene.weather=o.weather;}
    for(const key of ['sourceStealthState','positionVisibility','sourceVisibility'] as const)if(o[key]!==undefined){if(!Number.isSafeInteger(o[key])||Math.abs(o[key] as number)>65535)return undefined;scene[key]=o[key] as number;}
    return scene;
}


/** A session owns cancellation and monotonic requests; it never owns an XML copy. */
export class PreviewSession {
    readonly id = randomBytes(18).toString('hex');
    private revision = 0;
    private incoming = -1;
    private controller = new AbortController();
    private closed = false;
    private documentActions: Promise<void> = Promise.resolve();
    visible = true;
    constructor(public documentVersion: number) {}

    get requestId(): number { return this.revision; }
    get signal(): AbortSignal { return this.controller.signal; }
    get disposed(): boolean { return this.closed; }
    /** Cancels every task for the previous graph/document generation. */
    renew(version = this.documentVersion): { requestId: number; signal: AbortSignal } {
        this.controller.abort();
        this.controller = new AbortController();
        this.documentVersion = version;
        this.revision++;
        return { requestId: this.revision, signal: this.controller.signal };
    }
    current(requestId: number): boolean { return !this.closed && requestId === this.revision && !this.signal.aborted; }
    accepts(value: unknown, requireVersion = true): value is { sessionId: string; documentVersion: number; requestId: number; type: string } {
        if (this.closed || !value || typeof value !== 'object' || Array.isArray(value)) return false;
        const m = value as Record<string, unknown>;
        return m.sessionId === this.id && typeof m.type === 'string'
            && Number.isSafeInteger(m.documentVersion) && (!requireVersion || m.documentVersion === this.documentVersion)
            && Number.isSafeInteger(m.requestId) && (m.requestId as number) >= 0;
    }
    /** Mutation replay protection is independent of asynchronous host requests. */
    acceptMutation(requestId: number): boolean {
        if (!Number.isSafeInteger(requestId) || requestId <= this.incoming) return false;
        this.incoming = requestId;
        return true;
    }
    /** Serializes edits with subsequent Save/Undo; resource ACKs never enter this queue. */
    enqueueDocumentAction(action: () => Promise<void>): Promise<void> {
        const run = this.documentActions.then(() => this.closed ? undefined : action());
        this.documentActions = run.catch(() => undefined);
        return run;
    }
    suspend(): void { this.visible = false; this.controller.abort(); }
    dispose(): void { this.closed = true; this.visible = false; this.controller.abort(); }
}

/** Only one canvas may be running. Inactive panels retain bounded UI state only. */
export class ActivePreview {
    private active?: { session: PreviewSession; stop: () => void };
    owns(session: PreviewSession): boolean { return this.active?.session === session; }
    get hasActive(): boolean { return !!this.active; }
    activate(session: PreviewSession, stop: () => void): void {
        if (session.disposed) return;
        if (this.active?.session !== session) {
            this.active?.session.suspend();
            this.active?.stop();
        }
        session.visible = true;
        this.active = { session, stop };
    }
    release(session: PreviewSession): void {
        session.suspend();
        if (this.active?.session === session) { this.active.stop(); this.active = undefined; }
    }
    dispose(): void { this.active?.session.suspend(); this.active?.stop(); this.active = undefined; }
}
