import { strict as assert } from 'node:assert';
import { appendFile, mkdtemp, mkdir, rename, rm, writeFile } from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { BuildEvent, BuildProtocolState, BuildReport, JsonlTail, readBuildReport } from '../src/buildProtocol';

function event(sequence: number, eventType: string, extra: Partial<BuildEvent> = {}): BuildEvent {
  return {
    schemaVersion: '1.0', buildId: 'current', sequence, timestampUtc: '2026-11-01T12:00:00Z',
    elapsedMs: sequence * 10, eventType, level: 'info', ...extra,
  };
}

function report(extra: Partial<BuildReport> = {}): BuildReport {
  return { schemaVersion: '1.0', buildId: 'current', status: 'succeeded', exitCode: 0, diagnostics: [], artifacts: [], ...extra };
}

function completed(extra: Partial<BuildEvent> = {}): BuildProtocolState {
  const state = new BuildProtocolState();
  state.accept([event(1, 'BuildStarted'), event(2, 'BuildFinished', { payload: { status: 'succeeded', exitCode: 0 }, ...extra })]);
  return state;
}

function lines(...events: BuildEvent[]): string {
  return events.map(item => JSON.stringify(item)).join('\r\n') + '\r\n';
}

async function temporary(run: (directory: string) => Promise<void>): Promise<void> {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'newsdk-protocol-'));
  try { await run(directory); }
  finally { await rm(directory, { force: true, recursive: true }); }
}

test('success requires agreeing process, terminal event and current report', () => {
  assert.equal(completed().finish({ exitCode: 0, report: report() }).status, 'succeeded');
  assert.equal(completed().finish({ exitCode: 4, report: report() }).status, 'failed');
  assert.equal(completed().finish({ exitCode: 0, report: report({ buildId: 'old' }) }).status, 'failed');
  assert.equal(completed().finish({ exitCode: 0 }).status, 'interrupted');
  assert.equal(completed().finish({ exitCode: undefined, report: report() }).status, 'interrupted');
  assert.equal(completed().finish({ exitCode: 0, report: report(), protocolError: 'bad JSONL' }).status, 'failed');
  assert.equal(new BuildProtocolState().finish({ exitCode: 0, report: report() }).status, 'interrupted');
  assert.equal(new BuildProtocolState().finish({ exitCode: 3 }).status, 'failed');
});

test('safe cancellation needs explicit cancelled/6; task termination stays interrupted', () => {
  const state = completed({ level: 'error', payload: { status: 'cancelled', exitCode: 6 } });
  assert.equal(state.finish({ exitCode: 6, report: report({ status: 'cancelled', exitCode: 6 }) }).status, 'cancelled');
  assert.equal(state.finish({ exitCode: 6, report: report({ status: 'cancelled', exitCode: 6 }), forced: true }).status, 'interrupted');
  assert.equal(state.finish({ exitCode: 6, report: report() }).status, 'failed');
  assert.equal(completed().finish({ exitCode: 0, report: report(), forced: true }).status, 'interrupted');
});

test('normal failure remains failed and preserves this report diagnostics', () => {
  const state = completed({ level: 'error', payload: { status: 'failed', exitCode: 4 } });
  const result = state.finish({ exitCode: 4, report: report({ status: 'failed', exitCode: 4, diagnostics: [{ code: 'XML_INVALID', message: '错误', level: 'error', source: 'Core' }] }) });
  assert.equal(result.status, 'failed');
  assert.deepEqual(result.diagnostics, [{ code: 'XML_INVALID', message: '错误', level: 'error' }]);
  assert.deepEqual(result.artifacts, []);
});

test('events are deduplicated and ordered and expose only stream progress', () => {
  const start = event(1, 'BuildStarted');
  const diagnostic = event(6, 'Diagnostic', { level: 'warning', diagnosticCode: 'CORE_TRACE_WARNING', message: '警告', payload: { source: 'Core', assets: 42 } });
  const state = new BuildProtocolState();
  state.accept([
    diagnostic,
    event(3, 'StageStarted', { stageId: 'compile' }),
    start, start,
    event(2, 'PlanResolved', { payload: { Tasks: [{ Id: 'apt/Menu', Content: 'apt', InputPath: 'D:\\mod\\Data\\AptUI\\Menu.xml', Dependencies: [] }], Skipped: [{ Content: 'base', Status: 'skipped', Reason: 'not present' }], PlanKind: 'Automatic' } }),
    event(5, 'Progress', { payload: { unit: 'streams', completed: 1, total: 1 } }),
    event(4, 'StreamStarted', { streamId: 'apt/Menu' }),
  ]);
  const progress = state.snapshot();
  assert.deepEqual(progress.events.map(item => item.sequence), [1, 2, 3, 4, 5, 6]);
  assert.equal(progress.phase, 'compile');
  assert.equal(progress.activeStream, 'apt/Menu');
  assert.equal(progress.completedStreams, 1);
  assert.equal(progress.totalStreams, 1);
  assert.equal(progress.plan?.tasks[0].content, 'apt');
  assert.equal(progress.plan?.skipped[0].content, 'base');
  assert.equal(progress.diagnostics[0].file, undefined);
  assert.equal(progress.diagnostics[0].line, undefined);
  assert.equal('percent' in progress, false);
  state.accept([event(7, 'BuildFinished', { payload: { status: 'succeeded', exitCode: 0 } })]);
  assert.equal(state.finish({ exitCode: 0, report: report({ diagnostics: [{ code: 'CORE_TRACE_WARNING', message: '警告', level: 'warning' }] }) }).diagnostics.length, 1);
});

