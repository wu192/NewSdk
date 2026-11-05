/** Tracks observed inputs without hashing or copying the project. */
export class BuildVersionTracker {
    private diskGeneration = 0;
    private readonly versions = new Map<string, number>();
    private started?: {generation: number; versions: Map<string, number>};
    memory(uri: string, version: number): void { this.versions.set(uri, version); }
    disk(): void { this.diskGeneration++; }
    begin(): void { this.started = {generation: this.diskGeneration, versions: new Map(this.versions)}; }
    result(): 'current' | 'previous-saved' | 'inputs-changed' {
        if (!this.started) throw new Error('构建尚未开始。');
        if (this.started.generation !== this.diskGeneration) return 'inputs-changed';
        for (const [uri, version] of this.versions) {
            if (this.started.versions.get(uri) !== version) return 'previous-saved';
        }
        return 'current';
    }
}
