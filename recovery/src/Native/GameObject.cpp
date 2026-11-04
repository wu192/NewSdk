#include "GameObject.h"
#include "GameObjectChildGroups.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include "ObjectFilter.h"
#include "WeaponSlotOrder.h"
#include <cstring>
#include <stdexcept>
#include <algorithm>
#include <cmath>
#include <unordered_map>
#include <type_traits>
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native {
namespace {
using U32=std::uint32_t;
enum class GameObjectTokenKind { Member, List, Null };
struct GameObjectEnum {const char* field; const char* name; int value;};
struct GameObjectEnumFallback {const char* field; int value;};
struct GameObjectToken { U32 name,type,offset; GameObjectTokenKind kind; };
enum class LeafValue { Float, Percent, Unsigned, Boolean, Time, Enum, String, Reference, AssetId };
struct LeafField {const char* name;unsigned offset;LeafValue kind;const char* fallback;};
struct LeafToken {U32 name,type,offset;};
struct LeafLayout {const char* name;unsigned offset,size;const LeafField* fields;unsigned count;const LeafToken* tokens;};
enum class AIValue { Float, Signed, Boolean, Time, Angle, Reference, Enum, Bits, NullableFloat, NullableSigned, NullableBoolean, NullableReference, NullableBits };
struct AIEnum {const EnumNameValue* names;unsigned count;int unknown;unsigned bits;};
struct AIField {const char* name;unsigned offset;AIValue kind;const char* fallback;const AIEnum* values;};
struct AILayout {unsigned size;const AIField* fields;unsigned count;const LeafToken* tokens;unsigned tokenCount;};
struct ModuleLayout;
struct ModuleToken {U32 name,type,offset;const ModuleLayout* nested;unsigned mode,stride;};
struct ModuleLayout {const ModuleToken* tokens;unsigned count;};
struct ModuleChoice {U32 type;const ModuleLayout* tokens;unsigned size,kind;};
enum class ModuleValue {Float,Percent,Time,String,Reference,NullableReference,Boolean,Enum,Unsigned,Short,NullablePositiveFloat,NullableModelCondition,ModelCondition,AssetId,SignedList,NullableSigned,NullableObjectStatus,ObjectStatus,Duration,StringList,Angle,DeathBits,Signed,NullableFloat,FloatList,ReplaceOptions,NullableTime,NullableBits,NullableWideBits,FrameSpeed,Bits,NullableAssetId,StringHash,NullableStringHash,Byte};
U32 ExactStringHash(const std::string& text);
struct ModuleField {
    const char* name;unsigned offset;ModuleValue kind;const char* fallback;const AIEnum* values;
    unsigned scalarSize;U32 defaultWord;
    ModuleField(const char* name,unsigned offset,ModuleValue kind,const char* fallback,const AIEnum* values);
};
static_assert(std::is_trivially_destructible<ModuleField>::value,"Decoded metadata must not register CRT exit destructors");
struct ModuleFieldSet {const ModuleField* fields;unsigned count;};
struct ModuleChildName {const char* parent;const char* child;};
#include "GameObjectFields.h"
#include "GameObjectSoundSelectors.h"
#include "GameObjectLifecycle.h"
#include "GameObjectDie.h"
#include "GameObjectUpgrade.h"
#include "GameObjectUpgradeRemaining.h"
#include "GameObjectProduction.h"
#include "GameObjectCreate.h"
#include "GameObjectSlaved.h"
#include "GameObjectTechReceiver.h"
#include "GameObjectVision.h"
#include "GameObjectWeaponBehavior.h"
#include "GameObjectBeamState.h"
#include "GameObjectMotion.h"
#include "GameObjectFlight.h"
#include "GameObjectReactive.h"
#include "GameObjectWeaponSet.h"
#include "GameObjectHealing.h"
#include "GameObjectCrate.h"
#include "GameObjectAIUpdate.h"
#include "GameObjectEconomy.h"
#include "GameObjectAssist.h"
#include "GameObjectSpawnUpdate.h"
#include "GameObjectSlaveWatcher.h"
#include "GameObjectQueueParking.h"
#include "GameObjectBuildingStructure.h"
#include "GameObjectEconomyPower.h"
#include "GameObjectStructureWall.h"
#include "GameObjectCombatResponse.h"
#include "GameObjectStatusEffects.h"
#include "GameObjectStealthVision.h"
#include "GameObjectTiberium.h"
#include "GameObjectWallEnclose.h"
#include "GameObjectResourceAllies.h"
#include "GameObjectStateEvent.h"
#include "GameObjectModelCondition.h"
#include "GameObjectMotionCleanup.h"
#include "GameObjectSpecialPowerBase.h"
#include "GameObjectSpecialPowerDerived.h"
#include "GameObjectPowerRouting.h"
#include "GameObjectSpecialAbility.h"
#include "GameObjectAbilityWeapon.h"
#include "GameObjectPowerCreation.h"
#include "GameObjectAbilityMovement.h"
#include "GameObjectAbilityState.h"
#include "GameObjectAbilityRouting.h"
#include "GameObjectAbilityResource.h"
#include "GameObjectAbilityToggle.h"
#include "GameObjectAbilityFinish.h"
#include "GameObjectAttach.h"
#include "GameObjectUtility.h"
#include "GameObjectArea.h"
#include "GameObjectSupport.h"
#include "GameObjectObjectControl.h"
#include "GameObjectSlowDeath.h"
#include "GameObjectContainCore.h"
#include "GameObjectContainRemaining.h"
#include "GameObjectDrawBeam.h"
extern const ModuleLayout drawMainW3DScriptedModelDrawModuleData;
#include "GameObjectDrawModelConditions.h"
#include "GameObjectDrawAnimation.h"
#include "GameObjectDrawDynamics.h"
#include "GameObjectDrawModel.h"
struct ReloString {U32 length;void* data;};
static_assert(sizeof(ReloString)==8, "GameObject strings require x86");
struct NativeList {U32 count;void* data;};
static_assert(sizeof(NativeList)==8, "GameObject lists require x86");
struct Procedure { U32 name,type; void* offset; };
static_assert(sizeof(Procedure)==12, "GameObject requires x86");
U32 EnumValue(const std::string& field,const std::string& text) {
    for(const auto& e:enumEntries) if(field==e.field && text==e.name) return static_cast<U32>(e.value);
    for(const auto& e:enumFallbacks) if(field==e.field) return static_cast<U32>(e.value);
    throw std::logic_error("Unknown GameObject enum field");
}
void Swap(void* p) { auto b=static_cast<unsigned char*>(p); std::swap(b[0],b[3]);std::swap(b[1],b[2]); }
// The four layouts form one complete AI dependency group. They share the
// existing scalar, bitset, resource and ObjectFilter implementations.
bool NullableAI(AIValue kind) {
    return kind==AIValue::NullableFloat || kind==AIValue::NullableSigned || kind==AIValue::NullableBoolean || kind==AIValue::NullableReference || kind==AIValue::NullableBits;
}
void MarshalAI(const GameObjectChild& input,unsigned char* output,unsigned layoutIndex,Relo::Tracker& data) {
    const auto& layout=aiLayouts[layoutIndex];
    for(unsigned i=0;i<layout.count;++i) {
        const auto& f=layout.fields[i];auto found=input.attributes.find(f.name);
        if(found==input.attributes.end() && !f.fallback) continue;
        std::string value=found==input.attributes.end()?f.fallback:found->second;
        auto at=output+f.offset;bool nullable=NullableAI(f.kind);
        if(nullable) at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(at),f.kind==AIValue::NullableBoolean?1:4,1));
        U32 word=0;float number=0;
        switch(f.kind) {
        case AIValue::NullableBoolean:case AIValue::Boolean:*at=ParseBoolean(value)?1:0;break;
        case AIValue::NullableFloat:case AIValue::Float:number=ParseFloat(value);std::memcpy(at,&number,4);break;
        case AIValue::Time:number=InitializeTime(value.c_str());std::memcpy(at,&number,4);break;
        case AIValue::Angle:number=InitializeAngle(value.c_str());std::memcpy(at,&number,4);break;
        case AIValue::NullableSigned:case AIValue::Signed:word=static_cast<U32>(ParseSigned(value));std::memcpy(at,&word,4);break;
        case AIValue::NullableReference:case AIValue::Reference:MarshalReference(data,at,value);break;
        case AIValue::NullableBits:case AIValue::Bits:MarshalEnumBitset(value,f.values->names,f.values->count,f.values->unknown,f.values->bits,reinterpret_cast<U32*>(at));break;
        case AIValue::Enum:
            word=static_cast<U32>(f.values->unknown);
            for(unsigned j=0;j<f.values->count;++j) if(value==f.values->names[j].name) {word=static_cast<U32>(f.values->names[j].value);break;}
            std::memcpy(at,&word,4);break;
        }
        if(nullable) data.Pop();
    }
    auto filter=[&](const GameObjectChild& child,unsigned offset) {
        ObjectFilterInput in;in.attributes=child.attributes;
        for(const auto& thing:child.children) if(thing.name=="IncludeThing") in.includeThings.push_back(thing.text);else in.excludeThings.push_back(thing.text);
        auto target=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+offset),112,1));MarshalObjectFilter(in,target,data);data.Pop();
    };
    if(layoutIndex==0) {
        auto list=reinterpret_cast<NativeList*>(output+60);
        for(const auto& child:input.children) if(child.name=="AIContextualPower") ++list->count;
        if(list->count) {
            auto items=static_cast<unsigned char*>(data.Push(&list->data,172,list->count));unsigned index=0;
            for(const auto& child:input.children) if(child.name=="AIContextualPower") MarshalAI(child,items+172*index++,1,data);
            data.Pop();
        }
        const char* names[]={"ConstructionBaseSelectionCriteria","ResourceNodeBaseClosenessTestSelectionCriteria"};
        for(unsigned i=0;i<2;++i) for(const auto& child:input.children) if(child.name==names[i]) {
            auto target=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+68+4*i),20,1));MarshalAI(child,target,2,data);data.Pop();
        }
        for(const auto& child:input.children) if(child.name=="DodgeWithUnits") filter(child,76);
    } else if(layoutIndex==1) {
        for(const auto& child:input.children) if(child.name=="ObjectFilter") filter(child,156);
        for(const auto& child:input.children) if(child.name=="SpecialPowerWeapon") {
            auto target=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+160),28,1));MarshalAI(child,target,3,data);data.Pop();
        }
    }
}
void TokenizeAI(unsigned char* input,unsigned layoutIndex,void** target,Relo::Tracker& procedures) {
    if(!input) return;
    const auto& layout=aiLayouts[layoutIndex];
    auto table=static_cast<Procedure*>(procedures.Push(target,12,layout.tokenCount+1));
    for(unsigned i=0;i<layout.tokenCount;++i) {
        const auto& t=layout.tokens[i];table[i].name=t.name;table[i].type=t.type;
        if(i<layout.count) {
            auto at=input+t.offset;if(NullableAI(layout.fields[i].kind)) at=*reinterpret_cast<unsigned char**>(at);
            if(at) procedures.BookmarkMemberOffset(at,&table[i].offset);
        } else if(layoutIndex==0 && t.offset==60) {
            auto list=reinterpret_cast<NativeList*>(input+60);
            auto items=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,list->count+1));
            items[0].name=1820487795u;items[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&items[0].offset);
            for(U32 j=0;j<list->count;++j) {items[j+1].name=2974916843u;TokenizeAI(static_cast<unsigned char*>(list->data)+172*j,1,&items[j+1].offset,procedures);}
            procedures.Pop();
        } else if(layoutIndex==0 && (t.offset==68 || t.offset==72)) TokenizeAI(*reinterpret_cast<unsigned char**>(input+t.offset),2,&table[i].offset,procedures);
        else if(layoutIndex==1 && t.offset==160) TokenizeAI(*reinterpret_cast<unsigned char**>(input+160),3,&table[i].offset,procedures);
        else {auto filter=*reinterpret_cast<ObjectFilter**>(input+t.offset);if(filter) TokenizeObjectFilter(filter,&table[i].offset,procedures);}
    }
    procedures.Pop();
}
void EndianAI(unsigned char* input,unsigned layoutIndex) {
    if(!input) return;
    const auto& layout=aiLayouts[layoutIndex];
    for(unsigned i=0;i<layout.count;++i) {
        const auto& f=layout.fields[i];auto at=input+f.offset;
        if(NullableAI(f.kind)) at=*reinterpret_cast<unsigned char**>(at);
        if(!at || f.kind==AIValue::Boolean || f.kind==AIValue::NullableBoolean || f.kind==AIValue::Reference || f.kind==AIValue::NullableReference) continue;
        unsigned words=f.values && f.values->bits?(f.values->bits+31)/32:1;
        for(unsigned j=0;j<words;++j) Swap(at+4*j);
    }
    if(layoutIndex==0) {
        auto list=reinterpret_cast<NativeList*>(input+60);
        for(U32 j=0;j<list->count;++j) EndianAI(static_cast<unsigned char*>(list->data)+172*j,1);
        Swap(&list->count);
        for(unsigned offset=68;offset<=72;offset+=4) EndianAI(*reinterpret_cast<unsigned char**>(input+offset),2);
        auto filter=*reinterpret_cast<ObjectFilter**>(input+76);if(filter) EndianObjectFilter(filter);
    } else if(layoutIndex==1) {
        auto filter=*reinterpret_cast<ObjectFilter**>(input+156);if(filter) EndianObjectFilter(filter);
        EndianAI(*reinterpret_cast<unsigned char**>(input+160),3);
    }
}

