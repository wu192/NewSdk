#pragma once
#include "AttributeMap.h"
#include "Relocation.h"
#include <map>
#include <string>
#include <stdexcept>
namespace Recovered { namespace Native {
class GameObjectFormatError : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};
enum class GameObjectValue { Float, Signed, Unsigned, Short, Byte, Boolean, Time, Angle, String, StringList, Bits, Enum, AssetId, Reference, DurationDefault };
struct GameObjectField { const char* name; unsigned offset; GameObjectValue kind; const char* fallback; int defaultValue; };
const GameObjectField* GameObjectFields();
unsigned GameObjectFieldCount();
unsigned GameObjectSoundSelectorKind(const std::string& typeId);
bool GameObjectModuleNestedSupported(const std::string& typeId, const std::string& name);
unsigned GameObjectRecursiveModuleKind(const std::string& typeId);
unsigned GameObjectModuleNodeKind(unsigned kind,const std::string& path);
bool GameObjectMotionMovePaths(const std::string& typeId);
struct GameObjectChild {
    std::string name, text;
    AttributeMap attributes;
    std::vector<GameObjectChild> children;
};
// Restricted component; unsupported attributes and child blocks are rejected.
Relo::Chunk CompileGameObject(const AttributeMap& attributes, const std::vector<GameObjectChild>& children, bool bigEndian);
}}

