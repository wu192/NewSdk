import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {parseXml} from '../src/ra3xml/core/xmlDocument';
import {applyTextPatches} from '../src/ra3xml/core/editTransaction';
import {XsdModel} from '../src/ra3xml/model/xsdModel';
import {AssetService} from '../src/ra3xml/services/assetService';
import {completionAt, completePaths, schemaIssues, defineIssues, prepareAssetRename} from '../src/ra3xml/providers/languageFeatures';

const schemaText = `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:a="urn:reference" targetNamespace="uri:ea.com:eala:asset">
<xs:simpleType name="Quality"><xs:restriction base="xs:string"><xs:enumeration value="Low"/><xs:enumeration value="High"/></xs:restriction></xs:simpleType>
<xs:complexType name="GameObject"><xs:attribute name="id" type="xs:string" use="required"/><xs:attribute name="Target" type="xs:string" a:refType="GameObject"/><xs:attribute name="Image" type="xs:string" a:refType="Texture"/><xs:attribute name="Quality" type="Quality" default="Low"/><xs:attribute name="inheritFrom" type="xs:string"/></xs:complexType>
<xs:complexType name="Texture"><xs:attribute name="id" type="xs:string" use="required"/></xs:complexType>
<xs:element name="GameObject" type="GameObject"/><xs:element name="Texture" type="Texture"/>
<xs:element name="AssetDeclaration"><xs:complexType><xs:choice minOccurs="0" maxOccurs="unbounded"><xs:element name="GameObject" type="GameObject"/><xs:element name="Texture" type="Texture"/><xs:element name="Includes"><xs:complexType><xs:sequence><xs:element name="Include" minOccurs="0" maxOccurs="unbounded"><xs:complexType><xs:attribute name="type"/><xs:attribute name="source"/></xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element></xs:choice></xs:complexType></xs:element>
</xs:schema>`;
async function fixture() {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-language-'));
    await fs.writeFile(path.join(root, 'CnC3Types.xsd'), schemaText);
    const schema = await XsdModel.load(root);
    return {root, schema, cleanup: () => fs.rm(root, {recursive: true, force: true})};
}

test('completion recovers an unfinished child tag and attribute value without inventing a semantic document', () => {
    const source = '<AssetDeclaration><GameObject id="Unit" Quality="Hi';
    const document = parseXml(source, '/mod.xml', 3);
    const context = completionAt(document, source.length);
    assert.equal(context?.kind, 'value');
    assert.equal(context?.node?.localName, 'GameObject');
    assert.equal(context?.node?.parent?.localName, 'AssetDeclaration');
    assert.equal(context?.value?.attribute, 'Quality');
    assert.equal(context?.prefix, 'Hi');
    assert.equal(source.slice(context!.start), 'Hi');
    const elementSource = '<AssetDeclaration>\n  <Game';
    const element = completionAt(parseXml(elementSource), elementSource.length);
    assert.equal(element?.kind, 'element'); assert.equal(element?.prefix, 'Game');
    assert.equal(element?.parent?.localName, 'AssetDeclaration');
    const comment = '<AssetDeclaration><!-- <Game';
    assert.equal(completionAt(parseXml(comment), comment.length), undefined);
});

test('XSD diagnostics use actual value ranges and leave unsaved syntax recovery to XML diagnostics', async t => {
    const fixtureData = await fixture(); t.after(fixtureData.cleanup);
    const text = '<AssetDeclaration><GameObject Quality="Ultra"/></AssetDeclaration>';
    const document = parseXml(text, '/mod.xml', 9);
    const diagnostics = schemaIssues(document, fixtureData.schema);
    const missing = diagnostics.find(issue => issue.code === 'xml.required-attribute');
    assert.equal(missing?.attribute, 'id');
    const value = diagnostics.find(issue => issue.code === 'xml.enum');
    assert.equal(text.slice(value!.start, value!.end), 'Ultra');
    assert.ok(!diagnostics.some(issue => issue.code === 'xml.required-element'), 'optional choice alternatives must not be required');
    const invalid = schemaIssues(parseXml('<GameObject id="unfinished'), fixtureData.schema);
    assert.ok(invalid.length); assert.ok(invalid.every(issue => issue.code.startsWith('xml.')));
    assert.ok(!invalid.some(issue => issue.code === 'xml.required-attribute'), 'partial XML does not produce misleading schema errors');
});

test('cross-file rename follows typed resolved identities and preserves same-id assets of another type', async t => {
    const fixtureData = await fixture(); t.after(fixtureData.cleanup);
    const modRoot = path.join(fixtureData.root, 'mod'); const data = path.join(modRoot, 'Data'); await fs.mkdir(data, {recursive: true});
    const entry = path.join(data, 'Mod.xml'), defs = path.join(data, 'defs.xml'), refs = path.join(data, 'refs.xml');
    await fs.writeFile(entry, '<AssetDeclaration><Includes><Include type="all" source="defs.xml"/><Include type="all" source="refs.xml"/></Includes></AssetDeclaration>');
    await fs.writeFile(defs, '<AssetDeclaration><GameObject id="Old"/><Texture id="Old"/></AssetDeclaration>');
    await fs.writeFile(refs, '<AssetDeclaration><Includes><Include type="all" source="defs.xml"/></Includes><GameObject id="User" Target="GameObject:Old" Image="Old"/><GameObject id="Derived" inheritFrom="Old"/><GameObject id="Dynamic" Target="$PICK"/></AssetDeclaration>');
    const service = new AssetService({roots: [modRoot], modRoot, schema: fixtureData.schema}); t.after(() => service.dispose());
    await service.ensure(entry); await service.whenComplete();
    assert.equal(service.completeness, 'complete');
    const target = service.findAssets({type: 'GameObject', id: 'Old'})[0]; assert.ok(target);
    const patches = prepareAssetRename(service, target, 'New');
    assert.equal(patches.size, 2);
    const changedDefs = applyTextPatches(service.getDocument(defs)!.text, patches.get(defs)!);
    const changedRefs = applyTextPatches(service.getDocument(refs)!.text, patches.get(refs)!);
    assert.match(changedDefs, /GameObject id="New"/); assert.match(changedDefs, /Texture id="Old"/);
    assert.match(changedRefs, /Target="GameObject:New" Image="Old"/); assert.match(changedRefs, /inheritFrom="New"/);
    assert.match(changedRefs, /Target="\$PICK"/);
    assert.throws(() => prepareAssetRename(service, target, 'User'), /同名资产/);
    service.completeness = 'partial';
    assert.throws(() => prepareAssetRename(service, target, 'New'), /索引尚未完整/);
});

