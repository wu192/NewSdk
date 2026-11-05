import * as vscode from 'vscode';
import * as path from 'node:path';
import * as fs from 'node:fs/promises';
import { CompilerRunner, RunConfiguration } from './compilerRunner';
import { installCompiler, validateCompilerExecutable } from './compilerInstaller';
import { canonicalPath, discoverProject, DiscoveredProject, GlobalSettings, ProjectSettings,
    resolveProjectSettings, ResolvedProjectSettings, containsPath, validateModVersion, validateOutputDirectory } from './project';
import { NewSdkSidebar, UiModel } from './sidebar';
import { NewSdkWorkbench } from './workbench';
import { NewSdkDiagnostics } from './diagnostics';
import { registerFx, FxApi } from './fx/editorProvider';
import { collectBuildInputs } from './fx/buildInputs';
import { pathIdentity } from './project';
import { CompilerOptionOverrides, requiresModVersion } from './compilerArguments';
import { acquireAssetService, disposeAssetServices, AssetService } from './ra3xml/services/assetService';
import { registerXmlProviders, XmlProviders } from './ra3xml/providers';
import { registerAssetCommands } from './ra3xml/features/indexCommands';
import { registerMcpCommands, McpController } from './agent/commands';
import { FxSkillManager } from './fx/skillManager';
import { SkillRunner } from './fx/skillRunner';
import { registerSkillCommands } from './fx/skillCommands';

const projectStorageKey = 'newsdk.projects.v1';
const lastProjectKey = 'newsdk.lastProject';

export interface ConfigurationValues {
    compilerPath: string;
    sdkRoot: string;
    outputDirectory: string;
    modVersion: string;
    compilerOptions?: CompilerOptionOverrides;
}

/** Public automation surface also exercised inside a real VS Code extension host. */
export interface NewSdkApi {
    getState(): CompilerRunner['state'];
    getProject(): DiscoveredProject | undefined;
    getConfiguration(): ResolvedProjectSettings | undefined;
    selectProject(root: string): Promise<void>;
    configure(values: ConfigurationValues): Promise<void>;
    build(): ReturnType<CompilerRunner['build']>;
    plan(): ReturnType<CompilerRunner['plan']>;
    stop(): void;
    showWorkbench(): void;
    readonly fx: FxApi;
    saveAndBuild(uris?: vscode.Uri[]): ReturnType<CompilerRunner['build']>;
}

