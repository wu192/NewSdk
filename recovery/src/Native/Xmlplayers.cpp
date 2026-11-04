#include "Xmlplayers.h"
#include "Xmlai.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <algorithm>
#include <cctype>
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace PlayersDetail {
using U32=Relo::U32;
enum class Kind { Float,Unsigned,Signed,Bool,Byte,Time,Angle,Percent,String,Hash,Reference,Enum,Model,Tokens };
enum class ChildKind { Inline,Pointer,List };
struct Field { const char* name;const char* fallback;unsigned offset;Kind kind;const char* enumeration; };
struct Child { const char* name;const char* type;unsigned offset;ChildKind kind; };
struct Layout { unsigned size,typeId;const Field* fields;unsigned fieldCount;const Child* children;unsigned childCount; };
#include "XmlplayersLayouts.inc"
#include "XmlplayersEnums.inc"
bool Metadata(const std::string& name) { return name=="TypeId"; }

void Swap(void* p) { auto b=static_cast<unsigned char*>(p);std::swap(b[0],b[3]);std::swap(b[1],b[2]); }
XmlaiNode AiNode(const XmlplayersNode& node) {
    XmlaiNode a;a.name=node.name;a.value=node.value;a.attributes=node.attributes;
    for(const auto& c:node.children)a.children.push_back(AiNode(c));return a;
}
struct Builder {
    Relo::Tracker data;
    std::vector<void*> words;
    std::vector<ObjectFilter*> filters;
    void Word(void* at,U32 value) { std::memcpy(at,&value,4);words.push_back(at); }
    void Float(void* at,float value) { std::memcpy(at,&value,4);words.push_back(at); }
    unsigned char* Push(void* at,unsigned size,unsigned count=1) { return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(at),size,count)); }
    void Filter(const XmlplayersNode& node,unsigned char* at) {

        ObjectFilterInput input;input.attributes=node.attributes;input.attributes.erase("TypeId");
        for(const auto& c:node.children){

            if(c.name=="IncludeThing")input.includeThings.push_back(c.value);
            else if(c.name=="ExcludeThing")input.excludeThings.push_back(c.value);

        }
        auto filter=reinterpret_cast<ObjectFilter*>(at);MarshalObjectFilter(input,filter,data);filters.push_back(filter);
    }
    void FieldValue(const Field& f,unsigned char* root,const std::string& text) {
        auto p=root+f.offset;
        switch(f.kind) {
        case Kind::Float:Float(p,ParseFloat(text));break;
        case Kind::Time:Float(p,InitializeTime(text.c_str()));break;
        case Kind::Angle:Float(p,InitializeAngle(text.c_str()));break;
        case Kind::Percent:Float(p,ParsePercentage(text));break;
        case Kind::Unsigned:Word(p,ParseUnsigned(text));break;
        case Kind::Signed:Word(p,static_cast<U32>(ParseSigned(text)));break;
        case Kind::Bool:*p=ParseBoolean(text)?1:0;break;
        case Kind::Byte:*p=static_cast<unsigned char>(ParseUnsigned(text));break;
        case Kind::Enum:Word(p,PlayerEnum(f.enumeration,text));break;
        case Kind::Hash:Word(p,HashAssetId(text));break;
        case Kind::Reference:MarshalReference(data,p,text);break;
        case Kind::String:MarshalString(data,p,text);words.push_back(p);break;
        case Kind::Model:MarshalModelCondition(text,reinterpret_cast<U32*>(p));for(unsigned i=0;i<15;++i)words.push_back(p+4*i);break;
        case Kind::Tokens:{
            std::vector<std::string> tokens;const char* s=text.c_str();
            while(*s){while(*s&&std::isspace(static_cast<unsigned char>(*s)))++s;const char* first=s;while(*s&&!std::isspace(static_cast<unsigned char>(*s)))++s;if(s!=first)tokens.emplace_back(first,s);}
            Word(p,static_cast<U32>(tokens.size()));
            if(!tokens.empty()){auto values=Push(p+4,4,static_cast<unsigned>(tokens.size()));for(unsigned i=0;i<tokens.size();++i)Word(values+4*i,ParseUnsigned(tokens[i]));data.Pop();}break;
        }
        }
    }
    void Apply(const std::string& name,const XmlplayersNode& node,unsigned char* root,bool big,unsigned depth=0) {
        if(depth>96)throw std::invalid_argument("Player nesting exceeds 96");
        if(name=="ObjectFilter"){Filter(node,root);return;}
        if(name=="Heuristics"){MarshalXmlaiHeuristics(data,root,AiNode(node),big);return;}
        if(name=="ReferenceText"||name=="StringText"||name=="HashText"||name=="SignedText"||name=="BoolText"){

            if(name=="ReferenceText")MarshalReference(data,root,node.value);
            else if(name=="StringText"){MarshalString(data,root,node.value);words.push_back(root);}
            else if(name=="HashText")Word(root,HashAssetId(node.value));
            else if(name=="SignedText")Word(root,static_cast<U32>(ParseSigned(node.value)));
            else *root=ParseBoolean(node.value)?1:0;
            return;
        }
        if(name=="Color"){

            U32 packed=0;const char* names[]={"B","G","R","A"};
            for(unsigned i=0;i<4;++i){auto a=node.attributes.find(names[i]);unsigned value=a==node.attributes.end()?(i==3?255:0):ParseUnsigned(a->second);packed|=(value&255u)<<(8*i);}Word(root,packed);return;
        }
        Layout layout=GetLayout(name);

        for(unsigned i=0;i<layout.fieldCount;++i){const auto& f=layout.fields[i];auto a=node.attributes.find(f.name);if(a!=node.attributes.end())FieldValue(f,root,a->second);else if(f.fallback)FieldValue(f,root,f.fallback);}
        ;
        for(unsigned i=0;i<layout.childCount;++i){
            const auto& f=layout.children[i];std::vector<const XmlplayersNode*> children;for(const auto& c:node.children)if(c.name==f.name)children.push_back(&c);auto at=root+f.offset;
            if(f.kind==ChildKind::List){
                Word(at,static_cast<U32>(children.size()));
                if(!children.empty()){unsigned size=GetLayout(f.type).size;auto items=Push(at+4,size,static_cast<unsigned>(children.size()));for(unsigned j=0;j<children.size();++j)Apply(f.type,*children[j],items+size*j,big,depth+1);data.Pop();}
            } else {

                if(children.empty())continue;
                if(f.kind==ChildKind::Pointer){unsigned size=std::string(f.type)=="ObjectFilter"?112:GetLayout(f.type).size;auto item=Push(at,size);Apply(f.type,*children[0],item,big,depth+1);data.Pop();}
                else Apply(f.type,*children[0],at,big,depth+1);
            }
        }
    }
};
}
using namespace PlayersDetail;
unsigned XmlplayersTypeId(const std::string& name) { try{return GetLayout(name).typeId;}catch(const std::exception&){return 0;} }
Relo::Chunk CompileXmlplayers(const XmlplayersNode& node,bool big) {
    static_assert(sizeof(void*)==4,"Players requires x86");
    Layout layout=GetLayout(node.name);if(!layout.typeId)throw std::invalid_argument("Not a player root");
    Builder b;unsigned char* root=nullptr;b.data.Push(reinterpret_cast<void**>(&root),layout.size,1);b.Apply(node.name,node,root,big);
    if(big){for(void* p:b.words)Swap(p);for(auto f:b.filters)EndianObjectFilter(f);}
    return b.data.MakeRelocatable(big,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
