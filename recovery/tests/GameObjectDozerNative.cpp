// Production serializer smoke test. Requires the real x86 ABI, on Windows or
// a 32-bit host; it does not emulate the managed XML/Core preprocessing path.
#include "GameObject.h"
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>

using namespace Recovered;
using Native::GameObjectChild;
using Bytes = std::vector<unsigned char>;
static std::uint32_t Word(const Bytes& bytes, unsigned at, bool big) {
    std::uint32_t value=0;
    for(unsigned i=0;i<4;++i) value|=std::uint32_t(bytes.at(at+i))<<((big?3-i:i)*8);
    return value;
}
static void Require(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
static unsigned Find(const Bytes& bytes,unsigned table,std::uint32_t name,bool big) {
    for(unsigned at=table;Word(bytes,at,big);at+=12) if(Word(bytes,at,big)==name) return at;
    throw std::runtime_error("Missing token");
}
static std::vector<GameObjectChild> Input(const char* setting,bool defaults=false) {
    GameObjectChild dozer;dozer.name="DozerAIUpdate";
    dozer.attributes={{"TypeId","2366190371"},{"id","Dozer"}};
    if(!defaults) {
        dozer.attributes["RepairHealthPercentPerSecond"]="2.5";
        dozer.attributes["BoredTime"]="3.25";
        dozer.attributes["BoredRange"]="64";
        dozer.attributes["AttackPriority"]="Priority";
        GameObjectChild chooser;chooser.name="UnitAITargetChooserData";
        dozer.children.push_back(chooser);
    }
    GameObjectChild ai;ai.name="AI";ai.children.push_back(dozer);
    std::vector<GameObjectChild> children{ai};
    if(setting) {
        GameObjectChild extension;extension.name="EnhancerSettings";
        if(*setting) extension.attributes["DozerConstruction"]=setting;
        children.push_back(extension);
    }
    return children;
}
static void Check(const Relo::Chunk& chunk,bool big,const char* setting,bool defaults=false) {
    const auto& bytes=chunk.instance;
    auto root=Word(bytes,8,big)-4;
    auto list=Word(bytes,Find(bytes,0,1376227828u,big)+8,big);
    Require(Word(bytes,Word(bytes,list+8,big),big)==1,"AI list count");
    auto wrapper=Word(bytes,list+20,big);
    Require(Word(bytes,wrapper,big)==2 && Word(bytes,wrapper+4,big)==0x8D093323u,"Native Dozer polymorphic type");
    auto table=Word(bytes,wrapper+8,big);
    Require(Word(bytes,table,big)==1 && Word(bytes,table+4,big)==0x08DBC00Eu,"AIUpdate inheritance layer");
    for(unsigned i=0;i<12;++i) Require(bytes.at(table+48+i)==0,"Dozer token sentinel");
    auto repair=Find(bytes,table,0x8229777Eu,big);
    auto bored=Find(bytes,table,0x18C1A7F3u,big);
    auto range=Find(bytes,table,0xC492A6B5u,big);
    auto data=Word(bytes,repair+8,big)-76;
    Require(Word(bytes,data,big)==0x8D093323u,"Dozer native data type");
    Require(Word(bytes,bored+8,big)==data+80 && Word(bytes,range+8,big)==data+84,"Dozer native float offsets");
    for(auto row:{repair,bored,range}) Require(Word(bytes,row+4,big)==0x645864F3u,"Dozer raw float token type");
    Require(Word(bytes,data+76,big)==(defaults?0u:0x40200000u),"Repair uses raw float, not percent conversion");
    Require(Word(bytes,data+80,big)==(defaults?0u:0x40500000u),"BoredTime uses raw float");
    Require(Word(bytes,data+84,big)==(defaults?0u:0x42800000u),"BoredRange float");
    Require(Word(bytes,data+20,big)==0x43FA0000u && Word(bytes,data+52,big)==1,"Inherited AI defaults");
    if(defaults) Require(bytes.size()==data+88,"Dozer native size is 0x58");
    else Require(Word(bytes,data+12,big)==data+88 && Word(bytes,data+68,big)>data+88,"Inherited string and chooser after Dozer data");
    Require(data>=root+592,"Native GameObject layout remains 592 serialized bytes");
    if(setting) {
        auto extension=Find(bytes,0,0x09135D3Fu,big);
        Require(Word(bytes,extension+4,big)==0x09135D3Fu,"EnhancerSettings type ID");
        auto nested=Word(bytes,extension+8,big);
        Require(Word(bytes,nested,big)==0x45945FF5u && Word(bytes,nested+4,big)==0x77E3F4F7u,"DozerConstruction bool token");
        auto boolean=Word(bytes,nested+8,big);
        Require(bytes.at(boolean)==(*setting=='t'?1:0),"Construction bool/default value");
        for(unsigned i=0;i<12;++i) Require(bytes.at(nested+12+i)==0,"EnhancerSettings token sentinel");
        Require(boolean+4==bytes.size(),"Extension is independent aligned trailing data");
    } else Require(Word(bytes,1512,big)==0,"Absent extension preserves root token count");
    Require(chunk.imports.empty(),"Tokenized imports remain empty");
}
static void Write(const std::string& name,const Bytes& bytes) {
    std::ofstream file(name,std::ios::binary);file.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());
    Require(bool(file),"Cannot write fixture");
}
int main(int argc,char** argv) {
    try {
        for(bool big:{false,true}) {
            auto empty=Native::CompileGameObject({}, {},big);
            Require(empty.instance.size()==2212 && Word(empty.instance,8,big)==1624,"Original empty asset bytes/layout");
            auto defaults=Native::CompileGameObject({},Input(nullptr,true),big);Check(defaults,big,nullptr,true);
            for(const char* setting:{static_cast<const char*>(nullptr),"true","false",""}) {
                auto chunk=Native::CompileGameObject({},Input(setting),big);Check(chunk,big,setting);
                if(argc>1 && (!setting || *setting)) {
                    const auto stem=std::string(argv[1])+"/dozer-"+(setting?setting:"omitted")+(big?".be":".le");
                    Write(stem+".bin",chunk.instance);Write(stem+".relo",chunk.relocations);
                }
            }
        }
        std::cout<<"PASS native DozerAIUpdate + EnhancerSettings, x86 little/big endian; root token offset 0\n";
    } catch(const std::exception& error) { std::cerr<<error.what()<<"\n";return 1; }
}
