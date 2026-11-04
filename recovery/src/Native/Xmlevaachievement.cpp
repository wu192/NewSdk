#include "Xmlevaachievement.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace EvaAchievementDetail {
using Relo::U32;using Node=XmlevaachievementNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

std::string Value(const Node& n,const char* a,const char* fallback=""){auto i=n.attributes.find(a);return i==n.attributes.end()?fallback:i->second;}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder{
 Relo::Tracker tracker;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Real(void* p,float v){std::memcpy(p,&v,4);words.push_back(p);}
 void Ref(const Node& n,const char* a,unsigned char* p){auto i=n.attributes.find(a);if(i!=n.attributes.end())MarshalReference(tracker,p,i->second);}
 void Time(const Node& n,const char* a,unsigned char* p,const char* d=""){auto i=n.attributes.find(a);if(i!=n.attributes.end()||*d)Real(p,InitializeTime(Value(n,a,d).c_str()));}
 void Unsigned(const Node& n,const char* a,unsigned char* p){auto i=n.attributes.find(a);if(i!=n.attributes.end())Word(p,ParseUnsigned(i->second));}
 void Signed(const Node& n,const char* a,unsigned char* p){auto i=n.attributes.find(a);if(i!=n.attributes.end())Word(p,ParseSigned(i->second));}
 void Bool(const Node& n,const char* a,unsigned char* p,const char* d){*p=ParseBoolean(Value(n,a,d));}
 void Enum(const Node& n,const char* a,unsigned char* p,const char* const* names,const int* values,int count,int fallback,const char* d=nullptr){auto i=n.attributes.find(a);if(i==n.attributes.end()&&!d)return;const char* s=i==n.attributes.end()?d:i->second.c_str();int v=fallback;for(int j=0;j<count;++j)if(std::strcmp(s,names[j])==0){v=values[j];break;}Word(p,static_cast<U32>(v));}
 void List(const Node& n,const char* name,unsigned char* root,unsigned offset,unsigned size){
 U32 count=0;for(const auto& c:n.children)if(c.name==name)++count;Word(root+offset,count);if(!count)return;
 auto items=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+offset+4),size,count));unsigned i=0;
 for(const auto& c:n.children)if(c.name==name){auto p=items+size*i++;
 if(c.name=="SideSound"){Ref(c,"Sound",p);auto a=c.attributes.find("Side");if(a!=c.attributes.end())Word(p+4,HashAssetId(a->second));}
 else if(c.name=="OtherEvaEventToBlock"){Word(p,HashAssetId(c.value));}
 else if(c.name=="AIBeaconTypeSettings"){const char* names[]={"Assault","HoldObject","HoldPosition","Invalid"};const int values[]={0,2,1,-1};Enum(c,"BeaconType",p,names,values,4,0);Ref(c,"PlacedEvent",p+4);}
 else {Unsigned(c,"TargetCount",p);const char* names[]={"AWARD_ACHIEVEMENT_AFTER_POST_GAME_SHELL","AWARD_ACHIEVEMENT_BEFORE_POST_GAME_SHELL","AWARD_ACHIEVEMENT_IMMEDIATE","TRIGGER_EVENT"};const int values[]={3,2,1,0};Enum(c,"Type",p+4,names,values,4,3);Unsigned(c,"ActionObjId",p+8);}
 }tracker.Pop();
 }
};
}
unsigned int XmlevaachievementTypeId(const std::string& name){
 if(name=="SiegeEvaEvent")return 1268003905u;
 if(name=="EvaSettings")return 2361666546u;
 if(name=="EvaEvent")return 357806494u;
 if(name=="MiscEvaEvents")return 1807540972u;
 if(name=="AchievementEvent")return 4230032093u;
 if(name=="MapNameToAchievementEventMapping")return 3594283852u;
 if(name=="GameplayAchievementTriggerData")return 348670903u;
 return 0;
}
Relo::Chunk CompileXmlevaachievement(const XmlevaachievementNode& input,bool bigEndian){
 using namespace EvaAchievementDetail;static_assert(sizeof(void*)==4,"Eva/achievement requires x86.");
 Check(XmlevaachievementTypeId(input.name)!=0,"Unsupported Eva/achievement root.");Builder b;unsigned char* root=nullptr;unsigned size=input.name=="EvaSettings"?20:input.name=="EvaEvent"?44:input.name=="MiscEvaEvents"?164:input.name=="GameplayAchievementTriggerData"?12:16;
 b.tracker.Push(reinterpret_cast<void**>(&root),size,1);
 if(input.name=="SiegeEvaEvent"){b.Time(input,"Seconds",root+4);b.Ref(input,"EvaEvent",root+8);const char* names[]={"ALL","GENERIC","SIEGE"};const int values[]={2,1,0};b.Enum(input,"EventType",root+12,names,values,3,2,"ALL");}
 else if(input.name=="EvaSettings"){b.Signed(input,"EnemySightedMaxVoicePositionScanRange",root+4);b.Time(input,"MaxTimeToKeepJumpToEvents",root+8);b.Time(input,"MaxTimeBeforeResettingLastJumpTo",root+12);b.Signed(input,"MinDistanceBetweenJumpToEvents",root+16);}
 else if(input.name=="EvaEvent"){b.Unsigned(input,"Priority",root+4);b.Time(input,"TimeBetweenEvents",root+8,"0s");b.Time(input,"ExpirationTime",root+12,"2s");b.Bool(input,"AlwaysPlayFromHomeBase",root+40,"false");b.Bool(input,"CountAsJumpToLocation",root+41,"true");b.Time(input,"QuietTime",root+16,"0s");b.Time(input,"TimeToWaitBeforePlaying",root+20,"0s");b.Bool(input,"SuppressOn360",root+42,"false");b.Bool(input,"SuppressOnPC",root+43,"false");b.List(input,"SideSound",root,24,8);b.List(input,"OtherEvaEventToBlock",root,32,4);}
 else if(input.name=="MiscEvaEvents"){
 b.Ref(input,"LowPower",root+4);
 b.Ref(input,"SilosNeeded",root+8);
 b.Ref(input,"BuildingStolen",root+12);
 b.Ref(input,"IncomingRadarTransmission",root+16);
 b.Ref(input,"AllyDefeated",root+20);
 b.Ref(input,"EnemyDefeated",root+24);
 b.Ref(input,"LocalPlayerDefeated",root+28);
 b.Ref(input,"LocalPlayerVictorious",root+32);
 b.Ref(input,"CannotBuildDueToCPLimit",root+36);
 b.Ref(input,"BuildQueuePausedDueToCPLimit",root+40);
 b.Ref(input,"BuildQueuePausedDueToFunds",root+44);
 b.Ref(input,"CannotBuildDueToFunds",root+48);
 b.Ref(input,"CannotRepairDueToFunds",root+52);
 b.Ref(input,"CannotUsePowerDueToFunds",root+56);
 b.Ref(input,"TiberiumFieldDepleted",root+60);
 b.Ref(input,"NewMissionObjective",root+64);
 b.Ref(input,"CompletedMissionObjective",root+68);
 b.Ref(input,"NewBonusObjective",root+72);
 b.Ref(input,"CompletedBonusMissionObjective",root+76);
 b.Ref(input,"IntelDatabaseEntryReceived",root+80);
 b.Ref(input,"UpgradeComplete",root+84);
 b.Ref(input,"BuildingBeingStolen",root+88);
 b.Ref(input,"BeaconPlacedPlayer",root+92);
 b.Ref(input,"BeaconPlacedAlly",root+96);
 b.Ref(input,"ObservedPlayerDefeated",root+100);
 b.Ref(input,"WallsBeingClimbed",root+104);
 b.Ref(input,"StructureInfiltrated",root+108);
 b.Ref(input,"ControlPointCaptured",root+112);
 b.Ref(input,"ControlPointLost",root+116);
 b.Ref(input,"ControlPointHasBeenCaptured",root+120);
 b.Ref(input,"CTFFlagCaptured",root+124);
 b.Ref(input,"CTFEnemyCapturedFlag",root+128);
 b.Ref(input,"CTFFlagDropped",root+132);
 b.Ref(input,"CTFEnemyDroppedFlag",root+136);
 b.Ref(input,"CTFYouHaveFlag",root+140);
 b.Ref(input,"CTFEnemyHasFlag",root+144);
 b.Ref(input,"SiegeBarrierDisengaged",root+148);
 b.Ref(input,"SiegeBarrierEngaged",root+152);
 b.List(input,"AIBeaconTypeSettings",root,156,8);}
 else if(input.name=="AchievementEvent"){b.Unsigned(input,"EventId",root+4);b.List(input,"Action",root,8,12);}
 else if(input.name=="MapNameToAchievementEventMapping"){auto a=input.attributes.find("MapName");if(a!=input.attributes.end()){MarshalString(b.tracker,root+4,a->second);b.words.push_back(root+4);}b.Unsigned(input,"AchievementEventId",root+12);}
 else{b.Ref(input,"GameplayConditionData",root+4);b.Unsigned(input,"AchievementEventId",root+8);}
 if(bigEndian)for(void* p:b.words)Swap(p);return b.tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
