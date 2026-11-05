import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {spawnSync} from 'node:child_process';

const python = process.platform === 'win32' ? 'python' : 'python3';
const script = path.resolve(__dirname, '../../scripts/check-xsd.py');
const probe = spawnSync(python, ['-c', 'from lxml import etree'], {encoding: 'utf8'});
const available = !probe.error && probe.status === 0;
const required = process.env.NEWSDK_REQUIRE_LXML === '1';
const skipped = available || required ? false : 'Python lxml is unavailable';
interface Report {status: string; diagnostics: {code: string; message: string; line?: number}[]; backend: string;}

const schema = (body: string): string => `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">${body}</xs:schema>`;
const simple = schema('<xs:element name="Value" type="xs:int"/>');
async function fixture(t: {after(fn: () => Promise<void>): void}) {
    assert.equal(available, true, 'NEWSDK_REQUIRE_LXML requires an installed Python lxml');
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-xsd-checker-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const entry = path.join(root, 'entry.xsd'), xml = path.join(root, 'input.xml');
    await fs.writeFile(entry, simple); await fs.writeFile(xml, '<Value>7</Value>');
    return {root, entry, xml};
}
function run(entry: string, xml: string, extraEnv: NodeJS.ProcessEnv = {}): {code: number | null; report: Report} {
    const result = spawnSync(python, [script, '--schema', entry, '--xml', xml, '--json'], {encoding: 'utf8', timeout: 15_000, maxBuffer: 256 * 1024, env: {...process.env, ...extraEnv}});
    assert.ifError(result.error);
    assert.ok(result.stdout, result.stderr);
    return {code: result.status, report: JSON.parse(result.stdout) as Report};
}

test('standalone XSD checker distinguishes a true valid XML from type errors and schema failures', {skip: skipped}, async t => {
    const {entry, xml} = await fixture(t);
    let result = run(entry, xml);
    assert.equal(result.code, 0); assert.equal(result.report.status, 'PASS');
    await fs.writeFile(xml, '<Value>not-an-integer</Value>');
    result = run(entry, xml);
    assert.equal(result.code, 1); assert.equal(result.report.status, 'FAIL');
    assert.equal(result.report.diagnostics[0].code, 'XSD_INVALID');
    assert.equal(result.report.diagnostics[0].line, 1);
    await fs.writeFile(entry, schema('<xs:element name="Value" type="xs:UndefinedType"/>'));
    result = run(entry, xml);
    assert.equal(result.code, 2); assert.equal(result.report.status, 'UNKNOWN');
    assert.equal(result.report.diagnostics[0].code, 'XSD_UNAVAILABLE');
});

test('standalone XSD checker rejects remote, outside-directory and symlink schema dependencies', {skip: skipped}, async t => {
    const {root, entry, xml} = await fixture(t);
    const outside = path.join(root, 'outside.xsd'), nested = path.join(root, 'selected');
    await fs.writeFile(outside, simple); await fs.mkdir(nested);
    const nestedEntry = path.join(nested, 'entry.xsd');
    for (const location of ['https://example.invalid/unsafe.xsd', '../outside.xsd']) {
        await fs.writeFile(nestedEntry, schema(`<xs:include schemaLocation="${location}"/>`));
        assert.equal(run(nestedEntry, xml).report.status, 'UNKNOWN');
    }
    await fs.symlink(outside, path.join(nested, 'alias.xsd'));
    await fs.writeFile(nestedEntry, schema('<xs:include schemaLocation="alias.xsd"/>'));
    assert.equal(run(nestedEntry, xml).report.status, 'UNKNOWN');
    await fs.writeFile(entry, schema('<xs:include schemaLocation="selected/valid.xsd"/>'));
    await fs.writeFile(path.join(nested, 'valid.xsd'), simple);
    assert.equal(run(entry, xml).report.status, 'PASS', 'contained local schema dependencies remain usable');
});

test('standalone XSD checker rejects DTDs and leaves unverified provenance wrappers unknown', {skip: skipped}, async t => {
    const {root, entry, xml} = await fixture(t);
    await fs.writeFile(xml, '<!DOCTYPE Value [<!ENTITY value SYSTEM "file:///unreadable-private-file">]><Value>&value;</Value>');
    let result = run(entry, xml);
    assert.equal(result.report.status, 'FAIL');
    assert.equal(result.report.diagnostics[0].code, 'XML_DTD_FORBIDDEN');
    await fs.writeFile(xml, '<Value>7</Value>');
    await fs.writeFile(entry, `<!DOCTYPE xs:schema [<!ENTITY ignored "x">]>${simple}`);
    result = run(entry, xml);
    assert.equal(result.report.status, 'UNKNOWN');
    assert.match(result.report.diagnostics[0].message, /DTD/);
    const wrapper = path.join(root, 'public-fx-types-wrapper.xsd'); await fs.writeFile(wrapper, simple);
    result = run(wrapper, xml);
    assert.equal(result.report.status, 'UNKNOWN');
    assert.equal(result.report.diagnostics[0].code, 'XSD_PROVENANCE_REQUIRED');
});

test('standalone XSD checker decodes file URIs exactly once for spaces, Unicode and literal escapes', {skip: skipped}, async t => {
    const {root, entry, xml} = await fixture(t);
    const name = '空 格%20%#.xsd';
    await fs.writeFile(path.join(root, name), simple);
    await fs.writeFile(entry, schema(`<xs:include schemaLocation="${encodeURIComponent(name)}"/>`));
    const result = run(entry, xml);
    assert.equal(result.report.status, 'PASS', result.report.diagnostics.map(issue => issue.message).join('\n'));
});

test('standalone XSD Unicode diagnostics survive legacy Windows pipe encodings', {skip: skipped}, async t => {
    const {entry, xml} = await fixture(t);
    await fs.writeFile(xml, '<Value>不是数字☃</Value>');
    const result = run(entry, xml, {PYTHONIOENCODING: 'ascii'});
    assert.equal(result.code, 1); assert.equal(result.report.status, 'FAIL');
    assert.ok(result.report.diagnostics.some(issue => issue.message.includes('不是数字☃')));
});
