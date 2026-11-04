#include "Xmltacticalhotkeys.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace TacticalHotkeysDetail {
using Relo::U32;
using Node=XmltacticalhotkeysNode;
struct Field {const char* name;unsigned offset;};
struct EnumEntry {const char* name;int value;};
#include "XmltacticalhotkeysFields.inc"

void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Word(void* p,U32 value){std::memcpy(p,&value,4);}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);Word(p,(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24));}
void Reference(Relo::Tracker& t,unsigned char* p,const Node& n,const char* name){auto a=n.attributes.find(name);if(a!=n.attributes.end())MarshalReference(t,p,a->second);}
int Enum(const std::string& value,const EnumEntry* entries,unsigned count,int fallback){for(unsigned i=0;i<count;++i)if(std::strcmp(value.c_str(),entries[i].name)==0)return entries[i].value;return fallback;}
struct Builder {
    Relo::Tracker tracker;bool big;
    explicit Builder(bool endian):big(endian){}
    void RefList(const Node& n,const char* name,unsigned char* root,unsigned at) {
        std::vector<const Node*> items;for(const auto& c:n.children)if(c.name==name)items.push_back(&c);
        const U32 count=static_cast<U32>(items.size());Word(root+at,count);
        if(count){
            auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+at+4),4,count));
            for(U32 i=0;i<count;++i){MarshalReference(tracker,list+4*i,items[i]->value);}
            tracker.Pop();
        }
        if(big)Swap(root+at);
    }
    void EnumRefList(const Node& n,const char* name,unsigned char* root,unsigned at,const char* enumAttr,const char* refAttr,const EnumEntry* entries,unsigned enumCount,int fallback) {
        std::vector<const Node*> items;for(const auto& c:n.children)if(c.name==name)items.push_back(&c);
        const U32 count=static_cast<U32>(items.size());Word(root+at,count);
        if(count){
            auto list=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+at+4),8,count));
            for(U32 i=0;i<count;++i){
                auto item=list+8*i;
                auto value=items[i]->attributes.find(enumAttr);
                if(value!=items[i]->attributes.end())Word(item,static_cast<U32>(Enum(value->second,entries,enumCount,fallback)));
                Reference(tracker,item+4,*items[i],refAttr);if(big)Swap(item);
            }
            tracker.Pop();
        }
        if(big)Swap(root+at);
    }
};
}
unsigned int XmltacticalhotkeysTypeId(const std::string& name){
    if(name=="UIMouseTacticalSelectionDetailsButtonHotKeys")return 1296329066u;
    if(name=="UIMouseTacticalMainCommandBarHotKeys")return 2591692061u;
    if(name=="UIMouseTacticalPlayerPowerHotKeys")return 2283998413u;
    if(name=="UIMouseTacticalMiscHotKeys")return 2014061824u;
    if(name=="UIMouseHUDElementHotKeyGate")return 2738273474u;
    return 0;
}
Relo::Chunk CompileXmltacticalhotkeys(const XmltacticalhotkeysNode& input,bool bigEndian){
    using namespace TacticalHotkeysDetail;
    static_assert(sizeof(void*)==4,"Tactical hotkeys require x86.");
    const unsigned id=XmltacticalhotkeysTypeId(input.name);Check(id!=0,"Unsupported tactical hotkey root.");
    const unsigned size=id==2591692061u?24:id==2014061824u?200:12;
    Builder b(bigEndian);unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),size,1);
    if(id==1296329066u){b.RefList(input,"UnitAbilitySlot",root,4);}
    else if(id==2283998413u){b.RefList(input,"Slot",root,4);}
    else if(id==2591692061u){

        Reference(b.tracker,root+4,input,"SelectionRefinementPageSlot");
        b.EnumRefList(input,"BuildQueuePage",root,8,"QueueType","Slot",QueueValues,8,4);
        b.RefList(input,"ButtonSlot",root,16);
    }else if(id==2014061824u){

        for(const auto& f:MiscFields)Reference(b.tracker,root+f.offset,input,f.name);
        b.EnumRefList(input,"StanceSettings",root,192,"Stance","Slot",StanceValues,4,1);
    }else{

        b.EnumRefList(input,"ElementHotKeyMapping",root,4,"ElementId","HotKeySlot",ElementValues,36,21);
    }
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
