#include "Xmlgamemap.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace GameMapDetail {
using Relo::U32;using Node=XmlgamemapNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const Node* Child(const Node& n,const char* name){const Node* r=nullptr;for(const auto& c:n.children)if(c.name==name){r=&c;}return r;}
std::string Value(const Node& n,const char* a,const char* d=""){auto i=n.attributes.find(a);return i==n.attributes.end()?d:i->second;}
std::vector<const Node*> Children(const Node& n,const char* name){std::vector<const Node*> out;for(const auto& c:n.children)if(c.name==name)out.push_back(&c);return out;}
bool Space(unsigned char c){return c==' '||(c>=9&&c<=13);}
std::vector<std::string> Tokens(const std::string& s){std::vector<std::string> out;const char* p=s.c_str();while(*p){while(Space(*p))++p;if(!*p)break;const char* b=p;while(*p&&!Space(*p))++p;out.emplace_back(b,p-b);}return out;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
const EnumNameValue Flags[]={{"DONT_RENDER",7},{"DRAWS_IN_MIRROR",0},{"OBSOLETE_BRIDGE_POINT1",3},{"OBSOLETE_BRIDGE_POINT2",4},{"ROAD_CORNER_TIGHT",5},{"ROAD_JOIN",6},{"ROAD_POINT1",1},{"ROAD_POINT2",2}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void String(const Node& n,const char* a,unsigned char* p){auto i=n.attributes.find(a);if(i!=n.attributes.end())Text(i->second,p);}
 void Text(const std::string& s,unsigned char* p){MarshalString(t,p,s);words.push_back(p);}
 void Ref(const Node& n,const char* a,unsigned char* p){auto i=n.attributes.find(a);if(i!=n.attributes.end())MarshalReference(t,p,i->second);}
 template<class F> void List(const Node& n,const char* name,unsigned char* p,unsigned size,F f){auto rows=Children(n,name);Word(p,static_cast<U32>(rows.size()));if(!rows.empty()){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+4),size,static_cast<U32>(rows.size())));for(unsigned i=0;i<rows.size();++i)f(*rows[i],list+size*i);t.Pop();}}
 template<class F> void Optional(const Node& n,const char* name,unsigned char* p,unsigned size,F f){auto c=Child(n,name);if(c){auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),size,1));f(*c,target);t.Pop();}}
 void Object(const Node& n,unsigned char* p,bool usp){

  // Derived strings precede base marshaling in the original method.
  if(usp){String(n,"Faction",p+28);String(n,"Team",p+36);Real(p+44,ParseFloat(Value(n,"Health")));String(n,"EventList",p+48);}
  auto id=n.attributes.find("id");if(id!=n.attributes.end())Word(p,HashAssetId(id->second));Ref(n,"ThingTemplate",p+4);
  U32 flags=0;MarshalEnumBitset(Value(n,"Flags"),Flags,8,7,8,&flags);Word(p+8,flags);Real(p+12,ParseFloat(Value(n,"Angle")));
  auto position=Child(n,"Position");if(position){const char* names[]={"x","y","z"};for(unsigned j=0;j<3;++j)Real(p+16+4*j,ParseFloat(Value(*position,names[j],"0.0")));}
 }
 void Pair(const Node& n,unsigned char* p,unsigned kind){String(n,"Key",p);switch(kind){
 case 0:p[8]=ParseBoolean(Value(n,"Value"));break;
 case 1:Word(p+8,ParseSigned(Value(n,"Value")));break;
 case 2:Real(p+8,ParseFloat(Value(n,"Value")));break;
 case 3:case 4:String(n,"Value",p+8);break;
 case 5:Ref(n,"Value",p+8);break;
 case 6:{auto tokens=Tokens(Value(n,"Value"));Word(p+8,static_cast<U32>(tokens.size()));if(!tokens.empty()){auto list=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+12),8,static_cast<U32>(tokens.size())));for(unsigned i=0;i<tokens.size();++i)Text(tokens[i],list+8*i);t.Pop();}break;}
 }}
 void Dictionary(const Node& n,unsigned char* p){const char* names[]={"BoolProperty","IntProperty","RealProperty","StringProperty","UnicodeStringProperty","AssetIdProperty","AssetIdListProperty"};for(unsigned k=0;k<7;++k)List(n,names[k],p+8*k,(k==3||k==4||k==6)?16:12,[&](const Node& a,unsigned char* b){Pair(a,b,k);});}
 void Analysis(const Node& n,unsigned char* p){const char* fields[]={"GarrisonableStructures","TechStructures","CellsExamined","WaterCells","WaterAreas"};for(unsigned i=0;i<5;++i)Word(p+4*i,ParseSigned(Value(n,fields[i],"0")));List(n,"WaterAreaType",p+20,4,[&](const Node& a,unsigned char* b){std::string v(a.value.c_str());Word(b,v=="INTERIOR"?0:v=="EXTERIOR_PARTIAL"?2:1);});}
 void Root(const Node& n,unsigned char* p){
  const char* names[]={"Waypoint","Structure","Unit","Prop","Audio","Unknown"};for(unsigned i=0;i<6;++i){bool usp=i>=1&&i<=3;List(n,names[i],p+4+8*i,usp?56:28,[&](const Node& a,unsigned char* b){Object(a,b,usp);});}
  List(n,"Road",p+52,4,[&](const Node& a,unsigned char* b){Ref(a,"id",b);});
  Optional(n,"EnvironmentData",p+60,12,[&](const Node& a,unsigned char* b){Ref(a,"Cloud",b);Ref(a,"Macro",b+4);Ref(a,"Environment",b+8);});
  Optional(n,"WorldDict",p+64,56,[&](const Node& a,unsigned char* b){Dictionary(a,b);});
  List(n,"PostEffect",p+68,4,[&](const Node& a,unsigned char* b){Ref(a,"Effect",b);});
  Optional(n,"MapAnalysis",p+76,28,[&](const Node& a,unsigned char* b){Analysis(a,b);});
  // MapMetaData is pipeline-only and never queried by the original GameMap marshaler.
  Child(n,"MapMetaData");
 }
};}
unsigned int XmlgamemapTypeId(const std::string& name){return name=="GameMap"?3575309793u:0;}
Relo::Chunk CompileXmlgamemap(const XmlgamemapNode& input,bool bigEndian){using namespace GameMapDetail;static_assert(sizeof(void*)==4,"Game map requires x86.");Check(XmlgamemapTypeId(input.name)!=0,"Unsupported game map root.");Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),80,1);b.Root(input,root);if(bigEndian)for(auto p:b.words)Swap(p);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
