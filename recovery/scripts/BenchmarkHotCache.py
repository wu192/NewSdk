#!/usr/bin/env python3
"""Bounded L8 fresh-process measurements, without production algorithm changes."""
import argparse
from contextlib import contextmanager
import ctypes
import json
import math
import os
from pathlib import Path
import platform
import shutil
import subprocess
import time

from BenchmarkCompilerCache import MODES, process_metrics, read_json, sha
from HotCacheReport import save
from HotCacheWorkload import prepare, expand, apply_case
from CompilerCachePaths import cache_layout

LIMITS = {'common': (2100, 1920), 'full': (3600, 3360)}
CALIBRATION_TARGET_MS = 10500


class BudgetExhausted(TimeoutError):
    pass


class Budget:
    """All setup/IO counts globally; each process also observes its phase limit."""
    def __init__(self, group, job_start_unix=None, clock=time.perf_counter):
        self.clock = clock
        self.total, self.stop = LIMITS[group]
        self.origin = clock() - (max(0, time.time() - job_start_unix) if job_start_unix else 0)
        self.spent = {}
        self.active = None
        self.section_stop = self.stop

    def elapsed(self):
        return self.clock() - self.origin

    @contextmanager
    def phase(self, name, limit):
        start = self.clock()
        self.active = (name, limit, start)
        try:
            self.check()
            yield
            self.check()
        finally:
            self.spent[name] = self.spent.get(name, 0) + self.clock() - start
            self.active = None

    def remaining(self):
        left = min(self.total, self.stop, self.section_stop) - self.elapsed()
        if self.active:
            name, limit, start = self.active
            left = min(left, limit - self.spent.get(name, 0) - (self.clock() - start))
        return left

    def check(self):
        if self.remaining() <= 0:
            raise BudgetExhausted('Phase/global process-start deadline reached')

    def timeout(self, maximum):
        self.check()
        return min(maximum, self.remaining())


def stream_ids(report):
    plan = report.get('plan') or {}
    streams = plan.get('Tasks', plan.get('tasks', plan.get('streams', plan.get('Streams', []))))
    found = {str(s.get('id', s.get('Id', s.get('streamId', '')))) for s in streams}
    found.update(str(e.get('streamId') or '') for e in (report.get('performance') or {}).get('entries', []))
    return sorted(found - {''})


def admission(row, report, case):
    """Only minimal successful-path checks; never infer detailed reasons in off."""
    cache = report.get('cache', {})
    success = row['exitCode'] == 0 and report.get('exitCode') == 0 and report.get('status') == 'succeeded'
    if not success:
        return 'failed', False, 'Compiler did not complete normally'
    over = row['wallMs'] > 8000 and report.get('elapsedMs', 0) > 8000
    bc, sc, csc = MODES[row['mode']]
    options = report.get('effectiveOptions', {})
    if any(str(options.get(k, {}).get('Value', '')).lower() != str(v).lower()
           for k, v in (('bc', bc), ('sc', sc), ('csc', csc))):
        return 'path_not_exercised', over, 'Effective cache switches differ'
    path = True
    if row['phase'] in ('hot_before', 'hot_after', 'calibration_hot'):
        if sc:
            path = cache.get('documentReuse', 0) > 0 and cache.get('xmlParse', -1) == 0 and cache.get('instanceCompile', -1) == 0
        elif bc:
            # XML/GameObject/Weapon/Armor routes may be ineligible for bc.
            path = cache.get('hits', 0) > 0
    elif row['phase'] == 'modified':
        target = case.get('expectedPath', 'backend')
        if target == 'backend':
            path = cache.get('instanceCompile', 0) >= case.get('minInstanceCompile', 1)
        elif target in ('frontend', 'session'):
            path = cache.get('xmlParse', 0) >= case.get('minXmlParse', 1)
        elif target == 'output':
            deleted = row.get('mutation', {}).get('deletedPaths', [])
            path = bool(deleted) and all(Path(p).is_file() for p in deleted)
        elif target in ('link', 'linked', 'intermediate'):
            deleted = row.get('mutation', {}).get('deletedPaths', [])
            path = bool(deleted) and all(Path(p).is_file() for p in deleted)
        elif target == 'timestamp':
            path = bool(row.get('mutation', {}).get('changedPaths')) and cache.get('documentReuse', 0) > 0
        elif target == 'mode':
            path = True
        maximum = case.get('maxInstanceCompile')
        if maximum is not None:
            path = path and cache.get('instanceCompile', 0) <= maximum
    if not path:
        return 'path_not_exercised', over, 'Minimal cache/recompile/recovery evidence absent'
    if not over:
        return 'under_8s', False, 'wallMs and compiler elapsedMs must each be strictly >8000'
    return 'qualified', True, ''


