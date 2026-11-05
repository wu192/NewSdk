import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import * as vscode from 'vscode';
import { installCompiler, validateCompilerExecutable } from '../../src/compilerInstaller';
import type { BuildEvent, BuildResult } from '../../src/buildProtocol';
import { runNormalCancellation } from '../realCancellation';
import { runFxAcceptance } from './fxAcceptance';
import { runXmlAcceptance } from './xmlAcceptance';
import type { FxApi } from '../../src/fx/editorProvider';

type Result = BuildResult;
interface State {
    status: string;
    operation?: string;
    config?: { modRoot: string };
    progress?: { buildId?: string; events?: BuildEvent[] };
    result?: Result;
}
interface ExtensionApi {
    getState(): State;
    getProject(): unknown;
    getConfiguration(): { compilerPath?: string; sdkRoot?: string; outputDirectory?: string; modVersion?: string; root?: string } | undefined;
    selectProject(root: string): Promise<void>;
    configure(values: { compilerPath: string; sdkRoot: string; outputDirectory: string; modVersion: string }): Promise<void>;
    build(): Promise<Result | undefined>;
    plan(): Promise<Result | undefined>;
    stop(): void;
    showWorkbench(): void;
    fx: FxApi;
    saveAndBuild(uris?: vscode.Uri[]): Promise<Result>;
}

const execute = promisify(execFile);
const xml = '<?xml version="1.0" encoding="utf-8"?><AssetDeclaration xmlns="uri:ea.com:eala:asset"><ArmorTemplate id="NewSdkP0Probe" Default="100" /></AssetDeclaration>';
let activeApi: ExtensionApi | undefined;
let activeCheckpoint = 'startup';

function diagnosticSnapshot(): unknown {
    const state = activeApi?.getState();
    return {
        phase: process.env.NEWSDK_HOST_PHASE, checkpoint: activeCheckpoint,
        state: state ? { status: state.status, operation: state.operation, config: state.config,
            buildId: state.progress?.buildId, resultStatus: state.result?.status } : undefined,
        project: activeApi?.getProject(), configuration: activeApi?.getConfiguration(),
        globalCompiler: vscode.workspace.getConfiguration('newsdk').get<string>('compilerPath'),
        globalSdk: vscode.workspace.getConfiguration('newsdk').get<string>('sdkRoot'),
        tasks: vscode.tasks.taskExecutions.map(execution => ({ name: execution.task.name, definition: execution.task.definition })),
    };
}

function checkpoint(name: string): void {
    activeCheckpoint = name;
    console.log(`[newsdk-host] ${name} ${JSON.stringify(diagnosticSnapshot())}`);
}

async function inventory(root: string, excludedRootDirectories: string[] = []): Promise<Record<string, string>> {
    const result: Record<string, string> = {};
    async function visit(directory: string): Promise<void> {
        for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
            if (directory === root && excludedRootDirectories.includes(entry.name)) continue;
            const file = path.join(directory, entry.name);
            if (entry.isDirectory()) await visit(file);
            else if (entry.isFile()) result[path.relative(root, file)] = createHash('sha256').update(await fs.readFile(file)).digest('hex');
        }
    }
    await visit(root);
    return result;
}

async function waitFor(predicate: () => boolean, timeout = 15000): Promise<void> {
    const start = Date.now();
    while (!predicate()) {
        if (Date.now() - start > timeout) throw new Error('等待 VS Code 编译状态超时。');
        await new Promise(resolve => setTimeout(resolve, 100));
    }
}

async function timed<T>(promise: Promise<T>, milliseconds = 90000): Promise<T> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
        return await Promise.race([promise, new Promise<T>((_, reject) => { timer = setTimeout(() => {
            const snapshot = JSON.stringify(diagnosticSnapshot());
            console.error(`[newsdk-host] timeout ${snapshot}`);
            reject(new Error(`编译任务验收超时（${activeCheckpoint}）：${snapshot}`));
        }, milliseconds); })]);
    } finally { if (timer) clearTimeout(timer); }
}

export async function run(): Promise<void> {
    const root = process.env.NEWSDK_TEST_ROOT!;
    const evidence = process.env.NEWSDK_TEST_EVIDENCE!;
    try {
        await runSuite();
    } finally {
        // Preserve partial protocol files even if an assertion or a task fails.
        for (const directory of ['outputs', 'real-outputs']) {
            try {
                await fs.cp(path.join(root, directory), path.join(evidence, directory), { recursive: true });
            } catch (error) {
                if ((error as NodeJS.ErrnoException).code !== 'ENOENT') console.error(`Cannot retain ${directory} evidence`, error);
            }
        }
    }
}

