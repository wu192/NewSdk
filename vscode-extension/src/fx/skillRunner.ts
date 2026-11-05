import { spawn } from 'node:child_process';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import type { FxValidationLevel, FxValidationReport } from './skillIntent';
import { localAuthoringChecks } from './authoring';
import { SchemaAdapter } from './schemaAdapter';

export interface SkillRunnerOptions {
    pythonPath?: string;
    timeoutMs?: number;
    maxOutputBytes?: number;
    temporaryDirectory?: string;
    /** Derived XSD graphs only; never the SDK or MOD directory. */
    schemaStorageDirectory?: string;
}

export interface SkillRunResult {
    xml: string;
    outputDirectory: string;
    xmlPath: string;
    files: { path: string; relativePath: string }[];
    intent?: unknown;
    diff?: string;
    stdout?: string;
    stderr?: string;
}

export interface SkillGenerateOptions {
    description?: string;
    intent?: unknown;
    recipe?: 'impact' | 'smoke';
    name?: string;
}

export interface SkillEditOptions {
    xml: string;
    templateId: string;
    settings: Record<string, string | number>;
}

export interface SkillValidateOptions {
    xml?: string;
    xmlPath?: string;
    /** Keep a draft's original project-relative directory without copying the project. */
    sourceRelativePath?: string;
    projectRoot?: string;
    assetIndexPath?: string;
    schemaPath?: string;
}

export interface SkillRawDiagnostic {
    level: 'V0' | 'V1' | 'V2' | 'V3';
    severity: string;
    code: string;
    file: string;
    path: string;
    message: string;
    line?: number;
    column?: number;
}

export interface SkillRawValidationReport {
    format_version: number;
    levels: Record<'V0' | 'V1' | 'V2' | 'V3', 'PASS' | 'FAIL' | 'UNKNOWN' | 'SKIPPED'>;
    diagnostics: SkillRawDiagnostic[];
    resources: Record<string, unknown>[];
    checked_files?: string[];
    schema_profile?: string | null;
    schema_scope?: string;
    field_check_scope?: string;
    scope?: string;
}

export interface SkillValidationReport extends FxValidationReport {
    raw: SkillRawValidationReport;
}

export class SkillExecutionError extends Error {
    constructor(message: string, readonly exitCode?: number, readonly stderr?: string) {
        super(message);
        this.name = 'SkillExecutionError';
    }
}

const MAX_XML_BYTES = 8 * 1024 * 1024;
const SETTING_NAMES = new Set(['size', 'count', 'lifetime', 'speed', 'color', 'angle-degrees', 'angular-rate-degrees-per-second']);
const LEVELS = ['V0', 'V1', 'V2', 'V3'] as const;
const STATUSES = new Set(['PASS', 'FAIL', 'UNKNOWN', 'SKIPPED']);

