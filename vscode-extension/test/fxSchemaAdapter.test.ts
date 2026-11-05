import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {SchemaAdapter} from '../src/fx/schemaAdapter';

const header = '<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:test" targetNamespace="urn:test">';
const entryXml = `${header}<!--keep--><xs:include schemaLocation="types\\a.xsd"/><xs:include schemaLocation="Types/A.xsd"/><xs:element name="Root" type="t:Number"/></xs:schema>`;
const typeXml = `${header}<xs:simpleType name="Number"><xs:restriction base="xs:float "/></xs:simpleType></xs:schema>`;
const hash = (bytes: Buffer): string => createHash('sha256').update(bytes).digest('hex');
async function setup(t: {after(fn: () => Promise<void>): void}) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-adapter-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const schemas = path.join(root, 'Schemas');
    await fs.mkdir(path.join(schemas, 'Types'), {recursive: true});
    const entry = path.join(schemas, 'Entry.xsd');
    await fs.writeFile(entry, entryXml); await fs.writeFile(path.join(schemas, 'Types/A.xsd'), typeXml);
    const adapter = new SchemaAdapter(path.join(root, 'cache'));
    return {root, schemas, entry, adapter};
}
const python = process.platform === 'win32' ? 'python' : 'python3';
const hasLxml = spawnSync(python, ['-c', 'from lxml import etree'], {encoding: 'utf8', windowsHide: true}).status === 0;

test('XSD snapshot canonicalizes Windows/case aliases and QName whitespace while preserving original schemas', async t => {
    const {schemas, entry, adapter} = await setup(t);
    const before = await fs.readFile(entry);
    const snapshot = await adapter.prepare(entry);
    assert.equal(snapshot.sourcePath, await fs.realpath(entry));
    assert.equal(snapshot.files, 2); assert.equal(snapshot.normalized, true);
    assert.deepEqual(snapshot.adjustments, {schemaLocations: 1, qnames: 1});
    const transformed = await fs.readFile(snapshot.entryPath, 'utf8');
    assert.ok(transformed.includes('<!--keep-->'));
    assert.equal((transformed.match(/schemaLocation="Types\/A.xsd"/g) ?? []).length, 2);
    assert.ok((await fs.readFile(path.join(path.dirname(snapshot.entryPath), 'Types/A.xsd'), 'utf8')).includes('base="xs:float"'));
    assert.deepEqual(await fs.readFile(entry), before);
    assert.equal(await fs.readFile(path.join(schemas, 'Types/A.xsd'), 'utf8'), typeXml);
    const stat = await fs.stat(snapshot.entryPath);
    const again = await adapter.prepare(entry);
    assert.equal(again.entryPath, snapshot.entryPath); assert.equal(again.version, snapshot.version);
    assert.equal((await fs.stat(again.entryPath)).mtimeMs, stat.mtimeMs, 'Unchanged content reuses immutable derived files');
    await fs.writeFile(path.join(schemas, 'Types/A.xsd'), typeXml.replace('xs:float ', 'xs:int '));
    const changed = await adapter.prepare(entry);
    assert.notEqual(changed.version, snapshot.version, 'Source content changes invalidate the snapshot');
    await fs.writeFile(changed.entryPath, '<tampered/>');
    assert.equal(await fs.readFile((await adapter.prepare(entry)).entryPath, 'utf8'), transformed, 'A changed derived cache is rebuilt from source');
});

test('XSD snapshot rejects remote, escaping, DTD, ambiguous and over-budget dependencies before SDK writes', async t => {
    const {root, schemas, entry, adapter} = await setup(t);
    await fs.writeFile(path.join(root, 'outside.xsd'), typeXml);
    for (const location of ['https://example.invalid/types.xsd', '../outside.xsd']) {
        await fs.writeFile(entry, `${header}<xs:include schemaLocation="${location}"/></xs:schema>`);
        await assert.rejects(adapter.prepare(entry), /远程|越出/);
    }
    await fs.writeFile(entry, '<!DOCTYPE schema [<!ENTITY x "unsafe">]>' + `${header}</xs:schema>`);
    await assert.rejects(adapter.prepare(entry), /格式无效/);
    await fs.writeFile(entry, entryXml);
    await assert.rejects(new SchemaAdapter(path.join(root, 'small'), {maxFiles: 1}).prepare(entry), /数量预算/);
    await assert.rejects(new SchemaAdapter(path.join(root, 'small'), {maxBytes: 20}).prepare(entry), /大小预算/);
    const forbiddenStorage = path.join(schemas, 'must-not-be-created');
    await assert.rejects(new SchemaAdapter(forbiddenStorage).prepare(entry), /不能写入/);
    await assert.rejects(fs.stat(forbiddenStorage), /ENOENT/);
    if (process.platform !== 'win32') {
        await fs.writeFile(path.join(schemas, 'Types/a.xsd'), typeXml);
        await assert.rejects(adapter.prepare(entry), /大小写歧义/);
        await fs.rm(path.join(schemas, 'Types/a.xsd'));
        await fs.symlink(path.join(root, 'outside.xsd'), path.join(schemas, 'Types/linked.xsd'));
        await fs.writeFile(entry, `${header}<xs:include schemaLocation="Types/linked.xsd"/></xs:schema>`);
        await assert.rejects(adapter.prepare(entry), /符号链接越出/);
    }
});

