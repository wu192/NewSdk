#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlai.h"
#include <stdexcept>
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
public ref class XmlaiCompiler abstract sealed {
    static Native::XmlaiNode Read(XmlNode^ node, int depth) {
        if (depth > 96) throw gcnew NotSupportedException("Xmlai nesting exceeds 96.");

        Native::XmlaiNode result;
        result.name = marshal_as<std::string>(node->LocalName);
        for each (XmlAttribute^ a in node->Attributes) {
            if (a->NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;

            result.attributes[marshal_as<std::string>(a->LocalName)] = marshal_as<std::string>(a->Value);
        }
        for each (XmlNode^ c in node->ChildNodes) {
            if (c->NodeType == XmlNodeType::Element) result.children.push_back(Read(c, depth+1));
            else if (c->NodeType == XmlNodeType::Text || c->NodeType == XmlNodeType::CDATA ||
                     c->NodeType == XmlNodeType::SignificantWhitespace)
                result.value += marshal_as<std::string>(c->Value);
            else ;
        }
        return result;
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& source) {
        array<Byte>^ result = gcnew array<Byte>(static_cast<int>(source.size()));
        if (result->Length) Runtime::InteropServices::Marshal::Copy(
            IntPtr(const_cast<unsigned char*>(source.data())), result, 0, result->Length);
        return result;
    }
public:
    static AssetBuffer^ CompileXml(XmlNode^ node, XmlNamespaceManager^ namespaces, bool bigEndian) {
        if (node == nullptr) throw gcnew ArgumentNullException("node");

        try {
            Relo::Chunk result = Native::CompileXmlai(Read(node,0),bigEndian);
            AssetBuffer^ buffer = gcnew AssetBuffer();
            buffer->InstanceData=Copy(result.instance);
            buffer->RelocationData=Copy(result.relocations);
            buffer->ImportsData=Copy(result.imports);
            return buffer;
        } catch (const std::exception& e) {
            throw gcnew NotSupportedException(gcnew String(e.what()));
        }
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration, TargetPlatform platform) {
        if (declaration == nullptr) throw gcnew ArgumentNullException("declaration");
        XmlNode^ node=declaration->Node;
        if (node == nullptr) throw gcnew ArgumentException("Missing Xmlai declaration node.");
        unsigned int expected=Native::XmlaiTypeId(marshal_as<std::string>(node->LocalName));
        if (!expected || declaration->Handle->TypeId != expected)
            throw gcnew NotSupportedException("Xmlai root/TypeId mismatch.");
        return CompileXml(node,declaration->Document->NamespaceManager,platform != TargetPlatform::Win32);
    }
};
}
