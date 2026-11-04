#include "Xmlpowers.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#include <algorithm>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native { namespace {
using U32=std::uint32_t;
#include "XmlpowersEnums.h"
static_assert(sizeof(void*)==4,"Xmlpowers requires x86");
static_assert(sizeof(ObjectFilter)==112,"ObjectFilter ABI");
enum Kind { Float, Unsigned, Signed, Boolean, Time, Angle, Percent, Velocity,
 Reference, AssetId, String, NullableString, VoiceHash, ModelBits, StatusBits,
 ModelEnum, NullableStatusEnum, TargetEnum, RestrictionEnum, PowerBits, OclBits,
 OptionBits, DispositionBits, ParabolicBits, WeaponSlot };
struct Field {const char* name;unsigned offset;Kind kind;const char* fallback;};
#define X(n,o,k) {n,o,k,nullptr}
#define D(n,o,k,d) {n,o,k,d}
const Field powerFields[]={
 X("DisplayName",4,String),X("Description",12,String),X("TargetType",20,TargetEnum),
 D("NumTargets",24,Signed,"1"),X("Flags",28,PowerBits),X("ReloadTime",32,Time),
 X("InitiateSound",36,Reference),X("InitiateAtLocationSound",40,Reference),
 X("ViewObjectDuration",44,Time),X("ViewObjectRange",48,Float),X("RadiusCursorRadius",52,Float),
 X("PreventConditions",56,StatusBits),X("RequiredConditions",84,StatusBits),
 X("DisallowedTargetModelStates",112,ModelBits),X("RequiredTargetObjectStatus",172,StatusBits),
 X("DisallowedTargetObjectStatus",200,StatusBits),D("MaxCastRange",228,Float,"1000000"),
 X("MinCastRange",232,Float),X("ForbiddenObjectRange",236,Float),
 D("EvaEventToPlayOnSuccess",240,Reference,""),D("EvaEventToPlayWhenSelectingTarget",244,Reference,""),
 D("EvaEventToPlayOnInitiateOwner",248,Reference,""),D("EvaEventToPlayOnInitiateAlly",252,Reference,""),
 D("EvaEventToPlayOnInitiateEnemy",256,Reference,""),
 X("NameOfVoiceNameToUseAsInitiateIntendToDoVoice",260,VoiceHash),
 X("NameOfVoiceNameToUseAsInitiateIntendToDoAfterMoveVoice",264,VoiceHash),
 X("NameOfVoiceNameToUseAsInitiateIntendToDoWhileUnderAttackVoice",268,VoiceHash),
 X("NameOfVoiceNameToUseAsInitiateIntendToDoAfterMoveWhileUnderAttackVoice",272,VoiceHash),
 X("NameOfVoiceNameToUseAsEnterStateInitiateIntendToDoVoice",276,VoiceHash),
 X("NameOfVoiceNameToUseAsEnterStateInitiateIntendToDoAfterMoveVoice",280,VoiceHash),
 X("NameOfVoiceNameToUseAsEnterStateInitiateIntendToDoWhileUnderAttackVoice",284,VoiceHash),
 X("NameOfVoiceNameToUseAsEnterStateInitiateIntendToDoAfterMoveWhileUnderAttackVoice",288,VoiceHash),
 D("RestrictionType",292,RestrictionEnum,"UNRESTRICTED"),D("Money",296,Signed,"0"),
 X("TimerImage",300,Reference),X("ReachableTargetsCircleDisplayRadius",304,Float),
 X("RequiredPlayerTech",308,Reference),D("WaypointModeTerminal",548,Boolean,"true")
};
const Field oclFields[]={D("Flags",4,OclBits,"NONE")};
const Field createFields[]={
 X("PutInContainer",4,AssetId),X("CreateFX",8,Reference),D("Count",12,Unsigned,"1"),
 X("Options",16,OptionBits),X("Disposition",24,DispositionBits),D("VelocityScale",28,Float,"1.0"),
 X("MinForceMagnitude",32,Float),X("MaxForceMagnitude",36,Float),
 X("MinLifetime",40,Time),X("MaxLifetime",44,Time),X("MinDistanceAFormation",48,Float),
 X("MinDistanceBFormation",52,Float),X("MaxDistanceFormation",56,Float),X("FadeTime",60,Time),
 D("StartingModelConditions",64,ModelBits,"INVALID"),D("TempModelCondition",124,ModelEnum,"INVALID"),
 D("TempModelConditionTime",128,Time,"0s"),X("DispositionIntensity",132,Velocity),
 X("DestinationPlayer",136,NullableString),X("WaypointSpawn",140,String),X("DispositionAngle",148,Angle),
 X("MinForcePitch",152,Angle),X("MaxForcePitch",156,Angle),D("MinForceYaw",160,Angle,"0d"),
 D("MaxForceYaw",164,Angle,"359d"),X("OrientationOffset",168,Angle),X("FadeSound",172,Reference),
 D("MinHordeDensity",176,Percent,"100"),D("MaxHordeDensity",180,Percent,"100"),
 D("MinHealth",184,Float,"1.0"),D("MaxHealth",188,Float,"1.0"),
 X("InvulnerableTime",192,Time),X("StartingBusyTime",196,Time),X("JustBuiltFrames",200,Time),
 X("VeterancyLevel",204,Signed),D("UseUpgradedLocomotor",244,Boolean,"false"),
 D("DisabledWhileBusy",245,Boolean,"false"),X("FireSpecialPowerTemplate",208,Reference),
 X("StatusToCopyFromParent",212,NullableStatusEnum)
};
const Field attackFields[]={D("NumberOfShots",4,Unsigned,"1"),D("WeaponSlot",8,WeaponSlot,"PRIMARY_WEAPON"),X("DeliveryDecalRadius",12,Float)};
const Field fireFields[]={X("Weapon",4,Reference)};
const Field flingFields[]={X("CreateObject",4,AssetId),X("Speed",8,Float),D("Height",12,Float,"0.0"),
 D("UsePrimaryObjectForOffsets",48,Boolean,"false"),D("Count",16,Unsigned,"1"),D("InitialDelay",20,Time,"0s")};
