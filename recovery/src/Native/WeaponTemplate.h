#pragma once
#include "AttributeMap.h"
#include "Relocation.h"
#include "ObjectFilter.h"
#include "WeaponNugget.h"
#include <map>
#include <string>
namespace Recovered { namespace Native {
enum class WeaponValue { Float, Angle, Time, Percentage, Integer, Boolean, Enum, Bits, String, Reference, ReferencePointer, StatusPointer, AssetId, Absent };
enum class WeaponToken { Member, Pointer, List };
struct WeaponField {
    const char* name;
    const char* fallback;
    std::uint32_t offset, nameHash, typeHash;
    WeaponValue value;
    WeaponToken token;
    const char* enumGroup;
};
const WeaponField* WeaponFields();
unsigned WeaponFieldCount();
using WeaponOptionalInput = AttributeMap;
struct WeaponScatterInput {
    std::string radius = "0.0", modelConditions;
    bool hasFilter = false;
    ObjectFilterInput filter;
};
struct WeaponDurationInput { std::string minSeconds, maxSeconds; };
Relo::Chunk CompileWeapon(const AttributeMap& attributes,
    const std::map<std::string, WeaponDurationInput>& durations,
    const std::map<std::string, WeaponOptionalInput>& optionalBlocks,
    const std::vector<std::string>& incompatibleModifiers,
    const std::map<std::string, ObjectFilterInput>& filters,
    const std::vector<WeaponScatterInput>& scatter,
    const std::vector<WeaponNuggetInput>& nuggets, bool bigEndian);
}}
