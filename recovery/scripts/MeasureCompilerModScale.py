#!/usr/bin/env python3
"""Measure small/full-mymod, medium/real-texture-subset, and large/full-Upheaval."""
import argparse
import copy
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import tarfile
import time
import xml.etree.ElementTree as E

from BenchmarkCompilerCache import process_metrics

HISTORICAL = 'bacfc69fc3e71c8116d1f22b933b8b6173170ecf'
UPHEAVAL_TREE = '5551fc26a6f96b62613443e0d0fa2958c14cbce0'
NS = 'uri:ea.com:eala:asset'
E.register_namespace('', NS)


def need(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def inventory(root):
    return [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)}
            for p in sorted(root.rglob('*')) if p.is_file()]


def dimensions(root):
    files = [p for p in root.rglob('*') if p.is_file()]
    xml = [p for p in files if p.suffix.lower() in ('.xml', '.w3x')]
    declarations = 0
    for path in xml:
        try:
            doc = E.parse(path).getroot()
        except E.ParseError:
            continue
        if doc.tag == '{%s}AssetDeclaration' % NS:
            declarations += sum(node.tag.rsplit('}', 1)[-1] not in ('Includes', 'Defines', 'Tags') for node in doc)
    return {'files': len(files), 'bytes': sum(p.stat().st_size for p in files),
            'xmlAndW3xFiles': len(xml), 'declaredAssetsInInputFiles': declarations}


def prepare(repo, work, evidence):
    # Full source files are copied; only the explicitly labelled medium sample is generated.
    small = work / 'small-mymod'
    shutil.copytree(repo / 'SDK/Mods/mymod', small)
    tree = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', HISTORICAL + ':SDK/Mods/Upheaval'], text=True).strip()
    need(tree == UPHEAVAL_TREE, 'Historical Upheaval tree changed')
    archive = work / 'upheaval.tar'
    with archive.open('wb') as stream:
        subprocess.run(['git', '-C', str(repo), 'archive', HISTORICAL, 'SDK/Mods/Upheaval'], stdout=stream, check=True, timeout=120)
    extracted = work / 'historical'
    extracted.mkdir()
    with tarfile.open(archive) as stream:
        stream.extractall(extracted, filter='data')
    large = extracted / 'SDK/Mods/Upheaval'
    archive.unlink()
    medium = work / 'medium-upheaval-textures'
    (medium / 'Data').mkdir(parents=True)
    includes = E.Element('{%s}AssetDeclaration' % NS)
    include_list = E.SubElement(includes, '{%s}Includes' % NS)
    sources = []
    ids = set()
    for source in sorted(large.rglob('*.xml')):
        try:
            doc = E.parse(source).getroot()
        except E.ParseError:
            continue
        if doc.tag != '{%s}AssetDeclaration' % NS:
            continue
        selected = [n for n in doc if n.tag == '{%s}Texture' % NS and n.get('File', '').upper().startswith('ART:') and n.get('id') not in ids]
        if not selected:
            continue
        target = 'textures-%03d.xml' % len(sources)
        generated = E.Element('{%s}AssetDeclaration' % NS)
        for node in selected:
            need(node.get('id') not in ids and not node.get('inheritFrom') and not len(node), 'Texture selection gained dependencies or duplicate identity')
            ids.add(node.get('id'))
            generated.append(copy.deepcopy(node))
            relative = node.get('File')[4:].replace('\\', '/')
            input_path = large / 'Art' / relative
            need(input_path.is_file(), 'Texture input is not local to historical MOD: ' + relative)
            target_path = medium / 'Art' / relative
            target_path.parent.mkdir(parents=True, exist_ok=True)
            if not target_path.exists():
                shutil.copy2(input_path, target_path)
        E.ElementTree(generated).write(medium / 'Data' / target, encoding='utf-8', xml_declaration=True)
        E.SubElement(include_list, '{%s}Include' % NS, {'type': 'all', 'source': target})
        sources.append({'sourcePath': source.relative_to(large).as_posix(), 'sourceSha256': sha(source),
                        'generatedPath': 'Data/' + target, 'assetIds': [n.get('id') for n in selected]})
    need(len(ids) == 96, 'Pinned real-texture sample changed')
    E.ElementTree(includes).write(medium / 'Data/Mod.xml', encoding='utf-8', xml_declaration=True)
    result = []
    for tier, name, mod, full, scope in [
        ('small', 'mymod', small, True, 'Complete current mymod input; all auto-discovered content, BIG+SKUDEF and Additional.'),
        ('medium', 'upheaval-textures-96', medium, False, 'Generated include entry and unchanged Texture elements from historical Upheaval; 96 unique Texture identities and their local real ART files. Texture workload only, not an independent full medium MOD.'),
        ('large', 'upheaval-full', large, True, 'Complete untouched historical Upheaval, all auto-discovered base/APT/maps, BIG+SKUDEF and Additional. Success is established only by this run, not older component/SDK evidence.')]:
        records = inventory(mod)
        write(evidence / ('inputs-' + tier + '.json'), records)
        result.append({'tier': tier, 'name': name, 'modRoot': str(mod), 'fullModInputCopy': full,
                       'scope': scope, 'dimensions': dimensions(mod),
                       'inputFingerprint': hashlib.sha256(json.dumps(records, sort_keys=True).encode()).hexdigest(),
                       'sourceCommit': HISTORICAL if tier != 'small' else subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
                       'sourceTree': UPHEAVAL_TREE if tier != 'small' else None})
    write(evidence / 'medium-sources.json', sources)
    write(evidence / 'workloads.json', result)
    return result


