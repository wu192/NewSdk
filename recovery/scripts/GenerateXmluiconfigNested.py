from pathlib import Path
import json
r=Path(__file__).resolve().parents[2]; e=r/'recovery/evidence/xml-source'; out=r/'recovery/src/Native/XmluiconfigNested.inc'
enums={'FactionType':0,'AIDifficultyValue':3,'GameplayType':2,'SlotStateType':3,'CareerStatType':1,'CareerStatOrder':33,'PostGameStatType':10,'ShadowType':3,'JoyPadButtonType':6,'JoyPadActionType':2}
fields={
 'AIDifficultySetting':(12,[('Label',0,'string',None),('Value',8,'enum:AIDifficultyValue',None)]),
 'TeamSetting':(12,[('Label',0,'string',None),('Value',8,'int',None)]),
 'RankedOptionSetting':(12,[('Label',0,'string',None),('Value',8,'int',None)]),
 'GameplayTypeSetting':(16,[('Label',0,'string',None),('Value',8,'enum:GameplayType',None),('AvailableWithAI',12,'bool','false')]),
 'GameTimeSetting':(20,[('Label',0,'string',None),('Value',8,'int',None),('Default',16,'bool','false'),('MinGameTimeValue',12,'int','0')]),
 'GameResourcesSetting':(8,[('Value',0,'int',None),('Default',4,'bool','false')]),
 'GamePointsSetting':(8,[('Value',0,'int',None),('Default',4,'bool','false')]),
 'GameFlagsSetting':(8,[('Value',0,'int',None),('Default',4,'bool','false')]),
 'SlotState':(16,[('Label',0,'string',None),('Value',8,'enum:SlotStateType',None),('AvailableInRanked',12,'bool','true'),('AvailableInModerated',13,'bool','true')]),
 'CareerStatData':(24,[('StatType',0,'enum:CareerStatType',None),('StatLabel',4,'string',None),('IsRatio',20,'bool','false'),('NoSummary',21,'bool','false'),('RatioNumerator',12,'enum:CareerStatOrder',None),('RatioDenominator',16,'enum:CareerStatOrder',None)]),
 'PostGameStatData':(12,[('StatType',0,'enum:PostGameStatType',None),('StatLabel',4,'string',None)]),
 'UIComponentTickerGroup':(16,[('Name',0,'string',None),('HTTPAddress',8,'string',None)]),
 'FontDesc':(16,[('Name',0,'string',None),('Size',8,'uint',None),('Bold',12,'bool','false')]),
 'RGBColor':(12,[('R',0,'float',None),('G',4,'float',None),('B',8,'float',None)]),
 'Vector2':(8,[('x',0,'float','0.0'),('y',4,'float','0.0')]),
 'ArchivableMovie':(32,[('ProgressLock',0,'uint','0'),('MovieImage',4,'ref',None),('FileName',8,'string',None),('OnScreenName',16,'string',None),('Description',24,'string',None)]),
 'RadiusDecalComponent':(64,[('Texture',0,'ref',None),('Texture2',4,'ref',None),('Style',8,'enum:ShadowType',None),('OpacityMin',12,'percent','1.0'),('OpacityMax',16,'percent','1.0'),('OpacityPulsesPerSecond',20,'float','0.0'),('RotationMin',24,'float','0.0'),('RotationMax',28,'float','0.0'),('RotationDegreesPerSecond',32,'float','0.0'),('MinRadius',36,'float','1.0'),('MaxRadius',40,'float','1.0'),('RadiusPulsesPerSecond',44,'float','0.0'),('PositionOffsetMin',48,'float','0.0'),('PositionOffsetMax',52,'float','0.0'),('PositionOffsetPulsesPerSecond',56,'float','0.0')]),
}
fields['DefaultValues']=(72,[
('Difficulty',0,'int','1'),('Gamma',4,'float','1.30'),('Brightness',8,'float','0.50'),('Contrast',12,'float','1.30'),('VolumeMusic',16,'float','0.45'),('VolumeFX',20,'float','0.70'),('VolumeVoice',24,'float','0.70'),('VolumeAmbient',28,'float','0.50'),('VolumeMovie',32,'float','0.70'),('ScrollSpeed',36,'float','1.00'),('ScrollMagnetism',40,'int','2'),
('ButtonIcon',60,'bool','true'),('ShowCommandList',61,'bool','true'),('HealthBars',62,'bool','true'),('HighlightPads',63,'bool','true'),('ToolTips',64,'bool','true'),('Subtitles',65,'bool','false'),('InvertRotate',66,'bool','true'),('Telemetry',67,'bool','true'),('DefaultFaction',44,'int','-1'),('VisionCamVisible',68,'bool','true'),('VisionCamZoom',48,'int','1'),('ScrollSpeedMin',52,'float','0.2'),('ScrollSpeedMax',56,'float','2.0')])
fields['InputButton']=(8,[('ButtonName',0,'enum:JoyPadButtonType',None),('ButtonAction',4,'enum:JoyPadActionType',None)])

