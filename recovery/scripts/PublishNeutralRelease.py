#!/usr/bin/env python3
"""Publish the already accepted neutral compiler without rebuilding binaries.

The source-free tag remains unchanged. Exact archived runtimes, their original
acceptance receipts, and the reproducible official-data ZIP are checked before
upload. Existing release assets are reused only when size and digest match.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import traceback
import zipfile

ROOT = Path(__file__).resolve().parents[2]
TAG = 'compiler-2026.11.01-neutral'
REPOSITORY = 'wu162/NewSdk'
RELEASE_ID = 430662411
RUN_ID = 41319194413
ARTIFACT_ID = 13088463710
RUNTIME_NAMES = {'NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'}
SMALL_ASSETS = ('compiler-manifest.json', 'release-metadata.json', 'USAGE.zh-CN.md', 'SHA256SUMS.txt')


def need(value, message):
    if not value:
        raise ValueError(message)


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def identity(path):
    return {'name': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path)}


def exact_identity(path, expected):
    actual = identity(path)
    need(actual['bytes'] == expected['bytes'] and actual['sha256'] == expected['sha256'],
         'File identity differs: ' + str(path))
    return actual


def gh(arguments, raw=False):
    process = subprocess.run(['gh', *map(str, arguments)], cwd=ROOT, capture_output=True)
    need(process.returncode == 0, 'GitHub command failed: gh ' + ' '.join(map(str, arguments)) + '\n' + process.stderr.decode('utf-8', 'replace'))
    return process.stdout if raw else json.loads(process.stdout)


def api(path):
    return gh(['api', 'repos/' + REPOSITORY + '/' + path])


def rebuild_reference(bundle, out, expected):
    recipe = read_json(bundle / 'reference-zip-layout.json')
    need(recipe['schemaVersion'] == 1 and recipe['file'] == expected['file'] and
         recipe['bytes'] == expected['bytes'] and recipe['sha256'] == expected['sha256'],
         'Reference ZIP recipe identity differs from release metadata')
    template = bundle / 'reference-zip-template.bin'
    need(template.stat().st_size == recipe['templateBytes'] and sha(template) == recipe['templateSha256'],
         'Reference ZIP template identity differs')
    target = out / recipe['file']
    digest, length = hashlib.sha256(), 0
    with template.open('rb') as source, target.open('wb') as destination:
        def write(data):
            nonlocal length
            destination.write(data); digest.update(data); length += len(data)
        for row in recipe['segments']:
            data = source.read(row['templateBytes'])
            need(len(data) == row['templateBytes'], 'Reference ZIP template ended early')
            write(data)
            block = ROOT / row['source']
            need(block.stat().st_size == row['sourceBytes'], 'Reference compressed block length differs')
            with block.open('rb') as stream:
                for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b''):
                    write(chunk)
        data = source.read(recipe['trailingTemplateBytes'])
        need(len(data) == recipe['trailingTemplateBytes'] and not source.read(1), 'Reference ZIP trailing template differs')
        write(data)
    need(length == expected['bytes'] and digest.hexdigest() == expected['sha256'], 'Rebuilt reference ZIP raw identity differs')
    return {'name': target.name, 'bytes': length, 'sha256': digest.hexdigest()}


def accepted_runtime(artifact, bundle, metadata, out):
    source = metadata['binarySourceCommit']
    package_manifest = read_json(artifact / 'neutral-packages/manifest.json')
    need((artifact / 'neutral-packages/manifest.json').read_bytes() == (bundle / 'compiler-manifest.json').read_bytes(),
         'Published compiler manifest differs from the accepted artifact bytes')
    need(package_manifest['sourceCommit'] == source and str(package_manifest['runId']) == str(RUN_ID) and
         package_manifest['packages'] == metadata['runtimePackages'], 'Runtime acceptance manifest identity differs')
    for kind in ('neutral-core', 'neutral-cli'):
        receipt = read_json(artifact / kind / 'validation.json')
        need(receipt['succeeded'] and receipt['sourceCommit'] == source and str(receipt['runId']) == str(RUN_ID),
             'Required actual Core/CLI acceptance missing: ' + kind)
    packages = []
    for package in metadata['runtimePackages']:
        mode = package['configuration']
        need(mode in ('Debug', 'Release'), 'Unexpected runtime configuration')
        mod = read_json(artifact / 'neutral-mod' / mode / 'validation.json')
        need(mod['succeeded'] and mod['sourceCommit'] == source and str(mod['runId']) == str(RUN_ID) and
             mod['configuration'] == mode and mod['sourceAndReferencesUnchanged'] and mod['exactOriginalChainRawMembers'],
             'Actual official neutral small MOD acceptance missing: ' + mode)
        need(len(mod['cases']) == 8 and all(case['passed'] for case in mod['cases']) and
             mod['originalChain']['hashFixChangedCount'] == 1 and mod['originalChain']['resolverInsertedAssets'] == 5,
             'Neutral configuration/hash/recursive-insertion scope differs: ' + mode)
        component = read_json(artifact / 'neutral-components' / mode / 'evidence/validation.json')
        need(component['succeeded'] and component['hashfixResolverSucceeded'] and not component['lodSucceeded'] and
             component['selectedStages'] == ['hashfix', 'resolver'] and len(component['cases']) == 10 and
             all(case['passed'] for case in component['cases']), 'Original repair component acceptance differs: ' + mode)
        product = read_json(artifact / 'neutral-product' / mode / 'build.json')
        host = read_json(artifact / 'neutral-product' / mode / 'validation/build.json')
        need(product['sourceCommit'] == source and product['configuration'] == mode and
             product['exeSha256'] == mod['binarySha256'] and host['sourceCommit'] == source and
             host['sameProductionObjects'] and host['productionExeUnchanged'] and
             host['productExeSha256'] == product['exeSha256'], 'Production/validation-object identity differs: ' + mode)
        deployed = package['independentZipDeployment']
        need(deployed['succeeded'] and deployed['sourceCommit'] == source and deployed['configuration'] == mode and
             deployed['zipSha256'] == package['sha256'] and deployed['coldSmallMod'] and deployed['exactAcceptedRawOutputs'],
             'Independent ZIP cold small-MOD deployment missing: ' + mode)
        need(read_json(artifact / 'neutral-packages' / ('deployment-' + mode) / 'validation.json') == deployed,
             'Independent ZIP deployment receipt differs: ' + mode)
        source_zip = artifact / 'neutral-packages' / package['name']
        exact_identity(source_zip, package)
        need({entry['name'] for entry in package['runtimeFiles']} == RUNTIME_NAMES and len(package['runtimeFiles']) == 3,
             'Runtime inventory is not exactly three files')
        with zipfile.ZipFile(source_zip) as archive:
            need(set(archive.namelist()) == RUNTIME_NAMES and len(archive.namelist()) == 3, 'Runtime ZIP layout differs')
            for entry in package['runtimeFiles']:
                data = archive.read(entry['name'])
                need(len(data) == entry['bytes'] and hashlib.sha256(data).hexdigest() == entry['sha256'],
                     'Runtime ZIP raw file identity differs: ' + entry['name'])
                built_file = artifact / 'neutral-product' / mode / 'bin' / entry['name']
                need(built_file.read_bytes() == data, 'Runtime ZIP differs from accepted production file')
        target = out / package['name']
        shutil.copyfile(source_zip, target)
        packages.append(exact_identity(target, package))
    need({p['configuration'] for p in metadata['runtimePackages']} == {'Debug', 'Release'} and len(packages) == 2,
         'Both accepted runtime configurations are required')
    return packages


def remote_assets(release, local):
    need(len(release['assets']) == len(local), 'Release asset count differs from exactly seven approved assets')
    by_name = {item['name']: item for item in release['assets']}
    need(set(by_name) == set(local), 'Release asset names differ')
    result = []
    for name, expected in local.items():
        row = by_name[name]
        need(row['state'] == 'uploaded' and row['size'] == expected['bytes'] and row.get('digest') == 'sha256:' + expected['sha256'],
             'Release asset size/digest differs: ' + name)
        result.append({'name': name, 'id': row['id'], 'bytes': row['size'], 'digest': row['digest'],
                       'url': row['browser_download_url']})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifact-root', required=True, type=Path)
    parser.add_argument('--bundle', type=Path, default=ROOT / 'recovery/releases' / TAG)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    bundle, artifact = args.bundle.resolve(), args.artifact_root.resolve()
    state = {'schemaVersion': 1, 'succeeded': False, 'published': False, 'tag': TAG,
             'releaseId': RELEASE_ID, 'binaryRebuilt': False, 'acceptanceRunId': RUN_ID,
             'publicationSourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
             'publicationRunId': os.environ.get('GITHUB_RUN_ID')}
    try:
        metadata = read_json(bundle / 'release-metadata.json')
        need(metadata['repository'] == REPOSITORY and metadata['tag'] == TAG and int(metadata['runId']) == RUN_ID and
             metadata['artifactId'] == ARTIFACT_ID, 'Release target or accepted artifact identity differs')
        state['binarySourceCommit'] = metadata['binarySourceCommit']
        assets = accepted_runtime(artifact, bundle, metadata, out)
        assets.append(rebuild_reference(bundle, out, metadata['referencePackage']))
        for name in SMALL_ASSETS:
            shutil.copyfile(bundle / name, out / name)
            assets.append(identity(out / name))
        local = {entry['name']: entry for entry in assets}
        need(len(local) == len(assets) == 7, 'Expected exactly seven unique release assets')
        checksums = {}
        for line in (out / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines():
            digest, name = line.split('  ', 1)
            checksums[name] = digest
        need(set(checksums) == set(local) - {'SHA256SUMS.txt'} and
             all(local[name]['sha256'] == digest for name, digest in checksums.items()), 'Release checksums differ')
        state['localAssets'] = assets
        if args.prepare_only:
            state['succeeded'] = True
            return
        run = api('actions/runs/' + str(RUN_ID))
        need(run['conclusion'] == 'success' and run['head_sha'] == metadata['binarySourceCommit'],
             'Accepted workflow run conclusion/source differs')
        archived = api('actions/artifacts/' + str(ARTIFACT_ID))
        need(archived['name'] == 'neutral-worldbuilder-41319194413-1' and not archived['expired'] and
             archived.get('digest') == 'sha256:' + metadata['artifactSha256'], 'Accepted workflow artifact identity differs')
        tag_bytes = subprocess.check_output(['git', 'show', TAG + ':release-metadata.json'], cwd=ROOT)
        need(tag_bytes == (bundle / 'release-metadata.json').read_bytes(), 'Source-free tag metadata differs from publication bundle')
        tag_files = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', TAG], cwd=ROOT, text=True).splitlines()
        need(set(tag_files) == set(metadata['autoArchiveFiles']) == {'README.md', 'release-metadata.json'},
             'Release tag contains files outside the approved source-free archive')
        release = api('releases/' + str(RELEASE_ID))
        need(release['id'] == RELEASE_ID and release['tag_name'] == TAG and release['draft'], 'Target is not the intended draft release')
        existing = {row['name']: row for row in release['assets']}
        need(set(existing) <= set(local), 'Draft contains unapproved asset names')
        pending = []
        for name, expected in local.items():
            if name in existing:
                row = existing[name]
                need(row['state'] == 'uploaded' and row['size'] == expected['bytes'] and
                     row.get('digest') == 'sha256:' + expected['sha256'],
                     'Existing release asset differs; refusing overwrite: ' + name)
            else:
                pending.append(out / name)
        if pending:
            # No --clobber: a concurrent same-name upload must fail safely.
            gh(['release', 'upload', TAG, *pending, '--repo', REPOSITORY], raw=True)
        state['remoteAssetsBeforePublication'] = remote_assets(api('releases/' + str(RELEASE_ID)), local)
        gh(['release', 'edit', TAG, '--draft=false', '--latest', '--repo', REPOSITORY,
            '--notes-file', bundle / 'release-notes.md'], raw=True)
        published = api('releases/' + str(RELEASE_ID))
        need(published['id'] == RELEASE_ID and published['tag_name'] == TAG and not published['draft'],
             'Release publication state differs')
        state['remoteAssets'] = remote_assets(published, local)
        state['releaseUrl'] = published['html_url']
        state['published'] = state['succeeded'] = True
    except Exception as error:
        state['error'] = str(error)
        (out / 'error.log').write_text(traceback.format_exc(), encoding='utf-8')
        raise
    finally:
        (out / 'summary.json').write_text(json.dumps(state, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
