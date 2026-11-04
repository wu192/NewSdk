#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmleffects.h"
#using <BinaryAssetBuilder.Core.dll>
#using <System.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmleffectsXml abstract sealed {
public:

    static void Root(XmlNode^ node, String^ name) {
        if (node == nullptr) throw gcnew ArgumentNullException("node");
        if (node->LocalName != name)
            throw gcnew NotSupportedException("Expected effects root " + name);
    }
    static array<Byte>^ Copy(const std::vector<unsigned char>& src) {
        array<Byte>^ dst = gcnew array<Byte>(static_cast<int>(src.size()));
        if (dst->Length) System::Runtime::InteropServices::Marshal::Copy(
            IntPtr(const_cast<unsigned char*>(src.data())), dst, 0, dst->Length);
        return dst;
    }
    static AssetBuffer^ Buffer(const Relo::Chunk& chunk) {
        AssetBuffer^ result = gcnew AssetBuffer();
        result->InstanceData = Copy(chunk.instance);
        result->RelocationData = Copy(chunk.relocations);
        result->ImportsData = Copy(chunk.imports);
        return result;
    }

    static XmlNode^ Child(XmlNode^ node, String^ name) {
        for each(XmlNode^ child in node->ChildNodes)
            if(child->NodeType == XmlNodeType::Element && child->LocalName == name && child->NamespaceURI == node->NamespaceURI) return child;
        return nullptr;
    }
    static std::string Attr(XmlNode^ node, String^ name, String^ fallback) {
        XmlNodeAdapter^ a = gcnew XmlNodeAdapter(node->CreateNavigator(), nullptr);
        return marshal_as<std::string>(a->Attribute(name, fallback));
    }

    static void Field(XmlNode^ node, String^ name, String^ fallback, unsigned int offset,
        Native::XmleffectsFieldKind kind, String^ enumType, Native::XmleffectsNuggetInput& input) {
        XmlNodeAdapter^ a=gcnew XmlNodeAdapter(node->CreateNavigator(),nullptr);
        String^ value=a->Attribute(name,fallback);
        if(value == nullptr) return;
        Native::XmleffectsFieldInput field;
        field.offset=offset; field.kind=kind; field.text=marshal_as<std::string>(value);
        if(enumType != nullptr) field.enumType=marshal_as<std::string>(enumType);
        input.fields.push_back(field);
    }
    static void Vector(XmlNode^ owner, String^ name, unsigned int offset, bool rgb, Native::XmleffectsNuggetInput& input) {
        XmlNode^ child=Child(owner,name);
        if(child == nullptr) return;
        array<String^>^ axes=rgb ? gcnew array<String^>{"R","G","B"} : gcnew array<String^>{"x","y","z"};

        String^ fallback = nullptr;
        if (!rgb) fallback = "0.0";
        for(int i=0;i<3;++i) Field(child,axes[i],fallback,offset+4*i,Native::EffectFloat,nullptr,input);
    }

    static void NuggetRandom(XmlNode^ owner, String^ name, unsigned int offset, Native::XmleffectsNuggetInput& input) {
        XmlNode^ node=Child(owner,name);
        if(node==nullptr) return;

        Field(node,"Type","UNIFORM",offset,Native::EffectEnum,"DistributionType",input);
        Field(node,"Low",nullptr,offset+4,Native::EffectFloat,nullptr,input);
        Field(node,"High",nullptr,offset+8,Native::EffectFloat,nullptr,input);
    }
    static void Nugget(XmlNode^ node, Native::XmleffectsNuggetInput& input) {
        input.typeId=Native::ParseUnsigned(Attr(node,"TypeId","0"));
        System::Collections::Generic::List<String^>^ attrs=gcnew System::Collections::Generic::List<String^>(gcnew array<String^>{"TypeId","StopIfPlayed","Weather","OnlyIfOnLand","PlayIfSourceIsStealthed","RequiredSecondaryModelConditions","ExcludedSecondaryModelConditions","RequiredSourceModelConditions","ExcludedSourceModelConditions"});
        System::Collections::Generic::List<String^>^ children=gcnew System::Collections::Generic::List<String^>(gcnew array<String^>{"SourceMustNotHaveBeenDisabledThisFrameByType","SecondaryObjectFilter","SourceObjectFilter"});
        switch(input.typeId) {
        case 503892219u: {
            input.size=48; attrs->Add("Value");
            Field(node,"Value",nullptr,44,Native::EffectReference,nullptr,input); break;
        }
        case 638391350u: {
            input.size=48; attrs->Add("Event");
            Field(node,"Event",nullptr,44,Native::EffectReference,nullptr,input); break;
        }
        case 4011332978u: {
            input.size=48; attrs->Add("Type");
            Field(node,"Type",nullptr,44,Native::EffectEnum,"ViewShakeType",input); break;
        }
        case 2424288100u: {
            input.size=56; attrs->AddRange(gcnew array<String^>{"EvaEventOwner","EvaEventAlly","EvaEventEnemy"});
            Field(node,"EvaEventOwner","",44,Native::EffectReference,nullptr,input);
            Field(node,"EvaEventAlly","",48,Native::EffectReference,nullptr,input);
            Field(node,"EvaEventEnemy","",52,Native::EffectReference,nullptr,input); break;
        }
        case 4234415617u: {
            input.size=56; attrs->AddRange(gcnew array<String^>{"FX","Bone"});
            Field(node,"FX",nullptr,44,Native::EffectReference,nullptr,input);
            Field(node,"Bone",nullptr,48,Native::EffectString,nullptr,input); break;
        }
        case 2263385183u: {
            input.size=56; attrs->AddRange(gcnew array<String^>{"Model","RandomlyRotate","ExpireTimer"});
            Field(node,"Model",nullptr,44,Native::EffectReference,nullptr,input);
            Field(node,"RandomlyRotate","false",52,Native::EffectBoolean,nullptr,input);
            Field(node,"ExpireTimer","8s",48,Native::EffectTime,nullptr,input); break;
        }
        case 904475169u: {
            input.size=64; attrs->AddRange(gcnew array<String^>{"LaserTemplate","LaserBackwards"}); children->Add("Offset");
            Field(node,"LaserTemplate",nullptr,44,Native::EffectHash,nullptr,input);
            Field(node,"LaserBackwards","false",60,Native::EffectBoolean,nullptr,input);
            Vector(node,"Offset",48,false,input); break;
        }
        case 2386148390u: {
            input.size=72; attrs->Add("Thing"); children->AddRange(gcnew array<String^>{"PrimaryOffset","SecondaryOffset"});
            Field(node,"Thing",nullptr,44,Native::EffectHash,nullptr,input);
            Vector(node,"PrimaryOffset",48,false,input); Vector(node,"SecondaryOffset",60,false,input); break;
        }
        case 4293352609u: {
            input.size=72; attrs->AddRange(gcnew array<String^>{"Radius","RadiusAsPercentOfObjectSize","IncreaseTime","DecreaseTime"}); children->Add("Color");
            Field(node,"Radius",nullptr,44,Native::EffectFloat,nullptr,input);
            Field(node,"RadiusAsPercentOfObjectSize",nullptr,48,Native::EffectPercentage,nullptr,input);
            Field(node,"IncreaseTime",nullptr,52,Native::EffectUnsigned,nullptr,input);
            Field(node,"DecreaseTime",nullptr,56,Native::EffectUnsigned,nullptr,input);
            Vector(node,"Color",60,true,input); break;
        }
        case 2106748504u: {
            input.size=76; attrs->AddRange(gcnew array<String^>{"PreColorTime","PostColorTime","SustainedColorTime","Frequency","Amplitude"}); children->Add("Color");
            Field(node,"PreColorTime","2s",44,Native::EffectTime,nullptr,input);
            Field(node,"PostColorTime","2s",48,Native::EffectTime,nullptr,input);
            Field(node,"SustainedColorTime","1s",52,Native::EffectTime,nullptr,input);
            Field(node,"Frequency","1.0",56,Native::EffectFloat,nullptr,input);
            Field(node,"Amplitude","1.0",60,Native::EffectFloat,nullptr,input);
            Vector(node,"Color",64,true,input); break;
        }
        case 3768725989u: {
            input.size=56; attrs->AddRange(gcnew array<String^>{"Radius","Duration","Amplitude"});
            Field(node,"Radius",nullptr,44,Native::EffectFloat,nullptr,input);
            Field(node,"Duration",nullptr,48,Native::EffectTime,nullptr,input);
            Field(node,"Amplitude",nullptr,52,Native::EffectAngle,nullptr,input); break;
        }
        case 138194854u: {
            input.size=56; attrs->AddRange(gcnew array<String^>{"Type","Radius"}); children->Add("RandomRange");
            Field(node,"Type","RANDOM",44,Native::EffectEnum,"TerrainScorchType",input);
            Field(node,"Radius",nullptr,48,Native::EffectFloat,nullptr,input);
            if(Child(node,"RandomRange") != nullptr) {
                XmlNode^ range=Child(node,"RandomRange");
                input.hasRange=true; input.rangeLow=Attr(range,"Low","SCORCH_1"); input.rangeHigh=Attr(range,"High","SCORCH_4");
            } break;

        }
        case 3121289073u: {
            input.size=84; attrs->AddRange(gcnew array<String^>{"Bone","RequireFrequentUpdate","TriggerType","HoldBetweenStateID","ActionType","Particle"}); children->Add("Offset");
            Field(node,"Bone",nullptr,44,Native::EffectString,nullptr,input);
            Field(node,"RequireFrequentUpdate",nullptr,80,Native::EffectBoolean,nullptr,input);
            Field(node,"TriggerType",nullptr,52,Native::EffectEnum,"FXTriggerType",input);
            Field(node,"HoldBetweenStateID",nullptr,56,Native::EffectSigned,nullptr,input);
            Field(node,"ActionType",nullptr,60,Native::EffectEnum,"FXActionType",input);
            Field(node,"Particle",nullptr,64,Native::EffectReference,nullptr,input);
            Vector(node,"Offset",68,false,input); break;
        }
        case 866573550u: {
            input.size=108; attrs->AddRange(gcnew array<String^>{"Decal","Shader","Size","OrientToObject","OpacityStart","OpacityPeak","OpacityEnd","OpacityFadeTimeOne","OpacityFadeTimeTwo","OpacityPeakTime","StartingDelay","Lifetime","AllowOnObjects"});
            children->AddRange(gcnew array<String^>{"Color","Offset"});
            Field(node,"Decal",nullptr,44,Native::EffectString,nullptr,input);
            Field(node,"Shader","ALPHA",52,Native::EffectEnum,"DynamicDecalShaderType",input);
            Field(node,"Size",nullptr,56,Native::EffectFloat,nullptr,input);
            Field(node,"OrientToObject","true",104,Native::EffectBoolean,nullptr,input);
            Field(node,"OpacityStart",nullptr,60,Native::EffectFloat,nullptr,input);
            Field(node,"OpacityPeak","1.0",64,Native::EffectFloat,nullptr,input);
            Field(node,"OpacityEnd",nullptr,68,Native::EffectFloat,nullptr,input);
            Field(node,"OpacityFadeTimeOne",nullptr,72,Native::EffectTime,nullptr,input);
            Field(node,"OpacityFadeTimeTwo",nullptr,76,Native::EffectTime,nullptr,input);
            Field(node,"OpacityPeakTime",nullptr,80,Native::EffectTime,nullptr,input);
            Field(node,"StartingDelay",nullptr,84,Native::EffectTime,nullptr,input);
            Field(node,"Lifetime",nullptr,88,Native::EffectTime,nullptr,input);
            Field(node,"AllowOnObjects","true",105,Native::EffectBoolean,nullptr,input);
            if(Child(node,"Color")!=nullptr) {
                XmlNode^ color=Child(node,"Color");
                input.hasPackedColor=true;
                array<String^>^ channels={"R","G","B","A"};
                for(int i=0;i<4;++i) {
                    String^ fallback = "0";
                    if(i==3) fallback = "255";
                    input.rgba[i]=Attr(color,channels[i],fallback);
                }
            }
            if(Child(node,"Offset")!=nullptr) {
                XmlNode^ offset=Child(node,"Offset");
                Field(offset,"x","0.0",96,Native::EffectFloat,nullptr,input); Field(offset,"y","0.0",100,Native::EffectFloat,nullptr,input);
            } break;
        }
        case 751572338u: {
            input.size=148; attrs->AddRange(gcnew array<String^>{"Particle","Count","OrientToObject","Ricochet","CreateAtGroundHeight","CreateOverrideBone","UseTarget","TargetOverrideBone","TargetCoeff","SystemLife","SetTargetMatrix","OnlyIfOnWater"});
            children->AddRange(gcnew array<String^>{"Rotate","Offset","TargetOffset","Radius","Height","InitialDelay"});
            Field(node,"Particle",nullptr,44,Native::EffectReference,nullptr,input);
            Field(node,"Count","1",48,Native::EffectSigned,nullptr,input);
            Field(node,"OrientToObject",nullptr,140,Native::EffectBoolean,nullptr,input);
            Field(node,"Ricochet",nullptr,141,Native::EffectBoolean,nullptr,input);
            Field(node,"CreateAtGroundHeight",nullptr,142,Native::EffectBoolean,nullptr,input);
            Field(node,"CreateOverrideBone",nullptr,52,Native::EffectString,nullptr,input);
            Field(node,"UseTarget",nullptr,143,Native::EffectBoolean,nullptr,input);
            Field(node,"TargetOverrideBone",nullptr,60,Native::EffectString,nullptr,input);
            Field(node,"TargetCoeff","1.0",68,Native::EffectFloat,nullptr,input);
            Field(node,"SystemLife","-1s",72,Native::EffectTime,nullptr,input);
            Field(node,"SetTargetMatrix",nullptr,144,Native::EffectBoolean,nullptr,input);
            Field(node,"OnlyIfOnWater",nullptr,145,Native::EffectBoolean,nullptr,input);
            if(Child(node,"Rotate")!=nullptr) {
                XmlNode^ rotate=Child(node,"Rotate");
                Field(rotate,"X",nullptr,76,Native::EffectAngle,nullptr,input); Field(rotate,"Y",nullptr,80,Native::EffectAngle,nullptr,input); Field(rotate,"Z",nullptr,84,Native::EffectAngle,nullptr,input);
            }
            Vector(node,"Offset",88,false,input);
            if(Child(node,"TargetOffset")!=nullptr) {
                XmlNode^ offset=Child(node,"TargetOffset");
                input.hasTargetOffset=true;
                array<String^>^ axes={"x","y","z"};
                for(int i=0;i<3;++i) input.targetOffset[i]=Attr(offset,axes[i],"0.0");
            }
            NuggetRandom(node,"Radius",104,input); NuggetRandom(node,"Height",116,input); NuggetRandom(node,"InitialDelay",128,input); break;
        }
        default: input.size=44; break; // Original unknown-polymorphic base fallback writes TypeId zero.
        }

        // Only the disabled enum list is repeatable. Other scalar children must be unique.

        array<String^>^ conditions={"RequiredSecondaryModelConditions","ExcludedSecondaryModelConditions","RequiredSourceModelConditions","ExcludedSourceModelConditions"};
        XmlNodeAdapter^ baseAdapter=gcnew XmlNodeAdapter(node->CreateNavigator(),nullptr);
        for(int i=0;i<4;++i) {
            String^ value=baseAdapter->Attribute(conditions[i],nullptr);
            input.hasModelConditions[i]=value!=nullptr;
            if(value!=nullptr) input.modelConditions[i]=marshal_as<std::string>(value);
        }
        Field(node,"StopIfPlayed",nullptr,40,Native::EffectBoolean,nullptr,input);
        Field(node,"Weather","INVALID",20,Native::EffectEnum,"WeatherType",input);
        Field(node,"OnlyIfOnLand","false",41,Native::EffectBoolean,nullptr,input);
        Field(node,"PlayIfSourceIsStealthed","false",42,Native::EffectBoolean,nullptr,input);
        array<String^>^ filterNames={"SecondaryObjectFilter","SourceObjectFilter"};
        for(int i=0;i<2;++i) {
            XmlNode^ filter=Child(node,filterNames[i]);
            if(filter==nullptr) continue;
            input.hasFilters[i]=true;

            for each(XmlAttribute^ attr in filter->Attributes) if(attr->NamespaceURI->Length==0 && attr->LocalName!="TypeId")
                input.filters[i].attributes[marshal_as<std::string>(attr->LocalName)]=marshal_as<std::string>(attr->Value);
            for each(XmlNode^ child in filter->ChildNodes) if(child->NodeType==XmlNodeType::Element) {

                if(child->LocalName=="IncludeThing") input.filters[i].includeThings.push_back(marshal_as<std::string>(child->InnerText));
                else input.filters[i].excludeThings.push_back(marshal_as<std::string>(child->InnerText));
            }
        }
        for each(XmlNode^ child in node->ChildNodes) if(child->NodeType==XmlNodeType::Element && child->LocalName=="SourceMustNotHaveBeenDisabledThisFrameByType") {

            input.disabled.push_back(marshal_as<std::string>(child->InnerText));
        }
    }

};
public ref class FXListCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node, XmlNamespaceManager^ namespaces, bool bigEndian) {
        if (namespaces == nullptr) throw gcnew ArgumentNullException("namespaces");
        XmleffectsXml::Root(node, "FXList");

        // BAB annotates the root as well as polymorphic children. It is
        // metadata, not a serialized FXList field; reject mismatched routing.
        XmlAttribute^ rootTypeId=node->Attributes["TypeId"];
        if(rootTypeId!=nullptr && Native::ParseUnsigned(marshal_as<std::string>(rootTypeId->Value))!=2254974584u)
            throw gcnew NotSupportedException("Mismatched FXList root TypeId");

        XmlNodeAdapter^ adapter = gcnew XmlNodeAdapter(node->CreateNavigator(), namespaces);
        Native::XmleffectsFXListInput input;
        XmlNode^ listNode = XmleffectsXml::Child(node,"NuggetList");
        if(listNode != nullptr) for each(XmlNode^ nugget in listNode->ChildNodes) if(nugget->NodeType==XmlNodeType::Element) {
            Native::XmleffectsNuggetInput item; XmleffectsXml::Nugget(nugget,item); input.nuggets.push_back(item);
        }
        input.playEvenIfShrouded = marshal_as<std::string>(adapter->Attribute("PlayEvenIfShrouded", "false"));
        input.tailorable = marshal_as<std::string>(adapter->Attribute("Tailorable", "false"));
        String^ value = adapter->Attribute("CullTracking", nullptr);
        input.hasCullTracking = value != nullptr;
        if (value != nullptr) input.cullTracking = marshal_as<std::string>(value);
        value = adapter->Attribute("CullTrackingMin", nullptr);
        input.hasCullTrackingMin = value != nullptr;
        if (value != nullptr) input.cullTrackingMin = marshal_as<std::string>(value);
        value = adapter->Attribute("CullTrackingMax", nullptr);
        input.hasCullTrackingMax = value != nullptr;
        if (value != nullptr) input.cullTrackingMax = marshal_as<std::string>(value);
        return XmleffectsXml::Buffer(Native::CompileXmleffectsFXList(input, bigEndian));
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration, TargetPlatform platform) {
        if (declaration == nullptr) throw gcnew ArgumentNullException("declaration");
        if (declaration->Handle->TypeId != 2254974584u)
            throw gcnew NotSupportedException("Expected FXList TypeId");
        return CompileXml(declaration->Node, declaration->Document->NamespaceManager, platform != TargetPlatform::Win32);
    }
};
}
