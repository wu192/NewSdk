#include "YurikoModuleTestSupport.h"
#include "ResourceMarshaling.h"
int main() {try {
    for(bool big:{false,true}) {
        GameObjectChild add;add.name="AddObjectsToLiftUpdateSpecialPower";
        add.attributes={{"TypeId","3652339679"},{"id","Pickup"},{"SpecialPowerTemplate","Power\\5"},{"TeleportLinkID","4"},{"LiftObjectLinkID","7"}};
        auto ao=Compile(add,big);auto at=Table(ao.instance,0xd9b247dfu,big);
        Require(Word(ao.instance,Field(ao.instance,at,0xddccddf3u,big),big)==7,"pickup lift link");
        Require(Word(ao.instance,InheritedField(ao.instance,at,0xfe5dbc0bu,big),big)==4,"pickup store link");
        GameObjectChild fling;fling.name="FlingStoredObjectsSpecialPower";
        fling.attributes={{"TypeId","315751129"},{"id","Throw"},{"SpecialPowerTemplate","Power\\6"},{"StoreObjectsLinkID","4"},{"LiftObjectLinkID","7"}};
        const char* sources[]={"SovietAntiInfantryInfantry","JapanCommando","CivilianBarrel"};
        const char* replacements[]={"FlingConscriptProxy","FlingCommandoProxy","FlingBarrelProxy"};
        for(unsigned i=0;i<3;++i) {GameObjectChild entry;entry.name="ObjectReplacement";entry.attributes={{"Source",sources[i]},{"Replacement",replacements[i]}};fling.children.push_back(entry);}
        auto fo=Compile(fling,big);auto ft=Table(fo.instance,0x12d1fad9u,big);
        Require(Word(fo.instance,Field(fo.instance,ft,0xfee927fcu,big),big)==4,"throw store link");
        Require(Word(fo.instance,Field(fo.instance,ft,0xddccddf3u,big),big)==7,"throw lift link");
        Require(Number(fo.instance,Field(fo.instance,ft,0x7b773fb9u,big),big)==300,"native maximum speed default");
        Require(Number(fo.instance,Field(fo.instance,ft,0x80503b2du,big),big)==100,"native minimum speed default");
        auto mappings=Field(fo.instance,ft,0x891c564bu,big);
        Require(Word(fo.instance,Word(fo.instance,mappings+8,big),big)==3,"replacement map count");
        for(unsigned i=0;i<3;++i) {
            auto pair=Word(fo.instance,mappings+12*(i+1)+8,big);
            Require(Word(fo.instance,Field(fo.instance,pair,0xd4edc301u,big),big)==HashAssetId(sources[i]),"replacement source weak ID");
            Require(Word(fo.instance,Field(fo.instance,pair,0xb955f215u,big),big)==HashAssetId(replacements[i]),"replacement target weak ID");
        }
        fling.children.clear();auto noMap=Compile(fling,big);auto nt=Table(noMap.instance,0x12d1fad9u,big);
        auto emptyMap=Field(noMap.instance,nt,0x891c564bu,big);
        Require(Word(noMap.instance,Word(noMap.instance,emptyMap+8,big),big)==0,"optional map default empty");
        GameObjectChild lift;lift.name="YurikoLiftObjectUpdate";
        lift.attributes={{"TypeId","2862485718"},{"id","Lift"},{"LiftObjectLinkID","7"},{"LiftVelocity","8"}};
        auto lo=Compile(lift,big);auto lt=Table(lo.instance,0xaa9e10d6u,big);
        Require(Word(lo.instance,Field(lo.instance,lt,0xddccddf3u,big),big)==7,"derived lift link");
        Require(Number(lo.instance,InheritedField(lo.instance,lt,0x38026647u,big),big)==8,"native LiftVelocity inherited");
        Require(GameObjectModuleNodeKind(293,"RequirementsFilterMP/IncludeThing")==3,"pickup inherited XML filter route");
        Require(GameObjectModuleNodeKind(294,"RequirementsFilterMP/IncludeThing")==3,"throw inherited XML filter route");
        Require(GameObjectModuleNodeKind(294,"ObjectReplacement")==2,"replacement XML reader route");
        GameObjectChild collide;collide.name="DamageDynamicsCollide";
        collide.attributes={{"TypeId","431960881"},{"id","Impact"}};
        GameObjectChild impact;impact.name="DamageNugget";
        impact.attributes={{"Damage","250"},{"DamageType","CRUSH"},{"DeathType","NORMAL"},
            {"Radius","12"},{"UnderAttackOverrideEvaEvent","Eva\\9"},{"VictimShroudRevealer","Revealer\\11"}};
        GameObjectChild filter;filter.name="SpecialObjectFilter";filter.attributes={{"Rule","ANY"},{"Include","INFANTRY"}};
        GameObjectChild include;include.name="IncludeThing";include.text="Infantry";filter.children.push_back(include);impact.children.push_back(filter);
        GameObjectChild required;required.name="RequiredUpgrade";required.text="CollisionUpgrade";impact.children.push_back(required);
        GameObjectChild scalar;scalar.name="DamageScalarDetails";scalar.attributes={{"Scalar","150%"}};
        filter.name="Filter";scalar.children.push_back(filter);scalar.children.push_back(required);impact.children.push_back(scalar);
        collide.children.push_back(impact);
        impact.attributes={{"Damage","0"},{"OnlyKillOwnerWhenTriggered","true"},{"DeathType","NORMAL"}};
        impact.children.clear();collide.children.push_back(impact);
        auto co=Compile(collide,big);auto ct=Table(co.instance,0x19bf3331u,big);
        Require(Number(co.instance,Field(co.instance,ct,0x00f63ca4u,big),big)==10,"native full-damage speed default");
        auto damageList=Field(co.instance,ct,0xf4948415u,big);
        Require(Word(co.instance,Word(co.instance,damageList+8,big),big)==2,"inline damage count");
        auto first=Word(co.instance,damageList+20,big),second=Word(co.instance,damageList+32,big);
        Require(Word(co.instance,first,big)==1,"damage entry has direct fields without polymorphic wrapper");
        auto firstStart=InheritedField(co.instance,first,430981792u,big),secondStart=InheritedField(co.instance,second,430981792u,big);
        Require(secondStart-firstStart==152,"RA3 damage entries are contiguous 152-byte values");
        Require(Word(co.instance,firstStart,big)==0xc0f53e99u,"inline damage type");
        Require(Number(co.instance,Field(co.instance,first,0x7c33e4e9u,big),big)==250,"impact damage");
        Require(Number(co.instance,InheritedField(co.instance,first,0x4f31c6c5u,big),big)==12,"inherited nugget radius");
        Require(Word(co.instance,Field(co.instance,first,4203588415u,big),big)==10,"inline EVA strong reference");
        Require(Word(co.instance,Field(co.instance,first,771372620u,big),big)==12,"inline revealer strong reference");
        auto scalarList=Field(co.instance,first,2442600653u,big);
        Require(Word(co.instance,Word(co.instance,scalarList+8,big),big)==1,"nested damage-scalar count");
        auto scalarEntry=Word(co.instance,scalarList+20,big);
        Require(Number(co.instance,Field(co.instance,scalarEntry,1556264584u,big),big)==1.5f,"nested percent scalar");
        auto upgrades=Field(co.instance,scalarEntry,2138150697u,big);
        Require(Word(co.instance,Word(co.instance,upgrades+8,big),big)==1,"nested scalar upgrade count");
        Require(Word(co.instance,Word(co.instance,upgrades+20,big),big)==HashAssetId("CollisionUpgrade"),"nested scalar weak upgrade ID");
        Require(co.instance[Field(co.instance,second,1874552506u,big)]==1,"self-kill flag from shared damage definition");
        Require(GameObjectModuleNodeKind(297,"DamageNugget/DamageScalarDetails/Filter/IncludeThing")==3,"nested damage filter XML path");
        collide.attributes["FullDamageSpeed"]="25";auto overrideSpeed=Compile(collide,big);auto ot=Table(overrideSpeed.instance,0x19bf3331u,big);
        Require(Number(overrideSpeed.instance,Field(overrideSpeed.instance,ot,0x00f63ca4u,big),big)==25,"full-damage speed override");

    }
    std::cout<<"Yuriko slam: pickup/store/lift links, native speed defaults, three source/proxy mappings, inherited LiftVelocity, inline collision damage with nested filters/scalars and LE/BE passed\n";return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}}
