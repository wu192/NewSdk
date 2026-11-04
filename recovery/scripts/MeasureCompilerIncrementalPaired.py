#!/usr/bin/env python3
"""Bounded same-VM diagnostics for representative edits after warm caches."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import time

from BenchmarkCompilerCache import inventory, process_metrics
from CompilerCachePaths import cache_root
from HotCacheWorkload import allocate_id, instance_hash
from MeasureCompilerCachePaired import git, need, product

# Last accepted production, not the later documentation-only branch head.
BASELINE_HEAD = BASELINE_BINARY_SOURCE = "e2dba3c321504752438b4e940d651bbfcdb58fff"
BASELINE_RUN = "41169755141"
BASELINE_FILES = {
    "NewModCompiler.exe": "5ad10b77e75a45f860e37c5e19eb30f2296fe6a0b79c7ece2bf634cf1f3a8b43",
    "NewModCompiler.exe.config": "94eedb0cd0150b90c8115aaee53f64ab0523b39bd67887991b1b62679a752e68",
    "antlr.runtime.dll": "febdc86d6299a550c6ec87050b0cbf5205e821851e75494c699c17ce8ba2c62a",
    "BinaryAssetBuilder.TextureCompiler.dll": "076509e81077fcd916ef884949caa4d9c1817366ffd5291e49bab44dfd7eae48",
    "BinaryAssetBuilder.TextureCompiler.dll.patch.json": "b233da311f085399d88ea1ec7b52315247559dfd1e7bb48713320356c9340b06",
}
from MeasureCompilerModScale import phases, write
from TestCompilerHotCache import declaration, published, same_stamp_edit


def prepare(work):
    mod = work / 'Incremental'
    data = mod / 'Data'
    data.mkdir(parents=True)
    occupied = {instance_hash(name):name for name in ('IncrementalBase','IncrementalDerived','IncrementalSibling')}
    includes = []
    def armor(name, default='72.5%'):
        return '<ArmorTemplate id="%s" Default="%s"/>' % (allocate_id(name, occupied), default)
    for doc in range(32):
        name = 'armor-%02d.xml' % doc
        (data / name).write_text(declaration(''.join(armor('Incremental_%02d_%d' % (doc, i)) for i in range(8))), encoding='utf-8')
        includes.append('<Include type="all" source="%s"/>' % name)
    (data / 'large.xml').write_text(declaration(''.join(armor('IncrementalLarge_%03d' % i) for i in range(256))), encoding='utf-8')
    (data / 'defines.xml').write_text(declaration('<Defines><Define name="INCREMENTAL_SCALE" value="83.25"/><Define name="UNUSED_SCALE" value="95.25"/></Defines>'), encoding='utf-8')
    (data / 'base.xml').write_text(declaration(
        '<Includes><Include type="all" source="defines.xml"/></Includes>'
        '<ArmorTemplate id="IncrementalBase" Default="72.5%" DamageScalar="=$INCREMENTAL_SCALE"/>'
        '<ArmorTemplate id="IncrementalSibling" Default="64.5%"/>'), encoding='utf-8')
    (data / 'derived.xml').write_text(declaration(
        '<Includes><Include type="instance" source="base.xml"/></Includes>'
        '<ArmorTemplate id="IncrementalDerived" inheritFrom="IncrementalBase" DamageScalar="=$INCREMENTAL_SCALE"/>'), encoding='utf-8')
    includes += ['<Include type="all" source="large.xml"/>', '<Include type="all" source="derived.xml"/>']
    (data / 'Mod.xml').write_text(declaration('<Includes>'+''.join(includes)+'</Includes>'), encoding='utf-8')
    return mod


def summarize(samples):
    result=[]
    for performance in ('off','detailed'):
        scenarios=dict.fromkeys(r['scenario'] for r in samples if r['performance']==performance and r['phase']=='modified')
        for scenario in scenarios:
            for phase in ('hot_before','modified','hot_after'):
                rows={label:[r for r in samples if r['scenario']==scenario and r['performance']==performance and
                             r['phase']==phase and r['implementation']==label and r['status']=='passed']
                      for label in ('baseline','candidate')}
                if not all(rows.values()):continue
                item=dict(mode='incremental-'+performance,scenario=scenario,phase=phase)
                for label,values in rows.items():
                    wall=[r['wallMs'] for r in values]
                    item[label]=dict(n=len(wall),medianMs=statistics.median(wall),minMs=min(wall),maxMs=max(wall),
                                     xmlParse=[r['cache']['xmlParse'] for r in values],
                                     instanceCompile=[r['cache']['instanceCompile'] for r in values])
                item['reductionPercent']=100*(1-item['candidate']['medianMs']/item['baseline']['medianMs'])
                result.append(item)
    return result


def save(out, state):
    state['summary']=summarize(state['samples'])
    write(out / 'measurement.json', state)
    lines=['# Warm-cache edit paired measurement','', 'Status: '+state['status'], '',
           'Same Windows VM, input and jobs=4; new process per sample, OS page cache uncontrolled. '
           'Three independent off pairs per core edit; detailed samples are separate attribution and excluded from off medians. '
           'Cold setup and raw published byte comparisons are outside edit process timing.', '',
           '| Mode | Scenario | Phase | Baseline n / median ms | Candidate n / median ms | Reduction | Parse baseline / candidate | Compile baseline / candidate |',
           '| --- | --- | --- | ---: | ---: | ---: | --- | --- |']
    for row in state['summary']:
        b,c=row['baseline'],row['candidate']
        lines.append('| %s | %s | %s | %d / %.2f | %d / %.2f | %.2f%% | %s / %s | %s / %s |' % (
            row['mode'],row['scenario'],row['phase'],b['n'],b['medianMs'],c['n'],c['medianMs'],row['reductionPercent'],
            b['xmlParse'],c['xmlParse'],b['instanceCompile'],c['instanceCompile']))
    lines += ['', 'Every generation compares actual publication bytes across implementations. Hot phases must parse/compile zero. '
              'Unrelated parent sibling and unused Define edits preserve publication bytes. '
              'The off samples measure only the two selected parent edits; detailed samples retain the other dependency diagnostics. '
              'This bounded run does not replace complete Core/CLI/SDK acceptance.']
    if state.get('error'):lines += ['',state['error']]
    (out/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')


def run(args):
    repo=Path(__file__).resolve().parents[2]
    work,out=args.work.resolve(),args.output.resolve()
    need(not work.exists() and not out.exists(),'Refusing to overwrite incremental evidence')
    work.mkdir(parents=True);out.mkdir(parents=True)
    if os.name=='nt':ctypes.windll.kernel32.SetErrorMode(0x0001|0x0002|0x8000)
    started=time.perf_counter()
    scenarios=['single-in-large-document','inheritance-source','unrelated-parent-sibling','define-value','unused-define-value','session-metadata-missing']
    state=dict(schemaVersion=1,status='preparing',sourceCommit=git(repo,'rev-parse','HEAD'),runId=os.getenv('GITHUB_RUN_ID'),
               runAttempt=os.getenv('GITHUB_RUN_ATTEMPT'),baselineHead=BASELINE_HEAD,baselineBinarySource=BASELINE_BINARY_SOURCE,
               baselineRunId=BASELINE_RUN,candidateSource=args.candidate_source,candidateRunId=args.candidate_run_id,
               measurementMode='incremental',performance='off-with-separate-detailed',armorPartitions=0,pairCount=args.pairs,budgetSeconds=args.budget,scenarios=scenarios,samples=[],mutations=[],
               machine=dict(system=platform.platform(),runnerImage=os.getenv('ImageVersion')),
               scope='Synthetic 512 Armor + three dependency assets; parent edit off pairs and separate six-event detailed diagnostics; not full MOD or L8 Common')
    references={}
    def remaining():
        seconds=args.budget-(time.perf_counter()-started)
        need(seconds>0,'Incremental total budget exhausted')
        return min(args.timeout,seconds)
    try:
        source=git(repo,'rev-parse',args.candidate_source+'^{commit}')
        need(source==args.candidate_source,'Candidate source must be full SHA')
        subprocess.run(['git','-C',str(repo),'merge-base','--is-ancestor',BASELINE_HEAD,source],check=True)
        closure=('SDK','recovery/src','recovery/build','recovery/probes','ui','recovery/scripts/Build*','recovery/scripts/PrepareLegacyTexture.ps1','recovery/scripts/RestoreUiDependencies.ps1')
        need(not git(repo,'diff','--name-only',BASELINE_BINARY_SOURCE,BASELINE_HEAD,'--',*closure),'Accepted baseline input closure changed')
        changes=git(repo,'diff','--name-only',BASELINE_HEAD,source,'--',*closure).splitlines()
        need(changes and all(p.startswith(('recovery/src/App/','recovery/src/Managed/Core/','recovery/src/Managed/Metrics/')) for p in changes),'Unexpected candidate production closure')
        state['productionChanges']=changes
        binaries,identities={},{}
        for label,directory,commit,pin in [('baseline',args.baseline_build.resolve(),BASELINE_BINARY_SOURCE,BASELINE_FILES),('candidate',args.candidate_build.resolve(),source,None)]:
            binaries[label],identities[label]=product(directory,commit,pin)
            shutil.copy2(directory/'build.json',out/(label+'-build.json'))
        state['runtimeFiles']=identities
        state['status']='running';save(out,state)

        def sample(label,scenario,phase,generation,performance,trial):
            name='%02d-%s-%s-%s-%s-%s' % (len(state['samples']),label,trial,scenario,phase,performance)
            evidence=out/name;evidence.mkdir()
            destination=destinations[label]
            report_path=destination/'build-report.json';report_path.unlink(missing_ok=True)
            command=[str(binaries[label]),'build','--sdk-root',str(repo/'SDK'),'--mod',str(mod),'--out',str(destination),
                     '--package-content','base','--package-format','loose','--include-additional=false','--jobs','4','--performance',performance]
            profile=cache_root(repo/'SDK',mod)/'validation'/'incremental'/label
            command += ['/scd:'+str(profile/'session'),'/bcd:'+str(profile/'assets'),'/iod:'+str(profile/'intermediate')]
            row=dict(implementation=label,scenario=scenario,phase=phase,generation=generation,performance=performance,trial=trial,status='started',command=command)
            state['samples'].append(row);save(out,state)
            try:
                with (evidence/'stdout.log').open('wb') as stdout,(evidence/'stderr.log').open('wb') as stderr:
                    tick=time.perf_counter();process=subprocess.Popen(command,cwd=binaries[label].parent,stdout=stdout,stderr=stderr)
                    try:process.wait(timeout=remaining())
                    except subprocess.TimeoutExpired:
                        process.kill();process.wait(timeout=5);row.update(status='timed_out',wallMs=(time.perf_counter()-tick)*1000);raise
                    row.update(wallMs=(time.perf_counter()-tick)*1000,exitCode=process.returncode,**process_metrics(process))
                report=json.loads(report_path.read_text(encoding='utf-8-sig'))
                row.update(reportElapsedMs=report['elapsedMs'],cache=report['cache'],diagnosticCounts=report['diagnosticCounts'],phases=phases(report))
                counts={}
                for counter in report.get('performance',{}).get('counters',[]):
                    if counter.get('streamId')=='base/Mod': counts[counter['name']]=counter['value']
                row['counters']=counts
                for key in ('core.xml.instance.hash','core.xml.instance.references','core.xml.schema.validate','core.xml.instances.validate','core.xml.overrides','core.xml.expressions','core.dependencies.inheritance.identity','core.xml.inheritance.frontend'):
                    row['phases'][key]=sum(e['totalMs'] for e in report.get('performance',{}).get('entries',[]) if e['name']==key)
                need(process.returncode==0 and report['status']=='succeeded' and report['diagnosticCounts']['error']==0,name+' failed')
                if phase.startswith('hot'):
                    need(report['cache']['xmlParse']==0 and report['cache']['instanceCompile']==0 and report['cache']['documentReuse']>0,name+' failed to become hot')
                    need(counts.get('cache.invalidation.reloads',0)==0,'Hot process reported invalidation')
                if phase=='modified':
                    if scenario=='session-metadata-missing':
                        need(report['cache']['xmlParse']>0 and report['cache']['instanceCompile']==0,'Metadata loss must rebuild frontend without backend calls')
                    elif scenario=='unused-define-value':
                        need(report['cache']['xmlParse']==1 and report['cache']['instanceCompile']==0,'Unused Define must only reload its own definition document')
                    else:
                        need(report['cache']['xmlParse']>0,'Edit did not reload XML')
                        reason='cache.invalidation.'+('contentChanged' if scenario in ('single-in-large-document','inheritance-source','unrelated-parent-sibling') else 'definesChanged')
                        if performance=='detailed':need(counts.get(reason,0)>0,'Expected actual invalidation reason missing: '+reason)
                        if scenario=='single-in-large-document':need(report['cache']['instanceCompile']==1,'Single Armor edit recompiled unrelated siblings')
                        if performance=='detailed' and scenario in ('inheritance-source','unrelated-parent-sibling'):
                            need(counts.get('cache.invalidation.dependentFileChanged',0)>0,'Instance-include dependency did not trigger frontend propagation')
                        if scenario=='unrelated-parent-sibling':
                            need(report['cache']['xmlParse']==2,'Parent file dependency did not reload both documents')
                            need(report['cache']['instanceCompile']==1,'Parent sibling edit changed backend work')
                        if performance=='detailed' and scenario=='define-value':need(counts.get('cache.invalidation.inheritanceChanged',0)>0,'Define did not propagate through inheritance')
                content=published(destination,report)
                if generation not in references: references[generation]=content
                need(content==references[generation],'Published bytes differ across implementation/generation: '+name)
                if phase=='modified' and scenario in ('unrelated-parent-sibling','unused-define-value','session-metadata-missing'):
                    need(content==references[generation-1],'Unrelated sibling/unused Define/metadata edit changed published assets')
                if phase=='modified' and scenario in ('single-in-large-document','inheritance-source','define-value'):
                    need(content!=references[generation-1],'Effective asset edit did not change any published bytes')
                row.update(status='passed',rawPublishedIdentical=True,publishedBytes=sum(map(len,content.values())))
            except Exception as error:
                row['error']=str(error)
                if row['status']=='started':row['status']='failed'
                raise
            finally:
                if report_path.exists():shutil.copy2(report_path,evidence/'build-report.json')
                save(out,state)
                print(json.dumps({k:row.get(k) for k in ('implementation','scenario','phase','status','wallMs')}),flush=True)

        sequences=[('off-'+str(i+1),['inheritance-source','unrelated-parent-sibling'],'off',i+1) for i in range(args.pairs)]
        sequences += [('detailed',scenarios,'detailed',1)]
        state['initialInputs']={}
        for sequence,selected,performance,trial in sequences:
            mod=prepare(work/sequence);data=mod/'Data';state['initialInputs'][sequence]=inventory(mod)
            destinations={label:work/sequence/('out-'+label) for label in binaries}
            references={}
            generation=0
            order=('baseline','candidate') if trial%2 else ('candidate','baseline')
            for label in order:sample(label,'warmup','cold',generation,performance,trial)
            for index,scenario in enumerate(selected):
                order=('baseline','candidate') if (index+trial)%2==0 else ('candidate','baseline')
                for label in order:sample(label,scenario,'hot_before',generation,performance,trial)
                if scenario=='session-metadata-missing':
                    deleted={}
                    for label,destination in destinations.items():
                        session=cache_root(repo/'SDK',mod)/'validation'/'incremental'/label/'session'
                        paths=[p for p in (session/'BinaryAssetBuilder.SessionCache.xml',session/'BinaryAssetBuilder.SessionCache.xml.deflate') if p.exists()]
                        need(paths,'No session metadata to remove')
                        deleted[label]=[str(p) for p in paths]
                        for p in paths:p.unlink()
                    state['mutations'].append(dict(scenario=scenario,deleted=deleted))
                else:
                    path,old,new={
                        'single-in-large-document':(data/'large.xml',b'72.5%',b'62.5%'),
                        'inheritance-source':(data/'base.xml',b'72.5%',b'62.5%'),
                        'unrelated-parent-sibling':(data/'base.xml',b'64.5%',b'54.5%'),
                        'define-value':(data/'defines.xml',b'83.25',b'73.25'),
                        'unused-define-value':(data/'defines.xml',b'95.25',b'85.25')}[scenario]
                    mutation=same_stamp_edit(path,lambda b:b.replace(old,new,1));mutation['scenario']=scenario;state['mutations'].append(mutation)
                generation+=1
                for label in order:sample(label,scenario,'modified',generation,performance,trial)
                for label in reversed(order):sample(label,scenario,'hot_after',generation,performance,trial)
        remaining();state['status']='passed'
    except Exception as error:
        state.update(status='failed',error=str(error),errorType=type(error).__name__);raise
    finally:
        state['elapsedSeconds']=time.perf_counter()-started;save(out,state)


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ('baseline-build','candidate-build','work','output'):p.add_argument('--'+name,type=Path,required=True)
    p.add_argument('--candidate-source',required=True);p.add_argument('--candidate-run-id',required=True)
    p.add_argument('--pairs',type=int,choices=range(3,6),default=3)
    p.add_argument('--budget',type=int,choices=range(60,241),default=240)
    p.add_argument('--timeout',type=int,choices=range(5,31),default=30)
    run(p.parse_args())
