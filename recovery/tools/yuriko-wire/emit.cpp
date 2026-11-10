#include "GameObject.h"
#include <fstream>
#include <iostream>
using namespace Recovered::Native;
GameObjectChild make(const char* name,unsigned type,AttributeMap attrs={}) {GameObjectChild m;m.name=name;m.attributes=std::move(attrs);m.attributes["TypeId"]=std::to_string(type);m.attributes["id"]=name;return m;}
void save(const char* directory,const char* name,GameObjectChild module) {GameObjectChild all;all.name="Behaviors";all.children.push_back(std::move(module));auto c=CompileGameObject({}, {all}, false);std::ofstream f(std::string(directory)+"/"+name+".bin",std::ios::binary);f.write((char*)c.instance.data(),c.instance.size());std::cout<<name<<" "<<c.instance.size()<<"\n";}
int main(int argc,char** argv) {
 std::string group=argv[2];
 if(group=="domination") {
  auto m=make("LureObjectsUpdate",0xb65ab72au,{{"LureObjectLinkID","7"},{"MaxRangeSquared","22500"},{"GuardRange","99"},{"UpdateRate","12"},{"TargetMarkerObjectRef","Marker"},{"GuardStatus","UNSELECTABLE"},{"DisabledTypesToProcess","HELD EMP"}});
  for(unsigned i=0;i<3;++i) {auto o=make("GuardOffset",0,{{"x",std::to_string(i*10)},{"y","20"},{"z","3"}});m.children.push_back(o);}save(argv[1],"lure",m);
  save(argv[1],"add-lure",make("AddObjectsToLureUpdateSpecialPower",0xa7deffccu,{{"TeleportLinkID","7"},{"SpecialPowerTemplate","Power\\5"}}));
 } else if(group=="slam") {
  save(argv[1],"add-lift",make("AddObjectsToLiftUpdateSpecialPower",0xd9b247dfu,{{"TeleportLinkID","4"},{"LiftObjectLinkID","7"},{"SpecialPowerTemplate","Power\\5"}}));
  auto m=make("FlingStoredObjectsSpecialPower",0x12d1fad9u,{{"StoreObjectsLinkID","4"},{"LiftObjectLinkID","7"},{"FlingSpeedMax","420"},{"FlingSpeedMin","180"},{"SpecialPowerTemplate","Power\\5"}});
  for(unsigned i=0;i<3;++i)m.children.push_back(make("ObjectReplacement",0,{{"Source","Source"+std::to_string(i)},{"Replacement","Proxy"+std::to_string(i)}}));save(argv[1],"fling",m);
  save(argv[1],"lift",make("YurikoLiftObjectUpdate",0xaa9e10d6u,{{"LiftObjectLinkID","7"},{"LiftVelocity","8"}}));
  auto c=make("DamageDynamicsCollide",0x19bf3331u,{{"FullDamageSpeed","25"}});
  c.children.push_back(make("DamageNugget",0xc0f53e99u,{{"Damage","250"},{"DamageType","CRUSH"},{"DeathType","NORMAL"}}));
  c.children.push_back(make("DamageNugget",0xc0f53e99u,{{"OnlyKillOwnerWhenTriggered","true"},{"DeathType","NORMAL"}}));save(argv[1],"collision",c);
 } else {
  auto m=make("YurikoShieldSphereUpdate",0x95d855b6u,{{"ReflectionFX","FX\\17"},{"AlternateReflectionFX","FX\\23"},{"AlternateReflectionDamageTypes","GUN CANNON"},{"RadiusMax","90"}});m.children.push_back(make("ObjectFilter",0,{{"Rule","ANY"},{"Include","INFANTRY"}}));save(argv[1],"shield",m);
 }
}
