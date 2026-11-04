#include "Xmlvisibility.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace VisibilityDetail {
using Relo::U32;using Node=XmlvisibilityNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
const char* DefaultConditions="PREATTACK_A FIRING_A FIRING_OR_PREATTACK_A RELOADING_A USING_WEAPON_A PREATTACK_B FIRING_B FIRING_OR_PREATTACK_B RELOADING_B USING_WEAPON_B PREATTACK_C FIRING_C FIRING_OR_PREATTACK_C RELOADING_C USING_WEAPON_C";
const char* DefaultStatus="IS_FIRING_WEAPON IS_AIMING_WEAPON SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING USING_ABILITY";
const EnumNameValue Options[]={{"ALLOW_NEAR_TREES",0},{"CAN_ATTACK_WHILE_ACTIVE",4},{"DETECTED_BY_FRIENDLIES",1},{"DISCONTINUE_WHEN_REVEALED",2},{"UNTOGGLE_HIDDEN_WHEN_LEAVING_STEALTH",3}};
}
unsigned int XmlvisibilityTypeId(const std::string& name){
 if(name=="InvisibilityTemplate")return 1431290504u;
 if(name=="InvisibilityManagerSettings")return 254471188u;
 return 0;
}
Relo::Chunk CompileXmlvisibility(const XmlvisibilityNode& input,bool bigEndian){
 using namespace VisibilityDetail;static_assert(sizeof(void*)==4,"Visibility requires x86.");Check(XmlvisibilityTypeId(input.name)!=0,"Unsupported visibility root.");Relo::Tracker t;unsigned char* root=nullptr;t.Push(reinterpret_cast<void**>(&root),input.name=="InvisibilityTemplate"?240:116,1);
 if(input.name=="InvisibilityTemplate"){

 MarshalModelCondition(Value(input,"ForbiddenConditions",DefaultConditions),reinterpret_cast<U32*>(root+4));MarshalModelCondition(Value(input,"ForbiddenConditionExceptions",""),reinterpret_cast<U32*>(root+64));MarshalObjectStatus(Value(input,"ForbiddenStatus",DefaultStatus),reinterpret_cast<U32*>(root+124));
 U32 camouflage=ParseUnsigned(Value(input,"CamouflageLevel"));std::memcpy(root+152,&camouflage,4);const auto invisible=Value(input,"InvisibilityType","STEALTH");U32 type=std::strcmp(invisible.c_str(),"INVALID")==0?0xffffffffu:std::strcmp(invisible.c_str(),"STEALTH")==0?0u:1u;std::memcpy(root+156,&type,4);
 auto opt=input.attributes.find("Options");if(opt!=input.attributes.end())MarshalEnumBitset(opt->second,Options,5,0,5,reinterpret_cast<U32*>(root+160));
 for(const auto& a:input.attributes){if(a.first=="EnteringStealthFX")MarshalReference(t,root+164,a.second);else if(a.first=="LeavingStealthFX")MarshalReference(t,root+168,a.second);}
 MarshalObjectStatus(Value(input,"HintDetectableStates","IS_ATTACKING"),reinterpret_cast<U32*>(root+172));float time=InitializeTime(Value(input,"NoStealthForAttackWindow","0s").c_str());std::memcpy(root+200,&time,4);auto status=input.attributes.find("RequiredStatus");if(status!=input.attributes.end())MarshalObjectStatus(status->second,reinterpret_cast<U32*>(root+204));
 U32 count=static_cast<U32>(input.children.size());std::memcpy(root+232,&count,4);if(count){auto list=static_cast<U32*>(t.Push(reinterpret_cast<void**>(root+236),4,count));for(unsigned i=0;i<count;++i){list[i]=HashAssetId(input.children[i].value);if(bigEndian)Swap(list+i);}t.Pop();}
 if(bigEndian){for(unsigned off=4;off<164;off+=4)Swap(root+off);for(unsigned off=172;off<236;off+=4)Swap(root+off);}
 }else{
 auto filter=Child(input,"CamouflageDetectorObjectFilter");if(filter){ObjectFilterInput f;for(const auto& a:filter->attributes)if(a.first!="TypeId")f.attributes[a.first]=std::string(a.second.c_str());for(const auto& c:filter->children){(c.name=="IncludeThing"?f.includeThings:f.excludeThings).push_back(std::string(c.value.c_str()));}auto object=reinterpret_cast<ObjectFilter*>(root+4);MarshalObjectFilter(f,object,t);if(bigEndian)EndianObjectFilter(object);}
 }
 return t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
