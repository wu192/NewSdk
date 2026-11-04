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
class XmlgamemapOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"GameMap"};
    static readonly uint[] TypeIds={3575309793u};
    static readonly int[] Sizes={80};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
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
            original.Initialize(null,p);InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlgamemapCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlgamemapCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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

    const string RootName="GameMap";
    static string[] Objects={"Waypoint","Structure","Unit","Prop","Audio","Unknown"};
    static string[] Pairs={"BoolProperty","IntProperty","RealProperty","StringProperty","UnicodeStringProperty","AssetIdProperty","AssetIdListProperty"};
    static string[] Analysis={"GarrisonableStructures","TechStructures","CellsExamined","WaterCells","WaterAreas"};
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(RootName,"",""),p);Check(d.InstanceData.Length==80,"root80");for(int i=0;i<80;i+=4)Check(Word(d.InstanceData,i,big)==0,"default root zero");
        AssetBuffer b=Test("ABI/USP",Root(RootName,"","<Unit id='Mixed' ThingTemplate='Object\\7' Flags='ROAD_POINT1 DONT_RENDER' Angle='0.5' Faction='a' Team='bc' Health='-2' EventList='def'><Position x='1' y='-2' z='0.5'/></Unit>"),p);
        Check(b.InstanceData.Length==148&&Word(b.InstanceData,20,big)==1&&Word(b.InstanceData,24,big)==80,"USP56 plus strings");
        Check(Word(b.InstanceData,84,big)==8&&Word(b.InstanceData,88,big)==130&&Word(b.InstanceData,92,big)==0x3f000000u,"USP base reference/flags/plain float angle");
        Check(Word(b.InstanceData,96,big)==0x3f800000u&&Word(b.InstanceData,100,big)==0xc0000000u&&Word(b.InstanceData,104,big)==0x3f000000u,"VectorHack lowercase float3");
        int[] at={108,116,128};for(int i=0;i<3;++i)Check(Word(b.InstanceData,at[i],big)==i+1&&Word(b.InstanceData,at[i]+4,big)==136+4*i,"derived ANSI string allocation order");Check(Word(b.InstanceData,124,big)==0xc0000000u,"health plain float");
        Offsets(b.RelocationData,new uint[]{24,112,120,132},big,"USP relocation");Offsets(b.ImportsData,new uint[]{84},big,"USP import");
        AssetBuffer all=Test("ABI/root order",Root(RootName,"","<MapAnalysis/><PostEffect Effect='P\\1'/><WorldDict/><EnvironmentData Cloud='C\\2' Macro='M\\0' Environment='E\\-1'/><Road id='R\\4'/><Unknown/><Audio/><Prop/><Unit/><Structure/><Waypoint/>"),p);
        int[] counts={4,12,20,28,36,44,52,68};uint[] targets={80,108,164,220,276,304,332,404};for(int i=0;i<8;++i)Check(Word(all.InstanceData,counts[i],big)==1&&Word(all.InstanceData,counts[i]+4,big)==targets[i],"root list order");
        Check(Word(all.InstanceData,60,big)==336&&Word(all.InstanceData,64,big)==348&&Word(all.InstanceData,76,big)==408&&all.InstanceData.Length==436,"optional dependencies root order");Offsets(all.RelocationData,new uint[]{8,16,24,32,40,48,56,60,64,72,76},big,"root pointer order");Offsets(all.ImportsData,new uint[]{332,340,336,344,404},big,"import sorted owner then index");
        AssetBuffer dict=Test("ABI/seven pairs",Root(RootName,"","<WorldDict><BoolProperty Value='true'/><IntProperty Value='-7'/><RealProperty Value='0.5'/><StringProperty Value='abc'/><UnicodeStringProperty Value='def'/><AssetIdProperty Value='X\\2'/><AssetIdListProperty Value='One TWO'/></WorldDict>"),p);
        uint[] pairTargets={136,148,160,172,192,212,224};for(int i=0;i<7;++i)Check(Word(dict.InstanceData,80+8*i,big)==1&&Word(dict.InstanceData,84+8*i,big)==pairTargets[i],"dictionary grouped order");Check(dict.InstanceData[144]==1&&dict.InstanceData[145]==0&&dict.InstanceData[146]==0&&dict.InstanceData[147]==0,"bool byte/padding");Check(Word(dict.InstanceData,156,big)==unchecked((uint)-7)&&Word(dict.InstanceData,168,big)==0x3f000000u,"int/real pair");Check(Word(dict.InstanceData,232,big)==2&&Word(dict.InstanceData,236,big)==240&&Word(dict.InstanceData,240,big)==3&&Word(dict.InstanceData,244,big)==256&&Word(dict.InstanceData,248,big)==3&&Word(dict.InstanceData,252,big)==260,"asset-id list holds ANSI strings, not hashes/references");Check(dict.ImportsData.Length==8&&Word(dict.ImportsData,0,big)==220,"only AssetIdProperty imports");
        AssetBuffer flags=Test("ABI/ALL",Root(RootName,"","<Waypoint Flags='ALL -DONT_RENDER'/>"),p);Check(Word(flags.InstanceData,88,big)==UInt32.MaxValue,"ALL fills padding and does not advance token");
        AssetBuffer ignored=Test("ABI/pipelineOnly",Root(RootName,"","<MapMetaData id='Ignored' Any='ignored'><Anything/></MapMetaData>"),p);Equal("pipeline metadata ignored",d.InstanceData,ignored.InstanceData);Equal("pipeline relocs ignored",d.RelocationData,ignored.RelocationData);Equal("pipeline imports ignored",d.ImportsData,ignored.ImportsData);comparisons-=3;
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        List<string> strings=new List<string>(References);strings.AddRange(new string[]{"MixedCase","MIXEDCASE","abc\0tail","\0abc","é","东京","😀",new string('x',4097)});
        foreach(string tag in Objects){bool usp=tag=="Structure"||tag=="Unit"||tag=="Prop";
            foreach(string attr in usp?new string[]{"id","ThingTemplate","Faction","Team","EventList"}:new string[]{"id","ThingTemplate"})foreach(string v in strings)TestDoc(tag+"/"+attr,Set(RootName,tag,attr,v),p);
            foreach(string attr in usp?new string[]{"Angle","Health"}:new string[]{"Angle"})foreach(string v in Numbers)TestDoc(tag+"/"+attr,Set(RootName,tag,attr,v),p);
            foreach(string axis in new string[]{"x","y","z"})foreach(string v in Numbers)TestDoc(tag+"/position",Set(RootName,tag+"/Position",axis,v),p);
            string[] flags={"DRAWS_IN_MIRROR","ROAD_POINT1","ROAD_POINT2","OBSOLETE_BRIDGE_POINT1","OBSOLETE_BRIDGE_POINT2","ROAD_CORNER_TIGHT","ROAD_JOIN","DONT_RENDER","INVALID","unknown","all","ALL -DONT_RENDER","-ROAD_POINT1","+ROAD_POINT1","DONT_RENDER -DONT_RENDER -DONT_RENDER","","ALL\0 -ROAD_POINT1"};
            foreach(string v in flags)TestDoc(tag+"/flags",Set(RootName,tag,"Flags",v),p);
            for(int mask=0;mask<256;++mask){StringBuilder f=new StringBuilder();for(int i=0;i<8;++i)if((mask&(1<<i))!=0)f.Append(flags[i]+" ");AssetBuffer b=TestDoc("all flag combinations",Set(RootName,tag,"Flags",f.ToString()),p);Check(Word(b.InstanceData,88,big)==mask,"independent 8 flag bits");}
        }
        foreach(string tag in Pairs)foreach(string v in strings)TestDoc("pairkey",Set(RootName,"WorldDict/"+tag,"Key",v),p);
        foreach(string tag in new string[]{"StringProperty","UnicodeStringProperty","AssetIdProperty","AssetIdListProperty"})foreach(string v in strings)TestDoc("pairvalue",Set(RootName,"WorldDict/"+tag,"Value",v),p);
        foreach(string tag in new string[]{"IntProperty","RealProperty"})foreach(string v in Numbers)TestDoc("pairnumber",Set(RootName,"WorldDict/"+tag,"Value",v),p);
        foreach(string v in new string[]{"true","false","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("pairbool",Set(RootName,"WorldDict/BoolProperty","Value",v),p);
        foreach(string attr in Analysis)foreach(string v in Numbers)TestDoc("analysis count",Set(RootName,"MapAnalysis",attr,v),p);
        foreach(string attr in new string[]{"Cloud","Macro","Environment"})foreach(string v in strings)TestDoc("environment refs",Set(RootName,"EnvironmentData",attr,v),p);
        foreach(string v in strings){TestDoc("road reference",Set(RootName,"Road","id",v),p);TestDoc("posteffect reference",Set(RootName,"PostEffect","Effect",v),p);}
        foreach(string v in new string[]{"INTERIOR","EXTERIOR","EXTERIOR_PARTIAL","interior","UNKNOWN","","  INTERIOR","INTERIOR ","INTERIOR\0x","0"}){XmlDocument d=Set(RootName,"MapAnalysis/WaterAreaType","TypeId","1");d.DocumentElement.FirstChild.FirstChild.InnerText=v;TestDoc("water enum",d,p);}
        foreach(string v in new string[]{""," ","a"," A  b\tc\nd\re\vf\fg ","a\0 b","Name\\2 Other\\7","é 东京 😀",new string('x',4097)}){AssetBuffer b=TestDoc("asset list tokenizer",Set(RootName,"WorldDict/AssetIdListProperty","Value",v),p);Check(b.ImportsData.Length==0,"assetid strings no imports");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            for(int k=0;k<8;++k){string tag=k<6?Objects[k]:k==6?"Road":"PostEffect";int size=k<6?(k>=1&&k<=3?56:28):4;StringBuilder rows=new StringBuilder();for(int i=0;i<count;++i)rows.Append("<"+tag+"/>");AssetBuffer b=Test("object growth",Root(RootName,"",rows.ToString()),p);Check(b.InstanceData.Length==80+size*count,"object exact growth");}
            foreach(string tag in Pairs){StringBuilder rows=new StringBuilder();for(int i=0;i<count;++i)rows.Append("<"+tag+"/>");AssetBuffer b=Test("pair growth",Root(RootName,"","<WorldDict>"+rows+"</WorldDict>"),p);int size=tag=="StringProperty"||tag=="UnicodeStringProperty"||tag=="AssetIdListProperty"?16:12;Check(b.InstanceData.Length==136+size*count,"pair exact growth");}
            StringBuilder water=new StringBuilder(),tokens=new StringBuilder();for(int i=0;i<count;++i){water.Append("<WaterAreaType>EXTERIOR_PARTIAL</WaterAreaType>");tokens.Append("a ");}AssetBuffer w=Test("water growth",Root(RootName,"","<MapAnalysis>"+water+"</MapAnalysis>"),p);Check(w.InstanceData.Length==108+4*count,"water exactgrowth");AssetBuffer a=TestDoc("assetid growth",Set(RootName,"WorldDict/AssetIdListProperty","Value",tokens.ToString()),p);Check(a.InstanceData.Length==152+12*count,"assetid list+strings growth");
        }
        Test("mixed nested ordering",Root(RootName,"","<Unit Faction='a' ThingTemplate='A\\9'/><Waypoint ThingTemplate='W\\8'/><Unit EventList='b' ThingTemplate='B\\0'/><WorldDict><AssetIdListProperty Key='key' Value='one two'/><StringProperty Key='x' Value='y'/><AssetIdProperty Key='ref' Value='r\\-1'/><StringProperty Value='z'/></WorldDict><PostEffect Effect='E\\1'/><EnvironmentData Macro='M\\27'/><MapAnalysis WaterAreas='-2'><WaterAreaType>INTERIOR</WaterAreaType><WaterAreaType>EXTERIOR</WaterAreaType></MapAnalysis>"),p);
        foreach(string path in new string[]{"Waypoint","Structure","Unit","Prop","Audio","Unknown","Road","PostEffect","EnvironmentData","WorldDict","MapAnalysis","Waypoint/Position","MapAnalysis/WaterAreaType","WorldDict/BoolProperty","WorldDict/IntProperty","WorldDict/RealProperty","WorldDict/StringProperty","WorldDict/UnicodeStringProperty","WorldDict/AssetIdProperty","WorldDict/AssetIdListProperty"}){
            TestDoc("metadata allowed",Set(RootName,path,"TypeId","1"),p);Reject("unknown attr",Set(RootName,path,"Bogus","1").OuterXml);XmlDocument d=Set(RootName,path,"TypeId","1");XmlNode node=d.DocumentElement;foreach(string tag in path.Split('/'))node=node.FirstChild;node.AppendChild(d.CreateElement("Bogus",NS));Reject("unknown child",d.OuterXml);node.RemoveAll();if(path!="MapAnalysis/WaterAreaType"){node.InnerText="payload";Reject("unexpected text",d.OuterXml);}}
        foreach(string tag in new string[]{"EnvironmentData","WorldDict","MapAnalysis","MapMetaData"})Reject("duplicate singleton",Root(RootName,"","<"+tag+"/><"+tag+"/>"));Reject("duplicate position",Root(RootName,"","<Unit><Position/><Position/></Unit>"));Reject("uppercasevector",Set(RootName,"Unit/Position","X","1").OuterXml);
        Reject("root unknownattr",Root(RootName,"Unknown='1'",""));Reject("root unknownchild",Root(RootName,"","<Bogus/>"));Reject("root text",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));Reject("unknown root",Root("Bogus","",""));
        bool rejected=false;try{XmlgamemapCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;
        XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlgamemapCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;
        var unboundSchemaResult=XmlgamemapCompiler.CompileXml(wrong.DocumentElement,new XmlNamespaceManager(wrong.NameTable),false);
        var boundSchemaResult=XmlgamemapCompiler.CompileXml(wrong.DocumentElement,Namespaces(wrong),false);
        Check(Convert.ToBase64String(unboundSchemaResult.InstanceData)==Convert.ToBase64String(boundSchemaResult.InstanceData) &&
            Convert.ToBase64String(unboundSchemaResult.RelocationData)==Convert.ToBase64String(boundSchemaResult.RelocationData) &&
            Convert.ToBase64String(unboundSchemaResult.ImportsData)==Convert.ToBase64String(boundSchemaResult.ImportsData),"plugin does not repeat Core namespace validation");++layouts;
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
            Console.WriteLine("PASS game map fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

