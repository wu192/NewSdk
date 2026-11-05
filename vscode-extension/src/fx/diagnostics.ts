import * as vscode from 'vscode';

export interface FxProblem {
    message: string;
    code?: string;
    severity?: string;
    uri?: string;
    start?: number;
    end?: number;
    source?: { uri: string; start?: number; end?: number };
}

/** Kept separate from the compiler's collection and 500ms refresh. */
export class FxDiagnostics implements vscode.Disposable {
    private readonly collection = vscode.languages.createDiagnosticCollection('newsdk-fx');
    private readonly owners = new Map<string, Map<string, vscode.Diagnostic[]>>();
    private readonly revisions = new Map<string, symbol>();
    private disposed = false;

    async update(owner: string, problems: readonly FxProblem[]): Promise<void> {
        if (this.disposed) return;
        const revision = Symbol(owner); this.revisions.set(owner, revision);
        const files = new Map<string, vscode.Diagnostic[]>();
        for (const issue of problems) {
            const source = issue.source ?? issue;
            if (!source.uri) continue;
            const uri = source.uri.startsWith('file:') ? vscode.Uri.parse(source.uri) : vscode.Uri.file(source.uri);
            let range = new vscode.Range(0, 0, 0, 1);
            try {
                const document = await vscode.workspace.openTextDocument(uri);
                range = new vscode.Range(document.positionAt(source.start ?? 0), document.positionAt(source.end ?? (source.start ?? 0) + 1));
            } catch { /* Missing resources still get a navigable file diagnostic. */ }
            const severity = issue.severity === 'error' ? vscode.DiagnosticSeverity.Error : issue.severity === 'information' ? vscode.DiagnosticSeverity.Information : vscode.DiagnosticSeverity.Warning;
            const diagnostic = new vscode.Diagnostic(range, issue.message, severity);
            diagnostic.source = 'NewSdk FX'; diagnostic.code = issue.code;
            const key = uri.toString();
            const list = files.get(key) ?? []; list.push(diagnostic); files.set(key, list);
        }
        if (this.disposed || this.revisions.get(owner) !== revision) return;
        this.owners.set(owner, files);
        this.publish();
    }
    remove(owner: string): void { this.revisions.delete(owner); this.owners.delete(owner); this.publish(); }
    clear(): void { this.owners.clear(); this.revisions.clear(); this.collection.clear(); }
    private publish(): void {
        this.collection.clear();
        const merged = new Map<string, vscode.Diagnostic[]>();
        for (const files of this.owners.values()) for (const [uri, diagnostics] of files) {
            const list = merged.get(uri) ?? [];
            for (const diagnostic of diagnostics) if (!list.some(d => d.message === diagnostic.message && d.range.isEqual(diagnostic.range))) list.push(diagnostic);
            merged.set(uri, list);
        }
        for (const [uri, diagnostics] of merged) this.collection.set(vscode.Uri.parse(uri), diagnostics);
    }
    dispose(): void { this.disposed = true; this.owners.clear(); this.revisions.clear(); this.collection.dispose(); }
}
