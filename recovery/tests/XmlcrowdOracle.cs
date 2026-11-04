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
class XmlcrowdOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"CrowdResponse"};
    static readonly uint[] TypeIds={400896388u};
    static readonly int[] Sizes={16};
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
            Type type=typeof(XmlcrowdCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlcrowdCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

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
    static readonly string[] VoiceNames={"voiceAlert","voiceAttack","voiceAttackAfterMoving","voiceAttackAir","voiceAttackCharge","voiceAttackGarrisonedStructure","voiceAttackMachine","voiceAttackStructure","voiceBombard","voiceBuildResponse","voiceCaptureBuildingComplete","voiceCombatDrop","voiceCombineWithHorde","voiceCreated","voiceDefectingAway","voiceDefectingTowards","voiceDeliverRing","voiceDesperateAttack","voiceDock","voiceEnter","voiceEnterHostile","voiceEnterStateAttack","voiceEnterStateAttackAfterMoving","voiceEnterStateAttackAir","voiceEnterStateAttackCharge","voiceEnterStateAttackGarrisonedStructure","voiceEnterStateAttackMachine","voiceEnterStateAttackStructure","voiceEnterStateMove","voiceEnterStateMoveLandToWater","voiceEnterStateMoveOverWalls","voiceEnterStateMoveToCamp","voiceEnterStateMoveToHigherGround","voiceEnterStateMoveWaterToLand","voiceEnterStateMoveWhileAttacking","voiceEnterStateRetreatToCastle","voiceFear","voiceFullyCreated","voiceGarrison","voiceGetHealed","voiceGuard","voiceLowFuel","voiceMove","voiceMoveLandToWater","voiceMoveOverWalls","voiceMoveToCamp","voiceMoveToHigherGround","voiceMoveWaterToLand","voiceMoveWhileAttacking","voiceNoBuild","voicePrimaryWeaponMode","voiceRapidFire","voiceRepair","voiceRetreatToCastle","voiceSalvage","voiceSecondaryWeaponMode","voiceSelect","voiceSelectBattle","voiceSelectIdleWorker","voiceSelectUnderConstruction","voiceSelectUnderFire","voiceSendToSlaughterhouse","voiceStartCharging","voiceSupply","voiceTaskComplete","voiceTertiaryWeaponMode","voiceUnload"};
    static readonly int[] VoiceValues={32,17,23,22,18,20,21,19,25,43,47,38,27,15,29,30,13,24,35,39,40,52,58,54,53,56,57,55,59,65,61,63,60,66,64,62,26,16,42,41,31,51,5,9,7,12,6,10,8,50,44,48,37,11,33,45,0,2,3,1,4,14,49,34,28,46,36};
    static readonly string[] ObjectVoiceNames={"voiceAttackUnit","voiceCreatedFrom","voiceEnterStateAttackUnit","voiceEnterStateEnterUnit","voiceEnterUnit","voiceFullyCreatedFrom"};
    static readonly int[] ObjectVoiceValues={0,4,1,3,2,5};
    const string Crowd="CrowdResponse",ArrayPath="Threshold/AudioArrayVoice";
    static string VoicePath(string entry){return ArrayPath+"/"+entry;}
    static string Threshold(string attrs,string voice){return "<Threshold "+attrs+">"+voice+"</Threshold>";}
    static string VoiceArray(string children){return "<AudioArrayVoice>"+children+"</AudioArrayVoice>";}
    static void Abi(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        AssetBuffer d=Test("ABI/default",Root(Crowd,"",""),p);Check(d.InstanceData.Length==16&&Word(d.InstanceData,4,big)==100&&Word(d.InstanceData,8,big)==0&&Word(d.InstanceData,12,big)==0,"root16 default weight100");Offsets(d.RelocationData,new uint[0],big,"default no pointers");Offsets(d.ImportsData,new uint[0],big,"default no imports");
        AssetBuffer t=Test("ABI/threshold",Root(Crowd,"Weight='-7'",Threshold("NumUnits='-3'","")),p);Check(t.InstanceData.Length==44&&Word(t.InstanceData,4,big)==unchecked((uint)-7)&&Word(t.InstanceData,8,big)==1&&Word(t.InstanceData,12,big)==16&&Word(t.InstanceData,16,big)==unchecked((uint)-3),"threshold28 inline voice24");for(int at=20;at<44;at+=4)Check(Word(t.InstanceData,at,big)==0,"absent voice zero");Offsets(t.RelocationData,new uint[]{12},big,"threshold pointer");
        AssetBuffer f=Test("ABI/complete",Root(Crowd,"Weight='13'",Threshold("NumUnits='9'",VoiceArray("<NamedEntry Sound='S\\5' EvaEvent='E\\1' Name='MixedCase'/><ObjectSpecificEntry Sound='S\\7' EvaEvent='E\\3' AudioType='voiceEnterUnit' TargetObject='MixedCase'/><AudioEntry Sound='S\\9' EvaEvent='E\\5' AudioType='voiceAttack'/>"))),p);
        Check(f.InstanceData.Length==96&&Word(f.InstanceData,12,big)==16&&Word(f.InstanceData,16,big)==9,"full root and threshold");int[] ats={20,28,36};uint[] targets={44,60,80};for(int i=0;i<3;++i)Check(Word(f.InstanceData,ats[i],big)==1&&Word(f.InstanceData,ats[i]+4,big)==targets[i],"three groups marshalorder not XMLorder");
        Check(Word(f.InstanceData,44,big)==56&&Word(f.InstanceData,48,big)==6&&Word(f.InstanceData,52,big)==17&&Word(f.InstanceData,56,big)==10,"AudioEntry12 nullable sound4 ref and voice enum");Check(Word(f.InstanceData,60,big)==76&&Word(f.InstanceData,64,big)==4&&Word(f.InstanceData,68,big)==2&&Word(f.InstanceData,76,big)==8,"ObjectEntry16 sound4 and enum");Check(Word(f.InstanceData,80,big)==92&&Word(f.InstanceData,84,big)==2&&Word(f.InstanceData,92,big)==6,"NamedEntry12 sound4");Check(Word(f.InstanceData,72,big)!=0&&Word(f.InstanceData,88,big)!=Word(f.InstanceData,72,big),"Named raw hash differs weak lowercase TargetObject");Offsets(f.RelocationData,new uint[]{12,24,32,40,44,60,80},big,"ownerblock relocation grouping");Offsets(f.ImportsData,new uint[]{48,56,64,76,84,92},big,"ownerblock imports not global index order");
        foreach(string tag in new string[]{"AudioEntry","ObjectSpecificEntry","NamedEntry"}){AssetBuffer a=Test("ABI/empty entry/"+tag,Root(Crowd,"",Threshold("",VoiceArray("<"+tag+"/>"))),p);int size=tag=="ObjectSpecificEntry"?16:12;Check(a.InstanceData.Length==44+size,"empty entry exact size");for(int at=44;at<44+size;at+=4)Check(Word(a.InstanceData,at,big)==0,"absent fields stay0");AssetBuffer present=Test("ABI/empty sound/"+tag,Root(Crowd,"",Threshold("",VoiceArray("<"+tag+" Sound=''/>") )),p);Check(present.InstanceData.Length==48+size&&Word(present.InstanceData,44,big)==44+size&&Word(present.InstanceData,44+size,big)==0,"presentempty Sound allocates nullable ref4");}
    }
    static void Matrix(TargetPlatform p){bool big=p!=TargetPlatform.Win32;
        foreach(string attr in new string[]{"Weight","NumUnits"})foreach(string v in Numbers)TestDoc("signed/"+attr,Set(Crowd,attr=="Weight"?"":"Threshold",attr,v),p);
        for(int i=0;i<VoiceNames.Length;++i)foreach(string v in new string[]{VoiceNames[i],VoiceNames[i].ToUpperInvariant(),VoiceNames[i]+"\0tail"," "+VoiceNames[i],VoiceNames[i]+" "}){AssetBuffer b=TestDoc("voice enum/"+i,Set(Crowd,VoicePath("AudioEntry"),"AudioType",v),p);Check(Word(b.InstanceData,52,big)==(uint)(v==VoiceNames[i]||v==VoiceNames[i]+"\0tail"?VoiceValues[i]:32),"independent voice value/fallback");}
        for(int i=0;i<ObjectVoiceNames.Length;++i)foreach(string v in new string[]{ObjectVoiceNames[i],ObjectVoiceNames[i].ToUpperInvariant(),ObjectVoiceNames[i]+"\0tail"," "+ObjectVoiceNames[i],ObjectVoiceNames[i]+" "}){AssetBuffer b=TestDoc("object voice enum/"+i,Set(Crowd,VoicePath("ObjectSpecificEntry"),"AudioType",v),p);Check(Word(b.InstanceData,52,big)==(uint)(v==ObjectVoiceNames[i]||v==ObjectVoiceNames[i]+"\0tail"?ObjectVoiceValues[i]:0),"independent object voice value/fallback");}
        foreach(string tag in new string[]{"AudioEntry","ObjectSpecificEntry"})foreach(string v in new string[]{"","UNKNOWN","0","-1","voiceAttack","VOICEATTACK","voiceattack","voiceEnterUnit","voiceAttack\0tail","\0voiceAttack"})TestDoc("enum edge",Set(Crowd,VoicePath(tag),"AudioType",v),p);
        foreach(string tag in new string[]{"AudioEntry","ObjectSpecificEntry","NamedEntry"})foreach(string attr in new string[]{"Sound","EvaEvent"})foreach(string v in References)TestDoc("reference/"+tag+"/"+attr,Set(Crowd,VoicePath(tag),attr,v),p);
        List<string> text=new List<string>(References);text.AddRange(new string[]{"MixedCase","MIXEDCASE","mixedcase","A B"," ABC ","ABC\0tail","\0ABC","é","东京","😀",new string('x',4097)});foreach(string v in text){TestDoc("weak target",Set(Crowd,VoicePath("ObjectSpecificEntry"),"TargetObject",v),p);TestDoc("raw name",Set(Crowd,VoicePath("NamedEntry"),"Name",v),p);}
        foreach(int count in new int[]{0,1,2,3,7,15,16,17,27,28,29,64,257,1024}){StringBuilder ts=new StringBuilder();for(int i=0;i<count;++i)ts.Append(Threshold("NumUnits='"+i+"'",VoiceArray("")));AssetBuffer b=Test("threshold growth",Root(Crowd,"",ts.ToString()),p);Check(b.InstanceData.Length==16+28*count&&Word(b.InstanceData,8,big)==count,"threshold28 exact growth");foreach(string tag in new string[]{"AudioEntry","ObjectSpecificEntry","NamedEntry"}){StringBuilder entries=new StringBuilder();for(int i=0;i<count;++i)entries.Append("<"+tag+" Sound='S\\"+(count-i)+"' EvaEvent='E\\"+i+"'/>");AssetBuffer a=Test("entry growth/"+tag,Root(Crowd,"",Threshold("",VoiceArray(entries.ToString()))),p);int size=tag=="ObjectSpecificEntry"?16:12;Check(a.InstanceData.Length==44+(size+4)*count,"entry and nullable4 exact growth");int offset=tag=="AudioEntry"?20:tag=="ObjectSpecificEntry"?28:36;Check(Word(a.InstanceData,offset,big)==count,"entry group count");}}
        // Every threshold marshals its three nested groups before the next threshold.
        string nested=Threshold("NumUnits='1'",VoiceArray("<NamedEntry Sound='S\\9'/><AudioEntry Sound='S\\1'/><ObjectSpecificEntry Sound='S\\3'/>")+"");Test("two threshold nested allocation",Root(Crowd,"",nested+nested),p);
        Test("metadata",Root(Crowd,"TypeId='1' inheritFrom='Ignored'","<!--c-->"),p);Reject("unknown rootattr",Root(Crowd,"Unknown='1'",""));Reject("unknown rootchild",Root(Crowd,"","<Unknown/>"));Reject("root text",Root(Crowd,"","payload"));Reject("foreign root",Root(Crowd,"","").Replace(NS,"urn:wrong"));Reject("qualified rootattr",Root(Crowd,"xmlns:q='urn:q' q:Weight='1'",""));
        foreach(string path in new string[]{"Threshold",ArrayPath,VoicePath("AudioEntry"),VoicePath("ObjectSpecificEntry"),VoicePath("NamedEntry")}){TestDoc("nested metadata",Set(Crowd,path,"TypeId","1"),p);Reject("nested unknown attr",Set(Crowd,path,"Unknown","1").OuterXml);XmlDocument d=Set(Crowd,path,"TypeId","1");XmlNode n=d.DocumentElement;foreach(string tag in path.Split('/'))n=n.FirstChild;n.InnerText="payload";Reject("nested text",d.OuterXml);n.InnerText="";n.AppendChild(d.CreateElement("Unknown",NS));Reject("nested unknown child",d.OuterXml);}
        Reject("duplicate voice singleton",Root(Crowd,"",Threshold("",VoiceArray("")+VoiceArray(""))));Reject("unknown root",Root("Unknown","",""));bool rejected=false;try{XmlcrowdCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration");++rejections;XmlDocument wrong=Parse(Root(Crowd,"",""));InstanceDeclaration dec=Declaration(wrong);dec.Handle.TypeName="EvaSettings";rejected=false;try{XmlcrowdCompiler.ProcessInstance(dec,p);}catch(NotSupportedException){rejected=true;}Check(rejected,"handle mismatch");++rejections;Abi(p);
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
            Console.WriteLine("PASS crowd fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

