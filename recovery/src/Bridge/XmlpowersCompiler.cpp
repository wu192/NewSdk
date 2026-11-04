#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlpowers.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlpowersBridge abstract sealed {
 static Native::XmlpowersNode Read(XmlNode^ node) {
  Native::XmlpowersNode input;

  input.name=marshal_as<std::string>(node->LocalName);
  if(node->Attributes!=nullptr)for each(XmlAttribute^ a in node->Attributes){
   if(a->NamespaceURI=="http://www.w3.org/2000/xmlns/")continue;

   // XmlCompiler OCL uses typed lists rather than this metadata for dispatch.
   // BAB injects TypeId on all named-schema elements, including filters and references.
   if(a->LocalName=="TypeId")continue;
   input.attributes[marshal_as<std::string>(a->LocalName)]=marshal_as<std::string>(a->Value);
  }
  for each(XmlNode^ child in node->ChildNodes){
   if(child->NodeType==XmlNodeType::Element)input.children.push_back(Read(child));
   else if(child->NodeType==XmlNodeType::Text||child->NodeType==XmlNodeType::CDATA||child->NodeType==XmlNodeType::SignificantWhitespace||child->NodeType==XmlNodeType::Whitespace)
    input.text+=marshal_as<std::string>(child->Value);
  }
  return input;
 }
 static array<Byte>^ Copy(const std::vector<unsigned char>& source){
  auto result=gcnew array<Byte>(static_cast<int>(source.size()));
  if(result->Length)System::Runtime::InteropServices::Marshal::Copy(IntPtr(const_cast<unsigned char*>(source.data())),result,0,result->Length);
  return result;
 }
public:
 static AssetBuffer^ Compile(XmlNode^ node,XmlNamespaceManager^ namespaces,bool big,String^ type){
  if(node==nullptr)throw gcnew ArgumentNullException("node");
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlpowers root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlpowers(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class SpecialPowerTemplateCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpowersBridge::Compile(node,namespaces,bigEndian,"SpecialPowerTemplate");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=2178440954u)throw gcnew NotSupportedException("Expected SpecialPowerTemplate TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class ObjectCreationListCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpowersBridge::Compile(node,namespaces,bigEndian,"ObjectCreationList");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=3899542881u)throw gcnew NotSupportedException("Expected ObjectCreationList TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
}
