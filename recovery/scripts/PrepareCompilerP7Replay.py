#!/usr/bin/env python3
"""Stage one failed-only-P7 run's independently accepted exact production bytes.

The failed source run remains failed. Candidate P7, protocol and ZIP deployment
must pass in the replay job; preparation never relabels them as accepted.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
import zipfile
import PrepareCompilerCliReuse as guard
import PrepareCompilerRelease as release

SOURCE='5f81f664f4970176d104b1aaa725501beffac410'
RUN=40421719356
ATTEMPT=1
ARTIFACT=12699266545
SHA256='1ecfb87e564d8eb256caa5bf26fe5828942db7b60570d3ee647f8767794099a4'
REPOSITORY='wu162/NewSdk'
WORKFLOW='.github/workflows/refactor-core-lifecycle.yml'
FAILED_STEP='Production BIG apt maps selection and original MakeBig differential'
SKIPPED_STEP='Validate actual BIG production protocol outputs'
ALLOWED={
    'recovery/scripts/PrepareCompilerP7Replay.py',
    '.github/workflows/refactor-p7-replay.yml',
    'recovery/scripts/TestCompilerBig.ps1',
    'recovery/scripts/PrepareCompilerAdditional.py',
    'recovery/scripts/PrepareCompilerNeutral.py',
    'recovery/scripts/ValidateCompilerBig.py',
    'recovery/scripts/ValidateCompilerLoD.py',
    'recovery/preparation/additional/AdditionalControl.cs',
    'recovery/scripts/PrepareCompilerRelease.py',
    'recovery/scripts/PublishCompiler.ps1',
    'recovery/COMPILER_RELEASE.md','recovery/REFACTOR_RESUME.md',
    'recovery/P7_PACKAGING.md','recovery/MAKEBIG_RECOVERY.md',
}
require=guard.require


def closure(repo,source,candidate):
    require(source==SOURCE,'Replay binary source differs from fixed accepted partial run')
    for ref in (source,candidate):
        require(re.fullmatch('[0-9a-f]{40}',ref),'Complete commit SHA required')
        guard.git(repo,'cat-file','-e',ref+'^{commit}')
    guard.git(repo,'merge-base','--is-ancestor',source,candidate)
    changed=sorted(p for p in guard.git(repo,'diff','--name-only','--no-renames','-z',source,candidate).decode().split('\0') if p)
    require(all(p in ALLOWED for p in changed),'Production/dependency/toolchain input changed: '+', '.join(p for p in changed if p not in ALLOWED))
    guard.git(repo,'diff','--exit-code',candidate,'--','recovery/src','SDK','recovery/build','recovery/scripts/TestCompilerBig.ps1','recovery/scripts/PublishCompiler.ps1')
    require(not guard.git(repo,'ls-files','--others','--exclude-standard','-z','--','recovery/src','SDK','recovery/build'),'Untracked production input')
    return changed


def metadata(run,artifact,jobs):
    require(run.get('id')==RUN and run.get('head_sha')==SOURCE and run.get('run_attempt')==ATTEMPT and run.get('path')==WORKFLOW and
            run.get('head_branch')=='refactor/modcompiler-p0-p3' and run.get('status')=='completed' and run.get('conclusion')=='failure','Original partial run identity/failure mismatch')
    require(all(run.get(k,{}).get('full_name')==REPOSITORY for k in ('repository','head_repository')),'Replay repository identity mismatch')
    origin=artifact.get('workflow_run',{})
    require(artifact.get('id')==ARTIFACT and artifact.get('name')==f'refactor-core-lifecycle-{RUN}-{ATTEMPT}' and artifact.get('expired') is False and
            artifact.get('digest')=='sha256:'+SHA256 and origin.get('id')==RUN and origin.get('head_sha')==SOURCE and
            origin.get('repository_id')==run['repository']['id'] and origin.get('head_repository_id')==run['head_repository']['id'],'Original artifact identity/digest mismatch')
    rows=jobs.get('jobs',[])
    require(jobs.get('total_count')==1 and len(rows)==1 and rows[0].get('run_id')==RUN and rows[0].get('status')=='completed' and rows[0].get('conclusion')=='failure','Original partial job identity mismatch')
    steps=rows[0].get('steps',[])
    require(len(steps)==len({s['name'] for s in steps}),'Duplicate original steps')
    require({s['name'] for s in steps if s.get('conclusion')=='failure'}=={FAILED_STEP},'Original failure extends beyond P7')
    require([(s.get('status'),s.get('conclusion')) for s in steps if s['name']==FAILED_STEP]==[('completed','failure')],'Original BIG failure missing')
    require([(s.get('status'),s.get('conclusion')) for s in steps if s['name']==SKIPPED_STEP]==[('completed','skipped')],'Original BIG protocol was not skipped')
    for name in release.SUCCESS_STEPS-{FAILED_STEP,SKIPPED_STEP}:
        require([(s.get('status'),s.get('conclusion')) for s in steps if s['name']==name]==[('completed','success')],'Independent accepted scope failed/skipped: '+name)


def prepare(archive,candidate,output,repo):
    changed=closure(repo,SOURCE,candidate)
    previous=guard.closure,guard.FIXTURES,guard.CLI_CASES
    try:
        guard.closure=lambda *a,**k:changed
        # Historical accepted archives included this fixture; current production no longer has the boundary.
        guard.FIXTURES=previous[1]|{'CompilerCacheContent','CompilerCacheBoundary','CompilerInputRoots'}
        guard.CLI_CASES=(previous[2]-{'cache-rejected','session-cache-rejected'})|{'compressed-session-cache-rejected'}
        receipt=guard.prepare(archive,SHA256,SOURCE,candidate,output,repo)
    finally:
        guard.closure,guard.FIXTURES,guard.CLI_CASES=previous
    target=Path(output)
    with zipfile.ZipFile(archive) as z:
        def doc(path):return json.loads(z.read(path).decode('utf-8-sig'))
        def take(path,relative):
            data=z.read(path);destination=target/relative;destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
            receipt['files'].append({'path':relative,'sha256':guard.digest(data)})
        cli_path=f'evidence/refactor/core-cli/{RUN}-{ATTEMPT}/validation.json';cli=doc(cli_path)
        for section in ('build-cache','input-roots','cache-payloads','session-cache'):
            base=f'evidence/refactor/{section}/{RUN}-{ATTEMPT}';d=doc(base+'/validation.json')
            require(d.get('succeeded') is True and d.get('sourceCommit')==SOURCE and str(d.get('runId'))==str(RUN) and str(d.get('runAttempt'))==str(ATTEMPT),'Independent receipt failed: '+section)
            guard.modes(d);protocol=doc(base+'/protocol.json')
            require(protocol.get('sourceCommit')==SOURCE and protocol.get('status')=='passed' and protocol.get('errorCount')==0 and protocol.get('errors')==[] and
                    len(protocol.get('matrix',[]))==2 and {m.get('configuration') for m in protocol['matrix']}==guard.MODES and protocol.get('validatedFiles'),'Independent protocol incomplete: '+section)
            for row in protocol['validatedFiles']:
                require(guard.digest(z.read(base+'/'+guard.safe_name(row['path'])))==row['sha256'],'Independent protocol raw file mismatch')
            take(base+'/validation.json','acceptance/'+section+'/validation.json');take(base+'/protocol.json','acceptance/'+section+'/protocol.json')
        mode_hashes={}
        for mode in sorted(guard.MODES):
            base=f'artifacts/core-lifecycle-product/{mode}/';accepted=next(m for m in cli['modes'] if m['configuration']==mode)
            require({k.replace('\\','/').lstrip('/') for k in accepted['runtimeInputs']}==set(release.RUNTIME),'Partial accepted CLI runtime inventory differs')
            for name in release.RUNTIME:
                sha=guard.digest(z.read(base+'bin/'+name))
                require([v for k,v in accepted['runtimeInputs'].items() if k.replace('\\','/').lstrip('/')==name]==[sha],'Partial accepted runtime byte mismatch')
                if name not in guard.RUNTIME:take(base+'bin/'+name,mode+'/bin/'+name)
            patch=doc(base+'bin/BinaryAssetBuilder.TextureCompiler.dll.patch.json')
            require(patch.get('targetExeSha256')==accepted['exeSha256'] and patch.get('patchedSha256')==guard.digest(z.read(base+'bin/BinaryAssetBuilder.TextureCompiler.dll')) and patch.get('protectedPeAudit',{}).get('status')=='passed','Partial accepted Texture patch identity mismatch')
            take(base+'build.json',mode+'/build.json');take(base+'link-inputs.json',mode+'/link-inputs.json')
            mode_hashes[mode]=accepted['exeSha256']
        take(cli_path,'accepted-cli.json')
        failed_path=f'evidence/refactor/big/{RUN}-{ATTEMPT}/validation.json';failed=doc(failed_path)
        require(failed.get('succeeded') is False and failed.get('sourceCommit')==SOURCE and str(failed.get('runId'))==str(RUN) and str(failed.get('runAttempt'))==str(ATTEMPT) and 'Original SDK Additional copy helper compilation failed' in failed.get('error',''),'Original P7 failure was not the exact isolated helper compilation failure')
        take(failed_path,'original-failed-big.json')
    receipt.update(binarySourceCommit=SOURCE,testSourceCommit=candidate,artifactId=ARTIFACT,productionSourcesUnchanged=True,modeExeSha256=mode_hashes,
                   requiresBigDeployment=True,testScriptGitSha256=guard.digest(guard.git(repo,'show',candidate+':recovery/scripts/TestCompilerBig.ps1')),
                   testScriptSha256=guard.digest((Path(repo)/'recovery/scripts/TestCompilerBig.ps1').read_bytes()),
                   originalRunConclusion='failure',originalFailedStep=FAILED_STEP,originalBigProtocol='skipped',acceptedIndependentGroups=['App','Core','CLI','Texture','build-cache','input-roots','cache-payloads','session-cache'],
                   requiredNextSteps=['Run PublishCompiler -VerifyBigDeployment; it executes the complete P7 group from extracted ZIPs and validates protocols before publication'],
                   scope='Fixed original failure retained; independently successful exact production runtime/Core/CLI/cache evidence reused; candidate P7 and ZIP remain unaccepted until fresh replay succeeds')
    (target/'p7-replay.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8');(target/'reuse.json').unlink()
    return receipt


def api(endpoint):return json.loads(subprocess.run(['gh','api',endpoint],check=True,capture_output=True).stdout)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('repo','candidate-commit','output','archive'):parser.add_argument('--'+name,required=True)
    args=parser.parse_args()
    try:
        closure(args.repo,SOURCE,args.candidate_commit)
        metadata(api(f'repos/{REPOSITORY}/actions/runs/{RUN}'),api(f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}'),api(f'repos/{REPOSITORY}/actions/runs/{RUN}/jobs?filter=latest&per_page=100'))
        with Path(args.archive).open('xb') as stream:subprocess.run(['gh','api',f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}/zip'],check=True,stdout=stream,stderr=subprocess.PIPE)
        result=prepare(args.archive,args.candidate_commit,args.output,args.repo)
        print(json.dumps({k:result[k] for k in ('prepared','candidateValidated','binarySourceCommit','testSourceCommit','modeExeSha256')}))
    except (ValueError,OSError,KeyError,TypeError,zipfile.BadZipFile,subprocess.CalledProcessError) as error:
        parser.exit(1,'P7 replay rejected: '+str(error)+'\n')


if __name__=='__main__':main()
