#!/usr/bin/env python3
"""Bounded same-VM baseline/candidate cold and hot builds of full Upheaval."""
import argparse
import ctypes
import filecmp
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import time
import xml.etree.ElementTree as ET

from BenchmarkCompilerCache import process_metrics
from HotCacheWorkload import (HISTORICAL, UPHEAVAL_TREE, add_include, allocate_id,
                              armor, declaration, extract_upheaval, inventory,
                              reserved_instance_hashes, save_xml)
from MeasureCompilerModScale import phases, sha, write

BASELINE_HEAD = '1810fff87727b70779f31328740bb0c2133157d2'
BASELINE_BINARY_SOURCE = 'b385337e463bddebdb267946d9edb1b3b6ee5f0a'
BASELINE_RUN = '41192817627'
BASELINE_FILES = {
    'NewModCompiler.exe': '8cc060e45a962604f06ea6ef73fa53c518cb173bd3ab72f3b66f7196b3485157',
    'NewModCompiler.exe.config': '94eedb0cd0150b90c8115aaee53f64ab0523b39bd67887991b1b62679a752e68',
    'antlr.runtime.dll': 'febdc86d6299a550c6ec87050b0cbf5205e821851e75494c699c17ce8ba2c62a',
}


def need(condition, message):
    if not condition:
        raise ValueError(message)


def git(repo, *arguments):
    return subprocess.check_output(['git', '-C', str(repo), *arguments], text=True).strip()


def product(directory, source, pinned=None):
    receipt = json.loads((directory / 'build.json').read_text(encoding='utf-8-sig'))
    need(receipt['sourceCommit'] == source and receipt['configuration'] == 'Release',
         'Production build identity differs: ' + str(directory))
    runtime = directory / 'bin'
    names = pinned.keys() if pinned else receipt['runtimeFiles']
    actual = {name: sha(runtime / name) for name in names}
    need(actual['NewModCompiler.exe'] == receipt['exeSha256'], 'EXE differs from build receipt')
    need(set(p.name for p in runtime.iterdir() if p.is_file()) == set(actual), 'Runtime file set differs from accepted identity or production receipt')
    if pinned:
        need(actual == pinned, 'Accepted baseline runtime identity changed')
    return runtime / 'NewModCompiler.exe', actual


def expand_armor(repo, mod, partitions):
    """Optional reachable load: 256 XML documents / 8192 assets per partition."""
    if not partitions:
        return
    occupied = reserved_instance_hashes(repo, mod)
    generated = mod / 'Data/CachePaired'
    index = declaration()
    for partition in range(partitions):
        for doc in range(256):
            name = 'armor-%02d-%03d.xml' % (partition, doc)
            nodes = [armor(allocate_id('CachePaired_%02d_%03d_%02d' % (partition, doc, i), occupied))
                     for i in range(32)]
            save_xml(generated / name, declaration(*nodes))
            add_include(index, name)
    save_xml(generated / 'index.xml', index)
    entry = mod / 'Data/Mod.xml'
    root = ET.parse(entry).getroot()
    add_include(root, 'CachePaired/index.xml')
    save_xml(entry, root)


def summarize(samples):
    result = []
    for mode in ('default', 'none'):
        for phase in ('cold', 'hot'):
            groups = {label: [r['wallMs'] for r in samples if r['status'] == 'passed' and
                             r['performance'] == 'off' and r['mode'] == mode and
                             r['phase'] == phase and r['implementation'] == label]
                      for label in ('baseline', 'candidate')}
            if not all(groups.values()):
                continue
            row = dict(mode=mode, phase=phase)
            for label, values in groups.items():
                row[label] = dict(n=len(values), medianMs=statistics.median(values),
                                  minMs=min(values), maxMs=max(values))
            row['reductionPercent'] = 100 * (1 - row['candidate']['medianMs'] / row['baseline']['medianMs'])
            result.append(row)
    return result


