#!/usr/bin/env python3
"""Stage the accepted legacy-cache Release and bounded real-source workloads."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as E
import zipfile

SOURCE = '39039961847cd1ca9cb93743fd51741a4e5c9f49'
RUN = '40814401237'
ARTIFACT = '12871674302'
ARCHIVE_SHA256 = '6c395456add02dec40c7759e9cc5a26d34ea353f5e3ee588a12a78df3bfab044'
HISTORICAL = 'bacfc69fc3e71c8116d1f22b933b8b6173170ecf'
UPHEAVAL_TREE = '5551fc26a6f96b62613443e0d0fa2958c14cbce0'
PREFIX = 'artifacts/final-release/40814401237-1/'
NAMES = frozenset(('NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll',
                   'BinaryAssetBuilder.TextureCompiler.dll', 'BinaryAssetBuilder.TextureCompiler.dll.patch.json'))
BENCHMARK_FILES = frozenset(('recovery/scripts/PrepareCompilerCacheBenchmark.py',
                            'recovery/scripts/BenchmarkCompilerCache.py',
                            'recovery/tests/test_prepare_cache_benchmark.py',
                            'recovery/tests/test_cache_benchmark.py',
                            'recovery/scripts/BenchmarkCompilerSdkComparison.py',
                            'recovery/scripts/PrepareCompilerSdkBenchmark.py',
                            'recovery/tests/test_sdk_benchmark.py',
                            'recovery/tests/test_prepare_sdk_benchmark.py',
                            '.github/workflows/compiler-sdk-benchmark.yml',
                            'recovery/scripts/AnalyzeCompilerSdkTimings.py',
                            'recovery/scripts/ProfileCompilerCacheIdentity.ps1',
                            'recovery/tests/CompilerCacheIdentityProfile.cs',
                            '.github/workflows/compiler-cache-profile.yml',
                            '.github/workflows/compiler-cache-benchmark.yml'))


def need(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def file_sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def git(repo, *args, check=True):
    return subprocess.run(['git', '-C', str(repo), *args], check=check, capture_output=True, timeout=60)


def allowed_change(path):
    # SDK and source documentation can be compiler inputs; never exempt them.
    if path.startswith(('SDK/', 'recovery/src/', 'recovery/probes/')):
        return False
    return path in BENCHMARK_FILES or path.endswith('.md') or path.startswith('recovery/evidence/')


def verify_sources(repo, candidate, source=SOURCE):
    candidate = git(repo, 'rev-parse', '--verify', candidate + '^{commit}').stdout.decode().strip()
    need(git(repo, 'merge-base', '--is-ancestor', source, candidate, check=False).returncode == 0,
         'Accepted binary source is not an ancestor of the benchmark candidate')
    # Compare both committed closure and actual checkout, including local edits.
    changed = set()
    for target in (candidate,):
        changed.update(git(repo, 'diff', '--name-only', '-z', source, target).stdout.decode().split('\0'))
    changed.update(git(repo, 'diff', '--name-only', '-z', source).stdout.decode().split('\0'))
    changed.discard('')
    denied = sorted(p for p in changed if not allowed_change(p))
    need(not denied, 'Production/SDK/recipe closure changed: ' + ', '.join(denied[:8]))
    # Additional untracked compiler inputs could affect glob-based recipes.
    unknown = git(repo, 'ls-files', '--others', '--exclude-standard', '-z').stdout.decode().split('\0')
    denied = [p for p in unknown if p and p.startswith(('SDK/', 'recovery/src/', 'recovery/scripts/',
              'recovery/probes/', '.github/workflows/')) and not allowed_change(p)
              and not (p.startswith('recovery/scripts/__pycache__/PrepareCompilerCacheBenchmark.') and p.endswith('.pyc'))
              and not (p.startswith('recovery/scripts/__pycache__/BenchmarkCompilerCache.') and p.endswith('.pyc'))]
    need(not denied, 'Untracked production/recipe input: ' + ', '.join(denied[:8]))
    return candidate, sorted(changed)


def verify_identity(body, source_key='binarySourceCommit', source=SOURCE, run=RUN, attempt=1):
    need(body.get(source_key) == source and str(body.get('runId')) == str(run) and str(body.get('runAttempt')) == str(attempt),
         'Accepted source/run/attempt identity differs')


def validate_bundle(path, expected_sha=ARCHIVE_SHA256, source=SOURCE, run=RUN, attempt=1):
    prefix = 'artifacts/final-release/%s-%s/' % (run, attempt)
    need(file_sha(path) == expected_sha, 'Pinned artifact SHA256 mismatch')
    with zipfile.ZipFile(path) as outer:
        need(len(outer.namelist()) == len(set(outer.namelist())), 'Duplicate outer archive member')
        release = json.loads(outer.read(prefix + 'release.json'))
        verify_identity(release, source=source, run=run, attempt=attempt)
        need(release.get('succeeded') is True and release.get('sourceCommit') == source,
             'Final release did not succeed for accepted source')
        configs = release.get('configurations', [])
        need(len(configs) == 2 and {c.get('configuration') for c in configs} == {'Debug', 'Release'},
             'Final release lacks exact Debug/Release acceptance')
        need(all(c.get('rawOutputEquality') is True for c in configs), 'ZIP raw output equality did not pass')
        manifest = json.loads(outer.read(prefix + 'NewModCompiler-Win32-Release.manifest.json'))
        verify_identity(manifest, source=source, run=run, attempt=attempt)
        need(manifest.get('configuration') == 'Release' and manifest.get('platform') == 'Win32'
             and manifest.get('status') == 'verified-small-mod-deployment'
             and manifest.get('packagingSourceCommit') == source, 'Release manifest identity differs')
        archive = manifest['archive']
        need(archive['name'] == 'NewModCompiler-Win32-Release.zip', 'Unexpected inner ZIP name')
        data = outer.read(prefix + archive['name'])
        need(len(data) == archive['bytes'] and sha(data) == archive['sha256'], 'Release ZIP digest mismatch')
        accepted = next(c for c in configs if c['configuration'] == 'Release')
        need(accepted.get('zipSha256') == archive['sha256'], 'Release receipt ZIP binding differs')
        identities = manifest.get('files', [])
        need(len(identities) == 5 and {f['name'] for f in identities} == NAMES, 'Manifest must identify exactly five runtime files')
        with zipfile.ZipFile(io.BytesIO(data)) as inner:
            need(len(inner.namelist()) == 5 and set(inner.namelist()) == NAMES, 'Runtime ZIP must contain exactly five root files')
            files = {name: inner.read(name) for name in NAMES}
        for row in identities:
            need(len(files[row['name']]) == row['bytes'] and sha(files[row['name']]) == row['sha256'],
                 'Runtime file digest mismatch: ' + row['name'])
        patch = json.loads(files['BinaryAssetBuilder.TextureCompiler.dll.patch.json'])
        need(patch.get('mode') == 'redirect' and patch.get('targetExeSha256') == sha(files['NewModCompiler.exe'])
             and patch.get('patchedSha256') == sha(files['BinaryAssetBuilder.TextureCompiler.dll'])
             and patch.get('originalSha256') == '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e'
             and patch.get('protectedPeAudit', {}).get('status') == 'passed'
             and patch.get('protectedPeAudit', {}).get('mode') == 'redirect', 'Texture patch is not bound to accepted runtime')
        need(patch == manifest.get('texturePatch'), 'Manifest/runtime patch identity differs')
        return files, identities


def inventory(root):
    rows = []
    for path in sorted(Path(root).rglob('*')):
        need(not path.is_symlink(), 'Workload symlink rejected')
        if path.is_file():
            rows.append(dict(path=path.relative_to(root).as_posix(), bytes=path.stat().st_size, sha256=file_sha(path)))
    return rows


def prepare_texture_workload(stage, destination):
    data_root = stage / 'workloads/texture-16/Data'
    data_root.mkdir(parents=True)
    # Same documented top-left BGRA fixture as TextureRedirect/MakeFixture.py.
    # The benchmark owns this synthetic input; no SDK texture is replaced.
    header = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 256, 256, 32, 0x28)
    pixels = b''.join(bytes((x, y, x ^ y, 255)) for y in range(256) for x in range(256))
    (data_root / 'probe.tga').write_bytes(header + pixels)
    textures = ''.join('<Texture id="BenchmarkTexture%02d" File="probe.tga" Type="StandardTexture" '
                       'GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/>' % index
                       for index in range(16))
    (data_root / 'Mod.xml').write_text('<AssetDeclaration xmlns="uri:ea.com:eala:asset">' + textures +
                                      '</AssetDeclaration>\n', encoding='utf-8')
    return dict(name='texture-16', modRoot=str(destination / 'workloads/texture-16'), assetCount=16,
                scope='Synthetic only: sixteen independent Texture identities using a generated 256x256 top-left BGRA TGA. Not Upheaval or a complete real MOD; no Audio, GameObject, APT or packaging claim.',
                synthetic=True, fullModInputCopy=False, files=inventory(data_root.parent))


def prepare_workloads(repo, stage, destination):
    workloads = []
    original = repo / 'SDK/Mods/mymod'
    before = inventory(original)
    need(before, 'Complete mymod inputs are absent')
    mymod = stage / 'workloads/mymod'
    shutil.copytree(original, mymod)
    need(inventory(original) == before and inventory(mymod) == before, 'mymod changed during preparation')
    workloads.append(dict(name='mymod', modRoot=str(destination / 'workloads/mymod'),
                          scope='Complete current SDK/Mods/mymod input copy; benchmark selects base only, without packaging.',
                          files=before, sourceCommit=SOURCE, sourcePath='SDK/Mods/mymod', fullModInputCopy=True))
    need(git(repo, 'rev-parse', HISTORICAL + ':SDK/Mods/Upheaval').stdout.decode().strip() == UPHEAVAL_TREE,
         'Historical Upheaval tree differs')
    entries = git(repo, 'ls-tree', '-rz', HISTORICAL, 'SDK/Mods/Upheaval').stdout.split(b'\0')
    selected = []
    for row in entries:
        if not row:
            continue
        meta, name = row.split(b'\t', 1)
        name = name.decode('utf-8')
        if name.endswith('/Armor.xml'):
            mode, kind, blob = meta.decode().split()
            need(mode == '100644' and kind == 'blob', 'Armor source is not an ordinary blob')
            selected.append((name, blob))
    selected.sort()
    need(len(selected) == 11, 'Pinned Upheaval Armor subset must contain eleven documents')
    armor = stage / 'workloads/upheaval-armor/Data'
    armor.mkdir(parents=True)
    sources = []
    assets = 0
    for index, (source_path, blob) in enumerate(selected):
        data = git(repo, 'cat-file', 'blob', blob).stdout
        doc = E.fromstring(data)
        need(doc.tag == '{uri:ea.com:eala:asset}AssetDeclaration', 'Unexpected Armor declaration root')
        children = list(doc)
        need(all(child.tag == '{uri:ea.com:eala:asset}ArmorTemplate' for child in children),
             'Armor subset gained non-Armor dependencies')
        assets += len(children)
        target = 'armor-%02d.xml' % index
        (armor / target).write_bytes(data)
        sources.append(dict(path='Data/' + target, sourcePath=source_path, gitBlob=blob, sha256=sha(data)))
    need(assets == 12, 'Pinned Armor asset count differs')
    includes = ''.join('<Include type="all" source="armor-%02d.xml"/>' % i for i in range(11))
    (armor / 'Mod.xml').write_text('<AssetDeclaration xmlns="uri:ea.com:eala:asset"><Includes>' + includes +
                                  '</Includes></AssetDeclaration>\n', encoding='utf-8')
    workloads.append(dict(name='upheaval-armor', modRoot=str(destination / 'workloads/upheaval-armor'),
                          scope='Subset only: eleven unchanged historical Upheaval Armor.xml documents, twelve ArmorTemplate assets, generated include entry. No full Upheaval, Texture, Audio, GameObject, APT or packaging claim.',
                          sourceCommit=HISTORICAL, sourceTree=UPHEAVAL_TREE, sources=sources, assetCount=assets,
                          fullModInputCopy=False, files=inventory(armor.parent)))
    workloads.append(prepare_texture_workload(stage, destination))
    return workloads


def download(target, artifact=ARTIFACT):
    with target.open('xb') as stream:
        result = subprocess.run(['gh', 'api', 'repos/wu162/NewSdk/actions/artifacts/' + str(artifact) + '/zip'],
                                stdout=stream, stderr=subprocess.PIPE, timeout=180)
    need(result.returncode == 0, 'Pinned artifact download failed (remote stderr withheld; exit %d)' % result.returncode)


def accepted_run(run):
    def api(path):
        return json.loads(subprocess.run(['gh', 'api', 'repos/wu162/NewSdk/' + path],
                                        check=True, capture_output=True, timeout=60).stdout)
    result = api('actions/runs/' + str(run))
    need(result['status'] == 'completed' and result['conclusion'] == 'success' and
         result['path'] == '.github/workflows/refactor-core-lifecycle.yml',
         'Benchmark requires a successful complete production acceptance run')
    attempt = result['run_attempt']
    artifacts = api('actions/runs/%s/artifacts' % run)['artifacts']
    selected = [a for a in artifacts if a['name'] == 'refactor-core-lifecycle-%s-%s' % (run, attempt)]
    need(len(selected) == 1 and not selected[0]['expired'], 'Accepted production artifact is unavailable')
    digest = selected[0]['digest']
    need(digest.startswith('sha256:') and len(digest) == 71, 'Accepted artifact has no SHA256 digest')
    return result['head_sha'], attempt, str(selected[0]['id']), digest[7:]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--candidate-commit', default='HEAD')
    parser.add_argument('--accepted-run', type=int, help='Use the exact five-file Release from a successful full production run')
    args = parser.parse_args()
    repo = args.repo.resolve()
    destination = args.output.resolve()
    need(not destination.exists(), 'Refusing to replace existing preparation output')
    need(not destination.is_relative_to(repo / 'SDK') and not destination.is_relative_to(repo / 'recovery/src'),
         'Preparation output must not modify SDK or production sources')
    source, run, attempt, artifact, archive_sha = SOURCE, RUN, 1, ARTIFACT, ARCHIVE_SHA256
    if args.accepted_run:
        run = str(args.accepted_run)
        source, attempt, artifact, archive_sha = accepted_run(run)
    candidate, changes = verify_sources(repo, args.candidate_commit, source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.cache-benchmark-', dir=destination.parent) as temporary:
        stage = Path(temporary) / 'prepared'
        stage.mkdir()
        archive = args.archive
        if archive is None:
            archive = Path(temporary) / 'artifact.zip'
            download(archive, artifact)
        files, identities = validate_bundle(archive, archive_sha, source, run, attempt)
        runtime = stage / 'runtime'
        runtime.mkdir()
        for name, data in files.items():
            (runtime / name).write_bytes(data)
        state = dict(schemaVersion=1, succeeded=True, binarySourceCommit=source, sourceCommit=source,
                     benchmarkSourceCommit=candidate, sourceRunId=run, sourceRunAttempt=attempt, artifactId=artifact,
                     archiveSha256=archive_sha, runtimeDirectory=str(destination / 'runtime'),
                     runtimeFiles=identities, productionSourcesUnchanged=True, allowedChanges=changes,
                     sdkRoot=str(repo / 'SDK'))
        state['workloads'] = prepare_workloads(repo, stage, destination)
        (stage / 'preparation.json').write_text(json.dumps(state, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        stage.rename(destination)
    print('Prepared accepted Release and three workloads: ' + str(destination / 'preparation.json'))


if __name__ == '__main__':
    main()
