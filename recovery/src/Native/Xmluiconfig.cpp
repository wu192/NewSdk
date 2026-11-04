#include "Xmluiconfig.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <algorithm>
#include <cstdint>
#include <utility>
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace UIConfigDetail {
using Relo::U32;using Node=XmluiconfigNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
std::vector<const Node*> Children(const Node& n,const char* name){std::vector<const Node*> out;for(const auto& c:n.children)if(c.name==name)out.push_back(&c);return out;}
bool Space(unsigned char c){return c==' '||(c>=9&&c<=13);}
std::vector<std::string> Tokens(const std::string& s){std::vector<std::string> out;const char* p=s.c_str();while(*p){while(Space(*p))++p;if(!*p)break;const char* b=p;while(*p&&!Space(*p))++p;out.emplace_back(b,p-b);}return out;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
U32 RawHash(const std::string& input){
    const auto p0=reinterpret_cast<const unsigned char*>(input.c_str());auto p=p0;U32 size=static_cast<U32>(std::strlen(input.c_str())),h=size;
    if(!size)return 0;
    auto shortWord=[](const unsigned char* b){return U32(b[0])|(U32(b[1])<<8);};
    auto signedByte=[](unsigned char c){return static_cast<U32>(static_cast<std::int8_t>(c));};
    for(U32 n=size/4;n;--n,p+=4){h+=shortWord(p);h^=(shortWord(p+2)^(h<<5))<<11;h+=h>>11;}
    switch(size&3){case 3:h+=shortWord(p);h^=((signedByte(p[2])<<2)^h)<<16;h+=h>>11;break;case 2:h+=shortWord(p);h^=h<<11;h+=h>>17;break;case 1:h+=signedByte(*p);h^=h<<10;h+=h>>1;break;}
    h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
}

struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Text(const std::string& s,unsigned char* p){MarshalString(t,p,s);words.push_back(p);}
 void String(const Node& n,const char* a,unsigned char* p,const char* def=nullptr){auto i=n.attributes.find(a);if(i!=n.attributes.end()||def)Text(i==n.attributes.end()?def:i->second,p);}
 void Ref(const Node& n,const char* a,unsigned char* p,const char* def=nullptr){auto i=n.attributes.find(a);if(i!=n.attributes.end()||def)MarshalReference(t,p,i==n.attributes.end()?def:i->second);}
 void Hash(const Node& n,const char* a,unsigned char* p,const char* def=nullptr){auto i=n.attributes.find(a);if(i!=n.attributes.end()||def)Word(p,HashAssetId(i==n.attributes.end()?def:i->second));}
 void Scalar(const Node& n,const char* a,unsigned char* p,const char* kind,const char* def=nullptr){auto i=n.attributes.find(a);if(i==n.attributes.end()&&!def)return;std::string s=i==n.attributes.end()?def:i->second;
  if(std::strcmp(kind,"int")==0)Word(p,ParseSigned(s));else if(std::strcmp(kind,"uint")==0)Word(p,ParseUnsigned(s));else if(std::strcmp(kind,"float")==0)Real(p,ParseFloat(s));else if(std::strcmp(kind,"bool")==0)*p=ParseBoolean(s);else throw std::invalid_argument("Unsupported scalar kind.");
 }
 template<class F> void List(const Node& n,const char* name,unsigned char* p,unsigned size,F f){auto rows=Children(n,name);Word(p,static_cast<U32>(rows.size()));if(!rows.empty()){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+4),size,static_cast<U32>(rows.size())));for(unsigned i=0;i<rows.size();++i)f(*rows[i],list+size*i);t.Pop();}}
#include "XmluiconfigNested.inc"
#include "XmluiconfigComponents.inc"
 void Dynamic(const Node& n,unsigned char* p){auto name=n.attributes.find("Name");if(name!=n.attributes.end())Word(p,RawHash(name->second));auto type=n.attributes.find("Type");if(type!=n.attributes.end()){std::string s(type->second.c_str());Word(p+4,s=="INGAME"?0:s=="SHELL"?1:s=="LOADSCREEN"?3:2);}
  auto components=Child(n,"ComponentList");if(components){Word(p+8,static_cast<U32>(components->children.size()));if(!components->children.empty()){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+12),4,static_cast<U32>(components->children.size())));for(unsigned i=0;i<components->children.size();++i){const Node& c=components->children[i];U32 id=ParseUnsigned(Value(c,"TypeId"));auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(list+4*i),ComponentSize(id),1));Dispatch(id,c,target);t.Pop();}t.Pop();}}
 }
 void Root(const Node& n,unsigned char* p){List(n,"UIDynamicConfig",p+4,16,[&](const Node& a,unsigned char* b){Dynamic(a,b);});}
};}
unsigned int XmluiconfigTypeId(const std::string& name){return name=="UIConfigList"?3072160547u:0;}
Relo::Chunk CompileXmluiconfig(const XmluiconfigNode& input,bool bigEndian){using namespace UIConfigDetail;static_assert(sizeof(void*)==4,"UI config requires x86.");Check(XmluiconfigTypeId(input.name)!=0,"Unsupported UI configuration root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),12,1);b.Root(input,root);if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
