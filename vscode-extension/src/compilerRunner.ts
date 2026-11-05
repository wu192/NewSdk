import * as vscode from 'vscode';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import {
    BuildProgress, BuildProtocolState, BuildResult, JsonlTail, readBuildReport,
} from './buildProtocol';
import {
    acquirePhysicalProjectLock, compilerArguments, CompilerOperation, diagnosticPaths, DiagnosticPaths, PhysicalProjectLock, ProjectRunLocks, RunConfiguration,
} from './compilerArguments';
import { canonicalPath, containsPath, pathIdentity, projectIdentity, validateOutputDirectory } from './project';
import { BuildVersionTracker } from './fx/buildVersion';

export { RunConfiguration, DiagnosticPaths } from './compilerArguments';

export interface RunnerState {
    operation?: CompilerOperation;
    status: 'idle' | 'running' | 'succeeded' | 'failed' | 'cancelled' | 'interrupted';
    config?: RunConfiguration;
    progress?: BuildProgress;
    result?: BuildResult;
    paths?: DiagnosticPaths;
    message?: string;
    elapsedMs: number;
}

interface TaskCompletion {
    resolve: (exitCode: number | undefined) => void;
    endTimer?: ReturnType<typeof setTimeout>;
}

const projectLocks = new ProjectRunLocks();

/** Owns transient VS Code tasks; never creates .vscode/tasks.json or shell command lines. */
export class CompilerRunner implements vscode.Disposable {
    private readonly changed = new vscode.EventEmitter<RunnerState>();
    readonly onDidChange = this.changed.event;
    private current: RunnerState = { status: 'idle', elapsedMs: 0 };
    private busy = false;
    private forced = false;
    private disposed = false;
    private execution?: vscode.TaskExecution;
    private physicalLock?: PhysicalProjectLock;
    private lockUpdates: Promise<void> = Promise.resolve();
    private readonly completions = new Map<string, TaskCompletion>();
    private readonly subscriptions: vscode.Disposable[];

    constructor() {
        this.subscriptions = [
            vscode.tasks.onDidStartTaskProcess(event => {
                if (this.completions.has(event.execution.task.definition.runId)) this.updateTaskProcess(event.processId);
            }),
            vscode.tasks.onDidEndTaskProcess(event => {
                if (this.completions.has(event.execution.task.definition.runId)) this.updateTaskProcess(undefined);
                this.completeTask(event.execution.task.definition.runId, event.exitCode);
            }),
            vscode.tasks.onDidEndTask(event => {
                const runId: unknown = event.execution.task.definition.runId;
                if (typeof runId !== 'string') return;
                const completion = this.completions.get(runId);
                // A process end normally arrives first. Tasks which never spawn can
                // emit only the task end event; retain an unknown exit code then.
                if (completion && !completion.endTimer) {
                    completion.endTimer = setTimeout(() => this.completeTask(runId, undefined), 100);
                }
            }),
        ];
    }

    get state(): RunnerState { return this.current; }
    get active(): boolean { return this.busy; }

    async doctor(configuration: RunConfiguration): Promise<boolean> {
        this.begin('doctor', configuration);
        const started = Date.now();
        const timer = setInterval(() => this.update({ elapsedMs: Date.now() - started }), 500);
        let release: (() => void) | undefined;
        let physicalLock: PhysicalProjectLock | undefined;
        try {
            const config = await this.prepare(configuration, false);
            const identity = await projectIdentity(config.modRoot);
            release = projectLocks.acquire(identity);
            physicalLock = await acquirePhysicalProjectLock(identity);
            this.physicalLock = physicalLock;
            this.update({ config, message: '正在检查 SDK 环境；原始诊断见任务终端。' });
            const exitCode = await this.runTask('doctor', config);
            const succeeded = exitCode === 0 && !this.forced;
            this.update({
                status: this.forced || exitCode === undefined ? 'interrupted' : succeeded ? 'succeeded' : 'failed',
                message: this.forced ? '环境检查已强制中断，未取得完整结果。' : succeeded
                    ? 'SDK 环境检查通过。' : `SDK 环境检查失败（退出码 ${exitCode ?? '未知'}）；原始诊断见任务终端。`,
            });
            return succeeded;
        } catch (error) {
            this.update({ status: this.forced ? 'interrupted' : 'failed', message: errorMessage(error) });
            return false;
        } finally {
            clearInterval(timer);
            await this.lockUpdates;
            this.physicalLock = undefined;
            await physicalLock?.release().catch(error => {
                this.update({ message: `${this.current.message ?? ''}；释放项目锁失败：${errorMessage(error)}` });
            });
            release?.();
            this.finishOperation(started);
        }
    }

