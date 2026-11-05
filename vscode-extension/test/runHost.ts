import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import { spawn } from 'node:child_process';
import { downloadAndUnzipVSCode, runVSCodeCommand } from '@vscode/test-electron';

async function launchWindow(executable: string, args: string[], env: NodeJS.ProcessEnv): Promise<void> {
    await new Promise<void>((resolve, reject) => {
        const child = spawn(executable, args, { env, stdio: ['ignore', 'pipe', 'pipe'] });
        let expired = false;
        const timer = setTimeout(() => {
            expired = true;
            if (child.pid) spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' }).unref();
            reject(new Error(`VS Code 测试窗口未按时退出（${env.NEWSDK_HOST_PHASE}）。`));
        }, 15 * 60 * 1000);
        child.stdout.on('data', data => process.stdout.write(data));
        child.stderr.on('data', data => process.stderr.write(data));
        child.once('error', error => { clearTimeout(timer); reject(error); });
        child.once('exit', (code, signal) => {
            clearTimeout(timer);
            // Windows child pipe descriptors can outlive the GUI process.
            child.stdout.destroy();
            child.stderr.destroy();
            if (expired) return;
            if (code === 0) resolve();
            else reject(new Error(`VS Code 测试窗口退出异常（${env.NEWSDK_HOST_PHASE}）：${code ?? signal}`));
        });
    });
}

async function main(): Promise<void> {
    if (process.platform !== 'win32') throw new Error('VS Code P0 扩展宿主验收仅在 Windows 本地运行。');
    const extensionDevelopmentPath = path.resolve(__dirname, '../..');
    const root = process.env.NEWSDK_TEST_ROOT ?? await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-host-'));
    await fs.mkdir(root, { recursive: true });
    const workspace = path.join(root, 'MockBasic');
    await fs.mkdir(workspace, { recursive: true });
    const evidence = process.env.NEWSDK_TEST_EVIDENCE ?? path.join(extensionDevelopmentPath, 'test-results');
    await fs.mkdir(evidence, { recursive: true });
    const installation = await runVSCodeCommand(['--install-extension', path.join(extensionDevelopmentPath, 'NewSdk-Mod-Tools-FX-Round2.vsix'),
        '--force', '--user-data-dir', path.join(root, 'install-user-data'), '--extensions-dir', path.join(root, 'install-extensions')]);
    await fs.writeFile(path.join(evidence, 'vsix-install.log'), `${installation.stdout}\n${installation.stderr}`);
    const executable = await downloadAndUnzipVSCode();
    const driver = path.join(root, 'test-driver');
    await fs.mkdir(driver, { recursive: true });
    await fs.writeFile(path.join(driver, 'package.json'), JSON.stringify({ name: 'newsdk-p0-test-driver',
        publisher: 'newsdk-tests', version: '0.0.1', engines: { vscode: '^1.96.0' },
        activationEvents: ['onStartupFinished'], extensionKind: ['ui'], main: './index.js' }));
    await fs.writeFile(path.join(driver, 'index.js'), `
const fs = require('node:fs/promises');
const vscode = require('vscode');
exports.activate = function () {
    setTimeout(async function () {
        let result;
        try {
            await require(${JSON.stringify(path.join(__dirname, 'host', 'index.js'))}).run();
            result = { passed: true, phase: process.env.NEWSDK_HOST_PHASE };
        } catch (error) {
            result = { passed: false, phase: process.env.NEWSDK_HOST_PHASE, error: String(error && error.stack || error) };
            console.error(result.error);
        }
        try { await fs.writeFile(process.env.NEWSDK_DRIVER_RESULT, JSON.stringify(result, null, 2)); }
        finally { await vscode.commands.executeCommand('workbench.action.quit'); }
    }, 0);
};
`);
    // VS Code deliberately uses in-memory storage with --extensionTestsPath.
    // A temporary driver runs the same host suite in ordinary windows instead,
    // so both phases exercise the actual persistent profile without production hooks.
    for (const phase of ['exercise', 'reopen']) {
        console.log(`[newsdk-host] Launch ordinary VS Code window: ${phase}`);
        const resultPath = path.join(evidence, `driver-${phase}.json`);
        await fs.rm(resultPath, { force: true });
        const env: NodeJS.ProcessEnv = { ...process.env, NEWSDK_HOST_PHASE: phase, NEWSDK_TEST_ROOT: root,
            NEWSDK_TEST_EVIDENCE: evidence, NEWSDK_REPOSITORY: path.dirname(extensionDevelopmentPath),
            NEWSDK_DRIVER_RESULT: resultPath,
            NEWSDK_COMPILER_URL: process.env.NEWSDK_COMPILER_URL ?? '',
            NEWSDK_COMPILER_SHA256: process.env.NEWSDK_COMPILER_SHA256 ?? '',
            NEWSDK_COMPILER_AUTHORIZATION: process.env.NEWSDK_COMPILER_AUTHORIZATION ?? '',
            NEWSDK_ACCEPTANCE: process.env.NEWSDK_ACCEPTANCE ?? 'smoke' };
        delete env.ELECTRON_RUN_AS_NODE;
        await launchWindow(executable, [workspace, '--new-window', '--wait', '--disable-extensions', '--disable-workspace-trust',
            '--skip-welcome', '--skip-release-notes',
            `--extensionDevelopmentPath=${extensionDevelopmentPath}`, `--extensionDevelopmentPath=${driver}`,
            '--user-data-dir', path.join(root, 'user-data'), '--extensions-dir', path.join(root, 'extensions')], env);
        const result = JSON.parse(await fs.readFile(resultPath, 'utf8')) as { passed: boolean; error?: string };
        if (!result.passed) throw new Error(`VS Code 宿主 ${phase} 验收失败：${result.error ?? '缺少详细错误'}`);
        console.log(`[newsdk-host] Passed: ${phase}`);
    }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
