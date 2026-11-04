#include "Xmlsystemsettings.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace SystemSettingsDetail {
using Relo::U32;using Node=XmlsystemsettingsNode;
#include "XmlsystemsettingsEnums.inc"
#include "XmlsystemsettingsPathFields.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
U32 LOD(const std::string& text){for(auto value:EGameLODType)if(std::strcmp(text.c_str(),value.name)==0)return value.value;return 5;}
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void UInt(const Node& n,const char* name,void* p){auto a=n.attributes.find(name);if(a!=n.attributes.end())Word(p,ParseUnsigned(a->second));}
    void Graphics(const Node& n,unsigned char* root){U32 count=static_cast<U32>(n.children.size());Word(root+4,count);if(!count)return;auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),24,count));unsigned i=0;
        for(const auto& c:n.children){auto p=data+24*i++;UInt(c,"VendorId",p);UInt(c,"DeviceId",p+4);auto name=c.attributes.find("Name");if(name!=c.attributes.end()){MarshalString(tracker,p+8,name->second);words.push_back(p+8);}auto level=c.attributes.find("Level");if(level!=c.attributes.end())Word(p+16,LOD(level->second));auto mac=c.attributes.find("MacLevel");if(mac!=c.attributes.end()){auto target=tracker.Push(reinterpret_cast<void**>(p+20),4,1);Word(target,LOD(mac->second));tracker.Pop();}}
        tracker.Pop();}
};
}
unsigned int XmlsystemsettingsTypeId(const std::string& name){if(name=="NetworkSettings")return 4272453293u;if(name=="PathfindSettings")return 970663416u;if(name=="ResourceSettings")return 3279338544u;if(name=="GraphicsHardware")return 2846460739u;return 0;}
Relo::Chunk CompileXmlsystemsettings(const XmlsystemsettingsNode& input,bool bigEndian){
    using namespace SystemSettingsDetail;static_assert(sizeof(void*)==4,"System settings requires x86.");unsigned id=XmlsystemsettingsTypeId(input.name);Check(id!=0,"Unsupported System settings root.");Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==970663416u?64:id==2846460739u?12:8,1);
    if(id==4272453293u){auto a=input.attributes.find("RunAheadSlack");b.Word(root+4,ParseSigned(a==input.attributes.end()?"30":a->second));}
    else if(id==970663416u){std::string attrs;for(auto name:PathFields){if(!attrs.empty())attrs+='|';attrs+=name;};for(unsigned i=0;i<15;++i)b.UInt(input,PathFields[i],root+4+4*i);}
    else if(id==3279338544u){auto a=input.attributes.find("SharedMoneyTransactionCategories");if(a!=input.attributes.end()){MarshalEnumBitset(std::string(a->second.c_str()),EMoneyTransactionCategory,30,22,30,reinterpret_cast<U32*>(root+4));b.words.push_back(root+4);}}
    else{b.Graphics(input,root);}
    if(bigEndian)for(auto p:b.words)Swap(p);return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
