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
class XmltacticalhotkeysOracle {
    const string NS="uri:ea.com:eala:asset";
    const string Selection="UIMouseTacticalSelectionDetailsButtonHotKeys";
    const string MainBar="UIMouseTacticalMainCommandBarHotKeys";
    const string Player="UIMouseTacticalPlayerPowerHotKeys";
    const string Misc="UIMouseTacticalMiscHotKeys";
    const string Gate="UIMouseHUDElementHotKeyGate";
    static readonly string[] Types={Selection,MainBar,Player,Misc,Gate};
    static readonly uint[] TypeIds={1296329066u,2591692061u,2283998413u,2014061824u,2738273474u};
    static readonly int[] Sizes={12,24,12,200,12};
    static readonly string[] MiscNames=new string[]{"AllCheer","AttackMove","CameraReset","ChatWithAllies","ChatWithBuddies","ChatWithEveryone","CommunicateWithAlliesMode","CreateFormation","CycleHarvesters","DeleteBeacon","ForceAttack","ForceAttackMove","ForceMove","GoToNextBuildQueue","GoToPriorBuildQueue","GoToNextSubGroup","GoToPriorSubGroup","OpenAdvancedCommands","OpenLoadGameScreen","OpenMessenger","OpenPauseScreen","OpenPlayerTechStore","OpenSaveGameScreen","OpenStatusScreen","PlaceRallyPoint","PlanningMode","PreferSelection","QuickChatMenu","ReverseMove","Scatter","SelectAll","SelectMatchingUnits","SellMode","ShowAllHealthBars","Stop","TelestratorErase","TelestratorNextColor","TelestratorNextLineWidth","TelestratorPriorColor","TelestratorToggle","ToggleFastForwardMode","ToggleHUD","TogglePowerMode","ToggleRepairMode","ViewHomeBase","ViewLastEvaEvent","WaypointMode"};
    static readonly string[] ElementValues=new string[]{"AggressiveStanceButton","AircraftTab","AttackMoveButton","CoOpAIAssaultOrderButton","CoOpAIHoldObjectOrderButton","CoOpAIHoldPositionOrderButton","CoOpAINormalOrderButton","CoOpAIOrderPanel","DeactivateHotSpotButton","EnergyMeter","ForceAttackButton","ForceAttackMoveButton","ForceMoveButton","GuardStanceButton","HoldFireStanceButton","HoldGroundStanceButton","InfantryTab","Invalid","MainStructuresTab","OpenAdvancedCommandsButton","OpenPlayerTechStoreButton","OtherStructuresTab","PauseButton","PlanningModeButton","Radar","RepairButton","Resources","ReverseMoveButton","SelectionRefinementTab","SellButton","StopButton","ThreatMeter","UnitIntroButton","VehicleTab","WatercraftTab","WaypointModeButton","","UNKNOWN","0","-1","lowercase"," AggressiveStanceButton","AggressiveStanceButton ","aggressivestancebutton"};
    static readonly string[] QueueValues=new string[]{"AIRCRAFT","INFANTRY","INVALID","MAIN_STRUCTURE","OTHER_STRUCTURE","UPGRADE","VEHICLE","WATERCRAFT","","UNKNOWN","0","-1","lowercase"," AIRCRAFT","AIRCRAFT ","aircraft"};
    static readonly string[] StanceValues=new string[]{"AGGRESSIVE","GUARD","HOLD_FIRE","HOLD_POSITION","","UNKNOWN","0","-1","lowercase"," AGGRESSIVE","AGGRESSIVE ","aggressive"};
    static readonly string[] References={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483647","Name\\2147483648","Name\\-2147483648","Name\\-2147483649","Name\\4294967295","Name\\9999999999999999999999","Name\\ +17junk","Name\\garbage","Name\\2\\3","Name\0\\7","Name\\7\0tail","\0Name\\7","资源\\7","😀\\7","MIXEDCase","  Name\\7  "};
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
            Type type=typeof(XmltacticalhotkeysCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmltacticalhotkeysCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static XmlDocument Set(string root,string path,string attribute,string value) {
        XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;
        if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement child=d.CreateElement(part,NS);node.AppendChild(child);node=child;}
        if(attribute=="#text")node.InnerText=value;else node.SetAttribute(attribute,value);return d;
    }
    static void Offsets(byte[] array,uint[] expected,bool big,string label){
        Check(array.Length==(expected.Length==0?0:4*(expected.Length+1)),label+" size");
        for(int i=0;i<expected.Length;++i)Check(Word(array,4*i,big)==expected[i],label+" exact block/value order "+i);
        if(expected.Length!=0)Check(Word(array,4*expected.Length,big)==UInt32.MaxValue,label+" sentinel");
    }
    static void Abi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        foreach(string root in new string[]{Selection,Player}) {
            string name=root==Selection?"UnitAbilitySlot":"Slot";
            AssetBuffer b=Test("ABI/ref-list/"+root,Root(root,"","<"+name+">K\\3</"+name+"><"+name+">K\\1</"+name+"><"+name+">K\\2</"+name+">"),p);
            Check(b.InstanceData.Length==24&&Word(b.InstanceData,4,big)==3&&Word(b.InstanceData,8,big)==12,"ref-list root/count/target");
            Check(Word(b.InstanceData,12,big)==4&&Word(b.InstanceData,16,big)==2&&Word(b.InstanceData,20,big)==3,"ref-list index+1");
            Offsets(b.ImportsData,new uint[]{16,20,12},big,"ref-list non-address-sorted imports");
            Offsets(b.RelocationData,new uint[]{8},big,"ref-list relocation");
        }
        AssetBuffer main=Test("ABI/main-schema-allocation-order",Root(MainBar,"SelectionRefinementPageSlot='K\\9'",
            "<ButtonSlot>K\\7</ButtonSlot><BuildQueuePage QueueType='INFANTRY' Slot='K\\11'/><ButtonSlot>K\\0</ButtonSlot><BuildQueuePage QueueType='UNKNOWN' Slot='K\\3'/><ButtonSlot>K\\2</ButtonSlot>"),p);
        Check(main.InstanceData.Length==52&&Word(main.InstanceData,4,big)==10,"main root/directref");
        Check(Word(main.InstanceData,8,big)==2&&Word(main.InstanceData,12,big)==24&&Word(main.InstanceData,16,big)==3&&Word(main.InstanceData,20,big)==40,"main schema-ordered list allocation independentofXML");
        Check(Word(main.InstanceData,24,big)==2&&Word(main.InstanceData,32,big)==4,"queue enum andunknown fallback");
        Check(Word(main.InstanceData,28,big)==12&&Word(main.InstanceData,36,big)==4&&Word(main.InstanceData,40,big)==8&&Word(main.InstanceData,44,big)==1&&Word(main.InstanceData,48,big)==3,"main all reference offsets");
        Offsets(main.ImportsData,new uint[]{4,36,28,44,48,40},big,"main rootblock-before-lists/value-sorted imports");
        Offsets(main.RelocationData,new uint[]{12,20},big,"main target-block-sorted relocations");
        StringBuilder attrs=new StringBuilder();for(int i=0;i<MiscNames.Length;++i)attrs.Append(MiscNames[i]+"='K\\"+(100-i)+"' ");
        AssetBuffer misc=Test("ABI/misc-all47",Root(Misc,attrs.ToString(),"<StanceSettings Stance='AGGRESSIVE' Slot='K\\99'/><StanceSettings Stance='UNKNOWN' Slot='K\\1'/><StanceSettings Stance='HOLD_FIRE' Slot='K\\7'/><StanceSettings Slot='K\\3'/>"),p);
        Check(misc.InstanceData.Length==232&&Word(misc.InstanceData,192,big)==4&&Word(misc.InstanceData,196,big)==200,"misc exact200-root/8-byte list");
        uint[] imports=new uint[51];for(int i=0;i<47;++i){Check(Word(misc.InstanceData,4+4*i,big)==101-i,"misc47 field offset/index+1");imports[i]=(uint)(188-4*i);}
        imports[47]=212;imports[48]=228;imports[49]=220;imports[50]=204;
        Offsets(misc.ImportsData,imports,big,"misc descendingrootrefs then stance listblock sortedbyvalue");
        Offsets(misc.RelocationData,new uint[]{196},big,"misc list relocation");
        Check(Word(misc.InstanceData,200,big)==1&&Word(misc.InstanceData,208,big)==1&&Word(misc.InstanceData,216,big)==3&&Word(misc.InstanceData,224,big)==0,"stance known/unknown/missing distinction");
        AssetBuffer gate=Test("ABI/gate",Root(Gate,"","<ElementHotKeyMapping ElementId='Resources' HotKeySlot='K\\9'/><ElementHotKeyMapping ElementId='UNKNOWN' HotKeySlot='K\\3'/><ElementHotKeyMapping HotKeySlot='K\\7'/>"),p);
        Check(gate.InstanceData.Length==36&&Word(gate.InstanceData,4,big)==3&&Word(gate.InstanceData,8,big)==12,"gate root/list");
        Check(Word(gate.InstanceData,12,big)==1&&Word(gate.InstanceData,20,big)==21&&Word(gate.InstanceData,28,big)==0,"gate known/unknown/missing distinction");
        Offsets(gate.ImportsData,new uint[]{24,32,16},big,"gate value-sorted imports");
        Offsets(gate.RelocationData,new uint[]{8},big,"gate relocation");
        AssetBuffer absent=Test("ABI/queue-missing",Root(MainBar,"","<BuildQueuePage/>"),p);
        Check(Word(absent.InstanceData,24,big)==0&&absent.ImportsData.Length==0,"missing queue stayszero andmissingref doesnotimport");
    }
    static void Growth(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        foreach(int count in new int[]{0,1,2,3,15,16,17,27,28,29,64,257,1024}) {
            StringBuilder selection=new StringBuilder(),player=new StringBuilder(),queues=new StringBuilder(),buttons=new StringBuilder(),stances=new StringBuilder(),mappings=new StringBuilder();
            for(int i=0;i<count;++i){
                int value=(i*37)%1009;
                selection.Append("<UnitAbilitySlot>K\\"+value+"</UnitAbilitySlot>");player.Append("<Slot>K\\"+value+"</Slot>");
                queues.Append("<BuildQueuePage QueueType='WATERCRAFT' Slot='K\\"+value+"'/>");buttons.Append("<ButtonSlot>K\\"+(1009-value)+"</ButtonSlot>");
                stances.Append("<StanceSettings Stance='HOLD_POSITION' Slot='K\\"+value+"'/>");mappings.Append("<ElementHotKeyMapping ElementId='Invalid' HotKeySlot='K\\"+value+"'/>");
            }
            AssetBuffer s=Test("selection-growth/"+count,Root(Selection,"",selection.ToString()),p),pw=Test("power-growth/"+count,Root(Player,"",player.ToString()),p),g=Test("gate-growth/"+count,Root(Gate,"",mappings.ToString()),p),m=Test("misc-growth/"+count,Root(Misc,"",stances.ToString()),p),main=Test("main-growth/"+count,Root(MainBar,"",buttons.ToString()+queues.ToString()),p);
            Check(s.InstanceData.Length==12+4*count&&pw.InstanceData.Length==12+4*count,"two reference lists exactgrowth");
            Check(g.InstanceData.Length==12+8*count&&m.InstanceData.Length==200+8*count,"two enum/ref lists exactgrowth");
            Check(main.InstanceData.Length==24+12*count&&Word(main.InstanceData,8,big)==count&&Word(main.InstanceData,16,big)==count,"main two-list exactgrowth");
            if(count!=0)Check(Word(main.InstanceData,12,big)==24&&Word(main.InstanceData,20,big)==24+8*count,"main allocationorder atall growthboundaries");
            Check(s.ImportsData.Length==(count==0?0:4*(count+1))&&main.ImportsData.Length==(count==0?0:4*(2*count+1)),"bookmark capacity boundaries preserved");
        }
        foreach(int[] counts in new int[][]{new int[]{0,1},new int[]{1,0},new int[]{2,3},new int[]{3,2},new int[]{17,29},new int[]{29,17}}) {
            StringBuilder body=new StringBuilder();for(int i=0;i<Math.Max(counts[0],counts[1]);++i){
                if(i<counts[1])body.Append("<ButtonSlot>K\\"+(50-i)+"</ButtonSlot>");
                if(i<counts[0])body.Append("<BuildQueuePage QueueType='UPGRADE' Slot='K\\"+i+"'/>");
            }
            AssetBuffer b=Test("independent-list-counts/"+counts[0]+"/"+counts[1],Root(MainBar,"",body.ToString()),p);
            Check(b.InstanceData.Length==24+8*counts[0]+4*counts[1],"independent main lists exactsize");
            if(counts[1]!=0)Check(Word(b.InstanceData,20,big)==24+8*counts[0],"button list allocationafterqueue evenwhenXMLfirst");
        }
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i) {
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown root attr",Root(Types[i],"Unknown='1'",""));Reject("qualified root attr",Root(Types[i],"xmlns:q='urn:q' q:TypeId='1'",""));
            Reject("unknown child",Root(Types[i],"","<Unknown/>"));Reject("foreign child",Root(Types[i],"","<Slot xmlns='urn:q'/>"));Reject("root text",Root(Types[i],"","payload"));
        }
        foreach(string name in MiscNames)foreach(string value in References)TestDoc("misc-ref/"+name+"/"+value.Length,Set(Misc,"",name,value),p);
        foreach(string value in References) {
            TestDoc("main-directref/"+value.Length,Set(MainBar,"","SelectionRefinementPageSlot",value),p);
            TestDoc("queue-ref/"+value.Length,Set(MainBar,"BuildQueuePage","Slot",value),p);
            TestDoc("stance-ref/"+value.Length,Set(Misc,"StanceSettings","Slot",value),p);
            TestDoc("gate-ref/"+value.Length,Set(Gate,"ElementHotKeyMapping","HotKeySlot",value),p);
            TestDoc("selection-ref/"+value.Length,Set(Selection,"UnitAbilitySlot","#text",value),p);
            TestDoc("power-ref/"+value.Length,Set(Player,"Slot","#text",value),p);
            TestDoc("button-ref/"+value.Length,Set(MainBar,"ButtonSlot","#text",value),p);
        }
        foreach(string value in ElementValues)TestDoc("element-enum/"+value,Set(Gate,"ElementHotKeyMapping","ElementId",value),p);
        foreach(string value in QueueValues)TestDoc("queue-enum/"+value,Set(MainBar,"BuildQueuePage","QueueType",value),p);
        foreach(string value in StanceValues)TestDoc("stance-enum/"+value,Set(Misc,"StanceSettings","Stance",value),p);
        foreach(string value in new string[]{"AIRCRAFT\0junk","UNKNOWN\0INFANTRY","\0INFANTRY"})TestDoc("queue-native-NUL",Set(MainBar,"BuildQueuePage","QueueType",value),p);
        foreach(string value in new string[]{"AGGRESSIVE\0junk","UNKNOWN\0GUARD","\0GUARD"})TestDoc("stance-native-NUL",Set(Misc,"StanceSettings","Stance",value),p);
        foreach(string value in new string[]{"Radar\0junk","UNKNOWN\0Radar","\0Radar"})TestDoc("element-native-NUL",Set(Gate,"ElementHotKeyMapping","ElementId",value),p);
        string[][] nested={new string[]{Selection,"UnitAbilitySlot"},new string[]{Player,"Slot"},new string[]{MainBar,"ButtonSlot"},new string[]{MainBar,"BuildQueuePage"},new string[]{Misc,"StanceSettings"},new string[]{Gate,"ElementHotKeyMapping"}};
        foreach(string[] row in nested) {
            TestDoc("nested metadata/"+row[1],Set(row[0],row[1],"TypeId","1"),p);
            Reject("unknown nestedattr",Set(row[0],row[1],"Unknown","1").OuterXml);
            XmlDocument foreign=Set(row[0],row[1],"TypeId","1");XmlAttribute q=foreign.CreateAttribute("q","Unknown","urn:q");q.Value="1";foreign.DocumentElement.FirstChild.Attributes.Append(q);Reject("qualified nestedattr",foreign.OuterXml);
            Reject("nested child",Root(row[0],"","<"+row[1]+"><Unknown/></"+row[1]+">"));
        }
        foreach(string[] row in new string[][]{new string[]{MainBar,"BuildQueuePage"},new string[]{Misc,"StanceSettings"},new string[]{Gate,"ElementHotKeyMapping"}})Reject("struct text",Root(row[0],"","<"+row[1]+">payload</"+row[1]+">"));
        Reject("unknown root",Root("Unknown","",""));
        bool nullInput=false;try{XmltacticalhotkeysCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument wrong=Parse(Root(Selection,"",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName=Player;
        bool mismatch=false;try{XmltacticalhotkeysCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch rejected");++rejections;
        var unboundSchemaResult=XmltacticalhotkeysCompiler.CompileXml(wrong.DocumentElement,new XmlNamespaceManager(wrong.NameTable),false);
        var boundSchemaResult=XmltacticalhotkeysCompiler.CompileXml(wrong.DocumentElement,Namespaces(wrong),false);
        Check(Convert.ToBase64String(unboundSchemaResult.InstanceData)==Convert.ToBase64String(boundSchemaResult.InstanceData) &&
            Convert.ToBase64String(unboundSchemaResult.RelocationData)==Convert.ToBase64String(boundSchemaResult.RelocationData) &&
            Convert.ToBase64String(unboundSchemaResult.ImportsData)==Convert.ToBase64String(boundSchemaResult.ImportsData),"plugin does not repeat Core namespace validation");++layouts;
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
            Console.WriteLine("PASS Tactical hotkeys fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

