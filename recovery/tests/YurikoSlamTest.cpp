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
    }
    std::cout<<"Yuriko slam: pickup/store/lift links, native speed defaults, three source/proxy mappings, inherited LiftVelocity and LE/BE passed\n";return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}}
