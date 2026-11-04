#include "Xmlw3d.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace Xmlw3dDetail {
using N=Xmlw3dNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EAnimationChannelType[]={{"Orientation",3},{"Visibility",4},{"XTranslation",0},{"YTranslation",1},{"ZTranslation",2}};
void Any(const N& n){}
struct Builder {
 Relo::Tracker t;std::vector<void*> words;std::vector<unsigned char*> shorts;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Short(unsigned char* p,U v){std::uint16_t value=static_cast<std::uint16_t>(v);std::memcpy(p,&value,2);shorts.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void Ref(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))MarshalReference(t,p+off,Get(n,attr));}
 template<class F>void List(const N& n,const char* name,unsigned char* p,unsigned off,unsigned size,F f){U count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);U i=0;for(const auto& c:n.children)if(c.name==name)f(c,q+size*i++);t.Pop();}
 void Vec(const N* n,unsigned char* p,bool quat=false){if(!n)return;const char* keys[]={"X","Y","Z","W"};for(unsigned i=0;i<(quat?4u:3u);++i)if(n->attributes.count(keys[i]))Float(p+4*i,ParseFloat(Get(*n,keys[i])));}
 void Pivot(const N& n,unsigned char* p){if(n.attributes.count("Name"))Word(p,HashAssetId(Get(n,"Name")));if(n.attributes.count("Parent"))Word(p+4,ParseSigned(Get(n,"Parent")));Vec(Child(n,"Translation"),p+8);Vec(Child(n,"Rotation"),p+20,true);if(auto m=Child(n,"FixupMatrix")){for(unsigned col=0;col<4;++col)for(unsigned row=0;row<4;++row){std::string key="M"+std::to_string(row)+std::to_string(col);if(m->attributes.count(key))Float(p+36+4*(4*col+row),ParseFloat(Get(*m,key.c_str())));}}}
 void Sub(const N& n,unsigned char* p){if(n.attributes.count("BoneIndex"))Word(p,ParseUnsigned(Get(n,"BoneIndex")));MarshalString(t,p+4,Get(n,"SubObjectID",""));words.push_back(p+4);if(auto r=Child(n,"RenderObject")){Any(*r);if(r->children.size()==1)MarshalReference(t,p+12,r->children[0].value);}}
 void Base(const N& n,unsigned char* p){if(n.attributes.count("Pivot"))Short(p+16,ParseUnsigned(Get(n,"Pivot")));if(n.attributes.count("Type")){int value=3;std::string s=Get(n,"Type");for(const auto& e:EAnimationChannelType)if(std::strcmp(s.c_str(),e.name)==0){value=e.value;break;}Word(p+4,value);}if(n.attributes.count("NumFrames"))Word(p+8,ParseUnsigned(Get(n,"NumFrames")));if(n.attributes.count("VectorLen"))Word(p+12,ParseUnsigned(Get(n,"VectorLen")));}
 void Numbers(const N* n,unsigned char* p,unsigned off,unsigned size){if(!n)return;Any(*n);U count=static_cast<U>(n->children.size());Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);for(U i=0;i<count;++i){const auto& c=n->children[i];if(size==1)q[i]=static_cast<unsigned char>(ParseUnsigned(c.value));else if(size==2)Short(q+2*i,ParseUnsigned(c.value));else Float(q+4*i,ParseFloat(c.value));}t.Pop();}
 void Init(const N& n,unsigned char* p){if(!n.attributes.count("Init"))return;std::string s(Get(n,"Init").c_str());std::vector<std::string> tokens;auto space=[](unsigned char c){return c==' '||(c>=9&&c<=13);};size_t at=0;while(at<s.size()){while(at<s.size()&&space(s[at]))++at;size_t first=at;while(at<s.size()&&!space(s[at]))++at;if(at>first)tokens.push_back(s.substr(first,at-first));}Word(p+24,static_cast<U>(tokens.size()));if(tokens.empty())return;auto q=Push(p,28,4,static_cast<U>(tokens.size()));for(size_t i=0;i<tokens.size();++i)Float(q+4*i,ParseFloat(tokens[i]));t.Pop();}
 void Channel(const N& n,unsigned char* pointer){U type=ParseUnsigned(Get(n,"TypeId","0"));bool delta=type==2012836613u,timecoded=type==1624821609u;if(delta);else if(timecoded);else ;auto p=Push(pointer,0,delta?44:timecoded?36:20);if(delta){if(n.attributes.count("Scale"))Float(p+20,ParseFloat(Get(n,"Scale")));Init(n,p);if(n.attributes.count("NumBits"))Word(p+32,ParseUnsigned(Get(n,"NumBits")));Numbers(Child(n,"CompressedValues"),p,36,1);}else if(timecoded){Numbers(Child(n,"FrameAndBinaryFlag"),p,20,2);Numbers(Child(n,"Values"),p,28,4);}Base(n,p);Word(p,delta?2012836613u:timecoded?1624821609u:0u);t.Pop();}
 void Animation(const N& n,unsigned char* p){Ref(n,"Hierarchy",p,4);if(n.attributes.count("NumFrames"))Word(p+8,ParseUnsigned(Get(n,"NumFrames")));if(n.attributes.count("FrameRate"))Word(p+12,ParseUnsigned(Get(n,"FrameRate")));if(auto c=Child(n,"Channels")){Any(*c);U count=static_cast<U>(c->children.size());Word(p+16,count);if(count){auto q=Push(p,20,4,count);for(U i=0;i<count;++i)Channel(c->children[i],q+4*i);t.Pop();}}}
};
}
unsigned Xmlw3dTypeId(const std::string& n){return n=="W3DHierarchy"?1641540160u:n=="W3DContainer"?4042295058u:n=="W3DAnimationRuntime"?3534264507u:n=="W3DCollisionBox"?3810008068u:0;}
Relo::Chunk CompileXmlw3d(const Xmlw3dNode& input,bool bigEndian){using namespace Xmlw3dDetail;static_assert(sizeof(void*)==4,"W3D requires x86");U id=Xmlw3dTypeId(input.name);Check(id!=0,"Unknown W3D root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==1641540160u?12:id==4042295058u?16:id==3534264507u?24:32,1);if(id==1641540160u){b.List(input,"Pivot",p,4,100,[&](const N& n,unsigned char* q){b.Pivot(n,q);});}else if(id==4042295058u){b.Ref(input,"Hierarchy",p,4);b.List(input,"SubObject",p,8,16,[&](const N& n,unsigned char* q){b.Sub(n,q);});}else if(id==3534264507u)b.Animation(input,p);else{p[28]=ParseBoolean(Get(input,"JoypadPickingOnly","false"));b.Vec(Child(input,"Center"),p+4);b.Vec(Child(input,"Extent"),p+16);}if(bigEndian){for(auto q:b.words)Swap(q);for(auto q:b.shorts)std::swap(q[0],q[1]);}return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