const Field strafeFields[]={X("CreateObject",4,AssetId),D("Count",8,Unsigned,"1"),D("Height",12,Float,"0.0"),
 D("DistanceFromTarget",16,Float,"0.0"),D("MaintainOffestToTarget",28,Boolean,"true")};
const Field parabolicFields[]={X("Options",4,ParabolicBits),X("CreateObject",8,AssetId),
 D("InitialDelay",12,Time,"0s"),D("ApproachTime",16,Time,"0s"),
 D("StartDistanceFromTarget",20,Float,"0.0"),D("EndDistanceFromTarget",24,Float,"0.0"),
 D("CruiseHeight",28,Float,"0.0"),D("AttackHeight",32,Float,"0.0"),
 D("ApproachRoll",36,Angle,"0d"),D("LeaveRoll",40,Angle,"0d")};
const Field vectorFields[]={X("x",0,Float),X("y",4,Float),X("z",8,Float)};
const Field rangeFields[]={X("Low",0,Float),X("High",4,Float)};
#undef X
#undef D
template<size_t N> U32 Enum(const std::string& s,const EnumNameValue(&v)[N],int fallback){
 for(const auto& n:v)if(s==n.name)return n.value;return static_cast<U32>(fallback);
}
U32 RawHash(const std::string& text) {
 // ResourceMarshaling HashAssetId's FastHashFn, with no CharToLower pass.
 const auto* p=reinterpret_cast<const unsigned char*>(text.c_str());
 U32 size=static_cast<U32>(std::strlen(text.c_str())),h=size;
 if(!size)return 0;
 auto sh=[](const unsigned char* b)->U32{return b[0]|(U32(b[1])<<8);};
 auto sb=[](unsigned char c)->U32{return static_cast<U32>(static_cast<std::int8_t>(c));};
 for(U32 n=size/4;n;--n,p+=4){h+=sh(p);h^=(sh(p+2)^(h<<5))<<11;h+=h>>11;}
 switch(size&3){case 3:h+=sh(p);h^=((sb(p[2])<<2)^h)<<16;h+=h>>11;break;
 case 2:h+=sh(p);h^=h<<11;h+=h>>17;break;case 1:h+=sb(*p);h^=h<<10;h+=h>>1;break;}
 h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return (h<<10)^h;
}
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