q=lambda x:'nullptr' if x is None else json.dumps(x)
lines=['// Complete UIConfigList nested layouts. Module blob 2d2f471c8008b7c2e1f78e39752d0726da395cea.', '// Included inside UIConfigDetail::Builder; scalar addresses are corrected once by the parent.']
lines+=['int EnumValue(const char* type,const std::string& text){']
for name,fallback in enums.items():
 entries=json.loads((e/'enums'/f'{name}.json').read_text())['entries']
 lines+=[f' if(std::strcmp(type,{q(name)})==0){{const EnumNameValue table[]={{'+','.join('{'+q(x['name'])+','+str(x['value'])+'}' for x in entries)+f'}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return {fallback};}}']
lines+=[' throw std::invalid_argument("Unknown UIConfig nested enum.");','}',
'void EnumAttr(const Node& n,const char* attr,unsigned char* p,const char* type,const char* fallback=nullptr){auto i=n.attributes.find(attr);if(i!=n.attributes.end()||fallback)Word(p,static_cast<U32>(EnumValue(type,i==n.attributes.end()?fallback:i->second)));}',
'void NestedScalar(const Node& n,const char* attr,unsigned char* p,const char* kind,const char* fallback=nullptr){auto i=n.attributes.find(attr);if(i==n.attributes.end()&&!fallback)return;std::string v=i==n.attributes.end()?fallback:i->second;if(std::strcmp(kind,"bool")==0)*p=ParseBoolean(v);else if(std::strcmp(kind,"int")==0)Word(p,ParseSigned(v));else if(std::strcmp(kind,"uint")==0)Word(p,ParseUnsigned(v));else if(std::strcmp(kind,"float")==0)Real(p,ParseFloat(v));else if(std::strcmp(kind,"percent")==0)Real(p,ParsePercentage(v));else throw std::invalid_argument("Unknown UIConfig nested scalar.");}',
'void NullableHash(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),4,1));Word(target,HashAssetId(i->second));t.Pop();}',
'void NullableRef(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),4,1));MarshalReference(t,target,i->second);t.Pop();}',
'void KindFlags(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;const EnumNameValue table[]={'+','.join('{'+q(x['name'])+','+str(x['value'])+'}' for x in json.loads((e/'enums/KindOfType.json').read_text())['entries'])+'};MarshalEnumBitset(i->second,table,sizeof(table)/sizeof(table[0]),149,284,reinterpret_cast<U32*>(p));for(unsigned j=0;j<9;++j)words.push_back(p+4*j);}',
'unsigned NestedSize(const char* type){']
sizes={n:s for n,(s,fs) in fields.items()}; sizes.update({'RadiusDecalTemplate':8,'ArchiveMovieList':12,'ObjectFilter':112,'FactionType':4,'MultiplayerLobbyData':48,'SkirmishLobbyData':24,'ColorTag':4,'AssetId':8,'String':8,'TypedAssetId':4,'SingleCommandList':16,'SingleCommandLine':24})
for name,size in sizes.items():lines+=[f' if(std::strcmp(type,{q(name)})==0)return {size};']
lines+=[' throw std::invalid_argument("Unknown UIConfig nested type size.");','}',
'void NamedList(const Node& n,const char* name,unsigned char* p,const char* type){List(n,name,p,NestedSize(type),[&](const Node& child,unsigned char* item){Marshal(type,child,item);});}',
'void Nullable(const Node& n,const char* child,unsigned char* p,const char* type){auto c=Child(n,child);if(!c)return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),NestedSize(type),1));Marshal(type,*c,target);t.Pop();}',
'void NestedChildString(const Node& n,const char* name,unsigned char* p){auto c=Child(n,name);if(c){Text(c->value,p);}}',
'void Marshal(const char* type,const Node& n,unsigned char* p){']
for name,(size,fs) in fields.items():
 lines+=[f' if(std::strcmp(type,{q(name)})==0){{']
 for a,offset,kind,default in fs:
  if kind.startswith('enum:'):lines+=[f'  EnumAttr(n,{q(a)},p+{offset},{q(kind[5:])},{q(default)});']
  elif kind=='string':lines+=[f'  String(n,{q(a)},p+{offset},{q(default)});']
  elif kind=='ref':lines+=[f'  Ref(n,{q(a)},p+{offset},{q(default)});']
  else:lines+=[f'  NestedScalar(n,{q(a)},p+{offset},{q(kind)},{q(default)});']
 if name=='RadiusDecalComponent':lines+=['  if(auto c=Child(n,"Color"))Marshal("ColorTag",*c,p+60);']
 lines+=['  return;',' }']
