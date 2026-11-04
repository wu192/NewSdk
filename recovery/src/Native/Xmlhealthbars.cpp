#include "Xmlhealthbars.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace HealthBarsDetail {
using Relo::U32;using Node=XmlhealthbarsNode;
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
unsigned int XmlhealthbarsTypeId(const std::string& name){if(name=="HealthBarTemplate")return 1507490649u;if(name=="HealthBarSettings")return 3919360587u;return 0;}
Relo::Chunk CompileXmlhealthbars(const XmlhealthbarsNode& input,bool bigEndian){
    using namespace HealthBarsDetail;static_assert(sizeof(void*)==4,"Health bars require x86.");
    unsigned id=XmlhealthbarsTypeId(input.name);Check(id!=0,"Unsupported Health bars root.");bool settings=id==3919360587u;

    Relo::Tracker tracker;unsigned char* root=nullptr;tracker.Push(reinterpret_cast<void**>(&root),settings?16:8,1);
    std::vector<ObjectFilter*> filters;
    if(!settings){auto a=input.attributes.find("Length");if(a!=input.attributes.end()){U32 value=ParseUnsigned(a->second);std::memcpy(root+4,&value,4);if(bigEndian)Swap(root+4);}}
    else{
        Ref(tracker,input,"DefaultHealthBar",root+4);U32 count=static_cast<U32>(input.children.size());std::memcpy(root+8,&count,4);
        if(count){auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+12),116,count));unsigned i=0;
            for(const auto& n:input.children){auto p=data+116*i++;Ref(tracker,n,"HealthBar",p);auto filter=Child(n,"ObjectFilter");if(filter){auto target=reinterpret_cast<ObjectFilter*>(p+4);Filter(tracker,*filter,target);filters.push_back(target);}}
            tracker.Pop();}
        if(bigEndian)Swap(root+8);
    }
    if(bigEndian)for(auto filter:filters)EndianObjectFilter(filter);
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
