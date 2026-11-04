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
class XmluichatOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Message="UIQuickChatMessage",Display="UIMouseTacticalQuickChatDisplay",Voice="UIMouseVoiceChat";
    static readonly string[] Types={Message,Display,Voice};
    static readonly uint[] TypeIds={4098431913u,2891878770u,3746130610u};
    static readonly int[] Sizes={16,28,20};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
    static readonly string[] TimeValues={"0s","1s","250ms","1.25s","0.5m","2d1s","-1s","1s junk","2ms","1000ms","garbage","","1e30s","2147483648ms","25,5s"};
    static readonly string[] Strings={"","a","A","ab","abc","abcd","abcde","abcdefg","abcdefgh","abcdefghi","MixedCase","资源","é","😀","e\u0301","A&B<text>","a\0tail","\0leading","资源\0尾部",new string('k',1024)};
    static readonly string[] Factions=new string[]{"Allies","Japan","Neutral","RANDOM","Soviet","","unknown","neutral"," Neutral","Neutral ","0","-1"};
    static readonly string[] Destinations={"Allies","Everyone","","UNKNOWN","allies","everyone"," Allies","Everyone ","0","1"};
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
            Type type=typeof(XmluichatCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmluichatCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static XmlDocument Set(string root,string path,string attribute,string value){
        XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;
        if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement c=d.CreateElement(part,NS);node.AppendChild(c);node=c;}
        node.SetAttribute(attribute,value);return d;
    }
    static void Offsets(byte[] bytes,uint[] expected,bool big,string label){
        Check(bytes.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(bytes,4*i,big)==expected[i],label+" block/value order "+i);
        if(expected.Length!=0)Check(Word(bytes,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static void Abi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer message=Test("ABI/message",Root(Message,"MessageText='abc' Sound='A\\9'",""),p);
        Check(message.InstanceData.Length==20&&Word(message.InstanceData,4,big)==3&&Word(message.InstanceData,8,big)==16&&Word(message.InstanceData,12,big)==10,"message16-root/string/reference");
        Check(message.InstanceData[16]==97&&message.InstanceData[17]==98&&message.InstanceData[18]==99&&message.InstanceData[19]==0,"message exact ANSI string");
        Offsets(message.ImportsData,new uint[]{12},big,"message import");Offsets(message.RelocationData,new uint[]{8},big,"message string relocation");
        AssetBuffer empty=Test("ABI/message-empty",Root(Message,"MessageText=''",""),p);
        Check(empty.InstanceData.Length==20&&Word(empty.InstanceData,4,big)==0&&Word(empty.InstanceData,8,big)==16,"present-empty string allocates four bytes");
        AssetBuffer voice=Test("ABI/voice",Root(Voice,"ToggleModeHotKeySlot='K\\9' TalkHotKeySlot='K\\3'","<TalkButtonHelp Title='Z'/><ModeButtonHelp Title='A' Description='BB' TypeDescription='CCC' Instructions='DDDD'/>"),p);
        Check(voice.InstanceData.Length==108&&Word(voice.InstanceData,12,big)==20&&Word(voice.InstanceData,16,big)==72,"voice20-root/help32/schema allocationorder");
        int[] at={24,32,40,48,76};uint[] targets={52u,56u,60u,64u,104u};for(int i=0;i<at.Length;++i)Check(Word(voice.InstanceData,at[i],big)==targets[i],"voice nested string target "+i);
        Check(Word(voice.InstanceData,20,big)==1&&Word(voice.InstanceData,28,big)==2&&Word(voice.InstanceData,36,big)==3&&Word(voice.InstanceData,44,big)==4,"help four string lengths");
        Offsets(voice.ImportsData,new uint[]{8,4},big,"voice imports sortbyvalue");
        Offsets(voice.RelocationData,new uint[]{12,16,24,32,40,48,76},big,"voice relocations ownerblock then targetblock");
        AssetBuffer menus=Test("ABI/recursive-display",Root(Display,"CloseKey='K\\7' DelayDuration='250ms'",
            "<CloseMenuItem Title='Root'/><MenuItem Title='A' ChatDestination='Everyone'><CloseMenuItem Title='D'/><MenuItem Title='BB'><CloseMenuItem Title='C'/></MenuItem><ChatMessageGroup><ChatMessage MessageRef='A'/><ChatMessage MessageRef='a'/></ChatMessageGroup></MenuItem>"),p);
        Check(menus.InstanceData.Length==156&&Word(menus.InstanceData,4,big)==8&&Word(menus.InstanceData,8,big)==0x3e800000u,"display28-root/full recursive size/time");
        int[] pointers={16,32,36,44,76,52,92,116,128,56,140,24};uint[] values={28u,60u,64u,68u,80u,88u,120u,124u,132u,136u,144u,148u};
        for(int i=0;i<pointers.Length;++i)Check(Word(menus.InstanceData,pointers[i],big)==values[i],"recursive allocationtarget "+i);
        Check(Word(menus.InstanceData,12,big)==1&&Word(menus.InstanceData,40,big)==1&&Word(menus.InstanceData,48,big)==1&&Word(menus.InstanceData,72,big)==2,"recursive list counts");
        Check(Word(menus.InstanceData,64,big)==1&&Word(menus.InstanceData,68,big)==3,"Everyone=1 anddefaultFactionNeutral=3");
        Check(Word(menus.InstanceData,80,big)==1214652683u&&Word(menus.InstanceData,84,big)==1214652683u,"weakMessageRef lower-case typedhash");
        Offsets(menus.RelocationData,new uint[]{16,24,32,36,44,52,56,76,92,116,128,140},big,"recursive relocation ownerblock then targetblock");
        Offsets(menus.ImportsData,new uint[]{4},big,"typed MessageRef isnotanimport");
        AssetBuffer destination=Test("ABI/destination-empty",Root(Display,"","<MenuItem ChatDestination=''/>"),p);
        Check(destination.InstanceData.Length==64&&Word(destination.InstanceData,36,big)==60&&Word(destination.InstanceData,60,big)==0,"present-empty destination allocates enum4/fallbackAllies");
        AssetBuffer faction=Test("ABI/faction-unknown",Root(Display,"","<MenuItem><ChatMessageGroup/><ChatMessageGroup Faction='UNKNOWN'/></MenuItem>"),p);
        Check(faction.InstanceData.Length==84&&Word(faction.InstanceData,60,big)==3&&Word(faction.InstanceData,72,big)==0,"missingFaction usesNeutral whileunknown usesAllies");
        AssetBuffer helpEmpty=Test("ABI/help-present-empty",Root(Voice,"","<ModeButtonHelp/>"),p);
        Check(helpEmpty.InstanceData.Length==52&&Word(helpEmpty.InstanceData,12,big)==20,"present-empty help stillallocates32");
        AssetBuffer closeEmpty=Test("ABI/close-present-empty",Root(Display,"","<MenuItem><CloseMenuItem/></MenuItem>"),p);
        Check(closeEmpty.InstanceData.Length==68&&Word(closeEmpty.InstanceData,56,big)==60,"menu closepresentempty stillallocates8");
    }
    static void Growth(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        foreach(int count in new int[]{0,1,2,3,8,9,10,15,16,17,27,28,29,64,257}){
            StringBuilder menus=new StringBuilder(),groups=new StringBuilder(),messages=new StringBuilder();
            for(int i=0;i<count;++i){
                menus.Append("<MenuItem><ChatMessageGroup Faction='Japan'><ChatMessage MessageRef='Mixed"+i+"'/><ChatMessage MessageRef='A'/></ChatMessageGroup><MenuItem/><CloseMenuItem/></MenuItem>");
                groups.Append("<ChatMessageGroup Faction='Soviet'><ChatMessage MessageRef='Mixed"+i+"'/></ChatMessageGroup>");
                messages.Append("<ChatMessage MessageRef='Mixed"+i+"'/>");
            }
            AssetBuffer wide=Test("menu-growth/"+count,Root(Display,"",menus.ToString()),p);
            Check(wide.InstanceData.Length==28+92*count&&Word(wide.InstanceData,12,big)==count,"menu32/group12/messages8/submenu32/close8 exactgrowth");
            AssetBuffer gs=Test("group-growth/"+count,Root(Display,"","<MenuItem>"+groups+"</MenuItem>"),p);
            Check(gs.InstanceData.Length==60+16*count&&Word(gs.InstanceData,40,big)==count,"group12/message4 exactgrowth");
            AssetBuffer ms=Test("message-growth/"+count,Root(Display,"","<MenuItem><ChatMessageGroup>"+messages+"</ChatMessageGroup></MenuItem>"),p);
            Check(ms.InstanceData.Length==72+4*count&&Word(ms.InstanceData,64,big)==count,"message4 list exactgrowth");
        }
        foreach(int depth in new int[]{1,2,3,8,16,24,30,31,32}){
            StringBuilder body=new StringBuilder();for(int i=0;i<depth;++i)body.Append("<MenuItem>");for(int i=0;i<depth;++i)body.Append("</MenuItem>");
            AssetBuffer d=Test("recursive-depth/"+depth,Root(Display,"",body.ToString()),p);Check(d.InstanceData.Length==28+32*depth,"deep recursive exactgrowth");
        }
        StringBuilder excessive=new StringBuilder();for(int i=0;i<33;++i)excessive.Append("<MenuItem>");for(int i=0;i<33;++i)excessive.Append("</MenuItem>");Reject("depth33 explicit boundary",Root(Display,"",excessive.ToString()));
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i){
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown rootattr",Root(Types[i],"Unknown='1'",""));Reject("qualified rootattr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));
            Reject("unknown child",Root(Types[i],"","<Unknown/>"));Reject("foreign child",Root(Types[i],"","<MenuItem xmlns='urn:q'/>"));Reject("root text",Root(Types[i],"","payload"));
        }
        string[][] stringFields={new string[]{Message,"","MessageText"},new string[]{Display,"CloseMenuItem","Title"},new string[]{Display,"MenuItem","Title"},new string[]{Display,"MenuItem/CloseMenuItem","Title"},new string[]{Display,"MenuItem/MenuItem","Title"}};
        foreach(string[] field in stringFields)foreach(string value in Strings)TestDoc("string/"+field[0]+"/"+field[1]+"/"+value.Length,Set(field[0],field[1],field[2],value),p);
        foreach(string help in new string[]{"ModeButtonHelp","TalkButtonHelp"})foreach(string name in new string[]{"Title","Description","TypeDescription","Instructions"})foreach(string value in Strings)TestDoc("help-string/"+help+"/"+name+"/"+value.Length,Set(Voice,help,name,value),p);
        foreach(string value in References){
            TestDoc("messageSound/"+value.Length,Set(Message,"","Sound",value),p);TestDoc("closeKey/"+value.Length,Set(Display,"","CloseKey",value),p);
            foreach(string name in new string[]{"ToggleModeHotKeySlot","TalkHotKeySlot"})TestDoc("voiceRef/"+name+"/"+value.Length,Set(Voice,"",name,value),p);
        }
        foreach(string value in TimeValues)TestDoc("delay/"+value,Set(Display,"","DelayDuration",value),p);
        foreach(string value in Destinations)TestDoc("destination/"+value,Set(Display,"MenuItem","ChatDestination",value),p);
        foreach(string value in Factions)TestDoc("faction/"+value,Set(Display,"MenuItem/ChatMessageGroup","Faction",value),p);
        foreach(string value in new string[]{"Everyone\0tail","Allies\0tail","UNKNOWN\0Everyone","\0Everyone"})TestDoc("destination-NUL",Set(Display,"MenuItem","ChatDestination",value),p);
        foreach(string value in new string[]{"Neutral\0tail","Japan\0tail","UNKNOWN\0Neutral","\0Neutral"})TestDoc("faction-NUL",Set(Display,"MenuItem/ChatMessageGroup","Faction",value),p);
        List<string> weak=new List<string>(Strings);weak.AddRange(References);weak.AddRange(new string[]{"UIQuickChatMessage:Mixed","ABC.Def-123","0","-1"});
        foreach(string value in weak)TestDoc("weakTypedHash/"+value.Length,Set(Display,"MenuItem/ChatMessageGroup/ChatMessage","MessageRef",value),p);
        string[][] nodes={new string[]{Voice,"ModeButtonHelp"},new string[]{Voice,"TalkButtonHelp"},new string[]{Display,"MenuItem"},new string[]{Display,"MenuItem/ChatMessageGroup"},new string[]{Display,"MenuItem/ChatMessageGroup/ChatMessage"},new string[]{Display,"CloseMenuItem"},new string[]{Display,"MenuItem/CloseMenuItem"}};
        foreach(string[] row in nodes){
            TestDoc("nested TypeId/"+row[1],Set(row[0],row[1],"TypeId","1"),p);
            Reject("unknown nestedattr",Set(row[0],row[1],"Unknown","1").OuterXml);
            XmlDocument d=Set(row[0],row[1],"TypeId","1");XmlNode at=d.DocumentElement;foreach(string name in row[1].Split('/'))at=at.FirstChild;XmlAttribute q=d.CreateAttribute("q","Unknown","urn:q");q.Value="1";at.Attributes.Append(q);Reject("qualified nestedattr",d.OuterXml);
        }
        Reject("duplicate rootclose",Root(Display,"","<CloseMenuItem/><CloseMenuItem/>"));
        Reject("duplicate menuclose",Root(Display,"","<MenuItem><CloseMenuItem/><CloseMenuItem/></MenuItem>"));
        foreach(string name in new string[]{"ModeButtonHelp","TalkButtonHelp"})Reject("duplicate help",Root(Voice,"","<"+name+"/><"+name+"/>"));
        Reject("messagegroup text",Root(Display,"","<MenuItem><ChatMessageGroup>payload</ChatMessageGroup></MenuItem>"));
        Reject("menu text",Root(Display,"","<MenuItem>payload</MenuItem>"));
        Reject("message child",Root(Display,"","<MenuItem><ChatMessageGroup><ChatMessage><Unknown/></ChatMessage></ChatMessageGroup></MenuItem>"));
        Reject("close child",Root(Display,"","<CloseMenuItem><Unknown/></CloseMenuItem>"));
        Reject("help child",Root(Voice,"","<ModeButtonHelp><Unknown/></ModeButtonHelp>"));
        Reject("unknown root",Root("Unknown","",""));
        bool nullInput=false;try{XmluichatCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument wrong=Parse(Root(Message,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName=Voice;
        bool mismatch=false;try{XmluichatCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch rejected");++rejections;
        Abi(p);Growth(p);
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
            Console.WriteLine("PASS UI chat fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