test('only explicit filename and positive source line become located diagnostics', () => {
  const state = new BuildProtocolState();
  state.accept([event(1, 'BuildStarted'), event(2, 'Diagnostic', { level: 'error', message: 'malformed', payload: { filePath: 'D:\\mod\\Data\\Mod.xml', line: 15, column: 3 } })]);
  assert.deepEqual(state.snapshot().diagnostics[0], { code: 'COMPILER_DIAGNOSTIC', message: 'malformed', level: 'error', file: 'D:\\mod\\Data\\Mod.xml', line: 15, column: 3 });
});

test('legacy base coordinates and structured source coordinates agree across event and report', () => {
  const source = { file: 'D:\\mod\\Data\\Weapon.xml', line: 17, column: 5, assetType: 'WeaponTemplate', assetId: 'TankCannon' };
  const state = new BuildProtocolState();
  state.accept([event(1, 'BuildStarted'), event(2, 'Diagnostic', { level: 'error', diagnosticCode: 'XML_INVALID',
    message: 'bad reference', streamId: 'base', stageId: 'compile', payload: { source } }),
    event(3, 'BuildFinished', { payload: { status: 'failed', exitCode: 4 } })]);
  const expected = { code: 'XML_INVALID', message: 'bad reference', level: 'error', ...source, streamId: 'base', stageId: 'compile' };
  assert.deepEqual(state.snapshot().diagnostics[0], expected);
  const result = state.finish({ exitCode: 4, report: report({ status: 'failed', exitCode: 4,
    diagnostics: [{ ...expected, source }], effectiveOptions: { 'package-format': { Value: 'loose', Source: 'project' } } }) });
  assert.equal(result.diagnostics.length, 1);
  assert.deepEqual(result.effectiveOptions, { 'package-format': { value: 'loose', source: 'project' } });
});

test('file-only, asset-only and conflicting source metadata never create fabricated coordinates', () => {
  const state = new BuildProtocolState();
  state.accept([event(1, 'BuildStarted'),
    event(2, 'Diagnostic', { level: 'error', message: 'no line', payload: { source: { file: 'schema.xsd', line: 0, column: 1 } } }),
    event(3, 'Diagnostic', { level: 'error', message: 'asset only', payload: { source: { assetType: 'Texture', assetId: 'Known' } } }),
    event(4, 'Diagnostic', { level: 'error', message: 'different sources', payload: { file: 'first.xml', source: { file: 'second.xml', line: 9, column: 3 } } })]);
  assert.equal(state.snapshot().diagnostics[0].file, 'schema.xsd');
  assert.equal(state.snapshot().diagnostics[0].line, undefined);
  assert.equal(state.snapshot().diagnostics[1].assetId, 'Known');
  assert.equal(state.snapshot().diagnostics[1].file, undefined);
  assert.equal(state.snapshot().diagnostics[2].file, 'first.xml');
  assert.equal(state.snapshot().diagnostics[2].line, undefined);
});

test('conflicting duplicate events, gaps, foreign build IDs and events after terminal fail', () => {
  const duplicate = completed();
  duplicate.accept([event(2, 'BuildFinished', { payload: { status: 'failed', exitCode: 4 } })]);
  assert.equal(duplicate.finish({ exitCode: 0, report: report() }).status, 'failed');
  const gap = new BuildProtocolState();
  gap.accept([event(1, 'BuildStarted'), event(3, 'BuildFinished', { payload: { status: 'succeeded', exitCode: 0 } })]);
  assert.equal(gap.finish({ exitCode: 0, report: report() }).status, 'failed');
  const foreign = completed();
  foreign.accept([event(3, 'StageStarted', { buildId: 'foreign', stageId: 'compile' })]);
  assert.equal(foreign.finish({ exitCode: 0, report: report() }).status, 'failed');
  const after = completed();
  after.accept([event(3, 'Diagnostic', { level: 'error', message: 'late' })]);
  assert.equal(after.finish({ exitCode: 0, report: report() }).status, 'failed');
});

