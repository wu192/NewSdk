#!/usr/bin/env python3
"""Same-VM before/after BIG bytes, wall time and Windows logical process I/O.

All byte comparisons and hashes run in this controller after the child exits.
Process I/O includes runtime loading and diagnostics, and is not physical disk I/O.
"""
import argparse
import csv
from collections import Counter
from contextlib import contextmanager, nullcontext
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import time

from TestCompilerHotCache import need, prepare


class IoCounters(ctypes.Structure):
    _fields_ = [(name, ctypes.c_ulonglong) for name in (
        'readOperations', 'writeOperations', 'otherOperations',
        'readBytes', 'writeBytes', 'otherBytes')]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_cache(cache, phase, label):
    if phase == 'disabled':
        need(not cache['sessionCache'] and cache['hits'] == cache['misses'] == 0,
             label + ' did not disable the native caches')
    elif phase == 'cold':
        need(cache['sessionCache'] and cache['sessionPackageMisses'] > 0 and
             cache['documentReuse'] == 0 and cache['instanceCompile'] > 0 and cache['hits'] == 0,
             label + ' did not exercise a cold cache')
    else:
        need(cache['sessionPackageHits'] > 0 and cache['xmlParse'] == cache['instanceCompile'] == 0 and
             cache['linkedStreamsRestored'] > 0, label + ' did not exercise hot document/four-stream reuse')


