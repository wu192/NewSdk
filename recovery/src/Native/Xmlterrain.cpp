#include "Xmlterrain.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace TerrainDetail {
using Relo::U32;using Node=XmlterrainNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmlterrainTypeId(const std::string& name){
 if(name=="TerrainAssetCollection")return 582251659u;
 if(name=="TerrainTextureAtlasRuntime")return 3878948534u;
 return 0;
}
Relo::Chunk CompileXmlterrain(const XmlterrainNode& input,bool bigEndian){using namespace TerrainDetail;static_assert(sizeof(void*)==4,"Terrain requires x86.");Check(XmlterrainTypeId(input.name)!=0,"Unsupported terrain root.");bool atlas=input.name=="TerrainTextureAtlasRuntime";Relo::Tracker t;unsigned char* root=nullptr;t.Push(reinterpret_cast<void**>(&root),atlas?28:12,1);U32 count=static_cast<U32>(input.children.size());std::memcpy(root+4,&count,4);if(count){unsigned stride=atlas?12:36;auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(root+8),stride,count));for(unsigned i=0;i<count;++i){const auto& n=input.children[i];auto p=list+stride*i;if(atlas){U32 id=ParseUnsigned(Value(n,"TextureID"));std::memcpy(p,&id,4);if(bigEndian)Swap(p);const char* coords[]={"UpperLeftX","UpperLeftY","BottomRightX","BottomRightY"};for(unsigned j=0;j<4;++j){unsigned short v=static_cast<unsigned short>(ParseUnsigned(Value(n,coords[j])));if(bigEndian)v=static_cast<unsigned short>((v<<8)|(v>>8));std::memcpy(p+4+2*j,&v,2);}}
 else{const char* strings[]={"id","Texture","BumpTexture","WBFolder"};unsigned offsets[]={0,8,16,28};for(unsigned j=0;j<4;++j){auto a=n.attributes.find(strings[j]);if(a!=n.attributes.end()){MarshalString(t,p+offsets[j],a->second);if(bigEndian)Swap(p+offsets[j]);}}auto cls=n.attributes.find("Class");if(cls!=n.attributes.end()){U32 value=3;const char* names[]={"Cliff","Dirt","Grass","Misc","Mud","Road","Rock","Sand","Shrub","Snow","UNSPECIFIED"};const int values[]={3,2,4,1,7,6,5,8,9,10,0};for(unsigned j=0;j<11;++j)if(std::strcmp(cls->second.c_str(),names[j])==0){value=values[j];break;}std::memcpy(p+24,&value,4);if(bigEndian)Swap(p+24);}}
 }t.Pop();}if(bigEndian)Swap(root+4);return t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