test('Named public FX wrapper stays unchanged so the Skill can verify its provenance manifest', async t => {
    const {schemas, adapter} = await setup(t);
    const wrapper = path.join(schemas, 'public-fx-types-wrapper.xsd');
    await fs.writeFile(wrapper, entryXml);
    const snapshot = await adapter.prepare(wrapper);
    assert.equal(snapshot.entryPath, await fs.realpath(wrapper)); assert.equal(snapshot.normalized, false);
    assert.equal(await fs.readFile(wrapper, 'utf8'), entryXml);
});

test('Canonical XSD dependency URIs preserve encoded spaces, Unicode and literal percent/hash filenames', async t => {
    const {schemas, entry, adapter} = await setup(t);
    const name = '空 格%#.xsd';
    await fs.writeFile(path.join(schemas, 'Types', name), typeXml);
    const encoded = encodeURIComponent(name);
    await fs.writeFile(entry, `${header}<xs:include schemaLocation="types/${encoded}"/><xs:element name="Root" type="t:Number"/></xs:schema>`);
    const snapshot = await adapter.prepare(entry);
    assert.ok((await fs.readFile(snapshot.entryPath, 'utf8')).includes(`schemaLocation="Types/${encoded}"`));
    assert.ok((await fs.readFile(path.join(path.dirname(snapshot.entryPath), 'Types', name), 'utf8')).includes('xs:float"'));
    if (hasLxml) {
        const result = spawnSync(python, ['-c', 'import sys;from lxml import etree;etree.XMLSchema(etree.parse(sys.argv[1]))', snapshot.entryPath], {encoding: 'utf8', windowsHide: true, timeout: 10_000});
        assert.equal(result.status, 0, result.stderr || result.error?.message);
    }
});

test('Real SDK schema snapshot compiles under lxml and rejects invalid XML without changing SDK files', {skip: !hasLxml && 'Python lxml is unavailable'}, async t => {
    const entry = path.resolve(__dirname, '../../../SDK/Schemas/xsd/CnC3Types.xsd');
    try {await fs.access(entry);} catch {t.skip('Repository SDK schema fixture is unavailable'); return;}
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-full-schema-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const schemaRoot = path.dirname(entry);
    const inventory = async (): Promise<Map<string, string>> => {
        const result = new Map<string, string>();
        const visit = async (directory: string): Promise<void> => {
            for (const child of await fs.readdir(directory, {withFileTypes: true})) {
                const file = path.join(directory, child.name);
                if (child.isDirectory()) await visit(file);
                else if (child.isFile() && /\.xsd$/i.test(child.name)) result.set(path.relative(schemaRoot, file), hash(await fs.readFile(file)));
            }
        }; await visit(schemaRoot); return result;
    };
    const before = await inventory();
    const snapshot = await new SchemaAdapter(root).prepare(entry);
    assert.ok(snapshot.files > 800, `Expected the complete SDK schema dependency graph, got ${snapshot.files}`);
    assert.ok(snapshot.adjustments.schemaLocations > 0); assert.ok(snapshot.adjustments.qnames > 0);
    const fixture = path.resolve(__dirname, '../../test/fixtures/fx/Data/flash.xml');
    const result = spawnSync(python, ['-c', `
import json, sys
from lxml import etree
schema=etree.XMLSchema(etree.parse(sys.argv[1],etree.XMLParser(no_network=True,resolve_entities=False,load_dtd=False)))
valid=etree.fromstring(b'<AssetDeclaration xmlns="uri:ea.com:eala:asset"><ArmorTemplate id="Valid" Default="100"/></AssetDeclaration>')
invalid=etree.fromstring(b'<AssetDeclaration xmlns="uri:ea.com:eala:asset"><ArmorTemplate id="Invalid" Default="not-a-number"/></AssetDeclaration>')
assert schema.validate(valid),str(schema.error_log)
assert not schema.validate(invalid),'Invalid scalar must be rejected'
fx=etree.parse(sys.argv[2])
assert schema.validate(fx),str(schema.error_log)
fx.getroot()[1].set('Type','NOT_A_PARTICLE_TYPE')
assert not schema.validate(fx),'Invalid FX enum must be rejected'
print(json.dumps({'valid':True,'invalid':False,'fx_valid':True,'fx_invalid':False}))
`, snapshot.entryPath, fixture], {encoding: 'utf8', windowsHide: true, timeout: 20_000});
    assert.equal(result.status, 0, result.stderr || result.error?.message);
    assert.deepEqual(JSON.parse(result.stdout), {valid: true, invalid: false, fx_valid: true, fx_invalid: false});
    assert.deepEqual(await inventory(), before, 'Schema validation must never edit the original SDK');
});