async function runSuite(): Promise<void> {
    assert.equal(process.platform, 'win32');
    const root = process.env.NEWSDK_TEST_ROOT!;
    const evidence = process.env.NEWSDK_TEST_EVIDENCE!;
    const extension = vscode.extensions.getExtension<ExtensionApi>('wu162.newsdk-mod-tools');
    assert.ok(extension, 'VSIX development extension must be discoverable');
    const api = await extension.activate();
    activeApi = api;
    checkpoint('activated');
    const commandIds = await vscode.commands.getCommands(true);
    for (const command of ['newsdk.build', 'newsdk.stop', 'newsdk.openWorkbench', 'newsdk.plan', 'newsdk.doctor']) {
        assert.ok(commandIds.includes(command), `Missing command ${command}`);
    }
    api.showWorkbench();
    if (process.env.NEWSDK_HOST_PHASE === 'reopen') {
        // No configuration calls here: the previous process must have persisted it.
        checkpoint('reopen-before-build');
        const persisted = api.getConfiguration();
        assert.ok(persisted?.compilerPath, 'Compiler path must persist before reopening build');
        assert.ok(persisted.sdkRoot, 'SDK path must persist before reopening build');
        assert.ok(persisted.outputDirectory, 'Project output path must persist before reopening build');
        assert.equal(persisted.modVersion, '1.0', 'Project MOD version must persist before reopening build');
        const result = await timed(api.build());
        assert.equal((result ?? api.getState().result)?.status, 'succeeded');
        checkpoint('reopen-build-passed');
        await fs.writeFile(path.join(evidence, 'persistence.json'), JSON.stringify({ succeeded: true, scope: 'Second VS Code process using the same isolated profile' }, null, 2));
        return;
    }
    const compilerDirectory = path.join(root, 'fixture-compiler');
    await fs.mkdir(compilerDirectory, { recursive: true });
    const executable = path.join(compilerDirectory, 'NewModCompiler.exe');
    const csc = path.join(process.env.WINDIR ?? 'C:\\Windows', 'Microsoft.NET', 'Framework', 'v4.0.30319', 'csc.exe');
    await execute(csc, ['/nologo', '/target:exe', '/reference:System.Web.Extensions.dll', `/out:${executable}`,
        path.resolve(__dirname, '../../../test/host/compilerFixture.cs')], { timeout: 30000 });
    await fs.writeFile(`${executable}.config`, '<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" /></startup></configuration>');
    await fs.writeFile(path.join(compilerDirectory, 'antlr.runtime.dll'), 'Unused protocol fixture sidecar');
    const sdk = path.join(root, 'fixture-sdk');
    await fs.mkdir(sdk, { recursive: true });
    const cases: Array<{ name: string; status: string }> = [];
    async function select(name: string, compilerPath = executable, sdkRoot = sdk): Promise<string> {
        const mod = path.join(root, name);
        await fs.mkdir(path.join(mod, 'Data'), { recursive: true });
        await fs.writeFile(path.join(mod, 'Data', 'Mod.xml'), xml);
        await api.selectProject(mod);
        await api.configure({ compilerPath, sdkRoot, outputDirectory: path.join(root, 'outputs', name), modVersion: '1.0' });
        return mod;
    }
    const basic = await select('MockBasic');
    const source = await inventory(basic);
    checkpoint('basic-plan');
    let result = await timed(api.plan());
    assert.equal((result ?? api.getState().result)?.status, 'succeeded');
    checkpoint('basic-build');
    result = await timed(api.build());
    assert.equal((result ?? api.getState().result)?.status, 'succeeded');
    assert.ok((result ?? api.getState().result)?.artifacts?.length);
    assert.deepEqual(await inventory(basic), source, 'Extension and task must leave MOD source bytes unchanged');
    cases.push({ name: 'plan-build-artifacts-source-readonly', status: 'passed' });
    const fxMod = await select('MockFx');
    checkpoint('fx-editor-acceptance');
    await runFxAcceptance(api.fx, fxMod, evidence, uris => api.saveAndBuild(uris));
    await select('MockFailure');
    checkpoint('compiler-failure');
    result = await timed(api.build());
    assert.equal((result ?? api.getState().result)?.status, 'failed');
    assert.ok((result ?? api.getState().result)?.diagnostics?.some(item => item.code === 'FIXTURE_ERROR'));
    assert.equal(vscode.languages.getDiagnostics().filter(([uri]) => uri.fsPath.startsWith(root)).length, 0,
        'Unlocated compiler diagnostics must not create invented Problems file locations');
    cases.push({ name: 'compiler-error-no-fake-location', status: 'passed' });
    await select('MockMissing');
    checkpoint('missing-report');
    result = await timed(api.build());
    assert.notEqual((result ?? api.getState().result)?.status, 'succeeded');
    cases.push({ name: 'missing-report-never-success', status: 'passed' });
    const slowMod = await select('MockSlow');
    const canonicalSlowMod = await fs.realpath(slowMod);
    const previousBuildId = api.getState().progress?.buildId;
    checkpoint('slow-build-start');
    const stopped = timed(api.build());
    await waitFor(() => {
        const state = api.getState();
        return state.status === 'running' && state.operation === 'build'
            && Boolean(state.progress?.buildId) && state.progress?.buildId !== previousBuildId
            && state.config?.modRoot.toLowerCase() === canonicalSlowMod.toLowerCase();
    });
    const stoppingState = api.getState();
    assert.equal(stoppingState.status, 'running', 'Stop must target the currently running build');
    assert.ok(stoppingState.progress?.buildId, 'Current build must have started its own event stream');
    assert.notEqual(stoppingState.progress.buildId, previousBuildId, 'Previous build state must not satisfy the stop precondition');
    checkpoint('slow-build-stop');
    api.stop();
    result = await stopped;
    assert.equal((result ?? api.getState().result)?.status, 'interrupted');
    cases.push({ name: 'stop-button-forced-interruption', status: 'passed' });
    await select('MockBasic');
    checkpoint('retry-after-stop');
    result = await timed(api.build());
    assert.equal((result ?? api.getState().result)?.status, 'succeeded');
    cases.push({ name: 'retry-after-stop', status: 'passed' });
    await fs.writeFile(path.join(evidence, 'extension-host.json'), JSON.stringify({ succeeded: true, vscodeVersion: vscode.version,
        scope: 'Real VS Code extension host and Windows ProcessExecution with a synthetic protocol fixture; not real compiler acceptance', cases }, null, 2));

    if (process.env.NEWSDK_COMPILER_PATH || process.env.NEWSDK_COMPILER_URL) {
        let executablePath: string;
        let source: Record<string, unknown>;
        if (process.env.NEWSDK_COMPILER_PATH) {
            executablePath = (await validateCompilerExecutable(process.env.NEWSDK_COMPILER_PATH)).executablePath;
            source = {mode:'fixed-source-build', repository:'wu192/NewSdk', ref:process.env.NEWSDK_COMPILER_SOURCE_REF};
        } else {
            const installed = await installCompiler({ url: process.env.NEWSDK_COMPILER_URL!,
                expectedSha256: process.env.NEWSDK_COMPILER_SHA256 || undefined,
                headers: process.env.NEWSDK_COMPILER_AUTHORIZATION ? { Authorization: process.env.NEWSDK_COMPILER_AUTHORIZATION } : undefined,
                storageDirectory: path.join(root, 'provided-compiler') });
            executablePath = installed.executablePath;
            const release = JSON.parse(await fs.readFile(path.resolve(__dirname, '../../../compiler-release.json'), 'utf8')) as { sha256: string };
            source = {...installed, mode:'provided-release', release: installed.sha256.toLowerCase() === release.sha256.toLowerCase() ? release : undefined};
        }
        await fs.writeFile(path.join(evidence, 'compiler-source.json'), JSON.stringify({...source,
            executableSha256: createHash('sha256').update(await fs.readFile(executablePath)).digest('hex')}, null, 2));
        await realCompilerCases(api, root, evidence, executablePath);
    } else {
        await fs.writeFile(path.join(evidence, 'real-compiler.json'), JSON.stringify({ status: 'skipped', reason: '没有提供编译器路径或下载地址，未运行真实编译器。' }, null, 2));
        if (process.env.GITHUB_ACTIONS === 'true') throw new Error('Actions 必须执行实际编译器验收。');
    }
    // Leave the synthetic working project selected for the reopen persistence check.
    await select('MockBasic');
    checkpoint('exercise-persisted-final-configuration');
}

