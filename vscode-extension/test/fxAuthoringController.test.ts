import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import type { AuthoringHost, FxAuthoringController as Controller } from '../src/fx/authoringController';
import type { FxAuthoringView, FxValidationReport } from '../src/fx/skillIntent';
import type { FxSkillManager } from '../src/fx/skillManager';
import type { SkillRunner, SkillValidateOptions } from '../src/fx/skillRunner';

const wrap = (body: string): string => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${body}</AssetDeclaration>`;
const report = (): FxValidationReport => ({ checkedAt: new Date().toISOString(), levels: [
    { level: 'V0', status: 'PASS', summary: 'fixture field checks', rules: ['fixture'] },
    { level: 'V1', status: 'PASS', summary: 'fixture XML check', rules: ['fixture'] },
    { level: 'V2', status: 'SKIPPED', summary: 'no schema', rules: [] },
    { level: 'V3', status: 'UNKNOWN', summary: 'external shader unavailable', rules: [] },
] });

// Load the controller against a tiny VS Code boundary; normal parser/patch logic is real.
const uri = (file: string) => ({ fsPath: file, scheme: 'file', toString: () => `file://${file}` });
let editCalls = 0;
let schemaOverride = '';
const vscodeStub = {
    workspace: {
        registerTextDocumentContentProvider: () => ({ dispose() {} }),
        getConfiguration: () => ({ get: (key: string, fallback: unknown) => key === 'fxSchemaPath' ? schemaOverride : fallback }),
        applyEdit: async () => { editCalls++; return true; },
    },
    window: { showWarningMessage: async () => '确认应用' },
    Uri: { file: uri, parse: uri },
    Range: class { constructor(..._args: unknown[]) {} },
    WorkspaceEdit: class { replace(..._args: unknown[]) {} createFile(..._args: unknown[]) {} },
    commands: { executeCommand: async () => undefined },
};
const moduleLoader = require('node:module') as { _load: (request: string, ...args: unknown[]) => unknown };
const originalLoad = moduleLoader._load;
let FxAuthoringController: typeof Controller;
try {
    moduleLoader._load = function (request, ...args) { return request === 'vscode' ? vscodeStub : originalLoad.call(this, request, ...args); };
    FxAuthoringController = (require('../src/fx/authoringController') as { FxAuthoringController: typeof Controller }).FxAuthoringController;
} finally { moduleLoader._load = originalLoad; }

function hostFor(file: string, xml: string) {
    const document = { uri: uri(file), version: 1, isDirty: true, getText: () => xml, positionAt: (offset: number) => ({ line: 0, character: offset }) };
    const views: FxAuthoringView[] = [];
    const host = {
        document, target: { type: 'FXList', id: 'A' }, modRoot: path.dirname(file),
        editable: async () => undefined, publish: async (view: FxAuthoringView) => { views.push(view); }, preview: async () => undefined,
    } as unknown as AuthoringHost;
    return { host, document, views };
}

test('FX current check cannot attach an old XML report to a document version changed during validation', async () => {
    let started!: () => void;
    const checking = new Promise<void>(resolve => { started = resolve; });
    let finish!: (value: FxValidationReport) => void;
    const pending = new Promise<FxValidationReport>(resolve => { finish = resolve; });
    const manager = { current: async () => ({ rootPath: '/imported-skill', version: 'fixture' }) } as unknown as FxSkillManager;
    const runner = { validate: async () => { started(); return pending; } } as unknown as SkillRunner;
    const controller = new FxAuthoringController(manager, () => runner);
    const { host, document, views } = hostFor('/mod/effect.xml', wrap('<FXList id="A"/>'));
    try {
        const action = controller.action(host, { action: 'check' });
        await checking;
        document.version++;
        finish(report());
        await assert.rejects(action, /变化|更新/);
        assert.ok(!views.at(-1)?.draft || views.at(-1)?.draft?.stale, 'old results cannot appear current');
    } finally { controller.dispose(); }
});

test('FX pending edit stays bound to the selected asset and cannot apply after selection changes', async () => {
    const controller = new FxAuthoringController();
    const xml = wrap('<FXList id="A" value="1"/><FXList id="B" value="2"/>');
    const { host } = hostFor('/mod/effect.xml', xml);
    const before = editCalls;
    try {
        await controller.action(host, { action: 'import', mode: 'edit', xml: wrap('<FXList id="A" value="9"/>') });
        host.target = { type: 'FXList', id: 'B' };
        await assert.rejects(controller.action(host, { action: 'apply' }), /目标|资产|选择|身份/);
        assert.equal(editCalls, before, 'no WorkspaceEdit reaches VS Code for another selected target');
    } finally { controller.dispose(); }
});

