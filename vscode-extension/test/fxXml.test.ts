import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { parseXml, toFxNode, textContent, evaluateExpression, parseScalar } from '../src/fx/xmlDocument';
import { applyTextPatches, prepareTransaction, prepareMultiDocumentTransaction, createChildInsertion, createAttributeInsertionPatch, prepareFieldTransaction, EditConflictError, EditGesture } from '../src/fx/editTransaction';

const source = `\uFEFF<?xml version="1.0"?>\r\n<AssetDeclaration xmlns="urn:sage" xmlns:x="urn:extension">\r\n  <!-- keep exactly -->\r\n  <FXParticleSystemTemplate id='Smoke' Type="GPU_PARTICLE">\r\n    <Lifetime High='0012.0' Low="1e1" />\r\n    <x:Unrecognised x:flag='&quot;&amp;' >untouched <![CDATA[<raw>]]></x:Unrecognised>\r\n    <Lifetime Low="100" High="200"/>\r\n  </FXParticleSystemTemplate>\r\n</AssetDeclaration>\r\n`;

test('FX XML token round trip changes only the selected lexical token', () => {
    const document = parseXml(source, 'file:///smoke.xml', 19);
    assert.deepEqual(document.issues, []);
    const lifetimes = document.elements.filter(node => node.localName === 'Lifetime');
    assert.equal(lifetimes[0].namespaceUri, 'urn:sage');
    assert.equal(lifetimes[0].attributeTokens.High.quote, "'");
    assert.equal(lifetimes[0].attributeTokens.High.rawValue, '0012.0');
    const patches = prepareTransaction(document, [{ nodePath: lifetimes[0].path, attribute: 'High', documentVersion: 19, expectedValue: '0012.0', value: '14.75' }]);
    assert.equal(patches.length, 1);
    const edited = applyTextPatches(source, patches);
    assert.equal(edited, source.replace("High='0012.0'", "High='14.75'"));
    assert.equal(parseXml(edited).valid, true);
    assert.equal(applyTextPatches(edited, [{ ...patches[0], end: patches[0].start + patches[0].text.length, text: patches[0].expected, expected: patches[0].text }]), source);
    const unknown = document.elements.find(node => node.localName === 'Unrecognised')!;
    assert.equal(unknown.namespaceUri, 'urn:extension');
    assert.equal(unknown.attributes['x:flag'], '"&');
    assert.equal(textContent(unknown), 'untouched <raw>');
    assert.equal(document.elementAt(lifetimes[0].attributeTokens.Low.valueStart), lifetimes[0]);
    assert.equal(toFxNode(lifetimes[0]).attributes.High, '0012.0');
});

test('FX XML paths ignore whitespace/comments and identify same-name siblings', () => {
    const original = parseXml('<A><Value Low="1"/><B/><Value Low="2"/></A>');
    const modified = parseXml('<A>\n<!--comment--><Value Low="1"/><B/>\n<Value Low="2"/></A>');
    assert.deepEqual(original.elements.map(node => node.path), modified.elements.map(node => node.path));
    assert.equal(original.find('/A[1]/Value[2]')!.attributes.Low, '2');
});

test('FX XML rejects incomplete, unsafe and ambiguous inputs with exact ranges', () => {
    for (const text of [
        '<A><B></A>', '<A v="unfinished>', '<A><B>', '<A/><B/>', '<A v="1"v="2"/>',
        '<A><!DOCTYPE B SYSTEM "https://example.invalid/entity"></A>',
        '<!DOCTYPE A [<!ENTITY bomb "bad">]><A>&bomb;</A>', '<A>&unknown;</A>', '<A>&amp</A>',
        '<A>&#0;</A>', '<A>&#x110000;</A>', '<A>]]></A>', '<x:A/>', '<A a="a<b"/>',
        '<??><A/>', '<A/><?xml version="1.0"?>', '<?xml?><A/>', '<A xmlns:x=""/>',
        '<A xmlns:a="urn:x" xmlns:b="urn:x" a:v="1" b:v="2"/>', '<A>\0</A>',
    ]) {
        const document = parseXml(text);
        assert.equal(document.valid, false, text);
        assert.ok(document.issues.length, text);
        assert.ok(document.issues.every(issue => issue.start >= 0 && issue.end <= text.length), text);
    }
    assert.equal(parseXml('<A>&amp;&#32;&#x1F4A5;</A>').valid, true);
    assert.equal(textContent(parseXml('<A>&amp;&#32;&#x1F4A5;</A>').roots[0]), '& 💥');
    assert.equal(parseXml('<A>' + '<B>'.repeat(257) + '</B>'.repeat(257) + '</A>').valid, false);
});

