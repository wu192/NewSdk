#!/usr/bin/env python3
"""Prepare a fixed fully accepted P7 runtime for P6 ZIP deployment validation.

Business tests are not rerun. The prior complete mymod evidence is explicitly
retained at its f2 binary / 9d test source, rather than relabelled as a P7 run.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile
import tempfile
import ValidateCompilerBig as big_contract
import PrepareCompilerCliReuse as guard

SOURCE = '828fead203f38a4599c98fb9fe93290acc271b96'
RUN = 40415442552
ATTEMPT = 1
ARTIFACT = 12694779350
SHA256 = '12c731a9a460ea57e7e0e18bcab29d03884796ec6874bcb5297a4bada0b66eac'
# Enable only with a fully successful base/Additional/optional-neutral production pin.
REQUIRE_BASE_BIG = False
REPOSITORY = 'wu162/NewSdk'
WORKFLOW = '.github/workflows/refactor-core-lifecycle.yml'
SDK_SOURCE = '9dcaac8212a5d5dfd690af0d1e69f58cc6c3cee6'
SDK_BINARY = 'f2ba141284469411d604d756f7916b0cf6aecf0a'
SDK_RUN = 40409382810
SDK_ARTIFACT = 12692512691
SDK_SHA256 = '128cc5333661ac1b3f793d47402c3c99399331b146985e1f32d5df3ec89922ad'
RUNTIME = guard.RUNTIME + ('BinaryAssetBuilder.TextureCompiler.dll','BinaryAssetBuilder.TextureCompiler.dll.patch.json')
ALLOWED = {
    'recovery/scripts/PrepareCompilerRelease.py', '.github/workflows/refactor-release.yml',
    'recovery/REFACTOR_RESUME.md', 'recovery/P5_LIFECYCLE_AUDIT.md', 'recovery/COMPILER_RELEASE.md',
    'recovery/P7_PACKAGING.md', 'recovery/MAKEBIG_RECOVERY.md', 'recovery/scripts/PublishCompiler.ps1',
    'recovery/scripts/TestCompilerBig.ps1', 'recovery/scripts/ValidateCompilerBig.py',
    'recovery/scripts/ValidateCompilerLoD.py', 'recovery/scripts/PrepareCompilerAdditional.py',
    'recovery/scripts/PrepareCompilerNeutral.py',
    'recovery/scripts/PrepareCompilerSessionReplay.py',
    # Independent preparations are outside the production object/link closure.
    '.github/workflows/refactor-hashfix.yml', '.github/workflows/refactor-lod.yml',
    'recovery/preparation/hashfix/HashFix.cs', 'recovery/preparation/hashfix/HashFixControl.cs',
    'recovery/preparation/hashfix/TestCompilerHashFix.py',
    'recovery/preparation/lod/LoDControl.cs', 'recovery/preparation/lod/LoDStreamBuilder.cs',
    'recovery/preparation/lod/TestCompilerLoD.py',
}
SUCCESS_STEPS = {
    'Standalone P2 application contracts in Debug', 'Standalone P2 application contracts in Release',
    'Validate CLI scripts and output contracts before long Native linking', 'Build managed Foundation inputs',
    'Build full production combination and verify deployment and route metadata',
    'Targeted real Core, backend and application pipeline in Debug and Release',
    'Isolated original BAB baseline for the two targeted backend cases',
    'Validate actual application JSONL and reports against protocol schemas',
    'Host-bound Texture payload and deployment rejection in Debug and Release',
    'Real production CLI, cold processes and actual CtrlBreak cancellation',
    'Validate real production CLI protocol outputs', 'Actual build-cache cold hot invalidation and retry matrix',
    'Effective input roots and original BAB output differential', 'Actual Audio CDATA and W3X build-cache payload replay',
    'Actual session-cache documents intermediate and CDATA reuse matrix', 'Validate actual session-cache protocol outputs',
    'Production BIG apt maps selection and original MakeBig differential', 'Validate actual BIG production protocol outputs',
    'Validate actual cache and input-root protocol outputs', 'Run actions/upload-artifact@v4',
}
require = guard.require


def api(endpoint):
    return json.loads(subprocess.run(['gh','api',endpoint],check=True,capture_output=True).stdout)


def closure(repo, source, candidate):
    require(source == SOURCE, 'Release source differs from the fixed P7 candidate')
    for ref in (source,candidate):
        require(re.fullmatch('[0-9a-f]{40}',ref), 'Complete commit SHA required')
        guard.git(repo,'cat-file','-e',ref+'^{commit}')
    guard.git(repo,'merge-base','--is-ancestor',source,candidate)
    changed = sorted(p for p in guard.git(repo,'diff','--name-only','--no-renames','-z',source,candidate).decode().split('\0') if p)
    require(all(p in ALLOWED for p in changed), 'Production, SDK, dependency or build input changed: '+', '.join(p for p in changed if p not in ALLOWED))
    guard.git(repo,'diff','--exit-code',candidate,'--','recovery/src','SDK')
    require(not guard.git(repo,'ls-files','--others','--exclude-standard','-z','--','recovery/src','SDK'), 'Untracked production input')
    return changed


def metadata(run, artifact, jobs):
    require(ARTIFACT > 0 and re.fullmatch('[0-9a-f]{64}',SHA256), 'P7 archive is not pinned to successful evidence')
    require(run.get('id') == RUN and run.get('head_sha') == SOURCE and run.get('run_attempt') == ATTEMPT and
            run.get('status') == 'completed' and run.get('conclusion') == 'success' and run.get('path') == WORKFLOW and
            run.get('head_branch') == 'refactor/modcompiler-p0-p3', 'P7 run identity/full success mismatch')
    require(all(run.get(k,{}).get('full_name') == REPOSITORY for k in ('repository','head_repository')), 'Release repository mismatch')
    origin=artifact.get('workflow_run',{})
    require(artifact.get('id') == ARTIFACT and artifact.get('name') == f'refactor-core-lifecycle-{RUN}-{ATTEMPT}' and
            artifact.get('expired') is False and artifact.get('digest') == 'sha256:'+SHA256 and origin.get('id') == RUN and
            origin.get('head_sha') == SOURCE and origin.get('repository_id') == run['repository']['id'] and
            origin.get('head_repository_id') == run['head_repository']['id'], 'P7 artifact identity/digest mismatch')
    rows=jobs.get('jobs',[])
    require(jobs.get('total_count') == 1 and len(rows) == 1 and rows[0].get('run_id') == RUN and
            rows[0].get('conclusion') == 'success' and rows[0].get('status') == 'completed', 'P7 job did not fully succeed')
    steps=rows[0].get('steps',[])
    require(len(steps)==len({s['name'] for s in steps}), 'Duplicate P7 steps')
    for name in SUCCESS_STEPS:
        require([(s.get('status'),s.get('conclusion')) for s in steps if s.get('name') == name] == [('completed','success')], 'Required P7 scope failed/skipped: '+name)


def sdk_receipt(archive, repo, candidate):
    guard.git(repo,'merge-base','--is-ancestor',SDK_SOURCE,candidate)
    guard.git(repo,'diff','--exit-code',SDK_BINARY,SOURCE,'--','SDK')
    with open(archive,'rb') as stream:
        require(hashlib.sha256(stream.read()).hexdigest()==SDK_SHA256, 'Accepted SDK archive digest mismatch')
        stream.seek(0)
        with zipfile.ZipFile(stream) as z:
            path=f'evidence/refactor/sdk-mymod/{SDK_RUN}-1/validation.json'
            raw=z.read(path);d=json.loads(raw.decode('utf-8-sig'))
            require(d.get('sourceCommit')==SDK_SOURCE and d.get('binarySourceCommit')==SDK_BINARY and str(d.get('runId'))==str(SDK_RUN) and
                    str(d.get('runAttempt'))=='1' and all(d.get(k) is True for k in ('succeeded','originalBaselineSucceeded','originalBaselinePassed','inputsUnchanged')),
                    'Historical complete mymod receipt identity/acceptance mismatch')
            require(d.get('sdkGitTree')==guard.git(repo,'rev-parse',SOURCE+':SDK').decode().strip() and
                    d.get('modGitTree')==guard.git(repo,'rev-parse',SOURCE+':SDK/Mods/mymod').decode().strip(), 'Historical SDK/MOD inputs changed')
            modes=d.get('modes',[])
            require(len(modes)==2 and {m.get('configuration') for m in modes}==guard.MODES and all(m.get('succeeded') is True for m in modes), 'Historical SDK modes incomplete')
            for mode in modes:
                require(mode.get('comparisons') and all(c.get('outputs') and all(o.get('byteIdentical') is True and o['original']['sha256']==o['product']['sha256'] for o in c['outputs']) for c in mode['comparisons']), 'Historical raw SDK output comparison incomplete')
            base=path.rsplit('/',1)[0];protocol=json.loads(z.read(base+'/protocol.json').decode('utf-8-sig'))
            require(protocol.get('sourceCommit')==SDK_SOURCE and protocol.get('status')=='passed' and protocol.get('errorCount')==0 and
                    len(protocol.get('matrix',[]))==2 and {m.get('configuration') for m in protocol['matrix']}==guard.MODES and protocol.get('validatedFiles'), 'Historical SDK protocol acceptance incomplete')
            for row in protocol['validatedFiles']:
                require(guard.digest(z.read(base+'/'+guard.safe_name(row['path'])))==row['sha256'], 'Historical SDK protocol raw input hash differs')
            return raw, {'sourceCommit':SDK_SOURCE,'binarySourceCommit':SDK_BINARY,'runId':SDK_RUN,'artifactId':SDK_ARTIFACT,'archiveSha256':SDK_SHA256,
                         'currentBinaryRetested':False,'scope':d['scope'],'emptyOptionalGroups':d.get('emptyOptionalGroups'),
                         'sdkInputsUnchanged':True,'reason':'P7 only adds explicit BIG apt/maps; current affected Core/CLI/cache/Texture/BIG groups must pass; historical full-mymod binary identity is retained'}


def apt_marker_receipt(archive):
    """Inspect accepted raw product/original BIGs, rather than trusting a success flag."""
    prefix=f'evidence/refactor/big/{RUN}-{ATTEMPT}'
    checked=[]
    with tempfile.TemporaryDirectory(prefix='release-big-marker-') as work:
        target=Path(work)/'accepted.big'
        for mode in sorted(guard.MODES):
            for selection in ('apt', 'apt-maps'):
                for relative in (f'{selection}-big/artifacts/business_1.0.big',
                                 f'{selection}-original-makebig/original.big'):
                    path=f'{prefix}/{mode}/{relative}'
                    data=archive.read(path);target.write_bytes(data)
                    parsed=big_contract.read_archive(target)
                    versions=[m for m in parsed['members'] if m['name'].endswith('.version')]
                    require(len(versions)==2 and all(m['name'].startswith('data\\aptui\\') and
                            m['data']==b' \r\n' for m in versions),
                            'Accepted BIG does not contain exact original space+CRLF APT markers: '+path)
                    checked.append({'path':path,'sha256':guard.digest(data),
                                    'members':[m['name'] for m in versions]})
    return {'expectedBytes':[32,13,10],'rawProductAndOriginalArchives':checked}


def base_big_receipt(archive, cli, repo):
    """Require raw base/combined archives and their original LoD evidence."""
    prefix=f'evidence/refactor/big/{RUN}-{ATTEMPT}'
    validation=json.loads(archive.read(prefix+'/validation.json').decode('utf-8-sig'))
    guard.modes(validation)
    checked=[]
    original_lod=guard.digest((Path(repo)/'SDK/tools/LoDStreamBuilder.exe').read_bytes())
    original_makebig=guard.digest((Path(repo)/'SDK/tools/MakeBig.exe').read_bytes())
    lod_names={f'data\\mod{suffix}{extension}' for suffix in ('_l','_m')
               for extension in ('.manifest','.bin','.imp','.relo')}
    with tempfile.TemporaryDirectory(prefix='release-base-big-') as work:
        path=Path(work)/'accepted.big'
        for mode in sorted(guard.MODES):
            row=next(m for m in validation['modes'] if m['configuration']==mode)
            accepted=next(m for m in cli['modes'] if m['configuration']==mode)
            require(row.get('exeSha256')==accepted.get('exeSha256'), 'Base BIG used another production EXE')
            for selection in ('base','base-apt-maps'):
                for case in (selection+'-loose',selection+'-big'):
                    require([c.get('passed') for c in row['cases'] if c.get('name')==case]==[True],
                            'Required base BIG case failed/missing: '+case)
                comparison_path=f'{prefix}/{mode}/{selection}-original-makebig'
                comparison=json.loads(archive.read(comparison_path+'/comparison.json').decode('utf-8-sig'))
                expected=json.loads(archive.read(comparison_path+'/expected.json').decode('utf-8-sig'))
                accepted_comparison=[c for c in row['comparisons'] if c['name']==selection+'-original-makebig']
                require(len(accepted_comparison)==1 and accepted_comparison[0]['originalExeSha256']==original_makebig, 'Base BIG used another MakeBig oracle')
                require(comparison.get('succeeded') is True and comparison.get('exactMemberNamesAndRawContents') is True,
                        'Base BIG original comparison did not succeed')
                parsed=[]
                for name,kind in ((f'{prefix}/{mode}/{selection}-big/artifacts/business_1.0.big','product'),
                                  (comparison_path+'/original.big','originalMakeBig')):
                    data=archive.read(name);path.write_bytes(data)
                    actual=big_contract.read_archive(path)
                    require(actual['sha256']==comparison[kind]['sha256'], 'Base BIG comparison archive identity differs')
                    members={m['name']:m for m in actual['members']}
                    require(len(expected)==len(members) and {e['name'] for e in expected}==set(members), 'Base BIG inventory differs')
                    require(all(members[e['name']]['bytes']==e['bytes'] and members[e['name']]['sha256']==e['sha256'] for e in expected),
                            'Base BIG raw member differs from accepted original oracle')
                    require(lod_names <= set(members), 'Base BIG omitted original low/medium outputs')
                    parsed.append(members)
                require(all(parsed[0][name]['data']==parsed[1][name]['data'] for name in parsed[0]),
                        'Product/original base BIG raw contents differ')
                # An exit-zero legacy process alone is insufficient. Its fresh
                # eight output files, complete inventory and main inputs are retained.
                loose_prefix=f'{prefix}/{mode}/{selection}-loose/'
                inventories=[n for n in archive.namelist() if n.startswith(loose_prefix+'original-lod-') and n.endswith('/inventory.json')]
                require(inventories, 'Original LoD raw evidence missing')
                for inventory_path in inventories:
                    directory=inventory_path.rsplit('/',1)[0]
                    inventory=json.loads(archive.read(inventory_path).decode('utf-8-sig'))
                    receipt=json.loads(archive.read(directory+'/validation.json').decode('utf-8-sig'))
                    require(receipt.get('originalExeSha256')==original_lod and guard.digest(archive.read(directory+'/tools/LoDStreamBuilder.exe'))==original_lod, 'Original LoD oracle executable differs')
                    require(inventory.get('succeeded') is True and receipt.get('succeeded') is True and receipt.get('processExitCode')==0 and
                            len(inventory.get('files',[]))==8 and {f'data\\'+e['name'].lower() for e in inventory['files']}==lod_names,
                            'Original LoD eight-file acceptance incomplete')
                    require(len(receipt.get('inputs',[]))==4 and {e['name'] for e in receipt['inputs']}=={'mod'+extension for extension in ('.manifest','.bin','.imp','.relo')}, 'Original LoD main input inventory incomplete')
                    for entry in receipt['inputs']:
                        raw=archive.read(directory+'/stream/'+entry['name'])
                        require(len(raw)==entry['bytes'] and guard.digest(raw)==entry['sha256'] and raw==parsed[0]['data\\'+entry['name'].lower()]['data'], 'Original LoD current main input differs from packaged main stream')
                    for entry in inventory['files']:
                        raw=archive.read(directory+'/stream/'+entry['name'])
                        require(len(raw)==entry['bytes'] and guard.digest(raw)==entry['sha256'] and
                                raw==parsed[0]['data\\'+entry['name'].lower()]['data'], 'Original LoD raw output differs from packaged member')
                checked.append({'configuration':mode,'selection':selection,'productSha256':comparison['product']['sha256'],
                                'originalMakeBigSha256':comparison['originalMakeBig']['sha256'],'originalLoDInventories':inventories})
    return {'succeeded':True,'rawBaseAndCombinedArchives':checked,'requiredZipDeploymentGroups':['base','apt','maps']}


def additional_big_receipt(archive, cli, repo):
    """Bind explicit Additional execution to the original CopyFolders/MakeBig bytes."""
    prefix=f'evidence/refactor/big/{RUN}-{ATTEMPT}'
    validation=json.loads(archive.read(prefix+'/validation.json').decode('utf-8-sig'))
    guard.modes(validation)
    script_sha=guard.digest((Path(repo)/'SDK/defaultscript.cs').read_bytes())
    source_sha=guard.digest((Path(repo)/'recovery/preparation/additional/AdditionalControl.cs').read_bytes())
    control_sha=guard.digest(archive.read(prefix+'/AdditionalControl.exe'))
    makebig_sha=guard.digest((Path(repo)/'SDK/tools/MakeBig.exe').read_bytes())
    checked=[]
    with tempfile.TemporaryDirectory(prefix='release-additional-big-') as work:
        path=Path(work)/'accepted.big'
        for mode in sorted(guard.MODES):
            row=next(m for m in validation['modes'] if m['configuration']==mode)
            accepted=next(m for m in cli['modes'] if m['configuration']==mode)
            require(row.get('exeSha256')==accepted.get('exeSha256'), 'Additional BIG used another production EXE')
            case=[c for c in row['cases'] if c.get('name')=='all-with-additional']
            require(len(case)==1 and case[0].get('passed') is True, 'Explicit Additional CLI group failed/missing')
            arguments=case[0]['arguments']
            require('--include-additional=true' in arguments, 'Additional CLI was not explicitly enabled')
            comparison_path=f'{prefix}/{mode}/additional-original-copy-makebig'
            comparison=json.loads(archive.read(comparison_path+'/comparison.json').decode('utf-8-sig'))
            expected=json.loads(archive.read(comparison_path+'/expected.json').decode('utf-8-sig'))
            accepted_comparison=[c for c in row['comparisons'] if c['name']=='additional-original-copy-makebig']
            require(len(accepted_comparison)==1 and accepted_comparison[0]['originalExeSha256']==makebig_sha and
                    comparison.get('succeeded') is True and comparison.get('exactMemberNamesAndRawContents') is True,
                    'Additional original MakeBig comparison incomplete')
            oracle_path=comparison_path+'/additional-original'
            oracle=json.loads(archive.read(oracle_path+'/validation.json').decode('utf-8-sig'))
            inventory=json.loads(archive.read(oracle_path+'/inventory.json').decode('utf-8-sig'))
            require(oracle.get('succeeded') is True and oracle.get('processExitCode')==0 and inventory.get('succeeded') is True and
                    oracle.get('scriptSha256')==script_sha and oracle.get('controlSourceSha256')==source_sha and oracle.get('controlSha256')==control_sha,
                    'Exact original CopyFolders execution/source identity mismatch')
            require({i['root'] for i in oracle.get('inputs',[])}=={'sdk','mod'}, 'Additional oracle omitted SDK or MOD overlay')
            raw_members={}
            for name in archive.namelist():
                if name.startswith(oracle_path+'/input/') and not name.endswith('/'):
                    relative=name[len(oracle_path+'/input/'):].replace('/','\\').lower()
                    if not relative.endswith('.asset'):
                        require(relative not in raw_members, 'Case-ambiguous original Additional member')
                        raw_members[relative]=archive.read(name)
            names={e['name'] for e in expected}
            require(len(expected)==len(names) and names==set(raw_members) and names=={e['name'] for e in inventory['files']} and
                    all(not n.endswith('.asset') for n in names), 'Additional original raw inventory incomplete')
            for entry in expected:
                raw=raw_members[entry['name']]
                require(len(raw)==entry['bytes'] and guard.digest(raw)==entry['sha256'], 'Original Additional raw member differs from accepted inventory')
            for name,kind in ((f'{prefix}/{mode}/all-with-additional/artifacts/business_1.0.big','product'),
                              (comparison_path+'/original.big','originalMakeBig')):
                data=archive.read(name);path.write_bytes(data)
                actual=big_contract.read_archive(path)
                require(actual['sha256']==comparison[kind]['sha256'] and {m['name'] for m in actual['members']}==names and
                        all(m['data']==raw_members[m['name']] for m in actual['members']), 'Additional product/original BIG raw inventory/content differs')
            checked.append({'configuration':mode,'productSha256':comparison['product']['sha256'],
                            'originalMakeBigSha256':comparison['originalMakeBig']['sha256'],'originalCopyFoldersReceipt':oracle_path+'/validation.json'})
    return {'succeeded':True,'explicitAdditional':True,'rawProductCopyFoldersAndMakeBig':checked}


def neutral_big_receipt(archive, cli, repo):
    """Require an actual opt-in hash repair and the original three-stage raw oracle."""
    prefix=f'evidence/refactor/big/{RUN}-{ATTEMPT}'
    validation=json.loads(archive.read(prefix+'/validation.json').decode('utf-8-sig'))
    guard.modes(validation)
    tools=('HashFix.exe','ModAssetResolver.exe','LoDStreamBuilder.exe',
           'ModAssetResolver_TypeHashes.txt','ModAssetResolver_InstanceHashes.txt')
    tool_hashes={name:guard.digest((Path(repo)/'SDK/tools'/name).read_bytes()) for name in tools}
    makebig_sha=guard.digest((Path(repo)/'SDK/tools/MakeBig.exe').read_bytes())
    checked=[]
    with tempfile.TemporaryDirectory(prefix='release-neutral-big-') as work:
        path=Path(work)/'accepted.big'
        for mode in sorted(guard.MODES):
            row=next(m for m in validation['modes'] if m['configuration']==mode)
            accepted=next(m for m in cli['modes'] if m['configuration']==mode)
            require(row.get('exeSha256')==accepted.get('exeSha256'), 'Neutral repair used another production EXE')
            for name in ('base-with-neutral-repair','neutral-missing-reference','neutral-retry-after-reference-restored'):
                cases=[c for c in row['cases'] if c.get('name')==name]
                require(len(cases)==1 and cases[0].get('passed') is True, 'Required optional neutral case failed/missing: '+name)
                arguments=cases[0]['arguments']
                require('--fix-neutral-assets=true' in arguments, 'Neutral repair was not explicitly enabled')
            comparison_path=f'{prefix}/{mode}/neutral-original-hashfix-resolver-lod-makebig'
            comparison=json.loads(archive.read(comparison_path+'/comparison.json').decode('utf-8-sig'))
            expected=json.loads(archive.read(comparison_path+'/expected.json').decode('utf-8-sig'))
            accepted_comparison=[c for c in row['comparisons'] if c['name']=='neutral-original-hashfix-resolver-lod-makebig']
            require(len(accepted_comparison)==1 and accepted_comparison[0]['originalExeSha256']==makebig_sha and
                    comparison.get('succeeded') is True and comparison.get('exactMemberNamesAndRawContents') is True, 'Neutral original MakeBig comparison incomplete')
            oracle_path=comparison_path+'/neutral-original'
            oracle=json.loads(archive.read(oracle_path+'/validation.json').decode('utf-8-sig'))
            inventory=json.loads(archive.read(oracle_path+'/inventory.json').decode('utf-8-sig'))
            require(oracle.get('succeeded') is True and inventory.get('succeeded') is True and
                    oracle.get('hashFixChanged') is True and oracle.get('hashFixChangedCount',0)>0 and
                    inventory.get('hashFixChanged') is True and inventory.get('hashFixChangedCount')==oracle['hashFixChangedCount'] and
                    oracle.get('resolverInsertedAssets')==0 and inventory.get('resolverInsertedAssets')==0 and oracle.get('originalTools')==tool_hashes,
                    'Neutral chain lacks actual hash repair or exceeds accepted no-insertion scope')
            for name,sha in tool_hashes.items():
                require(guard.digest(archive.read(oracle_path+'/SDK/tools/'+name))==sha, 'Neutral original tool bytes differ')
            references=oracle.get('referenceInputs',{})
            require(references, 'Neutral original reference inventory missing')
            for name,entry in references.items():
                data=archive.read(oracle_path+'/SDK/BuiltMods/'+guard.safe_name(name))
                require(len(data)==entry['bytes'] and guard.digest(data)==entry['sha256'], 'Neutral oracle reference inputs changed')
            original={entry['name']:entry for entry in inventory['files']}
            require(len(original)==len(inventory['files'])==len(expected) and {e['name'] for e in expected}==set(original), 'Neutral raw oracle inventory incomplete')
            raw={}
            for name,entry in original.items():
                require(name.startswith('data\\'), 'Neutral oracle returned a non-data member')
                data=archive.read(oracle_path+'/stream/'+guard.safe_name(name[5:].replace('\\','/')))
                require(len(data)==entry['bytes'] and guard.digest(data)==entry['sha256'], 'Neutral original output raw bytes differ')
                raw[name]=data
            for name,kind in ((f'{prefix}/{mode}/base-with-neutral-repair/artifacts/business_1.0.big','product'),
                              (comparison_path+'/original.big','originalMakeBig')):
                data=archive.read(name);path.write_bytes(data);actual=big_contract.read_archive(path)
                require(actual['sha256']==comparison[kind]['sha256'] and {m['name'] for m in actual['members']}==set(raw) and
                        all(m['data']==raw[m['name']] for m in actual['members']), 'Neutral product/original BIG raw contents differ')
            checked.append({'configuration':mode,'productSha256':comparison['product']['sha256'],'hashFixChangedCount':oracle['hashFixChangedCount'],
                            'resolverInsertedAssets':0,'originalChainReceipt':oracle_path+'/validation.json'})
    return {'succeeded':True,'explicitNeutralRepair':True,'scope':'Original HashFix -> ModAssetResolver -> LoD, actual hash correction and no resolver insertion',
            'rawProductOriginalChainAndMakeBig':checked}


def prepare(archive, sdk_archive, candidate, output, repo):
    changed=closure(repo,SOURCE,candidate)
    sdk_raw,sdk_scope=sdk_receipt(sdk_archive,repo,candidate)
    # Reuse the complete existing archive/path/integrity and full Core/CLI guard.
    previous=guard.closure,guard.FIXTURES,guard.CLI_CASES
    try:
        guard.closure=lambda *a,**k: changed
        # Historical accepted archives included this fixture; current production no longer has the boundary.
        guard.FIXTURES=previous[1]|{'CompilerCacheContent','CompilerCacheBoundary','CompilerInputRoots'}
        guard.CLI_CASES=(previous[2]-{'cache-rejected','session-cache-rejected'})|{'compressed-session-cache-rejected'}
        receipt=guard.prepare(archive,SHA256,SOURCE,candidate,output,repo)
    finally:
        guard.closure,guard.FIXTURES,guard.CLI_CASES=previous
    target=Path(output)
    with zipfile.ZipFile(archive) as z:
        marker=apt_marker_receipt(z)
        def doc(path):return json.loads(z.read(path).decode('utf-8-sig'))
        def take(path,relative):
            data=z.read(path);p=target/relative;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
            receipt['files'].append({'path':relative,'sha256':guard.digest(data)})
        cli_path=f'evidence/refactor/core-cli/{RUN}-{ATTEMPT}/validation.json';cli=doc(cli_path)
        base_big=base_big_receipt(z,cli,repo) if REQUIRE_BASE_BIG else None
        additional_big=additional_big_receipt(z,cli,repo) if REQUIRE_BASE_BIG else None
        neutral_big=neutral_big_receipt(z,cli,repo) if REQUIRE_BASE_BIG else None
        for section in ('build-cache','input-roots','cache-payloads','session-cache','big'):
            base=f'evidence/refactor/{section}/{RUN}-{ATTEMPT}';d=doc(base+'/validation.json')
            require(d.get('succeeded') is True and d.get('sourceCommit')==SOURCE and str(d.get('runId'))==str(RUN) and str(d.get('runAttempt'))==str(ATTEMPT), 'P7 affected receipt failed: '+section)
            guard.modes(d);protocol=doc(base+'/protocol.json')
            require(protocol.get('sourceCommit')==SOURCE and protocol.get('status')=='passed' and protocol.get('errorCount')==0 and protocol.get('errors')==[] and
                    len(protocol.get('matrix',[]))==2 and {m.get('configuration') for m in protocol['matrix']}==guard.MODES and protocol.get('validatedFiles'), 'P7 affected protocol failed/incomplete')
            for row in protocol['validatedFiles']:
                require(guard.digest(z.read(base+'/'+guard.safe_name(row['path'])))==row['sha256'], 'P7 affected protocol input hash differs')
            take(base+'/validation.json','acceptance/'+section+'/validation.json');take(base+'/protocol.json','acceptance/'+section+'/protocol.json')
        for mode in sorted(guard.MODES):
            base=f'artifacts/core-lifecycle-product/{mode}/';cm=[m for m in cli['modes'] if m['configuration']==mode][0]
            require({k.replace('\\','/').lstrip('/') for k in cm['runtimeInputs']}==set(RUNTIME), 'P7 CLI runtime contains extra/missing files')
            for name in RUNTIME:
                sha=guard.digest(z.read(base+'bin/'+name))
                require([v for k,v in cm['runtimeInputs'].items() if k.replace('\\','/').lstrip('/')==name]==[sha], 'P7 runtime does not match accepted CLI bytes')
                if name not in guard.RUNTIME:take(base+'bin/'+name,mode+'/bin/'+name)
            patch=doc(base+'bin/BinaryAssetBuilder.TextureCompiler.dll.patch.json')
            require(patch.get('targetExeSha256')==cm['exeSha256'] and patch.get('patchedSha256')==guard.digest(z.read(base+'bin/BinaryAssetBuilder.TextureCompiler.dll')) and
                    patch.get('protectedPeAudit',{}).get('status')=='passed', 'P7 Texture deployment audit mismatch')
            take(base+'build.json',mode+'/build.json');take(base+'link-inputs.json',mode+'/link-inputs.json')
        take(cli_path,'accepted-cli.json')
    (target/'accepted-mymod.json').write_bytes(sdk_raw)
    receipt.update(binarySourceCommit=SOURCE,testSourceCommit=candidate,artifactId=ARTIFACT,productionSourcesUnchanged=True,
                   aptVersionMarker=marker,baseBigAcceptance=base_big,additionalBigAcceptance=additional_big,neutralBigAcceptance=neutral_big,reusedMymod=sdk_scope,
                   requiredNextSteps=['Run P6 five-file ZIP deployment validation'+(' with -VerifyBigDeployment' if REQUIRE_BASE_BIG else ' only')],
                   scope='Current P7 production/cache/session/BIG acceptance plus historical f2/9d complete mymod scope; packaging preparation only, no new business execution')
    (target/'release-preparation.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8');(target/'reuse.json').unlink()
    return receipt


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for n in ('repo','candidate-commit','output','archive','sdk-archive'):parser.add_argument('--'+n,required=True)
    a=parser.parse_args()
    try:
        closure(a.repo,SOURCE,a.candidate_commit)
        require(ARTIFACT>0 and re.fullmatch('[0-9a-f]{64}',SHA256), 'P7 archive is not pinned to successful evidence')
        metadata(api(f'repos/{REPOSITORY}/actions/runs/{RUN}'),api(f'repos/{REPOSITORY}/actions/artifacts/{ARTIFACT}'),api(f'repos/{REPOSITORY}/actions/runs/{RUN}/jobs?filter=latest&per_page=100'))
        old=api(f'repos/{REPOSITORY}/actions/artifacts/{SDK_ARTIFACT}')
        old_run=api(f'repos/{REPOSITORY}/actions/runs/{SDK_RUN}')
        require(old_run.get('head_sha')==SDK_SOURCE and old_run.get('run_attempt')==1 and old_run.get('status')=='completed' and
                old_run.get('conclusion')=='success' and old_run.get('path')=='.github/workflows/refactor-session-replay.yml', 'Historical SDK run is not the accepted successful source')
        require(old.get('id')==SDK_ARTIFACT and old.get('expired') is False and old.get('digest')=='sha256:'+SDK_SHA256 and
                old.get('workflow_run',{}).get('id')==SDK_RUN and old['workflow_run'].get('head_sha')==SDK_SOURCE,'Historical SDK artifact origin/digest mismatch')
        for file,artifact in ((a.archive,ARTIFACT),(a.sdk_archive,SDK_ARTIFACT)):
            with Path(file).open('xb') as stream:subprocess.run(['gh','api',f'repos/{REPOSITORY}/actions/artifacts/{artifact}/zip'],check=True,stdout=stream,stderr=subprocess.PIPE)
        r=prepare(a.archive,a.sdk_archive,a.candidate_commit,a.output,a.repo)
        print(json.dumps({k:r[k] for k in ('prepared','candidateValidated','binarySourceCommit','testSourceCommit','runId')}))
    except (ValueError,OSError,KeyError,TypeError,zipfile.BadZipFile,subprocess.CalledProcessError) as error:
        parser.exit(1,'Release preparation rejected: '+str(error)+'\n')


if __name__=='__main__':main()
