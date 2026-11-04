#pragma once
#include <cstddef>
#include <cstdint>
#include <string>
namespace Recovered { namespace Native {
struct EnumNameValue { const char* name; int value; };
// Mutates an existing bitset, like the original inline Marshal helper.
void MarshalEnumBitset(const std::string& text, const EnumNameValue* names,
    std::size_t nameCount, int fallback, unsigned bitCount, std::uint32_t* words, bool sortedNames = false);
void MarshalModelCondition(const std::string& text, std::uint32_t* fifteenWords);
void MarshalObjectStatus(const std::string& text, std::uint32_t* sevenWords);
}}
