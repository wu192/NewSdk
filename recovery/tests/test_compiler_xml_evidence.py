"""Receipt aggregation regressions; synthetic evidence is not Windows acceptance."""
import copy
import hashlib
import importlib.util
import json
import pathlib
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('xml_evidence', ROOT / 'recovery/scripts/CheckCompilerXmlEvidence.py')
CHECK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECK)


def sha(text):
    return hashlib.sha256(text.encode()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


class XmlEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repository_temp = tempfile.TemporaryDirectory()
        cls.repository = pathlib.Path(cls.repository_temp.name)
        def git(*args):
            return CHECK.git_bytes(cls.repository, *args)
        git('init', '-q')
        tests = cls.repository / 'recovery/tests'
        tests.mkdir(parents=True)
        names = ['Xmlpowers', 'Xmllocomotor', 'Xmlfxparticle', 'Xmlattributes', 'Xmlplayers', 'Xmlai', 'Xmlaudio']
        names += [f'Xmlgroup{i:02d}' for i in range(56)]
        for name in names:
            (tests / (name + 'Oracle.cs')).write_text('synthetic oracle ' + name + '\n', encoding='utf-8')
        (tests / 'Xmlgroup00Cases.cs').write_text('synthetic cases\n', encoding='utf-8')
        (tests / 'XmlNodeOracle.cs').write_text('excluded oracle', encoding='utf-8')
        for path, data in [('recovery/scripts/PrepareCompilerXmlMatrices.py', 'adapter'),
                           ('recovery/scripts/TestCompilerXmlMatrices.ps1', 'runner'),
                           ('recovery/tests/XmlProductOracleTransport.cs', 'transport'),
                           ('SDK/tools/BinaryAssetBuilder.XmlCompiler.dll', 'synthetic original oracle')]:
            target = cls.repository / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(data if path.endswith('.dll') else data + '\n', encoding='utf-8')
        git('add', '.')
        git('-c', 'user.name=Evidence Test', '-c', 'user.email=evidence@example.invalid', 'commit', '-qm', 'synthetic evidence inputs')
        cls.source = git('rev-parse', 'HEAD').decode().strip()
        cls.expected = CHECK.repository_groups(cls.repository, cls.source)
        cls.template = tempfile.TemporaryDirectory()
        root = pathlib.Path(cls.template.name)
        matrix = dict(schemaVersion=1, succeeded=True, sourceCommit=cls.source, runId='123', runAttempt='1',
                      status=CHECK.MATRIX_STATUS, sdkValidated=False,
                      groups=[dict(group=n, inputs=inputs) for n, inputs in cls.expected.items()], modes=[])
        for field, path in [('adapterSha256', 'recovery/scripts/PrepareCompilerXmlMatrices.py'),
                            ('runnerSha256', 'recovery/scripts/TestCompilerXmlMatrices.ps1'),
                            ('transportSha256', 'recovery/tests/XmlProductOracleTransport.cs'),
                            ('originalDllSha256', 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll')]:
            matrix[field] = CHECK.revision_hash(cls.repository, cls.source, path)
        for mode in CHECK.MODES:
            folder = root / 'artifacts/native-product' / mode
            inputs = [dict(kind=k, path='C:/' + mode + '/' + k, sha256=sha(mode + k))
                      for k in ('foundation', 'application', 'entry', 'nativeOrBridge', 'resource')]
            recipe = dict(schemaVersion=1, sourceCommit=cls.source, configuration=mode,
                          arguments=['/ENTRY:NewModCompiler.Hosting.CompilerEntry.Main'], inputs=inputs)
            write(folder / 'link-inputs.json', recipe)
            product = dict(schemaVersion=2, sourceCommit=cls.source, configuration=mode,
                           entryPoint='NewModCompiler.Hosting.CompilerEntry.Main', exeSha256=sha(mode + 'product'),
                           foundationSha256=inputs[0]['sha256'], applicationSha256=inputs[1]['sha256'],
                           nativeBuild=dict(objects=[inputs[3]['path']]),
                           resources=[dict(path=inputs[4]['path'], sha256=inputs[4]['sha256'])],
                           runtimeFiles=['NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'],
                           texturePlugin={'implementation': 'built-in-source', 'externalRuntimeFiles': []})
            write(folder / 'build.json', product)
            host = dict(schemaVersion=2, sourceCommit=cls.source, configuration=mode,
                        sameProductionObjects=True, productionExeUnchanged=True,
                        productExeSha256=product['exeSha256'], exeSha256=sha(mode + 'host'),
                        entryPoint='NewModCompiler.Validation.ValidationEntry.Main', productionEntryPoint=product['entryPoint'],
                        inputs=[r for r in inputs if r['kind'] != 'entry'],
                        linkRecipeSha256=CHECK.digest(folder / 'link-inputs.json'),
                        runtimeFiles=[dict(name=n, sha256=sha(n)) for n in ('NewModCompiler.exe.config', 'antlr.runtime.dll')])
            write(folder / 'validation/build.json', host)
            state = dict(configuration=mode, productExeSha256=product['exeSha256'], validationHostSha256=host['exeSha256'],
                         passed=True, groups=[])
            matrix['modes'].append(state)
            for name in cls.expected:
                f, c, r, l = 2, 12, 1, 2
                if name in ('Xmlpowers', 'Xmllocomotor', 'Xmlfxparticle', 'Xmlattributes'):
                    c = 6
                if name in ('Xmlplayers', 'Xmlai'):
                    f, c, r, l = 1000, 6000, 30, 300
                rows = []
                logs = []
                wire = []
                for p in CHECK.PLATFORMS:
                    rows.append(dict(platform=p, fixtures=f, comparisons=c, rejections=r, layouts=l))
                    if name == 'Xmlattributes':
                        logs.append(f'SUMMARY platform={p} fixtures={f} threeArrayComparisons={c} rejected={r} independentLayouts={l}')
                    elif name == 'Xmlaudio':
                        logs.append(f'PASS platform={p} fixtures={f} threeArrayComparisons={c//2} directArrayComparisons={c//2} rejections={r} independentLayouts={l}')
                    else:
                        logs.append(f'PASS platform={p} fixtures={f} arrayComparisons={c} rejections={r} layoutAssertions={l}')
                    for _ in range(f):
                        wire.append(f'{len(wire)+1}\t{p}\t' + '\t'.join([sha('data')] * 4))
                calls = len(wire)
                logs.append('ORACLE-END calls=' + str(calls))
                wire.append('END\t' + str(calls))
                group_path = root / 'evidence/refactor/native-xml-matrices/123-1' / mode / name
                group_path.mkdir(parents=True)
                (group_path / 'host.log').write_text('\n'.join(logs) + '\n', encoding='utf-8')
                (group_path / 'original.tsv').write_text('\n'.join(wire) + '\n', encoding='utf-8')
                state['groups'].append(dict(group=name, passed=True, generatedHostSha256=sha(name), platforms=rows,
                                            originalCalls=calls, transcriptSha256=CHECK.digest(group_path / 'original.tsv')))
        write(root / 'evidence/refactor/native-xml-matrices/123-1/validation.json', matrix)

    @classmethod
    def tearDownClass(cls):
        cls.template.cleanup()
        cls.repository_temp.cleanup()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        shutil.copytree(self.template.name, self.root, dirs_exist_ok=True)
        self.matrix_path = self.root / 'evidence/refactor/native-xml-matrices/123-1/validation.json'
        self.pin_patch = mock.patch.object(CHECK, 'ORIGINAL_XML_SHA', sha('synthetic original oracle'))
        self.pin_patch.start()
        self.addCleanup(self.pin_patch.stop)
        self.addCleanup(self.temp.cleanup)

    def check(self):
        return CHECK.check(self.root, self.repository, self.source)

    def mutate(self, path, action):
        value = CHECK.read_json(path)
        action(value)
        write(path, value)

    def rejected(self):
        with self.assertRaises((ValueError, OSError)):
            self.check()

    def test_recorded_revision_group_inventory_uses_git_bytes(self):
        # A separate disposable repository works in the guard's sparse checkout;
        # no SDK binaries, network or historical project commits are required.
        with tempfile.TemporaryDirectory() as directory:
            repo = pathlib.Path(directory)
            def git(*args):
                return subprocess.check_output(['git', '-C', str(repo), *args], stderr=subprocess.PIPE)
            git('init', '-q')
            tests = repo / 'recovery/tests'
            tests.mkdir(parents=True)
            for i in range(63):
                (tests / f'Xmlgroup{i:02d}Oracle.cs').write_text('old', encoding='utf-8')
            (tests / 'Xmlgroup00Cases.cs').write_text('cases', encoding='utf-8')
            (tests / 'XmlNodeOracle.cs').write_text('excluded', encoding='utf-8')
            git('add', 'recovery')
            git('-c', 'user.name=Evidence Test', '-c', 'user.email=evidence@example.invalid', 'commit', '-qm', 'fixture')
            source = git('rev-parse', 'HEAD').decode().strip()
            (tests / 'Xmlgroup00Oracle.cs').write_text('new uncommitted', encoding='utf-8')
            groups = CHECK.repository_groups(repo, source)
            self.assertEqual(len(groups), 63)
            self.assertEqual(groups['Xmlgroup00'][0]['sha256'], sha('old'))
            self.assertEqual(len(groups['Xmlgroup00']), 2)
            self.assertEqual(CHECK.digest(tests / 'Xmlgroup00Oracle.cs'), sha('new uncommitted'))
            self.assertEqual(hashlib.sha256(CHECK.git_bytes(repo, 'show', source + ':recovery/tests/Xmlgroup00Oracle.cs')).hexdigest(), sha('old'))

    def test_complete_receipts_and_exact_duplicate_copies(self):
        source = self.root / 'artifacts/native-product/Debug/build.json'
        duplicate = self.root / 'evidence/refactor/native/123-1/Debug/build.json'
        duplicate.parent.mkdir(parents=True)
        shutil.copyfile(source, duplicate)
        result = self.check()
        self.assertEqual([r['groups'] for r in result['configurations']], [63, 63])
        self.assertIn('not independently rehashed', result['limitations'][0])

    def test_historical_dll_runtime_receipts_remain_verifiable(self):
        def historical(product):
            product.pop('texturePlugin')
            product['runtimeFiles'] += ['BinaryAssetBuilder.TextureCompiler.dll',
                                       'BinaryAssetBuilder.TextureCompiler.dll.patch.json']
        for mode in ('Debug', 'Release'):
            self.mutate(self.root / f'artifacts/native-product/{mode}/build.json', historical)
        self.assertEqual(len(self.check()['configurations']), 2)

    def test_missing_group_and_duplicate_group_rejected(self):
        for duplicate in (False, True):
            with self.subTest(duplicate=duplicate):
                original = self.matrix_path.read_bytes()
                self.mutate(self.matrix_path, lambda x: x['modes'][0]['groups'].__setitem__(0, copy.deepcopy(x['modes'][0]['groups'][1])) if duplicate else x['modes'][0]['groups'].pop())
                self.rejected()
                self.matrix_path.write_bytes(original)

    def test_duplicate_mode_and_inventory_rejected(self):
        for field in ('modes', 'groups'):
            with self.subTest(field=field):
                original = self.matrix_path.read_bytes()
                self.mutate(self.matrix_path, lambda x: x[field].__setitem__(0, copy.deepcopy(x[field][1])))
                self.rejected()
                self.matrix_path.write_bytes(original)

    def test_failed_or_partial_receipt_rejected(self):
        for action in (lambda x: x.update(succeeded=False), lambda x: x.update(status='started'),
                       lambda x: x['modes'][1].update(passed=False),
                       lambda x: x['modes'][0]['groups'][0].update(passed=False)):
            with self.subTest(action=action):
                original = self.matrix_path.read_bytes()
                self.mutate(self.matrix_path, action)
                self.rejected()
                self.matrix_path.write_bytes(original)

    def test_source_exe_link_runtime_and_input_drift_rejected(self):
        cases = [
            ('artifacts/native-product/Debug/build.json', lambda x: x.update(sourceCommit='f'*40)),
            ('artifacts/native-product/Debug/validation/build.json', lambda x: x.update(productExeSha256=sha('other'))),
            ('artifacts/native-product/Debug/validation/build.json', lambda x: x.update(exeSha256=sha('other'))),
            ('artifacts/native-product/Debug/validation/build.json', lambda x: x['inputs'].pop()),
            ('artifacts/native-product/Debug/validation/build.json', lambda x: x['runtimeFiles'].pop()),
            ('artifacts/native-product/Debug/link-inputs.json', lambda x: x['inputs'][0].update(sha256=sha('other'))),
            ('evidence/refactor/native-xml-matrices/123-1/validation.json', lambda x: x['groups'][0]['inputs'][0].update(sha256=sha('other'))),
        ]
        for name, action in cases:
            with self.subTest(name=name, action=action):
                path = self.root / name
                original = path.read_bytes()
                self.mutate(path, action)
                self.rejected()
                path.write_bytes(original)

    def test_missing_build_receipt_rejected(self):
        (self.root / 'artifacts/native-product/Release/validation/build.json').unlink()
        self.rejected()

    def test_divergent_duplicate_receipt_rejected(self):
        value = CHECK.read_json(self.root / 'artifacts/native-product/Debug/build.json')
        value['exeSha256'] = sha('other')
        write(self.root / 'duplicate/build.json', value)
        self.rejected()

    def test_missing_platform_and_raw_count_drift_rejected(self):
        group = 'Xmlgroup00'
        path = self.matrix_path.parent / 'Debug' / group / 'host.log'
        original = path.read_bytes()
        for change in (lambda s: '\n'.join(s.splitlines()[1:]) + '\n',
                       lambda s: s.replace('arrayComparisons=12', 'arrayComparisons=6', 1),
                       lambda s: s.replace('layoutAssertions=2', 'layoutAssertions=1', 1),
                       lambda s: s + 'ORACLE-END calls=8\n'):
            with self.subTest(change=change):
                path.write_text(change(original.decode()), encoding='utf-8')
                self.rejected()
                path.write_bytes(original)

    def test_single_path_and_dual_path_contracts_rejected(self):
        for name, before, after in [('Xmllocomotor', 'arrayComparisons=6', 'arrayComparisons=12'),
                                    ('Xmlaudio', 'directArrayComparisons=6', 'directArrayComparisons=3'),
                                    ('Xmlai', 'rejections=30', 'rejections=29')]:
            path = self.matrix_path.parent / 'Debug' / name / 'host.log'
            original = path.read_bytes()
            path.write_text(original.decode().replace(before, after, 1), encoding='utf-8')
            self.rejected()
            path.write_bytes(original)

    def test_transcript_hash_sequence_and_terminal_rejected(self):
        name = 'Xmlgroup00'
        path = self.matrix_path.parent / 'Debug' / name / 'original.tsv'
        original = path.read_bytes()
        for data in (original.replace(b'1\t0', b'2\t0', 1), original.rsplit(b'END\t', 1)[0], original.replace(b'\t0\t', b'\t99\t')):
            with self.subTest(data=data[:20]):
                path.write_bytes(data)
                self.rejected()
                path.write_bytes(original)

    def test_exact_windows_crlf_source_checkout_accepted(self):
        def windows_hash(path):
            data = CHECK.git_bytes(self.repository, 'show', self.source + ':' + path)
            self.assertNotIn(b'\r', data)
            return hashlib.sha256(data.replace(b'\n', b'\r\n')).hexdigest()
        def update(value):
            for field, path in [('adapterSha256', 'recovery/scripts/PrepareCompilerXmlMatrices.py'),
                                ('runnerSha256', 'recovery/scripts/TestCompilerXmlMatrices.ps1'),
                                ('transportSha256', 'recovery/tests/XmlProductOracleTransport.cs')]:
                value[field] = windows_hash(path)
            for group in value['groups']:
                for item in group['inputs']:
                    item['sha256'] = windows_hash(item['path'])
        self.mutate(self.matrix_path, update)
        self.assertEqual(self.check()['sourceCheckoutHashForms'], ['windows-crlf'])
        # Converting source text cannot excuse a changed implementation byte.
        self.mutate(self.matrix_path, lambda x: x['groups'][0]['inputs'][0].update(sha256=sha('different source\r\n')))
        self.rejected()

    def test_wrong_pinned_original_rejected(self):
        with mock.patch.object(CHECK, 'ORIGINAL_XML_SHA', sha('wrong pinned original')):
            self.rejected()

    def test_tool_and_oracle_fingerprints_rejected(self):
        for field in ('adapterSha256', 'runnerSha256', 'transportSha256', 'originalDllSha256'):
            with self.subTest(field=field):
                original = self.matrix_path.read_bytes()
                self.mutate(self.matrix_path, lambda x: x.update({field: sha('different')}))
                self.rejected()
                self.matrix_path.write_bytes(original)

    def test_invalid_run_attempt_rejected(self):
        for attempt in (True, False, None, '', '0', 0):
            with self.subTest(attempt=attempt):
                original = self.matrix_path.read_bytes()
                self.mutate(self.matrix_path, lambda x: x.update(runAttempt=attempt))
                self.rejected()
                self.matrix_path.write_bytes(original)

    def test_duplicate_json_key_rejected(self):
        self.matrix_path.write_text('{"succeeded":true,"succeeded":true}', encoding='utf-8')
        self.rejected()


if __name__ == '__main__':
    unittest.main()
