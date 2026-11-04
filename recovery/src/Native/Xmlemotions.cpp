#include "Xmlemotions.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace EmotionsDetail {
using Relo::U32;using Node=XmlemotionsNode;
#include "XmlemotionsEnums.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    template<unsigned N>void Enum(const Node& n,const char* name,void* p,const EnumNameValue(&values)[N],U32 fallback){auto a=n.attributes.find(name);if(a==n.attributes.end())return;U32 v=fallback;for(auto x:values)if(std::strcmp(a->second.c_str(),x.name)==0){v=x.value;break;}Word(p,v);}
    void Time(const Node& n,const char* name,void* p){auto a=n.attributes.find(name);if(a==n.attributes.end())return;float value=InitializeTime(a->second.c_str());std::memcpy(p,&value,4);words.push_back(p);}
    void Int(const Node& n,const char* name,void* p){auto a=n.attributes.find(name);if(a!=n.attributes.end())Word(p,ParseSigned(a->second));}
    void Ref(const Node& n,const char* name,void* p){auto a=n.attributes.find(name);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
    void Model(const Node& n,const char* name,unsigned char* p){auto a=n.attributes.find(name);if(a==n.attributes.end())return;MarshalModelCondition(std::string(a->second.c_str()),reinterpret_cast<U32*>(p));for(unsigned i=0;i<15;++i)words.push_back(p+4*i);}
};
}
unsigned int XmlemotionsTypeId(const std::string& name){if(name=="StanceTemplate")return 2525284163u;if(name=="EmotionTemplate")return 2836737428u;return 0;}
Relo::Chunk CompileXmlemotions(const XmlemotionsNode& input,bool bigEndian){
    using namespace EmotionsDetail;static_assert(sizeof(void*)==4,"Emotions require x86.");unsigned id=XmlemotionsTypeId(input.name);Check(id!=0,"Unsupported Emotions root.");bool stance=id==2525284163u;
    Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),stance?12:324,1);
    if(stance){U32 count=static_cast<U32>(input.children.size());b.Word(root+4,count);
        if(count){auto data=static_cast<unsigned char*>(b.tracker.Push(reinterpret_cast<void**>(root+8),8,count));unsigned i=0;
            for(const auto& n:input.children){auto p=data+8*i++;b.Enum(n,"Type",p,EStance,1);auto a=n.attributes.find("AttributeModifier");if(a!=n.attributes.end()){auto target=b.tracker.Push(reinterpret_cast<void**>(p+4),4,1);MarshalReference(b.tracker,target,a->second);b.tracker.Pop();}}
            b.tracker.Pop();}
    }else{

        b.Enum(input,"Type",root+4,EEmotionType,8);auto option=input.attributes.find("Options");if(option!=input.attributes.end()){MarshalEnumBitset(std::string(option->second.c_str()),EEmotionTemplateOptions,6,4,6,reinterpret_cast<U32*>(root+8));b.words.push_back(root+8);}
        const char* times[]={"DurationActive","DurationInactive","DurationInactiveObject","DurationInactiveType","AttributeModifierStartDelay","AttributeModifierDuration","AIStateLockDuration"};const unsigned offsets[]={12,16,20,24,60,64,68};for(unsigned i=0;i<7;++i)b.Time(input,times[i],root+offsets[i]);
        b.Enum(input,"EnemyThreatComparison",root+28,EEmotionTemplateThreatComparisonType,0);b.Int(input,"EnemyThreat",root+32);b.Enum(input,"FriendlyThreatComparison",root+36,EEmotionTemplateThreatComparisonType,0);b.Int(input,"FriendlyThreat",root+40);
        b.Ref(input,"StartFX",root+44);b.Ref(input,"UpdateFX",root+48);b.Ref(input,"EndFX",root+52);b.Ref(input,"AttributeModifier",root+56);b.Enum(input,"AIState",root+72,EEmotionAIType,2);
        b.Model(input,"ModelConditionsSetOnStart",root+76);b.Model(input,"ModelConditionsSetOnEnd",root+136);b.Model(input,"ModelConditionsClearOnStart",root+196);b.Model(input,"ModelConditionsClearOnEnd",root+256);
        auto lua=input.attributes.find("AILuaEventsList");if(lua!=input.attributes.end()){MarshalString(b.tracker,root+316,lua->second);b.words.push_back(root+316);}
    }
    if(bigEndian)for(void* p:b.words)Swap(p);return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
