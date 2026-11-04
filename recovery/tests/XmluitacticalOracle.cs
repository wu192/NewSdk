using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
partial class XmluitacticalOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"UIMouseTacticalCoOpAIButtonsHelpButton","UIMouseTacticalCommandCommunicateWithAllies","UIMouseTacticalToggleRepairModeButton","UIMouseTacticalAttackMoveButton","UIMouseTacticalCommandPlaceWall","UIMouseTacticalForceAttackMoveButton","UIMouseTacticalCoOpAIButtonsHoldObjectButton","UIMouseTacticalExitContainerButton","UIMouseTacticalPlayerTechStoreButton","UIMouseTacticalCommandMove","UIMouseTacticalCommandSell","UIMouseTacticalUnitIntroButton","UIMouseTacticalCommandSelect","UIMouseTacticalCoOpAIButtonsNormalButton","UIMouseTacticalCommandPlaceRallyPoint","UIMouseTacticalCommandHarvest","UIMouseTacticalCommandToggleSelfRepair","UIMouseTacticalBuildQueueTab","UIMouseTacticalCommandGetCrate","UIMouseTacticalDeactivateMissionHotSpotsButton","UIMouseTacticalCommandForceAttack","UIMouseTacticalPlanningModeButton","UIMouseTacticalCommandMisc","UIMouseTacticalSelectionRefinementTab","UIMouseTacticalCommandForceMove","UIMouseTacticalOpenAdvancedCommandsButton","UIMouseTacticalSetStanceButton","UIMouseTacticalCoOpAIButtonsHoldPositionButton","UIMouseTacticalCommandMineOre","UIMouseTacticalWaypointModeButton","UIMouseTacticalReverseMoveButton","UIMouseTacticalCommandEnter","UIMouseTacticalCoOpAIButtonsAssaultButton","UIMouseTacticalPauseButton","UIMouseTacticalCommandAttack","UIMouseTacticalCommandAttackMove","UIMouseTacticalForceMoveButton","UIMouseTacticalForceAttackButton","UIMouseTacticalCommandPlaceAIBeacon","UIMouseTacticalCommandDock","UIMouseTacticalCommandReverseMove","UIMouseTacticalBuildQueueTypeTab","UIMouseTacticalSellModeButton","UIMouseTacticalStopButton","UIMouseTacticalCommandSetRallyPoint","UIMouseTacticalFastForwardButton","UIMouseTacticalGroupSelection","UIMouseTacticalLookAt","UIMouseTacticalRadarTelestrator","UIMouseTacticalTelestrator","UIMouseTacticalThreatMeter"};
    static readonly int[] Sizes={36,20,36,36,8,36,36,20,40,12,20,36,16,36,8,8,12,12,8,36,12,36,16,36,8,36,12,36,8,36,36,20,36,36,32,12,36,36,12,8,8,12,36,36,8,36,12,60,12,8,36};
    static readonly uint[] TypeIds={104126924u,113029208u,346077409u,442957564u,485977567u,694838069u,772462768u,1014034471u,1138847078u,1234485619u,1249197161u,1277109486u,1334543376u,1371977384u,1474300054u,1499648070u,1956336137u,1974922393u,2067763016u,2047841880u,2074164365u,2278083954u,2505151835u,2635272840u,2697587777u,2808773267u,2873949396u,2914137727u,2957071942u,2958905921u,2987234945u,3057018816u,3094553884u,3420020972u,3548787763u,3575834349u,3716369729u,3756168709u,3809842710u,3976885676u,4026878150u,4094578721u,4102242385u,4140422551u,4180354873u,4232364483u,241329492u,1795213437u,3366599054u,1921726501u,4143989686u};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7","MIXEDCase"};
    static readonly string[] Bools={"true","false","TRUE","1","0","t","T"," true","yes","","10"};
    static readonly string[] TimeValues={"0s","1s","250ms","1.25s","0.5m","2d1s","-1s","1s junk","2ms","1000ms","garbage","","1e30s","2147483648ms","25,5s"};
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
            Type type=typeof(XmluitacticalCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmluitacticalCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}
    static XmlDocument FieldDocument(string root,string path,string name,string value){XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement child=d.CreateElement(part,NS);node.AppendChild(child);node=child;}node.SetAttribute(name,value);return d;}
    static readonly string[][] Fields={
        new string[]{"UIMouseTacticalCoOpAIButtonsHelpButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHelpButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHelpButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHelpButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandCommunicateWithAllies","","PlaceUnattachedBeaconCursor","Reference"},
        new string[]{"UIMouseTacticalCommandCommunicateWithAllies","","PlaceAttachedBeaconCursor","Reference"},
        new string[]{"UIMouseTacticalCommandCommunicateWithAllies","","PingRadarCursor","Reference"},
        new string[]{"UIMouseTacticalToggleRepairModeButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalToggleRepairModeButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalToggleRepairModeButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalToggleRepairModeButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalAttackMoveButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalAttackMoveButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalAttackMoveButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalAttackMoveButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandPlaceWall","","ValidPlacementCursor","Reference"},
        new string[]{"UIMouseTacticalForceAttackMoveButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalForceAttackMoveButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalForceAttackMoveButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalForceAttackMoveButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldObjectButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldObjectButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldObjectButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldObjectButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalExitContainerButton","","HelpTitleFormat","String"},
        new string[]{"UIMouseTacticalExitContainerButton","","HelpDescription","String"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","","TechPointAwardedSound","NullableReference"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandMove","","ForwardCursor","Reference"},
        new string[]{"UIMouseTacticalCommandMove","","ReverseCursor","Reference"},
        new string[]{"UIMouseTacticalCommandSell","DefaultCursors","ValidTarget","Reference"},
        new string[]{"UIMouseTacticalCommandSell","DefaultCursors","InvalidTarget","Reference"},
        new string[]{"UIMouseTacticalCommandSell","FactionCursors","Faction","Enum:FactionType"},
        new string[]{"UIMouseTacticalCommandSell","FactionCursors/Cursors","ValidTarget","Reference"},
        new string[]{"UIMouseTacticalCommandSell","FactionCursors/Cursors","InvalidTarget","Reference"},
        new string[]{"UIMouseTacticalUnitIntroButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalUnitIntroButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalUnitIntroButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalUnitIntroButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandSelect","","Cursor","Reference"},
        new string[]{"UIMouseTacticalCommandSelect","","TakeUnitsFromAllyHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalCommandSelect","","GiveUnitsToAllyHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalCoOpAIButtonsNormalButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsNormalButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsNormalButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsNormalButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandPlaceRallyPoint","","Cursor","Reference"},
        new string[]{"UIMouseTacticalCommandHarvest","","Cursor","Reference"},
        new string[]{"UIMouseTacticalCommandToggleSelfRepair","","ValidTargetCursor","Reference"},
        new string[]{"UIMouseTacticalCommandToggleSelfRepair","","InvalidTargetCursor","Reference"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings","QueueType","Enum:ProductionQueueType"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings/MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings/MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings/MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings/MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandGetCrate","","Cursor","Reference"},
        new string[]{"UIMouseTacticalDeactivateMissionHotSpotsButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalDeactivateMissionHotSpotsButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalDeactivateMissionHotSpotsButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalDeactivateMissionHotSpotsButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandForceAttack","","ObjectCursor","Reference"},
        new string[]{"UIMouseTacticalCommandForceAttack","","GroundCursor","Reference"},
        new string[]{"UIMouseTacticalPlanningModeButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalPlanningModeButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalPlanningModeButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalPlanningModeButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandMisc","","CombineCursor","Reference"},
        new string[]{"UIMouseTacticalCommandMisc","","ChargeDefensesCursor","Reference"},
        new string[]{"UIMouseTacticalCommandMisc","","ConversionBeamCursor","Reference"},
        new string[]{"UIMouseTacticalSelectionRefinementTab","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalSelectionRefinementTab","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalSelectionRefinementTab","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalSelectionRefinementTab","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandForceMove","","Cursor","Reference"},
        new string[]{"UIMouseTacticalOpenAdvancedCommandsButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalOpenAdvancedCommandsButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalOpenAdvancedCommandsButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalOpenAdvancedCommandsButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings","Stance","Enum:Stance"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings/MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings/MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings/MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings/MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldPositionButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldPositionButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldPositionButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldPositionButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandMineOre","","Cursor","Reference"},
        new string[]{"UIMouseTacticalWaypointModeButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalWaypointModeButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalWaypointModeButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalWaypointModeButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalReverseMoveButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalReverseMoveButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalReverseMoveButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalReverseMoveButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandEnter","","DefaultCursor","Reference"},
        new string[]{"UIMouseTacticalCommandEnter","","ContestCursor","Reference"},
        new string[]{"UIMouseTacticalCommandEnter","InfiltratorEffectTypeSettings","Effect","Enum:InfiltratorEffectType"},
        new string[]{"UIMouseTacticalCommandEnter","InfiltratorEffectTypeSettings","Cursor","Reference"},
        new string[]{"UIMouseTacticalCoOpAIButtonsAssaultButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsAssaultButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsAssaultButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalCoOpAIButtonsAssaultButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalPauseButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalPauseButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalPauseButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalPauseButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandAttack","","AttackObjectCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","ForceAttackObjectCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","ForceAttackGroundCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","ExplosiveChargeCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","ClearGarrisonCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","DisguiseCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttack","","CallAirstrikeCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttackMove","","NormalCursor","Reference"},
        new string[]{"UIMouseTacticalCommandAttackMove","","ForcedCursor","Reference"},
        new string[]{"UIMouseTacticalForceMoveButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalForceMoveButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalForceMoveButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalForceMoveButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalForceAttackButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalForceAttackButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalForceAttackButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalForceAttackButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","Type","Enum:AIBeaconType"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","PlaceUnattachedCursor","Reference"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","PlaceAttachedCursor","Reference"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","CanTargetObjects","Bool"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","CanTargetPosition","Bool"},
        new string[]{"UIMouseTacticalCommandDock","","Cursor","Reference"},
        new string[]{"UIMouseTacticalCommandReverseMove","","Cursor","Reference"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings","QueueType","Enum:ProductionQueueType"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings/MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings/MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings/MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings/MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalSellModeButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalSellModeButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalSellModeButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalSellModeButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalStopButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalStopButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalStopButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalStopButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalCommandSetRallyPoint","","Cursor","Reference"},
        new string[]{"UIMouseTacticalFastForwardButton","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalFastForwardButton","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalFastForwardButton","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalFastForwardButton","MouseOverHelp","Instructions","String"},
        new string[]{"UIMouseTacticalGroupSelection","Group","CreateHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalGroupSelection","Group","AddToHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalGroupSelection","Group","SelectHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalGroupSelection","Group","ViewHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ScrollCursor","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ScrollLeftHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ScrollRightHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ScrollUpHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ScrollDownHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","RotateLeftHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","RotateRightHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ZoomInHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","ZoomOutHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","","KeyScrollFactor","Percentage"},
        new string[]{"UIMouseTacticalLookAt","","MouseDragScrollFactor","Percentage"},
        new string[]{"UIMouseTacticalLookAt","","MouseEdgeScrollFactor","Percentage"},
        new string[]{"UIMouseTacticalLookAt","ViewBookmark","GoToHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalLookAt","ViewBookmark","SaveHotKeySlot","Reference"},
        new string[]{"UIMouseTacticalRadarTelestrator","","StrokeSustainDuration","Time"},
        new string[]{"UIMouseTacticalRadarTelestrator","","StrokeFadeDuration","Time"},
        new string[]{"UIMouseTacticalTelestrator","","DrawCursor","Reference"},
        new string[]{"UIMouseTacticalThreatMeter","MouseOverHelp","Title","String"},
        new string[]{"UIMouseTacticalThreatMeter","MouseOverHelp","Description","String"},
        new string[]{"UIMouseTacticalThreatMeter","MouseOverHelp","TypeDescription","String"},
        new string[]{"UIMouseTacticalThreatMeter","MouseOverHelp","Instructions","String"},
    };
    static readonly string[][] Children={
        new string[]{"UIMouseTacticalCoOpAIButtonsHelpButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCommandCommunicateWithAllies","","BeaconAttachableObjectFilter","ObjectFilter","Pointer"},
        new string[]{"UIMouseTacticalToggleRepairModeButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalAttackMoveButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalForceAttackMoveButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldObjectButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalPlayerTechStoreButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCommandSell","","DefaultCursors","UIMouseTacticalCommandSellCursorSet","Inline"},
        new string[]{"UIMouseTacticalCommandSell","","FactionCursors","UIMouseTacticalCommandSellFactionCursorSet","List"},
        new string[]{"UIMouseTacticalCommandSell","FactionCursors","Cursors","UIMouseTacticalCommandSellCursorSet","Inline"},
        new string[]{"UIMouseTacticalUnitIntroButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCoOpAIButtonsNormalButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalBuildQueueTab","","QueueTypeSettings","UIMouseTacticalBuildQueueTabQueueTypeSettings","List"},
        new string[]{"UIMouseTacticalBuildQueueTab","QueueTypeSettings","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalDeactivateMissionHotSpotsButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalPlanningModeButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalSelectionRefinementTab","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalOpenAdvancedCommandsButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalSetStanceButton","","StanceSettings","UIMouseTacticalSetStanceButtonStanceSettings","List"},
        new string[]{"UIMouseTacticalSetStanceButton","StanceSettings","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCoOpAIButtonsHoldPositionButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalWaypointModeButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalReverseMoveButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCommandEnter","","InfiltratorEffectTypeSettings","UIMouseTacticalCommandEnterInfiltratorEffectTypeSettings","List"},
        new string[]{"UIMouseTacticalCoOpAIButtonsAssaultButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalPauseButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalForceMoveButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalForceAttackButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","","BeaconTypeSettings","UIMouseTacticalCommandPlaceAIBeaconTypeSettings","List"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings","AttachableObjectFilter","ObjectFilter","Pointer"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","","QueueTypeSettings","UIMouseTacticalBuildQueueTypeTabQueueTypeSettings","List"},
        new string[]{"UIMouseTacticalBuildQueueTypeTab","QueueTypeSettings","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalSellModeButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalStopButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalFastForwardButton","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
        new string[]{"UIMouseTacticalGroupSelection","","Group","UIMouseTacticalGroupSelectionGroup","List"},
        new string[]{"UIMouseTacticalLookAt","","ViewBookmark","UIMouseTacticalLookAtViewBookmark","List"},
        new string[]{"UIMouseTacticalThreatMeter","","MouseOverHelp","InGameUISimpleHelpTemplate","Inline"},
    };
    static readonly string[][] Filters={
        new string[]{"UIMouseTacticalCommandCommunicateWithAllies","BeaconAttachableObjectFilter"},
        new string[]{"UIMouseTacticalCommandPlaceAIBeacon","BeaconTypeSettings/AttachableObjectFilter"},
    };
    static string[] EnumValues(string name) { switch(name) {
        case "ProductionQueueType":return WithNul(new string[]{"AIRCRAFT","INFANTRY","INVALID","MAIN_STRUCTURE","OTHER_STRUCTURE","UPGRADE","VEHICLE","WATERCRAFT","unknown","","0","lowercase"});
        case "InfiltratorEffectType":return WithNul(new string[]{"DISABLE","ENERGY","INVALID","KILL","PARALYZE","RADAR_FREEZE","RESET","STEAL_MONEY","VISION","VISION_UNITS","unknown","","0","lowercase"});
        case "AIBeaconType":return WithNul(new string[]{"Assault","HoldObject","HoldPosition","Invalid","unknown","","0","lowercase"});
        case "FactionType":return WithNul(new string[]{"Allies","Japan","Neutral","RANDOM","Soviet","unknown","","0","lowercase"});
        case "Stance":return WithNul(new string[]{"AGGRESSIVE","GUARD","HOLD_FIRE","HOLD_POSITION","unknown","","0","lowercase"});
        default:throw new Exception("Unknown enum "+name); } }
    static readonly string[] KindNames={"ACTIVATE_AFTER_UNPACK","AIRCRAFT","AIRCRAFT_PATH_AROUND","AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED","AIRFIELD","AI_CAN_UNPACK_NEAR_CONYARD","AI_NEEDS_PLAYER_POWER_UPDATE","ALLY_TARGETABLE","ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES","ALWAYS_SELECTABLE","ALWAYS_SHOW_HOUSE_COLOR","ALWAYS_VISIBLE","ALWAYS_VISIBLE_IN_RADAR","AMPHIBIOUS","ANTI_GARRISON","ARMY_SUMMARY","ASSAULT_AIRCRAFT","ATTACH_ATTACK","ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED","AUTO_ACQUIRABLE_BY_AI","AUTO_RALLYPOINT","BALLISTIC_MISSILE","BANNER","BASE_FOUNDATION","BASE_SITE","BEACON","BEAM_TARGET","BEAM_TARGET_REFLECTOR","BLOCKING_GATE","BOMBER_AIRCRAFT","BRIDGE","BRIDGE_ENDCAP","BRIDGE_GATEHOUSE","BRIDGE_SEGMENT","BROADCASTS_INVISIBILITY","BUFF","BUILD_FOR_FREE","CANNOT_BE_DETECTED","CANNOT_BUILD_NEAR_SUPPLIES","CANNOT_HIJACK","CANNOT_RETALIATE","CANNOT_STOP_ON_WATER","CANT_TOGGLE_POWER","CAN_ATTACK","CAN_ATTACK_STEALTHED","CAN_ATTACK_WALLS","CAN_BE_CAPTURED","CAN_BE_FAVORITE_UNIT","CAN_BE_REPULSED","CAN_BUILD_ON_DEEP_WATER","CAN_BUILD_ON_WATER","CAN_CAPTURE","CAN_CAST_REFLECTIONS","CAN_CLIMB_WALLS","CAN_HEAL_ALLIES","CAN_NOT_BUILD_ON_LAND","CAN_PLACE_CHARGE","CAN_PLACE_MANIPULATOR","CAN_RAPPEL","CAN_SEE_THROUGH_STRUCTURE","CAN_SHOOT_OVER_WALLS","CAN_SKIP_SHADOW","CAN_TOPPLE","CAPTURABLE","CAPTUREFLAG","CARPET_BOMBER","CASTLE_CENTER","CASTLE_KEEP","CAVALRY","CHUNK_VENDOR","CIVILIAN_BUILDING","CIVILIAN_UNIT","CLASSIFY_ENTIRE_FOOTPRINT","CLEARED_BY_BUILD","CLICK_THROUGH","COMBO_HORDE","COMMANDCENTER","COMMANDO","CONSTRUCTION_YARD","COVER","CRANE","CRATE","CREEP","CRUSHABLE_BUILDING","CRUSHABLE_OBSTACLE","CRUSH_CRUSHABLE_BUILDINGS","CYCLE_SELECTION","DEBRIS","DEFENSIVE_WALL","DEFLECT_BY_SPECIAL_POWER","DEPLOYED_MINE","DEPRECATED","DISGUISER","DONT_AUTO_CRUSH_INFANTRY","DONT_DESTROY_IF_ON_BRIDGE","DONT_HIDE_IF_FOGGED","DONT_SUBMERGE_BY_WAVES","DONT_UNATTACH_WHEN_HEALING","DONT_USE_CANCEL_BUILD_BUTTON","DOZER","DOZER_FACTORY","DO_NOT_CLASSIFY","DO_NOT_PICK_ME_WHEN_BUILDING","DRAWABLE_ONLY","DRONE","ECONOMY_STRUCTURE","EGG","ENCLOSURE","ENGINEER","ENVIRONMENT","EXPANSION_PAD","EXPANSION_UNIT","FIGHTER_AIRCRAFT","FORCEATTACKABLE","FS_AIR_FIELD","FS_BARRACKS","FS_BASE_DEFENSE","FS_CASH_PRODUCER","FS_FACTORY","FS_MONEY_STORAGE","FS_POWER","FS_RADAR","FS_TECHNOLOGY","FS_TECH_CENTER","FS_WAR_FACTORY","GARRISON","GARRISONABLE_UNTIL_DESTROYED","GRABBABLE","GRAB_AND_DROP","GRAB_AND_KILL","GRANTS_VETERANCY","HARVESTABLE","HARVESTER","HAS_HEALTH_BAR","HEAL_PAD","HEAVY_MELEE_HITTER","HIDE_IF_FOGGED","HORDE","HUGE_VEHICLE","HULK","HUSK","IGNORED_IN_FINDPOSITIONAROUND","IGNORED_IN_GUI","IGNORES_SELECT_ALL","IGNORE_CAN_NOT_BUILD","IGNORE_FORCE_MOVE","IGNORE_FOR_EVA_SPEECH_POSITION","IGNORE_FOR_VICTORY","IGNORE_IN_AI_HUNT_TACTIC","IMMOBILE","IMMUNE_TO_CAPTURE","IMMUNE_TO_CYCLONIC_SHOCKWAVE","INERT","INERT_SHROUD_REVEALER","INFANTRY","INFILTRATOR","KEEP_CLASSIFIED_WHEN_DEAD","LARGE_RECTANGLE_PATHFIND","LEAVE_PARKING_ON_BUILD_COMPLETE","LIMITED_PRODUCTION_AIRCRAFT","LINE_OF_SIGHT_IGNORES_BUILDINGS","LINKED_TO_FLAG","LOW_OVERLAPPABLE","MACHINE","MCV","MELEE_HORDE","MINE","MINE_TRIGGER","MONSTER","MOVE_FOR_NOONE","MOVE_ONLY","MP_COUNT_FOR_VICTORY","NEED_BASE_FOUNDATION","NEUTRAL_TECH","NEVER_CULL_FOR_MP","NONOCCLUDING","NOTIFY_OF_PREATTACK","NOT_AUTOACQUIRABLE","NOT_SELLABLE","NO_BASE_CAPTURE","NO_COLLIDE","NO_FORMATION_MOVEMENT","NO_FREEWILL_ENTER","NO_GARRISON","NO_HEAL_ICON","NO_SHADOW","OBSTACLE","OCL_BIT","OPTIMIZED_PROP","OPTIMIZED_SOUND","ORE_NODE","ORIENTS_TO_CAMERA","OUTPOST","PARACHUTABLE","PARACHUTE","PASS_EXPERIENCE_TO_CONTAINED","PASS_EXPERIENCE_TO_CONTAINER","PASS_EXPERIENCE_TO_PRODUCER","PASS_EXPERIENCE_TO_SLAVER","PATH_THROUGH_EACH_OTHER","PATH_THROUGH_INFANTRY","PORT","POWERED","POWERED_POWERS_ONLY","PRELOAD","PRODUCED_AT_HELIPAD","PROJECTILE","PROJECT_BUILDABILITY_FOR_ALLIES","RADAR_SCRAMBLER","REACT_WHEN_SELECTED","REBUILD_HOLE","REFINERY","REPAIR_PAD","RESIST_EMP","REVEAL_TO_ALL","ROCK","ROCK_VENDOR","SALVAGER","SCALEABLE_WALL","SCARY","SCORE","SCORE_CREATE","SCORE_DESTROY","SELECTABLE","SHADOW_ULTRA_HIGH_ONLY","SHIP","SHOW_BEHIND_OCCLUDERS","SHRUBBERY","SIEGE_TOWER","SIEGE_WEAPON","SIMPLE_OBJECT_PROP","SKIP_DEFECT_IF_UNCOMBINED","SKIP_IDLE_WHEN_CAPTURED","SKIRMISH_AI_ATTEMPT_TO_DODGE","SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME","SKIRMISH_AI_DONT_CONSIDER_THREAT","SKIRMISH_AI_DONT_GARRISON","SKIRMISH_AI_DONT_MULTI_TARGET","SKIRMISH_AI_DONT_REPAIR","SKYBOX","SLAVE_OWNER","SMALL_MISSILE","SNIPER","SPELL_BOOK","STEALTH_GARRISON","STICK_TO_TERRAIN_SLOPE","STRUCTURE","SUBMARINE","SUMMONED","SUPER_WEAPON","SUPPLY_GATHERING_CENTER","SUPPLY_SOURCE","SUPPLY_SOURCE_ON_PREVIEW","SURPRISE_ATTACKER","T1_UNIT","T2_UNIT","T3_UNIT","TACTICAL_MARKER","TARGETABLE","TERRAIN_LIKE_GROUND_OBJECT","THREAT_FINDER","THROWN_OBJECT","TIBERIUM","TIBERIUM_BASED","TIBERIUM_FIELD","TIME_BOMB","TRANSFORMER","TRANSPORT","UNATTACKABLE","UNIQUE_UNIT","UNPACKS_INTO_BUILDING","VALID_TARGET_FOR_C4","VEHICLE","VITAL_FOR_BASE_SURVIVAL","WALK_ON_TOP_OF_WALL","WALL_GATE","WALL_HUB","WALL_PIECE","WALL_SEGMENT","WALL_UPGRADE","WB_DISPLAY_SCRIPT_NAME","WEAPON_SALVAGER","WORKING_PASSENGER","WORLDBUILDER_SNAP_TO_GRID"};
    static readonly string[] StatusNames={"ABSORBED_DAMAGE","ADVANCED_MISSILE_PACKS","AFLAME","AIRBORNE_TARGET","AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE","AIR_LOCOMOTOR_ACTIVE","ANTI_GARRISON","ATTACHED","ATTACKING_GARRISONED_STRUCTURE","BOOBY_TRAPPED","BOOBY_TRAP_EXPLODE","BOOKENDING","BRIDGE_IMPASSABLE","BROADCASTING_INVISIBILITY","BUILD_BEING_CANCELED","BURNED","CANNOT_BE_SOLD","CANNOT_LAND_AT_AIRFIELD","CAN_ATTACK","CAN_ATTACK_WHILE_STEALTHED","CAN_CONTEST_GARRISON","CAN_ENTER_ANYTHING","CAN_NOT_WALK_ON","CAN_STEALTH","CAN_STEALTH_FROM_PRODUCER","CARRYING_FLAG","CHANTING","CHARGING_BASE_DEFENSES","CHARGING_WEAPON","CLEARED_FOR_LANDING","CLONED","COMBINED_ATTACHED","COMBINED_CHILD","COMBINED_PARENT","COMMAND_BUTTON_TOGGLED","CONTAINER_OCCUPIED","CONTESTED","CONTESTING_BUILDING","DAMAGED","DEATH_1","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEFLECT_INCOMING_FIRE","DELAYED_ENTER_STRUCTURE","DEPART_FROM_BOARDING","DEPLOYED","DEPLOYING","DESTROYED","DETECTED","DOCKING","DOES_CONTAIN_TIBERIUM","DONT_CLEAR_FOR_BUILD","DO_NOT_AVOID_TALLBUILDING","DO_NOT_PICK_ME","DO_NOT_SCORE","ENABLE_TRACER_DRAW","ENCLOSED","END_FIRE_STATE","ENRAGED","ENTERING_GOAL","EXCEEDED_PATH_NEIGHBOR_COUNT","EXECUTING_STORED_COMMAND","EXITING_COMBINED","EXPLOSIVES_ATTACHED","EXPLOSIVES_DETONATING","FLEE_OFF_MAP","FOLLOWING_THROUGH","FORCE_ATTACKING","FORCE_ATTACK_MOVING","GARRISONED","GARRISON_CAN_STEALTH","GENERIC_TOGGLE_STATE","GRAPPLE_UPDATE_ACTIVE","GRAPPLE_UPDATE_BEING_MOVED","GROUND_LOCOMOTOR_ACTIVE","HARVESTING","HAS_SECONDARY_DAMAGE","HAS_TIBERIUM_GROWTH_MOD","HAS_TIBERIUM_UPGRADE","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIJACKED","HIT_SHIELD_SPHERE","HORDE_MEMBER","IGNORE_AI_COMMAND","IGNORING_POWER_DOWN","IGNORING_STEALTH","IMMOBILE","IMMOBILE_ALLOW_ROTATE","IMMUNE_TO_BARK","INAUDIBLE","INHERITED_FROM_ALLY_TEAM","INSIDE_GARRISON","INVISIBLE_DETECTED","INVISIBLE_DETECTED_BY_FRIEND","IN_COVER","IN_FORMATION_TEMPLATE","IN_NANOHIVE","IN_SHIELD_SPHERE","IN_SPIDER_HOLE","IN_STASIS","IRRADIATED","IS_AIMING_WEAPON","IS_ATTACKING","IS_BEING_DRAGGED","IS_BEING_HARVESTED","IS_BRAKING","IS_ENGAGED","IS_FIRING_WEAPON","IS_HIDEOUT","IS_LEAVING_FACTORY","IS_MELEE_ATTACKING","IS_MOVING_TO_RALLY_POINT","IS_RELOADING_WEAPON","LANDING_IN_PROGRESS","LEASHED_RETURNING","LEECHED_TARGET_ACTIVE","LIFTED_INTO_AIR","LINE_SEGMENT_DAMAGE_ACTIVE","LOADED_FROM_MAP","MAGNETIZED","MARCH_OF_DEATH","MATCH_TARGETS_SPEED","MOVING_TO_DISMOUNT","NEXT_MOVE_IS_FORCE_ATTACK_MOVE","NEXT_MOVE_IS_REVERSE","NON_AUTOACQUIRABLE","NOT_IN_WORLD","NO_ATTACK","NO_ATTACK_FROM_AI","NO_AUTO_ACQUIRE","NO_BRIBE","NO_COLLISIONS","NO_HERO_PROPERTIES","NO_REFUND","NO_SHADOW","NO_SPECIAL_ABILITY","NO_SQUISHCOLLIDE_DELAY","OCLMONITOR_COMPLETED_TASK","OCLMONITOR_MONITOR_RELEASED","OUT_OF_PHASE","OVERCHARGING_WEAPON","OVER_WATER","PARACHUTING","PARKED_AT_AIRFIELD","PAUSING","PENDING_CONSTRUCTION","PHANTOM_STRUCTURE","PLACED_BY_PLAYER","PLAYER_POWER_1","PLAYER_POWER_2","PLAYER_POWER_3","PLAYER_POWER_4","PLAYER_POWER_5","POINT_DEFENSE_DRONE_ATTACHED","PORTER_TAGGED","POWERED_DOWN_EMP","PREEMPT_PRE_ATTACK","RAMPAGING","REALLYDAMAGED","RECONSTRUCTING","REPAIR_ALLIES_WHEN_IDLE","REPULSOR","RESET_GOAL_POSITION","RIDER_IS_PILOT","RIFT_OCCUPIED","RIOT_SHIELDED","RUBBLE","RUNNING_DOWN_FROM_BEHIND","SCARED_CIVILIAN_CAR","SCRAMBLED","SHIELDBODY_ENABLED","SHROUD_REVEAL_TO_ALL","SHRUNK","SINKING","SKIRMISH_AI_DO_NOT_ATTACK","SKIRMISH_AI_DO_NOT_TAKE","SOLD","SPECIALABILITY_ACTIVE","SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING","SPECIAL_ARMOR_ACTIVE","STAND_GROUND","STEALTHED","STEAL_NEXT_UNIT_TRAPPED","SUBMERGED","SURFACED","SURFACED_IMMOBILE","SWITCHED_WEAPONS","TAGGED","TARGETED_FOR_REPAIR","TEMPORARILY_DEFECTED","TIBERIUM_VIBRATING","TOPPLING","TRANSFORMATION_TOGGLE_STATE","TRANSPORT_MOVING_TO_CONTAIN","UNATTACKABLE","UNCONTROLLABLY_SCARED","UNDERGOING_REPAIR","UNDER_CONSTRUCTION","UNDER_FROZEN","UNDER_IRON_CURTAIN","UNDER_RUSH_ATTACK","UNIT_WANTS_TO_REGARRISON","UNPACKING","UNSELECTABLE","UPDATING_AI","USER_PARALYZED","USER_POWERED_DOWN","USING_ABILITY","VEHICLE_ATTACHED","WAITING_FOR_BOARDING","WALL_SEGMENT","WATER_LOCOMOTOR_ACTIVE","WEAPON_UPGRADED_01","WEAPON_UPGRADED_02","WEAPON_UPGRADED_03","WONT_RIDE_WITH_YOU"};
    static readonly string[] Strings={"","a","abc","abcd","abcde","A&B<help>","é","资源","😀","e\u0301",new string('x',257),"a\0tail","\0leading","多字节\0尾部"};
    static string[] WithNul(string[] input){List<string> result=new List<string>(input);foreach(string s in input)result.Add(s+"\0tail");result.Add("\0HIGH");result.Add("unknown\0HIGH");return result.ToArray();}
    static string[] Values(string kind){if(kind=="Time")return WithNul(TimeValues);if(kind.StartsWith("Enum:"))return EnumValues(kind.Substring(5));if(kind=="String")return Strings;if(kind=="Bool")return WithNul(Bools);if(kind=="Reference"||kind=="NullableReference")return WithNul(Refs);return WithNul(Numbers);}
    static XmlElement At(XmlDocument d,string path){XmlElement n=d.DocumentElement;if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement c=d.CreateElement(part,NS);n.AppendChild(c);n=c;}return n;}
    static XmlDocument Field(string[] f,string value){XmlDocument d=Parse(Root(f[0],"",""));At(d,f[1]).SetAttribute(f[2],value);return d;}
    static void Fill(XmlElement node,string type,int seed){
        XmlDocument d=node.OwnerDocument;
        if(type=="ObjectFilter"){
            node.SetAttribute("Rule","ANY");node.SetAttribute("Relationship","ENEMIES");node.SetAttribute("Alignment","EVIL");node.SetAttribute("Include","INFANTRY VEHICLE");node.SetAttribute("Exclude","STRUCTURE");node.SetAttribute("StatusBitFlags","AFLAME");node.SetAttribute("StatusBitFlagsExclude","DESTROYED");
            foreach(string child in new string[]{"IncludeThing","ExcludeThing"}){XmlElement c=d.CreateElement(child,NS);c.InnerText="Object"+seed;node.AppendChild(c);}return;
        }
        int i=0;foreach(string[] f in LayoutFields)if(f[0]==type){string k=f[3];node.SetAttribute(f[1],k=="String"?"Help"+seed+"_"+i:k=="Reference"||k=="NullableReference"?"Name\\"+(seed+i):k=="Bool"?"false":k=="Enum"?EnumValues(f[4])[0]:"7");++i;}
        foreach(string[] c in LayoutChildren)if(c[0]==type){int count=c[4]=="List"?2:1;for(int j=0;j<count;++j){XmlElement ch=d.CreateElement(c[1],NS);node.AppendChild(ch);Fill(ch,c[2],seed+7+j);}}
    }
    static XmlDocument Filled(string root){XmlDocument d=Parse(Root(root,"",""));Fill(d.DocumentElement,root,17);return d;}
    static void WordIs(AssetBuffer b,int at,uint value,bool big,string message){Check(Word(b.InstanceData,at,big)==value,message+" @"+at);}
    static void Abi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;string help="<MouseOverHelp Title='T' Description='DD' TypeDescription='TTT' Instructions='IIII'/>";
        foreach(string type in Types)if(type.EndsWith("Button")||type.EndsWith("SelectionRefinementTab")||type.EndsWith("ThreatMeter")){
            if(type.EndsWith("ExitContainerButton")||type.EndsWith("SetStanceButton"))continue;
            bool tech=type.EndsWith("PlayerTechStoreButton");AssetBuffer b=Test("ABI/help/"+type,Root(type,tech?"TechPointAwardedSound='Sound\\7'":"",help),p);int root=tech?40:36,baseAt=tech?44:36;
            Check(b.InstanceData.Length==baseAt+20,"help four aligned ANSI allocations");int[] offsets={4,12,20,28},ptrs={0,4,8,12};for(int i=0;i<4;++i){WordIs(b,offsets[i],(uint)(i+1),big,"help byte counts");WordIs(b,offsets[i]+4,(uint)(baseAt+ptrs[i]),big,"help allocation order");Check(b.InstanceData[baseAt+ptrs[i]+i+1]==0,"help NUL terminator");}
            Check(b.RelocationData.Length==4*(tech?6:5),"help relocation count");if(tech){WordIs(b,36,40,big,"nullable sound allocated before base help");WordIs(b,40,8,big,"nullable sound imported index+1");Check(b.ImportsData.Length==8&&Word(b.ImportsData,0,big)==40,"nullable sound import address");}
        }
        AssetBuffer attack=Test("ABI/attack cursors",Root("UIMouseTacticalCommandAttack","AttackObjectCursor='A\\8' ForceAttackObjectCursor='B\\2' ForceAttackGroundCursor='C\\8' ExplosiveChargeCursor='D\\0' ClearGarrisonCursor='E\\-1' DisguiseCursor='F\\3' CallAirstrikeCursor='G\\1'",""),p);
        uint[] ids={9,3,9,1,0,4,2};for(int i=0;i<7;++i)WordIs(attack,4+4*i,ids[i],big,"attack exact import fields");Check(attack.InstanceData.Length==32&&attack.ImportsData.Length==32,"attack seven imports fixed root");HashSet<uint> imp=new HashSet<uint>();uint prior=0;for(int i=0;i<7;++i){uint at=Word(attack.ImportsData,4*i,big);Check(imp.Add(at),"attack unique field imports");uint key=unchecked(Word(attack.InstanceData,(int)at,big)-1);Check(i==0||key>=prior,"same-block import index sort");prior=key;}for(uint at=4;at<=28;at+=4)Check(imp.Contains(at),"attack complete import offset set");
        AssetBuffer enter=Test("ABI/enter list",Root("UIMouseTacticalCommandEnter","DefaultCursor='D\\7' ContestCursor='C\\1'","<InfiltratorEffectTypeSettings Effect='unknown' Cursor='A\\5'/><InfiltratorEffectTypeSettings Effect='unknown' Cursor='A\\5'/>"),p);
        Check(enter.InstanceData.Length==36,"enter stride8");WordIs(enter,12,2,big,"enter count@12");WordIs(enter,16,20,big,"enter pointer@16");WordIs(enter,20,1,big,"infiltrator unknown fallback");WordIs(enter,24,6,big,"infiltrator cursor index+1");WordIs(enter,28,1,big,"infiltrator second stride");Check(enter.RelocationData.Length==8&&Word(enter.RelocationData,0,big)==16,"enter list relocation");
        foreach(string type in new string[]{"UIMouseTacticalBuildQueueTab","UIMouseTacticalBuildQueueTypeTab","UIMouseTacticalSetStanceButton"}){
            string child=type.EndsWith("SetStanceButton")?"StanceSettings":"QueueTypeSettings",attr=child=="StanceSettings"?"Stance":"QueueType";uint fallback=child=="StanceSettings"?1u:4u;
            AssetBuffer b=Test("ABI/enum-help-list/"+type,Root(type,"","<"+child+" "+attr+"='unknown'><MouseOverHelp Title='abc'/></"+child+"><"+child+" "+attr+"='unknown'/>"),p);
            Check(b.InstanceData.Length==88,"enum/help stride36 root12 and one string4");WordIs(b,4,2,big,"enum/help list count");WordIs(b,8,12,big,"enum/help list pointer");WordIs(b,12,fallback,big,"enum unknown fallback");WordIs(b,48,fallback,big,"enum/help second stride36");WordIs(b,16,3,big,"nested help byte count");WordIs(b,20,84,big,"nested string follows full array");
        }
        AssetBuffer sell=Test("ABI/sell nested list",Root("UIMouseTacticalCommandSell","","<DefaultCursors ValidTarget='A\\7' InvalidTarget='B\\8'/><FactionCursors Faction='unknown'><Cursors ValidTarget='C\\3' InvalidTarget='D\\2'/></FactionCursors>"),p);Check(sell.InstanceData.Length==32,"sell stride12 root20");WordIs(sell,4,8,big,"sell inline default valid");WordIs(sell,8,9,big,"sell inline default invalid");WordIs(sell,12,1,big,"sell count");WordIs(sell,16,20,big,"sell pointer");WordIs(sell,20,0,big,"faction unknown0");WordIs(sell,24,4,big,"sell nested valid");WordIs(sell,28,3,big,"sell nested invalid");
        AssetBuffer beacon=Test("ABI/beacon nullable",Root("UIMouseTacticalCommandPlaceAIBeacon","","<BeaconTypeSettings Type='unknown'><AttachableObjectFilter/></BeaconTypeSettings>"),p);
        Check(beacon.InstanceData.Length==144,"beacon root12 plus component20 plus filter112");WordIs(beacon,4,1,big,"beacon count");WordIs(beacon,8,12,big,"beacon array pointer");WordIs(beacon,12,0,big,"beacon unknown enum0");WordIs(beacon,24,32,big,"beacon filter pointer@component12");Check(beacon.InstanceData[28]==1&&beacon.InstanceData[29]==1&&beacon.InstanceData[30]==0&&beacon.InstanceData[31]==0,"beacon bool defaults padding");
        AssetBuffer filter=Test("ABI/communicate nullable",Root("UIMouseTacticalCommandCommunicateWithAllies","","<BeaconAttachableObjectFilter/>"),p);Check(filter.InstanceData.Length==132,"communicate root20 plus filter112");WordIs(filter,16,20,big,"communicate nullable pointer16");
    }
    static void Matrix(TargetPlatform p){
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i){string type=Types[i];AssetBuffer b=Test("defaults/"+type,Root(type,"",""),p);Check(b.InstanceData.Length==Sizes[i],"independent root Tracker size "+type);Test("metadata/"+type,Root(type,"TypeId='"+TypeIds[i]+"'",""),p);TestDoc("full/"+type,Filled(type),p);Reject("root unknown attr",Root(type,"Unknown='1'",""));Reject("root qualified attr",Root(type,"xmlns:q='urn:other' q:TypeId='1'",""));Reject("root unknown child",Root(type,"","<Unknown/>"));Reject("root foreign child",Root(type,"","<Unknown xmlns='urn:other'/>"));Reject("root text",Root(type,"","payload"));}
        foreach(string[] f in Fields){foreach(string value in Values(f[3]))TestDoc("field/"+f[0]+"/"+f[1]+"/"+f[2]+"/"+value,Field(f,value),p);XmlDocument d=Field(f,Values(f[3])[0]);foreach(XmlNode n in d.SelectNodes("//*"))((XmlElement)n).SetAttribute("TypeId","1");TestDoc("nested metadata/"+f[0]+f[1]+f[2],d,p);XmlNodeList ns=d.SelectNodes("//*");((XmlElement)ns[ns.Count-1]).SetAttribute("Unknown","1");Reject("nested unknown attr",d.OuterXml);}
        foreach(string[] c in Children){string path=c[1].Length==0?c[2]:c[1]+"/"+c[2];XmlDocument d=Parse(Root(c[0],"",""));XmlElement el=At(d,path);TestDoc("present-empty/"+c[0]+path,d,p);el.AppendChild(d.CreateTextNode("payload"));Reject("child text",d.OuterXml);el.RemoveAll();el.AppendChild(d.CreateElement("Unknown",NS));Reject("child unknown",d.OuterXml);
            if(c[4]!="List"){d=Parse(Root(c[0],"",""));el=At(d,path);el.ParentNode.AppendChild(el.CloneNode(true));Reject("duplicate singleton",d.OuterXml);}
            else foreach(int count in new int[]{0,1,2,3,4,15,16,27,28,29,64,257}){d=Parse(Root(c[0],"",""));XmlElement parent=At(d,c[1]);for(int i=0;i<count;++i){XmlElement row=d.CreateElement(c[2],NS);parent.AppendChild(row);Fill(row,c[3],i%11);}TestDoc("list/"+c[0]+path+count,d,p);}
        }
        FilterCases(p);Abi(p);NavigationAbi(p);
        Reject("unknown root",Root("Unknown","",""));Reject("foreign root","<UIMouseTacticalCommandMove xmlns='foreign'/>");bool caught=false;try{XmluitacticalCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){caught=true;}Check(caught,"null declaration");++rejections;
    }
    static string[] FilterValues(string attr){
        if(attr=="Rule")return WithNul(new string[]{"ALL","ANY","NONE","UNPARSED","unknown","all",""});
        if(attr=="Alignment")return WithNul(new string[]{"EVIL","GOOD","NONE","unknown","evil",""});
        if(attr=="Relationship")return WithNul(new string[]{"ALLIES","ENEMIES","NEUTRAL","SAME_PLAYER","ALL","NONE","unknown","allies","","ALL -ENEMIES","ENEMIES ENEMIES","ENEMIES -ENEMIES","+ALLIES","-ALLIES","ALLIES\tENEMIES"});
        if(attr=="id")return Strings;
        List<string> result=new List<string>(attr.StartsWith("Status")?StatusNames:KindNames);result.AddRange(new string[]{""," ","unknown","ALL","NONE","+INFANTRY","-INFANTRY","ALL -INFANTRY","INFANTRY -INFANTRY","INFANTRY INFANTRY","INFANTRY\tVEHICLE","INFANTRY\nVEHICLE","+AFLAME","-AFLAME","AFLAME -AFLAME","ALL -AFLAME","all"});return WithNul(result.ToArray());
    }
    static void FilterCases(TargetPlatform p){
        foreach(string[] f in Filters){
            foreach(string attr in new string[]{"id","Rule","Relationship","Alignment","Include","Exclude","StatusBitFlags","StatusBitFlagsExclude"})foreach(string value in FilterValues(attr)){
                XmlDocument d=Parse(Root(f[0],"",""));At(d,f[1]).SetAttribute(attr,value);TestDoc("filter/"+f[0]+f[1]+attr+value,d,p);
            }
            foreach(int count in new int[]{0,1,2,3,15,16,27,28,29,64,257}){
                XmlDocument d=Parse(Root(f[0],"",""));XmlElement filter=At(d,f[1]);filter.SetAttribute("StatusBitFlags","");filter.SetAttribute("StatusBitFlagsExclude","ALL -AFLAME");
                for(int i=0;i<count;++i)foreach(string name in new string[]{"IncludeThing","ExcludeThing"}){XmlElement row=d.CreateElement(name,NS);row.InnerText="Object"+(i%11);filter.AppendChild(row);}TestDoc("filter-things/"+f[0]+f[1]+count,d,p);
            }
            foreach(string value in Strings){XmlDocument d=Parse(Root(f[0],"",""));XmlElement filter=At(d,f[1]);foreach(string name in new string[]{"IncludeThing","ExcludeThing"}){XmlElement thing=d.CreateElement(name,NS);thing.InnerText=value;thing.SetAttribute("TypeId","1");filter.AppendChild(thing);}TestDoc("filter-ANSI/"+f[0]+f[1]+value.Length,d,p);}
            foreach(string body in new string[]{"<IncludeThing Unknown='1'>Name</IncludeThing>","<ExcludeThing><Unknown/></ExcludeThing>","<Unknown/>","payload"}){XmlDocument d=Parse(Root(f[0],"",""));At(d,f[1]).InnerXml=body;Reject("filter rejects "+body,d.OuterXml);}
            {XmlDocument d=Parse(Root(f[0],"",""));At(d,f[1]).SetAttribute("Unknown","1");Reject("filter unknown attribute",d.OuterXml);}
        }
    }
    static void NavigationAbi(TargetPlatform p){
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer defaultLook=Test("ABI/look defaults",Root("UIMouseTacticalLookAt","",""),p);for(int at=40;at<=48;at+=4)WordIs(defaultLook,at,0x3f800000,big,"look100 percent defaults");
        AssetBuffer partialLook=Test("ABI/look partial defaults",Root("UIMouseTacticalLookAt","KeyScrollFactor='25%'",""),p);WordIs(partialLook,40,0x3e800000,big,"look partial key25 percent");WordIs(partialLook,44,0x3f800000,big,"look partial drag default100 percent");WordIs(partialLook,48,0x3f800000,big,"look partial edge default100 percent");
        AssetBuffer group=Test("ABI/group selection",Root("UIMouseTacticalGroupSelection","","<Group CreateHotKeySlot='A\\7' AddToHotKeySlot='B\\0' SelectHotKeySlot='C\\-1' ViewHotKeySlot='D\\7'/><Group/>"),p);
        Check(group.InstanceData.Length==44,"group stride16 plus root12");WordIs(group,4,2,big,"group count");WordIs(group,8,12,big,"group list pointer");uint[] values={8,1,0,8};for(int i=0;i<4;++i)WordIs(group,12+4*i,values[i],big,"group four refs");for(int i=28;i<44;i+=4)WordIs(group,i,0,big,"empty group refs");Check(group.ImportsData.Length==20,"group only four present refs import");HashSet<uint> addresses=new HashSet<uint>();for(int i=0;i<4;++i)addresses.Add(Word(group.ImportsData,4*i,big));for(uint i=12;i<=24;i+=4)Check(addresses.Contains(i),"group exact import offset set");
        string[] names={"ScrollCursor","ScrollLeftHotKeySlot","ScrollRightHotKeySlot","ScrollUpHotKeySlot","ScrollDownHotKeySlot","RotateLeftHotKeySlot","RotateRightHotKeySlot","ZoomInHotKeySlot","ZoomOutHotKeySlot"};StringBuilder attrs=new StringBuilder();for(int i=0;i<names.Length;++i)attrs.Append(names[i]+"='Name\\"+(i+1)+"' ");attrs.Append("KeyScrollFactor='50%' MouseDragScrollFactor='75%' MouseEdgeScrollFactor='125%'");
        AssetBuffer look=Test("ABI/look at",Root("UIMouseTacticalLookAt",attrs.ToString(),"<ViewBookmark GoToHotKeySlot='G\\7' SaveHotKeySlot='S\\2'/><ViewBookmark GoToHotKeySlot='G\\1' SaveHotKeySlot='S\\1'/>"),p);
        Check(look.InstanceData.Length==76,"look root60 plus two stride8 bookmarks");for(int i=0;i<9;++i)WordIs(look,4+4*i,(uint)(i+2),big,"look nine refs exact offsets");WordIs(look,40,0x3f000000,big,"key scroll percentage");WordIs(look,44,0x3f400000,big,"drag scroll percentage");WordIs(look,48,0x3fa00000,big,"edge scroll percentage");WordIs(look,52,2,big,"look bookmark count");WordIs(look,56,60,big,"look bookmark pointer");WordIs(look,60,8,big,"go-to bookmark ref");WordIs(look,64,3,big,"save bookmark ref");WordIs(look,68,2,big,"second bookmark go-to");WordIs(look,72,2,big,"second bookmark save");Check(look.ImportsData.Length==56,"look13 imports plus sentinel");
        AssetBuffer radar=Test("ABI/radar defaults",Root("UIMouseTacticalRadarTelestrator","",""),p);Check(radar.InstanceData.Length==12,"radar root12");WordIs(radar,4,0x41200000,big,"radar10 seconds default");WordIs(radar,8,0x40000000,big,"radar2 seconds default");radar=Test("ABI/radar nondefault",Root("UIMouseTacticalRadarTelestrator","StrokeSustainDuration='250ms' StrokeFadeDuration='0.5s'",""),p);WordIs(radar,4,0x3e800000,big,"radar milliseconds scaling");WordIs(radar,8,0x3f000000,big,"radar half second");
        AssetBuffer tel=Test("ABI/telestrator cursor",Root("UIMouseTacticalTelestrator","DrawCursor='A\\7'",""),p);Check(tel.InstanceData.Length==8&&tel.ImportsData.Length==8,"telestrator fixed root/direct ref");WordIs(tel,4,8,big,"draw cursor index+1");Check(Word(tel.ImportsData,0,big)==4,"draw cursor import address");
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
            Console.WriteLine("PASS Tactical UI fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}


