#include "Xmluichat.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace UiChatDetail {
using Relo::U32;using Node=XmluichatNode;
struct Entry {const char* name;int value;};
#include "XmluichatEnums.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

const Node* Child(const Node& n,const char* name){const Node* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Value(const Node& n,const char* name,const char* fallback=""){auto a=n.attributes.find(name);return a==n.attributes.end()?fallback:a->second;}
int Enum(const std::string& value,const Entry* entries,unsigned count,int fallback=0){for(unsigned i=0;i<count;++i)if(std::strcmp(value.c_str(),entries[i].name)==0)return entries[i].value;return fallback;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder {
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 value){std::memcpy(p,&value,4);words.push_back(p);}
    void Real(void* p,float value){std::memcpy(p,&value,4);words.push_back(p);}
    unsigned char* Allocate(void* pointer,unsigned size,unsigned count=1){return static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(pointer),size,count));}
    void String(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end()){MarshalString(tracker,p,a->second);words.push_back(p);}}
    void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(tracker,p,a->second);}
    void Close(const Node* n,unsigned char* p){if(!n)return;String(*n,"Title",p);}
    void Help(const Node* n,unsigned char* pointer){
        if(!n)return;
        auto p=Allocate(pointer,32);const char* names[]={"Title","Description","TypeDescription","Instructions"};
        for(unsigned i=0;i<4;++i)String(*n,names[i],p+8*i);tracker.Pop();
    }
    void Messages(const Node& n,unsigned char* p){
        const U32 count=static_cast<U32>(n.children.size());Word(p+4,count);
        if(count){auto list=Allocate(p+8,4,count);for(U32 i=0;i<count;++i){
            auto a=n.children[i].attributes.find("MessageRef");
            if(a!=n.children[i].attributes.end())Word(list+4*i,HashAssetId(a->second));
        }tracker.Pop();}
    }
    void Groups(const Node& n,unsigned char* p){
        std::vector<const Node*> groups;for(const auto& c:n.children)if(c.name=="ChatMessageGroup")groups.push_back(&c);
        const U32 count=static_cast<U32>(groups.size());Word(p+12,count);
        if(count){auto list=Allocate(p+16,12,count);for(U32 i=0;i<count;++i){
            auto item=list+12*i;
            Word(item,static_cast<U32>(Enum(Value(*groups[i],"Faction","Neutral"),Factions,5)));
            Messages(*groups[i],item);
        }tracker.Pop();}
    }
    void Menus(const Node& n,unsigned char* p,unsigned offset,unsigned depth){
        Check(depth<=32,"UI chat menu depth exceeds 32.");
        std::vector<const Node*> menus;for(const auto& c:n.children)if(c.name=="MenuItem")menus.push_back(&c);
        const U32 count=static_cast<U32>(menus.size());Word(p+offset,count);
        if(count){auto list=Allocate(p+offset+4,32,count);for(U32 i=0;i<count;++i){
            const Node& menu=*menus[i];auto item=list+32*i;
            String(menu,"Title",item);auto destination=menu.attributes.find("ChatDestination");
            if(destination!=menu.attributes.end()){auto value=Allocate(item+8,4);Word(value,static_cast<U32>(Enum(destination->second,Destinations,2)));tracker.Pop();}
            Groups(menu,item);
            if(depth==32){for(const auto& c:menu.children)Check(c.name!="MenuItem","UI chat menu depth exceeds 32.");Word(item+20,0);}
            else Menus(menu,item,20,depth+1);
            auto close=Child(menu,"CloseMenuItem");
            if(close){auto value=Allocate(item+28,8);Close(close,value);tracker.Pop();}
        }tracker.Pop();}
    }
};
}
unsigned int XmluichatTypeId(const std::string& name){
    if(name=="UIQuickChatMessage")return 4098431913u;
    if(name=="UIMouseTacticalQuickChatDisplay")return 2891878770u;
    if(name=="UIMouseVoiceChat")return 3746130610u;
    return 0;
}
Relo::Chunk CompileXmluichat(const XmluichatNode& input,bool bigEndian){
    using namespace UiChatDetail;static_assert(sizeof(void*)==4,"UI chat requires x86.");
    const unsigned id=XmluichatTypeId(input.name);Check(id!=0,"Unsupported UI chat root.");
    Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==4098431913u?16:id==2891878770u?28:20,1);
    if(id==4098431913u){b.String(input,"MessageText",root+4);b.Ref(input,"Sound",root+12);}
    else if(id==2891878770u){b.Ref(input,"CloseKey",root+4);b.Real(root+8,InitializeTime(Value(input,"DelayDuration","0s").c_str()));b.Menus(input,root,12,1);b.Close(Child(input,"CloseMenuItem"),root+20);}
    else{b.Ref(input,"ToggleModeHotKeySlot",root+4);b.Ref(input,"TalkHotKeySlot",root+8);b.Help(Child(input,"ModeButtonHelp"),root+12);b.Help(Child(input,"TalkButtonHelp"),root+16);}
    if(bigEndian)for(void* p:b.words)Swap(p);
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