    plan(configuration: RunConfiguration): Promise<BuildResult> {
        return this.run('plan', configuration);
    }

    build(configuration: RunConfiguration): Promise<BuildResult> {
        return this.run('build', configuration);
    }

    /** VS Code terminate is a forced interruption, never advertised as safe Core cancellation. */
    stop(): void {
        if (!this.busy) return;
        this.forced = true;
        this.update({ message: '正在强制停止任务；结果将标记为中断。' });
        this.execution?.terminate();
    }

    dispose(): void {
        if (this.disposed) return;
        this.disposed = true;
        this.stop();
        // Keep task-end listeners until an active task actually ends. Resolving
        // it here would release the cross-window lock while its process may
        // still be shutting down in VS Code's terminal.
        if (!this.busy) this.subscriptions.forEach(subscription => subscription.dispose());
        this.changed.dispose();
    }

    private begin(operation: CompilerOperation, config: RunConfiguration): void {
        if (this.disposed) throw new Error('NewSdk 编译器服务已经关闭。');
        if (this.busy) throw new Error('已有正在运行的编译器任务，请先等待完成或停止。');
        if (process.platform !== 'win32' || vscode.env.remoteName) {
            throw new Error('P0 仅支持 Windows 本地 VS Code。');
        }
        if (!vscode.workspace.isTrusted) throw new Error('请先信任这个工作区，再运行编译器。');
        this.busy = true;
        this.forced = false;
        // Replace the whole state so previous issues/artifacts cannot appear to
        // belong to the new invocation, including while doctor is running.
        this.current = {
            operation, status: 'running', config, elapsedMs: 0,
            message: operation === 'build' ? '准备构建并检查 SDK 环境。' : operation === 'plan' ? '正在解析编译计划。' : '正在检查 SDK 环境。',
        };
        this.changed.fire(this.current);
    }

    private async run(operation: 'plan' | 'build', configuration: RunConfiguration): Promise<BuildResult> {
        this.begin(operation, configuration);
        const started = Date.now();
        const protocol = new BuildProtocolState();
        let release: (() => void) | undefined;
        let physicalLock: PhysicalProjectLock | undefined;
        let timer: ReturnType<typeof setInterval> | undefined;
        let polling: Promise<void> | undefined;
        let poll: (() => Promise<void>) | undefined;
        let protocolError: string | undefined;
        let inputObservation: ReturnType<typeof observeInputs> | undefined;
        try {
            timer = setInterval(() => {
                this.update({ elapsedMs: Date.now() - started });
                void poll?.().catch(error => { protocolError = errorMessage(error); });
            }, 500);
            const config = await this.prepare(configuration, true);
            const identity = await projectIdentity(config.modRoot);
            release = projectLocks.acquire(identity);
            physicalLock = await acquirePhysicalProjectLock(identity);
            this.physicalLock = physicalLock;
            const paths = diagnosticPaths(operation, config.outputDirectory);
            this.update({ config, paths });
            if (operation === 'build') {
                this.update({ message: '正在检查 SDK 环境；通过后开始编译。' });
                const doctorExit = await this.runTask('doctor', config);
                if (doctorExit !== 0 || this.forced) {
                    const result: BuildResult = {
                        ...protocol.snapshot(), status: this.forced || doctorExit === undefined ? 'interrupted' : 'failed',
                        exitCode: doctorExit,
                        reason: this.forced ? 'SDK 环境检查已强制中断，未开始构建。' : `SDK 环境检查失败（退出码 ${doctorExit ?? '未知'}）；未开始构建，原始诊断见任务终端。`,
                        artifacts: [],
                    };
                    this.setResult(result);
                    return result;
                }
            }
            if (this.forced) {
                const result = protocol.finish({ exitCode: undefined, forced: true });
                this.setResult(result);
                return result;
            }
            // Capture old build IDs before launching. No old success event/report
            // may certify this invocation, even when startup fails before truncation.
            const tail = await JsonlTail.capture(paths.events);
            if (operation === 'build') inputObservation = observeInputs(config);
            poll = () => {
                if (polling) return polling;
                polling = (async () => {
                    const events = await tail.poll();
                    protocol.accept(events);
                    this.update({ progress: protocol.snapshot() });
                })().finally(() => { polling = undefined; });
                return polling;
            };
            this.update({ message: operation === 'plan' ? '正在解析编译计划。' : '正在编译 MOD。', progress: protocol.snapshot() });
            const exitCode = await this.runTask(operation, config);
            if (timer) { clearInterval(timer); timer = undefined; }
            // A read which started before process exit may have observed an older
            // file length. Drain it, then perform one new read after exit.
            await polling;
            await poll();
            let report;
            try { report = await readBuildReport(paths.report); }
            catch (error) { protocolError = errorMessage(error); }
            const problems = [...tail.errors];
            if (protocolError) problems.push(protocolError);
            if (tail.hasPartialLine) problems.push('事件日志末尾存在未完成的 JSONL 行。');
            const result = protocol.finish({
                exitCode, report, forced: this.forced,
                protocolError: problems.length ? problems.join('\n') : undefined,
            });
            if (inputObservation) result.inputState = inputObservation.tracker.result();
            this.setResult(result);
            return result;
        } catch (error) {
            const result = protocol.finish({
                exitCode: undefined, forced: this.forced, protocolError: errorMessage(error),
            });
            if (inputObservation) result.inputState = inputObservation.tracker.result();
            // Startup and configuration failures must remain clear even without
            // a compiler session or a structured report.
            if (!this.forced && !result.buildId) {
                result.status = 'failed';
                result.reason = errorMessage(error);
            }
            this.setResult(result);
            return result;
        } finally {
            inputObservation?.dispose();
            if (timer) clearInterval(timer);
            await polling?.catch(() => undefined);
            await this.lockUpdates;
            this.physicalLock = undefined;
            await physicalLock?.release().catch(error => {
                this.update({ message: `${this.current.message ?? ''}；释放项目锁失败：${errorMessage(error)}` });
            });
            release?.();
            this.finishOperation(started);
        }
    }

