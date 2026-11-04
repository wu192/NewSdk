import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('benchmark', Path(__file__).parents[1] / 'scripts/BenchmarkCompilerCache.py')
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)


def report(bc=False, sc=False, compressed=False, **counters):
    cache = dict(hits=0, misses=0, documentReuse=0, xmlParse=3, instanceCompile=2,
                 sessionCache=sc, compressedSessionCache=compressed)
    cache.update(counters)
    return dict(exitCode=0, status='succeeded', publicationStatus='PublishedDirectory',
                executionMode='InProcess', externalTools=[],
                effectiveOptions={'bc': {'Value': str(bc).lower()}}, cache=cache)


class BenchmarkChecks(unittest.TestCase):
    def test_hot_session_requires_actual_reuse_and_no_compilation(self):
        valid = report(sc=True, documentReuse=3, instanceCompile=0)
        bench.validate_sample(valid, 'session-xml', 'hot')
        for change in ({'documentReuse': 0}, {'instanceCompile': 1}):
            invalid = copy.deepcopy(valid)
            invalid['cache'].update(change)
            with self.assertRaises(ValueError):
                bench.validate_sample(invalid, 'session-xml', 'hot')

    def test_asset_only_hot_requires_hits_and_no_misses(self):
        bench.validate_sample(report(bc=True, hits=2), 'asset', 'hot')
        for change in ({'hits': 0}, {'misses': 1}):
            invalid = report(bc=True, hits=2, **change) if 'hits' not in change else report(bc=True, **change)
            with self.assertRaises(ValueError):
                bench.validate_sample(invalid, 'asset', 'hot')

    def test_cold_and_disabled_counters_reject_hidden_cache_reuse(self):
        with self.assertRaises(ValueError):
            bench.validate_sample(report(bc=True, hits=1), 'asset', 'cold')
        with self.assertRaises(ValueError):
            bench.validate_sample(report(hits=1), 'none', 'uncached')
        with self.assertRaises(ValueError):
            bench.validate_sample(report(bc=True), 'none', 'uncached')

    def test_no_activity_is_valid_only_when_sdk_types_are_proven_ineligible(self):
        inactive = report(bc=True)
        with self.assertRaises(ValueError):
            bench.validate_sample(inactive, 'asset', 'hot')
        bench.validate_sample(inactive, 'asset', 'hot', asset_cache_expected=False)
        with self.assertRaises(ValueError):
            bench.validate_sample(report(bc=True, hits=1), 'asset', 'hot', asset_cache_expected=False)

    def test_sdk_eligibility_distinguishes_omitted_flags_from_native_types(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            (root / 'tools').mkdir()
            (root / 'tools/BinaryAssetBuilder.exe.config').write_text(
                '<configuration><assetbuilder><plugins>'
                '<plugin assetTypes="ArmorTemplate"/>'
                '<plugin assetTypes="Texture,OnDemandTexture" useBuildCache="true"/>'
                '</plugins></assetbuilder></configuration>')
            (root / 'Mod.xml').write_text('<AssetDeclaration xmlns="uri:ea.com:eala:asset">'
                                         '<ArmorTemplate id="a"/><Texture id="t"/></AssetDeclaration>')
            result = bench.cache_eligibility(root, root)
            self.assertEqual(result['declaredTypes'], ['ArmorTemplate', 'Texture'])
            self.assertEqual(result['cacheableTypes'], ['Texture'])

    def test_failed_samples_do_not_contribute_to_speedup(self):
        def sample(mode, duration, status='passed'):
            return dict(workload='w', mode=mode, phase='uncached' if mode == 'none' else 'hot',
                        status=status, wallMs=duration, reportElapsedMs=duration,
                        cpuMs=1, peakWorkingSetBytes=1, cacheBytes=1,
                        cache=dict(hits=1, misses=0, documentReuse=1, xmlParse=0, instanceCompile=0))
        rows = bench.summarize([sample('none', 100), sample('none', 300), sample('asset', 50),
                                sample('asset', 70), sample('asset', 1, 'timed-out')])
        self.assertEqual(rows[1]['n'], 2)
        self.assertEqual(rows[1]['wallMsMedian'], 60)
        self.assertAlmostEqual(rows[1]['speedupVsNoCache'], 200 / 60)

    def test_output_inventory_uses_current_allowlist_and_rejects_escaped_artifacts(self):
        with tempfile.TemporaryDirectory() as root:
            published = Path(root) / 'published'
            published.mkdir()
            artifacts = []
            for ext in ('.manifest', '.bin', '.imp', '.relo'):
                path = published / ('mod' + ext)
                path.write_bytes(b'original')
                artifacts.append(dict(Path=str(path), Size=8, Sha256=bench.sha(path)))
            result = dict(artifacts=artifacts)
            self.assertEqual(len(bench.output_inventory(root, result)), 4)
            (published / 'extra.cdata').write_bytes(b'unreported')
            self.assertEqual(len(bench.output_inventory(root, result)), 4)
            self.assertNotIn('extra.cdata', bench.output_inventory(root, result))
            artifacts[0]['Path'] = str(Path(root) / 'outside.manifest')
            with self.assertRaises(ValueError):
                bench.output_inventory(root, result)


if __name__ == '__main__':
    unittest.main()
