import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { acquirePhysicalProjectLock, compilerArguments, diagnosticPaths, physicalProjectLockPath, ProjectRunLocks, requiresModVersion, RunConfiguration } from '../src/compilerArguments';

const configuration: RunConfiguration = {
    compilerPath: 'C:\\Compiler folder\\NewModCompiler.exe',
    sdkRoot: 'D:\\SDK & tools',
    modRoot: 'D:\\MODs\\中文 MOD; $(echo attack)',
    outputDirectory: 'E:\\Builds\\中文 MOD',
    modVersion: '1.0',
};

test('build and plan preserve hostile-looking paths as single argv values and explicitly supply BIG version', () => {
    for (const operation of ['plan', 'build'] as const) {
        const args = compilerArguments(operation, configuration);
        assert.equal(args[args.indexOf('--mod') + 1], configuration.modRoot);
        assert.equal(args[args.indexOf('--sdk-root') + 1], configuration.sdkRoot);
        assert.equal(args[args.indexOf('--mod-version') + 1], '1.0');
        assert.ok(!args.includes('--package-content'), 'compiler/project chooses content');
        assert.ok(!args.includes('--package-format'), 'compiler/project chooses package format');
        assert.ok(!args.some(value => value.startsWith('--include-additional')), 'compiler/project chooses Additional');
        assert.ok(!args.includes('--events -'));
        assert.ok(!args.includes('--jobs'));
        assert.ok(!args.includes('/ss'));
    }
});

test('preset-only paths and advanced options retain compiler ownership; only explicit overrides enter argv', () => {
    const presetOptions = { 'sdk-root': configuration.sdkRoot, out: configuration.outputDirectory,
        'package-format': 'loose', 'include-additional': 'false', jobs: '8', ss: 'false' };
    const args = compilerArguments('build', { ...configuration, presetOptions });
    for (const key of ['sdk-root', 'out', 'package-format', 'include-additional', 'jobs', 'ss']) {
        assert.ok(!args.some(value => value === `--${key}` || value.startsWith(`--${key}=`)), key);
    }
    assert.equal(args[args.indexOf('--mod-version') + 1], '1.0', 'BIG version remains explicit even when in a preset');
    const overridden = compilerArguments('plan', { ...configuration, presetOptions,
        compilerOptions: { 'package-content': 'apt,global-metadata', 'package-format': 'big', 'include-additional': false, ss: false, jobs: 2 } });
    assert.equal(overridden[overridden.indexOf('--package-content') + 1], 'apt,global-metadata');
    assert.ok(overridden.includes('--include-additional=false'));
    assert.ok(overridden.includes('--ss=false'));
    assert.ok(!overridden.includes('false'), 'standalone booleans are positional errors in the real CLI');
    assert.equal(overridden[overridden.indexOf('--jobs') + 1], '2');
    assert.throws(() => compilerArguments('build', { ...configuration, compilerOptions: { events: 'bad-path' } }), /不支持/);
    assert.throws(() => compilerArguments('build', { ...configuration, compilerOptions: { 'include-additional': '0' } }), /true.*false/);
});

test('doctor requires only SDK, and cannot accidentally compile a MOD', () => {
    assert.deepEqual(compilerArguments('doctor', { ...configuration, modRoot: '', outputDirectory: '', modVersion: '' }),
        ['doctor', '--sdk-root', configuration.sdkRoot]);
});

test('known loose plans can omit a version, while explicit BIG keeps the CLI requirement', () => {
    const loose = { ...configuration, modVersion: '', presetOptions: { 'package-format': 'loose' } };
    assert.equal(requiresModVersion(loose), false);
    assert.ok(!compilerArguments('plan', loose).includes('--mod-version'));
    assert.equal(requiresModVersion({ compilerOptions: { profile: 'development' } }), false);
    assert.ok(!compilerArguments('build', { ...configuration, modVersion: '', compilerOptions: { profile: 'development' } }).includes('--mod-version'));
    const big = { ...loose, compilerOptions: { 'package-format': 'big' } };
    assert.equal(requiresModVersion(big), true);
    assert.throws(() => compilerArguments('build', big), /MOD 版本/);
    assert.equal(requiresModVersion({ compilerOptions: { profile: 'development' }, presetOptions: { 'package-format': 'big' } }), true);
});

