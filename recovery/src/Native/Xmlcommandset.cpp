#include "Xmlcommandset.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlcommandsetDetail {
using N=XmlcommandsetNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EDistributionType[]={{"CONSTANT",0},{"GAUSSIAN",2},{"HIGH_BIAS",5},{"LOW_BIAS",4},{"TRIANGULAR",3},{"UNIFORM",1}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void String(const N& n,unsigned char* p){if(!n.attributes.count("Name"))return;MarshalString(t,p,Get(n,"Name"));words.push_back(p);}
 void Random(const N& n,unsigned char* p){std::string s=Get(n,"Type","UNIFORM");int value=0;for(const auto& e:EDistributionType)if(std::strcmp(s.c_str(),e.name)==0){value=e.value;break;}Word(p,value);if(n.attributes.count("Low"))Float(p+4,ParseFloat(Get(n,"Low")));if(n.attributes.count("High"))Float(p+8,ParseFloat(Get(n,"High")));}
 void Buttons(const N& n,unsigned char* p){U count=static_cast<U>(n.children.size());Word(p+16,count);if(!count)return;auto q=Push(p,20,16,count);for(U i=0;i<count;++i){const auto& c=n.children[i];auto b=q+16*i;String(c,b);if(c.attributes.count("Index"))Word(b+8,ParseUnsigned(Get(c,"Index")));if(auto delay=Child(c,"InitialDelay")){auto d=Push(b,12,12);Random(*delay,d);t.Pop();}}t.Pop();}
};
}
unsigned XmlcommandsetTypeId(const std::string& n){return n=="CommandSet"?2584759047u:0;}
Relo::Chunk CompileXmlcommandset(const XmlcommandsetNode& input,bool bigEndian){using namespace XmlcommandsetDetail;static_assert(sizeof(void*)==4,"CommandSet requires x86");Check(XmlcommandsetTypeId(input.name)!=0,"Unknown command set root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),24,1);b.String(input,p+4);b.Word(p+12,ParseSigned(Get(input,"InitialVisible","32")));b.Buttons(input,p);if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
