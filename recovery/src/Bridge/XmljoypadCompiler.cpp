#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmljoypad.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class XmljoypadCompiler abstract sealed {
    static Native::XmljoypadNode Read(XmlNode^ node,String^ assetNamespace,int depth) {
        if(depth>32)throw gcnew NotSupportedException("Joypad nesting exceeds 32.");

        Native::XmljoypadNode result;result.name=marshal_as<std::string>(node->LocalName);
        result.namespaceUri=marshal_as<std::string>(node->NamespaceURI);
        result.assetNamespace=marshal_as<std::string>(assetNamespace);
        result.value=marshal_as<std::string>(node->InnerText);
        for each(XmlAttribute^ attr in node->Attributes) {
            if(attr->NamespaceURI->Length)continue;

            std::string value=marshal_as<std::string>(attr->Value);
            if(!value.empty() && value[0]=='\\')value=std::string(value.c_str()+1);
            result.attributes[marshal_as<std::string>(attr->LocalName)]=value;
        }
        for each(XmlNode^ child in node->ChildNodes) {
            if(child->NodeType==XmlNodeType::Element)result.children.push_back(Read(child,assetNamespace,depth+1));
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
            String^ assetNamespace=namespaces==nullptr?nullptr:namespaces->LookupNamespace("ea");
            if(assetNamespace==nullptr)assetNamespace="uri:ea.com:eala:asset";
            Relo::Chunk chunk=Native::CompileXmljoypad(Read(node,assetNamespace,0),bigEndian);
            AssetBuffer^ result=gcnew AssetBuffer();result->InstanceData=Copy(chunk.instance);result->RelocationData=Copy(chunk.relocations);result->ImportsData=Copy(chunk.imports);return result;
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if(node==nullptr)throw gcnew ArgumentException("Missing Joypad node.");
        unsigned expected=Native::XmljoypadTypeId(marshal_as<std::string>(node->LocalName));
        if(!expected || declaration->Handle->TypeId!=expected)throw gcnew NotSupportedException("Joypad root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarMixedGroupCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarMixedGroup")throw gcnew NotSupportedException("Expected UIJoypadCommandBarMixedGroup root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1276968974u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarMixedGroup TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarHomogenousGroupCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarHomogenousGroup")throw gcnew NotSupportedException("Expected UIJoypadCommandBarHomogenousGroup root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1546953756u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarHomogenousGroup TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarCoopCommunicatorCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarCoopCommunicator")throw gcnew NotSupportedException("Expected UIJoypadCommandBarCoopCommunicator root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1654297531u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarCoopCommunicator TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class JoypadCommandBarButtonTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="JoypadCommandBarButtonTemplate")throw gcnew NotSupportedException("Expected JoypadCommandBarButtonTemplate root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1804891371u)throw gcnew NotSupportedException("Expected JoypadCommandBarButtonTemplate TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarStancesCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarStances")throw gcnew NotSupportedException("Expected UIJoypadCommandBarStances root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1887864852u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarStances TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class JoypadCommandBarTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="JoypadCommandBarTemplate")throw gcnew NotSupportedException("Expected JoypadCommandBarTemplate root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1931769760u)throw gcnew NotSupportedException("Expected JoypadCommandBarTemplate TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarTopMenuCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarTopMenu")throw gcnew NotSupportedException("Expected UIJoypadCommandBarTopMenu root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2170044043u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarTopMenu TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarPlayerPowersCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarPlayerPowers")throw gcnew NotSupportedException("Expected UIJoypadCommandBarPlayerPowers root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2742786440u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarPlayerPowers TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarButtonBuildCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarButtonBuild")throw gcnew NotSupportedException("Expected UIJoypadCommandBarButtonBuild root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3423839194u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarButtonBuild TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarControlGroupsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarControlGroups")throw gcnew NotSupportedException("Expected UIJoypadCommandBarControlGroups root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3645205636u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarControlGroups TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UIJoypadCommandBarSingleUnitCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UIJoypadCommandBarSingleUnit")throw gcnew NotSupportedException("Expected UIJoypadCommandBarSingleUnit root.");
        return XmljoypadCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3901433122u)throw gcnew NotSupportedException("Expected UIJoypadCommandBarSingleUnit TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
