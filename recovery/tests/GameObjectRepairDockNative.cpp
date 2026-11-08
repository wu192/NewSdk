// Contract checks against the production x86 writer. The original DLL has no
// RepairDock marshaler, so only the unchanged Dock path uses it as an oracle.
#include "GameObject.h"
#include "ResourceMarshaling.h"
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace Recovered;
using Native::GameObjectChild;
using Bytes=std::vector<unsigned char>;
static void Require(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
static std::uint32_t Word(const Bytes& b,unsigned at,bool big) {
    std::uint32_t value=0;for(unsigned i=0;i<4;++i)value|=std::uint32_t(b.at(at+i))<<((big?3-i:i)*8);return value;
}
static unsigned Token(const Bytes& b,unsigned table,std::uint32_t name,bool big) {
    for(unsigned at=table;Word(b,at,big);at+=12)if(Word(b,at,big)==name)return at;
    throw std::runtime_error("Missing token");
}
static bool Relocated(const Bytes& b,unsigned location,bool big) {
    for(unsigned at=0;at+4<=b.size();at+=4)if(Word(b,at,big)==location)return true;return false;
}
static GameObjectChild Input(bool filter=false,bool repair=true) {
    GameObjectChild module;module.name=repair?"RepairDockUpdate":"DockUpdate";
    module.attributes={{"TypeId",repair?"347729765":"3651459681"},{"id","Repair"},
        {"NumberApproachPositions","2"},{"AllowsPassthrough","false"},
        {"GoToRallyPointAfterDock","false"},{"ShouldDockInReverse","true"},
        {"MinDockTime","0s"},{"ObjectsInLineWeight","1500ms"}};
    if(repair)module.attributes["FramesForFullHeal"]="150.5";
    if(filter) {
        GameObjectChild harbor;harbor.name="ForVoiceRetreatThisIsASafeHarborToObjectFilter";
        harbor.attributes={{"Rule","ANY"},{"Relationship","SAME_PLAYER"},{"Include","VEHICLE"},{"StatusBitFlags","DAMAGED"}};
        GameObjectChild include;include.name="IncludeThing";include.text="SovietConscript";
        GameObjectChild exclude;exclude.name="ExcludeThing";exclude.text="JapanCommando";
        harbor.children={include,exclude};module.children={harbor};
    }
    return module;
}
static Relo::Chunk Compile(GameObjectChild module,bool big) {
    GameObjectChild behaviors;behaviors.name="Behaviors";behaviors.children={module};
    GameObjectChild active;active.name="ActiveBody";active.attributes={{"TypeId","1510882715"},{"id","Body"},{"MaxHealth","100"}};
    GameObjectChild body;body.name="Body";body.children={active};
    return Native::CompileGameObject({}, {behaviors,body},big);
}
static void Check(const Relo::Chunk& chunk,bool big,bool filter,bool repair=true) {
    const auto& b=chunk.instance;auto root=Word(b,8,big)-4,header=Word(b,root+468,big);
    auto slots=Word(b,header+4,big),data=Word(b,slots,big);
    const std::uint32_t type=repair?347729765u:3651459681u;
    Require(Word(b,header,big)==1 && Word(b,data,big)==type,"One real Dock module, native type identity");
    Require(Word(b,data+4,big)==Native::HashAssetId("Repair"),"Inherited ModuleData id");
    Require(Word(b,data+8,big)==2 && Word(b,data+12,big)==0 && Word(b,data+16,big)==0x3FC00000u,"Dock int and Time fields");
    Require(b.at(data+24)==0 && b.at(data+25)==0 && b.at(data+26)==1 && b.at(data+27)==0,"Inherited booleans and padding");
    unsigned wrapper=0;
    for(unsigned at=0;at+12<root;at+=12)if(Word(b,at,big)==2 && Word(b,at+4,big)==type){Require(!wrapper,"One behavior wrapper");wrapper=at;}
    Require(wrapper!=0,"Polymorphic wrapper present");auto table=Word(b,wrapper+8,big),dockTable=table;
    if(repair) {
        Require(Word(b,table,big)==1 && Word(b,table+4,big)==3651459681u,"Repair inherits Dock token table");
        dockTable=Word(b,table+8,big);auto frames=Token(b,table,627633844u,big);
        Require(Word(b,frames+4,big)==1683514611u && Word(b,frames+8,big)==data+28,"Raw float token bookmark at +28");
        Require(Word(b,data+28,big)==0x43168000u,"FramesForFullHeal is a float count, without Time conversion");
        Require(!Relocated(chunk.relocations,data+28,big),"Frames is not a pointer relocation");
        for(unsigned i=0;i<12;++i)Require(b.at(table+24+i)==0,"Repair two-token table sentinel");
    }
    Require(Word(b,dockTable,big)==1 && Word(b,dockTable+4,big)==4043318533u,"Dock Update inheritance");
    for(unsigned i=0;i<12;++i)Require(b.at(dockTable+96+i)==0,"Dock eight-token table sentinel");
    const auto harbor=Token(b,dockTable,610727759u,big);
    Require(Word(b,harbor+4,big)==3832456114u,"Existing ObjectFilter token identity");
    auto pointer=Word(b,data+20,big);
    if(filter) {
        Require(pointer==data+(repair?32:28),"Filter allocation follows entire native module");
        Require(Relocated(chunk.relocations,data+20,big),"Present optional filter pointer relocates");
        Require(Word(b,pointer+4,big)==2 && Word(b,pointer+8,big)==8,"Filter ANY/same-player fields and endian");
        Require((Word(b,pointer+40,big)&(1u<<12))!=0,"Filter vehicle KindOf preserved");
        Require(Word(b,pointer+96,big)==1 && Word(b,pointer+104,big)==1,"Filter included/excluded template list counts");
        auto included=Word(b,pointer+100,big),excluded=Word(b,pointer+108,big);
        Require(Word(b,included,big)==Native::HashAssetId("SovietConscript") && Word(b,excluded,big)==Native::HashAssetId("JapanCommando"),"Filter template hashes");
        Require(Relocated(chunk.relocations,pointer+88,big) && Relocated(chunk.relocations,pointer+100,big) && Relocated(chunk.relocations,pointer+108,big),"Filter status and list pointers relocate");
        auto filterTable=Word(b,harbor+8,big);
        Require(filterTable!=0 && Word(b,Token(b,filterTable,2363267224u,big)+8,big)==pointer,"Filter table bookmarks actual pointee");
    } else {
        Require(pointer==0 && Word(b,harbor+8,big)==0,"Absent optional filter remains null in data and token");
        Require(!Relocated(chunk.relocations,data+20,big),"Null filter has no data relocation");
        Require(Word(b,root+480,big)==data+(repair?32:28),"Next module proves exact 32/28 byte native extent");
    }
    Require(chunk.imports.empty(),"No new module import protocol");
}
static void Reject(GameObjectChild module) {
    try{Compile(module,false);}catch(const std::exception&){return;}
    throw std::runtime_error("Invalid RepairDock accepted");
}
static void Write(const std::string& file,const Bytes& bytes) {
    std::ofstream out(file,std::ios::binary);out.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());Require(bool(out),"Write fixture");
}
int main(int argc,char** argv) {
    try {
        for(bool big:{false,true})for(bool repair:{false,true})for(bool filter:{false,true}) {
            auto result=Compile(Input(filter,repair),big);Check(result,big,filter,repair);
            if(argc>1) {
                auto stem=std::string(argv[1])+"/"+(repair?"repair-dock":"dock")+(filter?"-filter":"-null")+(big?".be":".le");
                Write(stem+".bin",result.instance);Write(stem+".relo",result.relocations);Write(stem+".imp",result.imports);
            }
        }
        for(const auto& value:{"0","-1","NaN","INF","-INF","1e39","1e-99","150s","=REPAIR_FRAMES","bad"}) {
            auto input=Input();input.attributes["FramesForFullHeal"]=value;Reject(input);
        }
        auto missing=Input();missing.attributes.erase("FramesForFullHeal");Reject(missing);
        for(const auto& value:{"0","-1","2147483648","1.5","=WAITERS","bad"}) {
            auto input=Input();input.attributes["NumberApproachPositions"]=value;Reject(input);
        }
        missing=Input();missing.attributes.erase("NumberApproachPositions");Reject(missing);
        std::cout<<"PASS RepairDock x86 native: 32-byte type, inherited Dock fields/tokens, raw float, optional filter data/relocations, little/big endian, invalid values; unchanged 28-byte Dock\n";
    }catch(const std::exception& error){std::cerr<<error.what()<<"\n";return 1;}
}
