import { open, readFile } from 'node:fs/promises';
import type { Stats } from 'node:fs';
import * as path from 'node:path';
import { TextDecoder } from 'node:util';

/** The durable compiler protocol, checked against compiler commit 680c4f0. */
export interface BuildEvent {
  schemaVersion: string;
  buildId: string;
  sequence: number;
  timestampUtc: string;
  elapsedMs: number;
  eventType: string;
  level: string;
  payload?: Record<string, unknown>;
  message?: string;
  diagnosticCode?: string;
  streamId?: string;
  stageId?: string;
}

export interface ProtocolDiagnostic {
  code: string;
  message: string;
  level: string;
  file?: string;
  line?: number;
  column?: number;
  assetType?: string;
  assetId?: string;
  streamId?: string;
  stageId?: string;
}

export interface EffectiveOption { value: string; source: string; }
export type EffectiveOptions = Record<string, EffectiveOption>;

export interface BuildTask {
  id: string;
  content: string;
  inputPath: string;
  outputName?: string;
  reason?: string;
  dependencies: string[];
}

export interface BuildPlan {
  tasks: BuildTask[];
  skipped: Array<{ content: string; status: string; reason: string }>;
  kind?: string;
}

export interface BuildArtifact {
  path: string;
  kind: string;
  size: number;
}

export interface BuildReport {
  schemaVersion: string;
  buildId: string;
  status: string;
  exitCode: number;
  diagnostics: unknown[];
  artifacts: unknown[];
  publicationDirectory?: string | null;
  plan?: unknown;
  executionMode?: string;
  [key: string]: unknown;
}

export interface BuildProgress {
  buildId?: string;
  phase: string;
  activeStream?: string;
  completedStreams: number;
  totalStreams: number;
  elapsedMs: number;
  plan?: BuildPlan;
  diagnostics: ProtocolDiagnostic[];
  events: BuildEvent[];
  effectiveOptions?: EffectiveOptions;
}

export interface BuildResult extends BuildProgress {
  status: 'succeeded' | 'failed' | 'cancelled' | 'interrupted';
  exitCode?: number;
  reason: string;
  artifacts: BuildArtifact[];
  publicationDirectory?: string;
  report?: BuildReport;
  /** File observations only; this does not change the compiler's success verdict. */
  inputState?: 'current' | 'previous-saved' | 'inputs-changed';
}

function object(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown> : undefined;
}

function missing(error: unknown): boolean {
  return (error as NodeJS.ErrnoException).code === 'ENOENT';
}

function identity(value: Stats): string {
  return `${value.dev}:${value.ino}:${value.birthtimeMs}`;
}

/**
 * Capture BEFORE starting the task. The compiler replaces its event file per run;
 * a prefix check also catches truncation followed by regrowth between polls.
 * Buffer whole lines as bytes so a split UTF-8 character never becomes U+FFFD.
 */
export class JsonlTail {
  readonly errors: string[] = [];
  private offset = 0;
  private fileIdentity?: string;
  private prefix = Buffer.alloc(0);
  private pending = Buffer.alloc(0);
  private readonly previousBuildIds = new Set<string>();
  private polling = false;

  private constructor(readonly filePath: string) { }

