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
class XmlredalertOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"RedAlertTemplate"};
    static readonly uint[] TypeIds={4141306833u};
    static readonly int[] Sizes={92};
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
            Type type=typeof(XmlredalertCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlredalertCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Integers={"OrangeAreaThreatThreshold","RedAreaThreatThreshold","MaxAllowedThreat","YellowAreaThreatDecayPerSecond","OrangeAreaThreatDecayPerSecond","RedAreaThreatDecayPerSecond","RedAlertBuildingDamagePerSecond","RedAlertUpdatedBuildingDamagePerSecond"};
    static readonly int[] IntegersOffsets={4,8,12,24,28,32,36,40};
    static readonly string[] Reals={"KillThreatMultiplier","DeathThreatMultiplier","YellowAlertTechPointMultiplier","OrangeAlertTechPointMultiplier","RedAlertTechPointMultiplier"};
    static readonly int[] RealsOffsets={16,20,60,64,68};
    static readonly string[] Booleans={"DisableNonRepairability","RedAlertDisableStructureBuilding","RedAlertShowAllUnitsAndStructures"};
    static readonly string[] ReferencesAttrs={"EvaEventToPlayOnRedAlertInitiateOwner","EvaEventToPlayOnRedAlertInitiateAlly","EvaEventToPlayOnRedAlertInitiateEnemy"};
    static readonly string[] Effects={"YellowAreaEffect","OrangeAreaEffect","RedAreaEffect","RedAlertEffect"};
    const string RootName="RedAlertTemplate";
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(RootName,"",""),p);Check(d.InstanceData.Length==92,"root92");for(int off=4;off<88;off+=4){uint expected=off==16||off==20||off==60||off==64||off==68?0x3f800000u:off==24||off==28||off==32?5u:0u;Check(Word(d.InstanceData,off,big)==expected,"exactdefault word "+off);}Check(d.InstanceData[88]==0&&d.InstanceData[89]==1&&d.InstanceData[90]==1&&d.InstanceData[91]==0,"bool88/89/90 padding91");Offsets(d.RelocationData,new uint[0],big,"default no effects pointers");Offsets(d.ImportsData,new uint[0],big,"default refs empty");
        StringBuilder attrs=new StringBuilder();for(int i=0;i<Integers.Length;++i)attrs.Append(Integers[i]+"='"+(100+IntegersOffsets[i])+"' ");foreach(string name in Reals)attrs.Append(name+"='0.5' ");attrs.Append("DisableNonRepairability='true' RedAlertDisableStructureBuilding='false' RedAlertShowAllUnitsAndStructures='false' RedAlertBuildingDamageIncreaseTime='500ms' ");attrs.Append(ReferencesAttrs[0]+"='E\\9' "+ReferencesAttrs[1]+"='E\\1' "+ReferencesAttrs[2]+"='E\\5'");
        string effects="<RedAlertEffect>A\\1</RedAlertEffect><RedAreaEffect>A\\9</RedAreaEffect><OrangeAreaEffect>A\\3</OrangeAreaEffect><YellowAreaEffect>A\\7</YellowAreaEffect>";AssetBuffer f=Test("ABI/fullReverseXML",Root(RootName,attrs.ToString(),effects),p);Check(f.InstanceData.Length==108,"four nullable4 refs");for(int i=0;i<Integers.Length;++i)Check(Word(f.InstanceData,IntegersOffsets[i],big)==100+IntegersOffsets[i],"uint exactoffset");foreach(int off in RealsOffsets)Check(Word(f.InstanceData,off,big)==0x3f000000u,"all5 real offsets");Check(Word(f.InstanceData,44,big)==0x3f000000u,"time@44");Check(f.InstanceData[88]==1&&f.InstanceData[89]==0&&f.InstanceData[90]==0&&f.InstanceData[91]==0,"boolbytes remainunswapped");uint[] refs={10,2,6};for(int i=0;i<3;++i)Check(Word(f.InstanceData,48+4*i,big)==refs[i],"root Eva refs");uint[] effectRefs={8,4,10,2};for(int i=0;i<4;++i)Check(Word(f.InstanceData,72+4*i,big)==92+4*i&&Word(f.InstanceData,92+4*i,big)==effectRefs[i],"effects marshal fixedorder notXML");Offsets(f.RelocationData,new uint[]{72,76,80,84},big,"effects pointer order");Offsets(f.ImportsData,new uint[]{52,56,48,92,96,100,104},big,"rootindex thenowner effect imports");
        foreach(string name in Effects){AssetBuffer e=Test("ABI/presentEmpty/"+name,Root(RootName,"","<"+name+"/>"),p);Check(e.InstanceData.Length==96&&Word(e.InstanceData,72+4*Array.IndexOf(Effects,name),big)==92&&Word(e.InstanceData,92,big)==0,"empty effect stillalloc4");Offsets(e.ImportsData,new uint[0],big,"empty effect no import");}
    }
    static void Matrix(TargetPlatform p){
        foreach(string attr in Integers)foreach(string v in Numbers)TestDoc("uint/"+attr,Set(RootName,"",attr,v),p);foreach(string attr in Reals)foreach(string v in Numbers)TestDoc("real/"+attr,Set(RootName,"",attr,v),p);foreach(string v in Numbers)TestDoc("time",Set(RootName,"","RedAlertBuildingDamageIncreaseTime",v),p);foreach(string v in new string[]{"500ms","2s","1ms2s","1s2ms","-200ms","1MS","1S","12\0s"})TestDoc("time units",Set(RootName,"","RedAlertBuildingDamageIncreaseTime",v),p);
        foreach(string attr in Booleans)foreach(string v in new string[]{"true","false","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("bool/"+attr,Set(RootName,"",attr,v),p);
        foreach(string attr in ReferencesAttrs)foreach(string v in References)TestDoc("Eva ref/"+attr,Set(RootName,"",attr,v),p);foreach(string name in Effects)foreach(string v in References){XmlDocument d=Parse(Root(RootName,"","<"+name+"/>"));d.DocumentElement.FirstChild.InnerText=v;TestDoc("effectref/"+name,d,p);}
        for(int mask=0;mask<16;++mask){StringBuilder list=new StringBuilder();for(int i=3;i>=0;--i)if((mask&(1<<i))!=0)list.Append("<"+Effects[i]+">A\\"+i+"</"+Effects[i]+">");AssetBuffer b=Test("effect presence combinations/"+mask,Root(RootName,"",list.ToString()),p);int count=0;for(int i=0;i<4;++i)if((mask&(1<<i))!=0)++count;Check(b.InstanceData.Length==92+4*count,"exact presence allocation4");}
        Test("metadata",Root(RootName,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown attr",Root(RootName,"Unknown='1'",""));Reject("unknown child",Root(RootName,"","<Unknown/>"));Reject("root text",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));foreach(string name in Effects){TestDoc("effect metadata",Set(RootName,name,"TypeId","1"),p);Reject("unknown effectattr",Set(RootName,name,"Unknown","1").OuterXml);Reject("unknown effectchild",Root(RootName,"","<"+name+"><Unknown/></"+name+">"));Reject("duplicate effect",Root(RootName,"","<"+name+"/><"+name+"/>"));Reject("foreign effect",Root(RootName,"","<"+name+" xmlns='urn:wrong'/>"));Reject("qualified effectattr",Root(RootName,"","<"+name+" xmlns:q='urn:q' q:Unknown='1'/>"));}
        Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlredalertCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlredalertCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS red alert fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

