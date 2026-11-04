#!/usr/bin/env python3
"""Bounded fresh-process hot-cache and invalidation matrix for a current build."""
import argparse
import copy
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import statistics
import struct
import subprocess
import time
import wave
import xml.etree.ElementTree as ET

from BenchmarkCompilerCache import MODES, inventory, validate_sample
from CompilerCachePaths import cache_layout, session_directory

NS = 'uri:ea.com:eala:asset'
ET.register_namespace('', NS)
TIERS = {'small': (32, 4), 'medium': (256, 32), 'large': (2048, 128)}


def need(value, message):
    if not value:
        raise AssertionError(message)


def write(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def stage_runtime(build, source_commit, destination):
    receipt = json.loads((build / 'build.json').read_text(encoding='utf-8-sig'))
    need(receipt['sourceCommit'] == source_commit and receipt['configuration'] == 'Release'
         and receipt['backendIntegrated'], 'Current production build identity differs')
    names = {'NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'}
    source_texture = receipt.get('texturePlugin')
    if source_texture is not None:
        need(source_texture == {'implementation': 'built-in-source', 'externalRuntimeFiles': []}, 'Source texture receipt differs')
    else:
        names.update(('BinaryAssetBuilder.TextureCompiler.dll', 'BinaryAssetBuilder.TextureCompiler.dll.patch.json'))
    need(set(receipt['runtimeFiles']) == names, 'Runtime deployment set differs')
    destination.mkdir()
    for name in sorted(names):
        shutil.copy2(build / 'bin' / name, destination / name)
    identities = inventory(destination)
    need(identities['NewModCompiler.exe']['sha256'] == receipt['exeSha256'], 'Current EXE digest differs')
    if source_texture is None:
        patch = json.loads((destination / 'BinaryAssetBuilder.TextureCompiler.dll.patch.json').read_text(encoding='utf-8-sig'))
        need(patch['targetExeSha256'] == receipt['exeSha256'] and
             patch['patchedSha256'] == identities['BinaryAssetBuilder.TextureCompiler.dll']['sha256'],
             'Historical Texture binding differs')
    return receipt, identities


def declaration(children=''):
    return '<AssetDeclaration xmlns="' + NS + '">' + children + '</AssetDeclaration>\n'


def prepare(repo, work):
    workloads = {}
    for tier, (assets, documents) in TIERS.items():
        mod = work / tier
        data = mod / 'Data'
        data.mkdir(parents=True)
        includes = []
        for doc in range(documents):
            name = 'armor-%03d.xml' % doc
            includes.append('<Include type="all" source="' + name + '"/>')
            nodes = ''.join('<ArmorTemplate id="HotArmor%04d" Default="72.5%%"/>' % i
                            for i in range(doc, assets, documents))
            (data / name).write_text(declaration(nodes), encoding='utf-8')
        (data / 'Mod.xml').write_text(declaration('<Includes>' + ''.join(includes) + '</Includes>'), encoding='utf-8')
        workloads[tier] = {'modRoot': str(mod), 'armorAssets': assets, 'armorDocuments': documents,
                           'scope': 'Synthetic Armor scale fixture; not a full MOD'}
    # A single mixed workload exercises CDATA and two cacheable native families
    # without multiplying all sizes, cache modes, jobs and packaging choices.
    data = Path(workloads['medium']['modRoot']) / 'Data'
    textures = ''.join('<Texture id="HotTexture%d" File="probe.tga" Type="StandardTexture" '
                       'GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/>' % i
                       for i in range(8))
    image = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 64, 64, 32, 0x28)
    (data / 'probe.tga').write_bytes(image + bytes((30, 60, 90, 255)) * 64 * 64)
    with wave.open(str(data / 'probe.wav'), 'wb') as audio:
        audio.setparams((2, 2, 22050, 0, 'NONE', 'not compressed'))
        audio.writeframes(struct.pack('<hhhh', 0, 1234, -1234, 32767) * 2048)
    audio_xml = ''.join('<AudioFile id="HotAudio%d" File="probe.wav" PCCompression="NONE" '
                        'IsStreamedOnPC="false"/>' % i for i in range(2))
    dependencies = ET.parse(repo / 'SDK/Mods/W3xRecovery/Data/Dependencies.xml').getroot()
    animations = ET.parse(repo / 'SDK/Mods/W3xRecovery/Data/Animations.xml').getroot()
    hierarchy = dependencies.find('{%s}W3DHierarchy' % NS)
    animation = animations.find('{%s}W3DAnimation' % NS)
    need(hierarchy is not None and animation is not None and animation.get('Hierarchy') == hierarchy.get('id'),
         'Existing W3X fixture dependency closure differs')
    native = ET.Element('{%s}AssetDeclaration' % NS)
    native.append(copy.deepcopy(hierarchy))
    for index in range(2):
        node = copy.deepcopy(animation)
        node.set('id', 'HotAnimation%d' % index)
        native.append(node)
    ET.ElementTree(native).write(data / 'native.xml', encoding='utf-8', xml_declaration=True)
    root = ET.parse(data / 'Mod.xml').getroot()
    ET.SubElement(root.find('{%s}Includes' % NS), '{%s}Include' % NS, {'type': 'all', 'source': 'native.xml'})
    for node in ET.fromstring(declaration(textures + audio_xml)):
        root.append(node)
    # A separate defines file bypasses the instance-include file-hash check:
    # expression changes then propagate through the cached inheritance chain.
    (data / 'defines.xml').write_text(declaration(
        '<Defines><Define name="HOT_SCALE" value="83.25"/></Defines>'), encoding='utf-8')
    (data / 'base.xml').write_text(declaration(
        '<Includes><Include type="all" source="defines.xml"/></Includes>'
        '<ArmorTemplate id="HotBase" Default="72.5%" DamageScalar="=$HOT_SCALE"/>'), encoding='utf-8')
    (data / 'derived.xml').write_text(declaration(
        '<Includes><Include type="instance" source="base.xml"/></Includes>'
        '<ArmorTemplate id="HotDerived" inheritFrom="HotBase" DamageScalar="=$HOT_SCALE"/>'), encoding='utf-8')
    ET.SubElement(root.find('{%s}Includes' % NS), '{%s}Include' % NS, {'type': 'all', 'source': 'derived.xml'})
    ET.ElementTree(root).write(data / 'Mod.xml', encoding='utf-8', xml_declaration=True)
    workloads['medium'].update(textureAssets=8, textureSize=64, audioAssets=2, audioFrames=4096, dependencyArmorAssets=2,
                               animationAssets=2, hierarchyAssets=1,
                               scope='Synthetic Armor + Texture/PCM Audio CDATA + existing W3X fixture subset; not a full MOD')
    return workloads


