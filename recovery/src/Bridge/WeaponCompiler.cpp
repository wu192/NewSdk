#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include <utility>
#include "XmlNodeAdapter.h"
#include "../Native/WeaponTemplate.h"
#include "../Native/EnumBitset.h"
#include "../Native/ScalarParsing.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class WeaponCompiler abstract sealed {
    static array<Byte>^ Copy(const std::vector<unsigned char>& source) {
        auto result = gcnew array<Byte>(static_cast<int>(source.size()));
        if (result->Length) System::Runtime::InteropServices::Marshal::Copy(
            IntPtr(const_cast<unsigned char*>(source.data())), result, 0, result->Length);
        return result;
    }
    // BAB ValidateInstances annotates every named schema element, including
    // non-polymorphic helpers and the root. Only nugget dispatch consumes TypeId.
    static bool IsTypeAnnotation(XPathNavigator^ attribute) {
        return attribute->NamespaceURI->Length==0 && attribute->LocalName=="TypeId";
    }
    static Native::ObjectFilterInput ReadFilter(XPathNavigator^ node) {
        Native::ObjectFilterInput result;

        auto attrs = node->Clone();
        if (attrs->MoveToFirstAttribute()) do {
            if (attrs->NamespaceURI == "http://www.w3.org/2000/xmlns/" || IsTypeAnnotation(attrs)) continue;

            result.attributes[marshal_as<std::string>(attrs->LocalName)] = marshal_as<std::string>(attrs->Value);
        } while (attrs->MoveToNextAttribute());
        auto children = node->SelectChildren(XPathNodeType::Element);
        while (children->MoveNext()) {
            auto child = children->Current;

            auto value = marshal_as<std::string>(child->Value);
            if (child->LocalName == "IncludeThing") result.includeThings.push_back(std::move(value)); else result.excludeThings.push_back(std::move(value));
        }
        return result;
    }
    static std::string ReadUpgrade(XPathNavigator^ node) {

        return marshal_as<std::string>(node->Value);
    }
    static Native::NuggetScalarInput ReadScalar(XPathNavigator^ node) {
        Native::NuggetScalarInput result;auto attrs=node->Clone();
        if(attrs->MoveToFirstAttribute()) do {
            if(attrs->NamespaceURI == "http://www.w3.org/2000/xmlns/" || IsTypeAnnotation(attrs)) continue;

            result.scalar=marshal_as<std::string>(attrs->Value);
        } while(attrs->MoveToNextAttribute());
        auto children=node->SelectChildren(XPathNodeType::Element);
        while(children->MoveNext()) {
            auto child=children->Current;

            if(child->LocalName == "Filter") {
                auto filter=ReadFilter(child);if(!result.hasFilter) {result.filter=std::move(filter);result.hasFilter=true;}
            } else if(child->LocalName == "RequiredUpgrade") result.requiredUpgrades.push_back(ReadUpgrade(child));

        }
        return result;
    }
    static Native::AttributeMap ReadProjectileAttributes(XPathNavigator^ node) {
        Native::AttributeMap result;auto attrs=node->Clone();
        if(attrs->MoveToFirstAttribute()) do {
            if(attrs->NamespaceURI == "http://www.w3.org/2000/xmlns/" || IsTypeAnnotation(attrs)) continue;

            result[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        } while(attrs->MoveToNextAttribute());

        return result;
    }
    static Native::WeaponNuggetInput ReadNugget(XPathNavigator^ node) {
        Native::WeaponNuggetInput result;auto attrs=node->Clone();
        if(attrs->MoveToFirstAttribute()) do {
            if(attrs->NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;

            if(attrs->LocalName == "id") continue; // pipeline-only, not emitted
            if(attrs->LocalName == "TypeId") result.typeId=Native::ParseUnsigned(marshal_as<std::string>(attrs->Value));
            else result.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        } while(attrs->MoveToNextAttribute());
        auto children=node->SelectChildren(XPathNodeType::Element);
        while(children->MoveNext()) {
            auto child=children->Current;

            if(child->LocalName == "SpecialObjectFilter") {
                auto filter=ReadFilter(child);if(!result.hasSpecialFilter) {result.specialFilter=std::move(filter);result.hasSpecialFilter=true;}
            } else if(child->LocalName == "RequiredUpgrade") result.requiredUpgrades.push_back(ReadUpgrade(child));
            else if(child->LocalName == "ForbiddenUpgrade") result.forbiddenUpgrades.push_back(ReadUpgrade(child));
            else if(child->LocalName == "AttackOffset") {
                auto value=ReadProjectileAttributes(child);if(!result.hasAttackOffset) {result.attackOffset=std::move(value);result.hasAttackOffset=true;}
            } else if(child->LocalName == "SpecificBarrelOverride") {
                auto value=ReadUpgrade(child);if(!result.hasBarrel) {result.barrel=std::move(value);result.hasBarrel=true;}
            } else if(child->LocalName == "VeterancyProjectiles") {
                Native::VeteranProjectileInput value;value.attributes=ReadProjectileAttributes(child);result.veterans.push_back(std::move(value));
            }
            else if(child->LocalName == "DamageScalarDetails") result.damageScalars.push_back(ReadScalar(child));
            else if(child->LocalName == "SpawnTemplate") {
                auto value=ReadUpgrade(child);if(!result.hasSpawnTemplate) {result.spawnTemplate=std::move(value);result.hasSpawnTemplate=true;}
            } else if(child->LocalName == "Filter") {
                auto value=ReadFilter(child);if(!result.hasTargetFilter) {result.targetFilter=std::move(value);result.hasTargetFilter=true;}
            } else if(child->LocalName == "DamageObjectFilter") {
                auto value=ReadFilter(child);if(!result.hasDamageFilter) {result.damageFilter=std::move(value);result.hasDamageFilter=true;}
            } else if(child->LocalName == "KillObjectFilter") {
                auto value=ReadFilter(child);if(!result.hasKillFilter) {result.killFilter=std::move(value);result.hasKillFilter=true;}
            } else if(child->LocalName == "Color") {
                auto value=ReadProjectileAttributes(child);

                if(!result.hasColor) {result.color=std::move(value);result.hasColor=true;}
            }
            else throw gcnew NotSupportedException("Unrecovered nugget child: "+child->Name);
        }
        return result;
    }
    static Native::WeaponScatterInput ReadScatter(XPathNavigator^ node) {
        Native::WeaponScatterInput result;
        auto attrs = node->Clone();
        if (attrs->MoveToFirstAttribute()) do {
            if (attrs->NamespaceURI == "http://www.w3.org/2000/xmlns/" || IsTypeAnnotation(attrs)) continue;

            if (attrs->LocalName == "Radius") result.radius = marshal_as<std::string>(attrs->Value);
            else result.modelConditions = marshal_as<std::string>(attrs->Value);
        } while (attrs->MoveToNextAttribute());
        auto children = node->SelectChildren(XPathNodeType::Element);
        while (children->MoveNext()) {
            auto child = children->Current;

            auto filter = ReadFilter(child);
            if (!result.hasFilter) { result.filter=std::move(filter); result.hasFilter = true; }
        }
        return result;
    }
public:
    static array<UInt32>^ MarshalModelCondition(String^ text, array<UInt32>^ initial) {
        if (text == nullptr) throw gcnew ArgumentNullException("text");

        if (initial == nullptr || initial->Length != 15) throw gcnew ArgumentException("ModelCondition requires fifteen words.", "initial");
        std::uint32_t words[15];
        for (int i = 0; i < 15; ++i) words[i] = initial[i];
        Native::MarshalModelCondition(marshal_as<std::string>(text), words);
        auto result = gcnew array<UInt32>(15);
        for (int i = 0; i < 15; ++i) result[i] = words[i];
        return result;
    }
    // Direct inline helper, including accumulation into a previously populated mask.
    static array<UInt32>^ MarshalObjectStatus(String^ text, array<UInt32>^ initial) {
        if (text == nullptr) throw gcnew ArgumentNullException("text");

        if (initial == nullptr || initial->Length != 7) throw gcnew ArgumentException("ObjectStatus requires seven words.", "initial");
        std::uint32_t words[7];
        for (int i = 0; i < 7; ++i) words[i] = initial[i];
        Native::MarshalObjectStatus(marshal_as<std::string>(text), words);
        auto result = gcnew array<UInt32>(7);
        for (int i = 0; i < 7; ++i) result[i] = words[i];
        return result;
    }
    // Partial WeaponTemplate entry point. Unsupported structures are rejected.
    static AssetBuffer^ CompileXml(XmlNode^ node, XmlNamespaceManager^ namespaces, bool bigEndian) {
        if (node == nullptr) throw gcnew ArgumentNullException("node");
        auto children = node->CreateNavigator()->SelectChildren(XPathNodeType::Element);
        String^ assetNamespace = node->NamespaceURI;
        String^ modifierNamespace = namespaces->LookupNamespace("ea");
        std::map<std::string, Native::WeaponDurationInput> durations;
        std::map<std::string, Native::WeaponOptionalInput> optionalBlocks;
        std::map<std::string, Native::ObjectFilterInput> filters;
        std::vector<Native::WeaponScatterInput> scatter;
        std::vector<Native::WeaponNuggetInput> nuggets;
        std::vector<std::string> incompatibleModifiers;
        bool readNuggets = false;
        // Read each child once. Native marshaling still emits schema order,
        // and singleton blocks retain the first matching sibling.
        while (children->MoveNext()) {
            auto child = children->Current;
            String^ name = child->LocalName;
            if (name == "IncompatibleAttributeModifier" && child->NamespaceURI == modifierNamespace)
                incompatibleModifiers.push_back(marshal_as<std::string>(child->Value));
            if (child->NamespaceURI != assetNamespace) continue;
            if (name == "Nuggets") {
                if (!readNuggets) {
                    auto list = child->SelectChildren(XPathNodeType::Element);
                    while (list->MoveNext()) nuggets.push_back(ReadNugget(list->Current));
                    readNuggets = true;
                }
            } else if (name == "ScatterRadiusVsType") {
                scatter.push_back(ReadScatter(child));
            } else if (name == "SurpriseAttackObjectFilter" || name == "CombinedAttackObjectFilter" || name == "HitStoredObjectFilter") {
                auto key = marshal_as<std::string>(name);
                if (!filters.count(key)) filters.emplace(std::move(key), ReadFilter(child));
            } else if (name == "PreAttackDelay" || name == "FiringDuration" || name == "CoolDownDelayBetweenShots" || name == "ClipReloadTime") {
                auto key = marshal_as<std::string>(name);
                if (!durations.count(key)) {
                    Native::WeaponDurationInput duration;
                    // An absent or empty duration attribute both marshal as zero.
                    duration.minSeconds = marshal_as<std::string>(child->GetAttribute("MinSeconds", ""));
                    duration.maxSeconds = marshal_as<std::string>(child->GetAttribute("MaxSeconds", ""));
                    durations.emplace(std::move(key), std::move(duration));
                }
            } else if (name == "OverrideVoiceAttackSound" || name == "OverrideVoiceEnterStateAttackSound" || name == "WeaponAiHintInfo") {
                auto key = marshal_as<std::string>(name);
                if (!optionalBlocks.count(key)) optionalBlocks.emplace(std::move(key), ReadProjectileAttributes(child));
            }
        }
        Native::AttributeMap attributes;
        for each (XmlAttribute^ attribute in node->Attributes) {
            if (attribute->NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;

            if (attribute->Name == "id" || attribute->Name == "TypeId") continue;
            attributes[marshal_as<std::string>(attribute->Name)] = marshal_as<std::string>(attribute->Value);
        }
        try {
            auto chunk = Native::CompileWeapon(attributes, durations, optionalBlocks, incompatibleModifiers, filters, scatter, nuggets, bigEndian);
            auto buffer = gcnew AssetBuffer();
            buffer->InstanceData = Copy(chunk.instance);
            buffer->RelocationData = Copy(chunk.relocations);
            buffer->ImportsData = Copy(chunk.imports);
            return buffer;
        } catch (const std::invalid_argument& error) {
            throw gcnew NotSupportedException(gcnew String(error.what()));
        }
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration, TargetPlatform platform) {
        if (declaration == nullptr) throw gcnew ArgumentNullException("declaration");
        if (declaration->Handle->TypeId != 2496977262u) throw gcnew NotSupportedException("Only WeaponTemplate is handled by this component.");
        return CompileXml(declaration->Node, declaration->Document->NamespaceManager, platform != TargetPlatform::Win32);
    }
};
}