test('plan and build diagnostics never overwrite one another', () => {
    assert.deepEqual(diagnosticPaths('build', configuration.outputDirectory), {
        events: 'E:\\Builds\\中文 MOD\\build.events.jsonl',
        report: 'E:\\Builds\\中文 MOD\\build-report.json',
        log: 'E:\\Builds\\中文 MOD\\build.log',
    });
    const plan = diagnosticPaths('plan', configuration.outputDirectory);
    assert.ok(plan.report.endsWith('plan-report.json'));
    assert.ok(plan.events.endsWith('plan.events.jsonl'));
});

test('BIG calls reject missing version rather than relying on preset or compiler default', () => {
    assert.throws(() => compilerArguments('plan', { ...configuration, modVersion: ' ' }), /版本/);
    assert.throws(() => compilerArguments('build', { ...configuration, modVersion: '' }), /版本/);
});

test('physical project lock blocks overlapping actions, releases idempotently and permits retry', () => {
    const locks = new ProjectRunLocks();
    const release = locks.acquire('canonical-mod');
    assert.throws(() => locks.acquire('canonical-mod'), /正在运行/);
    const independent = locks.acquire('another-mod');
    independent();
    release();
    release();
    locks.acquire('canonical-mod')();
});

// Exercise the task lifecycle and real protocol reducer without starting VS Code.
// The Windows host suite separately verifies the actual VS Code task APIs.
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';

type Listener<T> = (value: T) => unknown;
class MockEmitter<T> {
    private listeners = new Set<Listener<T>>();
    event = (listener: Listener<T>) => {
        this.listeners.add(listener);
        return { dispose: () => this.listeners.delete(listener) };
    };
    fire(value: T): void { for (const listener of this.listeners) listener(value); }
    dispose(): void { this.listeners.clear(); }
}
class MockProcess {
    constructor(readonly process: string, readonly args: string[], readonly options: unknown) { }
}
class MockTask {
    presentationOptions?: unknown;
    isBackground?: boolean;
    constructor(readonly definition: Record<string, string>, readonly scope: unknown,
        readonly name: string, readonly source: string, readonly execution: MockProcess) { }
}
interface MockExecution { task: MockTask; terminate(): void }
const taskProcessStarted = new MockEmitter<{ execution: MockExecution; processId: number }>();
const taskProcessEnded = new MockEmitter<{ execution: MockExecution; exitCode?: number }>();
const taskEnded = new MockEmitter<{ execution: MockExecution }>();
const launchedTasks: MockTask[] = [];
interface MockUri { scheme: string; fsPath: string; }
interface MockDocument { uri: MockUri; version: number; isDirty: boolean; }
const documentChanged = new MockEmitter<{document: MockDocument; contentChanges: unknown[]}>();
const documentSaved = new MockEmitter<MockDocument>();
const diskChanged = new MockEmitter<MockUri>();
const liveWatchers = new Set<unknown>();
let taskBehavior: (execution: MockExecution) => Promise<void> = async () => undefined;
const mockVscode = {
    EventEmitter: MockEmitter,
    Task: MockTask,
    ProcessExecution: MockProcess,
    TaskScope: { Workspace: 1 },
    TaskRevealKind: { Always: 1 },
    TaskPanelKind: { Dedicated: 1 },
    env: {},
    RelativePattern: class { constructor(readonly base: string, readonly pattern: string) {} },
    workspace: { isTrusted: true, textDocuments: [] as MockDocument[],
        onDidChangeTextDocument: documentChanged.event,
        onDidSaveTextDocument: documentSaved.event,
        createFileSystemWatcher: () => {
            const watcher = { onDidChange: diskChanged.event, onDidCreate: diskChanged.event, onDidDelete: diskChanged.event,
                dispose: () => liveWatchers.delete(watcher) };
            liveWatchers.add(watcher); return watcher;
        },
    },
    tasks: {
        onDidStartTaskProcess: taskProcessStarted.event,
        onDidEndTaskProcess: taskProcessEnded.event,
        onDidEndTask: taskEnded.event,
        executeTask: async (task: MockTask): Promise<MockExecution> => {
            launchedTasks.push(task);
            const execution: MockExecution = { task, terminate: () => endExecution(execution, undefined) };
            queueMicrotask(() => {
                taskProcessStarted.fire({ execution, processId: process.pid });
                void taskBehavior(execution);
            });
            return execution;
        },
    },
};
function endExecution(execution: MockExecution, exitCode: number | undefined): void {
    taskProcessEnded.fire({ execution, exitCode });
    taskEnded.fire({ execution });
}
function loadRunnerWithMock(): typeof import('../src/compilerRunner') {
    const loader = require('node:module') as { _load: (request: string, parent: unknown, isMain: boolean) => unknown };
    const original = loader._load;
    loader._load = function (request, parent, isMain) {
        return request === 'vscode' ? mockVscode : original.call(this, request, parent, isMain);
    };
    try { return require('../src/compilerRunner') as typeof import('../src/compilerRunner'); }
    finally { loader._load = original; }
}
async function writeSuccessfulEvidence(task: MockTask): Promise<void> {
    const args = task.execution.args;
    const eventsFile = args[args.indexOf('--events') + 1];
    const reportFile = args[args.indexOf('--report') + 1];
    await fs.mkdir(path.dirname(eventsFile), { recursive: true });
    const id = task.definition.runId;
    const common = { schemaVersion: '1.0', buildId: id, level: 'info', timestampUtc: new Date().toISOString(), elapsedMs: 1 };
    await fs.writeFile(eventsFile, [
        { ...common, sequence: 1, eventType: 'BuildStarted' },
        { ...common, sequence: 2, eventType: 'BuildFinished', payload: { status: 'succeeded', exitCode: 0 } },
    ].map(event => JSON.stringify(event)).join('\n') + '\n');
    await fs.writeFile(reportFile, JSON.stringify({
        schemaVersion: '1.0', buildId: id, status: 'succeeded', exitCode: 0,
        artifacts: [], diagnostics: [], plan: { Tasks: [], Skipped: [] },
        publicationDirectory: path.join(path.dirname(reportFile), 'published'),
    }));
}