def tree_metadata(root):
    """Use directory-entry metadata already returned by Win32 enumeration.

    Path.rglob followed by repeated Path.stat calls opens every file again on
    Python 3.12/Windows. No content hashes or per-file trace are needed here.
    """
    files, directories = {}, set()
    pending = [(str(root), '')]
    while pending:
        directory, prefix = pending.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                relative = prefix + entry.name
                if entry.is_dir(follow_symlinks=False):
                    directories.add(relative)
                    pending.append((entry.path, relative + '/'))
                elif entry.is_file(follow_symlinks=False):
                    stamp = entry.stat(follow_symlinks=False)
                    files[relative] = (stamp.st_size, stamp.st_mtime_ns)
    return files, directories


class Snapshot:
    """Replay one immutable warm baseline at original absolute paths.

    BAB stores absolute source/intermediate paths. Each case/repeat exclusively
    owns a full reset of this namespace, avoiding relocation-induced cache misses.
    Copies are outside process timing and inside the active phase/global budget.
    Register every source mutation with mark_changed, including same-stamp writes.
    """
    def __init__(self, mod, destination, store, budget, cache=None):
        self.mod, self.destination, self.store = Path(mod), Path(destination), Path(store)
        self.budget = budget
        self.dirty_source = set()
        self.indices = {}
        self.directories = {}
        self.store.mkdir()
        self.targets = [('source', self.mod), ('output', self.destination)]
        if cache is not None:
            self.targets.append(('cache', Path(cache)))
        for name, source in self.targets:
            budget.check()
            shutil.copytree(source, self.store / name)
            self.indices[name], self.directories[name] = tree_metadata(source)
        budget.check()

    def mark_changed(self, mutation):
        # Equal-length/same-mtime edits cannot be detected by metadata alone.
        self.dirty_source.update(mutation.get('changedPaths', []))

    def restore(self):
        for name, target in self.targets:
            self.budget.check()
            if name == 'source' and not self.dirty_source:
                continue
            baseline = self.indices[name]
            current, directories = tree_metadata(target)
            for relative in current.keys() - baseline.keys():
                (target / relative).unlink()
            for relative in self.directories[name] - directories:
                (target / relative).mkdir(parents=True, exist_ok=True)
            for index, (relative, stamp) in enumerate(baseline.items()):
                if index % 256 == 0:
                    self.budget.check()
                forced = name == 'source' and relative in self.dirty_source
                if forced or current.get(relative) != stamp:
                    path = target / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(self.store / name / relative, path)
            # Empty added search roots can change resolver behavior too.
            for relative in sorted(directories - self.directories[name], key=lambda p: p.count('/'), reverse=True):
                (target / relative).rmdir()
        self.dirty_source.clear()
        self.budget.check()


