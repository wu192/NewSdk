#!/usr/bin/env python3
"""Verify a complete historical artifact and stage only its CLI runtime files.

Used by the opt-in CLI-only Actions job. This prepares historical binaries; it does not execute or
accept the candidate. The caller must regenerate the EXE-bound texture deployment
and run the candidate CLI/protocol validation, retaining both source identities.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import zipfile

RUNTIME = ('NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll')
MODES = {'Debug', 'Release'}
CLI_CASES = {'doctor', 'plan-all', 'combined', 'ascii-companion', 'base-only',
             'apt-only', 'maps-only', 'sdk-internal', 'empty-optional',
             'cache-rejected', 'bad-xml', 'retry-after-bad', 'log-directory-rejected',
             'required-log-fault', 'retry-after-log-fault', 'ctrl-break',
             'retry-after-cancel', 'business-combined', 'business-base-only',
             'business-apt-only', 'business-maps-only'}
FIXTURES = {'CompilerCorePaths', 'CompilerUnicodeSettings', 'CompilerCoreLifecycle',
            'CompilerResourceLifecycle', 'CompilerStateLifecycle',
            'CompilerBackendLifecycle', 'CompilerApplicationBuild'}
# Deliberately enumerate tools/docs. No wildcard permits production, SDK,
# build recipes, other fixture suites or arbitrary future workflow changes.
WORKFLOW = '.github/workflows/refactor-core-lifecycle.yml'
# Exact reviewed migration from legacy CLI-only source checks to pinned archive reuse.
WORKFLOW_MIGRATION = (
    '49f2b553e62114f13507cae579c45cbb4482a526f276dceac53fd7c672747899',
    'a70b941dd56f2e4ec6e5b91a70c15913a5563f7c2571de318fa7967aa6fd89eb')
# Dedicated historical-runtime validation is admitted only at the reviewed bytes.
# It cannot silently become a different build/deployment driver.
PINNED_TOOL_WORKFLOWS = {
    '.github/workflows/refactor-ui-unicode.yml': '1e58ce9f466c7dfacd5f6f9cf8086b296c7768c4c8771f5f38f55d4c65f3bfee',
}
ALLOWED = {
    # Independent preparation-unit-test CI; never a production build driver.
    '.github/workflows/refactor-cli-reuse.yml',
    '.github/workflows/refactor-ui-unicode.yml',
    'recovery/scripts/TestCompilerCliUiUnicode.ps1',
    'recovery/scripts/TestCompilerBackendBaseline.ps1',
    'recovery/scripts/TestCompilerCli.ps1',
    'recovery/scripts/CompilerCliOutputs.ps1',
    # Read-only cache-location oracles used by validation/benchmark scripts.
    'recovery/scripts/CompilerCacheLayout.ps1',
    'recovery/scripts/CompilerCachePaths.py',
    'recovery/scripts/TestCompilerCliOutputs.ps1',
    'recovery/scripts/BuildCompilerCliControl.ps1',
    'recovery/scripts/PrepareCompilerCliBusiness.py',
    'recovery/scripts/PrepareCompilerCliUi.py',
    'recovery/scripts/ValidateAppProtocol.py',
    'recovery/scripts/PrepareCompilerCliReuse.py',
    'recovery/scripts/DownloadCompilerCliReuse.py',
    'recovery/tests/test_download_compiler_cli_reuse.py',
    'recovery/tests/CompilerCliControl.cs',
    'recovery/tests/test_prepare_compiler_cli_reuse.py',
    'recovery/REFACTOR_RESUME.md', 'recovery/PATH_AUDIT.md',
    'recovery/P5_LIFECYCLE_AUDIT.md',
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(repo, *args):
    return subprocess.run(['git', '-C', str(repo), *args], check=True,
                          capture_output=True).stdout


def workflow_without_trigger_paths(data):
    # Only remove the unique on.push.paths list. Preserve every other byte,
    # including steps, environments, setup versions and compilation flags.
    pattern = rb'(?m)^    paths:\r?\n(?:      - [^\r\n]*\r?\n)+'
    matches = list(re.finditer(pattern, data))
    require(len(matches) == 1, 'Workflow must contain one explicit push.paths list')
    match = matches[0]
    prior = data[:match.start()].splitlines()
    top = [line for line in prior if line and not line.startswith(b' ')]
    nested = [line for line in prior if line.startswith(b'  ') and not line.startswith(b'   ')]
    require(top and top[-1] == b'on:' and nested and nested[-1] == b'  push:',
            'Workflow paths list must belong to on.push')
    header = match.group().splitlines(keepends=True)[0]
    return data[:match.start()] + header + data[match.end():]


def closure(repo, source, candidate):
    for value in (source, candidate):
        require(re.fullmatch(r'[0-9a-f]{40}', value), 'Use full lowercase commit SHAs')
        git(repo, 'cat-file', '-e', value + '^{commit}')
    git(repo, 'merge-base', '--is-ancestor', source, candidate)
    changed = git(repo, 'diff', '--name-only', '--no-renames', '-z', source, candidate).decode().split('\0')
    changed = sorted(p for p in changed if p)
    require(all(p in ALLOWED or p == WORKFLOW for p in changed), 'Non-CLI input changed: ' + ', '.join(p for p in changed if p not in ALLOWED and p != WORKFLOW))
    for path in changed:
        if path in PINNED_TOOL_WORKFLOWS:
            require(digest(git(repo, 'show', candidate + ':' + path)) == PINNED_TOOL_WORKFLOWS[path],
                    'Dedicated runtime validation workflow differs from reviewed bytes: ' + path)
    if WORKFLOW in changed:
        old = workflow_without_trigger_paths(git(repo, 'show', source + ':' + WORKFLOW))
        new = workflow_without_trigger_paths(git(repo, 'show', candidate + ':' + WORKFLOW))
        # One reviewed migration wires strict historical-runtime preparation.
        # Hash both complete normalized workflows, not a permissive step subset.
        require(old == new or (digest(old), digest(new)) == WORKFLOW_MIGRATION,
                'Workflow change exceeds on.push.paths trigger list or pinned integration')
    return changed


def safe_name(name):
    require(name and '\\' not in name and ':' not in name and '\0' not in name,
            'Unsafe ZIP name: ' + name)
    normalized = name[:-1] if name.endswith('/') else name
    parts = normalized.split('/')
    require(all(p and p not in ('.', '..') for p in parts) and not PurePosixPath(name).is_absolute(),
            'Unsafe ZIP name: ' + name)
    return normalized


def modes(document, key='modes'):
    rows = document.get(key, [])
    require(len(rows) == 2 and {r.get('configuration') for r in rows} == MODES,
            'Missing or duplicate Debug/Release modes')
    require(all(r.get('passed') is True for r in rows), 'Mode did not pass')
    return {r['configuration']: r for r in rows}


def prepare(archive, expected_sha256, source_commit, candidate_commit, output, repo):
    changed = closure(repo, source_commit, candidate_commit)
    require(re.fullmatch(r'[0-9a-f]{64}', expected_sha256), 'Invalid expected SHA256')
    # Verify exactly the bytes subsequently opened, rather than hashing one path
    # and allowing it to be replaced before ZIP parsing.
    with open(archive, 'rb') as stream:
        hasher = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            hasher.update(block)
        require(hasher.hexdigest() == expected_sha256, 'Archive SHA256 mismatch')
        stream.seek(0)
        with zipfile.ZipFile(stream) as z:
            names, folded = set(), set()
            for entry in z.infolist():
                # ZipInfo preserves the decoded central-directory name here
                # before platform separator normalization and NUL truncation.
                # Inspect it first; filename alone can hide malicious bytes.
                name = safe_name(entry.orig_filename)
                require(entry.orig_filename == entry.filename,
                        'Unsafe ZIP name normalization: ' + repr(entry.orig_filename))
                require(name.casefold() not in folded, 'Duplicate ZIP path: ' + name)
                require(not stat.S_ISLNK(entry.external_attr >> 16), 'ZIP symlink forbidden: ' + name)
                require(not entry.flag_bits & 1, 'Encrypted ZIP entry forbidden')
                names.add(entry.filename); folded.add(name.casefold())

            def read(path):
                require(path in names, 'Missing artifact input: ' + path)
                return z.read(path)

            def document(path):
                value = json.loads(read(path).decode('utf-8-sig'))
                require(isinstance(value, dict), 'Receipt must be a JSON object: ' + path)
                return value

            anchors = {}
            for section in ('core-lifecycle-deployment', 'core-lifecycle', 'core-lifecycle-texture', 'core-cli'):
                prefix = 'evidence/refactor/' + section + '/'
                found = [p for p in names if p.startswith(prefix) and p.endswith('/validation.json') and len(p[len(prefix):].split('/')) == 2]
                require(len(found) == 1, 'Expected exactly one complete receipt: ' + section)
                anchors[section] = (found[0], document(found[0]))
            identities = {(d.get('sourceCommit'), str(d.get('runId')), str(d.get('runAttempt'))) for _, d in anchors.values()}
            require(len(identities) == 1, 'Source/run identity differs across receipts')
            source, run, attempt = identities.pop()
            require(source == source_commit and run.isdigit() and attempt.isdigit(), 'Invalid historical source/run')
            for path, _ in anchors.values():
                require(path.split('/')[-2] == run + '-' + attempt, 'Receipt run directory mismatch')
            dep = anchors['core-lifecycle-deployment'][1]
            core = anchors['core-lifecycle'][1]
            texture = anchors['core-lifecycle-texture'][1]
            cli_path, cli = anchors['core-cli']
            require(dep.get('status') == texture.get('status') == 'passed', 'Deployment/Texture receipt failed')
            require(core.get('succeeded') is True and cli.get('succeeded') is True, 'Core/CLI receipt failed')
            dep_modes, core_modes, tex_modes, cli_modes = modes(dep), modes(core), modes(texture, 'configurations'), modes(cli)
            def verify_protocol(base, filename):
                protocol = document(base + '/' + filename)
                require(protocol.get('sourceCommit') == source and protocol.get('status') == 'passed' and
                        protocol.get('errorCount') == 0 and protocol.get('errors') == [], 'Application/CLI protocol failed or identity mismatch')
                matrix = protocol.get('matrix', [])
                require(len(matrix) == 2 and {r.get('configuration') for r in matrix} == MODES and
                        all(all(isinstance(r.get(k), int) and r[k] > 0 for k in ('reports', 'eventFiles', 'events')) for r in matrix),
                        'Incomplete Application/CLI protocol coverage')
                require(protocol.get('validatedFiles'), 'No validated protocol files')
                for row in protocol['validatedFiles']:
                    relative = safe_name(row['path'])
                    require(digest(read(base + '/' + relative)) == row['sha256'], 'Protocol input hash mismatch')
            verify_protocol(cli_path.rsplit('/', 1)[0], 'cli-protocol.json')
            verify_protocol(anchors['core-lifecycle'][0].rsplit('/', 1)[0], 'app-protocol.json')
            files, payload = [], {}
            for mode in sorted(MODES):
                build_path = 'artifacts/core-lifecycle-product/' + mode + '/build.json'
                build = document(build_path)
                require(build.get('sourceCommit') == source and build.get('configuration') == mode and
                        build.get('containsProbeOrTestSources') is False, 'Product build identity/test isolation mismatch')
                hashes = [build.get('exeSha256'), dep_modes[mode].get('exeSha256'), core_modes[mode].get('productExeSha256'), tex_modes[mode].get('exeSha256'), cli_modes[mode].get('exeSha256')]
                require(len(set(hashes)) == 1 and hashes[0], 'Production EXE identity differs across suites')
                fixtures = core_modes[mode].get('fixtures', [])
                require(len(fixtures) == len(FIXTURES) and {f.get('name') for f in fixtures} == FIXTURES and all(f.get('passed') is True for f in fixtures), 'Incomplete Core fixture success')
                for fixture in fixtures:
                    path = anchors['core-lifecycle'][0].rsplit('/', 1)[0] + '/' + mode + '/' + fixture['name'] + '/result.json'
                    require(digest(read(path)) == fixture['resultSha256'], 'Core fixture result hash mismatch')
                    result = document(path)
                    require(result.get('succeeded') is True and result.get('sourceCommit') == source and str(result.get('runId')) == run, 'Core fixture identity/success mismatch')
                cases = cli_modes[mode].get('cases', [])
                require(CLI_CASES <= {c.get('name') for c in cases} and
                        len({c.get('name') for c in cases}) == len(cases) and
                        all(c.get('passed') is True for c in cases), 'CLI case failed or absent')
                business = cli_modes[mode].get('business', {})
                require(business.get('sourceCommit') == source and str(business.get('runId')) == run and
                        business.get('configuration') == mode and all(business.get(k) is True for k in
                        ('succeeded', 'inputsUnchanged', 'coldGroupOutputsIdentical', 'originalFourOutputsIdentical')),
                        'CLI business acceptance incomplete')
                for baseline in ('original-baseline', 'business-original-baseline'):
                    original = document(cli_path.rsplit('/', 1)[0] + '/' + mode + '/' + baseline + '/validation.json')
                    require(original.get('sourceCommit') == source and str(original.get('runId')) == run and
                            str(original.get('runAttempt')) == attempt and original.get('succeeded') is True,
                            'CLI original baseline identity/success mismatch')
                runtime = cli_modes[mode].get('runtimeInputs', {})
                for name in RUNTIME:
                    matches = [v for k, v in runtime.items() if k.replace('\\', '/').lstrip('/') == name]
                    require(len(matches) == 1, 'Missing or duplicate CLI runtime input: ' + name)
                    path = 'artifacts/core-lifecycle-product/' + mode + '/bin/' + name
                    data = read(path); sha = digest(data)
                    require(sha == matches[0], 'CLI runtime binary hash mismatch: ' + path)
                    if name.endswith('.exe'):
                        require(sha == hashes[0], 'Product build EXE hash mismatch')
                    else:
                        refs = [r.get('sha256') for r in core_modes[mode].get('runtimeFiles', []) if r.get('name') == name]
                        require(refs == [sha], 'Core runtime hash mismatch: ' + name)
                    relative = mode + '/bin/' + name
                    payload[relative] = data; files.append({'path': relative, 'sha256': sha})
            receipt = {'schemaVersion': 1, 'prepared': True, 'candidateValidated': False,
                       'sourceCommit': source, 'candidateCommit': candidate_commit,
                       'runId': run, 'runAttempt': attempt, 'archiveSha256': expected_sha256,
                       'changedPaths': changed, 'files': files,
                       'requiredNextSteps': ['Regenerate EXE-bound texture deployment', 'Run candidate CLI and protocol validation'],
                       'scope': 'Historical runtime preparation only; no candidate execution or acceptance; current CLI/protocol tests required'}
            output = Path(output)
            require(not output.exists(), 'Refusing to replace existing output')
            output.parent.mkdir(parents=True, exist_ok=True)
            temporary = Path(tempfile.mkdtemp(prefix=output.name + '-', dir=output.parent))
            try:
                for relative, data in payload.items():
                    target = temporary / relative; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(data)
                (temporary / 'reuse.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
                temporary.rename(output)
            finally:
                if temporary.exists():
                    shutil.rmtree(temporary)
            return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('archive', 'expected-sha256', 'source-commit', 'candidate-commit', 'output', 'repo'):
        parser.add_argument('--' + option, required=True)
    args = parser.parse_args()
    try:
        receipt = prepare(**vars(args))
        print(json.dumps({'prepared': True, 'candidateValidated': False, 'files': len(receipt['files'])}))
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(1, 'CLI reuse rejected: ' + str(error) + '\n')


if __name__ == '__main__':
    main()
