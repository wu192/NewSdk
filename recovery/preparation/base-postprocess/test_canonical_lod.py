"""The canonical-contract gate must reject regressions beyond the pinned defect."""
import json
from pathlib import Path
import shutil
import struct
import tempfile
import unittest

import CheckCanonicalLoD as canonical
import TestCompilerBasePostprocess as raw


class CanonicalLoDTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source_commit = 'a' * 40

    def evidence(self, defect='no-references', checksum=0xf1234567):
        receipt = {'schemaVersion': 1, 'sourceCommit': self.source_commit,
                   'selectedStages': ['hashfix', 'resolver', 'lod'],
                   'originalTools': {'lod': {'sha256': canonical.ORIGINAL_LOD_SHA256}},
                   'hashfixResolverSucceeded': True, 'cases': []}
        for name in sorted(canonical.CASES):
            case = self.root / name
            for kind in ('original', 'candidate'):
                work = case / kind
                work.mkdir(parents=True)
                raw.stream(work / 'mod', [raw.asset(1, 1)])
                for extension in raw.EXTENSIONS:
                    path = work / ('mod' + extension)
                    data = bytearray(path.read_bytes())
                    struct.pack_into('<I', data, 4 if extension == '.manifest' else 0, checksum)
                    path.write_bytes(data)
            stages = [raw.compare(case, stage, ('mod',)) for stage in ('hashfix', 'resolver')]
            source = (case / 'candidate/mod.manifest').read_bytes()
            # Independent one-asset fixture: preserve the asset table, clear its
            # three payload sizes and replace only the dependency string.
            low = bytearray(source[:96])
            low[80:92] = b'\0' * 12
            dependency = b'\x02mod.manifest\0'
            struct.pack_into('<I', low, 36, len(dependency))
            low += dependency + source[-28:]
            for kind in ('original', 'candidate'):
                manifest = bytes(low)
                if kind == 'original' and name == defect:
                    manifest = manifest[:-1] + b'\x01'
                for stem in ('mod_l', 'mod_m'):
                    for extension in raw.EXTENSIONS:
                        (case / kind / (stem + extension)).write_bytes(
                            manifest if extension == '.manifest' else source[4:8])
            stages.append(raw.compare(case, 'lod', ('mod', 'mod_l', 'mod_m')))
            passed = all(stage['passed'] for stage in stages)
            row = {'name': name, 'stages': stages, 'passed': passed}
            if not passed:
                row.update(failedStage='lod', errorType='AssertionError')
            receipt['cases'].append(row)
        receipt['succeeded'] = receipt['lodSucceeded'] = all(c['passed'] for c in receipt['cases'])
        self.write_receipt(receipt)
        return receipt

    def write_receipt(self, receipt):
        (self.root / 'validation.json').write_text(json.dumps(receipt), encoding='utf-8')

    def test_accepts_exact_known_tail_and_retains_failed_raw_receipt(self):
        self.evidence()
        before = (self.root / 'validation.json').read_bytes()
        result = canonical.check(self.root, self.source_commit)
        self.assertTrue(result['succeeded'])
        self.assertFalse(result['rawByteIdentical'])
        self.assertEqual(2, len(result['knownOriginalTailDifferences']))
        self.assertEqual({137}, {d['offset'] for d in result['knownOriginalTailDifferences']})
        self.assertEqual(before, (self.root / 'validation.json').read_bytes())

    def test_accepts_fully_identical_original_outputs(self):
        self.evidence(defect=None)
        self.assertTrue(canonical.check(self.root, self.source_commit)['rawByteIdentical'])

    def test_rejects_unrecognized_case_or_input(self):
        self.evidence(defect='worldbuilder-insertion')
        with self.assertRaisesRegex(AssertionError, 'unrecognized defect input'):
            canonical.check(self.root, self.source_commit)
        shutil.rmtree(self.root)
        self.root.mkdir()
        self.evidence(checksum=1)
        with self.assertRaisesRegex(AssertionError, 'unrecognized defect input'):
            canonical.check(self.root, self.source_commit)

    def test_rejects_changed_canonical_output_even_with_matching_receipt_hash(self):
        receipt = self.evidence()
        path = self.root / 'no-references/stages/lod/candidate/mod_l.manifest'
        data = bytearray(path.read_bytes())
        data[16] ^= 1
        path.write_bytes(data)
        row = next(c for c in receipt['cases'] if c['name'] == 'no-references')
        file = next(f for f in row['stages'][2]['files'] if f['path'] == 'mod_l.manifest')
        file['candidateSha256'] = canonical.digest(data)
        self.write_receipt(receipt)
        with self.assertRaisesRegex(AssertionError, 'canonical output differs'):
            canonical.check(self.root, self.source_commit)

    def test_rejects_incomplete_or_forged_raw_evidence(self):
        receipt = self.evidence()
        row = receipt['cases'].pop()
        self.write_receipt(receipt)
        with self.assertRaisesRegex(AssertionError, 'Missing/duplicate cases'):
            canonical.check(self.root, self.source_commit)
        receipt['cases'].append(row)
        receipt['succeeded'] = True
        self.write_receipt(receipt)
        with self.assertRaisesRegex(AssertionError, 'Raw overall result differs'):
            canonical.check(self.root, self.source_commit)

    def test_rejects_different_source_or_original_tool(self):
        receipt = self.evidence()
        with self.assertRaisesRegex(AssertionError, 'identity differs'):
            canonical.check(self.root, 'b' * 40)
        receipt['originalTools']['lod']['sha256'] = 'b' * 64
        self.write_receipt(receipt)
        with self.assertRaisesRegex(AssertionError, 'Unrecognized original LoD tool'):
            canonical.check(self.root, self.source_commit)


if __name__ == '__main__':
    unittest.main()