/** Read the real uncompressed MakeBig archive to inspect current stream payloads. */
async function bigMembers(file: string): Promise<Map<string, Buffer>> {
    const bytes = await fs.readFile(file);
    assert.ok(bytes.length >= 24);
    assert.equal(bytes.toString('ascii', 0, 4), 'BIG4');
    assert.equal(bytes.readUInt32LE(4), bytes.length);
    const count = bytes.readUInt32BE(8), header = bytes.readUInt32BE(12);
    assert.ok(count > 0 && count <= 1000000 && header >= 24 && header <= bytes.length);
    const members = new Map<string, Buffer>();
    let position = 16, expectedOffset = header;
    for (let index = 0; index < count; index++) {
        assert.ok(position + 9 <= header - 8);
        const offset = bytes.readUInt32BE(position), size = bytes.readUInt32BE(position + 4);
        position += 8;
        const end = bytes.indexOf(0, position);
        assert.ok(end >= position && end < header - 8);
        const name = bytes.toString('ascii', position, end);
        assert.ok(name && name === name.toLowerCase() && !members.has(name));
        assert.equal(offset, expectedOffset);
        assert.ok(offset + size <= bytes.length);
        members.set(name, bytes.subarray(offset, offset + size));
        expectedOffset += size;
        position = end + 1;
    }
    assert.deepEqual(bytes.subarray(position, header), Buffer.from([76, 50, 49, 57, 0, 0, 0, 0]));
    assert.equal(expectedOffset, bytes.length);
    return members;
}

function manifestAssets(members: Map<string, Buffer>, member: string): string[] {
    const bytes = members.get(member);
    assert.ok(bytes, `Missing actual linked stream ${member}`);
    assert.ok(bytes.length >= 48);
    assert.deepEqual(bytes.subarray(0, 4), Buffer.from([0, 1, 6, 0]));
    const count = bytes.readUInt32LE(12), nameBytes = bytes.readUInt32LE(40);
    const names = 48 + 48 * count + bytes.readUInt32LE(32) + bytes.readUInt32LE(36);
    assert.equal(names + nameBytes + bytes.readUInt32LE(44), bytes.length);
    const assets: string[] = [];
    for (let index = 0; index < count; index++) {
        const offset = bytes.readUInt32LE(48 + 48 * index + 24);
        assert.ok(offset < nameBytes);
        const end = bytes.indexOf(0, names + offset);
        assert.ok(end >= names + offset && end < names + nameBytes);
        assets.push(bytes.toString('ascii', names + offset, end));
    }
    return assets;
}

