#!/usr/bin/env python3
"""Bounded, fresh-process benchmark of the accepted Win32 legacy-cache runtime.

No production compilation, packaging, oracle execution or SDK mutation is done
here. Setup/hash verification is outside process timing. Cache cold means an
empty compiler cache; OS page cache is deliberately not flushed.
"""
import argparse
import csv
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import time
import xml.etree.ElementTree as ET

from CompilerCachePaths import cache_root

MODES = {
    'none': (False, False, False),
    'asset': (True, False, False),
    'session-xml': (False, True, False),
    'session-deflate': (False, True, True),
    'both-xml': (True, True, False),
    'both-deflate': (True, True, True),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inventory(directory):
    base = Path(directory)
    return {p.relative_to(base).as_posix(): {'bytes': p.stat().st_size, 'sha256': sha(p)}
            for p in sorted(base.rglob('*')) if p.is_file()}


def process_metrics(process):
    """Read the finished process handle before Popen releases it (Windows only)."""
    class Memory(ctypes.Structure):
        _fields_ = [('cb', ctypes.c_uint32), ('faults', ctypes.c_uint32)] + [
            (name, ctypes.c_size_t) for name in
            ('peakWorkingSet', 'workingSet', 'peakPagedPool', 'pagedPool',
             'peakNonPagedPool', 'nonPagedPool', 'pagefile', 'peakPagefile')]
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    psapi = ctypes.WinDLL('psapi', use_last_error=True)
    kernel.GetProcessTimes.argtypes = [ctypes.c_void_p] + [
        ctypes.POINTER(ctypes.c_uint64)] * 4
    psapi.GetProcessMemoryInfo.argtypes = [ctypes.c_void_p, ctypes.POINTER(Memory), ctypes.c_uint32]
    values = [ctypes.c_uint64() for _ in range(4)]
    handle = ctypes.c_void_p(int(process._handle))
    require(kernel.GetProcessTimes(handle, *(ctypes.byref(v) for v in values)),
            'GetProcessTimes failed')
    memory = Memory()
    memory.cb = ctypes.sizeof(memory)
    require(psapi.GetProcessMemoryInfo(handle, ctypes.byref(memory), memory.cb),
            'GetProcessMemoryInfo failed')
    return {'cpuMs': (values[2].value + values[3].value) / 10000,
            'peakWorkingSetBytes': memory.peakWorkingSet}


def output_inventory(destination, report):
    published = (Path(destination) / 'published').resolve()
    expected = {}
    for row in report['artifacts']:
        path = Path(row['Path']).resolve()
        require(path.is_relative_to(published), 'Reported artifact escaped published directory')
        name = path.relative_to(published).as_posix()
        require(name not in expected, 'Duplicate reported artifact')
        require(path.stat().st_size == row['Size'], 'Reported output length differs')
        expected[name] = {'bytes': row['Size'], 'sha256': sha(path)}
    require(expected, 'No current completed output')
    actual = expected
    require({Path(p).suffix for p in actual} >= {'.manifest', '.bin', '.imp', '.relo'},
            'Linked output is incomplete')
    return actual


def cache_eligibility(sdk, mod):
    plugins = ET.parse(Path(sdk) / 'tools/BinaryAssetBuilder.exe.config').getroot()
    routes, fallback = {}, False
    for plugin in plugins.findall('./assetbuilder/plugins/plugin'):
        if plugin.get('enabled', 'true').lower() != 'true':
            continue
        cacheable = plugin.get('useBuildCache', 'false').lower() == 'true'
        for name in plugin.get('assetTypes', '').split(','):
            if name.strip() == '#all':
                fallback = cacheable
            else:
                routes[name.strip()] = cacheable
    types = set()
    for path in Path(mod).rglob('*.xml'):
        declaration = ET.parse(path).getroot()
        if declaration.tag == '{uri:ea.com:eala:asset}AssetDeclaration':
            types.update(c.tag.rsplit('}', 1)[-1] for c in declaration if c.get('id'))
    require(types, 'No declared assets in benchmark workload')
    return {'declaredTypes': sorted(types),
            'cacheableTypes': sorted(t for t in types if routes.get(t, fallback)),
            'basis': 'SDK plugin useBuildCache flags (omitted defaults false) and workload root asset declarations'}


def validate_sample(report, mode, phase, asset_cache_expected=True):
    require(report.get('exitCode') == 0 and report.get('status') == 'succeeded' and
            report.get('publicationStatus') == 'PublishedDirectory' and
            report.get('executionMode') == 'InProcess' and report.get('externalTools') == [],
            'CLI did not successfully publish in process')
    cache = report['cache']
    bc, sc, compressed = MODES[mode]
    effective_bc = str(report['effectiveOptions']['bc']['Value']).lower()
    require(cache.get('sessionCache') == sc and effective_bc == str(bc).lower(),
            'Effective cache switches differ from benchmark mode')
    if sc:
        require(cache.get('compressedSessionCache') == compressed, 'Compression option differs')
    for key in ('hits', 'misses', 'documentReuse', 'xmlParse', 'instanceCompile'):
        require(type(cache.get(key)) is int and cache[key] >= 0, 'Missing cache counter: ' + key)
    if phase == 'hot' and sc:
        require(cache['documentReuse'] > 0 and cache['instanceCompile'] == 0,
                'Hot session did not reuse completed documents/assets')
    elif phase == 'hot' and bc:
        if asset_cache_expected:
            require(cache['hits'] > 0 and cache['misses'] == 0,
                    'Hot asset cache did not hit independently of intermediate files')
        else:
            require(cache['hits'] == cache['misses'] == 0,
                    'Unexpected cache activity for SDK-disabled asset types')
    elif phase == 'cold':
        require(cache['documentReuse'] == 0 and (not bc or cache['hits'] == 0),
                'Cold run reused a nonempty cache')
    if not bc:
        require(cache['hits'] == cache['misses'] == 0, 'Disabled build cache recorded activity')
    if not sc:
        require(cache['documentReuse'] == 0, 'Disabled session cache reused documents')


def summarize(samples):
    groups = {}
    for row in samples:
        if row.get('status') != 'passed':
            continue
        groups.setdefault((row['workload'], row['mode'], row['phase']), []).append(row)
    results = []
    for (workload, mode, phase), rows in groups.items():
        result = {'workload': workload, 'mode': mode, 'phase': phase, 'n': len(rows),
                  'assetCacheApplicable': rows[0].get('assetCacheApplicable', True)}
        for key in ('wallMs', 'reportElapsedMs', 'cpuMs', 'peakWorkingSetBytes', 'cacheBytes'):
            values = [r[key] for r in rows]
            result[key + 'Median'] = statistics.median(values)
            if key == 'wallMs':
                result['wallMsMin'], result['wallMsMax'] = min(values), max(values)
        for key in ('hits', 'misses', 'documentReuse', 'xmlParse', 'instanceCompile'):
            result[key + 'Median'] = statistics.median(r['cache'][key] for r in rows)
        results.append(result)
    for row in results:
        baseline = next((x for x in results if x['workload'] == row['workload'] and
                         x['mode'] == 'none'), None)
        row['speedupVsNoCache'] = (baseline['wallMsMedian'] / row['wallMsMedian']
                                   if baseline and row['wallMsMedian'] > 0 else None)
    return results


def markdown(state):
    lines = ['# Compiler cache benchmark', '',
             'Status: ' + state['status'] + '. Binary source: `' + state['binarySourceCommit'] + '`.', '',
             'Release, fresh process per sample, loose base output; setup and hash checks excluded from wall time.',
             'OS page cache is uncontrolled. Cold refers only to compiler cache. Small sample counts are descriptive.', '',
             '| Workload | Mode | Phase | n | Wall median ms | Min–max ms | CPU ms | Peak MiB | Cache MiB | vs no-cache |',
             '| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
    for row in state['summary']:
        ratio = row['speedupVsNoCache']
        lines.append('| {} | {} | {} | {} | {:.1f} | {:.1f}–{:.1f} | {:.1f} | {:.1f} | {:.2f} | {} |'.format(
            row['workload'], row['mode'] + (' (type cache disabled)' if row['mode'] == 'asset' and
                                          not row['assetCacheApplicable'] else ''),
            row['phase'], row['n'], row['wallMsMedian'],
            row['wallMsMin'], row['wallMsMax'], row['cpuMsMedian'],
            row['peakWorkingSetBytesMedian'] / 1048576, row['cacheBytesMedian'] / 1048576,
            '{:.2f}x'.format(ratio) if ratio else '—'))
    lines += ['', 'All passed samples have identical complete output inventories within their workload.', '']
    for row in state['workloads']:
        lines.append('- ' + row['name'] + ': ' + row['scope'])
    if state.get('error'):
        lines += ['', 'Error: ' + state['error']]
    return '\n'.join(lines) + '\n'


def save(output, state):
    state['summary'] = summarize(state['samples'])
    (output / 'benchmark.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    (output / 'benchmark.md').write_text(markdown(state), encoding='utf-8')
    if state['summary']:
        with (output / 'summary.csv').open('w', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=state['summary'][0].keys())
            writer.writeheader()
            writer.writerows(state['summary'])


def run(args):
    require(os.name == 'nt', 'Execution requires Windows; Python unit checks run cross-platform')
    # Loader/runtime errors must exit rather than opening an unattended Windows
    # error dialog. Children inherit this mode; process timeouts remain strict.
    ctypes.WinDLL('kernel32', use_last_error=True).SetErrorMode(0x0001 | 0x0002 | 0x8000)
    prepared = read_json(args.preparation)
    runtime = Path(prepared['runtimeDirectory']).resolve()
    require(not Path(args.output).exists(), 'Refusing to overwrite benchmark evidence')
    output = Path(args.output).resolve()
    output.mkdir(parents=True)
    sdk = Path(args.sdk_root).resolve()
    work = output / 'work'
    work.mkdir()
    before_runtime = inventory(runtime)
    expected_runtime = {r['name']: {'bytes': r['bytes'], 'sha256': r['sha256']}
                        for r in prepared['runtimeFiles']}
    require(before_runtime == expected_runtime, 'Prepared runtime inventory changed')
    workloads = prepared['workloads']
    input_before = {w['name']: inventory(w['modRoot']) for w in workloads}
    eligibility = {w['name']: cache_eligibility(sdk, w['modRoot']) for w in workloads}
    sdk_inputs = {'schemas': inventory(sdk / 'Schemas'),
                  'configSha256': sha(sdk / 'tools/BinaryAssetBuilder.exe.config')}
    state = {'schemaVersion': 1, 'status': 'running', 'samples': [], 'summary': [],
             'binarySourceCommit': prepared['binarySourceCommit'],
             'benchmarkSourceCommit': prepared['benchmarkSourceCommit'],
             'sourceRunId': prepared['sourceRunId'], 'runId': os.getenv('GITHUB_RUN_ID'),
             'runAttempt': os.getenv('GITHUB_RUN_ATTEMPT'), 'runtimeFiles': prepared['runtimeFiles'],
             'workloads': [{'name': w['name'], 'scope': w['scope'],
                            'assetCacheEligibility': eligibility[w['name']],
                            'inputFiles': len(input_before[w['name']]),
                            'inputBytes': sum(v['bytes'] for v in input_before[w['name']].values())}
                           for w in workloads],
             'limits': {'perProcessSeconds': args.timeout_seconds, 'totalSeconds': args.budget_seconds,
                        'hotRuns': args.hot_runs},
             'machine': {'platform': platform.platform(), 'cpuCount': os.cpu_count(),
                         'processor': platform.processor()},
             'timing': 'Popen launch to exit; includes startup, parsing, compilation, linking and publication; excludes setup, input/output hashing and cache inventory',
             'limitations': ['OS page cache uncontrolled; compiler-cold is not disk-cold',
                             'One runner, small sample counts, no statistical significance claim',
                             'No input-edit/invalidation, BIG packaging or full Upheaval performance measured']}
    started = time.perf_counter()
    baselines = {}
    try:
        # Check the same runtime contract as release deployment before timing
        # builds. This also gives every mode an equally loaded EXE/page cache.
        doctor_tick = time.perf_counter()
        doctor_command = [str(runtime / 'NewModCompiler.exe'), 'doctor', '--sdk-root', str(sdk)]
        with (output / 'doctor-stdout.log').open('wb') as stdout, (output / 'doctor-stderr.log').open('wb') as stderr:
            doctor = subprocess.run(doctor_command, cwd=runtime, stdout=stdout, stderr=stderr,
                                    timeout=min(args.timeout_seconds, args.budget_seconds))
        state['startupDoctor'] = {'exitCode': doctor.returncode,
                                  'wallMs': (time.perf_counter() - doctor_tick) * 1000,
                                  'countedInBudget': True, 'countedInBuildSamples': False}
        require(doctor.returncode == 0 and 'CORE_ENVIRONMENT_OK' in
                (output / 'doctor-stdout.log').read_text(encoding='utf-8-sig'),
                'Startup doctor failed; build measurements were not started')
        # Alternate mode order between rounds. Each mode owns an independent
        # output/cache root; no-cache and asset-only always get fresh intermediate.
        for round_index in range(args.hot_runs + 1):
            order = list(MODES)
            if round_index % 2:
                order.reverse()
            for workload in workloads:
                for mode in order:
                    remaining = args.budget_seconds - (time.perf_counter() - started)
                    if remaining < 1:
                        state['status'] = 'budget-exhausted'
                        return state
                    phase = 'uncached' if mode == 'none' else ('cold' if round_index == 0 else 'hot')
                    label = '{}-{}-{}'.format(workload['name'], mode, round_index)
                    evidence = output / label
                    evidence.mkdir()
                    destination = work / workload['name'] / mode
                    bc, sc, compressed = MODES[mode]
                    command = [str(runtime / 'NewModCompiler.exe'), 'build', '--sdk-root', str(sdk),
                               '--mod', workload['modRoot'], '--out', str(destination),
                               '--package-content', 'base', '--package-format', 'loose',
                               '/bc:' + str(bc).lower(), '/sc:' + str(sc).lower(),
                               '/csc:' + str(compressed).lower(), '/psc:true', '/fsc:false', '/atc:false']
                    # Cache modes remain isolated while every writable cache
                    # path stays in this MOD's SDK partition.
                    profile = cache_root(sdk, workload['modRoot']) / 'validation' / ('bench-' + hashlib.sha256(str(destination).encode()).hexdigest()[:12])
                    intermediate = profile / ('intermediate' if sc else 'fresh-' + str(round_index))
                    command += ['/scd:' + str(profile / 'session'), '/bcd:' + str(profile / 'assets'),
                                '/iod:' + str(intermediate)]
                    sample = {'workload': workload['name'], 'mode': mode, 'phase': phase,
                              'assetCacheApplicable': bool(eligibility[workload['name']]['cacheableTypes']),
                              'round': round_index, 'status': 'started', 'command': command,
                              'timeoutSeconds': min(args.timeout_seconds, remaining)}
                    state['samples'].append(sample)
                    (evidence / 'request.json').write_text(json.dumps(sample, indent=2), encoding='utf-8')
                    tick = time.perf_counter()
                    with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                        process = subprocess.Popen(command, cwd=runtime, stdout=stdout, stderr=stderr)
                        try:
                            process.wait(timeout=sample['timeoutSeconds'])
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait(timeout=5)
                            sample['wallMs'] = (time.perf_counter() - tick) * 1000
                            sample['exitCode'] = process.returncode
                            sample['eventsObserved'] = (destination / 'build.events.jsonl').exists()
                            sample['reportObserved'] = (destination / 'build-report.json').exists()
                            sample['status'] = 'timed-out'
                            raise TimeoutError('Process timeout: ' + label)
                        sample['wallMs'] = (time.perf_counter() - tick) * 1000
                        sample['exitCode'] = process.returncode
                        sample.update(process_metrics(process))
                    require(process.returncode == 0, 'Process failed: ' + label)
                    report = read_json(destination / 'build-report.json')
                    (evidence / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
                    validate_sample(report, mode, phase, sample['assetCacheApplicable'])
                    files = output_inventory(destination, report)
                    if workload['name'] not in baselines:
                        require(mode == 'none', 'No-cache baseline must run first')
                        baselines[workload['name']] = files
                    require(files == baselines[workload['name']], 'Output differs from no-cache: ' + label)
                    sample.update({'status': 'passed', 'reportElapsedMs': report['elapsedMs'],
                                   'cache': report['cache'], 'outputFiles': files,
                                   'cacheBytes': sum(p.stat().st_size for p in profile.rglob('*') if p.is_file())})
                    print('CACHE_BENCHMARK_SAMPLE ' + json.dumps({k: sample[k] for k in
                          ('workload', 'mode', 'phase', 'wallMs', 'cpuMs', 'status')}), flush=True)
                    save(output, state)
        state['status'] = 'passed'
    except Exception as error:
        state['status'] = 'failed'
        state['error'] = type(error).__name__ + ': ' + str(error)
        if state['samples'] and state['samples'][-1]['status'] == 'started':
            state['samples'][-1]['status'] = 'failed'
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        state['runtimeUnchanged'] = inventory(runtime) == before_runtime
        state['inputsUnchanged'] = all(inventory(w['modRoot']) == input_before[w['name']] for w in workloads)
        state['sdkUnchanged'] = (inventory(sdk / 'Schemas') == sdk_inputs['schemas'] and
                                 sha(sdk / 'tools/BinaryAssetBuilder.exe.config') == sdk_inputs['configSha256'])
        if not all(state[k] for k in ('runtimeUnchanged', 'inputsUnchanged', 'sdkUnchanged')):
            state['status'] = 'failed'
            state['error'] = 'Runtime, SDK or workload inputs changed'
        save(output, state)
        print('CACHE_BENCHMARK_RESULT ' + json.dumps({k: state.get(k) for k in
              ('status', 'elapsedSeconds', 'error', 'summary')}), flush=True)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preparation', required=True)
    parser.add_argument('--sdk-root', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--timeout-seconds', type=int, choices=range(5, 61), default=30)
    parser.add_argument('--budget-seconds', type=int, choices=range(30, 301), default=180)
    parser.add_argument('--hot-runs', type=int, choices=range(1, 4), default=2)
    args = parser.parse_args()
    state = run(args)
    return 0 if state['status'] == 'passed' else 1


if __name__ == '__main__':
    raise SystemExit(main())