U32 ExactStringHash(const std::string& text);
void MarshalSelectorAudio(const GameObjectChild& child,void** slot,Relo::Tracker& data) {
    bool voice=child.name=="AudioArrayVoice";
    auto lists=static_cast<NativeList*>(data.Push(slot,voice?24:8,1));
    const char* names[]={"AudioEntry","ObjectSpecificEntry","NamedEntry"};
    for(unsigned group=0;group<(voice?3u:1u);++group) {
        auto list=lists+group;std::vector<const GameObjectChild*> values;
        for(const auto& entry:child.children) if(!voice || entry.name==names[group]) values.push_back(&entry);
        list->count=static_cast<U32>(values.size());
        if(!list->count) continue;
        unsigned stride=voice?(group==1?16:12):8;
        auto elements=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));
        for(U32 j=0;j<list->count;++j) {
            auto at=elements+stride*j;const auto& attrs=values[j]->attributes;
            if(voice) {
                if(group==2) {auto n=attrs.find("Name");if(n!=attrs.end()) {auto hash=ExactStringHash(n->second);std::memcpy(at+8,&hash,4);}}
                else {auto type=attrs.find("AudioType");if(type!=attrs.end()) {auto word=EnumValue(group==0?"AudioVoiceEntry":"AudioObjectSpecificVoiceEntry",type->second);std::memcpy(at+8,&word,4);}if(group==1) {auto target=attrs.find("TargetObject");if(target!=attrs.end()) {auto hash=HashAssetId(target->second);std::memcpy(at+12,&hash,4);}}}
                auto sound=attrs.find("Sound");if(sound!=attrs.end()) {auto ref=data.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(data,ref,sound->second);data.Pop();}
                auto eva=attrs.find("EvaEvent");MarshalReference(data,at+4,eva==attrs.end()?"":eva->second);
            } else {
                bool sound=child.name=="AudioArraySound";
                auto ref=attrs.find(sound?"Sound":"EvaEvent");if(ref!=attrs.end()) MarshalReference(data,at,ref->second);
                auto type=attrs.find(sound?"AudioType":"EvaType");if(type!=attrs.end()) {auto word=EnumValue(child.name,type->second);std::memcpy(at+4,&word,4);}
            }
        }
        data.Pop();
    }
    data.Pop();
}
void MarshalSelectorPriority(const GameObjectChild& input,unsigned char* at,Relo::Tracker& data) {
    auto value=input.attributes.find("VoicePriorityOverride");if(value==input.attributes.end()) return;
    auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=static_cast<U32>(ParseSigned(value->second));data.Pop();
}
void MarshalSelectorAudioChildren(const GameObjectChild& input,unsigned char* output,bool eva,Relo::Tracker& data) {
    const char* names[]={"AudioArrayVoice","AudioArraySound","EvaEventArray"};
    for(unsigned group=eva?2:0;group<(eva?3u:2u);++group)
        for(const auto& child:input.children) if(child.name==names[group]) MarshalSelectorAudio(child,reinterpret_cast<void**>(output+(eva?0:4*group)),data);
}
void MarshalSoundSelector(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==13) {
        auto value=input.attributes.find("Chance");float chance=ParseFloat(value==input.attributes.end()?"50":value->second)*0.01f;std::memcpy(output+8,&chance,4);
        MarshalSelectorPriority(input,output+12,data);
        value=input.attributes.find("RerollOnEveryFrame");output[24]=ParseBoolean(value==input.attributes.end()?"false":value->second)?1:0;
        MarshalSelectorAudioChildren(input,output+16,false,data);return;
    }
    auto list=reinterpret_cast<NativeList*>(output+8);list->count=static_cast<U32>(input.children.size());
    if(!list->count) return;
    unsigned stride=kind==14?132:kind==15?148:124;
    auto entries=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));
    for(U32 i=0;i<list->count;++i) {
        const auto& entry=input.children[i];auto at=entries+stride*i;
        const char* names[]={kind==15?"RequiredModelConditions":"RequiredFlags",kind==15?"ConflictingModelConditions":"ExcludedFlags"};
        for(unsigned group=0;group<2;++group) {auto v=entry.attributes.find(names[group]);MarshalModelCondition(v==entry.attributes.end()?"":v->second,reinterpret_cast<U32*>(at+60*group));}
        if(kind!=16) MarshalSelectorPriority(entry,at+120,data);
        MarshalSelectorAudioChildren(entry,at+(kind==16?120:124),kind==16,data);
        if(kind==15) for(unsigned group=0;group<2;++group) {
            auto upgrades=reinterpret_cast<NativeList*>(at+132+8*group);std::vector<const GameObjectChild*> values;
            for(const auto& child:entry.children) if(child.name==(group==0?"RequiredUpgrade":"ConflictingUpgrade")) values.push_back(&child);
            upgrades->count=static_cast<U32>(values.size());
            if(upgrades->count) {auto words=static_cast<U32*>(data.Push(&upgrades->data,4,upgrades->count));for(U32 j=0;j<upgrades->count;++j) words[j]=HashAssetId(values[j]->text);data.Pop();}
        }
    }
    data.Pop();
}
void EndianSelectorAudio(void* input,bool voice) {
    if(!input) return;
    auto lists=static_cast<NativeList*>(input);
    for(unsigned group=0;group<(voice?3u:1u);++group) {
        auto list=lists+group;unsigned stride=voice?(group==1?16:12):8;
        for(U32 j=0;j<list->count;++j) {auto at=static_cast<unsigned char*>(list->data)+stride*j;Swap(at+(voice?8:4));if(voice && group==1) Swap(at+12);}
        Swap(&list->count);
    }
}
void EndianSelectorAudioChildren(unsigned char* input,bool eva) {
    if(eva) EndianSelectorAudio(*reinterpret_cast<void**>(input),false);
    else {EndianSelectorAudio(*reinterpret_cast<void**>(input),true);EndianSelectorAudio(*reinterpret_cast<void**>(input+4),false);}
}
void EndianSoundSelector(unsigned char* output,unsigned kind) {
    if(kind==13) {Swap(output+8);auto priority=*reinterpret_cast<void**>(output+12);if(priority) Swap(priority);EndianSelectorAudioChildren(output+16,false);return;}
    auto list=reinterpret_cast<NativeList*>(output+8);unsigned stride=kind==14?132:kind==15?148:124;
    for(U32 i=0;i<list->count;++i) {
        auto at=static_cast<unsigned char*>(list->data)+stride*i;
        for(unsigned j=0;j<30;++j) Swap(at+4*j);
        if(kind!=16) {auto priority=*reinterpret_cast<void**>(at+120);if(priority) Swap(priority);}
        EndianSelectorAudioChildren(at+(kind==16?120:124),kind==16);
        if(kind==15) for(unsigned group=0;group<2;++group) {auto upgrades=reinterpret_cast<NativeList*>(at+132+8*group);for(U32 j=0;j<upgrades->count;++j) Swap(static_cast<U32*>(upgrades->data)+j);Swap(&upgrades->count);}
    }
    Swap(&list->count);
}

