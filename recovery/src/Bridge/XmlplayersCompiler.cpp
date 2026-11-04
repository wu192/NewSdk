#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlplayers.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlplayersBridge abstract sealed {
    static Native::XmlplayersNode Read(XmlNode^ node,int depth) {
        if(depth>100)throw gcnew NotSupportedException("Player XML nesting exceeds 100");

        Native::XmlplayersNode result;result.name=marshal_as<std::string>(node->LocalName);
        for each(XmlAttribute^ a in node->Attributes){
            if(a->NamespaceURI=="http://www.w3.org/2000/xmlns/")continue;

            result.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
        }
        for each(XmlNode^ c in node->ChildNodes){
            if(c->NodeType==XmlNodeType::Element)result.children.push_back(Read(c,depth+1));
            else if(c->NodeType==XmlNodeType::Text||c->NodeType==XmlNodeType::CDATA)result.value+=marshal_as<std::string>(c->Value);
        }
        return result;
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& value){
        array<Byte>^ result=gcnew array<Byte>(static_cast<int>(value.size()));
        if(result->Length)System::Runtime::InteropServices::Marshal::Copy(IntPtr(const_cast<unsigned char*>(value.data())),result,0,result->Length);return result;
    }
public:
    static AssetBuffer^ Compile(XmlNode^ node,XmlNamespaceManager^ ns,bool big,String^ name){
        if(ns==nullptr)throw gcnew ArgumentNullException("namespaces");
        if(node==nullptr)throw gcnew ArgumentNullException("node");

        if(node->LocalName!=name)throw gcnew NotSupportedException("Incorrect player root");
        try{
            auto result=Native::CompileXmlplayers(Read(node,0),big);auto buffer=gcnew AssetBuffer();
            buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);return buffer;
        }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
    }
};
public ref class AIDataCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"AIData");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3827723072u)throw gcnew NotSupportedException("Expected AIData type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UnitAISettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"UnitAISettings");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2964891936u)throw gcnew NotSupportedException("Expected UnitAISettings type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class PlayerTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"PlayerTemplate");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4063352333u)throw gcnew NotSupportedException("Expected PlayerTemplate type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class MultiplayerSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"MultiplayerSettings");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=191082224u)throw gcnew NotSupportedException("Expected MultiplayerSettings type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class AIBuildStateDefinitionCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"AIBuildStateDefinition");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1644972043u)throw gcnew NotSupportedException("Expected AIBuildStateDefinition type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class AIStrategicStateNameCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"AIStrategicStateName");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2010562192u)throw gcnew NotSupportedException("Expected AIStrategicStateName type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class MultiplayerColorCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"MultiplayerColor");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2384988189u)throw gcnew NotSupportedException("Expected MultiplayerColor type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class AIPersonalityToAchievementEventMappingCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"AIPersonalityToAchievementEventMapping");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4086175864u)throw gcnew NotSupportedException("Expected AIPersonalityToAchievementEventMapping type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class FormationAssistantUnitDefinitionCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"FormationAssistantUnitDefinition");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=838130914u)throw gcnew NotSupportedException("Expected FormationAssistantUnitDefinition type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class FormationAssistantTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"FormationAssistantTemplate");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2912385462u)throw gcnew NotSupportedException("Expected FormationAssistantTemplate type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class FormationSelectionTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"FormationSelectionTemplate");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3374519264u)throw gcnew NotSupportedException("Expected FormationSelectionTemplate type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class FormationAssistantCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"FormationAssistant");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1963196508u)throw gcnew NotSupportedException("Expected FormationAssistant type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class GameStatsModeDataCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"GameStatsModeData");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3249689110u)throw gcnew NotSupportedException("Expected GameStatsModeData type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class SkirmishSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlplayersBridge::Compile(node,namespaces,bigEndian,"SkirmishSettings");}
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=402082255u)throw gcnew NotSupportedException("Expected SkirmishSettings type");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
