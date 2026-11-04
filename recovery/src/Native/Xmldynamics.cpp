#include "Xmldynamics.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace DynamicsDetail {
using Relo::U32;using Node=XmldynamicsNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

std::string Value(const Node& n,const char* a,const char* fallback=""){auto i=n.attributes.find(a);return i==n.attributes.end()?fallback:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
}
unsigned int XmldynamicsTypeId(const std::string& name){return name=="DynamicsContactTagMapping"?1044178106u:0;}
Relo::Chunk CompileXmldynamics(const XmldynamicsNode& input,bool bigEndian){
 using namespace DynamicsDetail;static_assert(sizeof(void*)==4,"Dynamics requires x86.");Check(XmldynamicsTypeId(input.name)!=0,"Unsupported dynamics root.");Relo::Tracker t;unsigned char* root=nullptr;t.Push(reinterpret_cast<void**>(&root),12,1);U32 count=static_cast<U32>(input.children.size());std::memcpy(root+4,&count,4);if(count){auto items=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(root+8),16,count));const char* names[]={"DEBRIS","GROUND","NONE","PROP","TREE","VEHICLE","WALL","WATER"};const int values[]={7,1,0,4,6,3,5,2};for(unsigned i=0;i<count;++i){const auto& n=input.children[i];auto p=items+16*i;const char* attrs[]={"ContactA","ContactB"};for(unsigned j=0;j<2;++j){auto a=n.attributes.find(attrs[j]);if(a!=n.attributes.end()){U32 v=7;for(unsigned k=0;k<8;++k)if(std::strcmp(a->second.c_str(),names[k])==0){v=values[k];break;}std::memcpy(p+4*j,&v,4);}}float force=ParseFloat(Value(n,"MinForce","1.2"));std::memcpy(p+8,&force,4);auto fx=n.attributes.find("FX");if(fx!=n.attributes.end())MarshalReference(t,p+12,fx->second);if(bigEndian)for(unsigned off=0;off<12;off+=4)Swap(p+off);}t.Pop();}if(bigEndian)Swap(root+4);return t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
