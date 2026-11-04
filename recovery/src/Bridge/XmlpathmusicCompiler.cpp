#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlpathmusic.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlpathmusicBridge abstract sealed {
 static Native::XmlpathmusicNode Read(XmlNode^ node) {
  Native::XmlpathmusicNode input;

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
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlpathmusic root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlpathmusic(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class PathMusicGameDynamicStateCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicGameDynamicState");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=97402485u)throw gcnew NotSupportedException("Expected PathMusicGameDynamicState TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class PathMusicGameDynamicStateSetCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicGameDynamicStateSet");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=343094706u)throw gcnew NotSupportedException("Expected PathMusicGameDynamicStateSet TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class PathMusicGameDynamicTransitionCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicGameDynamicTransition");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2343360052u)throw gcnew NotSupportedException("Expected PathMusicGameDynamicTransition TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class PathMusicEventRuntimeCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicEventRuntime");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1122327197u)throw gcnew NotSupportedException("Expected PathMusicEventRuntime TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class PathMusicEventSetCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicEventSet");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3339496789u)throw gcnew NotSupportedException("Expected PathMusicEventSet TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class PathMusicMapRuntimeCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"PathMusicMapRuntime");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2026048286u)throw gcnew NotSupportedException("Expected PathMusicMapRuntime TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicPaletteCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlpathmusicBridge::Compile(node,namespaces,bigEndian,"MusicPalette");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2407383451u)throw gcnew NotSupportedException("Expected MusicPalette TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
}

