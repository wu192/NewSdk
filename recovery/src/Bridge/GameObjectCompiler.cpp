#include <msclr/marshal_cppstd.h>
#include <stdexcept>
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
    static void NoStructuredText(XPathNavigator^ node) {
        auto contents=node->SelectChildren(XPathNodeType::All);
        while(contents->MoveNext()) if((contents->Current->NodeType==XPathNodeType::Text || contents->Current->NodeType==XPathNodeType::SignificantWhitespace) && contents->Current->Value->Trim()->Length!=0)
            throw gcnew NotSupportedException("Unexpected text in GameDependency structure.");
    }
    static Native::GameObjectChild ReadDependencyFilter(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="ObjectFilter";
        array<String^>^ allowed={"id","Rule","Relationship","Alignment","Include","Exclude","StatusBitFlags","StatusBitFlagsExclude"};
        auto a=node->Clone();
        if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            if(a->NamespaceURI->Length!=0 || Array::IndexOf<String^>(allowed,a->LocalName)<0) throw gcnew NotSupportedException("Unsupported dependency filter attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            if(child->NamespaceURI!=node->NamespaceURI || (child->LocalName!="IncludeThing" && child->LocalName!="ExcludeThing") || child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported dependency filter child.");
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(!IsXmlContextAttribute(attrs) && !(attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported filter thing attribute.");
            } while(attrs->MoveToNextAttribute());
            Native::GameObjectChild thing;thing.name=marshal_as<std::string>(child->LocalName);thing.text=marshal_as<std::string>(child->Value);result.children.push_back(thing);
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadDependency(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="GameDependency";
        array<String^>^ allowed={"RequiredModelConditionsAny","ForbiddenModelConditions","RequiredObjectStatusAny"};
        auto a=node->Clone();
        if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            // GameDependencyType.id is pipelineOnly, not a Token/native field.
            if(a->NamespaceURI->Length==0 && a->LocalName=="id") continue;
            if(a->NamespaceURI->Length!=0 || Array::IndexOf<String^>(allowed,a->LocalName)<0) throw gcnew NotSupportedException("Unsupported GameDependency attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        bool filterSeen=false;auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            if(child->NamespaceURI!=node->NamespaceURI) throw gcnew NotSupportedException("Foreign GameDependency child.");
            if(child->LocalName=="ObjectFilter") {
                if(filterSeen) throw gcnew NotSupportedException("Duplicate dependency ObjectFilter.");filterSeen=true;result.children.push_back(ReadDependencyFilter(child));continue;
            }
            if((child->LocalName!="RequiredObject" && child->LocalName!="ForbiddenUpgrade" && child->LocalName!="NeededUpgrade") || child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported GameDependency child.");
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(!IsXmlContextAttribute(attrs) && !(attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported dependency list attribute.");
            } while(attrs->MoveToNextAttribute());
            Native::GameObjectChild value;value.name=marshal_as<std::string>(child->LocalName);value.text=marshal_as<std::string>(child->Value);result.children.push_back(value);
        }
        NoStructuredText(node);return result;
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& source) {
        auto result=gcnew array<Byte>(static_cast<int>(source.size()));
        if(result->Length) System::Runtime::InteropServices::Marshal::Copy(IntPtr(const_cast<unsigned char*>(source.data())),result,0,result->Length);
        return result;
    }
    static Native::GameObjectChild ReadPlacementGroup(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        bool crusher=node->LocalName=="CrusherInfo";
        array<String^>^ crushAttributes={"CrusherLevel","CrushableLevel","CrushKnockback","CrushZFactor","CrushEqualLevelProps","UseCrushAttack","CrushOnlyWhileCharging","MinCrushVelocityPercent","CrushDecelerationPercent","CrushAllies","CrushWeapon","CrushWhileStationary"};
        array<String^>^ projectionAttributes={"Radius","RadiusY","BuildPlacementTypes","StatusToReject","ModelConditionsToReject","AllowedBuildabilityHeightVariation","PrimaryBuidability"};
        auto allowed=crusher?crushAttributes:projectionAttributes;
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            // CrusherInfo.id is XSD pipeline-only and has no native member.
            if(crusher && a->NamespaceURI->Length==0 && a->LocalName=="id") continue;
            if(a->NamespaceURI->Length!=0 || Array::IndexOf<String^>(allowed,a->LocalName)<0) throw gcnew NotSupportedException("Unsupported crush/placement attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);bool filterSeen=false;
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            if(child->NamespaceURI!=node->NamespaceURI || (crusher?child->LocalName!="ExtraCrushLevels":child->LocalName!="AllowedObjectFilter")) throw gcnew NotSupportedException("Unsupported crush/placement child.");
            if(!crusher) {
                if(filterSeen) throw gcnew NotSupportedException("Duplicate AllowedObjectFilter.");filterSeen=true;
                auto filter=ReadDependencyFilter(child);filter.name="AllowedObjectFilter";result.children.push_back(filter);continue;
            }
            if(child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Nested ExtraCrushLevels content.");
            Native::GameObjectChild level;level.name="ExtraCrushLevels";
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;
                if(attrs->NamespaceURI->Length!=0 || (attrs->LocalName!="CrusherLevel" && attrs->LocalName!="CrushableLevel" && attrs->LocalName!="ModelConditionMatch")) throw gcnew NotSupportedException("Unsupported ExtraCrushLevels attribute.");
                level.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            NoStructuredText(child);result.children.push_back(level);
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadGeometry(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto name=node->LocalName;
        array<String^>^ allowed;
        if(name=="Geometry") allowed=gcnew array<String^>{"IsSmall"};
        else if(name=="Shape" || name=="PlacementShape") allowed=gcnew array<String^>{"Type","ContactPointGeneration","Name","Active","UsedForHealthBox","MajorRadius","MinorRadius","Other","Height","FrontAngle"};
        else if(name=="ContactPoint") allowed=gcnew array<String^>{"Label"};
        else if(name=="RotationAnchorOffset") allowed=gcnew array<String^>{"x","y"};
        else allowed=gcnew array<String^>{"x","y","z"};
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            if(a->NamespaceURI->Length!=0 || Array::IndexOf<String^>(allowed,a->LocalName)<0) throw gcnew NotSupportedException("Unsupported Geometry attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;auto n=child->LocalName;
            bool valid=(name=="Geometry" && (n=="RotationAnchorOffset" || n=="PlacementShape" || n=="Shape" || n=="ContactPoint")) || ((name=="Shape" || name=="PlacementShape") && n=="Offset") || (name=="ContactPoint" && n=="Pos");
            if(child->NamespaceURI!=node->NamespaceURI || !valid) throw gcnew NotSupportedException("Unsupported Geometry child.");
            if(!(name=="Geometry" && (n=="Shape" || n=="ContactPoint")) && !seen->Add(n)) throw gcnew NotSupportedException("Duplicate Geometry singleton.");
            result.children.push_back(ReadGeometry(child));
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadAudioArray(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        bool sound=node->LocalName=="AudioArraySound",voice=node->LocalName=="AudioArrayVoice";
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(!IsXmlContextAttribute(a) && !(a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported audio array attribute.");
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            if(child->NamespaceURI!=node->NamespaceURI || (voice?(child->LocalName!="AudioEntry" && child->LocalName!="ObjectSpecificEntry" && child->LocalName!="NamedEntry"):(sound?child->LocalName!="AudioEntry":child->LocalName!="EvaEntry")) || child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported audio array entry.");
            Native::GameObjectChild entry;entry.name=marshal_as<std::string>(child->LocalName);
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;
                bool allowed=voice?(attrs->LocalName=="Sound" || attrs->LocalName=="EvaEvent" || (child->LocalName!="NamedEntry" && attrs->LocalName=="AudioType") || (child->LocalName=="NamedEntry" && attrs->LocalName=="Name") || (child->LocalName=="ObjectSpecificEntry" && attrs->LocalName=="TargetObject")):(sound?(attrs->LocalName=="Sound" || attrs->LocalName=="AudioType"):(attrs->LocalName=="EvaEvent" || attrs->LocalName=="EvaType"));
                if(attrs->NamespaceURI->Length!=0 || !allowed) throw gcnew NotSupportedException("Unsupported audio entry attribute.");
                entry.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            NoStructuredText(child);result.children.push_back(entry);
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadSkirmishAI(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            if(a->NamespaceURI->Length!=0 || !Native::GameObjectAIAttributeSupported(result.name,marshal_as<std::string>(a->LocalName))) throw gcnew NotSupportedException("Unsupported SkirmishAI attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;auto n=child->LocalName;auto parent=node->LocalName;
            bool valid=(parent=="SkirmishAIInformation" && (n=="AIContextualPower" || n=="ConstructionBaseSelectionCriteria" || n=="ResourceNodeBaseClosenessTestSelectionCriteria" || n=="DodgeWithUnits")) || (parent=="AIContextualPower" && (n=="ObjectFilter" || n=="SpecialPowerWeapon"));
            if(child->NamespaceURI!=node->NamespaceURI || !valid) throw gcnew NotSupportedException("Unsupported SkirmishAI child.");
            if(n!="AIContextualPower" && !seen->Add(n)) throw gcnew NotSupportedException("Duplicate SkirmishAI singleton.");
            if(n=="ObjectFilter" || n=="DodgeWithUnits") {auto filter=ReadDependencyFilter(child);filter.name=marshal_as<std::string>(n);result.children.push_back(filter);}
            else result.children.push_back(ReadSkirmishAI(child));
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadSoundSelectorOverride(XPathNavigator^ node,unsigned kind) {
        Native::GameObjectChild result;result.name="Override";
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
            bool allowed=kind==15?(a->LocalName=="RequiredModelConditions" || a->LocalName=="ConflictingModelConditions"):(a->LocalName=="RequiredFlags" || a->LocalName=="ExcludedFlags");
            allowed=allowed || (kind!=16 && a->LocalName=="VoicePriorityOverride");
            if(a->NamespaceURI->Length!=0 || !allowed) throw gcnew NotSupportedException("Unsupported sound selector override attribute.");
            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        } while(a->MoveToNextAttribute());
        auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;auto name=child->LocalName;
            if(child->NamespaceURI!=node->NamespaceURI) throw gcnew NotSupportedException("Foreign sound selector override child.");
            bool audio=kind==16?name=="EvaEventArray":(name=="AudioArrayVoice" || name=="AudioArraySound");
            if(audio) {if(!seen->Add(name)) throw gcnew NotSupportedException("Duplicate sound selector audio array.");result.children.push_back(ReadAudioArray(child));continue;}
            if(kind!=15 || (name!="RequiredUpgrade" && name!="ConflictingUpgrade") || child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported sound selector override child.");
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(!IsXmlContextAttribute(attrs) && !(attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported selector upgrade attribute.");
            } while(attrs->MoveToNextAttribute());
            Native::GameObjectChild entry;entry.name=marshal_as<std::string>(name);entry.text=marshal_as<std::string>(child->Value);result.children.push_back(entry);
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadMotionMovement(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="Movement";
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {if(!IsXmlContextAttribute(a) && !(a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported Movement attribute.");}while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;if(child->NamespaceURI!=node->NamespaceURI) throw gcnew NotSupportedException("Unsupported Movement namespace.");
            Native::GameObjectChild move;move.name=marshal_as<std::string>(child->LocalName);std::string typeId=marshal_as<std::string>(child->GetAttribute("TypeId",""));move.attributes["TypeId"]=typeId;
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;
                if(attrs->NamespaceURI->Length!=0 || !Native::GameObjectMotionMoveAttribute(typeId,marshal_as<std::string>(attrs->LocalName))) throw gcnew NotSupportedException("Unsupported typed Movement attribute.");
                move.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            }while(attrs->MoveToNextAttribute());
            auto paths=child->SelectChildren(XPathNodeType::Element);while(paths->MoveNext()) {
                auto path=paths->Current;if(!Native::GameObjectMotionMovePaths(typeId) || path->LocalName!="Path" || path->NamespaceURI!=node->NamespaceURI || path->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported typed Movement child.");
                auto pa=path->Clone();if(pa->MoveToFirstAttribute()) do {if(!IsXmlContextAttribute(pa) && !(pa->NamespaceURI->Length==0 && pa->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported Movement Path attribute.");}while(pa->MoveToNextAttribute());
                Native::GameObjectChild item;item.name="Path";item.text=marshal_as<std::string>(path->Value);move.children.push_back(item);
            }
            NoStructuredText(child);result.children.push_back(move);
        }
        NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadDrawShaderConstants(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name="Constants";
        auto a=node->Clone();if(a->MoveToFirstAttribute())do{if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId"))continue;throw gcnew NotSupportedException("Unsupported shader Constants attribute.");}while(a->MoveToNextAttribute());
        NoStructuredText(node);auto rows=node->SelectChildren(XPathNodeType::Element);
        while(rows->MoveNext()){
            auto row=rows->Current;if(row->NamespaceURI!=node->NamespaceURI)throw gcnew NotSupportedException("Foreign shader constant.");Native::GameObjectChild entry;entry.name=marshal_as<std::string>(row->LocalName);
            auto attrs=row->Clone();if(attrs->MoveToFirstAttribute())do{if(IsXmlContextAttribute(attrs))continue;if(attrs->NamespaceURI->Length!=0 || (attrs->LocalName!="Name" && attrs->LocalName!="TypeId"))throw gcnew NotSupportedException("Unsupported shader constant attribute.");entry.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);}while(attrs->MoveToNextAttribute());
            auto type=entry.attributes.find("TypeId");auto id=type==entry.attributes.end()?0:Native::ParseUnsigned(type->second);bool known=id==2750958653u || id==2300057986u || id==2777716390u || id==3741900581u;unsigned count=0;
            NoStructuredText(row);auto values=row->SelectChildren(XPathNodeType::Element);while(values->MoveNext()){
                auto value=values->Current;if(!known || value->NamespaceURI!=row->NamespaceURI || value->LocalName!="Value" || (++count>1 && id!=3741900581u) || value->SelectChildren(XPathNodeType::Element)->MoveNext())throw gcnew NotSupportedException("Unsupported or duplicate shader constant Value.");
                auto va=value->Clone();if(va->MoveToFirstAttribute())do{if(IsXmlContextAttribute(va) || (va->NamespaceURI->Length==0 && va->LocalName=="TypeId"))continue;throw gcnew NotSupportedException("Unsupported shader Value attribute.");}while(va->MoveToNextAttribute());
                Native::GameObjectChild v;v.name="Value";v.text=marshal_as<std::string>(value->Value);entry.children.push_back(v);
            }
            result.children.push_back(entry);
        }
        return result;
    }
    static Native::GameObjectChild ReadModuleNode(XPathNavigator^ node,unsigned kind,std::string path) {
        if(((kind==283 || kind==285) && path=="FXShader/Constants") || (kind>=286 && kind<=290 && path=="ModelConditionState/Material/Constants"))return ReadDrawShaderConstants(node);
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        if(kind==80 && path=="Movement") return ReadMotionMovement(node);
        unsigned shape=Native::GameObjectModuleNodeKind(kind,path);
        if(!shape) throw gcnew NotSupportedException("Unsupported Upgrade nested node.");
        if((kind==278 && path=="CanEnterFilter") || (kind>=280 && kind<=282 && (path=="FadeFilter" || path=="PassengerFilter" || path=="PassengerData/Filter")) || (kind>=270 && kind<=277 && (path=="PassengerFilter" || path=="PassengerData/Filter" || (kind==277 && path=="CanAlwaysEnterObjectFilter"))) || (kind==254 && (path=="InstaKillObjectFilter" || path=="CollidesWithOnTeleport")) || (kind==256 && path=="Filter") || (kind==259 && (path=="Filter" || path=="KillFilter")) || (kind==262 && path=="ObjectEffectTuple/ObjectFilter") || (kind==247 && (path=="AllowFilter" || path=="ContainerFilter")) || (kind==249 && path=="Queries/ObjectFilter") || (kind==250 && (path=="TauntAndPointExcluded" || path=="AfraidOf" || path=="AlwaysAfraidOf" || path=="PointAt")) || (kind==224 && path=="PreventEnteringFilter") || (kind==217 && path=="ReplaceObjectNugget/TargetObjectFilter") || (kind==218 && (path=="ObjectFilter" || path=="IgnoreInsideToInsideCheck")) || (kind==214 && (path=="NearestSecondaryObjectFilter" || path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || (kind==206 && path=="AttributeModifierAffects") || (kind==207 && path=="AcceptanceFilter") || (kind==198 && path=="NearestSecondaryObjectFilter") || (((kind>=198 && kind<=199) || (kind>=162 && kind<=190)) && (path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || ((kind>=165 && kind<=187) && (path=="CombineWithObjectFilter" || path=="AcceptObjectFilter")) || ((kind>=162 && kind<=190) && (path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic")) || (kind==119 && (path=="HintObject/ObjectFilter" || path=="PlacementRestriction/RestrictedObjectFilter")) || (kind==120 && path=="RequirementObjectFilter") || ((kind==112 || kind==115 || kind==116) && path=="ForVoiceRetreatThisIsASafeHarborToObjectFilter") || (kind==118 && path=="Filter") || path=="ObjectFilter" || path=="BroadcastObjectFilter" || path=="ProductionModifier/ModifierFilter" || (kind==103 && path=="BlockingUnits") || (kind==129 && path=="CanParkHereFilter") || (kind==137 && path=="ConnectingObjectFilter") || (kind==141 && path=="AcceptableFriends") || (kind==146 && path=="RequiresNearbyObjectFilter") || (kind==149 && (path=="DamageFilter" || path=="HealFilter")) || (kind==151 && path=="Filter") || (kind==161 && path=="KeepAliveObjectFilter") || ((kind==91 || (kind>=104 && kind<=111) || kind==291) && path.size()>=7 && path.substr(path.size()-7)=="/Filter")) {auto filter=ReadDependencyFilter(node);filter.name=marshal_as<std::string>(node->LocalName);return filter;}
        auto attrs=node->Clone();if(attrs->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(attrs) || (attrs->NamespaceURI->Length==0 && attrs->LocalName=="TypeId")) continue;
            // Shared chooser ObjectVisionRangeItem inherits BaseAssetType.id identity metadata; original Marshal ignores it.
            if(node->LocalName=="ObjectVisionRange" && attrs->NamespaceURI->Length==0 && attrs->LocalName=="id") continue;
            if(attrs->NamespaceURI->Length!=0 || !Native::GameObjectModuleNodeAttribute(kind,path,marshal_as<std::string>(attrs->LocalName))) throw gcnew NotSupportedException(String::Format("Unsupported module nested attribute: kind={0} path={1} attribute={2}",kind,gcnew String(path.c_str()),attrs->LocalName));
            result.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        }while(attrs->MoveToNextAttribute());
        auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto children=node->SelectChildren(XPathNodeType::Element);
        while(children->MoveNext()) {
            auto child=children->Current;auto subpath=path+"/"+marshal_as<std::string>(child->LocalName);unsigned subshape=Native::GameObjectModuleNodeKind(kind,subpath);
            if(child->NamespaceURI!=node->NamespaceURI || shape==3 || shape==4 || !subshape || ((subshape==1 || subshape==4) && !seen->Add(child->LocalName))) throw gcnew NotSupportedException("Unsupported or duplicate Upgrade nested child.");
            result.children.push_back(ReadModuleNode(child,kind,subpath));
        }
        if(shape==3 || shape==4) result.text=marshal_as<std::string>(node->Value);else NoStructuredText(node);return result;
    }
    static Native::GameObjectChild ReadModules(XPathNavigator^ node) {
        Native::GameObjectChild result;result.name=marshal_as<std::string>(node->LocalName);
        auto a=node->Clone();if(a->MoveToFirstAttribute()) do {
            if(!IsXmlContextAttribute(a) && !(a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) throw gcnew NotSupportedException("Unsupported module container attribute.");
        } while(a->MoveToNextAttribute());
        auto nodes=node->SelectChildren(XPathNodeType::Element);
        while(nodes->MoveNext()) {
            auto child=nodes->Current;Native::GameObjectChild item;item.name=marshal_as<std::string>(child->LocalName);
            if(child->NamespaceURI!=node->NamespaceURI || !Native::GameObjectModuleChildSupported(result.name,item.name)) throw gcnew NotSupportedException("Unsupported module child structure.");
            auto typeId=marshal_as<std::string>(child->GetAttribute("TypeId",""));
            auto attrs=child->Clone();if(attrs->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(attrs)) continue;
                if(attrs->NamespaceURI->Length!=0 || (attrs->LocalName!="id" && attrs->LocalName!="TypeId" && !Native::GameObjectModuleAttributeSupported(typeId,marshal_as<std::string>(attrs->LocalName)))) throw gcnew NotSupportedException("Unsupported module attribute: "+child->LocalName+"."+attrs->LocalName+" {"+attrs->NamespaceURI+"} TypeId="+child->GetAttribute("TypeId",""));
                item.attributes[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
            } while(attrs->MoveToNextAttribute());
            if(unsigned kind=Native::GameObjectSoundSelectorKind(typeId)) {
                auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;auto name=value->LocalName;
                    if(value->NamespaceURI!=child->NamespaceURI) throw gcnew NotSupportedException("Foreign sound selector child.");
                    if(kind==13) {
                        if((name!="AudioArrayVoice" && name!="AudioArraySound") || !seen->Add(name)) throw gcnew NotSupportedException("Unsupported or duplicate random selector child.");
                        item.children.push_back(ReadAudioArray(value));
                    } else {
                        if(name!="Override") throw gcnew NotSupportedException("Expected sound selector Override.");
                        item.children.push_back(ReadSoundSelectorOverride(value,kind));
                    }
                }
                NoStructuredText(child);result.children.push_back(item);continue;
            }
            if(unsigned kind=Native::GameObjectRecursiveModuleKind(typeId)) {
                auto seen=gcnew System::Collections::Generic::HashSet<String^>();auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;std::string path=marshal_as<std::string>(value->LocalName);unsigned shape=Native::GameObjectModuleNodeKind(kind,path);
                    if(value->NamespaceURI!=child->NamespaceURI || !shape || ((shape==1 || shape==4) && !seen->Add(value->LocalName))) throw gcnew NotSupportedException("Unsupported or duplicate Upgrade child.");
                    item.children.push_back(ReadModuleNode(value,kind,path));
                }
                NoStructuredText(child);result.children.push_back(item);continue;
            }
            bool validationSeen=false,dieMuxSeen=false,filterSeen=false,animSeen=false;auto nested=child->SelectChildren(XPathNodeType::Element);
            while(nested->MoveNext()) {
                auto value=nested->Current;Native::GameObjectChild entry;entry.name=marshal_as<std::string>(value->LocalName);
                if(entry.name=="ObjectFilter" && value->NamespaceURI==child->NamespaceURI && Native::GameObjectModuleNestedSupported(typeId,entry.name)) {
                    if(filterSeen) throw gcnew NotSupportedException("Duplicate Die ObjectFilter.");filterSeen=true;item.children.push_back(ReadDependencyFilter(value));continue;
                }
                if(value->NamespaceURI!=child->NamespaceURI || !Native::GameObjectModuleNestedSupported(typeId,entry.name) || value->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Unsupported module nested structure.");
                if(entry.name=="CustomAnimAndDuration") {if(animSeen) throw gcnew NotSupportedException("Duplicate CustomAnimAndDuration.");animSeen=true;}
                if(entry.name=="DieMuxData") {if(dieMuxSeen) throw gcnew NotSupportedException("Duplicate DieMuxData.");dieMuxSeen=true;}
                if(entry.name=="ObjectStatusValidation") {if(validationSeen) throw gcnew NotSupportedException("Duplicate ObjectStatusValidation.");validationSeen=true;}
                auto a=value->Clone();if(a->MoveToFirstAttribute()) do {
                    if(IsXmlContextAttribute(a) || (a->NamespaceURI->Length==0 && a->LocalName=="TypeId")) continue;
                    if(a->NamespaceURI->Length!=0 || (entry.name=="Key" || entry.name=="UpgradeRequired" || entry.name=="UpgradeForbidden" || entry.name=="TriggeredBy" || entry.name=="ConflictsWith") || !(entry.name=="DamageRangeModifier"?(a->LocalName=="Min" || a->LocalName=="Max" || a->LocalName=="AttributeModifier"):Native::GameObjectModuleNestedAttributeSupported(typeId,entry.name,marshal_as<std::string>(a->LocalName)))) throw gcnew NotSupportedException("Unsupported nested module attribute.");
                    entry.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
                } while(a->MoveToNextAttribute());
                if(entry.name=="Key" || entry.name=="UpgradeRequired" || entry.name=="UpgradeForbidden" || entry.name=="TriggeredBy" || entry.name=="ConflictsWith") entry.text=marshal_as<std::string>(value->Value);else NoStructuredText(value);item.children.push_back(entry);
            }
            NoStructuredText(child);result.children.push_back(item);
        }
        if(node->LocalName=="Body" && result.children.size()>1) throw gcnew NotSupportedException("Body requires a single module.");
        NoStructuredText(node);return result;
    }
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr) throw gcnew ArgumentNullException("node");
        if(node->LocalName!="GameObject" || node->NamespaceURI!="uri:ea.com:eala:asset") throw gcnew NotSupportedException("Expected GameObject asset element.");
        auto nav=node->CreateNavigator();auto attrs=nav->Clone();std::map<std::string,std::string> input;
        if(attrs->MoveToFirstAttribute()) do {
            if(IsXmlContextAttribute(attrs)) continue;
            if(attrs->NamespaceURI->Length!=0) throw gcnew NotSupportedException("Namespaced GameObject attribute.");
            if(attrs->LocalName=="id" || attrs->LocalName=="TypeId") continue;
            input[marshal_as<std::string>(attrs->LocalName)]=marshal_as<std::string>(attrs->Value);
        } while(attrs->MoveToNextAttribute());
        std::vector<Native::GameObjectChild> children;
        auto nodes=nav->SelectChildren(XPathNodeType::Element);
        auto seen=gcnew System::Collections::Generic::HashSet<String^>();
        while(nodes->MoveNext()) {
            auto child=nodes->Current;
            auto name=child->LocalName;
            if(child->NamespaceURI!="uri:ea.com:eala:asset" ||
                (name!="Buildable" && name!="ThingClass" && name!="DisplayColor" && name!="DisplayName" && name!="DisplayNameTransformed" && name!="EquivalentTo" && name!="UpgradeCameo" && name!="DisplayUpgrade" && name!="Flammability" && name!="LiveCameraOffset" && name!="ArmorSet" && name!="LocomotorSet" && name!="FormationPreviewDecal" && name!="FormationPreviewItemDecal" && name!="ShadowInfo" && name!="VisionInfo" && name!="AutoResolveArmor" && name!="AutoResolveWeapon" && name!="AutoResolveInfo" && name!="BuildFadeInOnCreateList" && name!="BuildVariations" && name!="ObjectResourceInfo" && name!="AudioArrayVoice" && name!="AudioArraySound" && name!="EvaEventArray" && name!="Geometry" && name!="GameDependency" && name!="CrusherInfo" && name!="ProjectedBuildabilityInfo" && name!="SkirmishAIInformation" && name!="ReplaceModule" && name!="InheritableModule" && name!="Draws" && name!="Behaviors" && name!="AI" && name!="Body" && name!="ClientUpdates" && name!="ClientBehaviors"))
                throw gcnew NotSupportedException("Unsupported GameObject child.");
            bool repeated=name=="ProjectedBuildabilityInfo" || name=="ArmorSet" || name=="LocomotorSet" || name=="EquivalentTo" || name=="UpgradeCameo" || name=="DisplayUpgrade";
            if(!repeated && !seen->Add(name)) throw gcnew NotSupportedException("Duplicate GameObject child.");
            if(name=="AudioArrayVoice" || name=="AudioArraySound" || name=="EvaEventArray") {children.push_back(ReadAudioArray(child));continue;}
            if(name=="Draws" || name=="Behaviors" || name=="AI" || name=="Body" || name=="ClientUpdates" || name=="ClientBehaviors") {children.push_back(ReadModules(child));continue;}
            if(name=="SkirmishAIInformation") {children.push_back(ReadSkirmishAI(child));continue;}
            if(name=="Geometry") {children.push_back(ReadGeometry(child));continue;}
            if(name=="GameDependency") {children.push_back(ReadDependency(child));continue;}
            if(name=="CrusherInfo" || name=="ProjectedBuildabilityInfo") {children.push_back(ReadPlacementGroup(child));continue;}
            bool autoSet=name=="AutoResolveArmor" || name=="AutoResolveWeapon";
            bool resourceInfo=name=="ObjectResourceInfo";
            if(!autoSet && !resourceInfo && child->SelectChildren(XPathNodeType::Element)->MoveNext()) throw gcnew NotSupportedException("Nested GameObject child content.");
            Native::GameObjectChild item;item.name=marshal_as<std::string>(name);item.text=marshal_as<std::string>(child->Value);
            if(autoSet || resourceInfo) {
                auto values=child->SelectChildren(XPathNodeType::Element);
                while(values->MoveNext()) {
                    auto value=values->Current;
                    if(value->NamespaceURI!="uri:ea.com:eala:asset" || (resourceInfo?value->LocalName!="BuildCost":(value->LocalName!="RequiredUpgrade" && value->LocalName!="ExcludedUpgrade")) || value->SelectChildren(XPathNodeType::Element)->MoveNext())
                        throw gcnew NotSupportedException("Unsupported auto-resolve upgrade child.");
                    Native::GameObjectChild upgrade;upgrade.name=marshal_as<std::string>(value->LocalName);upgrade.text=marshal_as<std::string>(value->Value);
                    auto va=value->Clone();
                    if(va->MoveToFirstAttribute()) do {
                        if(IsXmlContextAttribute(va)) continue;
                        if(va->NamespaceURI->Length!=0 || (va->LocalName!="TypeId" && !(resourceInfo && (va->LocalName=="Account" || va->LocalName=="Amount")))) throw gcnew NotSupportedException("Unsupported nested list attribute.");
                        if(va->LocalName!="TypeId") upgrade.attributes[marshal_as<std::string>(va->LocalName)]=marshal_as<std::string>(va->Value);
                    } while(va->MoveToNextAttribute());
                    if(resourceInfo && value->Value->Trim()->Length!=0) throw gcnew NotSupportedException("Unexpected text in BuildCost.");
                    item.children.push_back(upgrade);
                }
                auto content=child->SelectChildren(XPathNodeType::All);
                while(content->MoveNext()) if((content->Current->NodeType==XPathNodeType::Text || content->Current->NodeType==XPathNodeType::SignificantWhitespace) && content->Current->Value->Trim()->Length!=0)
                    throw gcnew NotSupportedException("Unexpected text in auto-resolve set.");
            }
            auto a=child->Clone();
            if(a->MoveToFirstAttribute()) do {
                if(IsXmlContextAttribute(a)) continue;
                if(a->NamespaceURI->Length!=0) throw gcnew NotSupportedException("Namespaced GameObject child attribute.");
                if(a->LocalName=="TypeId") continue;
                // ArmorTemplateSet.id and LocomotorSet.id are pipelineOnly in AssetTypeGameObject.xsd.
                if((name=="ArmorSet" || name=="LocomotorSet") && a->LocalName=="id") continue;
                bool allowed=(name=="AutoResolveArmor" && a->LocalName=="Armor") || (name=="AutoResolveWeapon" && a->LocalName=="Weapon") || (name=="DisplayColor" && (a->LocalName=="R" || a->LocalName=="G" || a->LocalName=="B" || a->LocalName=="A")) ||
                    (name=="Flammability" && (a->LocalName=="Fuel" || a->LocalName=="MaxBurnRate" || a->LocalName=="Decay" || a->LocalName=="Resistance")) ||
                    (name=="LiveCameraOffset" && (a->LocalName=="x" || a->LocalName=="y" || a->LocalName=="z")) ||
                    (name=="ArmorSet" && (a->LocalName=="Armor" || a->LocalName=="DamageFX" || a->LocalName=="Conditions")) ||
                    (name=="LocomotorSet" && (a->LocalName=="Speed" || a->LocalName=="Condition" || a->LocalName=="Locomotor"));
                allowed=allowed || Native::GameObjectLeafAttributeSupported(item.name,marshal_as<std::string>(a->LocalName));
                if(!allowed)
                    throw gcnew NotSupportedException("Unsupported GameObject child attribute: "+name+"."+a->LocalName);
                item.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
            } while(a->MoveToNextAttribute());
            if((name=="ReplaceModule" || name=="InheritableModule" || name=="DisplayColor" || name=="Flammability" || name=="LiveCameraOffset" || name=="ArmorSet" || name=="LocomotorSet" || name=="FormationPreviewDecal" || name=="FormationPreviewItemDecal" || name=="ShadowInfo" || name=="VisionInfo" || name=="AutoResolveInfo") && child->Value->Trim()->Length!=0) throw gcnew NotSupportedException("Unexpected text in structured GameObject child.");
            children.push_back(item);
            if(name=="UpgradeCameo" || name=="DisplayUpgrade") {
                unsigned count=0;for(const auto& c:children) if(c.name==item.name) ++count;
                if(count>(name=="UpgradeCameo"?5u:2u)) throw gcnew NotSupportedException("GameObject list exceeds XSD maximum.");
            }
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












