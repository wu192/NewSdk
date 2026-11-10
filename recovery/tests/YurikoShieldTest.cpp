#include "YurikoModuleTestSupport.h"
int main() {try {
    for(bool big:{false,true}) {
        GameObjectChild module;module.name="YurikoShieldSphereUpdate";
        module.attributes={{"TypeId","2513982902"},{"id","YurikoShield"},{"RadiusMax","90"},
            {"ReflectionFX","Reflect\\17"},{"AlternateReflectionFX","Alternate\\23"},
            {"AlternateReflectionDamageTypes","GUN CANNON"}};
        GameObjectChild filter;filter.name="ObjectFilter";filter.attributes={{"Rule","ANY"},{"Include","INFANTRY"}};
        module.children.push_back(filter);
        auto output=Compile(module,big);const auto& b=output.instance;auto table=Table(b,0x95d855b6u,big);
        Require(Word(b,Field(b,table,0x945463f7u,big),big)==18,"reflection FX one-based reference");
        Require(Word(b,Field(b,table,0x92285c9fu,big),big)==24,"alternate reflection FX one-based reference");
        auto mask=Field(b,table,0xd9086106u,big);
        Require(Word(b,mask,big)==((1u<<21)|(1u<<22)) && Word(b,mask+4,big)==0,"64-bit damage mask");
        Require(Number(b,InheritedField(b,table,0xe28d5e3bu,big),big)==90,"inherited shield radius");
        Require(GameObjectModuleNodeKind(295,"ObjectFilter/IncludeThing")==3,"schema reader inherits filter path");
        module.attributes.erase("ReflectionFX");module.attributes.erase("AlternateReflectionFX");module.attributes.erase("AlternateReflectionDamageTypes");
        auto defaults=Compile(module,big);auto dt=Table(defaults.instance,0x95d855b6u,big);
        Require(Word(defaults.instance,Field(defaults.instance,dt,0x945463f7u,big),big)==0,"optional FX default null");
        Require(Word(defaults.instance,Field(defaults.instance,dt,0xd9086106u,big),big)==0,"optional mask default empty");
    }
    std::cout<<"Yuriko shield: native tokens, reference indices, 64-bit mask, inherited radius and LE/BE defaults passed\n";
    return 0;
} catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}}
