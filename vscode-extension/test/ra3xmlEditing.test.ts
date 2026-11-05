import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {XsdModel} from '../src/ra3xml/model/xsdModel';
import {parseXml} from '../src/ra3xml/core/xmlDocument';
import {completionAt, schemaIssues, unresolvedReferenceSeverity} from '../src/ra3xml/providers/languageFeatures';
import {completionField, valueCompletionState, visibleDefineDocuments, defineDeclarations, defineNameAt, documentOutline, semanticRanges} from '../src/ra3xml/providers/editorFeatures';
import {AssetService} from '../src/ra3xml/services/assetService';

const schema = `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:a="urn:reference">
<xs:simpleType name="Flags"><xs:list><xs:simpleType><xs:restriction base="xs:string"><xs:enumeration value="AIR"/><xs:enumeration value="LAND"/><xs:enumeration value="WATER"/></xs:restriction></xs:simpleType></xs:list></xs:simpleType>
<xs:simpleType name="GameObjectRef" a:refType="GameObject"><xs:restriction base="xs:string"/></xs:simpleType>
<xs:complexType name="GameObject"><xs:sequence><xs:element name="CreateObject" type="GameObjectRef" minOccurs="0"/><xs:element name="RequiredUpgrade" type="GameObjectRef" minOccurs="0"/></xs:sequence>
<xs:attribute name="id" use="required"/><xs:attribute name="KindOf" type="Flags"/><xs:attribute name="Count" type="xs:unsignedByte"/><xs:attribute name="Enabled" type="xs:boolean" default="true"/>
</xs:complexType><xs:element name="GameObject" type="GameObject"/>
<xs:element name="AssetDeclaration"><xs:complexType><xs:choice minOccurs="0" maxOccurs="unbounded"><xs:element name="GameObject" type="GameObject"/></xs:choice></xs:complexType></xs:element>
<xs:element name="Extensible"><xs:complexType><xs:sequence><xs:any minOccurs="0"/></xs:sequence><xs:anyAttribute/></xs:complexType></xs:element>
</xs:schema>`;
async function fixture(t: {after(fn: () => Promise<void>): void}) {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-editing-')));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    await fs.writeFile(path.join(root, 'types.xsd'), schema); return {root, model: await XsdModel.load(root)};
}

test('unknown static references honor configured severity while partial index results stay informational', () => {
    assert.equal(unresolvedReferenceSeverity('complete', 'warning'), 'warning');
    assert.equal(unresolvedReferenceSeverity('complete', 'information'), 'information');
    assert.equal(unresolvedReferenceSeverity('complete', 'off'), undefined);
    for (const value of ['warning', 'information', 'off']) assert.equal(unresolvedReferenceSeverity('partial', value), 'information');
});

test('flag completion replaces only the current item and excludes already selected flags', async t => {
    const {model} = await fixture(t);
    const text = '<AssetDeclaration><GameObject id="A" KindOf="AIR LA WATER"/></AssetDeclaration>';
    const offset = text.indexOf('LA WATER') + 2, document = parseXml(text);
    const current = completionAt(document, offset)!;
    const state = valueCompletionState(current, offset, completionField(model, current));
    assert.equal(state.prefix, 'LA'); assert.equal(text.slice(state.start, offset), 'LA');
    assert.deepEqual(state.values, ['LAND']);
    const middle = text.replace('LA WATER', 'LAND WATER'), middleContext = completionAt(parseXml(middle), offset)!;
    const middleState = valueCompletionState(middleContext, offset, completionField(model, middleContext));
    assert.equal(middle.slice(middleState.start, middleState.end), 'LAND', 'accepting completion in the middle must replace the remaining word suffix');
    const empty = '<AssetDeclaration><GameObject id="A" KindOf="AIR "/></AssetDeclaration>', at = empty.indexOf('AIR ') + 4;
    const trailing = completionAt(parseXml(empty), at)!;
    assert.deepEqual(valueCompletionState(trailing, at, completionField(model, trailing)).values, ['LAND', 'WATER']);
});

test('text reference fields use XSD target types and actual continuation ranges', async t => {
    const {model} = await fixture(t);
    for (const name of ['CreateObject', 'RequiredUpgrade']) {
        const text = `<AssetDeclaration><GameObject id="A"><${name}>  Uni  </${name}></GameObject></AssetDeclaration>`;
        const at = text.indexOf('Uni') + 3, current = completionAt(parseXml(text), at)!;
        assert.equal(completionField(model, current)?.referenceType, 'GameObject');
        assert.equal(model.getReferenceType(current.node!), 'GameObject');
        assert.equal(text.slice(current.start, at), 'Uni');
    }
});

