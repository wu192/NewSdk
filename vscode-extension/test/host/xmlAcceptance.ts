import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as vscode from 'vscode';

interface XmlTestApi {
    selectProject(root: string): Promise<void>;
    configure(values: {compilerPath: string; sdkRoot: string; outputDirectory: string; modVersion: string}): Promise<void>;
}
async function eventually<T>(action: () => PromiseLike<T>, accept: (value: T) => boolean): Promise<T> {
    const until = Date.now() + 20000; let lastError: unknown;
    while (Date.now() < until) {
        try {const value = await action(); if (accept(value)) return value;} catch (error) {lastError = error;}
        await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error(`XML language service did not become ready: ${String(lastError ?? 'no expected result')}`);
}

/** Real extension registration, dirty overlays and rename preview; no compiler invocation. */
export async function runXmlAcceptance(api: XmlTestApi, root: string, evidence: string, compilerPath: string, sdkRoot: string): Promise<void> {
    const mod = path.join(root, 'XmlLanguageMod'), data = path.join(mod, 'Data');
    const xmlSdk = path.join(root, 'xml-language-sdk');
    await fs.mkdir(path.join(xmlSdk, 'tools'), {recursive: true});
    await fs.mkdir(path.join(xmlSdk, 'builtmods'), {recursive: true});
    await fs.cp(path.join(sdkRoot, 'Schemas'), path.join(xmlSdk, 'Schemas'), {recursive: true});
    await fs.copyFile(path.join(sdkRoot, 'tools', 'BinaryAssetBuilder.exe.config'), path.join(xmlSdk, 'tools', 'BinaryAssetBuilder.exe.config'));
    // This language fixture has an explicitly empty original asset catalogue.
    // Keep the real compiler fixture and its input-preservation inventory separate.
    const emptyManifest = Buffer.alloc(48); emptyManifest.set([0, 1, 6, 0]);
    await fs.writeFile(path.join(xmlSdk, 'builtmods', 'empty.manifest'), emptyManifest);
    await fs.mkdir(data, {recursive: true});
    const declaration = (body: string) => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${body}</AssetDeclaration>`;
    const base = path.join(data, 'Base.xml'), child = path.join(data, 'Child.xml');
    await fs.writeFile(path.join(data, 'Mod.xml'), declaration('<Includes><Include type="all" source="Base.xml"/><Include type="all" source="Child.xml"/></Includes>'));
    await fs.writeFile(base, declaration('<Defines><Define name="XML_ARMOR" value="100"/></Defines><ArmorTemplate id="XmlLanguageBase" Default="100"/>'));
    await fs.writeFile(child, declaration('<Includes><Include type="all" source="Base.xml"/></Includes><ArmorTemplate id="XmlLanguageChild" inheritFrom="XmlLanguageBase" Default="=$XML_ARMOR"/>'));
    const originalFiles = (await fs.readdir(data)).sort();
    await api.selectProject(mod);
    await api.configure({compilerPath, sdkRoot: xmlSdk, outputDirectory: path.join(root, 'xml-language-output'), modVersion: '1.0'});
    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(child));
    await vscode.window.showTextDocument(document, {preview: false});
    const offset = document.getText().indexOf('XmlLanguageBase');
    const position = document.positionAt(offset + 3);
    const definitions = await eventually(
        () => vscode.commands.executeCommand<(vscode.Location | vscode.LocationLink)[]>('vscode.executeDefinitionProvider', document.uri, position),
        values => !!values?.some(value => ('uri' in value ? value.uri : value.targetUri).fsPath.toLowerCase() === base.toLowerCase()));
    assert.equal(definitions.length, 1, 'Inheritance definition must resolve uniquely to the included MOD asset');
    const references = await vscode.commands.executeCommand<vscode.Location[]>('vscode.executeReferenceProvider', document.uri, position);
    assert.ok(references.some(reference => reference.uri.toString() === document.uri.toString()), 'References must include the physical inheritFrom token');
    const hovers = await vscode.commands.executeCommand<vscode.Hover[]>('vscode.executeHoverProvider', document.uri, position);
    assert.ok(hovers.length > 0, 'Resolved asset hover must be registered');
    const completion = await vscode.commands.executeCommand<vscode.CompletionList>('vscode.executeCompletionItemProvider', document.uri, position);
    assert.ok(completion.items.some(item => (typeof item.label === 'string' ? item.label : item.label.label) === 'XmlLanguageBase'), 'Type-aware inheritance completion must include the visible parent');
    const macroPosition = document.positionAt(document.getText().indexOf('$XML_ARMOR') + 3);
    const macroDefinitions = await vscode.commands.executeCommand<vscode.Location[]>('vscode.executeDefinitionProvider', document.uri, macroPosition);
    assert.equal(macroDefinitions.length, 1, 'Known macro must navigate to its visible Include definition');
    assert.equal(macroDefinitions[0].uri.fsPath.toLowerCase(), base.toLowerCase());
    const macroHovers = await vscode.commands.executeCommand<vscode.Hover[]>('vscode.executeHoverProvider', document.uri, macroPosition);
    assert.ok(macroHovers.some(hover => hover.contents.some(content => typeof content === 'object' && 'value' in content && content.value.includes('100'))), 'Macro hover shows the actual value');
    const outline = await vscode.commands.executeCommand<vscode.DocumentSymbol[]>('vscode.executeDocumentSymbolProvider', document.uri);
    assert.ok(outline.some(symbol => symbol.name === 'ArmorTemplate: XmlLanguageChild'), 'Outline lists top-level assets by type and ID');
    const rename = await eventually(
        () => vscode.commands.executeCommand<vscode.WorkspaceEdit>('vscode.executeDocumentRenameProvider', document.uri, position, 'XmlLanguageRenamed'),
        value => !!value && value.has(vscode.Uri.file(base)) && value.has(document.uri));
    assert.equal(rename.size, 2, 'Rename preview must contain only the actual definition and confirmed reference documents');
    assert.ok(rename.entries().every(([uri]) => uri.fsPath.toLowerCase().startsWith(mod.toLowerCase() + path.sep)), 'SDK source must never be included in rename');
    assert.equal(await fs.readFile(child, 'utf8'), document.getText(), 'Preparing rename must not write XML');

    const missing = new vscode.WorkspaceEdit();
    missing.replace(document.uri, new vscode.Range(document.positionAt(offset), document.positionAt(offset + 'XmlLanguageBase'.length)), 'XmlLanguageMissing');
    assert.equal(await vscode.workspace.applyEdit(missing), true);
    assert.equal(document.isDirty, true);
    await eventually(
        () => vscode.commands.executeCommand<(vscode.Location | vscode.LocationLink)[]>('vscode.executeDefinitionProvider', document.uri, position),
        values => !!values && values.length === 0);
    await eventually(async () => vscode.languages.getDiagnostics(document.uri),
        values => values.some(value => value.source === 'NewSdk XML' && value.code === 'xml.unresolved-reference'));
    assert.ok((await fs.readFile(child, 'utf8')).includes('XmlLanguageBase'), 'Dirty overlay must not write MOD source');
    await vscode.commands.executeCommand('undo');
    assert.ok(document.getText().includes('XmlLanguageBase'), 'XML source edits must remain undoable');
    assert.deepEqual((await fs.readdir(data)).sort(), originalFiles, 'No cache files may be written into MOD source');
    await fs.writeFile(path.join(evidence, 'xml-host.json'), JSON.stringify({succeeded: true,
        cases: ['include-definition', 'typed-completion', 'references', 'hover', 'macro-definition', 'macro-hover', 'asset-outline', 'cross-file-rename-preview', 'sdk-not-written', 'dirty-overlay', 'static-diagnostics', 'undo', 'no-mod-cache']}, null, 2));
}
