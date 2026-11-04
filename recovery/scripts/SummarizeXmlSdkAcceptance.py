#!/usr/bin/env python3
"""Fail-closed aggregation of real SDK profiles, including guarded evidence reuse.

This only reads evidence and writes an optional acceptance JSON. It never runs a
compiler, changes catalogs, or treats non-global registrations as SDK asset calls.
Run after a successful SDK matrix artifact has been extracted, for example:
  python recovery/scripts/SummarizeXmlSdkAcceptance.py --evidence-root recovery/evidence/xml-priority-sdk --output recovery/evidence/xml-sdk-acceptance-20261025.json
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
REQUIRED=('XmlCompilerRecoveryBaseline','XmlCompilerRecoveryA','XmlCompilerRecoveryB','FullMymod','XmlCompilerRecoveryApt')
ORIGINAL='d4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6'
class EvidenceError(ValueError):pass
def require(condition,message):
 if not condition:raise EvidenceError(message)
def read(path):
 require(path.is_file(),f'Missing evidence file: {path}')
 return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def same_hash(left,right):return isinstance(left,str) and isinstance(right,str) and left.lower()==right.lower()
def success(document,label):require(document.get('succeeded') is True,f'{label}: not a successful runtime result')
def counts(document,key,label):
 value=document.get(key)
 require(isinstance(value,dict) and value,f'{label}: missing {key}')
 require(all(isinstance(k,str) and isinstance(n,int) and n>0 for k,n in value.items()),f'{label}: invalid {key}')
 return value

def aggregate(evidence):
 catalog=read(ROOT/'recovery/catalog/XmlSdkTypes.json')
 registered={r['name']:r for r in catalog['roots']}
 require(len(registered)==len(catalog['roots'])==300,'Expected 300 unique current registered routes')
 expected_global={name for name,row in registered.items() if row['schemaGlobal']}
 non_global=set(registered)-expected_global
 require(len(expected_global)==278 and len(non_global)==22,'Current catalog must split into 278 global and 22 non-global registrations')
 excluded={r['name'] for r in catalog['excluded']}
 require(excluded=={'TestGameObject'},'Only TestGameObject remains excluded')
 require(not excluded.intersection(registered),'Excluded roots entered current SDK routes')
 baseline=read(ROOT/'recovery/catalog/XmlRecoveredTypes.json')
 additions=read(ROOT/'recovery/catalog/XmlComponentOnlyTypes.json')
 old={r['name'] for r in baseline['roots']};added={r['name'] for r in additions['roots']}
 joypad={r['name'] for r in read(ROOT/'recovery/catalog/XmljoypadTypes.json')['roots']}
 require(len(old)==82 and len(added)==207 and len(joypad)==11 and not old.intersection(added) and not joypad.intersection(old|added) and old|added|joypad==set(registered),'Historical 82-root catalog, 207 additions and 11 Joypad roots must account for the current 300 routes')
 summary=read(evidence/'validation.json');success(summary,'SDK matrix')
 require(summary.get('sdkMode')=='/O2' and summary.get('sdkPlatform')==0,'SDK acceptance must name actual /O2 Win32 scope')
 require(summary.get('originalDllUnchanged') is True and summary.get('fullMymodGate') is True,'Original DLL integrity/full mymod gate missing')
 require(summary.get('studioStages')==[1,2,5],'Expected EALAModStudio base-data stages 1/2/5 only')
 for key in ('sourceCommit','runId','compilerSha256','pluginSha256'):require(bool(summary.get(key)),f'SDK matrix missing {key}')
 metadata=read(evidence/'metadata.json');success(metadata,'SDK metadata')
 require(metadata.get('supportedTypes')==300 and metadata.get('excludedTypes')==1,'Metadata must validate current 300 routes and one exclusion; historical metadata is not current acceptance')
 require(metadata.get('platforms')==[0,1,2,99] and metadata.get('initializations')==4 and metadata.get('reinitializations')==4,'Incomplete Initialize/ReInitialize four-platform metadata matrix')
 require(set(metadata.get('nonGlobalRegisteredRoots',[]))==non_global,'Metadata non-global root set differs from catalog')
 require(metadata.get('unknownTypeRejections')==12 and metadata.get('fieldComparisons')==16816,'Incomplete fixed 300-type metadata/unknown/excluded rejection matrix')
 require(same_hash(metadata.get('originalSha256'),ORIGINAL),'Original DLL fingerprint drift')
 require(same_hash(metadata.get('recoveredSha256'),summary['pluginSha256']),'Metadata validated another SDK plugin')
 fresh={row['name']:row for row in summary.get('profiles',[])}
 reused={row['name']:row for row in summary.get('reusedProfiles',[])}
 require(len(fresh)==len(summary.get('profiles',[])) and len(reused)==len(summary.get('reusedProfiles',[])),'Duplicate SDK profile rows')
 require(not set(fresh).intersection(reused),'Same SDK profile marked both fresh and reused')
 require(set(fresh)|set(reused)==set(REQUIRED),'Final acceptance requires exactly four base profiles plus the APT file-payload supplement')
 profile_rows=[];global_calls=Counter();total_calls=0;total_comparisons=0
 for name in REQUIRED:
  folder=evidence/name;entry=read(folder/'validation.json');success(entry,name)
  # Older successful receipts lack anchors; preserve the file and obtain them
  # only from its successful runtime replay, as the SDK runner does in memory.
  anchor_replay=read(folder/'replay.json');success(anchor_replay,name+' anchor replay')
  anchor_from_replay=[]
  for key in ('sourceCommit','runId'):
   if not entry.get(key):
    require(bool(anchor_replay.get(key)),f'{name}: legacy receipt and replay lack {key}')
    entry[key]=anchor_replay[key];anchor_from_replay.append(key)
  require(entry.get('name')==name,'Profile validation identity mismatch: '+name)
  for flag in ('schemaValidated','fourOutputsIdentical','originalAndIsolatedReplay'):require(entry.get(flag) is True,f'{name}: missing {flag}')
  require(same_hash(entry.get('compilerSha256'),summary['compilerSha256']),f'{name}: accepted native DLL differs from current matrix')
  is_reused=name in reused
  anchor=reused[name] if is_reused else fresh[name];success(anchor,name+' matrix row')
  require(entry.get('sourceCommit')==anchor.get('sourceCommit') and str(entry.get('runId'))==str(anchor.get('runId')),f'{name}: profile anchor source/run mismatch')
  if not is_reused:
   require(entry['sourceCommit']==summary['sourceCommit'] and str(entry['runId'])==str(summary['runId']),f'{name}: fresh profile not tied to current source/run')
   require(same_hash(entry.get('pluginSha256'),summary['pluginSha256']),f'{name}: fresh profile used another plugin')
   for mode in ('Original','Recovered'):
    flow=read(folder/(mode+'-studio')/'studio-flow.json');success(flow,name+'/'+mode+' Studio base-data build')
    require(sum(s.get('step')==5 and s.get('label')=='mod' and s.get('exitCode')==0 for s in flow.get('steps',[]))==1,f'{name}/{mode}: missing successful actual BAB stage5')
  else:
   for flag in ('inputAndNativeUnchanged','metadataAndDelegationUnchanged','newGuardLeavesAllPriorInputsIdentical','priorFourOutputsAndReplaysRetained'):
    require(anchor.get(flag) is True,f'{name}: reused profile missing {flag}')
   require(anchor.get('nativeCompilerCalls')==0 and anchor.get('realSdkBuildsThisRun')==0,f'{name}: reuse accounting is not zero compiler calls')
   guard=read(folder/'reuse-guard.json');success(guard,name+' reuse guard')
   for flag in ('rootMetadataAbsent','originalDeclarationIdentityPreserved','originalNodeIdentityPreserved','originalXmlPreserved','originalDllFilesAbsent','originalCompilerAssembliesAbsent'):
    require(guard.get(flag) is True,f'{name}: identity reuse proof missing {flag}')
   require(guard.get('nativeCompilerCalls')==0 and guard.get('processInstanceCalls')==0 and guard.get('guardChecks',0)>0,f'{name}: invalid zero-call reuse proof')
   require(same_hash(guard.get('pluginSha256'),summary['pluginSha256']),f'{name}: guard did not load current plugin')
  schema=read(folder/'schema.json');success(schema,name+' schema')
  require(schema.get('registeredTypes')==300 and schema.get('expectedAssets',0)>0 and schema.get('expectedTypes',0)>0,f'{name}: schema validated another/empty scope')
  probe=folder/'source-probe.xml';require(probe.is_file(),f'{name}: no actual source probe')
  require(same_hash(digest(probe),entry.get('probeSha256')) and same_hash(digest(probe),schema.get('probeSha256')),f'{name}: source/schema probe fingerprints differ')
  original_input=folder/'Original-Mod.xml';recovered_input=folder/'Recovered-Mod.xml'
  require(original_input.is_file() and recovered_input.is_file() and digest(original_input)==digest(recovered_input),f'{name}: original/recovered MOD inputs differ')
  output_hashes={}
  for ext in ('bin','manifest','imp','relo'):
   original=folder/('Original-mod.'+ext);recovered=folder/('Recovered-mod.'+ext)
   require(original.is_file() and recovered.is_file(),f'{name}: missing actual {ext} outputs')
   require(digest(original)==digest(recovered),f'{name}: {ext} output bytes differ')
   output_hashes[ext]={'bytes':recovered.stat().st_size,'sha256':digest(recovered)}
  if name!='XmlCompilerRecoveryApt':
   require(entry.get('nonemptyImports') is True and output_hashes['imp']['bytes']>4,f'{name}: nonempty SDK import coverage missing')
  else:
   require(entry.get('rawBlobOnlyImports') is True and entry.get('filePayloadsVerified') is True and output_hashes['imp']['bytes']<=4,f'{name}: missing raw-blob/no-import file-payload capability gate')
  replay=read(folder/'replay.json');isolated=read(folder/'isolated.json')
  for label,result in (('replay',replay),('isolated',isolated)):
   success(result,name+'/'+label)
   require(result.get('profile')==name and result.get('platform')==0,f'{name}/{label}: wrong profile/platform')
   require(result.get('sourceCommit')==entry['sourceCommit'] and str(result.get('runId'))==str(entry['runId']),f'{name}/{label}: evidence anchor differs')
   require(result.get('registeredTypes')==300 and set(result.get('nonGlobalRegisteredRoots',[]))==non_global,f'{name}/{label}: not the current registration scope')
   require(result.get('nonGlobalRootEntryCoverageClaimed') is False,f'{name}/{label}: non-global root coverage overclaimed')
   require(result.get('expectedAssets')==schema['expectedAssets']==result.get('requiredAssetsVerified'),f'{name}/{label}: missing required source asset calls')
   require(result.get('requiredTypeCount')==schema['expectedTypes'],f'{name}/{label}: required type count differs from schema probe')
   require(result.get('comparisons')==3*result.get('verifiedCalls',0)>0,f'{name}/{label}: incomplete three-buffer replay')
   require(same_hash(result.get('probeSha256'),entry['probeSha256']) and same_hash(result.get('modSha256'),output_hashes['bin']['sha256']),f'{name}/{label}: replay used another probe or SDK mod.bin')
   type_calls=counts(result,'typeCalls',name+'/'+label);id_calls=counts(result,'idCalls',name+'/'+label)
   require(set(type_calls)<=expected_global and sum(type_calls.values())==sum(id_calls.values())==result['verifiedCalls'],f'{name}/{label}: invalid actual type/id call matrix')
   trace=folder/'trace'/'calls.tsv';require(trace.is_file() and same_hash(digest(trace),result.get('traceManifestSha256')),f'{name}/{label}: trace manifest fingerprint differs')
  require(replay['typeCalls']==isolated['typeCalls'] and replay['idCalls']==isolated['idCalls'] and replay['verifiedCalls']==isolated['verifiedCalls'],f'{name}: original/isolated replay coverage differs')
  require(same_hash(replay.get('compilerSha256'),ORIGINAL),f'{name}: original replay did not use fixed original DLL')
  require(same_hash(isolated.get('compilerSha256'),entry['pluginSha256']),f'{name}: isolated replay used another accepted plugin')
  require(isolated.get('originalDllFilesAbsent') is True and isolated.get('originalCompilerAssembliesAbsent') is True,f'{name}: isolated replay retained original compilers')
  if is_reused:
   require(guard.get('guardChecks')==replay['verifiedCalls'] and guard.get('typeChecks')==replay['typeCalls'],f'{name}: guard did not cover every prior actual SDK call')
   require(same_hash(guard.get('traceManifestSha256'),replay['traceManifestSha256']),f'{name}: guard checked another trace')
   for stem,hash_value in guard.get('xmlSha256',{}).items():
    require(stem==Path(stem).name and same_hash(digest(folder/'trace'/(stem+'.xml')),hash_value),f'{name}: guarded XML changed')
   require(len(guard.get('xmlSha256',{}))==replay['verifiedCalls'],f'{name}: incomplete per-input guard hashes')
  global_calls.update(replay['typeCalls']);total_calls+=replay['verifiedCalls'];total_comparisons+=replay['comparisons']+isolated['comparisons']
  profile_rows.append({'name':name,'reused':is_reused,'sourceCommit':entry['sourceCommit'],'runId':str(entry['runId']),'actualCalls':replay['verifiedCalls'],'actualTypes':len(replay['typeCalls']),'requiredAssets':replay['expectedAssets'],'requiredTypes':replay['requiredTypeCount'],'receiptAnchorFieldsFromReplay':anchor_from_replay,'threeBufferComparisons':replay['comparisons']+isolated['comparisons'],'traceManifestSha256':replay['traceManifestSha256'],'probeSha256':entry['probeSha256'],'outputs':output_hashes})
 require(set(global_calls)==expected_global,'Actual SDK root-call union is not exactly 278 globals; missing='+repr(sorted(expected_global-set(global_calls)))+' extra='+repr(sorted(set(global_calls)-expected_global)))
 apt=read(evidence/'XmlCompilerRecoveryApt'/'apt-payloads.json');success(apt,'APT payload capability')
 # The dedicated checker owns structural/payload validation; bind its result to
 # this same actual SDK trace before claiming nonempty file-read coverage.
 apt_trace=digest(evidence/'XmlCompilerRecoveryApt'/'trace'/'calls.tsv')
 require(same_hash(apt.get('callsManifestSha256'),apt_trace),'APT capability belongs to another trace')
 require(apt.get('profile')=='XmlCompilerRecoveryApt' and apt.get('platform')==0,'APT capability profile/platform differs')
 require(apt.get('checkedCalls')==12 and apt.get('nonemptyFileCalls')==8 and apt.get('emptyFileCalls')==4,'APT capability must prove eight nonempty and four empty file reads')
 require(set(apt.get('types',[]))=={'AptAptData','AptConstData','AptDatData','AptGeometryData'},'APT capability must prove all four concrete roots')
 require(len(apt.get('checks',[]))==12 and apt.get('payloadBytesCompared')==236,'APT exact file-byte matrix differs')
 apt_profile=next(r for r in profile_rows if r['name']=='XmlCompilerRecoveryApt')
 require(apt_profile['actualCalls']==12 and apt_profile['outputs']['imp']['bytes']<=4,'APT supplemental call/import scope differs')
 require(same_hash(apt.get('modBinSha256'),apt_profile['outputs']['bin']['sha256']),'APT capability checked another final mod.bin')
 for check in apt['checks']:
  stem=check.get('stem');require(isinstance(stem,str) and stem==Path(stem).name,'APT capability has invalid trace stem')
  require(same_hash(check.get('sourceSha256'),check.get('payloadSha256')),'APT source and payload byte fingerprints differ')
  require(same_hash(digest(evidence/'XmlCompilerRecoveryApt'/'trace'/(stem+'.bin')),check.get('traceInstanceSha256')),'APT traced instance changed after capability validation')
  require(same_hash(digest(evidence/'XmlCompilerRecoveryApt'/'trace'/(stem+'.xml')),check.get('traceXmlSha256')),'APT normalized source XML changed after capability validation')
 return {'succeeded':True,'scope':'Current /O2 Win32 SDK base-data compilation and original/original-free replay for 278 actual global root types; all 300 registration metadata verified separately; 22 non-global root entry SDK calls not claimed','sourceCommit':summary['sourceCommit'],'runId':str(summary['runId']),'nativeCompilerSha256':summary['compilerSha256'].lower(),'sdkPluginSha256':summary['pluginSha256'].lower(),'originalDllSha256':ORIGINAL,'sdkMode':'/O2','sdkPlatform':0,'studioStages':[1,2,5],'currentRegisteredEntries':300,'actualSdkGlobalEntries':278,'nonGlobalRegisteredEntries':22,'nonGlobalRootEntryCoverageClaimed':False,'excludedEntries':1,'excludedRoots':sorted(excluded),'nonGlobalRootsWithoutDirectSdkEntryAcceptance':sorted(non_global),'historicalSdkCatalog':{'entries':82,'sourceCommit':additions['sdkBaselineSourceCommit'],'runId':str(additions['sdkBaselineRunId']),'isCurrentDllAcceptance':False},'profiles':profile_rows,'actualSdkCalls':total_calls,'originalAndIsolatedThreeBufferComparisons':total_comparisons,'actualTypeCalls':dict(sorted(global_calls.items())),'aptPayloadCapability':apt,'realSdkBuildsThisRun':summary.get('realSdkBuilds'),'reusedProfiles':[r['name'] for r in profile_rows if r['reused']],'currentCatalogsMutated':False}

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--evidence-root',type=Path,default=ROOT/'recovery/evidence/xml-priority-sdk');parser.add_argument('--output',type=Path);args=parser.parse_args()
 try:
  result=aggregate(args.evidence_root.resolve())
  if args.output:
   args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
  print(json.dumps({k:v for k,v in result.items() if k not in ('profiles','actualTypeCalls','aptPayloadCapability','excludedRoots','nonGlobalRootsWithoutDirectSdkEntryAcceptance')},indent=2))
 except (EvidenceError,KeyError,TypeError,OSError,json.JSONDecodeError) as error:
  print('SDK acceptance not established: '+str(error),file=sys.stderr);return 1
 return 0
if __name__=='__main__':raise SystemExit(main())
