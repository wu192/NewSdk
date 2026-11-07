#!/usr/bin/env python3
"""Independent binary and mocked-Lua verification of the integrated 8-player map."""
from pathlib import Path
import argparse, collections, ctypes, ctypes.util, hashlib, json, re, struct

ROOT=Path(__file__).resolve().parent
class Reader:
 def __init__(self,data):self.data=data;self.p=0
 def take(self,n):
  value=self.data[self.p:self.p+n];self.p+=n
  assert len(value)==n,'Truncated data'
  return value
 def num(self,fmt):return struct.unpack('<'+fmt,self.take(struct.calcsize('<'+fmt)))[0]
 def text(self,unicode=False):
  n=self.num('H');return self.take(n*(2 if unicode else 1)).decode('utf-16-le' if unicode else 'gbk',errors='replace')
 def done(self):assert self.p==len(self.data),(self.p,len(self.data))

def decompress(raw):
 if raw[:4]==b'CkMp':return raw
 assert raw[:4]==b'EAR\0','Unsupported map header'
 p=14 if raw[8]&128 else 13;out=bytearray()
 while True:
  c=raw[p];p+=1
  if c<128:
   c2=raw[p];p+=1;lit=c&3;offset=1+c2+(c&96)*8;count=(c&28)//4+3
  elif c<192:
   c2,c3=raw[p:p+2];p+=2;lit=c2>>6;offset=1+((c2&63)<<8)+c3;count=(c&63)+4
  elif c<224:
   c2,c3,c4=raw[p:p+3];p+=3;lit=c&3;offset=1+((c&16)<<12)+(c2<<8)+c3;count=((c&12)<<6)+c4+5
  elif c<252:
   lit=(c&31)*4+4;out.extend(raw[p:p+lit]);p+=lit;continue
  else:
   lit=c&3;out.extend(raw[p:p+lit]);p+=lit;break
  out.extend(raw[p:p+lit]);p+=lit
  for _ in range(count):out.append(out[-offset])
 assert len(out)==struct.unpack_from('<I',raw,4)[0]
 return bytes(out)

def parse_map(raw):
 r=Reader(decompress(raw));assert r.take(4)==b'CkMp';names={}
 for _ in range(r.num('I')):
  n=0;shift=0
  while True:
   b=r.num('B');n|=(b&127)<<shift;shift+=7
   if not b&128:break
  name=r.take(n).decode('utf8');names[r.num('I')]=name
 assets={}
 while r.p<len(r.data):
  aid=r.num('I');version=r.num('H');payload=r.take(r.num('I'))
  assert names[aid] not in assets
  assets[names[aid]]=(version,payload)
 r.done();return names,assets

def properties(r,names):
 props={}
 for _ in range(r.num('H')):
  typ=r.num('B');pid=int.from_bytes(r.take(3),'little');key=names[pid]
  if typ==0:value=bool(r.num('B'))
  elif typ==1:value=r.num('i')
  elif typ==2:value=r.num('f')
  elif typ in (3,5):value=r.text()
  elif typ==4:value=r.text(True)
  else:raise AssertionError(('Unsupported property',typ,key))
  assert key not in props
  props[key]=value
 return props

def objects(data,names):
 r=Reader(data);out=[]
 while r.p<len(data):
  aid=r.num('I');version=r.num('H');o=Reader(r.take(r.num('I')))
  assert names[aid]=='Object' and version==3
  pos=[o.num('f') for _ in range(3)];angle=o.num('f');road=o.num('i');typ=o.text();props=properties(o,names);o.done()
  out.append({'type':typ,'position':pos,'angle':angle,'road':road,'properties':props})
 r.done();return out

def areas(data):
 r=Reader(data);out=[]
 for _ in range(r.num('I')):
  name=r.text();flags=r.num('H');aid=r.num('I');xy=[(r.num('f'),r.num('f')) for _ in range(r.num('I'))];tail=r.num('I')
  out.append({'name':name,'flags':flags,'id':aid,'points':xy,'tail':tail})
 r.done();return out