def phases(report):
    rows = report.get('performance', {}).get('entries', [])
    total = lambda name: sum(row['totalMs'] for row in rows if row['name'] == name)
    return {name: total(name) for name in [
        'stage.plan', 'stage.compile', 'backend.initialize', 'core.providers', 'core.schema', 'core.schema.read',
        'core.schema.compile', 'core.schema.metadata', 'cache.prepare', 'cache.load', 'core.process',
        'core.xml.parse', 'core.xml.validate', 'core.asset.compile', 'core.asset.commit', 'cache.save',
        'artifacts.collect', 'stage.package', 'package.big', 'package.lod', 'package.additional-inputs',
        'package.additional-merge', 'package.hash', 'stage.publish', 'publish.prepare', 'cache.commit']}


def comparison_key(report, destination):
    found = {}
    for artifact in report['artifacts']:
        path = Path(artifact['Path'])
        need(path.is_relative_to(destination / 'published'), 'Published artifact escaped output root')
        need(path.stat().st_size == artifact['Size'], 'Reported output bytes differ')
        found[path.relative_to(destination / 'published').as_posix().lower()] = (artifact['Size'], sha(path))
    need(found, 'No published artifacts')
    return found


def summary(samples):
    rows = []
    for tier in ('small', 'medium', 'large'):
        for phase in ('cold', 'hot'):
            subset = [s for s in samples if s['tier'] == tier and s['phase'] == phase and s['status'] == 'passed']
            if not subset:
                continue
            row = {'tier': tier, 'phase': phase, 'n': len(subset),
                   'wallMs': statistics.median(s['wallMs'] for s in subset),
                   'cpuMs': statistics.median(s['cpuMs'] for s in subset),
                   'peakWorkingSetBytes': statistics.median(s['peakWorkingSetBytes'] for s in subset),
                   'publishedBytes': statistics.median(s['publishedBytes'] for s in subset),
                   'phases': {name: statistics.median(s['phases'][name] for s in subset) for name in subset[0]['phases']},
                   'cache': {key: statistics.median(s['cache'][key] for s in subset) for key in
                             ('xmlParse', 'instanceCompile', 'fileHashReads', 'fileHashReuses', 'fileHashBytesRead')},
                   'topCompilers': []}
            compiler_names = set(n for s in subset for n in s['compilers'])
            row['topCompilers'] = sorted([
                {'name': name, 'totalMs': statistics.median(s['compilers'].get(name, {}).get('totalMs', 0) for s in subset),
                 'count': statistics.median(s['compilers'].get(name, {}).get('count', 0) for s in subset)}
                for name in compiler_names], key=lambda r: r['totalMs'], reverse=True)[:10]
            rows.append(row)
    return rows


