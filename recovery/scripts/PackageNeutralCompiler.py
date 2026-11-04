#!/usr/bin/env python3
"""Archive the current three-file products only after focused acceptance."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ('NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll')


def need(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--product-root', required=True, type=Path)
    parser.add_argument('--validation-root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    states = [json.loads((args.validation_root / mode / 'validation.json').read_text(encoding='utf-8-sig'))
              for mode in ('Debug', 'Release')]
    need(all(s['succeeded'] and s['exactOriginalChainRawMembers'] and s['sourceAndReferencesUnchanged']
             and s['sourceCommit'] == commit for s in states), 'Small MOD acceptance is incomplete or from another source')
    for kind in ('neutral-core', 'neutral-cli'):
        state = json.loads((args.validation_root.parent / kind / 'validation.json').read_text(encoding='utf-8-sig'))
        need(state['succeeded'] and state['sourceCommit'] == commit, kind + ' acceptance incomplete or from another source')
    manifest = {'schemaVersion': 1, 'sourceCommit': commit,
                'runId': os.environ.get('GITHUB_RUN_ID'), 'runAttempt': os.environ.get('GITHUB_RUN_ATTEMPT'),
                'scope': 'Debug/Release current products; App/Core/CLI and official neutral small MOD original postprocess/BIG raw differential. SDK assets are separate; no in-game/full MOD acceptance.',
                'packages': []}
    for mode, state in zip(('Debug', 'Release'), states):
        product = args.product_root / mode
        receipt = json.loads((product / 'build.json').read_text(encoding='utf-8-sig'))
        need(receipt['sourceCommit'] == commit and receipt['configuration'] == mode and
             receipt['exeSha256'] == state['binarySha256'], 'Product and acceptance EXE differ: ' + mode)
        bin_path = product / 'bin'
        need({p.name for p in bin_path.iterdir() if p.is_file()} == set(RUNTIME), 'Runtime is not the exact three-file layout')
        files = {name: (bin_path / name).read_bytes() for name in RUNTIME}
        need(sha(files['NewModCompiler.exe']) == receipt['exeSha256'], 'Runtime EXE changed after acceptance')
        archive = out / ('NewModCompiler-Win32-' + mode + '.zip')
        with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as target:
            for name, data in files.items():
                target.writestr(name, data)
        with zipfile.ZipFile(archive) as source:
            need(source.namelist() == list(RUNTIME), 'ZIP runtime member set/order differs')
            for name, data in files.items():
                need(source.read(name) == data, 'ZIP runtime bytes differ: ' + name)
        # Execute only files freshly extracted from the final ZIP. Compile a
        # cold small MOD against a separate SDK/cache and compare the actual
        # BIG/SKUDEF bytes with the product accepted above.
        short_root = Path(os.environ.get('RUNNER_TEMP', tempfile.gettempdir())).resolve().anchor
        work = Path(tempfile.mkdtemp(prefix='nwz-', dir=short_root))
        deployment = out / ('deployment-' + mode)
        deployment.mkdir()
        try:
            runtime, sdk, mod, destination = work / 'runtime', work / 'sdk', work / 'NeutralWbProbe', work / 'out'
            with zipfile.ZipFile(archive) as source:
                source.extractall(runtime)
            sdk.mkdir()
            for source in (ROOT / 'SDK').iterdir():
                if source.name.lower() in ('mods', 'supercompilercache'):
                    continue
                if source.is_dir():
                    shutil.copytree(source, sdk / source.name)
                else:
                    shutil.copy2(source, sdk / source.name)
            shutil.copytree(ROOT / 'recovery/fixtures/neutral-worldbuilder', mod)
            arguments = [str(runtime / 'NewModCompiler.exe'), 'build', '--mod', str(mod), '--sdk-root', str(sdk),
                         '--out', str(destination), '--mod-version', '1.0']
            process = subprocess.run(arguments, cwd=runtime, capture_output=True, timeout=180)
            (deployment / 'stdout.log').write_bytes(process.stdout)
            (deployment / 'stderr.log').write_bytes(process.stderr)
            need(process.returncode == 0, 'Extracted ZIP small MOD build failed: ' + mode)
            report = json.loads((destination / 'build-report.json').read_text(encoding='utf-8-sig'))
            need(report['exitCode'] == 0 and not report['externalTools'] and
                 report['effectiveOptions']['fix-neutral-assets'] == {'Value': 'true', 'Source': 'project'},
                 'Extracted ZIP did not use the intended in-process neutral configuration')
            shutil.copyfile(destination / 'build-report.json', deployment / 'build-report.json')
            expected_dir = args.validation_root / mode / 'project-true/artifacts'
            deployment_files = []
            for artifact in report['artifacts']:
                path = Path(artifact['Path'])
                expected_file = expected_dir / path.name
                need(path.read_bytes() == expected_file.read_bytes(), 'Extracted ZIP raw package bytes differ: ' + path.name)
                deployment_files.append({'name': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path.read_bytes())})
            need(len(deployment_files) == 2, 'Extracted ZIP did not publish exactly BIG and SKUDEF')
            deployment_receipt = {'succeeded': True, 'sourceCommit': commit, 'configuration': mode,
                'zipSha256': sha(archive.read_bytes()), 'coldSmallMod': True, 'exactAcceptedRawOutputs': True,
                'arguments': arguments[1:], 'runtimeFiles': [
                    {'name': name, 'sha256': sha((runtime / name).read_bytes())} for name in RUNTIME],
                'artifacts': deployment_files}
            (deployment / 'validation.json').write_text(json.dumps(deployment_receipt, indent=2) + '\n')
        finally:
            shutil.rmtree(work)
        manifest['packages'].append({'configuration': mode, 'name': archive.name,
            'bytes': archive.stat().st_size, 'sha256': sha(archive.read_bytes()),
            'runtimeFiles': [{'name': name, 'bytes': len(data), 'sha256': sha(data)} for name, data in files.items()],
            'neutralAcceptance': {'hashFixChangedCount': state['originalChain']['hashFixChangedCount'],
                'resolverInsertedAssets': state['originalChain']['resolverInsertedAssets'],
                'caseCount': len(state['cases']), 'rawOriginalMembers': state['exactOriginalChainRawMembers']},
            'independentZipDeployment': deployment_receipt})
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'succeeded': True, 'sourceCommit': commit, 'packages': [p['name'] for p in manifest['packages']]}))


if __name__ == '__main__':
    main()
