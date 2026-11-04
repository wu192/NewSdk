#include "Xmlmouse.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace MouseDetail {
using Relo::U32;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmlmouseTypeId(const std::string& name){return name=="Mouse"?2525492603u:0;}
Relo::Chunk CompileXmlmouse(const XmlmouseNode& input,bool bigEndian){
    using namespace MouseDetail;static_assert(sizeof(void*)==4,"Mouse requires x86.");Check(XmlmouseTypeId(input.name)!=0,"Unsupported Mouse root.");
    Relo::Tracker tracker;unsigned char* root=nullptr;tracker.Push(reinterpret_cast<void**>(&root),20,1);const char* fields[]={"DragTolerance","DragThreshold","InGameUIMaxDoubleClickDistance","AptHitRegionMaxDoubleClickDistance"};const char* defaults[]={"48","100ms","20","10"};
    for(unsigned i=0;i<4;++i){auto a=input.attributes.find(fields[i]);std::string value=a==input.attributes.end()?defaults[i]:a->second;auto p=root+4+4*i;if(i==1){float f=InitializeTime(value.c_str());std::memcpy(p,&f,4);}else{U32 v=i==0?static_cast<U32>(ParseSigned(value)):ParseUnsigned(value);std::memcpy(p,&v,4);}if(bigEndian)Swap(p);}
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
