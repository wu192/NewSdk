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
class XmlconnectionsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"ConnectionLineManager"};
    static readonly uint[] TypeIds={105587076u};
    static readonly int[] Sizes={32};
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
            Type type=typeof(XmlconnectionsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlconnectionsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    const string RootName="ConnectionLineManager";
    static readonly string[] Tags={"Float","Bool","Int","Texture"};
    static readonly uint[] Ids={3741900581u,2750958653u,2300057986u,2777716390u};
    static string Shader(string attrs,string constants){return "<FXShader "+attrs+">"+constants+"</FXShader>";}
    static string Constant(int type,string attrs,string values){return "<"+Tags[type]+" TypeId='"+Ids[type]+"' "+attrs+">"+values+"</"+Tags[type]+">";}
    static string Constants(string children){return "<Constants>"+children+"</Constants>";}
    static XmlDocument SetValue(int type,string value){XmlDocument d=Parse(Root(RootName,"",Shader("",Constants(Constant(type,"","<Value/>")))));d.GetElementsByTagName("Value",NS)[0].InnerText=value;return d;}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer absent=Test("ABI/absentShader",Root(RootName,"",""),p);Check(absent.InstanceData.Length==32,"root32 inlineMaterial28");for(int at=0;at<32;at+=4)Check(Word(absent.InstanceData,at,big)==0,"absent allzero");Offsets(absent.RelocationData,new uint[0],big,"absent no pointers");
        AssetBuffer empty=Test("ABI/emptyShader",Root(RootName,"",Shader("","")),p);Check(empty.InstanceData.Length==36&&Word(empty.InstanceData,12,big)==0&&Word(empty.InstanceData,16,big)==32,"present shader defaultTechniqueName allocNUL4");Offsets(empty.RelocationData,new uint[]{16},big,"emptyTechniqueName pointer");
        string constants=Constant(0,"Name='f'","<Value>0.5</Value><Value>-2</Value>")+Constant(1,"Name='b'","<Value>true</Value>")+Constant(2,"Name='i'","<Value>-7</Value>")+Constant(3,"Name='t'","<Value>T\\5</Value>");
        AssetBuffer f=Test("ABI/allFour",Root(RootName,"",Shader("TechniqueIndex='258'",Constants(constants))),p);Check(f.InstanceData.Length==144&&Word(f.InstanceData,20,big)==4&&Word(f.InstanceData,24,big)==36&&f.InstanceData[28]==2&&f.InstanceData[29]==0&&f.InstanceData[30]==0&&f.InstanceData[31]==0,"four constants list plus uint8 techniqueindex");uint[] pointers={52,84,104,124};for(int i=0;i<4;++i)Check(Word(f.InstanceData,36+4*i,big)==pointers[i]&&Word(f.InstanceData,(int)pointers[i],big)==Ids[i],"constant pointer/tag");Check(Word(f.InstanceData,56,big)==1&&Word(f.InstanceData,60,big)==80&&Word(f.InstanceData,64,big)==2&&Word(f.InstanceData,68,big)==72&&Word(f.InstanceData,72,big)==0x3f000000u&&Word(f.InstanceData,76,big)==0xc0000000u&&f.InstanceData[80]==102,"Float20 values allocation before Name");Check(f.InstanceData[96]==1&&f.InstanceData[97]==0&&f.InstanceData[98]==0&&f.InstanceData[99]==0,"Bool16 payloadbyte no swap");Check(Word(f.InstanceData,116,big)==unchecked((uint)-7)&&Word(f.InstanceData,136,big)==6,"Int16 signed/Texture16 strongref");Offsets(f.RelocationData,new uint[]{16,24,36,40,44,48,68,60,92,112,132},big,"ownerblock/target allocation order");Offsets(f.ImportsData,new uint[]{136},big,"Texture direct reference");
        AssetBuffer unknown=Test("ABI/unknownBase",Root(RootName,"",Shader("",Constants("<Unknown TypeId='1' Name='x'/><Bool/><Float/>"))),p);Check(unknown.InstanceData.Length==88&&Word(unknown.InstanceData,20,big)==3,"unknown or missing typeid fallback12");int list=(int)Word(unknown.InstanceData,24,big);for(int i=0;i<3;++i){int target=(int)Word(unknown.InstanceData,list+4*i,big);Check(Word(unknown.InstanceData,target,big)==0,"unknown base serializedtag0");}
        AssetBuffer strings=Test("ABI/materialStrings",Root(RootName,"",Shader("ShaderName='abc' TechniqueName='d' TechniqueIndex='-1'","")),p);Check(strings.InstanceData.Length==40&&Word(strings.InstanceData,4,big)==3&&Word(strings.InstanceData,8,big)==32&&Word(strings.InstanceData,12,big)==1&&Word(strings.InstanceData,16,big)==36&&strings.InstanceData[28]==255,"material two8byte strings and byteindex");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string attr in new string[]{"ShaderName","TechniqueName"})foreach(string v in new string[]{"","abc","ABC","a b","abc\0tail","\0abc","é","东京","😀",new string('x',4097)})TestDoc("materialstring/"+attr,Set(RootName,"FXShader",attr,v),p);foreach(string v in Numbers)TestDoc("techniqueindex",Set(RootName,"FXShader","TechniqueIndex",v),p);
        List<string> numbers=new List<string>(Numbers);numbers.AddRange(new string[]{"NaN","INF","-INF","0x1.8p2","1.000000059604644775390625","2d3","2D3","1\0tail","\0-1","é","东京"});foreach(int type in new int[]{0,2})foreach(string v in numbers)TestDoc("numericValue/"+Tags[type],SetValue(type,v),p);foreach(string v in new string[]{"true","false","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("boolValue",SetValue(1,v),p);foreach(string v in References)TestDoc("textureValue",SetValue(3,v),p);
        for(int type=0;type<4;++type){Test("known empty/"+Tags[type],Root(RootName,"",Shader("",Constants(Constant(type,"","")))),p);foreach(string v in new string[]{"","Name","MixedCase","abc\0tail","\0abc","é","东京","😀",new string('x',4097)}){XmlDocument d=Parse(Root(RootName,"",Shader("",Constants(Constant(type,"","")))));((XmlElement)d.GetElementsByTagName(Tags[type],NS)[0]).SetAttribute("Name",v);TestDoc("constantNames/"+Tags[type],d,p);}}
        foreach(string id in new string[]{"0","1","garbage","","-1","4294967295","4294967296","0xdf098b25","3741900581","2750958653","2300057986","2777716390","3741900581junk","3741900581\0tail"}){XmlDocument d=Parse(Root(RootName,"",Shader("",Constants("<Any Name='n'/>"))));((XmlElement)d.GetElementsByTagName("Any",NS)[0]).SetAttribute("TypeId",id);TestDoc("numeric dispatch TypeId",d,p);}
        for(int type=0;type<4;++type){string mismatched="<Any TypeId='"+Ids[type]+"' Name='n'><Value>1</Value></Any>";Test("dispatch independent elementName",Root(RootName,"",Shader("",Constants(mismatched))),p);}
        foreach(int count in new int[]{0,1,2,3,4,5,7,15,16,17,27,28,29,64,257,1024}){StringBuilder values=new StringBuilder();for(int i=0;i<count;++i)values.Append("<Value>0.5</Value>");AssetBuffer b=Test("floatValues growth",Root(RootName,"",Shader("",Constants(Constant(0,"",values.ToString())))),p);int list=(int)Word(b.InstanceData,24,big),target=(int)Word(b.InstanceData,list,big);Check(Word(b.InstanceData,target+12,big)==count&&b.InstanceData.Length==60+4*count,"float20 valuesunbounded4 even aboveXSD4");StringBuilder constants=new StringBuilder();for(int i=0;i<count;++i)constants.Append(Constant(i%4,"",""));AssetBuffer c=Test("polymorphiclist growth",Root(RootName,"",Shader("",Constants(constants.ToString()))),p);int total=36+4*count;for(int i=0;i<count;++i)total+=i%4==0?20:16;Check(c.InstanceData.Length==total&&Word(c.InstanceData,20,big)==count,"exact4byte pointerlist andtypedblocks");}
        Test("metadata",Root(RootName,"TypeId='1' inheritFrom='Ignored'",Shader("TypeId='1'","<!--c-->")),p);Reject("root unknownattr",Root(RootName,"Unknown='1'",""));Reject("root unknownchild",Root(RootName,"","<Unknown/>"));Reject("roottext",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified rootattr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));Reject("duplicate Shader",Root(RootName,"",Shader("","")+Shader("","")));Reject("duplicate Constants",Root(RootName,"",Shader("",Constants("")+Constants(""))));Reject("shader unknownattr",Set(RootName,"FXShader","Unknown","1").OuterXml);Reject("shader text",Root(RootName,"",Shader("","payload")));Reject("shader child",Root(RootName,"",Shader("","<Unknown/>")));Reject("constants attr",Root(RootName,"",Shader("","<Constants Unknown='1'/>")));Reject("constants text",Root(RootName,"",Shader("",Constants("payload"))));
        for(int type=0;type<4;++type){Reject("constant unknownattr",Root(RootName,"",Shader("",Constants(Constant(type,"Unknown='1'","")))));Reject("constant text",Root(RootName,"",Shader("",Constants(Constant(type,"","payload")))));Reject("constant child",Root(RootName,"",Shader("",Constants(Constant(type,"","<Unknown/>")))));Reject("Value attr",Root(RootName,"",Shader("",Constants(Constant(type,"","<Value Unknown='1'/>")))));Reject("Value child",Root(RootName,"",Shader("",Constants(Constant(type,"","<Value><Unknown/></Value>")))));if(type!=0)Reject("duplicate scalar Value",Root(RootName,"",Shader("",Constants(Constant(type,"","<Value/><Value/>")))));}
        Reject("unknownpoly payload",Root(RootName,"",Shader("",Constants("<Unknown TypeId='1'><Value>payload</Value></Unknown>"))));Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlconnectionsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlconnectionsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS connection shaders fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

