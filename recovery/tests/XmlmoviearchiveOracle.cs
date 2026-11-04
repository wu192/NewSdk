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
class XmlmoviearchiveOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Movie="ArchivableMovie",List="ArchiveMovieList";
    static readonly string[] Types={Movie,List};
    static readonly uint[] TypeIds={3741496377u,2099472558u};
    static readonly int[] Sizes={32,12};
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
            Type type=typeof(XmlmoviearchiveCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(b.InstanceData.Length>=Sizes[Array.IndexOf(Types,d.DocumentElement.LocalName)],label+" root minimum size");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlmoviearchiveCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] Scalars={"","0","-0","1","-1","TRUE","true","True","false","False","FALSE","yes","077","0xff","garbage","2147483647","2147483648","-2147483648","-2147483649","4294967295","4294967296","9999999999999999999999999999","  +17junk","1\0tail","\0TRUE"};
    static readonly string[] EFactionType={"Allies","Japan","Neutral","RANDOM","Soviet"};
    static readonly int[] VFactionType={0,2,3,-1,1};
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;AssetBuffer m=Test("ABI/movie-default",Root(Movie,"",""),p);Check(m.InstanceData.Length==32&&Word(m.InstanceData,0,big)==0&&m.RelocationData.Length==0&&m.ImportsData.Length==0,"Movie32 defaultprogress0 no allocations/imports");AssetBuffer l=Test("ABI/list-default",Root(List,"",""),p);Check(l.InstanceData.Length==12&&Word(l.InstanceData,0,big)==1&&Word(l.InstanceData,4,big)==0&&l.RelocationData.Length==0,"ArchiveMovieList12 defaultFactionSoviet1 not reservedzero");
        AssetBuffer a=Test("ABI/movie-full",Root(Movie,"ProgressLock='7' MovieImage='M\\9' FileName='A' OnScreenName='BC' Description='DEF'",""),p);Check(a.InstanceData.Length==44&&Word(a.InstanceData,0,big)==7&&Word(a.InstanceData,4,big)==10&&Word(a.InstanceData,8,big)==1&&Word(a.InstanceData,12,big)==32&&Word(a.InstanceData,16,big)==2&&Word(a.InstanceData,20,big)==36&&Word(a.InstanceData,24,big)==3&&Word(a.InstanceData,28,big)==40,"Movie32 Progress/Image/String8x3 exact offsets");Check(a.InstanceData[32]==65&&a.InstanceData[33]==0&&a.InstanceData[36]==66&&a.InstanceData[37]==67&&a.InstanceData[38]==0,"movie exact string bytes and terminators");Offsets(a.RelocationData,new uint[]{12,20,28},big,"movie three strings allocation order");Offsets(a.ImportsData,new uint[]{4},big,"MovieImage only import");
        AssetBuffer b=Test("ABI/list-full",Root(List,"Faction='Japan'","<Movie ProgressLock='1' MovieImage='M\\9' FileName='A' OnScreenName='' Description='B'/><Movie ProgressLock='2' MovieImage='M\\1' FileName='C' OnScreenName='D' Description='E'/>"),p);Check(b.InstanceData.Length==100&&Word(b.InstanceData,0,big)==2&&Word(b.InstanceData,4,big)==2&&Word(b.InstanceData,8,big)==12,"FactionJapan2 count2 list32byte stride");Check(Word(b.InstanceData,12,big)==1&&Word(b.InstanceData,44,big)==2&&Word(b.InstanceData,16,big)==10&&Word(b.InstanceData,48,big)==2,"nested movie exact progress/reference offsets");Check(Word(b.InstanceData,24,big)==76&&Word(b.InstanceData,32,big)==80&&Word(b.InstanceData,40,big)==84&&Word(b.InstanceData,56,big)==88&&Word(b.InstanceData,64,big)==92&&Word(b.InstanceData,72,big)==96,"full array allocated before entry source-ordered strings");Offsets(b.RelocationData,new uint[]{8,24,32,40,56,64,72},big,"root then list owner relocation ordering");Offsets(b.ImportsData,new uint[]{48,16},big,"nested imports index ordering");
        AssetBuffer e=Test("ABI/emptystrings",Root(Movie,"FileName='' OnScreenName='' Description=''",""),p);Check(e.InstanceData.Length==44&&Word(e.InstanceData,12,big)==32&&Word(e.InstanceData,20,big)==36&&Word(e.InstanceData,28,big)==40,"empty strings allocate4 each distinct from absence");
    }
    static void Matrix(TargetPlatform p){original.Initialize(null,p);bool big=p!=TargetPlatform.Win32;for(int i=0;i<Types.Length;++i){Test("defaults",Root(Types[i],"",""),p);Test("metadata",Root(Types[i],"TypeId='"+TypeIds[i]+"' inheritFrom='parent'",""),p);Reject("rootattr",Root(Types[i],"Unknown='1'",""));Reject("roottext",Root(Types[i],"","payload"));Reject("rootchild",Root(Types[i],"","<Unknown/>"));Reject("qualifiedrootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));Reject("foreignchild",Root(Types[i],"","<Movie xmlns='urn:q'/>"));}
        foreach(string root in Types){string path=root==Movie?"":"Movie";foreach(string value in Scalars)TestDoc("progress",Set(root,path,"ProgressLock",value),p);foreach(string value in References)TestDoc("image-ref",Set(root,path,"MovieImage",value),p);foreach(string attr in new string[]{"FileName","OnScreenName","Description"}){foreach(string value in Strings)TestDoc("movie-string",Set(root,path,attr,value),p);foreach(int count in new int[]{0,1,2,3,4,7,8,15,16,27,28,31,32,255,256,257,1023,1024,4095,4096,65535}){AssetBuffer a=TestDoc("stringgrowth",Set(root,path,attr,new string('A',count)),p);int slot=(root==Movie?0:12)+(attr=="FileName"?8:attr=="OnScreenName"?16:24);Check(Word(a.InstanceData,slot,big)==count&&a.InstanceData.Length==(root==Movie?32:44)+((count+4)&~3),"exact String8 length and padded size");}}}
        for(int i=0;i<EFactionType.Length;++i){AssetBuffer a=TestDoc("Factionexact",Set(List,"","Faction",EFactionType[i]),p);Check(Word(a.InstanceData,0,big)==unchecked((uint)VFactionType[i]),"original PE faction enum value");foreach(string value in new string[]{EFactionType[i].ToLowerInvariant()," "+EFactionType[i],EFactionType[i]+" ",EFactionType[i]+"\0tail","+"+EFactionType[i],"-"+EFactionType[i]})TestDoc("Factionboundary",Set(List,"","Faction",value),p);}foreach(string value in new string[]{"","UNKNOWN","0","1","\0Soviet"}){AssetBuffer a=TestDoc("Factionfallback",Set(List,"","Faction",value),p);Check(Word(a.InstanceData,0,big)==0,"presentunknown factionfallbackAllies0 differs from absentSoviet1");}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,31,32,33,64,257,1024}){StringBuilder items=new StringBuilder();for(int i=0;i<count;++i)items.Append("<Movie ProgressLock='"+i+"' MovieImage='M\\"+(count-i)+"' FileName='A' OnScreenName='' Description='B'/>");AssetBuffer a=Test("listgrowth",Root(List,"Faction='Neutral'",items.ToString()),p);Check(a.InstanceData.Length==12+44*count&&Word(a.InstanceData,4,big)==count,"movie32 plus three4byte stringblocks growth");if(count>0){uint[] imports=new uint[count];for(int i=0;i<count;++i)imports[i]=(uint)(16+32*(count-1-i));Offsets(a.ImportsData,imports,big,"all movie imports numeric-index order");}}
        TestDoc("nestedmetadata",Set(List,"Movie","TypeId","1"),p);Reject("movieattr",Set(List,"Movie","Unknown","1").OuterXml);Reject("movietext",Root(List,"","<Movie>payload</Movie>"));Reject("moviechild",Root(List,"","<Movie><Unknown/></Movie>"));Reject("qualifiedmovieattr",Root(List,"","<Movie xmlns:q='urn:q' q:TypeId='1'/>"));Reject("unknownroot",Root("Unknown","",""));bool nil=false;try{XmlmoviearchiveCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nil=true;}Check(nil,"null declaration rejected");++rejections;InstanceDeclaration dec=Declaration(Parse(Root(Movie,"","")));dec.Handle.TypeName=List;bool wrong=false;try{XmlmoviearchiveCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){wrong=true;}Check(wrong,"wrong handle rejected");++rejections;Abi(p);
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
            Console.WriteLine("PASS Movie archive fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

