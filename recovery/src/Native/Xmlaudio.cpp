#include "Xmlaudio.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <cctype>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native {
namespace XmlaudioDetail {
using N=XmlaudioNode;using U=Relo::U32;

void Check(bool ok,const char* msg){if(!ok)throw std::invalid_argument(msg);}

const N* Child(const N& n,const char* key){const N* p=nullptr;for(const auto& c:n.children)if(c.name==key){p=&c;}return p;}
std::string Get(const N& n,const char* key,const char* fallback=""){auto i=n.attributes.find(key);return i==n.attributes.end()?fallback:i->second;}
void W(void* p,U x){std::memcpy(p,&x,4);}void Swap(void* p){U x;std::memcpy(&x,p,4);W(p,(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24));}
struct EnumPair{const char* name;int value;};
#include "XmlaudioEnums.inc"
template<size_t C>int Value(const std::string& s,const EnumPair(&e)[C],int fallback){for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0)return a.value;return fallback;}
enum Kind{Int,UInt,Real,Percent,Time,Bool,Priority,Lod};
struct Field{const char* name;unsigned offset;Kind kind;const char* fallback;};
#include "XmlaudioFields.inc"
float Seconds(const std::string& text){std::string s(text.c_str());auto ms=s.find("ms");if(ms!=std::string::npos)return static_cast<float>(static_cast<double>(ParseFloat(s.substr(0,ms)))*0.0010000000474974513);auto sec=s.find('s');return ParseFloat(s.substr(0,sec));}
struct Builder{
 Relo::Tracker t;bool big;std::vector<void*> swaps;
 Builder(bool b):big(b){}
 void Word(void* p,U x){W(p,x);swaps.push_back(p);}void Float(void* p,float x){std::memcpy(p,&x,4);swaps.push_back(p);}
 unsigned char* Push(void* p,unsigned offset,U size,U count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(static_cast<unsigned char*>(p)+offset),size,count));}
 void String(void* p,const std::string& s){MarshalString(t,p,s);swaps.push_back(p);}
 template<size_t C>void Fields(const N& n,unsigned char* p,const Field(&f)[C],const char* children="",bool root=false){for(const auto& a:f){auto it=n.attributes.find(a.name);if(it==n.attributes.end()&&!a.fallback)continue;std::string s=it==n.attributes.end()?a.fallback:it->second;auto q=p+a.offset;switch(a.kind){case Int:Word(q,static_cast<U>(ParseSigned(s)));break;case UInt:Word(q,ParseUnsigned(s));break;case Real:Float(q,ParseFloat(s));break;case Percent:Float(q,ParsePercentage(s));break;case Time:Float(q,Seconds(s));break;case Bool:*q=ParseBoolean(s);break;case Priority:Word(q,Value(s,EAudioPriority,5));break;case Lod:Word(q,Value(s,EAudioLODType,1));break;}}}
 void Color(const N* n,unsigned char* p,bool rgba){if(!n)return;const char* names=rgba?"RGBA":"rgb";for(unsigned i=0;i<(rgba?4u:3u);++i){std::string key(1,names[i]);auto it=n->attributes.find(key);if(it!=n->attributes.end())Float(p+4*i,ParseFloat(it->second));}}
 void Fade(const N* n,unsigned char* parent,unsigned offset){if(!n)return;auto p=Push(parent,offset,28);if(n->attributes.count("FrameCount"))Word(p,ParseSigned(Get(*n,"FrameCount")));Color(Child(*n,"StartColor"),p+4,false);Color(Child(*n,"EndColor"),p+16,false);t.Pop();}
 void Text(const N& n,unsigned char* p){auto it=n.attributes.find("TextId");if(it!=n.attributes.end()){const std::string s(it->second.c_str());std::vector<std::string> values;size_t i=0;while(i<s.size()){while(i<s.size()&&std::isspace(static_cast<unsigned char>(s[i])))++i;size_t first=i;while(i<s.size()&&!std::isspace(static_cast<unsigned char>(s[i])))++i;if(i>first)values.push_back(s.substr(first,i-first));}if(!values.empty()){Word(p,static_cast<U>(values.size()));auto q=Push(p,4,8,static_cast<U>(values.size()));for(size_t j=0;j<values.size();++j)String(q+8*j,values[j]);t.Pop();}}
 Word(p+8,Value(Get(n,"Justification","LEFT"),ETextJustificationType,2));Float(p+12,ParseFloat(Get(n,"XOffset","0.0")));Float(p+16,ParseFloat(Get(n,"YOffset","0.0")));if(n.attributes.count("Font"))String(p+20,Get(n,"Font"));if(n.attributes.count("PointSize"))Word(p+28,ParseSigned(Get(n,"PointSize")));Color(Child(n,"TextColor"),p+32,false);}
 void Event(const N& n,unsigned char* owner,unsigned off){U type=ParseUnsigned(Get(n,"TypeId","0"));bool movie=type==3074724042u,title=type==866596483u;auto p=Push(owner,off,movie?16:title?24:12);if(movie&&n.attributes.count("MovieAsset"))MarshalReference(t,p+12,Get(n,"MovieAsset"));if(title){if(n.attributes.count("DisplayTime"))Float(p+12,Seconds(Get(n,"DisplayTime")));std::vector<const N*> items;for(const auto& c:n.children)if(c.name=="TextInstance")items.push_back(&c);Word(p+16,static_cast<U>(items.size()));if(!items.empty()){auto q=Push(p,20,44,static_cast<U>(items.size()));for(size_t j=0;j<items.size();++j)Text(*items[j],q+44*j);t.Pop();}}Fade(Child(n,"FadeIn"),p,4);Fade(Child(n,"FadeOut"),p,8);Word(p,movie||title?type:0);t.Pop();}
 void Settings(const N& n,unsigned char* p){Fields(n,p,settings,"AutomaticSubtitleWindowColor|AutomaticSubtitleTextColor|VolumeSliderMultiplierForMovie|TacticalMicSettings|LivingWorldMicSettings|ShellMicSettings|VolumeCompressorSettings",true);Color(Child(n,"AutomaticSubtitleWindowColor"),p+176,true);Color(Child(n,"AutomaticSubtitleTextColor"),p+192,true);std::vector<const N*> values;for(const auto& c:n.children)if(c.name=="VolumeSliderMultiplierForMovie")values.push_back(&c);Word(p+208,static_cast<U>(values.size()));if(!values.empty()){auto q=Push(p,212,8,static_cast<U>(values.size()));for(size_t i=0;i<values.size();++i){const auto& c=*values[i];if(c.attributes.count("Slider"))Word(q+8*i,Value(Get(c,"Slider"),EAudioVolumeSlider,3));if(c.attributes.count("Multiplier"))Float(q+8*i+4,ParseFloat(Get(c,"Multiplier")));}t.Pop();}const char* names[]={"TacticalMicSettings","LivingWorldMicSettings","ShellMicSettings"};for(unsigned i=0;i<3;++i){auto c=Child(n,names[i]);if(c)Fields(*c,p+216+48*i,mic,"",true);}auto c=Child(n,"VolumeCompressorSettings");if(c)Fields(*c,p+360,compressor);}
 Relo::Chunk Finish(){if(big)for(auto p:swaps)Swap(p);return t.MakeRelocatable(big,0);}
 void Leaf(const N& n){}
 void Bootup(const N& n,unsigned char* p){if(n.attributes.count("LoadingTextTexture"))MarshalReference(t,p+4,Get(n,"LoadingTextTexture"));U count=static_cast<U>(n.children.size());Word(p+8,count);if(!count)return;auto q=Push(p,12,28,count);for(U i=0;i<count;++i){const auto& c=n.children[i];auto item=q+28*i;if(c.attributes.count("Type"))Word(item,Value(Get(c,"Type"),EBootupDisplayItemType,1));if(c.attributes.count("Duration"))Word(item+4,ParseSigned(Get(c,"Duration")));if(c.attributes.count("Priority"))Word(item+8,ParseSigned(Get(c,"Priority")));if(c.attributes.count("ShowOnReboot"))item[24]=ParseBoolean(Get(c,"ShowOnReboot"));std::vector<const N*> textures;for(const auto& child:c.children)if(child.name=="RandomTexture")textures.push_back(&child);Word(item+12,static_cast<U>(textures.size()));if(!textures.empty()){auto refs=Push(item,16,4,static_cast<U>(textures.size()));for(size_t j=0;j<textures.size();++j){Leaf(*textures[j]);MarshalReference(t,refs+4*j,textures[j]->text);}t.Pop();}auto movie=Child(c,"Movie");if(movie){Leaf(*movie);auto str=Push(item,20,8);String(str,movie->text);t.Pop();}}t.Pop();}
};
}
unsigned int XmlaudioTypeId(const std::string& n){if(n=="AudioEventLimitGroup")return 3906227957u;if(n=="AudioLOD")return 299416263u;if(n=="AudioSettings")return 1443425905u;if(n=="Video")return 587347702u;if(n=="VideoEventList")return 1447728787u;if(n=="MicrophoneSettings")return 1826140078u;if(n=="BootupDisplaySequence")return 3370391592u;return 0;}
Relo::Chunk CompileXmlaudio(const XmlaudioNode& n,bool big){using namespace XmlaudioDetail;Builder b(big);unsigned size=n.name=="AudioEventLimitGroup"?8:n.name=="AudioLOD"?44:n.name=="AudioSettings"?384:n.name=="Video"?28:n.name=="VideoEventList"?12:n.name=="MicrophoneSettings"?48:n.name=="BootupDisplaySequence"?16:0;Check(size!=0,"Unsupported audio/video root");unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),size,1);
 if(n.name=="AudioEventLimitGroup"){if(n.attributes.count("Limit"))b.Word(p+4,ParseSigned(Get(n,"Limit")));}
 else if(n.name=="BootupDisplaySequence")b.Bootup(n,p);
 else if(n.name=="MicrophoneSettings")b.Fields(n,p,mic,"",true);
 else if(n.name=="AudioSettings")b.Settings(n,p);
 else if(n.name=="AudioLOD"){b.Fields(n,p,lod,"Processor",true);U count=static_cast<U>(n.children.size());b.Word(p+32,count);if(count){auto q=b.Push(p,36,8,count);for(U i=0;i<count;++i){const auto& c=n.children[i];if(c.attributes.count("Type"))b.Word(q+8*i,Value(Get(c,"Type"),EProcessorType,3));if(c.attributes.count("MinMHz"))b.Word(q+8*i+4,ParseSigned(Get(c,"MinMHz")));}b.t.Pop();}}
 else if(n.name=="Video"){if(n.attributes.count("Filename"))b.String(p+4,Get(n,"Filename"));b.String(p+12,Get(n,"Comment","No Comment"));b.Float(p+20,ParsePercentage(Get(n,"Volume","80")));b.Word(p+24,Value(Get(n,"SoundOverride","DUCK"),EVideoSoundOverrideEventType,0));}
 else{auto c=Child(n,"EventList");}
 if(n.name=="VideoEventList"){auto c=Child(n,"EventList");if(c){/* all element names are dispatched solely by their numeric TypeId */U count=static_cast<U>(c->children.size());b.Word(p+4,count);if(count){auto q=b.Push(p,8,4,count);for(U i=0;i<count;++i)b.Event(c->children[i],q,4*i);b.t.Pop();}}}
 return b.Finish();}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
