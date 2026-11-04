"""Cheap fixture, observation and deadline checks before the Windows build."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import wave
import xml.etree.ElementTree as ET

SCRIPTS = Path(__file__).resolve().parents[1] / 'scripts'
sys.path.insert(0, str(SCRIPTS))
import TestCompilerHotCache as hot


class HotCacheTests(unittest.TestCase):
    def test_sizes_and_mixed_dependency_closure(self):
        with tempfile.TemporaryDirectory() as temp:
            workloads = hot.prepare(SCRIPTS.parents[1], Path(temp))
            ns = '{' + hot.NS + '}'
            for tier, (assets, documents) in hot.TIERS.items():
                data = Path(workloads[tier]['modRoot']) / 'Data'
                files = sorted(data.glob('armor-*.xml'))
                nodes = [n for f in files for n in ET.parse(f).getroot()]
                self.assertEqual(len(files), documents)
                self.assertEqual(len(nodes), assets)
                self.assertEqual(len({n.get('id') for n in nodes}), assets)
                for f in data.glob('*.xml'):
                    for include in ET.parse(f).getroot().iter(ns + 'Include'):
                        self.assertTrue((data / include.get('source')).is_file())
            data = Path(workloads['medium']['modRoot']) / 'Data'
            root = ET.parse(data / 'Mod.xml').getroot()
            self.assertEqual(len(root.findall(ns + 'Texture')), 8)
            self.assertEqual(len(root.findall(ns + 'AudioFile')), 2)
            with wave.open(str(data / 'probe.wav')) as audio:
                self.assertEqual((audio.getnframes(), audio.getnchannels(), audio.getsampwidth()), (4096, 2, 2))
            self.assertEqual((data / 'probe.tga').stat().st_size, 18 + 64 * 64 * 4)
            native = ET.parse(data / 'native.xml').getroot()
            self.assertEqual(len(native.findall(ns + 'W3DAnimation')), 2)
            self.assertEqual(len(native.findall(ns + 'W3DHierarchy')), 1)
            derived = ET.parse(data / 'derived.xml').getroot().find(ns + 'ArmorTemplate')
            self.assertEqual(derived.get('inheritFrom'), 'HotBase')
            self.assertEqual(derived.get('DamageScalar'), '=$HOT_SCALE')
            base = ET.parse(data / 'base.xml').getroot()
            self.assertEqual(base.find(ns + 'ArmorTemplate').get('DamageScalar'), '=$HOT_SCALE')
            self.assertEqual(base.find(ns + 'Includes/' + ns + 'Include').get('type'), 'all')

    def test_mutation_preserves_length_and_timestamp_and_rejects_noop(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'input.xml'
            path.write_bytes(b'percent=72.5%')
            stamp = path.stat().st_mtime_ns
            result = hot.same_stamp_edit(path, lambda b: b.replace(b'72.5%', b'62.5%'))
            self.assertEqual(path.stat().st_mtime_ns, stamp)
            self.assertNotEqual(result['beforeSha256'], result['afterSha256'])
            for transform in (lambda b: b, lambda b: b + b'X'):
                with self.assertRaises(AssertionError):
                    hot.same_stamp_edit(path, transform)

    def test_last_process_is_clipped_to_global_deadline(self):
        with patch.object(hot.time, 'perf_counter', return_value=119.5):
            self.assertEqual(hot.remaining_timeout(0, 120, 15), .5)
        with patch.object(hot.time, 'perf_counter', return_value=120):
            with self.assertRaises(TimeoutError):
                hot.remaining_timeout(0, 120, 15)

    def test_hot_observations_reject_fake_reuse_and_republication(self):
        report = dict(exitCode=0, status='succeeded', publicationStatus='PublishedDirectory',
                      executionMode='InProcess', externalTools=[],
                      effectiveOptions={'bc': {'Value': 'true'}},
                      cache=dict(hits=0, misses=0, sessionCache=True, compressedSessionCache=True,
                                 documentReuse=3, xmlParse=0, instanceCompile=0,
                                 linkedStreamsRestored=1, fileHashReads=0, fileHashBytesRead=0))
        hot.validate_hot(report, 'both-deflate', 'loose', {'f': 1}, {'f': 1}, False)
        for key, value in (('xmlParse', 1), ('instanceCompile', 1), ('linkedStreamsRestored', 0), ('fileHashReads', 1)):
            invalid = copy.deepcopy(report)
            invalid['cache'][key] = value
            with self.assertRaises(AssertionError if key != 'instanceCompile' else ValueError):
                hot.validate_hot(invalid, 'both-deflate', 'loose', {'f': 1}, {'f': 1}, False)
        with self.assertRaises(AssertionError):
            hot.validate_hot(report, 'both-deflate', 'loose', {'f': 1}, {'f': 2}, False)

    def test_asset_only_allows_native_cdata_restore_but_keeps_linked_output(self):
        report = dict(exitCode=0, status='succeeded', publicationStatus='PublishedDirectory',
                      executionMode='InProcess', externalTools=[],
                      effectiveOptions={'bc': {'Value': 'true'}},
                      cache=dict(hits=1, misses=0, sessionCache=False, documentReuse=0,
                                 xmlParse=1, instanceCompile=1))
        before = {'f.manifest': 1, 'f.bin': 1, 'f.imp': 1, 'f.relo': 1, 'f/cdata/a.cdata': 1}
        restored = dict(before, **{'f/cdata/a.cdata': 2})
        hot.validate_hot(report, 'asset', 'loose', before, restored, True)
        with self.assertRaises(AssertionError):
            hot.validate_hot(report, 'asset', 'loose', before, dict(restored, **{'f.bin': 2}), True)

    def test_counter_schema_rejects_negative_counts_and_bad_aggregation(self):
        from jsonschema import Draft202012Validator
        schema = json.loads((SCRIPTS.parent / 'protocol/build-report.schema.json').read_text())
        validator = Draft202012Validator(schema['properties']['performance']['properties']['counters'])
        row = dict(streamId='base/Mod', name='dependency.include.visits', value=2, aggregation='sum')
        validator.validate([row])
        for change in (dict(value=-1), dict(aggregation='average')):
            self.assertTrue(list(validator.iter_errors([dict(row, **change)])))


if __name__ == '__main__':
    unittest.main()