  static async capture(filePath: string): Promise<JsonlTail> {
    const reader = new JsonlTail(filePath);
    let handle;
    try {
      handle = await open(filePath, 'r');
      const info = await handle.stat();
      reader.offset = info.size;
      reader.fileIdentity = identity(info);
      reader.prefix = Buffer.alloc(Math.min(512, info.size));
      const read = await handle.read(reader.prefix, 0, reader.prefix.length, 0);
      reader.prefix = reader.prefix.subarray(0, read.bytesRead);
      // The compiler writes one build per file, with buildId in its envelope
      // before the potentially large request payload.
      const match = /"buildId"\s*:\s*"([^"\\]+)"/.exec(reader.prefix.toString('utf8'));
      if (match) { reader.previousBuildIds.add(match[1]); }
    } catch (error) {
      if (!missing(error)) { throw error; }
    } finally {
      await handle?.close();
    }
    return reader;
  }

  get hasPartialLine(): boolean { return this.pending.length !== 0; }

  async poll(): Promise<BuildEvent[]> {
    if (this.polling) { throw new Error('JSONL polls must not overlap.'); }
    this.polling = true;
    let handle;
    const events: BuildEvent[] = [];
    try {
      try { handle = await open(this.filePath, 'r'); }
      catch (error) { if (missing(error)) { return []; } throw error; }
      const info = await handle.stat();
      const prefix = Buffer.alloc(Math.min(512, info.size));
      const first = await handle.read(prefix, 0, prefix.length, 0);
      const currentPrefix = prefix.subarray(0, first.bytesRead);
      const sharedLength = Math.min(this.prefix.length, currentPrefix.length);
      if (this.fileIdentity !== identity(info) || info.size < this.offset ||
        !this.prefix.subarray(0, sharedLength).equals(currentPrefix.subarray(0, sharedLength))) {
        this.offset = 0;
        this.pending = Buffer.alloc(0);
      }
      this.fileIdentity = identity(info);
      this.prefix = currentPrefix;
      while (this.offset < info.size) {
        const chunk = Buffer.alloc(Math.min(256 * 1024, info.size - this.offset));
        const read = await handle.read(chunk, 0, chunk.length, this.offset);
        if (read.bytesRead === 0) { break; }
        this.offset += read.bytesRead;
        const bytes = Buffer.concat([this.pending, chunk.subarray(0, read.bytesRead)]);
        let start = 0;
        for (let end = bytes.indexOf(10, start); end !== -1; end = bytes.indexOf(10, start)) {
          if (end - start > 32 * 1024 * 1024) { throw new Error('JSONL event exceeds 32 MiB.'); }
          const line = bytes.subarray(start, end);
          start = end + 1;
          this.parseLine(line, events);
        }
        this.pending = Buffer.from(bytes.subarray(start));
        if (this.pending.length > 32 * 1024 * 1024) { throw new Error('JSONL event exceeds 32 MiB.'); }
      }
      return events.sort((a, b) => a.sequence - b.sequence);
    } finally {
      this.polling = false;
      await handle?.close();
    }
  }

  private parseLine(bytes: Buffer, events: BuildEvent[]): void {
    try {
      const line = new TextDecoder('utf-8', { fatal: true }).decode(bytes).trim();
      if (!line) { return; }
      const value = object(JSON.parse(line));
      if (!value || value.schemaVersion !== '1.0' || typeof value.buildId !== 'string' || !value.buildId ||
        !Number.isSafeInteger(value.sequence) || (value.sequence as number) < 1 ||
        typeof value.eventType !== 'string' || !value.eventType || typeof value.level !== 'string' ||
        typeof value.elapsedMs !== 'number' || !Number.isFinite(value.elapsedMs) || value.elapsedMs < 0 ||
        typeof value.timestampUtc !== 'string') {
        throw new Error('Unsupported or malformed compiler event envelope.');
      }
      if (!this.previousBuildIds.has(value.buildId)) { events.push(value as unknown as BuildEvent); }
    } catch (error) {
      this.errors.push(`无法读取编译事件：${(error as Error).message}`);
    }
  }
}

export async function readBuildReport(filePath: string): Promise<BuildReport | undefined> {
  let text: string;
  try { text = await readFile(filePath, 'utf8'); }
  catch (error) { if (missing(error)) { return undefined; } throw error; }
  const value = object(JSON.parse(text.replace(/^\uFEFF/, '')));
  if (!value || value.schemaVersion !== '1.0' || typeof value.buildId !== 'string' || !value.buildId ||
    typeof value.status !== 'string' || !Number.isSafeInteger(value.exitCode) ||
    !Array.isArray(value.diagnostics) || !Array.isArray(value.artifacts) ||
    !(value.publicationDirectory === undefined || value.publicationDirectory === null || typeof value.publicationDirectory === 'string')) {
    throw new Error('构建报告格式无效或协议版本不受支持。');
  }
  return value as unknown as BuildReport;
}

