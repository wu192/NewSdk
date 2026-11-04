#!/usr/bin/env python3
"""Paired fresh-process original/new SDK timings against identical shared inputs."""
import argparse
import csv
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import statistics
import subprocess
import time

from CompilerCachePaths import cache_root

from BenchmarkCompilerCache import (MODES, cache_eligibility, inventory,
    output_inventory, process_metrics, read_json, require, validate_sample)


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def root_arguments(roots):
    return ['/dr:' + roots['dataRoot']] + [
        '/' + name + ':' + ';'.join(roots[name + 'Paths']) for name in ('data', 'art', 'audio')]


def cache_profile(engine, workload, profile, prepared):
    if engine != 'new':
        return profile
    return cache_root(prepared['sdkRoot'], workload['modRoot']) / 'validation' / ('paired-' + hashlib.sha256(str(profile).encode()).hexdigest()[:12])


def sdk_source_inventory(sdk, workloads):
    # Only the tested MOD partitions are writable. Keep other MODs' partitions
    # in the snapshot, along with every ordinary SDK input.
    base = Path(sdk).resolve()
    partitions = tuple(cache_root(base, row['modRoot']).relative_to(base).as_posix().casefold() + '/'
                       for row in workloads)
    return {name: value for name, value in inventory(sdk).items()
            if not name.replace('\\', '/').casefold().startswith(partitions)}


def invocation(engine, workload, mode, profile, round_index, prepared, original):
    bc, sc, compressed = MODES[mode]
    flags = ['/bc:' + str(bc).lower(), '/sc:' + str(sc).lower(), '/csc:' + str(compressed).lower(),
             '/psc:true', '/fsc:false', '/atc:false', '/ss:true', '/pc:true', '/slowclean:true',
             '/tp:Win32', '/ls:true', '/tl:3', '/el:0']
    storage = cache_profile(engine, workload, profile, prepared)
    intermediate = storage / ('intermediate' if sc else 'fresh-' + str(round_index))
    session = storage / ('session' if sc else 'fresh-session-' + str(round_index))
    flags += ['/iod:' + str(intermediate), '/scd:' + str(session),
              '/bcp:' + str(storage / 'assets')] + root_arguments(workload['effectiveInputRoots'])
    if engine == 'new':
        return [str(Path(prepared['runtimeDirectory']) / 'NewModCompiler.exe'), 'build',
                '--sdk-root', prepared['sdkRoot'], '--mod', workload['modRoot'],
                '--out', str(profile / 'product'), '--package-content', 'base',
                '--package-format', 'loose'] + flags
    schema = str(Path(prepared['sdkRoot']) / 'Schemas/xsd/CnC3Types.xsd')
    return [original['originalExe'], str(Path(workload['modRoot']) / 'Data/Mod.xml'),
            '/sp:' + schema, '/od:' + str(profile / 'streams'),
            '/res:false', '/gui:false', '/poe:false', '/mr:false', '/sh:false',
            '/oof:false', '/sf:false', '/oix:false', '/vf:false', '/osh:false', '/oar:false'] + flags


def path_identity(value):
    return str(value).replace('\\', '/').rstrip('/').casefold()


def validate_shared_report(report, workload, sdk):
    roots = report['inputRoots']
    expected = workload['effectiveInputRoots']
    require(path_identity(roots['dataRoot']) == path_identity(expected['dataRoot']), 'Data root differs')
    require(path_identity(roots['schemaPath']) == path_identity(Path(sdk) / 'Schemas/xsd/CnC3Types.xsd'),
            'Schema path differs')
    for key in ('dataPaths', 'artPaths', 'audioPaths'):
        require([path_identity(p) for p in roots[key]] == [path_identity(p) for p in expected[key]],
                'Actual search root membership/order differs: ' + key)
    tasks = report['plan']['Tasks']
    expected_tasks = workload.get('tasks')
    if expected_tasks:
        require([(task['Content'], path_identity(task['InputPath'])) for task in tasks] ==
                [(task['content'], path_identity(task['inputPath'])) for task in expected_tasks],
                'Complete stream entry membership/order differs')
    else:
        require(len(tasks) == 1 and path_identity(tasks[0]['InputPath']) ==
                path_identity(Path(workload['modRoot']) / 'Data/Mod.xml'), 'Stream entry differs')


