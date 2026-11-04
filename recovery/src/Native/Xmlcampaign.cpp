#include "Xmlcampaign.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlcampaignDetail {
using N=XmlcampaignNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EFactionType[]={{"Allies",0},{"Japan",2},{"Neutral",3},{"RANDOM",-1},{"Soviet",1}};
const EnumNameValue EShadowType[]={{"ADDITIVE_DECAL",3},{"ADDITIVE_DECAL_DYNAMIC",5},{"ALPHA_DECAL",2},{"ALPHA_DECAL_DYNAMIC",4},{"DECAL",1},{"MERGE_DECAL",6},{"NONE",0}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 template<size_t C>void Enum(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback){int value=fallback;for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0){value=a.value;break;}Word(p,static_cast<U>(value));}
 void StringValue(unsigned char* p,const std::string& value){MarshalString(t,p,value);words.push_back(p);}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))StringValue(p+off,Get(n,attr));}
 void Ref(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))MarshalReference(t,p+off,Get(n,attr));}
 template<class F>void List(const N& n,const char* name,unsigned char* p,unsigned off,unsigned size,F f){U count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);U i=0;for(const auto& c:n.children)if(c.name==name)f(c,q+size*i++);t.Pop();}
 void References(const N& n,const char* name,unsigned char* p,unsigned off){List(n,name,p,off,4,[&](const N& c,unsigned char* q){MarshalReference(t,q,c.value);});}
 void Movie(const N* n,unsigned char* p){if(!n)return;Ref(*n,"ImageRef",p,0);String(*n,"FileName",p,4);String(*n,"ScreenName",p,12);String(*n,"Description",p,20);}
 void Commander(const N& n,unsigned char* p){Ref(n,"Portrait",p,0);String(n,"Personality",p,4);String(n,"Description",p,12);if(n.attributes.count("Faction"))Enum(p+20,Get(n,"Faction"),EFactionType,0);}
 void Campaign(const N& n,unsigned char* p){String(n,"DisplayName",p,4);String(n,"ConsoleAutosaveFilename",p,12);References(n,"Mission",p,20);Movie(Child(n,"FinalMovie"),p+28);Movie(Child(n,"AlternateFinalMovie"),p+56);}
 void Mission(const N& n,unsigned char* p){const char* names[]={"DisplayName","ShortDisplayName","OverviewTalkingHead","OverviewBackground","Title","MapName","BriefingFLA","BriefingSummary"};for(unsigned i=0;i<8;++i)String(n,names[i],p,4+8*i);const char* wins[]={"VictoryAchEvent","VictoryAchEventMedium","VictoryAchEventHard","VictoryAchEventCoop","VictoryAchEventBonusObj"};for(unsigned i=0;i<5;++i)if(n.attributes.count(wins[i]))Word(p+68+4*i,ParseUnsigned(Get(n,wins[i])));List(n,"Prerequisite",p,88,4,[&](const N& c,unsigned char* q){Word(q,ParseSigned(c.value));});List(n,"Objective",p,96,8,[&](const N& c,unsigned char* q){StringValue(q,c.value);});List(n,"BonusObjective",p,104,8,[&](const N& c,unsigned char* q){StringValue(q,c.value);});List(n,"AICommander",p,112,24,[&](const N& c,unsigned char* q){Commander(c,q);});Movie(Child(n,"IntroMovie"),p+120);}
 void Color(const N* n,unsigned char* p){if(!n)return;U r=ParseUnsigned(Get(*n,"R","0"))&255,g=ParseUnsigned(Get(*n,"G","0"))&255,b=ParseUnsigned(Get(*n,"B","0"))&255,a=ParseUnsigned(Get(*n,"A","255"))&255;Word(p,(a<<24)|(r<<16)|(g<<8)|b);}
 void Decal(const N& n,unsigned char* p){Ref(n,"Texture",p,0);Ref(n,"Texture2",p,4);if(n.attributes.count("Style"))Enum(p+8,Get(n,"Style"),EShadowType,3);Float(p+12,ParsePercentage(Get(n,"OpacityMin","1.0")));Float(p+16,ParsePercentage(Get(n,"OpacityMax","1.0")));const char* keys[]={"OpacityPulsesPerSecond","RotationMin","RotationMax","RotationDegreesPerSecond","MinRadius","MaxRadius","RadiusPulsesPerSecond","PositionOffsetMin","PositionOffsetMax","PositionOffsetPulsesPerSecond"};for(unsigned i=0;i<10;++i)Float(p+20+4*i,ParseFloat(Get(n,keys[i],i==4||i==5?"1.0":"0.0")));Color(Child(n,"Color"),p+60);}
 void Hotspot(const N& n,unsigned char* p){Ref(n,"WaypointMarker",p,4);if(auto c=Child(n,"DecalTemplate")){List(*c,"Component",p,8,64,[&](const N& item,unsigned char* q){Decal(item,q);});}}
 void Tutorial(const N& n,unsigned char* p){List(n,"Lesson",p,4,24,[&](const N& c,unsigned char* q){String(c,"Map",q,0);String(c,"Summary",q,8);String(c,"ImageName",q,16);});}
};
}
unsigned XmlcampaignTypeId(const std::string& n){if(n=="CampaignTemplate")return 1482556238u;if(n=="CampaignEnumerator")return 731899443u;if(n=="MissionTemplate")return 3477794083u;if(n=="MissionHotSpotSettings")return 3175516876u;if(n=="TutorialMapData")return 3408790761u;return 0;}
Relo::Chunk CompileXmlcampaign(const XmlcampaignNode& input,bool bigEndian){using namespace XmlcampaignDetail;static_assert(sizeof(void*)==4,"Campaign requires x86");U id=XmlcampaignTypeId(input.name);Check(id!=0,"Unknown campaign root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==1482556238u?84:id==3477794083u?148:id==3175516876u?16:12,1);if(id==1482556238u)b.Campaign(input,p);else if(id==3477794083u)b.Mission(input,p);else if(id==3175516876u)b.Hotspot(input,p);else if(id==3408790761u)b.Tutorial(input,p);else{b.References(input,"Campaign",p,4);}if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
