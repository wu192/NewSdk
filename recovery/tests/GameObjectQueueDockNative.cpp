// Exercises the production GameObject writer using its actual x86 layout.
#include "GameObject.h"
#include "ResourceMarshaling.h"
#include <array>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace Recovered;
using Native::GameObjectChild;
using Bytes=std::vector<unsigned char>;
static void Require(bool value,const char* message) {if(!value) throw std::runtime_error(message);}
static std::uint32_t Word(const Bytes& b,unsigned at,bool big) {
    std::uint32_t value=0;for(unsigned i=0;i<4;++i)value|=std::uint32_t(b.at(at+i))<<((big?3-i:i)*8);return value;
}
static unsigned Token(const Bytes& b,unsigned at,std::uint32_t name,bool big) {
    for(;Word(b,at,big);at+=12)if(Word(b,at,big)==name)return at;
    throw std::runtime_error("Missing QueueDock token");
}
static const char* Names[]={"DockStartBoneName","DockActionBoneName","DockEndBoneName","QueueBonePrefix","TurnTableBoneName"};
static const char* Values[]={"DockStart","DockAction","DockEnd","Que","TurnTable"};
static GameObjectChild Input(const std::string& capacity="",bool strings=true) {
    GameObjectChild module;module.name="QueueDockBehavior";
    module.attributes={{"TypeId","2941001505"},{"id","QueueDock"}};
    if(!capacity.empty())module.attributes["MaxConcurrentMiners"]=capacity;
    if(strings)for(unsigned i=0;i<5;++i)module.attributes[Names[i]]=Values[i];
    return module;
}
static Relo::Chunk Compile(const GameObjectChild& module,bool big) {
    GameObjectChild behaviors;behaviors.name="Behaviors";behaviors.children={module};
    return Native::CompileGameObject({}, {behaviors},big);
}
static bool Relocated(const Bytes& bytes,unsigned location,bool big) {
    for(unsigned at=0;at+4<=bytes.size();at+=4)if(Word(bytes,at,big)==location)return true;
    return false;
}
static void Check(const Relo::Chunk& chunk,bool big,unsigned capacity,bool strings=true) {
    const auto& b=chunk.instance;auto root=Word(b,8,big)-4;
    auto header=Word(b,root+468,big),slots=Word(b,header+4,big),data=Word(b,slots,big);
    Require(Word(b,header,big)==1 && Word(b,data,big)==0xAF4C1F21u,"Original QueueDock TypeId and one module");
    Require(Word(b,data+4,big)==Native::HashAssetId("QueueDock"),"Original ModuleData id");
    Require(Word(b,data+48,big)==0 && Word(b,data+52,big)==capacity,"Animation prefix and checked capacity at +52");
    unsigned wrapper=0;
    for(unsigned at=0;at+12<root;at+=12)if(Word(b,at,big)==2 && Word(b,at+4,big)==0xAF4C1F21u){Require(!wrapper,"One original type wrapper");wrapper=at;}
    Require(wrapper!=0,"QueueDock wrapper exists");auto table=Word(b,wrapper+8,big);
    Require(Word(b,table,big)==1 && Word(b,table+4,big)==816016269u,"Original CreateModuleData parent");
    auto capacityToken=Token(b,table,0x3A9B0156u,big);
    Require(Word(b,capacityToken+4,big)==0x1E0413C5u && Word(b,capacityToken+8,big)==data+52,"Unsigned token targets scalar payload");
    for(unsigned i=0;i<12;++i)Require(b.at(table+96+i)==0,"Eight-token body sentinel");
    Require(!Relocated(chunk.relocations,data+52,big),"Capacity is scalar without a new data relocation");
    unsigned end=data+56;
    const std::uint32_t tokens[]={3160487442u,1056961469u,3246219667u,2098610190u,3567440301u};
    for(unsigned i=0;i<5;++i) {
        auto at=data+8+8*i;const char* expected=strings?Values[i]:"";auto length=std::strlen(expected);
        Require(Word(b,at,big)==length && Word(b,at+4,big)==end,"Five original ReloString layouts after the complete 56-byte body");
        Require(std::memcmp(b.data()+end,expected,length+1)==0,"Original ReloString contents and terminator");
        Require(Relocated(chunk.relocations,at+4,big),"Five original string pointer relocations");
        auto token=Token(b,table,tokens[i],big);
        Require(Word(b,token+4,big)==438907818u && Word(b,token+8,big)==at,"Original string token payloads");
        end+=static_cast<unsigned>((length+4)&~3u);
    }
    Require(b.size()==end,"Exactly 56-byte body and five padded strings, no companion data");
    Require(chunk.imports.empty(),"No new resource imports");
}
static void Write(const std::string& file,const Bytes& bytes) {
    std::ofstream output(file,std::ios::binary);output.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());Require(bool(output),"Write fixture");
}
int main(int argc,char** argv) {
    try {
        for(bool big:{false,true})for(bool strings:{false,true}) {
            Check(Compile(Input("",strings),big),big,1,strings);
            for(unsigned capacity:{1u,2u,10u}) {
                auto chunk=Compile(Input(std::to_string(capacity),strings),big);Check(chunk,big,capacity,strings);
                if(argc>1 && strings) {
                    auto stem=std::string(argv[1])+"/queue-dock-"+std::to_string(capacity)+(big?".be":".le");
                    Write(stem+".bin",chunk.instance);Write(stem+".relo",chunk.relocations);Write(stem+".imp",chunk.imports);
                }
            }
            for(const auto& value:{"08","09","010","+0002"," 2 "})
                Check(Compile(Input(value,strings),big),big,value==std::string("010")?10:value==std::string("08")?8:value==std::string("09")?9:2,strings);
            auto empty=Input("",strings);empty.attributes["MaxConcurrentMiners"]="";
            bool emptyRejected=false;try{Compile(empty,big);}catch(const std::invalid_argument&){emptyRejected=true;}
            Require(emptyRejected,"Explicit empty capacity is not a default");
            for(const auto& value:{"0","11","-1","4294967296","9999999999999999999999999","1.5","text","2junk","0x2","+"," "}) {
                bool rejected=false;try{Compile(Input(value,strings),big);}catch(const std::invalid_argument&){rejected=true;}
                Require(rejected,"Invalid QueueDock capacity must be a production compile error");
            }
        }
        std::cout<<"PASS QueueDock production native serializer: unchanged TypeId/kind, native56, default1/1/2/10, strict decimal/range, eight-token payload offsets, five ReloStrings/relocations, little/big endian\n";
    }catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}
}
