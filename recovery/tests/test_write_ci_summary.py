"""CI evidence selection and bounded summaries, without compiler or MOD execution."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/WriteCiSummary.py'
SPEC = importlib.util.spec_from_file_location('write_ci_summary', SCRIPT)
SUMMARY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARY)


class CiSummaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.addCleanup(self.temporary.cleanup)

    def put(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')
        return path

    def test_recursive_allowlist_exclusions_and_duplicates(self):
        root_report = self.put('evidence/measurement.json', '{}')
        nested_report = self.put('evidence/phase/process.json', '{}')
        self.put('evidence/phase/stdout.log', 'diagnostic')
        self.put('evidence/baseline-streams/new/mod.bin', 'reproducible output')
        self.put('evidence/phase/ignored.json', '{}')
        patterns = [str(self.root / 'evidence/**/*.json'), str(root_report),
                    '!' + str(self.root / '**/ignored.json'), '', '  ']
        self.assertEqual([root_report, nested_report], SUMMARY.selected_files(patterns))

    def test_exclusions_filter_candidates_without_scanning_unrelated_trees(self):
        root_report = self.put('evidence/report.json', '{}')
        self.put('evidence/bin/ignored.json', '{}')
        self.put('evidence/nested/bin/ignored.json', '{}')
        pattern = str(self.root / 'evidence/**/*.json')
        original_glob = SUMMARY.glob.iglob
        with mock.patch.object(SUMMARY.glob, 'iglob', wraps=original_glob) as scan:
            actual = SUMMARY.selected_files([pattern, '!**/bin/**'])
        self.assertEqual([root_report], actual)
        scan.assert_called_once_with(pattern, recursive=True)

    def test_size_uses_stat_and_never_reads_large_or_binary_files(self):
        report = self.put('report.md', 'Small measured result')
        raw = self.put('mod.bin', 'raw output must not be read')
        large = self.root / 'measurement.json'
        with large.open('wb') as stream:
            stream.truncate(9 * 1024 * 1024)
        read_text = Path.read_text
        reads = []

        def guarded(path, *args, **kwargs):
            self.assertEqual(report, path, 'Only a small displayable report may be read')
            reads.append(path)
            return read_text(path, *args, **kwargs)

        with mock.patch.object(Path, 'read_text', guarded):
            output = SUMMARY.summarize('Benchmark', [str(self.root / '*')])
        expected = sum(path.stat().st_size for path in (report, raw, large))
        self.assertIn(f'3 files, {expected:,} bytes', output)
        self.assertIn('exceeds 5 MiB', output)
        self.assertIn('Small measured result', output)
        self.assertNotIn('raw output must not be read', output)
        self.assertEqual([report], reads)

    def test_summary_body_and_file_table_are_bounded(self):
        for index in range(20):
            self.put(f'{index:02d}/report.md', 'x' * 32000)
        output = SUMMARY.summarize('Bounded', [str(self.root / '**/report.md')])
        self.assertEqual(15, sum(line.startswith('| `') for line in output.splitlines()))
        bodies = [line for line in output.splitlines() if line and not line.strip('x')]
        self.assertLessEqual(sum(map(len, bodies)), 128 * 1024)
        self.assertLess(len(output), 150 * 1024)
        self.assertIn('20 files, 640,000 bytes', output)

    def test_zero_matches_and_malformed_json_do_not_hide_diagnostics(self):
        empty = SUMMARY.summarize('No evidence yet', [str(self.root / '**/*.json')])
        self.assertIn('0 files, 0 bytes', empty)
        self.assertNotIn('Largest selected files', empty)
        self.put('validation.json', '{incomplete')
        self.put('report.md', 'Failure remains visible')
        output = SUMMARY.summarize('Failed run', [str(self.root / '*')])
        self.assertIn('Failure remains visible', output)
        self.assertIn('2 files', output)

    def test_small_json_displays_status_without_full_inventory(self):
        self.put('measurement.json', json.dumps({'status': 'outputs-differ',
                  'errors': ['mod.bin differs'], 'outputInventory': {'large': 'omit'}}))
        output = SUMMARY.summarize('Correctness', [str(self.root / '*.json')])
        self.assertIn('outputs-differ', output)
        self.assertIn('mod.bin differs', output)
        self.assertNotIn('outputInventory', output)

    def test_large_result_inventory_still_displays_acceptance_status(self):
        self.put('validation.json', json.dumps({'succeeded': True,
                 'outputInventory': 'x' * (3 * 1024 * 1024)}))
        output = SUMMARY.summarize('Complete correctness result', [str(self.root / '*.json')])
        self.assertIn('"succeeded": true', output)
        self.assertNotIn('outputInventory', output)
        self.assertLess(len(output), 4000)

    def test_main_combines_multiline_environment_cli_and_appends_summary(self):
        first = self.put('first.md', 'First report')
        second = self.put('second.md', 'Second report')
        self.put('excluded.md', 'Must be excluded')
        destination = self.put('step-summary.txt', 'Existing summary\n')
        environment = {'ARTIFACT_PATHS': '\n' + str(self.root / '*.md') + '\n',
                       'GITHUB_STEP_SUMMARY': str(destination)}
        with mock.patch.dict(os.environ, environment), mock.patch('sys.argv',
                ['WriteCiSummary.py', '--title', 'Combined', '--paths', str(first),
                 '--paths', str(second), '--paths', '!' + str(self.root / 'excluded.md')]):
            SUMMARY.main()
        output = destination.read_text(encoding='utf-8')
        self.assertTrue(output.startswith('Existing summary\n### Combined'))
        self.assertIn('2 files', output)
        self.assertIn('First report', output)
        self.assertIn('Second report', output)
        self.assertNotIn('Must be excluded', output)

    def test_main_prints_when_step_summary_is_unavailable(self):
        with mock.patch.dict(os.environ, {'ARTIFACT_PATHS': '', 'GITHUB_STEP_SUMMARY': ''}), \
                mock.patch('sys.argv', ['WriteCiSummary.py']), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            SUMMARY.main()
        self.assertIn('0 files, 0 bytes', output.getvalue())


if __name__ == '__main__':
    unittest.main()