test('task lifecycle rejects doctor failure and stale evidence, and forced stop allows a clean retry', async () => {
    const actualPlatform = Object.getOwnPropertyDescriptor(process, 'platform')!;
    Object.defineProperty(process, 'platform', { value: 'win32', configurable: true });
    const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-runner-'));
    const { CompilerRunner } = loadRunnerWithMock();
    const runner = new CompilerRunner();
    const config: RunConfiguration = {
        compilerPath: path.join(temporary, 'compiler', 'NewModCompiler.exe'),
        sdkRoot: path.join(temporary, 'sdk'), modRoot: path.join(temporary, 'mod'),
        outputDirectory: path.join(temporary, 'output'), modVersion: '1.0',
    };
    try {
        for (const directory of [path.dirname(config.compilerPath), config.sdkRoot, config.modRoot]) {
            await fs.mkdir(directory);
        }
        await fs.writeFile(config.compilerPath, 'fixture');
        taskBehavior = async execution => endExecution(execution, 3);
        const doctorFailed = await runner.build(config);
        assert.equal(doctorFailed.status, 'failed');
        assert.match(doctorFailed.reason, /环境检查失败/);
        assert.deepEqual(launchedTasks.map(task => task.definition.operation), ['doctor']);
        assert.equal(runner.active, false);

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') await writeSuccessfulEvidence(execution.task);
            endExecution(execution, 0);
        };
        const current = await runner.build(config);
        assert.equal(current.status, 'succeeded');
        assert.equal(current.inputState, 'current');
        assert.equal(liveWatchers.size, 0, 'source listeners are released after completion');

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') {
                documentChanged.fire({ document: { uri: { scheme: 'file', fsPath: path.join(config.modRoot, 'Data', 'Mod.xml') },
                    version: 2, isDirty: true }, contentChanges: [{}] });
                await writeSuccessfulEvidence(execution.task);
            }
            endExecution(execution, 0);
        };
        const changedMemory = await runner.build(config);
        assert.equal(changedMemory.status, 'succeeded', 'source changes do not rewrite compiler success');
        assert.equal(changedMemory.inputState, 'previous-saved');
        assert.match(runner.state.message!, /上一保存版本/);

        const modAlias = path.join(temporary, 'mod-alias');
        await fs.symlink(config.modRoot, modAlias, actualPlatform.value === 'win32' ? 'junction' : 'dir');
        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') {
                documentChanged.fire({ document: { uri: { scheme: 'file', fsPath: path.join(modAlias, 'Data', 'Mod.xml') },
                    version: 3, isDirty: true }, contentChanges: [{}] });
                await writeSuccessfulEvidence(execution.task);
            }
            endExecution(execution, 0);
        };
        assert.equal((await runner.build(config)).inputState, 'previous-saved', 'TextDocument aliases must resolve to the physical compiler input');

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') {
                diskChanged.fire({ scheme: 'file', fsPath: path.join(modAlias, 'Data', 'New.xml') });
                await writeSuccessfulEvidence(execution.task);
            }
            endExecution(execution, 0);
        };
        assert.equal((await runner.build(config)).inputState, 'inputs-changed', 'Create/delete paths under a junction must retain the physical input root');

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') {
                diskChanged.fire({ scheme: 'file', fsPath: path.join(config.sdkRoot, 'SuperCompilerCache', 'mod', 'session.xml') });
                await writeSuccessfulEvidence(execution.task);
            }
            endExecution(execution, 0);
        };
        assert.equal((await runner.build(config)).inputState, 'current', 'compiler cache writes are not source changes');

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') {
                diskChanged.fire({ scheme: 'file', fsPath: path.join(config.modRoot, 'Data', 'Mod.xml') });
                await writeSuccessfulEvidence(execution.task);
            }
            endExecution(execution, 0);
        };
        assert.equal((await runner.build(config)).inputState, 'inputs-changed');

        // Startup exits before replacing diagnostics: the old report still says
        // succeeded, but must never certify this new invocation.
        taskBehavior = async execution => endExecution(execution,
            execution.task.definition.operation === 'doctor' ? 0 : 3);
        const stale = await runner.build(config);
        assert.notEqual(stale.status, 'succeeded');
        assert.deepEqual(stale.artifacts, []);

        let buildHasStarted!: () => void;
        const started = new Promise<void>(resolve => { buildHasStarted = resolve; });
        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'doctor') endExecution(execution, 0);
            else buildHasStarted();
        };
        const pending = runner.build(config);
        await started;
        runner.stop();
        const interrupted = await pending;
        assert.equal(interrupted.status, 'interrupted');
        assert.equal(runner.active, false);
        assert.deepEqual(interrupted.artifacts, []);

        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'build') await writeSuccessfulEvidence(execution.task);
            endExecution(execution, 0);
        };
        const retry = await runner.build(config);
        assert.equal(retry.status, 'succeeded');
        assert.equal(runner.active, false);
    } finally {
        runner.dispose();
        Object.defineProperty(process, 'platform', actualPlatform);
        await fs.rm(temporary, { recursive: true, force: true });
    }
});

