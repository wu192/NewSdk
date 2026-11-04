#include "Xmlbuildeconomy.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include "DataInitializers.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace BuildEconomyDetail {
using Relo::U32;using Node=XmlbuildeconomyNode;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
const Node* Child(const Node& n,const char* name){const Node* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
void Ref(Relo::Tracker& tracker,const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
void Filter(Relo::Tracker& tracker,const Node& n,ObjectFilter* target){
    ObjectFilterInput input;
    // The original IValue contract supplies C strings, including enum attributes.
    for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=std::string(a.second.c_str());
    for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(std::string(c.value.c_str()));}
    MarshalObjectFilter(input,target,tracker);
}
}
unsigned int XmlbuildeconomyTypeId(const std::string& name){if(name=="LocalBuildListMonitor")return 315614191u;if(name=="BuildSystemSettings")return 3974617950u;if(name=="GiftManagerSettings")return 1047799468u;if(name=="MpGameRules")return 741706624u;return 0;}
namespace BuildEconomyDetail {
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;std::vector<ObjectFilter*> filters;
    std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void Real(void* p,float value){std::memcpy(p,&value,4);words.push_back(p);}
    void DoFilter(const Node& n,unsigned char* p){auto target=reinterpret_cast<ObjectFilter*>(p);Filter(tracker,n,target);filters.push_back(target);}
    U32 Geometry(const std::string& value){const char* text=value.c_str();return std::strcmp(text,"SPHERE")==0?0:std::strcmp(text,"CYLINDER")==0?2:std::strcmp(text,"RING")==0?3:1;}
};
}
Relo::Chunk CompileXmlbuildeconomy(const XmlbuildeconomyNode& input,bool bigEndian){
    using namespace BuildEconomyDetail;static_assert(sizeof(void*)==4,"Build economy requires x86.");unsigned id=XmlbuildeconomyTypeId(input.name);Check(id!=0,"Unsupported Build economy root.");Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==3974617950u?36:id==1047799468u?120:24,1);
    if(id==315614191u){b.Real(root+4,InitializeTime(b.Value(input,"TimeBetweenUpdates","0.25s").c_str()));b.Word(root+8,ParseUnsigned(b.Value(input,"UpdatesToSkipAfterStart","4")));b.Word(root+12,ParseUnsigned(b.Value(input,"UpdatesToSkipAfterSaveFileLoad","2")));b.Word(root+16,ParseUnsigned(b.Value(input,"UpdatesToSkipAfterTargetPlayerChanges","2")));auto f=Child(input,"ProducerObjectFilter");if(f){auto p=static_cast<unsigned char*>(b.tracker.Push(reinterpret_cast<void**>(root+20),112,1));b.DoFilter(*f,p);b.tracker.Pop();}}
    else if(id==3974617950u){b.Real(root+4,ParseFloat(b.Value(input,"AllowedHeightVariationForBuildingPlacement","3.0")));b.Real(root+8,ParseFloat(b.Value(input,"RotationalSnapIterations","4.0")));b.Real(root+12,ParseFloat(b.Value(input,"BuildabilityDisplayRadius","90.0")));b.Word(root+16,b.Geometry(b.Value(input,"BuildabilityDisplayShape","CYLINDER")));b.Real(root+20,ParseFloat(b.Value(input,"BuildabilityDecalMargin","0.142857")));b.Real(root+24,ParseFloat(b.Value(input,"BuildabilityDecalSize","30.0")));b.Real(root+28,ParseFloat(b.Value(input,"BuildabilityDecalRadiusPercent","18.5185"))*0.01f);root[32]=ParseBoolean(b.Value(input,"BuildabilityUsesStaticDecalSize","true"));}
    else if(id==1047799468u){auto gift=Child(input,"ObjectGiftSettings");if(gift){root[116]=ParseBoolean(b.Value(*gift,"AllowTransferToSkirmishPlayer","true"));auto f=Child(*gift,"AllowObjectFilter");if(f)b.DoFilter(*f,root+4);}}
    else{b.Word(root+4,ParseSigned(b.Value(input,"NumMinutesBeforePlayersCanTransferMoney","5")));auto cash=Child(input,"SkirmishStartCash");if(cash){const char* names[]={"LoCash","HiCash","ChoiceStepAmount","DefaultChoiceIndex"};for(unsigned i=0;i<4;++i){auto a=cash->attributes.find(names[i]);if(a!=cash->attributes.end()||i==3)b.Word(root+8+4*i,ParseSigned(a==cash->attributes.end()?"1":a->second));}}}
    if(bigEndian){for(auto p:b.words)Swap(p);for(auto p:b.filters)EndianObjectFilter(p);}return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