test('unknown schema names and primitive errors are diagnosed without rejecting expressions or wildcards', async t => {
    const {model} = await fixture(t);
    const text = '<AssetDeclaration><GameObject id="A" Bogus="1" Count="256" Enabled="yes"><Typo/></GameObject><GameObject id="B" Count="=$COUNT" Enabled="$ENABLED"/></AssetDeclaration>';
    const issues = schemaIssues(parseXml(text, '/mod.xml'), model);
    assert.ok(issues.some(issue => issue.code === 'xml.unknown-attribute' && text.slice(issue.start, issue.end) === 'Bogus'));
    assert.ok(issues.some(issue => issue.code === 'xml.unknown-element' && text.slice(issue.start, issue.end) === 'Typo'));
    assert.equal(issues.filter(issue => issue.code === 'xml.type').length, 2);
    assert.ok(schemaIssues(parseXml('<AssetDeclaration><GameObject id=""/></AssetDeclaration>'), model).some(issue => issue.code === 'xml.empty-id'));
    assert.ok(!schemaIssues(parseXml('<Extensible Custom="yes"><Custom/></Extensible>'), model).some(issue => issue.code.startsWith('xml.unknown')));
    model.complete = false;
    assert.ok(schemaIssues(parseXml(text), model).filter(issue => issue.code.startsWith('xml.unknown')).every(issue => issue.severity === 'information'));
});

test('macro definitions obey outgoing Include scope, preserve duplicate sources, and expose exact source ranges', () => {
    const main = parseXml('<AssetDeclaration><GameObject id="A" Value="=$VALUE + $OTHER"/></AssetDeclaration>', '/mod/main.xml');
    const included = parseXml('<AssetDeclaration><Defines><Define name="VALUE" value="4"/></Defines></AssetDeclaration>', '/mod/inc.xml');
    const unrelated = parseXml('<AssetDeclaration><Defines><Define name="OTHER" value="9"/></Defines></AssetDeclaration>', '/mod/other.xml');
    const edge = {from: main.uri, to: included.uri, mode: 'all' as const, source: {uri: main.uri, nodePath: '', start: 0, end: 0}};
    const visible = visibleDefineDocuments(main.uri, [main, included, unrelated], [edge]);
    assert.deepEqual(visible.map(document => document.uri).sort(), [main.uri, included.uri].sort());
    assert.deepEqual(defineDeclarations(visible).map(record => [record.name, record.value]), [['VALUE', '4']]);
    const at = main.text.indexOf('$VALUE') + 3, selected = defineNameAt(main, at)!;
    assert.equal(main.text.slice(selected.start, selected.end), '$VALUE');
    assert.equal(defineNameAt(included, included.text.indexOf('VALUE') + 1)?.name, 'VALUE');
    const second = parseXml(included.text, '/mod/duplicate.xml');
    assert.equal(defineDeclarations([included, second]).length, 2, 'ambiguous definitions remain selectable');
});

test('outline and semantic ranges reuse the shared definitions and cover macros, Includes and resource paths', async t => {
    const {root, model} = await fixture(t), file = path.join(root, 'main.xml');
    const text = '<AssetDeclaration><Defines><Define name="COUNT" value="1"/></Defines><Includes><Include type="all" source="missing.xml"/></Includes><GameObject id="A" Count="=$COUNT"><CreateObject>B</CreateObject></GameObject><GameObject id="B" Image="ART:fx_flash.tga"/></AssetDeclaration>';
    await fs.writeFile(file, text); const service = new AssetService({roots: [root], modRoot: root, schema: model}); t.after(() => service.dispose());
    await service.ensure(file); await service.whenComplete();
    const source = service.getDocument(file)!;
    assert.deepEqual(documentOutline(source).map(entry => entry.name), ['$COUNT', 'GameObject: A', 'GameObject: B']);
    const ranges = semanticRanges(source, service.findAssets(), service.getReferences(file));
    const tokens = ranges.map(item => [item.type, text.slice(item.start, item.end)]);
    assert.ok(tokens.some(([type, value]) => type === 'newsdkAsset' && value === 'A'));
    assert.ok(tokens.some(([type, value]) => type === 'newsdkReference' && value === 'B'));
    assert.ok(tokens.some(([type, value]) => type === 'newsdkDefine' && value === '$COUNT'));
    assert.ok(tokens.some(([type, value]) => type === 'newsdkInclude' && value === 'missing.xml'));
    assert.ok(tokens.some(([type, value]) => type === 'newsdkResource' && value === 'ART:fx_flash.tga'));
    for (let i = 1; i < ranges.length; i++) assert.ok(ranges[i - 1].end <= ranges[i].start, 'tokens cannot overlap');
});
