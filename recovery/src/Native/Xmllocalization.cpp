#include "Xmllocalization.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmllocalizationDetail {
using N=XmllocalizationNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EFontUse[]={{"DefaultDisplayStringFont",2},{"DefaultWindowFont",1},{"DrawableCaptionFont",0},{"NativeDebugDisplay",4},{"TooltipFontName",3}};
const EnumNameValue EStyleName[]={{"ColumnFont",3},{"MinorTitleFont",1},{"NormalFont",2},{"TitleFont",0}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 template<size_t C>void Enum(void* p,const std::string& s,const EnumNameValue(&e)[C],int fallback){int value=fallback;for(const auto& a:e)if(std::strcmp(s.c_str(),a.name)==0){value=a.value;break;}Word(p,static_cast<U>(value));}
 void StringValue(unsigned char* p,const std::string& value){MarshalString(t,p,value);words.push_back(p);}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))StringValue(p+off,Get(n,attr));}
 void Leaf(const N* n,unsigned char* p){if(!n)return;StringValue(p,n->value);}
 void Font(const N* n,unsigned char* p){if(!n)return;String(*n,"Name",p,0);if(n->attributes.count("Size"))Word(p+8,ParseUnsigned(Get(*n,"Size")));p[12]=ParseBoolean(Get(*n,"Bold","false"));}
 void Color(const N* n,unsigned char* p){if(!n)return;U r=ParseUnsigned(Get(*n,"R","0"))&255,g=ParseUnsigned(Get(*n,"G","0"))&255,b=ParseUnsigned(Get(*n,"B","0"))&255,a=ParseUnsigned(Get(*n,"A","255"))&255;Word(p,(a<<24)|(r<<16)|(g<<8)|b);}
 void Separators(const N* n,unsigned char* p){if(!n)return;StringValue(p,Get(*n,"Thousand",","));StringValue(p+8,Get(*n,"Decimal","."));StringValue(p+16,Get(*n,"TimeMinuteToSecond",":"));}
 template<class F>void List(const N& n,const char* name,unsigned char* p,unsigned off,unsigned size,F f){U count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);U i=0;for(const auto& c:n.children)if(c.name==name)f(c,q+size*i++);t.Pop();}
 void Language(const N& n,unsigned char* p){Separators(Child(n,"Separators"),p+4);Leaf(Child(n,"UnicodeFontName"),p+28);List(n,"FontInfo",p,36,20,[&](const N& c,unsigned char* q){if(auto title=Child(c,"FontTitle")){Enum(q,title->value,EFontUse,2);}Font(Child(c,"Font"),q+4);});}
 void Credits(const N& n,unsigned char* p){if(auto c=Child(n,"Scroll")){if(c->attributes.count("Rate"))Word(p+4,ParseUnsigned(Get(*c,"Rate")));if(c->attributes.count("RateEveryFrame"))Word(p+8,ParseUnsigned(Get(*c,"RateEveryFrame")));if(c->attributes.count("ScrollDown"))p[12]=ParseBoolean(Get(*c,"ScrollDown"));}List(n,"Style",p,16,24,[&](const N& c,unsigned char* q){if(c.attributes.count("Name"))Enum(q,Get(c,"Name"),EStyleName,3);Color(Child(c,"Color"),q+4);Font(Child(c,"Font"),q+8);});List(n,"CreditItem",p,24,12,[&](const N& c,unsigned char* q){if(c.attributes.count("Style"))Enum(q,Get(c,"Style"),EStyleName,3);List(c,"Text",q,4,8,[&](const N& item,unsigned char* text){StringValue(text,item.value);});});}
};
}
unsigned XmllocalizationTypeId(const std::string& n){if(n=="Language")return 2915726794u;if(n=="HeaderTemplate")return 1218703046u;if(n=="FontDirectory")return 3569007303u;if(n=="TheVersion")return 4236434438u;if(n=="Credits")return 3978455785u;return 0;}
Relo::Chunk CompileXmllocalization(const XmllocalizationNode& input,bool bigEndian){using namespace XmllocalizationDetail;static_assert(sizeof(void*)==4,"Localization requires x86");U id=XmllocalizationTypeId(input.name);Check(id!=0,"Unknown localization root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==2915726794u?44:id==1218703046u?20:id==3978455785u?32:12,1);if(id==2915726794u)b.Language(input,p);else if(id==3978455785u)b.Credits(input,p);else if(id==1218703046u){b.Font(Child(input,"Font"),p+4);}else if(id==3569007303u){b.String(input,"Path",p,4);}else{if(auto n=Child(input,"Version")){if(n->attributes.count("Major"))b.Word(p+4,ParseSigned(Get(*n,"Major")));if(n->attributes.count("Minor"))b.Word(p+8,ParseSigned(Get(*n,"Minor")));}}if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
