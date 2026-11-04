"""Fast checks for identical old/new benchmark configuration and resolver inputs."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
import xml.etree.ElementTree as E

sys.dont_write_bytecode = True
SPEC = importlib.util.spec_from_file_location('prepare_sdk_benchmark',
    Path(__file__).resolve().parents[1] / 'scripts/PrepareCompilerSdkBenchmark.py')
P = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(P)


class SdkPreparationTests(unittest.TestCase):
    def configuration(self, startup=b''):
        return (b'<?xml version="1.0" encoding="utf-8"?>\r\n<configuration>\r\n'
                b'<configSections><section name="assetbuilder" type="Original.Handler,Original"/></configSections>'
                b'<assetbuilder defaultDataPaths=".;.\\Mods;.\\SAGEXML" defaultArtPaths=".\\Art" '
                b'defaultAudioPaths=".\\Audio"><plugins><plugin targetType="Original.Plugin,Original" '
                b'useBuildCache="false"/></plugins><buildConfigurations>'
                b'<buildConfiguration name="LowLOD" postfix="L"/>'
                b'<buildConfiguration name="MediumLOD" postfix="M"/>'
                b'</buildConfigurations></assetbuilder>' + startup + b'</configuration>')

    def test_startup_only_change_preserves_plugin_and_lod_configuration(self):
        for old in (b'', b'<startup><supportedRuntime version="v2.0.50727"/></startup>', b'<startup/>'):
            before = self.configuration(old)
            after = P.startup_config(before)
            original = E.fromstring(before)
            effective = E.fromstring(after)
            self.assertEqual(P.semantic(original.find('assetbuilder')), P.semantic(effective.find('assetbuilder')))
            self.assertEqual(effective.find('startup').attrib, {'useLegacyV2RuntimeActivationPolicy': 'true'})
            self.assertEqual(effective.find('startup/supportedRuntime').attrib, {'version': 'v4.0'})
            # The complete original assetbuilder byte range remains untouched.
            start, end = before.index(b'<assetbuilder'), before.index(b'</assetbuilder>') + len(b'</assetbuilder>')
            self.assertIn(before[start:end], after)
            self.assertEqual(len(effective.findall('assetbuilder/buildConfigurations/buildConfiguration')), 2)

    def test_resolver_keeps_shared_mod_precedence_and_missing_candidates(self):
        with tempfile.TemporaryDirectory() as temp:
            sdk = Path(temp) / 'sdk'
            mod = Path(temp) / 'mod'
            for path in (sdk, mod / 'Data', sdk / 'Mods', sdk / 'SageXml', sdk / 'Art'):
                path.mkdir(parents=True, exist_ok=True)
            roots, missing = P.effective_roots(sdk, mod, E.fromstring(self.configuration()).find('assetbuilder'))
            # Win32 temp directories can be supplied as 8.3 aliases. Compare
            # physical identities while retaining every ordered candidate.
            resolved = lambda values: [Path(value).resolve() for value in values]
            self.assertEqual(Path(roots['dataRoot']).resolve(), sdk.resolve())
            self.assertEqual(resolved(roots['dataPaths']), resolved([mod / 'Data', sdk, sdk / 'Mods', sdk / 'SageXml']))
            self.assertEqual(resolved(roots['artPaths']), resolved([mod / 'Art', sdk / 'Art']))
            self.assertEqual(resolved(roots['audioPaths']), resolved([mod / 'Audio', sdk / 'Audio']))
            self.assertEqual(missing['audioPaths'], roots['audioPaths'])
            self.assertEqual(resolved(missing['artPaths']), resolved([mod / 'Art']))
            self.assertFalse((mod / 'Audio').exists())

    def test_resolver_preserves_duplicate_candidates_and_config_order(self):
        with tempfile.TemporaryDirectory() as temp:
            sdk, mod = Path(temp) / 'sdk', Path(temp) / 'mod'
            sdk.mkdir()
            mod.mkdir()
            section = E.fromstring(self.configuration()).find('assetbuilder')
            section.set('defaultDataPaths', '.;.;.\\Missing;.\\Mods')
            roots, missing = P.effective_roots(sdk, mod, section)
            resolved = lambda values: [Path(value).resolve() for value in values]
            self.assertEqual(resolved(roots['dataPaths']), resolved([mod / 'Data', sdk, sdk, sdk / 'Missing', sdk / 'Mods']))
            self.assertEqual(resolved(missing['dataPaths']), resolved([mod / 'Data', sdk / 'Missing', sdk / 'Mods']))

    def test_rejects_ambiguous_startup(self):
        with self.assertRaisesRegex(ValueError, 'Ambiguous'):
            P.startup_config(self.configuration(b'<startup/><startup/>'))


if __name__ == '__main__':
    unittest.main()
