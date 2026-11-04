#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlaudio.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlaudioBridge abstract sealed {
 static Native::XmlaudioNode Read(XmlNode^ node) {
  Native::XmlaudioNode input;

  input.name=marshal_as<std::string>(node->LocalName);
  if(node->Attributes!=nullptr)for each(XmlAttribute^ a in node->Attributes){
   if(a->NamespaceURI=="http://www.w3.org/2000/xmlns/")continue;

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
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlaudio root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlaudio(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class AudioEventLimitGroupCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"AudioEventLimitGroup");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3906227957u)throw gcnew NotSupportedException("Expected AudioEventLimitGroup TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class AudioLODCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"AudioLOD");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=299416263u)throw gcnew NotSupportedException("Expected AudioLOD TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class AudioSettingsCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"AudioSettings");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1443425905u)throw gcnew NotSupportedException("Expected AudioSettings TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class VideoCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"Video");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=587347702u)throw gcnew NotSupportedException("Expected Video TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class VideoEventListCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"VideoEventList");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1447728787u)throw gcnew NotSupportedException("Expected VideoEventList TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MicrophoneSettingsCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"MicrophoneSettings");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1826140078u)throw gcnew NotSupportedException("Expected MicrophoneSettings TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class BootupDisplaySequenceCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlaudioBridge::Compile(node,namespaces,bigEndian,"BootupDisplaySequence");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3370391592u)throw gcnew NotSupportedException("Expected BootupDisplaySequence TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
}

