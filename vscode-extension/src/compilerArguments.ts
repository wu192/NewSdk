import * as path from 'node:path';

export type CompilerOperation = 'doctor' | 'plan' | 'build';
export type CompilerOptionOverrides = Readonly<Record<string, string | number | boolean>>;

export interface RunConfiguration {
    compilerPath: string;
    sdkRoot: string;
    modRoot: string;
    outputDirectory: string;
    modVersion: string;
    /** Only explicitly saved/UI overrides. Preset values stay owned by the compiler. */
    compilerOptions?: CompilerOptionOverrides;
    presetOptions?: Readonly<Record<string, string>>;
}

export interface DiagnosticPaths {
    events: string;
    report: string;
    log: string;
}

/** BIG is the default; only an explicitly known loose configuration can omit a version. */
export function requiresModVersion(configuration: Pick<RunConfiguration, 'compilerOptions' | 'presetOptions'>): boolean {
    const format = configuration.compilerOptions?.['package-format'] ?? configuration.presetOptions?.['package-format'];
    if (format !== undefined) return String(format).toLowerCase() !== 'loose';
    const profile = configuration.compilerOptions?.profile ?? configuration.presetOptions?.profile;
    return String(profile).toLowerCase() !== 'development';
}

/** Separate plan evidence so checking a plan never replaces the previous build report. */
export function diagnosticPaths(operation: 'plan' | 'build', outputDirectory: string): DiagnosticPaths {
    const paths = /^[a-z]:[\\/]|^\\\\/i.test(outputDirectory) ? path.win32 : path;
    return {
        events: paths.join(outputDirectory, `${operation}.events.jsonl`),
        report: paths.join(outputDirectory, `${operation}-report.json`),
        log: paths.join(outputDirectory, `${operation}.log`),
    };
}

/** Return an argv array; these values are never interpreted by a shell. */
export function compilerArguments(operation: CompilerOperation, configuration: RunConfiguration): string[] {
    if (!configuration.sdkRoot.trim()) throw new Error('请选择 SDK 目录。');
    if (operation === 'doctor') return ['doctor', '--sdk-root', configuration.sdkRoot];
    if (!configuration.modRoot.trim()) throw new Error('请选择 MOD 根目录。');
    if (!configuration.outputDirectory.trim()) throw new Error('请选择输出目录。');
    if (requiresModVersion(configuration) && !configuration.modVersion.trim()) throw new Error('请填写 BIG 构建所需的 MOD 版本。');
    const paths = diagnosticPaths(operation, configuration.outputDirectory);
    const args = [
        operation,
        '--mod', configuration.modRoot,
        '--events', paths.events,
        '--report', paths.report,
        '--log', paths.log,
    ];
    if (configuration.modVersion.trim()) args.push('--mod-version', configuration.modVersion.trim());
    for (const [key, value] of [['sdk-root', configuration.sdkRoot], ['out', configuration.outputDirectory]]) {
        if (!samePath(configuration.presetOptions?.[key], value)) args.push(`--${key}`, value);
    }
    args.push(...compilerOptionArguments(configuration.compilerOptions));
    return args;
}

const overrideKeys = new Set(['package-content', 'package-format', 'include-additional', 'fix-neutral-assets',
    'jobs', 'profile', 'performance', 'game-root', 'game-version', 'skudef-name', 'tp', 'ss', 'pc', 'slowclean',
    'sc', 'bc', 'csc', 'psc', 'fsc', 'atc', 'session-cache-directory', 'build-cache-directory', 'iod']);
const booleanKeys = new Set(['include-additional', 'fix-neutral-assets', 'ss', 'pc', 'slowclean', 'sc', 'bc', 'csc', 'psc', 'fsc', 'atc']);

/** Booleans must use --key=value: a separate false token is positional in the compiler CLI. */
export function compilerOptionArguments(options: CompilerOptionOverrides = {}): string[] {
    const args: string[] = [];
    for (const [key, raw] of Object.entries(options)) {
        if (!overrideKeys.has(key)) throw new Error(`不支持的编译覆盖项：${key}`);
        if (!['string', 'number', 'boolean'].includes(typeof raw) || (typeof raw === 'number' && !Number.isFinite(raw))) {
            throw new Error(`编译覆盖项需要有限标量值：${key}`);
        }
        const value = String(raw);
        if (!value.trim() || value.includes('\0')) throw new Error(`编译覆盖项不能为空：${key}`);
        if (booleanKeys.has(key)) {
            if (!/^(true|false)$/i.test(value)) throw new Error(`编译覆盖项 ${key} 需要 true 或 false。`);
            args.push(`--${key}=${value.toLowerCase()}`);
        } else args.push(`--${key}`, value);
    }
    return args;
}

