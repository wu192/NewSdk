#include "Xmlconnections.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace ConnectionsDetail {
using Relo::U32;using Node=XmlconnectionsNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void String(const Node& n,const char* attr,unsigned char* p,const char* fallback=nullptr){auto a=n.attributes.find(attr);if(a!=n.attributes.end()||fallback){MarshalString(t,p,a==n.attributes.end()?fallback:a->second);words.push_back(p);}}
 void Constant(const Node& n,unsigned char* slot){U32 id=ParseUnsigned(Value(n,"TypeId","0"));bool scalar=id==2750958653u||id==2300057986u||id==2777716390u;bool real=id==3741900581u;unsigned size=real?20:scalar?16:12;auto p=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(slot),size,1));
 if(real){U32 count=static_cast<U32>(n.children.size());Word(p+12,count);if(count){auto values=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+16),4,count));for(unsigned i=0;i<count;++i){Real(values+4*i,ParseFloat(n.children[i].value));}t.Pop();}}
 else if(scalar){auto value=Child(n,"Value");if(value){if(id==2750958653u)p[12]=ParseBoolean(value->value);else if(id==2300057986u)Word(p+12,ParseSigned(value->value));else MarshalReference(t,p+12,value->value);}}
 String(n,"Name",p+4);Word(p,scalar||real?id:0);t.Pop();
 }
 void Material(const Node& n,unsigned char* p){String(n,"ShaderName",p);String(n,"TechniqueName",p+8,"");p[24]=static_cast<unsigned char>(ParseUnsigned(Value(n,"TechniqueIndex","0")));auto constants=Child(n,"Constants");if(constants){U32 count=static_cast<U32>(constants->children.size());Word(p+16,count);if(count){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+20),4,count));for(unsigned i=0;i<count;++i)Constant(constants->children[i],list+4*i);t.Pop();}}}
};
}
unsigned int XmlconnectionsTypeId(const std::string& name){return name=="ConnectionLineManager"?105587076u:0;}
Relo::Chunk CompileXmlconnections(const XmlconnectionsNode& input,bool bigEndian){
 using namespace ConnectionsDetail;static_assert(sizeof(void*)==4,"Connection shaders require x86.");Check(XmlconnectionsTypeId(input.name)!=0,"Unsupported connection shader root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),32,1);auto shader=Child(input,"FXShader");if(shader)b.Material(*shader,root+4);if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
