"""Portable cache-location checks; no compiler process is launched."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parents[1] / 'scripts'))
from CompilerCachePaths import cache_layout, cache_root, report_cache_root, session_directory


class CacheLocationTests(unittest.TestCase):
    def test_uppercase_mod_label_and_same_name_mod_isolation(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sdk, first, second = root / 'SDK', root / 'a/Upper.MOD', root / 'b/Upper.MOD'
            for path in (sdk, first, second):
                path.mkdir(parents=True)
            layout = cache_layout(sdk, first)
            self.assertTrue(layout['root'].name.startswith('upper_mod_'))
            self.assertRegex(layout['root'].name, r'_[0-9a-f]{32}$')
            self.assertNotEqual(layout['root'], cache_root(sdk, second))
            self.assertTrue(all(value.is_relative_to(layout['root']) for key, value in layout.items()))
            self.assertFalse((sdk / 'SuperCompilerCache').exists())

    def test_actual_report_session_override_is_used_without_changing_partition(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sdk, mod = root / 'sdk', root / 'mod'
            sdk.mkdir(); mod.mkdir()
            layout = cache_layout(sdk, mod)
            custom = layout['root'] / 'validation/session'
            report = dict(cache=dict(sessionCacheDirectory=str(custom)))
            self.assertEqual(session_directory(report, sdk, mod), custom)
            self.assertEqual(report_cache_root(report, sdk, mod), layout['root'])
            self.assertEqual(session_directory({}, sdk, mod), layout['session'])


if __name__ == '__main__':
    unittest.main()
