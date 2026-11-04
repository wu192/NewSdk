"""Actual production CLI AudioFile jobs/cache/recompile acceptance, no alternate cache."""
import argparse,hashlib,json,os,shutil,struct,subprocess,time
from pathlib import Path
import xml.etree.ElementTree as ET
from AudioEaLayer3Fixtures import make,NS
from ValidateAudioEaLayer3 import need

def run(a):
    repo=Path(a.repo).resolve();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=True)
    work=Path(a.work).resolve();mod=work/'EALayer3Mod';data=mod/'Data';data.mkdir(parents=True)
    cases=make(mod/'Audio',repo)
    # Exercise legacy and EALayer3 identities in the same cached document.
    root=ET.parse(mod/'Audio/assets.xml').getroot()
    for compression in ('NONE','XAS'):
        ET.SubElement(root,'{%s}AudioFile'%NS,{'id':'legacy_'+compression,
            'File':str((mod/'Audio/c2_s1_l0.wav').resolve()),'PCCompression':compression,'IsStreamedOnPC':'true'})
        cases.append({'id':'legacy_'+compression})
    ET.ElementTree(root).write(data/'Mod.xml',encoding='utf-8',xml_declaration=True)
    state={'sourceCommit':os.environ.get('GITHUB_SHA'),'runId':os.environ.get('GITHUB_RUN_ID'),'passed':False,'cases':[]}
    def invoke(name,destination,jobs,sc=True,bc=True,exe=None):
        row={'name':name,'jobs':jobs};state['cases'].append(row)
        command=[str(Path(exe or a.exe).resolve()),'build','--mod',str(mod),'--sdk-root',str(repo/'SDK'),'--out',str(destination),'--jobs',str(jobs),'--performance','detailed','--sc='+str(sc).lower(),'--bc='+str(bc).lower(),'--package-content','base','--package-format','loose']
        caseout=out/name;caseout.mkdir()
        with (caseout/'stdout.log').open('wb') as stdout,(caseout/'stderr.log').open('wb') as stderr:
            result=subprocess.run(command,cwd=Path(a.exe).resolve().parent,stdout=stdout,stderr=stderr,timeout=180)
        body=json.loads((destination/'build-report.json').read_text(encoding='utf-8-sig'));(caseout/'build-report.json').write_text(json.dumps(body,indent=2))
        row['exitCode']=result.returncode;row['cache']=body['cache'];need(result.returncode==0 and body['exitCode']==0,name+': production build failed')
        files={}
        for artifact in body['artifacts']:
            path=Path(artifact['Path']);files[path.relative_to(destination/'published').as_posix()]=path.read_bytes()
        cdata=[v for k,v in files.items() if k.endswith('.cdata')]
        need(len(cdata)==len(cases),name+': missing AudioFile CustomData')
        row['customFiles']=len(cdata);row['outputs']={k:hashlib.sha256(v).hexdigest() for k,v in files.items()}
        row['passed']=True;return files,body
    try:
        if a.previous_exe:
            invoke('previous-session',work/'migration',4,exe=a.previous_exe)
        j1,b1=invoke('jobs1-cold',work/'j1',1)
        j4,b4=invoke('jobs4-cold',work/'j4',4)
        need(j1==j4,'Production jobs1/4 full artifacts differ')
        parallel=b4['cache']['parallel']
        need(parallel['backendCallsByType'].get('AudioFile')==len(cases),'AudioFile backend invocation count differs')
        need(parallel['maxBackendConcurrency']>1,'AudioFile jobs4 was globally serialized')
        hot,h=invoke('jobs4-hot',work/'j4',4)
        need(hot==j4 and h['cache']['instanceCompile']==0 and h['cache']['xmlParse']==0,'Hot cache called backend or changed output')
        if a.previous_exe:
            migrated,m=invoke('previous-session-upgraded',work/'migration',4)
            need(migrated==j4 and m['cache']['xmlParse']>0,'Previous type-wide AudioFile identity did not migrate')
            warm_migration,mh=invoke('previous-session-upgraded-hot',work/'migration',4)
            need(warm_migration==j4 and mh['cache']['xmlParse']==0 and mh['cache']['instanceCompile']==0,'Migrated mixed audio document did not become hot')
        source=mod/'Audio/c2_s1_l0.wav';raw=bytearray(source.read_bytes())
        # Change sustained signal, preserving a real valid PCM container.
        for at in range(44,len(raw)-1,2):
            v=struct.unpack_from('<h',raw,at)[0];struct.pack_into('<h',raw,at,-v)
        source.write_bytes(raw)
        changed,c=invoke('modified-audio',work/'j4',4)
        need(changed!=hot and c['cache']['instanceCompile']>=1,'Changed WAV did not invalidate the existing cache')
        fresh,f=invoke('modified-independent',work/'fresh',1,False,False)
        need(fresh==changed,'Modified cached output differs from independent no-cache compilation')
        warm,w=invoke('modified-hot',work/'j4',4)
        need(warm==changed and w['cache']['instanceCompile']==0,'Modified audio did not return to hot cache')
        state['passed']=True
    finally:(out/'validation.json').write_text(json.dumps(state,indent=2))
    print('PASS production AudioFile CLI jobs/cache: %d assets, cold1/4, hot, mutation, independent rebuild'%len(cases))
if __name__=='__main__':
    p=argparse.ArgumentParser()
    for n in ('repo','exe','output','work'):p.add_argument('--'+n,required=True)
    p.add_argument('--previous-exe')
    run(p.parse_args())

