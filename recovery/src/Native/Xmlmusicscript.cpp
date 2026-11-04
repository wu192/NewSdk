#include "Xmlmusicscript.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include <cstring>
#include <cctype>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native {
namespace XmlmusicscriptDetail {
using N=XmlmusicscriptNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* key){const N* out=nullptr;for(const auto& c:n.children)if(c.name==key){out=&c;}return out;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void W(void* p,U x){std::memcpy(p,&x,4);}void Swap(void* p){U x;std::memcpy(&x,p,4);W(p,(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24));}
U RawHash(const std::string& input){
    const auto p0=reinterpret_cast<const unsigned char*>(input.c_str());auto p=p0;U size=static_cast<U>(std::strlen(input.c_str())),h=size;
    if(!size)return 0;
    auto shortWord=[](const unsigned char* b){return U(b[0])|(U(b[1])<<8);};
    auto signedByte=[](unsigned char c){return static_cast<U>(static_cast<std::int8_t>(c));};
    for(U n=size/4;n;--n,p+=4){h+=shortWord(p);h^=(shortWord(p+2)^(h<<5))<<11;h+=h>>11;}
    switch(size&3){case 3:h+=shortWord(p);h^=((signedByte(p[2])<<2)^h)<<16;h+=h>>11;break;case 2:h+=shortWord(p);h^=h<<11;h+=h>>17;break;case 1:h+=signedByte(*p);h^=h<<10;h+=h>>1;break;}
    h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
}

#include "XmlmusicscriptEnums.inc"
template<size_t C>int Enum(const std::string& s,const EnumNameValue(&e)[C],int fallback){for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0)return a.value;return fallback;}
enum Kind{Int,Time,Real,Bool,LowerHash,RawHashKind,Ref,PtrRef,Compare,Change,Screen,Relationship,Model};
struct Field{const char* name;unsigned offset;Kind kind;const char* fallback;};
struct Layout{const char* name;unsigned size;const Field* fields;size_t count;unsigned filterOffset;const char* child;};
#include "XmlmusicscriptFields.inc"
float Seconds(const std::string& text){const std::string s(text.c_str());auto at=s.find("ms");float scale=0.001f;if(at==std::string::npos){at=s.find('s');scale=1;}return static_cast<float>(static_cast<double>(ParseFloat(s.substr(0,at)))*static_cast<double>(scale));}
struct Builder{
 Relo::Tracker t;bool big;std::vector<void*> swaps;std::vector<ObjectFilter*> filters;Builder(bool b):big(b){}
 void Word(void* p,U x){W(p,x);swaps.push_back(p);}void Float(void* p,float x){std::memcpy(p,&x,4);swaps.push_back(p);}
 unsigned char* Push(void* p,unsigned offset,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(static_cast<unsigned char*>(p)+offset),size,count));}
 void FieldValue(const N& n,unsigned char* p,const Field& f){auto it=n.attributes.find(f.name);if(it==n.attributes.end()&&!f.fallback)return;std::string s=it==n.attributes.end()?f.fallback:it->second;auto q=p+f.offset;switch(f.kind){case Int:Word(q,ParseSigned(s));break;case Time:Float(q,Seconds(s));break;case Real:Float(q,ParseFloat(s));break;case Bool:*q=ParseBoolean(s);break;case LowerHash:Word(q,HashAssetId(s));break;case RawHashKind:Word(q,RawHash(s));break;case Ref:MarshalReference(t,q,s);break;case PtrRef:{auto target=Push(p,f.offset,4);MarshalReference(t,target,s);t.Pop();break;}case Compare:Word(q,Enum(s,EComparisonOperator,2));break;case Change:Word(q,Enum(s,EStateChangeType,0));break;case Screen:Word(q,Enum(s,EScreenStatus,1));break;case Relationship:MarshalEnumBitset(s,EObjectFilterRelationship,4,0,4,reinterpret_cast<U*>(q));swaps.push_back(q);break;case Model:MarshalModelCondition(s,reinterpret_cast<U*>(q));for(unsigned j=0;j<15;++j)swaps.push_back(q+4*j);break;}}
 void Filter(const N& n,unsigned char* p,unsigned off){ObjectFilterInput input;for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=std::string(a.second.c_str());for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(std::string(c.text.c_str()));}auto q=reinterpret_cast<ObjectFilter*>(Push(p,off,112));MarshalObjectFilter(input,q,t);filters.push_back(q);t.Pop();}
 void Refs(const N& n,unsigned char* p){Word(p+4,static_cast<U>(n.children.size()));if(n.children.empty())return;auto q=Push(p,8,4,static_cast<U>(n.children.size()));for(size_t i=0;i<n.children.size();++i){MarshalReference(t,q+4*i,n.children[i].text);}t.Pop();}
 void Timeouts(const N& n,unsigned char* p){Word(p+24,static_cast<U>(n.children.size()));if(n.children.empty())return;auto q=Push(p,28,8,static_cast<U>(n.children.size()));for(size_t i=0;i<n.children.size();++i){const auto& c=n.children[i];Word(q+8*i,ParseSigned(Get(c,"Weight","1000")));if(c.attributes.count("Duration"))Float(q+8*i+4,Seconds(Get(c,"Duration")));}t.Pop();}
 Relo::Chunk Finish(){if(big){for(auto p:swaps)Swap(p);for(auto p:filters)EndianObjectFilter(p);}return t.MakeRelocatable(big,0);}
};
}
unsigned int XmlmusicscriptTypeId(const std::string& name){
 if(name=="MusicScriptConditionNuggetBase")return 2292378780u;
 if(name=="MusicScriptConditionExpensiveNuggetBase")return 849624115u;
 if(name=="MusicScriptConditionNugget_LocalPlayerIsObserver")return 188286714u;
 if(name=="MusicScriptConditionNugget_UnitsFarFromBase")return 2620791304u;
 if(name=="MusicScriptConditionNugget_TimeFromStartOfLevel")return 1464425282u;
 if(name=="MusicScriptConditionNugget_TrackPlayedCount")return 57370491u;
 if(name=="MusicScriptConditionNugget_SpecificTrackTypePlaying")return 739280456u;
 if(name=="MusicScriptConditionNugget_AnyTrackPlaying")return 3402992833u;
 if(name=="MusicScriptConditionNugget_ObjectsOfTypeExist")return 4286308287u;
 if(name=="MusicScriptConditionNugget_EvaEventPlayedRecently")return 2743973749u;
 if(name=="MusicScriptConditionNugget_ObjectsNearEvaEvent")return 3386715757u;
 if(name=="MusicScriptConditionNugget_ScoredKillCount")return 3620764004u;
 if(name=="MusicScriptConditionNugget_Not")return 4007043648u;
 if(name=="MusicScriptConditionNugget_Or")return 2281773838u;
 if(name=="MusicScriptConditionNugget_And")return 1343682755u;
 if(name=="MusicScriptConditionNugget_PathMusicStateLastActive")return 2912435838u;
 if(name=="MusicScriptConditionNugget_PathMusicStateCurrentlyActive")return 3727134806u;
 if(name=="MusicScriptConditionNugget_EngagementCount")return 3476397162u;
 if(name=="MusicScriptConditionNugget_LocalPlayerKillRatio")return 2818967523u;
 if(name=="MusicScriptConditionNugget_HitBySuperWeapon")return 1634186921u;
 if(name=="MusicScriptTrack")return 1038067901u;
 return 0;
}
Relo::Chunk CompileXmlmusicscript(const XmlmusicscriptNode& n,bool big){using namespace XmlmusicscriptDetail;size_t i=0;while(i<sizeof(layouts)/sizeof(layouts[0])&&n.name!=layouts[i].name)++i;Check(i<sizeof(layouts)/sizeof(layouts[0]),"Unsupported music-script root");const auto& l=layouts[i];Builder b(big);unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),l.size,1);for(size_t j=0;j<l.count;++j)b.FieldValue(n,p,l.fields[j]);
 if(l.filterOffset){auto filter=Child(n,"Filter");if(filter)b.Filter(*filter,p,l.filterOffset);}
 else if(i==12){auto c=Child(n,"Condition");if(c){MarshalReference(b.t,p+4,c->text);}}
 else if(i==13||i==14)b.Refs(n,p);
 else if(i==20)b.Timeouts(n,p);
 return b.Finish();}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