#include "GameObjectScalarDefaultsRuntime.h"
#include "GameObjectModuleFieldsRuntime.h"
#include "GameObjectModuleFamiliesRuntime.h"
#include "GameObjectModuleDispatchRuntime.h"
U32 ExactStringHash(const std::string& text) {
    U32 size=static_cast<U32>(std::strlen(text.c_str())),hash=size;if(!size) return 0;
    auto p=reinterpret_cast<const unsigned char*>(text.data());
    auto shortWord=[](const unsigned char* b)->U32 {return b[0]|(U32(b[1])<<8);};
    auto signedByte=[](unsigned char b)->U32 {return static_cast<U32>(static_cast<std::int8_t>(b));};
    for(U32 n=size/4;n;--n,p+=4) {hash+=shortWord(p);hash^=(shortWord(p+2)^(hash<<5))<<11;hash+=hash>>11;}
    switch(size&3) {
    case 3:hash+=shortWord(p);hash^=((signedByte(p[2])<<2)^hash)<<16;hash+=hash>>11;break;
    case 2:hash+=shortWord(p);hash^=hash<<11;hash+=hash>>17;break;
    case 1:hash+=signedByte(*p);hash^=hash<<10;hash+=hash>>1;break;
    }
    hash^=hash<<3;hash+=hash>>5;hash^=hash<<2;hash+=hash>>15;return (hash<<10)^hash;
}
bool RootScalarDefault(GameObjectValue kind) {
    return kind!=GameObjectValue::String && kind!=GameObjectValue::StringList && kind!=GameObjectValue::Reference;
}
void MarshalGameObjectRootField(const GameObjectField& f,const std::string& text,unsigned char* at,Relo::Tracker& data) {
    U32 word=0;float number=0;
        switch(f.kind) {
        case GameObjectValue::DurationDefault: {
            // Original helper: atof -> float -> scaled float -> ceil -> float
            // -> conv.u4. The x86 CLR conversion truncates through signed int64;
            // out-of-int64 values produce the integer-indefinite low word (0).
            const float value=ParseFloat(text);
            const float scaled=static_cast<float>(static_cast<double>(0.005f)*static_cast<double>(value));
            const float rounded=static_cast<float>(std::ceil(static_cast<double>(scaled)));
            const double wide=static_cast<double>(rounded);
            word=(wide>=-9223372036854775808.0 && wide<9223372036854775808.0)
                ?static_cast<U32>(static_cast<std::int64_t>(wide)):0u;
            break;
        }
        case GameObjectValue::Bits: MarshalEnumBitset(text,kindOfEntries,284,149,284,reinterpret_cast<U32*>(at),true);return;
        case GameObjectValue::AssetId: word=HashAssetId(text);break;
        case GameObjectValue::Reference: MarshalReference(data,at,text);return;
        case GameObjectValue::Enum: word=EnumValue(f.name,text);break;
        case GameObjectValue::String: MarshalString(data,at,text);return;
        case GameObjectValue::Boolean: *at=ParseBoolean(text)?1:0;return;
        case GameObjectValue::Byte: *at=static_cast<unsigned char>(ParseUnsigned(text));return;
        case GameObjectValue::Short: {auto value=static_cast<std::uint16_t>(ParseUnsigned(text));std::memcpy(at,&value,2);return;}
        case GameObjectValue::Signed: word=static_cast<U32>(ParseSigned(text));break;
        case GameObjectValue::Unsigned: word=ParseUnsigned(text);break;
        case GameObjectValue::Float: number=ParseFloat(text);std::memcpy(&word,&number,4);break;
        case GameObjectValue::Time: number=InitializeTime(text.c_str());std::memcpy(&word,&number,4);break;
        case GameObjectValue::Angle: number=InitializeAngle(text.c_str());std::memcpy(&word,&number,4);break;
        default: throw std::logic_error("Unsupported GameObject value");
        }
    std::memcpy(at,&word,4);
}
struct GameObjectRootDefaults {
    unsigned char bytes[592]{};
    GameObjectRootDefaults() {
        Relo::Tracker unused;
        for(const auto& f:fields) {
            if(f.kind==GameObjectValue::DurationDefault) {U32 word=static_cast<U32>(f.defaultValue);std::memcpy(bytes+f.offset,&word,4);}
            else if(f.fallback && RootScalarDefault(f.kind))MarshalGameObjectRootField(f,std::string(f.fallback),bytes+f.offset,unused);
        }
    }
};
static_assert(std::is_trivially_destructible<GameObjectRootDefaults>::value,"Root defaults must not own exit-time state");
const GameObjectRootDefaults rootDefaults;
}
const GameObjectField* GameObjectFields() { return fields; }
unsigned GameObjectFieldCount() { return sizeof(fields)/sizeof(fields[0]); }

