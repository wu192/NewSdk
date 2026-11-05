import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {spawnSync} from 'node:child_process';
import {SkillRunner} from '../src/fx/skillRunner';
import {selectFxSchema} from '../src/fx/schemaSelection';

const python = process.platform === 'win32' ? 'python' : 'python3';
const hasLxml = spawnSync(python, ['-c', 'from lxml import etree'], {windowsHide: true}).status === 0;
const skip = !hasLxml && !process.env.NEWSDK_REQUIRE_LXML ? 'Python lxml is unavailable' : false;
const declaration = (body: string) => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${body}</AssetDeclaration>`;
const syntheticChecker = `import json,sys
if '--help' in sys.argv:
 print('--project-root --asset-index --schema --json'); sys.exit(0)
print(json.dumps({'format_version':1,'levels':{'V0':'PASS','V1':'PASS','V2':'SKIPPED','V3':'UNKNOWN'},'diagnostics':[{'level':'V3','severity':'unknown','code':'SHADER_MISSING','file':'input.xml','path':'/','message':'shader bytes not checked'}],'resources':[],'field_check_scope':'synthetic field fixture'}))
`;
async function setup(t: {after(fn: () => Promise<void>): void}) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-v2-integration-'));
    const runner = new SkillRunner({temporaryDirectory: path.join(root, 'temporary'), schemaStorageDirectory: path.join(root, 'schema-cache')});
    t.after(async () => {await runner.dispose(); await fs.rm(root, {recursive: true, force: true});});
    return {root, runner};
}

test('Configured real SDK drives standalone V2 PASS/FAIL without private Skill or SDK writes', {skip}, async t => {
    assert.equal(hasLxml, true, 'CI requires the real lxml checker');
    const {root, runner} = await setup(t);
    const sdk = path.resolve(__dirname, '../../../SDK');
    const selected = await selectFxSchema(sdk);
    assert.equal(selected.source, 'sdk-config'); assert.equal(selected.issue, undefined); assert.ok(selected.path);
    const xml = await fs.readFile(path.resolve(__dirname, '../../test/fixtures/fx/Data/flash.xml'), 'utf8');
    const valid = await runner.validateSchema({xml, schemaPath: selected.path});
    assert.deepEqual(valid.levels.map(level => level.status), ['UNKNOWN', 'PASS', 'PASS', 'UNKNOWN']);
    assert.equal(valid.schema, selected.path); assert.match(valid.schemaVersion!, /^[a-f0-9]{64}$/);
    assert.ok(valid.levels[2].rules.some(rule => rule.includes(selected.path!)), 'Report identifies the original schema instead of cache path');
    const invalid = await runner.validateSchema({xml: xml.replace('Type="GPU_PARTICLE"', 'Type="INVALID_PARTICLE_FAMILY"'), schemaPath: selected.path});
    assert.equal(invalid.levels[1].status, 'PASS'); assert.equal(invalid.levels[2].status, 'FAIL');
    assert.ok(invalid.levels[2].diagnostics?.some(issue => issue.line! > 0 && /INVALID_PARTICLE_FAMILY/.test(issue.message)));
    assert.equal(invalid.schemaVersion, valid.schemaVersion);
    assert.equal((await fs.readdir(path.join(root, 'schema-cache'))).length, 1, 'Unchanged SDK reuses its content-addressed XSD graph');
});

test('Skill checks merge actual public V2 while preserving V0/V1 and unknown shader state', {skip}, async t => {
    const {root, runner} = await setup(t);
    const skill = path.join(root, 'synthetic-skill'); await fs.mkdir(path.join(skill, 'scripts'), {recursive: true});
    await fs.writeFile(path.join(skill, 'scripts', 'validate_fx.py'), syntheticChecker);
    const schemaPath = path.resolve(__dirname, '../../../SDK/Schemas/xsd/CnC3Types.xsd');
    const valid = await runner.validate(skill, {xml: declaration('<ArmorTemplate id="Probe" Default="100"/>'), schemaPath});
    assert.deepEqual(valid.levels.map(level => level.status), ['PASS', 'PASS', 'PASS', 'UNKNOWN']);
    const invalid = await runner.validate(skill, {xml: declaration('<ArmorTemplate id="Probe" Default="not-a-number"/>'), schemaPath});
    assert.deepEqual(invalid.levels.map(level => level.status), ['PASS', 'PASS', 'FAIL', 'UNKNOWN']);
    assert.equal(invalid.raw.levels.V2, 'FAIL');
    assert.ok(invalid.raw.diagnostics.some(issue => issue.level === 'V2' && issue.line! > 0));
    assert.ok(invalid.raw.diagnostics.some(issue => issue.code === 'SHADER_MISSING'));
});

test('Changing XSD source updates the validation version and result on the next check', {skip}, async t => {
    const {root, runner} = await setup(t);
    await fs.mkdir(path.join(root, 'source'));
    const schemaPath = path.join(root, 'source', 'number.xsd');
    const schema = (type: string) => `<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="Number" type="xs:${type}"/></xs:schema>`;
    await fs.writeFile(schemaPath, schema('float'));
    const first = await runner.validateSchema({xml: '<Number>1.5</Number>', schemaPath});
    assert.equal(first.levels[2].status, 'PASS');
    await fs.writeFile(schemaPath, schema('int'));
    const changed = await runner.validateSchema({xml: '<Number>1.5</Number>', schemaPath});
    assert.equal(changed.levels[2].status, 'FAIL'); assert.notEqual(changed.schemaVersion, first.schemaVersion);
});

test('No schema stays SKIPPED; missing schema or Python returns UNKNOWN with a useful diagnosis', async t => {
    const {root, runner} = await setup(t);
    const xml = declaration('<FXList id="Probe"/>');
    assert.equal((await runner.validateSchema({xml})).levels[2].status, 'SKIPPED');
    const missing = await runner.validateSchema({xml, schemaPath: path.join(root, 'missing.xsd')});
    assert.equal(missing.levels[1].status, 'PASS'); assert.equal(missing.levels[2].status, 'UNKNOWN');
    assert.ok(missing.levels[2].diagnostics?.length);
    await fs.mkdir(path.join(root, 'source'));
    const schemaPath = path.join(root, 'source', 'root.xsd');
    await fs.writeFile(schemaPath, '<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="Number" type="xs:int"/></xs:schema>');
    const unavailable = new SkillRunner({pythonPath: path.join(root, 'unavailable-python'), temporaryDirectory: path.join(root, 'unavailable-temporary')});
    t.after(() => unavailable.dispose());
    const report = await unavailable.validateSchema({xml: '<Number>1</Number>', schemaPath});
    assert.equal(report.levels[2].status, 'UNKNOWN');
    assert.ok(report.levels[2].diagnostics?.some(issue => issue.message.includes('Python')));
});