test('doctor is reused only after success for the same observed toolchain; manual checks always execute', async () => {
    const actualPlatform = Object.getOwnPropertyDescriptor(process, 'platform')!;
    Object.defineProperty(process, 'platform', { value: 'win32', configurable: true });
    const temporary = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-doctor-'));
    const { CompilerRunner } = loadRunnerWithMock();
    const runner = new CompilerRunner();
    const config: RunConfiguration = { compilerPath: path.join(temporary, 'compiler', 'NewModCompiler.exe'),
        sdkRoot: path.join(temporary, 'sdk'), modRoot: path.join(temporary, 'mod'),
        outputDirectory: path.join(temporary, 'output'), modVersion: '1.0' };
    const sdkConfig = path.join(config.sdkRoot, 'tools', 'BinaryAssetBuilder.exe.config');
    const compilerConfig = `${config.compilerPath}.config`;
    const sidecar = path.join(path.dirname(config.compilerPath), 'antlr.runtime.dll');
    const succeed = async (execution: MockExecution): Promise<void> => {
        if (execution.task.definition.operation !== 'doctor') await writeSuccessfulEvidence(execution.task);
        endExecution(execution, 0);
    };
    async function operations(action: () => Promise<unknown>): Promise<string[]> {
        const first = launchedTasks.length;
        await action();
        return launchedTasks.slice(first).map(task => task.definition.operation);
    }
    try {
        await Promise.all([fs.mkdir(path.dirname(config.compilerPath), { recursive: true }),
            fs.mkdir(path.dirname(sdkConfig), { recursive: true }), fs.mkdir(config.modRoot)]);
        await Promise.all([fs.writeFile(config.compilerPath, 'fixture'), fs.writeFile(compilerConfig, 'configuration'),
            fs.writeFile(sidecar, 'runtime'), fs.writeFile(sdkConfig, 'sdk-configuration')]);
        taskBehavior = succeed;
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build']);
        assert.deepEqual(await operations(() => runner.build(config)), ['build']);
        assert.deepEqual(await operations(() => runner.plan(config)), ['plan']);
        assert.deepEqual(await operations(() => runner.doctor(config)), ['doctor']);
        assert.deepEqual(await operations(() => runner.doctor(config)), ['doctor'], 'Manual doctor never reuses a previous result');
        assert.deepEqual(await operations(() => runner.build(config)), ['build']);

        for (const file of [config.compilerPath, compilerConfig, sdkConfig, sidecar]) {
            await fs.appendFile(file, '-changed');
            assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], `Changed toolchain file: ${file}`);
            assert.deepEqual(await operations(() => runner.build(config)), ['build']);
        }
        const addedSidecar = path.join(path.dirname(config.compilerPath), 'NewProvider.dll');
        await fs.writeFile(addedSidecar, 'new-provider');
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'New side libraries invalidate the environment check');
        await fs.unlink(addedSidecar);
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'Removing a side library also invalidates it');

        const secondSdk = path.join(temporary, 'second-sdk');
        await fs.mkdir(path.join(secondSdk, 'tools'), { recursive: true });
        await fs.writeFile(path.join(secondSdk, 'tools', 'BinaryAssetBuilder.exe.config'), 'configuration');
        const second = { ...config, sdkRoot: secondSdk };
        assert.deepEqual(await operations(() => runner.build(second)), ['doctor', 'build'], 'Changing the SDK root requires a new check');
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build']);
        await fs.writeFile(path.join(config.modRoot, 'Mod.xml'), '<AssetDeclaration/>');
        assert.deepEqual(await operations(() => runner.build(config)), ['build'], 'MOD source changes do not invalidate toolchain validation');

        taskBehavior = async execution => endExecution(execution, 3);
        assert.equal(await runner.doctor(config), false);
        taskBehavior = succeed;
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'A failed manual check must not preserve old success');
        await fs.appendFile(sdkConfig, '-new-environment');
        taskBehavior = async execution => endExecution(execution, 3);
        assert.deepEqual(await operations(async () => assert.equal((await runner.build(config)).status, 'failed')), ['doctor']);
        taskBehavior = succeed;
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'A failed preflight must retry doctor');

        await fs.appendFile(compilerConfig, '-before-check');
        let changedDuringDoctor = false;
        taskBehavior = async execution => {
            if (execution.task.definition.operation === 'doctor' && !changedDuringDoctor) {
                changedDuringDoctor = true;
                await fs.appendFile(compilerConfig, '-during-check');
            }
            await succeed(execution);
        };
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build']);
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'A check of a changing toolchain is never cached');
        assert.deepEqual(await operations(() => runner.build(config)), ['build']);

        await fs.appendFile(sidecar, '-interrupt-check');
        let doctorStarted!: () => void;
        const started = new Promise<void>(resolve => { doctorStarted = resolve; });
        taskBehavior = async execution => { assert.equal(execution.task.definition.operation, 'doctor'); doctorStarted(); };
        const interrupted = runner.build(config);
        await started; runner.stop();
        assert.equal((await interrupted).status, 'interrupted');
        taskBehavior = succeed;
        assert.deepEqual(await operations(() => runner.build(config)), ['doctor', 'build'], 'An interrupted check cannot authorize a skip');
        taskBehavior = async execution => { assert.equal(execution.task.definition.operation, 'build'); endExecution(execution, 3); };
        assert.equal((await runner.build(config)).status, 'failed');
        taskBehavior = succeed;
        assert.deepEqual(await operations(() => runner.build(config)), ['build'], 'Source compilation failure does not erase a successful environment check');
    } finally {
        runner.dispose();
        Object.defineProperty(process, 'platform', actualPlatform);
        await fs.rm(temporary, { recursive: true, force: true });
    }
});

