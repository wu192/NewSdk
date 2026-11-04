#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlw3d.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class Xmlw3dCompiler abstract sealed {
    static Native::Xmlw3dNode Read(XmlNode^ node,int depth) {
        if(depth>32)throw gcnew NotSupportedException("w3d nesting exceeds 32.");

        Native::Xmlw3dNode result;result.name=marshal_as<std::string>(node->LocalName);
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
            Relo::Chunk chunk=Native::CompileXmlw3d(Read(node,0),bigEndian);
            AssetBuffer^ result=gcnew AssetBuffer();result->InstanceData=Copy(chunk.instance);result->RelocationData=Copy(chunk.relocations);result->ImportsData=Copy(chunk.imports);return result;
        } catch(const std::exception& error) {throw gcnew NotSupportedException(gcnew String(error.what()));}
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform) {
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if(node==nullptr)throw gcnew ArgumentException("Missing w3d node.");
        unsigned expected=Native::Xmlw3dTypeId(marshal_as<std::string>(node->LocalName));
        if(!expected || declaration->Handle->TypeId!=expected)throw gcnew NotSupportedException("w3d root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class W3DHierarchyCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="W3DHierarchy")throw gcnew NotSupportedException("Expected W3DHierarchy root.");
        return Xmlw3dCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=1641540160u)throw gcnew NotSupportedException("Expected W3DHierarchy TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class W3DContainerCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="W3DContainer")throw gcnew NotSupportedException("Expected W3DContainer root.");
        return Xmlw3dCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=4042295058u)throw gcnew NotSupportedException("Expected W3DContainer TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class W3DAnimationRuntimeCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="W3DAnimationRuntime")throw gcnew NotSupportedException("Expected W3DAnimationRuntime root.");
        return Xmlw3dCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3534264507u)throw gcnew NotSupportedException("Expected W3DAnimationRuntime TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
public ref class W3DCollisionBoxCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){
        if(node==nullptr)throw gcnew ArgumentNullException("node");
        if(node->LocalName!="W3DCollisionBox")throw gcnew NotSupportedException("Expected W3DCollisionBox root.");
        return Xmlw3dCompiler::CompileXml(node,namespaces,bigEndian);
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
        if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
        if(declaration->Handle->TypeId!=3810008068u)throw gcnew NotSupportedException("Expected W3DCollisionBox TypeId.");
        return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
    }
};
}
