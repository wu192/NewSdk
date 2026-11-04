#!/usr/bin/env python3
"""Prepare one fixed /sc test replay without rebuilding production binaries.

The original run's failure is preserved. Only new session/SDK test results may be
accepted; the historical production/Core/CLI receipts retain their own source.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import stat
import tempfile
import subprocess
import sys
import zipfile
import PrepareCompilerCliReuse as guard

SOURCE = 'f2ba141284469411d604d756f7916b0cf6aecf0a'
RUN = 40406049617
ATTEMPT = 1
# Set only from the completed fixed run's actual uploaded artifact metadata.
# Zero/empty constants deliberately reject preparation before that evidence exists.
ARTIFACT = 12692880009
SHA256 = 'fa8d820ff40fb55139fbcf6ce6be970ddbc69a3f2fedd3c1c43cadfce05f8846'
REPOSITORY = 'wu162/NewSdk'
WORKFLOW = '.github/workflows/refactor-core-lifecycle.yml'
NAME = f'refactor-core-lifecycle-{RUN}-{ATTEMPT}'
WORKFLOW_BYTES = {
    'source': '887a15ccfe26b834c9ae9fccd545b35f27b3b70af79bffd8ae5aaefee529b2f8',
    # This production build driver already contains the fixture preflight and
    # SDK milestone. No subsequent recipe changes are admitted by this profile.
    'candidate': '887a15ccfe26b834c9ae9fccd545b35f27b3b70af79bffd8ae5aaefee529b2f8',
}
ALLOWED = {
    '.github/workflows/refactor-session-replay.yml',
    'recovery/scripts/PrepareCompilerSessionReplay.py',
    'recovery/scripts/TestCompilerSessionCache.ps1',
    'recovery/scripts/TestCompilerSdk.ps1',
    'recovery/scripts/TestCompilerUnicodeReplay.ps1', 'recovery/tests/CompilerUnicodeSettings.cs',
    'recovery/scripts/PublishCompiler.ps1', 'recovery/COMPILER_RELEASE.md',
    'recovery/REFACTOR_RESUME.md', 'recovery/P5_LIFECYCLE_AUDIT.md',
    'recovery/evidence/refactor/core-protocol-recovery/40406049617-1/protocol.json',
    'recovery/evidence/refactor/core-protocol-recovery/40406049617-1/source.json',
}
# The exact failure set must be confirmed against the completed source run.
FAILED_STEPS = {'Targeted real Core, backend and application pipeline in Debug and Release'}
SUCCESS_STEPS = {
    'Standalone P2 application contracts in Debug',
    'Standalone P2 application contracts in Release',
    'Validate CLI scripts and output contracts before long Native linking',
    'Build managed Foundation inputs',
    'Build full production combination and verify deployment and route metadata',
    'Host-bound Texture payload and deployment rejection in Debug and Release',
    'Real production CLI, cold processes and actual CtrlBreak cancellation',
    'Validate real production CLI protocol outputs',
    'Actual build-cache cold hot invalidation and retry matrix',
    'Effective input roots and original BAB output differential',
    'Actual Audio CDATA and W3X build-cache payload replay',
    'Validate actual cache and input-root protocol outputs',
    'Actual session-cache documents intermediate and CDATA reuse matrix',
    'Validate actual session-cache protocol outputs',
    'Run actions/upload-artifact@v4',
}
require = guard.require


def api(endpoint):
    return json.loads(subprocess.run(['gh', 'api', endpoint], check=True, capture_output=True).stdout)


def closure(repo, source, candidate):
    require(source == SOURCE, 'Session replay source must be the fixed production commit')
    for value in (source, candidate):
        require(re.fullmatch('[0-9a-f]{40}', value), 'Use complete lowercase commit SHAs')
        guard.git(repo, 'cat-file', '-e', value + '^{commit}')
    guard.git(repo, 'merge-base', '--is-ancestor', source, candidate)
    guard.git(repo, 'diff', '--exit-code', candidate, '--', 'recovery/src', 'SDK')
    require(not guard.git(repo, 'ls-files', '--others', '--exclude-standard', '-z', '--', 'recovery/src', 'SDK'),
            'Untracked production/SDK input is not covered by the candidate commit')
    # Complete changed-path closure also rejects removed dependencies and build
    # recipes outside src/SDK, rather than assuming their directory names.
    changed = sorted(p for p in guard.git(repo, 'diff', '--name-only', '--no-renames', '-z', source, candidate).decode().split('\0') if p)
    require(all(path in ALLOWED or path == WORKFLOW for path in changed),
            'Production/build input changed: ' + ', '.join(p for p in changed if p not in ALLOWED and p != WORKFLOW))
    guard.git(repo, 'diff', '--exit-code', source, candidate, '--', 'recovery/src', 'SDK')
    if WORKFLOW in changed:
        for ref, key in ((source, 'source'), (candidate, 'candidate')):
            require(guard.digest(guard.git(repo, 'show', ref + ':' + WORKFLOW)) == WORKFLOW_BYTES[key],
                    'Core production workflow changed from its fixed reviewed bytes')
    return changed


def metadata(run, artifact, jobs):
    require(ARTIFACT > 0 and re.fullmatch('[0-9a-f]{64}', SHA256), 'Fixed archive has not yet been pinned')
    require(run.get('id') == RUN and run.get('head_sha') == SOURCE and run.get('run_attempt') == ATTEMPT and
            run.get('status') == 'completed' and run.get('conclusion') == 'failure' and
            run.get('path') == WORKFLOW and run.get('head_branch') == 'refactor/modcompiler-p0-p3', 'Fixed source run identity/terminal mismatch')
    for key in ('repository', 'head_repository'):
        require(run.get(key, {}).get('full_name') == REPOSITORY, 'Replay repository mismatch')
    require(artifact.get('id') == ARTIFACT and artifact.get('name') == NAME and artifact.get('expired') is False and
            artifact.get('digest') == 'sha256:' + SHA256, 'Fixed artifact identity/digest mismatch')
    origin = artifact.get('workflow_run', {})
    require(origin.get('id') == RUN and origin.get('head_sha') == SOURCE and origin.get('head_branch') == 'refactor/modcompiler-p0-p3' and
            origin.get('repository_id') == run['repository']['id'] and origin.get('head_repository_id') == run['head_repository']['id'], 'Fixed artifact origin mismatch')
    rows = jobs.get('jobs', [])
    require(jobs.get('total_count') == 1 and len(rows) == 1 and rows[0].get('run_id') == RUN and
            rows[0].get('status') == 'completed' and rows[0].get('conclusion') == 'failure', 'Incomplete fixed source jobs')
    steps = rows[0].get('steps', [])
    names = [s.get('name') for s in steps]
    require(len(names) == len(set(names)), 'Duplicate source step names')
    require({s['name'] for s in steps if s.get('conclusion') == 'failure'} == FAILED_STEPS,
            'Fixed run failure set differs from the reviewed test-tool failures')
    for name in SUCCESS_STEPS:
        matched = [s for s in steps if s.get('name') == name]
        require(len(matched) == 1 and matched[0].get('status') == 'completed' and matched[0].get('conclusion') == 'success',
                'Required independent production scope failed: ' + name)


def prepare(archive, candidate, output, repo):
    changed = closure(repo, SOURCE, candidate)
    target = Path(output)
    require(not target.exists(), 'Refusing to overwrite replay output')
    payload, files, validation_files, reused = {}, [], [], {}
    def identity(d, attempt=True):
        require(d.get('sourceCommit') == SOURCE and str(d.get('runId')) == str(RUN) and
                (not attempt or str(d.get('runAttempt')) == str(ATTEMPT)), 'Original receipt identity mismatch')
    with open(archive, 'rb') as stream:
        require(hashlib.sha256(stream.read()).hexdigest() == SHA256, 'Fixed archive SHA256 mismatch')
        stream.seek(0)
        with zipfile.ZipFile(stream) as z:
            folded = set()
            for entry in z.infolist():
                name = guard.safe_name(entry.orig_filename)
                require(entry.orig_filename == entry.filename and name.casefold() not in folded and
                        not stat.S_ISLNK(entry.external_attr >> 16) and not entry.flag_bits & 1, 'Unsafe or duplicate ZIP entry')
                folded.add(name.casefold())
            def read(path):
                require(path in z.namelist(), 'Missing original archive path: ' + path)
                return z.read(path)
            def doc(path):
                d = json.loads(read(path).decode('utf-8-sig'))
                require(isinstance(d, dict), 'Receipt is not an object')
                return d
            def take(path, relative, runtime=False):
                data = read(path); payload[relative] = data
                row = {'path':relative, 'sha256':guard.digest(data)}
                (files if runtime else validation_files).append(row)
            def base(section):
                return f'evidence/refactor/{section}/{RUN}-{ATTEMPT}'
            def protocol(section, name='protocol.json'):
                root = base(section); d = doc(root + '/' + name)
                require(d.get('sourceCommit') == SOURCE and d.get('status') == 'passed' and d.get('errorCount') == 0 and
                        d.get('errors') == [], 'Original protocol failed: ' + section)
                require(len(d.get('matrix', [])) == 2 and {r.get('configuration') for r in d['matrix']} == guard.MODES and
                        all(all(isinstance(r.get(k), int) and r[k] > 0 for k in ('reports','eventFiles','events')) for r in d['matrix']),
                        'Original protocol configurations/coverage incomplete')
                require(d.get('validatedFiles'), 'No protocol input hashes')
                for row in d['validatedFiles']:
                    require(guard.digest(read(root + '/' + guard.safe_name(row['path']))) == row['sha256'], 'Original protocol raw bytes changed')
                take(root + '/' + name, 'original-evidence/' + section + '/' + name)
                return d
            receipts = {}
            for section in ('core-lifecycle-deployment','core-lifecycle','core-lifecycle-texture','core-cli','build-cache','input-roots','cache-payloads','session-cache'):
                path = base(section) + '/validation.json'; d = doc(path); identity(d)
                take(path, 'original-evidence/' + section + '/validation.json'); receipts[section] = d
            take(base('core-cli')+'/validation.json','accepted-cli.json')
            # The original overall Core failure gated its protocol step. Keep
            # these raw inputs for a new protocol-only check without rerunning
            # any of the nine successful fixture processes.
            core_prefix = base('core-lifecycle')+'/'
            for name in z.namelist():
                if name.startswith(core_prefix) and name.rsplit('/',1)[-1] in ('build-report.json','build.events.jsonl'):
                    take(name,'original-core-protocol-input/'+guard.safe_name(name[len(core_prefix):]))
            dep, core, texture, cli = [receipts[k] for k in ('core-lifecycle-deployment','core-lifecycle','core-lifecycle-texture','core-cli')]
            require(dep.get('status') == texture.get('status') == 'passed' and cli.get('succeeded') is True, 'Independent deployment/Texture/CLI scope failed')
            dep_modes, tex_modes, cli_modes = guard.modes(dep), guard.modes(texture,'configurations'), guard.modes(cli)
            require(core.get('succeeded') is False and core.get('status') == 'failed', 'Original partial Core terminal changed')
            core_rows = core.get('modes', [])
            require(len(core_rows) == 2 and {r.get('configuration') for r in core_rows} == guard.MODES, 'Partial Core modes incomplete')
            core_modes = {r['configuration']:r for r in core_rows}
            # Historical accepted archives included this fixture; current production no longer has the boundary.
            expected = guard.FIXTURES | {'CompilerCacheContent','CompilerCacheBoundary','CompilerInputRoots'}
            for section in ('build-cache','input-roots','cache-payloads','session-cache'):
                require(receipts[section].get('succeeded') is True, 'Independent scope failed: ' + section)
                guard.modes(receipts[section]); reused[section] = protocol(section)
            protocol('core-cli','cli-protocol.json')
            for mode in sorted(guard.MODES):
                cm = core_modes[mode]; rows = cm.get('fixtures', [])
                require(cm.get('passed') is False and len(rows) == len(expected) and {r.get('name') for r in rows} == expected,
                        'Partial Core fixture inventory mismatch')
                require({r['name'] for r in rows if r.get('passed') is not True} == {'CompilerUnicodeSettings'}, 'Partial Core failure set differs')
                successes = []
                for row in rows:
                    fixture = row['name']; root = base('core-lifecycle') + '/' + mode + '/' + fixture
                    result = doc(root + '/result.json'); identity(result,False)
                    source_bytes = guard.git(repo,'show',SOURCE+':recovery/tests/'+fixture+'.cs')
                    # Windows checkout uses core.autocrlf. Permit only the exact
                    # Git blob or that documented CRLF checkout transformation.
                    source_hashes = {guard.digest(source_bytes), guard.digest(source_bytes.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n'))}
                    require(row.get('sourceSha256') in source_hashes, 'Original fixture source identity differs: ' + fixture)
                    if fixture == 'CompilerUnicodeSettings':
                        require(result.get('succeeded') is False and 'System.NullReferenceException' in result.get('error','') and
                                'AssetDeclarationDocument.LoadXml' in result['error'], 'Original Unicode failure differs from identified fixture setup error')
                    else:
                        require(result.get('succeeded') is True and guard.digest(read(root+'/result.json')) == row.get('resultSha256') and
                                guard.digest(read(root+'/runtime.log')) == row.get('runtimeLogSha256') and
                                guard.digest(read(root+'/fixture.exe')) == row.get('fixtureExeSha256'), 'Successful original Core scope integrity failed')
                        successes.append(fixture)
                    take(root+'/result.json','original-evidence/core-lifecycle/'+mode+'/'+fixture+'/result.json')
                reused[mode] = {'coreSuccessfulFixtures':successes, 'coreFailedFixtures':['CompilerUnicodeSettings']}
                product = f'artifacts/core-lifecycle-product/{mode}/'; build = doc(product+'build.json')
                host = doc(product+'validation/build.json'); recipe = doc(product+'link-inputs.json')
                for d in (build,host,recipe):
                    require(d.get('sourceCommit') == SOURCE and d.get('configuration') == mode, 'Build recipe source/configuration mismatch')
                exe = guard.digest(read(product+'bin/NewModCompiler.exe')); host_exe = guard.digest(read(product+'validation/bin/NewModCompiler.exe'))
                require(build.get('containsProbeOrTestSources') is False and build.get('backendIntegrated') is True and
                        {exe,build.get('exeSha256'),dep_modes[mode].get('exeSha256'),cm.get('productExeSha256'),tex_modes[mode].get('exeSha256'),cli_modes[mode].get('exeSha256')} == {exe},
                        'Product binary/test-isolation identity differs across independent scopes')
                require(host.get('schemaVersion') == 2 and host.get('sameProductionObjects') is True and host.get('productionExeUnchanged') is True and
                        host.get('productExeSha256') == exe and host.get('exeSha256') == host_exe == cm.get('validationHostSha256') and
                        host.get('entryPoint') == 'NewModCompiler.Validation.ValidationEntry.Main' and
                        host.get('linkRecipeSha256') == guard.digest(read(product+'link-inputs.json')), 'Archived validation host identity/recipe mismatch')
                require([(r['kind'],r['path'],r['sha256']) for r in recipe['inputs'] if r['kind']!='entry'] ==
                        [(r['kind'],r['path'],r['sha256']) for r in host['inputs']], 'Validation host differs from exact production object closure')
                runtime = cli_modes[mode].get('runtimeInputs', {})
                for name in guard.RUNTIME:
                    data = read(product+'bin/'+name); sha = guard.digest(data)
                    require([v for k,v in runtime.items() if k.replace('\\','/').lstrip('/') == name] == [sha], 'CLI runtime bytes differ')
                    if name != 'NewModCompiler.exe':
                        require([r.get('sha256') for r in host['runtimeFiles'] if r.get('name')==name] == [sha] and
                                guard.digest(read(product+'validation/bin/'+name)) == sha and
                                [r.get('sha256') for r in cm['runtimeFiles'] if r.get('name')==name] == [sha], 'Shared host/runtime dependency differs')
                    take(product+'bin/'+name,mode+'/bin/'+name,True)
                    take(product+'validation/bin/'+name,mode+'/validation/bin/'+name)
                for name in ('build.json','validation/build.json','link-inputs.json'):
                    take(product+name,mode+'/'+name)
                patch = doc(product+'bin/BinaryAssetBuilder.TextureCompiler.dll.patch.json'); tex = guard.digest(read(product+'bin/BinaryAssetBuilder.TextureCompiler.dll'))
                require(patch.get('targetExeSha256') == exe and patch.get('patchedSha256') == tex and
                        patch.get('originalSha256') == '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e' and
                        patch.get('protectedPeAudit',{}).get('status') == 'passed' and patch['protectedPeAudit'].get('outputSha256') == tex,
                        'Original EXE-bound Texture deployment differs')
                for name in ('BinaryAssetBuilder.TextureCompiler.dll','BinaryAssetBuilder.TextureCompiler.dll.patch.json'):
                    take(product+'bin/'+name,mode+'/bin/'+name)
                cases = cli_modes[mode].get('cases', [])
                wanted = (guard.CLI_CASES - {'cache-rejected','session-cache-rejected'}) | {'compressed-session-cache-rejected'}
                require(wanted <= {r.get('name') for r in cases} and len(cases)==len({r.get('name') for r in cases}) and
                        all(r.get('passed') is True for r in cases), 'Original production CLI cases incomplete')
                for kind in ('business','realUi'):
                    d = cli_modes[mode].get(kind,{}); identity(d,False)
                    require(d.get('succeeded') is True and d.get('configuration') == mode, 'Original CLI business/UI acceptance incomplete')
                for original in ('original-baseline','business-original-baseline','real-ui-original-baseline'):
                    d = doc(base('core-cli')+'/'+mode+'/'+original+'/validation.json'); identity(d)
                    require(d.get('succeeded') is True, 'Original CLI BAB baseline incomplete')
    receipt = {'schemaVersion':1,'prepared':True,'candidateValidated':False,'sourceCommit':SOURCE,'candidateCommit':candidate,
               'binarySourceCommit':SOURCE,'testSourceCommit':candidate,'runId':str(RUN),'runAttempt':str(ATTEMPT),'archiveSha256':SHA256,
               'artifactId':ARTIFACT,'productionSourcesUnchanged':True,'changedPaths':changed,'files':files,'validationFiles':validation_files,
               'sourceRunConclusion':'failure','sourceFailedSteps':sorted(FAILED_STEPS),'reusedScopes':reused,
               'requiredNextSteps':['Validate original archived Core App protocol without rerunning successful fixtures','Run corrected Unicode fixture and fresh backend oracle input in both modes','Run skipped original BAB baseline','Run complete mymod SDK differential','Package and verify P6 runtime'],
               'scope':'Fixed production and validation-host bytes; original nine successful Core groups and complete SC/CLI/cache/Texture evidence reused; original failed Core terminal preserved; no repeated SC or Native relink'}
    target.parent.mkdir(parents=True,exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=target.name+'-',dir=target.parent))
    try:
        for relative,data in payload.items():
            path=temporary/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
        (temporary/'session-replay.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
        temporary.rename(target)
    finally:
        if temporary.exists():shutil.rmtree(temporary)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('repo', 'candidate-commit', 'output', 'archive'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    try:
        closure(args.repo, SOURCE, args.candidate_commit)
        require(ARTIFACT > 0 and re.fullmatch('[0-9a-f]{64}', SHA256), 'Fixed archive has not yet been pinned')
        metadata(api(f'repos/{REPOSITORY}/actions/runs/{RUN}'), api(f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}'),
                 api(f'repos/{REPOSITORY}/actions/runs/{RUN}/jobs?filter=latest&per_page=100'))
        with Path(args.archive).open('xb') as stream:
            subprocess.run(['gh', 'api', f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}/zip'], check=True, stdout=stream, stderr=subprocess.PIPE)
        receipt = prepare(args.archive, args.candidate_commit, args.output, args.repo)
        print(json.dumps({k: receipt[k] for k in ('prepared', 'candidateValidated', 'binarySourceCommit', 'testSourceCommit', 'runId')}))
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        parser.exit(1, 'Fixed session replay rejected: ' + str(error) + '\n')


if __name__ == '__main__':
    main()