function samePath(first: string | undefined, second: string): boolean {
    if (!first) return false;
    const windows = /^[a-z]:[\\/]|^\\\\/i.test(second);
    const api = windows ? path.win32 : path;
    const normalize = (value: string): string => windows ? api.normalize(value).toLowerCase() : api.normalize(value);
    return normalize(first) === normalize(second);
}

/** Pure lock manager, shared by all runner instances in one extension host. */
export class ProjectRunLocks {
    private readonly held = new Set<string>();

    acquire(identity: string): () => void {
        if (this.held.has(identity)) throw new Error('这个 MOD 已有正在运行的编译器任务。');
        this.held.add(identity);
        let released = false;
        return () => {
            if (!released) {
                released = true;
                this.held.delete(identity);
            }
        };
    }
}

// A per-user OS temporary directory is shared by VS Code windows and profiles.
// Keep the lock outside the MOD, SDK and compiler-managed cache directories.
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import { createHash, randomUUID } from 'node:crypto';

export interface PhysicalProjectLock {
    readonly lockPath: string;
    release(): Promise<void>;
    setProcessId(processId: number | undefined): Promise<void>;
}
export interface PhysicalProjectLockOptions {
    lockDirectory?: string;
    /** An injectable liveness probe keeps stale-owner tests deterministic. */
    probeProcess?: (pid: number) => 'alive' | 'dead' | 'unknown';
}
interface LockOwner { version: 1; pid: number; token: string; identity: string; processId?: number }

export function sharedProjectLockDirectory(): string {
    const user = os.userInfo();
    const identity = typeof process.getuid === 'function' ? `uid:${process.getuid()}`
        : `windows:${user.username.toLowerCase()}:${os.homedir().toLowerCase()}`;
    const key = createHash('sha256').update(identity).digest('hex').slice(0, 20);
    return path.join(os.tmpdir(), `newsdk-mod-tools-${key}`);
}

export function physicalProjectLockPath(identity: string, directory = sharedProjectLockDirectory()): string {
    return path.join(directory, `${createHash('sha256').update(identity).digest('hex')}.lock`);
}

function nodeErrorCode(error: unknown): string | undefined {
    return error instanceof Error && 'code' in error ? String(error.code) : undefined;
}
function processLiveness(pid: number): 'alive' | 'dead' | 'unknown' {
    try { process.kill(pid, 0); return 'alive'; }
    catch (error) { return nodeErrorCode(error) === 'ESRCH' ? 'dead' : 'unknown'; }
}
function parseOwner(text: string, expectedIdentity: string): LockOwner | undefined {
    try {
        const value: unknown = JSON.parse(text);
        if (!value || typeof value !== 'object') return undefined;
        const owner = value as Partial<LockOwner>;
        if (owner.version !== 1 || owner.identity !== expectedIdentity ||
            !Number.isSafeInteger(owner.pid) || owner.pid! <= 0 ||
            (owner.processId !== undefined && (!Number.isSafeInteger(owner.processId) || owner.processId <= 0)) ||
            typeof owner.token !== 'string' || !/^[a-f0-9-]{36}$/i.test(owner.token)) return undefined;
        return owner as LockOwner;
    } catch { return undefined; }
}
async function readOwner(file: string, identity: string): Promise<LockOwner | undefined> {
    try {
        const owner = parseOwner(await fs.readFile(file, 'utf8'), identity);
        if (!owner) throw new Error(`无法确认项目锁的归属。请确认没有窗口运行此 MOD，再删除锁文件后重试：${file}`);
        return owner;
    } catch (error) {
        if (nodeErrorCode(error) === 'ENOENT') return undefined;
        throw error;
    }
}
async function requirePrivateLockDirectory(directory: string): Promise<void> {
    await fs.mkdir(directory, { recursive: true, mode: 0o700 });
    const stat = await fs.lstat(directory);
    if (stat.isSymbolicLink() || !stat.isDirectory()) throw new Error(`项目锁目录必须是当前用户的真实目录：${directory}`);
    if (typeof process.getuid === 'function' && (stat.uid !== process.getuid() || (stat.mode & 0o077) !== 0)) {
        throw new Error(`项目锁目录不属于当前用户或权限过宽，请使用当前用户的私有目录：${directory}`);
    }
}

/**
 * All cooperating windows serialize the short lock-file mutations through a
 * separate gate. A stale owner's cleanup therefore cannot unlink a replacement
 * acquired by another window between the PID check and deletion.
 */
