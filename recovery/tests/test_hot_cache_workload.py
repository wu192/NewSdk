"""Cheap checks of the real L8 mutation inputs; no compiler execution."""
import copy
import importlib.util
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zlib

SCRIPTS = Path(__file__).resolve().parents[1] / 'scripts'
sys.path.insert(0, str(SCRIPTS))
import HotCacheWorkload as W


class WorkloadChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.work = Path(cls.temporary.name)
        cls.repo = SCRIPTS.parents[1]

        def extracted(repo, work):
            mod = work / 'historical/SDK/Mods/Upheaval'
            for name in ('Data/Mod.xml', 'Data/AptUI/Screen.xml',
                         'Data/AdditionalMaps/MapMetaData_Global.xml'):
                W.save_xml(mod / name, W.declaration())
            return mod

        with patch.object(W, 'extract_upheaval', extracted):
            cls.load = W.prepare(cls.repo, cls.work / 'load', 1)
        cls.mod = Path(cls.load['modRoot'])
        cls.baseline = cls.work / 'baseline'
        shutil.copytree(cls.mod, cls.baseline)
        cls.lookup = {case['id']: case for case in cls.load['cases']}

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        shutil.rmtree(self.mod)
        shutil.copytree(self.baseline, self.mod)
        self.output = self.work / 'output'
        if self.output.exists():
            shutil.rmtree(self.output)
        self.output.mkdir()
        self.cache = self.work / 'cache'
        if self.cache.exists():
            shutil.rmtree(self.cache)
        self.cache.mkdir()

    def apply(self, identifier):
        return W.apply_case(self.load, self.lookup[identifier], self.output, self.cache)

    def test_all_generated_declarations_are_reachable_and_ids_unique(self):
        paths = set()
        def visit(path):
            path = path.resolve()
            if path in paths:
                return
            paths.add(path)
            root = ET.parse(path).getroot()
            for node in root.iter():
                if node.tag == '{%s}Include' % W.NS:
                    source = node.get('source')
                    visit(self.mod / 'Data' / source[5:] if source.startswith('DATA:') else path.parent / source)
                elif node.tag == '{%s}include' % W.XI:
                    visit(path.parent / node.get('href'))
        for _, entry in W.stream_entries(self.mod):
            visit(entry)
        generated = set(path.resolve() for path in (self.mod / 'Data/L8').glob('*.xml'))
        self.assertTrue(generated <= paths)
        ids = [node.get('id') for path in generated for node in ET.parse(path).getroot() if node.get('id')]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(len(ids), len({W.instance_hash(identifier) for identifier in ids}))
        # Generated hierarchy/parent references must follow the renamed ids.
        references = [node.get(key) for path in generated for node in ET.parse(path).getroot().iter()
                      for key in ('inheritFrom', 'Hierarchy') if node.get(key)]
        self.assertTrue(set(references) <= set(ids))
        self.assertEqual(self.load['streamCount'], 3)
        self.assertGreater(self.load['assets'], 8192)
        self.assertEqual(self.load['originalUpheaval']['assets'], 0)

    def test_native_hash_known_collision_and_salted_allocator_retry(self):
        first, second = 'L8_Scale_000_001_00', 'L8_Scale_000_000_12'
        self.assertEqual(W.instance_hash(first), 0x1DA8CF5A)
        self.assertEqual(W.instance_hash(second), 0x1DA8CF5A)
        occupied = {}
        identifier = W.allocate_id(first, occupied)
        occupied_again = dict(occupied)
        retried = W.allocate_id(first, occupied_again)
        self.assertNotEqual(W.instance_hash(identifier), W.instance_hash(retried))
        self.assertEqual(W.allocate_id(first, {}), identifier)

    def test_expand_only_adds_reachable_documents_and_preserves_existing_inputs(self):
        existing = self.mod / 'Data/L8/scale-000-000.xml'
        original = existing.read_bytes(), existing.stat().st_mtime_ns
        workload = copy.deepcopy(self.load)
        result = W.expand(workload, 2)
        self.assertEqual(result['modRoot'], self.load['modRoot'])
        self.assertEqual((existing.read_bytes(), existing.stat().st_mtime_ns), original)
        self.assertEqual(result['assets'], self.load['assets'] + 8192)
        self.assertEqual(result['documents'], self.load['documents'] + 256)
        self.assertNotEqual(result['fingerprint'], self.load['fingerprint'])
        ids = [node.get('id') for path in (self.mod / 'Data/L8').glob('*.xml')
               for node in ET.parse(path).getroot() if node.get('id')]
        self.assertEqual(len(ids), len({W.instance_hash(identifier) for identifier in ids}))

    def test_dense_and_spread_really_edit_equal_asset_counts(self):
        spread = self.apply('C01')
        dense = self.apply('C02')
        self.assertEqual(spread['changedAssets'], dense['changedAssets'])
        self.assertEqual(spread['changedAssets'], 128)
        self.assertEqual(spread['changedDocuments'], 16)
        self.assertEqual(dense['changedDocuments'], 1)
        root = ET.parse(self.mod / 'Data/L8/dense.xml').getroot()
        self.assertEqual(sum(node.get('Default') == '73.5%' for node in root), 128)
        self.assertEqual(len(root), 256)

    def test_parent_change_is_effective_and_sibling_change_keeps_parent(self):
        children = ET.parse(self.mod / 'Data/L8/children-00.xml').getroot()
        self.assertTrue(all(node.get('Default') is None for node in children if node.get('id')))
        row = self.apply('C08')
        self.assertEqual(row['changedAssets'], 64)
        root = ET.parse(self.mod / 'Data/L8/parent-00.xml').getroot()
        self.assertEqual(root[0].get('Default'), '72.5%')
        self.assertTrue(all(node.get('Default') == '73.5%' for node in root[1:]))
        row = self.apply('C07')
        self.assertEqual(row['changedAssets'], 16)
        self.assertGreaterEqual(row['affectedAssets'], 128)

    def test_full_depth_has_four_real_inheritance_edges_and_152_descendants(self):
        roots = []
        graph = {}
        for path in (self.mod / 'Data/L8').glob('depth-*.xml'):
            for node in ET.parse(path).getroot():
                if not node.get('id'):
                    continue
                graph[node.get('id')] = node.get('inheritFrom')
                if node.get('inheritFrom'):
                    self.assertIsNone(node.get('Default'))
                else:
                    roots.append(node.get('id'))
        self.assertEqual(len(roots), 8)
        self.assertEqual(len(graph) - len(roots), 152)
        depths = []
        for identifier in graph:
            depth, parent = 0, graph[identifier]
            while parent:
                depth += 1
                parent = graph[parent]
            depths.append(depth)
        self.assertEqual(max(depths), 4)
        self.assertEqual(depths.count(4), 128)
        result = self.apply('F02-depth')
        self.assertEqual(result['changedAssets'], 8)
        self.assertEqual(result['affectedAssets'], 152)
        self.assertEqual(result['changedDocuments'], 8)

    def test_defines_and_xinclude_are_shared_by_32_real_hosts(self):
        hosts = sorted((self.mod / 'Data/L8').glob('defines-host-*.xml'))
        self.assertEqual(len(hosts), 32)
        for path in hosts:
            root = ET.parse(path).getroot()
            self.assertIsNotNone(root.find('{%s}include' % W.XI))
            self.assertIsNone(root.find('{%s}Includes' % W.NS))
            nodes = root.findall('{%s}ArmorTemplate' % W.NS)
            self.assertEqual(len(nodes), 4)
            self.assertTrue(all('$L8_SCALE' in node.get('DamageScalar') and
                                '$L8_XI_SCALE' in node.get('DamageScalar') for node in nodes))
        result = self.apply('C09')
        self.assertEqual(result['affectedAssets'], 128)
        self.assertEqual(result['changedDocuments'], 3)
        fragment = ET.parse(self.mod / 'Data/L8/fragment.xml').getroot()
        self.assertEqual(fragment.tag, '{%s}Includes' % W.NS)
        self.assertEqual([node.get('source') for node in fragment], ['defines.xml', 'xi-defines-b.xml'])
        self.assertFalse(any(node.tag == '{%s}Define' % W.NS for node in fragment.iter()))
        self.assertEqual(ET.parse(self.mod / 'Data/L8/xi-defines-a.xml').getroot()[0][0].get('value'), '74.25')
        self.assertEqual(ET.parse(self.mod / 'Data/L8/xi-defines-b.xml').getroot()[0][0].get('value'), '75.25')

    def test_material_updates_preserve_valid_headers_and_mixed_load_counts(self):
        texture = self.mod / 'Data/L8/materials/texture-000.dds'
        before = texture.read_bytes()
        self.assertEqual(before[:4], b'DDS ')
        self.assertEqual(struct.unpack('<I', before[4:8])[0], 124)
        self.assertEqual(len(before), 128 + 64 * 64 * 4)
        self.apply('F04-independent')
        self.assertEqual(texture.read_bytes()[:128], before[:128])
        result = self.apply('C12')
        self.assertEqual(result['changedAssets'] + result['affectedAssets'], 256)
        self.assertEqual(result['changedMaterials'], 64)
        self.assertEqual(result['changedAssets'], 192)
        self.assertEqual(result['changedDocuments'], 22)
        self.assertEqual(len(result['streamIdsTouched']), 3)
        self.assertEqual(result['streamIdsTouched'][0], 'base/Mod')
        self.assertEqual(set(result['streamIdsTouched'][1:]), {'apt/Screen', 'maps/MapMetaData_Global'})

    def test_same_stamp_changes_real_content_at_same_length(self):
        target = self.mod / 'Data/L8/spread-00.xml'
        stamp, size, digest = target.stat().st_mtime_ns, target.stat().st_size, W.sha(target)
        result = self.apply('F09-same-stamp')
        self.assertEqual(result['changedAssets'], 128)
        self.assertEqual(target.stat().st_mtime_ns, stamp)
        self.assertEqual(target.stat().st_size, size)
        self.assertNotEqual(W.sha(target), digest)

    def test_search_shadow_only_changes_override_and_provides_modified_roots(self):
        source = self.mod / 'Data/L8Shadow/leaf-00.xml'
        before = source.read_bytes()
        row = self.apply('F09-shadow')
        self.assertEqual(row['changedAssets'], 64)
        self.assertEqual(row['changedDocuments'], 16)
        self.assertEqual(source.read_bytes(), before)
        self.assertEqual(row['extraArgs'][0], '--data')
        first = Path(row['extraArgs'][1].split(';')[0])
        self.assertEqual(ET.parse(first / 'L8Shadow/leaf-00.xml').getroot()[0].get('Default'), '73.5%')

    def test_full_entry_renames_actual_apt_stream_without_relocating_include_roots(self):
        old_stream, entry = next((stream, path) for stream, path in W.stream_entries(self.mod)
                                 if stream.startswith('apt/'))
        before, parent = entry.read_bytes(), entry.parent
        result = self.apply('F09-entry')
        self.assertFalse(entry.exists())
        new_entry = parent / (entry.stem + '_L8Renamed.xml')
        self.assertEqual(new_entry.read_bytes(), before)
        actual_streams = sorted(stream for stream, path in W.stream_entries(self.mod))
        self.assertEqual(result['expectedStreams'], actual_streams)
        self.assertNotIn(old_stream, result['expectedStreams'])
        self.assertEqual(result['changedDocuments'], 1)
        self.assertEqual(result['changedPaths'], sorted([entry.relative_to(self.mod).as_posix(),
                                                        new_entry.relative_to(self.mod).as_posix()]))

    def test_include_target_move_keeps_unique_declarations_and_deepens_graph(self):
        row = self.apply('F06')
        self.assertFalse(list((self.mod / 'Data/L8').glob('include-parent-*.xml')))
        self.assertEqual(len(list((self.mod / 'Data/L8/include-moved').glob('*.xml'))), 32)
        wide = ET.parse(self.mod / 'Data/L8/include-wide.xml').getroot()
        self.assertEqual(sum(node.get('source', '').startswith('include-depth/') for node in W.includes(wide)), 4)
        self.assertGreaterEqual(row['changedDocuments'], 64)

    def test_cache_layers_are_deleted_separately(self):
        for name in ('base/BinaryAssetBuilder.SessionCache.xml.deflate',
                     'apt/BinaryAssetBuilder.SessionCache.xml.deflate',
                     'streams/base/Mod/stream.bin'):
            target = self.cache / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(b'baseline')
        (self.output / 'published').mkdir()
        (self.output / 'published/Mod.big').write_bytes(b'BIG')
        result = self.apply('C10')
        self.assertEqual(len(result['deletedPaths']), 1)
        self.assertTrue((self.cache / 'streams/base/Mod/stream.bin').exists())
        self.assertTrue((self.output / 'published/Mod.big').exists())
        result = self.apply('C11')
        self.assertEqual(result['deletedPaths'], ['published/Mod.big'])
        self.assertTrue((self.cache / 'streams/base/Mod/stream.bin').exists())
        result = self.apply('F07-linked')
        self.assertEqual(result['deletedPaths'], [str((self.cache / 'streams/base/Mod/stream.bin').resolve())])

    def test_partial_session_preserves_other_streams_and_hash_collection(self):
        selected = next(row for row in self.load['streamFixtures'] if row['streamId'].startswith('apt/'))
        source = str((self.mod / selected['path']).resolve())
        entry = str(next(path for identifier, path in W.stream_entries(self.mod)
                         if identifier == selected['streamId']).resolve())
        for compressed in (False, True):
            payload = ('<sc d="created;18;11;1"><fic c="4"><fi><hi d="' + source +
                       ';1;0;default;0"/><ad marker="selected"/></fi>'
                       '<fi><hi d="exclusive;1;0;default;0"/><ad marker="exclusive"><dsf>' + entry + '</dsf></ad></fi>'
                       '<fi><hi d="shared;1;0;default;0"/><ad marker="shared"><dsf>' + entry + ';another-stream</dsf></ad></fi>'
                       '<fi><hi d="other;1;0;default;0"/>'
                       '<ad marker="other"/></fi></fic></sc>').encode()
            path = self.cache / ('BinaryAssetBuilder.SessionCache.xml' + ('.deflate' if compressed else ''))
            if compressed:
                compressor = zlib.compressobj(wbits=-15)
                payload = compressor.compress(payload) + compressor.flush()
            path.write_bytes(payload)
            result = self.apply('F07-session-partial')
            rewritten = path.read_bytes()
            root = ET.fromstring(zlib.decompress(rewritten, -15) if compressed else rewritten)
            self.assertEqual(root.find('fic').get('c'), '4')
            self.assertEqual(len(root.findall('./fic/fi/hi')), 4)
            self.assertEqual({node.get('marker') for node in root.findall('./fic/fi/ad')}, {'shared', 'other'})
            self.assertEqual(result['metadataDocumentsRemoved'], 2)
            self.assertEqual(result['metadataStreamId'], selected['streamId'])
            path.unlink()

    def test_stress_uses_quarter_of_whole_load_across_material_types(self):
        result = self.apply('F10')
        target = self.lookup['F10']['changedAssets']
        self.assertEqual(result['changedAssets'] + result['affectedAssets'], target)
        self.assertGreaterEqual(target / self.load['assets'], .25)
        self.assertLess(target / self.load['assets'], .26)
        extensions = {Path(path).suffix for path in result['changedPaths']}
        self.assertTrue({'.tga', '.wav', '.xml'} <= extensions)


if __name__ == '__main__':
    unittest.main()
