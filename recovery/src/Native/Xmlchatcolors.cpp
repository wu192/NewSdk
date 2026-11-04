#include "Xmlchatcolors.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace ChatColorsDetail {
using Relo::U32;using Node=XmlchatcolorsNode;
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
unsigned int XmlchatcolorsTypeId(const std::string& name){return name=="OnlineChatColors"?1345252658u:0;}
Relo::Chunk CompileXmlchatcolors(const XmlchatcolorsNode& input,bool bigEndian){
 using namespace ChatColorsDetail;static_assert(sizeof(void*)==4,"Chat colors requires x86.");Check(XmlchatcolorsTypeId(input.name)!=0,"Unsupported chat colors root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),120,1);
 b.Packed(Child(input,"Default"),root+4);
 b.Packed(Child(input,"CurrentRoom"),root+8);
 b.Packed(Child(input,"ChatRoom"),root+12);
 b.Packed(Child(input,"Game"),root+16);
 b.Packed(Child(input,"GameFull"),root+20);
 b.Packed(Child(input,"GameCRCMismatch"),root+24);
 b.Packed(Child(input,"PlayerNormal"),root+28);
 b.Packed(Child(input,"PlayerBuddy"),root+32);
 b.Packed(Child(input,"PlayerOwner"),root+36);
 b.Packed(Child(input,"OfflinePlayerBuddy"),root+40);
 b.Packed(Child(input,"PlayerSelf"),root+44);
 b.Packed(Child(input,"PlayerIgnored"),root+48);
 b.Packed(Child(input,"OfflinePlayerIgnored"),root+52);
 b.Packed(Child(input,"ChatNormal"),root+56);
 b.Packed(Child(input,"ChatEmote"),root+60);
 b.Packed(Child(input,"ChatOwner"),root+64);
 b.Packed(Child(input,"ChatOwnerEmote"),root+68);
 b.Packed(Child(input,"ChatPrivate"),root+72);
 b.Packed(Child(input,"ChatPrivateEmote"),root+76);
 b.Packed(Child(input,"ChatPrivateOwner"),root+80);
 b.Packed(Child(input,"ChatPrivateOwnerEmote"),root+84);
 b.Packed(Child(input,"ChatBuddy"),root+88);
 b.Packed(Child(input,"ChatSelf"),root+92);
 b.Packed(Child(input,"AcceptTrue"),root+96);
 b.Packed(Child(input,"AcceptFalse"),root+100);
 b.Packed(Child(input,"MapSelected"),root+104);
 b.Packed(Child(input,"MapUnselected"),root+108);
 b.Packed(Child(input,"MessageOfTheDay"),root+112);
 b.Packed(Child(input,"MessageOfTheDayHeading"),root+116);
 if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
