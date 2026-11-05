import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { SkillRunner } from '../src/fx/skillRunner';

// Synthetic protocol fixture, not the separately distributed private Skill.
const fixture = `import json, pathlib, sys, time
args = sys.argv[1:]
if '--help' in args:
    print('--description --intent --recipe --name --output --template --set --project-root --asset-index --schema --json')
    sys.exit(0)
def val(flag):
    return args[args.index(flag) + 1]
if args and args[0] == 'generate':
    description = val('--description') if '--description' in args else ''
    if description == 'timeout': time.sleep(5)
    if description == 'overflow': print('x' * 100000); sys.exit(0)
    output = pathlib.Path(val('--output'))
    output.write_text('<AssetDeclaration xmlns="uri:ea.com:eala:asset"/>')
    (output.parent / 'assets').mkdir()
    (output.parent / 'assets' / 'fixture.tga').write_bytes(b'fixture')
    payload = '../escape' if description == 'escape' else 'assets/fixture.tga'
    output.with_suffix('.intent.json').write_text(json.dumps({'resource_files_copied':[payload], 'intent':{'description':description},'external_resources':['GPUParticle.fx']}))
    print(json.dumps({'xml':str(output),'V1':'PASS','V2':'SKIPPED','V3':'UNKNOWN'}))
elif args and args[0] == 'edit':
    original = pathlib.Path(args[1]).read_text()
    output = pathlib.Path(val('--output'))
    output.write_text(original.replace('Low="1"', 'Low="6"'))
    output.with_suffix('.diff').write_text('-Low="1"\\n+Low="6"\\n')
    print(json.dumps({'xml':str(output)}))
else:
    xml = pathlib.Path(args[0]).read_text()
    if 'invalid-report' in xml:
        print('{}'); sys.exit(0)
    failing = 'FAIL' in xml
    report = {'format_version':1,'levels':{'V0':'FAIL' if failing else 'PASS','V1':'PASS','V2':'SKIPPED','V3':'UNKNOWN'},'diagnostics':[{'level':'V3','severity':'unknown','code':'SHADER_MISSING','file':'input.xml','path':'Draw/Gpu','message':'shader bytes absent'}],'resources':[{'type':'Shader','id':'GPUParticle.fx','payload_status':'missing'}],'checked_files':[str(pathlib.Path(args[0]).relative_to(pathlib.Path(val('--project-root'))))],'schema_scope':'No XSD supplied.','field_check_scope':'Fixture rule coverage.'}
    print(json.dumps(report))
    sys.exit(1 if failing else 0)
`;

async function setup(t: { after(fn: () => Promise<void>): void }) {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-runner-test-'));
    t.after(() => fs.rm(root, { recursive: true, force: true }));
    const skill = path.join(root, 'imported-skill');
    await fs.mkdir(path.join(skill, 'scripts'), { recursive: true });
    for (const script of ['generate_fx.py', 'validate_fx.py']) { await fs.writeFile(path.join(skill, 'scripts', script), fixture); }
    const runner = new SkillRunner({ temporaryDirectory: path.join(root, 'temporary'), timeoutMs: 2000, maxOutputBytes: 16 * 1024 });
    t.after(() => runner.dispose());
    return { root, skill, runner };
}

test('Skill runner passes arbitrary description as one argv value and stages XML/resources outside the project', async t => {
    const { root, skill, runner } = await setup(t);
    const project = path.join(root, 'mod');
    await fs.mkdir(project);
    const description = 'blue sparks; $(touch unexpected) `echo no` "quoted"';
    const result = await runner.generate(skill, { description, recipe: 'impact', name: 'SafeName' });
    assert.ok(result.outputDirectory.startsWith(path.join(root, 'temporary')));
    assert.match(result.xml, /AssetDeclaration/);
    assert.equal((result.intent as { intent: { description: string } }).intent.description, description);
    assert.deepEqual(result.files.map(file => file.relativePath), ['assets/fixture.tga']);
    assert.equal(await fs.readFile(result.files[0].path, 'utf8'), 'fixture');
    assert.deepEqual(await fs.readdir(project), []);
    await runner.disposeResult(result);
    await assert.rejects(fs.stat(result.outputDirectory), /ENOENT/);
});

