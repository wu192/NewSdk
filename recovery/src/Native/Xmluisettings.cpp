#include "Xmluisettings.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "DataInitializers.h"
#include <cstring>
#include <stdexcept>
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native { namespace UiSettingsDetail {
using Relo::U32;
enum class Kind { Float,Signed,Unsigned,Bool,Reference,Hash,Enum,Percentage,ModelFlags,Time,NullableReference,String,Velocity };
struct Field { const char* name; unsigned offset; Kind kind; const char* fallback; const char* enumeration; };
enum class ChildKind { Inline,Pointer,List };
struct Child { const char* name; const char* type; unsigned offset; ChildKind kind; };
struct Layout { unsigned size; std::vector<Field> fields; std::vector<Child> children; };
#include "XmluisettingsLayouts.inc"
// Process-lifetime immutable registry: no mixed-mode CRT exit destructor.
INIT_ONCE UiSettingsRegistryOnce=INIT_ONCE_STATIC_INIT;
const std::map<std::string,Layout>* UiSettingsRegistry=nullptr;
BOOL CALLBACK InitializeUiSettingsRegistry(PINIT_ONCE,PVOID,PVOID*) {
    try {UiSettingsRegistry=new const std::map<std::string,Layout>(MakeLayouts());return TRUE;}
    catch(...) {SetLastError(ERROR_NOT_ENOUGH_MEMORY);return FALSE;}
}
const std::map<std::string,Layout>& Layouts() {
    if(!InitOnceExecuteOnce(&UiSettingsRegistryOnce,InitializeUiSettingsRegistry,nullptr,nullptr))throw std::runtime_error("UiSettings layout initialization failed.");
    return *UiSettingsRegistry;
}
U32 Enum(const std::string& type,const std::string& input) {
    const std::string value(input.c_str()); // Original EnumLookup compares NUL-terminated C strings.
    if(type=="AnimationLODType") {
        if(value=="HIGH")return 3u;
        if(value=="LOW")return 1u;
        if(value=="MEDIUM")return 2u;
        if(value=="ULTRA_HIGH")return 4u;
        if(value=="VERY_LOW")return 0u;
        return 3u;
    }
    if(type=="DecalLODType") {
        if(value=="HIGH")return 2u;
        if(value=="LOW")return 1u;
        if(value=="OFF")return 0u;
        return 2u;
    }
    if(type=="EffectsLODType") {
        if(value=="HIGH")return 3u;
        if(value=="LOW")return 1u;
        if(value=="MEDIUM")return 2u;
        if(value=="ULTRA_HIGH")return 4u;
        if(value=="VERY_LOW")return 0u;
        return 3u;
    }
    if(type=="GameLODType") {
        if(value=="CUSTOM")return 5u;
        if(value=="HIGH")return 3u;
        if(value=="LOW")return 1u;
        if(value=="MEDIUM")return 2u;
        if(value=="ULTRA_HIGH")return 4u;
        if(value=="UNKNOWN")return 4294967295u;
        if(value=="VERY_LOW")return 0u;
        return 5u;
    }
    if(type=="ModelLODType") {
        if(value=="HIGH")return 2u;
        if(value=="LOW")return 0u;
        if(value=="MEDIUM")return 1u;
        return 2u;
    }
    if(type=="ProcessorType") {
        if(value=="ATHLON")return 3u;
        if(value=="ATHLON_64")return 4u;
        if(value=="CORE_DUO")return 2u;
        if(value=="PENTIUM_4")return 1u;
        if(value=="UNKNOWN")return 0u;
        return 3u;
    }
    if(type=="ShaderLODType") {
        if(value=="HIGH")return 2u;
        if(value=="LOW")return 0u;
        if(value=="MEDIUM")return 1u;
        if(value=="ULTRA_HIGH")return 3u;
        return 2u;
    }
    if(type=="ShadowLODType") {
        if(value=="HIGH")return 3u;
        if(value=="LOW")return 1u;
        if(value=="MEDIUM")return 2u;
        if(value=="OFF")return 0u;
        if(value=="ULTRA_HIGH")return 4u;
        return 3u;
    }
    if(type=="TerrainLODType") {
        if(value=="HIGH")return 2u;
        if(value=="LOW")return 0u;
        if(value=="MEDIUM")return 1u;
        if(value=="ULTRA_HIGH")return 3u;
        return 2u;
    }
    if(type=="WaterLODType") {
        if(value=="HIGH")return 2u;
        if(value=="LOW")return 0u;
        if(value=="MEDIUM")return 1u;
        if(value=="ULTRA_HIGH")return 3u;
        return 2u;
    }
    if(type=="FXParticleSystem_Priority") {
        if(value=="ALWAYS_RENDER")return 6u;
        if(value=="HIGH_OR_ABOVE")return 2u;
        if(value=="HIGH_OR_BELOW")return 8u;
        if(value=="INVALID_PRIORITY")return 0u;
        if(value=="LOW_OR_ABOVE")return 4u;
        if(value=="LOW_OR_BELOW")return 10u;
        if(value=="MEDIUM_OR_ABOVE")return 3u;
        if(value=="MEDIUM_OR_BELOW")return 9u;
        if(value=="ULTRA_HIGH_ONLY")return 1u;
        if(value=="ULTRA_HIGH_OR_BELOW")return 7u;
        if(value=="VERY_LOW_ONLY")return 11u;
        if(value=="VERY_LOW_OR_ABOVE")return 5u;
        return 6u;
    }
    if(type=="ShadowType") {
        if(value=="ADDITIVE_DECAL")return 3u;
        if(value=="ADDITIVE_DECAL_DYNAMIC")return 5u;
        if(value=="ALPHA_DECAL")return 2u;
        if(value=="ALPHA_DECAL_DYNAMIC")return 4u;
        if(value=="DECAL")return 1u;
        if(value=="MERGE_DECAL")return 6u;
        if(value=="NONE")return 0u;
        return 3u;
    }
    if(type=="HotKeySlotGroup") {
    const char* names[]={"Bookmarks","Camera","Cheat","Misc","NonConfigurable","PlayerPowersBar","SideBar","UnitCommands"};
    const U32 numbers[]={3,0,7,5,6,4,1,2};
    for(unsigned i=0;i<8;++i)if(value==names[i])return numbers[i];
    return 3;
    }
    throw std::invalid_argument("Unknown UI settings enumeration.");
}
const Layout& Get(const std::string& name) {
    const auto& all=Layouts();auto it=all.find(name);
    if(it==all.end())throw std::invalid_argument("Unknown uisettings structure: "+name);
    return it->second;
}
void Swap(void* location) { U32 n;std::memcpy(&n,location,4);n=(n<<24)|((n<<8)&0xff0000)|((n>>8)&0xff00)|(n>>24);std::memcpy(location,&n,4); }
bool NonWhitespace(const std::string& text) { return text.find_first_not_of(" \t\r\n")!=std::string::npos; }
struct Builder {
    Relo::Tracker data;std::vector<void*> words;
    void Word(void* p,U32 n){std::memcpy(p,&n,4);words.push_back(p);}
    void Float(void* p,float n){std::memcpy(p,&n,4);words.push_back(p);}
    unsigned char* Allocate(void* p,unsigned size,unsigned count=1) {return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(p),size,count));}
    void Apply(const std::string& type,const XmluisettingsNode& node,unsigned char* target,unsigned depth) {
        if(depth>32)throw std::invalid_argument("UiSettings nesting exceeds 32.");
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
            case Kind::Velocity:Float(p,static_cast<float>(static_cast<double>(0.2f)*static_cast<double>(ParseFloat(text))));break;
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
            std::vector<const XmluisettingsNode*> matches;
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
using namespace UiSettingsDetail;
unsigned int XmluisettingsTypeId(const std::string& name) {
    if(name=="HotKeySlot")return 2800139175u;
    if(name=="MouseCursor")return 3784442503u;
    if(name=="BasicMouseCursors")return 611271731u;
    if(name=="UIMouseDragSelectionFeedback")return 611126373u;
    if(name=="UIFloatingTextDisplay")return 1559937240u;
    if(name=="UIMessageDisplay")return 1697313181u;
    if(name=="UIMilitaryCaptionDisplay")return 1043989897u;
    if(name=="UISubtitleDisplay")return 165611890u;
    if(name=="UITimerDisplay")return 951036436u;
    if(name=="DynamicGameLOD")return 2901356964u;
    if(name=="GameLODPreset")return 354222972u;
    if(name=="StaticGameLOD")return 531798225u;
    if(name=="UICoopTinting")return 4243804314u;
    if(name=="RadiusCursor")return 544776545u;
    return 0;
}
Relo::Chunk CompileXmluisettings(const XmluisettingsNode& node,bool bigEndian) {
    if(!XmluisettingsTypeId(node.name))throw std::invalid_argument("Unsupported uisettings root.");
    static_assert(sizeof(void*)==4,"UiSettings component requires x86.");
    Builder b;unsigned char* root=nullptr;b.data.Push(reinterpret_cast<void**>(&root),Get(node.name).size,1);b.Apply(node.name,node,root,0);
    if(bigEndian)for(auto p:b.words)Swap(p);
    return b.data.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

