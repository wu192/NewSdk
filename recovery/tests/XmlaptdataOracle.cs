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
class XmlaptdataOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"AptAptData","AptConstData","AptDatData","AptGeometryData"};
    static readonly uint[] TypeIds={1816258268u,983359664u,1110676961u,1034587168u};
    static readonly int[] Sizes={12,12,12,16};
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
        HashSet<uint> seen=new HashSet<uint>();for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at%4==0&&(ulong)at+4<=(ulong)instance.Length,label+" aligned field bounds");Check(seen.Add(at),label+" unique bookmark offsets");if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&(target<instance.Length||(at==4&&target==instance.Length&&(instance.Length==12||instance.Length==16)&&Word(instance,8,big)==0)),label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            original.Initialize(null,p);InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlaptdataCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlaptdataCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static string fixtureDirectory;
    static readonly int[] BlobSizes={0,1,2,3,4,5,6,7,8,15,16,17,27,28,29,31,32,33,63,64,65,255,256,257,1023,1024,1025,4095,4096,4097,65535,65536,65537};
    static readonly Dictionary<int,string> BlobFiles=new Dictionary<int,string>();
    static byte[] Bytes(int size){byte[] data=new byte[size];for(int i=0;i<size;++i)data[i]=(byte)(i*73+19);return data;}
    static void CreateFixtures(){fixtureDirectory=Path.Combine(Path.GetTempPath(),"XmlAptdataOracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixtureDirectory);foreach(int size in BlobSizes){string path=Path.Combine(fixtureDirectory,"blob ASCII spaces "+size+".bin");File.WriteAllBytes(path,Bytes(size));BlobFiles.Add(size,path);}}
    static void BlobAbi(string root,byte[] data,AssetBuffer b,TargetPlatform p){bool big=p!=TargetPlatform.Win32;int size=root=="AptGeometryData"?16:12;Check(b.InstanceData.Length==size+((data.Length+3)&~3),"blob exact bytecount andalignment");Check(Word(b.InstanceData,4,big)==size&&Word(b.InstanceData,8,big)==data.Length,"DataBlob pointerthenlength");for(int i=0;i<data.Length;++i)Check(b.InstanceData[size+i]==data[i],"raw filebyte unchanged endian/"+i);for(int i=size+data.Length;i<b.InstanceData.Length;++i)Check(b.InstanceData[i]==0,"blob padding0");Offsets(b.RelocationData,new uint[]{4},big,"DataBlob pointer includingzerolen");Offsets(b.ImportsData,new uint[0],big,"DataBlob rawbytes no imports");}
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string root in Types){int size=root=="AptGeometryData"?16:12;AssetBuffer d=Test("ABI/default/"+root,Root(root,"",""),p);Check(d.InstanceData.Length==size,"root exact12or16");for(int at=0;at<size;at+=4)Check(Word(d.InstanceData,at,big)==0,"absent allzero");Offsets(d.RelocationData,new uint[0],big,"no File nopointer");
            foreach(int count in BlobSizes){AssetBuffer b=TestDoc("filebytes/"+root+"/"+count,Set(root,"","File",BlobFiles[count]),p);BlobAbi(root,Bytes(count),b,p);}
            foreach(string path in new string[]{"",Path.Combine(fixtureDirectory,"missing.bin"),BlobFiles[7]+".missing","file://"+BlobFiles[7],"😀","é","东京","\\0","NUL-does-not-exist","\0"+BlobFiles[7]}){AssetBuffer b=TestDoc("fileopen failure/"+root,Set(root,"","File",path),p);Check(b.InstanceData.Length==size&&Word(b.InstanceData,4,big)==0&&Word(b.InstanceData,8,big)==0,"failed fopen leaves zero DataBlob");Offsets(b.RelocationData,new uint[0],big,"failed fopen no pointer");}
            AssetBuffer nul=TestDoc("File Cstring truncation/"+root,Set(root,"","File",BlobFiles[7]+"\0missing-suffix"),p);BlobAbi(root,Bytes(7),nul,p);
            Test("metadata",Root(root,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown attr",Root(root,"Unknown='1'",""));Reject("unknown child",Root(root,"","<Unknown/>"));Reject("root text",Root(root,"","payload"));Reject("foreign root",Root(root,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(root,"xmlns:q='urn:q' q:File='missing'",""));
        }
        foreach(string v in Numbers)TestDoc("geometry signed AptID",Set("AptGeometryData","","AptID",v),p);foreach(string v in new string[]{"-7","2147483647","-2147483648","2147483648","-2147483649","1\0tail","\0-1","é","东京"})TestDoc("geometry AptID boundary",Set("AptGeometryData","","AptID",v),p);
        XmlDocument geometry=Set("AptGeometryData","","File",BlobFiles[257]);geometry.DocumentElement.SetAttribute("AptID","-7");AssetBuffer g=TestDoc("ABI/geometry File andid",geometry,p);BlobAbi("AptGeometryData",Bytes(257),g,p);Check(Word(g.InstanceData,12,big)==unchecked((uint)-7),"AptID@12 signed-endian independent ofblob");
        foreach(string root in new string[]{"AptAptData","AptConstData","AptDatData"})Reject("AptID onlygeometry",Root(root,"AptID='1'",""));Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlaptdataCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root("AptAptData","",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="AptDatData";rejected=false;try{XmlaptdataCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;
    }
    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Assembly assembly=Assembly.LoadFrom(args[0]);original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            using(SHA256 sha=SHA256.Create()){string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");Check(hash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","fixed original DLL");result["originalDllSha256"]=hash;}
            CreateFixtures();List<object> rows=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;Matrix(p);Dictionary<string,object> row=new Dictionary<string,object>();row["platform"]=(int)p;row["fixtures"]=fixtures-f;row["arrayComparisons"]=comparisons-c;row["rejections"]=rejections-r;row["layoutAssertions"]=layouts-l;rows.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=rows;result["fixtures"]=fixtures;result["arrayComparisons"]=comparisons;result["rejections"]=rejections;result["layoutAssertions"]=layouts;result["succeeded"]=true;
            Console.WriteLine("PASS APT data fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));if(fixtureDirectory!=null&&Directory.Exists(fixtureDirectory))Directory.Delete(fixtureDirectory,true);}
    }
}

