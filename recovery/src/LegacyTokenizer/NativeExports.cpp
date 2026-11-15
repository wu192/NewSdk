#include "../Native/ArmorTemplate.h"
#include "../Native/GameObject.h"
#include "../Native/ScalarParsing.h"
#include "../Native/WeaponTemplate.h"
#include <cstring>
#include <memory>
#include <utility>

// This boundary has no C++/CLI or dynamic CRT dependency. The CLR 2.0 module
// calls these two exports in its own DLL; allocations stay on the native side.
#if defined(_MSC_VER)
#define LEGACY_CALL __cdecl
#else
#define LEGACY_CALL
#endif

struct LegacyTokenizerResult {
    const unsigned char* instance;
    unsigned instanceLength;
    const unsigned char* relocations;
    unsigned relocationLength;
    const unsigned char* imports;
    unsigned importLength;
    const char* error;
    void* owner;
};

namespace {
using namespace Recovered;
using Node = Native::GameObjectChild;
using Attributes = std::map<std::string, std::string>;

// Private wire format produced by Plugin.cs, in little-endian order:
// name, text, attribute count, (name,value)*, child count, child*.
// Each native ANSI string begins with its unsigned 32-bit byte count.
class NodeReader {
    const unsigned char* cursor_;
    unsigned Word() {
        unsigned result;
        std::memcpy(&result, cursor_, sizeof(result));
        cursor_ += sizeof(result);
        return result;
    }
    std::string Text() {
        const unsigned length = Word();
        std::string result(reinterpret_cast<const char*>(cursor_), length);
        cursor_ += length;
        return result;
    }
public:
    explicit NodeReader(const unsigned char* input) : cursor_(input) {}
    Node Read() {
        Node node;
        node.name = Text();
        node.text = Text();
        unsigned count = Word();
        for (unsigned i = 0; i < count; ++i) {
            const auto name = Text();
            node.attributes[name] = Text();
        }
        count = Word();
        for (unsigned i = 0; i < count; ++i) node.children.push_back(Read());
        return node;
    }
};

std::string Attribute(const Node& node, const char* name, const char* fallback = "") {
    const auto value = node.attributes.find(name);
    return value == node.attributes.end() ? fallback : value->second;
}

Attributes PlainAttributes(const Node& node) {
    auto result = node.attributes;
    result.erase("TypeId");
    return result;
}

bool ModuleContainer(const std::string& name) {
    return name == "Draws" || name == "Behaviors" || name == "AI" || name == "Body" ||
        name == "ClientUpdates" || name == "ClientBehaviors" || name == "Movement" ||
        name == "Constants";
}

void NormalizeGameNode(Node& node, const std::string& parent) {
    // BAB annotates all named schema elements. Only polymorphic nodes consume
    // TypeId; the restored GameObject compiler receives the same input tree as
    // its original XmlNodeAdapter bridge, without annotations on leaf helpers.
    if (!ModuleContainer(parent)) node.attributes.erase("TypeId");
    if ((parent == "GameObject" && (node.name == "ArmorSet" || node.name == "LocomotorSet")) ||
        node.name == "GameDependency" || node.name == "CrusherInfo" || node.name == "ObjectVisionRange")
        node.attributes.erase("id");
    for (auto& child : node.children) NormalizeGameNode(child, node.name);
}

Relo::Chunk Armor(const Node& node, bool bigEndian) {
    Native::ArmorInput input;
    const char* names[] = { "Default", "DamageScalar", "SideDamageScalar", "RearDamageScalar", "FlankedPenalty" };
    for (unsigned i = 0; i < 5; ++i)
        input.percentages[i] = Attribute(node, names[i], i == 4 ? "100%" : "100");
    for (const auto& child : node.children) {
        if (child.name != "Armor") continue;
        Native::ArmorEntryInput entry;
        entry.hasDamage = child.attributes.count("Damage") != 0;
        entry.hasPercent = child.attributes.count("Percent") != 0;
        entry.damage = Attribute(child, "Damage");
        entry.percent = Attribute(child, "Percent");
        input.entries.push_back(entry);
    }
    return Native::CompileArmor(input, bigEndian);
}

Native::ObjectFilterInput Filter(const Node& node) {
    Native::ObjectFilterInput input;
    input.attributes = PlainAttributes(node);
    for (const auto& child : node.children) {
        if (child.name == "IncludeThing") input.includeThings.push_back(child.text);
        else if (child.name == "ExcludeThing") input.excludeThings.push_back(child.text);
        else throw std::invalid_argument("Unsupported ObjectFilter child: " + child.name);
    }
    return input;
}

Native::NuggetScalarInput Scalar(const Node& node) {
    Native::NuggetScalarInput input;
    input.scalar = Attribute(node, "Scalar", "100%");
    for (const auto& child : node.children) {
        if (child.name == "Filter") {
            if (!input.hasFilter) { input.filter = Filter(child); input.hasFilter = true; }
        } else if (child.name == "RequiredUpgrade") input.requiredUpgrades.push_back(child.text);
        else throw std::invalid_argument("Unsupported DamageScalarDetails child: " + child.name);
    }
    return input;
}

Native::WeaponNuggetInput Nugget(const Node& node) {
    Native::WeaponNuggetInput input;
    input.typeId = Native::ParseUnsigned(Attribute(node, "TypeId", "0"));
    input.attributes = PlainAttributes(node);
    input.attributes.erase("id");
    for (const auto& child : node.children) {
        const auto& name = child.name;
        if (name == "SpecialObjectFilter") {
            if (!input.hasSpecialFilter) { input.specialFilter = Filter(child); input.hasSpecialFilter = true; }
        } else if (name == "RequiredUpgrade") input.requiredUpgrades.push_back(child.text);
        else if (name == "ForbiddenUpgrade") input.forbiddenUpgrades.push_back(child.text);
        else if (name == "AttackOffset") {
            if (!input.hasAttackOffset) { input.attackOffset = PlainAttributes(child); input.hasAttackOffset = true; }
        } else if (name == "SpecificBarrelOverride") {
            if (!input.hasBarrel) { input.barrel = child.text; input.hasBarrel = true; }
        } else if (name == "VeterancyProjectiles") {
            Native::VeteranProjectileInput value;
            value.attributes = PlainAttributes(child);
            input.veterans.push_back(value);
        } else if (name == "DamageScalarDetails") input.damageScalars.push_back(Scalar(child));
        else if (name == "SpawnTemplate") {
            if (!input.hasSpawnTemplate) { input.spawnTemplate = child.text; input.hasSpawnTemplate = true; }
        } else if (name == "Filter") {
            if (!input.hasTargetFilter) { input.targetFilter = Filter(child); input.hasTargetFilter = true; }
        } else if (name == "DamageObjectFilter") {
            if (!input.hasDamageFilter) { input.damageFilter = Filter(child); input.hasDamageFilter = true; }
        } else if (name == "KillObjectFilter") {
            if (!input.hasKillFilter) { input.killFilter = Filter(child); input.hasKillFilter = true; }
        } else if (name == "Color") {
            if (!input.hasColor) { input.color = PlainAttributes(child); input.hasColor = true; }
        } else throw std::invalid_argument("Unrecovered nugget child: " + name);
    }
    return input;
}

Relo::Chunk Weapon(const Node& node, bool bigEndian) {
    auto attributes = PlainAttributes(node);
    attributes.erase("id");
    std::map<std::string, Native::WeaponDurationInput> durations;
    std::map<std::string, Native::WeaponOptionalInput> optionalBlocks;
    std::vector<std::string> incompatibleModifiers;
    std::map<std::string, Native::ObjectFilterInput> filters;
    std::vector<Native::WeaponScatterInput> scatter;
    std::vector<Native::WeaponNuggetInput> nuggets;
    bool nuggetsSeen = false;
    for (const auto& child : node.children) {
        const auto& name = child.name;
        if (name == "PreAttackDelay" || name == "FiringDuration" ||
            name == "CoolDownDelayBetweenShots" || name == "ClipReloadTime") {
            if (!durations.count(name)) {
                Native::WeaponDurationInput value;
                value.minSeconds = Attribute(child, "MinSeconds", "0s");
                value.maxSeconds = Attribute(child, "MaxSeconds", "0s");
                durations[name] = value;
            }
        } else if (name == "OverrideVoiceAttackSound" || name == "OverrideVoiceEnterStateAttackSound" || name == "WeaponAiHintInfo") {
            if (!optionalBlocks.count(name)) optionalBlocks[name] = PlainAttributes(child);
        } else if (name == "IncompatibleAttributeModifier") incompatibleModifiers.push_back(child.text);
        else if (name == "SurpriseAttackObjectFilter" || name == "CombinedAttackObjectFilter" || name == "HitStoredObjectFilter") {
            if (!filters.count(name)) filters[name] = Filter(child);
        } else if (name == "ScatterRadiusVsType") {
            Native::WeaponScatterInput value;
            value.radius = Attribute(child, "Radius", "0.0");
            value.modelConditions = Attribute(child, "RequiredModelConditions");
            for (const auto& nested : child.children) {
                if (nested.name != "Filter") throw std::invalid_argument("Unsupported ScatterRadiusVsType child: " + nested.name);
                if (!value.hasFilter) { value.filter = Filter(nested); value.hasFilter = true; }
            }
            scatter.push_back(value);
        } else if (name == "Nuggets") {
            if (!nuggetsSeen) {
                for (const auto& nested : child.children) nuggets.push_back(Nugget(nested));
                nuggetsSeen = true;
            }
        } else throw std::invalid_argument("WeaponTemplate child structure is not reconstructed: " + name);
    }
    return Native::CompileWeapon(attributes, durations, optionalBlocks, incompatibleModifiers,
        filters, scatter, nuggets, bigEndian);
}

struct OwnedResult {
    Relo::Chunk chunk;
    std::string error;
};

void Publish(OwnedResult& owned, LegacyTokenizerResult& result) {
    result.instance = owned.chunk.instance.data();
    result.instanceLength = static_cast<unsigned>(owned.chunk.instance.size());
    result.relocations = owned.chunk.relocations.data();
    result.relocationLength = static_cast<unsigned>(owned.chunk.relocations.size());
    result.imports = owned.chunk.imports.data();
    result.importLength = static_cast<unsigned>(owned.chunk.imports.size());
    result.error = owned.error.c_str();
    result.owner = &owned;
}
}

