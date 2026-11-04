#!/usr/bin/env python3
"""Publish six checked game-WorldBuilder assets from an accepted workflow run.

No binary is rebuilt and no tag is modified. Native Actions credentials handle
uploads; existing same-name assets must already have the exact approved digest.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import traceback
import zipfile

from PublishNeutralRelease import ROOT, need, read_json, identity, exact_identity, gh
from ValidateCompilerBig import read_archive

TAG = 'compiler-2026.11.01-game-worldbuilder'
REPOSITORY = 'wu162/NewSdk'
RUNTIME = {'NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'}
SMALL = ('compiler-manifest.json', 'release-metadata.json', 'USAGE.zh-CN.md', 'SHA256SUMS.txt')


def api(path):
    return gh(['api', 'repos/' + REPOSITORY + '/' + path])


def verify_acceptance(artifact, bundle, metadata, out):
    source = metadata['sourceCommit']
    validation = read_json(artifact / 'game-wb-validation/validation.json')
    need(validation['succeeded'] and validation['sourceCommit'] == source and validation['gameInputUnchanged'] and
         str(validation['runId']) == str(metadata['runId']), 'Automatic game extraction acceptance/source differs')
    packages = artifact / 'game-wb-validation/packages'
    manifest = read_json(packages / 'manifest.json')
    need((packages / 'manifest.json').read_bytes() == (bundle / 'compiler-manifest.json').read_bytes(),
         'Compiler manifest differs from accepted artifact bytes')
    need(manifest['sourceCommit'] == source and str(manifest['runId']) == str(metadata['runId']) and
         manifest['packages'] == validation['packages'] == metadata['runtimePackages'], 'Accepted runtime package manifest differs')
    need(len(validation['modes']) == 2 and {r['configuration'] for r in validation['modes']} == {'Debug', 'Release'},
         'Both production configurations must be accepted')
    local = []
    for mode in validation['modes']:
        name = mode['configuration']
        need(mode['passed'] and mode['inputsUnchanged'] and mode['hotExtractionMetadataUnchanged'] and
             mode['exactOriginalChainRawMembers'] and len(mode['cases']) == 9 and
             all(c['passed'] for c in mode['cases']), 'Game extraction/configuration/registry cases incomplete: ' + name)
        if metadata.get('sharedWorldbuilderCache'):
            need(mode.get('sharedCacheAcrossMods') and mode.get('concurrentSharedExtraction'),
                 'Shared WorldBuilder reuse and concurrent extraction acceptance missing: ' + name)
        need(mode['originalChain']['hashFixChangedCount'] == 1 and mode['originalChain']['resolverInsertedAssets'] == 5,
             'Original HashFix/recursive insertion scope differs')
        registry = [c for c in mode['cases'] if c['name'].startswith('registry-')]
        need({c['registry']['view'] for c in registry} == {32, 64} and all(c['registryRestored'] for c in registry),
             'Both original registry views/restoration are required')
        product = read_json(artifact / 'game-wb-product' / name / 'build.json')
        need(product['sourceCommit'] == source and product['configuration'] == name and
             product['exeSha256'] == mode['exeSha256'], 'Production binary receipt differs')
        package = next(p for p in validation['packages'] if p['configuration'] == name)
        deployed = package['independentZipDeployment']
        need(package['sourceCommit'] == source and deployed['succeeded'] and deployed['coldGameExtraction'] and
             deployed['exactAcceptedRawOutputs'] and len(deployed['artifacts']) == 2,
             'Independent three-file ZIP cold game extraction acceptance missing')
        cold = artifact / 'game-wb-validation' / name / 'explicit-cold/artifacts'
        for entry in deployed['artifacts']:
            exact_identity(cold / entry['name'], entry)
        cold_big = next(cold.glob('*.big'))
        original_big = artifact / 'game-wb-validation' / name / 'original-chain/original.big'
        actual = {r['name']: r['data'] for r in read_archive(cold_big)['members']}
        original = {r['name']: r['data'] for r in read_archive(original_big)['members']}
        need(actual == original, 'Accepted BIG raw members differ from original SDK chain')
        need({e['name'] for e in package['runtimeFiles']} == RUNTIME and len(package['runtimeFiles']) == 3,
             'Runtime receipt is not the exact three-file layout')
        source_zip = packages / package['name']
        exact_identity(source_zip, package)
        with zipfile.ZipFile(source_zip) as archive:
            need(set(archive.namelist()) == RUNTIME and len(archive.namelist()) == 3, 'Runtime ZIP layout differs')
            for entry in package['runtimeFiles']:
                raw = archive.read(entry['name'])
                import hashlib
                need(len(raw) == entry['bytes'] and hashlib.sha256(raw).hexdigest() == entry['sha256'],
                     'Runtime ZIP file identity differs')
                need(raw == (artifact / 'game-wb-product' / name / 'bin' / entry['name']).read_bytes(),
                     'Runtime ZIP bytes differ from production')
        shutil.copyfile(source_zip, out / package['name'])
        local.append(exact_identity(out / package['name'], package))
    need(len(local) == 2 and len(validation['packages']) == 2, 'Unexpected runtime package count')
    return local


def verify_remote(release, local):
    need(len(release['assets']) == 6, 'Release must contain exactly six approved assets')
    rows = {row['name']: row for row in release['assets']}
    need(set(rows) == set(local), 'Release asset names differ')
    result = []
    for name, expected in local.items():
        row = rows[name]
        need(row['state'] == 'uploaded' and row['size'] == expected['bytes'] and
             row.get('digest') == 'sha256:' + expected['sha256'], 'Release asset size/digest differs: ' + name)
        result.append({'name': name, 'bytes': row['size'], 'digest': row['digest'], 'url': row['browser_download_url']})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifact-root', required=True, type=Path)
    parser.add_argument('--tag', default=TAG)
    parser.add_argument('--bundle', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    tag = args.tag
    bundle = (args.bundle or ROOT / 'recovery/releases' / tag).resolve()
    artifact = args.artifact_root.resolve()
    state = {'schemaVersion': 1, 'succeeded': False, 'published': False, 'tag': tag, 'binaryRebuilt': False,
             'publicationSourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
             'publicationRunId': os.environ.get('GITHUB_RUN_ID')}
    try:
        metadata = read_json(bundle / 'release-metadata.json')
        need(metadata['tag'] == tag and metadata.get('repository', REPOSITORY) == REPOSITORY, 'Publication target differs')
        state.update({key: metadata[key] for key in ('runId', 'artifactId', 'sourceCommit')})
        entries = verify_acceptance(artifact, bundle, metadata, out)
        for name in SMALL:
            shutil.copyfile(bundle / name, out / name)
            entries.append(identity(out / name))
        local = {entry['name']: entry for entry in entries}
        need(len(local) == len(entries) == 6, 'Expected exactly six unique release assets')
        checksums = dict((line.split('  ', 1)[1], line.split('  ', 1)[0]) for line in
                         (out / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines())
        need(set(checksums) == set(local) - {'SHA256SUMS.txt'} and
             all(local[name]['sha256'] == value for name, value in checksums.items()), 'Release checksum list differs')
        state['localAssets'] = entries
        if args.prepare_only:
            state['succeeded'] = True
            return
        run = api('actions/runs/' + str(metadata['runId']))
        need(run['conclusion'] == 'success' and run['head_sha'] == metadata['sourceCommit'], 'Accepted workflow run differs')
        archived = api('actions/artifacts/' + str(metadata['artifactId']))
        artifact_name = metadata.get('artifactName', 'game-worldbuilder-' + str(metadata['runId']) + '-' + str(metadata.get('runAttempt', 1)))
        need(archived['name'] == artifact_name and not archived['expired'] and
             archived.get('digest') == 'sha256:' + metadata['artifactSha256'], 'Accepted workflow artifact digest differs')
        need(subprocess.check_output(['git', 'show', tag + ':release-metadata.json'], cwd=ROOT) ==
             (bundle / 'release-metadata.json').read_bytes(), 'Source-free tag raw metadata differs')
        need(set(subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', tag], cwd=ROOT, text=True).splitlines()) ==
             {'README.md', 'release-metadata.json'}, 'Source-free release tag contains unapproved files')
        # GitHub's by-tag endpoint excludes draft releases. Publication-only
        # routing stays separate from the immutable source-free tag metadata.
        release_id = metadata.get('releaseId')
        if release_id is None:
            release_id = read_json(bundle / 'publication.json')['releaseId']
        release_path = 'releases/' + str(release_id)
        release = api(release_path)
        state['releaseId'] = release_id
        need(release['id'] == release_id and release['tag_name'] == tag and release['draft'],
             'Target is not the intended draft release')
        existing = {entry['name']: entry for entry in release['assets']}
        need(set(existing) <= set(local), 'Draft contains unapproved asset names')
        pending = []
        for name, expected in local.items():
            if name in existing:
                row = existing[name]
                need(row['state'] == 'uploaded' and row['size'] == expected['bytes'] and
                     row.get('digest') == 'sha256:' + expected['sha256'], 'Existing same-name asset differs; refusing overwrite')
            else:
                pending.append(out / name)
        if pending:
            gh(['release', 'upload', tag, *pending, '--repo', REPOSITORY], raw=True)
        state['remoteAssetsBeforePublication'] = verify_remote(api(release_path), local)
        gh(['release', 'edit', tag, '--draft=false', '--latest', '--repo', REPOSITORY,
            '--notes-file', bundle / 'release-notes.md'], raw=True)
        published = api(release_path)
        need(published['id'] == release_id and published['tag_name'] == tag and not published['draft'],
             'Release publication state differs')
        state['remoteAssets'] = verify_remote(published, local)
        state['releaseUrl'] = published['html_url']
        state['published'] = state['succeeded'] = True
    except Exception as error:
        state['error'] = str(error)
        (out / 'error.log').write_text(traceback.format_exc(), encoding='utf-8')
        raise
    finally:
        (out / 'summary.json').write_text(json.dumps(state, indent=2) + '\n')


if __name__ == '__main__':
    main()
