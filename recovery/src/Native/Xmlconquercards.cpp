#include "Xmlconquercards.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlconquercardsDetail {
using N=XmlconquercardsNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
const EnumNameValue EVeterancyLevel[]={{"ELITE",2},{"HEROIC",3},{"INVALID",-1},{"REGULAR",0},{"VETERAN",1}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void Vet(void* p,const std::string& s){int value=2;for(const auto& e:EVeterancyLevel)if(std::strcmp(s.c_str(),e.name)==0){value=e.value;break;}Word(p,static_cast<U>(value));}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr)){MarshalString(t,p+off,Get(n,attr));words.push_back(p+off);}}
 void Ref(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr))MarshalReference(t,p+off,Get(n,attr));}
 template<class F>void List(const N& n,const char* name,unsigned char* p,unsigned off,unsigned size,F f){U count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);U i=0;for(const auto& c:n.children)if(c.name==name)f(c,q+size*i++);t.Pop();}
 void Base(const N& n,unsigned char* p){if(n.attributes.count("Cost"))Word(p+4,ParseSigned(Get(n,"Cost")));Word(p+8,ParseSigned(Get(n,"Maximum","0")));}
 void DefinitionUnit(const N& n,unsigned char* p){Ref(n,"Unit",p,12);List(n,"Upgrade",p,16,8,[&](const N& c,unsigned char* q){Ref(c,"Upgrade",q,0);if(c.attributes.count("Cost"))Word(q+4,ParseSigned(Get(c,"Cost")));});List(n,"Veterancy",p,24,8,[&](const N& c,unsigned char* q){if(c.attributes.count("Veterancy"))Vet(q,Get(c,"Veterancy"));if(c.attributes.count("Cost"))Word(q+4,ParseSigned(Get(c,"Cost")));});Base(n,p);}
 void Card(const N& n,unsigned char* pointer){U type=ParseUnsigned(Get(n,"TypeId","0"));bool unit=type==849812912u;if(unit);else ;auto q=Push(pointer,0,unit?24:8);if(unit){Word(q+8,ParseSigned(Get(n,"Count","1")));Vet(q+12,Get(n,"Veterancy","REGULAR"));List(n,"Upgrade",q,16,4,[&](const N& c,unsigned char* up){Ref(c,"Upgrade",up,0);});}Ref(n,"Card",q,4);Word(q,unit?849812912u:0);t.Pop();}
 void Deck(const N& n,unsigned char* p){String(n,"DisplayName",p,4);String(n,"Description",p,12);String(n,"Side",p,20);if(auto cards=Child(n,"Cards")){U count=static_cast<U>(cards->children.size());Word(p+28,count);if(count){auto q=Push(p,32,4,count);for(U i=0;i<count;++i)Card(cards->children[i],q+4*i);t.Pop();}}}
};
}
unsigned XmlconquercardsTypeId(const std::string& n){if(n=="ConquerCardDefinitionBase")return 3258480561u;if(n=="ConquerCardDefinitionUnit")return 3562795973u;if(n=="ConquerCardDefinitionStructure")return 4289687760u;if(n=="ConquerCardDefinitionPower")return 4164089898u;if(n=="ConquerCardPrebuiltDeck")return 2806522526u;return 0;}
Relo::Chunk CompileXmlconquercards(const XmlconquercardsNode& input,bool bigEndian){using namespace XmlconquercardsDetail;static_assert(sizeof(void*)==4,"Conquer cards require x86");U id=XmlconquercardsTypeId(input.name);Check(id!=0,"Unknown Conquer card root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==3258480561u?12:id==3562795973u?32:id==4289687760u?16:id==4164089898u?24:36,1);if(id==3562795973u)b.DefinitionUnit(input,p);else if(id==2806522526u)b.Deck(input,p);else if(id==4164089898u){b.Ref(input,"Power",p,12);b.String(input,"Side",p,16);b.Base(input,p);}else if(id==4289687760u){b.Ref(input,"Structure",p,12);b.Base(input,p);}else{b.Base(input,p);}if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
