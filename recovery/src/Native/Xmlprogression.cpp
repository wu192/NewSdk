#include "Xmlprogression.h"
#include "XmlprogressionEnums.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#include <cctype>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native {
namespace {
using N=XmlprogressionNode;using U=Relo::U32;

void Check(bool ok,const char* msg){if(!ok)throw std::invalid_argument(msg);}
const N* Child(const N& n,const char* name){const N* out=nullptr;for(const auto& c:n.children)if(c.name==name){out=&c;}return out;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?std::string(d):i->second;}

void W(void* p,U x){std::memcpy(p,&x,4);}void F(void* p,float x){std::memcpy(p,&x,4);}void Swap(void* p){U x;std::memcpy(&x,p,4);W(p,(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24));}
float Units(const std::string& s,bool angle){auto cut=s.find(angle?"r":"ms");float scale=angle?1.0f:0.001f;if(cut==std::string::npos){cut=s.find(angle?"d":"s");scale=angle&&cut!=std::string::npos?static_cast<float>(3.14159265358979323846)/180.0f:1.0f;}return static_cast<float>(static_cast<double>(ParseFloat(s.substr(0,cut)))*static_cast<double>(scale));}
struct Table {const EnumNameValue* p;size_t n;int fallback;unsigned bits;};
#define T(name,f,b) {P##name,sizeof(P##name)/sizeof(P##name[0]),f,b}
const Table enums[]={T(LogicCommandType,10,0),T(LogicCommandOptions,7,11),T(AISpecialPowerHeuristic,4,0),T(AISpecialPowerManager,3,0),T(MicroManagerState,0,10),T(AITargetLocationType,1,2),T(AIAoeAttackPowerLocationPreference,1,0),T(AIAoeAttackPowerEmanationLocation,0,0),T(ShadowType,3,0),T(KindOfType,149,284),T(FactionType,0,0)};
#undef T
enum Kind{Int,UInt,Real,Time,Angle,Bool,Ref,String,Hash,Enum,Bits,PtrReal,PtrInt,PtrBool,PtrRef,Model};
struct Field{const char* name;unsigned offset;Kind kind;const char* fallback;int table;};
#define A(n,o,k,d) {n,o,k,d,0}
#define E(n,o,d,t) {n,o,Enum,d,t}
#define B(n,o,d,t) {n,o,Bits,d,t}
const Field button[]={A("Image",0,Ref,nullptr),A("Title",4,String,nullptr),A("Description",12,String,nullptr),A("TypeDescription",20,String,nullptr)};
const Field store[]={E("Faction",4,nullptr,10)};
const Field purchase[]={A("PlayerTech",4,Ref,nullptr),A("StateData",8,Ref,nullptr)};
const Field logic[]={E("Type",4,"NONE",0),B("Options",8,nullptr,1)};
const Field tech[]={A("TechPointsRequired",4,UInt,"0")};
const Field rank[]={A("ExperiencePointsToNextRank",4,UInt,"0"),A("TechPointsGranted",8,UInt,"0")};
const Field experience[]={A("Rank",4,Int,"0"),A("RequiredExperience",8,Int,"1"),A("ExperienceAward",12,Int,"0"),A("ExperienceAwardOwnGuysDie",16,Int,"0"),A("Prerequisite",20,PtrRef,nullptr),A("LevelUpOCL",24,Ref,nullptr),A("ModelConditionState",28,Model,nullptr),A("InformUpdateModule",104,Bool,"false"),A("SinglePlayerOnly",105,Bool,"false"),A("MultiPlayerOnly",106,Bool,"false"),A("ShowLevelUpTint",107,Bool,"false"),A("LevelUpTintPreColorTime",32,Time,"0.0s"),A("LevelUpTintPostColorTime",36,Time,"0.0s"),A("LevelUpTintSustainColorTime",40,Time,"0.0s"),A("LevelUpTintFrequency",44,Real,"0.0"),A("LevelUpTintAmplitude",48,Real,"0.0")};
const Field ai[]={E("Heuristic",0,nullptr,2),E("Manager",4,nullptr,3),A("Distance",8,Real,"100.0"),A("HealthFraction",12,Real,"0.0"),B("TargetObjectInclude",16,nullptr,9),B("TargetObjectExclude",52,nullptr,9),A("UseOnlyWhenPowerAdvantageIsAbove",88,PtrReal,nullptr),A("NeverUseWhileRetreating",164,Bool,"false"),A("NeverUseWhilePullingBack",165,Bool,"false"),A("InvulnerabilityDamageLookAheadTime",92,Time,"1s"),A("InvulnerabilityDamageLookBehindTime",96,Time,"1s"),A("InvulnerabilityDamageFractionWhenOutOfRangeOfTarget",100,Real,".3"),A("InvulnerabilityDamageFractionWhenInRangeOfTarget",104,Real,".6"),A("InvulnerabilityAvoidUseIfAngleToNextPathNodeIsMoreThan",108,Angle,"360d"),A("MaxTargetSpeed",112,Real,"99999.0"),A("RequireKillTargets",166,Bool,"true"),A("MinKillValue",116,Real,"0.0"),A("MinTargetsHit",120,Int,"1"),A("MinEnemiesNearby",124,Int,"1"),A("NextLogicCommand",128,PtrRef,nullptr),B("UseInMicroManagerState",132,"",4),A("UseNearTacticalTarget",167,Bool,"false"),A("UseWhenOutOfAmmo",168,Bool,"false"),A("UpdateObject",136,PtrRef,nullptr),A("SearchRadius",140,Real,"150.0"),B("Location",144,"Water Land",5),A("MaxUpdatables",148,PtrInt,nullptr),A("UseToCounterGarrison",152,PtrBool,nullptr)};
const Field weapon[]={A("Weapon",0,Ref,nullptr),A("IsDisablingAttack",24,Bool,"false"),A("AffectsFriendlies",25,Bool,"false"),A("AffectsSelf",26,Bool,"false"),A("TotalValueGreaterThan",4,Int,"0"),E("AoeAttackPowerLocationPreference",8,"PREFER_NEAREST",6),A("AoeAttackPowerBlockAreaAfterActivationTime",12,Time,"0s"),E("AoeAttackEmanationLocation",16,"EMANATE_FROM_SELF",7),A("ToggleRangeHysteresis",20,Real,"0.0"),A("ToggleShortRangeTargetForcesTarget",27,Bool,"false")};
const Field decal[]={A("Texture",0,Ref,nullptr),A("Texture2",4,Ref,nullptr),E("Style",8,nullptr,8),A("OpacityMin",12,Real,"1.0"),A("OpacityMax",16,Real,"1.0"),A("OpacityPulsesPerSecond",20,Real,"0.0"),A("RotationMin",24,Real,"0.0"),A("RotationMax",28,Real,"0.0"),A("RotationDegreesPerSecond",32,Real,"0.0"),A("MinRadius",36,Real,"1.0"),A("MaxRadius",40,Real,"1.0"),A("RadiusPulsesPerSecond",44,Real,"0.0"),A("PositionOffsetMin",48,Real,"0.0"),A("PositionOffsetMax",52,Real,"0.0"),A("PositionOffsetPulsesPerSecond",56,Real,"0.0")};
#undef A
#undef E
#undef B
struct Builder{
 Relo::Tracker t;bool big,correct;std::vector<void*> swaps;std::vector<ObjectFilter*> filters;
 Builder(bool b,bool c):big(b),correct(c){}
 void Scalar(void* p,U x){W(p,x);if(correct)swaps.push_back(p);}
 void RealValue(void* p,float x){F(p,x);if(correct)swaps.push_back(p);}
 void* Push(void* owner,unsigned off,unsigned size,unsigned count=1){return t.Push(reinterpret_cast<void**>(static_cast<unsigned char*>(owner)+off),size,count);}
 void FieldValue(const N& n,unsigned char* p,const Field& f){auto it=n.attributes.find(f.name);if(it==n.attributes.end()&&!f.fallback)return;std::string s=it==n.attributes.end()?f.fallback:it->second;void* a=p+f.offset;
  switch(f.kind){case Int:Scalar(a,static_cast<U>(ParseSigned(s)));break;case UInt:Scalar(a,ParseUnsigned(s));break;case Real:RealValue(a,ParseFloat(s));break;case Time:RealValue(a,Units(s,false));break;case Angle:RealValue(a,Units(s,true));break;case Bool:*static_cast<unsigned char*>(a)=ParseBoolean(s);break;case Ref:MarshalReference(t,a,s);break;case String:MarshalString(t,a,s);if(correct)swaps.push_back(a);break;case Hash:Scalar(a,HashAssetId(s));break;
  case Enum:{const auto& e=enums[f.table];int v=e.fallback;for(size_t i=0;i<e.n;++i)if(s==e.p[i].name){v=e.p[i].value;break;}Scalar(a,static_cast<U>(v));break;}
  case Bits:{const auto& e=enums[f.table];auto words=reinterpret_cast<U*>(a);MarshalEnumBitset(s,e.p,e.n,e.fallback,e.bits,words);if(correct)for(unsigned i=0;i<(e.bits+31)/32;++i)swaps.push_back(words+i);break;}
  case PtrRef:{void* q=Push(p,f.offset,4);MarshalReference(t,q,s);t.Pop();break;}
  case PtrReal:{void* q=Push(p,f.offset,4);RealValue(q,ParseFloat(s));t.Pop();break;}
  case PtrInt:{void* q=Push(p,f.offset,4);Scalar(q,static_cast<U>(ParseSigned(s)));t.Pop();break;}
  case PtrBool:{void* q=Push(p,f.offset,1);*static_cast<unsigned char*>(q)=ParseBoolean(s);t.Pop();break;}
  case Model:{auto q=static_cast<U*>(Push(p,f.offset,60));MarshalModelCondition(s,q);if(correct)for(unsigned i=0;i<15;++i)swaps.push_back(q+i);t.Pop();break;}
  }
 }
 void Fields(const N& n,unsigned char* p,const Field* fs,size_t count,const char* children,bool allowIdentityId=false){for(size_t i=0;i<count;++i)FieldValue(n,p,fs[i]);}
 void Leaf(const N& n){}
 void List(const N& n,const char* name,unsigned char* p,unsigned off,bool refs){std::vector<const N*> items;for(const auto& c:n.children)if(c.name==name)items.push_back(&c);Scalar(p+off,static_cast<U>(items.size()));if(items.empty())return;auto q=static_cast<unsigned char*>(Push(p,off+4,4,static_cast<U>(items.size())));for(size_t i=0;i<items.size();++i){Leaf(*items[i]);if(refs)MarshalReference(t,q+4*i,items[i]->text);else Scalar(q+4*i,HashAssetId(items[i]->text));}t.Pop();}
 void Filter(const N* n,unsigned char* p,unsigned off){if(!n)return;ObjectFilterInput x;x.attributes=n->attributes;for(const auto& c:n->children){Leaf(c);(c.name=="IncludeThing"?x.includeThings:x.excludeThings).push_back(c.text);}auto q=static_cast<ObjectFilter*>(Push(p,off,112));MarshalObjectFilter(x,q,t);filters.push_back(q);t.Pop();}
 void AI(const N& n,unsigned char* p){/* AISpecialPowerInfo.id is schema pipeline metadata. */Fields(n,p,ai,sizeof(ai)/sizeof(Field),"ObjectFilter|SpecialPowerWeapon",true);Filter(Child(n,"ObjectFilter"),p,156);auto w=Child(n,"SpecialPowerWeapon");if(w){auto q=static_cast<unsigned char*>(Push(p,160,28));Fields(*w,q,weapon,sizeof(weapon)/sizeof(Field),"");t.Pop();}}
 void Color(const N* n,unsigned char* p,bool packed){if(!n)return;if(packed){U r=ParseUnsigned(Get(*n,"R","0"))&255,g=ParseUnsigned(Get(*n,"G","0"))&255,b=ParseUnsigned(Get(*n,"B","0"))&255,a=ParseUnsigned(Get(*n,"A","255"))&255;Scalar(p,(a<<24)|(r<<16)|(g<<8)|b);}else {const char* axes[]={"r","g","b","a"};for(unsigned i=0;i<4;++i)if(n->attributes.count(axes[i]))RealValue(p+4*i,ParseFloat(Get(*n,axes[i])));}}
 Relo::Chunk Finish(){if(big){for(void* p:swaps)Swap(p);if(correct)for(auto f:filters)EndianObjectFilter(f);}return t.MakeRelocatable(big,0);}
};
}
unsigned int XmlprogressionTypeId(const std::string& n){if(n=="FontSubstitution")return 91719417u;if(n=="FontTranslation")return 1318336226u;if(n=="FontDefaultSetting")return 1371131236u;if(n=="UICommandButton")return 134745024u;if(n=="ButtonSingleStateData")return 3048039928u;if(n=="PlayerTechStoreTemplate")return 1213037816u;if(n=="PurchasePlayerTechButtonTemplate")return 1566941920u;if(n=="LogicCommand")return 2101756272u;if(n=="PlayerTech")return 2131237081u;if(n=="PlayerTechRank")return 1802643394u;if(n=="PlayerTechUpgradeBinding")return 2140592493u;if(n=="ExperienceLevelTemplate")return 1874610847u;return 0;}
Relo::Chunk CompileXmlprogression(const N& input,bool big){const auto& name=input.name;if(name=="FontSubstitution"||name=="FontTranslation"||name=="FontDefaultSetting")return CompileXmlprogressionFont(input,big);if(name=="UICommandButton")return CompileXmlprogressionUICommandButton(input,big);unsigned size=name=="ButtonSingleStateData"?32:name=="PlayerTechStoreTemplate"?16:name=="PurchasePlayerTechButtonTemplate"?12:name=="LogicCommand"?28:name=="PlayerTech"?16:name=="PlayerTechRank"?20:name=="PlayerTechUpgradeBinding"?12:name=="ExperienceLevelTemplate"?108:0;Check(size!=0,"Unsupported progression root");Builder b(big,true);unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),size,1);
 if(name=="ButtonSingleStateData"){auto state=Child(input,"State");if(state)b.Fields(*state,root+4,button,4,"");}
 else if(name=="PlayerTechStoreTemplate"){b.Fields(input,root,store,1,"Row",true);b.Scalar(root+8,static_cast<U>(input.children.size()));if(!input.children.empty()){auto rows=static_cast<unsigned char*>(b.Push(root,12,8,static_cast<U>(input.children.size())));for(size_t i=0;i<input.children.size();++i){b.List(input.children[i],"Button",rows+8*i,0,true);}b.t.Pop();}}
 else if(name=="PurchasePlayerTechButtonTemplate"){b.Fields(input,root,purchase,2,"",true);}
 else if(name=="LogicCommand"){b.Fields(input,root,logic,sizeof(logic)/sizeof(Field),"SpecialPower|Upgrade|Object|AISpecialPowerInfo",true);const char* ns[]={"SpecialPower","Upgrade","Object"};for(unsigned i=0;i<3;++i){auto c=Child(input,ns[i]);if(c){b.Leaf(*c);auto q=b.Push(root,12+4*i,4);MarshalReference(b.t,q,c->text);b.t.Pop();}}auto aiNode=Child(input,"AISpecialPowerInfo");if(aiNode){auto q=static_cast<unsigned char*>(b.Push(root,24,172));b.AI(*aiNode,q);b.t.Pop();}}
 else if(name=="PlayerTech"){b.Fields(input,root,tech,1,"TechDependency",true);b.List(input,"TechDependency",root,8,true);}
 else if(name=="PlayerTechRank"){b.Fields(input,root,rank,2,"TechGranted",true);b.List(input,"TechGranted",root,12,true);}
 else if(name=="PlayerTechUpgradeBinding"){std::vector<const N*> items;for(const auto& c:input.children)items.push_back(&c);b.Scalar(root+4,static_cast<U>(items.size()));if(!items.empty()){auto q=static_cast<unsigned char*>(b.Push(root,8,8,static_cast<U>(items.size())));for(size_t i=0;i<items.size();++i){if(items[i]->attributes.count("TechDependency"))MarshalReference(b.t,q+8*i,Get(*items[i],"TechDependency"));if(items[i]->attributes.count("UpgradeToGrant"))MarshalReference(b.t,q+8*i+4,Get(*items[i],"UpgradeToGrant"));}b.t.Pop();}}
 else {b.Fields(input,root,experience,sizeof(experience)/sizeof(Field),"LevelUpTintColor|Upgrade|LevelUpFxList|Target|AttributeModifier|Decal",true);b.Color(Child(input,"LevelUpTintColor"),root+52,false);b.List(input,"Upgrade",root,68,false);
 std::vector<const N*> fx;for(const auto& c:input.children)if(c.name=="LevelUpFxList")fx.push_back(&c);b.Scalar(root+76,static_cast<U>(fx.size()));if(!fx.empty()){auto q=static_cast<unsigned char*>(b.Push(root,80,12,static_cast<U>(fx.size())));for(size_t i=0;i<fx.size();++i){if(fx[i]->attributes.count("FxList"))MarshalReference(b.t,q+12*i,Get(*fx[i],"FxList"));if(fx[i]->attributes.count("BoneName"))MarshalString(b.t,q+12*i+4,Get(*fx[i],"BoneName"));if(b.correct)b.swaps.push_back(q+12*i+4);}b.t.Pop();}
 b.List(input,"Target",root,84,false);b.List(input,"AttributeModifier",root,92,true);auto d=Child(input,"Decal");if(d){auto q=static_cast<unsigned char*>(b.Push(root,100,8));b.Scalar(q,static_cast<U>(d->children.size()));if(!d->children.empty()){auto c=static_cast<unsigned char*>(b.Push(q,4,64,static_cast<U>(d->children.size())));for(size_t i=0;i<d->children.size();++i){auto at=c+64*i;b.Fields(d->children[i],at,decal,sizeof(decal)/sizeof(Field),"Color"); // Percentages are applied after original float rounding.
 F(at+12,ParseFloat(Get(d->children[i],"OpacityMin","1.0"))*0.01f);F(at+16,ParseFloat(Get(d->children[i],"OpacityMax","1.0"))*0.01f);b.Color(Child(d->children[i],"Color"),at+60,true);}b.t.Pop();}b.t.Pop();}}
 return b.Finish();
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
