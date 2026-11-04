import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parents[1] / 'scripts'))
spec = importlib.util.spec_from_file_location('sdk_comparison',
    Path(__file__).parents[1] / 'scripts/BenchmarkCompilerSdkComparison.py')
B = importlib.util.module_from_spec(spec)
spec.loader.exec_module(B)


class ComparisonChecks(unittest.TestCase):
    def test_both_engines_receive_identical_ordered_roots_and_cache_flags(self):
        roots = {'dataRoot': '/shared/sdk', 'dataPaths': ['/shared/mod/Data', '/shared/sdk'],
                 'artPaths': ['/shared/mod/Art', '/shared/sdk/Art'],
                 'audioPaths': ['/shared/mod/Audio', '/shared/sdk/Audio']}
        w = dict(modRoot='/shared/mod', effectiveInputRoots=roots)
        prepared = dict(runtimeDirectory='/runtime/new', sdkRoot='/shared/sdk')
        old = dict(originalExe='/runtime/original/BinaryAssetBuilder.exe')
        for mode in B.MODES:
            commands = [B.invocation(e, w, mode, Path('/work') / e, 1, prepared, old)
                        for e in ('original', 'new')]
            for prefix in ['/dr:', '/data:', '/art:', '/audio:', '/bc:', '/sc:', '/csc:',
                           '/psc:', '/ss:', '/pc:', '/slowclean:', '/tp:', '/ls:']:
                values = [[arg for arg in cmd if arg.startswith(prefix)] for cmd in commands]
                self.assertEqual(values[0], values[1], (mode, prefix))
            self.assertEqual(B.path_identity(commands[0][1]),
                             B.path_identity(Path(w['modRoot']) / 'Data/Mod.xml'))
            self.assertEqual(commands[1][commands[1].index('--mod') + 1], '/shared/mod')
            self.assertNotEqual(next(x for x in commands[0] if x.startswith('/bcp:')),
                                next(x for x in commands[1] if x.startswith('/bcp:')))
            for command in commands:
                session = next(x for x in command if x.startswith('/scd:'))
                self.assertTrue(session.endswith('session' if B.MODES[mode][1] else 'fresh-session-1'))

    def test_report_rejects_reordered_or_extra_search_candidates(self):
        w = dict(modRoot='/mod', effectiveInputRoots=dict(dataRoot='/sdk',
                 dataPaths=['/mod/Data', '/sdk'], artPaths=['/missing/Art'], audioPaths=['/missing/Audio']))
        report = dict(inputRoots=dict(w['effectiveInputRoots'], schemaPath='/sdk/Schemas/xsd/CnC3Types.xsd'),
                      plan={'Tasks': [{'InputPath': '/mod/Data/Mod.xml'}]})
        B.validate_shared_report(report, w, '/sdk')
        report['inputRoots']['dataPaths'] = ['/sdk', '/mod/Data']
        with self.assertRaises(ValueError):
            B.validate_shared_report(report, w, '/sdk')

    def test_all_raw_bytes_and_output_members_must_match(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ('original', 'new'):
                target = root / name / 'different-host-directory'
                target.mkdir(parents=True)
                for suffix in ('.manifest', '.bin', '.imp', '.relo'):
                    (target / ('mod' + suffix)).write_bytes(b'original')
            left, lp = B.stream_inventory(root / 'original')
            right, rp = B.stream_inventory(root / 'new')
            B.compare_pair(left, right, lp, rp)
            rp['mod.bin'].write_bytes(b'mutation')
            with self.assertRaises(ValueError):
                B.compare_pair(left, right, lp, rp)
            (root / 'original/different-host-directory/extra.manifest').write_bytes(b'bad')
            with self.assertRaises(ValueError):
                B.stream_inventory(root / 'original')

    def test_sdk_inventory_only_excludes_the_tested_mod_partition(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            sdk, mod = root / 'sdk', root / 'mod'
            sdk.mkdir()
            mod.mkdir()
            owned = B.cache_root(sdk, mod) / 'session' / 'cache.xml'
            sibling = sdk / 'SuperCompilerCache' / 'another-mod' / 'sentinel.keep'
            for path in (owned, sibling, sdk / 'ordinary-input.xml'):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'original')
            workloads = [dict(modRoot=str(mod))]
            before = B.sdk_source_inventory(sdk, workloads)
            self.assertEqual(len(before), 2)
            owned.write_bytes(b'allowed build cache update')
            self.assertEqual(B.sdk_source_inventory(sdk, workloads), before)
            sibling.write_bytes(b'illegal write in another MOD partition')
            self.assertNotEqual(B.sdk_source_inventory(sdk, workloads), before)

    def test_unmatched_or_failed_measurements_do_not_produce_comparisons(self):
        def sample(engine, duration, status='passed'):
            return dict(workload='w', mode='none', phase='uncached', engine=engine, status=status,
                        wallMs=duration, cpuMs=duration, peakWorkingSetBytes=1, cacheBytes=0)
        self.assertEqual(B.summaries([sample('original', 100)]), [])
        rows = B.summaries([sample('original', 100), sample('new', 150), sample('new', 1, 'failed')])
        self.assertEqual(rows[0]['nPerEngine'], 1)
        self.assertEqual(rows[0]['newOverOriginalWallRatio'], 1.5)


if __name__ == '__main__':
    unittest.main()