import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

test('OS project lock rejects a second host, permits release/retry and reclaims only known dead owners', async () => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-project-lock-'));
    const identity = 'physical-project-identity';
    try {
        const first = await acquirePhysicalProjectLock(identity, { lockDirectory: directory });
        await assert.rejects(acquirePhysicalProjectLock(identity, { lockDirectory: directory }), /另一个 VS Code 窗口/);
        const execute = promisify(execFile);
        // A different Node process sees the same lock, proving this is not just
        // an in-memory mutex in the original VS Code extension host.
        await assert.rejects(execute(process.execPath, ['-e',
            'require(process.argv[1]).acquirePhysicalProjectLock(process.argv[3], {lockDirectory:process.argv[2]}).then(async lock=>{await lock.release();process.exit(0)},error=>{process.stderr.write(error.message);process.exit(9)})',
            require.resolve('../src/compilerArguments'), directory, identity]),
        (error: unknown) => error instanceof Error && 'stderr' in error &&
            String(error.stderr).includes('另一个 VS Code 窗口'));
        await first.release();
        await first.release();
        const retry = await acquirePhysicalProjectLock(identity, { lockDirectory: directory });
        await retry.release();

        const lockPath = physicalProjectLockPath(identity, directory);
        const oldOwner = { version: 1, pid: 2147483647,
            token: '00000000-0000-0000-0000-000000000000', identity };
        await fs.writeFile(lockPath, JSON.stringify(oldOwner));
        await assert.rejects(acquirePhysicalProjectLock(identity, {
            lockDirectory: directory, probeProcess: () => 'unknown',
        }), /无法确认进程/);
        assert.deepEqual(JSON.parse(await fs.readFile(lockPath, 'utf8')), oldOwner);
        await fs.writeFile(lockPath, JSON.stringify({ ...oldOwner, processId: process.pid }));
        await assert.rejects(acquirePhysicalProjectLock(identity, {
            lockDirectory: directory, probeProcess: pid => pid === process.pid ? 'alive' : 'dead',
        }), /编译器进程/);
        await fs.writeFile(lockPath, JSON.stringify(oldOwner));
        const reclaimed = await acquirePhysicalProjectLock(identity, {
            lockDirectory: directory, probeProcess: () => 'dead',
        });
        const current = JSON.parse(await fs.readFile(lockPath, 'utf8')) as { token: string; pid: number };
        assert.equal(current.pid, process.pid);
        assert.notEqual(current.token, oldOwner.token);
        await reclaimed.release();
    } finally { await fs.rm(directory, { recursive: true, force: true }); }
});

