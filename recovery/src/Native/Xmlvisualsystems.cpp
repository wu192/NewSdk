#include "Xmlvisualsystems.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace VisualSystemsDetail {
using Relo::U32;using Node=XmlvisualsystemsNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
enum Kind {Real,Time,Signed,Bool,Reference,String,Weak};
struct Field {const char* name;unsigned offset;Kind kind;const char* fallback;};
const Field breeze[]={{"Direction",8,Real,"1.0472"},{"Intensity",12,Real,"0.027488125"},{"Lean",16,Real,"0.0157075"},{"Randomness",20,Real,"0.2"},{"BreezePeriod",24,Time,"10s"},{"LoadAfter",4,Reference,nullptr}};
const Field model[]={{"ForceModelsToFollowTimeOfDay",20,Bool,nullptr},{"ForceModelsToFollowWeather",21,Bool,nullptr},{"AnimationSharingCap",8,Signed,nullptr},{"AnimationSharingFrameTolerance",12,Real,nullptr},{"AnimationSharingDrasticThreshold",16,Real,nullptr},{"LoadAfter",4,Reference,nullptr}};
const Field particle[]={{"ParticleScale",4,Real,nullptr},{"MaxParticleCount",8,Signed,"2500"}};
const Field settings[]={{"ParticlePrefix",0,String,nullptr},{"ParticleSystemId",8,Weak,nullptr},{"ParticleMax",12,Signed,nullptr}};
struct Builder {
    Relo::Tracker tracker;std::vector<void*> words;
    void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
    void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
    void Fields(const Node& n,unsigned char* p,const Field* fields,unsigned count){for(unsigned i=0;i<count;++i){const auto& f=fields[i];auto a=n.attributes.find(f.name);if(a==n.attributes.end()&&!f.fallback)continue;std::string s=a==n.attributes.end()?f.fallback:a->second;auto q=p+f.offset;
        switch(f.kind){case Real:Float(q,ParseFloat(s));break;case Time:Float(q,InitializeTime(s.c_str()));break;case Signed:Word(q,ParseSigned(s));break;case Bool:*q=ParseBoolean(s);break;case Reference:MarshalReference(tracker,q,s);break;case String:MarshalString(tracker,q,s);words.push_back(q);break;case Weak:Word(q,HashAssetId(s));break;}
    }}
};
}
unsigned int XmlvisualsystemsTypeId(const std::string& name){if(name=="BreezeInfo")return 1626415288u;if(name=="ModelSettings")return 1968811301u;if(name=="AutoParticleSystemSettings")return 3278684478u;return 0;}
Relo::Chunk CompileXmlvisualsystems(const XmlvisualsystemsNode& n,bool bigEndian){using namespace VisualSystemsDetail;static_assert(sizeof(void*)==4,"Visual systems require x86.");
    unsigned id=XmlvisualsystemsTypeId(n.name);Check(id!=0,"Unsupported visual systems root.");Builder b;unsigned char* root=nullptr;b.tracker.Push(reinterpret_cast<void**>(&root),id==1626415288u?28:id==1968811301u?24:124,1);
    if(id==1626415288u){b.Fields(n,root,breeze,6);}
    else if(id==1968811301u){b.Fields(n,root,model,6);}
    else{b.Fields(n,root,particle,2);
        const char* names[]={"FireSmall","FireMedium","FireLarge","SmokeSmall","SmokeMedium","SmokeLarge","Aflame"};for(unsigned i=0;i<7;++i)if(const Node* c=Child(n,names[i])){b.Fields(*c,root+12+16*i,settings,3);}
    }
    if(bigEndian)for(void* word:b.words)Swap(word);
    return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