#include "GameObjectDrawModelSupport.h"
#include "GameObjectModuleSupport.h"
Relo::Chunk CompileGameObject(const AttributeMap& attributes,const std::vector<GameObjectChild>& children,bool bigEndian) {

    Relo::Tracker data, procedures;
    unsigned char* root=nullptr;
    data.Push(reinterpret_cast<void**>(&root),592,1);
    std::memcpy(root,rootDefaults.bytes,592);
    // Original Browser Marshal precedes every scalar/string allocation.
    std::vector<std::string> browser;
    auto browserInput=attributes.find("Browser");
    if(browserInput!=attributes.end()) {
        const auto& text=browserInput->second;
        auto space=[](unsigned char c) {return c==' ' || (c>=9 && c<=13);};
        std::size_t at=0;
        while(at<text.size() && text[at]) {
            while(at<text.size() && space(static_cast<unsigned char>(text[at]))) ++at;
            auto first=at;
            while(at<text.size() && text[at] && !space(static_cast<unsigned char>(text[at]))) ++at;
            if(at>first) browser.push_back(text.substr(first,at-first));
        }
    }
    ReloString* browserStrings=nullptr;
    if(!browser.empty()) {
        U32 count=static_cast<U32>(browser.size());std::memcpy(root+40,&count,4);
        browserStrings=static_cast<ReloString*>(data.Push(reinterpret_cast<void**>(root+44),8,count));
        for(U32 i=0;i<count;++i) MarshalString(data,browserStrings+i,browser[i]);
        data.Pop();
    }
    for(const auto& f:fields) {
        if(f.kind==GameObjectValue::StringList)continue;
        auto found=attributes.find(f.name);
        if(found==attributes.end()) {
            if(!f.fallback || RootScalarDefault(f.kind))continue;
            MarshalGameObjectRootField(f,std::string(f.fallback),root+f.offset,data);
        } else {
            // Core/XSD can materialize the same defaults as explicit attributes.
            if(f.fallback && RootScalarDefault(f.kind) && f.kind!=GameObjectValue::DurationDefault && found->second==f.fallback)continue;
            MarshalGameObjectRootField(f,found->second,root+f.offset,data);
        }
    }
    ReloString* displayNames[2]={nullptr,nullptr};
    // Original allocation order is schema order, independent of XML order.
    for(unsigned i=0;i<2;++i) for(const auto& child:children)
        if(child.name==(i==0?"DisplayName":"DisplayNameTransformed")) {
            displayNames[i]=static_cast<ReloString*>(data.Push(reinterpret_cast<void**>(root+396+4*i),8,1));
            MarshalString(data,displayNames[i],child.text);data.Pop();
        }
    for(const auto& child:children) {
        U32 word=0;unsigned offset=0;
        if(child.name=="Buildable" || child.name=="ThingClass") {
            word=EnumValue(child.name,child.text);offset=child.name=="Buildable"?428:432;
        } else if(child.name=="DisplayColor") {
            auto channel=[&](const char* name,U32 fallback) {
                auto found=child.attributes.find(name);
                return (found==child.attributes.end()?fallback:ParseUnsigned(found->second))&255u;
            };
            word=(channel("A",255)<<24)|(channel("R",0)<<16)|(channel("G",0)<<8)|channel("B",0);offset=452;
        } else continue;
        std::memcpy(root+offset,&word,4);
    }
    unsigned char* dependency=nullptr;
    U32* dependencyMasks[3]={nullptr,nullptr,nullptr};
    ObjectFilter* dependencyFilter=nullptr;
    for(const auto& child:children) if(child.name=="GameDependency") {
        dependency=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+404),40,1));
        const char* names[]={"RequiredModelConditionsAny","ForbiddenModelConditions","RequiredObjectStatusAny"};
        for(unsigned j=0;j<3;++j) {
            auto found=child.attributes.find(names[j]);if(found==child.attributes.end()) continue;
            dependencyMasks[j]=static_cast<U32*>(data.Push(reinterpret_cast<void**>(dependency+4*j),j==2?28:60,1));
            if(j==2) MarshalObjectStatus(found->second,dependencyMasks[j]);else MarshalModelCondition(found->second,dependencyMasks[j]);
            data.Pop();
        }
        const char* groups[]={"RequiredObject","ForbiddenUpgrade","NeededUpgrade"};
        for(unsigned group=0;group<3;++group) {
            std::vector<std::string> values;
            for(const auto& value:child.children) if(value.name==groups[group]) values.push_back(value.text);
            auto list=reinterpret_cast<NativeList*>(dependency+12+8*group);list->count=static_cast<U32>(values.size());
            if(list->count) {
                auto elements=static_cast<U32*>(data.Push(&list->data,4,list->count));
                for(U32 j=0;j<list->count;++j) elements[j]=HashAssetId(values[j]);
                data.Pop();
            }
        }
        for(const auto& value:child.children) if(value.name=="ObjectFilter") {
            ObjectFilterInput input;input.attributes=value.attributes;
            for(const auto& thing:value.children) if(thing.name=="IncludeThing") input.includeThings.push_back(thing.text);else input.excludeThings.push_back(thing.text);
            dependencyFilter=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(dependency+36),112,1));
            MarshalObjectFilter(input,dependencyFilter,data);data.Pop();
        }
        data.Pop();
    }
    NativeList* buildCosts=nullptr;
    for(const auto& child:children) if(child.name=="ObjectResourceInfo") {
        buildCosts=static_cast<NativeList*>(data.Push(reinterpret_cast<void**>(root+408),8,1));
        buildCosts->count=static_cast<U32>(child.children.size());
        if(buildCosts->count) {
            auto elements=static_cast<U32*>(data.Push(&buildCosts->data,8,buildCosts->count));
            for(U32 i=0;i<buildCosts->count;++i) for(unsigned j=0;j<2;++j) {
                auto found=child.children[i].attributes.find(j==0?"Account":"Amount");
                if(found!=child.children[i].attributes.end()) elements[2*i+j]=ParseUnsigned(found->second);
            }
            data.Pop();
        }
        data.Pop();
    }
    std::map<unsigned,NativeList*> lists;
    lists[40]=reinterpret_cast<NativeList*>(root+40);
    auto marshalSets=[&](const char* name,unsigned offset,bool armor) {
        std::vector<const GameObjectChild*> values;
        for(const auto& child:children) if(child.name==name) values.push_back(&child);
        auto list=reinterpret_cast<NativeList*>(root+offset);lists[offset]=list;
        list->count=static_cast<U32>(values.size());
        if(values.empty()) return;
        auto elements=static_cast<unsigned char*>(data.Push(&list->data,12,list->count));
        for(U32 i=0;i<list->count;++i) {
            auto element=elements+12*i;const auto& attrs=values[i]->attributes;
            auto find=[&](const char* key)->const std::string* {auto it=attrs.find(key);return it==attrs.end()?nullptr:&it->second;};
            if(armor) {
                if(auto value=find("Armor")) MarshalReference(data,element,*value);
                if(auto value=find("DamageFX")) MarshalReference(data,element+4,*value);
                if(auto value=find("Conditions")) MarshalEnumBitset(*value,armorSetEntries,23,12,23,reinterpret_cast<U32*>(element+8));
            } else {
                if(auto value=find("Speed")) {auto number=ParseFloat(*value);std::memcpy(element,&number,4);}
                if(auto value=find("Condition")) {auto word=EnumValue("LocomotorSet",*value);std::memcpy(element+4,&word,4);}
                if(auto value=find("Locomotor")) MarshalReference(data,element+8,*value);
            }
        }
        data.Pop();
    };
    marshalSets("ArmorSet",412,true);
    marshalSets("LocomotorSet",420,false);
    auto marshalList=[&](const char* name,unsigned offset,bool strings) {
        std::vector<std::string> values;
        for(const auto& child:children) if(child.name==name) values.push_back(child.text);
        auto list=reinterpret_cast<NativeList*>(root+offset);lists[offset]=list;
        list->count=static_cast<U32>(values.size());
        if(values.empty()) return;
        auto elements=static_cast<unsigned char*>(data.Push(&list->data,strings?8:4,list->count));
        for(U32 i=0;i<list->count;++i) {
            if(strings) MarshalString(data,elements+8*i,values[i]);
            else {auto id=HashAssetId(values[i]);std::memcpy(elements+4*i,&id,4);}
        }
        data.Pop();
    };
    // The original pointer-list overload selects element children, not text
    // tokens. XSD StringList permits text only, so present valid nodes allocate
    // an empty list header even when their text contains several words.
    std::map<unsigned,NativeList*> pointerLists;
    for(unsigned index=0;index<2;++index) for(const auto& child:children)
        if(child.name==(index==0?"BuildFadeInOnCreateList":"BuildVariations")) {
            unsigned offset=436+4*index;
            pointerLists[offset]=static_cast<NativeList*>(data.Push(reinterpret_cast<void**>(root+offset),8,1));
            data.Pop();
        }
    marshalList("EquivalentTo",444,false);
    U32* flammability=nullptr;float* camera=nullptr;
    std::map<unsigned,unsigned char*> leafObjects;
    auto marshalLeaves=[&](unsigned first,unsigned last) {
        for(const auto& leaf:leafLayouts) if(leaf.offset>=first && leaf.offset<=last)
            for(const auto& child:children) if(child.name==leaf.name) {
                auto bytes=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+leaf.offset),leaf.size,1));
                leafObjects[leaf.offset]=bytes;
                for(unsigned i=0;i<leaf.count;++i) {
                    const auto& f=leaf.fields[i];auto found=child.attributes.find(f.name);
                    if(found==child.attributes.end() && !f.fallback) continue;
                    std::string value=found==child.attributes.end()?f.fallback:found->second;
                    auto at=bytes+f.offset;U32 word=0;float number=0;
                    switch(f.kind) {
                    case LeafValue::String: MarshalString(data,at,value);continue;
                    case LeafValue::Reference: MarshalReference(data,at,value);continue;
                    case LeafValue::AssetId: word=HashAssetId(value);break;
                    case LeafValue::Boolean: *at=ParseBoolean(value)?1:0;continue;
                    case LeafValue::Enum: word=EnumValue(leaf.name,value);break;
                    case LeafValue::Unsigned: word=ParseUnsigned(value);break;
                    default:
                        number=f.kind==LeafValue::Time?InitializeTime(value.c_str()):f.kind==LeafValue::Percent?ParsePercentage(value):ParseFloat(value);
                        std::memcpy(&word,&number,4);break;
                    }
                    std::memcpy(at,&word,4);
                }
                data.Pop();
            }
    };
    for(const auto& child:children) if(child.name=="Flammability") {
        flammability=static_cast<U32*>(data.Push(reinterpret_cast<void**>(root+456),16,1));
        const char* names[]={"Fuel","MaxBurnRate","Decay","Resistance"};
        for(unsigned i=0;i<4;++i) {auto at=child.attributes.find(names[i]);flammability[i]=at==child.attributes.end()?0:ParseUnsigned(at->second);}
        data.Pop();
    }
    unsigned char* skirmishAI=nullptr;
    for(const auto& child:children) if(child.name=="SkirmishAIInformation") {
        skirmishAI=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+460),88,1));
        MarshalAI(child,skirmishAI,0,data);data.Pop();
    }
    std::map<unsigned,NativeList*> moduleLists;
    const char* moduleContainers[]={"Draws","Behaviors","AI","ClientUpdates","ClientBehaviors"};
    const unsigned moduleOffsets[]={464,468,472,484,488};
    U32* bodyModule=nullptr;
    // Schema order places Body after AI and before the two client lists.
    for(unsigned group=0;group<5;++group) {
        if(group==3) for(const auto& child:children) if(child.name=="Body" && child.children.size()==1) MarshalModule(child.children[0],reinterpret_cast<void**>(root+480),data);
        for(const auto& child:children) if(child.name==moduleContainers[group]) {
            auto list=group==2?reinterpret_cast<NativeList*>(root+472):static_cast<NativeList*>(data.Push(reinterpret_cast<void**>(root+moduleOffsets[group]),8,1));
            moduleLists[moduleOffsets[group]]=list;MarshalModuleList(child,list,data);if(group!=2) data.Pop();
        }
    }
    bodyModule=*reinterpret_cast<U32**>(root+480);
    unsigned char* geometry=nullptr;
    NativeList* geometryShapes=nullptr;NativeList* contactPoints=nullptr;
    auto marshalVector=[&](const GameObjectChild& input,void* output,unsigned dimensions) {
        const char* names[]={"x","y","z"};
        for(unsigned j=0;j<dimensions;++j) {auto found=input.attributes.find(names[j]);float value=found==input.attributes.end()?0.0f:ParseFloat(found->second);std::memcpy(static_cast<unsigned char*>(output)+4*j,&value,4);}
    };
    auto marshalShape=[&](const GameObjectChild& input,unsigned char* output) {
        auto value=[&](const char* name,const char* fallback)->std::string {auto found=input.attributes.find(name);return found==input.attributes.end()?fallback:found->second;};
        U32 type=EnumValue("GeometryShapeType",value("Type","SPHERE")),generation=EnumValue("ContactPointGeneration",value("ContactPointGeneration","NONE"));
        std::memcpy(output,&type,4);std::memcpy(output+4,&generation,4);
        auto name=input.attributes.find("Name");if(name!=input.attributes.end()) MarshalString(data,output+8,name->second);
        output[44]=ParseBoolean(value("Active","true"))?1:0;output[45]=ParseBoolean(value("UsedForHealthBox","true"))?1:0;
        float major=ParseFloat(value("MajorRadius","1.0")),minor=ParseFloat(value("MinorRadius","1.0"));std::memcpy(output+16,&major,4);std::memcpy(output+20,&minor,4);
        auto other=input.attributes.find("Other");if(other!=input.attributes.end()) MarshalString(data,output+24,other->second);
        float height=ParseFloat(value("Height","1.0"));std::memcpy(output+32,&height,4);
        auto angle=input.attributes.find("FrontAngle");if(angle!=input.attributes.end()) {float value=InitializeAngle(angle->second.c_str());std::memcpy(output+36,&value,4);}
        if(!input.children.empty()) {auto vector=data.Push(reinterpret_cast<void**>(output+40),12,1);marshalVector(input.children[0],vector,3);data.Pop();}
    };
    for(const auto& child:children) if(child.name=="Geometry") {
        geometry=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+492),28,1));
        auto isSmall=child.attributes.find("IsSmall");geometry[24]=isSmall!=child.attributes.end() && ParseBoolean(isSmall->second)?1:0;
        for(const auto& c:child.children) if(c.name=="RotationAnchorOffset") {auto vector=data.Push(reinterpret_cast<void**>(geometry),8,1);marshalVector(c,vector,2);data.Pop();}
        for(const auto& c:child.children) if(c.name=="PlacementShape") {auto shape=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(geometry+4),48,1));marshalShape(c,shape);data.Pop();}
        geometryShapes=reinterpret_cast<NativeList*>(geometry+8);contactPoints=reinterpret_cast<NativeList*>(geometry+16);
        for(const auto& c:child.children) if(c.name=="Shape") ++geometryShapes->count;else if(c.name=="ContactPoint") ++contactPoints->count;
        if(geometryShapes->count) {
            auto shapes=static_cast<unsigned char*>(data.Push(&geometryShapes->data,48,geometryShapes->count));U32 index=0;
            for(const auto& c:child.children) if(c.name=="Shape") {marshalShape(c,shapes+48*index);++index;}
            data.Pop();
        }
        if(contactPoints->count) {
            auto points=static_cast<unsigned char*>(data.Push(&contactPoints->data,20,contactPoints->count));U32 index=0;
            for(const auto& c:child.children) if(c.name=="ContactPoint") {
                auto at=points+20*index++;auto label=c.attributes.find("Label");if(label!=c.attributes.end()) MarshalString(data,at,label->second);
                if(!c.children.empty()) marshalVector(c.children[0],at+8,3);
            }
            data.Pop();
        }
        data.Pop();
    }
    // Original 66174/66183: both schema types are empty one-byte objects.
    // Root Tokenize 97735/97742 creates no pointee bookmark or child table.
    for(unsigned i=0;i<2;++i) for(const auto& child:children)
        if(child.name==(i==0?"ReplaceModule":"InheritableModule")) {
            data.Push(reinterpret_cast<void**>(root+496+4*i),1,1);data.Pop();
        }
    std::map<unsigned,unsigned char*> autoObjects;
    for(unsigned index=0;index<2;++index) for(const auto& child:children)
        if(child.name==(index==0?"AutoResolveArmor":"AutoResolveWeapon")) {
            unsigned offset=504+4*index;
            auto bytes=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+offset),20,1));
            autoObjects[offset]=bytes;
            auto ref=child.attributes.find(index==0?"Armor":"Weapon");
            if(ref!=child.attributes.end()) MarshalReference(data,bytes,ref->second);
            for(unsigned group=0;group<2;++group) {
                std::vector<std::string> values;
                for(const auto& value:child.children) if(value.name==(group==0?"RequiredUpgrade":"ExcludedUpgrade")) values.push_back(value.text);
                auto list=reinterpret_cast<NativeList*>(bytes+4+8*group);
                list->count=static_cast<U32>(values.size());
                if(!values.empty()) {
                    auto elements=static_cast<U32*>(data.Push(&list->data,4,list->count));
                    for(U32 i=0;i<list->count;++i) elements[i]=HashAssetId(values[i]);
                    data.Pop();
                }
            }
            data.Pop();
        }
    marshalLeaves(512,516);
    for(const auto& child:children) if(child.name=="LiveCameraOffset") {
        camera=static_cast<float*>(data.Push(reinterpret_cast<void**>(root+520),12,1));
        const char* names[]={"x","y","z"};
        for(unsigned i=0;i<3;++i) {auto at=child.attributes.find(names[i]);camera[i]=at==child.attributes.end()?0.0f:ParseFloat(at->second);}
        data.Pop();
    }
    NativeList* voiceLists=nullptr;
    for(const auto& child:children) if(child.name=="AudioArrayVoice") {
        voiceLists=static_cast<NativeList*>(data.Push(reinterpret_cast<void**>(root+524),24,1));
        const char* names[]={"AudioEntry","ObjectSpecificEntry","NamedEntry"};
        for(unsigned group=0;group<3;++group) {
            auto list=voiceLists+group;std::vector<const GameObjectChild*> values;
            for(const auto& entry:child.children) if(entry.name==names[group]) values.push_back(&entry);
            list->count=static_cast<U32>(values.size());
            if(list->count) {
                unsigned stride=group==1?16:12;auto elements=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));
                for(U32 j=0;j<list->count;++j) {
                    auto at=elements+stride*j;const auto& attrs=values[j]->attributes;
                    if(group==2) {auto name=attrs.find("Name");if(name!=attrs.end()) {auto hash=ExactStringHash(name->second);std::memcpy(at+8,&hash,4);}}
                    else {auto type=attrs.find("AudioType");if(type!=attrs.end()) {auto v=EnumValue(group==0?"AudioVoiceEntry":"AudioObjectSpecificVoiceEntry",type->second);std::memcpy(at+8,&v,4);}if(group==1) {auto target=attrs.find("TargetObject");if(target!=attrs.end()) {auto hash=HashAssetId(target->second);std::memcpy(at+12,&hash,4);}}}
                    auto sound=attrs.find("Sound");if(sound!=attrs.end()) {auto ref=data.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(data,ref,sound->second);data.Pop();}
                    auto eva=attrs.find("EvaEvent");MarshalReference(data,at+4,eva==attrs.end()?"":eva->second);
                }
                data.Pop();
            }
        }
        data.Pop();
    }
    NativeList* audioArrays[2]={nullptr,nullptr};
    for(unsigned group=0;group<2;++group) for(const auto& child:children) if(child.name==(group==0?"AudioArraySound":"EvaEventArray")) {
        auto list=static_cast<NativeList*>(data.Push(reinterpret_cast<void**>(root+528+4*group),8,1));audioArrays[group]=list;
        list->count=static_cast<U32>(child.children.size());
        if(list->count) {
            auto elements=static_cast<U32*>(data.Push(&list->data,8,list->count));
            for(U32 j=0;j<list->count;++j) {
                const auto& attributes=child.children[j].attributes;
                auto ref=attributes.find(group==0?"Sound":"EvaEvent");if(ref!=attributes.end()) MarshalReference(data,elements+2*j,ref->second);
                auto type=attributes.find(group==0?"AudioType":"EvaType");if(type!=attributes.end()) elements[2*j+1]=EnumValue(child.name,type->second);
            }
            data.Pop();
        }
        data.Pop();
    }
    marshalList("UpgradeCameo",536,true);
    marshalLeaves(544,552);
    unsigned char* crusher=nullptr;
    NativeList* extraLevels=nullptr;
    for(const auto& child:children) if(child.name=="CrusherInfo") {
        crusher=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(root+556),36,1));
        auto value=[&](const char* name,const char* fallback)->std::string {auto it=child.attributes.find(name);return it==child.attributes.end()?fallback:it->second;};
        auto word=static_cast<U32>(ParseSigned(value("CrushKnockback","0")));std::memcpy(crusher,&word,4);
        const char* floats[]={"CrushZFactor","MinCrushVelocityPercent","CrushDecelerationPercent"};
        for(unsigned j=0;j<3;++j) {auto text=value(floats[j],j==2?"0":"1");float number=j==0?ParseFloat(text):ParsePercentage(text);std::memcpy(crusher+4+4*j,&number,4);}
        auto ref=child.attributes.find("CrushWeapon");if(ref!=child.attributes.end()) MarshalReference(data,crusher+16,ref->second);
        crusher[28]=static_cast<unsigned char>(ParseSigned(value("CrusherLevel","0")));
        crusher[29]=static_cast<unsigned char>(ParseSigned(value("CrushableLevel","127")));
        const char* flags[]={"CrushEqualLevelProps","UseCrushAttack","CrushOnlyWhileCharging","CrushAllies","CrushWhileStationary"};
        for(unsigned j=0;j<5;++j) crusher[30+j]=ParseBoolean(value(flags[j],j==1?"true":"false"))?1:0;
        extraLevels=reinterpret_cast<NativeList*>(crusher+20);extraLevels->count=static_cast<U32>(child.children.size());
        if(extraLevels->count) {
            auto items=static_cast<unsigned char*>(data.Push(&extraLevels->data,8,extraLevels->count));
            for(U32 j=0;j<extraLevels->count;++j) {
                const auto& attrs=child.children[j].attributes;auto at=items+8*j;
                for(unsigned k=0;k<2;++k) {auto found=attrs.find(k==0?"CrusherLevel":"CrushableLevel");at[4+k]=found==attrs.end()?(k==0?0:127):static_cast<unsigned char>(ParseSigned(found->second));}
                auto found=attrs.find("ModelConditionMatch");if(found!=attrs.end()) {auto v=EnumValue("ExtraCrushLevels",found->second);std::memcpy(at,&v,4);}
            }
            data.Pop();
        }
        data.Pop();
    }
    auto projections=reinterpret_cast<NativeList*>(root+560);
    std::vector<const GameObjectChild*> projectionInputs;
    for(const auto& child:children) if(child.name=="ProjectedBuildabilityInfo") projectionInputs.push_back(&child);
    projections->count=static_cast<U32>(projectionInputs.size());
    std::vector<ObjectFilter*> projectionFilters(projections->count,nullptr);
    if(projections->count) {
        auto items=static_cast<unsigned char*>(data.Push(&projections->data,112,projections->count));
        const EnumNameValue placements[]={{"BLOCKED",4},{"INVALID",0},{"MAIN_STRUCTURE",1},{"OTHER_STRUCTURE",2},{"TIBERIUM_FIELD",3}};
        for(U32 j=0;j<projections->count;++j) {
            auto at=items+112*j;const auto& child=*projectionInputs[j];
            auto value=[&](const char* name,const char* fallback)->std::string {auto it=child.attributes.find(name);return it==child.attributes.end()?fallback:it->second;};
            float radius=ParseFloat(value("Radius","0"));std::memcpy(at,&radius,4);
            auto ry=child.attributes.find("RadiusY");if(ry!=child.attributes.end()) {
                auto number=static_cast<float*>(data.Push(reinterpret_cast<void**>(at+4),4,1));*number=ParseFloat(ry->second);data.Pop();
            }
            MarshalEnumBitset(value("BuildPlacementTypes","INVALID"),placements,5,4,5,reinterpret_cast<U32*>(at+8));
            auto status=child.attributes.find("StatusToReject");if(status!=child.attributes.end()) MarshalObjectStatus(status->second,reinterpret_cast<U32*>(at+12));
            MarshalModelCondition(value("ModelConditionsToReject","STRUCTURE_UNPACKING"),reinterpret_cast<U32*>(at+40));
            float height=ParseFloat(value("AllowedBuildabilityHeightVariation","50.0"));std::memcpy(at+100,&height,4);
            at[108]=ParseBoolean(value("PrimaryBuidability","true"))?1:0;
            if(!child.children.empty()) {
                const auto& filter=child.children[0];ObjectFilterInput input;input.attributes=filter.attributes;
                for(const auto& thing:filter.children) if(thing.name=="IncludeThing") input.includeThings.push_back(thing.text);else input.excludeThings.push_back(thing.text);
                projectionFilters[j]=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(at+104),112,1));
                MarshalObjectFilter(input,projectionFilters[j],data);data.Pop();
            }
        }
        data.Pop();
    }
    marshalList("DisplayUpgrade",568,false);
    if(!moduleLists.count(472)) moduleLists[472]=reinterpret_cast<NativeList*>(root+472);
    // Unsupported optional pointers and remaining list counts stay absent.
    // Eight inline empty lists still produce count tables in original token order.
    Procedure* table=nullptr;
    procedures.Push(reinterpret_cast<void**>(&table),12,127);
    for(unsigned i=0;i<126;++i) {
        const auto& t=tokens[i];table[i].name=t.name;table[i].type=t.type;
        if(t.kind==GameObjectTokenKind::Member) procedures.BookmarkMemberOffset(root+t.offset,&table[i].offset);
        else if((t.offset==396 || t.offset==400) && displayNames[(t.offset-396)/4])
            procedures.BookmarkMemberOffset(displayNames[(t.offset-396)/4],&table[i].offset);
        else if(t.offset==520 && camera) procedures.BookmarkMemberOffset(camera,&table[i].offset);
        else if(t.offset==456 && flammability) {
            // Original Tokenize(Flammability), line 95659: four members + sentinel.
            const U32 names[]={2194468989u,1555868979u,1413518551u,2097559226u};
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,5));
            for(unsigned j=0;j<4;++j) {nested[j].name=names[j];nested[j].type=1982953380u;procedures.BookmarkMemberOffset(flammability+j,&nested[j].offset);}
            procedures.Pop();
        }
        else if(t.offset==404 && dependency) {
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,8));
            const U32 keys[]={618545001u,849164020u,2787973588u,850409515u,725359606u,2925224871u,3832456114u};
            const U32 types[]={1742454729u,1742454729u,2814214430u,344905565u,653971399u,653971399u,3832456114u};
            for(unsigned j=0;j<7;++j) {nested[j].name=keys[j];nested[j].type=types[j];}
            for(unsigned j=0;j<3;++j) if(dependencyMasks[j]) procedures.BookmarkMemberOffset(dependencyMasks[j],&nested[j].offset);
            for(unsigned group=0;group<3;++group) {
                auto list=reinterpret_cast<NativeList*>(dependency+12+8*group);
                auto count=static_cast<Procedure*>(procedures.Push(&nested[group+3].offset,12,list->count+1));
                count[0].name=1820487795u;count[0].type=1564774360u;
                procedures.BookmarkMemberOffset(list,&count[0].offset);
                for(U32 j=0;j<list->count;++j) {
                    count[j+1].name=2974916843u;count[j+1].type=0;
                    procedures.BookmarkMemberOffset(static_cast<U32*>(list->data)+j,&count[j+1].offset);
                }
                procedures.Pop();
            }
            if(dependencyFilter) TokenizeObjectFilter(dependencyFilter,&nested[6].offset,procedures);
            procedures.Pop();
        }
        else if(moduleLists.count(t.offset)) TokenizeModuleList(moduleLists.at(t.offset),&table[i].offset,procedures);
        else if(t.offset==480 && bodyModule) TokenizeModule(bodyModule,&table[i].offset,procedures);
        else if(t.offset==460 && skirmishAI) TokenizeAI(skirmishAI,0,&table[i].offset,procedures);
        else if(t.offset==408 && buildCosts) {
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,2));
            nested[0].name=4043881713u;nested[0].type=3070636877u;
            auto count=static_cast<Procedure*>(procedures.Push(&nested[0].offset,12,buildCosts->count+1));
            count[0].name=1820487795u;count[0].type=1564774360u;
            procedures.BookmarkMemberOffset(buildCosts,&count[0].offset);
            for(U32 j=0;j<buildCosts->count;++j) {
                count[j+1].name=2974916843u;count[j+1].type=0;
                auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,3));
                for(unsigned k=0;k<2;++k) {
                    item[k].name=k==0?78324893u:1343336868u;item[k].type=1982953380u;
                    procedures.BookmarkMemberOffset(static_cast<U32*>(buildCosts->data)+2*j+k,&item[k].offset);
                }
                procedures.Pop();
            }
            procedures.Pop();procedures.Pop();
        }
        else if(pointerLists.count(t.offset)) {
            auto count=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,1));
            count[0].name=1820487795u;count[0].type=1564774360u;
            procedures.BookmarkMemberOffset(pointerLists.at(t.offset),&count[0].offset);
            procedures.Pop();
        }
        else if(autoObjects.count(t.offset)) {
            auto bytes=autoObjects.at(t.offset);
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,4));
            nested[0].name=t.offset==504?735930353u:63188358u;
            nested[0].type=t.offset==504?1636048523u:3494305058u;
            procedures.BookmarkMemberOffset(bytes,&nested[0].offset);
            for(unsigned group=0;group<2;++group) {
                nested[group+1].name=group==0?2138150697u:4283231007u;
                nested[group+1].type=653971399u;
                auto list=reinterpret_cast<NativeList*>(bytes+4+8*group);
                auto count=static_cast<Procedure*>(procedures.Push(&nested[group+1].offset,12,list->count+1));
                count[0].name=1820487795u;count[0].type=1564774360u;
                procedures.BookmarkMemberOffset(list,&count[0].offset);
                for(U32 j=0;j<list->count;++j) {
                    count[j+1].name=2974916843u;count[j+1].type=0;
                    procedures.BookmarkMemberOffset(static_cast<U32*>(list->data)+j,&count[j+1].offset);
                }
                procedures.Pop();
            }
            procedures.Pop();
        }
        else if(t.offset==524 && voiceLists) {
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,4));
            for(unsigned group=0;group<3;++group) {
                nested[group].name=AudioArrayVoiceProc[group].name;nested[group].type=AudioArrayVoiceProc[group].type;auto list=voiceLists+group;
                auto count=static_cast<Procedure*>(procedures.Push(&nested[group].offset,12,list->count+1));
                count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&count[0].offset);
                for(U32 j=0;j<list->count;++j) {
                    count[j+1].name=2974916843u;unsigned members=group==1?3:2;auto proc=group==0?AudioVoiceEntryProc:group==1?AudioObjectSpecificVoiceEntryProc:AudioVoiceReferentialEntryProc;
                    auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,members+1));auto bytes=static_cast<unsigned char*>(list->data)+(group==1?16:12)*j;
                    for(unsigned k=0;k<members;++k) {item[k].name=proc[k].name;item[k].type=proc[k].type;if(k) procedures.BookmarkMemberOffset(bytes+proc[k].offset,&item[k].offset);}
                    auto base=static_cast<Procedure*>(procedures.Push(&item[0].offset,12,3));
                    for(unsigned k=0;k<2;++k) {base[k].name=SoundOrEvaEventProc[k].name;base[k].type=SoundOrEvaEventProc[k].type;}
                    auto sound=*reinterpret_cast<void**>(bytes);if(sound) procedures.BookmarkMemberOffset(sound,&base[0].offset);
                    procedures.BookmarkMemberOffset(bytes+4,&base[1].offset);procedures.Pop();procedures.Pop();
                }
                procedures.Pop();
            }
            procedures.Pop();
        }
        else if((t.offset==528 || t.offset==532) && audioArrays[(t.offset-528)/4]) {
            unsigned group=(t.offset-528)/4;auto list=audioArrays[group];auto proc=group==0?AudioArraySoundProc:EvaEventArrayProc;auto entryProc=group==0?AudioSoundEntryProc:EvaEventEntryProc;
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,2));nested[0].name=proc[0].name;nested[0].type=proc[0].type;
            auto count=static_cast<Procedure*>(procedures.Push(&nested[0].offset,12,list->count+1));
            count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&count[0].offset);
            for(U32 j=0;j<list->count;++j) {
                count[j+1].name=2974916843u;auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,3));
                for(unsigned k=0;k<2;++k) {item[k].name=entryProc[k].name;item[k].type=entryProc[k].type;procedures.BookmarkMemberOffset(static_cast<U32*>(list->data)+2*j+k,&item[k].offset);}
                procedures.Pop();
            }
            procedures.Pop();procedures.Pop();
        }
        else if(t.offset==492 && geometry) {
            auto tokenizeShape=[&](unsigned char* shape,void** destination) {
                auto nested=static_cast<Procedure*>(procedures.Push(destination,12,12));
                for(unsigned j=0;j<11;++j) {nested[j].name=ShapeProc[j].name;nested[j].type=ShapeProc[j].type;
                    if(j==10) {auto vector=*reinterpret_cast<void**>(shape+40);if(vector) procedures.BookmarkMemberOffset(vector,&nested[j].offset);}
                    else procedures.BookmarkMemberOffset(shape+ShapeProc[j].offset,&nested[j].offset);
                }
                procedures.Pop();
            };
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,6));
            for(unsigned j=0;j<5;++j) {nested[j].name=GeometryProc[j].name;nested[j].type=GeometryProc[j].type;}
            procedures.BookmarkMemberOffset(geometry+24,&nested[0].offset);
            auto anchor=*reinterpret_cast<void**>(geometry);if(anchor) procedures.BookmarkMemberOffset(anchor,&nested[1].offset);
            auto placement=*reinterpret_cast<unsigned char**>(geometry+4);if(placement) tokenizeShape(placement,&nested[2].offset);
            for(unsigned group=0;group<2;++group) {
                auto list=group==0?geometryShapes:contactPoints;
                auto count=static_cast<Procedure*>(procedures.Push(&nested[group+3].offset,12,list->count+1));
                count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&count[0].offset);
                for(U32 j=0;j<list->count;++j) {
                    count[j+1].name=2974916843u;
                    auto bytes=static_cast<unsigned char*>(list->data)+(group==0?48:20)*j;
                    if(group==0) tokenizeShape(bytes,&count[j+1].offset);
                    else {
                        auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,3));
                        for(unsigned k=0;k<2;++k) {item[k].name=ContactPointProc[k].name;item[k].type=ContactPointProc[k].type;procedures.BookmarkMemberOffset(bytes+ContactPointProc[k].offset,&item[k].offset);}
                        procedures.Pop();
                    }
                }
                procedures.Pop();
            }
            procedures.Pop();
        }
        else if(t.offset==556 && crusher) {
            auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,14));
            const U32 keys[]={910974594u,3643225156u,3253443162u,3004652270u,2213912127u,3110872486u,4183563336u,1698114463u,797901100u,4120968022u,3910970335u,3401922681u,4110060450u};
            const U32 types[]={924736962u,924736962u,602296504u,1683514611u,2011428087u,2011428087u,2011428087u,1683514611u,1683514611u,2011428087u,3494305058u,2011428087u,2425485691u};
            const unsigned offsets[]={28,29,0,4,30,31,32,8,12,33,16,34};
            for(unsigned j=0;j<13;++j) {nested[j].name=keys[j];nested[j].type=types[j];if(j<12) procedures.BookmarkMemberOffset(crusher+offsets[j],&nested[j].offset);}
            auto count=static_cast<Procedure*>(procedures.Push(&nested[12].offset,12,extraLevels->count+1));
            count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(extraLevels,&count[0].offset);
            for(U32 j=0;j<extraLevels->count;++j) {
                count[j+1].name=2974916843u;
                auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,4));
                const U32 names[]={910974594u,3643225156u,4049621940u};const U32 types2[]={924736962u,924736962u,359808523u};const unsigned off[]={4,5,0};
                for(unsigned k=0;k<3;++k) {item[k].name=names[k];item[k].type=types2[k];procedures.BookmarkMemberOffset(static_cast<unsigned char*>(extraLevels->data)+8*j+off[k],&item[k].offset);}
                procedures.Pop();
            }
            procedures.Pop();procedures.Pop();
        }
        else if(t.offset==560) {
            auto count=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,projections->count+1));
            count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(projections,&count[0].offset);
            for(U32 j=0;j<projections->count;++j) {
                count[j+1].name=2974916843u;
                auto item=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,9));auto bytes=static_cast<unsigned char*>(projections->data)+112*j;
                const U32 keys[]={1328662213u,3519330798u,3300370196u,1859875231u,2524881596u,1464587907u,789445011u,3484925126u};
                const U32 types[]={1683514611u,1683514611u,4098281327u,2814214430u,1742454729u,1683514611u,2011428087u,3832456114u};
                const unsigned offsets[]={0,4,8,12,40,100,108,104};
                for(unsigned k=0;k<8;++k) {item[k].name=keys[k];item[k].type=types[k];if(k==1) {auto radius=*reinterpret_cast<float**>(bytes+4);if(radius) procedures.BookmarkMemberOffset(radius,&item[k].offset);}else if(k==7) {if(projectionFilters[j]) TokenizeObjectFilter(projectionFilters[j],&item[k].offset,procedures);}else procedures.BookmarkMemberOffset(bytes+offsets[k],&item[k].offset);}
                procedures.Pop();
            }
            procedures.Pop();
        }
        else if(leafObjects.count(t.offset)) {
            for(const auto& leaf:leafLayouts) if(leaf.offset==t.offset) {
                auto nested=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,leaf.count+1));
                for(unsigned j=0;j<leaf.count;++j) {
                    nested[j].name=leaf.tokens[j].name;nested[j].type=leaf.tokens[j].type;
                    procedures.BookmarkMemberOffset(leafObjects.at(t.offset)+leaf.tokens[j].offset,&nested[j].offset);
                }
                procedures.Pop();break;
            }
        }
        else if(t.kind==GameObjectTokenKind::List) {
            auto found=lists.find(t.offset);auto list=found==lists.end()?nullptr:found->second;
            const U32 length=list?list->count:0;
            auto count=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,length+1));
            count->name=1820487795u;count->type=1564774360u;
            procedures.BookmarkMemberOffset(root+t.offset,&count->offset);
            for(U32 j=0;j<length;++j) {
                count[j+1].name=2974916843u;count[j+1].type=0;
                if(t.offset==412 || t.offset==420) {
                    // Original item Tokenize methods 96970 / 97024: 3 members + sentinel.
                    const U32 armorNames[]={735930353u,2905958645u,912300027u};
                    const U32 armorTypes[]={887689851u,2593517055u,3985361129u};
                    const U32 locoNames[]={2322663157u,45929675u,728601449u};
                    const U32 locoTypes[]={1683514611u,2220692146u,4275178604u};
                    auto nested=static_cast<Procedure*>(procedures.Push(&count[j+1].offset,12,4));
                    for(unsigned k=0;k<3;++k) {
                        nested[k].name=t.offset==412?armorNames[k]:locoNames[k];nested[k].type=t.offset==412?armorTypes[k]:locoTypes[k];
                        procedures.BookmarkMemberOffset(static_cast<unsigned char*>(list->data)+12*j+4*k,&nested[k].offset);
                    }
                    procedures.Pop();
                } else {
                    const unsigned stride=t.offset==40 || t.offset==536?8:4;
                    procedures.BookmarkMemberOffset(static_cast<unsigned char*>(list->data)+stride*j,&count[j+1].offset);
                }
            }
            procedures.Pop();
        }
    }
    if(bigEndian) {
        EndianAI(skirmishAI,0);
        if(bodyModule) EndianModule(bodyModule);
        for(const auto& entry:moduleLists) {
            auto list=entry.second;
            for(U32 j=0;j<list->count;++j) {auto module=static_cast<U32**>(list->data)[j];EndianModule(module);}
            if(entry.first!=472) Swap(&list->count);
        }
        if(voiceLists) for(unsigned group=0;group<3;++group) {
            auto list=voiceLists+group;unsigned stride=group==1?16:12;
            for(U32 j=0;j<list->count;++j) {auto at=static_cast<unsigned char*>(list->data)+stride*j;Swap(at+8);if(group==1) Swap(at+12);}
            Swap(&list->count);
        }
        for(auto list:audioArrays) if(list) {for(U32 j=0;j<list->count;++j) Swap(static_cast<U32*>(list->data)+2*j+1);Swap(&list->count);}
        if(geometry) {
            auto swapShape=[&](unsigned char* shape) {
                const unsigned offsets[]={0,4,8,16,20,24,32,36};for(auto off:offsets) Swap(shape+off);
                auto vector=*reinterpret_cast<float**>(shape+40);if(vector) for(unsigned j=0;j<3;++j) Swap(vector+j);
            };
            auto anchor=*reinterpret_cast<float**>(geometry);if(anchor) for(unsigned j=0;j<2;++j) Swap(anchor+j);
            auto placement=*reinterpret_cast<unsigned char**>(geometry+4);if(placement) swapShape(placement);
            for(U32 j=0;j<geometryShapes->count;++j) swapShape(static_cast<unsigned char*>(geometryShapes->data)+48*j);
            for(U32 j=0;j<contactPoints->count;++j) {auto at=static_cast<unsigned char*>(contactPoints->data)+20*j;Swap(at);for(unsigned k=0;k<3;++k) Swap(at+8+4*k);}
            Swap(&geometryShapes->count);Swap(&contactPoints->count);
        }
        if(crusher) {
            for(unsigned j=0;j<4;++j) Swap(crusher+4*j);
            for(U32 j=0;j<extraLevels->count;++j) Swap(static_cast<unsigned char*>(extraLevels->data)+8*j);
            Swap(&extraLevels->count);
        }
        for(U32 j=0;j<projections->count;++j) {
            auto at=static_cast<unsigned char*>(projections->data)+112*j;
            auto ry=*reinterpret_cast<float**>(at+4);if(ry) Swap(ry);
            Swap(at);for(unsigned off=8;off<=100;off+=4) Swap(at+off);
            if(projectionFilters[j]) EndianObjectFilter(projectionFilters[j]);
        }
        for(U32 i=0;i<browser.size();++i) Swap(&browserStrings[i].length);
        for(auto name:displayNames) if(name) Swap(&name->length);
        if(flammability) for(unsigned i=0;i<4;++i) Swap(flammability+i);
        if(camera) for(unsigned i=0;i<3;++i) Swap(camera+i);
        if(dependency) {
            for(unsigned j=0;j<3;++j) if(dependencyMasks[j]) for(unsigned k=0;k<(j==2?7u:15u);++k) Swap(dependencyMasks[j]+k);
            for(unsigned group=0;group<3;++group) {
                auto list=reinterpret_cast<NativeList*>(dependency+12+8*group);
                for(U32 j=0;j<list->count;++j) Swap(static_cast<U32*>(list->data)+j);
                Swap(&list->count);
            }
            if(dependencyFilter) EndianObjectFilter(dependencyFilter);
        }
        if(buildCosts) {
            for(U32 i=0;i<buildCosts->count*2;++i) Swap(static_cast<U32*>(buildCosts->data)+i);
            Swap(&buildCosts->count);
        }
        for(const auto& entry:autoObjects) for(unsigned group=0;group<2;++group) {
            auto list=reinterpret_cast<NativeList*>(entry.second+4+8*group);
            for(U32 j=0;j<list->count;++j) Swap(static_cast<U32*>(list->data)+j);
            Swap(&list->count);
        }
        for(const auto& leaf:leafLayouts) if(leafObjects.count(leaf.offset))
            for(unsigned i=0;i<leaf.count;++i) if(leaf.fields[i].kind!=LeafValue::Boolean) Swap(leafObjects.at(leaf.offset)+leaf.fields[i].offset);
        for(const auto& entry:lists) if(entry.first!=40) {
            const unsigned stride=entry.first==412 || entry.first==420?12:entry.first==536?8:4;
            auto bytes=static_cast<unsigned char*>(entry.second->data);
            for(U32 i=0;i<entry.second->count;++i) {
                Swap(bytes+stride*i);
                if(stride==12) {Swap(bytes+stride*i+4);Swap(bytes+stride*i+8);}
            }
        }
        // In this restricted surface all words below 576 are scalars, lengths,
        // zero masks/counts, or pointers overwritten by MakeRelocatable.
        for(unsigned at=4;at<576;at+=4) Swap(root+at);
        std::swap(root[576],root[577]);std::swap(root[578],root[579]);
    }
    auto result=procedures.MakeTokenized(data,bigEndian);
    result.imports.clear();return result;
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif

