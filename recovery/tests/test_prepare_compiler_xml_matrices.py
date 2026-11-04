"""Local binding contracts; Windows execution remains the acceptance gate."""
import importlib.util
import pathlib
import re
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('prepare_xml_matrices', ROOT / 'recovery/scripts/PrepareCompilerXmlMatrices.py')
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)

class PrepareXmlMatrixTests(unittest.TestCase):
    def test_all_groups_preserve_assertions_and_partial_cases(self):
        with tempfile.TemporaryDirectory() as directory:
            rows = PREPARE.prepare(ROOT, pathlib.Path(directory))
            self.assertEqual(len(rows), 64)
            self.assertEqual(len({r['group'] for r in rows}), 64)
            for row in rows:
                group = row['group']
                expected = [ROOT / ('recovery/tests/' + group + 'Oracle.cs')] + sorted((ROOT / 'recovery/tests').glob(group + '*Cases.cs')) + [ROOT / 'recovery/tests/PluginSchemaBoundary.cs']
                self.assertEqual([i['path'] for i in row['inputs']], [p.relative_to(ROOT).as_posix() for p in expected])
                for original, target in zip(expected, row['sources']):
                    before = original.read_text(encoding='utf-8-sig')
                    after = pathlib.Path(target).read_text(encoding='utf-8')
                    # Every fixture/assertion/helper call survives adaptation;
                    # transport is the only added call in successful Main.
                    for name in ('Equal', 'Reject', 'Check', 'Assert', 'Test', 'TestDoc', 'TestDocument', 'Matrix', 'Cases'):
                        self.assertEqual(len(re.findall(r'\b' + name + r'\s*\(', before)), len(re.findall(r'\b' + name + r'\s*\(', after)), (group, name))
                    if original.name.endswith('Cases.cs'):
                        self.assertEqual(after, before.replace('IAssetBuilderPlugin', 'XmlOriginalClient'))
                    self.assertNotIn('Assembly.LoadFrom', after)
                    self.assertNotIn('Activator.CreateInstance', after)
                self.assertEqual(pathlib.Path(row['sources'][0]).read_text(encoding='utf-8').count('XmlOriginalClient.Finish();'), 1)
            by_group = {r['group']: r for r in rows}
            self.assertEqual(by_group['Xmlai']['defines'], ['XMLAI_PERSONALITY_EXTENSION'])
            self.assertTrue(by_group['Xmlpowers']['repositoryArgument'])
            self.assertEqual(sum(r['dynamicAssemblyArgument'] for r in rows), 5)

    def test_binding_drift_is_rejected(self):
        source = (ROOT / 'recovery/tests/XmlaudioOracle.cs').read_text(encoding='utf-8-sig')
        with self.assertRaises(ValueError):
            PREPARE.adapt(source.replace('BinaryAssetBuilder.XmlCompiler.Plugin', 'Different.Plugin'))
        with self.assertRaises(ValueError):
            PREPARE.adapt(source + '\n// Assembly.LoadFrom unexpected new binding')

    def test_original_abi_checks_still_execute_remotely(self):
        for group in ('Xmllocomotor', 'Xmlfxparticle'):
            before = (ROOT / ('recovery/tests/' + group + 'Oracle.cs')).read_text(encoding='utf-8-sig')
            after, _ = PREPARE.adapt(before)
            self.assertIn('XmlOriginalClient.OriginalSize("SageBinaryData.', after)
            self.assertIn('rootType>=0', after)
            # The existing expected size and Check assertion are retained.
            expected = re.search(r'Marshal.SizeOf\(rootType\)==(\d+)', before).group(1)
            self.assertIn('rootType==' + expected, after)

if __name__ == '__main__':
    unittest.main()