def save(out, state):
    state['summary'] = summarize(state['samples'])
    write(out / 'measurement.json', state)
    lines = ['# Compiler cache paired measurement', '', 'Status: ' + state['status'], '',
             'Same Windows VM and input, new process per sample; OS page cache is uncontrolled. '
             'Cold means empty compiler caches. Detailed attribution is excluded from off medians.', '',
             '| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |',
             '| --- | --- | ---: | ---: | ---: |']
    for row in state['summary']:
        lines.append('| %s | %s | %d / %.2f | %d / %.2f | %.2f%% |' % (
            row['mode'], row['phase'], row['baseline']['n'], row['baseline']['medianMs'],
            row['candidate']['n'], row['candidate']['medianMs'], row['reductionPercent']))
    lines += ['', 'Two hot samples share each cold namespace; cold pairs are independent. '
              'Small sample counts are descriptive. Raw published files are compared after process timing. '
              'This performance run does not replace production/SDK acceptance.', '',
              'Workload: ' + state.get('workloadScope', 'preparation incomplete')]
    if state.get('error'):
        lines += ['', 'Error: ' + state['error']]
    (out / 'report.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def run(args):
    repo = Path(__file__).resolve().parents[2]
    work, out = args.work.resolve(), args.output.resolve()
    need(not work.exists() and not out.exists(), 'Refusing to overwrite measurement')
    work.mkdir(parents=True)
    out.mkdir(parents=True)
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    started = time.perf_counter()
    state = dict(schemaVersion=1, status='preparing', sourceCommit=git(repo, 'rev-parse', 'HEAD'),
                 runId=os.environ.get('GITHUB_RUN_ID'), runAttempt=os.environ.get('GITHUB_RUN_ATTEMPT'),
                 baselineHead=BASELINE_HEAD, baselineBinarySource=BASELINE_BINARY_SOURCE,
                 baselineRunId=BASELINE_RUN, candidateSource=args.candidate_source,
                 candidateRunId=args.candidate_run_id, samples=[], budgetSeconds=args.budget,
                 armorPartitions=args.armor_partitions, pairCount=args.pairs,
                 machine=dict(system=platform.platform(), processor=platform.processor(),
                              runnerImage=os.environ.get('ImageVersion')),
                 comparison='All actual published files compared with filecmp(shallow=False), outside process timing.')
    reference = None

    def check_budget():
        remaining = args.budget - (time.perf_counter() - started)
        need(remaining > 0, 'Measurement budget exhausted')
        return remaining

    try:
        source = git(repo, 'rev-parse', args.candidate_source + '^{commit}')
        need(source == args.candidate_source, 'Candidate source must be its full commit SHA')
        subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor', BASELINE_HEAD, source], check=True)
        closure = ('SDK', 'recovery/src', 'recovery/build', 'recovery/probes', 'ui',
                   'recovery/scripts/Build*', 'recovery/scripts/PrepareLegacyTexture.ps1',
                   'recovery/scripts/RestoreUiDependencies.ps1')
        need(not git(repo, 'diff', '--name-only', BASELINE_BINARY_SOURCE, BASELINE_HEAD, '--', *closure),
             'Pinned accepted baseline differs from requested base production inputs')
        changes = git(repo, 'diff', '--name-only', BASELINE_HEAD, source, '--', *closure).splitlines()
        allowed = ('recovery/src/App/', 'recovery/src/Managed/Core/', 'recovery/src/Managed/Metrics/',
                   'recovery/src/Managed/GameDataVerifier/')
        need(changes and all(path.startswith(allowed) for path in changes),
             'Expected only recorded asset pipeline production changes: ' + repr(changes))
        state['productionChanges'] = changes
        binaries, runtime_hashes = {}, {}
        for label, directory, commit, pinned in (
                ('baseline', args.baseline_build.resolve(), BASELINE_BINARY_SOURCE, BASELINE_FILES),
                ('candidate', args.candidate_build.resolve(), source, None)):
            binaries[label], runtime_hashes[label] = product(directory, commit, pinned)
            shutil.copy2(directory / 'build.json', out / (label + '-build.json'))
        state['runtimeFiles'] = runtime_hashes
        mod = extract_upheaval(repo, work)
        state['originalInput'] = inventory(mod)
        state['originalInput'].update(sourceCommit=HISTORICAL, sourceTree=UPHEAVAL_TREE)
        expand_armor(repo, mod, args.armor_partitions)
        state['workload'] = inventory(mod)
        state['workloadScope'] = ('Complete untouched historical Upheaval, all auto-discovered streams/BIG/SKUDEF/Additional'
                                  if not args.armor_partitions else
                                  'Complete historical Upheaval plus %d reachable Armor partitions (%d additional documents / %d assets)'
                                  % (args.armor_partitions, args.armor_partitions * 256, args.armor_partitions * 8192))
        state['setupSeconds'] = time.perf_counter() - started
        state['status'] = 'running'
        save(out, state)

        def sample(label, trial, mode, phase, destination, performance='off'):
            nonlocal reference
            name = '%02d-%s-%s-%s-%s-%s' % (len(state['samples']), label, trial, mode, phase, performance)
            evidence = out / name
            evidence.mkdir()
            command = [str(binaries[label]), 'build', '--sdk-root', str(repo / 'SDK'), '--mod', str(mod),
                       '--out', str(destination), '--jobs', '4', '--performance', performance]
            if mode == 'none':
                command += ['--sc=false', '--bc=false']
            row = dict(implementation=label, trial=trial, mode=mode, phase=phase,
                       performance=performance, command=command, status='started')
            if phase == 'cold':
                row['coldNamespaceInitiallyAbsent'] = not destination.exists()
                need(row['coldNamespaceInitiallyAbsent'], 'Cold output/cache namespace already exists')
            state['samples'].append(row)
            save(out, state)
            report_path = destination / 'build-report.json'
            report_path.unlink(missing_ok=True)
            try:
                timeout = min(args.timeout, check_budget())
                with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                    tick = time.perf_counter()
                    process = subprocess.Popen(command, cwd=binaries[label].parent, stdout=stdout, stderr=stderr)
                    try:
                        process.wait(timeout=timeout)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
                        row.update(status='timed_out', wallMs=(time.perf_counter() - tick) * 1000,
                                   exitCode=process.returncode)
                        raise
                    row.update(wallMs=(time.perf_counter() - tick) * 1000,
                               exitCode=process.returncode, **process_metrics(process))
                report = json.loads(report_path.read_text(encoding='utf-8-sig'))
                row.update(reportElapsedMs=report['elapsedMs'], cache=report.get('cache'),
                           diagnosticCounts=report.get('diagnosticCounts'), phases=phases(report))
                need(process.returncode == 0 and report['status'] == 'succeeded', name + ' failed')
                if mode == 'default':
                    cache = report['cache']
                    need(cache['sessionCache'] is True, 'Default session cache is disabled')
                    if phase == 'hot':
                        need(cache['xmlParse'] == 0 and cache['instanceCompile'] == 0 and cache['documentReuse'] > 0,
                             'Hot process did not reuse completed documents')
                    # A cold process may reuse documents compiled by an earlier
                    # stream in this same process. The new namespace, rather
                    # than zero cross-stream reuse counters, proves cold input.
                published = destination / 'published'
                files = {p.relative_to(published).as_posix(): p for p in published.rglob('*') if p.is_file()}
                need(files, 'No published output')
                identities = {name: dict(bytes=p.stat().st_size, sha256=sha(p)) for name, p in files.items()}
                write(evidence / 'published.json', identities)
                if reference is None:
                    reference = work / 'reference-published'
                    shutil.copytree(published, reference)
                expected = {p.relative_to(reference).as_posix(): p for p in reference.rglob('*') if p.is_file()}
                filecmp.clear_cache()
                need(files.keys() == expected.keys() and all(filecmp.cmp(expected[k], files[k], shallow=False) for k in files),
                     'Raw published bytes differ across implementation/cache state: ' + name)
                row.update(status='passed', rawPublishedIdentical=True, publishedBytes=sum(p.stat().st_size for p in files.values()))
            except Exception as error:
                row.update(error=str(error))
                if row['status'] == 'started':
                    row['status'] = 'failed'
                raise
            finally:
                if report_path.exists():
                    shutil.copy2(report_path, evidence / 'build-report.json')
                state['elapsedSeconds'] = time.perf_counter() - started
                save(out, state)
                print(json.dumps({k: row.get(k) for k in ('implementation', 'trial', 'mode', 'phase', 'performance', 'status', 'wallMs')}), flush=True)

        for trial in range(args.pairs):
            order = ('baseline', 'candidate') if trial % 2 == 0 else ('candidate', 'baseline')
            for label in order:
                destination = work / 'outputs' / ('default-%d-%s' % (trial, label))
                sample(label, trial, 'default', 'cold', destination)
                for hot in range(2):
                    sample(label, trial, 'default', 'hot', destination)
        for label in ('candidate', 'baseline'):
            sample(label, 0, 'none', 'cold', work / 'outputs' / ('nocache-' + label))
        for label in ('baseline', 'candidate'):
            destination = work / 'outputs' / ('detailed-' + label)
            sample(label, 0, 'default', 'cold', destination, 'detailed')
            sample(label, 0, 'default', 'hot', destination, 'detailed')
        need(inventory(mod)['fingerprint'] == state['workload']['fingerprint'], 'Measured workload changed')
        check_budget()
        state['status'] = 'passed'
    except Exception as error:
        state.update(status='failed', error=str(error), errorType=type(error).__name__)
        raise
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        save(out, state)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-build', type=Path, required=True)
    parser.add_argument('--candidate-build', type=Path, required=True)
    parser.add_argument('--candidate-source', required=True)
    parser.add_argument('--candidate-run-id', required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--pairs', type=int, choices=range(1, 4), default=3)
    parser.add_argument('--armor-partitions', type=int, choices=range(0, 9), default=0)
    parser.add_argument('--budget', type=int, choices=range(60, 781), metavar='SECONDS', default=780)
    parser.add_argument('--timeout', type=int, choices=range(10, 181), metavar='SECONDS', default=180)
    run(parser.parse_args())
