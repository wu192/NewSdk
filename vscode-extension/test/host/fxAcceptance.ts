import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { FxApi } from '../../src/fx/editorProvider';

async function waitFor(predicate: () => boolean, label: string): Promise<void> {
    const until = Date.now() + 20000;
    while (!predicate()) {
        if (Date.now() > until) throw new Error(`FX host timeout: ${label}`);
        await new Promise(resolve => setTimeout(resolve, 50));
    }
}
/** Exercises the production CustomTextEditor and WorkspaceEdit path in a real host. */
export async function runFxAcceptance(fx: FxApi, modRoot: string, evidence: string,
    saveAndBuild: (uris?: vscode.Uri[]) => Promise<{status: string}>): Promise<void> {
    const cases: string[] = [];
    const data = path.join(modRoot, 'Data');
    await fs.cp(path.resolve(__dirname, '../../../test/fixtures/fx/Data'), data, {recursive: true});
    const flash = vscode.Uri.file(path.join(data, 'flash.xml'));
    const document = await vscode.workspace.openTextDocument(flash);
    const original = document.getText();
    await fx.open(flash);
    await waitFor(() => !!fx.getSnapshot(flash.fsPath).ir?.systems[0]?.config, 'open flash');
    const snapshot = fx.getSnapshot(flash.fsPath);
    const field = snapshot.ir!.systems[0].fields.find(f => f.attribute === 'Low' && /Lifetime/.test(f.nodePath));
    assert.ok(field, 'Lifetime control must map to a real XML token');
    assert.equal(await fx.applyEdits(flash.fsPath, [{key: field.key, expectedValue: field.value, value:'24'}], document.version), true);
    await waitFor(() => document.isDirty && /Low="24"/.test(document.getText()), 'document edit');
    assert.equal(await fs.readFile(flash.fsPath, 'utf8'), original, 'Preview must not write the disk outside save');
    const edited = document.getText();
    assert.equal(await fx.applyEdits(flash.fsPath, [{key: field.key, expectedValue: field.value, value:'25'}], snapshot.documentVersion!), false,
        'Stale document versions must never overwrite newer XML');
    await vscode.window.showTextDocument(document, {preview:false});
    await vscode.commands.executeCommand('undo');
    assert.equal(document.getText(), original, 'One visual gesture must undo as one TextDocument edit');
    await vscode.commands.executeCommand('redo');
    assert.equal(document.getText(), edited);
    assert.equal(await document.save(), true);
    assert.equal(await fs.readFile(flash.fsPath, 'utf8'), edited);
    cases.push('visual edit / dirty in-memory preview / stale rejection / native undo redo save');

    await fx.open(flash);
    await waitFor(() => fx.getSnapshot(flash.fsPath).ir?.systems[0]?.config?.lifetime.low === 24, 'updated lifetime');
    const previous = fx.getSnapshot(flash.fsPath).ir!;
    const breakXml = new vscode.WorkspaceEdit();
    breakXml.delete(flash, new vscode.Range(document.positionAt(document.getText().lastIndexOf('</AssetDeclaration>')), document.positionAt(document.getText().length)));
    assert.equal(await vscode.workspace.applyEdit(breakXml), true);
    await waitFor(() => vscode.languages.getDiagnostics(flash).some(d => String(d.source).includes('FX')), 'broken XML diagnostics');
    assert.equal(fx.getSnapshot(flash.fsPath).ir?.systems[0]?.config?.lifetime.low, previous.systems[0].config!.lifetime.low,
        'Malformed XML must retain the previous valid preview');
    await vscode.window.showTextDocument(document, {preview:false});
    await vscode.commands.executeCommand('undo');
    await waitFor(() => !vscode.languages.getDiagnostics(flash).some(d => d.severity === vscode.DiagnosticSeverity.Error), 'XML recovery');
    await document.save(); cases.push('broken XML preserves valid IR and recovers');

    const missing = vscode.Uri.file(path.join(data, 'missing.xml'));
    await fx.open(missing);
    await waitFor(() => !!fx.getSnapshot(missing.fsPath).ir, 'missing texture');
    assert.ok(fx.getSnapshot(missing.fsPath).ir!.issues.some(i => /texture|贴图|纹理/i.test(i.code + i.message)), 'Missing texture must have a concrete issue');
    cases.push('missing texture remains editable with a located issue');

    const entry = vscode.Uri.file(path.join(data, 'Mod.xml'));
    await fx.open(entry);
    await waitFor(() => !!fx.getSnapshot(entry.fsPath).ir?.systems.length, 'cross-file composition');
    const root = fx.getSnapshot(entry.fsPath).ir!;
    const aura = root.systems.find(s => /Aura/.test(s.name)); assert.ok(aura);
    await fx.openShared(entry.fsPath, aura.key);
    const auraUri = vscode.Uri.file(path.join(data, 'aura.xml'));
    await waitFor(() => !!fx.getSnapshot(auraUri.fsPath).ir, 'shared definition B');
    assert.equal(fx.getSnapshot(auraUri.fsPath).ir!.assetKey, root.assetKey, 'Preview A must stay pinned when editing B');
    const shared = fx.getSnapshot(auraUri.fsPath);
    const auraField = shared.ir!.systems.flatMap(s => s.fields).find(f => f.uri === auraUri.fsPath && f.attribute==='Low' && /Lifetime/.test(f.nodePath));
    assert.ok(auraField);
    const auraDoc = await vscode.workspace.openTextDocument(auraUri), beforeAura = auraDoc.getText();
    assert.equal(await fx.applyEdits(auraUri.fsPath,[{key:auraField.key, expectedValue:auraField.value, value:'48'}],auraDoc.version),true);
    assert.equal(document.getText(), edited, 'Editing B must leave A and other templates untouched');
    await vscode.window.showTextDocument(auraDoc,{preview:false}); await vscode.commands.executeCommand('undo');
    assert.equal(auraDoc.getText(),beforeAura); await auraDoc.save();
    cases.push('A references B / edit actual B / undo and save actual B');

    await fx.open(flash);
    await waitFor(() => !!fx.getSnapshot(flash.fsPath).ir, 'save and compile');
    const result = await saveAndBuild([entry]);
    assert.equal(result.status,'succeeded'); cases.push('save and compile through shared compiler service');
    for (const doc of vscode.workspace.textDocuments) if (doc.uri.fsPath.startsWith(modRoot) && doc.isDirty) await doc.save();
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    await waitFor(() => fx.getSnapshot().sessionCount===0,'dispose all sessions');
    assert.equal(fx.getSnapshot().activeCount,0);
    cases.push('close releases all active host sessions');
    await fs.writeFile(path.join(evidence,'fx-host.json'),JSON.stringify({passed:true,cases,vscode:vscode.version},null,2));
}
