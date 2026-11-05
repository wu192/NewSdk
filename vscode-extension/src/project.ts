import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { compilerOptionArguments, CompilerOptionOverrides } from './compilerArguments';

/** Global VS Code user settings. Project values belong in extension storage. */
export interface GlobalSettings {
    compilerPath?: string;
    sdkRoot?: string;
}

export interface ProjectSettings {
    root: string;
    outputDirectory?: string;
    modVersion?: string;
    compilerOptions?: CompilerOptionOverrides;
}

export interface ResolvedProjectSettings extends ProjectSettings, GlobalSettings {
    identity: string;
    presetOptions: Readonly<Record<string, string>>;
}

export interface ProjectPreset {
    sdkRoot?: string;
    outputDirectory?: string;
    modVersion?: string;
    /** Canonical compiler option names; retained for explaining the preset. */
    options: Readonly<Record<string, string>>;
}

export interface ProjectContents {
    base: boolean;
    apt: boolean;
    globalMetadata: boolean;
    additional: boolean;
}

export interface DiscoveredProject {
    root: string;
    name: string;
    identity: string;
    /** Presence hints only. The compiler plan decides valid and selected inputs. */
    contents: ProjectContents;
}

function filesystemError(error: unknown, code: string): boolean {
    return error instanceof Error && 'code' in error && error.code === code;
}

function checkedPath(value: string): string {
    if (!value || !value.trim() || value.includes('\0')) {
        throw new Error('路径不能为空，也不能包含空字符。');
    }
    return value;
}

/** Resolve junctions/symlinks, including an output directory not created yet. */
export async function canonicalPath(value: string, base = process.cwd()): Promise<string> {
    let candidate = path.resolve(base, checkedPath(value));
    const suffix: string[] = [];
    while (true) {
        try {
            const resolved = await fs.realpath(candidate);
            if (suffix.length > 0 && !(await fs.stat(resolved)).isDirectory()) {
                throw new Error(`路径的父级不是目录：${candidate}`);
            }
            return path.join(resolved, ...suffix);
        } catch (error) {
            if (!filesystemError(error, 'ENOENT')) {
                throw error;
            }
            // A dangling link is not a new directory. Do not silently resolve
            // its parent and later let output creation follow a different path.
            try {
                const entry = await fs.lstat(candidate);
                if (entry.isSymbolicLink()) {
                    throw new Error(`路径包含无效的符号链接：${candidate}`);
                }
            } catch (entryError) {
                if (!filesystemError(entryError, 'ENOENT')) {
                    throw entryError;
                }
            }
            const parent = path.dirname(candidate);
            if (parent === candidate) {
                throw error;
            }
            suffix.unshift(path.basename(candidate));
            candidate = parent;
        }
    }
}

/** Windows paths are compared case-insensitively; aliases use one storage key. */
export function pathIdentity(value: string, platform: NodeJS.Platform = process.platform): string {
    const api = platform === 'win32' ? path.win32 : path.posix;
    let normalized = api.normalize(value);
    const root = api.parse(normalized).root;
    while (normalized.length > root.length && /[\\/]$/.test(normalized)) {
        normalized = normalized.slice(0, -1);
    }
    return platform === 'win32' ? normalized.toLowerCase() : normalized;
}

export async function projectIdentity(root: string): Promise<string> {
    return pathIdentity(await canonicalPath(root));
}

/** True for the directory itself and descendants, never sibling prefix matches. */
export function containsPath(parent: string, child: string, platform: NodeJS.Platform = process.platform): boolean {
    const api = platform === 'win32' ? path.win32 : path.posix;
    const relative = api.relative(pathIdentity(parent, platform), pathIdentity(child, platform));
    return relative === '' || (relative !== '..' && !relative.startsWith(`..${api.sep}`) && !api.isAbsolute(relative));
}

async function requireDirectory(value: string, label: string): Promise<string> {
    const resolved = await canonicalPath(value);
    let stat;
    try {
        stat = await fs.stat(resolved);
    } catch (error) {
        if (filesystemError(error, 'ENOENT')) {
            throw new Error(`${label}目录不存在：${value}`);
        }
        throw error;
    }
    if (!stat.isDirectory()) {
        throw new Error(`${label}必须是目录：${value}`);
    }
    return resolved;
}

/** Reject every physical overlap in either direction, as the compiler does. */
export async function validateOutputDirectory(outputDirectory: string, modRoot: string, sdkRoot: string): Promise<string> {
    const [output, mod, sdk] = await Promise.all([
        canonicalPath(outputDirectory), requireDirectory(modRoot, 'MOD'), requireDirectory(sdkRoot, 'SDK'),
    ]);
    try {
        if (!(await fs.stat(output)).isDirectory()) {
            throw new Error(`输出路径必须是目录：${outputDirectory}`);
        }
    } catch (error) {
        if (!filesystemError(error, 'ENOENT')) {
            throw error;
        }
    }
    for (const [label, input] of [['MOD', mod], ['SDK', sdk]] as const) {
        if (containsPath(input, output) || containsPath(output, input)) {
            throw new Error(`输出目录不能与 ${label} 输入目录重叠：${outputDirectory}`);
        }
    }
    return output;
}