def stream_inventory(directory, stem='mod'):
    """Strip only the host's stream-directory prefix; never rewrite bytes."""
    root = Path(directory)
    paths = [p for p in root.rglob('*') if p.is_file()]
    manifests = [p for p in paths if p.suffix == '.manifest']
    stem = stem.lower()
    require(len(manifests) == 1 and manifests[0].name == stem + '.manifest',
            'Expected exactly the selected Mod stream; extra/missing reference streams')
    parent = manifests[0].parent
    accepted = {parent / (stem + suffix) for suffix in ('.manifest', '.bin', '.imp', '.relo')}
    require(all(p.is_file() for p in accepted), 'Incomplete linked stream')
    result, sources = {}, {}
    for path in paths:
        if path not in accepted:
            require(path.parent == parent / (stem + '/cdata') and
                    re.fullmatch(r'[0-9a-fA-F]{8}(?:\.[0-9a-fA-F]{8}){3}\.cdata', path.name),
                    'Unexpected output member: ' + str(path))
        name = path.relative_to(parent).as_posix()
        require(name not in result, 'Duplicate linked output member')
        data = path.read_bytes()
        result[name] = {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
        sources[name] = path
    return result, sources


def compare_pair(left, right, left_paths, right_paths):
    require(left == right, 'Original/new stream members or SHA256 differ')
    for name in left:
        require(left_paths[name].read_bytes() == right_paths[name].read_bytes(),
                'Original/new raw bytes differ: ' + name)


def summaries(samples):
    groups = {}
    for sample in samples:
        if sample.get('status') == 'passed':
            key = sample['workload'], sample['mode'], sample['phase']
            groups.setdefault(key, {}).setdefault(sample['engine'], []).append(sample)
    rows = []
    for (workload, mode, phase), engines in groups.items():
        if set(engines) != {'original', 'new'} or len(engines['original']) != len(engines['new']):
            continue
        row = {'workload': workload, 'mode': mode, 'phase': phase,
               'nPerEngine': len(engines['original'])}
        for engine in ('original', 'new'):
            samples_for_engine = engines[engine]
            for metric in ('wallMs', 'cpuMs', 'peakWorkingSetBytes', 'cacheBytes'):
                row[engine + metric[0].upper() + metric[1:] + 'Median'] = statistics.median(
                    s[metric] for s in samples_for_engine)
            row[engine + 'WallMsMin'] = min(s['wallMs'] for s in samples_for_engine)
            row[engine + 'WallMsMax'] = max(s['wallMs'] for s in samples_for_engine)
        row['newOverOriginalWallRatio'] = row['newWallMsMedian'] / row['originalWallMsMedian']
        rows.append(row)
    return rows


def markdown(state):
    lines = ['# Original/new SDK paired benchmark', '', 'Status: ' + state['status'] + '.', '',
             'Same physical inputs, Schema and ordered search roots; separate compiler caches.',
             'Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.', '',
             '| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |',
             '| --- | --- | --- | ---: | ---: | ---: | ---: |']
    for row in state['summary']:
        lines.append('| {} | {} | {} | {} | {:.1f} | {:.1f} | {:.2f}x |'.format(
            row['workload'], row['mode'], row['phase'], row['nPerEngine'],
            row['originalWallMsMedian'], row['newWallMsMedian'], row['newOverOriginalWallRatio']))
    lines += ['', 'A ratio above 1 means the new CLI took longer. Small samples are descriptive.',
              'All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.', '']
    if state.get('error'):
        lines += ['Error: ' + state['error'], '']
    return '\n'.join(lines)


def save(output, state):
    state['summary'] = summaries(state['samples'])
    write_json(output / 'comparison.json', state)
    (output / 'comparison.md').write_text(markdown(state), encoding='utf-8')
    if state['summary']:
        with (output / 'comparison.csv').open('w', newline='', encoding='utf-8') as stream:
            writer = csv.DictWriter(stream, fieldnames=state['summary'][0].keys())
            writer.writeheader()
            writer.writerows(state['summary'])


def measure(command, cwd, evidence, timeout, environment):
    evidence.mkdir(parents=True)
    row = {'command': command, 'cwd': str(cwd), 'timeoutSeconds': timeout, 'status': 'started'}
    write_json(evidence / 'request.json', row)
    tick = time.perf_counter()
    with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
        process = subprocess.Popen(command, cwd=cwd, stdout=stdout, stderr=stderr, env=environment)
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
            row.update(status='timed-out', wallMs=(time.perf_counter() - tick) * 1000,
                       exitCode=process.returncode, **process_metrics(process))
            write_json(evidence / 'process.json', row)
            raise TimeoutError('Process exceeded strict budget: ' + evidence.name)
        row.update(wallMs=(time.perf_counter() - tick) * 1000, exitCode=process.returncode,
                   **process_metrics(process))
    text = '\n'.join((evidence / name).read_text(encoding='utf-8-sig', errors='replace')
                     for name in ('stdout.log', 'stderr.log'))
    row['loggedCompilerError'] = bool(re.search(r'(?im)^(?:\[[^\]]+\]\s*)?(?:Critical|Error):', text))
    row['status'] = 'exited' if process.returncode == 0 and not row['loggedCompilerError'] else 'failed'
    row['traceSessionLoaded'] = 'Cached session data available.' in text
    row['traceStringHashesLoaded'] = 'Loading previous string hash file' in text
    row['traceAssetsCopiedFromCache'] = len(re.findall(r'copied from cache:', text))
    write_json(evidence / 'process.json', row)
    require(row['status'] == 'exited', 'Process failed: ' + evidence.name)
    return row


def run(args):
    require(os.name == 'nt', 'Paired execution requires Windows')
    ctypes.WinDLL('kernel32', use_last_error=True).SetErrorMode(0x0001 | 0x0002 | 0x8000)
    prepared, original = read_json(args.preparation), read_json(args.original_preparation)
    require(original['assetbuilderConfigUnchanged'] is True, 'Original SDK configuration was altered')
    sdk = Path(prepared['sdkRoot']).resolve()
    require(sdk == Path(original['sourceSdkRoot']).resolve(), 'Engines use different SDK inputs')
    output, work = Path(args.output).resolve(), Path(args.work_directory).resolve()
    require(not output.exists() and not work.exists(), 'Refusing to overwrite comparison evidence/cache')
    output.mkdir(parents=True)
    work.mkdir(parents=True)
    workloads = original['workloads']
    require(len(workloads) == len(prepared['workloads']), 'Shared workload membership differs')
    for w, shared in zip(workloads, prepared['workloads']):
        require(w['name'] == shared['name'] and w['modRoot'] == shared['modRoot'] and
                w['files'] == shared['files'], 'Engines do not share identical physical MOD inputs')
    runtime = Path(prepared['runtimeDirectory'])
    expected_runtime = {f['name']: {'bytes': f['bytes'], 'sha256': f['sha256']}
                        for f in prepared['runtimeFiles']}
    old_runtime = Path(original['originalRuntimeDirectory'])
    expected_old = {f['name']: {'bytes': f['bytes'], 'sha256': f['sha256']}
                    for f in original['originalRuntimeFiles']}
    require(inventory(runtime) == expected_runtime and inventory(old_runtime) == expected_old,
            'Runtime inputs differ from verified preparations')
    inputs = {w['name']: inventory(w['modRoot']) for w in workloads}
    for w in workloads:
        expected = {f['path']: {'bytes': f['bytes'], 'sha256': f['sha256']} for f in w['files']}
        require(inputs[w['name']] == expected, 'Shared MOD changed since preparation')
    sdk_inputs = sdk_source_inventory(sdk, workloads)
    root_exists = {p: Path(p).is_dir() for w in workloads for key in ('dataPaths', 'artPaths', 'audioPaths')
                   for p in w['effectiveInputRoots'][key]}
    write_json(output / 'shared-inputs.json', {'sdk': sdk_inputs, 'workloads': inputs,
                                             'inputRootExistence': root_exists})
    state = {'schemaVersion': 1, 'status': 'running', 'samples': [], 'pairs': [], 'warmups': [],
             'binarySourceCommit': prepared['binarySourceCommit'],
             'benchmarkSourceCommit': prepared['benchmarkSourceCommit'],
             'sourceRunId': prepared['sourceRunId'], 'runId': os.getenv('GITHUB_RUN_ID'),
             'runAttempt': os.getenv('GITHUB_RUN_ATTEMPT'), 'workloads': workloads,
             'sharedInputFingerprint': digest({'sdk': sdk_inputs, 'workloads': inputs, 'rootExistence': root_exists}),
             'originalRuntimeFiles': original['originalRuntimeFiles'], 'newRuntimeFiles': prepared['runtimeFiles'],
             'limits': {'perProcessSeconds': args.timeout_seconds, 'totalSeconds': args.budget_seconds,
                        'hotRuns': args.hot_runs},
             'machine': {'platform': platform.platform(), 'cpuCount': os.cpu_count(), 'processor': platform.processor()},
             'comparisonScope': 'End-to-end fresh processes. New CLI includes JSONL/report generation, staging and atomic publication; original BAB directly writes streams. Not a pure Core microbenchmark.',
             'originalAdaptation': 'Only CLR4 startup configuration adapted. Original EXE/DLL and assetbuilder settings preserved; res/gui/metrics disabled in both hosts.',
             'timing': 'Launch to exit, excludes preparation/hash checks; warmup one identical no-cache build per engine excluded from samples but included in total budget.',
             'limitations': ['OS page cache uncontrolled; compiler cold means empty per-engine cache',
                             'Small sample counts on one VM, no significance claim',
                             'No full Upheaval, BIG packaging or incremental edit timings',
                             'Original cache trace observations are not equivalent to new durable counters']}
    environment = os.environ.copy()
    for name in list(environment):
        if name.startswith('RA3_RECOVERED_') or name == 'RECOVERED_W3X_TRACE_DIRECTORY':
            del environment[name]
    environment['PATH'] = os.pathsep.join(p for p in environment.get('PATH', '').split(os.pathsep)
                                          if '/sdk/tools' not in p.replace('\\', '/').lower())
    eligibility = {w['name']: bool(cache_eligibility(sdk, w['modRoot'])['cacheableTypes']) for w in workloads}
    started, baselines = time.perf_counter(), {}

    def one(engine, w, mode, phase, round_index, warmup=False):
        remaining = args.budget_seconds - (time.perf_counter() - started)
        require(remaining >= 1, 'Total comparison budget exhausted')
        label = '{}-{}-{}-{}'.format(w['name'], mode, round_index, engine)
        if warmup:
            label = 'warmup-' + engine
        evidence = output / label
        profile = work / ('warmup' if warmup else 'measured') / w['name'] / mode / engine
        storage = cache_profile(engine, w, profile, prepared)
        for name in ('assets', 'session' if MODES[mode][1] else 'fresh-session-' + str(round_index),
                     'intermediate' if MODES[mode][1] else 'fresh-' + str(round_index)):
            (storage / name).mkdir(parents=True, exist_ok=True)
        for name in ('streams', 'product'):
            (profile / name).mkdir(parents=True, exist_ok=True)
        command = invocation(engine, w, mode, profile, round_index, prepared, original)
        sample = measure(command, sdk, evidence, min(args.timeout_seconds, remaining), environment)
        sample.update(engine=engine, workload=w['name'], mode=mode, phase=phase, round=round_index,
                      sharedInputFingerprint=state['sharedInputFingerprint'])
        target = profile / ('product/published' if engine == 'new' else 'streams')
        if engine == 'new':
            report = read_json(profile / 'product/build-report.json')
            write_json(evidence / 'report.json', report)
            validate_sample(report, mode, phase, eligibility[w['name']])
            validate_shared_report(report, w, sdk)
            output_inventory(profile / 'product', report)
            sample['cache'] = report['cache']
            sample['reportElapsedMs'] = report['elapsedMs']
        else:
            sample['cache'] = None  # No equivalent durable counter interface in original BAB.
            if not MODES[mode][1]:
                require(not sample['traceStringHashesLoaded'],
                        'Session-disabled original run reused prior string-hash metadata')
            if phase == 'hot' and MODES[mode][1]:
                require(sample['traceSessionLoaded'], 'Original hot session was not loaded according to its trace')
        files, paths = stream_inventory(target)
        sample['outputFiles'] = files
        sample['cacheBytes'] = sum(p.stat().st_size for dirname in ('assets', 'session', 'intermediate')
                                   for p in (storage / dirname).rglob('*') if p.is_file())
        sample['status'] = 'validated'
        return sample, paths

    try:
        warm_results = {}
        for engine in ('original', 'new'):
            sample, paths = one(engine, workloads[0], 'none', 'uncached', -1, warmup=True)
            state['warmups'].append(sample)
            warm_results[engine] = sample, paths
        compare_pair(warm_results['original'][0]['outputFiles'], warm_results['new'][0]['outputFiles'],
                     warm_results['original'][1], warm_results['new'][1])
        for sample in state['warmups']:
            sample['status'] = 'passed'
        for round_index in range(args.hot_runs + 1):
            order = list(MODES)
            if round_index % 2:
                order.reverse()
            for wi, w in enumerate(workloads):
                for mi, mode in enumerate(order):
                    phase = 'uncached' if mode == 'none' else ('cold' if round_index == 0 else 'hot')
                    engines = ['original', 'new']
                    if (wi + mi + round_index) % 2:
                        engines.reverse()
                    pair = {}
                    for engine in engines:
                        sample, paths = one(engine, w, mode, phase, round_index)
                        state['samples'].append(sample)
                        pair[engine] = sample, paths
                    compare_pair(pair['original'][0]['outputFiles'], pair['new'][0]['outputFiles'],
                                 pair['original'][1], pair['new'][1])
                    files = pair['original'][0]['outputFiles']
                    baselines.setdefault(w['name'], files)
                    require(files == baselines[w['name']], 'Cache mode changed original/new baseline output')
                    require(inventory(w['modRoot']) == inputs[w['name']], 'Shared MOD input changed during pair')
                    for engine in engines:
                        pair[engine][0]['status'] = 'passed'
                    state['pairs'].append({'workload': w['name'], 'mode': mode, 'phase': phase,
                                           'round': round_index, 'engineOrder': engines,
                                           'identicalMembersHashesAndRawBytes': True})
                    if round_index == 0 and mode == 'none':
                        for engine in engines:
                            for name, path in pair[engine][1].items():
                                target = output / 'baseline-outputs' / w['name'] / engine / name
                                target.parent.mkdir(parents=True, exist_ok=True)
                                shutil.copyfile(path, target)
                    print('SDK_BENCHMARK_PAIR ' + json.dumps({
                        'workload': w['name'], 'mode': mode, 'phase': phase,
                        'originalMs': pair['original'][0]['wallMs'], 'newMs': pair['new'][0]['wallMs']}), flush=True)
                    save(output, state)
        state['status'] = 'passed'
    except Exception as error:
        state['status'] = 'failed'
        state['error'] = type(error).__name__ + ': ' + str(error)
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        state['sdkUnchanged'] = sdk_source_inventory(sdk, workloads) == sdk_inputs
        state['inputsUnchanged'] = all(inventory(w['modRoot']) == inputs[w['name']] for w in workloads)
        state['rootExistenceUnchanged'] = all(Path(p).is_dir() == value for p, value in root_exists.items())
        state['runtimeUnchanged'] = inventory(runtime) == expected_runtime and inventory(old_runtime) == expected_old
        if not all(state[k] for k in ('sdkUnchanged', 'inputsUnchanged', 'rootExistenceUnchanged', 'runtimeUnchanged')):
            state['status'], state['error'] = 'failed', 'Shared inputs, runtime or input-root existence changed'
        save(output, state)
        print('SDK_BENCHMARK_RESULT ' + json.dumps({k: state.get(k) for k in
              ('status', 'error', 'elapsedSeconds', 'summary')}), flush=True)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preparation', required=True)
    parser.add_argument('--original-preparation', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--work-directory', required=True)
    parser.add_argument('--timeout-seconds', type=int, choices=range(5, 61), default=30)
    parser.add_argument('--budget-seconds', type=int, choices=range(30, 301), default=180)
    parser.add_argument('--hot-runs', type=int, choices=range(1, 8), default=2)
    args = parser.parse_args()
    return 0 if run(args)['status'] == 'passed' else 1


if __name__ == '__main__':
    raise SystemExit(main())
