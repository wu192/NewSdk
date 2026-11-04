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
class XmlvisualsystemsOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Breeze="BreezeInfo",Model="ModelSettings",Particles="AutoParticleSystemSettings";
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] ParticleNames={"FireSmall","FireMedium","FireLarge","SmokeSmall","SmokeMedium","SmokeLarge","Aflame"};
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
            Type type=typeof(XmlvisualsystemsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlvisualsystemsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
        AssetBuffer breeze=Test("ABI/breeze",Root(Breeze,"LoadAfter='T\\9' Direction='1' Intensity='0.5' Lean='0.25' Randomness='0.125' BreezePeriod='1500ms'",""),p);
        uint[] b={0,10,0x3f800000u,0x3f000000u,0x3e800000u,0x3e000000u,0x3fc00001u};for(int i=0;i<b.Length;++i)Check(Word(breeze.InstanceData,4*i,big)==b[i],"breeze complete independent layout");Offsets(breeze.ImportsData,new uint[]{4},big,"breeze dependency import");
        AssetBuffer model=Test("ABI/model",Root(Model,"LoadAfter='T\\1' AnimationSharingCap='-17' AnimationSharingFrameTolerance='0.5' AnimationSharingDrasticThreshold='0.25' ForceModelsToFollowTimeOfDay='true' ForceModelsToFollowWeather='false'",""),p);
        Check(model.InstanceData.Length==24&&Word(model.InstanceData,4,big)==2&&Word(model.InstanceData,8,big)==unchecked((uint)-17)&&Word(model.InstanceData,12,big)==0x3f000000u&&Word(model.InstanceData,16,big)==0x3e800000u,"model root24/ref/int/floats");
        Check(model.InstanceData[20]==1&&model.InstanceData[21]==0&&model.InstanceData[22]==0&&model.InstanceData[23]==0,"model bool packed20/21/padding");
        AssetBuffer ps=Test("ABI/particles",Root(Particles,"ParticleScale='0.5' MaxParticleCount='-17'","<Aflame ParticlePrefix='XYZ' ParticleSystemId='abc' ParticleMax='5'/><FireSmall ParticlePrefix='AB' ParticleSystemId='ABC' ParticleMax='-2'/>"),p);
        Check(ps.InstanceData.Length==132&&Word(ps.InstanceData,4,big)==0x3f000000u&&Word(ps.InstanceData,8,big)==unchecked((uint)-17),"particle root124 plus aligned2strings");
        Check(Word(ps.InstanceData,12,big)==2&&Word(ps.InstanceData,16,big)==124&&Word(ps.InstanceData,108,big)==3&&Word(ps.InstanceData,112,big)==128,"inline16 offsets/strings marshal schema order independent XML order");
        Check(ps.InstanceData[124]==65&&ps.InstanceData[125]==66&&ps.InstanceData[126]==0&&ps.InstanceData[128]==88&&ps.InstanceData[129]==89&&ps.InstanceData[130]==90&&ps.InstanceData[131]==0,"string bytes/nul/alignment");
        Check(Word(ps.InstanceData,20,big)==Word(ps.InstanceData,116,big)&&Word(ps.InstanceData,20,big)!=0,"weak id case folded");
        Check(Word(ps.InstanceData,24,big)==unchecked((uint)-2)&&Word(ps.InstanceData,120,big)==5,"inline max offset12");Offsets(ps.RelocationData,new uint[]{16,112},big,"inline string pointers");Offsets(ps.ImportsData,new uint[0],big,"weak ids no imports");
        AssetBuffer empty=Test("ABI/empty-prefix",Root(Particles,"","<FireSmall ParticlePrefix=''/><FireMedium/>"),p);
        Check(empty.InstanceData.Length==128&&Word(empty.InstanceData,12,big)==0&&Word(empty.InstanceData,16,big)==124&&Word(empty.InstanceData,32,big)==0,"present empty string allocates; absent doesnot");Offsets(empty.RelocationData,new uint[]{16},big,"empty prefix pointer");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        string[] roots={Breeze,Model,Particles};int[] sizes={28,24,124};
        for(int i=0;i<roots.Length;++i){AssetBuffer a=Test("defaults/"+roots[i],Root(roots[i],"",""),p);Check(a.InstanceData.Length==sizes[i],"independent root size");Offsets(a.RelocationData,new uint[0],big,"default no relocation");Offsets(a.ImportsData,new uint[0],big,"default no imports");
            Test("metadata/"+roots[i],Root(roots[i],"TypeId='1' inheritFrom='Ignored'",""),p);
            Reject("unknownattr",Root(roots[i],"Unknown='1'",""));Reject("qualifiedattr",Root(roots[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("unknownchild",Root(roots[i],"","<Unknown/>"));Reject("roottext",Root(roots[i],"","payload"));
        }
        AssetBuffer defaults=Test("ABI/defaults",Root(Breeze,"",""),p);
        uint[] defaultsBits={0x3f860aa6u,0x3ce12ec7u,0x3c80ad04u,0x3e4ccccdu,0x41200000u};for(int i=0;i<5;++i)Check(Word(defaults.InstanceData,8+4*i,big)==defaultsBits[i],"breeze independent default floatbits");
        AssetBuffer particleDefault=Test("ABI/particle-default",Root(Particles,"",""),p);Check(Word(particleDefault.InstanceData,8,big)==2500&&Word(particleDefault.InstanceData,4,big)==0,"max2500/default particleScale0");
        foreach(string value in References)foreach(string root in new string[]{Breeze,Model})TestDoc("dependency/"+root+"/"+value.Length,Set(root,"","LoadAfter",value),p);
        foreach(string field in new string[]{"Direction","Intensity","Lean","Randomness"})foreach(string value in Numbers)TestDoc("breeze-float/"+field+"/"+value,Set(Breeze,"",field,value),p);
        List<string> times=new List<string>(Numbers);times.AddRange(new string[]{"1ms","3s","1500ms","1s2ms","1MS","1S","2d1ms","1\0ms","-17ms","16777217ms"});foreach(string value in times)TestDoc("time/"+value,Set(Breeze,"","BreezePeriod",value),p);
        foreach(string field in new string[]{"AnimationSharingCap","AnimationSharingFrameTolerance","AnimationSharingDrasticThreshold"})foreach(string value in Numbers)TestDoc("modelscalar/"+field+"/"+value,Set(Model,"",field,value),p);
        foreach(string field in new string[]{"ForceModelsToFollowTimeOfDay","ForceModelsToFollowWeather"})foreach(string value in new string[]{"true","false","True","TRUE","1","0","2","garbage","T","t","yes",""," true","false\0true","1\0tail"})TestDoc("bool/"+field+"/"+value,Set(Model,"",field,value),p);
        foreach(string field in new string[]{"ParticleScale","MaxParticleCount"})foreach(string value in Numbers)TestDoc("particlescalar/"+field+"/"+value,Set(Particles,"",field,value),p);
        foreach(string name in ParticleNames){
            Test("emptyinline/"+name,Root(Particles,"","<"+name+"/>"),p);
            foreach(string value in Numbers)TestDoc("particlemax/"+name+"/"+value,Set(Particles,name,"ParticleMax",value),p);
            foreach(string value in new string[]{"","A","ABC","abc","MiXeD","资源","😀","A\0tail","\0tail","A\\17"," A "}){TestDoc("prefix/"+name+"/"+value.Length,Set(Particles,name,"ParticlePrefix",value),p);TestDoc("weak/"+name+"/"+value.Length,Set(Particles,name,"ParticleSystemId",value),p);}
            foreach(int count in new int[]{0,1,2,3,4,5,15,16,17,27,28,29,64,257,1024,4096}){AssetBuffer b=TestDoc("stringgrowth/"+name+"/"+count,Set(Particles,name,"ParticlePrefix",new string('X',count)),p);int at=Array.IndexOf(ParticleNames,name);Check(b.InstanceData.Length==124+((count+4)/4)*4&&Word(b.InstanceData,12+16*at,big)==count&&Word(b.InstanceData,16+16*at,big)==124,"prefix exact growth/length/offset");}
            TestDoc("metadata/"+name,Set(Particles,name,"TypeId","1"),p);Reject("unknown inlineattr",Set(Particles,name,"Unknown","1").OuterXml);Reject("duplicate inline",Root(Particles,"","<"+name+"/><"+name+"/>"));Reject("inline text",Root(Particles,"","<"+name+">payload</"+name+">"));Reject("inline child",Root(Particles,"","<"+name+"><Unknown/></"+name+">"));
        }
        // All seven inline settings simultaneously, reverse XML input order, unequal string lengths.
        StringBuilder all=new StringBuilder();for(int i=6;i>=0;--i)all.Append("<"+ParticleNames[i]+" ParticlePrefix='"+new string('A',i+1)+"' ParticleSystemId='abc' ParticleMax='"+i+"'/>");Test("allinline",Root(Particles,"",all.ToString()),p);
        Reject("foreignchild",Root(Particles,"","<FireSmall xmlns='urn:q'/>"));Reject("wrongnamespace","<BreezeInfo xmlns='urn:q'/>");Reject("unknownroot",Root("Unknown","",""));
        bool nullInput=false;try{XmlvisualsystemsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration");++rejections;
        XmlDocument wrong=Parse(Root(Breeze,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Model;bool mismatch=false;try{XmlvisualsystemsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/TypeId mismatch");++rejections;
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
            Console.WriteLine("PASS Visual systems fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