test('Skill runner retains V2 skipped and V3 unknown with rule coverage and resource payload status', async t => {
    const { skill, runner } = await setup(t);
    const report = await runner.validate(skill, { xml: '<AssetDeclaration/>' });
    assert.deepEqual(report.levels.map(level => level.status), ['PASS', 'PASS', 'SKIPPED', 'UNKNOWN']);
    assert.equal(report.raw.resources[0].payload_status, 'missing');
    assert.ok(report.levels.every(level => level.rules.length > 0));
    assert.match(report.levels[3].diagnostics![0].message, /shader bytes absent/);
});

test('Skill runner parses actual failed checks from exit 1 without converting them into success', async t => {
    const { skill, runner } = await setup(t);
    const report = await runner.validate(skill, { xml: '<FAIL/>' });
    assert.equal(report.levels[0].status, 'FAIL');
    await assert.rejects(runner.validate(skill, { xml: '<invalid-report/>' }), /报告版本/);
});

test('Skill draft validation retains project-relative directory in its temporary collection without copying MOD', async t => {
    const { root, skill, runner } = await setup(t);
    const sourceRelativePath = 'Data/Effects/current.xml';
    const report = await runner.validate(skill, { xml: '<AssetDeclaration/>', sourceRelativePath });
    assert.deepEqual(report.raw.checked_files, [path.join('Data', 'Effects', 'current.xml')]);
    assert.deepEqual(await fs.readdir(path.join(root, 'temporary')), [], 'check staging is removed after reporting');
    for (const escaped of ['../outside.xml', '/absolute.xml', 'C:\\outside.xml', 'Data/../../outside.xml']) {
        await assert.rejects(runner.validate(skill, { xml: '<A/>', sourceRelativePath: escaped }), /项目相对路径/);
    }
});

test('Skill edit is staged and preserves bytes beyond the selected synthetic attribute', async t => {
    const { skill, runner } = await setup(t);
    const xml = '<A><!--keep--><T id="Selected"><Size Low="1" High="9"/></T><Other unknown="unchanged"/></A>';
    const result = await runner.edit(skill, { xml, templateId: 'Selected', settings: { size: 6 } });
    assert.equal(result.xml, xml.replace('Low="1"', 'Low="6"'));
    assert.match(result.diff!, /Low="6"/);
    assert.deepEqual(result.files, []);
    await assert.rejects(runner.edit(skill, { xml, templateId: 'Selected', settings: { shell: 'anything' } }), /受控属性/);
});

test('Skill runner rejects escaped output payloads and removes failed stages', async t => {
    const { root, skill, runner } = await setup(t);
    await assert.rejects(runner.generate(skill, { description: 'escape' }), /越过临时区/);
    assert.deepEqual(await fs.readdir(path.join(root, 'temporary')), []);
});

test('Skill runner enforces timeout and output bounds', async t => {
    const { root, skill } = await setup(t);
    const runner = new SkillRunner({ temporaryDirectory: path.join(root, 'bounded'), timeoutMs: 250, maxOutputBytes: 1024 });
    t.after(() => runner.dispose());
    await assert.rejects(runner.generate(skill, { description: 'timeout' }), /超时/);
    await assert.rejects(runner.generate(skill, { description: 'overflow' }), /输出超过上限/);
    assert.deepEqual(await fs.readdir(path.join(root, 'bounded')), []);
});

test('Skill runner checks live --help contract before running and rejects script symlinks outside import', async t => {
    const { root, skill, runner } = await setup(t);
    await fs.writeFile(path.join(skill, 'scripts', 'generate_fx.py'), 'print("unsupported CLI")\n');
    await assert.rejects(runner.generate(skill, { description: 'hello' }), /--help 参数/);
    const external = path.join(root, 'outside.py');
    await fs.writeFile(external, fixture);
    await fs.unlink(path.join(skill, 'scripts', 'validate_fx.py'));
    await fs.symlink(external, path.join(skill, 'scripts', 'validate_fx.py'));
    await assert.rejects(runner.validate(skill, { xml: '<A/>' }), /越过导入目录/);
});
