import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {
    canonicalPath, containsPath, discoverProject, pathIdentity, projectIdentity,
    readProjectPreset, resolveProjectSettings, validateModVersion, validateOutputDirectory,
} from '../src/project';

async function fixture(run: (root: string) => Promise<void>): Promise<void> {
    // Windows runners may expose TEMP through an 8.3 user-directory alias.
    // Expectations use the same physical spelling as the project helpers.
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-project-')));
    try { await run(root); } finally { await fs.rm(root, { recursive: true, force: true }); }
}

test('Windows physical identity and containment ignore case without prefix collisions', () => {
    assert.equal(pathIdentity('C:\\MODS\\Demo\\', 'win32'), 'c:\\mods\\demo');
    assert.ok(containsPath('C:\\Mods\\Demo', 'c:\\mods\\DEMO\\build', 'win32'));
    assert.ok(!containsPath('C:\\Mods\\Demo', 'C:\\Mods\\Demo2', 'win32'));
    assert.ok(!containsPath('C:\\Mods', 'D:\\Mods', 'win32'));
});

test('output validation rejects logical, ancestral and symlink overlaps without creating directories', async () => fixture(async root => {
    const mod = path.join(root, 'mod');
    const sdk = path.join(root, 'sdk');
    await Promise.all([fs.mkdir(mod), fs.mkdir(sdk)]);
    for (const output of [mod, path.join(mod, 'build'), sdk, path.join(sdk, 'build'), root]) {
        await assert.rejects(validateOutputDirectory(output, mod, sdk), /重叠/);
    }
    const alias = path.join(root, 'alias');
    await fs.symlink(mod, alias, process.platform === 'win32' ? 'junction' : 'dir');
    assert.equal(await projectIdentity(alias), await projectIdentity(mod));
    assert.equal(await canonicalPath(path.join(alias, 'nested', 'new')), path.join(await fs.realpath(mod), 'nested', 'new'));
    await assert.rejects(validateOutputDirectory(path.join(alias, 'new'), mod, sdk), /重叠/);
    const output = path.join(root, 'build', 'first');
    assert.equal(await validateOutputDirectory(output, mod, sdk), output);
    await assert.rejects(fs.stat(path.join(root, 'build')), { code: 'ENOENT' });
}));

test('output validation rejects a file, a file ancestor and dangling symlinks', async () => fixture(async root => {
    const mod = path.join(root, 'mod');
    const sdk = path.join(root, 'sdk');
    const file = path.join(root, 'file');
    await Promise.all([fs.mkdir(mod), fs.mkdir(sdk), fs.writeFile(file, 'input')]);
    await assert.rejects(validateOutputDirectory(file, mod, sdk), /必须是目录/);
    await assert.rejects(validateOutputDirectory(path.join(file, 'child'), mod, sdk));
    if (process.platform !== 'win32') {
        const dangling = path.join(root, 'dangling');
        await fs.symlink(path.join(root, 'missing'), dangling, 'dir');
        await assert.rejects(canonicalPath(path.join(dangling, 'new')), /无效的符号链接/);
    }
}));

test('preset uses the real flat compiler schema and resolves relative paths from MOD', async () => fixture(async root => {
    const preset = { 'SDK-ROOT': '../SDK', OD: '../out', 'mod-version': '1.2_test', jobs: 2, ss: true };
    const text = `\uFEFF${JSON.stringify(preset)}`;
    const file = path.join(root, 'newmodcompiler.json');
    await fs.writeFile(file, text);
    const parsed = await readProjectPreset(root);
    assert.equal(parsed.sdkRoot, path.resolve(root, '../SDK'));
    assert.equal(parsed.outputDirectory, path.resolve(root, '../out'));
    assert.equal(parsed.modVersion, '1.2_test');
    assert.equal(parsed.options.jobs, '2');
    assert.equal(parsed.options.ss, 'true');
    const resolved = await resolveProjectSettings(root, { outputDirectory: 'chosen-output', modVersion: '2.0' }, { sdkRoot: 'global-sdk', compilerPath: 'compiler.exe' });
    assert.equal(resolved.outputDirectory, 'chosen-output');
    assert.equal(resolved.modVersion, '2.0');
    assert.equal(resolved.sdkRoot, 'global-sdk');
    assert.equal(resolved.compilerPath, 'compiler.exe');
    assert.equal((await resolveProjectSettings(root)).sdkRoot, parsed.sdkRoot, 'preset SDK is used when no global SDK is configured');
    assert.equal(await fs.readFile(file, 'utf8'), text, 'preset remains byte-for-byte unchanged');
}));

