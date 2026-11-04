#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlprogression.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlprogressionBridge abstract sealed {
 static Native::XmlprogressionNode Read(XmlNode^ node) {
  Native::XmlprogressionNode input;

  input.name=marshal_as<std::string>(node->LocalName);
  if(node->Attributes!=nullptr)for each(XmlAttribute^ a in node->Attributes){
   if(a->NamespaceURI=="http://www.w3.org/2000/xmlns/")continue;

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
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlprogression root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlprogression(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class LogicCommandCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"LogicCommand");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=2101756272u)throw gcnew NotSupportedException("Expected LogicCommand TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class PlayerTechCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"PlayerTech");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=2131237081u)throw gcnew NotSupportedException("Expected PlayerTech TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class PlayerTechRankCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"PlayerTechRank");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1802643394u)throw gcnew NotSupportedException("Expected PlayerTechRank TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class PlayerTechUpgradeBindingCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"PlayerTechUpgradeBinding");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=2140592493u)throw gcnew NotSupportedException("Expected PlayerTechUpgradeBinding TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class ExperienceLevelTemplateCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"ExperienceLevelTemplate");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1874610847u)throw gcnew NotSupportedException("Expected ExperienceLevelTemplate TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class PlayerTechStoreTemplateCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"PlayerTechStoreTemplate");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1213037816u)throw gcnew NotSupportedException("Expected PlayerTechStoreTemplate TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class PurchasePlayerTechButtonTemplateCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"PurchasePlayerTechButtonTemplate");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1566941920u)throw gcnew NotSupportedException("Expected PurchasePlayerTechButtonTemplate TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class ButtonSingleStateDataCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"ButtonSingleStateData");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=3048039928u)throw gcnew NotSupportedException("Expected ButtonSingleStateData TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class UICommandButtonCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"UICommandButton");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=134745024u)throw gcnew NotSupportedException("Expected UICommandButton TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class FontSubstitutionCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"FontSubstitution");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=91719417u)throw gcnew NotSupportedException("Expected FontSubstitution TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class FontTranslationCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"FontTranslation");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1318336226u)throw gcnew NotSupportedException("Expected FontTranslation TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
public ref class FontDefaultSettingCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlprogressionBridge::Compile(node,namespaces,bigEndian,"FontDefaultSetting");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){
  if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");
  if(declaration->Handle->TypeId!=1371131236u)throw gcnew NotSupportedException("Expected FontDefaultSetting TypeId.");
  return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);
 }
};
}