async function compilerCacheRoot(sdk: string, mod: string): Promise<string> {
    const physical = await fs.realpath(mod);
    const normalized = physical.replace(/[\\/]+$/, '').toUpperCase() + path.sep;
    const identity = createHash('sha256').update(normalized, 'utf8').digest('hex').slice(0, 32);
    const label = path.basename(physical).toLowerCase().replace(/[^a-z0-9_-]/g, '_').slice(0, 12);
    return path.join(sdk, 'SuperCompilerCache', `${label}_${identity}`);
}

async function realCompilerCases(api: ExtensionApi, root: string, evidence: string, compilerPath: string): Promise<void> {
    const repository = process.env.NEWSDK_REPOSITORY!;
    const sourceSdk = path.join(repository, 'SDK');
    const sdkRoot = path.join(root, 'real-sdk');
    // Use private copies of the exact SDK configuration/schema, as the compiler's
    // own UI acceptance does. No tracked SDK inputs or historical binaries change.
    await fs.mkdir(path.join(sdkRoot, 'tools'), { recursive: true });
    await fs.mkdir(path.join(sdkRoot, 'Schemas'), { recursive: true });
    await fs.cp(path.join(sourceSdk, 'Schemas', 'xsd'), path.join(sdkRoot, 'Schemas', 'xsd'), { recursive: true });
    await fs.copyFile(path.join(sourceSdk, 'tools', 'BinaryAssetBuilder.exe.config'), path.join(sdkRoot, 'tools', 'BinaryAssetBuilder.exe.config'));
    await fs.mkdir(path.join(sdkRoot, 'Additional'), { recursive: true });
    await fs.writeFile(path.join(sdkRoot, 'Additional', 'shared-p0.txt'), 'SDK overlay');
    await fs.writeFile(path.join(sdkRoot, 'Additional', 'sdk-only-p0.txt'), 'SDK only');
    const sdkInputs = await inventory(sdkRoot);
    await fs.writeFile(path.join(evidence, 'sdk-inputs.json'), JSON.stringify({ checkoutCommit: process.env.GITHUB_SHA,
        sourceSdk, temporarySdk: sdkRoot, inputs: sdkInputs }, null, 2));
    const full = process.env.NEWSDK_ACCEPTANCE === 'full';
    // This gate is enabled only for a verified compiler build/release with the
    // optional source-diagnostic capability. Custom older releases stay valid.
    const expectSourceDiagnostics = process.env.NEWSDK_EXPECT_SOURCE_DIAGNOSTICS === '1';
    const results: Array<Record<string, unknown>> = [];
    const scope = full
        ? 'Provided Win32/net48 release compiler through real VS Code ProcessExecution; real base, file-backed SDK APT business assets, MapMetaData, BIG/SKUDEF, Additional, errors, cancellation/retry, source and SDK input preservation. No in-game playback or full Upheaval performance claim.'
        : 'Provided Win32/net48 release compiler through real VS Code ProcessExecution; real base stream, BIG/SKUDEF, source and SDK input preservation, SDK-owned cache retention. Full content/error/cancellation matrix is separate.';
    const summaryPath = path.join(evidence, 'real-compiler.json');
    async function summary(status: string, error?: unknown): Promise<void> {
        await fs.writeFile(summaryPath, JSON.stringify({ status, scope, mode: full ? 'full' : 'smoke', expectSourceDiagnostics, cases: results,
            error: error instanceof Error ? error.message : error,
            pending: full ? [] : ['Full content/error/cancellation matrix requires full_acceptance dispatch'] }, null, 2));
    }
    async function preserve(output: string, label: string, result?: Result): Promise<void> {
        const target = path.join(evidence, 'real-cases', label);
        await fs.mkdir(target, { recursive: true });
        for (const file of ['build.events.jsonl', 'build-report.json', 'build.log', 'plan.events.jsonl', 'plan-report.json', 'plan.log']) {
            try { await fs.copyFile(path.join(output, file), path.join(target, file)); }
            catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
        }
        if (result) await fs.writeFile(path.join(target, 'extension-result.json'), JSON.stringify({ status: result.status,
            buildId: result.buildId, exitCode: result.exitCode, reason: result.reason, artifacts: result.artifacts,
            diagnostics: result.diagnostics }, null, 2));
    }
    async function selected(name: string): Promise<{ mod: string; output: string }> {
        const mod = path.join(root, name), output = path.join(root, 'real-outputs', name);
        await fs.mkdir(mod, { recursive: true });
        await api.selectProject(mod);
        await api.configure({ compilerPath, sdkRoot, outputDirectory: output, modVersion: '1.0' });
        return { mod, output };
    }
    async function write(mod: string, relative: string, contents: string): Promise<void> {
        const file = path.join(mod, relative);
        await fs.mkdir(path.dirname(file), { recursive: true });
        await fs.writeFile(file, contents);
    }
    async function fixture(name: string, content: string[]): Promise<{ mod: string; output: string }> {
        const mod = path.join(root, name);
        await fs.mkdir(mod, { recursive: true });
        if (content.includes('base')) await write(mod, 'Data/Mod.xml', xml);
        if (content.includes('apt')) {
            const apt = path.join(sourceSdk, 'Mods', 'Upheaval', 'Data', 'AptUI');
            await fs.mkdir(path.join(mod, 'Data', 'AptUI'), { recursive: true });
            await fs.copyFile(path.join(apt, 'TacticalHUDCommandListBox.xml'), path.join(mod, 'Data', 'AptUI', 'TacticalHUDCommandListBox.xml'));
            await fs.cp(path.join(apt, 'TacticalHUDCommandListBox'), path.join(mod, 'Data', 'AptUI', 'TacticalHUDCommandListBox'), { recursive: true });
        }
        if (content.includes('maps')) {
            await fs.mkdir(path.join(mod, 'Data', 'AdditionalMaps'), { recursive: true });
            await fs.copyFile(path.join(sourceSdk, 'SageXml', 'dlcontent', 'packages', 'package1', 'MapMetaData.xml'),
                path.join(mod, 'Data', 'AdditionalMaps', 'MapMetaData_P0.xml'));
        }
        if (content.includes('additional')) {
            await write(mod, 'Additional/shared-p0.txt', 'MOD overlay');
            await write(mod, 'Additional/mod-only-p0.txt', 'MOD only');
            await write(mod, 'Additional/excluded.asset', 'Must never be packaged');
        }
        return selected(name);
    }
    function final(value?: Result): Result {
        const result = value ?? api.getState().result;
        assert.ok(result);
        return result;
    }
    function record(value: unknown): Record<string, unknown> | undefined {
        return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : undefined;
    }
    async function locatedFailure(result: Result, file: string, expectedLine?: number): Promise<Record<string, unknown> | undefined> {
        if (!expectSourceDiagnostics) return undefined;
        assert.ok(result.report, 'Source diagnostics require the current build report');
        const physicalFile = (await fs.realpath(file)).toLowerCase();
        const sameFile = (value: unknown): boolean => typeof value === 'string' && path.resolve(value).toLowerCase() === physicalFile;
        const event = result.events.find(item => item.eventType === 'Diagnostic' && item.level === 'error'
            && sameFile(item.payload?.file) && Number.isSafeInteger(item.payload?.line) && Number(item.payload?.line) > 0);
        assert.ok(event, `Expected a real source-located Diagnostic event for ${file}`);
        const payload = event.payload!;
        const source = record(payload.source);
        assert.ok(source, 'New diagnostic events must carry structured source metadata');
        assert.ok(sameFile(source.file));
        assert.equal(source.line, payload.line, 'source.line and legacy line must retain the same 1-based meaning');
        assert.equal(source.column, payload.column);
        assert.ok(Number.isSafeInteger(payload.column) && Number(payload.column) > 0, 'The real XML reader provides a positive column for this fixture');
        if (expectedLine !== undefined) assert.equal(payload.line, expectedLine, 'Schema validation must point at the invalid asset, not a fabricated first line');
        else {
            const lines = (await fs.readFile(file, 'utf8')).split(/\r?\n/);
            assert.ok(Number(payload.line) > 1 && Number(payload.line) <= lines.length, 'Malformed multiline XML must retain its actual reader position');
        }
        assert.equal(event.streamId, 'base');
        assert.equal(event.stageId, 'compile');
        const reported = result.report.diagnostics.map(record).find(item => item && item.code === event.diagnosticCode
            && item.message === event.message && sameFile(item.file) && item.line === payload.line && item.column === payload.column);
        assert.ok(reported, 'The final report must contain the same source-located diagnostic as JSONL');
        assert.equal(reported.streamId, event.streamId);
        assert.equal(reported.stageId, event.stageId);
        assert.deepEqual(reported.source, source, 'JSONL and report source metadata must agree');
        for (const field of ['assetType', 'assetId'] as const) assert.equal(reported[field], payload[field]);
        const parsed = result.diagnostics.find(item => item.code === event.diagnosticCode && item.message === event.message
            && sameFile(item.file) && item.line === payload.line && item.column === payload.column);
        assert.ok(parsed, 'The plugin protocol adapter must preserve the compiler location');
        const problem = vscode.languages.getDiagnostics(vscode.Uri.file(file)).find(item =>
            item.source === 'NewSdk Compiler' && item.code === event.diagnosticCode && item.message === event.message);
        assert.ok(problem, 'A reliable compiler position must appear in Problems');
        assert.equal(problem.range.start.line, Number(payload.line) - 1);
        assert.equal(problem.range.start.character, Number(payload.column) - 1);
        return { file: payload.file, line: payload.line, column: payload.column, code: event.diagnosticCode,
            assetType: payload.assetType, assetId: payload.assetId, streamId: event.streamId, stageId: event.stageId,
            eventReportSourceAgreement: true, problemsLocationVerified: true };
    }
    async function verifySuccess(result: Result, mod: string, output: string, content: string[]): Promise<void> {
        assert.equal(result.status, 'succeeded', result.reason);
        assert.ok(result.report, 'A successful real build requires its current final report');
        assert.equal(result.report?.buildId, result.buildId);
        assert.equal(result.exitCode, 0);
        assert.equal(result.report.exitCode, 0);
        assert.equal(result.report.status, 'succeeded');
        assert.equal(result.artifacts.length, 2, 'Current report must confirm one BIG/SKUDEF pair');
        const big = result.artifacts.find(item => item.kind === 'package.big');
        const sku = result.artifacts.find(item => item.kind === 'package.skudef');
        assert.ok(big && sku);
        for (const artifact of result.artifacts) {
            assert.equal(path.dirname(artifact.path).toLowerCase(), path.join(output, 'published').toLowerCase());
            assert.equal((await fs.stat(artifact.path)).size, artifact.size);
        }
        assert.equal(await fs.readFile(sku.path, 'ascii'), `mod-game 1.12\r\nadd-big ${path.basename(big.path)}\r\n`);
        const members = await bigMembers(big.path);
        const expectedContent = content.filter(item => item !== 'additional').sort();
        assert.deepEqual(result.plan?.tasks.map(task => task.content).sort(), expectedContent);
        if (content.includes('base')) assert.ok(manifestAssets(members, 'data\\mod.manifest').includes('ArmorTemplate:NewSdkP0Probe'));
        if (content.includes('apt')) {
            const assets = manifestAssets(members, 'data\\aptui\\tacticalhudcommandlistbox.manifest');
            for (const part of ['AptAptData:TacticalHUDCommandListBox_apt', 'AptConstData:TacticalHUDCommandListBox_const', 'AptDatData:TacticalHUDCommandListBox_dat']) {
                assert.ok(assets.includes(part), `Real APT output omitted ${part}`);
            }
        }
        if (content.includes('maps')) assert.ok(manifestAssets(members, 'data\\additionalmaps\\mapmetadata_p0.manifest').includes('MapMetaData:package0'));
        assert.equal(members.get('sdk-only-p0.txt')?.toString(), 'SDK only');
        if (content.includes('additional')) {
            assert.equal(members.get('shared-p0.txt')?.toString(), 'MOD overlay');
            assert.equal(members.get('mod-only-p0.txt')?.toString(), 'MOD only');
            assert.ok(!members.has('excluded.asset'));
            assert.ok(result.diagnostics.some(item => item.code === 'ADDITIONAL_OVERWRITE' && item.level === 'warning'));
        }
        const cache = await compilerCacheRoot(sdkRoot, mod);
        assert.ok((await fs.stat(cache)).isDirectory(), 'Cache must remain in the SDK-owned MOD partition');
        const reportCache = result.report.cache as { sessionCacheDirectory?: string };
        assert.ok(reportCache.sessionCacheDirectory?.toLowerCase().startsWith(cache.toLowerCase() + path.sep));
        await assert.rejects(fs.access(path.join(mod, 'SuperCompilerCache')), { code: 'ENOENT' });
        await assert.rejects(fs.access(path.join(output, 'SuperCompilerCache')), { code: 'ENOENT' });
        assert.deepEqual(await inventory(sdkRoot, ['SuperCompilerCache']), sdkInputs, 'SDK configuration/schema/Additional inputs must remain unchanged');
        await fs.writeFile(path.join(evidence, 'real-cases', path.basename(mod), 'package-inventory.json'), JSON.stringify({
            buildId: result.buildId, cacheRoot: cache, members: [...members].map(([name, data]) => ({ name, bytes: data.length,
                sha256: createHash('sha256').update(data).digest('hex') })) }, null, 2));
    }
    await summary('running');
    try {
        await runXmlAcceptance(api, root, evidence, compilerPath, sdkRoot);
        const layouts = [{ name: 'RealBase', content: ['base'] }, ...(full ? [
            { name: 'RealAptBusiness', content: ['apt'] }, { name: 'RealGlobalMetadata', content: ['maps'] },
            { name: 'RealComplete', content: ['base', 'apt', 'maps', 'additional'] }] : [])];
        for (const layout of layouts) {
            const { mod, output } = await fixture(layout.name, layout.content);
            const before = await inventory(mod);
            await fs.mkdir(path.join(evidence, 'real-cases', layout.name), { recursive: true });
            await fs.writeFile(path.join(evidence, 'real-cases', layout.name, 'source-inputs.json'), JSON.stringify(before, null, 2));
            checkpoint(`real-${layout.name}-plan`);
            const planned = final(await timed(api.plan(), 180000));
            await preserve(output, `${layout.name}-plan`, planned);
            assert.equal(planned.status, 'succeeded', planned.reason);
            checkpoint(`real-${layout.name}-build`);
            const built = final(await timed(api.build(), 180000));
            await preserve(output, layout.name, built);
            await verifySuccess(built, mod, output, layout.content);
            assert.deepEqual(await inventory(mod), before, 'Real build must leave all MOD source files and bytes unchanged');
            results.push({ name: layout.name, status: 'passed', buildId: built.buildId, artifacts: built.artifacts });
            if (layout.name === 'RealBase') {
                const cache = await compilerCacheRoot(sdkRoot, mod), cacheBefore = await inventory(cache);
                checkpoint('real-cached-plan');
                const readOnlyPlan = final(await timed(api.plan(), 180000));
                assert.equal(readOnlyPlan.status, 'succeeded');
                assert.deepEqual(await inventory(cache), cacheBefore, 'Viewing a plan must leave existing compiler cache untouched');
                results.push({ name: 'plan-preserves-sdk-cache', status: 'passed', cacheRoot: cache });
            }
            await summary('running');
        }
        for (const fxLayout of [
            {name:'RealFx',entry:'Mod.xml',list:'MultiLayerAura',assets:['Flash','Smoke','Sparks','Aura','InheritedFlash'],contracts:['GPU modules','FXList','TGA Texture','ordinary inheritance']},
            {name:'RealFxRound2',entry:'Round2Mod.xml',list:'Round2Acceptance',assets:['CpuDefault','CenteredFlash','ConcentricFlash','PerpendicularFlash','EventEmitter','AttachedEmitter','AttachGlow'],contracts:['CPU Default','GPU geometry/shader combinations','EventFX','attached systems','eight CPU slots']},
        ]) {
            const fxCase = await selected(fxLayout.name);
            await fs.mkdir(path.join(fxCase.mod, 'Data'), {recursive:true});
            await fs.cp(path.resolve(__dirname, '../../../test/fixtures/fx/Data'), path.join(fxCase.mod,'Data'), {recursive:true});
            if (fxLayout.entry !== 'Mod.xml') await fs.copyFile(path.join(fxCase.mod,'Data',fxLayout.entry),path.join(fxCase.mod,'Data/Mod.xml'));
            const beforeFx = await inventory(fxCase.mod);
            checkpoint(`real-${fxLayout.name}-build`);
            const fxBuilt = final(await timed(api.build(),180000));
            await preserve(fxCase.output,fxLayout.name,fxBuilt);
            assert.equal(fxBuilt.status,'succeeded', JSON.stringify({reason:fxBuilt.reason,diagnostics:fxBuilt.diagnostics}));
            const fxBig = fxBuilt.artifacts.find(a=>a.kind==='package.big'); assert.ok(fxBig);
            const fxMembers = await bigMembers(fxBig.path);
            const fxAssets = manifestAssets(fxMembers,'data\\mod.manifest');
            for (const asset of [...fxLayout.assets.map(name=>`FXParticleSystemTemplate:${name}`),`FXList:${fxLayout.list}`,'Texture:FxFixtureTexture']) {
                assert.ok(fxAssets.includes(asset), `Compiled FX fixture omitted ${asset}: ${fxAssets.join(", ")}`);
            }
            assert.deepEqual(await inventory(fxCase.mod),beforeFx,'FX compiler must preserve original XML and source texture');
            results.push({name:fxLayout.name,status:'passed',buildId:fxBuilt.buildId,assets:fxAssets,contracts:fxLayout.contracts});
            await summary('running');
        }
        if (full) {
            const additional = await fixture('RealAdditionalOnly', ['additional']);
            const additionalBefore = await inventory(additional.mod);
            checkpoint('real-additional-only-plan-error');
            const noInputs = final(await timed(api.plan(), 180000));
            await preserve(additional.output, 'RealAdditionalOnly', noInputs);
            assert.equal(noInputs.status, 'failed');
            assert.ok(noInputs.diagnostics.some(item => item.code === 'PLAN_NO_INPUTS'));
            assert.equal(noInputs.artifacts.length, 0);
            assert.deepEqual(await inventory(additional.mod), additionalBefore);
            results.push({ name: 'additional-only-PLAN_NO_INPUTS', status: 'passed', buildId: noInputs.buildId });

            const malformed = await fixture('RealMalformed', ['base']);
            await write(malformed.mod, 'Data/Mod.xml', '<?xml version="1.0"?>\n<AssetDeclaration xmlns="uri:ea.com:eala:asset">\n  <broken');
            const malformedBefore = await inventory(malformed.mod);
            checkpoint('real-malformed-xml-error');
            const invalid = final(await timed(api.build(), 180000));
            await preserve(malformed.output, 'RealMalformed', invalid);
            assert.equal(invalid.status, 'failed');
            assert.ok(invalid.diagnostics.some(item => item.level === 'error' && item.message));
            assert.equal(invalid.artifacts.length, 0);
            const malformedLocation = await locatedFailure(invalid, path.join(malformed.mod, 'Data', 'Mod.xml'));
            assert.deepEqual(await inventory(malformed.mod), malformedBefore);
            assert.deepEqual(await inventory(sdkRoot, ['SuperCompilerCache']), sdkInputs, 'XML errors must preserve SDK inputs');
            results.push({ name: 'malformed-XML-real-error', status: 'passed', buildId: invalid.buildId,
                sourceDiagnostics: expectSourceDiagnostics ? malformedLocation : 'optional capability not required for this compiler' });

            const schemaInvalid = await fixture('RealSchemaInvalidArmor', ['base']);
            await write(schemaInvalid.mod, 'Data/Mod.xml', '<AssetDeclaration xmlns="uri:ea.com:eala:asset">\n  <ArmorTemplate id="BrokenArmor" Default="not-a-number"/>\n</AssetDeclaration>');
            const schemaBefore = await inventory(schemaInvalid.mod);
            checkpoint('real-schema-invalid-armor-error');
            const schemaFailure = final(await timed(api.build(), 180000));
            await preserve(schemaInvalid.output, 'RealSchemaInvalidArmor', schemaFailure);
            assert.equal(schemaFailure.status, 'failed');
            assert.ok(schemaFailure.diagnostics.some(item => item.level === 'error' && item.message));
            assert.equal(schemaFailure.artifacts.length, 0);
            const schemaLocation = await locatedFailure(schemaFailure, path.join(schemaInvalid.mod, 'Data', 'Mod.xml'), 2);
            assert.deepEqual(await inventory(schemaInvalid.mod), schemaBefore, 'Schema diagnostics must not rewrite invalid MOD source');
            assert.deepEqual(await inventory(sdkRoot, ['SuperCompilerCache']), sdkInputs, 'Schema diagnostics must not write SDK schema/configuration inputs');
            results.push({ name: 'schema-invalid-ArmorTemplate-real-error', status: 'passed', buildId: schemaFailure.buildId,
                sourceDiagnostics: expectSourceDiagnostics ? schemaLocation : 'optional capability not required for this compiler' });

            async function cancellationFixture(name: string, count: number): Promise<{ mod: string; output: string }> {
                const { mod, output } = await fixture(name, ['base']);
                const assets = Array.from({ length: count }, (_, index) => `<ArmorTemplate id="P0Cancel${index}" Default="100"/>`).join('');
                await write(mod, 'Data/Mod.xml', `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${assets}</AssetDeclaration>`);
                return { mod, output };
            }
            const force = await cancellationFixture('RealForceStop', 16384);
            const forceBefore = await inventory(force.mod), previousId = api.getState().progress?.buildId;
            const forcePhysical = await fs.realpath(force.mod);
            checkpoint('real-force-stop-start');
            const stopping = timed(api.build(), 180000);
            await waitFor(() => {
                const state = api.getState();
                return state.status === 'running' && state.operation === 'build'
                    && state.config?.modRoot.toLowerCase() === forcePhysical.toLowerCase()
                    && Boolean(state.progress?.buildId) && state.progress?.buildId !== previousId
                    && Boolean(state.progress?.events?.some(event => event.eventType === 'StreamStarted'));
            }, 60000);
            checkpoint('real-force-stop-current-stream');
            api.stop();
            const interrupted = final(await stopping);
            await preserve(force.output, 'RealForceStop-interrupted', interrupted);
            assert.equal(interrupted.status, 'interrupted');
            assert.equal(interrupted.artifacts.length, 0);
            assert.deepEqual(await inventory(force.mod), forceBefore);
            checkpoint('real-force-stop-retry');
            const forceRetry = final(await timed(api.build(), 180000));
            await preserve(force.output, 'RealForceStop-retry', forceRetry);
            assert.equal(forceRetry.status, 'succeeded', forceRetry.reason);
            assert.deepEqual(await inventory(force.mod), forceBefore);
            results.push({ name: 'real-plugin-force-stop-and-retry', status: 'passed', interruptedBuildId: interrupted.buildId, retryBuildId: forceRetry.buildId });

            const normal = await cancellationFixture('RealCancelNormal', 4096);
            const normalBefore = await inventory(normal.mod);
            checkpoint('real-normal-CtrlBreak');
            const cancelled = await timed(runNormalCancellation({ compilerPath, sdkRoot, modRoot: normal.mod,
                outputDirectory: normal.output, modVersion: '1.0', evidenceDirectory: path.join(evidence, 'real-cases', 'RealCancelNormal') }), 180000);
            assert.deepEqual(await inventory(normal.mod), normalBefore);
            checkpoint('real-normal-CtrlBreak-retry');
            const normalRetry = final(await timed(api.build(), 180000));
            await preserve(normal.output, 'RealCancelNormal-retry', normalRetry);
            assert.equal(normalRetry.status, 'succeeded', normalRetry.reason);
            assert.deepEqual(await inventory(normal.mod), normalBefore);
            results.push({ name: 'real-normal-CtrlBreak-and-retry', status: 'passed', cancellation: cancelled, retryBuildId: normalRetry.buildId });
        }
        assert.deepEqual(await inventory(sdkRoot, ['SuperCompilerCache']), sdkInputs);
        await summary('passed');
    } catch (error) {
        await summary('failed', error);
        throw error;
    }
}