export function normalizePlan(value: unknown): BuildPlan | undefined {
  const plan = object(value);
  if (!plan || !Array.isArray(plan.Tasks) || !Array.isArray(plan.Skipped)) { return undefined; }
  const tasks: BuildTask[] = [];
  for (const entry of plan.Tasks) {
    const task = object(entry);
    if (!task || typeof task.Id !== 'string' || typeof task.Content !== 'string' || typeof task.InputPath !== 'string') { continue; }
    tasks.push({
      id: task.Id, content: task.Content, inputPath: task.InputPath,
      outputName: typeof task.OutputName === 'string' ? task.OutputName : undefined,
      reason: typeof task.Reason === 'string' ? task.Reason : undefined,
      dependencies: Array.isArray(task.Dependencies) ? task.Dependencies.filter((item): item is string => typeof item === 'string') : [],
    });
  }
  const skipped: BuildPlan['skipped'] = [];
  for (const entry of plan.Skipped) {
    const item = object(entry);
    if (item && typeof item.Content === 'string') {
      skipped.push({ content: item.Content, status: typeof item.Status === 'string' ? item.Status : 'skipped', reason: typeof item.Reason === 'string' ? item.Reason : '' });
    }
  }
  return { tasks, skipped, kind: typeof plan.PlanKind === 'string' ? plan.PlanKind : undefined };
}

export function normalizeDiagnostic(value: Record<string, unknown>): ProtocolDiagnostic | undefined {
  if (typeof value.message !== 'string') { return undefined; }
  const result: ProtocolDiagnostic = {
    code: typeof value.code === 'string' ? value.code : 'COMPILER_DIAGNOSTIC',
    message: value.message,
    level: typeof value.level === 'string' ? value.level : 'info',
  };
  // Old Core `source` strings name a component. Only the optional structured
  // source object can provide fallback location/asset metadata.
  const source = object(value.source);
  const filename = (entry?: Record<string, unknown>): string | undefined =>
    typeof entry?.file === 'string' && entry.file ? entry.file : typeof entry?.filePath === 'string' && entry.filePath ? entry.filePath : undefined;
  const primaryFile = filename(value);
  const sourceFile = filename(source);
  const file = primaryFile ?? sourceFile;
  const location = primaryFile && Number.isSafeInteger(value.line) && (value.line as number) > 0 ? value
    : sourceFile && (!primaryFile || primaryFile === sourceFile) ? source : undefined;
  if (file) result.file = file;
  if (location && Number.isSafeInteger(location.line) && (location.line as number) > 0) {
    result.line = location.line as number;
    if (Number.isSafeInteger(location.column) && (location.column as number) > 0) result.column = location.column as number;
  }
  for (const key of ['assetType', 'assetId', 'streamId', 'stageId'] as const) {
    const entry = value[key] ?? source?.[key];
    if (typeof entry === 'string' && entry) result[key] = entry;
  }
  return result;
}

/** Display the values and origins the compiler actually selected. */
export function normalizeEffectiveOptions(value: unknown): EffectiveOptions | undefined {
  const entries = object(value);
  if (!entries) return undefined;
  const result: EffectiveOptions = {};
  for (const [key, raw] of Object.entries(entries)) {
    const option = object(raw);
    const selected = option?.Value ?? option?.value;
    const source = option?.Source ?? option?.source;
    if (typeof selected === 'string' && typeof source === 'string') result[key] = { value: selected, source };
  }
  return result;
}

