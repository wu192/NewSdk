#!/usr/bin/env python3
"""Check the exact original XmlCompiler registrations and formal route manifests.

This is a static consistency check, never a replacement for component matrices or
SDK acceptance. Native helper structs and original unknown-polymorphic branches
are not counted as registered root entries.
"""
import argparse
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PLUGIN = ROOT / 'recovery/decompiled/BinaryAssetBuilder.XmlCompiler/BinaryAssetBuilder.XmlCompiler/Plugin.cs'

def audit():
    source = PLUGIN.read_text(encoding='utf-8')
    original = {}
    pattern = (r'case (\d+)u?:\s*extendedTypeInformation\.TypeName = new string\(global::[^\n]*?'
               r'TypeName_0040(\w+)_0040SageBinaryData_0040[^\n]*\);\s*'
               r'extendedTypeInformation\.ProcessingHash = [^;]*;\s*'
               r'extendedTypeInformation\.TypeHash = (\d+)u?;')
    for type_id, name, type_hash in re.findall(pattern, source):
        assert name not in original, f'duplicate original registration: {name}'
        original[name] = {'typeId': int(type_id), 'typeHash': int(type_hash)}
    assert len(original) == 301, f'original registration count changed: {len(original)}'
    schema = ET.parse(ROOT / 'SDK/Schemas/xsd/CnC3Types.xsd')
    global_names = {e.get('name') for e in schema.iter('{http://www.w3.org/2001/XMLSchema}element')}
    catalogs = {}
    memberships = {}
    for path in sorted((ROOT / 'recovery/catalog').glob('Xml*Types.json')):
        doc = json.loads(path.read_text(encoding='utf-8-sig'))
        for row in doc.get('roots', []):
            name = row['name']
            assert name in original, f'{path.name}: unregistered root {name}'
            assert row['typeId'] == original[name]['typeId'], f'{name}: TypeId drift'
            assert row['typeHash'] == original[name]['typeHash'], f'{name}: TypeHash drift'
            if 'schemaGlobal' in row:
                assert row['schemaGlobal'] == (name in global_names), f'{name}: schemaGlobal drift'
            if name in catalogs:
                assert catalogs[name]['typeId'] == row['typeId'], f'{name}: conflicting catalogs'
            catalogs[name] = row
            memberships.setdefault(name, []).append(path.name)
    csharp = (ROOT / 'recovery/tests/XmlPriorityLinkOracle.cs').read_text(encoding='utf-8')
    ps = (ROOT / 'recovery/scripts/TestXmlPriorityLink.ps1').read_text(encoding='utf-8')
    roots = re.findall(r'new Root\("([^"]+)", (\d+)u, (true|false)\)', csharp)
    manifest = {name: int(tid) for name, tid, _ in roots}
    assert len(manifest) == len(roots), 'duplicate C# formal root routes'
    ps_rows = re.findall(r'^\s+(\w+) = \[uint32\](\d+)$', ps, flags=re.M)
    ps_manifest = {name: int(tid) for name, tid in ps_rows}
    assert len(ps_manifest) == len(ps_rows), 'duplicate PowerShell manifest names'
    assert manifest == ps_manifest, 'formal C#/PowerShell exact route maps differ'
    expected = int(re.search(r'expectedTypes = (\d+)', ps)[1])
    assert len(manifest) == expected, 'fixed expected route count differs'
    for name, tid in manifest.items():
        assert name in catalogs and tid == original[name]['typeId'], f'{name}: formal route not catalogued/original'
    baseline = json.loads((ROOT / 'recovery/catalog/XmlRecoveredTypes.json').read_text(encoding='utf-8-sig'))
    baseline_names = {r['name'] for r in baseline['roots']}
    assert len(baseline_names) == 82, 'historical SDK catalog must remain 82 entries'
    component = json.loads((ROOT / 'recovery/catalog/XmlComponentOnlyTypes.json').read_text(encoding='utf-8-sig'))
    component_names = {r['name'] for r in component['roots']}
    assert len(component_names) == len(component['roots']), 'duplicate component-only roots'
    assert not (baseline_names & component_names), 'SDK baseline/component-only catalogs overlap'
    joypad = json.loads((ROOT / 'recovery/catalog/XmljoypadTypes.json').read_text(encoding='utf-8-sig'))
    joypad_names = {r['name'] for r in joypad['roots']}
    assert len(joypad_names) == 11 and all('Joypad' in name for name in joypad_names), 'Joypad scope drift'
    assert not joypad_names.intersection(baseline_names | component_names), 'Joypad routes overlap historical snapshots'
    assert baseline_names | component_names | joypad_names == set(manifest), 'formal routes differ from historical catalogs plus Joypad'
    assert all(r['sdkValidated'] is False for r in component['roots']), 'component-only root incorrectly marked SDK accepted'
    assert component['snapshotPhase'] == 'component-acceptance-before-current-sdk', 'historical partition phase changed'
    assert component['combinedDllSdkValidatedAtSnapshot'] is False, 'historical component snapshot changed'
    assert component['realSdkCompilationInSnapshotRound'] is False, 'historical component snapshot changed'
    rows = []
    for name, values in original.items():
        rows.append(dict(name=name, **values, formalComponentRoute=name in manifest,
                         schemaGlobal=name in global_names, catalogs=memberships.get(name, []),
                         excludedRoot=(name == 'TestGameObject')))
    return dict(scope='Static registration/route/catalog audit only; not component or SDK validation',
                originalPluginBlob='00bb9e5df72d07a21204f85f9a9a2f1d9557f00b',
                registeredEntries=len(original), formalComponentRoutes=len(manifest),
                componentOnlyEntries=len(component_names), historicalSdkBaselineEntries=len(baseline_names),
                sdkAcceptanceEvaluated=False, currentSdkAcceptanceEvidence=component['currentSdkAcceptanceEvidence'],
                historicalComponentSnapshotSdkValidated=False,
                unresolved=[r['name'] for r in rows if not r['formalComponentRoute'] and not r['excludedRoot']],
                entries=rows)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='Optional full static audit JSON output')
    args = parser.parse_args()
    report = audit()
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'entries'}, ensure_ascii=False))
