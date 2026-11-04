#include "Xmluidecals.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace UiDecalsDetail {
using Relo::U32;using Node=XmluidecalsNode;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

const Node* Child(const Node& n,const char* name){const Node* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void Real(void* p,float value){std::memcpy(p,&value,4);words.push_back(p);}
    void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
    void Packed(const Node* n,unsigned char* p){if(!n)return;U32 r=ParseUnsigned(Value(*n,"R","0"))&255,g=ParseUnsigned(Value(*n,"G","0"))&255,b=ParseUnsigned(Value(*n,"B","0"))&255,a=ParseUnsigned(Value(*n,"A","255"))&255;Word(p,(a<<24)|(r<<16)|(g<<8)|b);}
    void RGB(const Node* n,unsigned char* p){if(!n)return;const char* names[]={"R","G","B"};for(unsigned i=0;i<3;++i){auto a=n->attributes.find(names[i]);if(a!=n->attributes.end())Real(p+4*i,ParseFloat(a->second));}}
    void Settings(const Node* n,unsigned char* p){if(!n)return;Ref(*n,"ValidTexture",p);Ref(*n,"InvalidTexture",p+4);Real(p+8,ParseFloat(Value(*n,"Opacity","1.0")));Packed(Child(*n,"LegalColor"),p+12);Packed(Child(*n,"InvalidColor"),p+16);}
    void Render(const Node* n,unsigned char* p){if(!n)return;Ref(*n,"Model",p);Real(p+4,ParseFloat(Value(*n,"ZBias","0")));RGB(Child(*n,"Color"),p+8);}
    void Destinations(const Node& n,unsigned char* root){
        U32 count=0;for(const auto& c:n.children)if(c.name=="DestinationTypeSettings")++count;Word(root+24,count);
        if(count){auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+28),24,count));unsigned i=0;
            for(const auto& c:n.children)if(c.name=="DestinationTypeSettings"){
                auto p=list+24*i++;auto a=c.attributes.find("DestinationType");
                if(a!=c.attributes.end())Word(p,std::strcmp(a->second.c_str(),"Normal")==0?0:1);
                Render(Child(c,"RenderInfo"),p+4);
            }tracker.Pop();
        }
    }
};
}
unsigned int XmluidecalsTypeId(const std::string& name){
    if(name=="UIDecalGrid")return 2599418799u;
    if(name=="UIDecalCloud")return 2524893660u;
    if(name=="UIDecalDestinationMarker")return 1532214660u;
    if(name=="UIWorldBuilderDecalGrid")return 1238702511u;
    return 0;
}
Relo::Chunk CompileXmluidecals(const XmluidecalsNode& input,bool bigEndian){
    using namespace UiDecalsDetail;
    static_assert(sizeof(void*)==4,"UI decals require x86.");
    const unsigned id=XmluidecalsTypeId(input.name);Check(id!=0,"Unsupported UI decal root.");
    Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==2599418799u?56:id==1532214660u?32:16,1);
    if(id==2599418799u){b.Ref(input,"WallTexture",root+4);b.Real(root+8,ParseFloat(Value(input,"WallOpacity","1.0")));b.Packed(Child(input,"WallDecalColor"),root+12);b.Settings(Child(input,"PrimarySettings"),root+16);b.Settings(Child(input,"SecondarySettings"),root+36);}
    else if(id==2524893660u){b.Ref(input,"BuildTexture",root+4);b.Ref(input,"DefenseTexture",root+8);b.Ref(input,"SpecialPowerRestrictionTexture",root+12);}
    else if(id==1532214660u){b.Render(Child(input,"DefaultRenderInfo"),root+4);b.Destinations(input,root);}
    else{b.Ref(input,"Texture",root+4);b.Real(root+8,ParseFloat(Value(input,"Opacity","1.0")));b.Packed(Child(input,"Color"),root+12);}
    if(bigEndian)for(void* p:b.words)Swap(p);
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
