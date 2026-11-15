#include "../src/Native/GameObject.h"
#include <cstring>
#include <iostream>
#include <stdexcept>

using Recovered::Native::GameObjectChild;
using Recovered::Relo::Chunk;
using U32 = std::uint32_t;

static void Require(bool value,const char* message) {
    if(!value) throw std::runtime_error(message);
}
static U32 Word(const std::vector<unsigned char>& bytes,U32 at) {
    U32 result;std::memcpy(&result,bytes.data()+at,4);return result;
}
static U32 Token(const std::vector<unsigned char>& bytes,U32 table,U32 name) {
    for(U32 at=table;Word(bytes,at);at+=12) if(Word(bytes,at)==name) return at;
    throw std::runtime_error("Missing contract token");
}
static bool Relocation(const Chunk& chunk,U32 at) {
    for(U32 i=0;i<chunk.relocations.size();i+=4) if(Word(chunk.relocations,i)==at) return true;
    return false;
}
static Chunk Compile(GameObjectChild module) {
    GameObjectChild ai;ai.name="AI";ai.children.push_back(module);
    return Recovered::Native::CompileGameObject({}, {ai},false);
}
static U32 Module(const Chunk& chunk) {
    U32 root=Word(chunk.instance,8)-4;
    Require(Word(chunk.instance,root+472)==1,"GameObject AI root count");
    U32 slots=Word(chunk.instance,root+476);
    Require(Relocation(chunk,root+476) && Relocation(chunk,slots),"AI list/polymorphic pointer relocations");
    return Word(chunk.instance,slots);
}
static U32 ModuleTable(const Chunk& chunk) {
    U32 list=Word(chunk.instance,Token(chunk.instance,0,1376227828u)+8);
    U32 wrapper=Word(chunk.instance,list+20);
    Require(Word(chunk.instance,wrapper)==2 && Word(chunk.instance,wrapper+4)==0x8D093323u,"Dozer polymorphic token");
    return Word(chunk.instance,wrapper+8);
}
static GameObjectChild Dozer() {
    GameObjectChild module;module.name="DozerAIUpdate";module.attributes["TypeId"]="0x8D093323";return module;
}
static void Contract() {
    auto module=Dozer();auto chunk=Compile(module);U32 data=Module(chunk),table=ModuleTable(chunk);
    Require(chunk.instance.size()==data+0x58,"Dozer data size 0x58");
    Require(Word(chunk.instance,data)==0x8D093323u,"Dozer native type");
    Require(Word(chunk.instance,data+0x4C)==0 && Word(chunk.instance,data+0x50)==0 && Word(chunk.instance,data+0x54)==0,"Dozer zero defaults");
    Require(Word(chunk.instance,table)==1 && Word(chunk.instance,table+4)==0x08DBC00Eu,"Dozer AIUpdate inheritance token");
    U32 parent=Word(chunk.instance,table+8);
    Require(Word(chunk.instance,parent)==1 && Word(chunk.instance,parent+4)==4043318533u,"AIUpdate Update inheritance token");
    const U32 names[]={0x8229777Eu,0x18C1A7F3u,0xC492A6B5u};
    for(U32 i=0;i<3;++i) {
        U32 at=table+12*(i+1);
        Require(Word(chunk.instance,at)==names[i] && Word(chunk.instance,at+4)==0x645864F3u,"Dozer scalar token ID/type");
        Require(Word(chunk.instance,at+8)==data+0x4C+4*i,"Dozer scalar token data offset");
    }
    for(U32 i=0;i<12;++i) Require(chunk.instance[table+48+i]==0,"Dozer token sentinel");
    auto base=module;base.name="AIUpdate";base.attributes["TypeId"]="0x08DBC00E";auto inherited=Compile(base);U32 baseAt=Module(inherited);
    Require(std::memcmp(chunk.instance.data()+data+4,inherited.instance.data()+baseAt+4,72)==0,"AIUpdate inherited defaults unchanged");
    Require(chunk.imports.empty(),"Dozer imports buffer");

    module.attributes["RepairHealthPercentPerSecond"]="25%";
    module.attributes["BoredTime"]="100ms";
    module.attributes["BoredRange"]="-1.5";
    module.attributes["StopChaseDistance"]="12.5";
    module.attributes["MinCowerTime"]="500ms";
    module.attributes["StandGround"]="true";
    chunk=Compile(module);data=Module(chunk);
    Require(Word(chunk.instance,data+0x4C)==0x41C80000u,"RepairHealthPercentPerSecond remains raw float");
    Require(Word(chunk.instance,data+0x50)==0x42C80000u,"BoredTime remains raw float");
    Require(Word(chunk.instance,data+0x54)==0xBFC00000u,"BoredRange raw negative float");
    Require(Word(chunk.instance,data+20)==0x41480000u && Word(chunk.instance,data+24)==0x3F000000u && chunk.instance[data+72]==1,"AIUpdate inherited marshal");
    module.attributes["BoredRange"]="-0";chunk=Compile(module);data=Module(chunk);
    Require(Word(chunk.instance,data+0x54)==0x80000000u,"Dozer signed zero bit pattern");

    module=Dozer();
    GameObjectChild chooser;chooser.name="UnitAITargetChooserData";chooser.attributes["TargetingCompareList"]="Target\\23";
    GameObjectChild range;range.name="ObjectVisionRange";range.attributes["Range"]="25%";
    GameObjectChild filter;filter.name="Filter";filter.attributes["Include"]="ALL";range.children.push_back(filter);
    chooser.children.push_back(range);module.children.push_back(chooser);
    chunk=Compile(module);data=Module(chunk);table=ModuleTable(chunk);parent=Word(chunk.instance,table+8);
    U32 choice=Word(chunk.instance,data+68);
    Require(choice==data+0x58 && Relocation(chunk,data+68),"UnitAITargetChooserData pointer and relocation");
    Require(Word(chunk.instance,choice+36)==24,"UnitAITargetChooserData reference");
    Require(Word(chunk.instance,choice+20)==1 && Word(chunk.instance,Word(chunk.instance,choice+24)+4)==0x3E800000u,"Inherited chooser range list");
    U32 chooserToken=Word(chunk.instance,Token(chunk.instance,parent,607907585u)+8);
    Require(Word(chunk.instance,chooserToken)==1 && Word(chunk.instance,chooserToken+4)==2856512412u,"UnitAITargetChooserData inherited tokens");

    const std::string type="0x8D093323";
    Require(Recovered::Native::GameObjectModuleChildSupported("AI","DozerAIUpdate"),"Dozer AI XML child dispatch");
    for(const char* field:{"RepairHealthPercentPerSecond","BoredTime","BoredRange","StopChaseDistance"})
        Require(Recovered::Native::GameObjectModuleAttributeSupported(type,field),"Dozer XML attributes");
    Require(Recovered::Native::GameObjectRecursiveModuleKind(type)==291,"Dozer recursive XML dispatch");
    Require(Recovered::Native::GameObjectModuleNodeKind(291,"UnitAITargetChooserData/ObjectVisionRange/Filter")==1,"Dozer inherited chooser Filter XML node");
    Require(Recovered::Native::GameObjectModuleNodeAttribute(291,"UnitAITargetChooserData","TargetingCompareList"),"Dozer inherited chooser XML attribute");
}

int main() {
    Contract();
    std::cout << "PASS LegacyDozerNative: native 0x58/type/float bits/inheritance/chooser/relocations/XML dispatch\n";
}
