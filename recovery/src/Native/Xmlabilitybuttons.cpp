#include "Xmlabilitybuttons.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace AbilityButtonsDetail {
using Relo::U32;using Node=XmlabilitybuttonsNode;
enum Kind{Empty,Single,Toggle,Targeted,MultiPowers,MultiTargets};struct Layout{U32 id,size;Kind kind;bool player;};
#include "XmlabilitybuttonsLayouts.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

const Node* Child(const Node& n,const char* name){const Node* found=nullptr;for(const auto& c:n.children)if(c.name==name){found=&c;}return found;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;
    std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void Real(void* p,float value){std::memcpy(p,&value,4);words.push_back(p);}
    void Ref(const Node& n,const char* name,void* p,const char* fallback=nullptr){auto a=n.attributes.find(name);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);else if(fallback)MarshalReference(tracker,p,fallback);}
    void Refs(const Node& n,const char* name,unsigned char* p,unsigned offset){U32 count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+offset,count);if(count){auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(p+offset+4),4,count));unsigned i=0;for(const auto& c:n.children)if(c.name==name){MarshalReference(tracker,data+4*i++,c.value);}tracker.Pop();}}
    U32 Outline(const std::string& text){const char* value=text.c_str();if(std::strcmp(value,"NONE")==0)return 0;if(std::strcmp(value,"SELECTED")==0)return 1;if(std::strcmp(value,"HOVER_SELECT")==0)return 2;if(std::strcmp(value,"UNKNOWN")==0)return ~0u;return 3;}
    void Target(const Node& n,unsigned char* p){Ref(n,"ValidTargetCursor",p+8,"Arrow");Ref(n,"InvalidTargetCursor",p+12,"Invalid");Ref(n,"RadiusCursor",p+16);Word(p+20,Outline(Value(n,"ValidTargetOutline","HOVER_ATTACK")));auto color=Child(n,"ValidTargetTintColor");if(color){auto rgb=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(p+24),12,1));const char* names[]={"R","G","B"};for(unsigned i=0;i<3;++i){auto a=color->attributes.find(names[i]);if(a!=color->attributes.end())Real(rgb+4*i,ParseFloat(a->second));}tracker.Pop();}Ref(n,"StateData",p+4);}
    void Payload(const Node& n,unsigned char* root,bool player){auto a=n.attributes.find("TypeId");U32 id=a==n.attributes.end()?0:ParseUnsigned(a->second);Layout layout={0,player?8u:4u,player?Single:Empty,player};for(auto l:Layouts)if(l.id==id&&l.player==player){layout=l;break;}
        bool targeted=layout.kind==Targeted||layout.kind==MultiPowers||layout.kind==MultiTargets;

        auto p=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),layout.size,1));Word(p,layout.id);
        if(layout.kind==Single)Ref(n,"StateData",p+4);else if(layout.kind==Toggle)Refs(n,"StateData",p,4);else if(targeted){
            if(layout.kind==MultiPowers){Refs(n,"AdditionalSpecialPowerTemplate",p,28);Refs(n,"AdditionalRadiusCursor",p,36);Refs(n,"AdditionalValidTargetCursor",p,44);}
            if(layout.kind==MultiTargets){auto marker=n.attributes.find("TargetMarkerTemplate");if(marker!=n.attributes.end())Word(p+28,HashAssetId(marker->second));auto fx=n.attributes.find("TargetMarkerFXList");if(fx!=n.attributes.end())Word(p+32,HashAssetId(fx->second));Word(p+36,ParseUnsigned(Value(n,"TargetCount","1")));Real(p+40,ParseFloat(Value(n,"MinTargetDistance","0.0")));Refs(n,"AdditionalRadiusCursor",p,44);}
            Target(n,p);
        }tracker.Pop();
    }
};
}
unsigned int XmlabilitybuttonsTypeId(const std::string& name){if(name=="UnitAbilityButtonTemplate")return 3517887782u;if(name=="PlayerPowerButtonTemplate")return 4044322798u;return 0;}
Relo::Chunk CompileXmlabilitybuttons(const XmlabilitybuttonsNode& input,bool bigEndian){using namespace AbilityButtonsDetail;static_assert(sizeof(void*)==4,"Ability buttons require x86.");unsigned id=XmlabilitybuttonsTypeId(input.name);Check(id!=0,"Unsupported Ability buttons root.");bool player=id==4044322798u;Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),12,1);b.Ref(input,player?"SpecialPower":"LogicCommand",root+4);auto data=Child(input,"Data");if(data){if(data->children.size()==1)b.Payload(data->children[0],root,player);}if(bigEndian)for(auto p:b.words)Swap(p);return b.tracker.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