lines+=r'''
 if(std::strcmp(type,"SingleCommandList")==0){String(n,"CategoryName",p);NamedList(n,"CommandLine",p+8,"SingleCommandLine");return;}
 if(std::strcmp(type,"SingleCommandLine")==0){String(n,"Purpose",p);String(n,"Description",p+8);NamedList(n,"Input",p+16,"InputButton");return;}
 if(std::strcmp(type,"ColorTag")==0){U32 red=ParseUnsigned(Value(n,"R","0"))&255u,green=ParseUnsigned(Value(n,"G","0"))&255u,blue=ParseUnsigned(Value(n,"B","0"))&255u,alpha=ParseUnsigned(Value(n,"A","255"))&255u;Word(p,(alpha<<24)|(red<<16)|(green<<8)|blue);return;}
 if(std::strcmp(type,"RadiusDecalTemplate")==0){NamedList(n,"Component",p,"RadiusDecalComponent");return;}
 if(std::strcmp(type,"ArchiveMovieList")==0){EnumAttr(n,"Faction",p,"FactionType","Soviet");NamedList(n,"Movie",p+4,"ArchivableMovie");return;}
 if(std::strcmp(type,"MultiplayerLobbyData")==0){NestedChildString(n,"LobbyTitleLabelSpec",p);NestedChildString(n,"UnmoderatedLobbyTitleTemplateSpec",p+8);NestedChildString(n,"ModeratedLobbyTitleTemplateSpec",p+16);NestedChildString(n,"PlayerNameLabelSpec",p+24);NestedChildString(n,"SlotLabelSpec",p+32);NamedList(n,"SlotState",p+40,"SlotState");return;}
 if(std::strcmp(type,"SkirmishLobbyData")==0){NestedChildString(n,"LoadMusic",p);NestedChildString(n,"SlotLabelSpec",p+8);NamedList(n,"SlotState",p+16,"SlotState");return;}
 if(std::strcmp(type,"FactionType")==0){Word(p,static_cast<U32>(EnumValue(type,n.value)));return;}
 if(std::strcmp(type,"AssetId")==0||std::strcmp(type,"String")==0){Text(n.value,p);return;}
 if(std::strcmp(type,"TypedAssetId")==0){Word(p,HashAssetId(n.value));return;}
 if(std::strcmp(type,"ObjectFilter")==0){
  ObjectFilterInput input;
  // Original scalar enum helpers63778/63789 compare C strings. Scope this adapter
  // normalization to UIConfigList; shared ObjectFilter users retain their accepted input contract.
  for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=(a.first=="Rule"||a.first=="Alignment")?std::string(a.second.c_str()):a.second;for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(c.value);}auto object=reinterpret_cast<ObjectFilter*>(p);MarshalObjectFilter(input,object,t);
  for(unsigned at=0;at<88;at+=4)words.push_back(p+at);words.push_back(p+96);words.push_back(p+104);
  if(object->status)for(unsigned j=0;j<7;++j)words.push_back(object->status+j);if(object->statusExclude)for(unsigned j=0;j<7;++j)words.push_back(object->statusExclude+j);for(unsigned j=0;j<object->includeCount;++j)words.push_back(object->includeThings+j);for(unsigned j=0;j<object->excludeCount;++j)words.push_back(object->excludeThings+j);return;
 }
 throw std::invalid_argument("Unknown UIConfig nested type.");
}
'''.strip().splitlines()
out.write_text('\n'.join(lines)+'\n')
manifest={'sourceBlob':'2d2f471c8008b7c2e1f78e39752d0726da395cea','componentValidated':False,'enums':{n:dict(fallback=f,entries=json.loads((e/'enums'/f'{n}.json').read_text())['entries']) for n,f in enums.items()},'types':{n:dict(bytes=size,fields=[dict(name=a,offset=o,kind=k,default=d) for a,o,k,d in fs]) for n,(size,fs) in fields.items()},'sizes':sizes}
manifest['types']['SingleCommandList']=dict(bytes=16,fields=[dict(name='CategoryName',offset=0,kind='string',default=None)],children=[dict(name='CommandLine',offset=8,kind='list',type='SingleCommandLine')])
manifest['types']['SingleCommandLine']=dict(bytes=24,fields=[dict(name='Purpose',offset=0,kind='string',default=None),dict(name='Description',offset=8,kind='string',default=None)],children=[dict(name='Input',offset=16,kind='list',type='InputButton')])
manifest['types']['ColorTag']=dict(bytes=4,fields=[dict(name=n,offset=0,kind='packed-byte',default=d) for n,d in [('R','0'),('G','0'),('B','0'),('A','255')]])
manifest['types']['RadiusDecalComponent']['children']=[dict(name='Color',offset=60,kind='inline',type='ColorTag')]
manifest['types']['RadiusDecalTemplate']=dict(bytes=8,fields=[],children=[dict(name='Component',offset=0,kind='list',type='RadiusDecalComponent')])
manifest['types']['ArchiveMovieList']=dict(bytes=12,fields=[dict(name='Faction',offset=0,kind='enum:FactionType',default='Soviet')],children=[dict(name='Movie',offset=4,kind='list',type='ArchivableMovie')])
manifest['types']['MultiplayerLobbyData']=dict(bytes=48,fields=[],children=[dict(name=n,offset=8*i,kind='inline',type='String') for i,n in enumerate(['LobbyTitleLabelSpec','UnmoderatedLobbyTitleTemplateSpec','ModeratedLobbyTitleTemplateSpec','PlayerNameLabelSpec','SlotLabelSpec'])]+[dict(name='SlotState',offset=40,kind='list',type='SlotState')])
manifest['types']['SkirmishLobbyData']=dict(bytes=24,fields=[],children=[dict(name='LoadMusic',offset=0,kind='inline',type='String'),dict(name='SlotLabelSpec',offset=8,kind='inline',type='String'),dict(name='SlotState',offset=16,kind='list',type='SlotState')])
for n,sz in [('FactionType',4),('AssetId',8),('String',8),('TypedAssetId',4)]:manifest['types'][n]=dict(bytes=sz,fields=[],text=True,textKind='enum:FactionType' if n=='FactionType' else 'hash' if n=='TypedAssetId' else 'string')
manifest['types']['ObjectFilter']=dict(bytes=112,fields=[dict(name=n,kind=k,offset=o,default=d) for n,k,o,d in [('id','hash',0,None),('Rule','objectfilter-rule',4,'NONE'),('Relationship','objectfilter-relationship-flags',8,None),('Alignment','objectfilter-alignment',12,'NONE'),('Include','kindof-flags',16,None),('Exclude','kindof-flags',52,None),('StatusBitFlags','objectstatus-nullable-flags',88,None),('StatusBitFlagsExclude','objectstatus-nullable-flags',92,None)]],children=[dict(name='IncludeThing',offset=96,kind='list',type='TypedAssetId'),dict(name='ExcludeThing',offset=104,kind='list',type='TypedAssetId')])
(r/'recovery/evidence/xml-uiconfiglist/nested-contract.json').write_text(json.dumps(manifest,indent=2)+'\n')
