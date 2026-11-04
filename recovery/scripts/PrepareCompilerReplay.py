#!/usr/bin/env python3
"""Replay only cache/payload tests against one fixed, verified production build.

The original run failed two test tools. This is not a successful-run reuse profile
and does not accept a current production rebuild. Production/SDK changes reject.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import zipfile
import PrepareCompilerCliReuse as archive_guard

SOURCE = 'fb6f67a0aaf5889bcd950485c41a883bc5807523'
RUN = 40360344360
ATTEMPT = 1
ARTIFACT = 12668516774
SHA256 = 'd1308d63759466789eb657ac3389a78f2274c52d261b0e078144923af435d8de'
REPOSITORY = 'wu162/NewSdk'
WORKFLOW = '.github/workflows/refactor-core-lifecycle.yml'
NAME = f'refactor-core-lifecycle-{RUN}-{ATTEMPT}'
ALLOWED = {
    '.github/workflows/refactor-cache-replay.yml',
    'recovery/scripts/PrepareCompilerReplay.py',
    'recovery/scripts/BuildCompilerCliControl.ps1',
    'recovery/tests/CompilerCliControl.cs',
    'recovery/scripts/TestCompilerBuildCache.ps1',
    'recovery/scripts/TestCompilerBuildCachePayloads.ps1',
    'recovery/scripts/ValidateAppProtocol.py',
    'recovery/protocol/README.md', 'recovery/REFACTOR_RESUME.md',
    'recovery/P5_LIFECYCLE_AUDIT.md',
}
FAILED_STEPS = {
    'Actual build-cache cold hot invalidation and retry matrix',
    'Actual Audio CDATA and W3X build-cache payload replay',
}
SUCCESS_STEPS = {
    'Standalone P2 application contracts in Debug',
    'Standalone P2 application contracts in Release',
    'Validate CLI scripts and output contracts before long Native linking',
    'Build managed Foundation inputs',
    'Build full production combination and verify deployment and route metadata',
    'Targeted real Core, backend and application pipeline in Debug and Release',
    'Isolated original BAB baseline for the two targeted backend cases',
    'Validate actual application JSONL and reports against protocol schemas',
    'Host-bound Texture payload and deployment rejection in Debug and Release',
    'Real production CLI, cold processes and actual CtrlBreak cancellation',
    'Validate real production CLI protocol outputs',
    'Effective input roots and original BAB output differential',
}
require = archive_guard.require


def api(endpoint):
    return json.loads(subprocess.run(['gh', 'api', endpoint], check=True, capture_output=True).stdout)


def closure(repo, source, candidate):
    require(source == SOURCE, 'Replay source must be the fixed fb6 production build')
    archive_guard.git(repo, 'diff', '--exit-code', source, candidate, '--', 'recovery/src', 'SDK')
    # Use the existing complete changed-file closure with a narrower exact list.
    # Only this process's imported module is configured; the old script is unchanged.
    previous = archive_guard.ALLOWED
    try:
        archive_guard.ALLOWED = ALLOWED
        changed = archive_guard.closure(repo, source, candidate)
        require(all(path in ALLOWED for path in changed), 'Replay changed a non-test input')
        return changed
    finally:
        archive_guard.ALLOWED = previous


def metadata(run, artifact, jobs):
    require(run.get('id') == RUN and run.get('head_sha') == SOURCE and run.get('run_attempt') == ATTEMPT and
            run.get('status') == 'completed' and run.get('conclusion') == 'failure' and run.get('path') == WORKFLOW and
            run.get('head_branch') == 'refactor/modcompiler-p0-p3', 'Fixed failed-test run identity mismatch')
    for key in ('repository', 'head_repository'):
        require(run.get(key, {}).get('full_name') == REPOSITORY, 'Replay repository mismatch')
    require(artifact.get('id') == ARTIFACT and artifact.get('name') == NAME and artifact.get('expired') is False and
            artifact.get('digest') == 'sha256:' + SHA256, 'Replay artifact identity/digest mismatch')
    origin = artifact.get('workflow_run', {})
    require(origin.get('id') == RUN and origin.get('head_sha') == SOURCE and
            origin.get('head_branch') == 'refactor/modcompiler-p0-p3' and
            origin.get('repository_id') == run['repository']['id'] and origin.get('head_repository_id') == run['head_repository']['id'],
            'Replay artifact origin mismatch')
    rows = jobs.get('jobs', [])
    require(jobs.get('total_count') == 1 and len(rows) == 1 and rows[0].get('run_id') == RUN and
            rows[0].get('status') == 'completed' and rows[0].get('conclusion') == 'failure', 'Replay jobs incomplete')
    steps = rows[0].get('steps', [])
    names = [step.get('name') for step in steps]
    require(len(names) == len(set(names)), 'Duplicate replay step names')
    require({step['name'] for step in steps if step.get('conclusion') == 'failure'} == FAILED_STEPS,
            'Replay failure set differs from the two identified test tools')
    for name in SUCCESS_STEPS:
        matches = [step for step in steps if step.get('name') == name]
        require(len(matches) == 1 and matches[0].get('status') == 'completed' and matches[0].get('conclusion') == 'success',
                'Production acceptance step not successful: ' + name)


def prepare(archive, candidate, output, repo):
    changed = closure(repo, SOURCE, candidate)
    # Exact same opened archive is checked again by the complete existing guard.
    require(hashlib.sha256(Path(archive).read_bytes()).hexdigest() == SHA256, 'Fixed replay archive SHA256 mismatch')
    with zipfile.ZipFile(archive) as z:
        def document(path):
            return json.loads(z.read(path).decode('utf-8-sig'))
        prefix = f'evidence/refactor/input-roots/{RUN}-{ATTEMPT}'
        roots = document(prefix + '/validation.json')
        require(roots.get('sourceCommit') == SOURCE and str(roots.get('runId')) == str(RUN) and
                str(roots.get('runAttempt')) == str(ATTEMPT) and roots.get('succeeded') is True,
                'Fixed input-root receipt identity/success mismatch')
        archive_guard.modes(roots)
        # The old final protocol step was gated by the two failed test tools.
        # Validate archived root reports/JSONL now without executing old inputs.
        with tempfile.TemporaryDirectory(prefix='fixed-roots-protocol-') as folder:
            root = Path(folder)
            for name in z.namelist():
                if name.startswith(prefix + '/') and name.rsplit('/', 1)[-1] in ('build-report.json', 'build.events.jsonl'):
                    relative = archive_guard.safe_name(name[len(prefix) + 1:])
                    target = root / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(z.read(name))
            protocol_path = root / 'protocol.json'
            subprocess.run([sys.executable, str(Path(repo) / 'recovery/scripts/ValidateAppProtocol.py'),
                            str(root), '--source-commit', SOURCE, '--output', str(protocol_path)], check=True)
            protocol = json.loads(protocol_path.read_text())
            require(protocol.get('status') == 'passed' and protocol.get('errorCount') == 0 and
                    {row.get('configuration') for row in protocol.get('matrix', [])} == {'Debug', 'Release'},
                    'Archived input-root protocol did not pass both modes')
        for mode in ('Debug', 'Release'):
            base = f'artifacts/core-lifecycle-product/{mode}/bin/'
            patch = document(base + 'BinaryAssetBuilder.TextureCompiler.dll.patch.json')
            texture_sha = archive_guard.digest(z.read(base + 'BinaryAssetBuilder.TextureCompiler.dll'))
            exe_sha = archive_guard.digest(z.read(base + 'NewModCompiler.exe'))
            require(patch.get('patchedSha256') == texture_sha and patch.get('targetExeSha256') == exe_sha and
                    patch.get('originalSha256') == '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e' and
                    patch.get('protectedPeAudit', {}).get('status') == 'passed' and
                    patch.get('protectedPeAudit', {}).get('outputSha256') == texture_sha,
                    'Fixed product Texture bytes/EXE-bound deployment mismatch')
    previous = archive_guard.closure, archive_guard.FIXTURES, archive_guard.CLI_CASES
    try:
        archive_guard.closure = lambda *args, **kwargs: changed
        # Historical accepted archives included this fixture; current production no longer has the boundary.
        archive_guard.FIXTURES = previous[1] | {'CompilerCacheContent', 'CompilerCacheBoundary', 'CompilerInputRoots'}
        archive_guard.CLI_CASES = (previous[2] - {'cache-rejected'}) | {'session-cache-rejected'}
        receipt = archive_guard.prepare(archive, SHA256, SOURCE, candidate, output, repo)
    finally:
        archive_guard.closure, archive_guard.FIXTURES, archive_guard.CLI_CASES = previous
    require(receipt['runId'] == str(RUN) and receipt['runAttempt'] == str(ATTEMPT), 'Replay archive run identity mismatch')
    receipt.update(prepared=True, candidateValidated=False, productionSourcesUnchanged=True,
                   binarySourceCommit=SOURCE, testSourceCommit=candidate,
                   scope='Fixed failed-test-source replay; not current production rebuild')
    target = Path(output)
    (target / 'candidate-replay.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    (target / 'reuse.json').unlink()
    (target / 'original-input-roots-protocol.json').write_text(json.dumps(protocol, indent=2) + '\n', encoding='utf-8')
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('repo', 'candidate-commit', 'output', 'archive'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    try:
        closure(args.repo, SOURCE, args.candidate_commit)
        metadata(api(f'repos/{REPOSITORY}/actions/runs/{RUN}'),
                 api(f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}'),
                 api(f'repos/{REPOSITORY}/actions/runs/{RUN}/jobs?filter=latest&per_page=100'))
        with Path(args.archive).open('xb') as stream:
            subprocess.run(['gh', 'api', f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}/zip'],
                           check=True, stdout=stream, stderr=subprocess.PIPE)
        receipt = prepare(args.archive, args.candidate_commit, args.output, args.repo)
        print(json.dumps({key:receipt[key] for key in ('prepared','candidateValidated','binarySourceCommit','testSourceCommit','runId')}))
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(1, 'Fixed candidate replay rejected: ' + type(error).__name__ + '\n')


if __name__ == '__main__':
    main()
