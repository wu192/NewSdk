"""Short rejection checks for the benchmark's accepted-binary boundary."""
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import xml.etree.ElementTree as E
import zipfile

SPEC = importlib.util.spec_from_file_location('prepare_cache_benchmark',
    Path(__file__).resolve().parents[1] / 'scripts/PrepareCompilerCacheBenchmark.py')
P = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(P)


class PreparationTests(unittest.TestCase):
    def bundle(self, root, bad_patch=False, extra=False, bad_file=False, source=P.SOURCE, run=P.RUN, attempt=1):
        files = {name: name.encode() for name in P.NAMES}
        patch = dict(mode='redirect', targetExeSha256=P.sha(files['NewModCompiler.exe']),
            patchedSha256=P.sha(files['BinaryAssetBuilder.TextureCompiler.dll']),
            originalSha256='4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e',
            protectedPeAudit=dict(status='passed', mode='redirect'))
        if bad_patch:
            patch['targetExeSha256'] = '0' * 64
        files['BinaryAssetBuilder.TextureCompiler.dll.patch.json'] = json.dumps(patch).encode()
        identities = [dict(name=name, bytes=len(data), sha256=P.sha(data)) for name, data in files.items()]
        if bad_file:
            files['NewModCompiler.exe'] += b'tampered'
        inner = io.BytesIO()
        with zipfile.ZipFile(inner, 'w') as archive:
            for name, data in files.items():
                archive.writestr(name, data)
            if extra:
                archive.writestr('../other.dll', b'extra')
        data = inner.getvalue()
        prefix = 'artifacts/final-release/%s-%s/' % (run, attempt)
        identity = dict(binarySourceCommit=source, runId=run, runAttempt=str(attempt))
        release = dict(identity, sourceCommit=source, succeeded=True,
            configurations=[dict(configuration=name, rawOutputEquality=True, zipSha256=P.sha(data))
                            for name in ('Debug', 'Release')])
        manifest = dict(identity, configuration='Release', platform='Win32',
            status='verified-small-mod-deployment', packagingSourceCommit=source, texturePatch=patch,
            archive=dict(name='NewModCompiler-Win32-Release.zip', bytes=len(data), sha256=P.sha(data)),
            files=identities)
        target = root / 'artifact.zip'
        with zipfile.ZipFile(target, 'w') as archive:
            archive.writestr(prefix + 'release.json', json.dumps(release))
            archive.writestr(prefix + 'NewModCompiler-Win32-Release.manifest.json', json.dumps(manifest))
            archive.writestr(prefix + 'NewModCompiler-Win32-Release.zip', data)
        return target

    def test_explicit_new_acceptance_identity_does_not_replace_historical_default(self):
        with tempfile.TemporaryDirectory() as temp:
            source, run = 'a' * 40, '12345'
            path = self.bundle(Path(temp), source=source, run=run, attempt=2)
            files, rows = P.validate_bundle(path, P.file_sha(path), source, run, 2)
            self.assertEqual(set(files), P.NAMES)
            with self.assertRaises((KeyError, ValueError)):
                P.validate_bundle(path, P.file_sha(path))
            with self.assertRaisesRegex(ValueError, 'identity differs'):
                P.validate_bundle(path, P.file_sha(path), 'b' * 40, run, 2)

    def test_accepted_members_are_returned_without_extracting_archive_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self.bundle(Path(temp))
            files, rows = P.validate_bundle(path, P.file_sha(path))
            self.assertEqual(set(files), P.NAMES)
            self.assertEqual(len(rows), 5)

    def test_outer_digest_is_mandatory(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self.bundle(Path(temp))
            with self.assertRaisesRegex(ValueError, 'artifact SHA256'):
                P.validate_bundle(path)

    def test_rejects_unbound_texture_even_when_inner_hashes_match(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self.bundle(Path(temp), bad_patch=True)
            with self.assertRaisesRegex(ValueError, 'Texture patch'):
                P.validate_bundle(path, P.file_sha(path))

    def test_rejects_extra_or_escaping_runtime_member(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self.bundle(Path(temp), extra=True)
            with self.assertRaisesRegex(ValueError, 'exactly five root'):
                P.validate_bundle(path, P.file_sha(path))

    def test_rejects_tampered_runtime_despite_valid_zip_hash(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self.bundle(Path(temp), bad_file=True)
            with self.assertRaisesRegex(ValueError, 'Runtime file digest'):
                P.validate_bundle(path, P.file_sha(path))

    def test_documentation_exception_cannot_hide_compiler_input(self):
        for name in ('SDK/README.md', 'recovery/src/recipe.md', 'recovery/probes/metadata.md',
                     'recovery/scripts/BuildCompiler.ps1', '.github/workflows/refactor-core-lifecycle.yml'):
            self.assertFalse(P.allowed_change(name), name)
        self.assertTrue(P.allowed_change('recovery/docs/CACHE_BENCHMARK.md'))
        self.assertTrue(P.allowed_change('recovery/scripts/BenchmarkCompilerCache.py'))

    def test_input_symlink_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'real').write_bytes(b'data')
            try:
                (root / 'alias').symlink_to(root / 'real')
            except (OSError, NotImplementedError):
                self.skipTest('Host does not permit symlinks')
            with self.assertRaisesRegex(ValueError, 'symlink'):
                P.inventory(root)

    def test_texture_workload_has_sixteen_unique_assets_and_valid_opaque_tga(self):
        with tempfile.TemporaryDirectory() as temp:
            stage = Path(temp) / 'stage'
            result = P.prepare_texture_workload(stage, Path(temp) / 'final')
            root = stage / 'workloads/texture-16/Data'
            textures = E.parse(root / 'Mod.xml').getroot().findall('{uri:ea.com:eala:asset}Texture')
            self.assertEqual(len(textures), 16)
            self.assertEqual(len({node.attrib['id'] for node in textures}), 16)
            self.assertEqual({node.attrib['File'] for node in textures}, {'probe.tga'})
            self.assertTrue(all(node.attrib['OutputFormat'] == 'A8R8G8B8' and
                                node.attrib['GenerateMipMaps'] == 'false' for node in textures))
            image = (root / 'probe.tga').read_bytes()
            header = struct.unpack('<BBBHHBHHHHBB', image[:18])
            self.assertEqual(header, (0, 0, 2, 0, 0, 0, 0, 0, 256, 256, 32, 0x28))
            self.assertEqual(len(image), 18 + 256 * 256 * 4)
            self.assertEqual(image[18 + (17 * 256 + 29) * 4:18 + (17 * 256 + 29) * 4 + 4],
                             bytes((29, 17, 12, 255)))
            self.assertEqual(set(image[21::4]), {255})
            self.assertEqual(result['assetCount'], 16)
            self.assertTrue(result['synthetic'])
            self.assertEqual(result['files'], P.inventory(root.parent))


if __name__ == '__main__':
    unittest.main()
