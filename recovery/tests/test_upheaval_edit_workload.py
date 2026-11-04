"""Cheap edit-format and restoration contracts; no compiler/full-MOD claim."""
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

SCRIPTS = Path(__file__).resolve().parents[1] / 'scripts'
sys.path.insert(0, str(SCRIPTS))
import UpheavalEditWorkload as edits
from HotCacheWorkload import dds


class UpheavalEditWorkloadTests(unittest.TestCase):
    def test_numeric_patch_retains_width_unit_and_positive_literal(self):
        for before in (b'100', b'9', b'12.50', b'25%', b'1.20s', b'5d'):
            after = edits.bump_numeric(before)
            self.assertNotEqual(before, after)
            self.assertEqual(len(before), len(after))
            self.assertGreater(float(after.rstrip(b'sd%')), 0)
            if before[-1:] in (b's', b'd', b'%'):
                self.assertEqual(before[-1:], after[-1:])
        for before in (b'0', b'-1', b'=$SCALE', b'false', b'1e3'):
            self.assertEqual(edits.bump_numeric(before), before)

    def test_xml_patching_tracks_actual_enclosing_instance_and_preserves_other_bytes(self):
        source = (b'<?xml version="1.0"?><AssetDeclaration xmlns="uri:ea.com:eala:asset">'
                  b'<!-- <GameObject id="Fake" BuildTime="10"/> -->'
                  b'<GameObject id="Unit" BuildTime="10"><Body><ActiveBody id="Body" MaxHealth="100"/></Body>'
                  b'<Text><![CDATA[<ActiveBody MaxHealth="100"/>]]></Text></GameObject>'
                  b'<WeaponTemplate id="Weapon" AttackRange="10"/></AssetDeclaration>')
        changed, records = edits.patch_attributes(
            source, lambda tag, name, value: edits.bump_numeric(value)
            if tag['topType'] == 'GameObject' and name in ('BuildTime', 'MaxHealth') else None)
        self.assertEqual(len(records), 2)
        self.assertEqual({(r['topType'], r['topId']) for r in records}, {('GameObject', 'Unit')})
        self.assertEqual({r['node'] for r in records}, {'GameObject', 'ActiveBody'})
        self.assertIn(b'<!-- <GameObject id="Fake" BuildTime="10"/> -->', changed)
        self.assertIn(b'<![CDATA[<ActiveBody MaxHealth="100"/>]]>', changed)
        self.assertIn(b'<WeaponTemplate id="Weapon" AttackRange="10"/>', changed)
        self.assertEqual(len(changed), len(source))
        ET.fromstring(changed)

    def test_renaming_uses_complete_identifiers_and_one_pass(self):
        source = (b'<WeaponTemplate id="BaseWeapon" Parent="WeaponTemplate:BaseWeapon" '
                  b'Other="BaseWeaponVeteran BaseWeapon_Alt BaseWeapon.xml"/>'
                  b'<Ref>BaseWeapon</Ref><Ref>NextWeapon</Ref>')
        changed, counts = edits.rename_tokens(
            source, {'BaseWeapon': 'NextWeapon', 'NextWeapon': 'FinalWeapon'})
        self.assertEqual(counts, {'BaseWeapon': 3, 'NextWeapon': 1})
        self.assertIn(b'id="NextWeapon" Parent="WeaponTemplate:NextWeapon"', changed)
        self.assertIn(b'<Ref>NextWeapon</Ref><Ref>FinalWeapon</Ref>', changed)
        self.assertIn(b'BaseWeaponVeteran BaseWeapon_Alt BaseWeapon.xml', changed)

    def test_renaming_matches_engine_casefolded_identifiers_and_rejects_ambiguous_sources(self):
        changed, counts = edits.rename_tokens(
            b'baseweapon BASEWEAPON BaseWeapon_Alt', {'BaseWeapon': 'RenamedWeapon'})
        self.assertEqual(changed, b'RenamedWeapon RenamedWeapon BaseWeapon_Alt')
        self.assertEqual(counts, {'BaseWeapon': 2})
        with self.assertRaises(ValueError):
            edits.rename_tokens(b'BaseWeapon', {'BaseWeapon': 'One', 'baseweapon': 'Two'})

    def test_weapon_reference_selection_ignores_comments_and_declaration_ids(self):
        with tempfile.TemporaryDirectory() as temp:
            mod = Path(temp)
            data = mod / 'Data'
            data.mkdir()
            (data / 'Mod.xml').write_bytes(
                b'<AssetDeclaration><Includes><Include type="all" source="Weapons.xml"/>'
                b'</Includes></AssetDeclaration>')
            (data / 'Weapons.xml').write_bytes(
                b'<AssetDeclaration><!-- Alpha is only mentioned in a comment. -->'
                b'<WeaponTemplate id="Alpha"/><WeaponTemplate id="Beta"/>'
                b'<ProjectileNugget WarheadTemplate="bEtA" Other="Beta_Upgraded"/>'
                b'<WeaponName>BETA</WeaponName></AssetDeclaration>')
            references = edits._weapon_references(mod, ['Alpha', 'Beta'])
            self.assertEqual(references['Alpha'], [])
            self.assertEqual(len(references['Beta']), 2)
            self.assertEqual({r['field'] for r in references['Beta']}, {'WarheadTemplate', 'text'})

    def test_wave_chunk_alignment_and_audio_only_edit(self):
        fmt = struct.pack('<HHIIHH', 1, 1, 22050, 44100, 2, 16)
        pcm = struct.pack('<8h', 0, 1, -1, 32000, -32000, 1200, -1200, 0)
        chunks = (b'fmt ' + struct.pack('<I', len(fmt)) + fmt +
                  b'JUNK' + struct.pack('<I', 3) + b'abc\0' +
                  b'data' + struct.pack('<I', len(pcm)) + pcm +
                  b'LIST' + struct.pack('<I', 4) + b'tail')
        source = b'RIFF' + struct.pack('<I', len(chunks) + 4) + b'WAVE' + chunks
        offset = next(offset for kind, offset, _ in edits.riff_chunks(source) if kind == b'data')
        changed, count = edits.patch_wave(source)
        self.assertEqual(count, 8)
        self.assertEqual(len(changed), len(source))
        self.assertEqual(changed[:offset], source[:offset])
        self.assertNotEqual(changed[offset:offset + len(pcm)], pcm)
        self.assertEqual(changed[offset + len(pcm):], source[offset + len(pcm):])
        self.assertEqual(list(edits.riff_chunks(changed)), list(edits.riff_chunks(source)))
        invalid = bytearray(source)
        struct.pack_into('<H', invalid, 20, 3)
        with self.assertRaises(ValueError):
            edits.patch_wave(bytes(invalid))

    def test_dds_preserves_header_and_unmodified_payload_tail(self):
        source = dds()
        changed, count = edits.patch_dds(source)
        self.assertEqual(changed[:128], source[:128])
        self.assertEqual(len(changed), len(source))
        self.assertGreater(count, 0)
        self.assertNotEqual(changed[128:128 + count], source[128:128 + count])
        self.assertEqual(changed[128 + count:], source[128 + count:])
        invalid = bytearray(source)
        invalid[84:88] = b'DX10'
        with self.assertRaises(ValueError):
            edits.patch_dds(bytes(invalid))

    def test_native_pcm8_wave_edits_unsigned_samples_without_touching_header_or_tail(self):
        fmt = struct.pack('<HHIIHH', 1, 1, 22050, 22050, 1, 8)
        pcm = bytes((0, 32, 127, 128, 200, 255))
        chunks = (b'fmt ' + struct.pack('<I', len(fmt)) + fmt +
                  b'data' + struct.pack('<I', len(pcm)) + pcm +
                  b'LIST' + struct.pack('<I', 4) + b'tail')
        source = b'RIFF' + struct.pack('<I', len(chunks) + 4) + b'WAVE' + chunks
        offset = next(offset for kind, offset, _ in edits.riff_chunks(source) if kind == b'data')
        changed, count = edits.patch_wave(source)
        self.assertEqual(count, len(pcm))
        self.assertEqual(changed[:offset], source[:offset])
        self.assertEqual(changed[offset + len(pcm):], source[offset + len(pcm):])
        self.assertEqual(changed[offset:offset + len(pcm)], bytes((0, 0, 95, 160, 232, 255)))

    def test_edit_and_restore_preserve_input_bytes_and_mtime(self):
        with tempfile.TemporaryDirectory() as temp:
            mod = Path(temp) / 'mod'
            path = mod / 'Data/GameObject.xml'
            path.parent.mkdir(parents=True)
            original = (b'<AssetDeclaration xmlns="uri:ea.com:eala:asset">'
                        b'<GameObject id="Unit" BuildTime="10"><Body>'
                        b'<ActiveBody id="Body" MaxHealth="100"/></Body>'
                        b'</GameObject></AssetDeclaration>')
            path.write_bytes(original)
            stamp = path.stat().st_mtime_ns
            (mod.parent / edits.SETUP_FILE).write_text(json.dumps({'scenario': 'gameobject-balance'}))
            mutation = edits.mutate(mod, 'gameobject-balance')
            self.assertEqual(mutation['totals']['files'], 1)
            self.assertEqual(mutation['totals']['assetDeclarationsWithFieldEdits'], 1)
            self.assertEqual(mutation['totals']['fieldEdits'], 2)
            self.assertNotEqual(path.read_bytes(), original)
            self.assertEqual(path.stat().st_mtime_ns, stamp)
            self.assertTrue(mutation['changes'][0]['sameLength'])
            self.assertTrue(mutation['changes'][0]['sameMtime'])
            edits.restore(mod, mutation)
            self.assertEqual(path.read_bytes(), original)
            self.assertEqual(path.stat().st_mtime_ns, stamp)


if __name__ == '__main__':
    unittest.main()