test('rename of a shared xi:include declaration updates confirmed references in every logical scope', async t => {
    const fixtureData = await fixture(); t.after(fixtureData.cleanup);
    const modRoot = path.join(fixtureData.root, 'mod'); const data = path.join(modRoot, 'Data'); await fs.mkdir(data, {recursive: true});
    const entry = path.join(data, 'Mod.xml'), fragment = path.join(data, 'shared.xml'), a = path.join(data, 'a.xml'), b = path.join(data, 'b.xml');
    await fs.writeFile(entry, '<AssetDeclaration><Includes><Include type="all" source="a.xml"/><Include type="all" source="b.xml"/></Includes></AssetDeclaration>');
    await fs.writeFile(fragment, '<GameObject xmlns="uri:ea.com:eala:asset" id="Shared"/>');
    for (const [file, id] of [[a, 'A'], [b, 'B']]) await fs.writeFile(file,
        `<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude"><xi:include href="shared.xml"/><GameObject id="${id}" Target="Shared"/></AssetDeclaration>`);
    const service = new AssetService({roots: [modRoot], modRoot, schema: fixtureData.schema}); t.after(() => service.dispose());
    await service.ensure(entry); await service.whenComplete();
    const aliases = service.findAssets({type: 'GameObject', id: 'Shared'}); assert.ok(aliases.length >= 2);
    const patches = prepareAssetRename(service, aliases[0], 'NewShared');
    assert.deepEqual([...patches.keys()].sort(), [fragment, a, b].sort());
    for (const file of [a, b]) assert.match(applyTextPatches(service.getDocument(file)!.text, patches.get(file)!), /Target="NewShared"/);
    assert.equal(patches.get(fragment)!.length, 1, 'one physical declaration receives one edit despite several scopes');
});

test('path completion merges MOD and SDK roots and applies ART bucket lookup', async t => {
    const fixtureData = await fixture(); t.after(fixtureData.cleanup);
    const modRoot = path.join(fixtureData.root, 'mod'), sdkRoot = path.join(fixtureData.root, 'sdk');
    await fs.mkdir(path.join(modRoot, 'Data'), {recursive: true}); await fs.mkdir(path.join(sdkRoot, 'SAGEXML'), {recursive: true});
    await fs.mkdir(path.join(modRoot, 'Art', 'fx'), {recursive: true}); await fs.mkdir(path.join(sdkRoot, 'Art', 'fx'), {recursive: true});
    await fs.writeFile(path.join(modRoot, 'Data', 'Mod.xml'), '<AssetDeclaration/>');
    await fs.writeFile(path.join(modRoot, 'Data', 'UnitMod.xml'), '<AssetDeclaration/>');
    await fs.writeFile(path.join(sdkRoot, 'SAGEXML', 'UnitSdk.xml'), '<AssetDeclaration/>');
    await fs.writeFile(path.join(modRoot, 'Art', 'fx', 'fx_mod.dds'), 'not decoded');
    await fs.writeFile(path.join(sdkRoot, 'Art', 'fx', 'fx_sdk.dds'), 'not decoded');
    const service = new AssetService({roots: [modRoot, sdkRoot], modRoot, sdkRoot, schema: fixtureData.schema}); t.after(() => service.dispose());
    const from = path.join(modRoot, 'Data', 'Mod.xml');
    const data = await completePaths(service, from, 'DATA:Unit');
    assert.deepEqual(data.map(item => item.value).sort(), ['DATA:UnitMod.xml', 'DATA:UnitSdk.xml']);
    const art = await completePaths(service, from, 'ART:fx_');
    assert.deepEqual(art.map(item => item.value).sort(), ['ART:fx_mod.dds', 'ART:fx_sdk.dds']);
});

test('$DEFINE diagnostics preserve source offsets and keep uncertain expressions informational', () => {
    const text = '<AssetDeclaration><Defines><Define name="KNOWN" value="1"/><Define name="KNOWN" value="2"/></Defines><GameObject id="A" Value="=$KNOWN + $UNKNOWN"/></AssetDeclaration>';
    const document = parseXml(text, '/mod.xml'); const issues = defineIssues(document, [document]);
    assert.equal(issues.filter(issue => issue.code === 'xml.duplicate-define').length, 1);
    const missing = issues.find(issue => issue.code === 'xml.unresolved-define'); assert.ok(missing);
    assert.equal(text.slice(missing.start, missing.end), '$UNKNOWN'); assert.equal(missing.severity, 'information');
});
