#!/usr/bin/env python3
"""Adapt all existing full XML matrices to EXE-owned types and a remote oracle.

Only binding/transport changes are permitted. Case bodies, comparisons, layouts
and rejection assertions are copied verbatim. Generated sources are temporary.
"""
import argparse
import hashlib
import json
import pathlib
import re

EXCLUDED = {"XmlNodeOracle.cs", "XmlPriorityLinkOracle.cs", "XmlPrioritySdkOracle.cs"}

def adapt(source):
    source, removed = re.subn(r'(?:Assembly|var) (?:assembly|asm)\s*=\s*Assembly.LoadFrom\(args\[0\]\);', '', source)
    source, created = re.subn(
        r'\(IAssetBuilderPlugin\)Activator.CreateInstance\((?:Assembly.LoadFrom\(args\[0\]\)|assembly|asm).GetType\("BinaryAssetBuilder.XmlCompiler.Plugin"\)\)',
        'new XmlOriginalClient()', source)
    if created != 1 or removed not in (0, 1):
        raise ValueError("Expected exactly one original plugin creation")
    source, sizes = re.subn(r'Type rootType=assembly.GetType\("(SageBinaryData.[^"]+)"\);',
                           r'int rootType=XmlOriginalClient.OriginalSize("\1");', source)
    if sizes:
        if sizes != 1 or source.count('rootType!=null') != 1 or source.count('Marshal.SizeOf(rootType)') != 1:
            raise ValueError("Original layout assertion shape changed")
        source = source.replace('rootType!=null', 'rootType>=0').replace('Marshal.SizeOf(rootType)', 'rootType')
    dynamic = 'recovered=Assembly.LoadFrom(args[1]);' in source
    if dynamic:
        source = source.replace('recovered=Assembly.LoadFrom(args[1]);', 'recovered=Assembly.GetEntryAssembly();')
    source = source.replace('IAssetBuilderPlugin', 'XmlOriginalClient')
    if 'Assembly.LoadFrom' in source or 'Activator.CreateInstance' in source:
        raise ValueError("Unadapted assembly binding")
    if source.count('return 0;') not in (1, 2):
        raise ValueError("Expected one successful terminal return")
    at = source.rindex('return 0;')
    source = source[:at] + 'XmlOriginalClient.Finish();' + source[at:]
    return source, dynamic

def prepare(root, output):
    tests = root / 'recovery/tests'
    groups = sorted(p for p in tests.glob('Xml*Oracle.cs') if p.name not in EXCLUDED)
    if len(groups) != 64:
        raise ValueError(f"Expected all 64 XML group oracles, got {len(groups)}")
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for oracle in groups:
        group = oracle.stem.removesuffix('Oracle')
        adapted, dynamic = adapt(oracle.read_text(encoding='utf-8-sig'))
        sources = [oracle] + sorted(tests.glob(group + '*Cases.cs')) + [tests / 'PluginSchemaBoundary.cs']
        folder = output / group
        folder.mkdir()
        written = []
        for path in sources:
            data = adapted if path == oracle else path.read_text(encoding='utf-8-sig').replace('IAssetBuilderPlugin', 'XmlOriginalClient')
            target = folder / path.name
            target.write_text(data, encoding='utf-8')
            written.append(str(target.resolve()))
        # Include every exact source in the receipt so a passing matrix cannot be
        # confused with a future fixture revision or a reduced group selection.
        rows.append(dict(group=group, dynamicAssemblyArgument=dynamic, sources=written,
                         defines=["XMLAI_PERSONALITY_EXTENSION"] if group == "Xmlai" else [],
                         repositoryArgument=group == "Xmlpowers",
                         inputs=[dict(path=p.relative_to(root).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sources]))
    (output / 'manifest.json').write_text(json.dumps(rows, indent=2) + '\n', encoding='utf-8')
    return rows

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2])
    parser.add_argument('--output', type=pathlib.Path, required=True)
    args = parser.parse_args()
    print(f"Prepared {len(prepare(args.root.resolve(), args.output.resolve()))} unchanged complete XML group matrices")
