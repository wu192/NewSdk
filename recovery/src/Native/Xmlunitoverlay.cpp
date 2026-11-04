#include "Xmlunitoverlay.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace UnitOverlayDetail {
using Relo::U32;using Node=XmlunitoverlayNode;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
U32 Faction(const std::string& s){const char* names[]={"Allies","Japan","Neutral","RANDOM","Soviet"};const U32 values[]={0,2,3,0xffffffffu,1};for(unsigned i=0;i<5;++i)if(std::strcmp(s.c_str(),names[i])==0)return values[i];return 0;}
struct Builder {
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
    void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
    void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
    void Ranks(const Node& n,unsigned char* p){
        U32 count=static_cast<U32>(n.children.size());Word(p+4,count);if(!count)return;
        auto items=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(p+8),8,count));
        for(U32 i=0;i<count;++i){const auto& c=n.children[i];auto a=c.attributes.find("Rank");if(a!=c.attributes.end())Word(items+8*i,ParseSigned(a->second));Ref(c,"Image",items+8*i+4);}
        tracker.Pop();
    }
    void Factions(const Node& n,unsigned char* root){
        U32 count=static_cast<U32>(n.children.size());Word(root+96,count);if(!count)return;
        auto items=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+100),12,count));
        for(U32 i=0;i<count;++i){const auto& c=n.children[i];auto a=c.attributes.find("Faction");if(a!=c.attributes.end())Word(items+12*i,Faction(a->second));Ranks(c,items+12*i);}
        tracker.Pop();
    }
};
}
unsigned int XmlunitoverlayTypeId(const std::string& name){return name=="UnitOverlayIconSettings"?2421413379u:0;}
Relo::Chunk CompileXmlunitoverlay(const XmlunitoverlayNode& n,bool bigEndian){
    using namespace UnitOverlayDetail;static_assert(sizeof(void*)==4,"Overlay requires x86.");
    Check(XmlunitoverlayTypeId(n.name)!=0,"Unsupported overlay root.");

    Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),104,1);
    b.Ref(n,"AmmoPipImage",root+4);
    b.Ref(n,"AmmoPipFrameImage",root+8);
    b.Ref(n,"ContainPipImage",root+12);
    b.Ref(n,"ContainPipFrameImage",root+16);
    b.Ref(n,"GarrisonPipImage",root+20);
    b.Ref(n,"UnitTypeIconBackgroundImage",root+24);
    b.Ref(n,"TargetIndicatorImage",root+28);
    b.Ref(n,"TargetIndicatorGlowImage",root+32);
    b.Ref(n,"PowerIconImageSequence",root+36);
    b.Ref(n,"RepairIconImageSequence",root+40);
    b.Ref(n,"InfiltratedIconImageSequence",root+44);
    b.Ref(n,"HealUnitIconImageSequence",root+48);
    b.Ref(n,"RepairVehicleIconImageSequence",root+52);
    b.Real(root+56,ParsePercentage(Value(n,"AmmoPipScale","100%")));
    b.Real(root+64,ParsePercentage(Value(n,"ContainPipScale","100%")));
    b.Real(root+68,ParsePercentage(Value(n,"GarrisonPipScale","100%")));
    b.Real(root+72,ParsePercentage(Value(n,"UnitTypeIconScale","100%")));
    b.Real(root+76,ParsePercentage(Value(n,"StatusIconScale","100%")));
    b.Real(root+80,ParsePercentage(Value(n,"VeterancyIconScale","100%")));
    b.Real(root+84,ParsePercentage(Value(n,"TargetIndicatorScale","100%")));
    b.Real(root+88,ParsePercentage(Value(n,"HealingIconScale","100%")));
    auto range=n.attributes.find("AmmoPipRange");if(range!=n.attributes.end())b.Word(root+60,ParseSigned(range->second));
    b.Real(root+92,InitializeTime(Value(n,"HealingIconDisplayTime","3s").c_str()));
    b.Factions(n,root);
    if(bigEndian)for(void* word:b.words)Swap(word);
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
