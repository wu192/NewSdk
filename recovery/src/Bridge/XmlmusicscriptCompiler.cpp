#include <msclr/marshal_cppstd.h>
#include <stdexcept>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlmusicscript.h"
#using <BinaryAssetBuilder.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlmusicscriptBridge abstract sealed {
 static Native::XmlmusicscriptNode Read(XmlNode^ node) {
  Native::XmlmusicscriptNode input;

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
  if(node->LocalName!=type)throw gcnew NotSupportedException("Incorrect Xmlmusicscript root type.");
  // XmlCompiler's EA-qualified node contract is independent of Tokenizer.
  // NamespaceManager is accepted for the same public ABI as the other bridges.
  try{
   auto result=Native::CompileXmlmusicscript(Read(node),big);
   auto buffer=gcnew AssetBuffer();
   buffer->InstanceData=Copy(result.instance);buffer->RelocationData=Copy(result.relocations);buffer->ImportsData=Copy(result.imports);
   return buffer;
  }catch(const std::exception& e){throw gcnew NotSupportedException(gcnew String(e.what()));}
 }
};
public ref class MusicScriptConditionNuggetBaseCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNuggetBase");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2292378780u)throw gcnew NotSupportedException("Expected MusicScriptConditionNuggetBase TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionExpensiveNuggetBaseCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionExpensiveNuggetBase");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=849624115u)throw gcnew NotSupportedException("Expected MusicScriptConditionExpensiveNuggetBase TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_LocalPlayerIsObserverCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_LocalPlayerIsObserver");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=188286714u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_LocalPlayerIsObserver TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_UnitsFarFromBaseCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_UnitsFarFromBase");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2620791304u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_UnitsFarFromBase TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_TimeFromStartOfLevelCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_TimeFromStartOfLevel");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1464425282u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_TimeFromStartOfLevel TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_TrackPlayedCountCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_TrackPlayedCount");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=57370491u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_TrackPlayedCount TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_SpecificTrackTypePlayingCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_SpecificTrackTypePlaying");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=739280456u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_SpecificTrackTypePlaying TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_AnyTrackPlayingCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_AnyTrackPlaying");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3402992833u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_AnyTrackPlaying TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_ObjectsOfTypeExistCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_ObjectsOfTypeExist");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=4286308287u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_ObjectsOfTypeExist TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_EvaEventPlayedRecentlyCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_EvaEventPlayedRecently");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2743973749u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_EvaEventPlayedRecently TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_ObjectsNearEvaEventCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_ObjectsNearEvaEvent");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3386715757u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_ObjectsNearEvaEvent TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_ScoredKillCountCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_ScoredKillCount");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3620764004u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_ScoredKillCount TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_NotCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_Not");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=4007043648u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_Not TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_OrCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_Or");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2281773838u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_Or TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_AndCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_And");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1343682755u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_And TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_PathMusicStateLastActiveCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_PathMusicStateLastActive");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2912435838u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_PathMusicStateLastActive TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_PathMusicStateCurrentlyActiveCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_PathMusicStateCurrentlyActive");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3727134806u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_PathMusicStateCurrentlyActive TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_EngagementCountCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_EngagementCount");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=3476397162u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_EngagementCount TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_LocalPlayerKillRatioCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_LocalPlayerKillRatio");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=2818967523u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_LocalPlayerKillRatio TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptConditionNugget_HitBySuperWeaponCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptConditionNugget_HitBySuperWeapon");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1634186921u)throw gcnew NotSupportedException("Expected MusicScriptConditionNugget_HitBySuperWeapon TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
public ref class MusicScriptTrackCompiler abstract sealed {
public:
 static AssetBuffer^ CompileXml(XmlNode^ node,XmlNamespaceManager^ namespaces,bool bigEndian){return XmlmusicscriptBridge::Compile(node,namespaces,bigEndian,"MusicScriptTrack");}
 static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration,TargetPlatform platform){if(declaration==nullptr)throw gcnew ArgumentNullException("declaration");if(declaration->Handle->TypeId!=1038067901u)throw gcnew NotSupportedException("Expected MusicScriptTrack TypeId.");return CompileXml(declaration->Node,declaration->Document->NamespaceManager,platform!=TargetPlatform::Win32);}
};
}