    private async prepare(configuration: RunConfiguration, needsOutput: boolean): Promise<RunConfiguration> {
        compilerArguments(needsOutput ? 'build' : 'doctor', configuration);
        if (!configuration.compilerPath.trim()) throw new Error('请选择 NewModCompiler.exe。');
        if (!configuration.modRoot.trim()) throw new Error('请选择 MOD 根目录。');
        const config: RunConfiguration = {
            ...configuration,
            compilerPath: await canonicalPath(configuration.compilerPath),
            sdkRoot: await canonicalPath(configuration.sdkRoot),
            modRoot: await canonicalPath(configuration.modRoot),
            outputDirectory: needsOutput ? await canonicalPath(configuration.outputDirectory) : configuration.outputDirectory,
        };
        if (!(await fs.stat(config.compilerPath)).isFile()) throw new Error('编译器路径必须指向 NewModCompiler.exe。');
        if (!(await fs.stat(config.sdkRoot)).isDirectory()) throw new Error('SDK 路径必须是目录。');
        if (!(await fs.stat(config.modRoot)).isDirectory()) throw new Error('MOD 路径必须是目录。');
        if (path.basename(config.compilerPath).toLowerCase() !== 'newmodcompiler.exe') {
            throw new Error('请选择 NewModCompiler.exe。');
        }
        compilerArguments(needsOutput ? 'build' : 'doctor', config);
        if (needsOutput) await validateOutputDirectory(config.outputDirectory, config.modRoot, config.sdkRoot);
        return config;
    }

