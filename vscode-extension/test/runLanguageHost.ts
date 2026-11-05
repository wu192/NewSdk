import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';

/** Small cross-platform host suite. It does not install, download or run a compiler. */
async function main(): Promise<void> {
    // test-electron accepts npm's proxy variables; retain an existing setting and
    // otherwise use the environment's normal HTTPS proxy before loading the SDK.
    if (!process.env.npm_config_https_proxy && (process.env.HTTPS_PROXY || process.env.https_proxy)) process.env.npm_config_https_proxy = process.env.HTTPS_PROXY || process.env.https_proxy;
    const {runTests} = await import('@vscode/test-electron');
    const extensionDevelopmentPath = path.resolve(__dirname, '../..');
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-language-host-')));
    const evidence = process.env.NEWSDK_TEST_EVIDENCE ?? path.join(extensionDevelopmentPath, 'test-results');
    const workspace = path.join(root, 'workspace');
    await fs.mkdir(workspace, {recursive: true});
    await fs.mkdir(evidence, {recursive: true});
    try {
        await runTests({
            // Pin the minimum supported API instead of depending on future stable releases.
            version: process.env.NEWSDK_TEST_VSCODE_VERSION ?? '1.96.4',
            vscodeExecutablePath: process.env.NEWSDK_TEST_VSCODE_EXECUTABLE || undefined,
            extensionDevelopmentPath,
            extensionTestsPath: path.join(__dirname, 'host', 'language.js'),
            extensionTestsEnv: {NEWSDK_LANGUAGE_TEST_ROOT: root, NEWSDK_TEST_EVIDENCE: evidence, ELECTRON_RUN_AS_NODE: undefined},
            launchArgs: [workspace, '--disable-extensions', '--disable-workspace-trust', '--skip-welcome', '--skip-release-notes',
                '--disable-gpu', '--disable-dev-shm-usage', '--user-data-dir', path.join(root, 'user-data'),
                '--extensions-dir', path.join(root, 'extensions'), ...(process.platform === 'linux' ? ['--no-sandbox'] : [])],
        });
    } finally { await fs.rm(root, {recursive: true, force: true}); }
}

main().catch(error => {console.error(error); process.exitCode = 1;});
