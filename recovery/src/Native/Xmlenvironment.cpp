#include "Xmlenvironment.h"
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
namespace Recovered { namespace Native { namespace EnvironmentDetail {
using Relo::U32;
enum class Kind { Float,Signed,Unsigned,Bool,Reference,Hash,Enum,Percentage,ModelFlags,Time,NullableReference };
struct Field { const char* name; unsigned offset; Kind kind; const char* fallback; const char* enumeration; };
enum class ChildKind { Inline,Pointer,List };
struct Child { const char* name; const char* type; unsigned offset; ChildKind kind; };
struct Layout { unsigned size; std::vector<Field> fields; std::vector<Child> children; };
#include "XmlenvironmentLayouts.inc"
// Process-lifetime immutable registry: no mixed-mode CRT exit destructor.
INIT_ONCE EnvironmentRegistryOnce=INIT_ONCE_STATIC_INIT;
const std::map<std::string,Layout>* EnvironmentRegistry=nullptr;
BOOL CALLBACK InitializeEnvironmentRegistry(PINIT_ONCE,PVOID,PVOID*) {
    try {EnvironmentRegistry=new const std::map<std::string,Layout>(MakeLayouts());return TRUE;}
    catch(...) {SetLastError(ERROR_NOT_ENOUGH_MEMORY);return FALSE;}
}
const std::map<std::string,Layout>& Layouts() {
    if(!InitOnceExecuteOnce(&EnvironmentRegistryOnce,InitializeEnvironmentRegistry,nullptr,nullptr))throw std::runtime_error("Environment layout initialization failed.");
    return *EnvironmentRegistry;
}
U32 Enum(const std::string& type,const std::string& value) {
#include "XmlenvironmentEnums.inc"
    throw std::invalid_argument("Unsupported environment enum.");
}
const Layout& Get(const std::string& name) {
    const auto& all=Layouts();auto it=all.find(name);
    if(it==all.end())throw std::invalid_argument("Unknown environment structure: "+name);
    return it->second;
}
void Swap(void* location) { U32 n;std::memcpy(&n,location,4);n=(n<<24)|((n<<8)&0xff0000)|((n>>8)&0xff00)|(n>>24);std::memcpy(location,&n,4); }
bool NonWhitespace(const std::string& text) { return text.find_first_not_of(" \t\r\n")!=std::string::npos; }
struct Builder {
    Relo::Tracker data;std::vector<void*> words;
    void Word(void* p,U32 n){std::memcpy(p,&n,4);words.push_back(p);}
    void Float(void* p,float n){std::memcpy(p,&n,4);words.push_back(p);}
    unsigned char* Allocate(void* p,unsigned size,unsigned count=1) {return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(p),size,count));}
    void Apply(const std::string& type,const XmlenvironmentNode& node,unsigned char* target,unsigned depth) {
        if(depth>32)throw std::invalid_argument("Environment nesting exceeds 32.");
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
            std::vector<const XmlenvironmentNode*> matches;
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
using namespace EnvironmentDetail;
unsigned int XmlenvironmentTypeId(const std::string& name) {
    if(name=="TerrainParticleMapping")return 1469498551u;
    if(name=="BounceKickTerrainMapping")return 2368878637u;
    if(name=="ShadowMap")return 4157475773u;
    if(name=="OrdersManagerSettings")return 2996766068u;
    if(name=="TerrainVisualSettings")return 1184276792u;
    if(name=="CameraSettings")return 3898802054u;
    if(name=="Weather")return 2430161325u;
    if(name=="WaterTransparency")return 1479531562u;
    if(name=="WaterSetting")return 2728605516u;
    if(name=="Environment")return 680780553u;
    if(name=="LightingSettings")return 3169690211u;
    if(name=="WaterTextureList")return 2995571269u;
    return 0;
}
Relo::Chunk CompileXmlenvironment(const XmlenvironmentNode& node,bool bigEndian) {
    if(!XmlenvironmentTypeId(node.name))throw std::invalid_argument("Unsupported environment root.");
    static_assert(sizeof(void*)==4,"Environment component requires x86.");
    Builder b;unsigned char* root=nullptr;b.data.Push(reinterpret_cast<void**>(&root),Get(node.name).size,1);b.Apply(node.name,node,root,0);
    if(bigEndian)for(auto p:b.words)Swap(p);
    return b.data.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