function within(root: string, candidate: string): boolean {
    const relative = path.relative(root, candidate);
    return relative === '' || (!path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`));
}

function jsonObject(text: string, label: string): Record<string, unknown> {
    try {
        const value: unknown = JSON.parse(text);
        if (value && typeof value === 'object' && !Array.isArray(value)) { return value as Record<string, unknown>; }
    } catch { /* reported below without echoing a potentially large private payload */ }
    throw new SkillExecutionError(`${label} 未返回有效 JSON 对象。`);
}

/** Calls only a user-imported Skill on an explicit authoring/check action. Never writes a MOD file. */
export class SkillRunner {
    private readonly pythonPath: string;
    private readonly timeoutMs: number;
    private readonly maxOutputBytes: number;
    private readonly temporaryDirectory: string;
    private readonly schemaStorageDirectory?: string;
    private schemaAdapter?: Promise<SchemaAdapter>;
    private readonly helpCache = new Set<string>();
    private readonly directories = new Set<string>();

    constructor(options: SkillRunnerOptions = {}) {
        this.pythonPath = options.pythonPath || (process.platform === 'win32' ? 'python' : 'python3');
        this.timeoutMs = Math.max(100, Math.min(options.timeoutMs ?? 60_000, 300_000));
        this.maxOutputBytes = Math.max(1024, Math.min(options.maxOutputBytes ?? 4 * 1024 * 1024, 32 * 1024 * 1024));
        this.temporaryDirectory = options.temporaryDirectory || os.tmpdir();
        this.schemaStorageDirectory = options.schemaStorageDirectory;
    }

    async generate(skillDirectory: string, options: SkillGenerateOptions): Promise<SkillRunResult> {
        if (!options.description && options.intent === undefined) { throw new Error('生成需要描述或结构化意图。'); }
        if (options.description && Buffer.byteLength(options.description) > 128 * 1024) { throw new Error('描述过长。'); }
        const recipe = options.recipe;
        if (recipe !== undefined && recipe !== 'impact' && recipe !== 'smoke') { throw new Error('自动配方仅支持 impact / smoke。'); }
        const name = options.name || 'Custom';
        if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(name) || name.length > 128) { throw new Error('资产前缀需要字母、数字或下划线。'); }
        const script = await this.script(skillDirectory, 'generate_fx.py');
        await this.ensureHelp(script, ['generate'], ['--description', '--intent', '--recipe', '--name', '--output']);
        const directory = await this.stage();
        try {
            const output = path.join(directory, 'result.xml');
            const args = [script.file, 'generate', '--name', name, '--output', output];
            if (options.description) { args.push('--description', options.description); }
            if (recipe) { args.push('--recipe', recipe); }
            if (options.intent !== undefined) {
                const intent = JSON.stringify(options.intent);
                if (intent === undefined || Buffer.byteLength(intent) > 1024 * 1024) { throw new Error('结构化意图过大或无法序列化。'); }
                jsonObject(intent, '结构化意图');
                const intentFile = path.join(directory, 'input.intent.json');
                await fs.writeFile(intentFile, intent, { flag: 'wx' });
                args.push('--intent', intentFile);
            }
            const execution = await this.run(script.directory, args);
            this.success(execution);
            const xml = await this.readFile(directory, 'result.xml');
            const metadata = jsonObject(await this.readFile(directory, 'result.intent.json'), '生成意图');
            const files: SkillRunResult['files'] = [];
            const payloads = metadata.resource_files_copied;
            if (payloads !== undefined && !Array.isArray(payloads)) { throw new Error('Skill 素材清单格式无效。'); }
            if (Array.isArray(payloads) && payloads.length > 64) { throw new Error('Skill 新增素材超过单次上限。'); }
            let total = 0;
            for (const relative of payloads || []) {
                if (typeof relative !== 'string') { throw new Error('Skill 素材相对路径无效。'); }
                const file = await this.stagedPath(directory, relative);
                const stat = await fs.stat(file);
                total += stat.size;
                if (!stat.isFile() || stat.size > MAX_XML_BYTES || total > 32 * 1024 * 1024) { throw new Error('Skill 素材大小超过单次上限。'); }
                files.push({ path: file, relativePath: relative.replace(/\\/g, '/') });
            }
            return { xml, xmlPath: output, outputDirectory: directory, files, intent: metadata, stdout: execution.stdout, stderr: execution.stderr };
        } catch (error) { await this.removeStage(directory); throw error; }
    }

    async edit(skillDirectory: string, options: SkillEditOptions): Promise<SkillRunResult> {
        this.checkXmlSize(options.xml);
        if (!/^[\w.-]+$/.test(options.templateId) || options.templateId.length > 256) { throw new Error('需要唯一模板 ID。'); }
        const entries = Object.entries(options.settings);
        if (!entries.length || entries.some(([key, value]) => !SETTING_NAMES.has(key) || !['number', 'string'].includes(typeof value) || String(value).length > 256)) {
            throw new Error('修改只接受受控属性和值。');
        }
        const script = await this.script(skillDirectory, 'generate_fx.py');
        await this.ensureHelp(script, ['edit'], ['--template', '--set', '--output']);
        const directory = await this.stage();
        try {
            const input = path.join(directory, 'input.xml');
            const output = path.join(directory, 'result.xml');
            await fs.writeFile(input, options.xml, { flag: 'wx' });
            const args = [script.file, 'edit', input, '--template', options.templateId, '--output', output];
            for (const [key, value] of entries) { args.push('--set', `${key}=${value}`); }
            const execution = await this.run(script.directory, args);
            this.success(execution);
            const xml = await this.readFile(directory, 'result.xml');
            const diff = await this.readFile(directory, 'result.diff');
            return { xml, xmlPath: output, outputDirectory: directory, files: [], diff, stdout: execution.stdout, stderr: execution.stderr };
        } catch (error) { await this.removeStage(directory); throw error; }
    }

    async validate(skillDirectory: string, options: SkillValidateOptions): Promise<SkillValidationReport> {
        if ((options.xml === undefined) === (options.xmlPath === undefined)) { throw new Error('检查需要 XML 文本或文件路径之一。'); }
        if (options.sourceRelativePath !== undefined && options.xml === undefined) { throw new Error('暂存相对位置仅适用于 XML 文本。'); }
        const relative = options.sourceRelativePath?.replace(/\\/g, '/') || 'input.xml';
        if (typeof relative !== 'string' || relative.length > 4096 || relative.includes('\0') || path.isAbsolute(relative) || /^[A-Za-z]:/.test(relative) || relative.split('/').some(part => !part || part === '.' || part === '..')) {
            throw new Error('XML 暂存位置需要安全的项目相对路径。');
        }
        if (options.xml !== undefined) { this.checkXmlSize(options.xml); }
        const script = await this.script(skillDirectory, 'validate_fx.py');
        await this.ensureHelp(script, [], ['--project-root', '--asset-index', '--schema', '--json']);
        let directory: string | undefined;
        try {
            let file = options.xmlPath;
            if (options.xml !== undefined) {
                directory = await this.stage();
                file = path.join(directory, relative);
                await fs.mkdir(path.dirname(file), { recursive: true });
                await fs.writeFile(file, options.xml, { flag: 'wx' });
            }
            file = path.resolve(file!);
            const args = [script.file, file, '--json', '--project-root', path.resolve(options.projectRoot || directory || path.dirname(file))];
            if (options.assetIndexPath) { args.push('--asset-index', path.resolve(options.assetIndexPath)); }
            let prepared: Awaited<ReturnType<SchemaAdapter['prepare']>> | undefined;
            let schemaIssue: string | undefined;
            if (options.schemaPath) {
                try {
                    prepared = await this.prepareSchema(options.schemaPath);
                    // This named wrapper retains the Skill's provenance verification.
                    // Ordinary local/SDK schemas use the public checker on all platforms.
                    if (!prepared.normalized) args.push('--schema', prepared.entryPath);
                }
                catch (error) { schemaIssue = error instanceof Error ? error.message : String(error); }
            }
            const execution = await this.run(script.directory, args);
            // Exit 1 is the checker's normal FAIL report, not a process/JSON failure.
            if (execution.code !== 0 && execution.code !== 1) { this.success(execution); }
            const raw = this.parseReport(execution.stdout);
            if (execution.code === 1 && !Object.values(raw.levels).includes('FAIL') && raw.levels.V3 !== 'UNKNOWN') {
                throw new SkillExecutionError('检查器退出状态与报告不一致。', execution.code);
            }
            if (prepared?.normalized) {
                const v2 = await this.checkPreparedSchema(file, prepared);
                raw.levels.V2 = v2.status;
                raw.schema_profile = 'local-xsd';
                raw.schema_scope = v2.summary;
                raw.diagnostics = raw.diagnostics.filter(issue => issue.level !== 'V2');
                raw.diagnostics.push(...(v2.diagnostics ?? []).map(issue => ({level: 'V2' as const, severity: v2.status === 'FAIL' ? 'error' : 'unknown',
                    code: v2.status === 'FAIL' ? 'XSD_INVALID' : 'XSD_UNAVAILABLE', file: options.xmlPath || options.sourceRelativePath || 'input.xml', path: '/', ...issue})));
            }
            if (schemaIssue) {
                raw.levels.V2 = 'UNKNOWN';
                raw.schema_scope = '所选 XSD 加载失败；未执行结构与类型校验。';
                raw.diagnostics = raw.diagnostics.filter(issue => issue.level !== 'V2');
                raw.diagnostics.push({level: 'V2', severity: 'unknown', code: 'XSD_UNAVAILABLE', file: options.schemaPath!, path: '/', message: schemaIssue});
            }
            return this.normalizeReport(raw, prepared?.sourcePath ?? options.schemaPath, prepared?.version);
        } finally { if (directory) { await this.removeStage(directory); } }
    }

    /** V2 also works without a private Skill; V0/V3 remain explicitly unknown. */
    async validateSchema(options: SkillValidateOptions): Promise<FxValidationReport> {
        if ((options.xml === undefined) === (options.xmlPath === undefined)) throw new Error('检查需要 XML 文本或文件路径之一。');
        let xml = options.xml;
        if (xml === undefined) {
            const stat = await fs.stat(options.xmlPath!);
            if (!stat.isFile() || stat.size > MAX_XML_BYTES) throw new Error('XML 超过 8 MiB 输入上限。');
            xml = await fs.readFile(options.xmlPath!, 'utf8');
        }
        this.checkXmlSize(xml);
        const report = localAuthoringChecks(xml);
        if (!options.schemaPath) return report;
        report.schema = path.resolve(options.schemaPath);
        let directory: string | undefined;
        let level: FxValidationLevel;
        try {
            const prepared = await this.prepareSchema(options.schemaPath);
            report.schema = prepared.sourcePath; report.schemaVersion = prepared.version;
            directory = await this.stage();
            // Use the captured bytes even for saved documents; never validate a later disk version.
            const input = path.join(directory, 'input.xml');
            await fs.writeFile(input, xml, {flag: 'wx'});
            level = await this.checkPreparedSchema(input, prepared);
        } catch (error) {
            level = {level: 'V2', status: 'UNKNOWN', summary: 'XSD 检查未能执行；请查看加载诊断。', rules: [`入口：${report.schema}`],
                diagnostics: [{message: error instanceof Error ? error.message : String(error)}]};
        } finally { if (directory) await this.removeStage(directory); }
        report.levels = report.levels.map(previous => previous.level === 'V2' ? level : previous);
        return report;
    }

    private async checkPreparedSchema(input: string, prepared: Awaited<ReturnType<SchemaAdapter['prepare']>>): Promise<FxValidationLevel> {
        try {
            const execution = await this.run(__dirname, [path.join(__dirname, 'check-xsd.py'), '--schema', prepared.entryPath, '--xml', input, '--json']);
            const data = jsonObject(execution.stdout, 'XSD 检查');
            const status = data.status;
            const expectedExit = status === 'PASS' ? 0 : status === 'FAIL' ? 1 : status === 'UNKNOWN' ? 2 : -1;
            if (execution.code !== expectedExit || expectedExit < 0 || !Array.isArray(data.diagnostics)
                || data.diagnostics.length > 64 || data.diagnostics.some(item => !item || typeof item !== 'object' || typeof item.message !== 'string')) {
                throw new SkillExecutionError('XSD 检查进程状态与报告不一致。', execution.code);
            }
            return {level: 'V2', status: status as 'PASS' | 'FAIL' | 'UNKNOWN', summary: status === 'UNKNOWN'
                ? '所选 XSD 尚未完成加载或校验；请查看诊断。'
                : '当前 XML 文档按所选完整 XSD 依赖图执行结构与类型校验；Include 合并、宏求值仍由编译器确认。',
                rules: ['lxml XMLSchema', `入口：${prepared.sourcePath}`, `版本：${prepared.version}`],
                diagnostics: (data.diagnostics as {message: string; line?: number; column?: number}[]).map(issue => ({message: issue.message,
                    ...(Number.isSafeInteger(issue.line) && issue.line! > 0 ? {line: issue.line} : {}),
                    ...(Number.isSafeInteger(issue.column) && issue.column! >= 0 ? {column: issue.column} : {})}))};
        } catch (error) {
            return {level: 'V2', status: 'UNKNOWN', summary: 'XSD 检查未能执行；请查看加载诊断。', rules: [`入口：${prepared.sourcePath}`, `版本：${prepared.version}`],
                diagnostics: [{message: error instanceof Error ? error.message : String(error)}]};
        }
    }

    private async prepareSchema(entryPath: string): Promise<Awaited<ReturnType<SchemaAdapter['prepare']>>> {
        this.schemaAdapter ??= (async () => new SchemaAdapter(this.schemaStorageDirectory || await this.stage()))();
        return (await this.schemaAdapter).prepare(entryPath);
    }

    async disposeResult(result: Pick<SkillRunResult, 'outputDirectory'>): Promise<void> { await this.removeStage(result.outputDirectory); }

    async dispose(): Promise<void> { await Promise.all([...this.directories].map(directory => this.removeStage(directory))); }

    private checkXmlSize(xml: string): void {
        if (typeof xml !== 'string' || Buffer.byteLength(xml, 'utf8') > MAX_XML_BYTES) { throw new Error('XML 超过 8 MiB 输入上限。'); }
    }

    private async script(directory: string, name: string): Promise<{ directory: string; file: string; stamp: string }> {
        const root = await fs.realpath(directory);
        const requested = path.join(root, 'scripts', name);
        const file = await fs.realpath(requested);
        if (!within(root, file)) { throw new Error('Skill 脚本越过导入目录。'); }
        const stat = await fs.stat(file);
        if (!stat.isFile() || stat.size > 2 * 1024 * 1024) { throw new Error('Skill 脚本无效。'); }
        return { directory: root, file, stamp: `${file}:${stat.size}:${stat.mtimeMs}` };
    }

    private async ensureHelp(script: { directory: string; file: string; stamp: string }, command: string[], flags: string[]): Promise<void> {
        const key = `${script.stamp}:${command.join(' ')}`;
        if (this.helpCache.has(key)) { return; }
        const result = await this.run(script.directory, [script.file, ...command, '--help']);
        this.success(result);
        const help = result.stdout + result.stderr;
        if (flags.some(flag => !help.includes(flag))) { throw new SkillExecutionError('导入 Skill 的 --help 参数与本扩展支持的接口不一致，请更新 Skill 或扩展。'); }
        this.helpCache.add(key);
    }

    private async stage(): Promise<string> {
        await fs.mkdir(this.temporaryDirectory, { recursive: true });
        const directory = await fs.mkdtemp(path.join(this.temporaryDirectory, 'newsdk-fx-skill-'));
        this.directories.add(directory);
        return directory;
    }

    private async stagedPath(directory: string, relative: string): Promise<string> {
        if (!relative || path.isAbsolute(relative) || /^[A-Za-z]:/.test(relative) || relative.includes('\0')) { throw new Error('Skill 输出必须使用临时区相对路径。'); }
        const candidate = path.resolve(directory, relative.replace(/\\/g, '/'));
        if (!within(directory, candidate)) { throw new Error('Skill 输出路径越过临时区。'); }
        const real = await fs.realpath(candidate);
        if (!within(await fs.realpath(directory), real)) { throw new Error('Skill 输出符号链接越过临时区。'); }
        return real;
    }

    private async readFile(directory: string, relative: string): Promise<string> {
        const file = await this.stagedPath(directory, relative);
        const stat = await fs.stat(file);
        if (!stat.isFile() || stat.size > MAX_XML_BYTES) { throw new Error('Skill 输出超过 8 MiB 上限。'); }
        return fs.readFile(file, 'utf8');
    }

    private async removeStage(directory: string): Promise<void> {
        if (!this.directories.has(directory)) { return; }
        this.directories.delete(directory);
        await fs.rm(directory, { recursive: true, force: true });
    }

    private success(result: { code: number; stderr: string }): void {
        if (result.code !== 0) {
            const detail = result.stderr.slice(-4000).trim();
            throw new SkillExecutionError(`Skill 脚本执行失败（退出码 ${result.code}）${detail ? `：${detail}` : ''}`, result.code, detail);
        }
    }

    private run(cwd: string, args: string[]): Promise<{ code: number; stdout: string; stderr: string }> {
        return new Promise((resolve, reject) => {
            const child = spawn(this.pythonPath, args, { cwd, shell: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
            const stdout: Buffer[] = [];
            const stderr: Buffer[] = [];
            let length = 0;
            let failure: Error | undefined;
            const timer = setTimeout(() => { failure = new SkillExecutionError('Skill 脚本执行超时，已终止。'); child.kill('SIGKILL'); }, this.timeoutMs);
            const collect = (chunks: Buffer[]) => (chunk: Buffer) => {
                length += chunk.length;
                if (length > this.maxOutputBytes) {
                    failure = new SkillExecutionError('Skill 脚本输出超过上限，已终止。');
                    child.kill('SIGKILL');
                } else { chunks.push(chunk); }
            };
            child.stdout.on('data', collect(stdout));
            child.stderr.on('data', collect(stderr));
            child.on('error', error => { clearTimeout(timer); reject(new SkillExecutionError(`无法启动 Python 3：${error.message}`)); });
            child.on('close', code => {
                clearTimeout(timer);
                if (failure) { reject(failure); return; }
                resolve({ code: code ?? -1, stdout: Buffer.concat(stdout).toString('utf8'), stderr: Buffer.concat(stderr).toString('utf8') });
            });
        });
    }

    private parseReport(text: string): SkillRawValidationReport {
        const report = jsonObject(text, '静态检查');
        const levels = report.levels as Record<string, unknown> | undefined;
        if (report.format_version !== 1 || !levels || LEVELS.some(level => !STATUSES.has(String(levels[level]))) || !Array.isArray(report.diagnostics) || !Array.isArray(report.resources)) {
            throw new SkillExecutionError('Skill 检查报告版本或 V0–V3 状态无效。');
        }
        if (report.diagnostics.some(item => !item || typeof item !== 'object' || !LEVELS.includes(item.level) || typeof item.message !== 'string')) {
            throw new SkillExecutionError('Skill 诊断格式无效。');
        }
        return report as unknown as SkillRawValidationReport;
    }

    private normalizeReport(raw: SkillRawValidationReport, schema?: string, schemaVersion?: string): SkillValidationReport {
        const coverage = {
            V0: raw.field_check_scope || 'Skill 已覆盖的字段规则；不代表完整 Schema 检查。',
            V1: '实际 XML 解析、根元素与命名空间检查。',
            V2: raw.schema_scope || (schema ? '仅指定的本地 XSD。' : '未提供 XSD，跳过。'),
            V3: `指定文件集合中的声明、Include 与素材检查（${raw.resources.length} 项）；未知依赖继续保留。`,
        };
        return {
            levels: LEVELS.map(level => ({
                level, status: raw.levels[level], summary: coverage[level], rules: [coverage[level], ...(level === 'V2' && schema ? [`入口：${schema}`, ...(schemaVersion ? [`版本：${schemaVersion}`] : [])] : [])],
                diagnostics: raw.diagnostics.filter(d => d.level === level).map(d => ({ message: `${d.severity} ${d.code || ''} ${d.file || ''} ${d.path || ''}: ${d.message}`.trim(),
                    ...(d.line ? {line: d.line} : {}), ...(d.column === undefined ? {} : {column: d.column}) })),
            })),
            schema, schemaVersion, checkedAt: new Date().toISOString(), raw,
        };
    }
}
