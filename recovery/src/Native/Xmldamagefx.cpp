#include "Xmldamagefx.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace DamagefxDetail {
using Relo::U32;
struct EnumEntry {const char* name;int value;};
#include "XmldamagefxEnums.inc"
const char* const References[]={"MajorFX","MinorFX","RearFX","SideFX","TopFX","VeteranMajorFX","VeteranMinorFX","VeteranRearFX","VeteranSideFX","VeteranTopFX"};

void Check(bool value,const char* text){if(!value)throw std::invalid_argument(text);}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
std::string Value(const XmldamagefxNode& node,const char* name,const char* fallback){auto at=node.attributes.find(name);return at==node.attributes.end()?std::string(fallback):at->second;}

int TypeValue(const std::string& value) {
    for(const auto& entry:DamageTypes)if(std::strcmp(entry.name,value.c_str())==0)return entry.value;
    return 35; // Original EnumLookup's unknown token fallback, not DEFAULT (-1).
}
}
unsigned int XmldamagefxTypeId(const std::string& name) {
    if(name=="DamageFX")return 2905958645u;
    if(name=="DamageFXSettings")return 75989905u;
    return 0;
}
Relo::Chunk CompileXmldamagefx(const XmldamagefxNode& input,bool bigEndian) {
    using namespace DamagefxDetail;
    static_assert(sizeof(void*)==4,"DamageFX component requires x86.");
    Check(XmldamagefxTypeId(input.name)!=0,"Unsupported DamageFX root.");
    const bool settings=input.name=="DamageFXSettings";

    Relo::Tracker tracker;unsigned char* root=nullptr;
    tracker.Push(reinterpret_cast<void**>(&root),settings?8:12,1);
    if(settings) {
        auto value=input.attributes.find("GroundDamageFX");
        if(value!=input.attributes.end()) {
            void* reference=tracker.Push(reinterpret_cast<void**>(root+4),4,1);
            MarshalReference(tracker,reference,value->second);tracker.Pop();
        }
    } else {
        const U32 count=static_cast<U32>(input.children.size());
        std::memcpy(root+4,&count,4);
        if(count) {
            auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),52,count));
            for(U32 i=0;i<count;++i) {
                const auto& group=input.children[i];
                unsigned char* item=list+52*i;
                int type=TypeValue(Value(group,"Type","DEFAULT"));
                float amount=ParseFloat(Value(group,"Amount","0.0"));
                float throttle=InitializeTime(Value(group,"ThrottleTime","0s").c_str());
                std::memcpy(item,&type,4);std::memcpy(item+4,&amount,4);std::memcpy(item+8,&throttle,4);
                for(unsigned r=0;r<10;++r){auto at=group.attributes.find(References[r]);if(at!=group.attributes.end())MarshalReference(tracker,item+12+4*r,at->second);}
                if(bigEndian){Swap(item);Swap(item+4);Swap(item+8);}
            }
            tracker.Pop();
        }
        if(bigEndian)Swap(root+4);
    }
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
