#include "Xmlmoviearchive.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlmoviearchiveDetail {
using N=XmlmoviearchiveNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EFactionType[]={{"Allies",0},{"Japan",2},{"Neutral",3},{"RANDOM",-1},{"Soviet",1}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr)){MarshalString(t,p+off,Get(n,attr));words.push_back(p+off);}}
 void Movie(const N& n,unsigned char* p,bool root=false){Word(p,ParseUnsigned(Get(n,"ProgressLock","0")));if(n.attributes.count("MovieImage"))MarshalReference(t,p+4,Get(n,"MovieImage"));String(n,"FileName",p,8);String(n,"OnScreenName",p,16);String(n,"Description",p,24);}
 void List(const N& n,unsigned char* p){std::string s=Get(n,"Faction","Soviet");int faction=0;for(const auto& e:EFactionType)if(std::strcmp(s.c_str(),e.name)==0){faction=e.value;break;}Word(p,static_cast<U>(faction));U count=static_cast<U>(n.children.size());Word(p+4,count);if(!count)return;auto q=Push(p,8,32,count);for(U i=0;i<count;++i)Movie(n.children[i],q+32*i);t.Pop();}
};
}
unsigned XmlmoviearchiveTypeId(const std::string& n){return n=="ArchivableMovie"?3741496377u:n=="ArchiveMovieList"?2099472558u:0;}
Relo::Chunk CompileXmlmoviearchive(const XmlmoviearchiveNode& input,bool bigEndian){using namespace XmlmoviearchiveDetail;static_assert(sizeof(void*)==4,"Movie archive requires x86");U id=XmlmoviearchiveTypeId(input.name);Check(id!=0,"Unknown moviearchive root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==3741496377u?32:12,1);if(id==3741496377u)b.Movie(input,p,true);else b.List(input,p);if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
