import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { collectBuildInputs } from '../src/fx/buildInputs';

async function fixture(run: (root: string, mod: string, sdk: string) => Promise<void>): Promise<void> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'fx-inputs-')));
    const mod = path.join(root, 'mod'), sdk = path.join(root, 'sdk');
    await fs.mkdir(mod); await fs.mkdir(sdk);
    try { await run(root, mod, sdk); } finally { await fs.rm(root, { recursive: true, force: true }); }
}
async function write(root: string, file: string, text: string): Promise<string> {
    const target = path.join(root, file); await fs.mkdir(path.dirname(target), { recursive: true }); await fs.writeFile(target, text); return target;
}
const declaration = (includes = '') => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${includes}</AssetDeclaration>`;
const include = (source: string) => `<Includes><Include source="${source}" type="all"/></Includes>`;

test('save scope covers Base, APT, Global metadata and Additional independently of selected FX', async () => fixture(async (_root, mod, sdk) => {
    const base = await write(mod, 'Data/Mod.xml', declaration(include('first.xml')));
    const first = await write(mod, 'Data/first.xml', declaration());
    const apt = await write(mod, 'Data/AptUI/Menu.xml', declaration(include('Menu/Definition.xml')));
    const aptChild = await write(mod, 'Data/AptUI/Menu/Definition.xml', declaration());
    const global = await write(mod, 'Data/AdditionalMaps/MapMetaData_Global.xml', declaration(include('Global.xml')));
    const globalChild = await write(mod, 'Data/AdditionalMaps/Global.xml', declaration());
    const overlay = await write(mod, 'Additional/packed.xml', '<packed/>');
    const sdkOverlay = await write(sdk, 'Additional/sdk-packed.xml', '<packed/>' );
    const unrelated = await write(mod, 'Data/unreferenced.xml', declaration());
    const result = await collectBuildInputs({ modRoot: mod, sdkRoot: sdk });
    assert.equal(result.complete, true, result.reasons.join('\n'));
    assert.deepEqual(new Set(result.entries), new Set([base, apt, global]));
    assert.deepEqual(new Set(result.files), new Set([base, first, apt, aptChild, global, globalChild, overlay, sdkOverlay]));
    assert.ok(!result.files.includes(unrelated));
}));

test('save scope reads unsaved Include changes from supplied document snapshots', async () => fixture(async (_root, mod, sdk) => {
    const base = await write(mod, 'Data/Mod.xml', declaration(include('old.xml')));
    const old = await write(mod, 'Data/old.xml', declaration());
    const next = await write(mod, 'Data/new.xml', declaration());
    const result = await collectBuildInputs({modRoot: mod, sdkRoot: sdk, readText: async file => file === base
        ? { text: declaration(include('new.xml')), version: 10 } : fs.readFile(file, 'utf8')});
    assert.equal(result.complete, true, result.reasons.join('\n'));
    assert.ok(result.files.includes(next)); assert.ok(!result.files.includes(old));
}));

test('unknown or invalid include closure requests explicit MOD dirty XML fallback', async () => fixture(async (_root, mod, sdk) => {
    await write(mod, 'Data/Mod.xml', declaration(include('missing.xml')));
    let result = await collectBuildInputs({ modRoot: mod, sdkRoot: sdk });
    assert.equal(result.complete, false); assert.ok(result.reasons.length);
    await write(mod, 'Data/Mod.xml', '<AssetDeclaration><broken');
    result = await collectBuildInputs({ modRoot: mod, sdkRoot: sdk });
    assert.equal(result.complete, false); assert.ok(result.reasons.length);
}));

test('scope never follows Additional or Include symlinks into an unrelated workspace', async () => fixture(async (root, mod, sdk) => {
    const external = path.join(root, 'external'); await fs.mkdir(external);
    const externalFile = await write(external, 'unrelated.xml', declaration());
    await write(mod, 'Data/Mod.xml', declaration(include('../escape/unrelated.xml')));
    await fs.symlink(external, path.join(mod, 'escape'), process.platform === 'win32' ? 'junction' : 'dir');
    const result = await collectBuildInputs({ modRoot: mod, sdkRoot: sdk });
    assert.equal(result.complete, false); assert.ok(!result.files.includes(externalFile));
}));
