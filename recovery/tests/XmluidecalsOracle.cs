using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
class XmluidecalsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Grid="UIDecalGrid",Cloud="UIDecalCloud",Marker="UIDecalDestinationMarker",World="UIWorldBuilderDecalGrid";
    static readonly string[] Types={Grid,Cloud,Marker,World};
    static readonly uint[] TypeIds={2599418799u,2524893660u,1532214660u,1238702511u};
    static readonly int[] Sizes={56,16,32,16};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Channels={"R","G","B","A"};
    static readonly string[] Destinations={"Normal","Aggressive","","UNKNOWN","normal","aggressive"," Normal","Normal ","0","1","Normal\0tail","Aggressive\0tail","UNKNOWN\0Normal"};
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
            Type type=typeof(XmluidecalsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmluidecalsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static XmlDocument Set(string root,string path,string attribute,string value){
        XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;
        if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement c=d.CreateElement(part,NS);node.AppendChild(c);node=c;}
        node.SetAttribute(attribute,value);return d;
    }
    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static void Abi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer grid=Test("ABI/grid",Root(Grid,"WallTexture='T\\9' WallOpacity='0.25'",
            "<SecondarySettings ValidTexture='T\\5' InvalidTexture='T\\1' Opacity='0.75'><InvalidColor R='1' G='2' B='3'/><LegalColor R='4' G='5' B='6' A='7'/></SecondarySettings><WallDecalColor R='17' G='34' B='51' A='68'/><PrimarySettings ValidTexture='T\\7' InvalidTexture='T\\3' Opacity='0.5'><LegalColor/><InvalidColor R='255' G='256' B='-1' A='0'/></PrimarySettings>"),p);
        Check(grid.InstanceData.Length==56&&Word(grid.InstanceData,8,big)==0x3e800000u&&Word(grid.InstanceData,12,big)==0x44112233u,"grid root56/wall float/packedARGB");
        int[] refAt={4,16,20,36,40};uint[] refValues={10u,8u,4u,6u,2u};for(int i=0;i<refAt.Length;++i)Check(Word(grid.InstanceData,refAt[i],big)==refValues[i],"grid inlineTexture fieldoffset");
        Check(Word(grid.InstanceData,24,big)==0x3f000000u&&Word(grid.InstanceData,44,big)==0x3f400000u,"grid twoinline20byte settings opacity");
        Check(Word(grid.InstanceData,28,big)==0xff000000u&&Word(grid.InstanceData,32,big)==0x00ff00ffu&&Word(grid.InstanceData,48,big)==0x07040506u&&Word(grid.InstanceData,52,big)==0xff010203u,"packed color presentempty/defaultalpha/truncatedchannels");
        Offsets(grid.ImportsData,new uint[]{40,20,36,16,4},big,"grid reverse-value importorder");Offsets(grid.RelocationData,new uint[0],big,"grid inline no pointers");
        AssetBuffer defaults=Test("ABI/grid-settings-presence",Root(Grid,"","<PrimarySettings/><SecondarySettings/>"),p);
        Check(Word(defaults.InstanceData,8,big)==0x3f800000u&&Word(defaults.InstanceData,24,big)==0x3f800000u&&Word(defaults.InstanceData,44,big)==0x3f800000u,"opacity defaults onlyapplied topresent settings");
        AssetBuffer absent=Test("ABI/grid-settings-absent",Root(Grid,"",""),p);
        Check(Word(absent.InstanceData,24,big)==0&&Word(absent.InstanceData,44,big)==0,"absentinline settings remainzero");
        AssetBuffer cloud=Test("ABI/cloud",Root(Cloud,"BuildTexture='T\\9' DefenseTexture='T\\1' SpecialPowerRestrictionTexture='T\\3'",""),p);
        Check(cloud.InstanceData.Length==16&&Word(cloud.InstanceData,4,big)==10&&Word(cloud.InstanceData,8,big)==2&&Word(cloud.InstanceData,12,big)==4,"cloud16 three refs");
        Offsets(cloud.ImportsData,new uint[]{8,12,4},big,"cloud importsbyvalue");
        AssetBuffer world=Test("ABI/world",Root(World,"Texture='T\\7' Opacity='0.5'","<Color R='17' G='34' B='51' A='68'/>"),p);
        Check(world.InstanceData.Length==16&&Word(world.InstanceData,4,big)==8&&Word(world.InstanceData,8,big)==0x3f000000u&&Word(world.InstanceData,12,big)==0x44112233u,"world16 ref/float/ARGB");
        Offsets(world.ImportsData,new uint[]{4},big,"world imports");
        AssetBuffer marker=Test("ABI/marker",Root(Marker,"","<DestinationTypeSettings DestinationType='Aggressive'><RenderInfo Model='M\\9' ZBias='2'><Color R='0.25' G='0.5' B='0.75'/></RenderInfo></DestinationTypeSettings><DefaultRenderInfo Model='M\\7' ZBias='-1'><Color R='1' G='2' B='3'/></DefaultRenderInfo><DestinationTypeSettings DestinationType='UNKNOWN'><RenderInfo Model='M\\3'/></DestinationTypeSettings>"),p);
        Check(marker.InstanceData.Length==80&&Word(marker.InstanceData,24,big)==2&&Word(marker.InstanceData,28,big)==32,"marker root32/list24 stride/allocationorder");
        Check(Word(marker.InstanceData,8,big)==0xbf800000u&&Word(marker.InstanceData,12,big)==0x3f800000u&&Word(marker.InstanceData,16,big)==0x40000000u&&Word(marker.InstanceData,20,big)==0x40400000u,"marker RenderInfo20 floatRGB distinctfrompackedColor");
        Check(Word(marker.InstanceData,32,big)==1&&Word(marker.InstanceData,56,big)==1&&Word(marker.InstanceData,40,big)==0x40000000u,"destination known/unknown andRenderInfooffset");
        Check(Word(marker.InstanceData,44,big)==0x3e800000u&&Word(marker.InstanceData,48,big)==0x3f000000u&&Word(marker.InstanceData,52,big)==0x3f400000u,"destination RGB offsets");
        Offsets(marker.ImportsData,new uint[]{4,60,36},big,"marker rootblock then listblock/valueimports");Offsets(marker.RelocationData,new uint[]{28},big,"marker list relocation");
        AssetBuffer missing=Test("ABI/missingDestination",Root(Marker,"","<DestinationTypeSettings/>"),p);
        Check(Word(missing.InstanceData,32,big)==0,"missingDestination staysNormal0");
        AssetBuffer emptyColor=Test("ABI/world-emptyColor",Root(World,"","<Color/>"),p);
        Check(Word(emptyColor.InstanceData,12,big)==0xff000000u,"presentempty packed color alpha255");
    }
    static readonly string[][] ReferenceFields={
        new string[]{Grid,"","WallTexture"},new string[]{Grid,"PrimarySettings","ValidTexture"},new string[]{Grid,"PrimarySettings","InvalidTexture"},new string[]{Grid,"SecondarySettings","ValidTexture"},new string[]{Grid,"SecondarySettings","InvalidTexture"},
        new string[]{Cloud,"","BuildTexture"},new string[]{Cloud,"","DefenseTexture"},new string[]{Cloud,"","SpecialPowerRestrictionTexture"},
        new string[]{World,"","Texture"},new string[]{Marker,"DefaultRenderInfo","Model"},new string[]{Marker,"DestinationTypeSettings/RenderInfo","Model"}
    };
    static readonly string[][] FloatFields={
        new string[]{Grid,"","WallOpacity"},new string[]{Grid,"PrimarySettings","Opacity"},new string[]{Grid,"SecondarySettings","Opacity"},new string[]{World,"","Opacity"},
        new string[]{Marker,"DefaultRenderInfo","ZBias"},new string[]{Marker,"DestinationTypeSettings/RenderInfo","ZBias"},
        new string[]{Marker,"DefaultRenderInfo/Color","R"},new string[]{Marker,"DefaultRenderInfo/Color","G"},new string[]{Marker,"DefaultRenderInfo/Color","B"},
        new string[]{Marker,"DestinationTypeSettings/RenderInfo/Color","R"},new string[]{Marker,"DestinationTypeSettings/RenderInfo/Color","G"},new string[]{Marker,"DestinationTypeSettings/RenderInfo/Color","B"}
    };
    static readonly string[][] PackedPaths={new string[]{Grid,"WallDecalColor"},new string[]{Grid,"PrimarySettings/LegalColor"},new string[]{Grid,"PrimarySettings/InvalidColor"},new string[]{Grid,"SecondarySettings/LegalColor"},new string[]{Grid,"SecondarySettings/InvalidColor"},new string[]{World,"Color"}};
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i){
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown rootattr",Root(Types[i],"Unknown='1'",""));Reject("qualified rootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknown child",Root(Types[i],"","<Unknown/>"));Reject("foreign child",Root(Types[i],"","<Color xmlns='urn:q'/>"));Reject("root text",Root(Types[i],"","payload"));
        }
        foreach(string[] field in ReferenceFields)foreach(string value in References)TestDoc("reference/"+field[0]+"/"+field[1]+"/"+field[2]+"/"+value.Length,Set(field[0],field[1],field[2],value),p);
        foreach(string[] field in FloatFields)foreach(string value in Numbers)TestDoc("float/"+field[0]+"/"+field[1]+"/"+field[2]+"/"+value,Set(field[0],field[1],field[2],value),p);
        List<string> channels=new List<string>(Numbers);channels.AddRange(new string[]{"255","256","257","65535","65536","-255","-256","-257","0x12345678","1\0tail"});
        foreach(string[] field in PackedPaths)foreach(string channel in Channels)foreach(string value in channels)TestDoc("packed/"+field[0]+"/"+field[1]+"/"+channel+"/"+value.Length,Set(field[0],field[1],channel,value),p);
        foreach(string value in Destinations)TestDoc("destination/"+value.Length,Set(Marker,"DestinationTypeSettings","DestinationType",value),p);
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            StringBuilder items=new StringBuilder();for(int i=0;i<count;++i)items.Append("<DestinationTypeSettings DestinationType='Normal'><RenderInfo Model='M\\"+(count-i)+"' ZBias='2.5'><Color R='0.25' G='0.5' B='0.75'/></RenderInfo></DestinationTypeSettings>");
            AssetBuffer b=Test("destination-growth/"+count,Root(Marker,"","<DefaultRenderInfo Model='M\\99'/>"+items),p);bool big=p!=TargetPlatform.Win32;
            Check(b.InstanceData.Length==32+24*count&&Word(b.InstanceData,24,big)==count,"destination exact listgrowth");
            if(count!=0){Check(Word(b.InstanceData,28,big)==32,"destination target root32");uint[] imports=new uint[count+1];imports[0]=4;for(int i=0;i<count;++i)imports[i+1]=(uint)(36+24*(count-1-i));Offsets(b.ImportsData,imports,big,"all descendingreferences sortedownerblock/value");}
        }
        List<string[]> nodes=new List<string[]>();nodes.AddRange(PackedPaths);nodes.AddRange(new string[][]{new string[]{Grid,"PrimarySettings"},new string[]{Grid,"SecondarySettings"},new string[]{Marker,"DefaultRenderInfo"},new string[]{Marker,"DefaultRenderInfo/Color"},new string[]{Marker,"DestinationTypeSettings"},new string[]{Marker,"DestinationTypeSettings/RenderInfo"},new string[]{Marker,"DestinationTypeSettings/RenderInfo/Color"}});
        foreach(string[] row in nodes){
            TestDoc("nested metadata/"+row[1],Set(row[0],row[1],"TypeId","1"),p);
            Reject("unknown nestedattr",Set(row[0],row[1],"Unknown","1").OuterXml);
            XmlDocument d=Set(row[0],row[1],"TypeId","1");XmlNode at=d.DocumentElement;foreach(string name in row[1].Split('/'))at=at.FirstChild;XmlAttribute q=d.CreateAttribute("q","Unknown","urn:q");q.Value="1";at.Attributes.Append(q);Reject("qualified nestedattr",d.OuterXml);
        }
        foreach(string name in new string[]{"WallDecalColor","PrimarySettings","SecondarySettings"})Reject("duplicate grid singleton",Root(Grid,"","<"+name+"/><"+name+"/>"));
        Reject("duplicate worldcolor",Root(World,"","<Color/><Color/>"));Reject("duplicate render",Root(Marker,"","<DefaultRenderInfo/><DefaultRenderInfo/>"));
        Reject("duplicate setting-render",Root(Marker,"","<DestinationTypeSettings><RenderInfo/><RenderInfo/></DestinationTypeSettings>"));
        foreach(string name in new string[]{"LegalColor","InvalidColor"})Reject("duplicate settingscolor",Root(Grid,"","<PrimarySettings><"+name+"/><"+name+"/></PrimarySettings>"));
        Reject("duplicate RGBcolor",Root(Marker,"","<DefaultRenderInfo><Color/><Color/></DefaultRenderInfo>"));
        Reject("RGBA notRGB",Root(Marker,"","<DefaultRenderInfo><Color A='255'/></DefaultRenderInfo>"));
        Reject("color child",Root(World,"","<Color><Unknown/></Color>"));Reject("settings text",Root(Grid,"","<PrimarySettings>payload</PrimarySettings>"));Reject("render text",Root(Marker,"","<DefaultRenderInfo>payload</DefaultRenderInfo>"));Reject("unknown root",Root("Unknown","",""));
        bool nullInput=false;try{XmluidecalsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument wrong=Parse(Root(Grid,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Cloud;
        bool mismatch=false;try{XmluidecalsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch rejected");++rejections;
        Abi(p);
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
            Console.WriteLine("PASS UI decals fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