class Runner:
    def __init__(self, args, prepared, budget, state):
        self.args, self.prepared, self.budget, self.state = args, prepared, budget, state
        self.exe = Path(prepared['runtimeDirectory']) / 'NewModCompiler.exe'
        self.output = Path(args.output)
        self.destination = Path(args.work) / 'active-output'
        self.workload = None

    def sample(self, case, phase, repeat=0, formal=False, performance='off', mode='both-deflate', jobs='auto', mutation=None):
        timeout = self.budget.timeout(case.get('timeoutSeconds', self.args.timeout_seconds))
        label = '%03d-%s-%s-%s' % (len(self.state['samples']), case['id'], repeat, phase)
        evidence = self.output / 'samples' / label
        evidence.mkdir(parents=True)
        bc, sc, csc = MODES[mode]
        layout = cache_layout(self.workload['sdkRoot'], self.workload['modRoot'])
        command = [str(self.exe), 'build', '--sdk-root', str(Path(self.args.repo) / 'SDK'),
                   '--mod', self.workload['modRoot'], '--out', str(self.destination), '--mod-version', '1.0',
                   '--jobs', str(jobs), '--performance', performance,
                   '--bc=' + str(bc).lower(), '--sc=' + str(sc).lower(), '--csc=' + str(csc).lower()]
        # All actual streams, normal BIG/SKUDEF and Additional inputs stay enabled.
        if not sc:
            command += ['--iod', str(layout['root'] / 'i' / label.split('-', 1)[0])]
        mutation = dict(mutation or {})
        command += mutation.get('extraArgs', [])
        mutation['deletedPaths'] = [str(self.destination / p) for p in mutation.get('deletedPaths', [])]
        row = dict(label=label, case=case['id'], phase=phase, repeat=repeat, formal=formal,
                   performance=performance, mode=mode, jobs=str(jobs), status='started', command=command,
                   timeoutSeconds=timeout, mutation=mutation, cacheNamespace=case['id'] + '/' + str(repeat),
                   workloadFingerprint=self.workload['fingerprint'])
        self.state['samples'].append(row)
        (evidence / 'request.json').write_text(json.dumps(row, indent=2), encoding='utf-8')
        save(self.output, self.state)
        # Old reports cannot masquerade as this process's evidence on failure.
        report_path = self.destination / 'build-report.json'
        report_path.unlink(missing_ok=True)
        try:
            with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                timeout = self.budget.timeout(case.get('timeoutSeconds', self.args.timeout_seconds))
                tick = time.perf_counter()
                process = subprocess.Popen(command, cwd=self.exe.parent, stdout=stdout, stderr=stderr)
                try:
                    process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
                    row.update(status='timed_out', wallMs=(time.perf_counter() - tick) * 1000, exitCode=process.returncode)
                    raise BudgetExhausted('Compiler exceeded scenario/phase/global timeout: ' + label)
                row.update(wallMs=(time.perf_counter() - tick) * 1000, exitCode=process.returncode,
                           **process_metrics(process))
            report = read_json(report_path)
            row.update(reportElapsedMs=report.get('elapsedMs'), cache=report.get('cache', {}),
                       streamIds=stream_ids(report), reportPerformance=report.get('performance'),
                       publishedBytes=sum(a['Size'] for a in report.get('artifacts', [])))
            if set(row['streamIds']) != set(mutation.get('expectedStreams', self.workload['streams'])):
                raise ValueError('Actual build plan does not match the workload stream inventory')
            row['status'], row['over8s'], row['error'] = admission(row, report, case)
            row['pathExercised'] = row['status'] in ('qualified', 'under_8s')
            if not formal and phase in ('warmup', 'mode_warmup') and row['exitCode'] == 0 and report.get('status') == 'succeeded':
                row['status'] = 'setup_only'
            if row['status'] in ('failed', 'path_not_exercised'):
                raise ValueError(label + ': ' + row['error'])
            return row
        except Exception as error:
            row.setdefault('error', str(error))
            if row['status'] == 'started':
                row['status'] = 'failed'
            raise
        finally:
            if report_path.exists():
                shutil.copy2(report_path, evidence / 'report.json')
            self.state['elapsedSeconds'] = self.budget.elapsed()
            save(self.output, self.state)
            print(json.dumps({k: row.get(k) for k in ('label', 'status', 'wallMs', 'reportElapsedMs')}, ensure_ascii=False), flush=True)


