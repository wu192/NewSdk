#include "Xmlai.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <stdexcept>
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native {
namespace {
using U32=std::uint32_t;
enum class Kind { Bool,Float,Time,Percent,Signed,Unsigned,Enum,Flags,Status,String,Hash,CaseHash,Reference,Reject };
enum class ChildKind { List,Sorted,Pointer,Inline,Heuristics,Budget,SingleHeuristic,ReferenceList,PointerHeuristics };
struct Field { const char* name; const char* fallback; unsigned offset; Kind kind; bool pointer; const char* enumeration; unsigned bits; int unknown; };
struct Child { const char* name; const char* type; unsigned offset; ChildKind kind; };
struct Layout { unsigned size; U32 hash; std::vector<Field> fields; std::vector<Child> children; };
struct EnumView { const EnumNameValue* entries; std::size_t count; };
#if __has_include("XmlaiPersonalityEnums.inc")
#include "XmlaiPersonalityEnums.inc"
#endif
#include "XmlaiLayouts.inc"
// The immutable registry owns only decoded schema tables. It is process-lifetime
// metadata: its allocation is reclaimed by the OS, never during CLR shutdown.
// Do not register a std::map/vector destructor with the mixed-mode CRT's exit
// machinery. A callback/native InitOnce barrier publishes the completed table
// exactly once to concurrent callers; failure remains a reported initialization
// error and may be retried, rather than publishing a partial registry.
INIT_ONCE XmlaiRegistryOnce=INIT_ONCE_STATIC_INIT;
const std::map<std::string,Layout>* XmlaiRegistry=nullptr;
BOOL CALLBACK InitializeXmlaiRegistry(PINIT_ONCE, PVOID, PVOID*) {
    try {
        XmlaiRegistry=new const std::map<std::string,Layout>(MakeLayouts());
        return TRUE;
    } catch (...) {
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return FALSE;
    }
}
const std::map<std::string,Layout>& Layouts() {
    if(!InitOnceExecuteOnce(&XmlaiRegistryOnce,InitializeXmlaiRegistry,nullptr,nullptr))
        throw std::runtime_error("Xmlai layout registry initialization failed.");
    return *XmlaiRegistry;
}
const Layout& Get(const std::string& name) {
    auto it=Layouts().find(name);
    if(it==Layouts().end()) throw std::invalid_argument("Unsupported Xmlai type: "+name);
    return it->second;
}
void Swap(void* location) { auto b=static_cast<unsigned char*>(location);std::swap(b[0],b[3]);std::swap(b[1],b[2]); }
U32 HashRaw(const std::string& text) {
    // Same FastHashFn as ResourceMarshaling, preserving case for StringHash.
    const unsigned char* p=reinterpret_cast<const unsigned char*>(text.c_str());
    U32 size=static_cast<U32>(std::strlen(text.c_str())),hash=size;
    if(!size)return 0;
    auto sh=[](const unsigned char* b)->U32{return b[0]|(U32(b[1])<<8);};
    auto sb=[](unsigned char c)->U32{return static_cast<U32>(static_cast<std::int8_t>(c));};
    for(U32 n=size/4;n;--n,p+=4){hash+=sh(p);hash^=(sh(p+2)^(hash<<5))<<11;hash+=hash>>11;}
    switch(size&3){case 3:hash+=sh(p);hash^=((sb(p[2])<<2)^hash)<<16;hash+=hash>>11;break;
    case 2:hash+=sh(p);hash^=hash<<11;hash+=hash>>17;break;
    case 1:hash+=sb(*p);hash^=hash<<10;hash+=hash>>1;break;}
    hash^=hash<<3;hash+=hash>>5;hash^=hash<<2;hash+=hash>>15;return(hash<<10)^hash;
}

// Original EASTL pointer introsort: 28-element threshold, median-of-three,
// right partition first, right-child preference in heap fallback.
using SortItem=const XmlaiNode*;
float Distance(SortItem p) { auto i=p->attributes.find("Distance");return i==p->attributes.end()?0:ParseFloat(i->second); }
bool DistanceLess(SortItem a,SortItem b) { return static_cast<double>(Distance(a))<static_cast<double>(Distance(b)); }
void DistanceHeap(SortItem* first,int top,int size,int pos,SortItem value) {
    int child=pos*2+2;
    while(child<size){if(DistanceLess(first[child],first[child-1]))--child;first[pos]=first[child];pos=child;child=child*2+2;}
    if(child==size){first[pos]=first[child-1];pos=child-1;}
    int parent=(pos-1)/2;
    while(pos>top&&DistanceLess(first[parent],value)){first[pos]=first[parent];pos=parent;parent=(pos-1)/2;}
    first[pos]=value;
}
void DistanceQuick(SortItem* first,SortItem* last,int depth) {
    while(last-first>28&&depth>0){
        auto a=first,b=first+(last-first)/2,c=last-1;
        auto median=DistanceLess(*a,*b)?(DistanceLess(*b,*c)?b:(DistanceLess(*a,*c)?c:a)):
            (DistanceLess(*a,*c)?a:(DistanceLess(*b,*c)?c:b));
        auto pivot=*median;auto lo=first,hi=last;
        for(;;){while(DistanceLess(*lo,pivot))++lo;do{--hi;}while(DistanceLess(pivot,*hi));if(lo>=hi)break;std::swap(*lo,*hi);++lo;}
        --depth;DistanceQuick(lo,last,depth);last=lo;
    }
    if(depth==0){int size=static_cast<int>(last-first);if(size>=2)for(int i=(size-2)/2;i>=0;--i)DistanceHeap(first,i,size,i,first[i]);
        while(size>1){auto value=first[size-1];first[size-1]=first[0];--size;DistanceHeap(first,0,size,0,value);}}
}
void DistanceSort(std::vector<SortItem>& values) {
    if(values.empty())return;int bits=0;for(auto n=values.size();n;n>>=1)++bits;
    DistanceQuick(values.data(),values.data()+values.size(),bits*2-2);
    for(std::size_t i=1;i<values.size();++i){auto v=values[i];auto j=i;while(j&&DistanceLess(v,values[j-1])){values[j]=values[j-1];--j;}values[j]=v;}
}

// BAB adds TypeId to all named schema types. Keep it available for heuristic
// dispatch, while structural Marshal contracts ignore that metadata.

struct Builder {
    Relo::Tracker ownedData;
    Relo::Tracker& data;
    Builder():data(ownedData){}
    explicit Builder(Relo::Tracker& tracker):data(tracker){}
    std::vector<void*> words;
    std::vector<ObjectFilter*> filters;
    void Word(void* p,U32 n) { std::memcpy(p,&n,4);words.push_back(p); }
    void Float(void* p,float n) { std::memcpy(p,&n,4);words.push_back(p); }
    unsigned char* Allocate(void* location,unsigned size,unsigned count=1) {
        return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(location),size,count));
    }
    void FieldValue(const Field& f,unsigned char* root,const std::string& text) {
        unsigned char* p=root+f.offset;
        if(f.pointer){p=Allocate(p,f.kind==Kind::String?8:f.kind==Kind::Status?28:f.kind==Kind::Flags?((f.bits+31)/32)*4:4);}
        switch(f.kind){
        case Kind::Bool:*p=ParseBoolean(text)?1:0;break;
        case Kind::Float:Float(p,ParseFloat(text));break;
        case Kind::Time:Float(p,InitializeTime(text.c_str()));break;
        case Kind::Percent:Float(p,ParsePercentage(text));break;
        case Kind::Signed:Word(p,static_cast<U32>(ParseSigned(text)));break;
        case Kind::Unsigned:Word(p,ParseUnsigned(text));break;
        case Kind::Hash:Word(p,HashAssetId(text));break;
        case Kind::CaseHash:Word(p,HashRaw(text));break;
        case Kind::Reference:MarshalReference(data,p,text);break;
        case Kind::Enum:{auto e=GetEnum(f.enumeration);U32 value=f.unknown;
            for(std::size_t i=0;i<e.count;++i)if(text==e.entries[i].name){value=e.entries[i].value;break;}
            Word(p,value);break;}
        case Kind::Flags:{auto e=GetEnum(f.enumeration);MarshalEnumBitset(text,e.entries,e.count,f.unknown,f.bits,reinterpret_cast<U32*>(p));
            for(unsigned i=0;i<(f.bits+31)/32;++i)words.push_back(p+4*i);break;}
        case Kind::Status:MarshalObjectStatus(text,reinterpret_cast<U32*>(p));for(unsigned i=0;i<7;++i)words.push_back(p+4*i);break;
        case Kind::String:MarshalString(data,p,text);words.push_back(p);break;
        default:throw std::invalid_argument("Unsupported Xmlai field: "+std::string(f.name));
        }
        if(f.pointer)data.Pop();
    }
    void Filter(const XmlaiNode& node,unsigned char* root) {

        ObjectFilterInput input;input.attributes=node.attributes;input.attributes.erase("TypeId");
        for(const auto& c:node.children){

            if(c.name=="IncludeThing")input.includeThings.push_back(c.value);
            else if(c.name=="ExcludeThing")input.excludeThings.push_back(c.value);

        }
        auto filter=reinterpret_cast<ObjectFilter*>(root);
        MarshalObjectFilter(input,filter,data);filters.push_back(filter);
    }
    void Heuristic(const XmlaiNode& node,void* location,unsigned depth) {
        auto it=node.attributes.find("TypeId");
        if(it==node.attributes.end())throw std::invalid_argument("Heuristic requires BAB TypeId.");
        U32 hash=ParseUnsigned(it->second);const Layout* layout=nullptr;std::string name;
        for(const auto& pair:Layouts())if(pair.second.hash==hash&&hash){layout=&pair.second;name=pair.first;break;}
        if(!layout)throw std::invalid_argument("Unknown heuristic TypeId.");
        auto p=Allocate(location,layout->size);Apply(name,node,p,depth+1);Word(p,hash);data.Pop();
    }
    void HeuristicList(const XmlaiNode& wrapper,unsigned char* at,unsigned depth) {

        Word(at,static_cast<U32>(wrapper.children.size()));
        if(!wrapper.children.empty()){
            auto p=Allocate(at+4,4,static_cast<unsigned>(wrapper.children.size()));
            for(unsigned i=0;i<wrapper.children.size();++i)Heuristic(wrapper.children[i],p+4*i,depth+1);
            data.Pop();
        }
    }
    void Apply(const std::string& name,const XmlaiNode& node,unsigned char* root,unsigned depth) {
        if(depth>96)throw std::invalid_argument("Xmlai nesting exceeds 96.");
        if(name=="ObjectFilter"){Filter(node,root);return;}
        const Layout& layout=Get(name);

        for(const auto& a:node.attributes){
            // These children extend BaseAssetType or declare pipelineOnly id.
            // Their IDs survive BAB inheritance but original Marshal ignores them.
            const bool childPipelineId = a.first=="id" &&
                (name=="AIStrategicState" || name=="AIBudgetState" ||
                 name=="AIBuildState" || name=="AIBuildDelayRange");
            if((XmlaiTypeId(name)&&(a.first=="id"||a.first=="inheritFrom"))||
               a.first=="TypeId"||childPipelineId)continue;

        }
        for(const auto& f:layout.fields){
            auto a=node.attributes.find(f.name);
            if(a!=node.attributes.end())FieldValue(f,root,a->second);
            else if(f.fallback)FieldValue(f,root,f.fallback);
        }
        ;
        for(const auto& r:layout.children){
            std::vector<const XmlaiNode*> children;for(const auto& c:node.children)if(c.name==r.name)children.push_back(&c);
            auto at=root+r.offset;
            if(r.kind==ChildKind::ReferenceList){
                Word(at,static_cast<U32>(children.size()));
                if(!children.empty()){
                    auto p=Allocate(at+4,4,static_cast<unsigned>(children.size()));
                    for(unsigned i=0;i<children.size();++i){
                        const auto& c=*children[i];

                        MarshalReference(data,p+4*i,c.value);
                    }
                    data.Pop();
                }
            } else if(r.kind==ChildKind::List||r.kind==ChildKind::Sorted){
                if(r.kind==ChildKind::Sorted){
                    for(auto c:children){auto i=c->attributes.find("Distance");if(i!=c->attributes.end()&&!std::isfinite(ParseFloat(i->second)))throw std::invalid_argument("Nonfinite sorted distance.");}
                    DistanceSort(children);
                }
                Word(at,static_cast<U32>(children.size()));
                if(!children.empty()){
                    unsigned size=Get(r.type).size;auto p=Allocate(at+4,size,static_cast<unsigned>(children.size()));
                    for(unsigned i=0;i<children.size();++i)Apply(r.type,*children[i],p+size*i,depth+1);
                    data.Pop();
                }
            } else {

                if(children.empty())continue;
                const auto& c=*children[0];
                if(r.kind==ChildKind::Pointer){unsigned size=std::string(r.type)=="ObjectFilter"?112:Get(r.type).size;auto p=Allocate(at,size);Apply(r.type,c,p,depth+1);data.Pop();}
                else if(r.kind==ChildKind::Inline)Apply(r.type,c,at,depth+1);
                else if(r.kind==ChildKind::Heuristics)HeuristicList(c,at,depth+1);
                else if(r.kind==ChildKind::PointerHeuristics){auto p=Allocate(at,8);HeuristicList(c,p,depth+1);data.Pop();}
                else if(r.kind==ChildKind::Budget){

                    Word(at,static_cast<U32>(c.children.size()));
                    if(!c.children.empty()){auto p=Allocate(at+4,8,static_cast<unsigned>(c.children.size()));for(unsigned i=0;i<c.children.size();++i)Apply(r.type,c.children[i],p+8*i,depth+1);data.Pop();}
                } else if(r.kind==ChildKind::SingleHeuristic){

                    Heuristic(c.children[0],at,depth+1);
                }
            }
        }
    }
};
}
void MarshalXmlaiHeuristics(Relo::Tracker& tracker,void* list,const XmlaiNode& wrapper,bool bigEndian) {
    Builder b(tracker);b.HeuristicList(wrapper,static_cast<unsigned char*>(list),0);
    if(bigEndian){for(void* p:b.words)Swap(p);for(auto f:b.filters)EndianObjectFilter(f);}
}
unsigned int XmlaiTypeId(const std::string& name) {
    if(name=="AIPersonalityDefinition")return 3604279694u;
    if(name=="AIStrategicStateDefinition")return 607123156u;
    if(name=="AIMicroManagerData")return 2423780215u;
    if(name=="AIBudgetStateDefinition")return 2893598307u;
    if(name=="AIStateDefinition")return 3239935619u;
    if(name=="AITargetingHeuristic")return 926814458u;
    return 0;
}
Relo::Chunk CompileXmlai(const XmlaiNode& node,bool bigEndian) {
    if(!XmlaiTypeId(node.name))throw std::invalid_argument("Unsupported Xmlai root.");
    static_assert(sizeof(void*)==4,"Xmlai requires an x86 host.");
    Builder b;unsigned char* root=nullptr;b.data.Push(reinterpret_cast<void**>(&root),Get(node.name).size,1);
    b.Apply(node.name,node,root,0);
    if(bigEndian){for(void* p:b.words)Swap(p);for(auto f:b.filters)EndianObjectFilter(f);}
    return b.data.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

