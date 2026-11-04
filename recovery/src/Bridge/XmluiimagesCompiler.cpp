#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmluiimages.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class XmluiimagesCompiler abstract sealed {
    static Native::XmluiimagesNode Read(XmlNode^ node,int depth) {
        if(depth>32)throw gcnew NotSupportedException("UI image nesting exceeds 32.");

        Native::XmluiimagesNode result;result.name=marshal_as<std::string>(node->LocalName);
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
            Relo::Chunk chunk=Native::CompileXmluiimages(Read(node,0),bigEndian);
            AssetBuffer^ result=gcnew AssetBuffer();result->InstanceData=Copy(chunk.instance);result->RelocationData=Copy(chunk.relocations);result->ImportsData=Copy(chunk.imports);return result;
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if(node==nullptr)throw gcnew ArgumentException("Missing UI image node.");
        unsigned expected=Native::XmluiimagesTypeId(marshal_as<std::string>(node->LocalName));
        if(!expected || declaration->Handle->TypeId!=expected)throw gcnew NotSupportedException("UI image root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class ImageSequenceCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="ImageSequence")throw gcnew NotSupportedException("Expected ImageSequence root.");
        return XmluiimagesCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=565855655u)throw gcnew NotSupportedException("Expected ImageSequence TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class UnitTypeIconCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="UnitTypeIcon")throw gcnew NotSupportedException("Expected UnitTypeIcon root.");
        return XmluiimagesCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3614134471u)throw gcnew NotSupportedException("Expected UnitTypeIcon TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class DrawGroupInfoCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="DrawGroupInfo")throw gcnew NotSupportedException("Expected DrawGroupInfo root.");
        return XmluiimagesCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=2913856066u)throw gcnew NotSupportedException("Expected DrawGroupInfo TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