extern "C" int LEGACY_CALL LegacyTokenizerCompile(unsigned typeId,
    const unsigned char* wire, unsigned wireLength, int platform, LegacyTokenizerResult* result) {
    (void)wireLength; // The private input is written by this assembly's Plugin.
    *result = LegacyTokenizerResult{};
    std::unique_ptr<OwnedResult> owned(new OwnedResult());
    int status = 0;
    try {
        Node node = NodeReader(wire).Read();
        const bool bigEndian = platform != 0; // TargetPlatform.Win32
        switch (typeId) {
        case 2486173485u:
            NormalizeGameNode(node, "");
            node.attributes.erase("id");
            owned->chunk = Native::CompileGameObject(node.attributes, node.children, bigEndian);
            break;
        case 2496977262u: owned->chunk = Weapon(node, bigEndian); break;
        case 980180622u: owned->chunk = Armor(node, bigEndian); break;
        default: throw std::invalid_argument("Unsupported Tokenizer asset type.");
        }
    } catch (const Native::GameObjectFormatError& error) {
        owned->error = error.what();
        status = 2;
    } catch (const std::exception& error) {
        // Native exceptions cannot cross the P/Invoke boundary. The managed
        // Plugin rethrows the existing formatting/unsupported error category.
        owned->error = error.what();
        status = 1;
    }
    Publish(*owned, *result);
    owned.release();
    return status;
}

extern "C" void LEGACY_CALL LegacyTokenizerFree(void* owner) {
    delete static_cast<OwnedResult*>(owner);
}
