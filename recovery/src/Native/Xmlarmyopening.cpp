#include "Xmlarmyopening.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace ArmyOpeningDetail {
using Relo::U32;using Node=XmlarmyopeningNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
U32 Account(const std::string& s){const char* names[]={"DEFENSE","INVESTMENT","PRODUCTION","SLUSH_FUND","SPECIAL_OPERATIONS","TECHNOLOGY"};const U32 values[]={3,0,2,5,1,4};for(unsigned i=0;i<6;++i)if(std::strcmp(s.c_str(),names[i])==0)return values[i];return 3;}
struct Builder {
    Relo::Tracker t;std::vector<void*> words;
    void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
    void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
    void String(void* p,const std::string& s){MarshalString(t,p,s);words.push_back(p);}
};
}
unsigned int XmlarmyopeningTypeId(const std::string& name){if(name=="ArmyDefinition")return 3587539190u;if(name=="SkirmishOpeningMove")return 3971904488u;return 0;}
Relo::Chunk CompileXmlarmyopening(const XmlarmyopeningNode& n,bool bigEndian){using namespace ArmyOpeningDetail;static_assert(sizeof(void*)==4,"Army opening requires x86.");
    unsigned id=XmlarmyopeningTypeId(n.name);Check(id!=0,"Unsupported army opening root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),id==3587539190u?64:20,1);

    if(n.attributes.count("Side"))b.String(root+4,Value(n,"Side"));
    if(id==3587539190u){
        const char* fields[]={"MCVTemplate","GathererTemplate","ExpanderTemplate","EnergyProducer","PrimaryResourceNodeCollector","PrimaryResourceThingToBuild","SecondaryResourceNodeCollector","VehicleRepairFacility","NavalRepairFacility","AircraftRepairFacility","WallPiece"};
        for(unsigned i=0;i<11;++i)if(n.attributes.count(fields[i]))b.Word(root+12+4*i,HashAssetId(Value(n,fields[i])));
        U32 count=static_cast<U32>(n.children.size());b.Word(root+56,count);if(count){auto items=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+60),4,count));for(U32 i=0;i<count;++i){const auto& c=n.children[i];b.Word(items+4*i,HashAssetId(c.value));}b.t.Pop();}
    }else{U32 count=static_cast<U32>(n.children.size());b.Word(root+12,count);if(count){auto items=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+16),20,count));for(U32 i=0;i<count;++i){const auto& c=n.children[i];auto q=items+20*i;b.Float(q,InitializeTime(Value(c,"Time","0s").c_str()));b.String(q+4,Value(c,"Build"));b.Word(q+12,Account(Value(c,"Account","SLUSH_FUND")));b.Word(q+16,ParseUnsigned(Value(c,"Deposit","0")));}b.t.Pop();}}
    if(bigEndian)for(void* word:b.words)Swap(word);
    return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