test('FX XML transactions reject stale values, versions, nodes and overlapping edits atomically', () => {
    const document = parseXml('<A><Value Low="10" High="20"/></A>', 'file:///a.xml', 7);
    const nodePath = '/A[1]/Value[1]';
    const intent = { nodePath, attribute: 'Low', expectedValue: '10', value: '12', documentVersion: 7 };
    for (const change of [{ documentVersion: 6 }, { expectedValue: '11' }, { nodePath: '/missing' }, { attribute: 'Missing' }]) {
        assert.throws(() => prepareTransaction(document, [{ ...intent, ...change }]), EditConflictError);
    }
    assert.throws(() => prepareTransaction(document, [intent, intent]), /重复|重叠/);
    assert.equal(document.text, '<A><Value Low="10" High="20"/></A>');
    const other = parseXml('<A x="v"/>', 'file:///b.xml', 3);
    assert.throws(() => prepareMultiDocumentTransaction([
        { document, documentVersion: 7, intents: [intent] },
        { document: other, documentVersion: 2, intents: [{ nodePath: '/A[1]', attribute: 'x', expectedValue: 'v', value: 'new', documentVersion: 2 }] },
    ]), EditConflictError);
    const patches = prepareTransaction(document, [intent, { ...intent, attribute: 'High', expectedValue: '20', value: '25' }]);
    assert.equal(applyTextPatches(document.text, patches), '<A><Value Low="12" High="25"/></A>');
    assert.throws(() => applyTextPatches(document.text.replace('10', '11'), patches), EditConflictError);
});

test('FX XML attribute editing escapes replacement values and preserves quote style', () => {
    const document = parseXml(`<A value='old'/>`, '', 1);
    const patches = prepareTransaction(document, [{ nodePath: '/A[1]', attribute: 'value', value: `a'"&<\n`, documentVersion: 1 }]);
    const text = applyTextPatches(document.text, patches);
    assert.equal(text, `<A value='a&apos;"&amp;&lt;&#10;'/>`);
    assert.equal(parseXml(text).roots[0].attributes.value, `a'"&<\n`);
});

test('FX gestures have one committed patch and cannot outlive document version or asset', () => {
    const document = parseXml('<A Value="1"/>', 'file:///a.xml', 4);
    const gesture = new EditGesture(); gesture.begin('drag', document);
    for (let i = 2; i <= 90; i++) gesture.update('drag', [{ nodePath: '/A[1]', attribute: 'Value', value: String(i), expectedValue: '1' }]);
    assert.equal(document.text, '<A Value="1"/>');
    const patches = gesture.commit('drag', document);
    assert.equal(patches.length, 1);
    assert.equal(applyTextPatches(document.text, patches), '<A Value="90"/>');
    assert.equal(gesture.draft, undefined);
    assert.throws(() => gesture.commit('drag', document), EditConflictError);
    gesture.begin('second', document); gesture.update('second', [{ nodePath: '/A[1]', attribute: 'Value', value: '3' }]);
    assert.throws(() => gesture.commit('second', parseXml(document.text, document.uri, 5)), EditConflictError);
    gesture.begin('third', document); gesture.cancel();
    assert.equal(gesture.draft, undefined);
});

test('FX child insertions follow an explicit XSD sequence without reserializing the parent', () => {
    const text = '<P xmlns="urn:sage">\r\n  <!-- comment -->\r\n  <First x="1"/>\r\n  <Last/>\r\n</P>';
    const document = parseXml(text, '', 2);
    const patch = createChildInsertion(document, '/P[1]', 'Middle', '<Middle value="2"/>', ['First', 'Middle', 'Last'], 2);
    const edited = applyTextPatches(text, [patch]);
    assert.equal(edited, text.replace('<Last/>', '<Middle value="2"/>\r\n  <Last/>'));
    assert.deepEqual(parseXml(edited).roots[0].children.map(node => node.localName), ['First', 'Middle', 'Last']);
    const empty = parseXml('<P/>', '', 2);
    const emptyEdited = applyTextPatches(empty.text, [createChildInsertion(empty, '/P[1]', 'Child', '<Child/>', ['Child'], 2)]);
    assert.equal(parseXml(emptyEdited).valid, true);
    assert.throws(() => createChildInsertion(document, '/P[1]', 'NoContract', '<NoContract/>', ['First', 'Last'], 2), /XSD/);
    assert.throws(() => createChildInsertion(document, '/P[1]', 'First', '<!DOCTYPE B><First/>', ['First'], 2), /无效/);
});