test('OS project lock preserves corrupt/foreign ownership and never releases another token', async () => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-project-lock-'));
    const identity = 'physical-project-identity';
    const lockPath = physicalProjectLockPath(identity, directory);
    try {
        await fs.writeFile(lockPath, '{unfinished');
        await assert.rejects(acquirePhysicalProjectLock(identity, { lockDirectory: directory }), /无法确认项目锁/);
        assert.equal(await fs.readFile(lockPath, 'utf8'), '{unfinished');
        await fs.unlink(lockPath);
        const lock = await acquirePhysicalProjectLock(identity, { lockDirectory: directory });
        const original = JSON.parse(await fs.readFile(lockPath, 'utf8')) as Record<string, unknown>;
        await fs.writeFile(lockPath, JSON.stringify({ ...original,
            token: 'ffffffff-ffff-ffff-ffff-ffffffffffff' }));
        await assert.rejects(lock.release(), /已由其他任务持有/);
        assert.equal((JSON.parse(await fs.readFile(lockPath, 'utf8')) as { token: string }).token,
            'ffffffff-ffff-ffff-ffff-ffffffffffff');
        await fs.unlink(lockPath);
        await lock.release();
    } finally { await fs.rm(directory, { recursive: true, force: true }); }
});

test('concurrent OS lock acquisitions have one owner and leave no gate behind', async () => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-project-lock-'));
    try {
        const acquired = await Promise.allSettled(Array.from({ length: 4 }, () =>
            acquirePhysicalProjectLock('same-physical-mod', { lockDirectory: directory })));
        const owners = acquired.filter(result => result.status === 'fulfilled');
        assert.equal(owners.length, 1);
        for (const owner of owners) if (owner.status === 'fulfilled') await owner.value.release();
        assert.deepEqual(await fs.readdir(directory), []);
    } finally { await fs.rm(directory, { recursive: true, force: true }); }
});
