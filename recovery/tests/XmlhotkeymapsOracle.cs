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
class XmlhotkeymapsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"GeneralHotKeySlots","DefaultHotKeys","DebugHotKeys","MappableKey"};
    static readonly uint[] TypeIds={417176643u,3741098742u,3359870461u,1350608344u};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7","MIXEDCase"};
    static readonly string[] ModifierValues={"CTRL","ALT","SHIFT","UNKNOWN","ctrl","alt","shift",""," ","+CTRL","-CTRL","CTRL -CTRL","-CTRL -CTRL","CTRL ALT SHIFT","CTRL\tALT\r\nSHIFT","ALL","all","-ALL","ALL -CTRL","-ALL CTRL","NONE","+","-","CTRL CTRL","ALT -UNKNOWN","CTRL,ALT","CTRL|ALT"};
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
        uint previous=0;for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at+4<=instance.Length,label+" field bounds");Check(i==0||at>previous,label+" sorted unique offsets");previous=at;if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlhotkeymapsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlhotkeymapsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static string Mapping(string attrs){return "<Map><HotKey "+attrs+"/></Map>";}
    static void Abi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer general=Test("ABI/general",Root("GeneralHotKeySlots","TakeScreenShot='Slot\\7' ReloadRapidIterationFeature='Slot\\11'",""),p);
        Check(general.InstanceData.Length==12&&Word(general.InstanceData,4,big)==8&&Word(general.InstanceData,8,big)==12,"General two references and indices");
        Check(general.RelocationData.Length==0&&general.ImportsData.Length==12&&Word(general.ImportsData,0,big)==4&&Word(general.ImportsData,4,big)==8,"General exact import offsets");
        foreach(string type in new string[]{"DefaultHotKeys","DebugHotKeys"}) {
            AssetBuffer map=Test("ABI/map/"+type,Root(type,"","<Map><HotKey Slot='S\\7' Key='K\\11' Modifiers='CTRL SHIFT'/><HotKey Modifiers='UNKNOWN'/><HotKey Modifiers='-CTRL -CTRL'/><HotKey Modifiers='-ALL CTRL'/></Map>"),p);
            Check(map.InstanceData.Length==60&&Word(map.InstanceData,4,big)==4&&Word(map.InstanceData,8,big)==12,"Hotkey map list root and stride");
            Check(Word(map.InstanceData,12,big)==8&&Word(map.InstanceData,16,big)==12&&Word(map.InstanceData,20,big)==5,"HotkeyDef refs and mask offsets");
            Check(Word(map.InstanceData,32,big)==2&&Word(map.InstanceData,44,big)==0&&Word(map.InstanceData,56,big)==UInt32.MaxValue,"Hotkey unknown fallback / XOR / signed ALL early termination");
            Check(map.RelocationData.Length==8&&Word(map.RelocationData,0,big)==8,"Map relocation pointer field");
            Check(map.ImportsData.Length==12&&Word(map.ImportsData,0,big)==12&&Word(map.ImportsData,4,big)==16,"Map import fields");
        }
        AssetBuffer absent=Test("ABI/key-absent",Root("MappableKey","",""),p);
        Check(absent.InstanceData.Length==12&&absent.RelocationData.Length==0,"MappableKey absent string zero");
        AssetBuffer empty=Test("ABI/key-empty",Root("MappableKey","KeyDef=''",""),p);
        Check(empty.InstanceData.Length==16&&Word(empty.InstanceData,4,big)==0&&Word(empty.InstanceData,8,big)==12&&empty.InstanceData[12]==0,"MappableKey empty-present allocates NUL");
        Check(empty.RelocationData.Length==8&&Word(empty.RelocationData,0,big)==8&&empty.ImportsData.Length==0,"MappableKey relocation");
        AssetBuffer key=Test("ABI/key-text",Root("MappableKey","KeyDef='KEY_A'",""),p);
        Check(key.InstanceData.Length==20&&Word(key.InstanceData,4,big)==5&&Word(key.InstanceData,8,big)==12,"MappableKey length and allocation alignment");
        byte[] ascii=Encoding.ASCII.GetBytes("KEY_A\0");for(int i=0;i<ascii.Length;++i)Check(key.InstanceData[12+i]==ascii[i],"MappableKey exact chars");
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i) {
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==12,"all four root exact sizes");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown root attribute",Root(Types[i],"Unknown='1'",""));
            Reject("qualified root attribute",Root(Types[i],"xmlns:q='urn:other' q:TypeId='1'",""));
            Reject("root unknown child",Root(Types[i],"","<Unknown/>"));
            Reject("root foreign child",Root(Types[i],"","<Map xmlns='urn:other'/>"));
            Reject("root text",Root(Types[i],"","payload"));
        }
        foreach(string name in new string[]{"TakeScreenShot","ReloadRapidIterationFeature"})foreach(string v in Refs)
            Test("general/"+name+"/"+v,Root("GeneralHotKeySlots",name+"='"+Escape(v)+"'",""),p);
        foreach(string type in new string[]{"DefaultHotKeys","DebugHotKeys"}) {
            Test("empty-map/"+type,Root(type,"","<Map/>"),p);
            Test("empty-hotkey/"+type,Root(type,"",Mapping("")),p);
            Test("metadata-map/"+type,Root(type,"","<Map TypeId='1'><HotKey TypeId='1'/></Map>"),p);
            foreach(string name in new string[]{"Slot","Key"})foreach(string v in Refs)
                Test("map-reference/"+type+"/"+name+"/"+v,Root(type,"",Mapping(name+"='"+Escape(v)+"'")),p);
            foreach(string a in ModifierValues) {
                Test("modifiers/"+type+"/"+a,Root(type,"",Mapping("Modifiers='"+Escape(a)+"'")),p);
                foreach(string b in ModifierValues)
                    Test("modifier-sequence/"+type+"/"+a+"/"+b,Root(type,"",Mapping("Modifiers='"+Escape(a+" "+b)+"'")),p);
            }
            foreach(int count in new int[]{0,1,2,3,27,28,29,64,257}) {
                StringBuilder entries=new StringBuilder();for(int i=0;i<count;++i)entries.Append("<HotKey Slot='S\\"+(2*i)+"' Key='K\\"+(2*i+1)+"' Modifiers='CTRL ALT -SHIFT'/>");
                AssetBuffer many=Test("list/"+type+"/"+count,Root(type,"","<Map>"+entries+"</Map>"),p);bool big=p!=TargetPlatform.Win32;
                Check(many.InstanceData.Length==12+12*count&&Word(many.InstanceData,4,big)==count,"many hotkeys independent size/count");
                Check(many.ImportsData.Length==(count==0?0:4*(2*count+1)),"many hotkeys bookmark growth");
            }
            foreach(string value in new string[]{"CTRL\0SHIFT","ALL\0-CTRL","CTRL\vALT\fSHIFT"}) {
                XmlDocument d=Parse(Root(type,"",Mapping("")));((XmlElement)d.DocumentElement.FirstChild.FirstChild).SetAttribute("Modifiers",value);TestDoc("modifier-native-separators/"+type,d,p);
            }
            Reject("duplicate Map",Root(type,"","<Map/><Map/>"));
            Reject("Map unknown attribute",Root(type,"","<Map Unknown='1'/>"));
            Reject("Map unknown child",Root(type,"","<Map><Unknown/></Map>"));
            Reject("HotKey unknown attribute",Root(type,"",Mapping("Unknown='1'")));
            Reject("HotKey qualified attribute",Root(type,"",Mapping("xmlns:q='urn:other' q:Modifiers='CTRL'")));
            Reject("HotKey child",Root(type,"","<Map><HotKey><Key/></HotKey></Map>"));
            Reject("HotKey text",Root(type,"","<Map><HotKey>payload</HotKey></Map>"));
        }
        foreach(string value in new string[]{"","a","abc","abcd","abcde","KEY_A","A&B<key>","é","资源","😀","e\u0301","a\0tail","\0leading","资源\0尾部",new string('k',1024)}) {
            XmlDocument d=Parse(Root("MappableKey","",""));d.DocumentElement.SetAttribute("KeyDef",value);TestDoc("KeyDef-string/"+value.Length,d,p);
        }
        foreach(string value in new string[]{"Name\0\\7","Name\\7\0tail","资源\\7","Name\\2147483647","Name\\-2147483648","Name\\99999999999999999999"}) {
            XmlDocument general=Parse(Root("GeneralHotKeySlots","",""));general.DocumentElement.SetAttribute("TakeScreenShot",value);general.DocumentElement.SetAttribute("ReloadRapidIterationFeature",value);TestDoc("general/native-reference",general,p);
            foreach(string type in new string[]{"DefaultHotKeys","DebugHotKeys"}) {
                XmlDocument map=Parse(Root(type,"",Mapping("")));XmlElement at=(XmlElement)map.DocumentElement.FirstChild.FirstChild;at.SetAttribute("Slot",value);at.SetAttribute("Key",value);TestDoc("map/native-reference/"+type,map,p);
            }
        }
        Reject("unknown root",Root("Unknown","",""));
        XmlDocument wrong=Parse(Root("DefaultHotKeys","",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName="MappableKey";
        bool mismatch=false;try{XmlhotkeymapsCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch rejected");++rejections;
        bool nullInput=false;try{XmlhotkeymapsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
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
            Console.WriteLine("PASS Hotkeymaps fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}