def hotspots(data):
 r=Reader(data);out=[(r.text(),r.text(),r.text()) for _ in range(r.num('I'))];r.done();return out

def position_records(data):
 r=Reader(data);out=[]
 while r.p<len(data):
  start=r.p;r.num('I');r.num('H');r.take(r.num('I'));out.append(data[start:r.p])
 return out

def verify_terrain(old,new):
 w,h,border,nw,edge,added=760,760,80,956,680,196
 od=old['HeightMapData'][1];nd=new['HeightMapData'][1]
 assert struct.unpack_from('<9i',nd)==(956,760,80,1,0,0,796,600,956*760)
 p=36
 for y in range(h):
  row=od[p+y*w*2:p+(y+1)*w*2];nr=nd[p+y*nw*2:p+(y+1)*nw*2]
  assert nr==row[:edge*2]+row[(edge-added)*2:edge*2]+row[edge*2:]
 od=old['BlendTileData'][1];nd=new['BlendTileData'][1];op=np=4
 assert struct.unpack_from('<i',nd)[0]==nw*h
 for plane in range(4):
  for y in range(h):
   row=od[op:op+w*2];nr=nd[np:np+nw*2];op+=w*2;np+=nw*2
   assert nr[:edge*2]==row[:edge*2],('Original terrain changed',plane,y)
   if plane==0:
    for x in range(edge,nw):
     src=struct.unpack_from('<H',row,(x-added)*2)[0];dst=struct.unpack_from('<H',nr,x*2)[0]
     assert src//64==dst//64
     assert dst%64==y%8//2*16+y%2*2+x%8//2*4+x%2
   else:assert nr==row[:edge*2]+row[(edge-added)*2:edge*2]+row[edge*2:]
 for plane in range(8):
  for y in range(h):
   row=od[op:op+(w+7)//8];nr=nd[np:np+(nw+7)//8];op+=(w+7)//8;np+=(nw+7)//8
   for x in range(nw):
    sx=x if x<edge else x-added
    assert bool(row[sx//8]&(1<<(sx%8)))==bool(nr[x//8]&(1<<(x%8)))
 for y in range(h):
  row=od[op:op+w];nr=nd[np:np+nw];op+=w;np+=nw
  assert nr==row[:edge]+row[edge-added:edge]+row[edge:]
 assert od[op:]==nd[np:]
 return {'all_height_and_13_blend_planes':'passed','original_left_grid_columns':680,'right_extension_columns':196,'texture_phase':'passed','texture_tables_preserved':True}

def snippets(node):
 if isinstance(node,dict):
  for value in node.values():yield from snippets(value)
 elif isinstance(node,list):
  for value in node:yield from snippets(value)
 elif isinstance(node,str) and node.startswith('#!ra3lua'):yield node.split('\n',1)[1]

class Lua:
 def __init__(self):
  lib=ctypes.util.find_library('lua5.4');assert lib,'liblua5.4 unavailable'
  self.lib=ctypes.CDLL(lib);l=self.lib
  l.luaL_newstate.restype=ctypes.c_void_p
  l.luaL_openlibs.argtypes=[ctypes.c_void_p]
  l.luaL_loadbufferx.argtypes=[ctypes.c_void_p,ctypes.c_char_p,ctypes.c_size_t,ctypes.c_char_p,ctypes.c_char_p]
  l.lua_pcallk.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_longlong,ctypes.c_void_p]
  l.lua_tolstring.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.POINTER(ctypes.c_size_t)];l.lua_tolstring.restype=ctypes.c_char_p
  l.lua_settop.argtypes=[ctypes.c_void_p,ctypes.c_int];l.lua_close.argtypes=[ctypes.c_void_p]
  self.state=l.luaL_newstate();l.luaL_openlibs(self.state)
 def execute(self,code,result=False):
  data=code.encode();l=self.lib;s=self.state
  rc=l.luaL_loadbufferx(s,data,len(data),b'verify',None)
  if not rc:rc=l.lua_pcallk(s,0,1 if result else 0,0,0,None)
  if rc:raise AssertionError(l.lua_tolstring(s,-1,None).decode())
  value=l.lua_tolstring(s,-1,None).decode() if result else None
  l.lua_settop(s,0);return value
 def close(self):self.lib.lua_close(self.state)

def verify_lua(scripts,team_names,waypoint_names,object_names):
 sources=list(snippets(scripts));required=['SpawnAttackTeam','SpawnAttackTeamQuietly','SpawnAttackTeamShielded','SpawnBoLuo','SpawnWeiHe','SpawnShiJi','TeamUseAbility','TeleportBossMove','TeleportBossMove2','SpawnParatroop','InsideMap']
 lua=Lua();checks={}
 try:
  lua.execute('''LOG={}; CONDITIONS=true
function ExecuteAction(...) table.insert(LOG,{...}) end
function EvaluateCondition(...) return CONDITIONS end
function GetObjectByScriptName(name) return {} end
function ObjectFindObjects(...) return {},0 end
function CreateObjectFilter(value) return value end
function ObjectGetPosition(...) return 0,0,0 end
function ObjectCreateAndFireTempWeaponToTarget(...) end
''')
  selected=[]
  for name in required:
   code=[c for c in sources if re.search(r'function\s+'+name+r'\s*\(',c)]
   assert len(code)==1,(name,len(code))
   if code[0] not in selected:selected.append(code[0]);lua.execute(code[0])
  def calls(expression,conditions=True):
   result=lua.execute('LOG={}; CONDITIONS='+str(conditions).lower()+'; '+expression+'''; local rows={}; for _,call in ipairs(LOG) do local s={}; for i,v in ipairs(call) do s[i]=tostring(v) end rows[#rows+1]=table.concat(s,"\\t") end;return table.concat(rows,"\\n")''',True)
   return [row.split('\t') for row in result.split('\n')] if result else []
  expectedteams=['K12'+' 1'*i for i in range(8)]
  assert all(t in team_names for t in expectedteams),'Attack team references missing'
  for name in ['SpawnAttackTeam','SpawnAttackTeamQuietly','SpawnAttackTeamShielded']:
   rows=calls(name+'(12)')
   for action,targets in [('CREATE_REINFORCEMENT_TEAM',['K'+str(i) for i in range(1,9)]),('ATTACK_MOVE_TEAM_TO',['L'+str(i) for i in range(1,9)]),('TEAM_SET_STANCE',['AGGRESSIVE']*8)]:
    actual=[r[1:] for r in rows if r[0]==action]
    assert actual==[list(p) for p in zip(expectedteams,targets)],(name,action,actual)
   checks[name]={'players':8,'actions':len(rows),'references':'passed'}
  for name,aircraft in [('SpawnBoLuo','AlliedFighterAircraft'),('SpawnWeiHe','AlliedAntiGroundAircraft'),('SpawnShiJi','AlliedBomberAircraft')]:
   rows=calls(name+'()');actual=[r for r in rows if r[0]=='UNIT_SPAWN_NAMED_OBJECT_ON_TEAM_AT_NAMED_OBJECT_LOCATION']
   assert len(actual)==8 and [r[-1] for r in actual]==['Carrier'+str(i)+'_3' for i in range(1,9)]
   assert all(r[2]==aircraft and r[-1] in object_names for r in actual)
   assert not [r for r in calls(name+'()',False) if r[0]=='UNIT_SPAWN_NAMED_OBJECT_ON_TEAM_AT_NAMED_OBJECT_LOCATION']
   checks[name]={'players':8,'named_carriers':'passed','destroyed_carriers_skipped':True}
  rows=calls('TeamUseAbility(12,"TestAbility")')
  assert [r[1:] for r in rows if r[0]=='TEAM_USE_COMMANDBUTTON_ABILITY']==[[t,'TestAbility'] for t in expectedteams]
  assert [r[1:] for r in rows if r[0]=='ATTACK_MOVE_TEAM_TO']==[[t,'L'+str(i+1)] for i,t in enumerate(expectedteams)]
  checks['TeamUseAbility']={'players':8,'actions':len(rows)}
  for name,suffix in [('TeleportBossMove',''),('TeleportBossMove2','_1')]:
   rows=calls(name+'("Carrier","TK")')
   assert [r[1:] for r in rows if r[0]=='UNIT_TELEPORT_TO_WAYPOINT']==[['Carrier'+str(i)+suffix,'TK'+str(i)] for i in range(1,9)]
   assert [r[1:] for r in rows if r[0]=='MOVE_NAMED_UNIT_TO']==[['Carrier'+str(i)+suffix,'Player_'+str(i)+'_Start'] for i in range(1,9)]
   assert len([r for r in rows if r[0]=='UNIT_SET_STANCE'])==8
   checks[name]={'players':8,'actions':len(rows),'waypoint_references':'passed'}
  rows=calls('SpawnParatroop("K12","Carrier")')
  assert [r[1:] for r in rows if r[0]=='CREATE_REINFORCEMENT_TEAM']==[[t,'TK'+str(i+1)] for i,t in enumerate(expectedteams)]
  assert [r[1:] for r in rows if r[0]=='TEAM_GARRISON_SPECIFIC_BUILDING_INSTANTLY']==[[t,'Carrier'+str(i+1)] for i,t in enumerate(expectedteams)]
  assert len([r for r in rows if r[0]=='NAMED_USE_COMMANDBUTTON_ABILITY'])==8
  assert len([r for r in rows if r[0]=='TEAM_MERGE_INTO_TEAM'])==8
  assert len(calls('SpawnParatroop("K12","Carrier")',False))==2
  checks['SpawnParatroop']={'players':8,'actions':len(rows),'absent_bosses_skipped':True}
  for x,y,ex,ey in [(7930,6000,7930,5950),(8000,6000,7910,5950),(0,0,50,50),(7959,5999,7959,5999),(7960,2000,7910,2000)]:
   result=lua.execute(f'local x,y=InsideMap({x},{y});return tostring(x)..","..tostring(y)',True)
   assert result==f'{ex},{ey}',(x,y,result)
  checks['InsideMap']={'boundary_cases':5,'x_limit':7960,'x_clamp':7910,'y_limit':6000,'y_clamp':5950}
  assert all('K'+str(i) in waypoint_names and 'L'+str(i) in waypoint_names and 'TK'+str(i) in waypoint_names and 'Player_'+str(i)+'_Start' in waypoint_names for i in range(1,9))
 finally:lua.close()
 return {'interpreter':'Lua 5.4 with mocked game actions/conditions; no game engine execution','snippets':len(sources),'tested_functions':checks}

def main():
 p=argparse.ArgumentParser();p.add_argument('--map',default=str(ROOT/'final.map'));p.add_argument('--scripts',default=str(ROOT/'final-scripts.json'));p.add_argument('--original',default=str(ROOT/'research/original.map'));p.add_argument('--report',default=str(ROOT/'verification.json'));args=p.parse_args()
 result={'status':'failed','map':args.map,'checks':{}}
 try:
  raw=Path(args.map).read_bytes();names,new=parse_map(raw);_,old=parse_map(Path(args.original).read_bytes())
  result['sha256']=hashlib.sha256(raw).hexdigest();result['scripts_sha256']=hashlib.sha256(Path(args.scripts).read_bytes()).hexdigest();result['checks']['terrain']=verify_terrain(old,new)
  oo=objects(new['ObjectsList'][1],names);assert len(oo)==446,len(oo)
  ids=[o['properties'].get('uniqueID') for o in oo];assert len(ids)==len(set(ids))
  named=[o['properties']['objectName'] for o in oo if o['properties'].get('objectName')];assert len(named)==len(set(named))
  wp=[o for o in oo if o['type']=='*Waypoints/Waypoint'];wnames=[o['properties']['waypointName'] for o in wp];assert len(wnames)==len(set(wnames))
  wids=[o['properties']['waypointID'] for o in wp];assert len(wids)==len(set(wids))
  starts={o['properties']['waypointName']:o['position'] for o in wp if re.fullmatch(r'Player_\d+_Start',o['properties']['waypointName'])};assert len(starts)==8
  for i in range(1,9):assert starts[f'Player_{i}_Start'][:2]==[400+980*(i-1),2160]
  ores=[o for o in oo if o['type']=='OreNode'];assert len(ores)==16,len(ores)
  r=Reader(new['Teams'][1]);teamcount=r.num('I');teams=[properties(r,names) for _ in range(teamcount)];r.done();assert teamcount==430,teamcount
  teamnames=[t['teamName'] for t in teams];fullteams=[t.get('teamOwner','')+'/'+t['teamName'] for t in teams]
  onames,_=parse_map(Path(args.original).read_bytes());ot=Reader(old['Teams'][1]);original_teams=[properties(ot,onames) for _ in range(ot.num('I'))];ot.done()
  original_duplicates={k:v for k,v in collections.Counter(t.get('teamOwner','')+'/'+t['teamName'] for t in original_teams).items() if v>1}
  duplicates={k:v for k,v in collections.Counter(fullteams).items() if v>1};assert duplicates==original_duplicates,(duplicates,original_duplicates)
  sides=Reader(new['SidesList'][1]);assert sides.num('B')==1;side_count=sides.num('I');side_data={}
  for _ in range(side_count):
   entry=properties(sides,names);side_data[entry['playerName']]=entry
   for _ in range(sides.num('I')):
    sides.text();sides.text();sides.take(18);sides.num('I');sides.text();sides.take(7)
  sides.done()
  assert sorted(n for n in side_data if re.fullmatch(r'Player_\d+',n))==[f'Player_{i}' for i in range(1,9)]
  result['checks']['player_slots']={'side_entries':side_count,'player_slots':8}
  result['checks']['objects_and_teams']={'objects':len(oo),'ore_nodes':len(ores),'starts':starts,'teams':teamcount,'unique_objects_waypoints_and_no_new_duplicate_teams':'passed','preserved_original_duplicate_teams':duplicates}
  a=areas(new['TriggerAreas'][1]);oa=areas(old['TriggerAreas'][1]);ad={x['name']:x for x in a};oad={x['name']:x for x in oa};assert len(a)==9 and len({x['id'] for x in a})==9
  for i in range(1,7):assert ad[f'Player{i}Zone']==oad[f'Player{i}Zone']
  for i,dx in [(7,980),(8,1960)]:assert ad[f'Player{i}Zone']['points']==[(x+dx,y) for x,y in ad['Player6Zone']['points']]
  assert ad['DeathZone']['points']==[(x+1960,y) for x,y in oad['DeathZone']['points']]
  hs=hotspots(new['MissionHotSpots'][1]);ohs=hotspots(old['MissionHotSpots'][1]);assert hs[:6]==ohs and len(hs)==8;assert [x[0] for x in hs]==['JavelinIFV'+str(i) for i in range(1,9)]
  mp=position_records(new['MPPositionList'][1]);omp=position_records(old['MPPositionList'][1]);assert mp[:6]==omp and mp[6:]==[omp[-1]]*2
  result['checks']['metadata']={'trigger_areas':len(a),'multiplayer_positions':len(mp),'hotspots':len(hs),'death_zone_translation':1960,'original_six_player_zones_preserved':True}
  result['checks']['lua']=verify_lua(json.loads(Path(args.scripts).read_text()),set(teamnames),set(wnames),set(named))
  result['status']='passed'
 except Exception as exc:
  result['error']=repr(exc)
  import traceback;result['traceback']=traceback.format_exc()
 finally:
  Path(args.report).write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(result,ensure_ascii=False,indent=2))
 assert result['status']=='passed',result.get('error')
if __name__=='__main__':main()
