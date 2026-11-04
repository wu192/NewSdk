#include "Xmlsinglesound.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <cctype>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native {
namespace XmlsinglesoundDetail {
using N=XmlsinglesoundNode;using U=Relo::U32;
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

const N* Child(const N& n,const char* key){const N* found=nullptr;for(const auto& c:n.children)if(c.name==key){found=&c;}return found;}
std::string Get(const N& n,const char* key,const char* fallback=""){auto i=n.attributes.find(key);return i==n.attributes.end()?fallback:i->second;}
void W(void* p,U x){std::memcpy(p,&x,4);}void Swap(void* p){U x;std::memcpy(&x,p,4);W(p,(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24));}
#include "XmlsinglesoundEnums.inc"
template<size_t C>int Enum(const std::string& s,const EnumNameValue(&e)[C],int fallback){for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0)return a.value;return fallback;}
enum Kind{Int,Real,Percent,Priority,TypeBits,ControlBits};
struct Field{const char* name;unsigned offset;Kind kind;const char* fallback;};
#include "XmlsinglesoundFields.inc"
float Seconds(const std::string& text){const std::string s(text.c_str());auto pos=s.find("ms");float scale=0.001f;if(pos==std::string::npos){pos=s.find('s');scale=1.0f;}return static_cast<float>(static_cast<double>(ParseFloat(s.substr(0,pos)))*static_cast<double>(scale));}
struct Builder{
 Relo::Tracker data;bool big;std::vector<void*> swaps;
 Builder(bool b):big(b){}
 void Word(void* p,U x){W(p,x);swaps.push_back(p);}void Float(void* p,float x){std::memcpy(p,&x,4);swaps.push_back(p);}
 unsigned char* Push(void* parent,unsigned offset,unsigned size,unsigned count=1){return static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(static_cast<unsigned char*>(parent)+offset),size,count));}
 template<size_t C>void Bits(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback,unsigned count){MarshalEnumBitset(s,e,C,fallback,count,static_cast<U*>(p));swaps.push_back(p);}
 void Range(const N* n,unsigned char* parent,unsigned offset,int kind){if(!n)return;auto p=Push(parent,offset,8);const char* keys[]={"Low","High"};for(unsigned i=0;i<2;++i)if(n->attributes.count(keys[i])){auto s=Get(*n,keys[i]);if(kind==1)Word(p+4*i,ParseSigned(s));else Float(p+4*i,kind==2?Seconds(s):ParseFloat(s));}data.Pop();}
 void ReferenceList(const N& n,const char* name,unsigned char* p,unsigned offset){std::vector<const N*> items;for(const auto& c:n.children)if(c.name==name)items.push_back(&c);Word(p+offset,static_cast<U>(items.size()));if(items.empty())return;auto q=Push(p,offset+4,4,static_cast<U>(items.size()));for(size_t i=0;i<items.size();++i){MarshalReference(data,q+4*i,items[i]->text);}data.Pop();}
 void Single(const N& n,unsigned char* p,const std::string& extra){std::string attrs="id|SubmixSlider",children="PitchShift|PerFilePitchShift|Delay|InitialDelay|VolumeSliderMultiplier|MinRangeShift|MaxRangeShift|LimitGroup|NonInterruptibleTime";for(const auto& f:fields)attrs+="|"+std::string(f.name);if(!extra.empty())children+="|"+extra;
  for(const auto& f:fields){const auto s=Get(n,f.name,f.fallback);auto q=p+f.offset;switch(f.kind){case Int:Word(q,ParseSigned(s));break;case Real:Float(q,ParseFloat(s));break;case Percent:Float(q,ParsePercentage(s));break;case Priority:Word(q,Enum(s,EAudioPriority,5));break;case TypeBits:Bits(q,s,EAudioTypeFlag,5,8);break;case ControlBits:Bits(q,s,EAudioControlFlag,7,9);break;}}
  if(n.attributes.count("SubmixSlider")){auto q=Push(p,80,4);Word(q,Enum(Get(n,"SubmixSlider"),EAudioVolumeSlider,3));data.Pop();}
  Range(Child(n,"PitchShift"),p,84,0);Range(Child(n,"PerFilePitchShift"),p,88,0);Range(Child(n,"Delay"),p,92,1);Range(Child(n,"InitialDelay"),p,96,1);
  std::vector<const N*> items;for(const auto& c:n.children)if(c.name=="VolumeSliderMultiplier")items.push_back(&c);Word(p+100,static_cast<U>(items.size()));if(!items.empty()){auto q=Push(p,104,8,static_cast<U>(items.size()));for(size_t i=0;i<items.size();++i){const auto& c=*items[i];if(c.attributes.count("Slider"))Word(q+8*i,Enum(Get(c,"Slider"),EAudioVolumeSlider,3));if(c.attributes.count("Multiplier"))Float(q+8*i+4,ParseFloat(Get(c,"Multiplier")));}data.Pop();}
  Range(Child(n,"MinRangeShift"),p,108,0);Range(Child(n,"MaxRangeShift"),p,112,0);ReferenceList(n,"LimitGroup",p,116);Range(Child(n,"NonInterruptibleTime"),p,124,2);
 }
 void Weighted(const N& n,const char* name,unsigned char* parent,unsigned offset){std::vector<const N*> items;for(const auto& c:n.children)if(c.name==name)items.push_back(&c);Word(parent+offset,static_cast<U>(items.size()));if(items.empty())return;auto p=Push(parent,offset+4,12,static_cast<U>(items.size()));for(size_t i=0;i<items.size();++i){const auto& c=*items[i];MarshalReference(data,p+12*i,c.text);Word(p+12*i+4,ParseUnsigned(Get(c,"Weight","1000")));Float(p+12*i+8,ParsePercentage(Get(c,"Volume","100")));}data.Pop();}
 void Multi(const N& n,unsigned char* p){Bits(p+4,Get(n,"Control"),EMultisoundControlFlag,0,2);Word(p+8,static_cast<U>(n.children.size()));if(n.children.empty())return;auto q=Push(p,12,28,static_cast<U>(n.children.size()));for(size_t i=0;i<n.children.size();++i){const auto& c=n.children[i];auto item=q+28*i;MarshalReference(data,item,c.text);Word(item+4,ParseUnsigned(Get(c,"Weight","1000")));const char* keys[]={"PitchShiftLow","PitchShiftHigh","Volume","PlayPercent","VolumeShift"};for(unsigned j=0;j<5;++j)if(c.attributes.count(keys[j])){auto scalar=Push(item,8+4*j,4);Float(scalar,j<2?ParseFloat(Get(c,keys[j])):ParsePercentage(Get(c,keys[j])));data.Pop();}}data.Pop();}
 Relo::Chunk Finish(){if(big)for(auto p:swaps)Swap(p);return data.MakeRelocatable(big,0);}
};
}
unsigned int XmlsinglesoundTypeId(const std::string& name){
 if(name=="BaseAudioEventInfo")return 1079232276u;
 if(name=="BaseSingleSound")return 3228425025u;
 if(name=="AudioEvent")return 2219670431u;
 if(name=="MusicTrack")return 1883691512u;
 if(name=="MP3MusicTrack")return 1013434420u;
 if(name=="DialogEvent")return 3558134211u;
 if(name=="MP3DialogEvent")return 1479988269u;
 if(name=="AmbientStream")return 3928762264u;
 if(name=="MP3AmbientStream")return 1813598662u;
 if(name=="Multisound")return 2745675575u;
 return 0;
}
Relo::Chunk CompileXmlsinglesound(const XmlsinglesoundNode& n,bool big){using namespace XmlsinglesoundDetail;Check(XmlsinglesoundTypeId(n.name)!=0,"Unsupported single-sound root");Builder b(big);unsigned size=n.name=="BaseAudioEventInfo"?4:n.name=="BaseSingleSound"?128:n.name=="AudioEvent"?152:n.name=="Multisound"?16:132;unsigned char* p=nullptr;b.data.Push(reinterpret_cast<void**>(&p),size,1);
 if(n.name=="BaseAudioEventInfo");
 else if(n.name=="Multisound")b.Multi(n,p);
 else if(n.name=="BaseSingleSound")b.Single(n,p,"");
 else if(n.name=="AudioEvent"){b.Weighted(n,"Attack",p,128);b.Weighted(n,"Sound",p,136);b.Weighted(n,"Decay",p,144);b.Single(n,p,"Attack|Sound|Decay");}
 else{auto filename=Child(n,"Filename");if(filename){auto q=b.Push(p,128,4);MarshalReference(b.data,q,filename->text);b.data.Pop();}b.Single(n,p,"Filename");}
 return b.Finish();}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
