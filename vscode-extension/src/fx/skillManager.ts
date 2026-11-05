import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import * as yauzl from 'yauzl';

export interface SkillFile { path: string; size: number; sha256: string; mode: number; }
export interface SkillSnapshot {
    version: string; hash: string; rootPath: string; source: string; importedAt: string;
    files: SkillFile[]; validation: { valid: boolean; warnings: string[] };
}
export interface SkillInstallation {
    id: string; version: string; destination: string; installedAt: string; files: SkillFile[];
}
interface SkillState { current?: string; installations: SkillInstallation[]; }
export interface SkillManagerLimits { maxFiles?: number; maxBytes?: number; maxFileBytes?: number; }
const requiredDirectories = ['references', 'examples', 'templates', 'scripts'];
const skillName = 'ra3-fx-authoring';
const digest = (data: string | Buffer): string => createHash('sha256').update(data).digest('hex');

function inside(root: string, candidate: string): boolean {
    const relative = path.relative(root, candidate);
    return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}
function relativeName(name: string): string {
    if (!name || name.includes('\\') || name.includes('\0') || name.startsWith('/') || name.includes(':')) throw new Error(`Skill 路径不安全：${name}`);
    const parts = name.replace(/\/$/, '').split('/');
    if (parts.length > 64 || parts.some(part => !part || part === '.' || part === '..' || /[. ]$/.test(part))) throw new Error(`Skill 路径不安全：${name}`);
    return parts.join('/');
}
async function exists(file: string): Promise<boolean> {
    try { await fs.lstat(file); return true; } catch (error) { if ((error as NodeJS.ErrnoException).code === 'ENOENT') return false; throw error; }
}