function mergeDiagnostics(first: ProtocolDiagnostic[], other: unknown[]): ProtocolDiagnostic[] {
  const values = [...first];
  for (const entry of other) {
    const item = object(entry);
    const parsed = item && normalizeDiagnostic(item);
    if (parsed) { values.push(parsed); }
  }
  const unique = new Map<string, ProtocolDiagnostic>();
  for (const item of values) {
    const key = JSON.stringify([item.level, item.code, item.message, item.file, item.line, item.column, item.assetType, item.assetId, item.streamId, item.stageId]);
    unique.set(key, item);
  }
  return [...unique.values()];
}

function within(directory: string, file: string): boolean {
  const windows = /^[A-Za-z]:[\\/]|^\\\\/.test(directory);
  const api = windows ? path.win32 : path.posix;
  if (!api.isAbsolute(directory) || !api.isAbsolute(file)) { return false; }
  const root = api.resolve(directory);
  const candidate = api.resolve(file);
  const relative = api.relative(windows ? root.toLowerCase() : root, windows ? candidate.toLowerCase() : candidate);
  return relative !== '' && !relative.startsWith(`..${api.sep}`) && relative !== '..' && !api.isAbsolute(relative);
}

export class BuildProtocolState {
  private buildId?: string;
  private readonly events = new Map<number, BuildEvent>();
  private readonly violations = new Set<string>();
  private cachedProgress?: BuildProgress;

  accept(incoming: readonly BuildEvent[]): void {
    const starts = incoming.filter(event => event.eventType === 'BuildStarted').sort((a, b) => a.sequence - b.sequence);
    if (!this.buildId && starts.length) { this.buildId = starts[0].buildId; }
    for (const event of incoming) {
      if (!this.buildId) { continue; }
      if (event.buildId !== this.buildId) {
        this.violations.add('本轮事件包含多个 buildId。');
        continue;
      }
      const previous = this.events.get(event.sequence);
      if (previous && JSON.stringify(previous) !== JSON.stringify(event)) { this.violations.add(`事件序号 ${event.sequence} 的内容冲突。`); }
      else if (!previous) {
        this.events.set(event.sequence, event);
        this.cachedProgress = undefined;
      }
    }
  }

  snapshot(): BuildProgress {
    if (this.cachedProgress) { return this.cachedProgress; }
    const events = [...this.events.values()].sort((a, b) => a.sequence - b.sequence);
    const result: BuildProgress = { buildId: this.buildId, phase: 'preparing', completedStreams: 0, totalStreams: 0, elapsedMs: 0, diagnostics: [], events };
    const completed = new Set<string>();
    for (const event of events) {
      result.elapsedMs = Math.max(result.elapsedMs, event.elapsedMs);
      const payload = object(event.payload);
      result.effectiveOptions = normalizeEffectiveOptions(payload?.effectiveOptions) ?? result.effectiveOptions;
      switch (event.eventType) {
        case 'PlanResolved':
          result.plan = normalizePlan(payload);
          result.totalStreams = result.plan?.tasks.length ?? 0;
          break;
        case 'StageStarted': result.phase = event.stageId ?? 'preparing'; break;
        case 'StreamStarted': result.activeStream = event.streamId; break;
        case 'StreamCompleted':
          if (event.streamId) { completed.add(event.streamId); }
          result.completedStreams = completed.size;
          if (result.activeStream === event.streamId) { result.activeStream = undefined; }
          break;
        case 'Progress':
          if (payload?.unit === 'streams' && Number.isSafeInteger(payload.completed) && Number.isSafeInteger(payload.total) &&
            (payload.completed as number) >= 0 && (payload.total as number) >= (payload.completed as number)) {
            result.completedStreams = payload.completed as number;
            result.totalStreams = payload.total as number;
          }
          break;
        case 'Diagnostic': {
          if (event.level !== 'warning' && event.level !== 'error') { break; }
          const item = normalizeDiagnostic({ ...payload, code: event.diagnosticCode ?? payload?.code,
            message: event.message ?? payload?.message, level: event.level,
            streamId: event.streamId ?? payload?.streamId, stageId: event.stageId ?? payload?.stageId });
          if (item) { result.diagnostics.push(item); }
          break;
        }
        case 'BuildFinished':
          result.phase = typeof payload?.status === 'string' ? payload.status : 'finished';
          result.activeStream = undefined;
          break;
      }
    }
    result.diagnostics = mergeDiagnostics(result.diagnostics, []);
    this.cachedProgress = result;
    return result;
  }

