#include "Xmlgamescripts.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace GameScriptsDetail {
using Relo::U32;using Node=XmlgamescriptsNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

std::string Value(const Node& n,const char* a,const char* fallback=""){auto i=n.attributes.find(a);return i==n.attributes.end()?fallback:i->second;}
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
const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
struct Builder{Relo::Tracker t;std::vector<void*> words;void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}void Id(const Node& n,unsigned char* p){auto a=n.attributes.find("id");if(a!=n.attributes.end())Word(p,HashAssetId(a->second));}
 void Parameter(const Node& n,unsigned char* slot){U32 id=ParseUnsigned(Value(n,"TypeId","0"));bool list=id==524038440u,single=id==58964679u;auto p=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(slot),list?12:single?8:4,1));Word(p,list||single?id:0);if(single){auto a=n.attributes.find("Object");if(a!=n.attributes.end())MarshalReference(t,p+4,a->second);}else if(list){U32 count=static_cast<U32>(n.children.size());Word(p+4,count);if(count){auto values=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+8),4,count));for(unsigned i=0;i<count;++i){MarshalReference(t,values+4*i,n.children[i].value);}t.Pop();}}t.Pop();}
 void Items(const Node& n,const char* tag,unsigned char* p,unsigned off,unsigned stride,int kind,int depth){U32 count=0;for(const auto& c:n.children)if(!tag||c.name==tag)++count;Word(p+off,count);if(!count)return;auto items=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off+4),stride,count));unsigned i=0;for(const auto& c:n.children)if(!tag||c.name==tag){auto at=items+stride*i++;if(kind==0)Group(c,at,depth+1);else if(kind==1)Script(c,at,depth);else if(kind==2)Action(c,at,depth);else Parameter(c,at);}t.Pop();}
 void Action(const Node& n,unsigned char* p,int depth){auto a=n.attributes.find("Type");if(a!=n.attributes.end())Word(p,RawHash(a->second));auto parameters=Child(n,"ParameterList");if(parameters){Items(*parameters,nullptr,p,4,4,3,depth);}}
 void Script(const Node& n,unsigned char* p,int depth){Id(n,p);Items(n,"Action",p,4,12,2,depth);}
 void Group(const Node& n,unsigned char* p,int depth){Check(depth<=32,"Script group depth exceeds32.");Id(n,p);Items(n,"Script",p,4,12,1,depth);Items(n,"ScriptGroup",p,12,20,0,depth);}
};}
unsigned int XmlgamescriptsTypeId(const std::string& name){return name=="GameScriptList"?3737094024u:0;}
Relo::Chunk CompileXmlgamescripts(const XmlgamescriptsNode& input,bool bigEndian){using namespace GameScriptsDetail;static_assert(sizeof(void*)==4,"Game scripts require x86.");Check(XmlgamescriptsTypeId(input.name)!=0,"Unsupported game scripts root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),8,1);auto set=Child(input,"ScriptSet");if(set){auto group=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+4),20,1));b.Group(*set,group,0);b.t.Pop();}if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
