#include "Xmlforcefeedback.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace ForceFeedbackDetail {
using Relo::U32;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmlforcefeedbackTypeId(const std::string& name){return name=="ForceFeedbackEvent"?474482543u:0;}
Relo::Chunk CompileXmlforcefeedback(const XmlforcefeedbackNode& input,bool bigEndian){
    using namespace ForceFeedbackDetail;static_assert(sizeof(void*)==4,"Force feedback requires x86.");Check(XmlforcefeedbackTypeId(input.name)!=0,"Unsupported Force feedback root.");

    Relo::Tracker tracker;unsigned char* root=nullptr;tracker.Push(reinterpret_cast<void**>(&root),20,1);
    const char* fields[]={"LeftMotorUse","RightMotorUse","Duration","PlayCount"};
    for(unsigned i=0;i<4;++i){auto a=input.attributes.find(fields[i]);if(a==input.attributes.end()&&i!=3)continue;
        const std::string& value=a==input.attributes.end()?std::string("1"):a->second;
        float number=i==2?InitializeTime(value.c_str()):ParseFloat(value);if(i<2)number*=0.01f;
        std::memcpy(root+4+4*i,&number,4);if(bigEndian)Swap(root+4+4*i);
    }
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
