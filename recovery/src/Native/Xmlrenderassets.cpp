#include "Xmlrenderassets.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlrenderassetsDetail {
using N=XmlrenderassetsNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 void Coord(const N* n,unsigned char* p){if(!n)return;Word(p,ParseSigned(Get(*n,"x","0")));Word(p+4,ParseSigned(Get(*n,"y","0")));}
 void Image(const N& n,unsigned char* p){if(n.attributes.count("Texture"))MarshalReference(t,p+4,Get(n,"Texture"));if(n.attributes.count("Rotated"))p[32]=ParseBoolean(Get(n,"Rotated"));Coord(Child(n,"Dimensions"),p+8);Coord(Child(n,"Coords"),p+16);Coord(Child(n,"TextureDimensions"),p+24);}
 void Mesh(const N& n,unsigned char* p){U count=static_cast<U>(n.children.size());Word(p+4,count);if(!count)return;auto q=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+8),12,count));for(U i=0;i<count;++i){const auto& c=n.children[i];if(c.attributes.count("MeshID")){MarshalString(t,q+12*i,Get(c,"MeshID"));words.push_back(q+12*i);}if(c.attributes.count("Instances"))Word(q+12*i+8,ParseSigned(Get(c,"Instances")));}t.Pop();}
};
}
unsigned XmlrenderassetsTypeId(const std::string& n){return n=="MeshInstanceTemplate"?815686049u:n=="PackedTextureImage"?1449288224u:n=="OnDemandTextureImage"?4262364347u:0;}
Relo::Chunk CompileXmlrenderassets(const XmlrenderassetsNode& input,bool bigEndian){using namespace XmlrenderassetsDetail;static_assert(sizeof(void*)==4,"Render assets require x86");U id=XmlrenderassetsTypeId(input.name);Check(id!=0,"Unknown render asset root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==815686049u?12:36,1);if(id==815686049u)b.Mesh(input,p);else b.Image(input,p);if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