def calibrate(runner):
    budget, args, state = runner.budget, runner.args, runner.state
    state['calibrationTargetMs'] = CALIBRATION_TARGET_MS
    partitions = args.initial_partitions
    for attempt in range(args.calibration_rounds):
        fixture = Path(args.work) / 'fixture'
        with budget.phase('workload', 420):
            if attempt == 0:
                runner.workload = prepare(Path(args.repo), fixture, partitions)
            else:
                runner.workload = expand(runner.workload, partitions)
            state['workload'] = runner.workload
            state['cases'] = runner.workload['cases']
        case = dict(id='calibration', expectedPath='backend')
        with budget.phase('calibration', 240):
            runner.sample(case, 'warmup')
            hot = [runner.sample(case, 'calibration_hot', repeat=i) for i in (1, 2)]
        minimum = min(min(row['wallMs'], row['reportElapsedMs']) for row in hot)
        state.setdefault('calibration', []).append(dict(attempt=attempt + 1, partitions=partitions,
                fingerprint=runner.workload['fingerprint'], minimumMs=minimum,
                targetReached=10000 <= minimum <= 14000, qualified=all(r['status'] == 'qualified' for r in hot)))
        if minimum > 8000 and (minimum >= 10000 or attempt + 1 == args.calibration_rounds):
            state['calibrated'] = True
            state['actualStreamIds'] = hot[-1]['streamIds']
            state['publishedBytes'] = hot[-1]['publishedBytes']
            return True
        # Measured ratio chooses a new common load; no fixed expansion multiplier.
        partitions = max(partitions + 1, math.ceil(partitions * CALIBRATION_TARGET_MS / max(1, minimum)))
        if partitions > 64:
            break
    state['status'] = 'under_8s'
    state['error'] = 'Finite calibration rounds could not satisfy both >8000ms gates'
    return False


def run_case(runner, case, snapshot, repeats=2, mode='both-deflate', jobs='auto'):
    if case.get('availability') in ('unavailable', 'not_applicable'):
        case['status'] = case['availability']
        case.setdefault('reason', 'No legal runnable fixture/version is available')
        case.setdefault('status', 'unavailable')
        return
    case['status'] = 'running'
    for repeat in range(1, repeats + 1):
        snapshot.restore()
        runner.sample(case, 'hot_before', repeat, True, mode=mode, jobs=jobs)
        mutation = apply_case(runner.workload, case, runner.destination)
        snapshot.mark_changed(mutation)
        runner.budget.check()
        runner.sample(case, 'modified', repeat, True, mode=mode, jobs=jobs, mutation=mutation)
        runner.sample(case, 'hot_after', repeat, True, mode=mode, jobs=jobs,
                      mutation={k: mutation[k] for k in ('extraArgs', 'expectedStreams') if k in mutation})
    rows = [r for r in runner.state['samples'] if r['case'] == case['id'] and r['formal'] and r['performance'] == 'off']
    case['status'] = 'complete' if len(rows) == repeats * 3 and all(r['status'] == 'qualified' for r in rows) else 'partial'


def diagnostics(runner, snapshot, cases, identifiers=('C08', 'C09', 'C12')):
    # Instrumented attribution never contributes to off medians.
    for identifier in identifiers:
        case = next((c for c in cases if c['id'] == identifier and c.get('status') == 'complete'), None)
        if not case:
            continue
        snapshot.restore()
        mutation = apply_case(runner.workload, case, runner.destination)
        snapshot.mark_changed(mutation)
        runner.sample(case, 'modified', performance='detailed', mutation=mutation)


