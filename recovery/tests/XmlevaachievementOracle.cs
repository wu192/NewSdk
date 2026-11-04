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
class XmlevaachievementOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"SiegeEvaEvent","EvaSettings","EvaEvent","MiscEvaEvents","AchievementEvent","MapNameToAchievementEventMapping","GameplayAchievementTriggerData"};
    static readonly uint[] TypeIds={1268003905u,2361666546u,357806494u,1807540972u,4230032093u,3594283852u,348670903u};
    static readonly int[] Sizes={16,20,44,164,16,16,12};
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
            Type type=typeof(XmlevaachievementCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlevaachievementCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] MiscReferences={"LowPower","SilosNeeded","BuildingStolen","IncomingRadarTransmission","AllyDefeated","EnemyDefeated","LocalPlayerDefeated","LocalPlayerVictorious","CannotBuildDueToCPLimit","BuildQueuePausedDueToCPLimit","BuildQueuePausedDueToFunds","CannotBuildDueToFunds","CannotRepairDueToFunds","CannotUsePowerDueToFunds","TiberiumFieldDepleted","NewMissionObjective","CompletedMissionObjective","NewBonusObjective","CompletedBonusMissionObjective","IntelDatabaseEntryReceived","UpgradeComplete","BuildingBeingStolen","BeaconPlacedPlayer","BeaconPlacedAlly","ObservedPlayerDefeated","WallsBeingClimbed","StructureInfiltrated","ControlPointCaptured","ControlPointLost","ControlPointHasBeenCaptured","CTFFlagCaptured","CTFEnemyCapturedFlag","CTFFlagDropped","CTFEnemyDroppedFlag","CTFYouHaveFlag","CTFEnemyHasFlag","SiegeBarrierDisengaged","SiegeBarrierEngaged"};
    static readonly string[] Times={"Seconds","TimeBetweenEvents","ExpirationTime","QuietTime","TimeToWaitBeforePlaying","MaxTimeToKeepJumpToEvents","MaxTimeBeforeResettingLastJumpTo"};
    static string AllMisc(int count){StringBuilder a=new StringBuilder();for(int i=0;i<MiscReferences.Length;++i)a.Append(MiscReferences[i]+"='R\\"+(count-i)+"' ");return a.ToString();}
    static void Abi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        for(int i=0;i<Types.Length;++i){AssetBuffer d=Test("ABI/default/"+Types[i],Root(Types[i],"",""),p);Check(d.InstanceData.Length==Sizes[i],"exact default root size "+Types[i]);for(int at=4;at<Sizes[i];at+=4){uint expected=(Types[i]=="SiegeEvaEvent"&&at==12)?2u:(Types[i]=="EvaEvent"&&at==12)?0x40000000u:0u;if(Types[i]=="EvaEvent"&&at==40)continue;Check(Word(d.InstanceData,at,big)==expected,"default word "+Types[i]+"/"+at);}if(Types[i]=="EvaEvent")Check(d.InstanceData[40]==0&&d.InstanceData[41]==1&&d.InstanceData[42]==0&&d.InstanceData[43]==0,"four byte booleans");Offsets(d.ImportsData,new uint[0],big,"no default imports");Offsets(d.RelocationData,new uint[0],big,"no default relocations");}
        AssetBuffer siege=Test("ABI/siege",Root("SiegeEvaEvent","Seconds='500ms' EvaEvent='E\\7' EventType='GENERIC'",""),p);Check(Word(siege.InstanceData,4,big)==0x3f000000u&&Word(siege.InstanceData,8,big)==8&&Word(siege.InstanceData,12,big)==1,"siege inline time ref enum");Offsets(siege.ImportsData,new uint[]{8},big,"siege import");
        AssetBuffer settings=Test("ABI/settings",Root("EvaSettings","EnemySightedMaxVoicePositionScanRange='-7' MaxTimeToKeepJumpToEvents='500ms' MaxTimeBeforeResettingLastJumpTo='2s' MinDistanceBetweenJumpToEvents='13'",""),p);Check(Word(settings.InstanceData,4,big)==unchecked((uint)-7)&&Word(settings.InstanceData,8,big)==0x3f000000u&&Word(settings.InstanceData,12,big)==0x40000000u&&Word(settings.InstanceData,16,big)==13,"settings signed/time fields");
        AssetBuffer eva=Test("ABI/eva",Root("EvaEvent","Priority='7' TimeBetweenEvents='500ms' ExpirationTime='4s' QuietTime='2s' TimeToWaitBeforePlaying='1s' AlwaysPlayFromHomeBase='true' CountAsJumpToLocation='false' SuppressOn360='true' SuppressOnPC='true'","<OtherEvaEventToBlock>MixedCase</OtherEvaEventToBlock><SideSound Sound='S\\9' Side='MixedCase'/><SideSound Sound='S\\1' Side=''/><OtherEvaEventToBlock/>"),p);
        Check(eva.InstanceData.Length==68&&Word(eva.InstanceData,24,big)==2&&Word(eva.InstanceData,28,big)==44&&Word(eva.InstanceData,32,big)==2&&Word(eva.InstanceData,36,big)==60,"eva two lists allocated marshal order independent of XML order");
        Check(Word(eva.InstanceData,4,big)==7&&Word(eva.InstanceData,8,big)==0x3f000000u&&Word(eva.InstanceData,12,big)==0x40800000u&&Word(eva.InstanceData,16,big)==0x40000000u&&Word(eva.InstanceData,20,big)==0x3f800000u,"eva scalar offsets");
        Check(eva.InstanceData[40]==1&&eva.InstanceData[41]==0&&eva.InstanceData[42]==1&&eva.InstanceData[43]==1,"booleans not endian swapped");Check(Word(eva.InstanceData,44,big)==10&&Word(eva.InstanceData,52,big)==2,"side refs index+1");Check(Word(eva.InstanceData,48,big)==Word(eva.InstanceData,60,big)&&Word(eva.InstanceData,48,big)!=0&&Word(eva.InstanceData,56,big)==0&&Word(eva.InstanceData,64,big)==0,"weak IDs equal cross-kind and empty0");Offsets(eva.ImportsData,new uint[]{52,44},big,"eva owner list reference sort");Offsets(eva.RelocationData,new uint[]{28,36},big,"eva two pointers");
        AssetBuffer misc=Test("ABI/misc",Root("MiscEvaEvents",AllMisc(100),"<AIBeaconTypeSettings BeaconType='Invalid' PlacedEvent='E\\7'/><AIBeaconTypeSettings BeaconType='HoldObject' PlacedEvent='E\\1'/>"),p);
        Check(misc.InstanceData.Length==180&&Word(misc.InstanceData,156,big)==2&&Word(misc.InstanceData,160,big)==164,"misc164 and8byteitems");for(int i=0;i<38;++i)Check(Word(misc.InstanceData,4+4*i,big)==101-i,"all38 original ref offsets");Check(Word(misc.InstanceData,164,big)==UInt32.MaxValue&&Word(misc.InstanceData,168,big)==8&&Word(misc.InstanceData,172,big)==2&&Word(misc.InstanceData,176,big)==2,"beacon enum/ref offsets");uint[] imports=new uint[40];for(int i=0;i<38;++i)imports[i]=(uint)(152-i*4);imports[38]=176;imports[39]=168;Offsets(misc.ImportsData,imports,big,"imports grouped root before childblock");Offsets(misc.RelocationData,new uint[]{160},big,"misc pointer");
        AssetBuffer actions=Test("ABI/actions",Root("AchievementEvent","EventId='17'","<Action TargetCount='9' Type='AWARD_ACHIEVEMENT_IMMEDIATE' ActionObjId='31'/><Action Type='UNKNOWN'/><Action/>"),p);Check(actions.InstanceData.Length==52&&Word(actions.InstanceData,4,big)==17&&Word(actions.InstanceData,8,big)==3&&Word(actions.InstanceData,12,big)==16,"achievement16 list12");Check(Word(actions.InstanceData,16,big)==9&&Word(actions.InstanceData,20,big)==1&&Word(actions.InstanceData,24,big)==31&&Word(actions.InstanceData,32,big)==3&&Word(actions.InstanceData,44,big)==0,"action offsets unknown fallback3 absent0");Offsets(actions.RelocationData,new uint[]{12},big,"action pointer");
        AssetBuffer map=Test("ABI/map",Root("MapNameToAchievementEventMapping","MapName='abc' AchievementEventId='42'",""),p);Check(map.InstanceData.Length==20&&Word(map.InstanceData,4,big)==3&&Word(map.InstanceData,8,big)==16&&Word(map.InstanceData,12,big)==42,"map16 string length/pointer");Check(map.InstanceData[16]==97&&map.InstanceData[17]==98&&map.InstanceData[18]==99&&map.InstanceData[19]==0,"map raw chars unaffected endian");Offsets(map.RelocationData,new uint[]{8},big,"map string pointer");
        AssetBuffer trigger=Test("ABI/trigger",Root("GameplayAchievementTriggerData","GameplayConditionData='C\\5' AchievementEventId='23'",""),p);Check(trigger.InstanceData.Length==12&&Word(trigger.InstanceData,4,big)==6&&Word(trigger.InstanceData,8,big)==23,"trigger12");Offsets(trigger.ImportsData,new uint[]{4},big,"trigger reference");
    }
    static void Matrix(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        string[][] scalars={new string[]{"SiegeEvaEvent","","Seconds"},new string[]{"EvaSettings","","EnemySightedMaxVoicePositionScanRange"},new string[]{"EvaSettings","","MaxTimeToKeepJumpToEvents"},new string[]{"EvaSettings","","MaxTimeBeforeResettingLastJumpTo"},new string[]{"EvaSettings","","MinDistanceBetweenJumpToEvents"},new string[]{"EvaEvent","","Priority"},new string[]{"EvaEvent","","TimeBetweenEvents"},new string[]{"EvaEvent","","ExpirationTime"},new string[]{"EvaEvent","","QuietTime"},new string[]{"EvaEvent","","TimeToWaitBeforePlaying"},new string[]{"AchievementEvent","","EventId"},new string[]{"AchievementEvent","Action","TargetCount"},new string[]{"AchievementEvent","Action","ActionObjId"},new string[]{"MapNameToAchievementEventMapping","","AchievementEventId"},new string[]{"GameplayAchievementTriggerData","","AchievementEventId"}};
        List<string> numbers=new List<string>(Numbers);numbers.AddRange(new string[]{"500ms","2s","1ms2s","1s2ms","-200ms","1MS","1S","NaN","INF","12\0s","+ 3","--1"});
        foreach(string[] f in scalars)foreach(string s in numbers)TestDoc("scalar/"+f[0]+"/"+f[2]+"/"+s.Length,Set(f[0],f[1],f[2],s),p);
        foreach(string attr in MiscReferences)foreach(string v in References)TestDoc("misc-reference/"+attr+"/"+v.Length,Set("MiscEvaEvents","",attr,v),p);
        string[][] rf={new string[]{"SiegeEvaEvent","","EvaEvent"},new string[]{"GameplayAchievementTriggerData","","GameplayConditionData"},new string[]{"EvaEvent","SideSound","Sound"},new string[]{"MiscEvaEvents","AIBeaconTypeSettings","PlacedEvent"}};
        foreach(string[] f in rf)foreach(string v in References)TestDoc("reference/"+f[0]+"/"+f[2]+"/"+v.Length,Set(f[0],f[1],f[2],v),p);
        foreach(string attr in new string[]{"AlwaysPlayFromHomeBase","CountAsJumpToLocation","SuppressOn360","SuppressOnPC"})foreach(string v in new string[]{"true","false","True","False","TRUE","FALSE","1","0","-1","t","T","yes",""," true","true\0false","1junk","2"})TestDoc("bool/"+attr+"/"+v.Length,Set("EvaEvent","",attr,v),p);
        string[][] ef={new string[]{"SiegeEvaEvent","","EventType"},new string[]{"AchievementEvent","Action","Type"},new string[]{"MiscEvaEvents","AIBeaconTypeSettings","BeaconType"}};
        foreach(string[] f in ef)foreach(string v in new string[]{"ALL","GENERIC","SIEGE","TRIGGER_EVENT","AWARD_ACHIEVEMENT_IMMEDIATE","AWARD_ACHIEVEMENT_BEFORE_POST_GAME_SHELL","AWARD_ACHIEVEMENT_AFTER_POST_GAME_SHELL","Assault","HoldObject","HoldPosition","Invalid","UNKNOWN","","0","-1","all","siege","trigger_event","holdobject"," ALL","ALL ","SIEGE\0UNKNOWN","TRIGGER_EVENT\0junk","HoldObject\0x"})TestDoc("enum/"+f[0]+"/"+v.Length,Set(f[0],f[1],f[2],v),p);
        List<string> strings=new List<string>(References);strings.AddRange(new string[]{"MixedCase","MIXEDCASE","mixedcase","a b"," a ","\0","abc\0tail","é","è","汉字","😀",new string('a',4097)});
        foreach(string v in strings){TestDoc("map-string/"+v.Length,Set("MapNameToAchievementEventMapping","","MapName",v),p);TestDoc("side-weak/"+v.Length,Set("EvaEvent","SideSound","Side",v),p);XmlDocument d=Parse(Root("EvaEvent","","<OtherEvaEventToBlock/>"));d.DocumentElement.FirstChild.InnerText=v;TestDoc("eva-weak/"+v.Length,d,p);}
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}){
            StringBuilder side=new StringBuilder(),weak=new StringBuilder(),beacon=new StringBuilder(),action=new StringBuilder();for(int i=0;i<count;++i){side.Append("<SideSound Sound='S\\"+(count-i)+"' Side='SIDE"+i+"'/>");weak.Append("<OtherEvaEventToBlock>Eva"+i+"</OtherEvaEventToBlock>");beacon.Append("<AIBeaconTypeSettings BeaconType='HoldPosition' PlacedEvent='E\\"+(count-i)+"'/>");action.Append("<Action TargetCount='"+i+"' Type='TRIGGER_EVENT' ActionObjId='"+(i+7)+"'/>");}
            AssetBuffer e=Test("growth/eva/"+count,Root("EvaEvent","",weak.ToString()+side),p);Check(e.InstanceData.Length==44+12*count&&Word(e.InstanceData,24,big)==count&&Word(e.InstanceData,32,big)==count,"eva exact list counts/growth");if(count>0)Check(Word(e.InstanceData,28,big)==44&&Word(e.InstanceData,36,big)==44+8*count,"eva allocation order");
            AssetBuffer m=Test("growth/misc/"+count,Root("MiscEvaEvents","",beacon.ToString()),p);Check(m.InstanceData.Length==164+8*count&&Word(m.InstanceData,156,big)==count,"misc exact growth");
            AssetBuffer a=Test("growth/action/"+count,Root("AchievementEvent","",action.ToString()),p);Check(a.InstanceData.Length==16+12*count&&Word(a.InstanceData,8,big)==count,"action exact growth");
        }
        foreach(string t in Types){Test("metadata/"+t,Root(t,"TypeId='1' inheritFrom='Ignored'",""),p);Test("comments/"+t,Root(t,"","<!--comment-->"),p);Reject("unknown attr",Root(t,"Unknown='1'",""));Reject("unknown child",Root(t,"","<Unknown/>"));Reject("text",Root(t,"","payload"));Reject("qualified attr",Root(t,"xmlns:q='urn:q' q:Priority='1'",""));Reject("wrong namespace",Root(t,"","").Replace(NS,"urn:wrong"));}
        foreach(string[] f in new string[][]{new string[]{"EvaEvent","SideSound"},new string[]{"EvaEvent","OtherEvaEventToBlock"},new string[]{"MiscEvaEvents","AIBeaconTypeSettings"},new string[]{"AchievementEvent","Action"}}){TestDoc("child metadata",Set(f[0],f[1],"TypeId","1"),p);Reject("unknown child attr",Set(f[0],f[1],"Unknown","1").OuterXml);Reject("unknown grandchild",Root(f[0],"","<"+f[1]+"><Unknown/></"+f[1]+">"));if(f[1]!="OtherEvaEventToBlock")Reject("child text",Root(f[0],"","<"+f[1]+">payload</"+f[1]+">"));Reject("foreign child",Root(f[0],"","<"+f[1]+" xmlns='urn:wrong'/>"));}
        Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlevaachievementCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration rejection");++rejections;
        XmlDocument mismatch=Parse(Root("EvaEvent","",""));InstanceDeclaration dec=Declaration(mismatch);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlevaachievementCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch rejection");++rejections;
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
            Console.WriteLine("PASS Eva/achievement fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

