#include "Xmlassetindexes.h"
#include "ScalarParsing.h"
#include "ResourceMarshaling.h"
#include "DataInitializers.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace XmlassetindexesDetail {
using N=XmlassetindexesNode;using U=Relo::U32;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

const N* Child(const N& n,const char* name){const N* result=nullptr;for(const auto& c:n.children)if(c.name==name){result=&c;}return result;}
std::string Get(const N& n,const char* k,const char* d=""){auto i=n.attributes.find(k);return i==n.attributes.end()?d:i->second;}
void Swap(void* p){U x;std::memcpy(&x,p,4);x=(x<<24)|((x<<8)&0xff0000u)|((x>>8)&0xff00u)|(x>>24);std::memcpy(p,&x,4);}
U RawHash(const std::string& input){
    const auto p0=reinterpret_cast<const unsigned char*>(input.c_str());auto p=p0;U size=static_cast<U>(std::strlen(input.c_str())),h=size;
    if(!size)return 0;
    auto shortWord=[](const unsigned char* b){return U(b[0])|(U(b[1])<<8);};
    auto signedByte=[](unsigned char c){return static_cast<U>(static_cast<std::int8_t>(c));};
    for(U n=size/4;n;--n,p+=4){h+=shortWord(p);h^=(shortWord(p+2)^(h<<5))<<11;h+=h>>11;}
    switch(size&3){case 3:h+=shortWord(p);h^=((signedByte(p[2])<<2)^h)<<16;h+=h>>11;break;case 2:h+=shortWord(p);h^=h<<11;h+=h>>17;break;case 1:h+=signedByte(*p);h^=h<<10;h+=h>>1;break;}
    h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
}

const EnumNameValue EStringHashBin[]={{"INSTANCEID",1},{"POID",2},{"STRINGHASH",3},{"TYPEID",0}};
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U v){std::memcpy(p,&v,4);words.push_back(p);}
 unsigned char* Push(unsigned char* p,unsigned off,unsigned size,unsigned count=1){return static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p+off),size,count));}
 void String(const N& n,const char* attr,unsigned char* p,unsigned off){if(n.attributes.count(attr)){MarshalString(t,p+off,Get(n,attr));words.push_back(p+off);}}
 template<class F>void List(const N& n,const char* name,unsigned char* p,unsigned off,unsigned size,F f){U count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(p+off,count);if(!count)return;auto q=Push(p,off+4,size,count);U i=0;for(const auto& c:n.children)if(c.name==name)f(c,q+size*i++);t.Pop();}
 void Hashes(const N& n,unsigned char* p){if(n.attributes.count("Id"))Word(p,RawHash(Get(n,"Id")));if(n.attributes.count("Type"))Word(p+4,RawHash(Get(n,"Type")));}
 void Report(const N& n,unsigned char* p){Hashes(n,p);if(n.attributes.count("AssetSize"))Word(p+8,ParseUnsigned(Get(n,"AssetSize")));List(n,"Reference",p,12,8,[&](const N& c,unsigned char* q){Hashes(c,q);});}
 void Content(const N& n,unsigned char* p){if(n.attributes.count("PackageNumber"))p[20]=static_cast<unsigned char>(ParseUnsigned(Get(n,"PackageNumber")));List(n,"License",p,4,12,[&](const N& c,unsigned char* q){if(c.attributes.count("LicenseMask"))Word(q,ParseUnsigned(Get(c,"LicenseMask")));List(c,"Asset",q,4,4,[&](const N& asset,unsigned char* word){Word(word,HashAssetId(asset.value));});});List(n,"Patch",p,12,12,[&](const N& c,unsigned char* q){String(c,"Name",q,0);if(c.attributes.count("PatchNumber"))q[8]=static_cast<unsigned char>(ParseUnsigned(Get(c,"PatchNumber")));});}
 void Table(const N& n,unsigned char* p){if(n.attributes.count("StringHashBin")){std::string s=Get(n,"StringHashBin");int value=1;for(const auto& e:EStringHashBin)if(std::strcmp(s.c_str(),e.name)==0){value=e.value;break;}Word(p+4,value);}List(n,"StringAndHash",p,8,12,[&](const N& c,unsigned char* q){if(c.attributes.count("Hash"))Word(q,ParseUnsigned(Get(c,"Hash")));String(c,"Text",q,4);});}
};
}
unsigned XmlassetindexesTypeId(const std::string& n){return n=="DLContent"?212695140u:n=="AssetReportTable"?255713734u:n=="StringHashTable"?300273508u:0;}
Relo::Chunk CompileXmlassetindexes(const XmlassetindexesNode& input,bool bigEndian){using namespace XmlassetindexesDetail;static_assert(sizeof(void*)==4,"Asset indexes require x86");U id=XmlassetindexesTypeId(input.name);Check(id!=0,"Unknown asset index root");Builder b;unsigned char* p=nullptr;b.t.Push(reinterpret_cast<void**>(&p),id==212695140u?24:id==255713734u?12:16,1);if(id==212695140u)b.Content(input,p);else if(id==255713734u){b.List(input,"AssetReport",p,4,20,[&](const N& c,unsigned char* q){b.Report(c,q);});}else b.Table(input,p);if(bigEndian)for(auto q:b.words)Swap(q);return b.t.MakeRelocatable(bigEndian,0);}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
