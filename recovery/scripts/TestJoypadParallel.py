"""Small Joypad production worker/cache matrix; never compiles a full MOD."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import struct
import subprocess
import time
import xml.etree.ElementTree as ET

from CompilerCachePaths import cache_root

NS = 'uri:ea.com:eala:asset'
ET.register_namespace('', NS)


def need(value, message):
    if not value:
        raise AssertionError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def run(args):
    repo, products, fixtures, work, out = (Path(value).resolve() for value in
        (args.repo, args.products, args.fixtures, args.work, args.output))
    need(not work.exists() and not out.exists(), 'Refusing to overwrite Joypad evidence/work')
    work.mkdir(parents=True); out.mkdir(parents=True)
    sdk = repo / 'SDK'
    mod = work / 'mod'
    shutil.copytree(fixtures / 'mod', mod)
    metadata = json.loads((fixtures / 'fixtures.json').read_text())
    # Two ordinary list-bearing assets make true overlap measurable without a
    # large MOD or artificial sleeps in the compiler. Total: 45 assets.
    entry = mod / 'Data/Mod.xml'
    document = ET.parse(entry)
    bar = document.getroot().find('{%s}JoypadCommandBarTemplate' % NS)
    slot = bar.find('{%s}Slot' % NS)
    need(slot is not None, 'Schema fixture has no Joypad slot')
    for index in range(2):
        extra = copy.deepcopy(bar)
        extra.set('id', 'JParallelBar%d' % index)
        for existing in list(extra):
            if existing.tag == '{%s}Slot' % NS:
                extra.remove(existing)
        for ordinal in range(256):
            item = copy.deepcopy(slot); item.set('Ordinal', str(ordinal)); extra.insert(ordinal, item)
        document.getroot().append(extra)
    document.write(entry, encoding='utf-8', xml_declaration=True)
    from lxml import etree as E
    schema = runpy.run_path(str(repo / 'recovery/scripts/GenerateXmlSdkModA.py'), run_name='joypad_schema')['aggregate_schema']()
    schema.assertValid(E.parse(str(entry)))
    original_xml = entry.read_bytes()
    expected_assets = metadata['sdkAssets'] + 2
    need(expected_assets == 45, 'Small Joypad fixture asset count changed')
    if args.prepare_only:
        write(out / 'preflight.json', dict(schemaValid=True, assets=expected_assets, sourceCommit=args.source_commit))
        print('PASS: source-XSD-valid 45-asset Joypad fixture, no compiler executed')
        return
    state = dict(succeeded=False, sourceCommit=args.source_commit,
                 runId=os.environ.get('GITHUB_RUN_ID'), runAttempt=os.environ.get('GITHUB_RUN_ATTEMPT'),
                 usePackagedRuntime=args.packaged, assets=expected_assets, rootNames=metadata['rootNames'],
                 schemaDataChoices=metadata['sdkChoices'], samples=[], configurations=[],
                 scope='45-asset Joypad MOD only: all 11 roots, 29 schema Data choices and two 256-slot list assets; jobs1/4/auto, cold/hot, one real edit and raw four-stream parity. No large MOD or full SDK compilation.')
    baseline = None
    reference = None
    if args.reference:
        accepted = json.loads(Path(args.reference).read_text(encoding='utf-8-sig'))
        need(accepted['succeeded'] and accepted['sourceCommit'] == args.source_commit, 'Packaged reference identity differs')
        reference = {name: Path(item['path']).read_bytes() for name, item in accepted['baselineFiles'].items()}

    def invoke(mode, label, jobs, cached, hot=False):
        runtime = products / mode / 'bin'
        exe = runtime / 'NewModCompiler.exe'
        case = out / (mode + '-' + label); case.mkdir()
        destination = work / 'outputs' / (mode + '-' + label)
        command = [str(exe), 'build', '--sdk-root', str(sdk), '--mod', str(mod), '--out', str(destination),
                   '--package-format', 'loose', '--package-content', 'base', '--include-additional=false',
                   '--jobs', str(jobs), '--performance', 'detailed',
                   '--sc=' + str(cached).lower(), '--bc=' + str(cached).lower()]
        if not cached:
            # Disabling session/build caches does not disable intermediate-file
            # reuse. A cold control therefore needs its own intermediate root.
            namespace = cache_root(sdk, mod) / 'tests' / (mode + '-' + label)
            command += ['/scd:' + str(namespace / 's'), '/bcd:' + str(namespace / 'b'), '/iod:' + str(namespace / 'i')]
        started = time.perf_counter()
        with (case / 'stdout.log').open('wb') as stdout, (case / 'stderr.log').open('wb') as stderr:
            process = subprocess.run(command, cwd=runtime, stdout=stdout, stderr=stderr, timeout=60)
        if process.returncode != 0:
            print((case / 'stderr.log').read_text(encoding='utf-8-sig', errors='replace')[-6000:], flush=True)
        need(process.returncode == 0, mode + '/' + label + ' failed with exit ' + str(process.returncode))
        report = json.loads((destination / 'build-report.json').read_text(encoding='utf-8-sig'))
        need(report['status'] == 'succeeded' and report['diagnosticCounts']['error'] == 0, 'Small MOD reported errors')
        need(len(report['artifacts']) == 4, 'Joypad fixture must emit exactly four stream files')
        files = {Path(item['Path']).suffix[1:]: Path(item['Path']) for item in report['artifacts']}
        need(set(files) == {'manifest', 'bin', 'imp', 'relo'}, 'Joypad four-stream layout differs')
        content = {name: path.read_bytes() for name, path in files.items()}
        need(struct.unpack_from('<I', content['manifest'], 12)[0] == expected_assets, 'Linked Joypad asset count differs')
        cache = report['cache']; parallel = cache['parallel']
        if hot:
            need(cache['xmlParse'] == cache['instanceCompile'] == parallel['backendCalls'] == 0, 'Hot jobs/output switch recompiled Joypad inputs')
        else:
            need(cache['instanceCompile'] > 0, 'Cold/edited Joypad scenario did not compile assets')
        if not cached:
            need(cache['instanceCompile'] == expected_assets, 'No-cache Joypad scenario omitted assets')
        joypad_maximum = 0
        if str(jobs) == '4' and not hot:
            need(parallel['minimumEffectiveJobs'] > 1, 'Runner did not admit multiple Joypad workers')
            need(not parallel['fallbackByReason'].get('uncertified-xml-type', 0), 'Joypad stayed on the uncertified owner path')
            if not cached or label == 'cache-cold':
                seen = {item['typeName'] for item in parallel['backendIntervals']}
                need(set(metadata['rootNames']) <= seen, 'Actual production trace omitted a Joypad root')
                intervals = [item for item in parallel['backendIntervals'] if item['typeName'] in metadata['rootNames']]
                points = [(item['startTicks'], 1) for item in intervals] + [(item['endTicks'], -1) for item in intervals]
                active = maximum = 0
                for _, delta in sorted(points):
                    active += delta; maximum = max(maximum, active)
                need(maximum > 1 and len({item['threadId'] for item in intervals}) > 1, 'No real overlapping Joypad worker calls')
                joypad_maximum = maximum
        for name in ('build-report.json', 'build.events.jsonl'):
            shutil.copy2(destination / name, case / name)
        archive = case / 'artifacts'; archive.mkdir()
        inventory = {}
        for name, path in files.items():
            target = archive / path.name; shutil.copy2(path, target)
            inventory[name] = dict(path=str(target), bytes=len(content[name]), sha256=sha(content[name]))
        row = dict(configuration=mode, label=label, jobs=str(jobs), cached=cached, arguments=command, passed=True,
                   wallMs=(time.perf_counter()-started)*1000, joypadMaximumConcurrency=joypad_maximum, cache=cache, files=inventory)
        state['samples'].append(row); write(out / 'validation.json', state)
        return content, inventory, report

    try:
        for mode in ('Debug', 'Release'):
            entry.write_bytes(original_xml)
            runtime = products / mode / 'bin'
            before = {path.name: sha(path.read_bytes()) for path in runtime.iterdir()}
            need(set(before) == {'NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'}, 'Unexpected runtime files')
            receipt = json.loads((products / mode / 'build.json').read_text(encoding='utf-8-sig'))
            need(receipt['sourceCommit'] == args.source_commit and receipt['exeSha256'] == before['NewModCompiler.exe'], 'Production executable/receipt differs')
            serial, inventory, report = invoke(mode, 'serial', '1', False)
            if baseline is None:
                baseline = serial
            need(serial == baseline and (reference is None or serial == reference), 'Configuration/ZIP source raw outputs differ')
            for jobs in ('4', 'auto'):
                content, _, _ = invoke(mode, 'uncached-' + jobs, jobs, False)
                need(content == baseline, 'Serial/parallel Joypad raw outputs differ')
            partition = cache_root(sdk, mod)
            if partition.exists():
                shutil.rmtree(partition)
            content, _, _ = invoke(mode, 'cache-cold', '4', True)
            need(content == baseline, 'Cold cached Joypad raw outputs differ')
            for jobs in ('1', 'auto'):
                content, _, _ = invoke(mode, 'hot-new-output-' + jobs, jobs, True, hot=True)
                need(content == baseline, 'Hot Joypad raw outputs differ after changing jobs/output')
            changed_xml, replacements = re.subn(rb'(<Slot\b[^>]*\bOrdinal=")(\d+)(")', lambda m: m[1] + str(int(m[2])+1).encode() + m[3], original_xml, count=1)
            need(replacements == 1, 'No real Joypad ordinal to mutate')
            entry.write_bytes(changed_xml)
            changed, _, _ = invoke(mode, 'edited-workers', '4', True)
            need(changed != baseline, 'Joypad edit did not change linked bytes')
            control, _, _ = invoke(mode, 'edited-owner-control', '1', False)
            need(changed == control, 'Incremental Joypad bytes differ from independent owner control')
            hot, _, _ = invoke(mode, 'edited-hot', 'auto', True, hot=True)
            need(hot == changed, 'Edited Joypad hot bytes differ')
            need({path.name: sha(path.read_bytes()) for path in runtime.iterdir()} == before, 'Joypad tests changed runtime files')
            state['configurations'].append(dict(configuration=mode, passed=True, exeSha256=before['NewModCompiler.exe']))
            if mode == 'Release':
                state['baselineFiles'] = inventory
                case = dict(name='mod-a', modRoot=str(mod), inputPath=str(entry), sdkRoot=str(sdk), originalToolsRoot=str(sdk / 'tools'),
                            stagingDirectory=str(Path(inventory['manifest']['path']).parent),
                            outputs={name: item['path'] for name, item in inventory.items()}, scope=state['scope'])
                write(out / 'original-fixture.json', dict(succeeded=True, sourceCommit=args.source_commit,
                    runId=os.environ.get('GITHUB_RUN_ID'), baselineCases=[case], scope=state['scope']))
        state['succeeded'] = True
    except Exception as error:
        state['error'] = str(error)
        raise
    finally:
        entry.write_bytes(original_xml)
        write(out / 'validation.json', state)
    print('PASS: 45-asset Joypad worker/cache matrix, both configurations, 18 actual CLI samples')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('repo', 'products', 'fixtures', 'work', 'output', 'source-commit'):
        parser.add_argument('--' + key, required=True)
    parser.add_argument('--packaged', action='store_true')
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--reference')
    run(parser.parse_args())
