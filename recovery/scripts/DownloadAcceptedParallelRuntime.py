#!/usr/bin/env python3
"""Reuse an actual accepted Release runtime for measurement-only script fixes."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

PERFORMANCE_STEP = 'One full Upheaval jobs 1 4 auto cold hot and modified A/B after acceptance'
REQUIRED = {
    'Standalone P2 application contracts in Debug', 'Standalone P2 application contracts in Release',
    'Build full production combination and verify deployment and route metadata',
    'Targeted real Core, backend and application pipeline in Debug and Release',
    'Full certified backend production cache and jobs matrix',
    'Isolated original BAB baseline for the two targeted backend cases',
    'Host-bound Texture payload and operation without deployment manifest in Debug and Release',
    'Real production CLI, cold processes and actual CtrlBreak cancellation',
    'Actual build-cache cold hot invalidation and retry matrix',
    'Effective input roots and original BAB output differential',
    'Actual Audio CDATA and W3X build-cache payload replay',
    'Actual session-cache documents intermediate and CDATA reuse matrix',
    'Production BIG apt maps selection and original MakeBig differential',
    'Complete mymod original BAB and production SDK differential',
    'Publish final five-file packages after complete production acceptance',
}


def need(value, message):
    if not value:
        raise AssertionError(message)


def api(path):
    return json.loads(subprocess.check_output(['gh', 'api', 'repos/wu162/NewSdk/'+path]))


def sha(data):
    return hashlib.sha256(data).hexdigest()


def run(args):
    repo = Path(args.repo).resolve()
    out = Path(args.output).resolve()
    archive = Path(args.archive).resolve()
    need(not out.exists(), 'Refusing to overwrite accepted runtime')
    run = api('actions/runs/'+str(args.run))
    need(run['status'] == 'completed' and run['conclusion'] in ('success', 'failure') and run['head_branch'] == 'recovery/legacy-sdk-cache' and
         run['repository']['full_name'] == run['head_repository']['full_name'] == 'wu162/NewSdk' and
         run['path'] == '.github/workflows/refactor-core-lifecycle.yml', 'Runtime run origin/status mismatch')
    jobs = api('actions/runs/'+str(args.run)+'/jobs?per_page=100')['jobs']
    need(len(jobs) == 1, 'Expected exactly the complete lifecycle job')
    steps = {step['name']:step['conclusion'] for step in jobs[0]['steps']}
    need(all(steps.get(name) == 'success' for name in REQUIRED), 'A required production acceptance scope did not pass')
    failures = {name for name, result in steps.items() if result == 'failure'}
    need(failures <= {PERFORMANCE_STEP}, 'Failure outside the independent measurement step')
    source = run['head_sha']
    subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor', source, 'HEAD'], check=True)
    subprocess.run(['git', '-C', str(repo), 'diff', '--exit-code', source, 'HEAD', '--',
                    'recovery/src', 'SDK', 'recovery/build', 'recovery/probes',
                    'recovery/scripts/Build*', 'recovery/scripts/PrepareLegacyTexture.ps1'], check=True)
    candidate = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
    artifacts = api('actions/runs/'+str(args.run)+'/artifacts?per_page=100')['artifacts']
    expected = 'refactor-core-lifecycle-%d-%d' % (args.run, run['run_attempt'])
    artifacts = [a for a in artifacts if a['name'] == expected and not a['expired']]
    need(len(artifacts) == 1, 'Accepted runtime archive missing/ambiguous')
    artifact = artifacts[0]
    archive.parent.mkdir(parents=True, exist_ok=True)
    with archive.open('wb') as stream:
        subprocess.run(['gh', 'api', 'repos/wu162/NewSdk/actions/artifacts/'+str(artifact['id'])+'/zip'], stdout=stream, check=True)
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    need(artifact['digest'] == 'sha256:'+digest, 'Runtime archive digest mismatch')
    out.mkdir(parents=True)
    with zipfile.ZipFile(archive) as data:
        candidates = [n for n in data.namelist() if n.endswith('artifacts/core-lifecycle-product/Release/build.json')]
        need(len(candidates) == 1, 'Release production build receipt missing/ambiguous')
        receipt_path = candidates[0]
        build = json.loads(data.read(receipt_path))
        need(build['sourceCommit'] == source and build['configuration'] == 'Release' and build['backendIntegrated'], 'Runtime build source mismatch')
        prefix = receipt_path.rsplit('/',1)[0]+'/bin/'
        names = ['NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll',
                 'BinaryAssetBuilder.TextureCompiler.dll', 'BinaryAssetBuilder.TextureCompiler.dll.patch.json']
        need(set(build['runtimeFiles']) == set(names), 'Unexpected runtime deployment set')
        runtime = {}
        for name in names:
            payload = data.read(prefix+name)
            (out/name).write_bytes(payload)
            runtime[name] = sha(payload)
        need(runtime['NewModCompiler.exe'] == build['exeSha256'], 'Production executable digest mismatch')
        patch = json.loads((out/names[-1]).read_text(encoding='utf-8-sig'))
        need(patch['targetExeSha256'] == runtime['NewModCompiler.exe'] and
             patch['patchedSha256'] == runtime['BinaryAssetBuilder.TextureCompiler.dll'], 'Host-bound texture identity mismatch')
        (out/'product-build.json').write_bytes(data.read(receipt_path))
    result = {'succeeded':True,'binarySourceCommit':source,'testSourceCommit':candidate,'sourceRunId':args.run,
              'sourceRunConclusion':run['conclusion'],'sourceArtifactId':artifact['id'],'sourceArchiveSha256':digest,
              'productionInputsUnchanged':True,'requiredSuccessfulSteps':sorted(REQUIRED),'runtimeSha256':runtime,
              'scope':'Accepted identical Release bytes, no source/native build; only measurement/test/controller/docs may differ.'}
    (out/'reuse.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k not in ('requiredSuccessfulSteps','runtimeSha256')}))
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=int, required=True)
    for key in ('repo','output','archive'):
        parser.add_argument('--'+key, required=True)
    raise SystemExit(run(parser.parse_args()))
