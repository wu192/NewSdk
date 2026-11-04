#include "Xmlrenderdevice.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace RenderDeviceDetail {
using Relo::U32;using Node=XmlrenderdeviceNode;
#include "XmlrenderdeviceEnums.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
const Node* Child(const Node& n,const char* name){const Node* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
void Ref(Relo::Tracker& tracker,const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
void Filter(Relo::Tracker& tracker,const Node& n,ObjectFilter* target){
    ObjectFilterInput input;
    // The original IValue contract supplies C strings, including enum attributes.
    for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=std::string(a.second.c_str());
    for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(std::string(c.value.c_str()));}
    MarshalObjectFilter(input,target,tracker);
}
}
unsigned int XmlrenderdeviceTypeId(const std::string& name){if(name=="W3DDeviceSettings")return 1469055344u;if(name=="ScorchMarkSettings")return 2383722691u;return 0;}
namespace RenderDeviceDetail {
template<unsigned N>U32 Enum(const std::string& text,const EnumNameValue(&values)[N],U32 fallback){for(auto x:values)if(std::strcmp(text.c_str(),x.name)==0)return x.value;return fallback;}
struct Builder{
    Relo::Tracker tracker;std::vector<void*> words;std::vector<ObjectFilter*> filters;
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void Real(void* p,float value){std::memcpy(p,&value,4);words.push_back(p);}
    std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
    void RGB(const Node& n,unsigned char* p){const char* names[]={"R","G","B"};for(unsigned i=0;i<3;++i){auto a=n.attributes.find(names[i]);if(a!=n.attributes.end())Real(p+4*i,ParseFloat(a->second));}}
    void Bones(const Node& n,unsigned char* root){U32 count=0;for(const auto& c:n.children)if(c.name=="StandardPublicBone")++count;Word(root+156,count);
        if(count){auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+160),8,count));unsigned i=0;for(const auto& c:n.children)if(c.name=="StandardPublicBone"){auto p=data+8*i++;MarshalString(tracker,p,c.value);words.push_back(p);}tracker.Pop();}}
    void Scorches(const Node& n,unsigned char* root){U32 count=static_cast<U32>(n.children.size());Word(root+4,count);if(!count)return;
        auto data=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+8),12,count));unsigned i=0;
        for(const auto& c:n.children){auto p=data+12*i++;auto terrain=c.attributes.find("TerrainClass");if(terrain!=c.attributes.end())Word(p,Enum(terrain->second,ETerrainClassType,3));U32 inner=static_cast<U32>(c.children.size());Word(p+4,inner);
            if(inner){auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(p+8),4,inner));unsigned j=0;for(const auto& s:c.children){Word(list+4*j++,Enum(s.value,ETerrainScorchType,~0u));}tracker.Pop();}}
        tracker.Pop();}
};
}
Relo::Chunk CompileXmlrenderdevice(const XmlrenderdeviceNode& input,bool bigEndian){
    using namespace RenderDeviceDetail;static_assert(sizeof(void*)==4,"Render device requires x86.");unsigned id=XmlrenderdeviceTypeId(input.name);Check(id!=0,"Unsupported Render device root.");bool device=id==1469055344u;
    Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),device?168:12,1);
    if(device){
        b.Word(root+4,ParseSigned(b.Value(input,"MaxTerrainTracks","100")));b.Real(root+8,ParseFloat(b.Value(input,"OpacityOfSimpleMergeDecals","35%"))*0.01f);b.Real(root+12,ParseFloat(b.Value(input,"OccludedColorLuminanceScale","0.5")));
        b.Word(root+16,ParseSigned(b.Value(input,"MaxRoadSegments","4000")));b.Word(root+20,ParseSigned(b.Value(input,"MaxRoadVertex","5000")));b.Word(root+24,ParseSigned(b.Value(input,"MaxRoadIndex","10000")));b.Word(root+28,Enum(b.Value(input,"DefaultShroudStatus","FOGGED"),EShroudStatusType,1));
        root[164]=ParseBoolean(b.Value(input,"UseSimpleHordeDecals","false"));root[165]=static_cast<unsigned char>(ParseUnsigned(b.Value(input,"FogAlpha","63")));root[166]=static_cast<unsigned char>(ParseUnsigned(b.Value(input,"ShroudAlpha","0")));root[167]=static_cast<unsigned char>(ParseUnsigned(b.Value(input,"ClearAlpha","255")));
        auto color=Child(input,"ShroudColor");if(color)b.RGB(*color,root+32);auto filter=Child(input,"TreeFadeObjectFilter");if(filter){auto target=reinterpret_cast<ObjectFilter*>(root+44);Filter(b.tracker,*filter,target);b.filters.push_back(target);}b.Bones(input,root);
    }else{b.Scorches(input,root);}
    if(bigEndian){for(auto p:b.words)Swap(p);for(auto p:b.filters)EndianObjectFilter(p);}return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
