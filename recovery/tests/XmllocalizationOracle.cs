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
class XmllocalizationOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Language="Language",Header="HeaderTemplate",Directory="FontDirectory",Version="TheVersion",Credits="Credits";
    static readonly string[] Types={Language,Header,Directory,Version,Credits};
    static readonly uint[] TypeIds={2915726794u,1218703046u,3569007303u,4236434438u,3978455785u};
    static readonly int[] Sizes={44,20,12,12,32};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Channels={"R","G","B","A"};
    static readonly string[] Destinations={"Normal","Aggressive","","UNKNOWN","normal","aggressive"," Normal","Normal ","0","1","Normal\0tail","Aggressive\0tail","UNKNOWN\0Normal"};
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
            Type type=typeof(XmllocalizationCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmllocalizationCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] EFontUse={"DefaultDisplayStringFont","DefaultWindowFont","DrawableCaptionFont","NativeDebugDisplay","TooltipFontName"};
    static readonly int[] VFontUse={2,1,0,4,3};
    static readonly string[] EStyleName={"ColumnFont","MinorTitleFont","NormalFont","TitleFont"};
    static readonly int[] VStyleName={3,1,2,0};
    static readonly string[] Strings={"","a","ab","abc","abcd","abcde","abcdefg","abcdefgh","abcdefghi","MixedCase","  spaces  ","A\tB\r\nC","<&'\"","资源","éßð","😀","A\0tail","\0tail","A\0B\0C"};
    static readonly string[] Scalars={"","0","-0","1","-1","TRUE","true","True","false","False","FALSE","yes","077","0xff","garbage","2147483647","2147483648","-2147483648","-2147483649","4294967295","4294967296","9999999999999999999999999999","  +17junk","1\0tail","\0TRUE"};
    static readonly string[][] FontPaths={new string[]{Header,"Font"},new string[]{Language,"FontInfo/Font"},new string[]{Credits,"Style/Font"}};
    static XmlDocument SetText(string root,string path,string value){XmlDocument d=Set(root,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string part in path.Split('/'))n=n.FirstChild;n.InnerText=value;return d;}
    static void Attr(string root,string path,string key,IEnumerable<string> values,TargetPlatform p){foreach(string value in values)TestDoc("attr/"+root+"/"+path+"/"+key+"/"+value.Length,Set(root,path,key,value),p);}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){AssetBuffer a=Test("ABI/defaults",Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");foreach(byte value in a.InstanceData)Check(value==0,"absent inlines remainzero");Check(a.RelocationData.Length==0&&a.ImportsData.Length==0,"defaults no bookmarks");}
        AssetBuffer h=Test("ABI/header",Root(Header,"","<Font Name='A' Size='19' Bold='true'/>"),p);Check(h.InstanceData.Length==24&&Word(h.InstanceData,4,big)==1&&Word(h.InstanceData,8,big)==20&&Word(h.InstanceData,12,big)==19&&h.InstanceData[16]==1&&h.InstanceData[20]==65&&h.InstanceData[21]==0,"FontDesc16 string8/unsignedSize/byteBold");Offsets(h.RelocationData,new uint[]{8},big,"headerFont name pointer");
        AssetBuffer l=Test("ABI/language",Root(Language,"","<FontInfo><Font Name='abc' Size='17' Bold='true'/><FontTitle>NativeDebugDisplay</FontTitle></FontInfo><UnicodeFontName>Face</UnicodeFontName><Separators/>"),p);Check(l.InstanceData.Length==88&&Word(l.InstanceData,4,big)==1&&Word(l.InstanceData,8,big)==44&&Word(l.InstanceData,12,big)==1&&Word(l.InstanceData,16,big)==48&&Word(l.InstanceData,20,big)==1&&Word(l.InstanceData,24,big)==52,"separator source order and String8 offsets");Check(l.InstanceData[44]==44&&l.InstanceData[48]==46&&l.InstanceData[52]==58,"separator original defaults comma period colon");Check(Word(l.InstanceData,28,big)==4&&Word(l.InstanceData,32,big)==56&&Word(l.InstanceData,36,big)==1&&Word(l.InstanceData,40,big)==64,"unicode name allocated before FontInfo20 list");Check(Word(l.InstanceData,64,big)==4&&Word(l.InstanceData,68,big)==3&&Word(l.InstanceData,72,big)==84&&Word(l.InstanceData,76,big)==17&&l.InstanceData[80]==1,"FontInfo titleEnum4 plus FontDesc16");Offsets(l.RelocationData,new uint[]{8,16,24,32,40,72},big,"language root then FontInfo owner relocation order");
        AssetBuffer c=Test("ABI/credits",Root(Credits,"","<CreditItem Style='MinorTitleFont'><Text>A</Text><Text/></CreditItem><Style Name='ColumnFont'><Font Name='F' Size='12' Bold='true'/><Color R='17' G='34' B='51' A='68'/></Style><Scroll Rate='2' RateEveryFrame='3' ScrollDown='true'/>"),p);Check(c.InstanceData.Length==96&&Word(c.InstanceData,4,big)==2&&Word(c.InstanceData,8,big)==3&&c.InstanceData[12]==1,"Credits32 inline Scroll12 and byte direction");Check(Word(c.InstanceData,16,big)==1&&Word(c.InstanceData,20,big)==32&&Word(c.InstanceData,24,big)==1&&Word(c.InstanceData,28,big)==60,"styles24 list processed before items12 list independent of XML order");Check(Word(c.InstanceData,32,big)==3&&Word(c.InstanceData,36,big)==0x44112233u&&Word(c.InstanceData,40,big)==1&&Word(c.InstanceData,44,big)==56&&Word(c.InstanceData,48,big)==12&&c.InstanceData[52]==1,"CreditStyle enum/ARGB/FontDesc16 offsets");Check(Word(c.InstanceData,60,big)==1&&Word(c.InstanceData,64,big)==2&&Word(c.InstanceData,68,big)==72&&Word(c.InstanceData,72,big)==1&&Word(c.InstanceData,76,big)==88&&Word(c.InstanceData,80,big)==0&&Word(c.InstanceData,84,big)==92,"CreditItem12 with nested String8 list and presentempty allocation");Offsets(c.RelocationData,new uint[]{20,28,44,68,76,84},big,"credits ownerblock/target allocation relocation contract");Check(c.ImportsData.Length==0,"font/text strings produce no imports");
        AssetBuffer color=Test("ABI/presentemptycolor",Root(Credits,"","<Style><Color/></Style>"),p);Check(color.InstanceData.Length==56&&Word(color.InstanceData,36,big)==0xff000000u,"presentempty ColorTag alpha255 while missingcolor zero");AssetBuffer v=Test("ABI/version",Root(Version,"","<Version Major='-1' Minor='077'/>"),p);Check(v.InstanceData.Length==12&&Word(v.InstanceData,4,big)==UInt32.MaxValue&&Word(v.InstanceData,8,big)==63,"signed version fields CRT octal");AssetBuffer dir=Test("ABI/directory",Root(Directory,"Path=''",""),p);Check(dir.InstanceData.Length==16&&Word(dir.InstanceData,4,big)==0&&Word(dir.InstanceData,8,big)==12,"presentempty Path allocates string bytes");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){Test("defaults",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("rootattr",Root(Types[i],"Unknown='1'",""));Reject("rootchild",Root(Types[i],"","<Unknown/>"));Reject("roottext",Root(Types[i],"","payload"));Reject("qualified rootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreignchild",Root(Types[i],"","<Font xmlns='urn:q'/>"));}
        foreach(string[] f in FontPaths){Attr(f[0],f[1],"Name",Strings,p);Attr(f[0],f[1],"Size",Scalars,p);Attr(f[0],f[1],"Bold",Scalars,p);}foreach(string key in new string[]{"Thousand","Decimal","TimeMinuteToSecond"})Attr(Language,"Separators",key,Strings,p);Attr(Directory,"","Path",Strings,p);foreach(string value in Strings){TestDoc("unicodefont-text",SetText(Language,"UnicodeFontName",value),p);TestDoc("credits-text",SetText(Credits,"CreditItem/Text",value),p);}foreach(string key in new string[]{"Rate","RateEveryFrame","ScrollDown"})Attr(Credits,"Scroll",key,Scalars,p);foreach(string key in new string[]{"Major","Minor"})Attr(Version,"Version",key,Scalars,p);foreach(string key in new string[]{"R","G","B","A"})Attr(Credits,"Style/Color",key,Scalars,p);
        foreach(string[] f in new string[][]{new string[]{Language,"FontInfo/FontTitle",""},new string[]{Credits,"Style","Name"},new string[]{Credits,"CreditItem","Style"}}){string[] names=f[0]==Language?EFontUse:EStyleName;int[] values=f[0]==Language?VFontUse:VStyleName;for(int i=0;i<names.Length;++i){AssetBuffer a=TestDoc("enumexact",f[2].Length==0?SetText(f[0],f[1],names[i]):Set(f[0],f[1],f[2],names[i]),p);int list=f[0]==Language?40:f[1]=="Style"?20:28;int at=(int)Word(a.InstanceData,list,big);Check(Word(a.InstanceData,at,big)==values[i],"exact original PE enum value");foreach(string value in new string[]{names[i].ToLowerInvariant()," "+names[i],names[i]+" ",names[i]+"\0tail","+"+names[i],"-"+names[i],"","UNKNOWN"})TestDoc("enum-boundary",f[2].Length==0?SetText(f[0],f[1],value):Set(f[0],f[1],f[2],value),p);}}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder list=new StringBuilder();for(int i=0;i<count;++i)list.Append("<FontInfo><FontTitle>DefaultWindowFont</FontTitle><Font Name='F' Size='12'/></FontInfo>");AssetBuffer l=Test("FontInfo-growth",Root(Language,"",list.ToString()),p);Check(l.InstanceData.Length==44+24*count&&Word(l.InstanceData,36,big)==count,"FontInfo20 stride plus one paddedstring perentry");list.Length=0;for(int i=0;i<count;++i)list.Append("<Style Name='NormalFont'><Font Name='F'/><Color/></Style><CreditItem Style='TitleFont'><Text>A</Text><Text/></CreditItem>");AssetBuffer c=Test("credits-growth",Root(Credits,"",list.ToString()),p);Check(c.InstanceData.Length==32+64*count&&Word(c.InstanceData,16,big)==count&&Word(c.InstanceData,24,big)==count,"Style24+font4+Item12+TextList16+strings8 growth");list.Length=0;for(int i=0;i<count;++i)list.Append("<Text>A</Text>");AssetBuffer text=Test("nestedText-growth",Root(Credits,"","<CreditItem>"+list+"</CreditItem>"),p);Check(text.InstanceData.Length==44+12*count,"nested Text list8 plus paddedstring4 growth");}
        foreach(string[] f in new string[][]{new string[]{Directory,"","Path"},new string[]{Header,"Font","Name"},new string[]{Language,"Separators","Thousand"}})foreach(int count in new int[]{0,1,3,4,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535})TestDoc("string-length",Set(f[0],f[1],f[2],new string('A',count)),p);
        foreach(string[] row in new string[][]{new string[]{Header,"Font"},new string[]{Language,"Separators"},new string[]{Language,"UnicodeFontName"},new string[]{Language,"FontInfo"},new string[]{Language,"FontInfo/Font"},new string[]{Language,"FontInfo/FontTitle"},new string[]{Version,"Version"},new string[]{Credits,"Scroll"},new string[]{Credits,"Style"},new string[]{Credits,"Style/Color"},new string[]{Credits,"Style/Font"},new string[]{Credits,"CreditItem"},new string[]{Credits,"CreditItem/Text"}}){TestDoc("nestedmetadata",Set(row[0],row[1],"TypeId","1"),p);Reject("nestedunknown",Set(row[0],row[1],"Unknown","1").OuterXml);}
        foreach(string[] row in new string[][]{new string[]{Header,"Font"},new string[]{Language,"Separators"},new string[]{Language,"UnicodeFontName"},new string[]{Version,"Version"},new string[]{Credits,"Scroll"}})Reject("duplicatesingleton",Root(row[0],"","<"+row[1]+"/><"+row[1]+"/>"));Reject("duplicateFontTitle",Root(Language,"","<FontInfo><FontTitle/><FontTitle/></FontInfo>"));Reject("duplicateFont",Root(Language,"","<FontInfo><Font/><Font/></FontInfo>"));Reject("duplicateStyleColor",Root(Credits,"","<Style><Color/><Color/></Style>"));Reject("duplicateStyleFont",Root(Credits,"","<Style><Font/><Font/></Style>"));Reject("fonttext",Root(Header,"","<Font>payload</Font>"));Reject("textchild",Root(Credits,"","<CreditItem><Text><Unknown/></Text></CreditItem>"));Reject("unknown root",Root("Unknown","",""));bool nil=false;try{XmllocalizationCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Header,"","")));dec.Handle.TypeName=Credits;bool wrong=false;try{XmllocalizationCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS Localization fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

