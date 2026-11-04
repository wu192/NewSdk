#!/usr/bin/env python3
"""One-VM full Upheaval A/B with unchanged legacy caches and raw byte comparison."""
import argparse
import ctypes
import filecmp
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
from MeasureCompilerModScale import prepare, sha, comparison_key, write
from BenchmarkCompilerCache import process_metrics
from CompilerCachePaths import cache_root


def need(value, message):
    if not value:
        raise AssertionError(message)


def animation_edit(mod):
    # FrameRate is xs:unsignedInt in the original AssetTypeW3D.xsd.
    # Plan and parse this change before any expensive compiler measurement.
    import xml.etree.ElementTree as ET
    pattern = re.compile(rb'FrameRate="(\d+)"')
    for path in sorted(mod.rglob('*')):
        if path.suffix.lower() not in ('.w3x', '.xml'):
            continue
        data = path.read_bytes()
        if b'<W3DAnimation' not in data:
            continue
        match = pattern.search(data)
        if match and int(match.group(1)) < 4294967295:
            value = str(int(match.group(1))+1).encode('ascii')
            changed = data[:match.start(1)]+value+data[match.end(1):]
            ET.fromstring(changed)
            return path, data, changed
    raise AssertionError('No real Upheaval animation available for single-asset edit')


def run(args):
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    repo, exe, work, out = (Path(x).resolve() for x in (args.repo, args.exe, args.work, args.output))
    need(not work.exists() and not out.exists(), 'Refusing to overwrite measurement')
    work.mkdir(parents=True); out.mkdir(parents=True)
    mod = None
    state = {'schemaVersion': 1, 'sourceCommit': args.source_commit, 'binarySourceCommit': args.binary_source_commit or args.source_commit,
             'binarySha256': sha(exe), 'runId': os.environ.get('GITHUB_RUN_ID'), 'status': 'started',
             'workload': None, 'samples': [],
             'comparison': 'Actual published bytes compared with filecmp(shallow=False); all report artifact hashes independently checked.',
             'sampleCountCaution': 'One cold sample per jobs/mode; OS page cache uncontrolled. No-cache starts jobs1; defaults reverses order4,1,auto. Do not treat small differences as deterministic regressions.',
             'boundary': 'Process wall; fixture setup, baseline copy and comparison excluded. All samples on one Windows VM, same executable. Off samples compared only with off; detailed samples used for attribution.'}
    references = {}
    diagnostic_references = {}
    started = time.perf_counter()

    def sample(label, jobs, cache, phase, performance='off', changed=False):
        need(time.perf_counter()-started < args.budget, 'Measurement budget exhausted')
        evidence = out / label; evidence.mkdir()
        destination = work / 'outputs' / (('defaults-' if cache else 'nocache-')+str(jobs))
        if phase == 'diagnostic':
            destination = work / 'outputs' / ('diagnostic-'+str(jobs))
        command = [str(exe), 'build', '--sdk-root', str(repo / 'SDK'), '--mod', str(mod), '--out', str(destination),
                   '--jobs', str(jobs), '--performance', performance, '--mod-version', '1.0']
        namespace = cache_root(repo / 'SDK', mod) / 'tests' / __import__('hashlib').sha256(str(destination).encode()).hexdigest()[:10]
        command += ['/scd:' + str(namespace / 's'), '/bcd:' + str(namespace / 'b'), '/iod:' + str(namespace / 'i')]
        if not cache:
            command += ['--sc=false', '--bc=false']
        row = {'label': label, 'jobs': str(jobs), 'legacyCaches': cache, 'phase': phase,
               'performanceLevel': performance, 'command': command, 'status': 'started'}
        state['samples'].append(row); write(out / 'measurement.json', state)
        tick = time.perf_counter()
        with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
            process = subprocess.Popen(command, cwd=exe.parent, stdout=stdout, stderr=stderr)
            try:
                process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait(); raise TimeoutError(label+' process timed out')
            row.update(wallMs=(time.perf_counter()-tick)*1000, exitCode=process.returncode, **process_metrics(process))
        report_path = destination / 'build-report.json'
        if report_path.exists():
            shutil.copy2(report_path, evidence / report_path.name)
        events = destination / 'build.events.jsonl'
        if events.exists():
            shutil.copy2(events, evidence / events.name)
        report = json.loads(report_path.read_text(encoding='utf-8-sig'))
        need(process.returncode == 0 and report['status'] == 'succeeded', label+' build failed')
        content = comparison_key(report, destination)
        group = 'changed' if changed else 'original'
        published = destination / 'published'
        if group not in references:
            baseline = work / ('baseline-'+group)
            shutil.copytree(published, baseline)
            references[group] = content
            write(out / ('outputs-'+group+'.json'), content)
        need(content == references[group], label+' artifact layout/digests differ')
        baseline = work / ('baseline-'+group)
        for relative in content:
            need(filecmp.cmp(baseline / relative, published / relative, shallow=False), label+' raw bytes differ: '+relative)
        counts = report['diagnosticCounts']
        need(counts['error'] == 0, label+' reported compilation errors')
        diagnostic_key = (group, cache, phase)
        if diagnostic_key not in diagnostic_references:
            diagnostic_references[diagnostic_key] = counts
        need(counts == diagnostic_references[diagnostic_key], label+' warning/error counts differ between job limits')
        cache_report = report['cache']; parallel = cache_report.get('parallel')
        if phase == 'hot':
            need(cache_report['xmlParse'] == 0 and cache_report['instanceCompile'] == 0, label+' hot cache parsed/compiled assets')
            if parallel is not None:
                need(parallel['backendCalls'] == 0 and parallel['prepareMs'] == 0, label+' hot cache prepared backend work')
        if phase == 'diagnostic':
            need(parallel and parallel['backendCalls'] == cache_report['instanceCompile'], 'Actual backend metrics missing')
            if str(jobs) != '1' and parallel['effectiveJobs'] > 1:
                need(parallel['maxBackendConcurrency'] > 1, 'Resources admitted multiple workers but full Upheaval showed no real backend overlap')
        if phase == 'modified':
            need(0 < cache_report['instanceCompile'] < 100, 'Single asset mutation invalidated an unexpectedly wide closure')
        row.update(status='passed', rawOutputIdentical=True, cache=cache_report, reportElapsedMs=report['elapsedMs'],
                   publishedBytes=sum(a['Size'] for a in report['artifacts']), errors=counts['error'], warnings=counts['warning'])
        write(evidence / 'sample.json', row); write(out / 'measurement.json', state)
        print(json.dumps({key:row[key] for key in ('label','status','wallMs','exitCode')}), flush=True)

    try:
        setup_started = time.perf_counter()
        workloads = prepare(repo, work, out)
        state['workload'] = next(x for x in workloads if x['tier'] == 'large')
        mod = Path(state['workload']['modRoot'])
        edit_path, edit_original, edit_changed = animation_edit(mod)
        state['setupSeconds'] = time.perf_counter()-setup_started
        started = time.perf_counter()
        # Same instrument level and executable for every throughput comparison.
        for jobs in ('1','4','auto'):
            sample('nocache-cold-'+jobs,jobs,False,'cold')
        for jobs in ('4','1','auto'):
            sample('defaults-cold-'+jobs,jobs,True,'cold')
            sample('defaults-hot-'+jobs,jobs,True,'hot')
            sample('defaults-hot-repeat-'+jobs,jobs,True,'hot')
            sample('defaults-hot-diagnostic-'+jobs,jobs,True,'hot','detailed')
        for jobs in ('1','4','auto'):
            sample('diagnostic-cold-'+jobs,jobs,False,'diagnostic','detailed')
        # Same roots and inputs: a real single animation edit invalidates only its
        # existing dependency closure. Keep bytes/encoding outside the token.
        edit_path.write_bytes(edit_changed)
        state['mutation'] = {'path':edit_path.relative_to(mod).as_posix(),
                             'beforeSha256':__import__('hashlib').sha256(edit_original).hexdigest(),
                             'afterSha256':sha(edit_path)}
        sample('modified-independent-control','1',False,'modified-control',changed=True)
        for jobs in ('1','4','auto'):
            sample('defaults-modified-'+jobs,jobs,True,'modified',changed=True)
        state['status']='passed'
        cold = {s['jobs']:s['wallMs'] for s in state['samples'] if s['phase']=='cold' and s['legacyCaches']}
        state['defaultColdSpeedupVsJobs1']={jobs:cold['1']/cold[jobs] for jobs in cold}
        return 0
    except Exception as error:
        state.update(status='failed',error=str(error))
        for row in state['samples']:
            if row['status'] == 'started':
                row.update(status='failed',error=str(error))
        raise
    finally:
        state['elapsedSeconds']=time.perf_counter()-started
        write(out / 'measurement.json', state)
        lines=['# Full Upheaval backend parallel A/B','', 'Status: '+state['status'],'',
               '| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |','| --- | ---: | ---: | ---: | ---: |']
        for row in state['samples']:
            cache=row.get('cache',{}); parallel=cache.get('parallel') or {}
            lines.append('| {} | {:.1f} | {:.1f} | {} | {} |'.format(row['label'],row.get('wallMs',0),row.get('cpuMs',0),cache.get('instanceCompile',''),parallel.get('maxBackendConcurrency','off')))
        lines+=['','Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.',state.get('error','')]
        (out/'measurement.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('repo','exe','work','output','source-commit'):
        parser.add_argument('--'+name,required=True)
    parser.add_argument('--binary-source-commit')
    parser.add_argument('--timeout',type=int,default=240)
    parser.add_argument('--budget',type=int,default=1200)
    raise SystemExit(run(parser.parse_args()))
