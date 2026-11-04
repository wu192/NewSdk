#include "Xmlobjectsets.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlobjectsetsDetail {
using N=XmlobjectsetsNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}

const EnumNameValue ERegisteredObjectSetId[]={{"AIRCRAFT",26},{"AI_PLAYER_POWER_UPDATABLE",48},{"BASE_DEFENSES",46},{"BASE_DEFENSES_AND_GARRISONS",16},{"BEACONS",14},{"CONYARDS",33},{"DOZERS",19},{"ENCLOSURES",52},{"ENEMY_BASE_DEFENSES",47},{"ENEMY_ENGINEERS",31},{"ENGINEERS",30},{"FACTION_STRUCTURES",11},{"FACTORIES",34},{"FARMS",45},{"GARRISONS",1},{"GLOBAL_HARVESTERS",6},{"GLOBAL_ORE_REFINERIES",8},{"GLOBAL_SIGMA_REFINERIES",9},{"GLOBAL_TIBERIUM_REFINERIES",7},{"HARVESTERS",20},{"HORDES",28},{"HORDE_MEMBERS",29},{"INFANTRY",23},{"MCVS",18},{"MOBILE_GROUND_UNITS",17},{"NEUTRAL_TECH",2},{"ORE_MINES",4},{"ORE_REFINERIES",42},{"OUTPOSTS",22},{"PLAYER_EVERYTHING",0},{"PLAYER_NEUTRAL_TECH",12},{"POWER_PLANTS",38},{"RADARS",36},{"REFINERIES",39},{"REFINERIES_AND_REFINERY_EGGS",40},{"SHIPS",25},{"SIGMA_FIELDS",5},{"SIGMA_REFINERIES",43},{"SILOS",44},{"SKIRMISH_AI_DEFENSE_TARGETS",49},{"SKIRMISH_AI_NORMAL_COMBAT_UNITS",51},{"SKIRMISH_AI_THREATS_TO_DODGE",50},{"STRUCTURES",10},{"SURVEYORS",21},{"TECH_CENTERS",37},{"TIBERIUM_FIELDS",3},{"TIBERIUM_REFINERIES",41},{"UNITS",13},{"UNITS_AND_STRUCTURES",15},{"UNIT_FACTORIES",35},{"UNPACKS_INTO_BUILDING_UNITS",27},{"VEHICLES",24},{"VICTORY_CONDITION_STRUCTURES",32}};
const EnumNameValue ERegisteredObjectSetType[]={{"GLOBAL",0},{"PLAYER",1}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;std::vector<ObjectFilter*> filters;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 template<size_t C>void Bits(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback,unsigned count){MarshalEnumBitset(s,e,C,fallback,count,reinterpret_cast<U*>(p));for(unsigned i=0;i<(count+31)/32;++i)words.push_back(static_cast<unsigned char*>(p)+4*i);}
 void Filter(const N& n,unsigned char* p){ObjectFilterInput input;for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=std::string(a.second.c_str());for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(std::string(c.value.c_str()));}auto q=reinterpret_cast<ObjectFilter*>(p);MarshalObjectFilter(input,q,t);filters.push_back(q);}
 template<size_t C>void Enum(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback){int value=fallback;for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0){value=a.value;break;}Word(p,static_cast<U>(value));}
};
}
unsigned XmlobjectsetsTypeId(const std::string& n){if(n=="ObjectFilterAsset")return 1151702845u;if(n=="RegisteredObjectSet")return 814796096u;if(n=="ObjectVisionRangeItem")return 772930644u;return 0;}
Relo::Chunk CompileXmlobjectsets(const XmlobjectsetsNode& input,bool bigEndian){using namespace XmlobjectsetsDetail;static_assert(sizeof(void*)==4,"Object sets require x86");U id=XmlobjectsetsTypeId(input.name);Check(id!=0,"Unknown object sets root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==1151702845u?116:id==814796096u?124:120,1);if(id==814796096u){if(input.attributes.count("SetId"))b.Enum(p+4,Get(input,"SetId"),ERegisteredObjectSetId,26);if(input.attributes.count("Type"))b.Enum(p+8,Get(input,"Type"),ERegisteredObjectSetType,0);if(auto n=Child(input,"ObjectFilter"))b.Filter(*n,p+12);}else if(id==772930644u){b.Float(p+4,ParsePercentage(Get(input,"Range","100")));if(auto n=Child(input,"Filter"))b.Filter(*n,p+8);}else{if(auto n=Child(input,"Filter"))b.Filter(*n,p+4);}if(bigEndian){for(auto q:b.words)Swap(q);for(auto q:b.filters)EndianObjectFilter(q);}return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