test('only report artifacts inside confirmed publication directory are returned', () => {
  const finalReport = report({ publicationDirectory: 'D:\\out\\published', artifacts: [{ Path: 'd:\\OUT\\published\\MyMod.big', Kind: 'big', Size: 100 }] });
  const result = completed().finish({ exitCode: 0, report: finalReport });
  assert.equal(result.status, 'succeeded');
  assert.deepEqual(result.artifacts, [{ path: 'd:\\OUT\\published\\MyMod.big', kind: 'big', size: 100 }]);
  assert.equal(completed().finish({ exitCode: 0, report: report({ ...finalReport, artifacts: [{ Path: 'D:\\out\\old.big', Kind: 'big', Size: 100 }] }) }).status, 'failed');
  assert.equal(completed().finish({ exitCode: 0, report: report({ artifacts: [{ Path: 'D:\\out\\published\\MyMod.big', Kind: 'big', Size: 100 }] }) }).status, 'failed');
});

test('JSONL tail handles split UTF-8, partial final lines and CRLF without duplicate reads', async () => {
  await temporary(async directory => {
    const file = path.join(directory, 'events.jsonl');
    const tail = await JsonlTail.capture(file);
    const first = Buffer.from(lines(event(1, 'BuildStarted', { message: '准备编译' })));
    const split = first.indexOf(Buffer.from('编')) + 1;
    await writeFile(file, first.subarray(0, split));
    assert.deepEqual(await tail.poll(), []);
    assert.equal(tail.hasPartialLine, true);
    await appendFile(file, first.subarray(split));
    const events = await tail.poll();
    assert.equal(events.length, 1);
    assert.equal(events[0].message, '准备编译');
    assert.equal(tail.hasPartialLine, false);
    assert.deepEqual(await tail.poll(), []);
    assert.deepEqual(tail.errors, []);
    await appendFile(file, '{"unfinished":');
    assert.deepEqual(await tail.poll(), []);
    assert.equal(tail.hasPartialLine, true);
  });
});

test('preexisting successful JSONL/report never certify new startup; replacement is read from zero', async () => {
  await temporary(async directory => {
    const file = path.join(directory, 'events.jsonl');
    const old = lines(event(1, 'BuildStarted', { buildId: 'old' }), event(2, 'BuildFinished', { buildId: 'old', payload: { status: 'succeeded', exitCode: 0 } }));
    await writeFile(file, old);
    const tail = await JsonlTail.capture(file);
    assert.deepEqual(await tail.poll(), []);
    await appendFile(file, lines(event(3, 'Progress', { buildId: 'old', payload: { unit: 'streams', completed: 1, total: 1 } })));
    assert.deepEqual(await tail.poll(), []);
    // The real compiler removes/replaces its file rather than modifying the
    // previous inode; ensure a longer new file also resets the read offset.
    const replacement = path.join(directory, 'replacement');
    await writeFile(replacement, lines(event(1, 'BuildStarted', { message: 'x'.repeat(4096) }), event(2, 'BuildFinished', { payload: { status: 'succeeded', exitCode: 0 } })));
    await rm(file);
    await rename(replacement, file);
    const state = new BuildProtocolState();
    state.accept(await tail.poll());
    assert.equal(state.finish({ exitCode: 0, report: report() }).status, 'succeeded');
  });
});

test('same inode truncate-and-regrow resets via envelope prefix', async () => {
  await temporary(async directory => {
    const file = path.join(directory, 'events.jsonl');
    await writeFile(file, lines(event(1, 'BuildStarted', { buildId: 'old' })));
    const tail = await JsonlTail.capture(file);
    await writeFile(file, lines(event(1, 'BuildStarted', { message: 'longer'.repeat(2000) })));
    assert.equal((await tail.poll())[0].buildId, 'current');
  });
});

test('malformed events and invalid report are explicit protocol failures', async () => {
  await temporary(async directory => {
    const events = path.join(directory, 'events');
    const tail = await JsonlTail.capture(events);
    await writeFile(events, 'broken JSON\n' + JSON.stringify({ eventType: 'BuildStarted', buildId: 'current' }) + '\n');
    assert.deepEqual(await tail.poll(), []);
    assert.equal(tail.errors.length, 2);
    const file = path.join(directory, 'report.json');
    assert.equal(await readBuildReport(file), undefined);
    await writeFile(file, JSON.stringify(report()));
    assert.deepEqual(await readBuildReport(file), report());
    await writeFile(file, '{invalid');
    await assert.rejects(readBuildReport(file));
    await writeFile(file, JSON.stringify({ buildId: 'current', status: 'succeeded', exitCode: 0 }));
    await assert.rejects(readBuildReport(file), /报告格式/);
    await mkdir(path.join(directory, 'folder'));
    await assert.rejects(readBuildReport(path.join(directory, 'folder')));
  });
});