    private async runTask(operation: CompilerOperation, config: RunConfiguration): Promise<number | undefined> {
        if (this.forced) return undefined;
        const runId = randomUUID();
        const scope = vscode.workspace.workspaceFolders?.find(folder => {
            const relative = path.relative(folder.uri.fsPath, config.modRoot);
            return relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative));
        }) ?? vscode.TaskScope.Workspace;
        const task = new vscode.Task(
            { type: 'newsdk', runId, operation }, scope,
            operation === 'doctor' ? 'NewSdk 环境检查' : operation === 'plan' ? 'NewSdk 编译计划' : 'NewSdk 编译 MOD',
            'NewSdk Mod Tools',
            new vscode.ProcessExecution(config.compilerPath, compilerArguments(operation, config), {
                cwd: operation === 'doctor' ? path.dirname(config.compilerPath) : config.modRoot,
            }),
            [],
        );
        task.presentationOptions = {
            reveal: vscode.TaskRevealKind.Always,
            panel: vscode.TaskPanelKind.Dedicated,
            clear: false,
            focus: false,
            showReuseMessage: false,
        };
        task.isBackground = false;
        let resolve!: (code: number | undefined) => void;
        const finished = new Promise<number | undefined>(accept => { resolve = accept; });
        this.completions.set(runId, { resolve });
        try {
            const execution = await vscode.tasks.executeTask(task);
            this.execution = execution;
            if (this.forced) execution.terminate();
            return await finished;
        } catch (error) {
            this.completeTask(runId, undefined);
            throw new Error(`无法启动编译器任务：${errorMessage(error)}`);
        } finally {
            this.execution = undefined;
            const completion = this.completions.get(runId);
            if (completion?.endTimer) clearTimeout(completion.endTimer);
            this.completions.delete(runId);
        }
    }

    private completeTask(runId: unknown, exitCode: number | undefined): void {
        if (typeof runId !== 'string') return;
        const completion = this.completions.get(runId);
        if (!completion) return;
        if (completion.endTimer) clearTimeout(completion.endTimer);
        this.completions.delete(runId);
        completion.resolve(exitCode);
    }

    private updateTaskProcess(processId: number | undefined): void {
        const lock = this.physicalLock;
        if (!lock) return;
        this.lockUpdates = this.lockUpdates.then(() => lock.setProcessId(processId)).catch(error => {
            this.update({ message: `${this.current.message ?? ''}；记录编译器进程失败：${errorMessage(error)}` });
        });
    }

    private setResult(result: BuildResult): void {
        const stale = result.inputState === 'inputs-changed' ? '；编译期间输入文件已变化，结果对应上一保存版本。'
            : result.inputState === 'previous-saved' ? '；结果对应上一保存版本，未包含当前未保存修改。' : '';
        this.update({ result, progress: result, status: result.status, message: result.reason + stale });
    }

    private update(change: Partial<RunnerState>): void {
        this.current = { ...this.current, ...change };
        if (!this.disposed) this.changed.fire(this.current);
    }

    private finishOperation(started: number): void {
        this.busy = false;
        this.execution = undefined;
        this.update({ elapsedMs: Date.now() - started });
        if (this.disposed) this.subscriptions.forEach(subscription => subscription.dispose());
    }
}

/** Observe source changes during this invocation; never scan/hash the SDK or its compiler caches. */
function observeInputs(config: RunConfiguration): { tracker: BuildVersionTracker; dispose(): void } {
    const tracker = new BuildVersionTracker();
    const cacheRoot = path.join(config.sdkRoot, 'SuperCompilerCache');
    // SDK resource paths may be customized in BinaryAssetBuilder.exe.config.
    // Observe changes conservatively, while excluding the compiler's own cache.
    const relevant = (uri: vscode.Uri): boolean => uri.scheme === 'file' && !containsPath(cacheRoot, uri.fsPath)
        && (containsPath(config.modRoot, uri.fsPath) || containsPath(config.sdkRoot, uri.fsPath));
    const subscriptions: vscode.Disposable[] = [];
    for (const root of new Set([config.modRoot, config.sdkRoot])) {
        const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, '**/*'));
        const changed = (uri: vscode.Uri): void => { if (relevant(uri)) tracker.disk(); };
        subscriptions.push(watcher, watcher.onDidChange(changed), watcher.onDidCreate(changed), watcher.onDidDelete(changed));
    }
    subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
        if (relevant(event.document.uri) && event.contentChanges.length) tracker.memory(pathIdentity(event.document.uri.fsPath), event.document.version);
    }), vscode.workspace.onDidSaveTextDocument(document => { if (relevant(document.uri)) tracker.disk(); }));
    const documents = vscode.workspace.textDocuments.filter(document => relevant(document.uri));
    for (const document of documents) tracker.memory(pathIdentity(document.uri.fsPath), document.version);
    tracker.begin(documents.some(document => document.isDirty));
    return { tracker, dispose: () => subscriptions.forEach(subscription => subscription.dispose()) };
}

function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
}
