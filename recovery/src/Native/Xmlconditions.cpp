#include "Xmlconditions.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlconditionsDetail {
using N=XmlconditionsNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
#include "XmlconditionsEnums.inc"
struct Builder {
 Relo::Tracker t;std::vector<void*> words;std::vector<ObjectFilter*> filters;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}void Float(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 template<size_t C>void Bits(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback,unsigned count){MarshalEnumBitset(s,e,C,fallback,count,reinterpret_cast<U*>(p));for(unsigned i=0;i<(count+31)/32;++i)words.push_back(static_cast<unsigned char*>(p)+4*i);}
 void Filter(const N& n,unsigned char* p){ObjectFilterInput input;for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=std::string(a.second.c_str());for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(std::string(c.value.c_str()));}auto q=reinterpret_cast<ObjectFilter*>(Push(p,4,112));MarshalObjectFilter(input,q,t);filters.push_back(q);t.Pop();}
 void Base(const N* n,unsigned char* p){if(!n)return;Word(p,ParseSigned(Get(*n,"Count","1")));p[8]=ParseBoolean(Get(*n,"Unique","false"));if(auto f=Child(*n,"ObjectFilter"))Filter(*f,p);}
 void Status(const N& n,const char* key,unsigned char* p,unsigned off){if(!n.attributes.count(key))return;auto q=Push(p,off,28);MarshalObjectStatus(Get(n,key),reinterpret_cast<U*>(q));for(unsigned i=0;i<7;++i)words.push_back(q+4*i);t.Pop();}
 void Strings(const std::string& s,unsigned char* p){std::vector<std::string> tokens;auto space=[](unsigned char c){return c==' '||(c>=9&&c<=13);};size_t at=0;while(at<s.size()&&s[at]){while(at<s.size()&&space(s[at]))++at;size_t first=at;while(at<s.size()&&s[at]&&!space(s[at]))++at;if(at>first)tokens.push_back(s.substr(first,at-first));}Word(p,static_cast<U>(tokens.size()));if(tokens.empty())return;auto q=Push(p,4,8,static_cast<U>(tokens.size()));for(size_t i=0;i<tokens.size();++i){MarshalString(t,q+8*i,tokens[i]);words.push_back(q+8*i);}t.Pop();}
 void Die(const N& n,unsigned char* p){if(n.attributes.count("VeterancyLevels"))Strings(Get(n,"VeterancyLevels"),p);Status(n,"ExemptStatus",p,8);Status(n,"RequiredStatus",p,12);Float(p+16,ParseFloat(Get(n,"DamageAmountRequired","-1")));Float(p+20,InitializeAngle(Get(n,"MinKillerAngle","1d").c_str()));Float(p+24,InitializeAngle(Get(n,"MaxKillerAngle","-1d").c_str()));if(n.attributes.count("DeathTypes"))Bits(p+28,Get(n,"DeathTypes"),EDeathType,33,34);if(n.attributes.count("DeathTypesForbidden"))Bits(p+36,Get(n,"DeathTypesForbidden"),EDeathType,33,34);}
 void Object(const N* n,unsigned char* p,bool root=false){if(!n)return;if(auto d=Child(*n,"DieMuxData")){auto q=Push(p,12,44);Die(*d,q);t.Pop();}Base(n,p);}
 void Upgrade(const N& n,unsigned char* p,bool root=false){U count=0;for(const auto& c:n.children)if(c.name=="UpgradeRequired")++count;Word(p+12,count);if(count){auto q=Push(p,16,4,count);unsigned i=0;for(const auto& c:n.children)if(c.name=="UpgradeRequired"){Word(q+4*i++,HashAssetId(c.value));}t.Pop();}Base(&n,p);}
 void Gameplay(const N& n,unsigned char* p){Bits(p+4,Get(n,"GameplayConditionBitFlags"),EGameplayConditionType,1,3);if(n.attributes.count("TimeReq")){auto q=Push(p,8,4);Float(q,InitializeTime(Get(n,"TimeReq").c_str()));t.Pop();}Bits(p+12,Get(n,"AllowedGameTypeBitFlags","SKIRMISH RANKED_ONLINE"),EGameplayModeType,0,4);Bits(p+16,Get(n,"GameDifficultyBitFlags","EASY NORMAL HARD BRUTAL"),EGameDifficulty,3,4);Object(Child(n,"PrimaryObject"),p+20);if(auto c=Child(n,"RequiredObjectCondition")){auto q=Push(p,36,16);Object(c,q);t.Pop();}if(auto c=Child(n,"RequiredUpgradeCondition")){auto q=Push(p,40,20);Upgrade(*c,q);t.Pop();}}
};
}
unsigned XmlconditionsTypeId(const std::string& n){if(n=="BaseConditionData")return 3899256902u;if(n=="ObjectConditionData")return 519519951u;if(n=="UpgradeConditionData")return 76901015u;if(n=="GameplayConditionData")return 2309012900u;return 0;}
Relo::Chunk CompileXmlconditions(const XmlconditionsNode& input,bool bigEndian){using namespace XmlconditionsDetail;static_assert(sizeof(void*)==4,"Conditions require x86");U id=XmlconditionsTypeId(input.name);Check(id!=0,"Unknown conditions root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==3899256902u?12:id==519519951u?16:id==76901015u?20:44,1);if(id==3899256902u){b.Base(&input,p);}else if(id==519519951u)b.Object(&input,p,true);else if(id==76901015u)b.Upgrade(input,p,true);else b.Gameplay(input,p);if(bigEndian){for(auto q:b.words)Swap(q);for(auto q:b.filters)EndianObjectFilter(q);}return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