export async function activate(context: vscode.ExtensionContext): Promise<NewSdkApi> {
    const runner = new CompilerRunner();
    const sidebar = new NewSdkSidebar();
    const diagnostics = new NewSdkDiagnostics();
    const log = vscode.window.createOutputChannel('NewSdk 扩展日志');
    let project: DiscoveredProject | undefined;
    let settings: ResolvedProjectSettings | undefined;
    let localMessage: string | undefined;
    let commandInProgress = false;
    let fx: FxApi | undefined;
    let xmlProviders: XmlProviders | undefined;
    let xmlProjectKey: string | undefined;
    let mcp: McpController | undefined;
    let watchedService: AssetService | undefined;
    let semanticWatchers: vscode.Disposable[] = [];
    const clearSemanticWatchers = () => {semanticWatchers.forEach(watcher => watcher.dispose()); semanticWatchers = []; watchedService = undefined;};
    const skillManager = new FxSkillManager(context.globalStorageUri.fsPath);
    let pythonPath = vscode.workspace.getConfiguration('newsdk').get<string>('fxPythonPath', '');
    const schemaStorageDirectory = path.join(context.globalStorageUri.fsPath, 'xml-schema');
    let skillRunner = new SkillRunner({pythonPath: pythonPath || undefined, schemaStorageDirectory});
    const currentSkillRunner = () => {
        const configured = vscode.workspace.getConfiguration('newsdk').get<string>('fxPythonPath', '');
        if (configured !== pythonPath) { void skillRunner.dispose().catch(error => log.appendLine(message(error))); pythonPath = configured; skillRunner = new SkillRunner({pythonPath: configured || undefined, schemaStorageDirectory}); }
        return skillRunner;
    };
    context.subscriptions.push({dispose: () => { void skillRunner.dispose().catch(() => undefined); clearSemanticWatchers(); }});
    const workbench = new NewSdkWorkbench(context.extensionUri, (command, value) => {
        // The webview only passes command names and an artifact index/version.
        void vscode.commands.executeCommand(command, value);
    });
    const tree = vscode.window.createTreeView('newsdk.sidebar', { treeDataProvider: sidebar });
    context.subscriptions.push(runner, sidebar, diagnostics, log, workbench, tree);
    context.subscriptions.push(vscode.tasks.registerTaskProvider('newsdk', {
        provideTasks: () => [],
        resolveTask: task => task.execution ? task : undefined,
    }));

    function globals(): GlobalSettings {
        const config = vscode.workspace.getConfiguration('newsdk');
        return { compilerPath: config.get<string>('compilerPath') || undefined, sdkRoot: config.get<string>('sdkRoot') || undefined };
    }
    function savedProjects(): Record<string, ProjectSettings> {
        return context.globalState.get<Record<string, ProjectSettings>>(projectStorageKey, {});
    }
    function ensureSupported(): void {
        if (process.platform !== 'win32') throw new Error('NewSdk P0 仅支持 Windows 本地 VS Code。');
        if (vscode.env.remoteName) throw new Error('请在 Windows 本地 VS Code 中使用 NewSdk；P0 不支持远程窗口。');
        if (!vscode.workspace.isTrusted) throw new Error('信任此工作区后才能配置和启动编译器。');
        if (vscode.workspace.workspaceFolders?.some(folder => folder.uri.scheme !== 'file')) {
            throw new Error('P0 需要本地文件系统中的 MOD 目录。');
        }
    }
    function ensureIdle(): void {
        if (runner.active) throw new Error('请先等待当前任务结束，或停止任务后再修改项目与配置。');
    }
    async function exclusive<T>(action: () => Promise<T>): Promise<T> {
        if (commandInProgress) throw new Error('请先完成当前配置或编译操作。');
        commandInProgress = true;
        try { return await action(); } finally { commandInProgress = false; }
    }
    function render(): void {
        const state = runner.state;
        const current = state.config?.modRoot === project?.root;
        // Full trace stays in JSONL; the UI only needs stream lifecycle events.
        const displayProgress = <T extends NonNullable<typeof state.progress>>(value: T): T => ({ ...value,
            events: value.events.filter(event => event.eventType === 'StreamStarted' || event.eventType === 'StreamCompleted') });
        const progress = current && state.progress ? displayProgress(state.progress) : undefined;
        const result = current && state.result ? displayProgress(state.result) : undefined;
        const model: UiModel = {
            project: project ? { name: project.name, root: project.root, contents: { ...project.contents } } : undefined,
            config: settings,
            active: runner.active,
            status: current ? state.status : 'idle',
            operation: current ? state.operation : undefined,
            elapsedMs: current ? state.elapsedMs : undefined,
            progress, result, plan: progress?.plan,
            message: localMessage ?? (current ? state.message : undefined)
                ?? (process.platform !== 'win32' ? 'P0 仅支持 Windows 本地 VS Code。' : undefined),
        };
        sidebar.update(model);
        workbench.update(model);
        diagnostics.update(result?.diagnostics ?? progress?.diagnostics ?? [], project?.root);
        void vscode.commands.executeCommand('setContext', 'newsdk.running', runner.active);
    }
    async function refresh(): Promise<void> {
        if (project) settings = await resolveProjectSettings(project.root, savedProjects()[project.identity], globals());
        refreshXmlProject();
        render();
    }
    function refreshXmlProject(): void {
        const key = JSON.stringify([project?.identity, settings?.sdkRoot,
            vscode.workspace.getConfiguration('newsdk').get<boolean>('xml.includeSdkXml', false)]);
        if (key === xmlProjectKey) return;
        mcp?.reset();
        clearSemanticWatchers();
        fx?.refreshProject();
        xmlProviders?.refresh();
        disposeAssetServices();
        xmlProjectKey = key;
    }
    async function saveProject(patch: Partial<ProjectSettings>): Promise<void> {
        ensureIdle();
        if (!project) throw new Error('请先选择 MOD 根目录。');
        const all = { ...savedProjects() };
        all[project.identity] = { ...all[project.identity], root: project.root, ...patch };
        await context.globalState.update(projectStorageKey, all);
        await refresh();
    }
    async function selectProject(root: string): Promise<void> {
        ensureIdle();
        const next = await discoverProject(root);
        const resolved = await resolveProjectSettings(next.root, savedProjects()[next.identity], globals());
        ensureIdle();
        project = next;
        settings = resolved;
        refreshXmlProject();
        localMessage = undefined;
        diagnostics.clear();
        await context.globalState.update(lastProjectKey, next.root);
        render();
    }
    async function pickFolder(title: string, initial?: string): Promise<string | undefined> {
        const selected = await vscode.window.showOpenDialog({ title, canSelectFiles: false, canSelectFolders: true,
            canSelectMany: false, defaultUri: initial ? vscode.Uri.file(initial) : undefined, openLabel: '选择目录' });
        if (!selected?.[0]) return undefined;
        if (selected[0].scheme !== 'file') throw new Error('请选择 Windows 本地目录。');
        return selected[0].fsPath;
    }
    async function chooseProject(): Promise<void> {
        ensureSupported();
        ensureIdle();
        const root = await pickFolder('选择 MOD 根目录', project?.root);
        if (root) await selectProject(root);
    }
    async function chooseCompiler(): Promise<void> {
        ensureSupported();
        ensureIdle();
        const release = JSON.parse(await fs.readFile(vscode.Uri.joinPath(context.extensionUri, 'compiler-release.json').fsPath, 'utf8')) as {
            url: string; sha256: string;
        };
        const choice = await vscode.window.showQuickPick(['选择现有 NewModCompiler.exe', '从提供的 HTTPS ZIP 链接下载'],
            { title: '配置编译器', placeHolder: '编译器包单独提供，不包含在插件中' });
        if (!choice) return;
        let compilerPath: string;
        if (choice.startsWith('选择')) {
            const selected = await vscode.window.showOpenDialog({ title: '选择 NewModCompiler.exe', canSelectMany: false,
                canSelectFiles: true, canSelectFolders: false, filters: { 'Windows 编译器': ['exe'] } });
            if (!selected?.[0]) return;
            compilerPath = (await validateCompilerExecutable(selected[0].fsPath)).executablePath;
        } else {
            const url = await vscode.window.showInputBox({ title: '编译器 ZIP 下载链接', ignoreFocusOut: true, value: release.url,
                placeHolder: 'https://…', validateInput: value => {
                    try { const parsed = new URL(value); return parsed.protocol === 'https:' && !parsed.username && !parsed.password
                        ? undefined : '请输入不含用户名和密码的 HTTPS 链接。'; } catch { return '请输入有效的 HTTPS 链接。'; }
                } });
            if (!url) return;
            const expectedSha256 = await vscode.window.showInputBox({ title: 'SHA-256（可留空）', ignoreFocusOut: true,
                value: url === release.url ? release.sha256 : '',
                prompt: '留空时记录实际下载哈希；提供校验值时必须匹配。', validateInput: value => !value || /^[a-f0-9]{64}$/i.test(value)
                    ? undefined : '请输入 64 位十六进制 SHA-256。' });
            if (expectedSha256 === undefined) return;
            const secretKey = `newsdk.downloadAuthorization:${new URL(url).origin}`;
            const authMode = await vscode.window.showQuickPick(['无需认证', '输入 Authorization 请求头', '使用已保存的认证'],
                { title: '编译器下载认证', placeHolder: '令牌只保存到 VS Code SecretStorage' });
            if (!authMode) return;
            let authorization: string | undefined;
            if (authMode === '输入 Authorization 请求头') {
                authorization = await vscode.window.showInputBox({ title: 'Authorization 请求头值', password: true,
                    ignoreFocusOut: true, placeHolder: 'Bearer … / token …' });
                if (!authorization) return;
                await context.secrets.store(secretKey, authorization);
            } else if (authMode === '使用已保存的认证') {
                authorization = await context.secrets.get(secretKey);
                if (!authorization) throw new Error('此下载站点尚未保存认证，请重新输入。');
            }
            compilerPath = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
                title: '下载并验证 NewSdk 编译器', cancellable: true }, async (_progress, token) => {
                const abort = new AbortController();
                const subscription = token.onCancellationRequested(() => abort.abort());
                try {
                    const installed = await installCompiler({ url, expectedSha256: expectedSha256 || undefined,
                        storageDirectory: context.globalStorageUri.fsPath, signal: abort.signal,
                        headers: authorization ? { Authorization: authorization } : undefined,
                        proxyUrl: vscode.workspace.getConfiguration('http').get<string>('proxy') || undefined });
                    log.appendLine(`编译器来源：${installed.sourceUrl}\nSHA-256：${installed.sha256}`);
                    return installed.executablePath;
                } finally { subscription.dispose(); }
            });
        }
        await vscode.workspace.getConfiguration('newsdk').update('compilerPath', compilerPath, vscode.ConfigurationTarget.Global);
        await refresh();
    }
    async function chooseSdk(): Promise<void> {
        ensureSupported(); ensureIdle();
        const root = await pickFolder('选择 SDK 根目录', settings?.sdkRoot ?? globals().sdkRoot);
        if (!root) return;
        const physical = await canonicalPath(root);
        if (!(await fs.stat(physical)).isDirectory()) throw new Error('SDK 根路径必须是目录。');
        if (settings?.outputDirectory && project) await validateOutputDirectory(settings.outputDirectory, project.root, physical);
        await vscode.workspace.getConfiguration('newsdk').update('sdkRoot', physical, vscode.ConfigurationTarget.Global);
        await refresh();
    }
    async function chooseOutput(): Promise<void> {
        ensureSupported(); ensureIdle();
        if (!project) await chooseProject();
        if (!project) return;
        if (!settings?.sdkRoot) await chooseSdk();
        if (!settings?.sdkRoot) return;
        const root = await pickFolder('选择输出目录（须在 SDK 与 MOD 输入目录之外）', settings.outputDirectory);
        if (!root) return;
        await saveProject({ outputDirectory: await validateOutputDirectory(root, project.root, settings.sdkRoot) });
    }
    async function setVersion(value?: unknown): Promise<void> {
        ensureSupported(); ensureIdle();
        if (!project) await chooseProject();
        if (!project) return;
        const version = typeof value === 'string' ? value : await vscode.window.showInputBox({ title: 'MOD 版本',
            value: settings?.modVersion ?? '', ignoreFocusOut: true, prompt: '每次 plan/build 都会显式传给编译器。',
            validateInput: input => { try { validateModVersion(input); return undefined; } catch (error) { return message(error); } } });
        if (version === undefined) return;
        await saveProject({ modVersion: validateModVersion(version) });
    }
    async function chooseBuildOptions(): Promise<void> {
        ensureSupported(); ensureIdle();
        if (!project) await chooseProject();
        if (!project) return;
        const choices = [
            { label: '打包内容', key: 'package-content' },
            { label: '输出格式', key: 'package-format' },
            { label: '包含 Additional 文件', key: 'include-additional' },
            { label: '恢复全部项目编译选项为预设值', key: 'reset' },
        ];
        const selected = await vscode.window.showQuickPick(choices, { title: '项目编译选项', placeHolder: '未覆盖的选项由 newmodcompiler.json 和编译器默认值决定' });
        if (!selected) return;
        const overrides = { ...settings?.compilerOptions };
        if (selected.key === 'reset') { await saveProject({ compilerOptions: {} }); return; }
        let values: { label: string; value?: string | boolean }[];
        if (selected.key === 'package-content') values = [
            { label: '跟随项目预设' }, { label: '自动选择有效内容', value: 'auto' },
            { label: 'Base', value: 'base' }, { label: 'APT', value: 'apt' }, { label: 'Global', value: 'maps' },
            { label: 'Base + APT + Global', value: 'base,apt,maps' },
        ];
        else if (selected.key === 'package-format') values = [
            { label: '跟随项目预设' }, { label: 'BIG / SKUDEF', value: 'big' }, { label: '散文件', value: 'loose' },
        ];
        else values = [{ label: '跟随项目预设' }, { label: '包含', value: true }, { label: '不包含', value: false }];
        const choice = await vscode.window.showQuickPick(values, { title: selected.label });
        if (!choice) return;
        if (choice.value === undefined) delete overrides[selected.key];
        else overrides[selected.key] = choice.value;
        await saveProject({ compilerOptions: overrides });
    }
    async function configureInteractive(): Promise<void> {
        ensureSupported(); ensureIdle();
        if (!project) await chooseProject();
        if (!project) return;
        await chooseCompiler();
        if (!globals().compilerPath) return;
        await chooseSdk();
        if (!settings?.sdkRoot) return;
        await chooseOutput();
        if (!settings?.outputDirectory) return;
        if (settings && (requiresModVersion(settings) || settings.modVersion)) await setVersion();
        if (settings && (!requiresModVersion(settings) || settings.modVersion)) await runner.doctor(await runConfiguration(false));
    }
    async function runConfiguration(prompt: boolean): Promise<RunConfiguration> {
        ensureSupported(); ensureIdle();
        if (!project && prompt) await chooseProject();
        await refresh();
        if (prompt && (!settings?.compilerPath || !settings.sdkRoot || !settings.outputDirectory || (requiresModVersion(settings) && !settings.modVersion))) {
            await configureInteractive();
        }
        if (!project || !settings?.compilerPath || !settings.sdkRoot || !settings.outputDirectory || (requiresModVersion(settings) && !settings.modVersion)) {
            throw new Error('请先配置编译器、SDK、输出目录和 MOD 版本。');
        }
        const compiler = await validateCompilerExecutable(settings.compilerPath);
        return { compilerPath: compiler.executablePath, sdkRoot: settings.sdkRoot, modRoot: project.root,
            outputDirectory: await validateOutputDirectory(settings.outputDirectory, project.root, settings.sdkRoot),
            modVersion: settings.modVersion ? validateModVersion(settings.modVersion) : '',
            compilerOptions: settings.compilerOptions, presetOptions: settings.presetOptions };
    }
    async function configure(values: ConfigurationValues): Promise<void> {
        ensureSupported(); ensureIdle();
        if (!project) throw new Error('请先选择 MOD 根目录。');
        const compiler = await validateCompilerExecutable(values.compilerPath);
        const sdk = await canonicalPath(values.sdkRoot);
        const output = await validateOutputDirectory(values.outputDirectory, project.root, sdk);
        const version = values.modVersion ? validateModVersion(values.modVersion) : '';
        if (!version && requiresModVersion({compilerOptions: values.compilerOptions ?? settings?.compilerOptions, presetOptions: settings?.presetOptions})) validateModVersion(version);
        const config = vscode.workspace.getConfiguration('newsdk');
        await config.update('compilerPath', compiler.executablePath, vscode.ConfigurationTarget.Global);
        await config.update('sdkRoot', sdk, vscode.ConfigurationTarget.Global);
        await saveProject({ outputDirectory: output, modVersion: version,
            ...(values.compilerOptions ? {compilerOptions: values.compilerOptions} : {}) });
    }
    async function buildSaved(): ReturnType<CompilerRunner['build']> {
        const config = await runConfiguration(true);
        localMessage = undefined;
        workbench.show();
        return runner.build(config);
    }
    async function saveAndBuild(uris?: vscode.Uri[]): ReturnType<CompilerRunner['build']> {
        const config = await runConfiguration(true);
        const scannedVersions = new Map<vscode.TextDocument, number>();
        const inputs = await collectBuildInputs({modRoot: config.modRoot, sdkRoot: config.sdkRoot,
            readText: async file => {
                const open = vscode.workspace.textDocuments.find(doc => pathIdentity(doc.uri.fsPath) === pathIdentity(file));
                if (open) { scannedVersions.set(open, open.version); return {text: open.getText(), version: open.version}; }
                const stat = await fs.stat(file);
                if (stat.size > 4 * 1024 * 1024) throw new Error('XML 超过构建输入扫描预算。');
                return {text: await fs.readFile(file, 'utf8'), version: 0};
            }});
        const scope = inputs.complete ? new Set([...inputs.files, ...(uris ?? []).map(uri => uri.fsPath)].map(file => pathIdentity(file))) : undefined;
        const inMod = (uri: vscode.Uri) => uri.scheme === 'file' && containsPath(config.modRoot, uri.fsPath);
        let dirty = vscode.workspace.textDocuments.filter(doc => doc.isDirty && inMod(doc.uri)
            && (scope ? scope.has(pathIdentity(doc.uri.fsPath)) : /\.xml$/i.test(doc.uri.fsPath)));
        if (!scope && dirty.length) {
            const picked = await vscode.window.showQuickPick(dirty.map(doc => ({label: path.relative(config.modRoot, doc.uri.fsPath),
                description: '未保存 XML', picked: true, document: doc})), {canPickMany: true,
                title: '依赖范围未确定：选择本次编译前保存的 MOD XML'});
            if (!picked) throw new Error('已取消保存，未启动编译。');
            dirty = picked.map(item => item.document);
        }
        const unchanged = () => [...scannedVersions].every(([doc, version]) => doc.version === version);
        if (!unchanged()) throw new Error('构建输入在扫描中更新，请重新编译。');
        for (const doc of dirty) {
            const physical = await canonicalPath(doc.uri.fsPath);
            if (!containsPath(config.modRoot, physical)) throw new Error('待保存文档位于 MOD 物理路径之外。');
            if (!await doc.save()) throw new Error(`保存失败，未启动编译：${path.basename(doc.uri.fsPath)}`);
        }
        if (!unchanged()) throw new Error('保存时文档发生修改，请重新编译以确认依赖范围。');
        if (dirty.some(doc => doc.isDirty)) throw new Error('保存后文档又被修改，未启动编译。');
        localMessage = undefined;
        workbench.show();
        const result = await runner.build(config);
        if (result.inputState === 'inputs-changed') localMessage = '输入在编译中变化，结果对应上一保存版本；需重新编译。';
        else if (result.status === 'succeeded') localMessage = result.inputState === 'previous-saved'
            ? '上一保存版本编译成功；当前预览包含未保存修改。' : '已保存版本编译成功。';
        log.appendLine(localMessage ?? `构建结果：${result.status}`);
        render();
        return result;
    }
    async function build(): ReturnType<CompilerRunner['build']> { return saveAndBuild(); }
    async function plan(): ReturnType<CompilerRunner['plan']> {
        const config = await runConfiguration(true);
        localMessage = undefined;
        workbench.show();
        return runner.plan(config);
    }
    async function openRecord(kind: 'events' | 'report' | 'log'): Promise<void> {
        const state = runner.state;
        if (state.config?.modRoot !== project?.root || !state.paths?.[kind] || !state.progress?.buildId) {
            throw new Error('当前项目尚未确认本轮编译记录；启动与环境诊断请查看任务终端。');
        }
        if (kind === 'report' && state.result?.report?.buildId !== state.progress.buildId) {
            throw new Error('尚未确认本轮最终报告，不能展示之前的报告。');
        }
        const file = state.paths[kind];
        try { await fs.access(file); } catch { throw new Error(`本轮尚未生成此文件：${path.basename(file)}`); }
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(file)), { preview: false });
    }
    async function openOutput(): Promise<void> {
        if (!settings?.outputDirectory) throw new Error('请先配置输出目录。');
        await vscode.env.openExternal(vscode.Uri.file(settings.outputDirectory));
    }
    async function openArtifact(index: unknown): Promise<void> {
        const result = runner.state.config?.modRoot === project?.root ? runner.state.result : undefined;
        if (typeof index !== 'number' || !Number.isSafeInteger(index) || index < 0 || !result?.artifacts[index]) {
            throw new Error('请选择本轮报告确认的产物。');
        }
        await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(result.artifacts[index].path));
    }
    function register(id: string, action: (...args: unknown[]) => unknown): void {
        context.subscriptions.push(vscode.commands.registerCommand(id, async (...args: unknown[]) => {
            try { return await action(...args); }
            catch (error) {
                localMessage = message(error);
                log.appendLine(localMessage);
                render();
                void vscode.window.showErrorMessage(`NewSdk：${localMessage}`);
                return undefined;
            }
        }));
    }
    register('newsdk.selectProject', () => exclusive(chooseProject));
    register('newsdk.configure', () => exclusive(configureInteractive));
    register('newsdk.configureCompiler', () => exclusive(chooseCompiler));
    register('newsdk.configureSdk', () => exclusive(chooseSdk));
    register('newsdk.configureOutput', () => exclusive(chooseOutput));
    register('newsdk.setVersion', value => exclusive(() => setVersion(value)));
    register('newsdk.configureBuildOptions', () => exclusive(chooseBuildOptions));
    register('newsdk.build', () => exclusive(build));
    register('newsdk.buildSaved', () => exclusive(buildSaved));
    register('newsdk.saveAndBuild', () => exclusive(() => saveAndBuild()));
    register('newsdk.plan', () => exclusive(plan));
    register('newsdk.doctor', () => exclusive(async () => runner.doctor(await runConfiguration(true))));
    register('newsdk.stop', () => runner.stop());
    register('newsdk.openWorkbench', () => workbench.show());
    register('newsdk.openLog', () => openRecord('log'));
    register('newsdk.openEvents', () => openRecord('events'));
    register('newsdk.openReport', () => openRecord('report'));
    register('newsdk.openOutput', openOutput);
    register('newsdk.openArtifact', openArtifact);
    context.subscriptions.push(runner.onDidChange(() => { localMessage = undefined; render(); }));
    context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration('newsdk')) { fx?.refreshProject(); void refresh().then(() => xmlProviders?.refresh()).catch(error => { localMessage = message(error); render(); }); }
    }));

    function currentAssetService(): AssetService | undefined {
        if (!project || !vscode.workspace.isTrusted) return undefined;
        const roots = [project.root, ...(settings?.sdkRoot ? [settings.sdkRoot] : [])];
        const service = acquireAssetService({ roots, modRoot: project.root, sdkRoot: settings?.sdkRoot,
            includeSdkXml: vscode.workspace.getConfiguration('newsdk').get<boolean>('xml.includeSdkXml', false),
            storagePath: path.join(context.globalStorageUri.fsPath, 'xml-index'),
            readText: async file => {
                const open = vscode.workspace.textDocuments.find(doc => doc.uri.scheme === 'file' && pathIdentity(doc.uri.fsPath) === pathIdentity(file));
                return open ? { text: open.getText(), version: open.version } : { text: await fs.readFile(file, 'utf8'), version: 0 };
            } });
        if (service !== watchedService) {
            clearSemanticWatchers(); watchedService = service;
            for (const root of new Set(roots)) {
                const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, '**/*.{xml,w3x,xsd,manifest,config}'));
                const invalidate = (uri: vscode.Uri) => {
                    if (/[\\/](?:SuperCompilerCache|node_modules|\.git|out|test-results)[\\/]/i.test(uri.fsPath)) return;
                    void service.index.checkedFile(uri.fsPath).then(file => {if (watchedService === service) service.invalidate(file);}).catch(() => undefined);
                };
                semanticWatchers.push(watcher, watcher.onDidCreate(invalidate), watcher.onDidChange(invalidate), watcher.onDidDelete(invalidate));
            }
        }
        return service;
    }
    async function refreshAssetService(service: AssetService): Promise<void> {
        if (service !== currentAssetService()) throw new Error('项目已切换。');
        for (const document of vscode.workspace.textDocuments) {
            if (document.uri.scheme !== 'file' || !/\.xml$/i.test(document.uri.fsPath)
                || !service.options.roots.some(root => containsPath(root, document.uri.fsPath))) continue;
            const checked = await service.index.checkedFile(document.uri.fsPath);
            service.setDocument(checked, document.getText(), document.version);
        }
        await service.startIndexing();
    }
    xmlProviders = registerXmlProviders(context, { getService: document => document.uri.scheme === 'file' ? currentAssetService() : undefined });
    registerAssetCommands(context, {getService: async () => {const service = currentAssetService(); if (service) await refreshAssetService(service); return service;}});
    mcp = registerMcpCommands(context, {getService: currentAssetService, refresh: refreshAssetService});
    registerSkillCommands(context, skillManager, () => fx?.refreshProject());
    context.subscriptions.push(xmlProviders, { dispose: disposeAssetServices });
    fx = registerFx(context, { getProject: () => project, getConfiguration: () => settings,
        skillManager, get skillRunner() { return currentSkillRunner(); },
        saveAndBuild: uris => exclusive(() => saveAndBuild(uris)) });
    context.subscriptions.push(fx);

    // Restoring a selection only reads source files; configuration is never written into the MOD.
    const folders = vscode.workspace.workspaceFolders ?? [];
    const last = context.globalState.get<string>(lastProjectKey);
    const localFolders = folders.filter(folder => folder.uri.scheme === 'file');
    const lastInWorkspace = last && localFolders.some(folder => containsPath(folder.uri.fsPath, last));
    const initial = localFolders.length === 1 ? localFolders[0].uri.fsPath
        : lastInWorkspace || localFolders.length === 0 ? last : undefined;
    if (initial) {
        try { await selectProject(initial); } catch (error) { localMessage = message(error); }
    }
    render();
    return { getState: () => runner.state, getProject: () => project, getConfiguration: () => settings ? { ...settings } : undefined,
        selectProject: root => exclusive(() => selectProject(root)), configure: values => exclusive(() => configure(values)),
        build: () => exclusive(build), plan: () => exclusive(plan),
        stop: () => runner.stop(), showWorkbench: () => workbench.show(), fx,
        saveAndBuild: uris => exclusive(() => saveAndBuild(uris)) };
}

function message(error: unknown): string { return error instanceof Error ? error.message : String(error); }

export function deactivate(): void { /* ExtensionContext subscriptions own all resources. */ }
