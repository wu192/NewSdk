import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {selectFxSchema} from '../src/fx/schemaSelection';

const xsd = '<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"/>';

test('FX selects the compiler-configured schema with Windows separators and actual SDK path casing', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-select-')));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    await fs.mkdir(path.join(root, 'Tools'));
    await fs.mkdir(path.join(root, 'Schemas', 'custom'), {recursive: true});
    const entry = path.join(root, 'Schemas', 'custom', 'Chosen.xsd');
    await fs.writeFile(entry, xsd);
    const config = path.join(root, 'Tools', 'BinaryAssetBuilder.exe.config');
    const configText = '<configuration><assetbuilder schema="..\\schemas\\CUSTOM\\chosen.XSD"/></configuration>';
    await fs.writeFile(config, configText);
    assert.deepEqual(await selectFxSchema(root), {path: await fs.realpath(entry), source: 'sdk-config'});
    assert.equal(await fs.readFile(config, 'utf8'), configText, 'selection leaves compiler settings unchanged');
    assert.equal(await fs.readFile(entry, 'utf8'), xsd, 'selection leaves schema source unchanged');
});

test('FX uses the SDK default without a schema config and an explicit override remains authoritative', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-default-')));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const sdk = path.join(root, 'sdk'), fallback = path.join(sdk, 'Schemas', 'xsd', 'CnC3Types.xsd'), override = path.join(root, 'Override.xsd');
    await fs.mkdir(path.dirname(fallback), {recursive: true});
    await fs.writeFile(fallback, xsd); await fs.writeFile(override, xsd);
    assert.deepEqual(await selectFxSchema(), {source: 'none'});
    assert.deepEqual(await selectFxSchema(sdk), {path: await fs.realpath(fallback), source: 'sdk-default'});
    assert.deepEqual(await selectFxSchema(path.join(root, 'missing-sdk'), override), {path: await fs.realpath(override), source: 'override'});
    const missing = await selectFxSchema(sdk, path.join(root, 'missing.xsd'));
    assert.equal(missing.source, 'override'); assert.match(missing.issue!, /不存在/);
});

test('FX schema selection reports malformed or missing SDK entries instead of silently choosing another schema', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-unavailable-')));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    await fs.mkdir(path.join(root, 'tools'));
    const config = path.join(root, 'tools', 'BinaryAssetBuilder.exe.config');
    const absent = await selectFxSchema(root);
    assert.equal(absent.source, 'sdk-default'); assert.match(absent.issue!, /不存在/);
    await fs.writeFile(config, '<configuration><assetbuilder schema="../Schemas/Missing.xsd"/></configuration>');
    const configured = await selectFxSchema(root);
    assert.equal(configured.source, 'sdk-config'); assert.match(configured.issue!, /入口不存在/);
    assert.equal(configured.path, path.join(root, 'Schemas', 'Missing.xsd'));
    await fs.writeFile(config, '<configuration><assetbuilder');
    assert.match((await selectFxSchema(root)).issue!, /有效 XML/);
    await fs.writeFile(config, '<configuration><assetbuilder schema="https://example.invalid/schema.xsd"/></configuration>');
    assert.match((await selectFxSchema(root)).issue!, /本地文件/);
});

test('compiler schema settings cannot leave the selected SDK through relative paths or links', async t => {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-boundary-')));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const sdk = path.join(root, 'sdk'), outside = path.join(root, 'outside');
    await fs.mkdir(path.join(sdk, 'tools'), {recursive: true}); await fs.mkdir(outside);
    await fs.writeFile(path.join(outside, 'External.xsd'), xsd);
    const config = path.join(sdk, 'tools', 'BinaryAssetBuilder.exe.config');
    await fs.writeFile(config, '<configuration><assetbuilder schema="../../outside/External.xsd"/></configuration>');
    assert.match((await selectFxSchema(sdk)).issue!, /SDK 之外/);
    await fs.symlink(outside, path.join(sdk, 'LinkedSchemas'), process.platform === 'win32' ? 'junction' : 'dir');
    await fs.writeFile(config, '<configuration><assetbuilder schema="../LinkedSchemas/External.xsd"/></configuration>');
    assert.match((await selectFxSchema(sdk)).issue!, /SDK 之外/);
});
