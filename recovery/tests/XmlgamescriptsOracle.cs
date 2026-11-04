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
class XmlgamescriptsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"GameScriptList"};
    static readonly uint[] TypeIds={3737094024u};
    static readonly int[] Sizes={8};
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
            Type type=typeof(XmlgamescriptsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlgamescriptsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    const string RootName="GameScriptList",ActionPath="ScriptSet/Script/Action";
    static string SetXml(string children){return "<ScriptSet>"+children+"</ScriptSet>";}
    static string ActionXml(string parameters){return SetXml("<Script><Action><ParameterList>"+parameters+"</ParameterList></Action></Script>");}
    static XmlDocument ParameterDoc(uint id,string attr,string value){XmlDocument d=Parse(Root(RootName,"",ActionXml("<Any/>")));XmlElement param=(XmlElement)d.GetElementsByTagName("Any",NS)[0];param.SetAttribute("TypeId",id.ToString());if(attr!=null)param.SetAttribute(attr,value);return d;}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(RootName,"",""),p);Check(d.InstanceData.Length==8&&Word(d.InstanceData,4,big)==0,"root8 absentset");AssetBuffer empty=Test("ABI/emptyset",Root(RootName,"",SetXml("")),p);Check(empty.InstanceData.Length==28&&Word(empty.InstanceData,4,big)==8,"present group20");for(int at=8;at<28;at+=4)Check(Word(empty.InstanceData,at,big)==0,"group defaults0");
        string set="<ScriptSet id='MixedCase'><ScriptGroup id='MIXEDCASE'/><Script id='mixedcase'><Action Type='MixedCase'><ParameterList><ObjectType TypeId='58964679' Object='O\\9'/><ObjectTypeList TypeId='524038440'><Object>O\\1</Object><Object>O\\5</Object></ObjectTypeList></ParameterList></Action></Script></ScriptSet>";AssetBuffer f=Test("ABI/full",Root(RootName,"",set),p);Check(f.InstanceData.Length==108&&Word(f.InstanceData,4,big)==8,"fullnested bytes108");Check(Word(f.InstanceData,12,big)==1&&Word(f.InstanceData,16,big)==28&&Word(f.InstanceData,20,big)==1&&Word(f.InstanceData,24,big)==88,"Group scripts allocatedfirst independent ofXML order");Check(Word(f.InstanceData,8,big)==Word(f.InstanceData,28,big)&&Word(f.InstanceData,8,big)==Word(f.InstanceData,88,big)&&Word(f.InstanceData,8,big)!=0,"group/script ids lowercaseweak");Check(Word(f.InstanceData,32,big)==1&&Word(f.InstanceData,36,big)==40&&Word(f.InstanceData,44,big)==2&&Word(f.InstanceData,48,big)==52,"script12/action12/parameterlist4");Check(Word(f.InstanceData,40,big)!=Word(f.InstanceData,8,big),"Action Type rawcase hash");Check(Word(f.InstanceData,52,big)==60&&Word(f.InstanceData,56,big)==68&&Word(f.InstanceData,60,big)==58964679u&&Word(f.InstanceData,64,big)==10&&Word(f.InstanceData,68,big)==524038440u&&Word(f.InstanceData,72,big)==2&&Word(f.InstanceData,76,big)==80&&Word(f.InstanceData,80,big)==2&&Word(f.InstanceData,84,big)==6,"typedparameter8/12 and objectreflist");Offsets(f.RelocationData,new uint[]{4,16,24,36,48,52,56,76},big,"ownerblock hierarchical pointer order");Offsets(f.ImportsData,new uint[]{64,80,84},big,"parameter ownerblock imports");
        AssetBuffer u=Test("ABI/unknown",Root(RootName,"",ActionXml("<Any TypeId='1'/><ObjectType/>")),p);Check(u.InstanceData.Length==68,"two unknown4 pluspointerlist8");int list=(int)Word(u.InstanceData,48,big);for(int i=0;i<2;++i)Check(Word(u.InstanceData,(int)Word(u.InstanceData,list+4*i,big),big)==0,"unknownbase4 serialtag0");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;List<string> texts=new List<string>(References);texts.AddRange(new string[]{"MixedCase","MIXEDCASE","mixedcase","ABC\0tail","\0ABC","é","东京","😀",new string('x',4097)});foreach(string path in new string[]{"ScriptSet","ScriptSet/Script","ScriptSet/ScriptGroup"})foreach(string v in texts)TestDoc("weakid/"+path,Set(RootName,path,"id",v),p);foreach(string v in texts)TestDoc("Action rawhash",Set(RootName,ActionPath,"Type",v),p);
        foreach(string v in References){TestDoc("ObjectType ref",ParameterDoc(58964679u,"Object",v),p);XmlDocument d=ParameterDoc(524038440u,null,null);XmlElement obj=d.CreateElement("Object",NS);obj.InnerText=v;d.GetElementsByTagName("Any",NS)[0].AppendChild(obj);TestDoc("ObjectTypeList ref",d,p);}
        foreach(string id in new string[]{"0","1","garbage","","-1","4294967295","4294967296","58964679","524038440","58964679junk","524038440\0tail"}){XmlDocument d=ParameterDoc(0,null,null);((XmlElement)d.GetElementsByTagName("Any",NS)[0]).SetAttribute("TypeId",id);TestDoc("numeric type dispatch",d,p);}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder scripts=new StringBuilder(),groups=new StringBuilder(),actions=new StringBuilder(),parameters=new StringBuilder(),objects=new StringBuilder();for(int i=0;i<count;++i){scripts.Append("<Script/>");groups.Append("<ScriptGroup/>");actions.Append("<Action/>");parameters.Append(i%2==0?"<Any TypeId='58964679'/>":"<Any TypeId='524038440'/>");objects.Append("<Object>O\\"+(count-i)+"</Object>");}
            AssetBuffer s=Test("scriptgrowth",Root(RootName,"",SetXml(scripts.ToString())),p);Check(s.InstanceData.Length==28+12*count&&Word(s.InstanceData,12,big)==count,"script12 growth");AssetBuffer g=Test("groupgrowth",Root(RootName,"",SetXml(groups.ToString())),p);Check(g.InstanceData.Length==28+20*count&&Word(g.InstanceData,20,big)==count,"group20 growth");AssetBuffer a=Test("actiongrowth",Root(RootName,"",SetXml("<Script>"+actions+"</Script>")),p);Check(a.InstanceData.Length==40+12*count&&Word(a.InstanceData,32,big)==count,"action12 growth");AssetBuffer q=Test("parameter growth",Root(RootName,"",ActionXml(parameters.ToString())),p);int expected=52+4*count;for(int i=0;i<count;++i)expected+=i%2==0?8:12;Check(q.InstanceData.Length==expected&&Word(q.InstanceData,44,big)==count,"pointerlist andboth typedblocks");AssetBuffer o=Test("objectlist growth",Root(RootName,"",ActionXml("<Any TypeId='524038440'>"+objects+"</Any>")),p);Check(o.InstanceData.Length==68+4*count&&Word(o.InstanceData,60,big)==count,"objectrefs4growth");}
        foreach(int depth in new int[]{0,1,2,3,7,15,24}){string groups="";for(int i=0;i<depth;++i)groups="<ScriptGroup id='Group"+i+"'>"+groups+"</ScriptGroup>";AssetBuffer b=Test("group recursion/"+depth,Root(RootName,"",SetXml(groups)),p);Check(b.InstanceData.Length==28+20*depth,"recursive groups no missingblocks");}
        Test("metadata",Root(RootName,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown attr",Root(RootName,"Unknown='1'",""));Reject("unknown child",Root(RootName,"","<Unknown/>"));Reject("text",Root(RootName,"","payload"));Reject("foreign root",Root(RootName,"","").Replace(NS,"urn:wrong"));Reject("qualified attr",Root(RootName,"xmlns:q='urn:q' q:Unknown='1'",""));Reject("duplicate set",Root(RootName,"",SetXml("")+SetXml("")));Reject("duplicate ParameterList",Root(RootName,"",SetXml("<Script><Action><ParameterList/><ParameterList/></Action></Script>")));
        foreach(string path in new string[]{"ScriptSet","ScriptSet/Script","ScriptSet/ScriptGroup",ActionPath,ActionPath+"/ParameterList"}){Reject("nestedunknownattr",Set(RootName,path,"Unknown","1").OuterXml);XmlDocument d=Set(RootName,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string tag in path.Split('/'))n=n.FirstChild;n.InnerText="payload";Reject("nestedtext",d.OuterXml);}
        foreach(uint id in new uint[]{0u,58964679u,524038440u}){Reject("paramunknownattr",ParameterDoc(id,"Unknown","1").OuterXml);XmlDocument d=ParameterDoc(id,null,null);d.GetElementsByTagName("Any",NS)[0].InnerText="payload";Reject("paramtext",d.OuterXml);}Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlgamescriptsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(RootName,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlgamescriptsCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS game scripts fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

