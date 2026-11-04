#!/usr/bin/env python3
"""Production legacy-cache/parallel matrix; archives reports and exact output bytes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import wave
import xml.etree.ElementTree as ET

from CompilerCachePaths import cache_root

NS = 'uri:ea.com:eala:asset'
ET.register_namespace('', NS)


def need(value, message):
    if not value:
        raise AssertionError(message)


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def fixture(repo, mod):
    shutil.copytree(repo / 'SDK/Mods/W3xRecovery', mod)
    data = mod / 'Data'
    audio = ET.Element('{%s}AssetDeclaration' % NS)
    for index in range(8):
        source = mod / ('Audio/probe%d.wav' % index)
        source.parent.mkdir(exist_ok=True)
        with wave.open(str(source), 'wb') as stream:
            stream.setparams((2, 2, 22050, 0, 'NONE', 'not compressed'))
            stream.writeframes(b'\x00\x00\x01\x00\xff\xff\x02\x00' * 65536)
        attrs = {'id': 'ParallelAudio%d' % index, 'File': '../Audio/' + source.name,
                 'IsStreamedOnPC': 'true' if index >= 4 else 'false'}
        compression = ('NONE', 'XAS', 'EALAYER3', None)[index % 4]
        if compression is not None:
            attrs['PCCompression'] = compression
        if index >= 4:
            attrs['PCSampleRate'] = '44100' if index % 2 == 0 else '11025'
        elif index == 1:
            attrs['PCSampleRate'] = '22050'
        ET.SubElement(audio, '{%s}AudioFile' % NS, attrs)
    ET.ElementTree(audio).write(data / 'ParallelAudio.xml', encoding='utf-8', xml_declaration=True)
    tokenizers = ET.Element('{%s}AssetDeclaration' % NS)
    for index in range(8):
        game_object = ET.SubElement(tokenizers, '{%s}GameObject' % NS,
                                    {'id': 'ParallelObject%d' % index, 'Description': 'borrowed input', 'Scale': '1.25'})
        body = ET.SubElement(game_object, '{%s}Body' % NS)
        ET.SubElement(body, '{%s}ActiveBody' % NS, {'id': 'Body', 'MaxHealth': '100'})
        ET.SubElement(tokenizers, '{%s}WeaponTemplate' % NS,
                      {'id': 'ParallelWeapon%d' % index, 'AttackRange': str(42 + index)})
        armor = ET.SubElement(tokenizers, '{%s}ArmorTemplate' % NS,
                              {'id': 'ParallelArmor%d' % index, 'Default': '50%'})
        ET.SubElement(armor, '{%s}Armor' % NS, {'Damage': 'GUN', 'Percent': '25%'})
    ET.ElementTree(tokenizers).write(data / 'ParallelTokenizers.xml', encoding='utf-8', xml_declaration=True)
    shutil.copy2(data / 'ParallelTokenizers.xml', data / 'DuplicateTokenizers.xml')
    apt = repo / 'SDK/Mods/XmlCompilerRecoveryApt/Data'
    for name in ('FixturesApt.xml', 'fixture.bin', 'fixture-odd.bin', 'fixture-empty.bin'):
        shutil.copy2(apt / name, data / name)
    root = ET.parse(data / 'Mod.xml')
    includes = root.getroot().find('{%s}Includes' % NS)
    # Distinct documents may legally repeat the same physical asset. Put the
    # duplicate immediately after its source, while its hierarchy can be pending.
    shutil.copy2(data / 'Dependencies.xml', data / 'DuplicateDependencies.xml')
    source_index = next(i for i, node in enumerate(includes) if node.get('source') == 'Dependencies.xml')
    includes.insert(source_index+1, ET.Element('{%s}Include' % NS, {'type':'all','source':'DuplicateDependencies.xml'}))
    for name in ('ParallelAudio.xml', 'FixturesApt.xml', 'ParallelTokenizers.xml', 'DuplicateTokenizers.xml'):
        ET.SubElement(includes, '{%s}Include' % NS, {'type': 'all', 'source': name})
    # Repeated include exercises the pending-document reuse barrier.
    ET.SubElement(includes, '{%s}Include' % NS, {'type': 'all', 'source': 'ParallelAudio.xml'})
    root.write(data / 'Mod.xml', encoding='utf-8', xml_declaration=True)


def run(args):
    repo, products, work, out = (Path(x).resolve() for x in (args.repo, args.products, args.work, args.output))
    need(not work.exists() and not out.exists(), 'Refusing to overwrite evidence/work')
    work.mkdir(parents=True); out.mkdir(parents=True)
    mod = work / 'mixed'
    fixture(repo, mod)
    state = {'sourceCommit': args.source_commit, 'runId': os.environ.get('GITHUB_RUN_ID'),
             'succeeded': False, 'cases': [], 'scope': 'Current production mixed GameObject/Weapon/Armor/W3X/Texture/XML/APT/Audio, cache/jobs switches, raw output bytes; full MOD and component certification separate'}
    serial_owner_threads = {}

    def invoke(mode, name, destination, jobs, sc=True, bc=True, extra=(), expected=0):
        exe = products / mode / 'bin/NewModCompiler.exe'
        evidence = out / mode / name
        evidence.mkdir(parents=True)
        command = [str(exe), 'build', '--mod', str(mod), '--sdk-root', str(repo / 'SDK'),
                   '--out', str(destination), '--jobs', str(jobs), '--performance', 'detailed',
                   '--sc=' + str(sc).lower(), '--bc=' + str(bc).lower(), '--package-content', 'base',
                   '--package-format', 'loose'] + list(extra)
        # --out no longer owns caches. Give independent cold scenarios explicit
        # namespaces inside the real SDK/MOD partition; a hot retry keeps its
        # destination and therefore retains the same three cache directories.
        namespace = cache_root(repo / 'SDK', mod) / 'tests' / hashlib.sha256(str(destination).encode()).hexdigest()[:10]
        command += ['/scd:' + str(namespace / 's'), '/bcd:' + str(namespace / 'b'), '/iod:' + str(namespace / 'i')]
        tick = time.perf_counter()
        with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
            process = subprocess.run(command, cwd=exe.parent, stdout=stdout, stderr=stderr, timeout=180)
        report_path = destination / 'build-report.json'
        if report_path.exists():
            shutil.copy2(report_path, evidence / report_path.name)
        body = json.loads(report_path.read_text(encoding='utf-8-sig'))
        row = {'mode': mode, 'name': name, 'command': command, 'exitCode': process.returncode,
               'wallMs': (time.perf_counter()-tick)*1000, 'cache': body.get('cache'), 'passed': False}
        state['cases'].append(row); write(out / 'validation.json', state)
        need(process.returncode == expected and body['exitCode'] == expected, name + ' exit/report differs')
        result = {}
        if expected == 0:
            for artifact in body['artifacts']:
                path = Path(artifact['Path']); relative = path.relative_to(destination / 'published')
                content = path.read_bytes()
                target = evidence / 'artifacts' / relative
                target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(content)
                result[relative.as_posix()] = content
            need(len(result) >= 8, 'Mixed fixture omitted stream or Audio CDATA')
            parallel = body['cache']['parallel']
            if str(jobs) == '1' and parallel['backendIntervals']:
                owners = {interval['threadId'] for interval in parallel['backendIntervals']}
                need(len(owners) == 1, 'Serial baseline executed backends on more than one owner thread')
                serial_owner_threads[mode] = owners
            need(parallel['backendCalls'] == body['cache']['instanceCompile'], 'Reported backend call count differs from actual Core compile count')
            if name.startswith('sc') and '-hot' not in name:
                need(parallel['backendCallsByType'].get('W3DHierarchy') == 1 and parallel['backendCallsByType'].get('Texture') == 1,
                     'Identical assets in distinct documents were compiled more than once')
                need(all(parallel['backendCallsByType'].get(kind) == 8 for kind in ('GameObject', 'WeaponTemplate', 'ArmorTemplate')),
                     'Repeated tokenizer assets were compiled more than once or omitted')
            if name.startswith('sc') and '-hot' not in name and jobs != '1' and parallel['minimumEffectiveJobs'] > 1:
                need(parallel['prepareMs'] > 0 and parallel['maxBackendConcurrency'] > 1,
                     'Production misses admitted workers but executed no real overlapping backend calls')
                owner_threads = serial_owner_threads.get(mode)
                need(owner_threads, 'Worker certification requires an observed jobs1 owner-thread baseline')
                worker_types = {interval['typeName'] for interval in parallel['backendIntervals'] if interval['threadId'] not in owner_threads}
                if parallel['minimumEffectiveJobs'] > 1 and owner_threads:
                    need({'GameObject', 'WeaponTemplate', 'ArmorTemplate', 'W3DAnimation', 'W3DMesh', 'AudioFile', 'AptAptData', 'AptConstData', 'AptDatData', 'AptGeometryData', 'W3DHierarchy', 'Texture'} <= worker_types,
                         'An admitted certified family stayed behind the lazy production wrapper')
                    texture_intervals = [interval for interval in parallel['backendIntervals'] if interval['typeName'] == 'Texture']
                    need(texture_intervals and all(interval['threadId'] not in owner_threads for interval in texture_intervals),
                         'Certified texture calls must execute on workers')
                    audio_intervals = [interval for interval in parallel['backendIntervals'] if interval['typeName'] == 'AudioFile']
                    need(len(audio_intervals) == 8 and all(interval['threadId'] not in owner_threads for interval in audio_intervals),
                         'Every AudioFile setting, including rate conversion and EALAYER3, must execute on a worker')
                    need(not {'audio-explicit-sample-rate', 'audio-compression-diagnostic'} & set(parallel['fallbackByReason']),
                         'Removed AudioFile gates still caused serial fallback')
            need(not list(destination.glob('**/result.bin')) and not list(destination.glob('**/action-cache*')), 'Unexpected action cache output')
        row['passed'] = True; write(out / 'validation.json', state)
        return result, body

    try:
        for mode in ('Debug', 'Release'):
            reference = None
            for sc, bc in ((False, False), (False, True), (True, False), (True, True)):
                for jobs in ('1', '4', 'auto'):
                    label = 'sc%d-bc%d-j%s' % (sc, bc, jobs)
                    destination = work / mode / label
                    outputs, body = invoke(mode, label, destination, jobs, sc, bc)
                    if reference is None:
                        reference = outputs
                    need(outputs == reference, label + ' serial/parallel/cache raw outputs differ')
                    if sc:
                        hot, report = invoke(mode, label + '-hot-switch', destination, '4' if jobs == '1' else 'auto', sc, bc)
                        need(hot == reference, 'Hot jobs switch changed raw bytes')
                        cache = report['cache']; parallel = cache['parallel']
                        need(cache['xmlParse'] == 0 and cache['instanceCompile'] == 0, 'Hot jobs switch invalidated old caches')
                        need(parallel['backendCalls'] == 0 and parallel['prepareMs'] == 0, 'Hot cache performed backend preparation')
            for flag in ('csc=false', 'psc=false', 'fsc=true', 'atc=true'):
                destination = work / mode / flag
                if flag == 'fsc=true':
                    warm, _ = invoke(mode, flag+'-seed', destination, '1')
                    need(warm == reference, 'Freeze seed bytes differ')
                result, _ = invoke(mode, flag, destination, '4', extra=('--'+flag,))
                need(result == reference, flag + ' changed original raw outputs')
                hot, report = invoke(mode, flag+'-hot', destination, 'auto', extra=('--'+flag,))
                need(hot == reference and report['cache']['instanceCompile'] == 0 and report['cache']['xmlParse'] == 0,
                     flag + ' hot cache compatibility differs')
            destination = work / mode / 'sc1-bc1-j4'
            path = mod / 'Data/ParallelAudio.xml'; original = path.read_bytes()
            path.write_bytes(original.replace(b'PCCompression="NONE"', b'PCCompression="XAS"', 1))
            changed, body = invoke(mode, 'single-asset-change', destination, '4')
            need(0 < body['cache']['instanceCompile'] < 8, 'Single Audio change recompiled unrelated assets')
            need(set(body['cache']['parallel']['backendCallsByType']) == {'AudioFile'}, 'Single Audio edit compiled an unrelated backend type')
            control, _ = invoke(mode, 'single-asset-control', work / mode / 'changed-control', '1', False, False)
            need(changed == control, 'Incremental asset change raw bytes differ from independent owner control')
            path.write_bytes(original)
            # Native tasks from preceding includes must join before partial-save/retry.
            broken = mod / 'Data/ParallelBroken.xml'
            broken.write_text('<AssetDeclaration xmlns="'+NS+'"><AudioFile id="Broken" File="missing.wav" PCCompression="NONE"/></AssetDeclaration>')
            entry = mod / 'Data/Mod.xml'; entry_bytes = entry.read_bytes(); root = ET.parse(entry)
            ET.SubElement(root.getroot().find('{%s}Includes' % NS), '{%s}Include' % NS, {'type':'all','source':broken.name})
            root.write(entry, encoding='utf-8', xml_declaration=True)
            invoke(mode, 'partial-failure', work / mode / 'partial', '4', expected=4)
            _, no_partial = invoke(mode, 'no-partial-failure', work / mode / 'no-partial', '4', extra=('--psc=false',), expected=4)
            need(no_partial['cache']['partialSessionsSaved'] == 0, 'psc=false saved a partial session')
            entry.write_bytes(entry_bytes); broken.unlink()
            retry, _ = invoke(mode, 'partial-retry', work / mode / 'partial', 'auto')
            need(retry == reference, 'Partial-session retry changed bytes')
        state['succeeded'] = True
        return 0
    except Exception as error:
        state['error'] = str(error)
        raise
    finally:
        write(out / 'validation.json', state)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('repo', 'products', 'work', 'output', 'source-commit'):
        parser.add_argument('--'+key, required=True)
    raise SystemExit(run(parser.parse_args()))
