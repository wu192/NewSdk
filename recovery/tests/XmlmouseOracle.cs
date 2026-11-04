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
class XmlmouseOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Mouse="Mouse";
    static readonly string[] Types={Mouse};
    static readonly uint[] TypeIds={2525492603u};
    static readonly int[] Sizes={20};
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
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlmouseCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlmouseCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static XmlDocument Attr(string type,string key,string value){XmlDocument d=Parse(Root(type,"",""));d.DocumentElement.SetAttribute(key,value);return d;}
    static uint FloatBits(float value){return BitConverter.ToUInt32(BitConverter.GetBytes(value),0);}
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        AssetBuffer defaults=Test("defaults",Root(Mouse,"",""),p);Check(defaults.InstanceData.Length==20&&Word(defaults.InstanceData,4,big)==48&&Word(defaults.InstanceData,8,big)==FloatBits(100f*0.001f)&&Word(defaults.InstanceData,12,big)==20&&Word(defaults.InstanceData,16,big)==10,"independent complete Mouse20 defaults and Time float product");Offsets(defaults.RelocationData,new uint[0],big,"no pointers");Offsets(defaults.ImportsData,new uint[0],big,"no imports");Test("metadata",Root(Mouse,"TypeId='2525492603' inheritFrom='Ignored'",""),p);
        foreach(string field in new[]{"DragTolerance","DragThreshold","InGameUIMaxDoubleClickDistance","AptHitRegionMaxDoubleClickDistance"}){foreach(string value in Numbers)TestDoc("scalar/"+field,Attr(Mouse,field,value),p);foreach(string value in new[]{"250ms","1s250ms","3ms4s","2d1ms","2\0ms","2ms\0s","17\0tail","\0-1","-4294967296","-4294967297","-18446744073709551615","18446744073709551615","--1","+ 1","é","東京"})TestDoc("scalar boundary/"+field,Attr(Mouse,field,value),p);}
        AssetBuffer full=Test("ABI/full",Root(Mouse,"DragTolerance='-17' DragThreshold='250ms' InGameUIMaxDoubleClickDistance='0x12345678' AptHitRegionMaxDoubleClickDistance='-1'",""),p);Check(full.InstanceData.Length==20&&Word(full.InstanceData,4,big)==0xffffffefu&&Word(full.InstanceData,8,big)==FloatBits(0.25f)&&Word(full.InstanceData,12,big)==0x12345678u&&Word(full.InstanceData,16,big)==UInt32.MaxValue,"all full field offsets and signed vs unsigned parsing");
        foreach(string field in new[]{"DragTolerance","DragThreshold","InGameUIMaxDoubleClickDistance","AptHitRegionMaxDoubleClickDistance"}){AssetBuffer empty=TestDoc("empty overrides/"+field,Attr(Mouse,field,""),p);int off=field=="DragTolerance"?4:field=="DragThreshold"?8:field=="InGameUIMaxDoubleClickDistance"?12:16;Check(Word(empty.InstanceData,off,big)==0,"explicit empty clears field default");}
        Test("comments whitespace",Root(Mouse,""," <!--comment--> \n"),p);Reject("unknown attr",Root(Mouse,"Unknown='1'",""));Reject("qualified attr",Root(Mouse,"xmlns:q='urn:q' q:TypeId='1'",""));Reject("child",Root(Mouse,"","<Unknown/>"));Reject("text",Root(Mouse,"","payload"));Reject("foreign root","<Mouse xmlns='urn:q'/>");Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlmouseCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Mouse,"",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName="TargetingBaseCompare";rejected=false;try{XmlmouseCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"mismatch TypeId");++rejections;
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
            Console.WriteLine("PASS Mouse fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

