#include "Xmlcrowd.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace CrowdDetail {
using Relo::U32;using Node=XmlcrowdNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

std::string Value(const Node& n,const char* a,const char* fallback=""){auto i=n.attributes.find(a);return i==n.attributes.end()?fallback:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
U32 RawHash(const std::string& input){
    const auto p0=reinterpret_cast<const unsigned char*>(input.c_str());auto p=p0;U32 size=static_cast<U32>(std::strlen(input.c_str())),h=size;
    if(!size)return 0;
    auto shortWord=[](const unsigned char* b){return U32(b[0])|(U32(b[1])<<8);};
    auto signedByte=[](unsigned char c){return static_cast<U32>(static_cast<std::int8_t>(c));};
    for(U32 n=size/4;n;--n,p+=4){h+=shortWord(p);h^=(shortWord(p+2)^(h<<5))<<11;h+=h>>11;}
    switch(size&3){case 3:h+=shortWord(p);h^=((signedByte(p[2])<<2)^h)<<16;h+=h>>11;break;case 2:h+=shortWord(p);h^=h<<11;h+=h>>17;break;case 1:h+=signedByte(*p);h^=h<<10;h+=h>>1;break;}
    h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
}
const std::pair<const char*,int> Voice[]={{"voiceAlert",32},{"voiceAttack",17},{"voiceAttackAfterMoving",23},{"voiceAttackAir",22},{"voiceAttackCharge",18},{"voiceAttackGarrisonedStructure",20},{"voiceAttackMachine",21},{"voiceAttackStructure",19},{"voiceBombard",25},{"voiceBuildResponse",43},{"voiceCaptureBuildingComplete",47},{"voiceCombatDrop",38},{"voiceCombineWithHorde",27},{"voiceCreated",15},{"voiceDefectingAway",29},{"voiceDefectingTowards",30},{"voiceDeliverRing",13},{"voiceDesperateAttack",24},{"voiceDock",35},{"voiceEnter",39},{"voiceEnterHostile",40},{"voiceEnterStateAttack",52},{"voiceEnterStateAttackAfterMoving",58},{"voiceEnterStateAttackAir",54},{"voiceEnterStateAttackCharge",53},{"voiceEnterStateAttackGarrisonedStructure",56},{"voiceEnterStateAttackMachine",57},{"voiceEnterStateAttackStructure",55},{"voiceEnterStateMove",59},{"voiceEnterStateMoveLandToWater",65},{"voiceEnterStateMoveOverWalls",61},{"voiceEnterStateMoveToCamp",63},{"voiceEnterStateMoveToHigherGround",60},{"voiceEnterStateMoveWaterToLand",66},{"voiceEnterStateMoveWhileAttacking",64},{"voiceEnterStateRetreatToCastle",62},{"voiceFear",26},{"voiceFullyCreated",16},{"voiceGarrison",42},{"voiceGetHealed",41},{"voiceGuard",31},{"voiceLowFuel",51},{"voiceMove",5},{"voiceMoveLandToWater",9},{"voiceMoveOverWalls",7},{"voiceMoveToCamp",12},{"voiceMoveToHigherGround",6},{"voiceMoveWaterToLand",10},{"voiceMoveWhileAttacking",8},{"voiceNoBuild",50},{"voicePrimaryWeaponMode",44},{"voiceRapidFire",48},{"voiceRepair",37},{"voiceRetreatToCastle",11},{"voiceSalvage",33},{"voiceSecondaryWeaponMode",45},{"voiceSelect",0},{"voiceSelectBattle",2},{"voiceSelectIdleWorker",3},{"voiceSelectUnderConstruction",1},{"voiceSelectUnderFire",4},{"voiceSendToSlaughterhouse",14},{"voiceStartCharging",49},{"voiceSupply",34},{"voiceTaskComplete",28},{"voiceTertiaryWeaponMode",46},{"voiceUnload",36}};
const std::pair<const char*,int> ObjectVoice[]={{"voiceAttackUnit",0},{"voiceCreatedFrom",4},{"voiceEnterStateAttackUnit",1},{"voiceEnterStateEnterUnit",3},{"voiceEnterUnit",2},{"voiceFullyCreatedFrom",5}};
template<size_t C>int Enum(const std::string& value,const std::pair<const char*,int>(&names)[C],int fallback){for(const auto& e:names)if(std::strcmp(value.c_str(),e.first)==0)return e.second;return fallback;}
const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
struct Builder{
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(t,p,a->second);}
 void VoiceArray(const Node& n,unsigned char* p){const char* names[]={"AudioEntry","ObjectSpecificEntry","NamedEntry"};for(unsigned g=0;g<3;++g){unsigned count=0;for(const auto& c:n.children)if(c.name==names[g])++count;Word(p+8*g,count);if(!count)continue;unsigned size=g==1?16:12;auto items=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+8*g+4),size,count));unsigned i=0;for(const auto& c:n.children)if(c.name==names[g]){auto at=items+size*i++;auto a=c.attributes.find(g==2?"Name":"AudioType");if(a!=c.attributes.end())Word(at+8,g==2?RawHash(a->second):g==0?Enum(a->second,Voice,32):Enum(a->second,ObjectVoice,0));if(g==1){a=c.attributes.find("TargetObject");if(a!=c.attributes.end())Word(at+12,HashAssetId(a->second));}a=c.attributes.find("Sound");if(a!=c.attributes.end()){auto ref=t.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(t,ref,a->second);t.Pop();}Ref(c,"EvaEvent",at+4);}t.Pop();}}
};
}
unsigned int XmlcrowdTypeId(const std::string& name){return name=="CrowdResponse"?400896388u:0;}
Relo::Chunk CompileXmlcrowd(const XmlcrowdNode& input,bool bigEndian){
 using namespace CrowdDetail;static_assert(sizeof(void*)==4,"Crowd requires x86.");Check(XmlcrowdTypeId(input.name)!=0,"Unsupported crowd root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),16,1);b.Word(root+4,ParseSigned(Value(input,"Weight","100")));U32 count=static_cast<U32>(input.children.size());b.Word(root+8,count);if(count){auto thresholds=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+12),28,count));for(unsigned i=0;i<count;++i){const auto& n=input.children[i];auto at=thresholds+28*i;b.Word(at,ParseSigned(Value(n,"NumUnits","0")));auto voice=Child(n,"AudioArrayVoice");if(voice)b.VoiceArray(*voice,at+4);}b.t.Pop();}if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