test('FX generated resources cannot escape MOD through an existing assets directory symlink', async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-authoring-boundary-'));
    t.after(() => fs.rm(root, { recursive: true, force: true }));
    const mod = path.join(root, 'mod'), outside = path.join(root, 'outside'), staged = path.join(root, 'staged');
    await Promise.all([fs.mkdir(mod), fs.mkdir(outside), fs.mkdir(staged)]);
    await fs.symlink(outside, path.join(mod, 'assets'), process.platform === 'win32' ? 'junction' : 'dir');
    const payload = path.join(staged, 'fixture.tga');
    await fs.writeFile(payload, 'synthetic payload');
    const file = path.join(mod, 'effect.xml');
    await fs.writeFile(file, wrap('<FXList id="A"/>'));
    const manager = { current: async () => ({ rootPath: '/imported-skill', version: 'fixture' }) } as unknown as FxSkillManager;
    const runner = {
        generate: async () => ({ xml: wrap('<FXList id="NewEffect"/>'), outputDirectory: staged, xmlPath: path.join(staged, 'result.xml'), files: [{ path: payload, relativePath: 'assets/fixture.tga' }] }),
        validate: async () => report(), disposeResult: async () => undefined,
    } as unknown as SkillRunner;
    const controller = new FxAuthoringController(manager, () => runner);
    const { host } = hostFor(file, wrap('<FXList id="A"/>'));
    const before = editCalls;
    try {
        await controller.action(host, { action: 'generate', mode: 'new', description: 'fixture impact' });
        await assert.rejects(controller.action(host, { action: 'apply' }), /MOD|符号链接|真实路径|边界/);
        assert.equal(editCalls, before);
        assert.deepEqual(await fs.readdir(outside), []);
    } finally { controller.dispose(); }
});

test('FX checks automatically use the selected SDK schema and keep the manual override authoritative', async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-authoring-schema-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const sdk = path.join(root, 'sdk'), entry = path.join(sdk, 'Schemas', 'xsd', 'CnC3Types.xsd'), custom = path.join(root, 'custom.xsd');
    await fs.mkdir(path.dirname(entry), {recursive: true}); await fs.writeFile(entry, '<schema/>'); await fs.writeFile(custom, '<schema/>');
    const calls: SkillValidateOptions[] = [];
    const manager = {current: async () => ({rootPath: '/imported-skill', version: 'fixture'})} as unknown as FxSkillManager;
    const runner = {validate: async (_skill: string, options: SkillValidateOptions) => {calls.push(options); return report();}} as unknown as SkillRunner;
    const controller = new FxAuthoringController(manager, () => runner);
    const {host} = hostFor(path.join(root, 'effect.xml'), wrap('<FXList id="A"/>')); host.sdkRoot = sdk;
    try {
        await controller.action(host, {action: 'check'});
        assert.equal(calls[0].schemaPath, await fs.realpath(entry));
        schemaOverride = custom;
        await controller.action(host, {action: 'check'});
        assert.equal(calls[1].schemaPath, await fs.realpath(custom));
    } finally {schemaOverride = ''; controller.dispose();}
});

test('FX schema checking works without an imported Skill and does not claim field or resource coverage', async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-authoring-standalone-xsd-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const sdk = path.join(root, 'sdk'), entry = path.join(sdk, 'Schemas', 'xsd', 'CnC3Types.xsd');
    await fs.mkdir(path.dirname(entry), {recursive: true}); await fs.writeFile(entry, '<schema/>');
    let selected: string | undefined;
    const runner = {validateSchema: async (options: SkillValidateOptions) => {
        selected = options.schemaPath;
        const checked = report(); checked.schema = options.schemaPath;
        checked.levels = checked.levels.map(level => level.level === 'V0' ? {...level, status: 'UNKNOWN'} : level.level === 'V2' ? {...level, status: 'PASS'} : level);
        return checked;
    }} as unknown as SkillRunner;
    const controller = new FxAuthoringController(undefined, () => runner);
    const {host, views} = hostFor(path.join(root, 'effect.xml'), wrap('<FXList id="A"/>')); host.sdkRoot = sdk;
    try {
        await controller.action(host, {action: 'check'});
        assert.equal(selected, await fs.realpath(entry));
        const levels = views.at(-1)!.draft!.report.levels;
        assert.equal(levels.find(level => level.level === 'V2')?.status, 'PASS');
        assert.equal(levels.find(level => level.level === 'V0')?.status, 'UNKNOWN');
        assert.equal(levels.find(level => level.level === 'V3')?.status, 'UNKNOWN');
    } finally {controller.dispose();}
});

test('FX reports an unavailable configured SDK schema as UNKNOWN while preserving other executed checks', async t => {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-authoring-missing-xsd-'));
    t.after(() => fs.rm(root, {recursive: true, force: true}));
    const sdk = path.join(root, 'sdk'); await fs.mkdir(sdk);
    const manager = {current: async () => ({rootPath: '/imported-skill', version: 'fixture'})} as unknown as FxSkillManager;
    let schemaPath: string | undefined = 'not-called';
    const runner = {validate: async (_skill: string, options: SkillValidateOptions) => {schemaPath = options.schemaPath; return report();}} as unknown as SkillRunner;
    const controller = new FxAuthoringController(manager, () => runner);
    const {host, views} = hostFor(path.join(root, 'effect.xml'), wrap('<FXList id="A"/>')); host.sdkRoot = sdk;
    try {
        await controller.action(host, {action: 'check'});
        assert.equal(schemaPath, undefined, 'a nonexistent schema is not passed as a usable entry');
        const levels = views.at(-1)!.draft!.report.levels;
        assert.equal(levels.find(level => level.level === 'V2')?.status, 'UNKNOWN');
        assert.match(levels.find(level => level.level === 'V2')!.summary, /入口不存在/);
        assert.equal(levels.find(level => level.level === 'V0')?.status, 'PASS');
        assert.equal(levels.find(level => level.level === 'V1')?.status, 'PASS');
    } finally {controller.dispose();}
});
