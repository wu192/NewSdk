#!/usr/bin/env python3
"""Same-VM paired large edits on complete Upheaval with verified warm state."""
import argparse
import ctypes
import filecmp
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import time
import traceback

from jsonschema import Draft202012Validator
from BenchmarkCompilerCache import process_metrics
from MeasureCompilerCachePaired import git, need, product
from MeasureCompilerIncrementalPaired import BASELINE_BINARY_SOURCE, BASELINE_RUN, BASELINE_FILES
from MeasureCompilerModScale import phases, sha, write
import UpheavalEditWorkload as workload

DETAIL_PHASES = ('core.xml.instance.hash', 'core.xml.instance.references', 'core.xml.schema.validate',
                'core.xml.instances.validate', 'core.xml.overrides', 'core.xml.expressions',
                'core.dependencies.inheritance.identity', 'core.xml.inheritance.frontend',
                'core.output.references', 'core.output.reference-closure', 'core.output.order')


def summary(samples):
    rows = []
    scenes = dict.fromkeys(r['scenario'] for r in samples)
    for scene in scenes:
        for level in ('off', 'detailed'):
            for phase in ('hot_before', 'modified', 'hot_after'):
                groups = {label: [r for r in samples if r['scenario'] == scene and r['performance'] == level and
                                 r['phase'] == phase and r['implementation'] == label and r['status'] == 'passed']
                          for label in ('baseline', 'candidate')}
                if not all(groups.values()):
                    continue
                row = dict(scenario=scene, performance=level, phase=phase)
                for label, data in groups.items():
                    wall = [r['wallMs'] for r in data]
                    elapsed = [r['reportElapsedMs'] for r in data]
                    row[label] = dict(n=len(data), medianMs=statistics.median(wall), minMs=min(wall), maxMs=max(wall),
                                      medianReportMs=statistics.median(elapsed),
                                      xmlParse=[r['cache']['xmlParse'] for r in data],
                                      instanceCompile=[r['cache']['instanceCompile'] for r in data])
                row['reductionPercent'] = 100 * (1 - row['candidate']['medianMs'] / row['baseline']['medianMs'])
                rows.append(row)
    return rows


