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
class XmlunitoverlayOracle {
    const string NS="uri:ea.com:eala:asset",Overlay="UnitOverlayIconSettings";
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] RefFields={"AmmoPipImage","AmmoPipFrameImage","ContainPipImage","ContainPipFrameImage","GarrisonPipImage","UnitTypeIconBackgroundImage","TargetIndicatorImage","TargetIndicatorGlowImage","PowerIconImageSequence","RepairIconImageSequence","InfiltratedIconImageSequence","HealUnitIconImageSequence","RepairVehicleIconImageSequence"};
    static readonly string[] ScaleFields={"AmmoPipScale","ContainPipScale","GarrisonPipScale","UnitTypeIconScale","StatusIconScale","VeterancyIconScale","TargetIndicatorScale","HealingIconScale"};
    static readonly int[] ScaleOffsets={56,64,68,72,76,80,84,88};
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
            Type type=typeof(XmlunitoverlayCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlunitoverlayCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
        AssetBuffer a=Test("ABI/nested",Root(Overlay,"AmmoPipImage='T\\9' RepairVehicleIconImageSequence='T\\1' AmmoPipScale='50%' AmmoPipRange='-17' HealingIconDisplayTime='1500ms'",
            "<FactionVeterancy Faction='Soviet'><RankIcon Rank='1' Image='T\\7'/><RankIcon Rank='-1' Image='T\\3'/></FactionVeterancy><FactionVeterancy Faction='Japan'><RankIcon Rank='4' Image='T\\2'/></FactionVeterancy>"),p);
        Check(a.InstanceData.Length==152,"root104/faction12/rank8 total152");
        Check(Word(a.InstanceData,4,big)==10&&Word(a.InstanceData,52,big)==2,"root13strong reference offsets");
        Check(Word(a.InstanceData,56,big)==0x3f000000u,"percentage50 ABI actual="+Word(a.InstanceData,56,big).ToString("X8"));
        Check(Word(a.InstanceData,60,big)==unchecked((uint)-17),"signedRange ABI actual="+Word(a.InstanceData,60,big).ToString("X8"));
        Check(Word(a.InstanceData,92,big)==0x3fc00001u,"Time1500ms preserves binary32 unit-scale rounding actual="+Word(a.InstanceData,92,big).ToString("X8"));
        for(int i=1;i<ScaleOffsets.Length;++i)Check(Word(a.InstanceData,ScaleOffsets[i],big)==0x3f800000u,"partialattrs retain independent100percent default");
        Check(Word(a.InstanceData,96,big)==2&&Word(a.InstanceData,100,big)==104,"root faction count/pointer");
        Check(Word(a.InstanceData,104,big)==1&&Word(a.InstanceData,108,big)==2&&Word(a.InstanceData,112,big)==128,"first faction enum/count/ranks pointer");
        Check(Word(a.InstanceData,116,big)==2&&Word(a.InstanceData,120,big)==1&&Word(a.InstanceData,124,big)==144,"second faction enum/count/ranks pointer");
        Check(Word(a.InstanceData,128,big)==1&&Word(a.InstanceData,136,big)==UInt32.MaxValue&&Word(a.InstanceData,144,big)==4,"rank signed int/stride8");
        Check(Word(a.InstanceData,132,big)==8&&Word(a.InstanceData,140,big)==4&&Word(a.InstanceData,148,big)==3,"rank strong refs index+1");
        Offsets(a.RelocationData,new uint[]{100,112,124},big,"nested pointer ownerblock order");
        Offsets(a.ImportsData,new uint[]{52,4,140,132,148},big,"imports ownerblock then refindex distinctfrom globalsort");
        AssetBuffer z=Test("ABI/emptyfactions",Root(Overlay,"","<FactionVeterancy/><FactionVeterancy Faction='RANDOM'/>"),p);
        Check(z.InstanceData.Length==128&&Word(z.InstanceData,104,big)==0&&Word(z.InstanceData,116,big)==UInt32.MaxValue,"missing faction0/RANDOM-1");
        Offsets(z.RelocationData,new uint[]{100},big,"empty rank lists no pointers");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        AssetBuffer a=Test("defaults",Root(Overlay,"",""),p);Check(a.InstanceData.Length==104,"independent root104");
        foreach(int offset in ScaleOffsets)Check(Word(a.InstanceData,offset,big)==0x3f800000u,"all8percent defaults100");
        Check(Word(a.InstanceData,92,big)==0x40400000u,"time default3s");
        Check(Word(a.InstanceData,96,big)==0&&Word(a.InstanceData,100,big)==0&&a.RelocationData.Length==0&&a.ImportsData.Length==0,"empty factions/bookmarks");
        Test("metadata",Root(Overlay,"TypeId='2421413379' inheritFrom='Ignored'",""),p);
        foreach(string field in RefFields)foreach(string value in References)TestDoc("reference/"+field+"/"+value.Length,Set(Overlay,"",field,value),p);
        foreach(string value in References)TestDoc("rank-reference/"+value.Length,Set(Overlay,"FactionVeterancy/RankIcon","Image",value),p);
        foreach(string field in ScaleFields)foreach(string value in Numbers)TestDoc("percentage/"+field+"/"+value,Set(Overlay,"",field,value),p);
        List<string> ints=new List<string>(Numbers);ints.AddRange(new string[]{"1\0tail","0x80000000","-0x80000001","9999999999999999999999","+ 12","--1","  -077rest"});
        foreach(string value in ints){TestDoc("int/root/"+value,Set(Overlay,"","AmmoPipRange",value),p);TestDoc("int/rank/"+value,Set(Overlay,"FactionVeterancy/RankIcon","Rank",value),p);}
        List<string> times=new List<string>(Numbers);times.AddRange(new string[]{"1ms","3s","1500ms","1s2ms","1MS","1S","3 m s","2d1ms","0.0000001ms","1\0ms","-17ms","16777217ms"});
        foreach(string value in times)TestDoc("time/"+value,Set(Overlay,"","HealingIconDisplayTime",value),p);
        string[] factions={"Allies","Japan","Neutral","RANDOM","Soviet","","UNKNOWN","allies","soviet","SOVIET"," Random","Japan ","0","1","Soviet\0tail","RANDOM\0tail","Unknown\0Japan"};
        uint[] factionValues={0,2,3,UInt32.MaxValue,1,0,0,0,0,0,0,0,0,0,1,UInt32.MaxValue,0};
        for(int i=0;i<factions.Length;++i){AssetBuffer b=TestDoc("enum/"+i,Set(Overlay,"FactionVeterancy","Faction",factions[i]),p);Check(Word(b.InstanceData,104,big)==factionValues[i],"independent Faction lookup/fallback/C-string");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            StringBuilder ranks=new StringBuilder(),f=new StringBuilder();for(int i=0;i<count;++i){ranks.Append("<RankIcon Rank='"+i+"' Image='T\\"+(count-i)+"'/>");f.Append("<FactionVeterancy Faction='Japan'/>");}
            AssetBuffer r=Test("rankgrowth/"+count,Root(Overlay,"","<FactionVeterancy Faction='Soviet'>"+ranks+"</FactionVeterancy>"),p);
            Check(r.InstanceData.Length==116+8*count&&Word(r.InstanceData,108,big)==count,"rank count/allocation boundaries");
            if(count>0){Check(Word(r.InstanceData,112,big)==116,"ranks followroot104/faction12");uint[] order=new uint[count];for(int i=0;i<count;++i)order[i]=(uint)(120+8*(count-1-i));Offsets(r.ImportsData,order,big,"rank refs reverseorder");}
            AssetBuffer b=Test("factiongrowth/"+count,Root(Overlay,"",f.ToString()),p);Check(b.InstanceData.Length==104+12*count&&Word(b.InstanceData,96,big)==count,"faction count/allocation boundaries");
        }
        Test("comments/CDATA",Root(Overlay,"","<!--comment--><FactionVeterancy> <![CDATA[ ]]><RankIcon/></FactionVeterancy>"),p);
        foreach(string path in new string[]{"","FactionVeterancy","FactionVeterancy/RankIcon"}){
            TestDoc("nested metadata/"+path,Set(Overlay,path,"TypeId","99"),p);
            Reject("unknownattr/"+path,Set(Overlay,path,"Unknown","1").OuterXml);
            XmlDocument d=Set(Overlay,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string part in path.Split('/'))if(part.Length>0)n=n.FirstChild;
            XmlAttribute q=d.CreateAttribute("q","TypeId","urn:q");q.Value="1";n.Attributes.Append(q);Reject("qualifiedattr/"+path,d.OuterXml);
            n.Attributes.Remove(q);n.AppendChild(d.CreateElement("Unknown",NS));Reject("unknownchild/"+path,d.OuterXml);
        }
        Reject("roottext",Root(Overlay,"","payload"));Reject("factiontext",Root(Overlay,"","<FactionVeterancy>payload</FactionVeterancy>"));Reject("ranktext",Root(Overlay,"","<FactionVeterancy><RankIcon>payload</RankIcon></FactionVeterancy>"));
        Reject("foreign child",Root(Overlay,"","<FactionVeterancy xmlns='urn:q'/>"));Reject("unknownroot",Root("Unknown","",""));
        bool nullInput=false;try{XmlunitoverlayCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument wrong=Parse(Root(Overlay,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="UIDecalGrid";
        bool mismatch=false;try{XmlunitoverlayCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch");++rejections;
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
            Console.WriteLine("PASS Unit overlay fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

