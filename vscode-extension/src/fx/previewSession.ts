import { randomBytes } from 'node:crypto';

/** A session owns cancellation and monotonic requests; it never owns an XML copy. */
export class PreviewSession {
    readonly id = randomBytes(18).toString('hex');
    private revision = 0;
    private incoming = -1;
    private controller = new AbortController();
    private closed = false;
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