const aliases: Readonly<Record<string, string>> = {
    od: 'out', scd: 'session-cache-directory', scp: 'session-cache-directory',
    bcd: 'build-cache-directory', bcp: 'build-cache-directory',
};
const presetKeys = new Set([
    'jobs', 'profile', 'sdk-root', 'game-root', 'out', 'package-content', 'package-format',
    'include-additional', 'fix-neutral-assets', 'mod-version', 'game-version',
    'skudef-name', 'tp', 'ss', 'pc', 'slowclean', 'sc', 'bc', 'csc', 'psc',
    'fsc', 'atc', 'session-cache-directory', 'build-cache-directory', 'iod',
]);
const presetPathKeys = new Set(['sdk-root', 'game-root', 'out', 'session-cache-directory', 'build-cache-directory', 'iod']);

/** Read the compiler's flat scalar-option format, without writing to the MOD. */
export async function readProjectPreset(root: string): Promise<ProjectPreset> {
    const file = path.join(root, 'newmodcompiler.json');
    let text: string;
    try {
        text = await fs.readFile(file, 'utf8');
    } catch (error) {
        if (filesystemError(error, 'ENOENT')) {
            return { options: {} };
        }
        throw error;
    }
    let raw: unknown;
    try {
        raw = JSON.parse(text.replace(/^\uFEFF/, ''));
    } catch (error) {
        throw new Error(`无法读取 ${file}：${error instanceof Error ? error.message : String(error)}`);
    }
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
        throw new Error(`项目预设必须是 JSON 对象：${file}`);
    }
    const options: Record<string, string> = Object.create(null) as Record<string, string>;
    for (const [name, entry] of Object.entries(raw)) {
        const lower = name.toLowerCase();
        const key = aliases[lower] ?? lower;
        if (!presetKeys.has(key)) {
            throw new Error(`项目预设包含编译器不支持的字段：${name}`);
        }
        if (!['string', 'number', 'boolean'].includes(typeof entry) || (typeof entry === 'number' && !Number.isFinite(entry))) {
            throw new Error(`项目预设字段必须是字符串、数字或布尔值：${name}`);
        }
        let value = String(entry);
        if (!value.trim()) {
            throw new Error(`项目预设字段不能为空：${name}`);
        }
        if (presetPathKeys.has(key)) {
            value = path.resolve(root, checkedPath(value));
        }
        if (options[key] !== undefined && options[key] !== value) {
            throw new Error(`项目预设包含冲突的字段：${name}`);
        }
        options[key] = value;
    }
    return {
        sdkRoot: options['sdk-root'], outputDirectory: options.out,
        modVersion: options['mod-version'], options,
    };
}

export function validateModVersion(value: string): string {
    if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(value) || value.endsWith('.')) {
        throw new Error('MOD 版本须以英文字母或数字开头，只含英文字母、数字、点、下划线或连字符，最多 64 字符且不能以点结尾。');
    }
    return value;
}

/** Explicit extension settings override the read-only compiler preset. */
export async function resolveProjectSettings(root: string, saved?: Partial<ProjectSettings>, globals: GlobalSettings = {},
    explicit: Partial<ProjectSettings> = {}): Promise<ResolvedProjectSettings> {
    const physicalRoot = await requireDirectory(root, 'MOD');
    const preset = await readProjectPreset(physicalRoot);
    const compilerOptions = { ...saved?.compilerOptions, ...explicit.compilerOptions };
    compilerOptionArguments(compilerOptions);
    return {
        root: physicalRoot,
        identity: pathIdentity(physicalRoot),
        compilerPath: globals.compilerPath,
        sdkRoot: globals.sdkRoot ?? preset.sdkRoot,
        outputDirectory: explicit.outputDirectory ?? saved?.outputDirectory ?? preset.outputDirectory,
        modVersion: explicit.modVersion ?? saved?.modVersion ?? preset.modVersion,
        compilerOptions,
        presetOptions: preset.options,
    };
}

async function existsWithType(value: string, directory: boolean): Promise<boolean> {
    try {
        const stat = await fs.stat(value);
        return directory ? stat.isDirectory() : stat.isFile();
    } catch (error) {
        if (filesystemError(error, 'ENOENT') || filesystemError(error, 'ENOTDIR')) {
            return false;
        }
        throw error;
    }
}

export async function discoverProject(root: string): Promise<DiscoveredProject> {
    const physicalRoot = await requireDirectory(root, 'MOD');
    const [base, apt, additional] = await Promise.all([
        existsWithType(path.join(physicalRoot, 'Data', 'Mod.xml'), false),
        existsWithType(path.join(physicalRoot, 'Data', 'AptUI'), true),
        existsWithType(path.join(physicalRoot, 'Additional'), true),
    ]);
    let globalMetadata = false;
    try {
        const entries = await fs.readdir(path.join(physicalRoot, 'Data', 'AdditionalMaps'), { withFileTypes: true });
        globalMetadata = entries.some(entry => entry.isFile() && /^MapMetaData_.*\.xml$/i.test(entry.name));
    } catch (error) {
        if (!filesystemError(error, 'ENOENT') && !filesystemError(error, 'ENOTDIR')) {
            throw error;
        }
    }
    return {
        root: physicalRoot, name: path.basename(physicalRoot), identity: pathIdentity(physicalRoot),
        contents: { base, apt, globalMetadata, additional },
    };
}
