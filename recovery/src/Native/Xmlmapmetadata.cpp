#include "Xmlmapmetadata.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace MapMetadataDetail {
using Relo::U32;using Node=XmlmapmetadataNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{Relo::Tracker t;std::vector<void*> words;void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}void String(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end()){MarshalString(t,p,a->second);words.push_back(p);}}
 void Metadata(const Node& n,unsigned char* p){auto id=n.attributes.find("id");if(id!=n.attributes.end())Word(p,HashAssetId(id->second));String(n,"Description",p+4);String(n,"DisplayName",p+12);p[56]=ParseBoolean(Value(n,"IsMultiplayer"));Word(p+20,ParseSigned(Value(n,"NumPlayers")));Word(p+24,ParseUnsigned(Value(n,"CRC")));String(n,"FileName",p+28);p[57]=ParseBoolean(Value(n,"IsOfficial"));Word(p+36,ParseSigned(Value(n,"Width")));Word(p+40,ParseSigned(Value(n,"Height")));Word(p+44,ParseSigned(Value(n,"BorderSize")));U32 count=static_cast<U32>(n.children.size());Word(p+48,count);if(count){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+52),20,count));for(unsigned i=0;i<count;++i){const auto& s=n.children[i];auto at=list+20*i;String(s,"Name",at);auto position=Child(s,"Position");if(position){const char* names[]={"x","y","z"};for(unsigned j=0;j<3;++j)Real(at+8+4*j,ParseFloat(Value(*position,names[j],"0.0")));}}t.Pop();}}
};}
unsigned int XmlmapmetadataTypeId(const std::string& name){return name=="MapMetaData"?1603703110u:0;}
Relo::Chunk CompileXmlmapmetadata(const XmlmapmetadataNode& input,bool bigEndian){using namespace MapMetadataDetail;static_assert(sizeof(void*)==4,"Map metadata requires x86.");Check(XmlmapmetadataTypeId(input.name)!=0,"Unsupported map metadata root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),12,1);U32 count=static_cast<U32>(input.children.size());b.Word(root+4,count);if(count){auto list=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+8),60,count));for(unsigned i=0;i<count;++i)b.Metadata(input.children[i],list+60*i);b.t.Pop();}if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