def full_modes(runner, snapshot, cases):
    for case in [c for c in cases if c['id'].startswith('F11-mode-')]:
        mode = case['mode']
        if mode == 'both-deflate':
            baseline = next(c for c in cases if c['id'] == 'C00')
            case.update(status=baseline['status'], reusedCase='C00',
                        reason='复用本次同指纹 C00 的默认模式三次热态，未增加独立样本')
            continue
        snapshot.restore()
        # The baseline already warmed native bc and compressed sc. Only an
        # XML session mode needs one conversion before measuring steady state.
        if MODES[mode][1] and not MODES[mode][2]:
            runner.sample(case, 'mode_warmup', mode=mode)
        row = runner.sample(case, 'hot_before', repeat=1, formal=True, mode=mode)
        case.update(status='complete' if row['status'] == 'qualified' else 'partial',
                    reason='缓存模式吞吐对照 n=1；独立恢复同一负载/缓存基线，不重复无修改三段链')
    # Only one audio scenario, avoiding a size/mode/jobs Cartesian product.
    for case in [c for c in cases if c['id'].startswith('F11-jobs-')]:
        if case['jobs'] == 'auto':
            baseline = next(c for c in cases if c['id'] == 'C05')
            case.update(status=baseline['status'], reusedCase='C05',
                        reason='复用本次 C05 同输入/同模式的 auto 两组三阶段，未增加独立样本')
        else:
            run_case(runner, case, snapshot, repeats=2, jobs=case['jobs'])


def feasibility(cases):
    expanded = [c for c in cases if c['id'].startswith('F') and
                not c['id'].startswith(('F11', 'F12')) and c.get('availability') != 'not_applicable']
    processes = len(expanded) * 2 * 3
    return dict(commonFormalProcesses=75, fullExtensionConditions=len(expanded),
                fullExtensionFormalProcesses=processes, fullExtensionStrictLowerBoundSeconds=processes * 8,
                fullExtensionPhaseSeconds=960, fullExtensionCanFitStrictLowerBound=processes * 8 < 960,
                note='Lower bound excludes setup/snapshots and assumes every real compile is only just above 8s; no coverage/repeats are silently removed')


