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
class XmlcontentOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Content="EngagingContent",Group="EngagingContentGroup",Sequence="EngagingContentSequence",Intel="IntelDB",Intro="UnitIntro";
    static readonly string[] Types={Content,Group,Sequence,Intel,Intro};
    static readonly uint[] TypeIds={3058114834u,11280974u,3799634821u,530081230u,1870180772u};
    static readonly int[] Sizes={28,12,20,12,12};
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
            Type type=typeof(XmlcontentCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlcontentCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Strings={"","a","ab","abc","abcd","abcde","abcdefg","abcdefgh","abcdefghi","MixedCase","  spaces  ","A\tB\r\nC","<&'\"","资源","éßð","😀","A\0tail","\0tail","A\0B\0C"};
    static readonly string[][] StringFields={new string[]{Content,"","TitleId"},new string[]{Content,"","DescId"},new string[]{Content,"","ImageId"},new string[]{Sequence,"","MapName"},new string[]{Intel,"IntelDBEntry","CategoryId"},new string[]{Intel,"IntelDBEntry","EntryId"},new string[]{Intel,"IntelDBEntry","TextureId"}};
    static XmlDocument SetText(string root,string path,string value){XmlDocument d=Set(root,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string part in path.Split('/'))n=n.FirstChild;n.InnerText=value;return d;}
    static void Bytes(byte[] actual,int offset,string text,string label){byte[] expected=Encoding.ASCII.GetBytes(text);for(int i=0;i<expected.Length;++i)Check(actual[offset+i]==expected[i],label+" exact ASCII byte");Check(actual[offset+expected.Length]==0,label+" string terminator");}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){AssetBuffer a=Test("ABI/defaults",Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");foreach(byte value in a.InstanceData)Check(value==0,"absent attrs and lists stayzero");Check(a.RelocationData.Length==0&&a.ImportsData.Length==0,"absent no bookmarks");}
        AssetBuffer c=Test("ABI/content",Root(Content,"ImageId='def' DescId='bc' TitleId='a'",""),p);Check(c.InstanceData.Length==40&&Word(c.InstanceData,4,big)==1&&Word(c.InstanceData,8,big)==28&&Word(c.InstanceData,12,big)==2&&Word(c.InstanceData,16,big)==32&&Word(c.InstanceData,20,big)==3&&Word(c.InstanceData,24,big)==36,"three ReloString8 in source order independent of attrs order");Bytes(c.InstanceData,28,"a","title");Bytes(c.InstanceData,32,"bc","description");Bytes(c.InstanceData,36,"def","image");Offsets(c.RelocationData,new uint[]{8,16,24},big,"content strings owner/target order");Check(c.ImportsData.Length==0,"content strings have no imports");
        AssetBuffer empty=Test("ABI/emptystrings",Root(Content,"TitleId='' DescId='' ImageId=''",""),p);Check(empty.InstanceData.Length==40&&Word(empty.InstanceData,8,big)==28&&Word(empty.InstanceData,16,big)==32&&Word(empty.InstanceData,24,big)==36,"present empty string allocates padded four bytes each");
        AssetBuffer group=Test("ABI/group",Root(Group,"","<EngagingContent>A\\9</EngagingContent><EngagingContent>B\\1</EngagingContent><EngagingContent>C\\5</EngagingContent>"),p);Check(group.InstanceData.Length==24&&Word(group.InstanceData,4,big)==3&&Word(group.InstanceData,8,big)==12,"group list4 count/target");Check(Word(group.InstanceData,12,big)==10&&Word(group.InstanceData,16,big)==2&&Word(group.InstanceData,20,big)==6,"group strong references index+1");Offsets(group.RelocationData,new uint[]{8},big,"group list pointer");Offsets(group.ImportsData,new uint[]{16,20,12},big,"group import numeric index order");
        AssetBuffer sequence=Test("ABI/sequence",Root(Sequence,"MapName='abc' RequiredContent='A\\9' RandomContent='B\\1'",""),p);Check(sequence.InstanceData.Length==24&&Word(sequence.InstanceData,4,big)==3&&Word(sequence.InstanceData,8,big)==20&&Word(sequence.InstanceData,12,big)==10&&Word(sequence.InstanceData,16,big)==2,"sequence20 with String8 then two refs");Bytes(sequence.InstanceData,20,"abc","mapname");Offsets(sequence.RelocationData,new uint[]{8},big,"sequence map pointer");Offsets(sequence.ImportsData,new uint[]{16,12},big,"sequence imports sorted values");
        AssetBuffer intel=Test("ABI/intel",Root(Intel,"","<IntelDBEntry CategoryId='A' EntryId=''/><IntelDBEntry EntryId='XY' TextureId='Z'/>"),p);Check(intel.InstanceData.Length==76&&Word(intel.InstanceData,4,big)==2&&Word(intel.InstanceData,8,big)==12,"intel12+two24byteentries+fourstrings");Check(Word(intel.InstanceData,12,big)==1&&Word(intel.InstanceData,16,big)==60&&Word(intel.InstanceData,20,big)==0&&Word(intel.InstanceData,24,big)==64&&Word(intel.InstanceData,32,big)==0&&Word(intel.InstanceData,40,big)==0,"first entry empty versus missing strings");Check(Word(intel.InstanceData,44,big)==2&&Word(intel.InstanceData,48,big)==68&&Word(intel.InstanceData,52,big)==1&&Word(intel.InstanceData,56,big)==72,"second entry24stride and source string order");Bytes(intel.InstanceData,60,"A","category");Bytes(intel.InstanceData,64,"","emptyentry");Bytes(intel.InstanceData,68,"XY","entry");Bytes(intel.InstanceData,72,"Z","texture");Offsets(intel.RelocationData,new uint[]{8,16,24,48,56},big,"intel root/list owner and target order");
        AssetBuffer intro=Test("ABI/intro",Root(Intro,"Video='MixedCase' Audio='A\\1'",""),p);AssetBuffer lower=Test("ABI/intro-lower",Root(Intro,"Video='mixedcase' Audio='UnboundName'",""),p);Check(intro.InstanceData.Length==12&&Word(intro.InstanceData,4,big)==Word(lower.InstanceData,4,big)&&Word(intro.InstanceData,4,big)!=0&&Word(intro.InstanceData,8,big)==2,"Video weak casefold hash; Audio strong index+1");Check(intro.RelocationData.Length==0&&lower.ImportsData.Length==0,"weak Video/unbound Audio creates no bookmark");Offsets(intro.ImportsData,new uint[]{8},big,"only Audio imports");
        XmlDocument nul=Set(Content,"","TitleId","abc\0ignored");AssetBuffer trunc=TestDoc("ABI/string-NUL",nul,p);Check(trunc.InstanceData.Length==32&&Word(trunc.InstanceData,4,big)==3&&Word(trunc.InstanceData,8,big)==28,"strings truncate at original C terminator");Bytes(trunc.InstanceData,28,"abc","Cstringprefix");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;
        for(int i=0;i<Types.Length;++i){Test("defaults",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("unknownattr",Root(Types[i],"Unknown='1'",""));Reject("unknownchild",Root(Types[i],"","<Unknown/>"));Reject("root text",Root(Types[i],"","payload"));Reject("qualified rootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreign child",Root(Types[i],"","<IntelDBEntry xmlns='urn:q'/>"));}
        foreach(string[] field in StringFields){foreach(string value in Strings)TestDoc("string/"+field[0]+"/"+field[2]+"/"+value.Length,Set(field[0],field[1],field[2],value),p);foreach(int count in new int[]{0,1,2,3,4,5,7,8,9,15,16,17,27,28,29,31,32,33,63,64,65,127,128,129,255,256,257,1023,1024,1025,4095,4096,65535}){AssetBuffer a=TestDoc("stringlength/"+count,Set(field[0],field[1],field[2],new string('A',count)),p);int slot=field[0]==Intel?12+(field[2]=="CategoryId"?0:field[2]=="EntryId"?8:16):field[0]==Sequence?4:field[2]=="TitleId"?4:field[2]=="DescId"?12:20;Check(Word(a.InstanceData,slot,big)==count,"exact string length");int size=field[0]==Intel?36:field[0]==Sequence?20:28;Check(a.InstanceData.Length==size+((count+4)&~3),"exact padded string size");}}
        foreach(string value in References){foreach(string attr in new string[]{"RequiredContent","RandomContent"})TestDoc("sequence-ref",Set(Sequence,"",attr,value),p);TestDoc("group-ref",SetText(Group,"EngagingContent",value),p);TestDoc("intro-ref",Set(Intro,"","Audio",value),p);TestDoc("intro-weakhash",Set(Intro,"","Video",value),p);}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder list=new StringBuilder();for(int i=0;i<count;++i)list.Append("<EngagingContent>R\\"+(count-i)+"</EngagingContent>");AssetBuffer group=Test("groupgrowth/"+count,Root(Group,"",list.ToString()),p);Check(group.InstanceData.Length==12+4*count&&Word(group.InstanceData,4,big)==count,"exact group list growth");if(count>0){uint[] imports=new uint[count];for(int i=0;i<count;++i)imports[i]=(uint)(12+4*(count-1-i));Offsets(group.ImportsData,imports,big,"group all references sorted by numeric index");}list.Length=0;for(int i=0;i<count;++i)list.Append("<IntelDBEntry CategoryId='A' EntryId='' TextureId='XYZ'/>");AssetBuffer intel=Test("intelgrowth/"+count,Root(Intel,"",list.ToString()),p);Check(intel.InstanceData.Length==12+36*count&&Word(intel.InstanceData,4,big)==count,"intel list24 plus three separately padded string blocks per item");}
        foreach(string root in new string[]{Intel,Group}){string child=root==Intel?"IntelDBEntry":"EngagingContent";TestDoc("child TypeId",Set(root,child,"TypeId","1"),p);Reject("child attr",Set(root,child,"Unknown","1").OuterXml);Reject("child nested",Root(root,"","<"+child+"><Unknown/></"+child+">"));Reject("qualified childattr",Root(root,"","<"+child+" xmlns:q='urn:q' q:TypeId='1'/>"));}Reject("intel text",Root(Intel,"","<IntelDBEntry>payload</IntelDBEntry>"));Reject("unknown root",Root("Unknown","",""));bool nil=false;try{XmlcontentCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Content,"","")));dec.Handle.TypeName=Intro;bool wrong=false;try{XmlcontentCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS Content media fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

