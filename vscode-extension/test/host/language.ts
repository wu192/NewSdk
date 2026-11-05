import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as http from 'node:http';
import * as vscode from 'vscode';

interface LanguageApi {selectProject(root: string): Promise<void>;}
interface Connection {type: 'http'; url: string; headers: {Authorization: string};}
interface ToolResult {isError?: boolean; structuredContent?: {items?: Array<{id: string; type: string; file: string; asset_key: string}>; index_version?: number;};}
const declaration = (body: string): string => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${body}</AssetDeclaration>`;
const schema = `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xas="uri:ea.com:eala:asset:schema" targetNamespace="uri:ea.com:eala:asset">
<xs:simpleType name="GameObjectRef" xas:refType="GameObject"><xs:restriction base="xs:string"/></xs:simpleType>
<xs:simpleType name="Flags"><xs:list><xs:simpleType><xs:restriction base="xs:string"><xs:enumeration value="AIR"/><xs:enumeration value="LAND"/><xs:enumeration value="WATER"/></xs:restriction></xs:simpleType></xs:list></xs:simpleType>
<xs:complexType name="GameObject"><xs:sequence><xs:element name="CreateObject" type="GameObjectRef" minOccurs="0"/></xs:sequence>
<xs:attribute name="id" use="required"/><xs:attribute name="inheritFrom"/><xs:attribute name="Count" type="xs:unsignedByte"/><xs:attribute name="KindOf" type="Flags"/></xs:complexType>
<xs:element name="GameObject" type="GameObject"/>
<xs:element name="AssetDeclaration"><xs:complexType><xs:choice minOccurs="0" maxOccurs="unbounded"><xs:element name="GameObject" type="GameObject"/></xs:choice></xs:complexType></xs:element>
</xs:schema>`;

async function eventually<T>(action: () => PromiseLike<T>, accept: (value: T) => boolean, label: string): Promise<T> {
    const until = Date.now() + 20_000; let last: unknown;
    while (Date.now() < until) {
        try {const result = await action(); if (accept(result)) return result; last = result;} catch (error) {last = error;}
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`${label} did not become ready: ${last instanceof Error ? last.message : 'expected result absent'}`);
}
async function bounded<T>(action: PromiseLike<T>, label: string): Promise<T> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {return await Promise.race([action, new Promise<T>((_, reject) => {timer = setTimeout(() => reject(new Error(`${label} timed out`)), 20_000);})]);}
    finally {if (timer) clearTimeout(timer);}
}
function flattened(symbols: vscode.DocumentSymbol[]): vscode.DocumentSymbol[] {return symbols.flatMap(symbol => [symbol, ...flattened(symbol.children)]);}
async function request(connection: Connection, method: string, params?: unknown): Promise<Record<string, unknown>> {
    const body = JSON.stringify({jsonrpc: '2.0', id: 1, method, ...(params ? {params} : {})});
    return new Promise((resolve, reject) => {
        const request = http.request(connection.url, {method: 'POST', headers: {...connection.headers, 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body)}, timeout: 5_000}, response => {
            let result = ''; response.setEncoding('utf8');
            response.on('data', chunk => {result += chunk; if (result.length > 256 * 1024) request.destroy(new Error('Unexpectedly large host test response'));});
            response.on('end', () => {try {assert.equal(response.statusCode, 200); resolve(JSON.parse(result));} catch (error) {reject(error);}});
        });
        request.on('timeout', () => request.destroy(new Error('MCP host test request timed out'))); request.on('error', reject); request.end(body);
    });
}
async function search(connection: Connection, query: string): Promise<ToolResult> {
    const response = await request(connection, 'tools/call', {name: 'search_assets', arguments: {query, limit: 20}});
    assert.equal(response.error, undefined, 'MCP must not return a protocol error');
    const result = response.result as ToolResult; assert.equal(result.isError, false); return result;
}
async function stopped(connection: Connection): Promise<boolean> {
    try {await request(connection, 'ping'); return false;} catch {return true;}
}

/** Runs real registered language providers and the opt-in MCP listener; no compiler dependency. */
export async function run(): Promise<void> {
    const root = process.env.NEWSDK_LANGUAGE_TEST_ROOT!;
    const evidence = process.env.NEWSDK_TEST_EVIDENCE!;
    const cases: string[] = [];
    const settings = vscode.workspace.getConfiguration('newsdk');
    const priorSdk = settings.inspect<string>('sdkRoot')?.globalValue;
    let dirtyDocument: vscode.TextDocument | undefined;
    try {
        const mod = path.join(root, 'LanguageMod'), data = path.join(mod, 'Data'), sdk = path.join(root, 'SyntheticSdk');
        await fs.mkdir(data, {recursive: true}); await fs.mkdir(path.join(sdk, 'Schemas/xsd'), {recursive: true});
        await fs.mkdir(path.join(sdk, 'tools'), {recursive: true}); await fs.mkdir(path.join(sdk, 'builtmods'), {recursive: true});
        await fs.writeFile(path.join(sdk, 'Schemas/xsd/CnC3Types.xsd'), schema);
        await fs.writeFile(path.join(sdk, 'tools/BinaryAssetBuilder.exe.config'), '<configuration/>');
        const manifest = Buffer.alloc(48); manifest.set([0, 1, 6, 0]); await fs.writeFile(path.join(sdk, 'builtmods/empty.manifest'), manifest);
        const entry = path.join(data, 'Mod.xml'), base = path.join(data, 'Base.xml'), child = path.join(data, 'Child.xml');
        await fs.writeFile(entry, declaration('<Includes><Include type="all" source="Base.xml"/><Include type="all" source="Child.xml"/></Includes>'));
        await fs.writeFile(base, declaration('<Defines><Define name="HOST_COUNT" value="5"/></Defines><GameObject id="HostParent" Count="5"/>'));
        const childText = declaration('<Includes><Include type="all" source="Base.xml"/></Includes><GameObject id="HostChild" inheritFrom="HostParent" Count="=$HOST_COUNT" KindOf="AIR LAND"><CreateObject>HostParent</CreateObject></GameObject><GameObject id="HostOther"/>');
        await fs.writeFile(child, childText);
        const beforeFiles = (await fs.readdir(data)).sort();
        await settings.update('sdkRoot', sdk, vscode.ConfigurationTarget.Global);
        const extension = vscode.extensions.getExtension<LanguageApi>('wu162.newsdk-mod-tools'); assert.ok(extension);
        const api = await bounded(extension.activate(), 'Extension activation');
        await api.selectProject(mod);
        const commands = await vscode.commands.getCommands(true);
        for (const command of ['newsdk.xml.searchAssets', 'newsdk.xml.unreferenced', 'newsdk.xml.unreferencedType', 'newsdk.xml.reindex', 'newsdk.xml.indexReport',
            'newsdk.xml.clearIndex', 'newsdk.fx.importSkill', 'newsdk.fx.installSkill', 'newsdk.fx.aiNew', 'newsdk.fx.aiModify',
            'newsdk.fx.check', 'newsdk.agent.start', 'newsdk.agent.stop']) assert.ok(commands.includes(command), `Missing command: ${command}`);
        cases.push('registered-commands');
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(child));
        await vscode.window.showTextDocument(document, {preview: false});
        const symbols = await eventually(() => vscode.commands.executeCommand<vscode.DocumentSymbol[]>('vscode.executeDocumentSymbolProvider', document.uri),
            values => !!values && flattened(values).some(symbol => symbol.name === 'GameObject: HostChild'), 'Document symbols');
        assert.ok(flattened(symbols).some(symbol => symbol.name === 'GameObject: HostOther')); cases.push('asset-outline');
        const semantic = await eventually(() => vscode.commands.executeCommand<vscode.SemanticTokens>('vscode.provideDocumentSemanticTokens', document.uri),
            values => !!values?.data.length, 'Semantic tokens');
        assert.ok(semantic.data.length >= 5); cases.push('semantic-tokens');
        const referenceAt = document.positionAt(childText.indexOf('HostParent</CreateObject>') + 3);
        const completion = await vscode.commands.executeCommand<vscode.CompletionList>('vscode.executeCompletionItemProvider', document.uri, referenceAt);
        assert.ok(completion.items.some(item => (typeof item.label === 'string' ? item.label : item.label.label) === 'HostParent'), 'XSD text references need GameObject completion');
        cases.push('typed-text-completion');
        const flagAt = document.positionAt(childText.indexOf('AIR LAND') + 'AIR LA'.length);
        const flags = await vscode.commands.executeCommand<vscode.CompletionList>('vscode.executeCompletionItemProvider', document.uri, flagAt);
        assert.ok(flags.items.some(item => (typeof item.label === 'string' ? item.label : item.label.label) === 'LAND'));
        assert.ok(!flags.items.some(item => (typeof item.label === 'string' ? item.label : item.label.label) === 'AIR'));
        cases.push('flag-continuation');
        const definitions = await vscode.commands.executeCommand<vscode.Location[]>('vscode.executeDefinitionProvider', document.uri, referenceAt);
        assert.equal(definitions.length, 1); assert.equal(definitions[0].uri.fsPath, base);
        const macroAt = document.positionAt(childText.indexOf('$HOST_COUNT') + 3);
        const macros = await vscode.commands.executeCommand<vscode.Location[]>('vscode.executeDefinitionProvider', document.uri, macroAt);
        assert.equal(macros.length, 1); assert.equal(macros[0].uri.fsPath, base);
        const hovers = await vscode.commands.executeCommand<vscode.Hover[]>('vscode.executeHoverProvider', document.uri, macroAt);
        assert.ok(hovers.some(hover => hover.contents.some(content => typeof content === 'object' && 'value' in content && content.value.includes('5'))));
        const includeAt = document.positionAt(childText.indexOf('Base.xml') + 2);
        const includes = await vscode.commands.executeCommand<vscode.Location[]>('vscode.executeDefinitionProvider', document.uri, includeAt);
        assert.equal(includes.length, 1); assert.equal(includes[0].uri.fsPath, base); cases.push('reference-define-include-navigation');

        const configuration = await bounded(vscode.commands.executeCommand<{mcpServers: {newsdk: Connection}}>('newsdk.agent.start'), 'MCP start command');
        assert.ok(configuration?.mcpServers?.newsdk?.url, 'MCP start returns connection config without requiring notification dismissal');
        const connection = configuration.mcpServers.newsdk;
        const initial = await search(connection, 'GameObject:HostOther');
        assert.ok(initial.structuredContent?.items?.some(item => item.id === 'HostOther' && item.file === 'Data/Child.xml'));
        const edit = new vscode.WorkspaceEdit(); const start = childText.indexOf('HostOther');
        edit.replace(document.uri, new vscode.Range(document.positionAt(start), document.positionAt(start + 'HostOther'.length)), 'HostUnsaved');
        assert.equal(await vscode.workspace.applyEdit(edit), true); dirtyDocument = document; assert.equal(document.isDirty, true);
        const overlay = await eventually(() => search(connection, 'GameObject:HostUnsaved'), result => !!result.structuredContent?.items?.some(item => item.id === 'HostUnsaved'), 'MCP dirty overlay');
        assert.ok((overlay.structuredContent?.index_version ?? -1) >= (initial.structuredContent?.index_version ?? 0));
        assert.equal((await search(connection, 'GameObject:HostOther')).structuredContent?.items?.length, 0);
        const dirtySymbols = await vscode.commands.executeCommand<vscode.DocumentSymbol[]>('vscode.executeDocumentSymbolProvider', document.uri);
        assert.ok(flattened(dirtySymbols).some(symbol => symbol.name === 'GameObject: HostUnsaved'));
        assert.equal(await fs.readFile(child, 'utf8'), childText, 'Query and editor changes do not write MOD XML'); cases.push('mcp-shared-dirty-overlay');
        await vscode.commands.executeCommand('newsdk.agent.stop');
        await eventually(() => stopped(connection), value => value, 'MCP stop'); cases.push('mcp-stop');
        const restarted = await bounded(vscode.commands.executeCommand<{mcpServers: {newsdk: Connection}}>('newsdk.agent.start'), 'MCP restart');
        assert.ok(restarted?.mcpServers?.newsdk?.url);
        const secondMod = path.join(root, 'SecondMod'); await fs.mkdir(path.join(secondMod, 'Data'), {recursive: true});
        await fs.writeFile(path.join(secondMod, 'Data/Mod.xml'), declaration('<GameObject id="SecondProject"/>'));
        await api.selectProject(secondMod);
        await eventually(() => stopped(restarted.mcpServers.newsdk), value => value, 'Project change closes MCP'); cases.push('mcp-project-lifecycle');
        assert.deepEqual((await fs.readdir(data)).sort(), beforeFiles, 'No XML/Skill/index artifacts appear in MOD source');
        assert.equal(await fs.readFile(child, 'utf8'), childText); cases.push('source-layout-preserved');
        await fs.writeFile(path.join(evidence, 'language-host.json'), JSON.stringify({succeeded: true, vscodeVersion: vscode.version,
            platform: process.platform, scope: 'Real VS Code providers and read-only MCP using a tiny synthetic SDK; no compiler execution', cases}, null, 2));
        console.log(`[newsdk-language-host] Passed ${cases.length} cases`);
    } finally {
        try {await vscode.commands.executeCommand('newsdk.agent.stop');} catch { /* Activation failure must retain its original error. */ }
        if (dirtyDocument?.isDirty) {await vscode.window.showTextDocument(dirtyDocument); await vscode.commands.executeCommand('workbench.action.files.revert');}
        await settings.update('sdkRoot', priorSdk, vscode.ConfigurationTarget.Global);
    }
}
