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
class XmldynamicsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"DynamicsContactTagMapping"};
    static readonly uint[] TypeIds={1044178106u};
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
            Type type=typeof(XmldynamicsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmldynamicsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    const string RootName="DynamicsContactTagMapping";
    static readonly string[] Contacts={"DEBRIS","GROUND","NONE","PROP","TREE","VEHICLE","WALL","WATER"};
    static readonly uint[] ContactValues={7,1,0,4,6,3,5,2};
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(RootName,"",""),p);Check(d.InstanceData.Length==12&&Word(d.InstanceData,4,big)==0&&Word(d.InstanceData,8,big)==0,"root12 empty");Offsets(d.RelocationData,new uint[0],big,"no mappings no pointers");
        AssetBuffer empty=Test("ABI/emptyMapping",Root(RootName,"","<Mapping/>"),p);Check(empty.InstanceData.Length==28&&Word(empty.InstanceData,4,big)==1&&Word(empty.InstanceData,8,big)==12&&Word(empty.InstanceData,12,big)==0&&Word(empty.InstanceData,16,big)==0&&Word(empty.InstanceData,20,big)==0x3f99999au&&Word(empty.InstanceData,24,big)==0,"mapping16 defaultforce1.2, enumsabsent0");Offsets(empty.RelocationData,new uint[]{8},big,"mapping pointer");Offsets(empty.ImportsData,new uint[0],big,"no default FX");
        AssetBuffer f=Test("ABI/full",Root(RootName,"","<Mapping ContactA='VEHICLE' ContactB='WALL' MinForce='0.5' FX='F\\9'/><Mapping ContactA='UNKNOWN' ContactB='NONE' MinForce='-2' FX='F\\1'/>"),p);Check(f.InstanceData.Length==44&&Word(f.InstanceData,4,big)==2&&Word(f.InstanceData,8,big)==12,"two mapping16");uint[] words={3,5,0x3f000000u,10,7,0,0xc0000000u,2};for(int i=0;i<words.Length;++i)Check(Word(f.InstanceData,12+4*i,big)==words[i],"exact mapping slots");Offsets(f.ImportsData,new uint[]{40,24},big,"FX refs byindex");Offsets(f.RelocationData,new uint[]{8},big,"single listpointer");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string attr in new string[]{"ContactA","ContactB"})for(int i=0;i<Contacts.Length;++i)foreach(string v in new string[]{Contacts[i],Contacts[i].ToLowerInvariant(),Contacts[i]+"\0tail"," "+Contacts[i],Contacts[i]+" "}){AssetBuffer b=TestDoc("contact enum/"+attr,Set(RootName,"Mapping",attr,v),p);Check(Word(b.InstanceData,attr=="ContactA"?12:16,big)==(v==Contacts[i]||v==Contacts[i]+"\0tail"?ContactValues[i]:7),"independent enum/fallback7");}
        foreach(string attr in new string[]{"ContactA","ContactB"})foreach(string v in new string[]{"","UNKNOWN","0","-1","\0GROUND","Ground"})TestDoc("contactedge",Set(RootName,"Mapping",attr,v),p);
        List<string> numbers=new List<string>(Numbers);numbers.AddRange(new string[]{"NaN","INF","-INF","0x1.8p2","1.000000059604644775390625","2d3","2D3","1\0tail","\0-1","é","东京"});foreach(string v in numbers)TestDoc("force",Set(RootName,"Mapping","MinForce",v),p);foreach(string v in References)TestDoc("FX",Set(RootName,"Mapping","FX",v),p);
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder items=new StringBuilder();for(int i=0;i<count;++i)items.Append("<Mapping ContactA='GROUND' ContactB='WATER' MinForce='0.5' FX='F\\"+(count-i)+"'/>");AssetBuffer b=Test("mapping growth",Root(RootName,"",items.ToString()),p);Check(b.InstanceData.Length==12+16*count&&Word(b.InstanceData,4,big)==count,"mapping exact16growth");if(count>0){uint[] imports=new uint[count];for(int i=0;i<count;++i)imports[i]=(uint)(24+16*(count-i-1));Offsets(b.ImportsData,imports,big,"descending FX references");}}
        Test("metadata",Root(RootName,"TypeId='1' inheritFrom='Ignored'","<!--c--><Mapping TypeId='1'/>"),p);Reject("unknown attr",Root(RootName,"Unknown='1'",""));Reject("unknown child",Root(RootName,"","<Unknown/>"));Reject("text",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));Reject("mapping unknownattr",Set(RootName,"Mapping","Unknown","1").OuterXml);Reject("mapping text",Root(RootName,"","<Mapping>payload</Mapping>"));Reject("mapping child",Root(RootName,"","<Mapping><Unknown/></Mapping>"));Reject("foreign mapping",Root(RootName,"","<Mapping xmlns='urn:wrong'/>"));Reject("qualified mapping",Root(RootName,"","<Mapping xmlns:q='urn:q' q:FX='F\\1'/>"));Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmldynamicsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmldynamicsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS dynamics fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

