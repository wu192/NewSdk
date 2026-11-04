#include "Xmlpathmusic.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <cctype>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native {
namespace XmlpathmusicDetail {
using N=XmlpathmusicNode;using U=Relo::U32;
void Check(bool ok,const char* msg){if(!ok)throw std::invalid_argument(msg);}

const N* Child(const N& n,const char* key){const N* found=nullptr;for(const auto& c:n.children)if(c.name==key){found=&c;}return found;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void W(void* p,U x){std::memcpy(p,&x,4);}void Swap(void* p){U x;std::memcpy(&x,p,4);W(p,(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24));}
struct Pair{const char* name;int value;};
const Pair StateCategories[]={{"SCRIPTED",1},{"STANDARD",0}};
const Pair EventCategories[]={{"SCRIPT",1},{"SHELL",0}};
template<size_t C>int Enum(const std::string& s,const Pair(&e)[C]){for(const auto& p:e)if(std::strcmp(s.c_str(),p.name)==0)return p.value;return 1;}
struct Builder{
 Relo::Tracker t;bool big;std::vector<void*> swaps;Builder(bool b):big(b){}
 void Word(void* p,U x){W(p,x);swaps.push_back(p);}
 unsigned char* Push(void* p,unsigned offset,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(static_cast<unsigned char*>(p)+offset),size,count));}
 void Ref(const N& n,const char* key,unsigned char* p,unsigned offset,bool nullable){if(!n.attributes.count(key))return;auto q=nullable?Push(p,offset,4):p+offset;MarshalReference(t,q,Get(n,key));if(nullable)t.Pop();}
 void List(const N& n,const char* name,unsigned char* p,unsigned off){std::vector<const N*> values;for(const auto& c:n.children)if(c.name==name)values.push_back(&c);Word(p+off,static_cast<U>(values.size()));if(values.empty())return;auto q=Push(p,off+4,4,static_cast<U>(values.size()));for(size_t i=0;i<values.size();++i){MarshalReference(t,q+4*i,values[i]->text);}t.Pop();}
 Relo::Chunk Finish(){if(big)for(auto p:swaps)Swap(p);return t.MakeRelocatable(big,0);}
};
}
unsigned int XmlpathmusicTypeId(const std::string& name){
 if(name=="PathMusicGameDynamicState")return 97402485u;
 if(name=="PathMusicGameDynamicStateSet")return 343094706u;
 if(name=="PathMusicGameDynamicTransition")return 2343360052u;
 if(name=="PathMusicEventRuntime")return 1122327197u;
 if(name=="PathMusicEventSet")return 3339496789u;
 if(name=="PathMusicMapRuntime")return 2026048286u;
 if(name=="MusicPalette")return 2407383451u;
 return 0;
}
Relo::Chunk CompileXmlpathmusic(const XmlpathmusicNode& n,bool big){using namespace XmlpathmusicDetail;Check(XmlpathmusicTypeId(n.name)!=0,"Unsupported path-music root");unsigned size=n.name=="PathMusicGameDynamicState"?20:n.name=="PathMusicGameDynamicTransition"||n.name=="PathMusicMapRuntime"?12:16;Builder b(big);unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),size,1);
 if(n.name=="PathMusicGameDynamicState"){b.Ref(n,"EnterEvent",p,4,true);b.Ref(n,"ExitEvent",p,8,true);b.List(n,"Transition",p,12);}
 else if(n.name=="PathMusicGameDynamicStateSet"){if(n.attributes.count("Category"))b.Word(p+4,Enum(Get(n,"Category"),StateCategories));b.List(n,"State",p,8);}
 else if(n.name=="PathMusicGameDynamicTransition"){if(n.attributes.count("DestinationState"))b.Word(p+4,HashAssetId(Get(n,"DestinationState")));auto c=Child(n,"Condition");if(c){MarshalReference(b.t,p+8,c->text);}}
 else if(n.name=="PathMusicEventRuntime"){if(n.attributes.count("EventNameHash"))b.Word(p+4,ParseSigned(Get(n,"EventNameHash")));if(n.attributes.count("XferAlternateEvent")){auto q=b.Push(p,8,4);b.Word(q,HashAssetId(Get(n,"XferAlternateEvent")));b.t.Pop();}p[12]=ParseBoolean(Get(n,"IsXferable","true"));}
 else if(n.name=="PathMusicEventSet"){if(n.attributes.count("Category"))b.Word(p+4,Enum(Get(n,"Category"),EventCategories));b.List(n,"Event",p,8);}
 else if(n.name=="MusicPalette"){p[12]=ParseBoolean(Get(n,"IsDefaultForNewMap","false"));b.List(n,"Track",p,4);}
 else{/* The original XmlCompiler handler reads and discards Value; the separate MusicCompiler is not this entry. */}
 return b.Finish();}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

