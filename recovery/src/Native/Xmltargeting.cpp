#include "Xmltargeting.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace TargetingDetail {
using Relo::U32;using Node=XmltargetingNode;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmltargetingTypeId(const std::string& name){
    if(name=="TargetingBaseCompare")return 3600209407u;
    if(name=="TargetingDistanceCompare")return 2359666647u;
    if(name=="TargetingCombatChainCompare")return 89767986u;
    if(name=="TargetingInTurretArcCompare")return 2007776008u;
    if(name=="TargetingCompareList")return 3188107749u;
    return 0;
}
Relo::Chunk CompileXmltargeting(const XmltargetingNode& input,bool bigEndian){
    using namespace TargetingDetail;
    static_assert(sizeof(void*)==4,"Targeting requires x86.");
    unsigned id=XmltargetingTypeId(input.name);Check(id!=0,"Unsupported Targeting root.");
    bool distance=id==2359666647u,list=id==3188107749u;

    Relo::Tracker tracker;unsigned char* root=nullptr;tracker.Push(reinterpret_cast<void**>(&root),list?12:distance?8:4,1);
    if(distance){auto a=input.attributes.find("Tolerance");float value=ParseFloat(a==input.attributes.end()?"30.0":a->second);std::memcpy(root+4,&value,4);if(bigEndian)Swap(root+4);}
    if(list){
        U32 count=static_cast<U32>(input.children.size());std::memcpy(root+4,&count,4);if(bigEndian)Swap(root+4);
        if(count){auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),4,count));unsigned i=0;
            for(const auto& c:input.children){MarshalReference(tracker,data+4*i++,c.value);}tracker.Pop();}
    }
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
