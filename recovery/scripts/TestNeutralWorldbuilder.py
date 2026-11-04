#!/usr/bin/env python3
"""Real small MOD and original neutral/WorldBuilder postprocess acceptance.

The fixture overrides AM_Box01 and strongly references the genuine WB-only AM_Bench.
No synthetic payload or hash mutation is used. Only private SDK/runtime copies
are modified. This is a focused packaging/configuration gate, not a full MOD
or in-game compatibility claim.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile
import traceback
import xml.etree.ElementTree as ET

from ValidateCompilerBig import validate
from ImportWorldbuilderReferences import unpack

ROOT = Path(__file__).resolve().parents[2]
EXTENSIONS = ('.manifest', '.bin', '.imp', '.relo')
RUNTIME = ('NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll')
TOOLS = ('HashFix.exe', 'ModAssetResolver.exe', 'LoDStreamBuilder.exe', 'MakeBig.exe',
         'ModAssetResolver_TypeHashes.txt', 'ModAssetResolver_InstanceHashes.txt')


def need(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def describe(path, name=None):
    row = {'source': str(path), 'bytes': path.stat().st_size, 'sha256': sha(path)}
    if name is not None:
        row['name'] = name
    return row


def snapshot(directory):
    return {p.relative_to(directory).as_posix(): {
        'bytes': p.stat().st_size, 'mtimeNs': p.stat().st_mtime_ns, 'sha256': sha(p)}
        for p in sorted(directory.rglob('*')) if p.is_file()}


def manifest(path):
    data = path.read_bytes()
    need(data[:4] == b'\0\1\6\0', 'Expected linked RA3 v6 reference stream: ' + str(path))
    count = struct.unpack_from('<I', data, 12)[0]
    sizes = struct.unpack_from('<4I', data, 32)
    need(len(data) == 48 + 48 * count + sum(sizes), 'Manifest table lengths differ')
    return data, [struct.unpack_from('<12I', data, 48 + 48 * i) for i in range(count)]


def config(path, value):
    tree = ET.parse(path)
    settings = tree.getroot().find('appSettings')
    if settings is None:
        settings = ET.SubElement(tree.getroot(), 'appSettings')
    for node in list(settings):
        if node.get('key') == 'fix-neutral-assets':
            settings.remove(node)
    ET.SubElement(settings, 'add', key='fix-neutral-assets', value=value)
    tree.write(path, encoding='utf-8', xml_declaration=True)


def run(command, cwd, out, expected=0):
    out.mkdir(parents=True, exist_ok=True)
    process = subprocess.run(list(map(str, command)), cwd=cwd, capture_output=True, timeout=180)
    (out / 'stdout.log').write_bytes(process.stdout)
    (out / 'stderr.log').write_bytes(process.stderr)
    (out / 'request.json').write_text(json.dumps({'command': list(map(str, command)),
        'cwd': str(cwd), 'expectedExit': expected, 'actualExit': process.returncode}, indent=2) + '\n')
    need(process.returncode == expected, 'Process failed: ' + str(out) + '; exit=' + str(process.returncode))
    return process


def original_stage(exe, args, tools, out, marker=None):
    process = run([exe, *args], tools, out)
    need(b'Error:' not in process.stdout and b'Error:' not in process.stderr,
         'Original tool reported an error: ' + exe.name)
    if marker:
        need(marker in process.stdout, 'Original tool did not finish actual work: ' + exe.name)
    return process.stdout


def neutral_world(sdk):
    root = sdk / 'builtmods'
    if any((root / ('Worldbuilder' + ext)).exists() for ext in EXTENSIONS):
        return root / 'Worldbuilder.manifest'
    return root / 'sagexml/Worldbuilder.manifest'


def oracle(source, sdk, out):
    """Run unmodified SDK tools against the exact source/reference bytes."""
    tools = out / 'SDK/tools'
    tools.mkdir(parents=True)
    built = out / 'SDK/BuiltMods'
    (built / 'SageXml').mkdir(parents=True)
    for name in TOOLS:
        shutil.copyfile(ROOT / 'SDK/tools' / name, tools / name)
    (tools / 'MakeBig.exe.config').write_text(
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true">'
        '<supportedRuntime version="v4.0"/></startup></configuration>')
    for name in ('Static', 'Global', 'Audio'):
        shutil.copyfile(sdk / 'builtmods' / (name + '.manifest'), built / (name + '.manifest'))
    world = neutral_world(sdk)
    for ext in EXTENSIONS:
        source_file = world.with_suffix(ext)
        if ext == '.bin' and not source_file.is_file():
            unpack(source_file.with_name(source_file.name + '.parts'), built / 'Worldbuilder.bin')
        else:
            shutil.copyfile(source_file, built / ('Worldbuilder' + ext))
    shutil.copyfile(sdk / 'builtmods/sagexml/Worldbuilder.manifest', built / 'SageXml/Worldbuilder.manifest')
    hashes = sdk / 'builtmods/StringHashes.xml'
    if hashes.exists():
        shutil.copyfile(hashes, built / 'StringHashes.xml')
    else:
        (built / 'StringHashes.xml').write_text('<AssetDeclaration/>')
    inputs = snapshot(built)
    stream = out / 'inputs/data'
    stream.mkdir(parents=True)
    target = stream / 'mod.manifest'
    for ext in EXTENSIONS:
        shutil.copyfile(source.with_suffix(ext), target.with_suffix(ext))
    cdata = source.with_suffix('') / 'cdata'
    if cdata.exists():
        shutil.copytree(cdata, stream / 'mod/cdata')
    before_count = len(manifest(target)[1])
    text = original_stage(tools / 'HashFix.exe', [target], tools, out / 'hashfix', b'Instance hashes fixed:')
    changed = re.search(rb'Instance hashes fixed:\s*(\d+)', text)
    need(changed and int(changed.group(1)) > 0, 'Fixture did not change a genuine official WB hash')
    for ext in EXTENSIONS:
        stage = out / 'after-hashfix'
        stage.mkdir(exist_ok=True)
        shutil.copyfile(target.with_suffix(ext), stage / ('mod' + ext))
    text = original_stage(tools / 'ModAssetResolver.exe', [target], tools, out / 'resolver', b'Assets prepended:')
    inserted = re.search(rb'Assets prepended:\s*(\d+)', text)
    need(inserted and int(inserted.group(1)) >= 2,
         'Fixture did not exercise genuine recursive WB insertion')
    need(len(manifest(target)[1]) == before_count + int(inserted.group(1)), 'Resolver count differs')
    for ext in EXTENSIONS:
        stage = out / 'after-resolver'
        stage.mkdir(exist_ok=True)
        shutil.copyfile(target.with_suffix(ext), stage / ('mod' + ext))
    original_stage(tools / 'LoDStreamBuilder.exe', [target], tools, out / 'lod')
    expected_names = {'mod' + suffix + ext for suffix in ('', '_l', '_m') for ext in EXTENSIONS}
    need({p.name for p in stream.iterdir() if p.is_file()} == expected_names,
         'Original LoD exact twelve-file inventory differs')
    expected = [describe(p, 'data\\' + p.relative_to(stream).as_posix().replace('/', '\\').lower())
                for p in sorted(stream.rglob('*')) if p.is_file()]
    (out / 'expected.json').write_text(json.dumps(expected, indent=2) + '\n')
    original_stage(tools / 'MakeBig.exe', ['-f', stream.parent, '-x:*.asset', '-o:' + str(out / 'original.big')],
                   tools, out / 'makebig')
    need(snapshot(built) == inputs, 'Original tools changed the reference files')
    # The 1.3 GiB original bin is recoverable from the committed parts and is
    # not duplicated into the Actions evidence archive.
    (built / 'Worldbuilder.bin').unlink()
    return {'hashFixChangedCount': int(changed.group(1)), 'resolverInsertedAssets': int(inserted.group(1)),
            'originalTools': {name: sha(tools / name) for name in TOOLS}, 'expected': expected,
            'originalMakeBig': validate(out / 'original.big', expected)}


def preflight():
    from lxml import etree
    import runpy
    schema = runpy.run_path(str(ROOT / 'recovery/scripts/GenerateXmlSdkModA.py'),
                           run_name='neutral_schema')['aggregate_schema']()
    schema.assertValid(etree.parse(str(ROOT / 'recovery/fixtures/neutral-worldbuilder/Data/Mod.xml')))
    preset = json.loads((ROOT / 'recovery/fixtures/neutral-worldbuilder/newmodcompiler.json').read_text())
    need(preset['fix-neutral-assets'] is True, 'Fixture preset does not enable neutral repair')
    sdk = ROOT / 'SDK'
    world = neutral_world(sdk)
    data, rows = manifest(world)
    names_start = 48 + 48 * len(rows) + sum(struct.unpack_from('<2I', data, 32))
    names = {data[names_start + r[6]:].split(b'\0', 1)[0].decode('utf-8') for r in rows}
    need({'GameObject:AM_Box01', 'GameObject:AM_Bench'} <= names, 'Official WB fixture assets missing')
    for ext in EXTENSIONS:
        path = world.with_suffix(ext)
        need(path.is_file() or ext == '.bin' and (path.with_name(path.name + '.parts') / 'index.json').is_file(),
             'Official WB quartet missing: ' + ext)
    print(json.dumps({'preflightSucceeded': True, 'officialWorldbuilder': str(world), 'referenceAssets': len(rows)}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--product-build', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--configuration', choices=('Debug', 'Release'), default='Release')
    parser.add_argument('--preflight', action='store_true')
    args = parser.parse_args()
    preflight()
    if args.preflight:
        return
    need(args.product_build and args.output, '--product-build and --output are required')
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    product = args.product_build.resolve()
    receipt = json.loads((product / 'build.json').read_text(encoding='utf-8-sig'))
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    need(receipt['sourceCommit'] == commit and receipt['configuration'] == args.configuration, 'Product source/configuration differs')
    need(sha(product / 'bin/NewModCompiler.exe') == receipt['exeSha256'], 'Product EXE differs from receipt')
    state = {'schemaVersion': 1, 'succeeded': False, 'sourceCommit': commit,
             'runId': os.environ.get('GITHUB_RUN_ID'), 'runAttempt': os.environ.get('GITHUB_RUN_ATTEMPT'),
             'binarySha256': receipt['exeSha256'], 'cases': [],
             'configuration': args.configuration,
             'scope': 'Current production; real two-asset neutral MOD; official WB references; configuration precedence, failure/retry, original HashFix/recursive Resolver/LoD/MakeBig raw differential. No full MOD or in-game acceptance.'}
    short_root = Path(os.environ.get('RUNNER_TEMP', tempfile.gettempdir())).resolve().anchor
    work = Path(tempfile.mkdtemp(prefix='nwb-', dir=short_root))
    try:
        sdk, mod, runtime = work / 'sdk', work / 'NeutralWbProbe', work / 'runtime'
        sdk.mkdir(); runtime.mkdir()
        for source in (ROOT / 'SDK').iterdir():
            if source.name.lower() in ('mods', 'supercompilercache'):
                continue
            if source.is_dir():
                shutil.copytree(source, sdk / source.name)
            else:
                shutil.copy2(source, sdk / source.name)
        shutil.copytree(ROOT / 'recovery/fixtures/neutral-worldbuilder', mod)
        for name in RUNTIME:
            shutil.copy2(product / 'bin' / name, runtime / name)
        state['runtimeFiles'] = {name: sha(runtime / name) for name in RUNTIME}
        original_references = snapshot(sdk / 'builtmods')
        original_source = snapshot(mod / 'Data')
        executable = runtime / 'NewModCompiler.exe'

        def cli(name, project_value, global_value='false', extra=(), expected=0, source=None, enabled=None, destination=None):
            config(runtime / 'NewModCompiler.exe.config', global_value)
            preset = {'package-content': 'base', 'package-format': 'big', 'include-additional': False}
            if project_value is not None:
                preset['fix-neutral-assets'] = project_value
            (mod / 'newmodcompiler.json').write_text(json.dumps(preset, indent=2) + '\n')
            case = out / name
            destination = destination or work / name
            command = [executable, 'build', '--mod', mod, '--sdk-root', sdk,
                       '--out', destination]
            if '--package-format' not in extra or extra[extra.index('--package-format') + 1] != 'loose':
                command += ['--mod-version', '1.0']
            command += list(extra)
            process = run(command, runtime, case, expected)
            report = json.loads((destination / 'build-report.json').read_text(encoding='utf-8-sig'))
            need(report['exitCode'] == expected, 'Build report exit code differs: ' + name)
            need(not report.get('externalTools'), 'Production invoked external tools')
            if source is not None:
                option = report['effectiveOptions']['fix-neutral-assets']
                need(option['Source'] == source and option['Value'] == str(enabled).lower(),
                     'Effective neutral option/source differs: ' + name + ': ' + str(option))
            archived = case / 'artifacts'
            archived.mkdir()
            files = []
            for artifact in report.get('artifacts', []):
                path = Path(artifact['Path'])
                relative = path.relative_to(destination / 'published')
                target = archived / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
                files.append(target)
            shutil.copyfile(destination / 'build-report.json', case / 'build-report.json')
            if (destination / 'build.events.jsonl').exists():
                shutil.copyfile(destination / 'build.events.jsonl', case / 'build.events.jsonl')
            need(snapshot(mod / 'Data') == original_source, 'Production changed MOD source')
            row = {'name': name, 'passed': True, 'exitCode': expected, 'arguments': list(map(str, command[1:])),
                   'globalValue': global_value, 'projectValue': project_value,
                   'fixNeutralOption': report['effectiveOptions']['fix-neutral-assets'],
                   'cache': report.get('cache'),
                   'artifacts': [describe(p, p.relative_to(archived).as_posix()) for p in files]}
            state['cases'].append(row)
            return files, process, report

        loose, _, _ = cli('cli-false-loose', True, extra=['--fix-neutral-assets=false', '--package-format', 'loose'],
                          source='commandLine', enabled=False)
        source = next(p for p in loose if p.suffix.lower() == '.manifest')
        original = oracle(source, sdk, out / 'original-chain')
        state['originalChain'] = {k: v for k, v in original.items() if k != 'expected'}

        enabled_files, _, _ = cli('project-true', True, source='project', enabled=True)
        enabled_big = next(p for p in enabled_files if p.suffix.lower() == '.big')
        comparison = validate(enabled_big, original['expected'])
        state['productBig'] = comparison
        need(next(p for p in enabled_files if p.suffix.lower() == '.skudef').read_bytes() ==
             b'mod-game 1.12\r\nadd-big NeutralWbProbe_1.0.big\r\n', 'SKUDEF raw bytes differ')

        disabled_files, _, _ = cli('project-false-over-global-true', False, 'true', source='project', enabled=False)
        disabled_big = next(p for p in disabled_files if p.suffix.lower() == '.big')
        need(disabled_big.read_bytes() != enabled_big.read_bytes(), 'Neutral enabled/disabled output did not differ')
        for name, project_value, global_value, extra, expected_source, enabled in (
            ('cli-true-over-project-false', False, 'false', ['--fix-neutral-assets=true'], 'commandLine', True),
            ('cli-false-over-project-global-true', True, 'true', ['--fix-neutral-assets=false'], 'commandLine', False),
            ('global-true', None, 'true', [], 'applicationConfig', True)):
            files, _, _ = cli(name, project_value, global_value, extra, source=expected_source, enabled=enabled)
            actual = next(p for p in files if p.suffix.lower() == '.big')
            expected_big = enabled_big if enabled else disabled_big
            need(actual.read_bytes() == expected_big.read_bytes(), 'Configuration precedence changed raw output: ' + name)

        # Manifest is mandatory in both raw-bin and packed-bin layouts.
        missing = neutral_world(sdk).with_suffix('.imp')
        held = missing.with_name(missing.name + '.held')
        missing.rename(held)
        retry_destination = work / 'retry'
        try:
            _, process, report = cli('missing-reference', True, expected=3, destination=retry_destination)
            need('PACKAGE_NEUTRAL_REFERENCE_MISSING' in json.dumps(report), 'Missing reference diagnostic differs')
            need(not report.get('artifacts'), 'Missing-reference build published artifacts')
        finally:
            held.rename(missing)
        files, _, _ = cli('retry-reference-restored', True, destination=retry_destination)
        need(next(p for p in files if p.suffix.lower() == '.big').read_bytes() == enabled_big.read_bytes(),
             'Restored-reference retry output differs')
        need(snapshot(sdk / 'builtmods') == original_references, 'Production changed official reference bytes/mtime')
        need(sha(runtime / 'NewModCompiler.exe') == state['runtimeFiles']['NewModCompiler.exe'] and
             sha(runtime / 'antlr.runtime.dll') == state['runtimeFiles']['antlr.runtime.dll'], 'Runtime binaries changed')
        state['sourceAndReferencesUnchanged'] = True
        state['exactOriginalChainRawMembers'] = True
        state['succeeded'] = True
    except Exception as error:
        state['error'] = str(error)
        (out / 'error.log').write_text(traceback.format_exc(), encoding='utf-8')
        raise
    finally:
        (out / 'validation.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')
        (out / 'original-chain/SDK/BuiltMods/Worldbuilder.bin').unlink(missing_ok=True)
        shutil.rmtree(work)
    print(json.dumps({'succeeded': state['succeeded'], 'cases': len(state['cases']),
                      'hashFixChangedCount': state['originalChain']['hashFixChangedCount'],
                      'resolverInsertedAssets': state['originalChain']['resolverInsertedAssets']}))


if __name__ == '__main__':
    main()
