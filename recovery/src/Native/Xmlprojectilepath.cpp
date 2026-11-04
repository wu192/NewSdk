#include "Xmlprojectilepath.h"
#include "ScalarParsing.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace ProjectilePathDetail {
using Relo::U32;using Node=XmlprojectilepathNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
void Vector(const Node& n,unsigned char* p,bool big){const char* attrs[]={"x","y","z","w"};for(unsigned i=0;i<4;++i){auto a=n.attributes.find(attrs[i]);float value=ParseFloat(a==n.attributes.end()?"0.0":a->second);std::memcpy(p+4*i,&value,4);if(big)Swap(p+4*i);}}
}
unsigned int XmlprojectilepathTypeId(const std::string& name){return name=="ProjectilePath"?1710207315u:0;}
Relo::Chunk CompileXmlprojectilepath(const XmlprojectilepathNode& input,bool bigEndian){
 using namespace ProjectilePathDetail;static_assert(sizeof(void*)==4,"Projectile path requires x86.");Check(XmlprojectilepathTypeId(input.name)!=0,"Unsupported projectile path root.");Relo::Tracker t;unsigned char* root=nullptr;t.Push(reinterpret_cast<void**>(&root),16,1);
 const Node* scale=nullptr;U32 count=0;for(const auto& c:input.children)if(c.name=="ComponentScale"){scale=&c;}else ++count;
 if(scale){auto v=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(root+4),16,1));Vector(*scale,v,bigEndian);t.Pop();}
 std::memcpy(root+8,&count,4);if(count){auto points=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(root+12),16,count));unsigned i=0;for(const auto& c:input.children)if(c.name=="Point")Vector(c,points+16*i++,bigEndian);t.Pop();}if(bigEndian)Swap(root+8);return t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
