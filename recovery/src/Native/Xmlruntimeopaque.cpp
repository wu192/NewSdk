#include "Xmlruntimeopaque.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace RuntimeOpaqueDetail {
using Relo::U32;using Node=XmlruntimeopaqueNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmlruntimeopaqueTypeId(const std::string& name){
 if(name=="BaseRenderAssetType")return 2989459401u;
 if(name=="AudioFileRuntime")return 584092973u;
 return 0;
}
Relo::Chunk CompileXmlruntimeopaque(const XmlruntimeopaqueNode& input,bool bigEndian){
 using namespace RuntimeOpaqueDetail;static_assert(sizeof(void*)==4,"Opaque runtime requires x86.");Check(XmlruntimeopaqueTypeId(input.name)!=0,"Unsupported opaque runtime root.");bool audio=input.name=="AudioFileRuntime";Relo::Tracker t;unsigned char* root=nullptr;t.Push(reinterpret_cast<void**>(&root),audio?32:4,1);return t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
