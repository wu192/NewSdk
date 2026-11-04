#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlsinglesound.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlsinglesoundBridge abstract sealed {
 static Native::XmlsinglesoundNode Read(XmlNode^ node) {
  Native::XmlsinglesoundNode input;

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
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlsinglesound root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlsinglesound(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class BaseAudioEventInfoCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"BaseAudioEventInfo");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1079232276u)throw gcnew NotSupportedException("Expected BaseAudioEventInfo TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class BaseSingleSoundCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"BaseSingleSound");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3228425025u)throw gcnew NotSupportedException("Expected BaseSingleSound TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class AudioEventCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"AudioEvent");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2219670431u)throw gcnew NotSupportedException("Expected AudioEvent TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicTrackCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"MusicTrack");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1883691512u)throw gcnew NotSupportedException("Expected MusicTrack TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MP3MusicTrackCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"MP3MusicTrack");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1013434420u)throw gcnew NotSupportedException("Expected MP3MusicTrack TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class DialogEventCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"DialogEvent");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3558134211u)throw gcnew NotSupportedException("Expected DialogEvent TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MP3DialogEventCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"MP3DialogEvent");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1479988269u)throw gcnew NotSupportedException("Expected MP3DialogEvent TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class AmbientStreamCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"AmbientStream");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3928762264u)throw gcnew NotSupportedException("Expected AmbientStream TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MP3AmbientStreamCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"MP3AmbientStream");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1813598662u)throw gcnew NotSupportedException("Expected MP3AmbientStream TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MultisoundCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlsinglesoundBridge::Compile(node,namespaces,bigEndian,"Multisound");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2745675575u)throw gcnew NotSupportedException("Expected Multisound TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
}