/** Private, immutable Skill snapshots. Importing copies data; it never runs imported code. */
export class FxSkillManager {
    readonly storagePath: string;
    private readonly limits: Required<SkillManagerLimits>;
    private transaction: Promise<unknown> = Promise.resolve();
    constructor(storagePath: string, limits: SkillManagerLimits = {}) {
        this.storagePath = path.join(path.resolve(storagePath), 'fx-skills');
        this.limits = { maxFiles: limits.maxFiles ?? 10_000, maxBytes: limits.maxBytes ?? 64 * 1024 * 1024, maxFileBytes: limits.maxFileBytes ?? 16 * 1024 * 1024 };
    }
    private serial<T>(operation: () => Promise<T>): Promise<T> {
        const result = this.transaction.then(operation, operation);
        this.transaction = result.catch(() => undefined);
        return result;
    }
    private async initialize(): Promise<void> { await fs.mkdir(path.join(this.storagePath, 'snapshots'), { recursive: true }); }
    private async state(): Promise<SkillState> {
        try { const state = JSON.parse(await fs.readFile(path.join(this.storagePath, 'state.json'), 'utf8')) as SkillState; return { current: state.current, installations: Array.isArray(state.installations) ? state.installations : [] }; }
        catch (error) { if ((error as NodeJS.ErrnoException).code === 'ENOENT') return { installations: [] }; throw error; }
    }
    private async saveState(state: SkillState): Promise<void> {
        await this.initialize();
        const temporary = path.join(this.storagePath, `${randomUUID()}.json`);
        await fs.writeFile(temporary, JSON.stringify(state, null, 2), { mode: 0o600 });
        await fs.rename(temporary, path.join(this.storagePath, 'state.json'));
    }
    private snapshotDirectory(version: string): string {
        if (!/^sha256-[a-f0-9]{64}$/.test(version)) throw new Error('Skill 版本标识无效。');
        return path.join(this.storagePath, 'snapshots', version);
    }
    private async snapshot(version: string): Promise<SkillSnapshot> {
        const directory = this.snapshotDirectory(version);
        const data = JSON.parse(await fs.readFile(path.join(directory, 'snapshot.json'), 'utf8')) as SkillSnapshot;
        return { ...data, version, rootPath: path.join(directory, skillName) };
    }
    async current(): Promise<SkillSnapshot | undefined> {
        const selected = (await this.state()).current;
        return selected ? this.snapshot(selected) : undefined;
    }
    async listSnapshots(): Promise<SkillSnapshot[]> {
        await this.initialize();
        const entries = await fs.readdir(path.join(this.storagePath, 'snapshots'), { withFileTypes: true });
        const snapshots: SkillSnapshot[] = [];
        for (const entry of entries) if (entry.isDirectory() && /^sha256-[a-f0-9]{64}$/.test(entry.name)) snapshots.push(await this.snapshot(entry.name));
        return snapshots.sort((a, b) => b.importedAt.localeCompare(a.importedAt));
    }
    async documentPath(): Promise<string | undefined> { const snapshot = await this.current(); return snapshot && path.join(snapshot.rootPath, 'SKILL.md'); }
    async promptText(): Promise<string> {
        const snapshot = await this.current();
        if (!snapshot) throw new Error('请先导入 ra3-fx-authoring Skill。');
        const prompt = path.join(snapshot.rootPath, 'PROMPT_FOR_CHAT.md');
        return fs.readFile(await exists(prompt) ? prompt : path.join(snapshot.rootPath, 'SKILL.md'), 'utf8');
    }
    async restore(version: string): Promise<SkillSnapshot> {
        return this.serial(async () => {
            const snapshot = await this.snapshot(version);
            await this.verifySnapshot(snapshot);
            const state = await this.state(); state.current = version; await this.saveState(state);
            return snapshot;
        });
    }
    async importSkill(sourcePath: string): Promise<SkillSnapshot> {
        return this.serial(async () => {
            await this.initialize();
            const source = path.resolve(sourcePath);
            if (inside(source, this.storagePath) || inside(this.storagePath, source)) throw new Error('Skill 来源不能与扩展 Skill 存储重叠。');
            const info = await fs.lstat(source);
            if (info.isSymbolicLink()) throw new Error('Skill 来源不能是符号链接。');
            const stage = await fs.mkdtemp(path.join(this.storagePath, 'import-'));
            try {
                const unpacked = path.join(stage, 'unpacked'); await fs.mkdir(unpacked);
                if (info.isDirectory()) await this.copyTree(source, unpacked);
                else if (info.isFile() && /\.zip$/i.test(source)) await this.extractZip(source, unpacked);
                else throw new Error('请选择 Skill 目录或 ZIP 文件。');
                const root = await this.findSkillRoot(unpacked);
                const files = await this.inventory(root);
                const warnings = await this.validate(root, files);
                const hash = digest(files.map(file => `${file.path}\0${file.size}\0${file.sha256}\0${file.mode}`).join('\n'));
                const version = `sha256-${hash}`;
                const directory = this.snapshotDirectory(version);
                if (!await exists(directory)) {
                    const prepared = path.join(stage, 'snapshot'); await fs.mkdir(prepared);
                    await fs.rename(root, path.join(prepared, skillName));
                    const snapshot: SkillSnapshot = { version, hash, source, importedAt: new Date().toISOString(), files, rootPath: path.join(directory, skillName), validation: { valid: true, warnings } };
                    await fs.writeFile(path.join(prepared, 'snapshot.json'), JSON.stringify(snapshot, null, 2), { mode: 0o600 });
                    await fs.rename(prepared, directory);
                }
                const snapshot = await this.snapshot(version); await this.verifySnapshot(snapshot);
                const state = await this.state(); state.current = version; await this.saveState(state);
                return snapshot;
            } finally { await fs.rm(stage, { recursive: true, force: true }); }
        });
    }
    private async copyTree(source: string, destination: string): Promise<void> {
        const files = await this.inventory(source);
        for (const file of files) {
            const target = path.join(destination, file.path); await fs.mkdir(path.dirname(target), { recursive: true });
            const content = await fs.readFile(path.join(source, file.path));
            if (content.length !== file.size || digest(content) !== file.sha256) throw new Error('Skill 来源在导入过程中发生变化，请重试。');
            await fs.writeFile(target, content, { mode: file.mode, flag: 'wx' });
        }
        // Keep empty resource directories as well.
        const directories = async (base: string, relative = ''): Promise<void> => {
            for (const entry of await fs.readdir(path.join(base, relative), { withFileTypes: true })) {
                if (entry.isSymbolicLink()) throw new Error('Skill 不允许符号链接。');
                if (entry.isDirectory()) { const child = path.join(relative, entry.name); await fs.mkdir(path.join(destination, child), { recursive: true }); await directories(base, child); }
            }
        };
        await directories(source);
    }
    private async inventory(root: string): Promise<SkillFile[]> {
        let bytes = 0; const files: SkillFile[] = []; const caseNames = new Set<string>();
        const walk = async (directory: string): Promise<void> => {
            for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
                const absolute = path.join(directory, entry.name);
                const relative = relativeName(path.relative(root, absolute).split(path.sep).join('/'));
                const folded = relative.toLowerCase();
                if (caseNames.has(folded)) throw new Error(`Skill 包含大小写冲突路径：${relative}`);
                caseNames.add(folded);
                if (caseNames.size > this.limits.maxFiles * 2) throw new Error('Skill 超过目录或文件数量预算。');
                const info = await fs.lstat(absolute);
                if (info.isSymbolicLink() || !info.isDirectory() && !info.isFile()) throw new Error(`Skill 不允许链接或特殊文件：${relative}`);
                if (info.isDirectory()) { await walk(absolute); continue; }
                bytes += info.size;
                if (files.length >= this.limits.maxFiles || bytes > this.limits.maxBytes || info.size > this.limits.maxFileBytes) throw new Error('Skill 超过文件数量或大小预算。');
                const content = await fs.readFile(absolute);
                if (content.length !== info.size) throw new Error('Skill 来源在读取过程中发生变化，请重试。');
                files.push({ path: relative, size: content.length, sha256: digest(content), mode: info.mode & 0o111 ? 0o755 : 0o644 });
            }
        };
        await walk(root); return files.sort((a, b) => a.path.localeCompare(b.path));
    }
    private async extractZip(source: string, destination: string): Promise<void> {
        if ((await fs.stat(source)).size > this.limits.maxBytes) throw new Error('Skill ZIP 超过大小预算。');
        const zip = await new Promise<yauzl.ZipFile>((resolve, reject) => yauzl.open(source, { lazyEntries: true, validateEntrySizes: true, strictFileNames: true }, (error, archive) => error || !archive ? reject(error ?? new Error('无法读取 ZIP。')) : resolve(archive)));
        let total = 0; let count = 0; const names = new Set<string>();
        try {
            await new Promise<void>((resolve, reject) => {
                let failed = false;
                const fail = (error: unknown): void => { if (!failed) { failed = true; zip.close(); reject(error); } };
                zip.on('error', fail); zip.on('end', resolve);
                zip.on('entry', (entry: yauzl.Entry) => { void (async () => {
                    const relative = relativeName(entry.fileName);
                    if (names.has(relative.toLowerCase())) throw new Error(`ZIP 包含重复或大小写冲突路径：${relative}`);
                    names.add(relative.toLowerCase());
                    if (names.size > this.limits.maxFiles * 2) throw new Error('Skill ZIP 超过目录或文件数量预算。');
                    const kind = entry.externalFileAttributes >>> 16 & 0o170000;
                    if (kind === 0o120000 || kind !== 0 && kind !== 0o040000 && kind !== 0o100000) throw new Error(`ZIP 不允许链接或特殊文件：${relative}`);
                    const target = path.join(destination, relative);
                    if (entry.fileName.endsWith('/')) { await fs.mkdir(target, { recursive: true }); zip.readEntry(); return; }
                    total += entry.uncompressedSize;
                    if (++count > this.limits.maxFiles || total > this.limits.maxBytes || entry.uncompressedSize > this.limits.maxFileBytes) throw new Error('Skill ZIP 超过解压预算。');
                    if (entry.generalPurposeBitFlag & 1) throw new Error('Skill ZIP 不支持加密条目。');
                    await fs.mkdir(path.dirname(target), { recursive: true });
                    const stream = await new Promise<import('node:stream').Readable>((accept, decline) => zip.openReadStream(entry, (error, value) => error || !value ? decline(error ?? new Error('无法读取 ZIP 条目。')) : accept(value)));
                    const buffers: Buffer[] = []; let length = 0;
                    for await (const chunk of stream) { const buffer = Buffer.from(chunk); length += buffer.length; if (length > entry.uncompressedSize || length > this.limits.maxFileBytes) { stream.destroy(); throw new Error('ZIP 条目长度超过声明或预算。'); } buffers.push(buffer); }
                    if (length !== entry.uncompressedSize) throw new Error('ZIP 条目长度与声明不一致。');
                    await fs.writeFile(target, Buffer.concat(buffers), { mode: entry.externalFileAttributes >>> 16 & 0o111 ? 0o755 : 0o644, flag: 'wx' });
                    if (!failed) zip.readEntry();
                })().catch(fail); });
                zip.readEntry();
            });
        } finally { zip.close(); }
    }
    private async findSkillRoot(unpacked: string): Promise<string> {
        let root = unpacked;
        for (let depth = 0; depth < 8; depth++) {
            if (await exists(path.join(root, 'SKILL.md'))) return root;
            const entries = await fs.readdir(root, { withFileTypes: true });
            if (entries.length !== 1 || !entries[0].isDirectory()) break;
            root = path.join(root, entries[0].name);
        }
        throw new Error('Skill 根目录缺少 SKILL.md；ZIP 应包含一个完整的 ra3-fx-authoring 目录。');
    }
    private async validate(root: string, files: SkillFile[]): Promise<string[]> {
        for (const directory of requiredDirectories) {
            const info = await fs.lstat(path.join(root, directory)).catch(() => undefined);
            if (!info?.isDirectory()) throw new Error(`Skill 缺少 ${directory}/ 目录。`);
            if (!files.some(file => file.path.startsWith(`${directory}/`))) throw new Error(`Skill 的 ${directory}/ 目录为空。`);
        }
        const names = new Set(files.map(file => file.path));
        const skill = await fs.readFile(path.join(root, 'SKILL.md'), 'utf8');
        if (!/^name:\s*['"]?ra3-fx-authoring['"]?\s*$/m.test(skill)) throw new Error('SKILL.md 的 name 必须为 ra3-fx-authoring。');
        for (const script of ['scripts/generate_fx.py', 'scripts/validate_fx.py']) if (!names.has(script)) throw new Error(`Skill 缺少 ${script}。`);
        for (const file of files.filter(file => /\.md$/i.test(file.path))) {
            const text = await fs.readFile(path.join(root, file.path), 'utf8');
            const targets = [...text.matchAll(/!?\[[^\]]*\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["'][^)]*)?\)/g)].map(match => match[1].replace(/^<|>$/g, ''));
            for (const target of targets) {
                if (/^(?:[a-z][a-z0-9+.-]*:|#|\/\/)/i.test(target)) continue;
                let decoded: string;
                try { decoded = decodeURIComponent(target.split(/[?#]/)[0]); } catch { throw new Error(`Skill 链接编码无效：${file.path} → ${target}`); }
                if (!decoded) continue;
                const absolute = path.resolve(path.dirname(path.join(root, file.path)), decoded);
                if (decoded.includes('\\') || !inside(root, absolute)) throw new Error(`Skill 相对引用越出包目录：${file.path} → ${target}`);
                if (!await exists(absolute)) throw new Error(`Skill 相对引用不存在：${file.path} → ${target}`);
            }
        }
        return names.has('PROMPT_FOR_CHAT.md') ? [] : ['未提供 PROMPT_FOR_CHAT.md，复制提示词时使用 SKILL.md。'];
    }
    private async verifySnapshot(snapshot: SkillSnapshot): Promise<void> {
        const actual = await this.inventory(snapshot.rootPath);
        const hash = digest(actual.map(file => `${file.path}\0${file.size}\0${file.sha256}\0${file.mode}`).join('\n'));
        if (`sha256-${hash}` !== snapshot.version || hash !== snapshot.hash || digest(JSON.stringify(actual)) !== digest(JSON.stringify(snapshot.files))) throw new Error('Skill 快照文件已变化，请重新导入。');
    }
    async listInstallations(): Promise<SkillInstallation[]> { return (await this.state()).installations; }
    private async changedFiles(installation: SkillInstallation): Promise<string[]> {
        if (!await exists(installation.destination)) return ['（安装目录已移除）'];
        if ((await fs.lstat(installation.destination)).isSymbolicLink()) return ['（安装目录已替换为链接）'];
        const actual = await this.inventory(installation.destination).catch(() => undefined);
        if (!actual) return ['（安装目录包含链接或超过预算）'];
        const expected = new Map(installation.files.map(file => [file.path, file]));
        const changed = actual.filter(file => { const before = expected.get(file.path); expected.delete(file.path); return !before || before.sha256 !== file.sha256 || before.mode !== file.mode; }).map(file => file.path);
        return [...changed, ...expected.keys()];
    }
    async install(assistantSkillsDirectory: string): Promise<SkillInstallation> {
        return this.serial(async () => {
            const snapshot = await this.current(); if (!snapshot) throw new Error('请先导入 ra3-fx-authoring Skill。');
            await this.verifySnapshot(snapshot);
            const parent = path.resolve(assistantSkillsDirectory); await fs.mkdir(parent, { recursive: true });
            if (!(await fs.lstat(parent)).isDirectory() || (await fs.lstat(parent)).isSymbolicLink()) throw new Error('请选择真实的 AI 助手 Skills 目录。');
            const destination = path.join(parent, skillName);
            if (inside(this.storagePath, destination) || inside(destination, this.storagePath)) throw new Error('安装目标不能与 Skill 存储重叠。');
            const state = await this.state();
            const previous = state.installations.find(item => item.destination === destination);
            if (await exists(destination)) {
                if (!previous) throw new Error('目标已有 Skill，未覆盖其他来源的安装。请选择其他目录。');
                const changes = await this.changedFiles(previous);
                if (changes.length) throw new Error(`已安装 Skill 有本地修改，保留原文件：${changes.slice(0, 10).join('、')}`);
            }
            const stage = await fs.mkdtemp(path.join(parent, '.newsdk-skill-'));
            const backup = path.join(parent, `.newsdk-skill-backup-${randomUUID()}`);
            let backedUp = false;
            try {
                await this.copyTree(snapshot.rootPath, stage);
                if (await exists(destination)) { await fs.rename(destination, backup); backedUp = true; }
                try { await fs.rename(stage, destination); } catch (error) { if (backedUp) await fs.rename(backup, destination); backedUp = false; throw error; }
                const installation: SkillInstallation = { id: previous?.id ?? randomUUID(), destination, version: snapshot.version, installedAt: new Date().toISOString(), files: snapshot.files };
                state.installations = [...state.installations.filter(item => item.destination !== destination), installation];
                await this.saveState(state);
                if (backedUp) await fs.rm(backup, { recursive: true, force: true });
                return installation;
            } finally { await fs.rm(stage, { recursive: true, force: true }); }
        });
    }
    async uninstall(installationId: string): Promise<{ removed: boolean; preserved: string[] }> {
        return this.serial(async () => {
            const state = await this.state(); const installation = state.installations.find(item => item.id === installationId);
            if (!installation) throw new Error('未找到由 NewSdk 管理的本次安装。');
            if (!await exists(installation.destination)) {
                state.installations = state.installations.filter(item => item.id !== installationId); await this.saveState(state);
                return { removed: true, preserved: [] };
            }
            const changed = await this.changedFiles(installation);
            if (changed.length) return { removed: false, preserved: changed };
            await fs.rm(installation.destination, { recursive: true });
            state.installations = state.installations.filter(item => item.id !== installationId); await this.saveState(state);
            return { removed: true, preserved: [] };
        });
    }
}
