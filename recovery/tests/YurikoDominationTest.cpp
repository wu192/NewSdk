#include "YurikoModuleTestSupport.h"
#include "ResourceMarshaling.h"
int main() {try {
    for(bool big:{false,true}) {
        GameObjectChild module;module.name="LureObjectsUpdate";
        module.attributes={{"TypeId","3059398442"},{"id","Lure"},{"LureObjectLinkID","7"},{"GuardStatus","UNSELECTABLE"},{"TargetMarkerObjectRef","Marker"}};
        for(int i=0;i<3;++i) {GameObjectChild offset;offset.name="GuardOffset";offset.attributes={{"x",std::to_string(i*10)},{"y","20"},{"z","3"}};module.children.push_back(offset);}
        auto output=Compile(module,big);const auto& b=output.instance;auto table=Table(b,0xb65ab72au,big);
        Require(Word(b,Field(b,table,0x16e57839u,big),big)==7,"lure link ID");
        Require(Number(b,Field(b,table,0x3283a5d7u,big),big)==1000000,"native range-squared default");
        Require(Number(b,Field(b,table,0x5052f256u,big),big)==99999,"native guard distance default");
        Require(Word(b,Field(b,table,0xfd25ec9eu,big),big)==10,"native update-rate default");
        Require(Word(b,Field(b,table,0xf8234249u,big),big)==HashAssetId("Marker"),"marker weak reference");
        Require(Word(b,InheritedField(b,table,0x8cdc9898u,big),big)==HashAssetId("Lure"),"deeply inherited module ID");
        auto list=Field(b,table,0x13b3d27cu,big);
        Require(Word(b,Word(b,list+8,big),big)==3,"guard-offset count controls capacity");
        for(unsigned i=0;i<3;++i) {
            auto vector=Word(b,list+12*(i+1)+8,big);
            Require(Number(b,Field(b,vector,0x043c9363u,big),big)==float(i*10),"guard-offset x");
            Require(Number(b,Field(b,vector,0x1e8fda1au,big),big)==20,"guard-offset y");
            Require(Number(b,Field(b,vector,0x30be6097u,big),big)==3,"guard-offset z");
        }
        GameObjectChild power;power.name="AddObjectsToLureUpdateSpecialPower";
        power.attributes={{"TypeId","2816409548"},{"id","Dominate"},{"SpecialPowerTemplate","Power\\5"},{"TeleportLinkID","4"},{"LureObjectLinkID","7"},{"Radius","150"}};
        auto po=Compile(power,big);auto pt=Table(po.instance,0xa7deffccu,big);
        Require(Word(po.instance,InheritedField(po.instance,pt,0xfe5dbc0bu,big),big)==4,"inherited store collection link is independent");
        Require(Number(po.instance,InheritedField(po.instance,pt,0x4f31c6c5u,big),big)==150,"inherited store radius");
        Require(Word(po.instance,Field(po.instance,pt,0x16e57839u,big),big)==7,"own destination lure link");
        power.attributes.erase("LureObjectLinkID");
        auto defaults=Compile(power,big);auto defaultTable=Table(defaults.instance,0xa7deffccu,big);
        Require(Word(defaults.instance,Field(defaults.instance,defaultTable,0x16e57839u,big),big)==0,"RA3 port destination-link default");
        Require(GameObjectModuleNodeKind(291,"GuardOffset")==2,"list XML reader route");
        Require(GameObjectModuleNodeKind(292,"AttributeModifierAffects/IncludeThing")==3,"inherited special-power filter route");
    }
    std::cout<<"Yuriko domination: native defaults, formation capacity, vector tokens, weak references, independent store/destination links and LE/BE passed\n";return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}}
