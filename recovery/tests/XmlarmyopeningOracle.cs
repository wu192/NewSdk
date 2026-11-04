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
class XmlarmyopeningOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Army="ArmyDefinition",Opening="SkirmishOpeningMove";
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] WeakFields={"MCVTemplate","GathererTemplate","ExpanderTemplate","EnergyProducer","PrimaryResourceNodeCollector","PrimaryResourceThingToBuild","SecondaryResourceNodeCollector","VehicleRepairFacility","NavalRepairFacility","AircraftRepairFacility","WallPiece"};
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
            Type type=typeof(XmlarmyopeningCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlarmyopeningCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
        AssetBuffer army=Test("ABI/army",Root(Army,"Side='ABC' MCVTemplate='ABC' GathererTemplate='abc' WallPiece='ABC'","<TechStructure>ABC</TechStructure><TechStructure>abc</TechStructure>"),p);
        Check(army.InstanceData.Length==76&&Word(army.InstanceData,4,big)==3&&Word(army.InstanceData,8,big)==64&&Word(army.InstanceData,56,big)==2&&Word(army.InstanceData,60,big)==68,"Army root64/Side string before weak list4stride");
        uint hash=Word(army.InstanceData,12,big);Check(hash!=0&&hash==Word(army.InstanceData,16,big)&&hash==Word(army.InstanceData,52,big)&&hash==Word(army.InstanceData,68,big)&&hash==Word(army.InstanceData,72,big),"all inline/list weak ids case-fold and exact offsets");
        Check(army.InstanceData[64]==65&&army.InstanceData[65]==66&&army.InstanceData[66]==67&&army.InstanceData[67]==0,"Side bytes/NUL");Offsets(army.RelocationData,new uint[]{8,60},big,"army ownerblock targetorder");Offsets(army.ImportsData,new uint[0],big,"army weak no imports");
        AssetBuffer opening=Test("ABI/opening",Root(Opening,"Side='AB'","<Order Time='1500ms' Build='XYZ' Account='UNKNOWN' Deposit='4294967295'/><Order/>"),p);
        Check(opening.InstanceData.Length==72&&Word(opening.InstanceData,4,big)==2&&Word(opening.InstanceData,8,big)==20&&Word(opening.InstanceData,12,big)==2&&Word(opening.InstanceData,16,big)==24,"opening root20/Side4/orders40/build4+4");
        Check(Word(opening.InstanceData,24,big)==0x3fc00001u&&Word(opening.InstanceData,28,big)==3&&Word(opening.InstanceData,32,big)==64&&Word(opening.InstanceData,36,big)==3&&Word(opening.InstanceData,40,big)==UInt32.MaxValue,"Order20 Time/defaultunknownDEFENSE3/unsignedDeposit");
        Check(Word(opening.InstanceData,44,big)==0&&Word(opening.InstanceData,48,big)==0&&Word(opening.InstanceData,52,big)==68&&Word(opening.InstanceData,56,big)==5&&Word(opening.InstanceData,60,big)==0,"Order missing defaults Buildempty allocates/SLUSH_FUND5");
        Check(opening.InstanceData[64]==88&&opening.InstanceData[65]==89&&opening.InstanceData[66]==90&&opening.InstanceData[67]==0&&opening.InstanceData[68]==0,"ordered Build strings and terminators");Offsets(opening.RelocationData,new uint[]{8,16,32,52},big,"opening root then orderblock pointers");Offsets(opening.ImportsData,new uint[0],big,"Build is string no imports");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        string[] roots={Army,Opening};int[] sizes={64,20};
        for(int i=0;i<roots.Length;++i){AssetBuffer a=Test("defaults/"+roots[i],Root(roots[i],"",""),p);Check(a.InstanceData.Length==sizes[i],"independent rootsize");foreach(byte b in a.InstanceData)Check(b==0,"empty root allzero");Offsets(a.RelocationData,new uint[0],big,"empty no reloc");Offsets(a.ImportsData,new uint[0],big,"empty no imports");
            Test("metadata/"+roots[i],Root(roots[i],"TypeId='1' inheritFrom='Ignored'",""),p);Reject("unknownattr",Root(roots[i],"Unknown='1'",""));Reject("qualifiedattr",Root(roots[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknownchild",Root(roots[i],"","<Unknown/>"));Reject("roottext",Root(roots[i],"","payload"));
        }
        List<string> strings=new List<string>(References);strings.AddRange(new string[]{"ABC","abc","MiXeD","A\0tail","\0tail","a\tb","a\nb"});
        foreach(string value in strings){foreach(string root in roots)TestDoc("Side/"+root+"/"+value.Length,Set(root,"","Side",value),p);foreach(string field in WeakFields)TestDoc("weak/"+field+"/"+value.Length,Set(Army,"",field,value),p);Test("TechStructure/"+value.Length,Root(Army,"","<TechStructure>"+Escape(value.Replace("\0",""))+"</TechStructure>"),p);
            XmlDocument tech=Parse(Root(Army,"","<TechStructure/>"));tech.DocumentElement.FirstChild.InnerText=value;TestDoc("TechStructureDOM/"+value.Length,tech,p);TestDoc("Build/"+value.Length,Set(Opening,"Order","Build",value),p);
        }
        foreach(string value in Numbers){TestDoc("Deposit/"+value,Set(Opening,"Order","Deposit",value),p);TestDoc("Time/"+value,Set(Opening,"Order","Time",value),p);}
        foreach(string value in new string[]{"1ms","1500ms","1s2ms","1S","1MS","2d1ms","-17ms","1\0ms","16777217ms"})TestDoc("Time-units/"+value,Set(Opening,"Order","Time",value),p);
        string[] enums={"DEFENSE","INVESTMENT","PRODUCTION","SLUSH_FUND","SPECIAL_OPERATIONS","TECHNOLOGY","","UNKNOWN","defense"," DEFENSE","DEFENSE ","0","DEFENSE\0tail","TECHNOLOGY\0tail"};uint[] values={3,0,2,5,1,4,3,3,3,3,3,3,3,4};
        for(int i=0;i<enums.Length;++i){AssetBuffer a=TestDoc("Account/"+i,Set(Opening,"Order","Account",enums[i]),p);Check(Word(a.InstanceData,32,big)==values[i],"independent full6enum/defaultfallback/C-string");}
        AssetBuffer emptyOrder=Test("emptyOrder",Root(Opening,"","<Order/>"),p);Check(emptyOrder.InstanceData.Length==44&&Word(emptyOrder.InstanceData,24,big)==0&&Word(emptyOrder.InstanceData,28,big)==40&&Word(emptyOrder.InstanceData,32,big)==5,"present empty order defaults allocate BuildNUL");
        foreach(int count in new int[]{0,1,2,3,4,5,15,16,17,27,28,29,64,257,1024,65535})foreach(string root in roots){AssetBuffer a=TestDoc("sidegrowth/"+root+"/"+count,Set(root,"","Side",new string('X',count)),p);int size=root==Army?64:20;Check(a.InstanceData.Length==size+((count+4)/4)*4&&Word(a.InstanceData,4,big)==count&&Word(a.InstanceData,8,big)==size,"side exact string alignment");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            StringBuilder tech=new StringBuilder(),orders=new StringBuilder();for(int i=0;i<count;++i){tech.Append("<TechStructure>ABC</TechStructure>");orders.Append("<Order/>");}
            AssetBuffer a=Test("techgrowth/"+count,Root(Army,"",tech.ToString()),p);Check(a.InstanceData.Length==64+4*count&&Word(a.InstanceData,56,big)==count,"weak list exactgrowth");
            AssetBuffer b=Test("ordergrowth/"+count,Root(Opening,"",orders.ToString()),p);Check(b.InstanceData.Length==20+24*count&&Word(b.InstanceData,12,big)==count,"Order20 plus defaultBuild4 each growth");
            if(count>0){uint[] offsets=new uint[count+1];offsets[0]=16;for(int i=0;i<count;++i){offsets[i+1]=(uint)(28+20*i);Check(Word(b.InstanceData,28+20*i,big)==20+20*count+4*i,"defaultBuild target order");}Offsets(b.RelocationData,offsets,big,"all Build string pointers");}
        }
        Test("nested metadata",Root(Opening,"","<Order TypeId='99'/>"),p);Test("Tech metadata",Root(Army,"","<TechStructure TypeId='99'>ABC</TechStructure>"),p);
        Reject("unknown orderattr",Root(Opening,"","<Order Unknown='1'/>"));Reject("unknown orderchild",Root(Opening,"","<Order><Unknown/></Order>"));Reject("ordertext",Root(Opening,"","<Order>payload</Order>"));Reject("techattr",Root(Army,"","<TechStructure Unknown='1'/>"));Reject("techchild",Root(Army,"","<TechStructure><Unknown/></TechStructure>"));Reject("foreignchild",Root(Army,"","<TechStructure xmlns='urn:q'/>"));Reject("wrongnamespace","<ArmyDefinition xmlns='urn:q'/>");Reject("unknownroot",Root("Unknown","",""));
        bool nullInput=false;try{XmlarmyopeningCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration");++rejections;
        XmlDocument wrong=Parse(Root(Army,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Opening;bool mismatch=false;try{XmlarmyopeningCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/TypeId mismatch");++rejections;
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
            Console.WriteLine("PASS Army opening fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

