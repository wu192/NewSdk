using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;

// Input matrix is derived from the original SDK XSD and original runtime IDs,
// independently of the recovered field/layout tables.
class XmljoypadOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Roots={"JoypadCommandBarButtonTemplate","JoypadCommandBarTemplate",
        "UIJoypadCommandBarButtonBuild","UIJoypadCommandBarControlGroups","UIJoypadCommandBarCoopCommunicator",
        "UIJoypadCommandBarHomogenousGroup","UIJoypadCommandBarMixedGroup","UIJoypadCommandBarPlayerPowers",
        "UIJoypadCommandBarSingleUnit","UIJoypadCommandBarStances","UIJoypadCommandBarTopMenu"};
    static IAssetBuilderPlugin original;
    static int fixtures,comparisons,rejections,layouts;
    static string evidence;
    static StreamWriter transcript;
    static XmlNamespaceManager Namespaces(XmlDocument d){XmlNamespaceManager n=new XmlNamespaceManager(d.NameTable);n.AddNamespace("ea",NS);return n;}
    static XmlDocument Parse(string text){XmlDocument d=new XmlDocument();d.LoadXml(text);return d;}
    static InstanceDeclaration Declaration(XmlDocument d){
        AssetDeclarationDocument document=new AssetDeclarationDocument();
        Type stateType=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);
        object state=FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state,Namespaces(d));
        typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(document,state);
        InstanceDeclaration declaration=new InstanceDeclaration(document);declaration.XmlNode=d.DocumentElement;return declaration;
    }
    static void Check(bool test,string label){if(!test)throw new Exception(label);++layouts;}
    static uint Word(byte[] b,int at,bool big){return big?((uint)b[at]<<24)|((uint)b[at+1]<<16)|((uint)b[at+2]<<8)|b[at+3]:BitConverter.ToUInt32(b,at);}
    static string Hash(byte[] b){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
    static void Equal(string label,byte[] a,byte[] b){
        if(a==null||b==null||a.Length!=b.Length)throw new Exception(label+" lengths "+(a==null?-1:a.Length)+"/"+(b==null?-1:b.Length));
        for(int i=0;i<a.Length;++i)if(a[i]!=b[i])throw new Exception(label+" byte "+i+" original="+a[i]+" recovered="+b[i]);++comparisons;
    }
    static void SideArray(byte[] b,byte[] instance,bool big,bool relocation,string label){
        if(b.Length==0)return;
        Check(b.Length%4==0,label+" aligned side array");Check(Word(b,b.Length-4,big)==UInt32.MaxValue,label+" sentinel");
        HashSet<uint> seen=new HashSet<uint>();
        for(int i=0;i<b.Length-4;i+=4){uint at=Word(b,i,big);Check(at%4==0&&(ulong)at+4<=(ulong)instance.Length,label+" owner bounds");Check(seen.Add(at),label+" unique owner");
            if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds");}}
    }
    static AssetBuffer Call(Type type,string method,object[] args){try{return (AssetBuffer)type.GetMethod(method).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Dump(string prefix,AssetBuffer b){if(b==null)return;File.WriteAllBytes(prefix+".bin",b.InstanceData);File.WriteAllBytes(prefix+".imp",b.ImportsData);File.WriteAllBytes(prefix+".relo",b.RelocationData);}
    static AssetBuffer Test(string label,XmlDocument d,TargetPlatform p,Dictionary<string,object> layout){
        AssetBuffer a=null,b=null;
        try{
            InstanceDeclaration declaration=Declaration(d);a=original.ProcessInstance(declaration);
            Type type=typeof(XmljoypadCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            Check(type!=null,"public root compiler present");
            b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/imports",a.ImportsData,b.ImportsData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+"/relo");SideArray(b.ImportsData,b.InstanceData,big,false,label+"/imp");
            if(layout!=null&&layout.ContainsKey("exactSize"))Check(b.InstanceData.Length==Convert.ToInt32(layout["exactSize"]),label+" independent original struct size");
            if(layout!=null&&layout.ContainsKey("payloadType")){
                Check(Word(b.InstanceData,4,big)==8,label+" root8 payload pointer at4");
                Check(Word(b.InstanceData,8,big)==Convert.ToUInt32(layout["payloadType"]),label+" original concrete runtime TypeId");
            }
            transcript.WriteLine(((int)p)+"\t"+label+"\t"+b.InstanceData.Length+"\t"+Hash(b.InstanceData)+"\t"+Hash(b.RelocationData)+"\t"+Hash(b.ImportsData));++fixtures;return b;
        }catch(Exception e){
            File.WriteAllText(Path.Combine(evidence,"failure.xml"),d.OuterXml);Dump(Path.Combine(evidence,"failure-original"),a);Dump(Path.Combine(evidence,"failure-recovered"),b);
            throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);
        }
    }
    static string Root(string name,string body){return "<"+name+" xmlns='"+NS+"' id='Oracle'>"+body+"</"+name+">";}
    static void RejectSchema(string xml){try{PluginSchemaBoundary.Validate(Parse(xml));}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected SDK rejection: "+xml);}
    static void Matrix(object[] cases,TargetPlatform p){
        original.Initialize(null,p);
        foreach(object item in cases){Dictionary<string,object> row=(Dictionary<string,object>)item;Test((string)row["name"],Parse((string)row["xml"]),p,row);}
        foreach(string root in Roots){
            RejectSchema(Root(root,"<Unknown/>"));RejectSchema(Root(root,"").Replace("id='Oracle'","id='Oracle' Unknown='1'"));
            InstanceDeclaration declaration=Declaration(Parse(Root(root,"")));declaration.Handle.TypeName="ArmorTemplate";
            bool rejected=false;try{XmljoypadCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"root handle mismatch rejected");++rejections;
        }
        foreach(uint id in new uint[]{0u,UInt32.MaxValue,1u}){
            AssetBuffer unknown=Test("unknown-TypeId/"+id,Parse(Root("JoypadCommandBarButtonTemplate","<Data><Payload TypeId='"+id+"'/></Data>")),p,null);
            Check(unknown.InstanceData.Length==12&&Word(unknown.InstanceData,8,p!=TargetPlatform.Win32)==0,"original unknown fallback4 and zero runtime ID");
        }
        Test("empty Data",Parse(Root("JoypadCommandBarButtonTemplate","<Data/>")),p,null);
        Test("first duplicate Data",Parse(Root("JoypadCommandBarButtonTemplate","<Data><SetStance TypeId='325685730' Stance='AGGRESSIVE'/></Data><Data><PlaceReadyStructure TypeId='2129834919'/></Data>")),p,null);
        Test("first duplicate nullable tint",Parse(Root("JoypadCommandBarButtonTemplate","<Data><TargetedSpecialPower TypeId='2577768761'><ValidTargetTintColor R='1'/><ValidTargetTintColor G='2'/></TargetedSpecialPower></Data>")),p,null);
        Test("foreign list excluded",Parse(Root("JoypadCommandBarTemplate","<Slot xmlns='urn:foreign' Ordinal='9' Button='R\\3'/><Slot Ordinal='2' Button='R\\1'/>")),p,null);
        Test("foreign reference attribute excluded",Parse(Root("UIJoypadCommandBarControlGroups","").Replace("id='Oracle'","id='Oracle' xmlns:x='urn:foreign' x:Template='R\\7'")),p,null);
        Test("escaped reference attribute",Parse(Root("UIJoypadCommandBarControlGroups","").Replace("id='Oracle'","id='Oracle' Template='\\R\\7'")),p,null);
        Test("escaped scalar enum attribute",Parse(Root("JoypadCommandBarButtonTemplate","<Data><PlayerControlGroup TypeId='2833918550' GroupShortcut='\\17' StateData='\\R\\3'/></Data>")),p,null);
        Test("escaped enum attribute",Parse(Root("JoypadCommandBarButtonTemplate","<Data><SetStance TypeId='325685730' Stance='\\AGGRESSIVE'/></Data>")),p,null);
        Test("descendant reference text",Parse(Root("JoypadCommandBarTemplate","<GeneralButton>R<Part>\\</Part>7</GeneralButton>")),p,null);
        Test("foreign polymorphic element counted",Parse(Root("JoypadCommandBarButtonTemplate","<Data><PlaceReadyStructure xmlns='urn:foreign' TypeId='2129834919'/></Data>")),p,null);
        // Original C-string parsing stops at embedded NUL; mutate the DOM because XML forbids NUL in files.
        foreach(string value in new[]{"R\0\\7","R\\7\0tail","\0R\\7","R\\-1\0tail"}){
            XmlDocument d=Parse(Root("UIJoypadCommandBarControlGroups",""));d.DocumentElement.SetAttribute("Template",value);Test("reference-NUL",d,p,null);
        }
        foreach(string value in new[]{"AGGRESSIVE\0tail","\0AGGRESSIVE","HOLD_FIRE\0tail"}){
            XmlDocument d=Parse(Root("JoypadCommandBarButtonTemplate","<Data><SetStance TypeId='325685730'/></Data>"));((XmlElement)d.GetElementsByTagName("SetStance",NS)[0]).SetAttribute("Stance",value);Test("enum-NUL",d,p,null);
        }
        bool nullRejected=false;try{XmljoypadCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullRejected=true;}Check(nullRejected,"null declaration rejected");++rejections;
    }
    static int Main(string[] args){
        Dictionary<string,object> result=new Dictionary<string,object>();result["succeeded"]=false;
        evidence=Path.GetDirectoryName(Path.GetFullPath(args[1]));Directory.CreateDirectory(evidence);
        try{
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            original=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[0]).GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            string hash=Hash(File.ReadAllBytes(args[0]));Check(hash=="d4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6","fixed original DLL");result["originalDllSha256"]=hash;
            string fixturePath=args.Length>2?args[2]:Environment.GetEnvironmentVariable("NEWSdk_JOYPAD_FIXTURES");
            if(String.IsNullOrEmpty(fixturePath))throw new Exception("Joypad fixture path required (third argument or NEWSdk_JOYPAD_FIXTURES)");
            JavaScriptSerializer json=new JavaScriptSerializer();json.MaxJsonLength=Int32.MaxValue;
            object[] cases=json.Deserialize<object[]>(File.ReadAllText(fixturePath));result["inputCases"]=cases.Length;result["rootNames"]=Roots;
            List<object> rows=new List<object>();
            using(transcript=new StreamWriter(Path.Combine(evidence,Path.GetFileNameWithoutExtension(args[1])+"-buffers.tsv"))){
                foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}){
                    int f=fixtures,c=comparisons,r=rejections,l=layouts;Matrix(cases,p);
                    Dictionary<string,object> row=new Dictionary<string,object>();row["platform"]=(int)p;row["fixtures"]=fixtures-f;row["arrayComparisons"]=comparisons-c;row["rejections"]=rejections-r;row["layoutAssertions"]=layouts-l;rows.Add(row);
                    Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
                }
            }
            result["platforms"]=rows;result["fixtures"]=fixtures;result["arrayComparisons"]=comparisons;result["rejections"]=rejections;result["layoutAssertions"]=layouts;result["succeeded"]=true;return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}
        finally{File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}
