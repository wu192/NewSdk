import * as path from 'node:path';
import * as vscode from 'vscode';
import type { ProtocolDiagnostic } from './buildProtocol';

/** The compiler's unlocated Core messages remain in the extension's issue list. */
export class NewSdkDiagnostics implements vscode.Disposable {
  private readonly collection = vscode.languages.createDiagnosticCollection('newsdk');
  update(diagnostics: readonly ProtocolDiagnostic[], modRoot?: string): void {
    this.collection.clear();
    const byFile = new Map<string, vscode.Diagnostic[]>();
    for (const source of diagnostics) {
      if (!source.file || !Number.isSafeInteger(source.line) || source.line! < 1) continue;
      if (source.column !== undefined && (!Number.isSafeInteger(source.column) || source.column < 1)) continue;
      if (!path.isAbsolute(source.file) && !modRoot) continue;
      const file = path.isAbsolute(source.file) ? source.file : path.resolve(modRoot!, source.file);
      const line = source.line! - 1;
      const column = (source.column ?? 1) - 1;
      const severity = source.level.toLowerCase() === 'error' ? vscode.DiagnosticSeverity.Error :
        source.level.toLowerCase() === 'warning' ? vscode.DiagnosticSeverity.Warning : vscode.DiagnosticSeverity.Information;
      const diagnostic = new vscode.Diagnostic(new vscode.Range(line, column, line, column), source.message, severity);
      diagnostic.source = 'NewSdk Compiler';
      diagnostic.code = source.code;
      const entries = byFile.get(file) ?? [];
      entries.push(diagnostic);
      byFile.set(file, entries);
    }
    for (const [file, entries] of byFile) this.collection.set(vscode.Uri.file(file), entries);
  }
  clear(): void { this.collection.clear(); }
  dispose(): void { this.collection.dispose(); }
}
