#include "Xmluiimages.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace UiImagesDetail {
using Relo::U32;using Node=XmluiimagesNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(t,p,a->second);}
 void Packed(const Node* n,unsigned char* p){if(!n)return;U32 r=ParseUnsigned(Value(*n,"R","0"))&255,g=ParseUnsigned(Value(*n,"G","0"))&255,b=ParseUnsigned(Value(*n,"B","0"))&255,a=ParseUnsigned(Value(*n,"A","255"))&255;Word(p,(a<<24)|(r<<16)|(g<<8)|b);}
};
}
unsigned int XmluiimagesTypeId(const std::string& name){
 if(name=="ImageSequence")return 565855655u;
 if(name=="UnitTypeIcon")return 3614134471u;
 if(name=="DrawGroupInfo")return 2913856066u;
 return 0;
}
Relo::Chunk CompileXmluiimages(const XmluiimagesNode& input,bool bigEndian){
 using namespace UiImagesDetail;static_assert(sizeof(void*)==4,"UI images requires x86.");Check(XmluiimagesTypeId(input.name)!=0,"Unsupported UI image root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),input.name=="DrawGroupInfo"?52:12,1);
 if(input.name=="ImageSequence"){U32 count=static_cast<U32>(input.children.size());b.Word(root+4,count);if(count){auto list=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+8),4,count));for(unsigned i=0;i<count;++i){MarshalReference(b.t,list+4*i,input.children[i].value);}b.t.Pop();}}
 else if(input.name=="UnitTypeIcon"){b.Ref(input,"Image",root+4);auto offset=Child(input,"Offset");if(offset){auto p=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+8),8,1));b.Real(p,ParseFloat(Value(*offset,"x","0.0")));b.Real(p+4,ParseFloat(Value(*offset,"y","0.0")));b.t.Pop();}}
 else{MarshalString(b.t,root+4,Value(input,"FontName","Arial"));b.words.push_back(root+4);b.Word(root+12,ParseUnsigned(Value(input,"FontSize","12")));root[48]=ParseBoolean(Value(input,"IsBold"));root[49]=ParseBoolean(Value(input,"UsePlayerColor","true"));root[50]=ParseBoolean(Value(input,"PixelOffsetAsPercentage","false"));b.Word(root+16,ParseSigned(Value(input,"DropShadowOffsetX","-1")));b.Word(root+20,ParseSigned(Value(input,"DropShadowOffsetY","-1")));b.Word(root+24,ParseSigned(Value(input,"PixelOffetXInteger")));b.Word(root+28,ParseSigned(Value(input,"PixelOffetYInteger")));b.Real(root+32,ParseFloat(Value(input,"PixelOffetXReal")));b.Real(root+36,ParseFloat(Value(input,"PixelOffetYReal")));b.Packed(Child(input,"TextColor"),root+40);b.Packed(Child(input,"TextDropShadowColor"),root+44);}
 if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
