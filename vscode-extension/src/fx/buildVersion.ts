/** Tracks observed inputs without hashing or copying the project. */
export class BuildVersionTracker {
    private diskGeneration = 0;
    private readonly versions = new Map<string, number>();
    private started?: {generation: number; versions: Map<string, number>; unsaved: boolean};
    memory(uri: string, version: number): void { this.versions.set(uri, version); }
    disk(): void { this.diskGeneration++; }
    begin(unsaved = false): void { this.started = {generation: this.diskGeneration, versions: new Map(this.versions), unsaved}; }
    result(): 'current' | 'previous-saved' | 'inputs-changed' {
        if (!this.started) throw new Error('构建尚未开始。');
        if (this.started.generation !== this.diskGeneration) return 'inputs-changed';
        if (this.started.unsaved) return 'previous-saved';
        for (const [uri, version] of this.versions) {
            if (this.started.versions.get(uri) !== version) return 'previous-saved';
        }
        return 'current';
    }
}
