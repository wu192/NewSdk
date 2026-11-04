"""Portable checks of admission, real snapshot replay, scheduling and budgets."""
import csv
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1] / 'scripts'))
import BenchmarkHotCache as B
import HotCacheReport as R
from VerifyHotCacheApproval import verify


def sample(wall=9000, elapsed=9000, phase='hot_before', performance='off', formal=True):
    row = dict(case='C01', mode='both-deflate', jobs='auto', phase=phase, wallMs=wall,
               reportElapsedMs=elapsed, cpuMs=42, exitCode=0, status='qualified',
               formal=formal, performance=performance, label='sample', repeat=1)
    row['cache'] = dict(xmlParse=0, instanceCompile=0, documentReuse=20, hits=0, misses=0)
    report = dict(exitCode=0, status='succeeded', elapsedMs=elapsed,
                  effectiveOptions={k: dict(Value='true') for k in ('bc', 'sc', 'csc')},
                  cache=dict(xmlParse=0, instanceCompile=0, documentReuse=20, hits=0, misses=0))
    return row, report


class AdmissionTests(unittest.TestCase):
    def test_stream_ids_come_from_actual_cli_plan_tasks(self):
        self.assertEqual(B.stream_ids(dict(plan=dict(Tasks=[dict(Id='base/Mod'), dict(Id='apt/HUD')]))),
                         ['apt/HUD', 'base/Mod'])

    def test_each_clock_must_strictly_exceed_eight_seconds(self):
        for wall, elapsed in ((8000, 9000), (9000, 8000), (7000, 12000), (12000, 7000)):
            row, report = sample(wall, elapsed)
            self.assertEqual(B.admission(row, report, {})[0], 'under_8s')
        row, report = sample(8000.01, 8001)
        self.assertEqual(B.admission(row, report, {})[0], 'qualified')

    def test_startup_overhead_does_not_substitute_for_compiler_elapsed(self):
        row, report = sample(15000, 3000)
        self.assertFalse(B.admission(row, report, {})[1])

    def test_no_edit_hot_run_must_reuse_without_backend_or_xml_work(self):
        for key in ('xmlParse', 'instanceCompile'):
            row, report = sample()
            report['cache'][key] = 1
            self.assertEqual(B.admission(row, report, {})[0], 'path_not_exercised')

    def test_modified_scene_requires_real_minimum_backend_activity(self):
        row, report = sample(phase='modified')
        report['cache']['instanceCompile'] = 63
        case = dict(expectedPath='backend', minInstanceCompile=64)
        self.assertEqual(B.admission(row, report, case)[0], 'path_not_exercised')
        report['cache']['instanceCompile'] = 64
        self.assertEqual(B.admission(row, report, case)[0], 'qualified')

    def test_failed_exit_or_wrong_cache_mode_cannot_score(self):
        row, report = sample()
        row['exitCode'] = 2
        self.assertEqual(B.admission(row, report, {})[0], 'failed')
        row['exitCode'] = 0
        report['effectiveOptions']['sc']['Value'] = 'false'
        self.assertEqual(B.admission(row, report, {})[0], 'path_not_exercised')

    def test_final_output_recovery_must_recreate_deleted_file(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'final.big'
            row, report = sample(phase='modified')
            row['mutation'] = dict(deletedPaths=[str(path)])
            case = dict(expectedPath='output')
            self.assertEqual(B.admission(row, report, case)[0], 'path_not_exercised')
            path.write_bytes(b'BIG')
            self.assertEqual(B.admission(row, report, case)[0], 'qualified')


class BudgetTests(unittest.TestCase):
    def test_impossible_full_matrix_is_reported_before_claiming_completion(self):
        cases = [dict(id='F%02d-case' % i) for i in range(1, 27)]
        result = B.feasibility(cases)
        self.assertFalse(result['fullExtensionCanFitStrictLowerBound'])
        self.assertGreater(result['fullExtensionStrictLowerBoundSeconds'], 960)

    def test_timeout_is_minimum_of_process_phase_and_whole_job(self):
        clock = [0]
        budget = B.Budget('common', clock=lambda: clock[0])
        with budget.phase('sampling', 18):
            clock[0] = 13
            self.assertEqual(budget.timeout(120), 5)
        clock[0] = 1918
        self.assertEqual(budget.timeout(120), 2)
        clock[0] = 1920
        with self.assertRaises(B.BudgetExhausted):
            budget.timeout(120)

    def test_reentering_calibration_does_not_reset_its_budget(self):
        clock = [0]
        budget = B.Budget('common', clock=lambda: clock[0])
        with budget.phase('calibration', 240):
            clock[0] += 150
        with budget.phase('calibration', 240):
            self.assertEqual(budget.timeout(120), 90)

    def test_full_has_one_total_ceiling_and_includes_prior_setup_time(self):
        with patch.object(B.time, 'time', return_value=120):
            budget = B.Budget('full', job_start_unix=100, clock=lambda: 1000)
        self.assertEqual(budget.elapsed(), 20)
        self.assertEqual(budget.total, 3600)
        self.assertEqual(budget.remaining(), 3340)


class ReplayAndReportTests(unittest.TestCase):
    def test_snapshot_restores_source_and_cache_at_same_absolute_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            mod, out = root / 'mod', root / 'out'
            mod.mkdir(); out.mkdir()
            (mod / 'input.xml').write_text('baseline')
            (out / 'cache').write_text(str(mod / 'input.xml'))
            snapshot = B.Snapshot(mod, out, root / 'snapshot', B.Budget('common'))
            (mod / 'input.xml').write_text('modified')
            (mod / 'new.xml').write_text('new')
            (out / 'cache').unlink()
            snapshot.mark_changed(dict(changedPaths=['input.xml', 'new.xml']))
            snapshot.restore()
            self.assertEqual((mod / 'input.xml').read_text(), 'baseline')
            self.assertFalse((mod / 'new.xml').exists())
            self.assertEqual((out / 'cache').read_text(), str(mod / 'input.xml'))

    def test_snapshot_restores_sdk_cache_separately_from_final_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            mod, out, cache = root / 'mod', root / 'out', root / 'sdk-cache'
            for directory in (mod, out, cache):
                directory.mkdir()
            (cache / 'session.xml').write_bytes(b'original session')
            (out / 'final.big').write_bytes(b'original final')
            snapshot = B.Snapshot(mod, out, root / 'snapshot', B.Budget('common'), cache=cache)
            (cache / 'session.xml').unlink()
            (cache / 'added.bin').write_bytes(b'new')
            (out / 'final.big').write_bytes(b'changed final')
            snapshot.restore()
            self.assertEqual((cache / 'session.xml').read_bytes(), b'original session')
            self.assertFalse((cache / 'added.bin').exists())
            self.assertEqual((out / 'final.big').read_bytes(), b'original final')

    def test_snapshot_replays_same_stamp_changes_and_removes_added_search_roots(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            mod, out = root / 'mod', root / 'out'
            mod.mkdir(); out.mkdir()
            source = mod / 'input.xml'
            source.write_bytes(b'original')
            (out / 'cache').write_bytes(b'baseline')
            snapshot = B.Snapshot(mod, out, root / 'snapshot', B.Budget('common'))
            stamp = source.stat()
            source.write_bytes(b'modified')
            B.os.utime(source, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
            (mod / 'override').mkdir()
            (mod / 'override/leaf').write_bytes(b'new')
            snapshot.mark_changed(dict(changedPaths=['input.xml', 'override/leaf']))
            snapshot.restore()
            self.assertEqual(source.read_bytes(), b'original')
            self.assertFalse((mod / 'override').exists())
            self.assertEqual(source.stat().st_mtime_ns, stamp.st_mtime_ns)

    def test_detailed_failed_and_under8_samples_never_enter_off_medians(self):
        before, _ = sample()
        modified, _ = sample(wall=12000, phase='modified')
        after, _ = sample(wall=10000, phase='hot_after')
        detailed = dict(modified, wallMs=90000, performance='detailed')
        rejected = dict(modified, wallMs=7000, status='under_8s')
        rows = R.summarize([before, modified, after, detailed, rejected])
        self.assertEqual(rows[0]['modifiedN'], 1)
        self.assertEqual(rows[0]['firstChangeExtraMs'], 3000)
        self.assertEqual(rows[0]['reheatedChangeMs'], 1000)

    def test_reports_with_no_results_still_have_csv_headers(self):
        with tempfile.TemporaryDirectory() as temp:
            R.save(temp, dict(samples=[], cases=[], group='common', status='under_8s'))
            for name in ('measurements.csv', 'scenarios.csv', 'phases.csv'):
                with (Path(temp) / name).open(encoding='utf-8-sig') as stream:
                    self.assertTrue(next(csv.reader(stream)))
            self.assertIn('under_8s', (Path(temp) / 'report.md').read_text(encoding='utf-8'))

    def test_independent_repeats_restore_before_each_three_phase_chain(self):
        samples, events = [], []
        def record(case, phase, repeat, formal, **kwargs):
            events.append(phase)
            samples.append(dict(case=case['id'], formal=formal, performance='off', status='qualified'))
        runner = SimpleNamespace(state=dict(samples=samples), workload={}, destination=Path('unused'),
                                 sample=record, budget=SimpleNamespace(check=lambda: None))
        snapshot = SimpleNamespace(restore=lambda: events.append('restore'), mark_changed=lambda mutation: None)
        case = dict(id='C01')
        with patch.object(B, 'apply_case', side_effect=lambda *args: events.append('edit') or {}):
            B.run_case(runner, case, snapshot)
        self.assertEqual(events, ['restore', 'hot_before', 'edit', 'modified', 'hot_after'] * 2)
        self.assertEqual(case['status'], 'complete')

    def test_full_contrasts_reuse_common_controls_without_duplicate_samples(self):
        samples = []
        cases = [dict(id='C00', status='complete'), dict(id='C05', status='complete')]
        cases += [dict(id='F11-mode-' + mode, mode=mode) for mode in B.MODES]
        cases += [dict(id='F11-jobs-' + jobs, jobs=jobs) for jobs in ('1', '4', 'auto')]
        def record(case, phase, repeat=0, formal=False, **kwargs):
            row = dict(case=case['id'], phase=phase, formal=formal, performance='off', status='qualified')
            samples.append(row)
            return row
        runner = SimpleNamespace(state=dict(samples=samples), workload={}, destination=Path('unused'),
                                 sample=record, budget=SimpleNamespace(check=lambda: None))
        snapshot = SimpleNamespace(restore=lambda: None, mark_changed=lambda mutation: None)
        with patch.object(B, 'apply_case', return_value={}):
            B.full_modes(runner, snapshot, cases)
        self.assertEqual(sum(s['formal'] for s in samples), 17)
        self.assertEqual(sum(s['phase'] == 'mode_warmup' for s in samples), 2)
        self.assertTrue(all(c['status'] == 'complete' for c in cases))
        self.assertEqual(cases[-1]['reusedCase'], 'C05')


class ApprovalTests(unittest.TestCase):
    def test_full_rejects_unprotected_and_unsupported_environment(self):
        for environment in ({}, dict(protection_rules=[]), dict(protection_rules=[dict(type='wait_timer')])):
            with self.assertRaises(ValueError):
                verify(environment)
        self.assertTrue(verify(dict(protection_rules=[dict(type='required_reviewers', reviewers=[dict(id=1)])]))['requiredReviewersConfigured'])


if __name__ == '__main__':
    unittest.main()