test('missing preset is optional; malformed or conflicting presets fail explicitly', async () => fixture(async root => {
    assert.deepEqual(await readProjectPreset(root), { options: {} });
    const resolved = await resolveProjectSettings(root, undefined, { sdkRoot: 'global-sdk' });
    assert.equal(resolved.sdkRoot, 'global-sdk');
    const file = path.join(root, 'newmodcompiler.json');
    for (const text of ['{', '[]', 'null', '{"modVersion":"1"}', '{"out":[]}', '{"mod-version":""}', '{"out":"a","od":"b"}']) {
        await fs.writeFile(file, text);
        await assert.rejects(readProjectPreset(root));
    }
}));

test('UI and saved compiler overrides are independent of advanced preset values', async () => fixture(async root => {
    const file = path.join(root, 'newmodcompiler.json');
    const text = JSON.stringify({ 'package-format': 'loose', 'include-additional': false, 'game-root': '../game', jobs: 8 });
    await fs.writeFile(file, text);
    const resolved = await resolveProjectSettings(root, { compilerOptions: { jobs: 2, 'include-additional': true } }, {},
        { compilerOptions: { jobs: 1 }, modVersion: 'ui-version' });
    assert.deepEqual(resolved.compilerOptions, { jobs: 1, 'include-additional': true });
    assert.equal(resolved.presetOptions['package-format'], 'loose');
    assert.equal(resolved.presetOptions['game-root'], path.resolve(root, '../game'));
    assert.equal(resolved.modVersion, 'ui-version');
    assert.equal(await fs.readFile(file, 'utf8'), text);
}));

test('discovery accepts APT-only, global-only and Additional-only folders without synthesizing inputs', async () => fixture(async root => {
    await fs.mkdir(path.join(root, 'Data', 'AptUI'), { recursive: true });
    await fs.writeFile(path.join(root, 'Data', 'AptUI', 'Menu.xml'), '<AssetDeclaration/>');
    assert.deepEqual((await discoverProject(root)).contents, { base: false, apt: true, globalMetadata: false, additional: false });
    await fs.rm(path.join(root, 'Data', 'AptUI'), { recursive: true });
    await fs.mkdir(path.join(root, 'Data', 'AdditionalMaps'));
    await fs.writeFile(path.join(root, 'Data', 'AdditionalMaps', 'MapMetaData_Test.xml'), '<AssetDeclaration/>');
    assert.deepEqual((await discoverProject(root)).contents, { base: false, apt: false, globalMetadata: true, additional: false });
    await fs.rm(path.join(root, 'Data'), { recursive: true });
    await fs.mkdir(path.join(root, 'Additional'));
    assert.deepEqual((await discoverProject(root)).contents, { base: false, apt: false, globalMetadata: false, additional: true });
    assert.deepEqual(await fs.readdir(root), ['Additional']);
}));

test('version matches compiler package identity constraints', () => {
    for (const value of ['1.0', 'v2_test-beta', 'a'.repeat(64)]) {
        assert.equal(validateModVersion(value), value);
    }
    for (const value of ['', '1.', '../1', 'a b', '_1', 'a'.repeat(65), '版本1']) {
        assert.throws(() => validateModVersion(value));
    }
});
