#include "Xmlhotkeymaps.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace HotkeymapsDetail {
using Relo::U32;
const EnumNameValue Modifiers[]={{"ALT",1},{"CTRL",0},{"SHIFT",2}};

void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 n;std::memcpy(&n,p,4);n=(n<<24)|((n<<8)&0xff0000u)|((n>>8)&0xff00u)|(n>>24);std::memcpy(p,&n,4);}
void Reference(Relo::Tracker& tracker,unsigned char* p,const XmlhotkeymapsNode& n,const char* name){auto a=n.attributes.find(name);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
}
unsigned int XmlhotkeymapsTypeId(const std::string& name) {
    if(name=="GeneralHotKeySlots")return 417176643u;
    if(name=="DefaultHotKeys")return 3741098742u;
    if(name=="DebugHotKeys")return 3359870461u;
    if(name=="MappableKey")return 1350608344u;
    return 0;
}
Relo::Chunk CompileXmlhotkeymaps(const XmlhotkeymapsNode& input,bool bigEndian) {
    using namespace HotkeymapsDetail;
    static_assert(sizeof(void*)==4,"Hotkey maps require x86.");
    Check(XmlhotkeymapsTypeId(input.name)!=0,"Unsupported hotkey map root.");
    Relo::Tracker tracker;unsigned char* root=nullptr;tracker.Push(reinterpret_cast<void**>(&root),12,1);
    if(input.name=="GeneralHotKeySlots") {

        Reference(tracker,root+4,input,"TakeScreenShot");Reference(tracker,root+8,input,"ReloadRapidIterationFeature");
    } else if(input.name=="MappableKey") {

        auto a=input.attributes.find("KeyDef");
        if(a!=input.attributes.end())MarshalString(tracker,root+4,a->second);
        if(bigEndian)Swap(root+4);
    } else {

        if(!input.children.empty()) {
            const auto& map=input.children[0];
            const U32 count=static_cast<U32>(map.children.size());std::memcpy(root+4,&count,4);
            if(count) {
                auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),12,count));
                for(U32 i=0;i<count;++i) {
                    const auto& entry=map.children[i];
                    unsigned char* item=list+12*i;
                    Reference(tracker,item,entry,"Slot");Reference(tracker,item+4,entry,"Key");
                    auto a=entry.attributes.find("Modifiers");
                    if(a!=entry.attributes.end())MarshalEnumBitset(a->second,Modifiers,3,1,3,reinterpret_cast<U32*>(item+8));
                    if(bigEndian)Swap(item+8);
                }
                tracker.Pop();
            }
            if(bigEndian)Swap(root+4);
        }
    }
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
