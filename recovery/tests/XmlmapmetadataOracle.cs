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
class XmlmapmetadataOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"MapMetaData"};
    static readonly uint[] TypeIds={1603703110u};
    static readonly int[] Sizes={12};
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
            Type type=typeof(XmlmapmetadataCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlmapmetadataCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    const string RootName="MapMetaData",Meta="MapMetaData",Start="MapMetaData/StartPosition";
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(RootName,"",""),p);Check(d.InstanceData.Length==12&&Word(d.InstanceData,4,big)==0&&Word(d.InstanceData,8,big)==0,"root12");AssetBuffer empty=Test("ABI/emptyMeta",Root(RootName,"","<MapMetaData/>"),p);Check(empty.InstanceData.Length==72&&Word(empty.InstanceData,4,big)==1&&Word(empty.InstanceData,8,big)==12,"metadata60");for(int at=12;at<72;at+=4)Check(Word(empty.InstanceData,at,big)==0,"metadata absent fields zero");
        AssetBuffer b=Test("ABI/full",Root(RootName,"","<MapMetaData id='MixedCase' Description='a' DisplayName='b' FileName='c' IsMultiplayer='true' IsOfficial='true' NumPlayers='-7' CRC='0x12345678' Width='128' Height='256' BorderSize='-3'><StartPosition Name='d'><Position x='0.5' y='-2' z='1'/></StartPosition></MapMetaData>"),p);Check(b.InstanceData.Length==108&&Word(b.InstanceData,12,big)!=0,"full60+threeString4+start20+Name4");int[] strings={16,24,40};uint[] targets={72,76,80};for(int i=0;i<3;++i)Check(Word(b.InstanceData,strings[i],big)==1&&Word(b.InstanceData,strings[i]+4,big)==targets[i]&&b.InstanceData[targets[i]]==97+i,"three string offsets/order");Check(Word(b.InstanceData,32,big)==unchecked((uint)-7)&&Word(b.InstanceData,36,big)==0x12345678u&&Word(b.InstanceData,48,big)==128&&Word(b.InstanceData,52,big)==256&&Word(b.InstanceData,56,big)==unchecked((uint)-3),"signed/CRC offsets");Check(Word(b.InstanceData,60,big)==1&&Word(b.InstanceData,64,big)==84&&b.InstanceData[68]==1&&b.InstanceData[69]==1&&b.InstanceData[70]==0&&b.InstanceData[71]==0,"startlist/boolbytes padding");Check(Word(b.InstanceData,84,big)==1&&Word(b.InstanceData,88,big)==104&&b.InstanceData[104]==100&&Word(b.InstanceData,92,big)==0x3f000000u&&Word(b.InstanceData,96,big)==0xc0000000u&&Word(b.InstanceData,100,big)==0x3f800000u,"StartPosition20 Name+inlinefloat3");Offsets(b.RelocationData,new uint[]{8,20,28,44,64,88},big,"nestedstring/start pointer allocationorder");Offsets(b.ImportsData,new uint[0],big,"metadata names weakhash orstrings noimports");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        List<string> strings=new List<string>(References);strings.AddRange(new string[]{"MixedCase","MIXEDCASE","abc\0tail","\0abc","é","东京","😀",new string('x',4097)});foreach(string attr in new string[]{"id","Description","DisplayName","FileName"})foreach(string v in strings)TestDoc("meta string/"+attr,Set(RootName,Meta,attr,v),p);foreach(string v in strings)TestDoc("startName",Set(RootName,Start,"Name",v),p);
        foreach(string attr in new string[]{"NumPlayers","CRC","Width","Height","BorderSize"})foreach(string v in Numbers)TestDoc("meta number/"+attr,Set(RootName,Meta,attr,v),p);foreach(string attr in new string[]{"x","y","z"})foreach(string v in Numbers)TestDoc("positionfloat/"+attr,Set(RootName,Start+"/Position",attr,v),p);
        foreach(string attr in new string[]{"IsMultiplayer","IsOfficial"})foreach(string v in new string[]{"true","false","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("metadata bool",Set(RootName,Meta,attr,v),p);
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder metas=new StringBuilder(),starts=new StringBuilder();for(int i=0;i<count;++i){metas.Append("<MapMetaData/>");starts.Append("<StartPosition><Position x='0.5' y='-2' z='1'/></StartPosition>");}AssetBuffer m=Test("metadata growth",Root(RootName,"",metas.ToString()),p);Check(m.InstanceData.Length==12+60*count&&Word(m.InstanceData,4,big)==count,"metadata60 exactgrowth");AssetBuffer s=Test("start growth",Root(RootName,"","<MapMetaData>"+starts+"</MapMetaData>"),p);Check(s.InstanceData.Length==72+20*count&&Word(s.InstanceData,60,big)==count,"StartPosition20 exactgrowth");}
        Test("two nested metadata order",Root(RootName,"","<MapMetaData Description='a'><StartPosition Name='b'/></MapMetaData><MapMetaData DisplayName='c'><StartPosition Name='d'/></MapMetaData>"),p);
        for(int mask=0;mask<8;++mask){XmlDocument d=Parse(Root(RootName,"","<MapMetaData/>"));string[] attrs={"Description","DisplayName","FileName"};int count=0;for(int i=0;i<3;++i)if((mask&(1<<i))!=0){((XmlElement)d.DocumentElement.FirstChild).SetAttribute(attrs[i],"");++count;}AssetBuffer b=TestDoc("empty stringpresence",d,p);Check(b.InstanceData.Length==72+4*count,"allstring emptyalloc4");}
        Test("metadata",Root(RootName,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("root unknownattr",Root(RootName,"Unknown='1'",""));Reject("root unknownchild",Root(RootName,"","<Unknown/>"));Reject("root text",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified rootattr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));foreach(string path in new string[]{Meta,Start,Start+"/Position"}){TestDoc("childmetadata",Set(RootName,path,"TypeId","1"),p);Reject("unknown childattr",Set(RootName,path,"Unknown","1").OuterXml);XmlDocument d=Set(RootName,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string tag in path.Split('/'))n=n.FirstChild;n.InnerText="payload";Reject("childtext",d.OuterXml);n.InnerText="";n.AppendChild(d.CreateElement("Unknown",NS));Reject("childchild",d.OuterXml);}Reject("duplicate Position",Root(RootName,"","<MapMetaData><StartPosition><Position/><Position/></StartPosition></MapMetaData>"));Reject("uppercaseposition",Set(RootName,Start+"/Position","X","1").OuterXml);
        Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlmapmetadataCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlmapmetadataCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS map metadata fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

