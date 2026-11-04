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
class XmlterrainOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"TerrainAssetCollection","TerrainTextureAtlasRuntime"};
    static readonly uint[] TypeIds={582251659u,3878948534u};
    static readonly int[] Sizes={12,28};
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
            Type type=typeof(XmlterrainCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlterrainCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Classes={"Cliff","Dirt","Grass","Misc","Mud","Road","Rock","Sand","Shrub","Snow","UNSPECIFIED"};
    static readonly int[] ClassValues={3,2,4,1,7,6,5,8,9,10,0};
    const string Assets="TerrainAssetCollection",Atlas="TerrainTextureAtlasRuntime";
    static uint Half(byte[] b,int at,bool big){return big?(uint)((b[at]<<8)|b[at+1]):(uint)(b[at]|(b[at+1]<<8));}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;foreach(string root in Types){AssetBuffer d=Test("ABI/default",Root(root,"",""),p);Check(d.InstanceData.Length==(root==Assets?12:28),"exactrootsize");foreach(byte b in d.InstanceData)Check(b==0,"default all0");}
        AssetBuffer a=Test("ABI/fullAsset",Root(Assets,"","<TerrainAsset id='a' Texture='b' BumpTexture='c' Class='Sand' WBFolder='d'/>"),p);Check(a.InstanceData.Length==64&&Word(a.InstanceData,4,big)==1&&Word(a.InstanceData,8,big)==12&&Word(a.InstanceData,36,big)==8,"36byteasset Class@24");int[] slots={12,20,28,40};for(int i=0;i<4;++i)Check(Word(a.InstanceData,slots[i],big)==1&&Word(a.InstanceData,slots[i]+4,big)==48+4*i&&a.InstanceData[48+4*i]==97+i&&a.InstanceData[49+4*i]==0,"four strings exactslots/order");Offsets(a.RelocationData,new uint[]{8,16,24,32,44},big,"root/list stringpointers");Offsets(a.ImportsData,new uint[0],big,"Texture names are strings notrefs");
        AssetBuffer t=Test("ABI/tile",Root(Atlas,"BaseTextureAtlas='ignored' NormalTextureAtlas='also ignored'","<Tile TextureID='0x11223344' UpperLeftX='0x1234' UpperLeftY='0x5678' BottomRightX='0x9abc' BottomRightY='0xdef0'/>"),p);Check(t.InstanceData.Length==40&&Word(t.InstanceData,4,big)==1&&Word(t.InstanceData,8,big)==28&&Word(t.InstanceData,28,big)==0x11223344u,"root28/tile12");uint[] halves={0x1234,0x5678,0x9abc,0xdef0};for(int i=0;i<4;++i)Check(Half(t.InstanceData,32+2*i,big)==halves[i],"uint16 independently endian");for(int at=12;at<28;at+=4)Check(Word(t.InstanceData,at,big)==0,"twoatlasdata fields ignored by XmlHandler");Offsets(t.RelocationData,new uint[]{8},big,"tilepointer");Offsets(t.ImportsData,new uint[0],big,"no atlasfile imports");
        AssetBuffer e=Test("ABI/empty strings",Root(Assets,"","<TerrainAsset id='' Texture='' BumpTexture='' WBFolder=''/>"),p);Check(e.InstanceData.Length==64,"four empty strings eachalloc4");for(int i=0;i<4;++i)Check(Word(e.InstanceData,slots[i],big)==0&&Word(e.InstanceData,slots[i]+4,big)==48+4*i,"empty strings vsabsent");
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        for(int i=0;i<Classes.Length;++i)foreach(string v in new string[]{Classes[i],Classes[i].ToLowerInvariant(),Classes[i]+"\0tail"," "+Classes[i],Classes[i]+" "}){AssetBuffer b=TestDoc("terrain class",Set(Assets,"TerrainAsset","Class",v),p);Check(Word(b.InstanceData,36,big)==(uint)(v==Classes[i]||v==Classes[i]+"\0tail"?ClassValues[i]:3),"independentclass/fallback3");}foreach(string v in new string[]{"","UNKNOWN","0","-1","\0Grass"})TestDoc("class boundary",Set(Assets,"TerrainAsset","Class",v),p);
        List<string> text=new List<string>(References);text.AddRange(new string[]{"MixedCase","a b","ABC\0tail","\0ABC","é","东京","😀",new string('x',4097)});foreach(string attr in new string[]{"id","Texture","BumpTexture","WBFolder"})foreach(string v in text)TestDoc("asset strings/"+attr,Set(Assets,"TerrainAsset",attr,v),p);foreach(string attr in new string[]{"BaseTextureAtlas","NormalTextureAtlas"})foreach(string v in text){AssetBuffer b=TestDoc("ignored atlasdata/"+attr,Set(Atlas,"",attr,v),p);Check(b.InstanceData.Length==28,"ignored atlas attr noalloc");foreach(byte x in b.InstanceData)Check(x==0,"ignored atlas attr all0");}
        List<string> numbers=new List<string>(Numbers);numbers.AddRange(new string[]{"65535","65536","65537","-65535","-65536","0x12345678","1\0tail","\0-1"});foreach(string attr in new string[]{"TextureID","UpperLeftX","UpperLeftY","BottomRightX","BottomRightY"})foreach(string v in numbers)TestDoc("tileinteger/"+attr,Set(Atlas,"Tile",attr,v),p);
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){StringBuilder assets=new StringBuilder(),tiles=new StringBuilder();for(int i=0;i<count;++i){assets.Append("<TerrainAsset/>");tiles.Append("<Tile TextureID='"+i+"' UpperLeftX='65537' UpperLeftY='-1' BottomRightX='256' BottomRightY='32768'/>");}AssetBuffer a=Test("assetgrowth",Root(Assets,"",assets.ToString()),p),t=Test("tilegrowth",Root(Atlas,"",tiles.ToString()),p);Check(a.InstanceData.Length==12+36*count&&Word(a.InstanceData,4,big)==count,"asset36 exactgrowth");Check(t.InstanceData.Length==28+12*count&&Word(t.InstanceData,4,big)==count,"tile12 exactgrowth");for(int i=0;i<count;++i)Check(Half(t.InstanceData,32+12*i,big)==1&&Half(t.InstanceData,34+12*i,big)==65535,"uint16 truncation endian");}
        for(int mask=0;mask<16;++mask){XmlDocument d=Parse(Root(Assets,"","<TerrainAsset/>"));string[] names={"id","Texture","BumpTexture","WBFolder"};int count=0;for(int i=0;i<4;++i)if((mask&(1<<i))!=0){((XmlElement)d.DocumentElement.FirstChild).SetAttribute(names[i],"");++count;}AssetBuffer b=TestDoc("allstring presence masks",d,p);Check(b.InstanceData.Length==48+4*count,"presentempty allocation exact");}
        foreach(string root in Types){Test("metadata",Root(root,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown attr",Root(root,"Unknown='1'",""));Reject("unknown child",Root(root,"","<Unknown/>"));Reject("text",Root(root,"","payload"));Reject("foreign root",Root(root,"","").Replace(NS,"urn:wrong"));Reject("qualified root",Root(root,"xmlns:q='urn:q' q:Unknown='1'",""));string tag=root==Assets?"TerrainAsset":"Tile";TestDoc("itemmetadata",Set(root,tag,"TypeId","1"),p);Reject("itemattr",Set(root,tag,"Unknown","1").OuterXml);Reject("itemtext",Root(root,"","<"+tag+">payload</"+tag+">"));Reject("itemchild",Root(root,"","<"+tag+"><Unknown/></"+tag+">"));Reject("foreignitem",Root(root,"","<"+tag+" xmlns='urn:wrong'/>"));}
        Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlterrainCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"nulldeclaration");++rejections;XmlDocument wrong=Parse(Root(Assets,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Atlas;rejected=false;try{XmlterrainCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS terrain fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

