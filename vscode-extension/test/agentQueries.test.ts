import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import * as http from 'node:http';
import {AssetService} from '../src/ra3xml/services/assetService';
import {XsdModel} from '../src/ra3xml/model/xsdModel';
import {ManifestIndex} from '../src/ra3xml/index/manifestIndex';
import {AssetQueryAdapter, QUERY_TOOLS} from '../src/agent/queryAdapter';
import {ReadonlyMcpServer} from '../src/agent/mcpServer';

const xml = (body: string) => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${body}</AssetDeclaration>`;
async function fixture(t: {after(fn: () => unknown): void}) {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-query-'))), mod = path.join(root, 'mod'), sdk = path.join(root, 'sdk'), data = path.join(mod, 'Data');
    await fs.mkdir(data, {recursive: true}); await fs.mkdir(sdk, {recursive: true});
    const entry = path.join(data, 'Mod.xml'), defs = path.join(data, 'defs.xml'), refs = path.join(data, 'refs.xml');
    await fs.writeFile(entry, xml('<Includes><Include type="all" source="defs.xml"/><Include type="all" source="refs.xml"/><Include type="all" source="../../sdk/defs.xml"/></Includes>'));
    await fs.writeFile(defs, xml('<Defines><Define name="SHARED" value="2"/></Defines><Texture id="Tex" File="ART:fx/texture.tga"/><FXList id="Target"/><FXParticleSystemTemplate id="Base" ParticleTexture="Tex"/><FXParticleSystemTemplate id="Derived" inheritFrom="Base"/>'));
    await fs.writeFile(refs, xml('<Includes><Include type="all" source="defs.xml"/></Includes><FXList id="User" EventFX="Target"/><FXList id="Dynamic" EventFX="$PICK"/>'));
    await fs.writeFile(path.join(data, 'unrelated.xml'), xml('<Defines><Define name="PRIVATE" value="9"/></Defines>'));
    await fs.writeFile(path.join(sdk, 'defs.xml'), xml('<FXList id="Target"/>'));
    const schema = new XsdModel();
    schema.version = 'test-schema';
    schema.elements.set('Texture', {name: 'Texture', type: 'TextureType', required: false, enumValues: []});
    schema.types.set('TextureType', {name: 'TextureType', namespace: 'uri:ea.com:eala:asset', attributes: [{name: 'File', type: 'string', required: true, enumValues: [], documentation: 'A local ART resource.'}], children: [], enumValues: []});
    const service = new AssetService({roots: [mod, sdk], modRoot: mod, schema});
    // SDK classification uses the same explicit configured root as the editor.
    service.options.sdkRoot = sdk;
    await service.ensure(entry); await service.whenComplete();
    t.after(() => {service.dispose(); return fs.rm(root, {recursive: true, force: true});});
    return {root, mod, sdk, entry, defs, refs, service, adapter: new AssetQueryAdapter(service)};
}
function items(result: Record<string, unknown>): Record<string, unknown>[] {return result.items as Record<string, unknown>[];}

test('all nine read-only tools use shared metadata, disambiguate sources and bound returned XML', async t => {
    const {adapter, root, service} = await fixture(t);
    assert.equal(QUERY_TOOLS.length, 9); assert.ok(QUERY_TOOLS.every(tool => tool.annotations.readOnlyHint));
    const search = await adapter.query('search_assets', {query: 'FXList:Target'});
    assert.equal(items(search).length, 2); assert.equal(new Set(items(search).map(item => item.asset_key)).size, 2);
    assert.ok(!JSON.stringify(search).includes(root), 'API metadata uses relative files and opaque keys');
    assert.equal((await adapter.query('get_asset_definition', {type: 'FXList', id: 'Target'})).state, 'ambiguous');
    const selected = items(search).find(item => item.source === 'mod')!;
    const definition = await adapter.query('get_asset_definition', {asset_key: selected.asset_key, max_chars: 8});
    assert.equal(definition.truncated, true); assert.equal((definition.xml as string).length, 8);
    const incoming = await adapter.query('find_references', {asset_key: selected.asset_key});
    assert.equal(items(incoming).length, 1); assert.equal(items(incoming)[0].value, 'Target');
    const child = items(await adapter.query('search_assets', {query: 'Derived'}))[0];
    const outgoing = items(await adapter.query('get_asset_references', {asset_key: child.asset_key}));
    assert.ok(outgoing.some(item => item.value === 'Tex' && item.inherited === true));
    const inheritance = await adapter.query('get_inheritance', {asset_key: child.asset_key});
    assert.equal((inheritance.chain as unknown[]).length, 2);
    assert.ok(items(inheritance).some(item => item.name === '/@ParticleTexture' && item.inherited));
    assert.equal(items(await adapter.query('resolve_include', {file: 'Data/refs.xml'})).length, 1);
    const fields = await adapter.query('get_schema_fields', {element: 'Texture'});
    assert.equal(fields.schema_version, 'test-schema'); assert.equal(items(fields)[0].name, 'File');
    const resources = await adapter.query('get_fx_resources', {file: 'Data/refs.xml', kind: 'Texture'});
    assert.equal(resources.binary_status, 'unknown'); assert.equal(items(resources)[0].resource_status, 'declaration-only');
    const define = await adapter.query('get_define', {file: 'Data/refs.xml', name: '$SHARED'});
    assert.equal(items(define)[0].value, '2');
    const invisible = await adapter.query('get_define', {file: 'Data/refs.xml', name: 'PRIVATE'});
    assert.equal(items(invisible).length, 0, 'global scan does not make unrelated macros visible');
    assert.equal(search.index_version, service.indexVersion);
});

test('queries reject arbitrary disk paths, preserve metadata-only manifests and reflect unsaved overlays', async t => {
    const {adapter, service, defs} = await fixture(t);
    const invalid = await adapter.query('resolve_include', {file: '../outside.xml'});
    assert.equal(invalid.state, 'unknown');
    await assert.rejects(adapter.query('search_assets', {limit: 101}), /Invalid limit/);
    await assert.rejects(adapter.query('search_assets', {write: 'something'}), /Unknown argument/);
    service.manifests = new ManifestIndex();
    service.manifests.entries.push({key: 'manifest-test', id: 'External', type: 'Texture', namespace: 'uri:ea.com:eala:asset', source: 'manifest', readonly: true,
        uri: path.join(service.options.sdkRoot!, 'builtmods', 'base.manifest'), typeHash: 1, idHash: 2, declaredSource: 'Art/fx/external.xml'});
    const manifest = items(await adapter.query('search_assets', {query: 'External'}))[0];
    assert.equal(manifest.location, undefined); assert.equal(manifest.source, 'manifest');
    const definition = await adapter.query('get_asset_definition', {asset_key: manifest.asset_key});
    assert.equal(definition.state, 'unknown'); assert.equal(definition.xml, undefined);
    const before = service.indexVersion;
    const document = service.getDocument(defs)!;
    service.setDocument(defs, document.text.replace('id="Tex"', 'id="NewTex"'), 7);
    await service.ensure(defs);
    assert.equal(items(await adapter.query('search_assets', {query: 'NewTex'})).length, 1);
    assert.ok(service.indexVersion > before);
    service.completeness = 'partial';
    const dynamic = items(await adapter.query('search_assets', {query: 'Dynamic'}))[0];
    const references = await adapter.query('get_asset_references', {asset_key: dynamic.asset_key});
    assert.equal(references.completeness, 'partial'); assert.equal(items(references)[0].state, 'unknown');
});

function post(url: string, token: string, body: unknown, headers: Record<string, string> = {}, method = 'POST'): Promise<{status: number; data: any}> {
    return new Promise((resolve, reject) => {
        const request = http.request(url, {method, headers: {'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...headers}}, response => {
            const chunks: Buffer[] = []; response.on('data', chunk => chunks.push(chunk)); response.on('end', () => {
                const text = Buffer.concat(chunks).toString('utf8'); resolve({status: response.statusCode!, data: text ? JSON.parse(text) : undefined});
            });
        });
        request.on('error', reject); request.end(typeof body === 'string' ? body : JSON.stringify(body));
    });
}

test('loopback MCP executes real initialize/list/call, authenticates, rejects origins and remains read-only', async t => {
    const {service, entry} = await fixture(t); let refreshed = 0;
    const server = new ReadonlyMcpServer(new AssetQueryAdapter(service, async () => {refreshed++; await service.ensure(entry);}));
    t.after(() => server.stop());
    const connection = await server.start(); assert.equal(server.running, true);
    assert.match(connection.url, /^http:\/\/127\.0\.0\.1:\d+\/mcp$/);
    const request = (method: string, params?: unknown) => ({jsonrpc: '2.0', id: 1, method, params});
    assert.equal((await post(connection.url, 'wrong', request('tools/list'))).status, 401);
    assert.equal((await post(connection.url, connection.token, request('tools/list'), {Origin: 'http://example.test'})).status, 403);
    assert.equal((await post(connection.url, connection.token, request('tools/list'), {Host: 'example.test'})).status, 403);
    const initialized = await post(connection.url, connection.token, request('initialize', {protocolVersion: '2025-03-26'}));
    assert.equal(initialized.data.result.protocolVersion, '2025-03-26'); assert.deepEqual(initialized.data.result.capabilities.tools, {listChanged: false});
    assert.equal((await post(connection.url, connection.token, {jsonrpc: '2.0', method: 'notifications/initialized'})).status, 202);
    const list = await post(connection.url, connection.token, request('tools/list'));
    assert.equal(list.data.result.tools.length, 9); assert.equal(refreshed, 0);
    const called = await post(connection.url, connection.token, request('tools/call', {name: 'search_assets', arguments: {query: 'Target', limit: 1}}));
    assert.equal(called.data.result.isError, false); assert.equal(called.data.result.structuredContent.items.length, 1); assert.equal(refreshed, 1);
    const write = await post(connection.url, connection.token, request('tools/call', {name: 'write_file', arguments: {}}));
    assert.equal(write.data.error.code, -32602);
    const invalidArgs = await post(connection.url, connection.token, request('tools/call', {name: 'search_assets', arguments: {limit: 999}}));
    assert.equal(invalidArgs.data.result.isError, true);
    const malformed = await post(connection.url, connection.token, 'invalid json'); assert.equal(malformed.data.error.code, -32700);
    const oversize = await post(connection.url, connection.token, 'a'.repeat(65 * 1024)); assert.equal(oversize.status, 413);
    await server.stop(); assert.equal(server.running, false);
});

test('opaque MCP identities retain repeated xi:include occurrences and logical scopes', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-query-alias-'))), data = path.join(root, 'Data');
    await fs.mkdir(data, {recursive: true});
    const entry = path.join(data, 'Mod.xml'), a = path.join(data, 'a.xml'), b = path.join(data, 'b.xml'), fragment = path.join(data, 'shared.xml');
    await fs.writeFile(entry, xml('<Includes><Include type="all" source="a.xml"/><Include type="all" source="b.xml"/></Includes>'));
    await fs.writeFile(fragment, '<FXList xmlns="uri:ea.com:eala:asset" id="Shared"/>');
    const open = '<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude">';
    await fs.writeFile(a, `${open}<xi:include href="shared.xml"/><xi:include href="shared.xml"/></AssetDeclaration>`);
    await fs.writeFile(b, `${open}<xi:include href="shared.xml"/></AssetDeclaration>`);
    const service = new AssetService({roots: [root], modRoot: root, schema: new XsdModel()});
    t.after(() => {service.dispose(); return fs.rm(root, {recursive: true, force: true});});
    await service.ensure(entry); await service.whenComplete();
    const adapter = new AssetQueryAdapter(service);
    const aliases = service.findAssets({id: 'Shared'});
    assert.ok(aliases.filter(asset => asset.scopeUri === a).length >= 2, 'same source node is present twice in one logical include scope');
    assert.ok(aliases.some(asset => asset.scopeUri === b), 'the source also belongs to another logical scope');
    const results = items(await adapter.query('search_assets', {query: 'FXList:Shared'}));
    assert.equal(results.length, aliases.length);
    assert.equal(new Set(results.map(item => item.asset_key)).size, aliases.length, 'canonical keys preserve namespace, scope and include occurrence');
    for (const result of results) {
        const definition = await adapter.query('get_asset_definition', {asset_key: result.asset_key});
        assert.equal(definition.state, 'known', 'each alias key resolves exactly once');
        assert.equal((definition.definition as Record<string, unknown>).file, 'Data/shared.xml');
        assert.equal(definition.xml, '<FXList xmlns="uri:ea.com:eala:asset" id="Shared"/>');
    }
    assert.equal((await adapter.query('get_asset_definition', {type: 'FXList', id: 'Shared', file: 'Data/shared.xml'})).state, 'ambiguous');
    // Changing only the namespace preserves the physical node path and every include scope,
    // but changes its semantic identity. A previous key must not silently select the new asset.
    const previousKeys = new Set(results.map(item => item.asset_key));
    service.setDocument(fragment, '<FXList id="Shared"/>', 3); await service.ensure(entry);
    const changed = items(await adapter.query('search_assets', {query: 'FXList:Shared'}));
    assert.equal(changed.length, aliases.length);
    assert.ok(service.findAssets({id: 'Shared'}).every(asset => asset.namespace === ''));
    assert.ok(changed.every(item => !previousKeys.has(item.asset_key)), 'namespace participates in canonical identity');
    assert.equal((await adapter.query('get_asset_definition', {asset_key: results[0].asset_key})).state, 'unknown');
});

test('physical root aliases keep MCP locations relative and can be used by later file queries', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-query-path-'))), physical = path.join(root, 'physical'), alias = path.join(root, 'selected-root');
    await fs.mkdir(path.join(physical, 'Data'), {recursive: true});
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    try {await fs.symlink(physical, alias, 'junction');} catch (error) {if ((error as NodeJS.ErrnoException).code === 'EPERM') {t.skip('Symlink privilege unavailable'); return;} throw error;}
    await fs.writeFile(path.join(physical, 'Data', 'Mod.xml'), xml('<Defines><Define name="FROM_ALIAS" value="yes"/></Defines><FXList id="Alias"/>'));
    const service = new AssetService({roots: [alias], modRoot: alias, schema: new XsdModel()}); t.after(() => service.dispose());
    await service.ensure(path.join(alias, 'Data', 'Mod.xml')); await service.whenComplete();
    const adapter = new AssetQueryAdapter(service), result = items(await adapter.query('search_assets', {query: 'Alias'}))[0];
    assert.equal(result.file, 'Data/Mod.xml'); assert.ok(!JSON.stringify(result).includes(root));
    const definition = await adapter.query('get_asset_definition', {asset_key: result.asset_key});
    assert.equal((definition.definition as Record<string, unknown>).file, 'Data/Mod.xml');
    const macro = await adapter.query('get_define', {file: result.file, name: 'FROM_ALIAS'});
    assert.equal(items(macro)[0].value, 'yes');
});
