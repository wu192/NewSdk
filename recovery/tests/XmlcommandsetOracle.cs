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
class XmlcommandsetOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Command="CommandSet";
    static readonly string[] Types={Command};
    static readonly uint[] TypeIds={2584759047u};
    static readonly int[] Sizes={24};
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
            Type type=typeof(XmlcommandsetCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlcommandsetCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Strings={"","a","ab","abc","abcd","abcde","abcdefg","abcdefgh","abcdefghi","MixedCase","  spaces  ","A\tB\r\nC","<&'\"","资源","éßð","😀","A\0tail","\0tail","A\0B\0C"};
    static readonly string[] Scalars={"","0","-0","1","-1","TRUE","true","True","false","False","FALSE","yes","077","0xff","garbage","2147483647","2147483648","-2147483648","-2147483649","4294967295","4294967296","9999999999999999999999999999","  +17junk","1\0tail","\0TRUE"};
    static readonly string[] EDistributionType={"CONSTANT","GAUSSIAN","HIGH_BIAS","LOW_BIAS","TRIANGULAR","UNIFORM"};
    static readonly int[] VDistributionType={0,2,5,4,3,1};
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;AssetBuffer empty=Test("ABI/defaults",Root(Command,"",""),p);Check(empty.InstanceData.Length==24&&Word(empty.InstanceData,12,big)==32,"CommandSet24 InitialVisible32 default");for(int i=0;i<24;++i)if(i<12||i>=16)Check(empty.InstanceData[i]==0,"all bytes outside InitialVisible word12..15 remainzero; byte="+i);Check(empty.RelocationData.Length==0&&empty.ImportsData.Length==0,"default no bookmarks");
        AssetBuffer a=Test("ABI/full",Root(Command,"Name='Root' InitialVisible='-1'","<CommandButton Name='A' Index='7'><InitialDelay High='1.5' Low='0.5' Type='GAUSSIAN'/></CommandButton><CommandButton Name='' Index='9'/>"),p);Check(a.InstanceData.Length==84&&Word(a.InstanceData,4,big)==4&&Word(a.InstanceData,8,big)==24&&Word(a.InstanceData,12,big)==UInt32.MaxValue&&Word(a.InstanceData,16,big)==2&&Word(a.InstanceData,20,big)==32,"root Name before two16bytebuttons and signedVisible");Check(Word(a.InstanceData,32,big)==1&&Word(a.InstanceData,36,big)==64&&Word(a.InstanceData,40,big)==7&&Word(a.InstanceData,44,big)==68,"CommandButton16 Name8/Index4/nullableDelay4");Check(Word(a.InstanceData,68,big)==2&&Word(a.InstanceData,72,big)==0x3f000000u&&Word(a.InstanceData,76,big)==0x3fc00000u,"ClientRandomVariable12 TypeEnum/Low/High");Check(Word(a.InstanceData,48,big)==0&&Word(a.InstanceData,52,big)==80&&Word(a.InstanceData,56,big)==9&&Word(a.InstanceData,60,big)==0,"secondbutton presentemptyName allocates4 while absentDelay doesnot");Check(a.InstanceData[64]==65&&a.InstanceData[65]==0&&a.InstanceData[80]==0,"name bytes and terminators");Offsets(a.RelocationData,new uint[]{8,20,36,44,52},big,"root then button-owner bookmark order by target block");Check(a.ImportsData.Length==0,"CommandSet uses strings and nullable data without asset imports");
        AssetBuffer d=Test("ABI/presentemptydelay",Root(Command,"","<CommandButton><InitialDelay/></CommandButton>"),p);Check(d.InstanceData.Length==52&&Word(d.InstanceData,20,big)==24&&Word(d.InstanceData,36,big)==40&&Word(d.InstanceData,40,big)==1&&Word(d.InstanceData,44,big)==0&&Word(d.InstanceData,48,big)==0,"presentemptyDelay allocates12 and defaultUNIFORM1");AssetBuffer absent=Test("ABI/absentdelay",Root(Command,"","<CommandButton/>"),p);Check(absent.InstanceData.Length==40&&Word(absent.InstanceData,36,big)==0,"absentDelay no allocation");
        XmlDocument nul=Set(Command,"CommandButton","Name","abc\0tail");AssetBuffer n=TestDoc("ABI/nulname",nul,p);Check(n.InstanceData.Length==44&&Word(n.InstanceData,24,big)==3&&Word(n.InstanceData,28,big)==40,"name C-string truncation");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;Test("defaults",Root(Command,"",""),p);Test("metadata",Root(Command,"TypeId='2584759047' inheritFrom='parent'",""),p);foreach(string value in Scalars){TestDoc("signedvisible",Set(Command,"","InitialVisible",value),p);TestDoc("unsignedindex",Set(Command,"CommandButton","Index",value),p);}foreach(string path in new string[]{"","CommandButton"}){foreach(string value in Strings)TestDoc("name",Set(Command,path,"Name",value),p);foreach(int count in new int[]{0,1,2,3,4,5,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535}){AssetBuffer n=TestDoc("namegrowth",Set(Command,path,"Name",new string('A',count)),p);int at=path.Length==0?4:24;Check(Word(n.InstanceData,at,big)==count&&n.InstanceData.Length==(path.Length==0?24:40)+((count+4)&~3),"independent string length and padded size");}}
        foreach(string attr in new string[]{"Low","High"})foreach(string value in Numbers)TestDoc("random-float",Set(Command,"CommandButton/InitialDelay",attr,value),p);for(int i=0;i<EDistributionType.Length;++i){AssetBuffer a=TestDoc("distributionexact",Set(Command,"CommandButton/InitialDelay","Type",EDistributionType[i]),p);Check(Word(a.InstanceData,40,big)==VDistributionType[i],"original PE distribution enum value");foreach(string value in new string[]{EDistributionType[i].ToLowerInvariant()," "+EDistributionType[i],EDistributionType[i]+" ",EDistributionType[i]+"\0tail","+"+EDistributionType[i],"-"+EDistributionType[i]})TestDoc("distributionboundary",Set(Command,"CommandButton/InitialDelay","Type",value),p);}foreach(string value in new string[]{"","UNKNOWN","0","1","\0UNIFORM"}){AssetBuffer a=TestDoc("distributionfallback",Set(Command,"CommandButton/InitialDelay","Type",value),p);Check(Word(a.InstanceData,40,big)==0,"presentunknown Type defaults CONSTANT0 distinct from absentUNIFORM1");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder list=new StringBuilder();for(int i=0;i<count;++i)list.Append("<CommandButton Name='A' Index='"+i+"'><InitialDelay Type='UNIFORM' Low='0.5' High='1.5'/></CommandButton>");AssetBuffer a=Test("buttongrowth/"+count,Root(Command,"Name='Root'",list.ToString()),p);Check(a.InstanceData.Length==32+32*count&&Word(a.InstanceData,16,big)==count,"16bytebutton+4namestring+12random each; component does not truncate schema32");if(count>0)Check(Word(a.InstanceData,20,big)==32,"button list allocated before nested names/delays");}
        foreach(string path in new string[]{"","CommandButton","CommandButton/InitialDelay"}){TestDoc("metadata",Set(Command,path,"TypeId","1"),p);Reject("unknownattr",Set(Command,path,"Unknown","1").OuterXml);XmlDocument d=Set(Command,path,"TypeId","1");XmlNode at=d.DocumentElement;if(path.Length>0)foreach(string name in path.Split('/'))at=at.FirstChild;XmlAttribute q=d.CreateAttribute("q","TypeId","urn:q");q.Value="1";at.Attributes.Append(q);Reject("qualifiedattr",d.OuterXml);}
        Reject("rootchild",Root(Command,"","<Unknown/>"));Reject("roottext",Root(Command,"","payload"));Reject("buttonchild",Root(Command,"","<CommandButton><Unknown/></CommandButton>"));Reject("buttontext",Root(Command,"","<CommandButton>payload</CommandButton>"));Reject("randomtext",Root(Command,"","<CommandButton><InitialDelay>payload</InitialDelay></CommandButton>"));Reject("randomchild",Root(Command,"","<CommandButton><InitialDelay><Unknown/></InitialDelay></CommandButton>"));Reject("duplicaterandom",Root(Command,"","<CommandButton><InitialDelay/><InitialDelay/></CommandButton>"));Reject("foreignbutton",Root(Command,"","<CommandButton xmlns='urn:q'/>"));Reject("foreignrandom",Root(Command,"","<CommandButton><InitialDelay xmlns='urn:q'/></CommandButton>"));Reject("unknownroot",Root("Unknown","",""));bool nil=false;try{XmlcommandsetCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Command,"","")));dec.Handle.TypeName="UnitIntro";bool wrong=false;try{XmlcommandsetCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS CommandSet fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

