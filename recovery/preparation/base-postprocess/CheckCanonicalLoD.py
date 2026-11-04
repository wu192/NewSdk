"""Check the documented canonical LoD contract without changing raw receipts.

The original ten-case differential still asserts exact bytes and records failure.
Only the five pinned synthetic inputs below may differ, and only at the final
source-table NUL (original 01, canonical 00). All other bytes remain strict.
See recovery/P7_LOD_ORIGINAL_TAIL.md.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


EXTENSIONS = ('.manifest', '.bin', '.imp', '.relo')
CASES = {
    'no-references', 'hash-fixed-before-resolver', 'worldbuilder-insertion',
    'transitive-insertion', 'duplicate-reference', 'static-texture-precedence',
    'global-texture-resolution', 'audio-texture-resolution',
    'worldbuilder-texture-insertion', 'combined-hash-and-insertion',
}
ORIGINAL_LOD_SHA256 = 'e28b665a17cbbf597b47a2069a6efd979da00b44021f8f73b6beb0fb076fac0b'
KNOWN_TAIL_INPUTS = {
    'no-references': 'd17ba4bef6bee77569371eec3ec043b0c99275498c2ef342532dc9ae65644828',
    'hash-fixed-before-resolver': '809a47b705a6484b2785ee3c2088a2c0d2ac2a691952e6a87fb99bfd9df14949',
    'static-texture-precedence': '1e2b2a1649bfc62e962269069c925d99be750a26193116e72ab397f1ce393650',
    'global-texture-resolution': '33328c2b417570bfb176c6e5288816e74698efa1b35b825a74dd3084377116f6',
    'audio-texture-resolution': '1e2b2a1649bfc62e962269069c925d99be750a26193116e72ab397f1ce393650',
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical_manifest(source):
    assert source[:4] == b'\0\x01\x06\0' and len(source) >= 48, 'Expected linked PC v6 input'
    count = struct.unpack_from('<I', source, 12)[0]
    references, dependencies, names, sources = struct.unpack_from('<4I', source, 32)
    refs_end = 48 + count * 48 + references
    names_start = refs_end + dependencies
    assert names_start + names + sources == len(source), 'Incomplete input tables'
    result = bytearray(source[:refs_end])
    for index in range(count):
        offset = 48 + index * 48 + 32
        result[offset:offset + 12] = b'\0' * 12
    dependency = b'\x02mod.manifest\0'
    struct.pack_into('<I', result, 36, len(dependency))
    return bytes(result) + dependency + source[names_start:]


def check(evidence, source_commit):
    raw = json.loads((evidence / 'validation.json').read_text(encoding='utf-8'))
    assert raw['schemaVersion'] == 1 and raw['sourceCommit'] == source_commit, 'Raw receipt identity differs'
    assert raw['selectedStages'] == ['hashfix', 'resolver', 'lod'], 'Full chained differential is required'
    assert raw['originalTools']['lod']['sha256'] == ORIGINAL_LOD_SHA256, 'Unrecognized original LoD tool'
    assert len(raw['cases']) == len(CASES) and {c['name'] for c in raw['cases']} == CASES, 'Missing/duplicate cases'
    accepted = []
    all_raw_passed = True
    for case in raw['cases']:
        name = case['name']
        assert [s['stage'] for s in case['stages']] == ['hashfix', 'resolver', 'lod'], f'{name}: incomplete stages'
        snapshots = {}
        for stage in case['stages']:
            stems = ('mod', 'mod_l', 'mod_m') if stage['stage'] == 'lod' else ('mod',)
            expected = {stem + extension for stem in stems for extension in EXTENSIONS}
            assert len(stage['files']) == len(expected) and {f['path'] for f in stage['files']} == expected, f'{name}: incomplete file inventory'
            identical = True
            for file in stage['files']:
                values = []
                for kind in ('original', 'candidate'):
                    data = (evidence / name / 'stages' / stage['stage'] / kind / file['path']).read_bytes()
                    assert len(data) == file[kind + 'Bytes'] and digest(data) == file[kind + 'Sha256'], f'{name}: snapshot identity differs'
                    snapshots[stage['stage'], kind, file['path']] = data
                    values.append(data)
                same = values[0] == values[1]
                assert file['byteIdentical'] == same, f'{name}: raw file result differs'
                identical &= same
            assert stage['passed'] == identical, f'{name}: raw stage result differs'
            if stage['stage'] != 'lod':
                assert identical, f'{name}: neutral repair raw differential failed'
        source = snapshots['resolver', 'candidate', 'mod.manifest']
        expected_manifest = canonical_manifest(source)
        case_differences = []
        for stem in ('mod', 'mod_l', 'mod_m'):
            for extension in EXTENSIONS:
                file = stem + extension
                original = snapshots['lod', 'original', file]
                candidate = snapshots['lod', 'candidate', file]
                if stem == 'mod':
                    expected = snapshots['resolver', 'candidate', file]
                else:
                    expected = expected_manifest if extension == '.manifest' else source[4:8]
                assert candidate == expected, f'{name}/{file}: canonical output differs'
                if original != candidate:
                    assert stem != 'mod' and extension == '.manifest', f'{name}/{file}: unexpected raw difference'
                    assert KNOWN_TAIL_INPUTS.get(name) == digest(source), f'{name}: unrecognized defect input'
                    assert original[:-1] == candidate[:-1] and original[-1:] == b'\x01' and candidate[-1:] == b'\0', f'{name}/{file}: difference exceeds known final 01/00 byte'
                    case_differences.append({'case': name, 'path': file, 'offset': len(candidate) - 1,
                                             'originalByte': 1, 'canonicalByte': 0})
        assert len(case_differences) in (0, 2), f'{name}: low/medium defect must agree'
        raw_passed = not case_differences
        assert case['passed'] == raw_passed, f'{name}: raw case result differs'
        if not raw_passed:
            assert case.get('failedStage') == 'lod' and case.get('errorType') == 'AssertionError', f'{name}: unexpected raw failure'
        all_raw_passed &= raw_passed
        accepted.extend(case_differences)
    assert raw['succeeded'] == all_raw_passed and raw['lodSucceeded'] == all_raw_passed, 'Raw overall result differs'
    assert raw['hashfixResolverSucceeded'], 'Neutral repair cases did not all pass'
    return {'schemaVersion': 1, 'succeeded': True, 'sourceCommit': source_commit,
            'rawByteIdentical': all_raw_passed, 'cases': len(CASES),
            'knownOriginalTailDifferences': accepted,
            'scope': 'Documented canonical LoD contract; exact neutral repair bytes and all twelve files checked. '
                     'Original raw failure receipt retained unchanged; no production BIG or SDK acceptance claim.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = {'schemaVersion': 1, 'succeeded': False, 'sourceCommit': args.source_commit}
    try:
        result = check(args.evidence, args.source_commit)
    except Exception as error:
        result['error'] = str(error)
        raise
    finally:
        args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
