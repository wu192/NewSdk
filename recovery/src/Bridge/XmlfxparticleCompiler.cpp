#include <msclr/marshal_cppstd.h>
#include "XmlNodeAdapter.h"
#include "../Native/Xmlfxparticle.h"
#using <BinaryAssetBuilder.Core.dll>
#using <System.Core.dll>
using namespace BinaryAssetBuilder::Core;
using namespace msclr::interop;
namespace Recovered {
ref class XmlfxparticleXml abstract sealed {
public:

    static void Root(XmlNode^ node, String^ name) {
        if (node == nullptr) throw gcnew ArgumentNullException("node");
        if (node->LocalName != name)
            throw gcnew NotSupportedException("Expected particle root " + name);
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
    static void Random(XmlNode^ node, Native::XmlfxparticleRandomInput& input) {
        if(node == nullptr) return;

        input.present = true;
        input.type = Attr(node,"Type","UNIFORM");
        input.low = Attr(node,"Low","");
        input.high = Attr(node,"High","");
    }

    static void Field(XmlNode^ node, String^ name, String^ fallback, unsigned int offset,
        Native::XmlfxparticleFieldKind kind, String^ enumType, Native::XmlfxparticleNuggetInput& input) {
        XmlNodeAdapter^ a=gcnew XmlNodeAdapter(node->CreateNavigator(),nullptr);
        String^ value=a->Attribute(name,fallback);
        if(value == nullptr) return;
        Native::XmlfxparticleFieldInput field;
        field.offset=offset; field.kind=kind; field.text=marshal_as<std::string>(value);
        if(enumType != nullptr) field.enumType=marshal_as<std::string>(enumType);
        input.fields.push_back(field);
    }
    static void Vector(XmlNode^ owner, String^ name, unsigned int offset, bool rgb, Native::XmlfxparticleNuggetInput& input) {
        XmlNode^ child=Child(owner,name);
        if(child == nullptr) return;
        array<String^>^ axes=rgb ? gcnew array<String^>{"R","G","B"} : gcnew array<String^>{"x","y","z"};

        String^ fallback = "0.0"; if(rgb) fallback = nullptr;
        for(int i=0;i<3;++i) Field(child,axes[i],fallback,offset+4*i,Native::ParticleFloat,nullptr,input);
    }
    static void ModuleRandom(XmlNode^ node, String^ name, unsigned int offset, Native::XmlfxparticleModuleInput& input) {
        XmlNode^ child=Child(node,name);
        if(child == nullptr) return;
        Native::XmlfxparticleModuleRandom field; field.offset=offset; Random(child,field.input); input.random.push_back(field);
    }
    #include "XmlfxparticleModuleHelpers.inc"
    static void Module(XmlNode^ container, Native::XmlfxparticleModuleInput& input) {
        if(container==nullptr) return;

        XmlNode^ node=nullptr;
        for each(XmlNode^ child in container->ChildNodes) {
            if(child->NodeType==XmlNodeType::Element) {

                node=child;
            } else ;
        }
        if(node==nullptr) return;
        ModuleNode(node,input);
    }
    static void ModuleNode(XmlNode^ node, Native::XmlfxparticleModuleInput& input) {
        input.present=true; input.typeId=Native::ParseUnsigned(Attr(node,"TypeId","0"));
        Native::XmlfxparticleNuggetInput values;
        switch(input.typeId) {
        #include "XmlfxparticleModuleCases.inc"
        case 877888681u: { // FXParticlePhysicsBase
            input.size=4;
            break;
        }
        case 3201797789u: { // FXParticleDrawBase
            input.size=4;
            break;
        }
        case 3809157760u: { // FXParticleDrawStreak
            input.size=4;
            break;
        }
        case 2268785311u: { // FXParticleEmissionVelocityBase
            input.size=4;
            break;
        }
        case 1903056374u: { // FXParticleUpdateBase
            input.size=4;
            break;
        }
        case 612354849u: { // FXParticleEmissionVolumeBase
            input.size=8;
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 3543124331u: { // FXParticleEmissionVolumePoint
            input.size=8;
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 1423381183u: { // FXParticleEmissionVolumeLightning
            input.size=8;
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 1373300932u: { // FXParticleDrawLightSource
            input.size=8;
            Field(node,"MaximumIntensityRadiusRatio","0.0",4,Native::ParticleFloat,nullptr,values);
            break;
        }
        case 1865622437u: { // FXParticleDrawTrail
            input.size=24;
            Field(node,"TrailLife","30",4,Native::ParticleSigned,nullptr,values);
            Field(node,"UTile","1.0",8,Native::ParticleFloat,nullptr,values);
            Field(node,"VTile","1.0",12,Native::ParticleFloat,nullptr,values);
            Field(node,"UScrollRate","0.0",16,Native::ParticleFloat,nullptr,values);
            Field(node,"VScrollRate","0.0",20,Native::ParticleFloat,nullptr,values);
            break;
        }
        case 2381754661u: { // FXParticleDrawSwarm
            input.size=24;
            Field(node,"OpaqueSpeed","0",4,Native::ParticleFloat,nullptr,values);
            Field(node,"TransparentSpeed","100.0",8,Native::ParticleFloat,nullptr,values);
            Field(node,"SpeedStretchAmount","1.0",12,Native::ParticleFloat,nullptr,values);
            Field(node,"AttractStrength","0.0",16,Native::ParticleFloat,nullptr,values);
            Field(node,"EnvironmentTexture",nullptr,20,Native::ParticleReference,nullptr,values);
            break;
        }
        case 3110487039u: { // FXParticleDefaultPhysics
            input.size=36;
            Field(node,"Gravity",nullptr,4,Native::ParticleFloat,nullptr,values);
            Field(node,"Swirly",nullptr,32,Native::ParticleBoolean,nullptr,values);
            Field(node,"ParticlesAttachToBone",nullptr,33,Native::ParticleBoolean,nullptr,values);
            ModuleRandom(node,"VelocityDamping",8,input);
            Vector(node,"DriftVelocity",20,false,values);
            break;
        }
        case 1976375475u: { // FXParticleSwarmPhysics
            input.size=8;
            Field(node,"AttractStrength","0.0",4,Native::ParticleFloat,nullptr,values);
            break;
        }
        case 3992468329u: { // FXParticleEmissionVolumeSphere
            input.size=12;
            Field(node,"Radius",nullptr,8,Native::ParticleFloat,nullptr,values);
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 1242273524u: { // FXParticleEmissionVolumeBox
            input.size=20;
            Vector(node,"HalfSize",8,false,values);
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 1552576278u: { // FXParticleEmissionVolumeCylinder
            input.size=32;
            Field(node,"Radius",nullptr,8,Native::ParticleFloat,nullptr,values);
            Field(node,"RadiusRate",nullptr,12,Native::ParticleFloat,nullptr,values);
            Field(node,"Length",nullptr,16,Native::ParticleFloat,nullptr,values);
            Vector(node,"Offset",20,false,values);
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 795809322u: { // FXParticleEmissionVolumeSpline
            input.size=32;
            Vector(node,"StartPoint",8,false,values);
            Vector(node,"EndPoint",20,false,values);
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 228810652u: { // FXParticleEmissionVolumeLine
            input.size=32;
            Vector(node,"StartPoint",8,false,values);
            Vector(node,"EndPoint",20,false,values);
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 3772792373u: { // FXParticleEmissionVolumeTerrainFire
            input.size=48;
            Field(node,"CellEmissionChance",nullptr,8,Native::ParticleFloat,nullptr,values);
            if(Child(node,"Offset")!=nullptr) {
                XmlNode^ coord=Child(node,"Offset");
                ModuleRandom(coord,"x",12,input); ModuleRandom(coord,"y",24,input); ModuleRandom(coord,"z",36,input);
            }
            Field(node,"IsHollow",nullptr,4,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 2343798068u: { // FXParticleEmissionVelocitySphere
            input.size=16;
            ModuleRandom(node,"Speed",4,input);
            break;
        }
        case 3082112962u: { // FXParticleEmissionVelocityHemisphere
            input.size=16;
            ModuleRandom(node,"Speed",4,input);
            break;
        }
        case 3476018911u: { // FXParticleEmissionVelocityOutward
            input.size=28;
            ModuleRandom(node,"Speed",4,input);
            ModuleRandom(node,"OtherSpeed",16,input);
            break;
        }
        case 4058790702u: { // FXParticleEmissionVelocityCylinder
            input.size=28;
            ModuleRandom(node,"Radial",4,input);
            ModuleRandom(node,"Normal",16,input);
            break;
        }
        case 3880943874u: { // FXParticleEmissionVelocityOrtho
            input.size=40;
            if(Child(node,"Position")!=nullptr) {
                XmlNode^ coord=Child(node,"Position");
                ModuleRandom(coord,"x",4,input); ModuleRandom(coord,"y",16,input); ModuleRandom(coord,"z",28,input);
            }
            break;
        }
        case 3975650247u: { // FXParticleEmissionVelocityWavefront
            input.size=68;
            Field(node,"Shape","CIRCLE",4,Native::ParticleEnum,"FXParticleSystem_WavefrontShape",values);
            ModuleRandom(node,"Velocity",8,input);
            ModuleRandom(node,"Amplitude",20,input);
            ModuleRandom(node,"InitialRadius",32,input);
            ModuleRandom(node,"LineWidth",44,input);
            ModuleRandom(node,"LineAngle",56,input);
            break;
        }
        case 2693838876u: { // FXParticleEventBase
            input.size=12;
            Field(node,"EventFX",nullptr,4,Native::ParticleReference,nullptr,values);
            Field(node,"PerParticle",nullptr,8,Native::ParticleBoolean,nullptr,values);
            Field(node,"KillAfterEvent",nullptr,9,Native::ParticleBoolean,nullptr,values);
            break;
        }
        case 1075723135u: { // FXParticleEventCollision
            input.size=28;
            Field(node,"EventFX",nullptr,4,Native::ParticleReference,nullptr,values);
            Field(node,"PerParticle",nullptr,8,Native::ParticleBoolean,nullptr,values);
            Field(node,"KillAfterEvent",nullptr,9,Native::ParticleBoolean,nullptr,values);
            Field(node,"OrientFXToTerrain","false",24,Native::ParticleBoolean,nullptr,values);
            ModuleRandom(node,"HeightOffset",12,input);
            break;
        }
        case 3465979181u: { // FXParticleEventLife
            input.size=24;
            Field(node,"EventFX",nullptr,4,Native::ParticleReference,nullptr,values);
            Field(node,"PerParticle",nullptr,8,Native::ParticleBoolean,nullptr,values);
            Field(node,"KillAfterEvent",nullptr,9,Native::ParticleBoolean,nullptr,values);

            ModuleRandom(node,"EventTime",12,input);
            break;
        }
        default: {
            // Original MarshalUnknownPolymorphicType allocates four zero bytes.

            input.size=4; input.typeId=0; break;
        }
        }
        input.fields=values.fields;
    }

};
public ref class FXParticleSystemTemplateCompiler abstract sealed {
public:
    static AssetBuffer^ CompileXml(XmlNode^ node, XmlNamespaceManager^ namespaces, bool bigEndian) {
        if(namespaces == nullptr) throw gcnew ArgumentNullException("namespaces");
        XmlfxparticleXml::Root(node, "FXParticleSystemTemplate");

        // BAB inserts the root TypeId during preprocessing. The original root ignores
        // this metadata; this component accepts only its own matching numeric value.
        if(node->Attributes["TypeId"] != nullptr &&
            Native::ParseUnsigned(XmlfxparticleXml::Attr(node,"TypeId","0")) != 686292351u)
            throw gcnew NotSupportedException("Mismatched FXParticleSystemTemplate root TypeId");
        Native::XmlfxparticleParticleInput input;
        input.priority = XmlfxparticleXml::Attr(node,"Priority","ULTRA_HIGH_ONLY");
        input.shader = XmlfxparticleXml::Attr(node,"Shader","ADDITIVE");
        input.type = XmlfxparticleXml::Attr(node,"Type","PARTICLE");
        array<String^>^ refs = {"ParticleTexture","Drawable","EmitterSound"};
        for(int i=0;i<3;++i) input.references[i] = XmlfxparticleXml::Attr(node,refs[i],"");
        input.hasEmitterSound=node->Attributes["EmitterSound"] != nullptr;
        input.typedIds[0] = XmlfxparticleXml::Attr(node,"SlaveSystem","");
        input.typedIds[1] = XmlfxparticleXml::Attr(node,"PerParticleAttachedSystem","");
        input.lifetime = XmlfxparticleXml::Attr(node,"SystemLifetime","0");
        input.sortLevel = XmlfxparticleXml::Attr(node,"SortLevel","0");
        array<String^>^ flags = {"IsOneShot","IsGroundAligned","IsEmitAboveGroundOnly","IsParticleUpTowardsEmitter","UseMaximumHeight"};
        for(int i=0;i<5;++i) input.flags[i] = XmlfxparticleXml::Attr(node,flags[i],"false");
        XmlNode^ offset = XmlfxparticleXml::Child(node,"SlavePosOffset");
        if(offset != nullptr) {

            array<String^>^ axes = {"x","y","z"};
            for(int i=0;i<3;++i) input.offset[i] = XmlfxparticleXml::Attr(offset,axes[i],"0.0");
        }
        array<String^>^ randomNames = {"Lifetime","Size","StartSizeRate","BurstDelay","BurstCount","InitialDelay"};
        for(int i=0;i<6;++i) XmlfxparticleXml::Random(XmlfxparticleXml::Child(node,randomNames[i]),input.random[i]);
        XmlNode^ alphas = XmlfxparticleXml::Child(node,"Alphas");
        if(alphas != nullptr) {
            input.hasAlphas = true;

            for each(XmlNode^ alpha in alphas->ChildNodes) if(alpha->NodeType == XmlNodeType::Element) {

                Native::XmlfxparticleAlphaInput item;
                item.value.present = true;
                item.value.type = XmlfxparticleXml::Attr(alpha,"Type","UNIFORM");
                item.value.low = XmlfxparticleXml::Attr(alpha,"Low","");
                item.value.high = XmlfxparticleXml::Attr(alpha,"High","");
                item.relativeAge = XmlfxparticleXml::Attr(alpha,"RelativeAge","");
                item.frame = XmlfxparticleXml::Attr(alpha,"Frame","1000");
                input.alphas.push_back(item);
            }
        }
        XmlNode^ colors = XmlfxparticleXml::Child(node,"Colors");
        if(colors != nullptr) {
            input.hasColors = true;

            input.houseColor = XmlfxparticleXml::Attr(colors,"UseHouseColor","false");
            input.saturation = XmlfxparticleXml::Attr(colors,"HouseColorSaturation","1.0");

            for each(XmlNode^ color in colors->ChildNodes) if(color->NodeType == XmlNodeType::Element) {
                if(color->LocalName == "ColorScale") {

                     XmlfxparticleXml::Random(color,input.colorScale); continue;
                }

                Native::XmlfxparticleColorInput item;
                item.relativeAge = XmlfxparticleXml::Attr(color,"RelativeAge","");
                item.frame = XmlfxparticleXml::Attr(color,"Frame","1000");
                XmlNode^ rgb = XmlfxparticleXml::Child(color,"Color");
                if(rgb != nullptr) {

                    array<String^>^ channels={"r","g","b"};
                    for(int i=0;i<3;++i) item.rgb[i]=XmlfxparticleXml::Attr(rgb,channels[i],"");
                }
                input.colors.push_back(item);
            }
        }
        XmlNode^ wind = XmlfxparticleXml::Child(node,"Wind");
        if(wind != nullptr) {
            input.hasWind = true;
            array<String^>^ names={"Motion","Strength","FullStrengthDist","ZeroStrengthDist","AngleChangeMin","AngleChangeMax","PingPongStartAngleMin","PingPongStartAngleMax","PingPongEndAngleMin","PingPongEndAngleMax","TurbulenceAmplitude","TurbulenceFrequency"};
            array<String^>^ defaults={"NOT_USED","2.0","75.0","200.0",".15",".45","0.0","0.7853981633974483","5.4977871437821381","6.2831853071795864","0","0"};

            for(int i=0;i<12;++i) input.wind[i]=XmlfxparticleXml::Attr(wind,names[i],defaults[i]);
        }
        XmlNode^ events = XmlfxparticleXml::Child(node,"Event");
        if(events != nullptr) {

            for each(XmlNode^ item in events->ChildNodes) {
                if(item->NodeType==XmlNodeType::Element) {

                    Native::XmlfxparticleModuleInput eventInput;
                    XmlfxparticleXml::ModuleNode(item,eventInput); input.events.push_back(eventInput);
                } else ;
            }
        }
        array<String^>^ moduleNames={"Physics","Draw","Volume","Velocity","Update"};
        for(int i=0;i<5;++i) XmlfxparticleXml::Module(XmlfxparticleXml::Child(node,moduleNames[i]),input.modules[i]);
        return XmlfxparticleXml::Buffer(Native::CompileXmlfxparticleParticle(input,bigEndian));
    }
    static AssetBuffer^ ProcessInstance(InstanceDeclaration^ declaration, TargetPlatform platform) {
        if (declaration == nullptr) throw gcnew ArgumentNullException("declaration");
        if (declaration->Handle->TypeId != 686292351u)
            throw gcnew NotSupportedException("Expected FXParticleSystemTemplate TypeId");
        return CompileXml(declaration->Node, declaration->Document->NamespaceManager, platform != TargetPlatform::Win32);
    }
};
}

