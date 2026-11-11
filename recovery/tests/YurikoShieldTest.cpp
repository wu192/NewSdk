#include "YurikoModuleTestSupport.h"
#include "EnumBitset.h"
#include <array>
int main() {try {
    for(bool big:{false,true}) {
        GameObjectChild module;module.name="YurikoShieldSphereUpdate";
        module.attributes={{"TypeId","2513982902"},{"id","YurikoShield"},{"RadiusMax","90"},
            {"ReflectionFX","Reflect\\17"},{"AlternateReflectionFX","Alternate\\23"},
            {"AlternateReflectionDamageTypes","GUN CANNON"},
            {"InterceptedProjectileObjectStatus","FOLLOWING_THROUGH LEASHED_RETURNING ANTI_GARRISON"}};
        GameObjectChild filter;filter.name="ObjectFilter";filter.attributes={{"Rule","ANY"},{"Include","INFANTRY"}};
        module.children.push_back(filter);
        auto output=Compile(module,big);const auto& b=output.instance;auto table=Table(b,0x95d855b6u,big);
        Require(Word(b,Field(b,table,0x945463f7u,big),big)==18,"reflection FX one-based reference");
        Require(Word(b,Field(b,table,0x92285c9fu,big),big)==24,"alternate reflection FX one-based reference");
        auto mask=Field(b,table,0xd9086106u,big);
        Require(Word(b,mask,big)==((1u<<21)|(1u<<22)) && Word(b,mask+4,big)==0,"64-bit damage mask");
        auto status=Field(b,table,0x91f93de7u,big);
        Require(Word(b,status,big)==0x80000000u && Word(b,status+4,big)==1,"projectile statuses cross DWORD boundary");
        for(unsigned i=2;i<6;++i) Require(Word(b,status+4*i,big)==0,"projectile status middle words stay clear");
        Require(Word(b,status+24,big)==0x40000000u,"last supported RA3 status bit222");
        bool statusType=false;
        for(unsigned at=table;Word(b,at,big) || Word(b,at+4,big);at+=12)
            if(Word(b,at,big)==0x91f93de7u)statusType=Word(b,at+4,big)==0xa7bd811eu;
        Require(statusType,"native projectile ObjectStatus token type");
        Require(Number(b,InheritedField(b,table,0xe28d5e3bu,big),big)==90,"inherited shield radius");
        Require(GameObjectModuleNodeKind(295,"ObjectFilter/IncludeThing")==3,"schema reader inherits filter path");
        module.attributes.erase("ReflectionFX");module.attributes.erase("AlternateReflectionFX");module.attributes.erase("AlternateReflectionDamageTypes");
        module.attributes.erase("InterceptedProjectileObjectStatus");
        auto defaults=Compile(module,big);auto dt=Table(defaults.instance,0x95d855b6u,big);
        Require(Word(defaults.instance,Field(defaults.instance,dt,0x945463f7u,big),big)==0,"optional FX default null");
        Require(Word(defaults.instance,Field(defaults.instance,dt,0xd9086106u,big),big)==0,"optional mask default empty");
        auto emptyStatus=Field(defaults.instance,dt,0x91f93de7u,big);
        for(unsigned i=0;i<7;++i)Require(Word(defaults.instance,emptyStatus+4*i,big)==0,"optional projectile status defaults empty");
    }
    // The shared RA3 marshaler writes seven words even for ALL, including its
    // native padding bit223; the adjacent eighth word must never be touched.
    std::array<U32,8> boundary{};boundary[7]=0xa5a55a5au;
    MarshalObjectStatus("ALL",boundary.data());
    for(unsigned i=0;i<7;++i)Require(boundary[i]==0xffffffffu,"ALL fills all seven RA3 status words");
    Require(boundary[7]==0xa5a55a5au,"RA3 status writer never writes eighth word");
    std::cout<<"Yuriko shield: native tokens, reference indices, damage/status masks, inherited radius and LE/BE defaults passed\n";
    return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}}
