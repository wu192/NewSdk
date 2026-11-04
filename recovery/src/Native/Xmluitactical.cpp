#include "Xmluitactical.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "DataInitializers.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native { namespace UiTacticalDetail {
using Relo::U32;
enum class Kind { Float,Signed,Unsigned,Bool,Reference,Hash,Enum,Percentage,ModelFlags,Time,NullableReference,String };
struct Field { const char* name; unsigned offset; Kind kind; const char* fallback; const char* enumeration; };
enum class ChildKind { Inline,Pointer,List };
struct Child { const char* name; const char* type; unsigned offset; ChildKind kind; };
struct Layout { unsigned size; std::vector<Field> fields; std::vector<Child> children; };
#include "XmluitacticalLayouts.inc"
// Process-lifetime immutable registry: no mixed-mode CRT exit destructor.
INIT_ONCE UiTacticalRegistryOnce=INIT_ONCE_STATIC_INIT;
const std::map<std::string,Layout>* UiTacticalRegistry=nullptr;
BOOL CALLBACK InitializeUiTacticalRegistry(PINIT_ONCE,PVOID,PVOID*) {
    try {UiTacticalRegistry=new const std::map<std::string,Layout>(MakeLayouts());return TRUE;}
    catch(...) {SetLastError(ERROR_NOT_ENOUGH_MEMORY);return FALSE;}
}
const std::map<std::string,Layout>& Layouts() {
    if(!InitOnceExecuteOnce(&UiTacticalRegistryOnce,InitializeUiTacticalRegistry,nullptr,nullptr))throw std::runtime_error("UiTactical layout initialization failed.");
    return *UiTacticalRegistry;
}
U32 Enum(const std::string& type,const std::string& input) { const std::string value(input.c_str());
    if(type=="ProductionQueueType") {
        if(value=="AIRCRAFT")return 4u;
        if(value=="INFANTRY")return 2u;
        if(value=="INVALID")return 4294967295u;
        if(value=="MAIN_STRUCTURE")return 0u;
        if(value=="OTHER_STRUCTURE")return 1u;
        if(value=="UPGRADE")return 5u;
        if(value=="VEHICLE")return 3u;
        if(value=="WATERCRAFT")return 6u;
        return 4u;
    }
    if(type=="InfiltratorEffectType") {
        if(value=="DISABLE")return 1u;
        if(value=="ENERGY")return 4u;
        if(value=="INVALID")return 4294967295u;
        if(value=="KILL")return 8u;
        if(value=="PARALYZE")return 7u;
        if(value=="RADAR_FREEZE")return 0u;
        if(value=="RESET")return 5u;
        if(value=="STEAL_MONEY")return 2u;
        if(value=="VISION")return 3u;
        if(value=="VISION_UNITS")return 6u;
        return 1u;
    }
    if(type=="AIBeaconType") {
        if(value=="Assault")return 0u;
        if(value=="HoldObject")return 2u;
        if(value=="HoldPosition")return 1u;
        if(value=="Invalid")return 4294967295u;
        return 0u;
    }
    if(type=="FactionType") {
        if(value=="Allies")return 0u;
        if(value=="Japan")return 2u;
        if(value=="Neutral")return 3u;
        if(value=="RANDOM")return 4294967295u;
        if(value=="Soviet")return 1u;
        return 0u;
    }
    if(type=="Stance") {
        if(value=="AGGRESSIVE")return 1u;
        if(value=="GUARD")return 0u;
        if(value=="HOLD_FIRE")return 3u;
        if(value=="HOLD_POSITION")return 2u;
        return 1u;
    }
    throw std::invalid_argument("Unknown tactical enum");
}
const Layout& Get(const std::string& name) {
    const auto& all=Layouts();auto it=all.find(name);
    if(it==all.end())throw std::invalid_argument("Unknown uitactical structure: "+name);
    return it->second;
}
void Swap(void* location) { U32 n;std::memcpy(&n,location,4);n=(n<<24)|((n<<8)&0xff0000)|((n>>8)&0xff00)|(n>>24);std::memcpy(location,&n,4); }
bool NonWhitespace(const std::string& text) { return text.find_first_not_of(" \t\r\n")!=std::string::npos; }
struct Builder {
    Relo::Tracker data;std::vector<void*> words;std::vector<ObjectFilter*> filters;
    void Word(void* p,U32 n){std::memcpy(p,&n,4);words.push_back(p);}
    void Float(void* p,float n){std::memcpy(p,&n,4);words.push_back(p);}
    unsigned char* Allocate(void* p,unsigned size,unsigned count=1) {return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(p),size,count));}
    void Filter(const XmluitacticalNode& node,unsigned char* root) {

        ObjectFilterInput input;input.attributes=node.attributes;input.attributes.erase("TypeId");
        for(auto& a:input.attributes)a.second=std::string(a.second.c_str()); // Original native IValue returns C strings.
        for(const auto& c:node.children){

            if(c.name=="IncludeThing")input.includeThings.push_back(c.value);
            else if(c.name=="ExcludeThing")input.excludeThings.push_back(c.value);

        }
        auto filter=reinterpret_cast<ObjectFilter*>(root);MarshalObjectFilter(input,filter,data);filters.push_back(filter);
    }
    void Apply(const std::string& type,const XmluitacticalNode& node,unsigned char* target,unsigned depth) {
        if(depth>32)throw std::invalid_argument("UiTactical nesting exceeds 32.");
        if(type=="ObjectFilter"){Filter(node,target);return;}
        const Layout& layout=Get(type);

        if(type=="String") {

            MarshalString(data,target,node.value);words.push_back(target);return;
        }
        ;
        if(type=="PackedColor") {

            const char* channels[]={"B","G","R","A"};U32 packed=0;
            for(unsigned i=0;i<4;++i){auto a=node.attributes.find(channels[i]);U32 n=a==node.attributes.end()?(i==3?255u:0u):ParseUnsigned(a->second);packed|=(n&255u)<<(8*i);}
            Word(target,packed);return;
        }
        for(const auto& f:layout.fields) {
            auto a=node.attributes.find(f.name);if(a==node.attributes.end()&&!f.fallback)continue;
            std::string text=a==node.attributes.end()?f.fallback:a->second;auto p=target+f.offset;
            switch(f.kind) {
            case Kind::Float:Float(p,ParseFloat(text));break;
            case Kind::String:MarshalString(data,p,text);words.push_back(p);break;
            case Kind::Time:Float(p,InitializeTime(text.c_str()));break;
            case Kind::Percentage:Float(p,ParsePercentage(text));break;
            case Kind::ModelFlags:MarshalModelCondition(text,reinterpret_cast<U32*>(p));for(unsigned i=0;i<15;++i)words.push_back(p+4*i);break;
            case Kind::Signed:Word(p,static_cast<U32>(ParseSigned(text)));break;
            case Kind::Unsigned:Word(p,ParseUnsigned(text));break;
            case Kind::Bool:*p=ParseBoolean(text);break;
            case Kind::Reference:MarshalReference(data,p,text);break;
            case Kind::NullableReference:{auto item=Allocate(p,4);MarshalReference(data,item,text);data.Pop();break;}
            case Kind::Hash:Word(p,HashAssetId(text));break;
            case Kind::Enum:Word(p,Enum(f.enumeration,text));break;
            }
        }
        ;
        for(const auto& child:layout.children) {
            std::vector<const XmluitacticalNode*> matches;
            for(const auto& c:node.children)if(c.name==child.name)matches.push_back(&c);
            auto p=target+child.offset;
            if(child.kind==ChildKind::List) {
                Word(p,static_cast<U32>(matches.size()));
                if(!matches.empty()) {
                    unsigned size=Get(child.type).size;auto list=Allocate(p+4,size,static_cast<unsigned>(matches.size()));
                    for(unsigned i=0;i<matches.size();++i)Apply(child.type,*matches[i],list+size*i,depth+1);
                    data.Pop();
                }
            } else {

                if(matches.empty())continue;
                if(child.kind==ChildKind::Pointer){auto item=Allocate(p,Get(child.type).size);Apply(child.type,*matches[0],item,depth+1);data.Pop();}
                else Apply(child.type,*matches[0],p,depth+1);
            }
        }
    }
};
}
using namespace UiTacticalDetail;
unsigned int XmluitacticalTypeId(const std::string& name) {
    if(name=="UIMouseTacticalCoOpAIButtonsHelpButton")return 104126924u;
    if(name=="UIMouseTacticalCommandCommunicateWithAllies")return 113029208u;
    if(name=="UIMouseTacticalToggleRepairModeButton")return 346077409u;
    if(name=="UIMouseTacticalAttackMoveButton")return 442957564u;
    if(name=="UIMouseTacticalCommandPlaceWall")return 485977567u;
    if(name=="UIMouseTacticalForceAttackMoveButton")return 694838069u;
    if(name=="UIMouseTacticalCoOpAIButtonsHoldObjectButton")return 772462768u;
    if(name=="UIMouseTacticalExitContainerButton")return 1014034471u;
    if(name=="UIMouseTacticalPlayerTechStoreButton")return 1138847078u;
    if(name=="UIMouseTacticalCommandMove")return 1234485619u;
    if(name=="UIMouseTacticalCommandSell")return 1249197161u;
    if(name=="UIMouseTacticalUnitIntroButton")return 1277109486u;
    if(name=="UIMouseTacticalCommandSelect")return 1334543376u;
    if(name=="UIMouseTacticalCoOpAIButtonsNormalButton")return 1371977384u;
    if(name=="UIMouseTacticalCommandPlaceRallyPoint")return 1474300054u;
    if(name=="UIMouseTacticalCommandHarvest")return 1499648070u;
    if(name=="UIMouseTacticalCommandToggleSelfRepair")return 1956336137u;
    if(name=="UIMouseTacticalBuildQueueTab")return 1974922393u;
    if(name=="UIMouseTacticalCommandGetCrate")return 2067763016u;
    if(name=="UIMouseTacticalDeactivateMissionHotSpotsButton")return 2047841880u;
    if(name=="UIMouseTacticalCommandForceAttack")return 2074164365u;
    if(name=="UIMouseTacticalPlanningModeButton")return 2278083954u;
    if(name=="UIMouseTacticalCommandMisc")return 2505151835u;
    if(name=="UIMouseTacticalSelectionRefinementTab")return 2635272840u;
    if(name=="UIMouseTacticalCommandForceMove")return 2697587777u;
    if(name=="UIMouseTacticalOpenAdvancedCommandsButton")return 2808773267u;
    if(name=="UIMouseTacticalSetStanceButton")return 2873949396u;
    if(name=="UIMouseTacticalCoOpAIButtonsHoldPositionButton")return 2914137727u;
    if(name=="UIMouseTacticalCommandMineOre")return 2957071942u;
    if(name=="UIMouseTacticalWaypointModeButton")return 2958905921u;
    if(name=="UIMouseTacticalReverseMoveButton")return 2987234945u;
    if(name=="UIMouseTacticalCommandEnter")return 3057018816u;
    if(name=="UIMouseTacticalCoOpAIButtonsAssaultButton")return 3094553884u;
    if(name=="UIMouseTacticalPauseButton")return 3420020972u;
    if(name=="UIMouseTacticalCommandAttack")return 3548787763u;
    if(name=="UIMouseTacticalCommandAttackMove")return 3575834349u;
    if(name=="UIMouseTacticalForceMoveButton")return 3716369729u;
    if(name=="UIMouseTacticalForceAttackButton")return 3756168709u;
    if(name=="UIMouseTacticalCommandPlaceAIBeacon")return 3809842710u;
    if(name=="UIMouseTacticalCommandDock")return 3976885676u;
    if(name=="UIMouseTacticalCommandReverseMove")return 4026878150u;
    if(name=="UIMouseTacticalBuildQueueTypeTab")return 4094578721u;
    if(name=="UIMouseTacticalSellModeButton")return 4102242385u;
    if(name=="UIMouseTacticalStopButton")return 4140422551u;
    if(name=="UIMouseTacticalCommandSetRallyPoint")return 4180354873u;
    if(name=="UIMouseTacticalFastForwardButton")return 4232364483u;
    if(name=="UIMouseTacticalGroupSelection")return 241329492u;
    if(name=="UIMouseTacticalLookAt")return 1795213437u;
    if(name=="UIMouseTacticalRadarTelestrator")return 3366599054u;
    if(name=="UIMouseTacticalTelestrator")return 1921726501u;
    if(name=="UIMouseTacticalThreatMeter")return 4143989686u;
    return 0;
}
Relo::Chunk CompileXmluitactical(const XmluitacticalNode& node,bool bigEndian) {
    if(!XmluitacticalTypeId(node.name))throw std::invalid_argument("Unsupported uitactical root.");
    static_assert(sizeof(void*)==4,"UiTactical component requires x86.");
    Builder b;unsigned char* root=nullptr;b.data.Push(reinterpret_cast<void**>(&root),Get(node.name).size,1);b.Apply(node.name,node,root,0);
    if(bigEndian){for(auto p:b.words)Swap(p);for(auto f:b.filters)EndianObjectFilter(f);}
    return b.data.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