def save(out, state):
    state['summary'] = summary(state['samples'])
    write(out / 'measurement.json', state)
    lines = ['# New SDK MOD size timing', '', 'Status: ' + state['status'], '',
             'Release; default caches; detailed profiling; auto content + BIG/SKUDEF/Additional.',
             'Cold = empty compiler caches. Hot = fresh process, compiler caches retained. OS page cache uncontrolled.',
             'Preparation, build and verification excluded from sample wall time. Medium is a labelled real Texture subset.', '',
             '| Tier | Phase | n | Process ms | CPU ms | Compile ms | Package ms | Publish ms | Assets compiled | XML parsed |',
             '| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
    for r in state['summary']:
        p = r['phases']
        lines.append('| {} | {} | {} | {:.2f} | {:.2f} | {:.2f} | {:.2f} | {:.2f} | {} | {} |'.format(
            r['tier'], r['phase'], r['n'], r['wallMs'], r['cpuMs'], p['stage.compile'], p['stage.package'],
            p['stage.publish'], r['cache']['instanceCompile'], r['cache']['xmlParse']))
    for s in state['samples']:
        if s['status'] != 'passed':
            lines += ['', 'Failure: ' + s['label'] + ': ' + s.get('error', str(s.get('exitCode')))]
    lines += ['', 'Nested totalMs rows overlap; do not add core.process to its XML/asset children.',
              'Tiny sample counts describe this VM/workload; no universal speedup claim.']
    (out / 'measurement.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def run(args):
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    repo, exe, work, out = (Path(v).resolve() for v in (args.repo, args.exe, args.work, args.output))
    need(not work.exists() and not out.exists(), 'Work/evidence directory already exists')
    work.mkdir(parents=True)
    out.mkdir(parents=True)
    workloads = prepare(repo, work, out)
    state = {'schemaVersion': 1, 'status': 'started', 'sourceCommit': args.source_commit,
             'exeSha256': sha(exe), 'workloads': workloads, 'samples': [],
             'limits': {'perProcessSeconds': args.timeout, 'measurementBudgetSeconds': args.budget, 'hotRuns': args.hot_runs}}
    started = time.perf_counter()
    references = {}
    failed = set()
    try:
        for index in range(args.hot_runs + 1):
            order = workloads if index % 2 == 0 else list(reversed(workloads))
            for workload in order:
                tier = workload['tier']
                if tier in failed:
                    continue
                phase = 'cold' if index == 0 else 'hot'
                label = tier + '-' + phase + '-' + str(index)
                evidence = out / label
                evidence.mkdir()
                destination = work / 'outputs' / tier
                remaining = args.budget - (time.perf_counter() - started)
                need(remaining > 0, 'Measurement budget exhausted')
                command = [str(exe), 'build', '--sdk-root', str(repo / 'SDK'), '--mod', workload['modRoot'],
                           '--out', str(destination), '--performance', 'detailed']
                sample = {'label': label, 'tier': tier, 'phase': phase, 'round': index, 'status': 'started',
                          'command': command, 'timeoutSeconds': min(args.timeout, remaining)}
                state['samples'].append(sample)
                write(evidence / 'request.json', sample)
                tick = time.perf_counter()
                try:
                    with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                        process = subprocess.Popen(command, cwd=exe.parent, stdout=stdout, stderr=stderr)
                        try:
                            process.wait(timeout=sample['timeoutSeconds'])
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait(timeout=5)
                            raise TimeoutError('Compiler process exceeded its time budget')
                        sample.update(wallMs=(time.perf_counter() - tick) * 1000, exitCode=process.returncode,
                                      **process_metrics(process))
                    report_path = destination / 'build-report.json'
                    report = None
                    if report_path.is_file():
                        shutil.copy2(report_path, evidence / 'build-report.json')
                        report = json.loads(report_path.read_text(encoding='utf-8-sig'))
                    events = destination / 'build.events.jsonl'
                    if events.is_file():
                        shutil.copy2(events, evidence / events.name)
                    need(process.returncode == 0 and report and report['status'] == 'succeeded',
                         'Build failed with exit ' + str(process.returncode))
                    need(report['performance']['level'] == 'detailed', 'Profiling was not enabled')
                    content = comparison_key(report, destination)
                    if tier not in references:
                        references[tier] = content
                    need(content == references[tier], 'Cold/hot package output bytes differ')
                    compiler_rows = {}
                    for row in report['performance']['entries']:
                        if not row['name'].startswith('core.compiler.'):
                            continue
                        entry = compiler_rows.setdefault(row['name'], {'count': 0, 'totalMs': 0})
                        entry['count'] += row['count']
                        entry['totalMs'] += row['totalMs']
                    sample.update(status='passed', phases=phases(report), compilers=compiler_rows, cache=report['cache'],
                                  reportElapsedMs=report['elapsedMs'], streamCount=len(report['plan']['Tasks']),
                                  publishedBytes=sum(a['Size'] for a in report['artifacts']), outputIdentical=True)
                    from jsonschema import Draft202012Validator
                    Draft202012Validator(json.loads((repo / 'recovery/protocol/build-report.schema.json').read_text())).validate(report)
                except Exception as error:
                    sample.update(status='failed', wallMs=(time.perf_counter() - tick) * 1000, error=str(error))
                    failed.add(tier)
                write(evidence / 'sample.json', sample)
                state['elapsedSeconds'] = time.perf_counter() - started
                save(out, state)
                print(json.dumps({k: sample.get(k) for k in ('label', 'status', 'wallMs', 'exitCode', 'error')}), flush=True)
        state['status'] = 'passed' if not failed else 'partial-failure'
    except Exception as error:
        state.update(status='failed', error=str(error))
        raise
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        save(out, state)
    return 0 if state['status'] == 'passed' else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('repo', 'exe', 'work', 'output', 'source-commit'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--timeout', type=int, choices=range(30, 601), default=180)
    parser.add_argument('--budget', type=int, choices=range(60, 1801), default=900)
    parser.add_argument('--hot-runs', type=int, choices=(1, 2, 3), default=2)
    raise SystemExit(run(parser.parse_args()))
