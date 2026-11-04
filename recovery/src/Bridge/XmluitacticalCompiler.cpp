#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmluitactical.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class XmluitacticalCompiler abstract sealed {
    static Native::XmluitacticalNode Read(XmlNode^ node,int depth) {
        if(depth>32)throw gcnew NotSupportedException("Tactical UI nesting exceeds 32.");

        Native::XmluitacticalNode result;result.name=marshal_as<std::string>(node->LocalName);
        for each(XmlAttribute^ attr in node->Attributes) {
            if(attr->NamespaceURI=="http://www.w3.org/2000/xmlns/")continue;

            result.attributes[marshal_as<std::string>(attr->LocalName)]=marshal_as<std::string>(attr->Value);
        }
        for each(XmlNode^ child in node->ChildNodes) {
            if(child->NodeType==XmlNodeType::Element)result.children.push_back(Read(child,depth+1));
            else if(child->NodeType==XmlNodeType::Text || child->NodeType==XmlNodeType::CDATA || child->NodeType==XmlNodeType::SignificantWhitespace)
                result.value+=marshal_as<std::string>(child->Value);
            else ;
        }
        return result;
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& source) {
        array<Byte>^ target=gcnew array<Byte>(static_cast<int>(source.size()));
        if(target->Length)Runtime::InteropServices::Marshal::Copy(IntPtr(const_cast<unsigned char*>(source.data())),target,0,target->Length);
        return target;
    }
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");

        try {
            Relo::Chunk chunk=Native::CompileXmluitactical(Read(node,0),bigEndian);
            AssetBuffer^ result=gcnew AssetBuffer();result->InstanceData=Copy(chunk.instance);result->RelocationData=Copy(chunk.relocations);result->ImportsData=Copy(chunk.imports);return result;
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if(node==nullptr)throw gcnew ArgumentException("Missing uitactical node.");
        unsigned expected=Native::XmluitacticalTypeId(marshal_as<std::string>(node->LocalName));
        if(!expected || declaration->Handle->TypeId!=expected)throw gcnew NotSupportedException("Tactical UI root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCoOpAIButtonsHelpButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCoOpAIButtonsHelpButton")throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHelpButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=104126924u)throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHelpButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandCommunicateWithAlliesCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandCommunicateWithAllies")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandCommunicateWithAllies root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=113029208u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandCommunicateWithAllies TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalToggleRepairModeButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalToggleRepairModeButton")throw gcnew NotSupportedException("Expected UIMouseTacticalToggleRepairModeButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=346077409u)throw gcnew NotSupportedException("Expected UIMouseTacticalToggleRepairModeButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalAttackMoveButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalAttackMoveButton")throw gcnew NotSupportedException("Expected UIMouseTacticalAttackMoveButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=442957564u)throw gcnew NotSupportedException("Expected UIMouseTacticalAttackMoveButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandPlaceWallCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandPlaceWall")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceWall root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=485977567u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceWall TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalForceAttackMoveButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalForceAttackMoveButton")throw gcnew NotSupportedException("Expected UIMouseTacticalForceAttackMoveButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=694838069u)throw gcnew NotSupportedException("Expected UIMouseTacticalForceAttackMoveButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCoOpAIButtonsHoldObjectButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCoOpAIButtonsHoldObjectButton")throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHoldObjectButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=772462768u)throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHoldObjectButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalExitContainerButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalExitContainerButton")throw gcnew NotSupportedException("Expected UIMouseTacticalExitContainerButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1014034471u)throw gcnew NotSupportedException("Expected UIMouseTacticalExitContainerButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalPlayerTechStoreButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalPlayerTechStoreButton")throw gcnew NotSupportedException("Expected UIMouseTacticalPlayerTechStoreButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1138847078u)throw gcnew NotSupportedException("Expected UIMouseTacticalPlayerTechStoreButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandMoveCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandMove")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMove root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1234485619u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMove TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandSellCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandSell")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSell root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1249197161u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSell TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalUnitIntroButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalUnitIntroButton")throw gcnew NotSupportedException("Expected UIMouseTacticalUnitIntroButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1277109486u)throw gcnew NotSupportedException("Expected UIMouseTacticalUnitIntroButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandSelectCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandSelect")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSelect root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1334543376u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSelect TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCoOpAIButtonsNormalButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCoOpAIButtonsNormalButton")throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsNormalButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1371977384u)throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsNormalButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandPlaceRallyPointCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandPlaceRallyPoint")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceRallyPoint root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1474300054u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceRallyPoint TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandHarvestCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandHarvest")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandHarvest root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1499648070u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandHarvest TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandToggleSelfRepairCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandToggleSelfRepair")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandToggleSelfRepair root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1956336137u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandToggleSelfRepair TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalBuildQueueTabCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalBuildQueueTab")throw gcnew NotSupportedException("Expected UIMouseTacticalBuildQueueTab root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1974922393u)throw gcnew NotSupportedException("Expected UIMouseTacticalBuildQueueTab TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandGetCrateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandGetCrate")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandGetCrate root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2067763016u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandGetCrate TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalDeactivateMissionHotSpotsButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalDeactivateMissionHotSpotsButton")throw gcnew NotSupportedException("Expected UIMouseTacticalDeactivateMissionHotSpotsButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2047841880u)throw gcnew NotSupportedException("Expected UIMouseTacticalDeactivateMissionHotSpotsButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandForceAttackCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandForceAttack")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandForceAttack root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2074164365u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandForceAttack TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalPlanningModeButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalPlanningModeButton")throw gcnew NotSupportedException("Expected UIMouseTacticalPlanningModeButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2278083954u)throw gcnew NotSupportedException("Expected UIMouseTacticalPlanningModeButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandMiscCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandMisc")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMisc root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2505151835u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMisc TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalSelectionRefinementTabCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalSelectionRefinementTab")throw gcnew NotSupportedException("Expected UIMouseTacticalSelectionRefinementTab root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2635272840u)throw gcnew NotSupportedException("Expected UIMouseTacticalSelectionRefinementTab TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandForceMoveCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandForceMove")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandForceMove root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2697587777u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandForceMove TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalOpenAdvancedCommandsButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalOpenAdvancedCommandsButton")throw gcnew NotSupportedException("Expected UIMouseTacticalOpenAdvancedCommandsButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2808773267u)throw gcnew NotSupportedException("Expected UIMouseTacticalOpenAdvancedCommandsButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalSetStanceButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalSetStanceButton")throw gcnew NotSupportedException("Expected UIMouseTacticalSetStanceButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2873949396u)throw gcnew NotSupportedException("Expected UIMouseTacticalSetStanceButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCoOpAIButtonsHoldPositionButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCoOpAIButtonsHoldPositionButton")throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHoldPositionButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2914137727u)throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsHoldPositionButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandMineOreCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandMineOre")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMineOre root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2957071942u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandMineOre TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalWaypointModeButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalWaypointModeButton")throw gcnew NotSupportedException("Expected UIMouseTacticalWaypointModeButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2958905921u)throw gcnew NotSupportedException("Expected UIMouseTacticalWaypointModeButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalReverseMoveButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalReverseMoveButton")throw gcnew NotSupportedException("Expected UIMouseTacticalReverseMoveButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2987234945u)throw gcnew NotSupportedException("Expected UIMouseTacticalReverseMoveButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandEnterCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandEnter")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandEnter root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3057018816u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandEnter TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCoOpAIButtonsAssaultButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCoOpAIButtonsAssaultButton")throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsAssaultButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3094553884u)throw gcnew NotSupportedException("Expected UIMouseTacticalCoOpAIButtonsAssaultButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalPauseButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalPauseButton")throw gcnew NotSupportedException("Expected UIMouseTacticalPauseButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3420020972u)throw gcnew NotSupportedException("Expected UIMouseTacticalPauseButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandAttackCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandAttack")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandAttack root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3548787763u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandAttack TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandAttackMoveCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandAttackMove")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandAttackMove root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3575834349u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandAttackMove TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalForceMoveButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalForceMoveButton")throw gcnew NotSupportedException("Expected UIMouseTacticalForceMoveButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3716369729u)throw gcnew NotSupportedException("Expected UIMouseTacticalForceMoveButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalForceAttackButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalForceAttackButton")throw gcnew NotSupportedException("Expected UIMouseTacticalForceAttackButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3756168709u)throw gcnew NotSupportedException("Expected UIMouseTacticalForceAttackButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandPlaceAIBeaconCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandPlaceAIBeacon")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceAIBeacon root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3809842710u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandPlaceAIBeacon TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandDockCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandDock")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandDock root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3976885676u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandDock TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandReverseMoveCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandReverseMove")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandReverseMove root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4026878150u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandReverseMove TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalBuildQueueTypeTabCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalBuildQueueTypeTab")throw gcnew NotSupportedException("Expected UIMouseTacticalBuildQueueTypeTab root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4094578721u)throw gcnew NotSupportedException("Expected UIMouseTacticalBuildQueueTypeTab TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalSellModeButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalSellModeButton")throw gcnew NotSupportedException("Expected UIMouseTacticalSellModeButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4102242385u)throw gcnew NotSupportedException("Expected UIMouseTacticalSellModeButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalStopButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalStopButton")throw gcnew NotSupportedException("Expected UIMouseTacticalStopButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4140422551u)throw gcnew NotSupportedException("Expected UIMouseTacticalStopButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalCommandSetRallyPointCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalCommandSetRallyPoint")throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSetRallyPoint root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4180354873u)throw gcnew NotSupportedException("Expected UIMouseTacticalCommandSetRallyPoint TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalFastForwardButtonCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalFastForwardButton")throw gcnew NotSupportedException("Expected UIMouseTacticalFastForwardButton root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4232364483u)throw gcnew NotSupportedException("Expected UIMouseTacticalFastForwardButton TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalGroupSelectionCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalGroupSelection")throw gcnew NotSupportedException("Expected UIMouseTacticalGroupSelection root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=241329492u)throw gcnew NotSupportedException("Expected UIMouseTacticalGroupSelection TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalLookAtCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalLookAt")throw gcnew NotSupportedException("Expected UIMouseTacticalLookAt root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1795213437u)throw gcnew NotSupportedException("Expected UIMouseTacticalLookAt TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalRadarTelestratorCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalRadarTelestrator")throw gcnew NotSupportedException("Expected UIMouseTacticalRadarTelestrator root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3366599054u)throw gcnew NotSupportedException("Expected UIMouseTacticalRadarTelestrator TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalTelestratorCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalTelestrator")throw gcnew NotSupportedException("Expected UIMouseTacticalTelestrator root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1921726501u)throw gcnew NotSupportedException("Expected UIMouseTacticalTelestrator TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIMouseTacticalThreatMeterCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIMouseTacticalThreatMeter")throw gcnew NotSupportedException("Expected UIMouseTacticalThreatMeter root.");
        return XmluitacticalCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4143989686u)throw gcnew NotSupportedException("Expected UIMouseTacticalThreatMeter TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
