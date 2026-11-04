using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
class XmluisettingsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"HotKeySlot","MouseCursor","BasicMouseCursors","UIMouseDragSelectionFeedback","UIFloatingTextDisplay","UIMessageDisplay","UIMilitaryCaptionDisplay","UISubtitleDisplay","UITimerDisplay","DynamicGameLOD","GameLODPreset","StaticGameLOD","UICoopTinting","RadiusCursor"};
    static readonly int[] Sizes={24,16,20,20,32,32,56,36,64,24,28,80,28,12};
    static readonly uint[] TypeIds={2800139175u,3784442503u,611271731u,611126373u,1559937240u,1697313181u,1043989897u,165611890u,951036436u,2901356964u,354222972u,531798225u,4243804314u,544776545u};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7","MIXEDCase"};
    static readonly string[] Bools={"true","false","TRUE","1","0","t","T"," true","yes","","10"};
    static readonly string[] TimeValues={"0s","1s","250ms","1.25s","0.5m","2d1s","-1s","1s junk","2ms","1000ms","garbage","","1e30s","2147483648ms","25,5s"};
    static IAssetBuilderPlugin original;
    static int fixtures,comparisons,rejections,layouts;
    static XmlDocument Parse(string text){XmlDocument d=new XmlDocument();d.LoadXml(text);return d;}
    static string Escape(string text){return text.Replace("&","&amp;").Replace("'","&apos;").Replace("<","&lt;").Replace("\"","&quot;");}
    static string Root(string type,string attrs,string children){return "<"+type+" xmlns='"+NS+"' id='Oracle' "+attrs+">"+children+"</"+type+">";}
    static XmlNamespaceManager Namespaces(XmlDocument d){XmlNamespaceManager n=new XmlNamespaceManager(d.NameTable);n.AddNamespace("ea",NS);return n;}
    static InstanceDeclaration Declaration(XmlDocument d) {
        AssetDeclarationDocument document=new AssetDeclarationDocument();
        Type stateType=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);
        object state=FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state,Namespaces(d));
        typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(document,state);
        InstanceDeclaration declaration=new InstanceDeclaration(document);declaration.XmlNode=d.DocumentElement;return declaration;
    }
    static void Equal(string label,byte[] a,byte[] b){
        if(a==null||b==null||a.Length!=b.Length)throw new Exception(label+" length original="+(a==null?-1:a.Length)+" recovered="+(b==null?-1:b.Length));
        for(int i=0;i<a.Length;++i)if(a[i]!=b[i])throw new Exception(label+" byte="+i+" original="+a[i]+" recovered="+b[i]);++comparisons;
    }
    static void Check(bool test,string label){if(!test)throw new Exception(label);++layouts;}
    static uint Word(byte[] bytes,int offset,bool big){return big?((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3]:BitConverter.ToUInt32(bytes,offset);}
    static AssetBuffer Call(Type type,string method,object[] args){try{return (AssetBuffer)type.GetMethod(method).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void SideArray(byte[] bytes,byte[] instance,bool big,bool relocation,string label){
        if(bytes.Length==0)return;
        Check(bytes.Length%4==0,label+" alignment");Check(Word(bytes,bytes.Length-4,big)==UInt32.MaxValue,label+" sentinel");
        HashSet<uint> seen=new HashSet<uint>();for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at%4==0&&(ulong)at+4<=(ulong)instance.Length,label+" aligned field bounds");Check(seen.Add(at),label+" unique bookmark offsets");if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmluisettingsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmluisettingsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}
    static XmlDocument FieldDocument(string root,string path,string name,string value){XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement child=d.CreateElement(part,NS);node.AppendChild(child);node=child;}node.SetAttribute(name,value);return d;}
    static readonly string[][] Fields={
        new string[]{"HotKeySlot","","Group","Enum:HotKeySlotGroup"},
        new string[]{"HotKeySlot","","Index","Signed"},
        new string[]{"HotKeySlot","","Name","String"},
        new string[]{"HotKeySlot","","VersionAdded","Signed"},
        new string[]{"MouseCursor","","File","String"},
        new string[]{"MouseCursor","","NumDirections","Signed"},
        new string[]{"BasicMouseCursors","","Normal","Reference"},
        new string[]{"BasicMouseCursors","","Arrow","Reference"},
        new string[]{"BasicMouseCursors","","Invalid","Reference"},
        new string[]{"BasicMouseCursors","","EnterText","Reference"},
        new string[]{"UIMouseDragSelectionFeedback","","BoxEdgeWidth","Float"},
        new string[]{"UIMouseDragSelectionFeedback","","LassoWidth","Float"},
        new string[]{"UIMouseDragSelectionFeedback","BoxColor","R","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","BoxColor","G","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","BoxColor","B","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","BoxColor","A","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","LassoColor","R","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","LassoColor","G","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","LassoColor","B","Unsigned"},
        new string[]{"UIMouseDragSelectionFeedback","LassoColor","A","Unsigned"},
        new string[]{"UIFloatingTextDisplay","","TimeOut","Time"},
        new string[]{"UIFloatingTextDisplay","","MoveUpSpeed","Velocity"},
        new string[]{"UIFloatingTextDisplay","","MoveVanishRate","Velocity"},
        new string[]{"UIFloatingTextDisplay","Font","Name","String"},
        new string[]{"UIFloatingTextDisplay","Font","Size","Unsigned"},
        new string[]{"UIFloatingTextDisplay","Font","Bold","Bool"},
        new string[]{"UIMessageDisplay","","Delay","Time"},
        new string[]{"UIMessageDisplay","Color1","R","Unsigned"},
        new string[]{"UIMessageDisplay","Color1","G","Unsigned"},
        new string[]{"UIMessageDisplay","Color1","B","Unsigned"},
        new string[]{"UIMessageDisplay","Color1","A","Unsigned"},
        new string[]{"UIMessageDisplay","Color2","R","Unsigned"},
        new string[]{"UIMessageDisplay","Color2","G","Unsigned"},
        new string[]{"UIMessageDisplay","Color2","B","Unsigned"},
        new string[]{"UIMessageDisplay","Color2","A","Unsigned"},
        new string[]{"UIMessageDisplay","Font","Name","String"},
        new string[]{"UIMessageDisplay","Font","Size","Unsigned"},
        new string[]{"UIMessageDisplay","Font","Bold","Bool"},
        new string[]{"UIMilitaryCaptionDisplay","","Centered","Bool"},
        new string[]{"UIMilitaryCaptionDisplay","","Delay","Time"},
        new string[]{"UIMilitaryCaptionDisplay","Color","R","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","Color","G","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","Color","B","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","Color","A","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","Position","x","Float"},
        new string[]{"UIMilitaryCaptionDisplay","Position","y","Float"},
        new string[]{"UIMilitaryCaptionDisplay","TitleFont","Name","String"},
        new string[]{"UIMilitaryCaptionDisplay","TitleFont","Size","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","TitleFont","Bold","Bool"},
        new string[]{"UIMilitaryCaptionDisplay","Font","Name","String"},
        new string[]{"UIMilitaryCaptionDisplay","Font","Size","Unsigned"},
        new string[]{"UIMilitaryCaptionDisplay","Font","Bold","Bool"},
        new string[]{"UISubtitleDisplay","","HoldTime","Time"},
        new string[]{"UISubtitleDisplay","","LineCount","Signed"},
        new string[]{"UISubtitleDisplay","Font","Name","String"},
        new string[]{"UISubtitleDisplay","Font","Size","Unsigned"},
        new string[]{"UISubtitleDisplay","Font","Bold","Bool"},
        new string[]{"UISubtitleDisplay","PanelColor","R","Unsigned"},
        new string[]{"UISubtitleDisplay","PanelColor","G","Unsigned"},
        new string[]{"UISubtitleDisplay","PanelColor","B","Unsigned"},
        new string[]{"UISubtitleDisplay","PanelColor","A","Unsigned"},
        new string[]{"UISubtitleDisplay","TextColor","R","Unsigned"},
        new string[]{"UISubtitleDisplay","TextColor","G","Unsigned"},
        new string[]{"UISubtitleDisplay","TextColor","B","Unsigned"},
        new string[]{"UISubtitleDisplay","TextColor","A","Unsigned"},
        new string[]{"UITimerDisplay","","Centered","Bool"},
        new string[]{"UITimerDisplay","","FlashDuration","Time"},
        new string[]{"UITimerDisplay","Position","x","Float"},
        new string[]{"UITimerDisplay","Position","y","Float"},
        new string[]{"UITimerDisplay","FlashColor","R","Unsigned"},
        new string[]{"UITimerDisplay","FlashColor","G","Unsigned"},
        new string[]{"UITimerDisplay","FlashColor","B","Unsigned"},
        new string[]{"UITimerDisplay","FlashColor","A","Unsigned"},
        new string[]{"UITimerDisplay","NormalFont","Name","String"},
        new string[]{"UITimerDisplay","NormalFont","Size","Unsigned"},
        new string[]{"UITimerDisplay","NormalFont","Bold","Bool"},
        new string[]{"UITimerDisplay","NormalColor","R","Unsigned"},
        new string[]{"UITimerDisplay","NormalColor","G","Unsigned"},
        new string[]{"UITimerDisplay","NormalColor","B","Unsigned"},
        new string[]{"UITimerDisplay","NormalColor","A","Unsigned"},
        new string[]{"UITimerDisplay","ReadyFont","Name","String"},
        new string[]{"UITimerDisplay","ReadyFont","Size","Unsigned"},
        new string[]{"UITimerDisplay","ReadyFont","Bold","Bool"},
        new string[]{"UITimerDisplay","ReadyColor","R","Unsigned"},
        new string[]{"UITimerDisplay","ReadyColor","G","Unsigned"},
        new string[]{"UITimerDisplay","ReadyColor","B","Unsigned"},
        new string[]{"UITimerDisplay","ReadyColor","A","Unsigned"},
        new string[]{"DynamicGameLOD","","Level","Enum:GameLODType"},
        new string[]{"DynamicGameLOD","","MinimumFPS","Signed"},
        new string[]{"DynamicGameLOD","","ParticleSkipMask","Signed"},
        new string[]{"DynamicGameLOD","","DebrisSkipMask","Signed"},
        new string[]{"DynamicGameLOD","","SlowDeathScale","Float"},
        new string[]{"GameLODPreset","","Level","Enum:GameLODType"},
        new string[]{"GameLODPreset","","SystemMemory","Signed"},
        new string[]{"GameLODPreset","","Xres","Signed"},
        new string[]{"GameLODPreset","","YRes","Signed"},
        new string[]{"GameLODPreset","Processor","Type","Enum:ProcessorType"},
        new string[]{"GameLODPreset","Processor","MinMHz","Signed"},
        new string[]{"StaticGameLOD","","Level","Enum:GameLODType"},
        new string[]{"StaticGameLOD","","ModelLOD","Enum:ModelLODType"},
        new string[]{"StaticGameLOD","","EffectsLOD","Enum:EffectsLODType"},
        new string[]{"StaticGameLOD","","MaxParticleCount","Signed"},
        new string[]{"StaticGameLOD","","UseShadowDecals","Bool"},
        new string[]{"StaticGameLOD","","UseShadowMapping","Bool"},
        new string[]{"StaticGameLOD","","ShadowLOD","Enum:ShadowLODType"},
        new string[]{"StaticGameLOD","","TerrainLOD","Enum:TerrainLODType"},
        new string[]{"StaticGameLOD","","UseTerrainNormalMap","Bool"},
        new string[]{"StaticGameLOD","","WaterLOD","Enum:WaterLODType"},
        new string[]{"StaticGameLOD","","MaxTankTrackEdges","Signed"},
        new string[]{"StaticGameLOD","","MaxTankTrackOpaqueEdges","Signed"},
        new string[]{"StaticGameLOD","","MaxTankTrackFadeDelay","Signed"},
        new string[]{"StaticGameLOD","","ShowProps","Bool"},
        new string[]{"StaticGameLOD","","TextureReductionFactor","Signed"},
        new string[]{"StaticGameLOD","","AnimationLOD","Enum:AnimationLODType"},
        new string[]{"StaticGameLOD","","ShaderLOD","Enum:ShaderLODType"},
        new string[]{"StaticGameLOD","","DecalLOD","Enum:DecalLODType"},
        new string[]{"StaticGameLOD","","MinParticlePriority","Enum:FXParticleSystem_Priority"},
        new string[]{"StaticGameLOD","","MinParticleSkipPriority","Enum:FXParticleSystem_Priority"},
        new string[]{"StaticGameLOD","","AntiAliasingQuality","Signed"},
        new string[]{"StaticGameLOD","","EnableVSync","Bool"},
        new string[]{"UICoopTinting","","FadeInFrames","Signed"},
        new string[]{"UICoopTinting","","FadeOutFrames","Signed"},
        new string[]{"UICoopTinting","","SustainFrames","Signed"},
        new string[]{"UICoopTinting","","TintFrequency","Float"},
        new string[]{"UICoopTinting","","TintAmplitude","Float"},
        new string[]{"UICoopTinting","TintColor","R","Unsigned"},
        new string[]{"UICoopTinting","TintColor","G","Unsigned"},
        new string[]{"UICoopTinting","TintColor","B","Unsigned"},
        new string[]{"UICoopTinting","TintColor","A","Unsigned"},
        new string[]{"RadiusCursor","DecalTemplate/Component","Texture","Reference"},
        new string[]{"RadiusCursor","DecalTemplate/Component","Texture2","Reference"},
        new string[]{"RadiusCursor","DecalTemplate/Component","Style","Enum:ShadowType"},
        new string[]{"RadiusCursor","DecalTemplate/Component","OpacityMin","Percentage"},
        new string[]{"RadiusCursor","DecalTemplate/Component","OpacityMax","Percentage"},
        new string[]{"RadiusCursor","DecalTemplate/Component","OpacityPulsesPerSecond","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","RotationMin","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","RotationMax","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","RotationDegreesPerSecond","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","MinRadius","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","MaxRadius","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","RadiusPulsesPerSecond","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","PositionOffsetMin","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","PositionOffsetMax","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component","PositionOffsetPulsesPerSecond","Float"},
        new string[]{"RadiusCursor","DecalTemplate/Component/Color","R","Unsigned"},
        new string[]{"RadiusCursor","DecalTemplate/Component/Color","G","Unsigned"},
        new string[]{"RadiusCursor","DecalTemplate/Component/Color","B","Unsigned"},
        new string[]{"RadiusCursor","DecalTemplate/Component/Color","A","Unsigned"},
    };
    static string[] WithNulEnumCases(string[] input) { List<string> values=new List<string>(input);foreach(string value in input)values.Add(value+"\0tail");values.Add("\0HIGH");values.Add("unknown\0HIGH");return values.ToArray(); }
    static readonly string[] Strings={"","a","abc","abcd","abcde","A&B<font>","é","资源","😀","e\u0301",new string('x',257),"a\0tail","\0leading","多字节\0尾部"};
    static string[] Values(string kind) {
        if(kind=="Enum:AnimationLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","ULTRA_HIGH","VERY_LOW","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:DecalLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","OFF","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:EffectsLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","ULTRA_HIGH","VERY_LOW","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:GameLODType")return WithNulEnumCases(new string[]{"CUSTOM","HIGH","LOW","MEDIUM","ULTRA_HIGH","UNKNOWN","VERY_LOW","unknown","","0","custom","CUSTOM unknown"});
        if(kind=="Enum:ModelLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:ProcessorType")return WithNulEnumCases(new string[]{"ATHLON","ATHLON_64","CORE_DUO","PENTIUM_4","UNKNOWN","unknown","","0","athlon","ATHLON unknown"});
        if(kind=="Enum:ShaderLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","ULTRA_HIGH","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:ShadowLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","OFF","ULTRA_HIGH","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:TerrainLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","ULTRA_HIGH","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:WaterLODType")return WithNulEnumCases(new string[]{"HIGH","LOW","MEDIUM","ULTRA_HIGH","unknown","","0","high","HIGH unknown"});
        if(kind=="Enum:FXParticleSystem_Priority")return WithNulEnumCases(new string[]{"ALWAYS_RENDER","HIGH_OR_ABOVE","HIGH_OR_BELOW","INVALID_PRIORITY","LOW_OR_ABOVE","LOW_OR_BELOW","MEDIUM_OR_ABOVE","MEDIUM_OR_BELOW","ULTRA_HIGH_ONLY","ULTRA_HIGH_OR_BELOW","VERY_LOW_ONLY","VERY_LOW_OR_ABOVE","unknown","","0","always_render","ALWAYS_RENDER unknown"});
        if(kind=="Enum:ShadowType")return WithNulEnumCases(new string[]{"ADDITIVE_DECAL","ADDITIVE_DECAL_DYNAMIC","ALPHA_DECAL","ALPHA_DECAL_DYNAMIC","DECAL","MERGE_DECAL","NONE","unknown","","0","additive_decal","ADDITIVE_DECAL unknown"});
        if(kind=="Time")return TimeValues;
        if(kind=="Bool")return Bools;
        if(kind=="Reference")return Refs;
        if(kind=="String")return Strings;
        if(kind=="Enum:HotKeySlotGroup")return WithNulEnumCases(new string[]{"Bookmarks","Camera","Cheat","Misc","NonConfigurable","PlayerPowersBar","SideBar","UnitCommands","misc","unknown","","0","Camera Misc"});
        return Numbers;
    }
    static XmlDocument SetField(string[] f,string value){XmlDocument d=FieldDocument(f[0],f[1],f[2],"");XmlElement last=(XmlElement)d.SelectNodes("//*")[d.SelectNodes("//*").Count-1];last.SetAttribute(f[2],value);return d;}
    static string FullChildren(string type) {
        const string color=" R='17' G='34' B='51'";
        const string font=" Name='Arial' Size='18' Bold='true'";
        switch(type) {
        case "RadiusCursor":return "<DecalTemplate><Component Style='DECAL' OpacityMin='25%' OpacityMax='75%'><Color"+color+" A='68'/></Component></DecalTemplate>";
        case "UICoopTinting":return "<TintColor"+color+"/>";
        case "UIMouseDragSelectionFeedback":return "<BoxColor"+color+" A='68'/><LassoColor"+color+" A='136'/>";
        case "UIFloatingTextDisplay":return "<Font"+font+"/>";
        case "UIMessageDisplay":return "<Color1"+color+"/><Color2"+color+"/><Font"+font+"/>";
        case "UIMilitaryCaptionDisplay":return "<Color"+color+"/><Position x='1.25' y='-2.5'/><TitleFont"+font+"/><Font Name='Courier' Size='11'/>";
        case "UISubtitleDisplay":return "<Font"+font+"/><PanelColor"+color+" A='68'/><TextColor"+color+"/>";
        case "UITimerDisplay":return "<Position x='1.25' y='-2.5'/><FlashColor"+color+"/><NormalFont"+font+"/><NormalColor"+color+"/><ReadyFont Name='Courier' Size='11'/><ReadyColor"+color+"/>";
        default:return "";
        }
    }
    static void ExpectWord(AssetBuffer b,int at,uint value,bool big,string label){Check(Word(b.InstanceData,at,big)==value,label+" @"+at);}
    static void Abi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer slot=Test("ABI/slot",Root("HotKeySlot","Group='unknown' Index='0x12345678' Name='abc' VersionAdded='-2'",""),p);
        ExpectWord(slot,4,3,big,"slot unknown group uses Bookmarks");ExpectWord(slot,8,0x12345678,big,"slot index");ExpectWord(slot,12,3,big,"slot byte string count");ExpectWord(slot,16,24,big,"slot string pointer");ExpectWord(slot,20,0xfffffffe,big,"slot version");
        Check(slot.InstanceData.Length==28&&slot.InstanceData[24]==97&&slot.InstanceData[27]==0,"slot ANSI payload and padding");Check(slot.RelocationData.Length==8&&Word(slot.RelocationData,0,big)==16,"slot string relocation offset");
        AssetBuffer cursor=Test("ABI/cursor",Root("MouseCursor","File='abcd' NumDirections='8'",""),p);
        ExpectWord(cursor,4,4,big,"cursor string count");ExpectWord(cursor,8,16,big,"cursor pointer");ExpectWord(cursor,12,8,big,"cursor directions");Check(cursor.InstanceData.Length==24,"cursor aligned payload");
        AssetBuffer refs=Test("ABI/reference",Root("BasicMouseCursors","Normal='Name\\7' Arrow='Name\\11' Invalid='Name\\13' EnterText='Name\\17'",""),p);
        Check(refs.InstanceData.Length==20&&refs.RelocationData.Length==0&&refs.ImportsData.Length==20,"four direct imported cursors");
        uint[] indices={8,12,14,18};for(int i=0;i<4;++i){ExpectWord(refs,4+4*i,indices[i],big,"cursor index");Check(Word(refs.ImportsData,4*i,big)==4+4*i,"cursor import address");}
        AssetBuffer drag=Test("ABI/drag",Root("UIMouseDragSelectionFeedback","",FullChildren("UIMouseDragSelectionFeedback")),p);
        ExpectWord(drag,4,0x40000000,big,"drag default width");ExpectWord(drag,8,0x40000000,big,"lasso default width");ExpectWord(drag,12,0x44112233,big,"ARGB packing");ExpectWord(drag,16,0x88112233,big,"second ARGB packing");
        AssetBuffer floating=Test("ABI/floating-default",Root("UIFloatingTextDisplay","",""),p);
        ExpectWord(floating,4,BitConverter.ToUInt32(BitConverter.GetBytes((float)((double)(float)333*(double)0.001f)),0),big,"time rounding");ExpectWord(floating,8,0x3e4ccccd,big,"logic frame velocity =0.2f");ExpectWord(floating,12,BitConverter.ToUInt32(BitConverter.GetBytes((float)((double)0.2f*(double)0.1f)),0),big,"velocity double multiplication");
        AssetBuffer military=Test("ABI/military",Root("UIMilitaryCaptionDisplay","Centered='true'",FullChildren("UIMilitaryCaptionDisplay")),p);
        Check(military.InstanceData[52]==1&&military.InstanceData[53]==0&&military.InstanceData[55]==0,"military packed bool/padding");ExpectWord(military,4,BitConverter.ToUInt32(BitConverter.GetBytes((float)((double)750f*0.001f)),0),big,"military time");ExpectWord(military,8,0xff112233,big,"military color");ExpectWord(military,12,0x3fa00000,big,"military x");ExpectWord(military,16,0xc0200000,big,"military y");ExpectWord(military,20,5,big,"font name count");ExpectWord(military,24,56,big,"first font payload order");ExpectWord(military,28,18,big,"font size");Check(military.InstanceData[32]==1,"font packed bold");ExpectWord(military,40,64,big,"second font payload order");ExpectWord(military,44,11,big,"second font size");
        AssetBuffer subtitle=Test("ABI/subtitle",Root("UISubtitleDisplay","",FullChildren("UISubtitleDisplay")),p);ExpectWord(subtitle,4,0x41a00000,big,"subtitle 20s");ExpectWord(subtitle,8,8,big,"subtitle default lines");ExpectWord(subtitle,28,0x44112233,big,"subtitle panel");ExpectWord(subtitle,32,0xff112233,big,"subtitle text");
        AssetBuffer timer=Test("ABI/timer",Root("UITimerDisplay","Centered='true'",FullChildren("UITimerDisplay")),p);ExpectWord(timer,4,0x3f800000,big,"timer default time");ExpectWord(timer,8,0x3fa00000,big,"timer x");ExpectWord(timer,16,0xff112233,big,"flash color");ExpectWord(timer,24,64,big,"normal font payload order");ExpectWord(timer,44,72,big,"ready font payload order");Check(timer.InstanceData[60]==1&&timer.InstanceData[61]==0&&timer.InstanceData[63]==0,"timer bool/padding");
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        foreach(string[] f in Fields) {
            foreach(string value in Values(f[3]))TestDoc("field/"+f[0]+"/"+f[1]+"/"+f[2]+"/"+value,SetField(f,value),p);
            XmlDocument d=SetField(f,Values(f[3])[0]);foreach(XmlNode n in d.SelectNodes("//*"))((XmlElement)n).SetAttribute("TypeId","1");TestDoc("BAB metadata/"+f[0]+"/"+f[1]+"/"+f[2],d,p);
            XmlNodeList nodes=d.SelectNodes("//*");((XmlElement)nodes[nodes.Count-1]).SetAttribute("Unknown","1");Reject("nested unknown",d.OuterXml);
        }
        for(int i=0;i<Types.Length;++i) {
            string n=Types[i];AssetBuffer b=Test("defaults/"+n,Root(n,"",""),p);Check(b.InstanceData.Length==Sizes[i],"independent root size "+n);
            Test("metadata/"+n,Root(n,"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown attr "+n,Root(n,"Unknown='1'",""));Reject("qualified attr "+n,Root(n,"xmlns:q='urn:other' q:TypeId='1'",""));Reject("unknown child "+n,Root(n,"","<Unknown/>"));Reject("foreign child "+n,Root(n,"","<Unknown xmlns='urn:other'/>"));Reject("text "+n,Root(n,"","payload"));
            string children=FullChildren(n);XmlDocument d=Parse(Root(n,"",children));if(children.Length>0){
                TestDoc("full-child/"+n,d,p);XmlNode clone=d.DocumentElement.CloneNode(true);for(int at=clone.ChildNodes.Count-1;at>=0;--at)d.DocumentElement.AppendChild(d.ImportNode(clone.ChildNodes[at],true));
                Reject("all duplicate "+n,d.OuterXml);
                d=Parse(Root(n,"",children));for(int at=d.DocumentElement.ChildNodes.Count-1;at>=0;--at){XmlDocument singleton=Parse(Root(n,"",""));singleton.DocumentElement.AppendChild(singleton.ImportNode(d.DocumentElement.ChildNodes[at],true));TestDoc("singleton/"+n+at,singleton,p);singleton.DocumentElement.AppendChild(singleton.DocumentElement.FirstChild.CloneNode(true));Reject("duplicate singleton "+n+at,singleton.OuterXml);}
                d=Parse(Root(n,"",children));AssetBuffer full=TestDoc("invariance full/"+n,d,p);for(int at=d.DocumentElement.ChildNodes.Count-2;at>=0;--at)d.DocumentElement.AppendChild(d.DocumentElement.ChildNodes[at]);AssetBuffer reversed=TestDoc("invariance reversed/"+n,d,p);Check(Convert.ToBase64String(full.InstanceData)==Convert.ToBase64String(reversed.InstanceData)&&Convert.ToBase64String(full.RelocationData)==Convert.ToBase64String(reversed.RelocationData)&&Convert.ToBase64String(full.ImportsData)==Convert.ToBase64String(reversed.ImportsData),"XML child order invariant "+n);
                foreach(XmlNode child in d.DocumentElement.ChildNodes){XmlDocument empty=Parse(Root(n,"","<"+child.LocalName+"/>"));TestDoc("present-empty/"+n+child.LocalName,empty,p);Reject("child text",Root(n,"","<"+child.LocalName+">text</"+child.LocalName+">"));Reject("grandchild",Root(n,"","<"+child.LocalName+"><Unknown/></"+child.LocalName+">"));}
            }
        }
        Reject("unknown root",Root("Unknown","",""));Reject("foreign root","<MouseCursor xmlns='other'/>");
        bool caught=false;try{XmluisettingsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){caught=true;}Check(caught,"null declaration");++rejections;
        Abi(p);
        LodAbi(p);
        RadiusAbi(p);
    }
    static void LodAbi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer dynamic=Test("LOD/default dynamic",Root("DynamicGameLOD","",""),p);
        uint[] dv={2,10,1,0,0x3f800000};for(int i=0;i<dv.Length;++i)ExpectWord(dynamic,4+4*i,dv[i],big,"dynamic defaults");
        AssetBuffer unknown=Test("LOD/unknown",Root("DynamicGameLOD","Level='unknown' MinimumFPS='-2' ParticleSkipMask='0x12345678' DebrisSkipMask='-1' SlowDeathScale='2.5'",""),p);
        uint[] uv={5,0xfffffffe,0x12345678,0xffffffff,0x40200000};for(int i=0;i<uv.Length;++i)ExpectWord(unknown,4+4*i,uv[i],big,"dynamic offsets");
        AssetBuffer stat=Test("LOD/static defaults",Root("StaticGameLOD","",""),p);
        uint[] sv={2,2,2,1500,2,1,1,100,25,30000,0,3,1,2,3,6,0};for(int i=0;i<sv.Length;++i)ExpectWord(stat,4+4*i,sv[i],big,"static defaults");
        byte[] bools={1,0,0,1,1,0,0,0};for(int i=0;i<bools.Length;++i)Check(stat.InstanceData[72+i]==bools[i],"static packed booleans and padding");
        stat=Test("LOD/static enums fallback",Root("StaticGameLOD","Level='unknown' ModelLOD='unknown' EffectsLOD='unknown' ShadowLOD='unknown' TerrainLOD='unknown' WaterLOD='unknown' AnimationLOD='unknown' ShaderLOD='unknown' DecalLOD='unknown' MinParticlePriority='unknown' MinParticleSkipPriority='unknown' UseShadowDecals='false' UseShadowMapping='true' UseTerrainNormalMap='true' ShowProps='false' EnableVSync='false'",""),p);
        int[] offsets={4,8,12,20,24,28,48,52,56,60,64};uint[] fallback={5,2,3,3,2,2,3,2,2,6,6};for(int i=0;i<offsets.Length;++i)ExpectWord(stat,offsets[i],fallback[i],big,"static enum fallback");
        byte[] setbools={0,1,1,0,0,0,0,0};for(int i=0;i<setbools.Length;++i)Check(stat.InstanceData[72+i]==setbools[i],"static alternate packed booleans");
        foreach(int count in new int[]{0,1,2,3,4,15,16,27,28,29,64,257}) {
            StringBuilder nodes=new StringBuilder();for(int i=0;i<count;++i)nodes.Append("<Processor Type='CORE_DUO' MinMHz='"+(i+100)+"'/>");
            AssetBuffer preset=Test("LOD/processor-list/"+count,Root("GameLODPreset","",nodes.ToString()),p);
            Check(preset.InstanceData.Length==28+8*count,"processor stride/count ABI");ExpectWord(preset,4,2,big,"preset level");ExpectWord(preset,8,500,big,"preset memory");ExpectWord(preset,12,1024,big,"preset Xres literal case");ExpectWord(preset,16,768,big,"preset YRes literal case");ExpectWord(preset,20,(uint)count,big,"processor count");ExpectWord(preset,24,count==0?0u:28u,big,"processor list pointer");
            Check(preset.ImportsData.Length==0,"processors no imports");Check(preset.RelocationData.Length==(count==0?0:8),"processor sole relocation");if(count>0)Check(Word(preset.RelocationData,0,big)==24,"processor pointer relocation offset");
            for(int i=0;i<count;++i){ExpectWord(preset,28+8*i,2,big,"processor enum stride");ExpectWord(preset,32+8*i,(uint)(100+i),big,"processor signed MHz stride");}
        }
        AssetBuffer empty=Test("LOD/empty processor",Root("GameLODPreset","","<Processor/>"),p);ExpectWord(empty,28,0,big,"absent processor enum remains zero");ExpectWord(empty,32,0,big,"absent processor MHz remains zero");
        empty=Test("LOD/unknown processor",Root("GameLODPreset","","<Processor Type='unknown' MinMHz='-1'/>"),p);ExpectWord(empty,28,3,big,"processor enum fallback ATHLON");ExpectWord(empty,32,0xffffffff,big,"processor signed MHz");
        Reject("wrong Xres case",Root("GameLODPreset","XRes='1'",""));Reject("wrong YRes case",Root("GameLODPreset","Yres='1'",""));Reject("processor content",Root("GameLODPreset","","<Processor>text</Processor>"));Reject("processor unknown",Root("GameLODPreset","","<Processor Unknown='1'/>"));Reject("processor nested",Root("GameLODPreset","","<Processor><Unknown/></Processor>"));
        AssetBuffer coop=Test("Coop/default",Root("UICoopTinting","","<TintColor R='17' G='34' B='51'/>"),p);uint[] cv={3,3,10,0x3f800000,0x3f800000,0xff112233};for(int i=0;i<cv.Length;++i)ExpectWord(coop,4+4*i,cv[i],big,"coop defaults/offsets");
    }
    static void RadiusAbi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        foreach(int count in new int[]{0,1,2,3,4,15,16,27,28,29,64,257}) {
            StringBuilder children=new StringBuilder();for(int i=0;i<count;++i)children.Append("<Component Texture='Name\\"+(i+7)+"' Texture2='Name\\"+(i+17)+"' Style='unknown' RotationMin='2' RotationMax='3' RotationDegreesPerSecond='4' OpacityPulsesPerSecond='5' RadiusPulsesPerSecond='6' PositionOffsetMin='7' PositionOffsetMax='8' PositionOffsetPulsesPerSecond='9'><Color R='17' G='34' B='51' A='68'/></Component>");
            AssetBuffer b=Test("Radius/list/"+count,Root("RadiusCursor","","<DecalTemplate>"+children+"</DecalTemplate>"),p);
            Check(b.InstanceData.Length==12+64*count,"radius component64 stride");ExpectWord(b,4,(uint)count,big,"radius count");ExpectWord(b,8,count==0?0u:12u,big,"radius inline list pointer");Check(b.RelocationData.Length==(count==0?0:8),"radius sole relocation");if(count>0)Check(Word(b.RelocationData,0,big)==8,"radius pointer bookmark");Check(b.ImportsData.Length==(count==0?0:4*(count*2+1)),"radius paired imports count");
            // All radius imports belong to the same component-array allocation block.
            // Original Tracker sorts by (block,index), with original QuickSort tie behavior.
            HashSet<uint> importOffsets=new HashSet<uint>();uint previousIndex=0;
            for(int j=0;j<count*2;++j){uint field=Word(b.ImportsData,4*j,big);Check(importOffsets.Add(field),"radius unique import offsets");uint index=unchecked(Word(b.InstanceData,(int)field,big)-1u);Check(j==0||index>=previousIndex,"radius same-block import index order");previousIndex=index;}
            for(int i=0;i<count;++i) {
                int at=12+64*i;ExpectWord(b,at,(uint)(i+8),big,"radius texture");ExpectWord(b,at+4,(uint)(i+18),big,"radius texture2");ExpectWord(b,at+8,3,big,"radius ShadowType unknown ADDITIVE_DECAL");ExpectWord(b,at+12,BitConverter.ToUInt32(BitConverter.GetBytes(0.01f),0),big,"radius default min opacity one percent");ExpectWord(b,at+16,BitConverter.ToUInt32(BitConverter.GetBytes(0.01f),0),big,"radius default max opacity one percent");ExpectWord(b,at+20,0x40a00000,big,"radius opacity frequency");ExpectWord(b,at+24,0x40000000,big,"radius rotation min raw");ExpectWord(b,at+28,0x40400000,big,"radius rotation max raw");ExpectWord(b,at+32,0x40800000,big,"radius degree speed raw");ExpectWord(b,at+36,0x3f800000,big,"radius min default");ExpectWord(b,at+40,0x3f800000,big,"radius max default");ExpectWord(b,at+44,0x40c00000,big,"radius pulse");ExpectWord(b,at+48,0x40e00000,big,"radius offset min");ExpectWord(b,at+52,0x41000000,big,"radius offset max");ExpectWord(b,at+56,0x41100000,big,"radius offset frequency");ExpectWord(b,at+60,0x44112233,big,"radius inline ARGB");Check(importOffsets.Contains((uint)at)&&importOffsets.Contains((uint)(at+4)),"radius exact imported field address set");
            }
        }
        AssetBuffer empty=Test("Radius/component defaults",Root("RadiusCursor","","<DecalTemplate><Component/></DecalTemplate>"),p);ExpectWord(empty,20,0,big,"radius absent Style remains0");ExpectWord(empty,72,0,big,"radius absent color remains0");Check(empty.ImportsData.Length==0,"radius absent refs do not import");
        Reject("radius duplicate template",Root("RadiusCursor","","<DecalTemplate/><DecalTemplate/>"));Reject("radius unknown template attr",Root("RadiusCursor","","<DecalTemplate Unknown='1'/>"));Reject("radius unknown template child",Root("RadiusCursor","","<DecalTemplate><Unknown/></DecalTemplate>"));Reject("radius duplicate color",Root("RadiusCursor","","<DecalTemplate><Component><Color/><Color/></Component></DecalTemplate>"));
    }

    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Assembly assembly=Assembly.LoadFrom(args[0]);original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            using(SHA256 sha=SHA256.Create()){string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");Check(hash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","fixed original DLL");result["originalDllSha256"]=hash;}
            List<object> rows=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;Matrix(p);Dictionary<string,object> row=new Dictionary<string,object>();row["platform"]=(int)p;row["fixtures"]=fixtures-f;row["arrayComparisons"]=comparisons-c;row["rejections"]=rejections-r;row["layoutAssertions"]=layouts-l;rows.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=rows;result["fixtures"]=fixtures;result["arrayComparisons"]=comparisons;result["rejections"]=rejections;result["layoutAssertions"]=layouts;result["succeeded"]=true;
            Console.WriteLine("PASS UI settings fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