def execute(args):
    if os.name != 'nt':
        raise ValueError('Actual benchmark execution requires Windows Release; unit checks are portable')
    ctypes.WinDLL('kernel32', use_last_error=True).SetErrorMode(0x0001 | 0x0002 | 0x8000)
    if args.group == 'full' and os.environ.get('HOT_CACHE_FULL_APPROVED') != 'true':
        raise ValueError('Full requires the manual workflow and protected Environment approval')
    for name in ('repo', 'work', 'output', 'preparation'):
        setattr(args, name, str(Path(getattr(args, name)).resolve()))
    output, work = Path(args.output), Path(args.work)
    if output.exists() or work.exists() or output == work or output.is_relative_to(work):
        raise ValueError('Evidence and temporary work must be separate new directories')
    output.mkdir(parents=True)
    work.mkdir(parents=True)
    budget = Budget(args.group, args.job_start_unix)
    prepared = read_json(args.preparation)
    state = dict(schemaVersion=1, status='running', group=args.group, samples=[], cases=[],
                 sourceCommit=prepared['benchmarkSourceCommit'], binarySourceCommit=prepared['binarySourceCommit'],
                 sourceRunId=prepared['sourceRunId'], runId=os.environ.get('GITHUB_RUN_ID'),
                 runAttempt=os.environ.get('GITHUB_RUN_ATTEMPT'), machine=dict(system=platform.platform(), cpu=platform.processor(),
                 runnerImage=os.environ.get('ImageVersion')), limits=dict(totalSeconds=budget.total,
                 stopStartingProcessesSeconds=budget.stop), calibrated=False)
    runner = Runner(args, prepared, budget, state)
    try:
        for item in prepared['runtimeFiles']:
            if sha(Path(prepared['runtimeDirectory']) / item['name']) != item['sha256']:
                raise ValueError('Prepared runtime digest changed: ' + item['name'])
        state['exeSha256'] = sha(runner.exe)
        if not calibrate(runner):
            return 2
        state['cases'] = runner.workload['cases']
        state['feasibility'] = feasibility(state['cases'])
        for case in state['cases']:
            if case.get('availability') in ('unavailable', 'not_applicable'):
                case['status'] = case['availability']
        # Full includes the entire Common and its 35-minute ceiling.
        budget.section_stop = 1920
        with budget.phase('workload', 420):
            snapshot = Snapshot(runner.workload['modRoot'], runner.destination, work / 'baseline', budget,
                                cache=cache_layout(runner.workload['sdkRoot'], runner.workload['modRoot'])['root'])
        common = [c for c in state['cases'] if c['id'].startswith('C')]
        c00 = next(c for c in common if c['id'] == 'C00')
        with budget.phase('common_sampling', 1080):
            for repeat in range(1, 4):
                snapshot.restore()
                runner.sample(c00, 'hot_before', repeat, True)
            c00['status'] = 'complete' if all(r['status'] == 'qualified' for r in state['samples'] if r['case'] == 'C00') else 'partial'
            for case in common:
                if case['id'] != 'C00':
                    run_case(runner, case, snapshot)
        with budget.phase('common_diagnostics', 120):
            diagnostics(runner, snapshot, common)
        common_complete = len(common) == 13 and all(c.get('status') == 'complete' for c in common)
        state['commonStatus'] = 'complete' if common_complete else 'partial'
        if args.group == 'full':
            budget.section_stop = budget.stop
            with budget.phase('full_scenarios', 960):
                for case in list(state['cases']):
                    if case['id'].startswith('F') and not case['id'].startswith(('F11', 'F12')):
                        run_case(runner, case, snapshot)
            with budget.phase('full_modes', 300):
                full_modes(runner, snapshot, state['cases'])
            with budget.phase('full_diagnostics', 120):
                diagnostics(runner, snapshot, state['cases'], ('F03-shared', 'F05', 'F10'))
        required = [c for c in state['cases'] if (args.group == 'full' or c['id'].startswith('C')) and c.get('status') != 'not_applicable']
        state['status'] = 'passed' if common_complete and all(c.get('status') == 'complete' for c in required) else 'partial'
        if any(r.get('formal') and r['status'] == 'under_8s' for r in state['samples']):
            state['status'] = 'under_8s'
        return 0 if state['status'] == 'passed' else 2
    except BudgetExhausted as error:
        state.update(status='budget_exhausted', error=str(error))
        return 2
    except Exception as error:
        state.update(status='partial', error=type(error).__name__ + ': ' + str(error))
        return 2
    finally:
        for case in state['cases']:
            if args.group == 'common' and case['id'].startswith('F'):
                case['status'] = 'not_selected'
                continue
            if case.get('status', 'pending') in ('pending', 'running'):
                case['status'] = 'budget_exhausted' if state['status'] == 'budget_exhausted' else 'not_completed'
        state['elapsedSeconds'] = budget.elapsed()
        state['phaseSeconds'] = budget.spent
        save(output, state)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default='.')
    parser.add_argument('--preparation', required=True)
    parser.add_argument('--work', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--group', choices=LIMITS, default='common')
    parser.add_argument('--job-start-unix', type=float)
    parser.add_argument('--timeout-seconds', type=float, default=120)
    parser.add_argument('--initial-partitions', type=int, choices=range(1, 65), default=1)
    parser.add_argument('--calibration-rounds', type=int, choices=range(1, 5), default=3)
    args = parser.parse_args()
    if args.timeout_seconds <= 0:
        parser.error('--timeout-seconds must be positive')
    raise SystemExit(execute(args))


if __name__ == '__main__':
    main()
