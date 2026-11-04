#include "Xmljoypad.h"
#include "ObjectFilter.h"
#include "ResourceMarshaling.h"
#include "ScalarParsing.h"
#include <cstring>
#include <stdexcept>

#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native { namespace JoypadDetail {
using Relo::U32;
using Node = XmljoypadNode;
struct EnumEntry { const char* name; int value; };
struct RootLayout { const char* name; U32 id; unsigned size; };
struct PayloadLayout { U32 id; unsigned size; };
#include "XmljoypadLayouts.inc"

const Node* Child(const Node& node, const char* name) {
    for (const auto& child : node.children)
        if (child.name == name && child.namespaceUri == node.namespaceUri) return &child;
    return nullptr;
}
void Swap(void* target) {
    U32 value; std::memcpy(&value, target, 4);
    value = (value << 24) | ((value << 8) & 0xff0000u) | ((value >> 8) & 0xff00u) | (value >> 24);
    std::memcpy(target, &value, 4);
}
struct Builder {
    Relo::Tracker tracker;
    std::vector<void*> words;
    std::vector<ObjectFilter*> filters;

    void Word(void* target, U32 value) {
        std::memcpy(target, &value, 4); words.push_back(target);
    }
    void Ref(const Node& node, const char* name, void* target) {
        auto value = node.attributes.find(name);
        if (value != node.attributes.end()) MarshalReference(tracker, target, value->second);
    }
    void Unsigned(const Node& node, const char* name, void* target, const char* fallback = nullptr) {
        auto value = node.attributes.find(name);
        if (value != node.attributes.end() || fallback)
            Word(target, ParseUnsigned(value == node.attributes.end() ? fallback : value->second));
    }
    void Float(const Node& node, const char* name, void* target, const char* fallback = nullptr) {
        auto value = node.attributes.find(name);
        if (value != node.attributes.end() || fallback) {
            float number = ParseFloat(value == node.attributes.end() ? fallback : value->second);
            std::memcpy(target, &number, 4); words.push_back(target);
        }
    }
    void Hash(const Node& node, const char* name, void* target) {
        auto value = node.attributes.find(name);
        if (value != node.attributes.end()) Word(target, HashAssetId(value->second));
    }
    template<unsigned count> void Enum(const Node& node, const char* name, void* target,
                                      const EnumEntry (&entries)[count], int unknown,
                                      const char* fallback = nullptr) {
        auto value = node.attributes.find(name);
        if (value == node.attributes.end() && !fallback) return;
        const char* text = value == node.attributes.end() ? fallback : value->second.c_str();
        int result = unknown;
        for (const auto& entry : entries) if (std::strcmp(text, entry.name) == 0) { result = entry.value; break; }
        Word(target, static_cast<U32>(result));
    }
    template<class F> void List(const Node& node, const char* name, unsigned char* target,
                                unsigned size, F marshal) {
        U32 count = 0;
        for (const auto& child : node.children)
            if (child.name == name && child.namespaceUri == node.assetNamespace) ++count;
        Word(target, count);
        if (count) {
            auto items = static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(target + 4), size, count));
            unsigned i = 0;
            for (const auto& child : node.children)
                if (child.name == name && child.namespaceUri == node.assetNamespace) marshal(child, items + size * i++);
            tracker.Pop();
        }
    }
    void Refs(const Node& node, const char* name, unsigned char* target) {
        List(node, name, target, 4, [&](const Node& item, unsigned char* out) {
            MarshalReference(tracker, out, item.value);
        });
    }
    void Tint(const Node& node, unsigned char* target) {
        const auto color = Child(node, "ValidTargetTintColor");
        if (!color) return;
        auto rgb = static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(target), 12, 1));
        Float(*color, "R", rgb); Float(*color, "G", rgb + 4); Float(*color, "B", rgb + 8);
        tracker.Pop();
    }
    void Filter(const Node& node, unsigned char* target) {
        const auto child = Child(node, "ObjectFilter");
        if (!child) return;
        ObjectFilterInput input;
        // XML's scalar enum readers compare C strings; TypeId is preprocessing metadata.
        for (const auto& attr : child->attributes) if (attr.first != "TypeId")
            input.attributes[attr.first] = (attr.first == "Rule" || attr.first == "Alignment")
                ? std::string(attr.second.c_str()) : attr.second;
        for (const auto& item : child->children) {
            if (item.namespaceUri != child->assetNamespace) continue;
            if (item.name == "IncludeThing") input.includeThings.push_back(item.value);
            else if (item.name == "ExcludeThing") input.excludeThings.push_back(item.value);
        }
        auto filter = reinterpret_cast<ObjectFilter*>(target);
        MarshalObjectFilter(input, filter, tracker); filters.push_back(filter);
    }
    void StateLogic(const Node& node, unsigned char* target) {
        Ref(node, "StateData", target + 8); Ref(node, "LogicCommand", target + 4);
    }
    void PlayerPower(const Node& node, unsigned char* target) {
        Ref(node, "SpecialPower", target + 4); Ref(node, "StateData", target + 8);
    }
    void Target(const Node& node, unsigned char* target, bool player) {
        Ref(node, "RadiusCursor", target + 12);
        Enum(node, "ValidTargetOutline", target + 16, OutlineDrawTypeValues, 3, "HOVER_ATTACK");
        Tint(node, target + 20);
        if (player) PlayerPower(node, target); else StateLogic(node, target);
    }
    void MultiplePowers(const Node& node, unsigned char* target, bool player) {
        Refs(node, "AdditionalSpecialPowerTemplate", target + 24);
        Refs(node, "AdditionalRadiusCursor", target + 32);
        Target(node, target, player);
    }
    void Payload(const Node& node, unsigned char* target) {
        auto type = node.attributes.find("TypeId");
        U32 id = type == node.attributes.end() ? 0 : ParseUnsigned(type->second);
        PayloadLayout layout = { 0, 4 };
        for (const auto& candidate : Payloads) if (candidate.id == id) { layout = candidate; break; }
        auto data = static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(target), layout.size, 1));
        switch (layout.id) {
            case 2129834919u: break; // PlaceReadyStructure has no fields.
            case 2610528252u: // ActivateHotSpot
            case 510951828u: // OpenUnitsMenu
            case 3819920017u: // SingleState
            case 3429542181u: // OpenPlayerTechStore
            case 3303154823u: // OpenCoopCommunicator
            case 2638435896u: // OpenControlGroups
            case 1938484980u: // OpenPlayerPowers
            case 1810871923u: // OpenUnitAbilities
            case 3304230519u: // RedAlert
                Ref(node, "StateData", data + 4); break;
            case 3496866284u:
                Enum(node, "QueueType", data + 8, ProductionQueueTypeValues, 4);
                Ref(node, "StateData", data + 4); break;
            case 4122740596u: // UnitAbility
            case 3432035804u: // BaseSpecialPower
                Ref(node, "LogicCommand", data + 4); break;
            case 2278403426u:
                Refs(node, "StateData", data + 8); Ref(node, "LogicCommand", data + 4); break;
            case 740107052u: // PlayerUpgrade
            case 938329078u: // ObjectUpgrade
            case 2091051053u: // Upgrade
            case 3450801933u: // ProduceStructureBase
            case 313940736u: // Unpack
            case 3272079493u: // AssembleStructure
            case 2916649698u: // Evacuate
            case 3804701620u: // SpecialPower
                StateLogic(node, data); break;
            case 3057934633u: PlayerPower(node, data); break;
            case 2577768761u: Target(node, data, false); break;
            case 2969956408u: Target(node, data, true); break;
            case 263057076u: MultiplePowers(node, data, true); break;
            case 2389969100u: MultiplePowers(node, data, false); break;
            case 3938890407u:
                Hash(node, "TargetMarkerTemplate", data + 24);
                Hash(node, "TargetMarkerFXList", data + 28);
                Unsigned(node, "TargetCount", data + 32, "1");
                Float(node, "MinTargetDistance", data + 36, "0.0");
                Refs(node, "AdditionalRadiusCursor", data + 40);
                Target(node, data, true); break;
            case 3302104001u:
                Filter(node, data + 8); Ref(node, "StateData", data + 4); break;
            case 2833918550u:
                Unsigned(node, "GroupShortcut", data + 8, "1"); Ref(node, "StateData", data + 4); break;
            case 325685730u:
                Enum(node, "Stance", data + 8, StanceValues, 1); Ref(node, "StateData", data + 4); break;
            case 1975379790u:
                Enum(node, "Directive", data + 8, AIStrategicDirectiveValues, 1); Ref(node, "StateData", data + 4); break;
            case 560773247u:
                Enum(node, "CommandMode", data + 8, CommandModeValues, 0); Ref(node, "StateData", data + 4); break;
            case 2778564342u:
                Ref(node, "MixedStanceStateData", data + 4);
                List(node, "StanceSettings", data + 8, 8, [&](const Node& item, unsigned char* out) {
                    Enum(item, "Stance", out, StanceValues, 1); Ref(item, "StateData", out + 4);
                }); break;
        }
        Word(data, layout.id); tracker.Pop();
    }
    void Root(const Node& node, unsigned char* target) {
        if (node.name == "JoypadCommandBarTemplate") {
            Ref(node, "HomeButton", target + 4);
            List(node, "Slot", target + 8, 8, [&](const Node& item, unsigned char* out) {
                Unsigned(item, "Ordinal", out); Ref(item, "Button", out + 4);
            });
            Refs(node, "GeneralButton", target + 16);
        } else if (node.name == "JoypadCommandBarButtonTemplate") {
            const auto wrapper = Child(node, "Data");
            if (wrapper && wrapper->children.size() == 1) Payload(wrapper->children.front(), target + 4);
        } else if (node.name == "UIJoypadCommandBarButtonBuild") {
            List(node, "FactionSettings", target + 4, 8, [&](const Node& item, unsigned char* out) {
                Enum(item, "Faction", out, FactionTypeValues, 0); Ref(item, "LockedButtonStateData", out + 4);
            });
        } else if (node.name == "UIJoypadCommandBarSingleUnit") {
            Ref(node, "UnitTemplate", target + 4);
            Ref(node, "ProductionStructureTemplate", target + 8);
            Ref(node, "NonProductionStructureTemplate", target + 12);
            Ref(node, "HotSpotTemplate", target + 16);
        } else Ref(node, node.name == "UIJoypadCommandBarCoopCommunicator" ? "AICoopTemplate" : "Template", target + 4);
    }
};
}
unsigned int XmljoypadTypeId(const std::string& name) {
    for (const auto& layout : JoypadDetail::Roots) if (name == layout.name) return layout.id;
    return 0;
}
Relo::Chunk CompileXmljoypad(const XmljoypadNode& input, bool bigEndian) {
    using namespace JoypadDetail;
    static_assert(sizeof(void*) == 4, "Joypad assets require x86.");
    unsigned size = 0;
    for (const auto& layout : Roots) if (input.name == layout.name) { size = layout.size; break; }
    if (!size) throw std::invalid_argument("Unsupported Joypad root.");
    Builder builder; unsigned char* root = nullptr;
    builder.tracker.Push(reinterpret_cast<void**>(&root), size, 1);
    builder.Root(input, root);
    if (bigEndian) {
        for (auto word : builder.words) Swap(word);
        for (auto filter : builder.filters) EndianObjectFilter(filter);
    }
    return builder.tracker.MakeRelocatable(bigEndian, 0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
