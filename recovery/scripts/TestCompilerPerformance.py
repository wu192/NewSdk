#!/usr/bin/env python3
"""Bounded production timing smoke: same bytes with profiling off/summary/detailed."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import time

from PrepareCompilerSdkBenchmark import startup_config
from BenchmarkCompilerSdkComparison import root_arguments
from CompilerCachePaths import cache_root


def need(value, message):
    if not value:
        raise ValueError(message)


def streams(directory):
    manifests = list(directory.rglob('*.manifest'))
    need(len(manifests) == 1, 'Expected one linked stream')
    return {p.relative_to(manifests[0].parent).as_posix().lower(): p.read_bytes()
            for p in manifests[0].parent.rglob('*')
            if p.is_file() and p.suffix.lower() in ('.manifest', '.bin', '.imp', '.relo', '.cdata')}


def inventory(directory):
    return {p.relative_to(directory).as_posix().lower(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in directory.rglob('*') if p.is_file()}


def validate_profile(report, level):
    need(('performance' in report) == (level != 'off'), 'Profiling switch ignored')
    if level == 'off':
        return
    profile = report['performance']
    need(profile['level'] == level and profile['frequency'] > 0, 'Timing metadata differs')
    rows = profile['entries']
    need(rows and len({(r['streamId'], r['name']) for r in rows}) == len(rows), 'Duplicate timing rows')
    for row in rows:
        need(row['count'] > 0 and 0 <= row['selfMs'] <= row['totalMs'] and
             0 <= row['maxMs'] <= row['totalMs'], 'Invalid timing arithmetic')
    names = {r['name'] for r in rows}
    need({'stage.plan', 'stage.compile', 'core.process'} <= names,
         'Missing main build phases')
    need(any(r['streamId'] == 'base/Mod' and r['name'] == 'core.process' for r in rows),
         'Missing stream identity')
    if level == 'summary':
        need(not any(n.startswith(('core.document', 'core.xml.', 'core.compiler.')) for n in names),
             'Summary collected detailed scopes')


def run(args):
    # A native failure must be governed by the process budget, not an OS dialog.
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    repo = Path(__file__).resolve().parents[2]
    exe, sdk, out, work = (Path(v).resolve() for v in (args.exe, args.sdk, args.output, args.work))
    need(not out.exists() and not work.exists(), 'Evidence/work directory already exists')
    out.mkdir(parents=True)
    work.mkdir(parents=True)
    mod = work / 'TimingMod'
    data = mod / 'Data'
    data.mkdir(parents=True)
    asset = '<AssetDeclaration xmlns="uri:ea.com:eala:asset">{}</AssetDeclaration>'
    (data / 'Mod.xml').write_text(asset.format('<Includes><Include type="all" source="probe.xml"/></Includes>'), encoding='utf-8')
    (data / 'probe.xml').write_text(asset.format('<ArmorTemplate id="TimingArmor"/>' + ''.join(
        '<Texture id="TimingTexture%d" File="probe.tga" Type="StandardTexture" GenerateMipMaps="false" '
        'OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/>' % i for i in range(2))), encoding='utf-8')
    (data / 'probe.tga').write_bytes(struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 16, 16, 32, 0x28) +
                                    bytes((30, 60, 90, 255)) * 16 * 16)
    started = time.perf_counter()
    state = {'status': 'started', 'samples': [], 'limits': {'totalSeconds': args.budget, 'perProcessSeconds': args.timeout},
             'scope': 'Synthetic two-Texture/one-Armor base stream; off/summary/detailed, cold+2 hot, BIG, invalid XML; no full MOD claim',
             'sourceCommit': args.source_commit, 'binarySourceCommit': args.source_commit,
             'testSourceCommit': os.environ.get('COMPILER_TEST_SOURCE_COMMIT', args.source_commit), 'exeSha256': hashlib.sha256(exe.read_bytes()).hexdigest(),
             'inputFiles': inventory(mod)}

    def invoke(label, command, expected=0):
        remaining = args.budget - (time.perf_counter() - started)
        need(remaining > 0, 'Measurement budget exhausted')
        log = out / label
        log.mkdir()
        tick = time.perf_counter()
        with (log / 'stdout.log').open('wb') as stdout, (log / 'stderr.log').open('wb') as stderr:
            result = subprocess.run(command, cwd=exe.parent, stdout=stdout, stderr=stderr,
                                    timeout=min(args.timeout, remaining))
        sample = {'label': label, 'wallMs': (time.perf_counter() - tick) * 1000, 'exitCode': result.returncode}
        state['samples'].append(sample)
        need(result.returncode == expected, 'Unexpected exit in ' + label + ': ' + str(result.returncode))
        return log, sample

    def build(level, label, fmt='loose', broken=False):
        destination = work / (('bad-' if broken else fmt + '-') + level)
        command = [str(exe), 'build', '--sdk-root', str(sdk), '--mod', str(mod), '--out', str(destination),
                   '--package-content', 'base', '--package-format', fmt, '--include-additional=false', '--performance', level]
        namespace = cache_root(sdk, mod) / 'tests' / destination.name
        command += ['/scd:' + str(namespace / 's'), '/bcd:' + str(namespace / 'b'), '/iod:' + str(namespace / 'i')]
        if fmt == 'big':
            command += ['--mod-version', '1.0']
        log, sample = invoke(label, command, 4 if broken else 0)
        report_path = destination / 'build-report.json'
        report = json.loads(report_path.read_text(encoding='utf-8-sig'))
        shutil.copy2(report_path, log / 'build-report.json')
        shutil.copy2(destination / 'build.events.jsonl', log / 'build.events.jsonl')
        # Optional timing extension must obey the same public report contract.
        from jsonschema import Draft202012Validator
        Draft202012Validator(json.loads((repo / 'recovery/protocol/build-report.schema.json').read_text())).validate(report)
        if not broken:
            validate_profile(report, level)
            sample['cache'] = report['cache']
        else:
            need(report['status'] == 'failed' and report['exitCode'] == 4, 'Profiled failure changed outcome')
            need(any(r['name'] == 'core.xml.parse' for r in report['performance']['entries']), 'Failed XML timing lost')
        return destination, report

    try:
        baseline = None
        first_report = None
        for round_index in range(3):
            levels = ('off', 'summary', 'detailed') if round_index % 2 == 0 else ('detailed', 'summary', 'off')
            for level in levels:
                destination, report = build(level, level + '-' + ('cold' if round_index == 0 else 'hot' + str(round_index)))
                content = streams(destination / 'published')
                if baseline is None:
                    baseline, first_report = content, report
                need(content == baseline, 'Profiling changed linked stream/CDATAs bytes')
                if level == 'detailed' and round_index == 0:
                    names = {r['name'] for r in report['performance']['entries']}
                    required = {'core.xml.parse', 'core.xml.validate', 'core.compiler.Texture', 'core.compiler.ArmorTemplate',
                                'core.asset.commit', 'core.schema.read', 'core.schema.compile', 'core.schema.metadata',
                                'identity.directory'}
                    need(required <= names, 'Missing cold detail phases: ' + str(sorted(required - names)))
        # Independent original runtime. Only CLR startup configuration is adjusted.
        original = work / 'original'
        original.mkdir()
        for source in (sdk / 'tools').iterdir():
            if source.is_file() and source.suffix.lower() in ('.exe', '.dll', '.config'):
                shutil.copy2(source, original / source.name)
        config = original / 'BinaryAssetBuilder.exe.config'
        config.write_bytes(startup_config(config.read_bytes()))
        original_output = work / 'original-output'
        roots = first_report['inputRoots']
        command = [str(original / 'BinaryAssetBuilder.exe'), str(data / 'Mod.xml'),
                   '/sp:' + str(sdk / 'Schemas/xsd/CnC3Types.xsd'), '/od:' + str(original_output),
                   '/iod:' + str(work / 'original-intermediate'), '/scd:' + str(work / 'original-session'),
                   '/bcp:' + str(work / 'original-assets'), '/bc:false', '/sc:false', '/ss:true', '/pc:true',
                   '/slowclean:true', '/tp:Win32', '/ls:true', '/tl:3', '/el:0', '/res:false', '/gui:false',
                   '/poe:false', '/mr:false', '/sh:false', '/oof:false', '/sf:false', '/oix:false', '/vf:false',
                   '/osh:false', '/oar:false'] + root_arguments(roots)
        invoke('original', command)
        need(streams(original_output) == baseline, 'Original SDK stream/CDATAs bytes differ')
        big_baseline = None
        for level in ('off', 'summary', 'detailed'):
            destination, report = build(level, 'big-' + level, 'big')
            content = inventory(destination / 'published')
            if big_baseline is None:
                big_baseline = content
            need(content == big_baseline, 'Profiling changed BIG/SKUDEF bytes')
            if level != 'off':
                need({'stage.package', 'package.big', 'package.lod'} <=
                     {r['name'] for r in report['performance']['entries']}, 'Missing packaging timings')
        (data / 'probe.xml').write_text('<broken>', encoding='utf-8')
        build('detailed', 'invalid-xml', broken=True)
        state.update(status='passed', originalBytesEqual=True, profilingBytesEqual=True,
                     streamFiles={k: {'bytes': len(v), 'sha256': hashlib.sha256(v).hexdigest()} for k, v in baseline.items()})
    except Exception as error:
        state.update(status='failed', error=str(error))
        raise
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        (out / 'validation.json').write_text(json.dumps(state, indent=2), encoding='utf-8')
        print(json.dumps({k: state[k] for k in ('status', 'elapsedSeconds', 'limits')}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('exe', 'sdk', 'output', 'work', 'source-commit'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--timeout', type=int, choices=range(5, 21), default=10)
    parser.add_argument('--budget', type=int, choices=range(30, 91), default=60)
    run(parser.parse_args())