def measure(args):
    need(os.name == 'nt', 'Windows process I/O counters are required')
    repo = Path(__file__).resolve().parents[2]
    work, out = Path(args.work).resolve(), Path(args.output).resolve()
    need(not work.exists() and not out.exists(), 'Refusing to overwrite evidence')
    work.mkdir(parents=True)
    out.mkdir(parents=True)
    mod = Path(prepare(repo, work / 'mods')['medium']['modRoot'])
    additional = mod / 'Additional' / 'payload.bin'
    additional.parent.mkdir()
    executables = {name: Path(getattr(args, name)).resolve() for name in ('before', 'after')}
    api = ctypes.WinDLL('kernel32', use_last_error=True)
    api.GetProcessIoCounters.argtypes = [wintypes.HANDLE, ctypes.POINTER(IoCounters)]
    api.GetProcessIoCounters.restype = wintypes.BOOL
    api.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    state = {'status': 'started', 'runId': os.getenv('GITHUB_RUN_ID'),
             'sourceCommit': args.source_commit, 'baselineCommit': args.baseline_commit,
             'binaries': {name: {'path': str(path), 'sha256': digest(path)} for name, path in executables.items()},
             'scope': 'Synthetic 256 Armor, 8 Texture, 2 PCM Audio, 2 animation and 1 hierarchy base stream; Additional absent/16 MiB. Not a full MOD benchmark.',
             'ioMeaning': 'Windows GetProcessIoCounters logical process transfers; includes runtime loading and report/log I/O, excludes controller comparisons; OS page cache uncontrolled.',
             'samples': [], 'medians': []}
    started = time.perf_counter()
    expected = {}

    @contextmanager
    def trace_files(pml):
        monitor = subprocess.Popen([args.procmon, '/AcceptEula', '/Quiet', '/Minimized', '/BackingFile', str(pml)],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            # A second /WaitForIdle invocation hung on the runner. Observe the
            # actual backing file from the one recorder instance instead.
            deadline = time.monotonic() + 15
            while not (pml.exists() and pml.stat().st_size > 0):
                need(time.monotonic() < deadline, 'Process Monitor did not open its backing file')
                time.sleep(0.1)
            yield
        finally:
            subprocess.run([args.procmon, '/AcceptEula', '/Quiet', '/Terminate'], check=True, timeout=30)
            monitor.wait(timeout=30)

    def sample(engine, scenario, phase, round_index, trace=False):
        label = f'{scenario}-{round_index}-{phase}-{engine}'
        evidence = out / label
        evidence.mkdir()
        destination = work / 'builds' / scenario / engine / (f'none-{round_index}' if phase == 'disabled' else f'cached-{round_index}')
        enabled = phase != 'disabled'
        command = [str(executables[engine]), 'build', '--mod', str(mod), '--sdk-root', str(repo / 'SDK'),
                   '--out', str(destination), '--package-content', 'base', '--package-format', 'big',
                   '--include-additional=true', '--jobs=4', '--performance=detailed',
                   '--bc=' + str(enabled).lower(), '--sc=' + str(enabled).lower()]
        remaining = args.budget - (time.perf_counter() - started)
        need(remaining > 0, 'Benchmark budget exhausted')
        pml = evidence / 'file-operations.pml'
        with trace_files(pml) if trace else nullcontext():
            tick = time.perf_counter()
            with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                with subprocess.Popen(command, cwd=executables[engine].parent, stdout=stdout, stderr=stderr) as child:
                    try:
                        child.wait(timeout=min(args.timeout, remaining))
                    except subprocess.TimeoutExpired:
                        child.kill()
                        child.wait()
                        raise
                    elapsed = (time.perf_counter() - tick) * 1000
                    counters = IoCounters()
                    need(api.GetProcessIoCounters(int(child._handle), ctypes.byref(counters)), 'GetProcessIoCounters failed')
                    exit_code = child.returncode
                    process_id = child.pid
            if trace:
                child._handle.Close()
                # Process Monitor consumes kernel events asynchronously. Let it
                # drain the process exit before stopping this separate trace.
                time.sleep(0.5)
        operations = None
        if trace:
            exported = evidence / 'file-operations.csv'
            subprocess.run([args.procmon, '/Quiet', '/OpenLog', str(pml), '/SaveAs', str(exported)], check=True, timeout=60)
            counts, sidecars = Counter(), Counter()
            filtered = evidence / 'compiler-file-operations.csv'
            with exported.open(encoding='utf-8-sig', newline='') as source, filtered.open('w', encoding='utf-8', newline='') as target:
                reader = csv.DictReader(source)
                writer = csv.DictWriter(target, reader.fieldnames)
                writer.writeheader()
                for event in reader:
                    if event['PID'] != str(process_id): continue
                    writer.writerow(event)
                    counts[event['Operation']] += 1
                    if event['Operation'] == 'CreateFile' and event['Result'] == 'SUCCESS':
                        counts['CreateFileSuccess'] += 1
                    if any(name in event['Path'] for name in ('.file-identities.json', 'session.identity')):
                        sidecars[event['Operation']] += 1
            need(counts['CreateFile'] > 0 and counts['Process Start'] == counts['Process Exit'] == 1, 'Process Monitor did not capture the complete compiler lifetime')
            operations = {'operations': dict(counts), 'removedSidecars': dict(sidecars), 'processId': process_id}
            # Keep the bounded per-process aggregate; the raw CSV/PML contains
            # unrelated runner processes and is not uploaded as task evidence.
            exported.unlink()
            pml.unlink()
        report = json.loads((destination / 'build-report.json').read_text(encoding='utf-8-sig'))
        (evidence / 'build-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        row = {'engine': engine, 'scenario': scenario, 'phase': phase, 'round': round_index,
               'wallMs': elapsed, 'exitCode': exit_code, 'traced': trace, 'fileOperations': operations, 'io': {name: getattr(counters, name) for name, _ in counters._fields_}}
        state['samples'].append(row)
        need(exit_code == report['exitCode'] == 0, label + ' failed')
        packages = {Path(item['Path']).name: Path(item['Path']).read_bytes()
                    for item in report['artifacts'] if item['Kind'] in ('package.big', 'package.skudef')}
        need(len(packages) == 2, label + ' omitted BIG/SKUDEF')
        if scenario not in expected:
            expected[scenario] = packages
        need(packages == expected[scenario], label + ' BIG/SKUDEF bytes differ')
        validate_cache(report['cache'], phase, label)
        entries = report['performance']['entries']
        row['fileHashReads'] = report['cache']['fileHashReads']
        row['fastHashReads'] = sum(entry['count'] for entry in entries if entry['name'] == 'core.filehash.read')
        row['sha256Reads'] = sum(entry['count'] for entry in entries if entry['name'] == 'identity.sha256')
        row['directoryEnumerations'] = sum(entry['count'] for entry in entries if entry['name'] == 'identity.directory')
        row['parallel'] = report['cache']['parallel']
        row['publicationMs'] = sum(entry['totalMs'] for entry in entries if entry['name'] == 'stage.publish')
        row['packageMs'] = sum(entry['totalMs'] for entry in entries if entry['name'] == 'stage.package')
        row['outputBytes'] = sum(map(len, packages.values()))
        row['rawBytesEqual'] = True
        row['cache'] = report['cache']
        print(label + ' passed: %.1f ms' % elapsed, flush=True)

    try:
        for scenario in ('compiled-only', 'additional-16MiB'):
            if scenario == 'additional-16MiB':
                additional.write_bytes(bytes(range(256)) * (16 * 1024 * 1024 // 256))
            for round_index in range(3):
                order = ('before', 'after') if round_index % 2 == 0 else ('after', 'before')
                for phase in ('disabled', 'cold', 'hot'):
                    for engine in order:
                        sample(engine, scenario, phase, round_index)
        for scenario in ('compiled-only', 'additional-16MiB'):
            for phase in ('disabled', 'cold', 'hot'):
                for engine in ('before', 'after'):
                    rows = [row for row in state['samples'] if not row['traced'] and (row['scenario'], row['phase'], row['engine']) == (scenario, phase, engine)]
                    state['medians'].append({'engine': engine, 'scenario': scenario, 'phase': phase, 'n': len(rows),
                        **{name: statistics.median(row[name] for row in rows) for name in ('wallMs', 'publicationMs', 'packageMs', 'fileHashReads', 'fastHashReads', 'sha256Reads', 'directoryEnumerations')},
                        **{name: statistics.median(row['io'][name] for row in rows) for name in ('readBytes', 'writeBytes', 'readOperations', 'writeOperations', 'otherOperations')}})
        if args.procmon:
            for phase in ('cold', 'hot'):
                for engine in ('before', 'after'):
                    sample(engine, 'additional-16MiB', phase, 999, trace=True)
        state['status'] = 'passed'
    except Exception as error:
        state.update(status='failed', error=str(error))
        raise
    finally:
        for name in ('file-operations.pml', 'file-operations.csv'):
            for temporary in out.rglob(name):
                temporary.unlink()
        state['elapsedSeconds'] = time.perf_counter() - started
        (out / 'measurement.json').write_text(json.dumps(state, indent=2), encoding='utf-8')
        lines = ['# Direct output paired measurement', '', 'Status: ' + state['status'], '', state['scope'], '', state['ioMeaning'], '',
                 '| Scenario | Phase | Engine | n | Wall ms | Package ms | Publish ms | Logical read MiB | Logical write MiB |',
                 '| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |']
        for row in state['medians']:
            lines.append('| {scenario} | {phase} | {engine} | {n} | {wallMs:.2f} | {packageMs:.2f} | {publicationMs:.2f} | {read:.2f} | {write:.2f} |'.format(
                **row, read=row['readBytes'] / 1048576, write=row['writeBytes'] / 1048576))
        (out / 'measurement.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', required=True)
    parser.add_argument('--after', required=True)
    parser.add_argument('--work', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--baseline-commit', required=True)
    parser.add_argument('--procmon', help='Capture separate cold/hot file-open and directory-query samples')
    parser.add_argument('--budget', type=float, default=120)
    parser.add_argument('--timeout', type=float, default=20)
    measure(parser.parse_args())
