"""Original SDK HashFix -> ModAssetResolver -> LoD raw chained differential.

Uses an isolated tools/../BuiltMods layout, without registry changes. Original
tools must report actual completion; Delphi's zero exit code alone is unsafe.
This component receipt does not claim production BIG or game acceptance.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import traceback


EXTENSIONS = ('.manifest', '.bin', '.imp', '.relo')
TEXTURE = 0xb22f87c9
STANDARD = 0xe3181c04
ONDEMAND = 0xf0f08712


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def asset(typeid, instance, hash=3, refs=()):
    return {'type': typeid, 'id': instance, 'hash': hash, 'refs': refs}


def stream(stem, rows):
    """Small self-consistent v6 stream with nonempty distinct raw payloads."""
    refs = bytearray(); names = bytearray(); sources = bytearray(); table = bytearray()
    payloads = [bytearray(), bytearray(), bytearray()]
    for i, row in enumerate(rows):
        chunks = [bytes([0x20 + i, row['id'] & 255]) * (i + 1),
                  struct.pack('<I', 0x10000000 + row['id']),
                  struct.pack('<II', 0x20000000 + row['id'], 0xffffffff)]
        table += struct.pack('<12I', row['type'], row['id'], 0x12345678,
                             row['hash'], len(refs), len(row['refs']), len(names),
                             len(sources), len(chunks[0]), len(chunks[2]), len(chunks[1]), 0)
        refs += b''.join(struct.pack('<II', *ref) for ref in row['refs'])
        names += f'Test:asset{row["id"]}\0'.encode('ascii')
        sources += f'Data/asset{row["id"]}.xml\0'.encode('ascii')
        for target, chunk in zip(payloads, chunks):
            target.extend(chunk)
    dependencies = b'\x02Static.manifest\0\x02Global.manifest\0\x02Audio.manifest\0'
    checksum = 0xf1234567
    header = struct.pack('<BBH11I', 0, 1, 6, checksum, 0x12345678, len(rows),
                         len(payloads[0]), max([0] + [2 * (i + 1) for i in range(len(rows))]),
                         8 if rows else 0, 4 if rows else 0, len(refs), len(dependencies), len(names), len(sources))
    stem.with_suffix('.manifest').write_bytes(header + table + refs + dependencies + names + sources)
    for extension, payload in zip(('.bin', '.imp', '.relo'), payloads):
        stem.with_suffix(extension).write_bytes(struct.pack('<I', checksum) + payload)


def invoke(exe, args, cwd, output, completion=None):
    process = subprocess.run([str(exe), *map(str, args)], cwd=cwd, capture_output=True, timeout=30)
    output.with_suffix('.stdout.log').write_bytes(process.stdout)
    output.with_suffix('.stderr.log').write_bytes(process.stderr)
    assert process.returncode == 0, (exe, process.returncode, process.stderr)
    assert b'Error:' not in process.stdout and b'Error:' not in process.stderr, (exe, process.stdout, process.stderr)
    if completion:
        assert completion in process.stdout, ('Original tool did not complete actual work', exe, process.stdout)
    return process.stdout


def snapshot(directory):
    return {f.relative_to(directory).as_posix(): sha(f) for f in directory.rglob('*') if f.is_file()}


def compare(case, stage, stems):
    rows = []
    for stem in stems:
        for extension in EXTENSIONS:
            name = stem + extension
            original = case / 'original' / name
            candidate = case / 'candidate' / name
            left, right = original.read_bytes(), candidate.read_bytes()
            rows.append({'path': name, 'originalBytes': len(left), 'originalSha256': sha(original),
                         'candidateBytes': len(right), 'candidateSha256': sha(candidate),
                         'byteIdentical': left == right})
            for kind, source in (('original', original), ('candidate', candidate)):
                archive = case / 'stages' / stage / kind
                archive.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, archive / name)
    return {'stage': stage, 'files': rows, 'passed': all(row['byteIdentical'] for row in rows)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tools', type=Path, required=True)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--hashfix-resolver-only', action='store_true',
                        help='Validate the two neutral-repair stages only; retain full raw LoD assertions in the default mode')
    args = parser.parse_args()
    tools, candidate, out = args.tools.resolve(), args.candidate.resolve(), args.output.resolve()
    selected_stages = ('hashfix', 'resolver') if args.hashfix_resolver_only else ('hashfix', 'resolver', 'lod')
    out.mkdir(parents=True, exist_ok=False)
    originals = {stage: tools / name for stage, name in
                 (('hashfix', 'HashFix.exe'), ('resolver', 'ModAssetResolver.exe'), ('lod', 'LoDStreamBuilder.exe'))}
    cases = [
        ('no-references', [asset(1, 1)], [], [], [], [], 0),
        ('hash-fixed-before-resolver', [asset(1, 1, 3)], [asset(1, 1, 7)], [], [], [], 0),
        ('worldbuilder-insertion', [asset(1, 1, refs=[(2, 2)])], [asset(2, 2)], [], [], [], 1),
        ('transitive-insertion', [asset(1, 1, refs=[(2, 2)])],
         [asset(2, 2, refs=[(3, 3)]), asset(3, 3)], [], [], [], 2),
        ('duplicate-reference', [asset(1, 1, refs=[(2, 2), (2, 2)])], [asset(2, 2)], [], [], [], 1),
        ('static-texture-precedence', [asset(1, 1, refs=[(TEXTURE, 2)])],
         [asset(ONDEMAND, 2)], [asset(STANDARD, 2)], [asset(ONDEMAND, 2)], [], 0),
        ('global-texture-resolution', [asset(1, 1, refs=[(TEXTURE, 2)])], [], [], [asset(ONDEMAND, 2)], [], 0),
        ('audio-texture-resolution', [asset(1, 1, refs=[(TEXTURE, 2)])], [], [], [], [asset(STANDARD, 2)], 0),
        ('worldbuilder-texture-insertion', [asset(1, 1, refs=[(TEXTURE, 2)])], [asset(STANDARD, 2)], [], [], [], 1),
        ('combined-hash-and-insertion', [asset(1, 1, 3, [(2, 2)])],
         [asset(1, 1, 7), asset(2, 2, 9, [(3, 3)]), asset(3, 3)], [], [], [], 2),
    ]
    state = {'schemaVersion': 1, 'succeeded': False, 'hashfixResolverSucceeded': False, 'lodSucceeded': False, 'cases': [],
             'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
             'originalTools': {stage: {'path': str(path), 'sha256': sha(path)} for stage, path in originals.items()},
             'candidateSha256': sha(candidate),
             'scope': 'Independent component host, original HashFix then ModAssetResolver then LoD raw four/twelve-file differential; no production base BIG acceptance'}
    state['selectedStages'] = list(selected_stages)
    if args.hashfix_resolver_only:
        state['scope'] = ('Independent component host; all ten original HashFix and ModAssetResolver cases, '
                          'exact raw four-file stage differentials. LoD is not executed or accepted by this receipt: '
                          'the default combined mode retains its raw assertions and known original tail defect; '
                          'canonical LoD and real neutral BIG are separate gates.')
    try:
        for name, mod, wb, static, global_, audio, inserted in cases:
            case = out / name; case.mkdir()
            row = {'name': name, 'passed': False, 'stages': [], 'expectedInsertedAssets': inserted}; state['cases'].append(row)
            stage = 'setup'
            try:
                for kind in ('original', 'candidate'):
                    work = case / kind; work.mkdir(); (work / 'tools').mkdir(); built = work / 'BuiltMods'; built.mkdir()
                    stream(work / 'mod', mod)
                    for stem, assets in (('Worldbuilder', wb), ('Static', static), ('Global', global_), ('Audio', audio)):
                        # Original tools assume a nonempty reference asset table.
                        # A unique nonmatching asset keeps the no-reference case
                        # meaningful without exercising the original empty-WB bug.
                        stream(built / stem, assets or [asset(0x70000001, 0x70000002)])
                    # Actual HashFix.exe reads BuiltMods/SageXml; Resolver.exe
                    # reads the BuiltMods root. Preserve the SAME WB manifest bytes
                    # in both explicit layouts; neither stage may mutate either.
                    sage = built / 'SageXml'; sage.mkdir()
                    shutil.copyfile(built / 'Worldbuilder.manifest', sage / 'Worldbuilder.manifest')
                    assert (built / 'Worldbuilder.manifest').read_bytes() == (sage / 'Worldbuilder.manifest').read_bytes()
                    (built / 'StringHashes.xml').write_text('<AssetDeclaration/>', encoding='utf-8')
                    # The original uses its isolated relative ../BuiltMods fallback.
                    # No machine registry or SDK input files are changed.
                    if kind == 'original':
                        for tool_stage, source in originals.items():
                            shutil.copyfile(source, work / 'tools' / source.name)
                        for file in ('ModAssetResolver_TypeHashes.txt', 'ModAssetResolver_InstanceHashes.txt'):
                            shutil.copyfile(tools / file, work / 'tools' / file)
                reference_before = {kind: snapshot(case / kind / 'BuiltMods') for kind in ('original', 'candidate')}
                for stage in selected_stages:
                    for kind in ('original', 'candidate'):
                        work = case / kind; manifest = work / 'mod.manifest'; built = work / 'BuiltMods'
                        if kind == 'original':
                            completion = {'hashfix': b'Instance hashes fixed:',
                                          'resolver': f'Assets prepended: {inserted}'.encode(), 'lod': None}[stage]
                            invoke(work / 'tools' / originals[stage].name, [manifest], work / 'tools', work / stage, completion)
                        else:
                            extra = [built / 'SageXml' / 'Worldbuilder.manifest'] if stage == 'hashfix' else ([built] if stage == 'resolver' else [])
                            result = invoke(candidate, [stage, manifest, *extra], work / 'tools', work / stage)
                            if stage == 'resolver':
                                assert result.strip() == str(inserted).encode(), ('Candidate insertion count differs', result)
                            if stage == 'lod':
                                assert result.strip() == b'8', ('Candidate LoD output count differs', result)
                        assert snapshot(built) == reference_before[kind], f'{kind}/{stage} changed reference inputs'
                    comparison = compare(case, stage, ('mod', 'mod_l', 'mod_m') if stage == 'lod' else ('mod',))
                    row['stages'].append(comparison)
                    assert comparison['passed'], f'{case.name}/{stage}: original/candidate raw bytes differ'
            except Exception as error:
                row['error'] = str(error)
                row['errorType'] = type(error).__name__
                row['failedStage'] = stage
                (case / 'error.log').write_text(traceback.format_exc(), encoding='utf-8')
                completed = next((item for item in row['stages'] if item['stage'] == stage), None)
                if completed is None:
                    row['stages'].append({'stage': stage, 'passed': False, 'error': str(error)})
                else:
                    completed['error'] = str(error)
                # Dependent later stages of this case are not run after a bad
                # source. Every next case owns fresh inputs and still executes.
            row['passed'] = len(row['stages']) == len(selected_stages) and all(item['passed'] for item in row['stages'])
        def every_stage_passed(stages):
            return len(state['cases']) == len(cases) and all(
                all(any(item['stage'] == stage and item['passed'] for item in row['stages']) for stage in stages)
                for row in state['cases'])
        state['hashfixResolverSucceeded'] = every_stage_passed(('hashfix', 'resolver'))
        state['lodSucceeded'] = 'lod' in selected_stages and every_stage_passed(('lod',))
        state['succeeded'] = all(row['passed'] for row in state['cases']) and len(state['cases']) == len(cases)
    finally:
        (out / 'validation.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'succeeded': state['succeeded'], 'cases': len(cases),
                      'hashfixResolverSucceeded': state['hashfixResolverSucceeded'],
                      'lodSucceeded': state['lodSucceeded'], 'scope': state['scope']}))
    if not state['succeeded']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