const XmlpowersNode* Child(const XmlpowersNode& n,const char* name){
 const XmlpowersNode* out=nullptr;for(const auto& c:n.children)if(c.name==name){out=&c;}return out;
}
struct Builder {
 Relo::Tracker data;std::vector<void*> words;std::vector<ObjectFilter*> filters;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Bits(void* p,const std::string& s,Kind k){
 auto w=static_cast<U32*>(p);unsigned count=1;
 switch(k){
 case ModelBits:MarshalModelCondition(s,w);count=15;break;
 case StatusBits:MarshalObjectStatus(s,w);count=7;break;
 case PowerBits:MarshalEnumBitset(s,SpecialPowerTemplateFlagNames,31,25,31,w);break;
 case OclBits:MarshalEnumBitset(s,OCLTypeNames,2,1,2,w);break;
 case OptionBits:MarshalEnumBitset(s,OCNuggetOptionNames,44,38,44,w);count=2;break;
 case DispositionBits:MarshalEnumBitset(s,DispositionTypeNames,21,13,21,w);break;
 case ParabolicBits:MarshalEnumBitset(s,ParabolicOptionNames,1,0,1,w);break;
 default:throw std::invalid_argument("Invalid bitset kind");}
 for(unsigned i=0;i<count;++i)words.push_back(w+i);
 }
 void Fields(const XmlpowersNode& n,unsigned char* p,const Field* fs,size_t count){
 for(size_t i=0;i<count;++i){const auto& f=fs[i];auto it=n.attributes.find(f.name);
 if(it==n.attributes.end()&&!f.fallback)continue;
 std::string s=it==n.attributes.end()?f.fallback:it->second;void* at=p+f.offset;
 switch(f.kind){
 case Float:Real(at,ParseFloat(s));break;case Unsigned:Word(at,ParseUnsigned(s));break;
 case Signed:Word(at,static_cast<U32>(ParseSigned(s)));break;
 case Boolean:*static_cast<unsigned char*>(at)=ParseBoolean(s);break;
 case Time:Real(at,InitializeTime(s.c_str()));break;case Angle:Real(at,InitializeAngle(s.c_str()));break;
 case Percent:Real(at,ParsePercentage(s));break;
 case Velocity:Real(at,static_cast<float>(static_cast<double>(1.0f/5.0f)*ParseFloat(s)));break;
 case Reference:MarshalReference(data,at,s);break;case AssetId:Word(at,HashAssetId(s));break;
 case String:MarshalString(data,at,s);words.push_back(at);break;
 case NullableString:{void* st=data.Push(reinterpret_cast<void**>(at),8,1);MarshalString(data,st,s);words.push_back(st);data.Pop();break;}
 case VoiceHash:{void* h=data.Push(reinterpret_cast<void**>(at),4,1);Word(h,RawHash(s));data.Pop();break;}
 case ModelEnum:Word(at,Enum(s,modelConditions,233));break;
 case NullableStatusEnum:{void* e=data.Push(reinterpret_cast<void**>(at),4,1);Word(e,Enum(s,objectStatus,198));data.Pop();break;}
 case TargetEnum:Word(at,Enum(s,SpecialPowerTemplateTargetTypeNames,0));break;
 case RestrictionEnum:Word(at,Enum(s,SpecialPowerRestrictionTypeNames,1));break;
 case WeaponSlot:{static const EnumNameValue names[]={{"PRIMARY_WEAPON",0},{"SECONDARY_WEAPON",1},{"TERTIARY_WEAPON",2},{"QUATERNARY_WEAPON",3},{"QUINARY_WEAPON",4},{"NO_WEAPON",5}};Word(at,Enum(s,names,5));break;}
 default:Bits(at,s,f.kind);break;
 }}}
 template<size_t N>void Fields(const XmlpowersNode& n,unsigned char* p,const Field(&f)[N]){Fields(n,p,f,N);}
 void Filter(const XmlpowersNode* n,ObjectFilter* p){
 if(!n)return;

 ObjectFilterInput input;input.attributes=n->attributes;
 for(const auto& c:n->children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(c.text);}
 MarshalObjectFilter(input,p,data);filters.push_back(p);
 }
 void LeafList(const XmlpowersNode& n,const char* name,unsigned char* p,bool reference){
 std::vector<const XmlpowersNode*> nodes;for(const auto& c:n.children)if(c.name==name)nodes.push_back(&c);
 Word(p,static_cast<U32>(nodes.size()));if(nodes.empty())return;
 auto values=static_cast<U32*>(data.Push(reinterpret_cast<void**>(p+4),4,static_cast<U32>(nodes.size())));
 for(size_t i=0;i<nodes.size();++i){
 if(reference)MarshalReference(data,values+i,nodes[i]->text);else Word(values+i,HashAssetId(nodes[i]->text));}
 data.Pop();
 }
 void Vector(const XmlpowersNode* n,unsigned char* p,bool range=false,bool pointer=true){
 if(!n)return;
 unsigned char* target=p;if(pointer)target=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(p),range?8:12,1));
 Fields(*n,target,range?rangeFields:vectorFields,range?2:3);if(pointer)data.Pop();
 }
 void Dependency(const XmlpowersNode* n,unsigned char* field){
 if(!n)return;

 auto p=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(field),40,1));
 const char* names[]={"RequiredModelConditionsAny","ForbiddenModelConditions","RequiredObjectStatusAny"};
 for(unsigned i=0;i<3;++i){auto a=n->attributes.find(names[i]);if(a!=n->attributes.end()){void* mask=data.Push(reinterpret_cast<void**>(p+4*i),i==2?28:60,1);Bits(mask,a->second,i==2?StatusBits:ModelBits);data.Pop();}}
 LeafList(*n,"RequiredObject",p+12,false);LeafList(*n,"ForbiddenUpgrade",p+20,false);LeafList(*n,"NeededUpgrade",p+28,false);
 auto filter=Child(*n,"ObjectFilter");if(filter){auto value=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(p+36),112,1));Filter(filter,value);data.Pop();}
 data.Pop();
 }
 void Nugget(const XmlpowersNode& n,unsigned char* p,unsigned group){
 static const Field* fields[]={createFields,attackFields,fireFields,flingFields,strafeFields,parabolicFields};
 static const size_t counts[]={sizeof(createFields)/sizeof(Field),3,1,6,5,10};

 Fields(n,p,fields[group],counts[group]);
 if(group==0){Vector(Child(n,"Offset"),p+216);LeafList(n,"RequiredUpgrade",p+220,true);LeafList(n,"ForbiddenUpgrade",p+228,true);LeafList(n,"CreateObject",p+236,false);}
 if(group==3){Vector(Child(n,"Offset"),p+24);Vector(Child(n,"DistanceFromTarget"),p+28,true,false);Vector(Child(n,"AngleAroundTarget"),p+36,true,false);Vector(Child(n,"DistanceAtTarget"),p+44,true);}
 if(group==4){Vector(Child(n,"InterOffset"),p+20);Vector(Child(n,"IntraOffset"),p+24);}
 if(group==5)Vector(Child(n,"Offset"),p+44);
 }
 Relo::Chunk Finish(bool big){
 if(big){for(void* p:words){auto b=static_cast<unsigned char*>(p);std::swap(b[0],b[3]);std::swap(b[1],b[2]);}for(auto f:filters)EndianObjectFilter(f);}
 return data.MakeRelocatable(big,0);
 }
};
} // namespace
Relo::Chunk CompileXmlpowers(const XmlpowersNode& input,bool bigEndian){
 Builder b;unsigned char* root=nullptr;
 if(input.name=="SpecialPowerTemplate"){

 b.data.Push(reinterpret_cast<void**>(&root),552,1);b.Fields(input,root,powerFields);
 b.Filter(Child(input,"ObjectFilter"),reinterpret_cast<ObjectFilter*>(root+312));
 b.Filter(Child(input,"ForbiddenObjectFilter"),reinterpret_cast<ObjectFilter*>(root+424));
 b.Dependency(Child(input,"GameDependency"),root+536);b.LeafList(input,"ForbiddenPlayerTech",root+540,true);
 }else if(input.name=="ObjectCreationList"){

 b.data.Push(reinterpret_cast<void**>(&root),56,1);b.Fields(input,root,oclFields);
 const char* names[]={"CreateObject","Attack","FireWeapon","FlingObject","StrafeAreaSquad","ParabolicCurve"};
 const unsigned sizes[]={248,16,8,52,32,48};
 for(unsigned group=0;group<6;++group){std::vector<const XmlpowersNode*> nodes;for(const auto& c:input.children)if(c.name==names[group])nodes.push_back(&c);
 auto list=root+8+8*group;b.Word(list,static_cast<U32>(nodes.size()));if(nodes.empty())continue;
 auto items=static_cast<unsigned char*>(b.data.Push(reinterpret_cast<void**>(list+4),sizes[group],static_cast<U32>(nodes.size())));
 for(size_t i=0;i<nodes.size();++i)b.Nugget(*nodes[i],items+sizes[group]*i,group);b.data.Pop();}
 }else throw std::invalid_argument("Unknown Xmlpowers type");
 return b.Finish(bigEndian);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
