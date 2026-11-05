import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import { FxSkillManager } from '../src/fx/skillManager';

const fixture: Record<string, string> = {
    'SKILL.md': '---\nname: ra3-fx-authoring\n---\n[Fields](references/fields.md)\n[Example](examples/impact.xml)\n',
    'PROMPT_FOR_CHAT.md': 'Use the selected project XML.\n',
    'references/fields.md': '# Fields\n[Template](../templates/gpu.xml)\n',
    'templates/gpu.xml': '<AssetDeclaration/>\n',
    'examples/impact.xml': '<AssetDeclaration/>\n',
    'scripts/generate_fx.py': 'raise RuntimeError("import must never execute scripts")\n',
    'scripts/validate_fx.py': 'raise RuntimeError("import must never execute scripts")\n',
    'examples/textures/original.tga': 'fixture texture data',
};
async function writeFixture(root: string, files = fixture): Promise<void> {
    for (const [name, content] of Object.entries(files)) {
        const file = path.join(root, name); await fs.mkdir(path.dirname(file), { recursive: true }); await fs.writeFile(file, content);
    }
}
async function temporary(t: { after(fn: () => Promise<void>): void }): Promise<string> {
    const root = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-skill-manager-')); t.after(() => fs.rm(root, { recursive: true, force: true })); return root;
}

function crc32(buffer: Buffer): number {
    let value = 0xffffffff;
    for (const byte of buffer) { value ^= byte; for (let bit = 0; bit < 8; bit++) value = value >>> 1 ^ (value & 1 ? 0xedb88320 : 0); }
    return (value ^ 0xffffffff) >>> 0;
}
/** Tiny store-only fixture ZIP writer, including deliberately invalid path metadata. */
function zipFixture(files: Record<string, string>, symlink?: string): Buffer {
    const local: Buffer[] = []; const central: Buffer[] = []; let offset = 0;
    for (const [name, text] of Object.entries(files)) {
        const file = Buffer.from(name); const data = Buffer.from(text); const crc = crc32(data);
        const header = Buffer.alloc(30); header.writeUInt32LE(0x04034b50); header.writeUInt16LE(20, 4); header.writeUInt32LE(crc, 14); header.writeUInt32LE(data.length, 18); header.writeUInt32LE(data.length, 22); header.writeUInt16LE(file.length, 26);
        const entry = Buffer.alloc(46); entry.writeUInt32LE(0x02014b50); entry.writeUInt16LE(3 << 8 | 20, 4); entry.writeUInt16LE(20, 6); entry.writeUInt32LE(crc, 16); entry.writeUInt32LE(data.length, 20); entry.writeUInt32LE(data.length, 24); entry.writeUInt16LE(file.length, 28); entry.writeUInt32LE(((name === symlink ? 0o120777 : 0o100644) << 16) >>> 0, 38); entry.writeUInt32LE(offset, 42);
        local.push(header, file, data); central.push(entry, file); offset += header.length + file.length + data.length;
    }
    const directory = Buffer.concat(central); const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(Object.keys(files).length, 8); end.writeUInt16LE(Object.keys(files).length, 10); end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16);
    return Buffer.concat([...local, directory, end]);
}

test('Skill import snapshots preserve all resources without executing code and restore verified versions', async t => {
    const root = await temporary(t); const source = path.join(root, 'source'); await writeFixture(source);
    const manager = new FxSkillManager(path.join(root, 'storage'));
    const first = await manager.importSkill(source);
    assert.equal(first.validation.valid, true); assert.match(first.version, /^sha256-[a-f0-9]{64}$/);
    assert.equal(first.files.length, Object.keys(fixture).length);
    assert.equal(await fs.readFile(path.join(first.rootPath, 'examples/textures/original.tga'), 'utf8'), fixture['examples/textures/original.tga']);
    assert.equal(await manager.promptText(), fixture['PROMPT_FOR_CHAT.md']);
    assert.equal(await manager.documentPath(), path.join(first.rootPath, 'SKILL.md'));
    await fs.writeFile(path.join(source, 'examples/impact.xml'), '<AssetDeclaration><!--updated--></AssetDeclaration>');
    const second = await manager.importSkill(source); assert.notEqual(first.version, second.version);
    assert.equal((await manager.listSnapshots()).length, 2);
    await manager.restore(first.version); assert.equal((await manager.current())?.version, first.version);
    await fs.writeFile(path.join(second.rootPath, 'examples/impact.xml'), 'tampered');
    await assert.rejects(manager.restore(second.version), /快照文件已变化/);
    assert.equal((await manager.current())?.version, first.version);
    await assert.rejects(manager.restore('../../source'), /版本标识无效/);
});

