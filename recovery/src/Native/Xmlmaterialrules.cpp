#include "Xmlmaterialrules.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace MaterialRulesDetail {
using Relo::U32;using Node=XmlmaterialrulesNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder {
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
    void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
    void Rule(const Node& n,unsigned char* p,bool root=false){
        auto a=n.attributes.find("IfOriginalShaderIs");if(a!=n.attributes.end()){auto q=tracker.Push(reinterpret_cast<void**>(p),4,1);Word(q,HashAssetId(a->second));tracker.Pop();}
        a=n.attributes.find("ReplaceShaderName");if(a!=n.attributes.end())Word(p+4,HashAssetId(a->second));
        a=n.attributes.find("ReplaceTechniqueName");if(a!=n.attributes.end()){MarshalString(tracker,p+8,a->second);words.push_back(p+8);}
    }
};
}
unsigned int XmlmaterialrulesTypeId(const std::string& name){if(name=="Road")return 3516764358u;if(name=="ShaderOverride")return 3166846828u;if(name=="ShaderOverrideRule")return 3648907125u;return 0;}
Relo::Chunk CompileXmlmaterialrules(const XmlmaterialrulesNode& n,bool bigEndian){using namespace MaterialRulesDetail;static_assert(sizeof(void*)==4,"Material rules require x86.");
    unsigned id=XmlmaterialrulesTypeId(n.name);Check(id!=0,"Unsupported material rules root.");Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==3516764358u?24:16,1);
    if(id==3516764358u){const char* refs[]={"Texture","NormalTexture"};for(unsigned i=0;i<2;++i){auto a=n.attributes.find(refs[i]);if(a!=n.attributes.end())MarshalReference(b.tracker,root+4+4*i,a->second);}const char* names[]={"RoadWidth","RoadWidthInTexture","Priority"};for(unsigned i=0;i<3;++i){auto a=n.attributes.find(names[i]);if(a!=n.attributes.end())b.Float(root+12+4*i,ParseFloat(a->second));}}
    else if(id==3648907125u)b.Rule(n,root,true);
    else{auto priority=n.attributes.find("Priority");b.Word(root+4,ParseUnsigned(priority==n.attributes.end()?"1":priority->second));U32 count=static_cast<U32>(n.children.size());b.Word(root+8,count);if(count){auto items=static_cast<unsigned char*>(b.tracker.Push(reinterpret_cast<void**>(root+12),16,count));for(U32 i=0;i<count;++i)b.Rule(n.children[i],items+16*i);b.tracker.Pop();}}
    if(bigEndian)for(void* word:b.words)Swap(word);
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