  finish(options: { exitCode: number | undefined; report?: BuildReport; forced?: boolean; protocolError?: string }): BuildResult {
    const progress = this.snapshot();
    const report = options.report;
    const result: BuildResult = { ...progress, status: 'failed', exitCode: options.exitCode, reason: '', artifacts: [] };
    const reject = (status: BuildResult['status'], reason: string): BuildResult => ({ ...result, status, phase: status, reason });
    if (options.forced) { return reject('interrupted', '任务已强制中断；未确认编译器安全取消。'); }
    if (options.protocolError || this.violations.size) { return reject('failed', options.protocolError ?? [...this.violations].join(' ')); }
    if (!progress.buildId) {
      return reject(options.exitCode !== undefined && options.exitCode !== 0 ? 'failed' : 'interrupted', `未收到本轮 BuildStarted；结果不完整${options.exitCode === undefined ? '。' : `（进程退出码 ${options.exitCode}）。`}`);
    }
    const starts = progress.events.filter(event => event.eventType === 'BuildStarted');
    const finishes = progress.events.filter(event => event.eventType === 'BuildFinished');
    if (starts.length !== 1 || starts[0].sequence !== 1) { return reject('failed', '本轮 BuildStarted 缺失、重复或序号无效。'); }
    if (finishes.length !== 1 || !report || options.exitCode === undefined) { return reject('interrupted', '缺少本轮完整终态、最终报告或进程退出码；结果不完整。'); }
    if (report.buildId !== progress.buildId) { return reject('failed', '最终报告 buildId 与本轮事件不一致；拒绝使用旧报告。'); }
    result.report = report;
    result.effectiveOptions = normalizeEffectiveOptions(report.effectiveOptions) ?? result.effectiveOptions;
    result.diagnostics = mergeDiagnostics(result.diagnostics, report.diagnostics);
    result.plan ??= normalizePlan(report.plan);
    const finish = finishes[0];
    const terminal = object(finish.payload);
    if (progress.events.some((event, index) => event.sequence !== index + 1)) {
      return reject('failed', '本轮事件序号不连续，无法确认完整结果。');
    }
    if (finish.sequence !== progress.events.at(-1)?.sequence || terminal?.status !== report.status ||
      terminal?.exitCode !== report.exitCode || report.exitCode !== options.exitCode) {
      return reject('failed', '编译进程、BuildFinished 与最终报告的结果矛盾。');
    }
    if (report.status === 'cancelled' && report.exitCode === 6) { return reject('cancelled', '编译器已在安全边界取消（退出码 6）。'); }
    if (report.status === 'failed' && report.exitCode !== 0) { return reject('failed', `编译失败（退出码 ${report.exitCode}）。`); }
    if (report.status !== 'succeeded' || report.exitCode !== 0) { return reject('failed', '编译器状态与退出码不符合协议。'); }
    const artifacts: BuildArtifact[] = [];
    for (const entry of report.artifacts) {
      const item = object(entry);
      if (!item || typeof item.Path !== 'string' || typeof item.Kind !== 'string' || typeof item.Size !== 'number' ||
        !Number.isFinite(item.Size) || item.Size < 0 || !report.publicationDirectory || !within(report.publicationDirectory, item.Path)) {
        return reject('failed', '报告中的产物格式无效或不属于本轮 publicationDirectory。');
      }
      artifacts.push({ path: item.Path, kind: item.Kind, size: item.Size });
    }
    return { ...result, status: 'succeeded', phase: 'succeeded', reason: '进程、事件与最终报告均确认成功。', artifacts,
      publicationDirectory: report.publicationDirectory ?? undefined };
  }
}
