#include "Xmlradar.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "ObjectFilter.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace RadarDetail {
using Relo::U32;using Node=XmlradarNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
 Relo::Tracker t;std::vector<void*> words;std::vector<ObjectFilter*> filters;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Ref(const Node& n,const char* attr,unsigned char* p){auto a=n.attributes.find(attr);if(a!=n.attributes.end())MarshalReference(t,p,a->second);}
 void Coord(const Node* n,unsigned char* p,bool real){if(!n)return;const char* names[]={"x","y"};for(unsigned i=0;i<2;++i){if(real)Real(p+4*i,ParseFloat(Value(*n,names[i],"0.0")));else Word(p+4*i,ParseSigned(Value(*n,names[i],"0")));}}
 void Packed(const Node* n,unsigned char* p){if(!n)return;U32 r=ParseUnsigned(Value(*n,"R","0"))&255,g=ParseUnsigned(Value(*n,"G","0"))&255,b=ParseUnsigned(Value(*n,"B","0"))&255,a=ParseUnsigned(Value(*n,"A","255"))&255;Word(p,(a<<24)|(r<<16)|(g<<8)|b);}
 void Filter(const Node* n,unsigned char* p){if(!n)return;ObjectFilterInput f;for(const auto& a:n->attributes)if(a.first!="TypeId")f.attributes[a.first]=std::string(a.second.c_str());for(const auto& c:n->children){(c.name=="IncludeThing"?f.includeThings:f.excludeThings).push_back(std::string(c.value.c_str()));}auto q=reinterpret_cast<ObjectFilter*>(p);MarshalObjectFilter(f,q,t);filters.push_back(q);}
};
}
unsigned int XmlradarTypeId(const std::string& name){
 if(name=="RadarTerrainImage")return 1099769729u;
 if(name=="UIRadarObjectsLayer")return 4039592522u;
 return 0;
}
Relo::Chunk CompileXmlradar(const XmlradarNode& input,bool bigEndian){
 using namespace RadarDetail;static_assert(sizeof(void*)==4,"Radar requires x86.");Check(XmlradarTypeId(input.name)!=0,"Unsupported radar root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),input.name=="RadarTerrainImage"?40:24,1);
 if(input.name=="RadarTerrainImage"){auto border=Value(input,"MapBorder","Orange");const char* names[]={"Blue","Cyan","Green","Invalid","Magenta","Orange","Pink","Purple","Yellow"};const int values[]={2,3,1,-1,4,0,7,6,5};int value=2;for(unsigned i=0;i<9;++i)if(std::strcmp(border.c_str(),names[i])==0){value=values[i];break;}b.Word(root+4,value);b.Ref(input,"Texture",root+8);root[36]=ParseBoolean(Value(input,"Rotated","false"));b.Coord(Child(input,"ImageSize"),root+12,false);b.Coord(Child(input,"ImagePosition"),root+20,false);b.Coord(Child(input,"TextureSize"),root+28,false);}
 else{b.Word(root+4,ParseUnsigned(Value(input,"MinUnitDotSize","3")));b.Word(root+8,ParseUnsigned(Value(input,"MinStructureDotSize","5")));b.Packed(Child(input,"NeutralColor"),root+12);U32 count=0;for(const auto& c:input.children)if(c.name=="SpecialIcon")++count;b.Word(root+16,count);if(count){auto icons=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+20),124,count));unsigned i=0;for(const auto& c:input.children)if(c.name=="SpecialIcon"){auto p=icons+124*i++;b.Ref(c,"Image",p);b.Coord(Child(c,"ImageOffset"),p+4,true);b.Filter(Child(c,"ObjectFilter"),p+12);}b.t.Pop();}}
 if(bigEndian){for(auto p:b.words)Swap(p);for(auto f:b.filters)EndianObjectFilter(f);}return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