test('Skill import rejects missing references, links and excess payload without replacing the selected snapshot', async t => {
    const root = await temporary(t); const source = path.join(root, 'source'); await writeFixture(source);
    const manager = new FxSkillManager(path.join(root, 'storage')); const first = await manager.importSkill(source);
    await fs.writeFile(path.join(source, 'references/fields.md'), '[Missing](missing.json)');
    await assert.rejects(manager.importSkill(source), /相对引用不存在/);
    await fs.writeFile(path.join(source, 'references/fields.md'), '[Escape](../../outside.md)');
    await assert.rejects(manager.importSkill(source), /相对引用越出/);
    await fs.writeFile(path.join(source, 'references/fields.md'), fixture['references/fields.md']);
    await fs.symlink(path.join(source, 'SKILL.md'), path.join(source, 'references/link.md'));
    await assert.rejects(manager.importSkill(source), /链接/); await fs.unlink(path.join(source, 'references/link.md'));
    const limited = new FxSkillManager(path.join(root, 'limited'), { maxBytes: 16 });
    await assert.rejects(limited.importSkill(source), /预算/);
    assert.equal((await manager.current())?.version, first.version);
    assert.equal((await fs.readdir(manager.storagePath)).some(name => name.startsWith('import-')), false);
});

test('Skill ZIP preserves a wrapped tree and rejects traversal, symlinks and extraction budgets', async t => {
    const root = await temporary(t); const archive = path.join(root, 'skill.zip');
    await fs.writeFile(archive, zipFixture(Object.fromEntries(Object.entries(fixture).map(([name, data]) => [`ra3-fx-authoring/${name}`, data]))));
    const manager = new FxSkillManager(path.join(root, 'storage')); const snapshot = await manager.importSkill(archive);
    assert.equal(snapshot.files.length, Object.keys(fixture).length);
    assert.equal(await fs.readFile(path.join(snapshot.rootPath, 'scripts/generate_fx.py'), 'utf8'), fixture['scripts/generate_fx.py']);
    await fs.writeFile(archive, zipFixture({ '../escape': 'bad' }));
    await assert.rejects(manager.importSkill(archive), /path|路径/i);
    assert.equal(await fs.stat(path.join(root, 'escape')).then(() => true, () => false), false);
    await fs.writeFile(archive, zipFixture({ link: '/somewhere' }, 'link'));
    await assert.rejects(manager.importSkill(archive), /链接/);
    await fs.writeFile(archive, zipFixture({ 'large.txt': 'x'.repeat(1024) }));
    const limited = new FxSkillManager(path.join(root, 'limited'), { maxFileBytes: 16 });
    await assert.rejects(limited.importSkill(archive), /预算/);
    assert.equal((await manager.current())?.version, snapshot.version);
});

test('assistant installation updates only an owned unchanged tree and preserves local changes on uninstall', async t => {
    const root = await temporary(t); const source = path.join(root, 'source'); await writeFixture(source);
    const manager = new FxSkillManager(path.join(root, 'storage')); const first = await manager.importSkill(source);
    const parent = path.join(root, 'assistant-skills'); const installation = await manager.install(parent);
    assert.equal(installation.destination, path.join(parent, 'ra3-fx-authoring')); assert.equal(installation.version, first.version);
    assert.equal((await manager.listInstallations()).length, 1);
    await fs.writeFile(path.join(source, 'examples/impact.xml'), '<updated/>');
    const next = await manager.importSkill(source); const updated = await manager.install(parent);
    assert.equal(updated.id, installation.id); assert.equal(updated.version, next.version);
    await fs.writeFile(path.join(updated.destination, 'examples/impact.xml'), '<user-edit/>');
    await assert.rejects(manager.install(parent), /本地修改/);
    assert.deepEqual(await manager.uninstall(updated.id), { removed: false, preserved: ['examples/impact.xml'] });
    assert.equal(await fs.readFile(path.join(updated.destination, 'examples/impact.xml'), 'utf8'), '<user-edit/>');
    await fs.writeFile(path.join(updated.destination, 'examples/impact.xml'), '<updated/>');
    assert.deepEqual(await manager.uninstall(updated.id), { removed: true, preserved: [] });
    assert.equal((await manager.listInstallations()).length, 0);
    await fs.mkdir(updated.destination); await fs.writeFile(path.join(updated.destination, 'keep.txt'), 'not managed');
    await assert.rejects(manager.install(parent), /其他来源/);
    assert.equal(await fs.readFile(path.join(updated.destination, 'keep.txt'), 'utf8'), 'not managed');
});