def save(out, state):
    state['summary'] = summary(state['samples'])
    write(out / 'measurement.json', state)
    lines = ['# Complete Upheaval large-edit paired comparison', '', 'Status: ' + state['status'], '',
             'Group ' + state['scenarioGroup'] + ': each scenario uses the same Windows VM, full MOD input and jobs=4 for both implementations. '
             'Three off edit pairs; detailed is separate attribution. New process per sample; OS page cache uncontrolled. '
             'Groups A/B may use different machines and their absolute times must not be compared.', '',
             'Warm preparation, same-path output/cache snapshot restoration and byte comparisons are outside edit timing. '
             'After each restore, an unmeasured verification must parse/compile zero; measured hot-before and hot-after must also be zero. '
             'The compiler still normally packages BIG/SKUDEF on every build.', '',
             '| Scenario | Level | Phase | Baseline n / median ms | Candidate n / median ms | Reduction | Parse b/c | Compile b/c |',
             '| --- | --- | --- | ---: | ---: | ---: | --- | --- |']
    for row in state['summary']:
        b, c = row['baseline'], row['candidate']
        lines.append('| %s | %s | %s | %d / %.3f | %d / %.3f | %.2f%% | %s / %s | %s / %s |' % (
            row['scenario'], row['performance'], row['phase'], b['n'], b['medianMs'], c['n'], c['medianMs'],
            row['reductionPercent'], b['xmlParse'], c['xmlParse'], b['instanceCompile'], c['instanceCompile']))
    lines += ['', 'All publication comparisons read actual bytes, including BIG/SKUDEF/Additional. '
              'Each trial restores the scenario setup, so edits do not accumulate. Failed or unmeasured scenarios remain explicit. '
              'Full production/SDK acceptance is reused only through the recorded immutable binary/input identity.']
    for scene in state['scenarios']:
        lines += ['', '- ' + scene['name'] + ': ' + scene['status']]
        if scene.get('error'):
            lines += ['  ' + scene['error']]
    if state.get('error'):
        lines += ['', state['error']]
    (out / 'report.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def published(directory):
    root = directory / 'published'
    files = {p.relative_to(root).as_posix(): p for p in root.rglob('*') if p.is_file()}
    need(files, 'No published output: ' + str(directory))
    return files


def identical(left, right):
    filecmp.clear_cache()
    return left.keys() == right.keys() and all(filecmp.cmp(left[k], right[k], shallow=False) for k in left)


def counters(report):
    result = {}
    for row in report.get('performance', {}).get('counters', []):
        name, value = row['name'], row['value']
        result[name] = max(result.get(name, 0), value) if name.endswith(('maxDepth', 'maxConcurrency')) else result.get(name, 0) + value
    return result


def run(args):
    repo = Path(__file__).resolve().parents[2]
    work, out = args.work.resolve(), args.output.resolve()
    need(not work.exists() and not out.exists(), 'Refusing to overwrite benchmark work or evidence')
    work.mkdir(parents=True); out.mkdir(parents=True)
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    started = time.perf_counter()
    state = dict(schemaVersion=1, status='preparing', sourceCommit=git(repo, 'rev-parse', 'HEAD'),
                 runId=os.getenv('GITHUB_RUN_ID'), runAttempt=os.getenv('GITHUB_RUN_ATTEMPT'),
                 baselineSource=BASELINE_BINARY_SOURCE, baselineRunId=BASELINE_RUN,
                 candidateSource=args.candidate_source, candidateRunId=args.candidate_run_id,
                 scenarioGroup=args.scenario_group, pairCount=args.pairs, jobs=4, budgetSeconds=args.budget,
                 samples=[], scenarios=[], machine=dict(system=platform.platform(), processor=platform.processor(),
                                                       runnerImage=os.getenv('ImageVersion')),
                 scope='Complete historical Upheaval, real large developer edits; explicitly recorded SDK parent overlays or lifecycle supplements only')
    validator = Draft202012Validator(json.loads((repo / 'recovery/protocol/build-report.schema.json').read_text()))
    references = {}
    binaries = {}
    current = None

    def remaining():
        seconds = args.budget - (time.perf_counter() - started)
        need(seconds > 0, 'Whole-group budget exhausted')
        return min(args.timeout, seconds)

    def checkpoint():
        state['elapsedSeconds'] = time.perf_counter() - started
        save(out, state)

    def capture(key, files):
        target = work / 'publication-references' / key
        need(not target.exists(), 'Publication reference already exists: ' + key)
        target.mkdir(parents=True)
        identities = {}
        for name, path in files.items():
            copied = target / name; copied.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(path, copied)
            identities[name] = dict(bytes=path.stat().st_size, sha256=sha(path))
        references[key] = {name: target / name for name in files}
        write(out / ('publication-' + key + '.json'), identities)
        return references[key]

    def sample(label, trial, phase, performance, expected_generation, expected_hot=False):
        scene = current['name']
        name = '%03d-%s-%s-%s-%s-%s' % (len(state['samples']), scene, label, trial, phase, performance)
        evidence = out / name; evidence.mkdir()
        destination = current['destinations'][label]
        report_path = destination / 'build-report.json'; report_path.unlink(missing_ok=True)
        command = [str(binaries[label]), 'build', '--sdk-root', str(repo / 'SDK'), '--mod', str(current['mod']),
                   '--out', str(destination), '--jobs', '4', '--performance', performance]
        row = dict(scenario=scene, implementation=label, trial=trial, phase=phase, performance=performance,
                   command=command, status='started', formal=phase in ('hot_before', 'modified', 'hot_after'))
        if phase == 'warmup':
            row['coldNamespaceInitiallyAbsent'] = not destination.exists()
            need(row['coldNamespaceInitiallyAbsent'], 'Initial warm generation already has an output/cache namespace')
        state['samples'].append(row); checkpoint()
        try:
            timeout = remaining()
            with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                tick = time.perf_counter(); process = subprocess.Popen(command, cwd=binaries[label].parent, stdout=stdout, stderr=stderr)
                try:
                    process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait(timeout=5)
                    row.update(status='timed_out', wallMs=(time.perf_counter() - tick) * 1000)
                    raise
                except BaseException:
                    if process.poll() is None: process.kill()
                    process.wait(timeout=5)
                    raise
                row.update(exitCode=process.returncode, wallMs=(time.perf_counter() - tick) * 1000, **process_metrics(process))
            need(report_path.exists(), 'Missing build report: ' + name)
            report = json.loads(report_path.read_text(encoding='utf-8-sig')); validator.validate(report)
            row.update(reportSchemaValid=True, reportElapsedMs=report['elapsedMs'], cache=report['cache'],
                       diagnosticCounts=report['diagnosticCounts'], phases=phases(report), counters=counters(report))
            for key in DETAIL_PHASES:
                row['phases'][key] = sum(e['totalMs'] for e in report.get('performance', {}).get('entries', []) if e['name'] == key)
            need(process.returncode == 0 and report['status'] == 'succeeded' and report['diagnosticCounts']['error'] == 0,
                 'Compiler rejected edit: ' + name)
            need(report['cache']['sessionCache'] is True, 'Default session cache was disabled')
            if expected_hot:
                need(report['cache']['xmlParse'] == 0 and report['cache']['instanceCompile'] == 0,
                     'Warm state failed: %s parse=%s compile=%s' % (name, report['cache']['xmlParse'], report['cache']['instanceCompile']))
            if phase == 'modified':
                need(report['cache']['xmlParse'] > 0 or report['cache']['instanceCompile'] > 0,
                     'Mutation caused no actual frontend/backend work: ' + name)
            files = published(destination)
            key = scene + '-' + expected_generation
            reference = references.get(key)
            if reference is None:
                reference = capture(key, files)
            need(identical(reference, files), 'Published raw bytes differ across implementation/trial: ' + name)
            if phase == 'modified':
                changed = not identical(references[scene + '-original'], files)
                need(changed == current['mutation']['expectedPublicationChanged'],
                     'Unexpected publication effect for ' + scene + ': changed=' + str(changed))
                row['publicationChanged'] = changed
            row.update(status='passed', rawPublishedIdentical=True, publishedFiles=len(files), publishedBytes=sum(p.stat().st_size for p in files.values()))
        except Exception as error:
            if row['status'] == 'started': row['status'] = 'failed'
            row['error'] = str(error)
            raise
        finally:
            if report_path.exists(): shutil.copy2(report_path, evidence / 'build-report.json')
            checkpoint()
            print(json.dumps({k: row.get(k) for k in ('scenario', 'implementation', 'trial', 'phase', 'performance', 'status', 'wallMs')}), flush=True)

    try:
        source = git(repo, 'rev-parse', args.candidate_source + '^{commit}')
        need(source == args.candidate_source, 'Candidate must be a full source SHA')
        subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor', BASELINE_BINARY_SOURCE, source], check=True)
        closure = ('SDK', 'recovery/src', 'recovery/build', 'recovery/probes', 'ui', 'recovery/scripts/Build*',
                   'recovery/scripts/PrepareLegacyTexture.ps1', 'recovery/scripts/RestoreUiDependencies.ps1')
        changed = git(repo, 'diff', '--name-only', BASELINE_BINARY_SOURCE, source, '--', *closure).splitlines()
        need(changed and all(p.startswith('recovery/src/Managed/Core/') for p in changed), 'Unexpected production closure change')
        need(not git(repo, 'diff', '--name-only', source, 'HEAD', '--', *closure), 'Benchmark source changes the accepted production/input closure')
        state['productionChanges'] = changed
        state['productionTrees'] = {key: git(repo, 'rev-parse', source + ':' + key) for key in ('SDK', 'recovery/src', 'recovery/build', 'recovery/probes')}
        state['runtimeFiles'] = {}
        for label, directory, commit, pin in (
            ('baseline', args.baseline_build.resolve(), BASELINE_BINARY_SOURCE, BASELINE_FILES),
            ('candidate', args.candidate_build.resolve(), source, None)):
            binaries[label], state['runtimeFiles'][label] = product(directory, commit, pin)
            shutil.copy2(directory / 'build.json', out / (label + '-build.json'))
        selected = [dict(name=name) for name in workload.GROUPS[args.scenario_group]]
        need(selected, 'No scenarios in requested group')
        state['status'] = 'running'; checkpoint()
        failed = []
        for spec in selected:
            scene = dict(name=spec['name'], status='preparing')
            state['scenarios'].append(scene)
            mutation = None
            try:
                scene_work = work / spec['name']; scene_work.mkdir()
                mod, metadata = workload.prepare(repo, scene_work, spec['name'])
                scene.update(metadata=metadata, status='warming')
                write(out / ('inputs-' + spec['name'] + '.json'), metadata)
                current = dict(name=spec['name'], mod=mod, destinations={label: scene_work / ('out-' + label) for label in binaries})
                # Exactly one complete warm generation per implementation/scenario.
                for label in ('baseline', 'candidate'):
                    sample(label, 0, 'warmup', 'off', 'original')
                    seed = scene_work / ('seed-' + label)
                    shutil.copytree(current['destinations'][label], seed)
                for index, level in [(i, 'off') for i in range(1, args.pairs + 1)] + [(1, 'detailed')]:
                    remaining()
                    trial = level + '-' + str(index)
                    order = ('baseline', 'candidate') if (index + (level == 'detailed')) % 2 else ('candidate', 'baseline')
                    for label in binaries:
                        destination = current['destinations'][label]
                        shutil.rmtree(destination)
                        shutil.copytree(scene_work / ('seed-' + label), destination)
                    # Restored inputs may have new ChangeTime: this verification
                    # may read hashes, but it must not parse XML or call a backend.
                    for label in order: sample(label, trial, 'restore-check', level, 'original', expected_hot=True)
                    for label in reversed(order): sample(label, trial, 'hot_before', level, 'original', expected_hot=True)
                    mutation = workload.mutate(mod, spec['name']); current['mutation'] = mutation
                    scene.setdefault('mutations', []).append(dict(trial=trial, **mutation))
                    for label in order: sample(label, trial, 'modified', level, 'modified')
                    for label in reversed(order): sample(label, trial, 'hot_after', level, 'modified', expected_hot=True)
                    workload.restore(mod, mutation); mutation = None
                    checkpoint()
                scene['status'] = 'passed'
            except Exception as error:
                scene.update(status='failed', error=str(error), errorType=type(error).__name__, traceback=traceback.format_exc())
                failed.append(scene['name'])
                if mutation is not None:
                    try: workload.restore(current['mod'], mutation)
                    except Exception as cleanup: scene['restoreError'] = str(cleanup)
                if args.budget - (time.perf_counter() - started) <= 0:
                    break
            finally:
                checkpoint()
                # Keep source/evidence of failed scenes; successful data are
                # reproducible from pinned trees + mutation recipes, not uploaded.
                if scene['status'] == 'passed':
                    shutil.rmtree(scene_work)
                for key in [key for key in references if key.startswith(spec['name'] + '-')]:
                    folder = work / 'publication-references' / key
                    if folder.exists(): shutil.rmtree(folder)
                    references.pop(key, None)
        need(not failed and len(state['scenarios']) == len(selected) and all(s['status'] == 'passed' for s in state['scenarios']),
             'Incomplete/failed scenarios: ' + ', '.join(failed))
        remaining()
        state['status'] = 'passed'
    except Exception as error:
        state.update(status='failed', error=str(error), errorType=type(error).__name__)
        raise
    finally:
        checkpoint()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('baseline-build', 'candidate-build', 'work', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--candidate-source', required=True); parser.add_argument('--candidate-run-id', required=True)
    parser.add_argument('--scenario-group', choices=('A', 'B'), required=True)
    parser.add_argument('--pairs', type=int, choices=range(3, 6), default=3)
    parser.add_argument('--budget', type=int, choices=range(300, 3601), default=2100)
    parser.add_argument('--timeout', type=int, choices=range(30, 301), default=240)
    run(parser.parse_args())
