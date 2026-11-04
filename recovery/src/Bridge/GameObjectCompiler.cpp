#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include <utility>
#include "XmlNodeAdapter.h"
#include "../Native/GameObject.h"
#include "../Native/ScalarParsing.h"
#using <BinaryAssetBuilder.Core.dll>
#using <System.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class GameObjectCompiler abstract sealed {
    // XIncludingReader supplies xml:base/xml:lang context after schema processing.
    // These XML context annotations have no native binary members.
    static bool IsXmlContextAttribute(XPathNavigator^ attribute) {
        return attribute->NamespaceURI=="http://www.w3.org/2000/xmlns/" ||
            (attribute->NamespaceURI=="http://www.w3.org/XML/1998/namespace" &&
             (attribute->LocalName=="base" || attribute->LocalName=="lang"));
    }
    static Native::GameObjectChild ReadDependencyFilter(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="ObjectFilter";

        auto a=node->Clone();
        if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;

            Native::GameObjectChild thing;thing.name=marshal_as<std::string>(child->LocalName);thing.text=marshal_as<std::string>(child->Value);result.children.push_back(std::move(thing));
        }
        return result;
    }
    static Native::GameObjectChild ReadDependency(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="GameDependency";

        auto a=node->Clone();
        if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            // GameDependencyType.id is pipelineOnly, not a Token/native field.
            if(a->NamespaceURI->Length==0 && a->LocalName=="id") continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;

            if(child->LocalName=="ObjectFilter") {
                result.children.push_back(ReadDependencyFilter(child));continue;
            }

            Native::GameObjectChild value;value.name=marshal_as<std::string>(child->LocalName);value.text=marshal_as<std::string>(child->Value);result.children.push_back(std::move(value));
        }
        return result;
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& source) {
        auto result=gcnew array<Byte>(static_cast<int>(source.size()));
        if(result->Length) System::Runtime::InteropServices::Marshal::Copy(IntPtr(const_cast<unsigned char*>(source.data())),result,0,result->Length);
        return result;
    }
    static Native::GameObjectChild ReadPlacementGroup(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        bool crusher=node->LocalName=="CrusherInfo";

        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            // CrusherInfo.id is XSD pipeline-only and has no native member.
            if(crusher && a->NamespaceURI->Length==0 && a->LocalName=="id") continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;

            if(!crusher) {

                auto filter=ReadDependencyFilter(child);filter.name="AllowedObjectFilter";result.children.push_back(std::move(filter));continue;
            }

            Native::GameObjectChild level;level.name="ExtraCrushLevels";
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;

                level.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            result.children.push_back(std::move(level));
        }
        return result;
    }
    static Native::GameObjectChild ReadGeometry(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;

            result.children.push_back(ReadGeometry(child));
        }
        return result;
    }
    static Native::GameObjectChild ReadAudioArray(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;

            Native::GameObjectChild entry;entry.name=marshal_as<std::string>(child->LocalName);
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;

                entry.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            result.children.push_back(std::move(entry));
        }
        return result;
    }
    static Native::GameObjectChild ReadSkirmishAI(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;auto n=child->LocalName;

            if(n=="ObjectFilter" || n=="DodgeWithUnits") {auto filter=ReadDependencyFilter(child);filter.name=marshal_as<std::string>(n);result.children.push_back(std::move(filter));}
            else result.children.push_back(ReadSkirmishAI(child));
        }
        return result;
    }
    static Native::GameObjectChild ReadSoundSelectorOverride(XPathNavigator^ node,unsigned kind) {
        Native::GameObjectChild result;result.name="Override";
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;auto name=child->LocalName;

            bool audio=kind==16?name=="EvaEventArray":(name=="AudioArrayVoice" || name=="AudioArraySound");
            if(audio) {result.children.push_back(ReadAudioArray(child));continue;}

            Native::GameObjectChild entry;entry.name=marshal_as<std::string>(name);entry.text=marshal_as<std::string>(child->Value);result.children.push_back(std::move(entry));
        }
        return result;
    }
    static Native::GameObjectChild ReadMotionMovement(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="Movement";
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            Native::GameObjectChild move;move.name=marshal_as<std::string>(child->LocalName);std::string typeId=marshal_as<std::string>(child->GetAttribute("TypeId",""));move.attributes["TypeId"]=typeId;
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;

                move.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            }while(attrs->MoveToNextAttribute());
            auto paths=child->SelectChildren(XPathNodeType::Element);while(paths->MoveNext()) {
                auto path=paths->Current;
                Native::GameObjectChild item;item.name="Path";item.text=marshal_as<std::string>(path->Value);move.children.push_back(std::move(item));
            }
            result.children.push_back(std::move(move));
        }
        return result;
    }
    static Native::GameObjectChild ReadDrawShaderConstants(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="Constants";
        auto rows=node->SelectChildren(XPathNodeType::Element);
        while(rows->MoveNext()){
            auto row=rows->Current;Native::GameObjectChild entry;entry.name=marshal_as<std::string>(row->LocalName);
            auto attrs=row->Clone();if(attrs->MoveToFirstAttribute())do{if(IsXmlContextAttribute(attrs))continue;entry.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);}while(attrs->MoveToNextAttribute());
            auto values=row->SelectChildren(XPathNodeType::Element);while(values->MoveNext()){
                auto value=values->Current;
                Native::GameObjectChild v;v.name="Value";v.text=marshal_as<std::string>(value->Value);entry.children.push_back(std::move(v));
            }
            result.children.push_back(std::move(entry));
        }
        return result;
    }
    static Native::GameObjectChild ReadModuleNode(XPathNavigator^ node,unsigned kind,const std::string& path) {
        if(((kind==283 || kind==285) && path=="FXShader/Constants") || (kind>=286 && kind<=290 && path=="ModelConditionState/Material/Constants"))return ReadDrawShaderConstants(node);
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        if(kind==80 && path=="Movement") return ReadMotionMovement(node);
        unsigned shape=Native::GameObjectModuleNodeKind(kind,path);

        if((kind==278 && path=="CanEnterFilter") || (kind>=280 && kind<=282 && (path=="FadeFilter" || path=="PassengerFilter" || path=="PassengerData/Filter")) || (kind>=270 && kind<=277 && (path=="PassengerFilter" || path=="PassengerData/Filter" || (kind==277 && path=="CanAlwaysEnterObjectFilter"))) || (kind==254 && (path=="InstaKillObjectFilter" || path=="CollidesWithOnTeleport")) || (kind==256 && path=="Filter") || (kind==259 && (path=="Filter" || path=="KillFilter")) || (kind==262 && path=="ObjectEffectTuple/ObjectFilter") || (kind==247 && (path=="AllowFilter" || path=="ContainerFilter")) || (kind==249 && path=="Queries/ObjectFilter") || (kind==250 && (path=="TauntAndPointExcluded" || path=="AfraidOf" || path=="AlwaysAfraidOf" || path=="PointAt")) || (kind==224 && path=="PreventEnteringFilter") || (kind==217 && path=="ReplaceObjectNugget/TargetObjectFilter") || (kind==218 && (path=="ObjectFilter" || path=="IgnoreInsideToInsideCheck")) || (kind==214 && (path=="NearestSecondaryObjectFilter" || path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || (kind==206 && path=="AttributeModifierAffects") || (kind==207 && path=="AcceptanceFilter") || (kind==198 && path=="NearestSecondaryObjectFilter") || (((kind>=198 && kind<=199) || (kind>=162 && kind<=190)) && (path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || ((kind>=165 && kind<=187) && (path=="CombineWithObjectFilter" || path=="AcceptObjectFilter")) || ((kind>=162 && kind<=190) && (path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || (kind==119 && (path=="HintObject/ObjectFilter" || path=="PlacementRestriction/RestrictedObjectFilter")) || (kind==120 && path=="RequirementObjectFilter") || ((kind==112 || kind==115 || kind==116) && path=="ForVoiceRetreatThisIsASafeHarborToObjectFilter") || (kind==118 && path=="Filter") || path=="ObjectFilter" || path=="BroadcastObjectFilter" || path=="ProductionModifier/ModifierFilter" || (kind==103 && path=="BlockingUnits") || (kind==129 && path=="CanParkHereFilter") || (kind==137 && path=="ConnectingObjectFilter") || (kind==141 && path=="AcceptableFriends") || (kind==146 && path=="RequiresNearbyObjectFilter") || (kind==149 && (path=="DamageFilter" || path=="HealFilter")) || (kind==151 && path=="Filter") || (kind==161 && path=="KeepAliveObjectFilter") || ((kind==91 || (kind>=104 && kind<=111)) && path.size()>=7 && path.substr(path.size()-7)=="/Filter")) {auto filter=ReadDependencyFilter(node);filter.name=marshal_as<std::string>(node->LocalName);return filter;}
        auto attrs=node->Clone();if(attrs->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;
            // Shared chooser ObjectVisionRangeItem inherits BaseAssetType.id identity metadata; original Marshal ignores it.
            if(node->LocalName=="ObjectVisionRange" && attrs->NamespaceURI->Length==0 && attrs->LocalName=="id") continue;

            result.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        }while(attrs->MoveToNextAttribute());
        auto children=node->SelectChildren(XPathNodeType::Element);
        while(children->MoveNext()) {
            auto child=children->Current;auto subpath=path+"/"+marshal_as<std::string>(child->LocalName);

            result.children.push_back(ReadModuleNode(child,kind,subpath));
        }
        if(shape==3 || shape==4) result.text=marshal_as<std::string>(node->Value);return result;
    }
    static Native::GameObjectChild ReadModules(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;Native::GameObjectChild item;item.name=marshal_as<std::string>(child->LocalName);

            auto typeId=marshal_as<std::string>(child->GetAttribute("TypeId",""));
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs)) continue;

                item.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            if(unsigned kind=Native::GameObjectSoundSelectorKind(typeId)) {
                auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;auto name=value->LocalName;

                    if(kind==13) {

                        item.children.push_back(ReadAudioArray(value));
                    } else {

                        item.children.push_back(ReadSoundSelectorOverride(value,kind));
                    }
                }
                result.children.push_back(std::move(item));continue;
            }
            if(unsigned kind=Native::GameObjectRecursiveModuleKind(typeId)) {
                auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;std::string path=marshal_as<std::string>(value->LocalName);

                    item.children.push_back(ReadModuleNode(value,kind,path));
                }
                result.children.push_back(std::move(item));continue;
            }
            auto nested=child->SelectChildren(XPathNodeType::Element);
            while(nested->MoveNext()) {
                auto value=nested->Current;Native::GameObjectChild entry;entry.name=marshal_as<std::string>(value->LocalName);
                if(entry.name=="ObjectFilter" && value->NamespaceURI==child->NamespaceURI && Native::GameObjectModuleNestedSupported(typeId,entry.name)) {
                    item.children.push_back(ReadDependencyFilter(value));continue;
                }

                auto a=value->Clone();if(a->MoveToFirstAttribute()) do {
                    if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;

                    entry.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
                } while(a->MoveToNextAttribute());
                if(entry.name=="Key" || entry.name=="UpgradeRequired" || entry.name=="UpgradeForbidden" || entry.name=="TriggeredBy" || entry.name=="ConflictsWith") entry.text=marshal_as<std::string>(value->Value);
                item.children.push_back(std::move(entry));
            }
            result.children.push_back(std::move(item));
        }

        return result;
    }
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr) throw gcnew ArgumentNullException("node");

        auto nav=node->CreateNavigator();auto attrs=nav->Clone();Native::AttributeMap input;
        if(attrs->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(attrs)) continue;

            if(attrs->LocalName=="id" || attrs->LocalName=="TypeId") continue;
            input[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        } while(attrs->MoveToNextAttribute());
        std::vector<Native::GameObjectChild> children;
        auto nodes=nav->SelectChildren(XPathNodeType::Element);

        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            auto name=child->LocalName;

            if(name=="AudioArrayVoice" || name=="AudioArraySound" || name=="EvaEventArray") {children.push_back(ReadAudioArray(child));continue;}
            if(name=="Draws" || name=="Behaviors" || name=="AI" || name=="Body" || name=="ClientUpdates" || name=="ClientBehaviors") {children.push_back(ReadModules(child));continue;}
            if(name=="SkirmishAIInformation") {children.push_back(ReadSkirmishAI(child));continue;}
            if(name=="Geometry") {children.push_back(ReadGeometry(child));continue;}
            if(name=="GameDependency") {children.push_back(ReadDependency(child));continue;}
            if(name=="CrusherInfo" || name=="ProjectedBuildabilityInfo") {children.push_back(ReadPlacementGroup(child));continue;}
            bool autoSet=name=="AutoResolveArmor" || name=="AutoResolveWeapon";
            bool resourceInfo=name=="ObjectResourceInfo";

            Native::GameObjectChild item;item.name=marshal_as<std::string>(name);item.text=marshal_as<std::string>(child->Value);
            if(autoSet || resourceInfo) {
                auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;

                    Native::GameObjectChild upgrade;upgrade.name=marshal_as<std::string>(value->LocalName);upgrade.text=marshal_as<std::string>(value->Value);
                    auto va=value->Clone();
                    if(va->MoveToFirstAttribute()) do {
                        if(IsXmlContextAttribute(va)) continue;

                        if(va->LocalName!="TypeId") upgrade.attributes[marshal_as<std::string>(va->LocalName)]=marshal_as<std::string>(va->Value);
                    } while(va->MoveToNextAttribute());

                    item.children.push_back(std::move(upgrade));
                }

            }
            auto a=child->Clone();
            if(a->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(a)) continue;

                if(a->LocalName=="TypeId") continue;
                // ArmorTemplateSet.id and LocomotorSet.id are pipelineOnly in AssetTypeGameObject.xsd.
                if((name=="ArmorSet" || name=="LocomotorSet") && a->LocalName=="id") continue;

                item.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
            } while(a->MoveToNextAttribute());

            children.push_back(std::move(item));

        }
        try {
            auto compiled=Native::CompileGameObject(input,children,bigEndian);auto buffer=gcnew AssetBuffer();
            buffer->InstanceData=Copy(compiled.instance);buffer->RelocationData=Copy(compiled.relocations);buffer->ImportsData=Copy(compiled.imports);return buffer;
        } catch(const Native::GameObjectFormatError& error) {
            throw gcnew BinaryAssetBuilder::BinaryAssetBuilderException(BinaryAssetBuilder::ErrorCode::XmlFormattingError,gcnew String(error.what()),gcnew array<Object^>(0));
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr) throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2486173485u) throw gcnew NotSupportedException("Expected GameObject type.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
