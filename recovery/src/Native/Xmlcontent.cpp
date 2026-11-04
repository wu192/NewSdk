#include "Xmlcontent.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlcontentDetail {
using N=XmlcontentNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(!n.attributes.count(attr))return;MarshalString(t,p+off,Get(n,attr));words.push_back(p+off);}
 void Ref(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))MarshalReference(t,p+off,Get(n,attr));}
 void Strings(const N& n,unsigned char* p,const char* a,const char* b,const char* c){String(n,a,p,0);String(n,b,p,8);String(n,c,p,16);}
 void List(const N& n,unsigned char* p,bool intel){U count=static_cast<U>(n.children.size());Word(p+4,count);if(!count)return;auto q=Push(p,8,intel?24:4,count);for(U i=0;i<count;++i){const auto& c=n.children[i];if(intel){Strings(c,q+24*i,"CategoryId","EntryId","TextureId");}else{MarshalReference(t,q+4*i,c.value);}}t.Pop();}
};
}
unsigned XmlcontentTypeId(const std::string& n){if(n=="EngagingContent")return 3058114834u;if(n=="EngagingContentGroup")return 11280974u;if(n=="EngagingContentSequence")return 3799634821u;if(n=="IntelDB")return 530081230u;if(n=="UnitIntro")return 1870180772u;return 0;}
Relo::Chunk CompileXmlcontent(const XmlcontentNode& input,bool bigEndian){using namespace XmlcontentDetail;static_assert(sizeof(void*)==4,"Content requires x86");U id=XmlcontentTypeId(input.name);Check(id!=0,"Unknown content root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==3058114834u?28:id==3799634821u?20:12,1);if(id==3058114834u){b.Strings(input,p+4,"TitleId","DescId","ImageId");}else if(id==11280974u){b.List(input,p,false);}else if(id==3799634821u){b.String(input,"MapName",p,4);b.Ref(input,"RequiredContent",p,12);b.Ref(input,"RandomContent",p,16);}else if(id==530081230u){b.List(input,p,true);}else{if(input.attributes.count("Video"))b.Word(p+4,HashAssetId(Get(input,"Video")));b.Ref(input,"Audio",p,8);}if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
