// Runs the production serializer with the real x86 data layout.
#include "GameObject.h"
#include "ResourceMarshaling.h"
#include <algorithm>
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace Recovered;
using Native::GameObjectChild;
using Bytes = std::vector<unsigned char>;
static void Require(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
static std::uint32_t Word(const Bytes& b,unsigned at,bool big) {
    std::uint32_t v=0;for(unsigned i=0;i<4;++i)v|=std::uint32_t(b.at(at+i))<<((big?3-i:i)*8);return v;
}
static unsigned Token(const Bytes& b,unsigned at,std::uint32_t name,bool big) {
    for(;Word(b,at,big);at+=12)if(Word(b,at,big)==name)return at;
    throw std::runtime_error("Missing token");
}
static GameObjectChild Input(const std::string& capacity="3",bool references=false) {
    GameObjectChild module;module.name="PassiveMindControlUpdate";
    module.attributes={{"TypeId","4000878923"},{"id","ModuleTag_PassiveMindControl"},{"AcquireRadius","200"}};
    if(!capacity.empty())module.attributes["Capacity"]=capacity;
    GameObjectChild filter;filter.name="TargetFilter";
    filter.attributes={{"Rule","ANY"},{"Relationship","ENEMIES"},{"Include","INFANTRY VEHICLE"}};
    if(references) {
        filter.attributes["StatusBitFlags"]="DAMAGED";
        GameObjectChild include;include.name="IncludeThing";include.text="SovietConscript";
        GameObjectChild exclude;exclude.name="ExcludeThing";exclude.text="JapanCommando";
        filter.children={include,exclude};
    }
    module.children={filter};return module;
}
static Relo::Chunk Compile(GameObjectChild module,bool big) {
    GameObjectChild behaviors;behaviors.name="Behaviors";behaviors.children={module};
    GameObjectChild activeBody;activeBody.name="ActiveBody";
    activeBody.attributes={{"TypeId","1510882715"},{"id","Body"},{"MaxHealth","100"}};
    GameObjectChild body;body.name="Body";body.children={activeBody};
    return Native::CompileGameObject({}, {behaviors,body},big);
}
static bool Relocated(const Bytes& b,unsigned location,bool big) {
    for(unsigned at=0;at+4<=b.size();at+=4) if(Word(b,at,big)==location) return true;
    return false;
}
static void Check(const Relo::Chunk& chunk,bool big,unsigned capacity,bool references,unsigned interval=15) {
    const auto& b=chunk.instance;
    auto root=Word(b,8,big)-4,header=Word(b,root+468,big),slots=Word(b,header+4,big),data=Word(b,slots,big);
    Require(Word(b,header,big)==1,"One behavior");
    Require(Word(b,data,big)==0xEE78914Bu,"Module TypeId");
    Require(Word(b,data+4,big)==Native::HashAssetId("ModuleTag_PassiveMindControl"),"Shared ModuleData id");
    Require(Word(b,data+8,big)==capacity && Word(b,data+12,big)==0x43480000u && Word(b,data+16,big)==interval,"Scalars/defaults/offsets");
    unsigned wrapper=0;
    for(unsigned at=0;at+12<root;at+=12)if(Word(b,at,big)==2 && Word(b,at+4,big)==0xEE78914Bu){Require(!wrapper,"One wrapper");wrapper=at;}
    Require(wrapper!=0,"Registered polymorphic wrapper");
    auto table=Word(b,wrapper+8,big);
    Require(table==wrapper+12,"Wrapper is one twelve-byte row without sentinel");
    Require(Word(b,table,big)==1 && Word(b,table+4,big)==0xD75D93A2u,"ModuleData parent token");
    auto base=Word(b,table+8,big);
    Require(Word(b,Token(b,base,0x8CDC9898u,big)+8,big)==data+4,"Parent id token offset");
    const std::uint32_t names[]={0xC890C0E1u,0xB4BD3599u,0x7EF7A1A1u};
    for(unsigned i=0;i<3;++i) {
        auto token=Token(b,table,names[i],big);
        Require(Word(b,token+4,big)==(i==1?0x4292E499u:0x1E0413C5u),"Scalar token type");
        Require(Word(b,token+8,big)==data+8+4*i,"Scalar token address");
    }
    auto filter=Token(b,table,0xB73A2D5Fu,big);
    Require(Word(b,filter+4,big)==0xE46EA3B2u,"ObjectFilter token type");
    auto filterTable=Word(b,filter+8,big);
    Require(Word(b,Token(b,filterTable,2363267224u,big)+8,big)==data+20,"Inline mode 8 filter starts at +0x14");
    Require(Word(b,data+24,big)==2 && Word(b,data+28,big)==2,"Filter ANY/enemies");
    Require((Word(b,data+36,big)&(1u<<10))!=0 && (Word(b,data+60,big)&(1u<<12))!=0,"Filter infantry/vehicle KindOf");
    for(unsigned i=0;i<12;++i)Require(b.at(table+60+i)==0,"Body sentinel");
    Require(chunk.imports.empty(),"Weak filter names remain hashes without imports");
    if(references) {
        Require(Word(b,data+116,big)==1 && Word(b,data+124,big)==1,"Inline template list counts");
        auto included=Word(b,data+120,big),excluded=Word(b,data+128,big);
        Require(included>=data+132 && excluded>=data+132,"Filter allocations follow the complete 0x84 body");
        Require(Word(b,included,big)==Native::HashAssetId("SovietConscript") && Word(b,excluded,big)==Native::HashAssetId("JapanCommando"),"Filter template hashes survive");
        Require(Relocated(chunk.relocations,data+108,big) && Relocated(chunk.relocations,data+120,big) && Relocated(chunk.relocations,data+128,big),"Inline filter status and lists relocate");
        auto includes=Word(b,Token(b,filterTable,3363839358u,big)+8,big);
        Require(Word(b,includes+8,big)==data+116 && Word(b,includes+20,big)==included,"Filter list token addresses");
    } else Require(Word(b,root+480,big)==data+132,"Next module follows fixed 0x84 body");
}
static void Reject(GameObjectChild module) {
    try {Compile(module,false);}catch(const std::exception&){return;}
    throw std::runtime_error("Invalid module accepted");
}
static void Write(const std::string& file,const Bytes& bytes) {
    std::ofstream output(file,std::ios::binary);output.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());Require(bool(output),"Write fixture");
}
int main(int argc,char** argv) {
    try {
        for(bool big:{false,true}) {
            for(unsigned capacity:{1u,3u,8u,0xFFFFFFFFu})for(bool references:{false,true}) {
                auto chunk=Compile(Input(std::to_string(capacity),references),big);Check(chunk,big,capacity,references);
                if(argc>1 && (capacity==3 || capacity==8) && references) {
                    auto stem=std::string(argv[1])+"/mind-control-capacity-"+std::to_string(capacity)+(big?".be":".le");
                    Write(stem+".bin",chunk.instance);Write(stem+".relo",chunk.relocations);Write(stem+".imp",chunk.imports);
                }
            }
            Check(Compile(Input(""),big),big,3,false);
            auto interval=Input();interval.attributes["CaptureIntervalFrames"]="1073741822";
            Check(Compile(interval,big),big,3,false,1073741822);
        }
        for(const auto& value:{"0","-1","4294967296","1.5","text"}){auto m=Input();m.attributes["Capacity"]=value;Reject(m);}
        Check(Compile(Input("+0008"),false),false,8,false);
        for(const auto& value:{"0","-1","NaN","INF","-INF","1e39","1junk","1e","0x1p1","1D2"}){auto m=Input();m.attributes["AcquireRadius"]=value;Reject(m);}
        for(const auto& value:{"0","-1","1073741823","4294967296"}){auto m=Input();m.attributes["CaptureIntervalFrames"]=value;Reject(m);}
        auto missingRadius=Input();missingRadius.attributes.erase("AcquireRadius");Reject(missingRadius);
        auto missingFilter=Input();missingFilter.children.clear();Reject(missingFilter);
        auto duplicateFilter=Input();duplicateFilter.children.push_back(duplicateFilter.children[0]);Reject(duplicateFilter);
        std::cout<<"PASS passive mind control production native serializer: capacity 1/3/8/uint32 max, defaults, invalid values, mode 8 inline filter, template hashes/relocations, little/big endian\n";
    } catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}
}
