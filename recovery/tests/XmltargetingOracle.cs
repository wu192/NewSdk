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
class XmltargetingOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Base="TargetingBaseCompare",Distance="TargetingDistanceCompare",Combat="TargetingCombatChainCompare",Turret="TargetingInTurretArcCompare",CompareList="TargetingCompareList";
    static readonly string[] Types={Base,Distance,Combat,Turret,CompareList};
    static readonly uint[] TypeIds={3600209407u,2359666647u,89767986u,2007776008u,3188107749u};
    static readonly int[] Sizes={4,8,4,4,12};
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
            Type type=typeof(XmltargetingCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmltargetingCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" ownerblock/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static XmlDocument Attr(string type,string key,string value){XmlDocument d=Parse(Root(type,"",""));d.DocumentElement.SetAttribute(key,value);return d;}
    static XmlDocument Refs(params string[] values){XmlDocument d=Parse(Root(CompareList,"",""));foreach(string value in values){XmlElement e=d.CreateElement("Compare",NS);e.InnerText=value;d.DocumentElement.AppendChild(e);}return d;}
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        for(int i=0;i<Types.Length;++i){
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");
            Check(a.RelocationData.Length==0&&a.ImportsData.Length==0,"empty roots no bookmarks");
            if(Types[i]==Distance)Check(Word(a.InstanceData,4,big)==0x41f00000u,"Tolerance default30");
            else foreach(byte value in a.InstanceData)Check(value==0,"known empty base/list zero ABI");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='Ignored'",""),p);
            Reject("unknown attr",Root(Types[i],"Unknown='1'",""));Reject("foreign attr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));
            Reject("unknown child",Root(Types[i],"","<Unknown/>"));Reject("root text",Root(Types[i],"","payload"));
            Reject("foreign child",Root(Types[i],"","<Compare xmlns='urn:q'>X</Compare>"));
        }
        foreach(string value in Numbers)TestDoc("float/"+value,Attr(Distance,"Tolerance",value),p);
        foreach(string value in new string[]{"1\0tail","1.25e+12rest","+INF","NaN","  -0.5","3.402823466e38","1.17549435e-38","1.40129846e-45"})TestDoc("float-extra/"+value.Length,Attr(Distance,"Tolerance",value),p);
        AssetBuffer tolerance=Test("ABI/Tolerance",Root(Distance,"Tolerance='12.25'",""),p);Check(Word(tolerance.InstanceData,4,big)==0x41440000u,"Tolerance offset4 binary32");
        foreach(string value in References)TestDoc("reference/"+value.Length,Refs(value),p);
        AssetBuffer references=TestDoc("ABI/references",Refs("T\\9","T\\1","T\\7","T\\3"),p);
        Check(references.InstanceData.Length==28&&Word(references.InstanceData,4,big)==4&&Word(references.InstanceData,8,big)==12,"root12 list4 stride");
        uint[] indexes={10,2,8,4};for(int i=0;i<4;++i)Check(Word(references.InstanceData,12+4*i,big)==indexes[i],"strong index+1 at list slot");
        Offsets(references.RelocationData,new uint[]{8},big,"list pointer relocation");Offsets(references.ImportsData,new uint[]{16,24,20,12},big,"imports sorted reference value");
        AssetBuffer mixed=TestDoc("ABI/null-and-reference",Refs("raw","","R\\5","R\\-1","R\\0"),p);
        Check(mixed.InstanceData.Length==32&&Word(mixed.InstanceData,12,big)==0&&Word(mixed.InstanceData,16,big)==0&&Word(mixed.InstanceData,20,big)==6&&Word(mixed.InstanceData,24,big)==0&&Word(mixed.InstanceData,28,big)==1,"missing delimiter staysnull; -1 retained import with zero output");
        Offsets(mixed.ImportsData,new uint[]{28,20,24},big,"zero/wraparound import ordering");
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            string[] values=new string[count];for(int i=0;i<count;++i)values[i]="R\\"+(count-i);AssetBuffer a=TestDoc("growth/"+count,Refs(values),p);
            Check(a.InstanceData.Length==12+4*count&&Word(a.InstanceData,4,big)==count,"exact list allocation growth");
            if(count>0){Check(Word(a.InstanceData,8,big)==12,"list begins root12");uint[] offsets=new uint[count];for(int i=0;i<count;++i)offsets[i]=(uint)(12+4*(count-1-i));Offsets(a.ImportsData,offsets,big,"descending refs sorted by index");}
        }
        Test("empty Compare",Root(CompareList,"","<Compare TypeId='1'/>"),p);Test("comments/CDATA",Root(CompareList,"","<!--comment--><Compare>R<![CDATA[\\7]]></Compare>"),p);
        Reject("nested ref child",Root(CompareList,"","<Compare><Unknown/></Compare>"));Reject("reference attr",Root(CompareList,"","<Compare Value='R\\1'/>"));
        foreach(string type in new string[]{Base,Combat,Turret,CompareList})Reject("Tolerance only Distance",Root(type,"Tolerance='1'",""));
        Reject("unknown root",Root("Unknown","",""));Reject("wrong namespace","<TargetingBaseCompare xmlns='urn:q'/>");
        bool rejected=false;try{XmltargetingCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;
        XmlDocument wrong=Parse(Root(Base,"",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName=Distance;rejected=false;
        try{XmltargetingCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"mismatch TypeId");++rejections;
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
            Console.WriteLine("PASS Targeting fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

