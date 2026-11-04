#pragma once
#include "AttributeMap.h"
#include "Relocation.h"
#include <map>
#include <string>
namespace Recovered { namespace Native {
struct ObjectFilterInput {
    AttributeMap attributes;
    std::vector<std::string> includeThings, excludeThings;
};
struct ObjectFilter {
    std::uint32_t id, rule, relationship, alignment;
    std::uint32_t include[9], exclude[9];
    std::uint32_t* status; std::uint32_t* statusExclude;
    std::uint32_t includeCount; std::uint32_t* includeThings;
    std::uint32_t excludeCount; std::uint32_t* excludeThings;
};
void MarshalObjectFilter(const ObjectFilterInput& input, ObjectFilter* object, Relo::Tracker& data);
void TokenizeObjectFilter(ObjectFilter* object, void** table, Relo::Tracker& tokens);
void EndianObjectFilter(ObjectFilter* object);
}}
