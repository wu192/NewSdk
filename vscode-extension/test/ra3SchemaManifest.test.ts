import test from 'node:test';
import assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import { XsdModel } from '../src/ra3xml/model/xsdModel';
import { ManifestIndex, parseManifest } from '../src/ra3xml/index/manifestIndex';
import { parseXml } from '../src/fx/xmlDocument';
const schema = (body: string): string => `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xas="uri:ea.com:eala:asset:schema" targetNamespace="uri:ea.com:eala:asset">${body}</xs:schema>`;
async function fixture(t: {after(fn: () => Promise<void>): void}): Promise<string> {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-schema-')); t.after(() => fs.rm(root, {recursive: true, force: true})); return root;
}
test('SDK XSD includes use Windows spelling and retain inherited required/default/enum/reference metadata', async t => {
    const root = await fixture(t); await fs.mkdir(path.join(root, 'Includes'));
    await fs.writeFile(path.join(root, 'main.xsd'), schema(`<xs:include schemaLocation="includes/base.xsd"/>
      <xs:complexType name="Unit"><xs:complexContent><xs:extension base="Base"><xs:sequence><xs:element name="Target" type="UnitRef"/>
      <xs:choice><xs:element name="One" type="xs:string"/><xs:element name="Two" type="xs:string"/></xs:choice></xs:sequence>
      <xs:attribute name="Mode" type="Modes" default="A"/></xs:extension></xs:complexContent></xs:complexType><xs:element name="Unit" type="Unit"/>`));
    await fs.writeFile(path.join(root, 'Includes', 'Base.xsd'), schema(`<xs:complexType name="Base"><xs:attribute name="id" type="xs:string" use="required"/></xs:complexType>
      <xs:simpleType name="UnitRef" xas:refType="GameObject"><xs:restriction base="xs:string"/></xs:simpleType>
      <xs:simpleType name="Modes"><xs:restriction base="xs:string"><xs:enumeration value="A"/><xs:enumeration value="B"/></xs:restriction></xs:simpleType>`));
    const model = await XsdModel.load(root, {entryFiles: ['main.xsd']}); assert.equal(model.complete, true, model.issues.join('\n')); assert.equal(model.files.length, 2);
    assert.equal(model.getAttributes('Unit').find(f => f.name === 'id')?.required, true);
    const mode = model.getAttributes('Unit').find(f => f.name === 'Mode'); assert.equal(mode?.default, 'A'); assert.deepEqual(mode?.enumValues, ['A', 'B']);
    assert.equal(model.getChildren('Unit').find(f => f.name === 'Target')?.referenceType, 'GameObject'); assert.equal(model.getChildren('Unit').find(f => f.name === 'One')?.required, false);
    assert.equal(model.version.length, 64);
});
test('local XSD elements resolve through parent types instead of sharing a local name', async t => {
    const root = await fixture(t);
    await fs.writeFile(path.join(root, 'main.xsd'), schema(`<xs:element name="AssetDeclaration"><xs:complexType><xs:choice>
      <xs:element name="A"><xs:complexType><xs:sequence><xs:element name="Default"><xs:complexType><xs:attribute name="Texture" type="TextureRef"/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>
      <xs:element name="B"><xs:complexType><xs:sequence><xs:element name="Default"><xs:complexType><xs:attribute name="Texture" type="xs:string"/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>
      </xs:choice></xs:complexType></xs:element><xs:simpleType name="TextureRef" xas:refType="Texture"><xs:restriction base="xs:string"/></xs:simpleType>`));
    const model = await XsdModel.load(root); const document = parseXml('<AssetDeclaration><A><Default Texture="T"/></A><B><Default Texture="T"/></B></AssetDeclaration>');
    const defaults = document.elements.filter(n => n.localName === 'Default'); assert.equal(model.getReferenceType(defaults[0], 'Texture'), 'Texture'); assert.equal(model.getReferenceType(defaults[1], 'Texture'), undefined);
    assert.ok(model.elements.has('A')); assert.ok(model.elements.has('B'));
});
test('schema budgets and nonlocal includes produce partial metadata', async t => {
    const root = await fixture(t); await fs.writeFile(path.join(root, 'main.xsd'), schema('<xs:import schemaLocation="https://example.invalid/remote.xsd"/><xs:complexType name="Local"/>'));
    const remote = await XsdModel.load(root, {entryFiles: ['main.xsd']}); assert.equal(remote.complete, false); assert.ok(remote.getType('Local')); assert.match(remote.issues.join(' '), /Nonlocal/);
    const limited = await XsdModel.load(root, {entryFiles: ['main.xsd'], maxBytes: 1}); assert.equal(limited.complete, false); assert.match(limited.issues.join(' '), /budget/);
});
test('XSD groups and reference lists retain type semantics', async t => {
    const root = await fixture(t); await fs.writeFile(path.join(root, 'main.xsd'), schema(`<xs:simpleType name="TextureRef" xas:refType="Texture"><xs:restriction base="xs:string"/></xs:simpleType>
      <xs:simpleType name="Textures"><xs:list itemType="TextureRef"/></xs:simpleType><xs:attributeGroup name="Named"><xs:attribute name="id" use="required"/></xs:attributeGroup>
      <xs:group name="Values"><xs:sequence><xs:element name="Texture" type="Textures"/></xs:sequence></xs:group><xs:element name="Root"><xs:complexType><xs:group ref="Values"/><xs:attributeGroup ref="Named"/></xs:complexType></xs:element>`));
    const model = await XsdModel.load(root); assert.equal(model.getAttributes('Root')[0].name, 'id'); const field = model.getChildren('Root')[0];
    assert.equal(field.name, 'Texture'); assert.equal(field.referenceType, 'Texture'); assert.equal(field.list, true);
    const node = parseXml('<Root><Texture>A B</Texture></Root>').elements[1]; assert.equal(model.isReferenceList(node), true); assert.equal(model.isReferenceList(node, 'id'), false);
});
function manifest(bigEndian = false): Buffer {
    const names = Buffer.from('Texture:Original\0'), sources = Buffer.from('DATA:Original.xml\0'); const b = Buffer.alloc(96 + names.length + sources.length); b[0] = bigEndian ? 1 : 0; b[1] = 1;
    const uint = (offset: number, value: number): void => { if (bigEndian) b.writeUInt32BE(value, offset); else b.writeUInt32LE(value, offset); };
    if (bigEndian) b.writeUInt16BE(6, 2); else b.writeUInt16LE(6, 2); uint(12, 1); uint(40, names.length); uint(44, sources.length); uint(48, 0xaabbccdd); uint(52, 0x12345678);
    names.copy(b, 96); sources.copy(b, 96 + names.length); return b;
}
test('manifest declarations preserve stream identity and readonly metadata without fabricated source positions', () => {
    for (const bigEndian of [false, true]) { const assets = parseManifest(manifest(bigEndian), '/sdk/builtmods/static.manifest'); assert.equal(assets.length, 1);
        assert.equal(assets[0].type, 'Texture'); assert.equal(assets[0].id, 'Original'); assert.equal(assets[0].typeHash, 0xaabbccdd); assert.equal(assets[0].readonly, true);
        assert.equal(assets[0].declaredSource, 'DATA:Original.xml'); assert.equal('line' in assets[0], false); assert.equal('node' in assets[0], false); }
});
test('manifest parser rejects malformed tables, strings and unsupported versions', () => {
    assert.throws(() => parseManifest(manifest().subarray(0, 70), 'm'), /truncated|inconsistent/);
    const badOffset = manifest(); badOffset.writeUInt32LE(99999, 72); assert.throws(() => parseManifest(badOffset, 'm'), /offset/);
    const unterminated = manifest(); unterminated[96 + Buffer.byteLength('Texture:Original')] = 65; assert.throws(() => parseManifest(unterminated, 'm'), /terminated/);
    const badVersion = manifest(); badVersion.writeUInt16LE(9, 2); assert.throws(() => parseManifest(badVersion, 'm'), /version/); assert.throws(() => parseManifest(manifest(), 'm', 0), /budget/);
});
test('builtmods preserves duplicate streams, isolates types and reports unreadable manifests as partial', async t => {
    const root = await fixture(t); await fs.mkdir(path.join(root, 'BuiltMods', 'SageXml'), {recursive: true});
    await fs.writeFile(path.join(root, 'BuiltMods', 'Static.manifest'), manifest()); await fs.writeFile(path.join(root, 'BuiltMods', 'SageXml', 'Static.manifest'), manifest());
    const index = await ManifestIndex.load(root); assert.equal(index.complete, true); assert.equal(index.find('Texture', 'original').length, 2); assert.equal(index.find('GameObject', 'Original').length, 0); assert.equal(index.findById('ORIGINAL').length, 2);
    assert.notEqual(index.entries[0].key, index.entries[1].key); await fs.writeFile(path.join(root, 'BuiltMods', 'bad.manifest'), Buffer.from('bad'));
    const partial = await ManifestIndex.load(root); assert.equal(partial.complete, false); assert.equal(partial.entries.length, 2); assert.match(partial.issues.join(' '), /truncated/);
});

test('reference assignability follows declared XSD inheritance and element type aliases', async t => {
    const root = await fixture(t); await fs.writeFile(path.join(root, 'main.xsd'), schema(`<xs:complexType name="BaseAssetType"/>
      <xs:complexType name="BaseRenderAssetType"><xs:complexContent><xs:extension base="BaseAssetType"/></xs:complexContent></xs:complexType>
      <xs:complexType name="TextureType"><xs:complexContent><xs:extension base="BaseRenderAssetType"/></xs:complexContent></xs:complexType>
      <xs:element name="Texture" type="TextureType"/><xs:complexType name="GameObject"/>`));
    const model = await XsdModel.load(root); assert.equal(model.isAssignable('Texture', 'BaseRenderAssetType'), true);
    assert.equal(model.isAssignable('Texture', 'BaseAssetType'), true); assert.equal(model.isAssignable('Texture', 'TextureType'), true);
    assert.equal(model.isAssignable('GameObject', 'BaseRenderAssetType'), false); assert.equal(model.isAssignable('Unknown', 'BaseAssetType'), false);
});