async function withLockGate<T>(lockPath: string, identity: string, action: () => Promise<T>): Promise<T> {
    const gatePath = `${lockPath}.gate`;
    const token = randomUUID();
    let handle: Awaited<ReturnType<typeof fs.open>> | undefined;
    for (let attempt = 0; attempt < 20; attempt++) {
        try { handle = await fs.open(gatePath, 'wx', 0o600); break; }
        catch (error) {
            if (nodeErrorCode(error) !== 'EEXIST') throw error;
            if (attempt === 19) {
                throw new Error(`另一个窗口正在更新项目锁，或上次锁操作已中断。请稍后重试；若问题持续，确认没有窗口运行此 MOD 后删除：${gatePath}`);
            }
            await new Promise(resolve => setTimeout(resolve, 25));
        }
    }
    if (!handle) throw new Error(`无法创建项目锁：${gatePath}`);
    const owner: LockOwner = { version: 1, pid: process.pid, token, identity };
    let recorded = false;
    try {
        await handle.writeFile(JSON.stringify(owner), 'utf8');
        recorded = true;
        await handle.close();
        return await action();
    } finally {
        await handle.close().catch(() => undefined);
        // A failed initial write still belongs to this exclusive open. Once
        // recorded, remove a gate only when its own token remains present.
        if (!recorded) await fs.unlink(gatePath).catch(() => undefined);
        else {
            const current = await readOwner(gatePath, identity);
            if (current?.token === token) await fs.unlink(gatePath);
        }
    }
}

/** Cross-window lock; only an explicitly dead PID can be reclaimed automatically. */
export async function acquirePhysicalProjectLock(identity: string,
    options: PhysicalProjectLockOptions = {}): Promise<PhysicalProjectLock> {
    const directory = options.lockDirectory ?? sharedProjectLockDirectory();
    await requirePrivateLockDirectory(directory);
    const lockPath = physicalProjectLockPath(identity, directory);
    const owner: LockOwner = { version: 1, pid: process.pid, token: randomUUID(), identity };
    const probe = options.probeProcess ?? processLiveness;
    await withLockGate(lockPath, identity, async () => {
        const existing = await readOwner(lockPath, identity);
        if (existing) {
            const state = probe(existing.pid);
            if (state === 'alive') throw new Error(`另一个 VS Code 窗口（进程 ${existing.pid}）正在运行此 MOD，请等待完成或在该窗口停止任务。`);
            if (state !== 'dead') throw new Error(`无法确认进程 ${existing.pid} 是否仍在运行，保留项目锁。请确认没有窗口运行此 MOD 后再删除：${lockPath}`);
            if (existing.processId !== undefined) {
                const processState = probe(existing.processId);
                if (processState !== 'dead') {
                    throw new Error(`上次 VS Code 窗口已退出，但编译器进程 ${existing.processId} 仍在运行或无法确认已退出，保留项目锁：${lockPath}`);
                }
            }
            await fs.unlink(lockPath);
        }
        const file = await fs.open(lockPath, 'wx', 0o600);
        try { await file.writeFile(JSON.stringify(owner), 'utf8'); }
        catch (error) { await fs.unlink(lockPath).catch(() => undefined); throw error; }
        finally { await file.close(); }
    });
    let released = false;
    return {
        lockPath,
        async setProcessId(processId: number | undefined): Promise<void> {
            if (released) throw new Error('项目锁已释放。');
            if (processId !== undefined && (!Number.isSafeInteger(processId) || processId <= 0)) {
                throw new Error('编译器进程 ID 无效。');
            }
            await withLockGate(lockPath, identity, async () => {
                const current = await readOwner(lockPath, identity);
                if (!current || current.token !== owner.token) {
                    throw new Error(`项目锁已由其他任务持有，保留该锁：${lockPath}`);
                }
                const temporary = `${lockPath}.${owner.token}.tmp`;
                try {
                    await fs.writeFile(temporary, JSON.stringify({ ...current, processId }), { flag: 'wx', mode: 0o600 });
                    await fs.rename(temporary, lockPath);
                } finally { await fs.unlink(temporary).catch(() => undefined); }
            });
        },
        async release(): Promise<void> {
            if (released) return;
            await withLockGate(lockPath, identity, async () => {
                const current = await readOwner(lockPath, identity);
                if (current && current.token !== owner.token) {
                    throw new Error(`项目锁已由其他任务持有，保留该锁：${lockPath}`);
                }
                if (current) await fs.unlink(lockPath);
                released = true;
            });
        },
    };
}
