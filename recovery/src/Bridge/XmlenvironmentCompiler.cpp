#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlenvironment.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class XmlenvironmentCompiler abstract sealed {
    static Native::XmlenvironmentNode Read(XmlNode^ node,int depth) {
        if(depth>32)throw gcnew NotSupportedException("Environment nesting exceeds 32.");

        Native::XmlenvironmentNode result;result.name=marshal_as<std::string>(node->LocalName);
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
            Relo::Chunk chunk=Native::CompileXmlenvironment(Read(node,0),bigEndian);
            AssetBuffer^ result=gcnew AssetBuffer();result->InstanceData=Copy(chunk.instance);result->RelocationData=Copy(chunk.relocations);result->ImportsData=Copy(chunk.imports);return result;
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if(node==nullptr)throw gcnew ArgumentException("Missing environment node.");
        unsigned expected=Native::XmlenvironmentTypeId(marshal_as<std::string>(node->LocalName));
        if(!expected || declaration->Handle->TypeId!=expected)throw gcnew NotSupportedException("Environment root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class WeatherCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="Weather")throw gcnew NotSupportedException("Expected Weather root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2430161325u)throw gcnew NotSupportedException("Expected Weather TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class WaterTransparencyCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="WaterTransparency")throw gcnew NotSupportedException("Expected WaterTransparency root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1479531562u)throw gcnew NotSupportedException("Expected WaterTransparency TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class WaterSettingCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="WaterSetting")throw gcnew NotSupportedException("Expected WaterSetting root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2728605516u)throw gcnew NotSupportedException("Expected WaterSetting TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class EnvironmentCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="Environment")throw gcnew NotSupportedException("Expected Environment root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=680780553u)throw gcnew NotSupportedException("Expected Environment TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class LightingSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="LightingSettings")throw gcnew NotSupportedException("Expected LightingSettings root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3169690211u)throw gcnew NotSupportedException("Expected LightingSettings TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class WaterTextureListCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="WaterTextureList")throw gcnew NotSupportedException("Expected WaterTextureList root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2995571269u)throw gcnew NotSupportedException("Expected WaterTextureList TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class TerrainParticleMappingCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="TerrainParticleMapping")throw gcnew NotSupportedException("Expected TerrainParticleMapping root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1469498551u)throw gcnew NotSupportedException("Expected TerrainParticleMapping TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class BounceKickTerrainMappingCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="BounceKickTerrainMapping")throw gcnew NotSupportedException("Expected BounceKickTerrainMapping root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2368878637u)throw gcnew NotSupportedException("Expected BounceKickTerrainMapping TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class ShadowMapCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="ShadowMap")throw gcnew NotSupportedException("Expected ShadowMap root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4157475773u)throw gcnew NotSupportedException("Expected ShadowMap TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class OrdersManagerSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="OrdersManagerSettings")throw gcnew NotSupportedException("Expected OrdersManagerSettings root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2996766068u)throw gcnew NotSupportedException("Expected OrdersManagerSettings TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class TerrainVisualSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="TerrainVisualSettings")throw gcnew NotSupportedException("Expected TerrainVisualSettings root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1184276792u)throw gcnew NotSupportedException("Expected TerrainVisualSettings TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class CameraSettingsCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian) {
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="CameraSettings")throw gcnew NotSupportedException("Expected CameraSettings root.");
        return XmlenvironmentCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3898802054u)throw gcnew NotSupportedException("Expected CameraSettings TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