test('FX controlled arithmetic and scalar units never execute code or assume unknown values', () => {
    assert.deepEqual(evaluateExpression('=(2 + 3) * $Scale / 2', { $Scale: 4 }), { status: 'evaluated', value: 10 });
    assert.deepEqual(evaluateExpression('1e-3 + -.2'), { status: 'evaluated', value: -0.199 });
    assert.equal(evaluateExpression('Missing + 1').status, 'unsupported');
    assert.equal(evaluateExpression('globalThis.process.exit(1)').status, 'unsupported');
    assert.equal(evaluateExpression('(()=>{throw 1})()').status, 'unsupported');
    assert.equal(evaluateExpression('1 / 0').status, 'invalid');
    assert.equal(evaluateExpression('1e999').status, 'invalid');
    assert.equal(evaluateExpression('('.repeat(300) + '1' + ')'.repeat(300)).status, 'unsupported');
    const delay = parseScalar('250ms', 'time'); assert.equal(delay.status, 'evaluated');
    if (delay.status === 'evaluated') assert.equal(delay.value, 0.25);
    const angle = parseScalar('90d', 'angle'); assert.equal(angle.status, 'evaluated');
    if (angle.status === 'evaluated') assert.equal(angle.value, Math.PI / 2);
    assert.equal(parseScalar('5d', 'time').status, 'invalid');
    assert.equal(parseScalar('5deg', 'angle').status, 'unsupported');
    assert.equal(parseScalar('$Speed', 'angle', { $Speed: 2 }).status, 'evaluated');
    assert.equal(parseScalar('5s', 'number').status, 'unsupported');
});


test('FX schema defaults insert minimally, combine same-point edits, and reject stale/unapproved values', () => {
    const text = `<AssetDeclaration><!--retain--><Gpu Shader='GPUParticle.fx' GeometryType='SIMPLE_QUAD' /></AssetDeclaration>`;
    const document = parseXml(text, 'file:///a.xml', 4);
    const nodePath = '/AssetDeclaration[1]/Gpu[1]';
    const intent = {nodePath, attribute: 'SingleRow', value: '1', documentVersion: 4};
    const patch = createAttributeInsertionPatch(document, intent);
    assert.equal(applyTextPatches(text, [patch]), text.replace("GeometryType='SIMPLE_QUAD'", "GeometryType='SIMPLE_QUAD' SingleRow='1'"));
    const patches = prepareFieldTransaction(document, [intent,
        {...intent, attribute: 'RandTex', value: '1'},
        {...intent, attribute: 'FramesPerRow', value: '4'},
        {...intent, attribute: 'Shader', expectedValue: 'GPUParticle.fx', value: 'Changed.fx'},
    ]);
    assert.equal(patches.length, 2);
    const updated = parseXml(applyTextPatches(text, patches));
    assert.equal(updated.valid, true);
    assert.equal(updated.find(nodePath)!.attributes.SingleRow, '1');
    assert.equal(updated.find(nodePath)!.attributes.RandTex, '1');
    assert.equal(updated.find(nodePath)!.attributes.FramesPerRow, '4');
    assert.equal(updated.find(nodePath)!.attributes.Shader, 'Changed.fx');
    assert.throws(() => createAttributeInsertionPatch(document, {...intent, expectedValue: '0'}), EditConflictError);
    assert.throws(() => createAttributeInsertionPatch(document, {...intent, documentVersion: 3}), EditConflictError);
    assert.throws(() => createAttributeInsertionPatch(document, {...intent, attribute: 'Shader'}), EditConflictError);
    assert.throws(() => createAttributeInsertionPatch(document, {...intent, attribute: 'xmlns'}), /不合法/);
    assert.throws(() => prepareFieldTransaction(document, [intent, intent]), /重复/);
    const escaped = applyTextPatches(text, [createAttributeInsertionPatch(document, {...intent, value: "a'&<"})]);
    assert.equal(parseXml(escaped).find(nodePath)!.attributes.SingleRow, "a'&<");
});

test('unchanged field commits preserve numeric/entity lexical spelling', () => {
    const document = parseXml('<A id="&#x41;" Low="0001.00"/>', '', 1);
    assert.deepEqual(prepareTransaction(document, [{nodePath:'/A[1]', attribute:'id', expectedValue:'A', value:'A', documentVersion:1}]), []);
    assert.deepEqual(prepareTransaction(document, [{nodePath:'/A[1]', attribute:'Low', expectedValue:'0001.00', value:'0001.00', documentVersion:1}]), []);
});
