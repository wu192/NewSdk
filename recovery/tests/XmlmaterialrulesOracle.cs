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
class XmlmaterialrulesOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Road="Road",Shader="ShaderOverride",Rule="ShaderOverrideRule";
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
            Type type=typeof(XmlmaterialrulesCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;if(d.DocumentElement.LocalName!=Rule)Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlmaterialrulesCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
        AssetBuffer road=Test("ABI/road",Root(Road,"Texture='T\\9' NormalTexture='T\\1' RoadWidth='0.5' RoadWidthInTexture='0.25' Priority='0.125'",""),p);
        uint[] rb={0,10,2,0x3f000000u,0x3e800000u,0x3e000000u};for(int i=0;i<rb.Length;++i)Check(Word(road.InstanceData,4*i,big)==rb[i],"road independent wholelayout");Offsets(road.ImportsData,new uint[]{8,4},big,"road refs sortedindex");
        AssetBuffer rule=Test("ABI/rule",Root(Rule,"IfOriginalShaderIs='' ReplaceShaderName='' ReplaceTechniqueName=''",""),p);
        Check(rule.InstanceData.Length==24&&Word(rule.InstanceData,0,big)==16&&Word(rule.InstanceData,4,big)==0&&Word(rule.InstanceData,8,big)==0&&Word(rule.InstanceData,12,big)==20&&Word(rule.InstanceData,16,big)==0&&Word(rule.InstanceData,20,big)==0,"rule16 presentempty nullableweak4/string4 allocations; firstword ispointer");
        Offsets(rule.RelocationData,new uint[]{0,12},big,"standalone rule pointers");Offsets(rule.ImportsData,new uint[0],big,"weak shaderids no import");
        AssetBuffer shader=Test("ABI/shader",Root(Shader,"Priority='17'","<Rule IfOriginalShaderIs='ABC' ReplaceShaderName='abc' ReplaceTechniqueName='AB'/><Rule IfOriginalShaderIs='' ReplaceTechniqueName='XYZ'/>"),p);
        Check(shader.InstanceData.Length==64&&Word(shader.InstanceData,4,big)==17&&Word(shader.InstanceData,8,big)==2&&Word(shader.InstanceData,12,big)==16,"shader root16/2rules16 plus4childblocks");
        Check(Word(shader.InstanceData,16,big)==48&&Word(shader.InstanceData,24,big)==2&&Word(shader.InstanceData,28,big)==52&&Word(shader.InstanceData,32,big)==56&&Word(shader.InstanceData,40,big)==3&&Word(shader.InstanceData,44,big)==60,"list items nullable/string offsets allocation order");
        Check(Word(shader.InstanceData,48,big)==Word(shader.InstanceData,20,big)&&Word(shader.InstanceData,20,big)!=0&&Word(shader.InstanceData,56,big)==0,"independent casefold matching/present emptyweak");
        Check(shader.InstanceData[52]==65&&shader.InstanceData[53]==66&&shader.InstanceData[54]==0&&shader.InstanceData[60]==88&&shader.InstanceData[62]==90&&shader.InstanceData[63]==0,"technique bytes and nul");
        Offsets(shader.RelocationData,new uint[]{12,16,28,32,44},big,"nested ownerblock/targetblock pointers");Offsets(shader.ImportsData,new uint[0],big,"all weak references no imports");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        string[] roots={Road,Shader,Rule};int[] sizes={24,16,16};
        for(int i=0;i<roots.Length;++i){AssetBuffer a=Test("defaults/"+roots[i],Root(roots[i],"",""),p);Check(a.InstanceData.Length==sizes[i],"independent rootsize");Offsets(a.RelocationData,new uint[0],big,"defaults no reloc");Offsets(a.ImportsData,new uint[0],big,"defaults no imports");
            Test("metadata/"+roots[i],Root(roots[i],"TypeId='1' inheritFrom='Ignored'",""),p);Reject("unknownattr",Root(roots[i],"Unknown='1'",""));Reject("qualifiedattr",Root(roots[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknownchild",Root(roots[i],"","<Unknown/>"));Reject("roottext",Root(roots[i],"","payload"));
        }
        AssetBuffer def=Test("ABI/default-priority",Root(Shader,"",""),p);Check(Word(def.InstanceData,4,big)==1,"Priority unsigned default1");
        foreach(string field in new string[]{"Texture","NormalTexture"})foreach(string value in References)TestDoc("ref/"+field+"/"+value.Length,Set(Road,"",field,value),p);
        foreach(string field in new string[]{"RoadWidth","RoadWidthInTexture","Priority"})foreach(string value in Numbers)TestDoc("roadfloat/"+field+"/"+value,Set(Road,"",field,value),p);
        List<string> nums=new List<string>(Numbers);nums.AddRange(new string[]{"1\0tail","--1","+ 1","-0xffffffff","-4294967296","0x80000000","99999999999999999999999"});foreach(string value in nums)TestDoc("unsigned-priority/"+value,Set(Shader,"","Priority",value),p);
        List<string> strings=new List<string>(References);strings.AddRange(new string[]{"ABC","abc","MiXeD","资源","😀","A\0tail","\0tail"," A ","a\tb","a\nb"});
        foreach(string root in new string[]{Rule,Shader})foreach(string field in new string[]{"IfOriginalShaderIs","ReplaceShaderName","ReplaceTechniqueName"})foreach(string value in strings)TestDoc("shaderfield/"+root+"/"+field+"/"+value.Length,Set(root,root==Shader?"Rule":"",field,value),p);
        foreach(int count in new int[]{0,1,2,3,4,5,15,16,17,27,28,29,64,257,1024,65535}){
            AssetBuffer a=TestDoc("stringgrowth/"+count,Set(Rule,"","ReplaceTechniqueName",new string('X',count)),p);Check(a.InstanceData.Length==16+((count+4)/4)*4&&Word(a.InstanceData,8,big)==count&&Word(a.InstanceData,12,big)==16,"technique count/terminator/alignment");
        }
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            StringBuilder b=new StringBuilder();for(int i=0;i<count;++i)b.Append("<Rule IfOriginalShaderIs='' ReplaceTechniqueName='XYZ'/>");AssetBuffer a=Test("listgrowth/"+count,Root(Shader,"",b.ToString()),p);
            Check(a.InstanceData.Length==16+24*count&&Word(a.InstanceData,8,big)==count,"list16stride/nullable4/string4 all allocations");
            if(count>0){Check(Word(a.InstanceData,12,big)==16,"listtarget16");uint[] offsets=new uint[1+2*count];offsets[0]=12;for(int i=0;i<count;++i){offsets[1+2*i]=(uint)(16+16*i);offsets[2+2*i]=(uint)(28+16*i);Check(Word(a.InstanceData,16+16*i,big)==16+16*count+8*i&&Word(a.InstanceData,28+16*i,big)==20+16*count+8*i,"perrule schema allocation order");}Offsets(a.RelocationData,offsets,big,"nested growth pointerordering");}
        }
        Test("nestedmetadata",Root(Shader,"","<Rule TypeId='1'/>"),p);Reject("nested unknownattr",Root(Shader,"","<Rule Unknown='1'/>"));Reject("nested qualifiedattr",Root(Shader,"","<Rule xmlns:q='urn:q' q:TypeId='1'/>"));Reject("nestedchild",Root(Shader,"","<Rule><Unknown/></Rule>"));Reject("nestedtext",Root(Shader,"","<Rule>payload</Rule>"));Reject("foreignchild",Root(Shader,"","<Rule xmlns='urn:q'/>"));Reject("wrongnamespace","<Road xmlns='urn:q'/>");Reject("unknownroot",Root("Unknown","",""));
        bool nullInput=false;try{XmlmaterialrulesCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration");++rejections;
        XmlDocument wrong=Parse(Root(Road,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Rule;bool mismatch=false;try{XmlmaterialrulesCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/TypeId mismatch");++rejections;
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
            Console.WriteLine("PASS Material rules fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