def same_stamp_edit(path, transform):
    before, stamp = path.read_bytes(), path.stat()
    after = transform(before)
    need(after != before and len(after) == len(before), 'Mutation must change content at identical length')
    path.write_bytes(after)
    os.utime(path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
    need(path.stat().st_mtime_ns == stamp.st_mtime_ns, 'Mutation did not preserve mtime')
    return {'path': str(path), 'bytes': len(after), 'mtimeNs': stamp.st_mtime_ns,
            'beforeSha256': hashlib.sha256(before).hexdigest(), 'afterSha256': hashlib.sha256(after).hexdigest()}


def remaining_timeout(started, budget, per_process):
    remaining = budget - (time.perf_counter() - started)
    if remaining <= 0:
        raise TimeoutError('Hot-cache matrix total budget exhausted')
    return min(remaining, per_process)


def published(destination, report):
    root = destination / 'published'
    current = {}
    for item in report['artifacts']:
        path = Path(item['Path']).resolve()
        need(path.is_relative_to(root.resolve()), 'Reported artifact escaped output')
        relative = path.relative_to(root.resolve()).as_posix()
        need(relative not in current and path.stat().st_size == item['Size'], 'Invalid current artifact')
        current[relative] = path.read_bytes()
    need(current, 'No current artifacts')
    return current


def linked_timestamps(values):
    return {path: stamp for path, stamp in values.items()
            if Path(path).suffix.lower() in ('.manifest', '.bin', '.imp', '.relo')}


def validate_hot(report, mode, fmt, before, after, has_payload):
    validate_sample(report, mode, 'hot')
    if MODES[mode][1]:
        cache = report['cache']
        need(cache['xmlParse'] == 0 and cache['linkedStreamsRestored'] > 0,
             'Hot session parsed XML or did not restore linked streams')
        if not has_payload:
            need(cache['fileHashReads'] == cache['fileHashBytesRead'] == 0,
                 'Unchanged XML-only hot build reread complete files for SHA256')
    if fmt == 'loose':
        if MODES[mode][1]:
            need(before == after, 'Hot session reuse rewrote cached streams or CDATA')
        else:
            # With /sc:false and a fresh intermediate directory BAB restores
            # CDATA from its asset cache. That copy may change mtime; the linked
            # quartet must still reuse its existing checksum-matched output.
            linked_before = linked_timestamps(before)
            need(linked_before and linked_before == linked_timestamps(after), 'Hot linking rewrote the four-file output')
    if fmt == 'big':
        need(any(row['name'] == 'package.big' and row['count'] > 0
                 for row in report['performance']['entries']), 'Hot BIG must still run normal packaging')


def save(out, state):
    write(out / 'validation.json', state)
    groups = {}
    for row in state['samples']:
        if row['status'] == 'passed':
            groups.setdefault((row['scenario'], row['phase']), []).append(row['wallMs'])
    lines = ['# Bounded hot-cache matrix', '', 'Status: ' + state['status'], '',
             'Fresh process per sample; OS page cache uncontrolled. Small samples describe this VM only.',
             'Wall time includes startup/build/publication; fixture setup and byte comparisons are outside sample timing.', '',
             '| Scenario | Phase | n | Median ms | Min–max ms |', '| --- | --- | ---: | ---: | ---: |']
    for (name, phase), values in groups.items():
        lines.append('| {} | {} | {} | {:.1f} | {:.1f}–{:.1f} |'.format(
            name, phase, len(values), statistics.median(values), min(values), max(values)))
    lines += ['', '## Cache invalidation and dependency propagation', '',
              'Counts are actual decisions/visits, not a reconstructed dependency graph. Omitted counters are zero.', '',
              '| Sample | Invalidated documents | Dependency reloads | Affected include parents | Affected include visits | Max include depth |',
              '| --- | ---: | ---: | ---: | ---: | ---: |']
    for row in state['samples']:
        if row.get('counters') is None:
            continue
        counts = row['counters']
        lines.append('| {} | {} | {} | {} | {} | {} |'.format(row['label'], *(counts.get(k, 0) for k in (
            'cache.invalidation.documents', 'dependency.propagation.reloads', 'dependency.include.affectedDocuments',
            'dependency.include.affectedVisits', 'dependency.include.maxDepth'))))
    lines += ['', '| Sample | Invalidation reason | Reloads |', '| --- | --- | ---: |']
    for row in state['samples']:
        for name, value in row.get('counters', {}).items():
            if name in ('cache.invalidation.contentChanged', 'cache.invalidation.dependentFileChanged',
                        'cache.invalidation.inheritanceChanged', 'cache.invalidation.definesChanged',
                        'cache.invalidation.outputMissing', 'cache.invalidation.typeChanged',
                        'cache.invalidation.pluginChanged', 'cache.invalidation.other'):
                lines.append('| {} | {} | {} |'.format(row['label'], name, value))
    if state.get('error'):
        lines += ['', state['error']]
    (out / 'validation.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def run(args):
    if os.name == 'nt':
        ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002 | 0x8000)
    repo = Path(__file__).resolve().parents[2]
    from jsonschema import Draft202012Validator
    report_validator = Draft202012Validator(json.loads((repo / 'recovery/protocol/build-report.schema.json').read_text()))
    work, out = Path(args.work).resolve(), Path(args.output).resolve()
    need(not work.exists() and not out.exists(), 'Refusing to overwrite work/evidence')
    work.mkdir(parents=True)
    out.mkdir(parents=True)
    state = {'status': 'started', 'samples': [], 'mutations': [], 'binarySourceCommit': args.source_commit,
             'testSourceCommit': args.source_commit, 'runId': os.getenv('GITHUB_RUN_ID'),
             'runAttempt': os.getenv('GITHUB_RUN_ATTEMPT'),
             'limits': {'totalSeconds': args.budget, 'perProcessSeconds': args.timeout},
             'scope': 'Release bounded representative matrix; not a full MOD, original DLL differential or game-runtime acceptance'}
    started = time.perf_counter()
    runtime = work / 'runtime'
    baselines = {}
    try:
        receipt, identities = stage_runtime(Path(args.build_directory).resolve(), args.source_commit, runtime)
        state['runtimeFiles'] = identities
        write(out / 'product-build.json', receipt)
        workloads = prepare(repo, work / 'mods')
        state['workloads'] = workloads
        state['initialInputs'] = {k: inventory(v['modRoot']) for k, v in workloads.items()}
        scenario_mods, scenario_generations = {}, {}
        state['setupSeconds'] = time.perf_counter() - started
        started = time.perf_counter()

        def sample(scenario, tier, mode, phase, jobs='4', fmt='loose', performance='off', generation='original'):
            label = '%02d-%s-%s' % (len(state['samples']), scenario, phase)
            evidence = out / label
            evidence.mkdir()
            destination = work / 'builds' / scenario
            bc, sc, csc = MODES[mode]
            template = Path(workloads[tier]['modRoot'])
            mod = work / 'scenario-mods' / scenario / template.name
            if scenario not in scenario_mods:
                # The physical MOD path owns all caches, including linked streams.
                # Keep its basename so independent BIG identities still agree.
                shutil.copytree(template, mod)
                scenario_mods[scenario] = mod
                scenario_generations[scenario] = generation
            elif scenario_generations[scenario] != generation:
                for source in template.rglob('*'):
                    if source.is_file():
                        target = mod / source.relative_to(template)
                        if not target.is_file() or target.read_bytes() != source.read_bytes():
                            target.parent.mkdir(parents=True, exist_ok=True)
                            shutil.copy2(source, target)
                scenario_generations[scenario] = generation
            layout = cache_layout(repo / 'SDK', mod)
            command = [str(runtime / 'NewModCompiler.exe'), 'build', '--sdk-root', str(repo / 'SDK'),
                       '--mod', str(mod), '--out', str(destination),
                       '--package-content', 'base', '--package-format', fmt, '--include-additional=false',
                       '--jobs', jobs, '--performance', performance,
                       '--bc=' + str(bc).lower(), '--sc=' + str(sc).lower(), '--csc=' + str(csc).lower()]
            if fmt == 'big':
                command += ['--mod-version', '1.0']
            if not sc:
                command += ['--iod', str(layout['root'] / 'i' / label.split('-', 1)[0])]
            row = {'label': label, 'scenario': scenario, 'tier': tier, 'mode': mode, 'phase': phase,
                   'jobs': jobs, 'format': fmt, 'performance': performance, 'generation': generation,
                   'status': 'started', 'command': command}
            state['samples'].append(row)
            write(evidence / 'request.json', row)
            save(out, state)
            before = {p.relative_to(layout['streams']).as_posix(): p.stat().st_mtime_ns
                      for p in layout['streams'].rglob('*') if p.is_file()}
            tick = time.perf_counter()
            try:
                with (evidence / 'stdout.log').open('wb') as stdout, (evidence / 'stderr.log').open('wb') as stderr:
                    result = subprocess.run(command, cwd=runtime, stdout=stdout, stderr=stderr,
                                            timeout=remaining_timeout(started, args.budget, args.timeout))
                row.update(wallMs=(time.perf_counter() - tick) * 1000, exitCode=result.returncode)
                need(result.returncode == 0, label + ' build failed')
            except subprocess.TimeoutExpired:
                row.update(status='timed-out', wallMs=(time.perf_counter() - tick) * 1000)
                raise
            finally:
                for name in ('build-report.json', 'build.events.jsonl'):
                    if (destination / name).exists():
                        shutil.copy2(destination / name, evidence / name)
            report = json.loads((evidence / 'build-report.json').read_text(encoding='utf-8-sig'))
            report_validator.validate(report)
            row['reportSchemaValid'] = True
            if performance != 'off':
                counters = report['performance']['counters']
                row['counters'] = {c['name']: c['value'] for c in counters if c['streamId'] == 'base/Mod'}
                need(row['counters'].get('dependency.include.visits', 0) > 0, 'Missing dependency visit observations')
                if phase.startswith('hot'):
                    need(row['counters'].get('cache.invalidation.reloads', 0) == 0 and
                         row['counters'].get('dependency.include.affectedVisits', 0) == 0,
                         'Unchanged hot sample reported invalidation/propagation')
            validate_sample(report, mode, 'hot' if phase.startswith('hot') else phase)
            need(report['diagnosticCounts']['error'] == 0, label + ' compilation errors')
            content = published(destination, report)
            need({Path(p).suffix for p in content} >=
                 ({'.big', '.skudef'} if fmt == 'big' else {'.manifest', '.bin', '.imp', '.relo'}),
                 'Expected complete published output family')
            key = (tier, fmt, generation)
            if key not in baselines:
                need(mode == 'none', 'Independent no-cache baseline must run first')
                if generation != 'original':
                    previous = next(value for (t, f, _), value in reversed(list(baselines.items()))
                                    if (t, f) == (tier, fmt))
                    need(content != previous, 'Mutation did not change observable output bytes')
                baselines[key] = content
            need(content == baselines[key], label + ' complete raw bytes differ from independent no-cache')
            after = {p.relative_to(layout['streams']).as_posix(): p.stat().st_mtime_ns
                     for p in layout['streams'].rglob('*') if p.is_file()}
            if phase.startswith('hot'):
                validate_hot(report, mode, fmt, before, after, tier == 'medium')
            if phase == 'modified':
                need(report['cache']['instanceCompile'] > 0, 'Content edit did not invalidate any compiled asset')
            row.update(status='passed', cache=report['cache'], streamFilesUnchanged=linked_timestamps(before) == linked_timestamps(after),
                       cacheStreamTimestampsUnchanged=before == after,
                       rawBytesEqual=True, outputFiles=len(content), outputBytes=sum(map(len, content.values())))
            save(out, state)
            print(label + ' passed (%.1f ms)' % row['wallMs'], flush=True)
            return destination

        for tier in TIERS:
            sample(tier + '-control', tier, 'none', 'uncached')
        sample('medium-big-control', 'medium', 'none', 'uncached', fmt='big')
        scenarios = [(tier + '-default', tier, 'both-deflate', '4', 'loose', 'off') for tier in TIERS]
        scenarios += [('medium-' + mode, 'medium', mode, '4', 'loose', 'off')
                      for mode in ('asset', 'session-xml', 'session-deflate', 'both-xml')]
        scenarios += [('medium-jobs1', 'medium', 'both-deflate', '1', 'loose', 'summary'),
                      ('medium-jobsauto', 'medium', 'both-deflate', 'auto', 'loose', 'detailed'),
                      ('medium-big', 'medium', 'both-deflate', '4', 'big', 'summary')]
        for round_index in range(3):
            for name, tier, mode, jobs, fmt, performance in (scenarios if round_index % 2 == 0 else reversed(scenarios)):
                sample(name, tier, mode, 'cold' if round_index == 0 else 'hot' + str(round_index), jobs, fmt, performance)
        # Six content boundaries; edits accumulate in the same MOD
        # and each generation gets a new no-cache control before cache reuse.
        data = Path(workloads['medium']['modRoot']) / 'Data'
        edits = [('xml', data / 'armor-000.xml', lambda b: b.replace(b'72.5%', b'62.5%', 1)),
                 ('texture', data / 'probe.tga', lambda b: b[:18] + bytes([b[18] ^ 1]) + b[19:]),
                 ('audio', data / 'probe.wav', lambda b: b[:44] + bytes([b[44] ^ 1]) + b[45:]),
                 ('animation', data / 'native.xml', lambda b: b.replace(b'FrameRate="30"', b'FrameRate="31"', 1)),
                 ('inheritance', data / 'base.xml', lambda b: b.replace(b'72.5%', b'62.5%', 1)),
                 ('defines', data / 'defines.xml', lambda b: b.replace(b'83.25', b'73.25', 1))]
        generation = 'original'
        for generation, path, transform in edits:
            state['mutations'].append(same_stamp_edit(path, transform))
            sample('control-' + generation, 'medium', 'none', 'uncached', generation=generation)
            sample('medium-default', 'medium', 'both-deflate', 'modified', performance='summary', generation=generation)
            counts = state['samples'][-1]['counters']
            reason = ('dependentFileChanged' if generation in ('texture', 'audio') else 'contentChanged')
            need(counts.get('cache.invalidation.' + reason, 0) > 0, 'Missing actual invalidation reason: ' + reason)
            need(counts.get('cache.invalidation.documents', 0) > 0, 'Missing invalidated document count')
            if generation in ('xml', 'animation', 'inheritance', 'defines'):
                need(counts.get('dependency.include.affectedDocuments', 0) > 0, 'Include change did not report propagation')
            if generation in ('inheritance', 'defines'):
                # Instance Includes retain whole-file content dependencies, so
                # a changed parent file invalidates its consumers before the
                # inheritance check. Definitions use their actual read values.
                need(counts.get('cache.invalidation.' + ('dependentFileChanged' if generation == 'inheritance' else 'definesChanged'), 0) > 0,
                     'Missing dependency-driven reload reason: ' + generation)
                need(counts.get('dependency.propagation.documents', 0) > 0, 'Missing dependency-invalidated documents')
                if generation == 'defines':
                    need(counts.get('cache.invalidation.inheritanceChanged', 0) > 0,
                         'Evaluated base asset change did not report inheritance propagation')
            sample('medium-default', 'medium', 'both-deflate', 'hot-after-' + generation, performance='summary', generation=generation)
        destination = work / 'builds/medium-default'
        missing = next((destination / 'published').rglob('*.bin'))
        state['removedPublishedFile'] = missing.relative_to(destination).as_posix()
        missing.unlink()
        sample('medium-default', 'medium', 'both-deflate', 'repair', generation=generation)
        sample('medium-default', 'medium', 'both-deflate', 'hot-after-repair', generation=generation)
        last_report = json.loads((destination / 'build-report.json').read_text(encoding='utf-8-sig'))
        session = session_directory(last_report, repo / 'SDK', scenario_mods['medium-default']) / 'BinaryAssetBuilder.SessionCache.xml.deflate'
        need(session.is_file(), 'Missing compressed session corruption target')
        session.write_bytes(b'broken deflate session')
        sample('medium-default', 'medium', 'both-deflate', 'repair-session', performance='summary', generation=generation)
        need(state['samples'][-1]['counters'].get('cache.session.invalidState', 0) > 0,
             'Corrupt session did not report invalid-state recovery')
        sample('medium-default', 'medium', 'both-deflate', 'hot-after-session-repair', performance='summary', generation=generation)
        need(inventory(runtime) == identities, 'Runtime changed during hot-cache matrix')
        remaining_timeout(started, args.budget, args.timeout)
        state['status'] = 'passed'
        return 0
    except Exception as error:
        state.update(status='failed', error=type(error).__name__ + ': ' + str(error))
        for row in state['samples']:
            if row['status'] == 'started':
                row['status'] = 'failed'
        return 1
    finally:
        state['elapsedSeconds'] = time.perf_counter() - started
        save(out, state)
        print(json.dumps({k: state.get(k) for k in ('status', 'elapsedSeconds', 'error')}), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('build-directory', 'work', 'output', 'source-commit'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--timeout', type=int, choices=range(5, 31), default=15)
    parser.add_argument('--budget', type=int, choices=range(30, 181), default=120)
    raise SystemExit(run(parser.parse_args()))
