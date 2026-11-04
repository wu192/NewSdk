using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;

class XmlattributesOracle
{
    const string Ns = "uri:ea.com:eala:asset";
    static int fixtures, comparisons, rejections, layouts;
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager ns) {
        var doc = new AssetDeclarationDocument();
        Type t = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(t);
        t.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(doc, state);
        var d = new InstanceDeclaration(doc); d.XmlNode = xml.DocumentElement; return d;
    }
    static void Equal(string label, byte[] a, byte[] b) {
        if (a == null || b == null || a.Length != b.Length) throw new Exception(label + " length");
        for (int i=0;i<a.Length;i++) if(a[i]!=b[i]) throw new Exception(label+" byte="+i+" expected="+a[i]+" actual="+b[i]);
        comparisons++;
    }
    static uint Word(byte[] b, int n, bool big) {
        return big ? ((uint)b[n]<<24)|((uint)b[n+1]<<16)|((uint)b[n+2]<<8)|b[n+3] : BitConverter.ToUInt32(b,n);
    }
    static void Require(bool ok, string label) { if(!ok) throw new Exception(label); }
    static string Xml(string attrs, string children) { return "<LogicCommandSet xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</LogicCommandSet>"; }
    static void Check(IAssetBuilderPlugin plugin, TargetPlatform platform, string label, string[] commands, uint?[] expected) {
        string children = "";
        foreach(string s in commands) children += "<Cmd>"+System.Security.SecurityElement.Escape(s)+"</Cmd>";
        var xml = new XmlDocument(); xml.LoadXml(Xml("",children));
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea",Ns);
        var decl = Declaration(xml,ns);
        var original = plugin.ProcessInstance(decl);
        var actual = LogicCommandSetCompiler.ProcessInstance(decl, platform);
        Equal(label+"/instance",original.InstanceData,actual.InstanceData);
        Equal(label+"/relocations",original.RelocationData,actual.RelocationData);
        Equal(label+"/imports",original.ImportsData,actual.ImportsData);
        bool big = platform != TargetPlatform.Win32;
        int n=commands.Length, imports=0;
        Require(actual.InstanceData.Length==12+4*n,label+" root/list size (no Tokenizer header)");
        Require(Word(actual.InstanceData,0,big)==0 && Word(actual.InstanceData,4,big)==n,label+" root/count");
        Require(Word(actual.InstanceData,8,big)==(n==0?0:12),label+" list pointer");
        Require(actual.RelocationData.Length==(n==0?0:8),label+" relocation size");
        if(n!=0) Require(Word(actual.RelocationData,0,big)==8 && Word(actual.RelocationData,4,big)==uint.MaxValue,label+" relocation words");
        var offsets = new HashSet<uint>();
        for(int i=0;i<n;i++) {
            uint value=expected[i].HasValue ? unchecked(expected[i].Value+1) : 0;
            Require(Word(actual.InstanceData,12+4*i,big)==value,label+" reference "+i);
            if(expected[i].HasValue) { imports++; offsets.Add((uint)(12+4*i)); }
        }
        Require(actual.ImportsData.Length==(imports==0?0:(imports+1)*4),label+" import size");
        for(int i=0;i<imports;i++) Require(offsets.Remove(Word(actual.ImportsData,4*i,big)),label+" import offset");
        if(imports!=0) Require(Word(actual.ImportsData,4*imports,big)==uint.MaxValue,label+" import sentinel");
        layouts++; fixtures++;
        Console.WriteLine("PASS platform={0} fixture={1} commands={2} threeArrayComparisons=3 independentLayout=1",(int)platform,label,n);
    }
    static void Reject(string text, TargetPlatform p) {
        var xml = new XmlDocument(); xml.LoadXml(text);
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea",Ns);
        try { PluginSchemaBoundary.Validate(xml);  LogicCommandSetCompiler.CompileXml(xml.DocumentElement,ns,p!=TargetPlatform.Win32); }
        catch(NotSupportedException) { rejections++; return; }
        throw new Exception("Accepted unsupported input: "+text);
    }

    static AssetBuffer Compile(InstanceDeclaration d, TargetPlatform p, string type) {
        if(type=="AttributeModifier") return AttributeModifierCompiler.ProcessInstance(d,p);
        return UpgradeTemplateCompiler.ProcessInstance(d,p);
    }
    static AssetBuffer Generic(IAssetBuilderPlugin plugin, TargetPlatform p, string type, string attrs, string children, string label) {
        var doc=new XmlDocument();doc.LoadXml("<"+type+" xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</"+type+">");
        var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("ea",Ns);
        var d=Declaration(doc,ns);
        var original=plugin.ProcessInstance(d);var actual=Compile(d,p,type);
        Equal(type+"/"+label+"/instance",original.InstanceData,actual.InstanceData);
        Equal(type+"/"+label+"/relocations",original.RelocationData,actual.RelocationData);
        Equal(type+"/"+label+"/imports",original.ImportsData,actual.ImportsData);
        Require(actual.InstanceData.Length>=(type=="AttributeModifier"?60:140),type+" no truncated root");
        fixtures++;
        return actual;
    }
    static void Scalar(IAssetBuilderPlugin plugin, TargetPlatform p, string type, string attribute, string text, int offset, uint expected) {
        var b=Generic(plugin,p,type,attribute+"='"+System.Security.SecurityElement.Escape(text)+"'","",attribute+"="+text);
        Require(Word(b.InstanceData,offset,p!=TargetPlatform.Win32)==expected,type+" independent enum/scalar "+attribute+" "+text);
        layouts++;
    }
    static void RejectGeneric(string type,string attrs,string children,TargetPlatform p) {
        var doc=new XmlDocument();doc.LoadXml("<"+type+" xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</"+type+">");
        var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("ea",Ns);
        try { PluginSchemaBoundary.Validate(doc);  Compile(Declaration(doc,ns),p,type); }
        catch(NotSupportedException) { rejections++;return; }
        throw new Exception(type+" accepted unsupported "+attrs+children);
    }
    static uint FloatWord(float f) { return BitConverter.ToUInt32(BitConverter.GetBytes(f),0); }
    static void OtherTypes(IAssetBuilderPlugin plugin,TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        var a=Generic(plugin,p,"AttributeModifier","","","empty");
        Require(a.InstanceData.Length==60 && Word(a.InstanceData,36,big)==1 && a.RelocationData.Length==0 && a.ImportsData.Length==0,"AttributeModifier empty layout");layouts++;
        a=Generic(plugin,p,"AttributeModifier","Duration='2500ms' ReplaceInCategoryIfLongest='TRUE' IgnoreIfAnticategoryActive='1' StackingLimit='0xFE'","","scalars");
        Require(Word(a.InstanceData,8,big)==FloatWord(2.5f) && Word(a.InstanceData,36,big)==254 && a.InstanceData[56]==1 && a.InstanceData[57]==1,"AttributeModifier scalar layout");layouts++;
        a=Generic(plugin,p,"AttributeModifier","StartFX='Fx\\7' EndFX='Fx\\2' Shader='Shader\\3'","","resources");
        Require(a.InstanceData.Length==64 && Word(a.InstanceData,16,big)==8 && Word(a.InstanceData,20,big)==3 && Word(a.InstanceData,44,big)==60 && Word(a.InstanceData,60,big)==4,"AttributeModifier resource layout");
        Require(a.RelocationData.Length==8 && Word(a.RelocationData,0,big)==44 && a.ImportsData.Length==16,"AttributeModifier resource buffers");layouts++;
        a=Generic(plugin,p,"AttributeModifier","ModelConditionsSet='ALL' ModelConditionsClear='nonsense' ObjectStatusToSet='ALL'","","nullable-bits");
        Require(a.InstanceData.Length==208 && Word(a.InstanceData,24,big)==60 && Word(a.InstanceData,28,big)==120 && Word(a.InstanceData,32,big)==180,"AttributeModifier nullable pointer layout");
        for(int i=0;i<15;i++) Require(Word(a.InstanceData,60+4*i,big)==uint.MaxValue,"model ALL padding");
        Require(Word(a.InstanceData,120+4*(233/32),big)==(1u<<(233%32)),"model unknown fallback");
        for(int i=0;i<7;i++) Require(Word(a.InstanceData,180+4*i,big)==uint.MaxValue,"status ALL padding");layouts++;
        a=Generic(plugin,p,"AttributeModifier","Shader='' ModelConditionsSet='' ObjectStatusToSet=''","<Modifier/><Modifier Value='25%'/><Modifier Type='unknown' Value='-20'/>","empty-present-and-list");
        Require(Word(a.InstanceData,48,big)==3 && Word(a.InstanceData,52,big)==152 && a.InstanceData.Length==176,"modifier list layout");
        Require(Word(a.InstanceData,152,big)==0 && Word(a.InstanceData,160+4,big)==FloatWord(0.25f) && Word(a.InstanceData,168,big)==32,"modifier defaults/fallback");layouts++;
        foreach(string text in new[]{"0","-1","4294967296","0xFFFFFFFF","010","junk","  +12tail"}) {
            Generic(plugin,p,"AttributeModifier","StackingLimit='"+text+"'","","unsigned "+text);
            Generic(plugin,p,"UpgradeTemplate","BuildCost='"+text+"' GroupOrder='"+text+"'","","integers "+text);
        }
        foreach(string text in new[]{"0s","1.25s","1ms","2000ms","-3.5s","1D2s","ms","1sMS","2sms","junk","0x1p3s"}) {
            Generic(plugin,p,"AttributeModifier","Duration='"+text+"'","","time "+text);
            Generic(plugin,p,"UpgradeTemplate","BuildTime='"+text+"'","","time "+text);
        }
        var u=Generic(plugin,p,"UpgradeTemplate","","","empty");
        Require(u.InstanceData.Length==140 && u.RelocationData.Length==0 && u.ImportsData.Length==0,"Upgrade empty layout");layouts++;
        u=Generic(plugin,p,"UpgradeTemplate","DisplayName='' Description='abc' TypeDescription='abcd' AcquireHint='a' GroupName='group' IconImage='image' WaypointQueueable='true'","","strings");
        Require(u.InstanceData.Length==176 && Word(u.InstanceData,4,big)==0 && Word(u.InstanceData,8,big)==140 &&
            Word(u.InstanceData,12,big)==3 && Word(u.InstanceData,16,big)==144 && Word(u.InstanceData,20,big)==4 && Word(u.InstanceData,24,big)==148 &&
            Word(u.InstanceData,32,big)==156 && Word(u.InstanceData,108,big)==160 && Word(u.InstanceData,128,big)==168 && u.InstanceData[136]==1,"Upgrade string allocation layout");layouts++;
        u=Generic(plugin,p,"UpgradeTemplate","ResearchSound='a\\0' ResearchCompleteEvaEvent='b\\1' UnitSpecificSound='c\\2' UpgradeFX='d\\3' LocalPlayerGainsUpgradeEvaEvent='e\\4' AlliedPlayerGainsUpgradeEvaEvent='f\\5' EnemyPlayerGainsUpgradeEvaEvent='g\\6' LocalPlayerLosesUpgradeEvaEvent='h\\7' AlliedPlayerLosesUpgradeEvaEvent='i\\8' EnemyPlayerLosesUpgradeEvaEvent='j\\9' LocalPlayerProductionStartedEvaEvent='k\\10' LocalPlayerBuildOnHoldEvaEvent='l\\11' LocalPlayerBuildCancelledEvaEvent='m\\12'","","all-references");
        for(int i=0;i<13;i++) Require(Word(u.InstanceData,48+4*i,big)==i+1,"Upgrade reference "+i);
        Require(u.ImportsData.Length==56 && u.RelocationData.Length==0,"Upgrade all reference imports");layouts++;
        u=Generic(plugin,p,"UpgradeTemplate","","<GameDependency/>","dependency-empty");
        Require(u.InstanceData.Length==180 && Word(u.InstanceData,132,big)==140 && u.RelocationData.Length==8,"dependency 40-byte layout");layouts++;
        u=Generic(plugin,p,"UpgradeTemplate","","<GameDependency RequiredModelConditionsAny='ALL' ForbiddenModelConditions='' RequiredObjectStatusAny='ALL'><RequiredObject>ABC</RequiredObject><RequiredObject>abc</RequiredObject><ForbiddenUpgrade/><NeededUpgrade>Upgrade_A</NeededUpgrade><ObjectFilter/></GameDependency>","dependency-full");
        Require(Word(u.InstanceData,132,big)==140 && Word(u.InstanceData,140,big)==180 && Word(u.InstanceData,144,big)==240 && Word(u.InstanceData,148,big)==300,"dependency bit pointers");
        Require(Word(u.InstanceData,152,big)==2 && Word(u.InstanceData,156,big)==328 && Word(u.InstanceData,160,big)==1 && Word(u.InstanceData,164,big)==336 &&
            Word(u.InstanceData,168,big)==1 && Word(u.InstanceData,172,big)==340 && Word(u.InstanceData,176,big)==344 && u.InstanceData.Length==456,"dependency lists/filter layout");
        Require(Word(u.InstanceData,328,big)==Word(u.InstanceData,332,big),"typed ID case folding");layouts++;
        u=Generic(plugin,p,"UpgradeTemplate","UseObjectTemplateForCostDiscount='ABC'","<GameDependency><ObjectFilter id='ABC' Rule='unknown' Alignment='unknown' Relationship='ALL' Include='ALL' Exclude='unknown' StatusBitFlags='' StatusBitFlagsExclude='ALL'><IncludeThing>abc</IncludeThing><ExcludeThing>ABC</ExcludeThing></ObjectFilter></GameDependency>","filter-full");
        int filter=180;
        Require(u.InstanceData.Length==356 && Word(u.InstanceData,176,big)==filter && Word(u.InstanceData,100,big)==Word(u.InstanceData,filter,big) &&
            Word(u.InstanceData,filter+4,big)==1 && Word(u.InstanceData,filter+8,big)==uint.MaxValue && Word(u.InstanceData,filter+12,big)==1,"filter scalar/hash layout");
        for(int i=0;i<9;i++) Require(Word(u.InstanceData,filter+16+4*i,big)==uint.MaxValue,"KindOf ALL padding");
        Require(Word(u.InstanceData,filter+52+4*(149/32),big)==(1u<<(149%32)),"KindOf unknown fallback");
        Require(Word(u.InstanceData,filter+88,big)==292 && Word(u.InstanceData,filter+92,big)==320,"filter nullable statuses");layouts++;

        foreach(string expression in new[]{"ALL -NONE","-ALL NONE","all nonsense","+ALL -UNKNOWN"}) {
            BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='"+expression+"'","",60,15,-1,"ALL");
            BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='"+expression+"'","",60,7,-1,"ALL");
            BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='"+expression+"'/></GameDependency>",196,9,-1,"ALL");
        }
        foreach(string expression in new[]{"nonsense","-nonsense","-nonsense +nonsense","nonsense nonsense"}) {
            // The '-' operator XORs a bit, including on an initially clear mask.
            BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='"+expression+"'","",12,1,0,expression);
            BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='"+expression+"'","",60,15,233,expression);
            BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='"+expression+"'","",60,7,198,expression);
        }

        EnumCases(plugin,p);
        foreach(string type in new[]{"AttributeModifier","UpgradeTemplate"}) {
            RejectGeneric(type,"Unknown='1'","",p);
            RejectGeneric(type,"","<Unknown/>",p);
            RejectGeneric(type,"","text",p);
            RejectGeneric(type,"xmlns:q='urn:q' q:Duration='1s'","",p);
        }
        RejectGeneric("AttributeModifier","","<Modifier Unknown='1'/>",p);
        RejectGeneric("AttributeModifier","","<Modifier><Nested/></Modifier>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency/><GameDependency/>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency Bogus='1'/>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><NeededUpgrade Bogus='1'/></GameDependency>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><ObjectFilter/><ObjectFilter/></GameDependency>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><ObjectFilter Bogus='1'/></GameDependency>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><ObjectFilter><IncludeThing><Nested/></IncludeThing></ObjectFilter></GameDependency>",p);
    }

    static void BitCase(IAssetBuilderPlugin plugin,TargetPlatform p,string type,string attrs,string children,int baseOffset,int count,int bit,string token) {
        var b=Generic(plugin,p,type,attrs,children,"bits "+token);
        bool all=token.Equals("ALL",StringComparison.OrdinalIgnoreCase);
        for(int i=0;i<count;i++) {
            uint expected=all?uint.MaxValue:(bit>=0 && bit<count*32 && i==bit/32 ? 1u<<(bit%32):0);
            Require(Word(b.InstanceData,baseOffset+4*i,p!=TargetPlatform.Win32)==expected,"independent bit "+token+" word "+i);
        }
        layouts++;
    }

    static void EnumCases(IAssetBuilderPlugin plugin,TargetPlatform p) {
        Scalar(plugin,p,"AttributeModifier","Category","BUFF",4,unchecked((uint)7));
        Scalar(plugin,p,"AttributeModifier","Category","DEBUFF",4,unchecked((uint)8));
        Scalar(plugin,p,"AttributeModifier","Category","FORMATION",4,unchecked((uint)2));
        Scalar(plugin,p,"AttributeModifier","Category","INNATE_ARMOR",4,unchecked((uint)10));
        Scalar(plugin,p,"AttributeModifier","Category","INNATE_AUTOHEAL",4,unchecked((uint)13));
        Scalar(plugin,p,"AttributeModifier","Category","INNATE_DAMAGEMULT",4,unchecked((uint)11));
        Scalar(plugin,p,"AttributeModifier","Category","INNATE_HEALTH",4,unchecked((uint)14));
        Scalar(plugin,p,"AttributeModifier","Category","INNATE_VISION",4,unchecked((uint)12));
        Scalar(plugin,p,"AttributeModifier","Category","LEADERSHIP",4,unchecked((uint)1));
        Scalar(plugin,p,"AttributeModifier","Category","LEVEL",4,unchecked((uint)6));
        Scalar(plugin,p,"AttributeModifier","Category","NONE",4,unchecked((uint)0));
        Scalar(plugin,p,"AttributeModifier","Category","SPELL",4,unchecked((uint)3));
        Scalar(plugin,p,"AttributeModifier","Category","STRUCTURE",4,unchecked((uint)5));
        Scalar(plugin,p,"AttributeModifier","Category","STUN",4,unchecked((uint)9));
        Scalar(plugin,p,"AttributeModifier","Category","WEAPON",4,unchecked((uint)4));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","AIRBORNE",40,unchecked((uint)12));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","ALTERNATE_FORMATION",40,unchecked((uint)6));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","AS_TOWER",40,unchecked((uint)11));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","ELITE",40,unchecked((uint)2));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","HERO",40,unchecked((uint)3));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","INVALID",40,unchecked((uint)0));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","IN_SPIDER_HOLE",40,unchecked((uint)15));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","MOUNTED",40,unchecked((uint)7));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","PLAYER_UPGRADE",40,unchecked((uint)4));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","PLAYER_UPGRADE_2",40,unchecked((uint)8));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","PLAYER_UPGRADE_3",40,unchecked((uint)9));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","SECONDARY_DAMAGE",40,unchecked((uint)13));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","SHIELDBODY_ENABLED",40,unchecked((uint)22));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","SHRINK_EFFECT",40,unchecked((uint)14));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNBESIEGEABLE",40,unchecked((uint)10));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_05",40,unchecked((uint)16));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_06",40,unchecked((uint)17));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_07",40,unchecked((uint)18));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_08",40,unchecked((uint)19));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_09",40,unchecked((uint)20));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","UNUSED_10",40,unchecked((uint)21));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","VETERAN",40,unchecked((uint)1));
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","WEAK_VERSUS_BASEDEFENSES",40,unchecked((uint)5));
        Scalar(plugin,p,"UpgradeTemplate","Type","OBJECT",36,unchecked((uint)1));
        Scalar(plugin,p,"UpgradeTemplate","Type","PLAYER",36,unchecked((uint)0));
        Scalar(plugin,p,"UpgradeTemplate","Options","DO_NOT_TRANSFER",120,unchecked((uint)0));
        Scalar(plugin,p,"UpgradeTemplate","Options","NO_UPGRADE_DISCOUNT",120,unchecked((uint)2));
        Scalar(plugin,p,"UpgradeTemplate","Options","OBJECT_UPGRADE_PROJECTED",120,unchecked((uint)3));
        Scalar(plugin,p,"UpgradeTemplate","Options","PERSISTS_IN_CAMPAIGN",120,unchecked((uint)1));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","BOILINGOIL",116,unchecked((uint)2));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","FACTORY_UNITUNLOCK",116,unchecked((uint)3));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","FORTRESS",116,unchecked((uint)4));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","IGNORE",116,unchecked((uint)5));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","IMPORTANT",116,unchecked((uint)1));
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","INVALID",116,unchecked((uint)0));
        Scalar(plugin,p,"AttributeModifier","Category","unknown",4,7);
        Scalar(plugin,p,"AttributeModifier","Category","",4,7);
        Scalar(plugin,p,"AttributeModifier","Category","none",4,7);
        Scalar(plugin,p,"AttributeModifier","Category"," NONE ",4,7);
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","unknown",40,12);
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","",40,12);
        Scalar(plugin,p,"AttributeModifier","ArmorSetType","none",40,12);
        Scalar(plugin,p,"AttributeModifier","ArmorSetType"," NONE ",40,12);
        Scalar(plugin,p,"UpgradeTemplate","Type","unknown",36,1);
        Scalar(plugin,p,"UpgradeTemplate","Type","",36,1);
        Scalar(plugin,p,"UpgradeTemplate","Type","none",36,1);
        Scalar(plugin,p,"UpgradeTemplate","Type"," NONE ",36,1);
        Scalar(plugin,p,"UpgradeTemplate","Options","unknown",120,0);
        Scalar(plugin,p,"UpgradeTemplate","Options","",120,0);
        Scalar(plugin,p,"UpgradeTemplate","Options","none",120,0);
        Scalar(plugin,p,"UpgradeTemplate","Options"," NONE ",120,0);
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","unknown",116,2);
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","",116,2);
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic","none",116,2);
        Scalar(plugin,p,"UpgradeTemplate","SkirmishAIHeuristic"," NONE ",116,2);
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='AREA_OF_EFFECT' Value='125'/>","Modifier AREA_OF_EFFECT");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)32) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='ARMOR' Value='125'/>","Modifier ARMOR");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)1) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='AUTO_HEAL' Value='125'/>","Modifier AUTO_HEAL");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)20) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='BOUNTY_PERCENTAGE' Value='125'/>","Modifier BOUNTY_PERCENTAGE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)18) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='BROADCAST_RANGE' Value='125'/>","Modifier BROADCAST_RANGE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)34) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='COLLISION_GEOMETRY_SIZE_MULT' Value='125'/>","Modifier COLLISION_GEOMETRY_SIZE_MULT");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)33) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='COMMAND_POINT_BONUS' Value='125'/>","Modifier COMMAND_POINT_BONUS");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)25) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='CRUSHABLE_LEVEL' Value='125'/>","Modifier CRUSHABLE_LEVEL");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)26) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='CRUSHED_DECELERATE' Value='125'/>","Modifier CRUSHED_DECELERATE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)27) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='CRUSHER_LEVEL' Value='125'/>","Modifier CRUSHER_LEVEL");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)24) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='CRUSH_DECELERATE' Value='125'/>","Modifier CRUSH_DECELERATE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)9) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='DAMAGE_ADD' Value='125'/>","Modifier DAMAGE_ADD");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)2) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='DAMAGE_MULT' Value='125'/>","Modifier DAMAGE_MULT");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)3) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='DAMAGE_STRUCTURE_BOUNTY_ADD' Value='125'/>","Modifier DAMAGE_STRUCTURE_BOUNTY_ADD");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)23) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='EXPERIENCE' Value='125'/>","Modifier EXPERIENCE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)6) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='HEALTH' Value='125'/>","Modifier HEALTH");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)15) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='HEALTH_MULT' Value='125'/>","Modifier HEALTH_MULT");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)16) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='INVULNERABLE' Value='125'/>","Modifier INVULNERABLE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)28) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='MIN_CRUSH_VELOCITY_PERCENTAGE' Value='125'/>","Modifier MIN_CRUSH_VELOCITY_PERCENTAGE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)19) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='NONE' Value='125'/>","Modifier NONE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)0) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='POWER_BOOST' Value='125'/>","Modifier POWER_BOOST");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)31) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='PRODUCTION' Value='125'/>","Modifier PRODUCTION");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)13) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='PRODUCTION_COST' Value='125'/>","Modifier PRODUCTION_COST");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)14) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RANGE' Value='125'/>","Modifier RANGE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)7) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RATE_OF_FIRE' Value='125'/>","Modifier RATE_OF_FIRE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)22) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RECHARGE_TIME' Value='125'/>","Modifier RECHARGE_TIME");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)12) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RESIST_EMP' Value='125'/>","Modifier RESIST_EMP");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)30) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RESIST_FEAR' Value='125'/>","Modifier RESIST_FEAR");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)4) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RESIST_KNOCKBACK' Value='125'/>","Modifier RESIST_KNOCKBACK");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)10) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='RESIST_TERROR' Value='125'/>","Modifier RESIST_TERROR");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)5) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='SHROUD_CLEARING' Value='125'/>","Modifier SHROUD_CLEARING");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)21) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='SPEED' Value='125'/>","Modifier SPEED");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)8) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='SPELL_DAMAGE' Value='125'/>","Modifier SPELL_DAMAGE");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)11) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='SUPPRESSABILITY' Value='125'/>","Modifier SUPPRESSABILITY");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)29) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        { var b=Generic(plugin,p,"AttributeModifier","","<Modifier Type='VISION' Value='125'/>","Modifier VISION");Require(Word(b.InstanceData,60,p!=TargetPlatform.Win32)==unchecked((uint)17) && Word(b.InstanceData,64,p!=TargetPlatform.Win32)==FloatWord(1.25f),"independent modifier enum/value");layouts++; }
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='DEFAULT'","",12,1,0,"DEFAULT");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='EMP'","",12,1,2,"EMP");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='FREEFALL'","",12,1,7,"FREEFALL");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='HELD'","",12,1,3,"HELD");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='INFILTRATED'","",12,1,11,"INFILTRATED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='PARALYZED'","",12,1,4,"PARALYZED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='SCRIPT_DISABLED'","",12,1,9,"SCRIPT_DISABLED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='SCRIPT_UNDERPOWERED'","",12,1,10,"SCRIPT_UNDERPOWERED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='TEMPORARILY_BUSY'","",12,1,8,"TEMPORARILY_BUSY");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='UNDERPOWERED'","",12,1,6,"UNDERPOWERED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='UNMANNED'","",12,1,5,"UNMANNED");
        BitCase(plugin,p,"AttributeModifier","RemoveWhenDisabledByTypes='USER_PARALYZED'","",12,1,1,"USER_PARALYZED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ABOUT_TO_HIT'","",60,15,233,"ABOUT_TO_HIT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ACCELERATE'","",60,15,236,"ACCELERATE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ACTIVELY_BEING_CONSTRUCTED'","",60,15,169,"ACTIVELY_BEING_CONSTRUCTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ACTIVELY_CONSTRUCTING'","",60,15,171,"ACTIVELY_CONSTRUCTING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AFFECTED_BY_EMP'","",60,15,380,"AFFECTED_BY_EMP");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AFLAME'","",60,15,175,"AFLAME");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIM_FAR'","",60,15,282,"AIM_FAR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIM_HIGH'","",60,15,278,"AIM_HIGH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIM_LOW'","",60,15,280,"AIM_LOW");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIM_NEAR'","",60,15,281,"AIM_NEAR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIM_STRAIGHT'","",60,15,279,"AIM_STRAIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AIR_POWER_EFFECT'","",60,15,425,"AIR_POWER_EFFECT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ALL'","",60,15,454,"ALL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ALTERNATE_FORMATION'","",60,15,323,"ALTERNATE_FORMATION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_ALTERNATE_FORMATION'","",60,15,345,"ARMORSET_ALTERNATE_FORMATION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_ELITE'","",60,15,342,"ARMORSET_ELITE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_HERO'","",60,15,343,"ARMORSET_HERO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_MOUNTED'","",60,15,346,"ARMORSET_MOUNTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_PLAYER_UPGRADE'","",60,15,347,"ARMORSET_PLAYER_UPGRADE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_PLAYER_UPGRADE_2'","",60,15,348,"ARMORSET_PLAYER_UPGRADE_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_PLAYER_UPGRADE_3'","",60,15,349,"ARMORSET_PLAYER_UPGRADE_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_UNBESIEGEABLE'","",60,15,350,"ARMORSET_UNBESIEGEABLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_VETERAN'","",60,15,341,"ARMORSET_VETERAN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ARMORSET_WEAK_VERSUS_BASEDEFENSES'","",60,15,344,"ARMORSET_WEAK_VERSUS_BASEDEFENSES");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ATTACHED'","",60,15,261,"ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ATTACKING'","",60,15,64,"ATTACKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ATTACKING_POSITION'","",60,15,66,"ATTACKING_POSITION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ATTACKING_STRUCTURE'","",60,15,65,"ATTACKING_STRUCTURE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ATTACK_MOVING'","",60,15,153,"ATTACK_MOVING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='AWAITING_CONSTRUCTION'","",60,15,167,"AWAITING_CONSTRUCTION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BACKCRUSHED'","",60,15,2,"BACKCRUSHED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BACKING_UP'","",60,15,157,"BACKING_UP");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BASE_BUILD'","",60,15,267,"BASE_BUILD");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BEING_MAULED'","",60,15,427,"BEING_MAULED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BETWEEN_FIRING_SHOTS_A'","",60,15,93,"BETWEEN_FIRING_SHOTS_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BETWEEN_FIRING_SHOTS_B'","",60,15,94,"BETWEEN_FIRING_SHOTS_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BETWEEN_FIRING_SHOTS_C'","",60,15,95,"BETWEEN_FIRING_SHOTS_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BETWEEN_FIRING_SHOTS_D'","",60,15,96,"BETWEEN_FIRING_SHOTS_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BETWEEN_FIRING_SHOTS_E'","",60,15,97,"BETWEEN_FIRING_SHOTS_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BUILD_PLACEMENT_CURSOR'","",60,15,211,"BUILD_PLACEMENT_CURSOR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BUILD_PLACEMENT_HINT'","",60,15,443,"BUILD_PLACEMENT_HINT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BUILD_VARIATION_ONE'","",60,15,377,"BUILD_VARIATION_ONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BUILD_VARIATION_TWO'","",60,15,378,"BUILD_VARIATION_TWO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BURNED'","",60,15,177,"BURNED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BURNINGDEATH'","",60,15,180,"BURNINGDEATH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BURNT_MODEL'","",60,15,178,"BURNT_MODEL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='BURNT_TEXTURE'","",60,15,179,"BURNT_TEXTURE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CANCEL_CAPTURE'","",60,15,217,"CANCEL_CAPTURE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURED'","",60,15,219,"CAPTURED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURE_0'","",60,15,328,"CAPTURE_0");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURE_100'","",60,15,324,"CAPTURE_100");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURE_25'","",60,15,327,"CAPTURE_25");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURE_50'","",60,15,326,"CAPTURE_50");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURE_75'","",60,15,325,"CAPTURE_75");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CAPTURING'","",60,15,218,"CAPTURING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CARRYING'","",60,15,192,"CARRYING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CHARGING'","",60,15,149,"CHARGING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CHRONORIFT'","",60,15,430,"CHRONORIFT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CLIMBING'","",60,15,158,"CLIMBING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='COLLAPSING'","",60,15,10,"COLLAPSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='COMBINED_CHILD'","",60,15,420,"COMBINED_CHILD");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='COMBINED_PARENT'","",60,15,419,"COMBINED_PARENT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='COMING_OUT_OF_FACTORY'","",60,15,152,"COMING_OUT_OF_FACTORY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CONTINUOUS_FIRE_FAST'","",60,15,223,"CONTINUOUS_FIRE_FAST");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CONTINUOUS_FIRE_MEAN'","",60,15,222,"CONTINUOUS_FIRE_MEAN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='CONTINUOUS_FIRE_SLOW'","",60,15,221,"CONTINUOUS_FIRE_SLOW");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='COVER'","",60,15,334,"COVER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DAMAGED'","",60,15,3,"DAMAGED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_1'","",60,15,251,"DEATH_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_10'","",60,15,448,"DEATH_10");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_11'","",60,15,449,"DEATH_11");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_12'","",60,15,450,"DEATH_12");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_13'","",60,15,451,"DEATH_13");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_14'","",60,15,452,"DEATH_14");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_15'","",60,15,453,"DEATH_15");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_2'","",60,15,252,"DEATH_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_3'","",60,15,253,"DEATH_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_4'","",60,15,254,"DEATH_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_5'","",60,15,255,"DEATH_5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_6'","",60,15,444,"DEATH_6");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_7'","",60,15,445,"DEATH_7");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_8'","",60,15,446,"DEATH_8");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEATH_9'","",60,15,447,"DEATH_9");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEBUG'","",60,15,215,"DEBUG");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DECAY'","",60,15,8,"DECAY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DECELERATE'","",60,15,237,"DECELERATE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEFLECT_SPECIAL_POWER'","",60,15,311,"DEFLECT_SPECIAL_POWER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DEPLOYED'","",60,15,265,"DEPLOYED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_BACK'","",60,15,244,"DESTROYED_BACK");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_FRONT'","",60,15,242,"DESTROYED_FRONT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_LEFT'","",60,15,245,"DESTROYED_LEFT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_RIGHT'","",60,15,243,"DESTROYED_RIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_WEAPON'","",60,15,62,"DESTROYED_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DESTROYED_WHILST_BEING_CONSTRUCTED'","",60,15,9,"DESTROYED_WHILST_BEING_CONSTRUCTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DISGUISED'","",60,15,329,"DISGUISED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DIVING'","",60,15,154,"DIVING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING'","",60,15,181,"DOCKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_ACTIVE'","",60,15,183,"DOCKING_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_BEGINNING'","",60,15,182,"DOCKING_BEGINNING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_ENDING'","",60,15,188,"DOCKING_ENDING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_EXTRACT'","",60,15,187,"DOCKING_EXTRACT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_FILL'","",60,15,186,"DOCKING_FILL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_MOVEBACK'","",60,15,185,"DOCKING_MOVEBACK");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_PRE_DOCK'","",60,15,189,"DOCKING_PRE_DOCK");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOCKING_RETURN'","",60,15,184,"DOCKING_RETURN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_1_CLOSING'","",60,15,104,"DOOR_1_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_1_OPENING'","",60,15,103,"DOOR_1_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_1_WAITING_OPEN'","",60,15,105,"DOOR_1_WAITING_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_1_WAITING_TO_CLOSE'","",60,15,106,"DOOR_1_WAITING_TO_CLOSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_2_CLOSING'","",60,15,108,"DOOR_2_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_2_OPENING'","",60,15,107,"DOOR_2_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_2_WAITING_OPEN'","",60,15,109,"DOOR_2_WAITING_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_2_WAITING_TO_CLOSE'","",60,15,110,"DOOR_2_WAITING_TO_CLOSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_3_CLOSING'","",60,15,112,"DOOR_3_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_3_OPENING'","",60,15,111,"DOOR_3_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_3_WAITING_OPEN'","",60,15,113,"DOOR_3_WAITING_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_3_WAITING_TO_CLOSE'","",60,15,114,"DOOR_3_WAITING_TO_CLOSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_4_CLOSING'","",60,15,116,"DOOR_4_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_4_OPENING'","",60,15,115,"DOOR_4_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_4_WAITING_OPEN'","",60,15,117,"DOOR_4_WAITING_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_4_WAITING_TO_CLOSE'","",60,15,118,"DOOR_4_WAITING_TO_CLOSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_HELIPAD_CLOSING'","",60,15,120,"DOOR_HELIPAD_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_HELIPAD_OPENING'","",60,15,119,"DOOR_HELIPAD_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_HELIPAD_WAITING_OPEN'","",60,15,121,"DOOR_HELIPAD_WAITING_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DOOR_HELIPAD_WAITING_TO_CLOSE'","",60,15,122,"DOOR_HELIPAD_WAITING_TO_CLOSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DRAFTED'","",60,15,262,"DRAFTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DYING'","",60,15,144,"DYING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DYING_WASATTACKING'","",60,15,146,"DYING_WASATTACKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='DYING_WASMOVING'","",60,15,145,"DYING_WASMOVING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EATING'","",60,15,227,"EATING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_AFRAID'","",60,15,367,"EMOTION_AFRAID");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_ALERT'","",60,15,366,"EMOTION_ALERT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_AMUSED'","",60,15,358,"EMOTION_AMUSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_BRACE_FOR_BEING_CRUSHED'","",60,15,364,"EMOTION_BRACE_FOR_BEING_CRUSHED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_CELEBRATING'","",60,15,357,"EMOTION_CELEBRATING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_CHEER_FOR_ABOUT_TO_CRUSH'","",60,15,365,"EMOTION_CHEER_FOR_ABOUT_TO_CRUSH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_COWER'","",60,15,361,"EMOTION_COWER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_DISSIDENT'","",60,15,362,"EMOTION_DISSIDENT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_DOOM'","",60,15,353,"EMOTION_DOOM");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_GUNG_HO'","",60,15,355,"EMOTION_GUNG_HO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_LOOK_TO_SKY'","",60,15,356,"EMOTION_LOOK_TO_SKY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_MORALE_HIGH'","",60,15,359,"EMOTION_MORALE_HIGH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_MORALE_LOW'","",60,15,360,"EMOTION_MORALE_LOW");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_PANIC'","",60,15,369,"EMOTION_PANIC");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_POINTING'","",60,15,354,"EMOTION_POINTING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_TAUNTING'","",60,15,352,"EMOTION_TAUNTING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_TERROR'","",60,15,368,"EMOTION_TERROR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EMOTION_UNCONTROLLABLY_AFRAID'","",60,15,363,"EMOTION_UNCONTROLLABLY_AFRAID");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='ENGAGED'","",60,15,67,"ENGAGED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EXPLODED_BOUNCING'","",60,15,235,"EXPLODED_BOUNCING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='EXPLODED_FLAILING'","",60,15,234,"EXPLODED_FLAILING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_A'","",60,15,78,"FIRING_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_B'","",60,15,79,"FIRING_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_C'","",60,15,80,"FIRING_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_D'","",60,15,81,"FIRING_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_E'","",60,15,82,"FIRING_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_PREATTACK_A'","",60,15,73,"FIRING_OR_PREATTACK_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_PREATTACK_B'","",60,15,74,"FIRING_OR_PREATTACK_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_PREATTACK_C'","",60,15,75,"FIRING_OR_PREATTACK_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_PREATTACK_D'","",60,15,76,"FIRING_OR_PREATTACK_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_PREATTACK_E'","",60,15,77,"FIRING_OR_PREATTACK_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_RELOADING_A'","",60,15,83,"FIRING_OR_RELOADING_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_RELOADING_B'","",60,15,84,"FIRING_OR_RELOADING_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_RELOADING_C'","",60,15,85,"FIRING_OR_RELOADING_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_RELOADING_D'","",60,15,86,"FIRING_OR_RELOADING_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FIRING_OR_RELOADING_E'","",60,15,87,"FIRING_OR_RELOADING_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FLOODED'","",60,15,263,"FLOODED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FLYING'","",60,15,164,"FLYING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FORMATION_PREVIEW'","",60,15,213,"FORMATION_PREVIEW");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FREEFALL'","",60,15,16,"FREEFALL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FRONTCRUSHED'","",60,15,1,"FRONTCRUSHED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='FROZEN'","",60,15,428,"FROZEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='GARRISONED'","",60,15,20,"GARRISONED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='GUARDING'","",60,15,257,"GUARDING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HARVEST_ACTION'","",60,15,191,"HARVEST_ACTION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HARVEST_PREPARATION'","",60,15,190,"HARVEST_PREPARATION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HEALTH_PERCENT_0'","",60,15,414,"HEALTH_PERCENT_0");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HEALTH_PERCENT_100'","",60,15,418,"HEALTH_PERCENT_100");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HEALTH_PERCENT_25'","",60,15,415,"HEALTH_PERCENT_25");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HEALTH_PERCENT_50'","",60,15,416,"HEALTH_PERCENT_50");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HEALTH_PERCENT_75'","",60,15,417,"HEALTH_PERCENT_75");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIDDEN'","",60,15,330,"HIDDEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIGH_TECH_EFFECT'","",60,15,424,"HIGH_TECH_EFFECT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIT_LEVEL_1'","",60,15,275,"HIT_LEVEL_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIT_LEVEL_2'","",60,15,276,"HIT_LEVEL_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIT_LEVEL_3'","",60,15,277,"HIT_LEVEL_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HIT_REACTION'","",60,15,274,"HIT_REACTION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='HORDE_EMPTY'","",60,15,321,"HORDE_EMPTY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='IMPENDING_DOOM'","",60,15,226,"IMPENDING_DOOM");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_DISABLE'","",60,15,431,"INFILTRATED_DISABLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_ENERGY'","",60,15,435,"INFILTRATED_ENERGY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_RADAR_FREEZE'","",60,15,432,"INFILTRATED_RADAR_FREEZE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_RESET'","",60,15,436,"INFILTRATED_RESET");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_STEAL_MONEY'","",60,15,433,"INFILTRATED_STEAL_MONEY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_VISION'","",60,15,434,"INFILTRATED_VISION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INFILTRATED_VISION_UNITS'","",60,15,437,"INFILTRATED_VISION_UNITS");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INSIDE_GARRISON'","",60,15,21,"INSIDE_GARRISON");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INVALID'","",60,15,-1,"INVALID");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INVISIBLE_CAMOUFLAGE'","",60,15,332,"INVISIBLE_CAMOUFLAGE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INVISIBLE_STEALTH'","",60,15,331,"INVISIBLE_STEALTH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='INVULNERABLE'","",60,15,370,"INVULNERABLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='IRONCURTAIN'","",60,15,429,"IRONCURTAIN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='JETAFTERBURNER'","",60,15,197,"JETAFTERBURNER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='JETEXHAUST'","",60,15,198,"JETEXHAUST");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='JUST_BUILT'","",60,15,266,"JUST_BUILT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LANDING'","",60,15,166,"LANDING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LAUNCHED'","",60,15,19,"LAUNCHED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LEASHED_RETURNING'","",60,15,373,"LEASHED_RETURNING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LEVELED'","",60,15,258,"LEVELED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOADED'","",60,15,264,"LOADED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_BURNINGDEATH'","",60,15,405,"LOCOMOTOR_BURNINGDEATH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_CHANGING_FIRINGARC'","",60,15,404,"LOCOMOTOR_CHANGING_FIRINGARC");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_COMBO'","",60,15,400,"LOCOMOTOR_COMBO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_COMBO2'","",60,15,401,"LOCOMOTOR_COMBO2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_COMBO3'","",60,15,402,"LOCOMOTOR_COMBO3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_CONTAINED'","",60,15,399,"LOCOMOTOR_CONTAINED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_ENRAGED'","",60,15,397,"LOCOMOTOR_ENRAGED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_FREEFALL'","",60,15,391,"LOCOMOTOR_FREEFALL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_MOUNTED'","",60,15,396,"LOCOMOTOR_MOUNTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_NORMAL_UPGRADED'","",60,15,390,"LOCOMOTOR_NORMAL_UPGRADED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_PANIC'","",60,15,393,"LOCOMOTOR_PANIC");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_SCARED'","",60,15,398,"LOCOMOTOR_SCARED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_SUPERSONIC'","",60,15,395,"LOCOMOTOR_SUPERSONIC");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_TAXIING'","",60,15,394,"LOCOMOTOR_TAXIING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_WALL_SCALING'","",60,15,403,"LOCOMOTOR_WALL_SCALING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='LOCOMOTOR_WANDER'","",60,15,392,"LOCOMOTOR_WANDER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MARCHING'","",60,15,156,"MARCHING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MARKED_FOR_HUNT_TACTIC'","",60,15,388,"MARKED_FOR_HUNT_TACTIC");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MARKED_FOR_NO_SKIRMISH_RECRUIT'","",60,15,389,"MARKED_FOR_NO_SKIRMISH_RECRUIT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MONEY_STORED_AMOUNT_1'","",60,15,407,"MONEY_STORED_AMOUNT_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MONEY_STORED_AMOUNT_2'","",60,15,408,"MONEY_STORED_AMOUNT_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MONEY_STORED_AMOUNT_3'","",60,15,409,"MONEY_STORED_AMOUNT_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MONEY_STORED_AMOUNT_4'","",60,15,410,"MONEY_STORED_AMOUNT_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MOUNTED'","",60,15,259,"MOUNTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MOVING'","",60,15,143,"MOVING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='MOVING_OUT_OF_THE_WAY'","",60,15,150,"MOVING_OUT_OF_THE_WAY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='NIGHT'","",60,15,14,"NIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='OVER_WATER'","",60,15,208,"OVER_WATER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING'","",60,15,199,"PACKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_1'","",60,15,202,"PACKING_TYPE_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_2'","",60,15,203,"PACKING_TYPE_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_3'","",60,15,204,"PACKING_TYPE_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_4'","",60,15,205,"PACKING_TYPE_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_5'","",60,15,206,"PACKING_TYPE_5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PACKING_TYPE_6'","",60,15,207,"PACKING_TYPE_6");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PANICKING'","",60,15,174,"PANICKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARACHUTE_LAND'","",60,15,18,"PARACHUTE_LAND");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARACHUTING'","",60,15,17,"PARACHUTING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARALYZED'","",60,15,379,"PARALYZED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_1_DOOR_CLOSED'","",60,15,126,"PARKINGPLACE_1_DOOR_CLOSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_1_DOOR_CLOSING'","",60,15,125,"PARKINGPLACE_1_DOOR_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_1_DOOR_OPEN'","",60,15,124,"PARKINGPLACE_1_DOOR_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_1_DOOR_OPENING'","",60,15,123,"PARKINGPLACE_1_DOOR_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_2_DOOR_CLOSED'","",60,15,130,"PARKINGPLACE_2_DOOR_CLOSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_2_DOOR_CLOSING'","",60,15,129,"PARKINGPLACE_2_DOOR_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_2_DOOR_OPEN'","",60,15,128,"PARKINGPLACE_2_DOOR_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_2_DOOR_OPENING'","",60,15,127,"PARKINGPLACE_2_DOOR_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_3_DOOR_CLOSED'","",60,15,134,"PARKINGPLACE_3_DOOR_CLOSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_3_DOOR_CLOSING'","",60,15,133,"PARKINGPLACE_3_DOOR_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_3_DOOR_OPEN'","",60,15,132,"PARKINGPLACE_3_DOOR_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_3_DOOR_OPENING'","",60,15,131,"PARKINGPLACE_3_DOOR_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_4_DOOR_CLOSED'","",60,15,138,"PARKINGPLACE_4_DOOR_CLOSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_4_DOOR_CLOSING'","",60,15,137,"PARKINGPLACE_4_DOOR_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_4_DOOR_OPEN'","",60,15,136,"PARKINGPLACE_4_DOOR_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_4_DOOR_OPENING'","",60,15,135,"PARKINGPLACE_4_DOOR_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_HELIPAD_DOOR_CLOSED'","",60,15,142,"PARKINGPLACE_HELIPAD_DOOR_CLOSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_HELIPAD_DOOR_CLOSING'","",60,15,141,"PARKINGPLACE_HELIPAD_DOOR_CLOSING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_HELIPAD_DOOR_OPEN'","",60,15,140,"PARKINGPLACE_HELIPAD_DOOR_OPEN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARKINGPLACE_HELIPAD_DOOR_OPENING'","",60,15,139,"PARKINGPLACE_HELIPAD_DOOR_OPENING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PARTIALLY_CONSTRUCTED'","",60,15,168,"PARTIALLY_CONSTRUCTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER'","",60,15,193,"PASSENGER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER_VARIATION_1'","",60,15,293,"PASSENGER_VARIATION_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER_VARIATION_2'","",60,15,294,"PASSENGER_VARIATION_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER_VARIATION_3'","",60,15,295,"PASSENGER_VARIATION_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER_VARIATION_4'","",60,15,296,"PASSENGER_VARIATION_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PASSENGER_VARIATION_5'","",60,15,297,"PASSENGER_VARIATION_5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PHANTOM_STRUCTURE'","",60,15,212,"PHANTOM_STRUCTURE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PLAYER_POWER_1'","",60,15,426,"PLAYER_POWER_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='POST_COLLAPSE'","",60,15,7,"POST_COLLAPSE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='POST_RUBBLE'","",60,15,6,"POST_RUBBLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='POWER_PLANT_UPGRADED'","",60,15,209,"POWER_PLANT_UPGRADED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='POWER_PLANT_UPGRADING'","",60,15,210,"POWER_PLANT_UPGRADING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREATTACK_A'","",60,15,68,"PREATTACK_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREATTACK_B'","",60,15,69,"PREATTACK_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREATTACK_C'","",60,15,70,"PREATTACK_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREATTACK_D'","",60,15,71,"PREATTACK_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREATTACK_E'","",60,15,72,"PREATTACK_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREORDER'","",60,15,224,"PREORDER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PREPARING'","",60,15,200,"PREPARING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PRIMARY_FORMATION'","",60,15,322,"PRIMARY_FORMATION");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='PRONE'","",60,15,333,"PRONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RADAR_EXTENDING'","",60,15,172,"RADAR_EXTENDING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RADAR_UPGRADED'","",60,15,173,"RADAR_UPGRADED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RAISING_FLAG'","",60,15,220,"RAISING_FLAG");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RAPPELLING'","",60,15,159,"RAPPELLING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_1'","",60,15,268,"REACT_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_2'","",60,15,269,"REACT_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_3'","",60,15,270,"REACT_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_4'","",60,15,271,"REACT_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_5'","",60,15,272,"REACT_5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REACT_6'","",60,15,273,"REACT_6");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REALLYDAMAGED'","",60,15,4,"REALLYDAMAGED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RELOADING_A'","",60,15,88,"RELOADING_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RELOADING_B'","",60,15,89,"RELOADING_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RELOADING_C'","",60,15,90,"RELOADING_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RELOADING_D'","",60,15,91,"RELOADING_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RELOADING_E'","",60,15,92,"RELOADING_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='REPAIRING_DISABLED'","",60,15,421,"REPAIRING_DISABLED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RESURRECTED'","",60,15,260,"RESURRECTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER1'","",60,15,312,"RIDER1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER2'","",60,15,313,"RIDER2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER3'","",60,15,314,"RIDER3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER4'","",60,15,315,"RIDER4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER5'","",60,15,316,"RIDER5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER6'","",60,15,317,"RIDER6");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER7'","",60,15,318,"RIDER7");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDER8'","",60,15,319,"RIDER8");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RIDERLESS'","",60,15,320,"RIDERLESS");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RUBBLE'","",60,15,5,"RUBBLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='RUNNING_OFF_MAP'","",60,15,151,"RUNNING_OFF_MAP");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SAIL_BLOWN_LEFT'","",60,15,376,"SAIL_BLOWN_LEFT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SAIL_BLOWN_RIGHT'","",60,15,375,"SAIL_BLOWN_RIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SAIL_FLAPPING'","",60,15,374,"SAIL_FLAPPING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SCALING_WALL'","",60,15,162,"SCALING_WALL");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SCALING_WALL_HORDE'","",60,15,163,"SCALING_WALL_HORDE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SELECTED'","",60,15,256,"SELECTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SHRINK_EFFECT'","",60,15,423,"SHRINK_EFFECT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SIEGE_CONTAIN'","",60,15,196,"SIEGE_CONTAIN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SMOLDERING'","",60,15,176,"SMOLDERING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SNOW'","",60,15,15,"SNOW");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SOLD'","",60,15,11,"SOLD");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER1_READY'","",60,15,301,"SPECIALPOWER1_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER2_READY'","",60,15,302,"SPECIALPOWER2_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER3_READY'","",60,15,303,"SPECIALPOWER3_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER4_READY'","",60,15,304,"SPECIALPOWER4_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER5_READY'","",60,15,305,"SPECIALPOWER5_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER6_READY'","",60,15,306,"SPECIALPOWER6_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER7_READY'","",60,15,307,"SPECIALPOWER7_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER8_READY'","",60,15,308,"SPECIALPOWER8_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIALPOWER9_READY'","",60,15,309,"SPECIALPOWER9_READY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_CHEERING'","",60,15,225,"SPECIAL_CHEERING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_DAMAGED'","",60,15,13,"SPECIAL_DAMAGED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_ENEMY_NEAR'","",60,15,371,"SPECIAL_ENEMY_NEAR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_POWER_1'","",60,15,298,"SPECIAL_POWER_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_POWER_2'","",60,15,299,"SPECIAL_POWER_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_POWER_3'","",60,15,300,"SPECIAL_POWER_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_FIVE'","",60,15,60,"SPECIAL_WEAPON_FIVE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_FOUR'","",60,15,59,"SPECIAL_WEAPON_FOUR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_ONE'","",60,15,56,"SPECIAL_WEAPON_ONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_SIX'","",60,15,61,"SPECIAL_WEAPON_SIX");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_THREE'","",60,15,58,"SPECIAL_WEAPON_THREE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPECIAL_WEAPON_TWO'","",60,15,57,"SPECIAL_WEAPON_TWO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SPLATTED'","",60,15,231,"SPLATTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='START_CAPTURE'","",60,15,216,"START_CAPTURE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='STRUCTURE_UNPACKING'","",60,15,12,"STRUCTURE_UNPACKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='STUNNED'","",60,15,229,"STUNNED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='STUNNED_FLAILING'","",60,15,228,"STUNNED_FLAILING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='STUNNED_STANDING_UP'","",60,15,230,"STUNNED_STANDING_UP");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SUCKED_UP_HIGH'","",60,15,455,"SUCKED_UP_HIGH");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SUPPRESSED'","",60,15,335,"SUPPRESSED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SWAPPING_TO_WEAPONSET_1'","",60,15,43,"SWAPPING_TO_WEAPONSET_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SWAPPING_TO_WEAPONSET_2'","",60,15,44,"SWAPPING_TO_WEAPONSET_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SWAPPING_TO_WEAPONSET_3'","",60,15,45,"SWAPPING_TO_WEAPONSET_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SWIMMING'","",60,15,161,"SWIMMING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='SWOOPING'","",60,15,155,"SWOOPING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TAKING_OFF'","",60,15,165,"TAKING_OFF");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='THROWN_PROJECTILE'","",60,15,232,"THROWN_PROJECTILE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_CRYSTAL_TYPE1'","",60,15,382,"TIBERIUM_CRYSTAL_TYPE1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_CRYSTAL_TYPE2'","",60,15,383,"TIBERIUM_CRYSTAL_TYPE2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_CRYSTAL_TYPE3'","",60,15,384,"TIBERIUM_CRYSTAL_TYPE3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_CRYSTAL_TYPE4'","",60,15,385,"TIBERIUM_CRYSTAL_TYPE4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_CRYSTAL_TYPE5'","",60,15,386,"TIBERIUM_CRYSTAL_TYPE5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TIBERIUM_GROWING'","",60,15,387,"TIBERIUM_GROWING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TOPPLED'","",60,15,0,"TOPPLED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TRANSPORT_MOVING'","",60,15,194,"TRANSPORT_MOVING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TRANSPORT_STOPPED'","",60,15,195,"TRANSPORT_STOPPED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURN_LEFT'","",60,15,238,"TURN_LEFT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURN_LEFT_HIGH_SPEED'","",60,15,240,"TURN_LEFT_HIGH_SPEED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURN_RIGHT'","",60,15,239,"TURN_RIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURN_RIGHT_HIGH_SPEED'","",60,15,241,"TURN_RIGHT_HIGH_SPEED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURRET_ANGLE_0'","",60,15,336,"TURRET_ANGLE_0");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURRET_ANGLE_180'","",60,15,338,"TURRET_ANGLE_180");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURRET_ANGLE_270'","",60,15,339,"TURRET_ANGLE_270");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURRET_ANGLE_90'","",60,15,337,"TURRET_ANGLE_90");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='TURRET_ROTATE'","",60,15,340,"TURRET_ROTATE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='UNCONTROLLABLE'","",60,15,372,"UNCONTROLLABLE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='UNDERPOWERED'","",60,15,381,"UNDERPOWERED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='UNIT_ACTIVELY_BEING_CONSTRUCTED'","",60,15,170,"UNIT_ACTIVELY_BEING_CONSTRUCTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='UNPACKING'","",60,15,201,"UNPACKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='UPGRADED_ARMOR'","",60,15,351,"UPGRADED_ARMOR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_1'","",60,15,283,"USER_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_10'","",60,15,292,"USER_10");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_2'","",60,15,284,"USER_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_3'","",60,15,285,"USER_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_4'","",60,15,286,"USER_4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_5'","",60,15,287,"USER_5");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_6'","",60,15,288,"USER_6");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_7'","",60,15,289,"USER_7");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_8'","",60,15,290,"USER_8");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USER_9'","",60,15,291,"USER_9");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_COMBO_LOCOMOTOR'","",60,15,406,"USING_COMBO_LOCOMOTOR");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_SPECIAL_ABILITY'","",60,15,310,"USING_SPECIAL_ABILITY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_WEAPON_A'","",60,15,98,"USING_WEAPON_A");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_WEAPON_B'","",60,15,99,"USING_WEAPON_B");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_WEAPON_C'","",60,15,100,"USING_WEAPON_C");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_WEAPON_D'","",60,15,101,"USING_WEAPON_D");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='USING_WEAPON_E'","",60,15,102,"USING_WEAPON_E");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='VEHICLE_CRUSH_CENTER'","",60,15,413,"VEHICLE_CRUSH_CENTER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='VEHICLE_CRUSH_LEFT'","",60,15,411,"VEHICLE_CRUSH_LEFT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='VEHICLE_CRUSH_RIGHT'","",60,15,412,"VEHICLE_CRUSH_RIGHT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WADING'","",60,15,160,"WADING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WALKING'","",60,15,148,"WALKING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WALL_SEGMENT'","",60,15,422,"WALL_SEGMENT");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WANDER'","",60,15,147,"WANDER");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONLOCK_PRIMARY'","",60,15,246,"WEAPONLOCK_PRIMARY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONLOCK_QUATERNARY'","",60,15,249,"WEAPONLOCK_QUATERNARY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONLOCK_QUINARY'","",60,15,250,"WEAPONLOCK_QUINARY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONLOCK_SECONDARY'","",60,15,247,"WEAPONLOCK_SECONDARY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONLOCK_TERTIARY'","",60,15,248,"WEAPONLOCK_TERTIARY");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_CLOSE_RANGE'","",60,15,33,"WEAPONSET_CLOSE_RANGE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_CONTAINED'","",60,15,41,"WEAPONSET_CONTAINED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_CONTESTING_BUILDING'","",60,15,34,"WEAPONSET_CONTESTING_BUILDING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_ELITE'","",60,15,23,"WEAPONSET_ELITE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_GARRISONED'","",60,15,32,"WEAPONSET_GARRISONED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_HERO'","",60,15,24,"WEAPONSET_HERO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_MOUNTED'","",60,15,42,"WEAPONSET_MOUNTED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_PASSENGER_TYPE_ONE'","",60,15,25,"WEAPONSET_PASSENGER_TYPE_ONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_PASSENGER_TYPE_TWO'","",60,15,26,"WEAPONSET_PASSENGER_TYPE_TWO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_PLAYER_UPGRADE'","",60,15,27,"WEAPONSET_PLAYER_UPGRADE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_RIDER1'","",60,15,35,"WEAPONSET_RIDER1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_RIDER2'","",60,15,36,"WEAPONSET_RIDER2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_RIDER3'","",60,15,37,"WEAPONSET_RIDER3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_RIDER4'","",60,15,38,"WEAPONSET_RIDER4");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_SPECIAL_ONE'","",60,15,39,"WEAPONSET_SPECIAL_ONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_SPECIAL_TWO'","",60,15,40,"WEAPONSET_SPECIAL_TWO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_SPECIAL_UPGRADE'","",60,15,31,"WEAPONSET_SPECIAL_UPGRADE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_TOGGLE_1'","",60,15,28,"WEAPONSET_TOGGLE_1");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_TOGGLE_2'","",60,15,29,"WEAPONSET_TOGGLE_2");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_TOGGLE_3'","",60,15,30,"WEAPONSET_TOGGLE_3");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSET_VETERAN'","",60,15,22,"WEAPONSET_VETERAN");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_01'","",60,15,51,"WEAPONSLOTID_01");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_02'","",60,15,52,"WEAPONSLOTID_02");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_03'","",60,15,53,"WEAPONSLOTID_03");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_04'","",60,15,54,"WEAPONSLOTID_04");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_05'","",60,15,55,"WEAPONSLOTID_05");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_06'","",60,15,438,"WEAPONSLOTID_06");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_07'","",60,15,439,"WEAPONSLOTID_07");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_08'","",60,15,440,"WEAPONSLOTID_08");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_09'","",60,15,441,"WEAPONSLOTID_09");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSLOTID_10'","",60,15,442,"WEAPONSLOTID_10");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSTATE_CLOSE_RANGE'","",60,15,49,"WEAPONSTATE_CLOSE_RANGE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSTATE_CONTAINED'","",60,15,50,"WEAPONSTATE_CONTAINED");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSTATE_ONE'","",60,15,46,"WEAPONSTATE_ONE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSTATE_THREE'","",60,15,48,"WEAPONSTATE_THREE");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPONSTATE_TWO'","",60,15,47,"WEAPONSTATE_TWO");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WEAPON_TOGGLING'","",60,15,63,"WEAPON_TOGGLING");
        BitCase(plugin,p,"AttributeModifier","ModelConditionsSet='WORLD_BUILDER'","",60,15,214,"WORLD_BUILDER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ABSORBED_DAMAGE'","",60,7,198,"ABSORBED_DAMAGE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ADVANCED_MISSILE_PACKS'","",60,7,182,"ADVANCED_MISSILE_PACKS");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='AFLAME'","",60,7,11,"AFLAME");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='AIRBORNE_TARGET'","",60,7,7,"AIRBORNE_TARGET");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE'","",60,7,151,"AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='AIR_LOCOMOTOR_ACTIVE'","",60,7,161,"AIR_LOCOMOTOR_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ANTI_GARRISON'","",60,7,222,"ANTI_GARRISON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ATTACHED'","",60,7,102,"ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ATTACKING_GARRISONED_STRUCTURE'","",60,7,135,"ATTACKING_GARRISONED_STRUCTURE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BOOBY_TRAPPED'","",60,7,109,"BOOBY_TRAPPED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BOOBY_TRAP_EXPLODE'","",60,7,146,"BOOBY_TRAP_EXPLODE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BOOKENDING'","",60,7,86,"BOOKENDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BRIDGE_IMPASSABLE'","",60,7,140,"BRIDGE_IMPASSABLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BROADCASTING_INVISIBILITY'","",60,7,214,"BROADCASTING_INVISIBILITY");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BUILD_BEING_CANCELED'","",60,7,89,"BUILD_BEING_CANCELED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='BURNED'","",60,7,12,"BURNED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CANNOT_BE_SOLD'","",60,7,13,"CANNOT_BE_SOLD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CANNOT_LAND_AT_AIRFIELD'","",60,7,202,"CANNOT_LAND_AT_AIRFIELD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_ATTACK'","",60,7,1,"CAN_ATTACK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_ATTACK_WHILE_STEALTHED'","",60,7,156,"CAN_ATTACK_WHILE_STEALTHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_CONTEST_GARRISON'","",60,7,88,"CAN_CONTEST_GARRISON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_ENTER_ANYTHING'","",60,7,96,"CAN_ENTER_ANYTHING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_NOT_WALK_ON'","",60,7,80,"CAN_NOT_WALK_ON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_STEALTH'","",60,7,20,"CAN_STEALTH");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CAN_STEALTH_FROM_PRODUCER'","",60,7,154,"CAN_STEALTH_FROM_PRODUCER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CARRYING_FLAG'","",60,7,148,"CARRYING_FLAG");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CHANTING'","",60,7,57,"CHANTING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CHARGING_BASE_DEFENSES'","",60,7,98,"CHARGING_BASE_DEFENSES");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CHARGING_WEAPON'","",60,7,177,"CHARGING_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CLEARED_FOR_LANDING'","",60,7,171,"CLEARED_FOR_LANDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CLONED'","",60,7,76,"CLONED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='COMBINED_ATTACHED'","",60,7,133,"COMBINED_ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='COMBINED_CHILD'","",60,7,132,"COMBINED_CHILD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='COMBINED_PARENT'","",60,7,131,"COMBINED_PARENT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='COMMAND_BUTTON_TOGGLED'","",60,7,104,"COMMAND_BUTTON_TOGGLED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CONTAINER_OCCUPIED'","",60,7,164,"CONTAINER_OCCUPIED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CONTESTED'","",60,7,38,"CONTESTED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='CONTESTING_BUILDING'","",60,7,39,"CONTESTING_BUILDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DAMAGED'","",60,7,43,"DAMAGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEATH_1'","",60,7,33,"DEATH_1");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEATH_2'","",60,7,34,"DEATH_2");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEATH_3'","",60,7,35,"DEATH_3");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEATH_4'","",60,7,36,"DEATH_4");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEATH_5'","",60,7,37,"DEATH_5");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEFLECT_INCOMING_FIRE'","",60,7,166,"DEFLECT_INCOMING_FIRE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DELAYED_ENTER_STRUCTURE'","",60,7,137,"DELAYED_ENTER_STRUCTURE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEPART_FROM_BOARDING'","",60,7,167,"DEPART_FROM_BOARDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEPLOYED'","",60,7,62,"DEPLOYED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DEPLOYING'","",60,7,67,"DEPLOYING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DESTROYED'","",60,7,0,"DESTROYED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DETECTED'","",60,7,19,"DETECTED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DOCKING'","",60,7,138,"DOCKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DOES_CONTAIN_TIBERIUM'","",60,7,119,"DOES_CONTAIN_TIBERIUM");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DONT_CLEAR_FOR_BUILD'","",60,7,114,"DONT_CLEAR_FOR_BUILD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DO_NOT_AVOID_TALLBUILDING'","",60,7,197,"DO_NOT_AVOID_TALLBUILDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DO_NOT_PICK_ME'","",60,7,82,"DO_NOT_PICK_ME");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='DO_NOT_SCORE'","",60,7,79,"DO_NOT_SCORE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ENABLE_TRACER_DRAW'","",60,7,179,"ENABLE_TRACER_DRAW");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ENCLOSED'","",60,7,64,"ENCLOSED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='END_FIRE_STATE'","",60,7,85,"END_FIRE_STATE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ENRAGED'","",60,7,58,"ENRAGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='ENTERING_GOAL'","",60,7,97,"ENTERING_GOAL");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='EXCEEDED_PATH_NEIGHBOR_COUNT'","",60,7,201,"EXCEEDED_PATH_NEIGHBOR_COUNT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='EXECUTING_STORED_COMMAND'","",60,7,111,"EXECUTING_STORED_COMMAND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='EXITING_COMBINED'","",60,7,136,"EXITING_COMBINED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='EXPLOSIVES_ATTACHED'","",60,7,209,"EXPLOSIVES_ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='EXPLOSIVES_DETONATING'","",60,7,210,"EXPLOSIVES_DETONATING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='FLEE_OFF_MAP'","",60,7,54,"FLEE_OFF_MAP");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='FOLLOWING_THROUGH'","",60,7,31,"FOLLOWING_THROUGH");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='FORCE_ATTACKING'","",60,7,142,"FORCE_ATTACKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='FORCE_ATTACK_MOVING'","",60,7,143,"FORCE_ATTACK_MOVING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GARRISONED'","",60,7,108,"GARRISONED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GARRISON_CAN_STEALTH'","",60,7,155,"GARRISON_CAN_STEALTH");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GENERIC_TOGGLE_STATE'","",60,7,87,"GENERIC_TOGGLE_STATE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GRAPPLE_UPDATE_ACTIVE'","",60,7,189,"GRAPPLE_UPDATE_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GRAPPLE_UPDATE_BEING_MOVED'","",60,7,219,"GRAPPLE_UPDATE_BEING_MOVED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='GROUND_LOCOMOTOR_ACTIVE'","",60,7,160,"GROUND_LOCOMOTOR_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HARVESTING'","",60,7,139,"HARVESTING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HAS_SECONDARY_DAMAGE'","",60,7,169,"HAS_SECONDARY_DAMAGE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HAS_TIBERIUM_GROWTH_MOD'","",60,7,116,"HAS_TIBERIUM_GROWTH_MOD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HAS_TIBERIUM_UPGRADE'","",60,7,117,"HAS_TIBERIUM_UPGRADE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HEALTH_PERCENT_0'","",60,7,123,"HEALTH_PERCENT_0");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HEALTH_PERCENT_100'","",60,7,127,"HEALTH_PERCENT_100");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HEALTH_PERCENT_25'","",60,7,124,"HEALTH_PERCENT_25");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HEALTH_PERCENT_50'","",60,7,125,"HEALTH_PERCENT_50");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HEALTH_PERCENT_75'","",60,7,126,"HEALTH_PERCENT_75");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HIDDEN'","",60,7,18,"HIDDEN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HIJACKED'","",60,7,10,"HIJACKED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HIT_SHIELD_SPHERE'","",60,7,175,"HIT_SHIELD_SPHERE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='HORDE_MEMBER'","",60,7,40,"HORDE_MEMBER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IGNORE_AI_COMMAND'","",60,7,77,"IGNORE_AI_COMMAND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IGNORING_POWER_DOWN'","",60,7,200,"IGNORING_POWER_DOWN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IGNORING_STEALTH'","",60,7,29,"IGNORING_STEALTH");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IMMOBILE'","",60,7,51,"IMMOBILE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IMMOBILE_ALLOW_ROTATE'","",60,7,52,"IMMOBILE_ALLOW_ROTATE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IMMUNE_TO_BARK'","",60,7,204,"IMMUNE_TO_BARK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='INAUDIBLE'","",60,7,56,"INAUDIBLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='INHERITED_FROM_ALLY_TEAM'","",60,7,83,"INHERITED_FROM_ALLY_TEAM");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='INSIDE_GARRISON'","",60,7,61,"INSIDE_GARRISON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='INVISIBLE_DETECTED'","",60,7,100,"INVISIBLE_DETECTED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='INVISIBLE_DETECTED_BY_FRIEND'","",60,7,99,"INVISIBLE_DETECTED_BY_FRIEND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_COVER'","",60,7,113,"IN_COVER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_FORMATION_TEMPLATE'","",60,7,92,"IN_FORMATION_TEMPLATE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_NANOHIVE'","",60,7,212,"IN_NANOHIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_SHIELD_SPHERE'","",60,7,174,"IN_SHIELD_SPHERE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_SPIDER_HOLE'","",60,7,221,"IN_SPIDER_HOLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IN_STASIS'","",60,7,48,"IN_STASIS");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IRRADIATED'","",60,7,46,"IRRADIATED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_AIMING_WEAPON'","",60,7,27,"IS_AIMING_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_ATTACKING'","",60,7,24,"IS_ATTACKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_BEING_DRAGGED'","",60,7,170,"IS_BEING_DRAGGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_BEING_HARVESTED'","",60,7,120,"IS_BEING_HARVESTED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_BRAKING'","",60,7,16,"IS_BRAKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_ENGAGED'","",60,7,147,"IS_ENGAGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_FIRING_WEAPON'","",60,7,14,"IS_FIRING_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_HIDEOUT'","",60,7,110,"IS_HIDEOUT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_LEAVING_FACTORY'","",60,7,93,"IS_LEAVING_FACTORY");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_MELEE_ATTACKING'","",60,7,30,"IS_MELEE_ATTACKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_MOVING_TO_RALLY_POINT'","",60,7,149,"IS_MOVING_TO_RALLY_POINT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='IS_RELOADING_WEAPON'","",60,7,15,"IS_RELOADING_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LANDING_IN_PROGRESS'","",60,7,207,"LANDING_IN_PROGRESS");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LEASHED_RETURNING'","",60,7,32,"LEASHED_RETURNING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LEECHED_TARGET_ACTIVE'","",60,7,183,"LEECHED_TARGET_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LIFTED_INTO_AIR'","",60,7,211,"LIFTED_INTO_AIR");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LINE_SEGMENT_DAMAGE_ACTIVE'","",60,7,181,"LINE_SEGMENT_DAMAGE_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='LOADED_FROM_MAP'","",60,7,134,"LOADED_FROM_MAP");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='MAGNETIZED'","",60,7,216,"MAGNETIZED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='MARCH_OF_DEATH'","",60,7,81,"MARCH_OF_DEATH");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='MATCH_TARGETS_SPEED'","",60,7,115,"MATCH_TARGETS_SPEED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='MOVING_TO_DISMOUNT'","",60,7,94,"MOVING_TO_DISMOUNT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NEXT_MOVE_IS_FORCE_ATTACK_MOVE'","",60,7,152,"NEXT_MOVE_IS_FORCE_ATTACK_MOVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NEXT_MOVE_IS_REVERSE'","",60,7,50,"NEXT_MOVE_IS_REVERSE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NON_AUTOACQUIRABLE'","",60,7,176,"NON_AUTOACQUIRABLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NOT_IN_WORLD'","",60,7,55,"NOT_IN_WORLD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_ATTACK'","",60,7,5,"NO_ATTACK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_ATTACK_FROM_AI'","",60,7,28,"NO_ATTACK_FROM_AI");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_AUTO_ACQUIRE'","",60,7,25,"NO_AUTO_ACQUIRE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_BRIBE'","",60,7,199,"NO_BRIBE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_COLLISIONS'","",60,7,4,"NO_COLLISIONS");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_HERO_PROPERTIES'","",60,7,95,"NO_HERO_PROPERTIES");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_REFUND'","",60,7,121,"NO_REFUND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_SHADOW'","",60,7,47,"NO_SHADOW");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_SPECIAL_ABILITY'","",60,7,6,"NO_SPECIAL_ABILITY");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='NO_SQUISHCOLLIDE_DELAY'","",60,7,70,"NO_SQUISHCOLLIDE_DELAY");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='OCLMONITOR_COMPLETED_TASK'","",60,7,105,"OCLMONITOR_COMPLETED_TASK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='OCLMONITOR_MONITOR_RELEASED'","",60,7,106,"OCLMONITOR_MONITOR_RELEASED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='OUT_OF_PHASE'","",60,7,49,"OUT_OF_PHASE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='OVERCHARGING_WEAPON'","",60,7,178,"OVERCHARGING_WEAPON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='OVER_WATER'","",60,7,163,"OVER_WATER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PARACHUTING'","",60,7,8,"PARACHUTING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PARKED_AT_AIRFIELD'","",60,7,173,"PARKED_AT_AIRFIELD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PAUSING'","",60,7,53,"PAUSING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PENDING_CONSTRUCTION'","",60,7,90,"PENDING_CONSTRUCTION");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PHANTOM_STRUCTURE'","",60,7,91,"PHANTOM_STRUCTURE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLACED_BY_PLAYER'","",60,7,165,"PLACED_BY_PLAYER");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLAYER_POWER_1'","",60,7,184,"PLAYER_POWER_1");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLAYER_POWER_2'","",60,7,185,"PLAYER_POWER_2");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLAYER_POWER_3'","",60,7,186,"PLAYER_POWER_3");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLAYER_POWER_4'","",60,7,187,"PLAYER_POWER_4");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PLAYER_POWER_5'","",60,7,188,"PLAYER_POWER_5");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='POINT_DEFENSE_DRONE_ATTACHED'","",60,7,218,"POINT_DEFENSE_DRONE_ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PORTER_TAGGED'","",60,7,69,"PORTER_TAGGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='POWERED_DOWN_EMP'","",60,7,141,"POWERED_DOWN_EMP");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='PREEMPT_PRE_ATTACK'","",60,7,180,"PREEMPT_PRE_ATTACK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RAMPAGING'","",60,7,60,"RAMPAGING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='REALLYDAMAGED'","",60,7,44,"REALLYDAMAGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RECONSTRUCTING'","",60,7,23,"RECONSTRUCTING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='REPAIR_ALLIES_WHEN_IDLE'","",60,7,196,"REPAIR_ALLIES_WHEN_IDLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='REPULSOR'","",60,7,9,"REPULSOR");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RESET_GOAL_POSITION'","",60,7,157,"RESET_GOAL_POSITION");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RIDER_IS_PILOT'","",60,7,42,"RIDER_IS_PILOT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RIFT_OCCUPIED'","",60,7,158,"RIFT_OCCUPIED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RIOT_SHIELDED'","",60,7,217,"RIOT_SHIELDED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RUBBLE'","",60,7,45,"RUBBLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='RUNNING_DOWN_FROM_BEHIND'","",60,7,78,"RUNNING_DOWN_FROM_BEHIND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SCARED_CIVILIAN_CAR'","",60,7,144,"SCARED_CIVILIAN_CAR");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SCRAMBLED'","",60,7,215,"SCRAMBLED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SHIELDBODY_ENABLED'","",60,7,122,"SHIELDBODY_ENABLED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SHROUD_REVEAL_TO_ALL'","",60,7,190,"SHROUD_REVEAL_TO_ALL");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SHRUNK'","",60,7,101,"SHRUNK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SINKING'","",60,7,59,"SINKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SKIRMISH_AI_DO_NOT_ATTACK'","",60,7,193,"SKIRMISH_AI_DO_NOT_ATTACK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SKIRMISH_AI_DO_NOT_TAKE'","",60,7,194,"SKIRMISH_AI_DO_NOT_TAKE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SOLD'","",60,7,21,"SOLD");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SPECIALABILITY_ACTIVE'","",60,7,153,"SPECIALABILITY_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING'","",60,7,73,"SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SPECIAL_ARMOR_ACTIVE'","",60,7,150,"SPECIAL_ARMOR_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='STAND_GROUND'","",60,7,71,"STAND_GROUND");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='STEALTHED'","",60,7,17,"STEALTHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='STEAL_NEXT_UNIT_TRAPPED'","",60,7,118,"STEAL_NEXT_UNIT_TRAPPED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SUBMERGED'","",60,7,192,"SUBMERGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SURFACED'","",60,7,191,"SURFACED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SURFACED_IMMOBILE'","",60,7,213,"SURFACED_IMMOBILE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='SWITCHED_WEAPONS'","",60,7,84,"SWITCHED_WEAPONS");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TAGGED'","",60,7,66,"TAGGED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TARGETED_FOR_REPAIR'","",60,7,220,"TARGETED_FOR_REPAIR");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TEMPORARILY_DEFECTED'","",60,7,65,"TEMPORARILY_DEFECTED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TIBERIUM_VIBRATING'","",60,7,74,"TIBERIUM_VIBRATING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TOPPLING'","",60,7,68,"TOPPLING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TRANSFORMATION_TOGGLE_STATE'","",60,7,195,"TRANSFORMATION_TOGGLE_STATE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='TRANSPORT_MOVING_TO_CONTAIN'","",60,7,41,"TRANSPORT_MOVING_TO_CONTAIN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNATTACKABLE'","",60,7,63,"UNATTACKABLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNCONTROLLABLY_SCARED'","",60,7,72,"UNCONTROLLABLY_SCARED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNDERGOING_REPAIR'","",60,7,22,"UNDERGOING_REPAIR");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNDER_CONSTRUCTION'","",60,7,2,"UNDER_CONSTRUCTION");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNDER_FROZEN'","",60,7,206,"UNDER_FROZEN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNDER_IRON_CURTAIN'","",60,7,208,"UNDER_IRON_CURTAIN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNDER_RUSH_ATTACK'","",60,7,205,"UNDER_RUSH_ATTACK");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNIT_WANTS_TO_REGARRISON'","",60,7,112,"UNIT_WANTS_TO_REGARRISON");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNPACKING'","",60,7,162,"UNPACKING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UNSELECTABLE'","",60,7,3,"UNSELECTABLE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='UPDATING_AI'","",60,7,75,"UPDATING_AI");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='USER_PARALYZED'","",60,7,145,"USER_PARALYZED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='USER_POWERED_DOWN'","",60,7,107,"USER_POWERED_DOWN");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='USING_ABILITY'","",60,7,26,"USING_ABILITY");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='VEHICLE_ATTACHED'","",60,7,172,"VEHICLE_ATTACHED");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WAITING_FOR_BOARDING'","",60,7,168,"WAITING_FOR_BOARDING");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WALL_SEGMENT'","",60,7,203,"WALL_SEGMENT");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WATER_LOCOMOTOR_ACTIVE'","",60,7,159,"WATER_LOCOMOTOR_ACTIVE");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WEAPON_UPGRADED_01'","",60,7,128,"WEAPON_UPGRADED_01");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WEAPON_UPGRADED_02'","",60,7,129,"WEAPON_UPGRADED_02");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WEAPON_UPGRADED_03'","",60,7,130,"WEAPON_UPGRADED_03");
        BitCase(plugin,p,"AttributeModifier","ObjectStatusToSet='WONT_RIDE_WITH_YOU'","",60,7,103,"WONT_RIDE_WITH_YOU");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ACTIVATE_AFTER_UNPACK'/></GameDependency>",196,9,149,"ACTIVATE_AFTER_UNPACK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AIRCRAFT'/></GameDependency>",196,9,14,"AIRCRAFT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AIRCRAFT_PATH_AROUND'/></GameDependency>",196,9,66,"AIRCRAFT_PATH_AROUND");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED'/></GameDependency>",196,9,251,"AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AIRFIELD'/></GameDependency>",196,9,36,"AIRFIELD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AI_CAN_UNPACK_NEAR_CONYARD'/></GameDependency>",196,9,143,"AI_CAN_UNPACK_NEAR_CONYARD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AI_NEEDS_PLAYER_POWER_UPDATE'/></GameDependency>",196,9,137,"AI_NEEDS_PLAYER_POWER_UPDATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALLY_TARGETABLE'/></GameDependency>",196,9,3,"ALLY_TARGETABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES'/></GameDependency>",196,9,187,"ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALWAYS_SELECTABLE'/></GameDependency>",196,9,59,"ALWAYS_SELECTABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALWAYS_SHOW_HOUSE_COLOR'/></GameDependency>",196,9,171,"ALWAYS_SHOW_HOUSE_COLOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALWAYS_VISIBLE'/></GameDependency>",196,9,54,"ALWAYS_VISIBLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ALWAYS_VISIBLE_IN_RADAR'/></GameDependency>",196,9,274,"ALWAYS_VISIBLE_IN_RADAR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AMPHIBIOUS'/></GameDependency>",196,9,211,"AMPHIBIOUS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ANTI_GARRISON'/></GameDependency>",196,9,242,"ANTI_GARRISON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ARMY_SUMMARY'/></GameDependency>",196,9,126,"ARMY_SUMMARY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ASSAULT_AIRCRAFT'/></GameDependency>",196,9,239,"ASSAULT_AIRCRAFT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ATTACH_ATTACK'/></GameDependency>",196,9,275,"ATTACH_ATTACK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED'/></GameDependency>",196,9,253,"ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AUTO_ACQUIRABLE_BY_AI'/></GameDependency>",196,9,230,"AUTO_ACQUIRABLE_BY_AI");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='AUTO_RALLYPOINT'/></GameDependency>",196,9,69,"AUTO_RALLYPOINT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BALLISTIC_MISSILE'/></GameDependency>",196,9,75,"BALLISTIC_MISSILE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BANNER'/></GameDependency>",196,9,157,"BANNER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BASE_FOUNDATION'/></GameDependency>",196,9,101,"BASE_FOUNDATION");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BASE_SITE'/></GameDependency>",196,9,118,"BASE_SITE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BEACON'/></GameDependency>",196,9,247,"BEACON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BEAM_TARGET'/></GameDependency>",196,9,111,"BEAM_TARGET");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BEAM_TARGET_REFLECTOR'/></GameDependency>",196,9,112,"BEAM_TARGET_REFLECTOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BLOCKING_GATE'/></GameDependency>",196,9,135,"BLOCKING_GATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BOMBER_AIRCRAFT'/></GameDependency>",196,9,240,"BOMBER_AIRCRAFT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BRIDGE'/></GameDependency>",196,9,23,"BRIDGE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BRIDGE_ENDCAP'/></GameDependency>",196,9,256,"BRIDGE_ENDCAP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BRIDGE_GATEHOUSE'/></GameDependency>",196,9,257,"BRIDGE_GATEHOUSE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BRIDGE_SEGMENT'/></GameDependency>",196,9,219,"BRIDGE_SEGMENT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BROADCASTS_INVISIBILITY'/></GameDependency>",196,9,144,"BROADCASTS_INVISIBILITY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BUFF'/></GameDependency>",196,9,139,"BUFF");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='BUILD_FOR_FREE'/></GameDependency>",196,9,154,"BUILD_FOR_FREE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANNOT_BE_DETECTED'/></GameDependency>",196,9,229,"CANNOT_BE_DETECTED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANNOT_BUILD_NEAR_SUPPLIES'/></GameDependency>",196,9,85,"CANNOT_BUILD_NEAR_SUPPLIES");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANNOT_HIJACK'/></GameDependency>",196,9,270,"CANNOT_HIJACK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANNOT_RETALIATE'/></GameDependency>",196,9,164,"CANNOT_RETALIATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANNOT_STOP_ON_WATER'/></GameDependency>",196,9,195,"CANNOT_STOP_ON_WATER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CANT_TOGGLE_POWER'/></GameDependency>",196,9,24,"CANT_TOGGLE_POWER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_ATTACK'/></GameDependency>",196,9,5,"CAN_ATTACK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_ATTACK_STEALTHED'/></GameDependency>",196,9,227,"CAN_ATTACK_STEALTHED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_ATTACK_WALLS'/></GameDependency>",196,9,145,"CAN_ATTACK_WALLS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_BE_CAPTURED'/></GameDependency>",196,9,104,"CAN_BE_CAPTURED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_BE_FAVORITE_UNIT'/></GameDependency>",196,9,258,"CAN_BE_FAVORITE_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_BE_REPULSED'/></GameDependency>",196,9,46,"CAN_BE_REPULSED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_BUILD_ON_DEEP_WATER'/></GameDependency>",196,9,265,"CAN_BUILD_ON_DEEP_WATER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_BUILD_ON_WATER'/></GameDependency>",196,9,264,"CAN_BUILD_ON_WATER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_CAPTURE'/></GameDependency>",196,9,70,"CAN_CAPTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_CAST_REFLECTIONS'/></GameDependency>",196,9,7,"CAN_CAST_REFLECTIONS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_CLIMB_WALLS'/></GameDependency>",196,9,174,"CAN_CLIMB_WALLS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_HEAL_ALLIES'/></GameDependency>",196,9,261,"CAN_HEAL_ALLIES");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_NOT_BUILD_ON_LAND'/></GameDependency>",196,9,263,"CAN_NOT_BUILD_ON_LAND");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_PLACE_CHARGE'/></GameDependency>",196,9,57,"CAN_PLACE_CHARGE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_PLACE_MANIPULATOR'/></GameDependency>",196,9,83,"CAN_PLACE_MANIPULATOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_RAPPEL'/></GameDependency>",196,9,44,"CAN_RAPPEL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_SEE_THROUGH_STRUCTURE'/></GameDependency>",196,9,74,"CAN_SEE_THROUGH_STRUCTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_SHOOT_OVER_WALLS'/></GameDependency>",196,9,208,"CAN_SHOOT_OVER_WALLS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_SKIP_SHADOW'/></GameDependency>",196,9,190,"CAN_SKIP_SHADOW");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAN_TOPPLE'/></GameDependency>",196,9,188,"CAN_TOPPLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAPTURABLE'/></GameDependency>",196,9,50,"CAPTURABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAPTUREFLAG'/></GameDependency>",196,9,194,"CAPTUREFLAG");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CARPET_BOMBER'/></GameDependency>",196,9,282,"CARPET_BOMBER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CASTLE_CENTER'/></GameDependency>",196,9,19,"CASTLE_CENTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CASTLE_KEEP'/></GameDependency>",196,9,29,"CASTLE_KEEP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CAVALRY'/></GameDependency>",196,9,11,"CAVALRY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CHUNK_VENDOR'/></GameDependency>",196,9,130,"CHUNK_VENDOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CIVILIAN_BUILDING'/></GameDependency>",196,9,201,"CIVILIAN_BUILDING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CIVILIAN_UNIT'/></GameDependency>",196,9,127,"CIVILIAN_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CLASSIFY_ENTIRE_FOOTPRINT'/></GameDependency>",196,9,58,"CLASSIFY_ENTIRE_FOOTPRINT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CLEARED_BY_BUILD'/></GameDependency>",196,9,52,"CLEARED_BY_BUILD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CLICK_THROUGH'/></GameDependency>",196,9,76,"CLICK_THROUGH");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='COMBO_HORDE'/></GameDependency>",196,9,107,"COMBO_HORDE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='COMMANDCENTER'/></GameDependency>",196,9,18,"COMMANDCENTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='COMMANDO'/></GameDependency>",196,9,47,"COMMANDO");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CONSTRUCTION_YARD'/></GameDependency>",196,9,207,"CONSTRUCTION_YARD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='COVER'/></GameDependency>",196,9,217,"COVER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CRANE'/></GameDependency>",196,9,136,"CRANE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CRATE'/></GameDependency>",196,9,49,"CRATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CREEP'/></GameDependency>",196,9,165,"CREEP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CRUSHABLE_BUILDING'/></GameDependency>",196,9,271,"CRUSHABLE_BUILDING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CRUSHABLE_OBSTACLE'/></GameDependency>",196,9,231,"CRUSHABLE_OBSTACLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CRUSH_CRUSHABLE_BUILDINGS'/></GameDependency>",196,9,272,"CRUSH_CRUSHABLE_BUILDINGS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='CYCLE_SELECTION'/></GameDependency>",196,9,189,"CYCLE_SELECTION");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DEBRIS'/></GameDependency>",196,9,280,"DEBRIS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DEFENSIVE_WALL'/></GameDependency>",196,9,61,"DEFENSIVE_WALL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DEFLECT_BY_SPECIAL_POWER'/></GameDependency>",196,9,99,"DEFLECT_BY_SPECIAL_POWER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DEPLOYED_MINE'/></GameDependency>",196,9,163,"DEPLOYED_MINE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DEPRECATED'/></GameDependency>",196,9,122,"DEPRECATED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DISGUISER'/></GameDependency>",196,9,88,"DISGUISER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_AUTO_CRUSH_INFANTRY'/></GameDependency>",196,9,91,"DONT_AUTO_CRUSH_INFANTRY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_DESTROY_IF_ON_BRIDGE'/></GameDependency>",196,9,281,"DONT_DESTROY_IF_ON_BRIDGE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_HIDE_IF_FOGGED'/></GameDependency>",196,9,203,"DONT_HIDE_IF_FOGGED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_SUBMERGE_BY_WAVES'/></GameDependency>",196,9,84,"DONT_SUBMERGE_BY_WAVES");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_UNATTACH_WHEN_HEALING'/></GameDependency>",196,9,276,"DONT_UNATTACH_WHEN_HEALING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DONT_USE_CANCEL_BUILD_BUTTON'/></GameDependency>",196,9,200,"DONT_USE_CANCEL_BUILD_BUTTON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DOZER'/></GameDependency>",196,9,16,"DOZER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DOZER_FACTORY'/></GameDependency>",196,9,184,"DOZER_FACTORY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DO_NOT_CLASSIFY'/></GameDependency>",196,9,147,"DO_NOT_CLASSIFY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DO_NOT_PICK_ME_WHEN_BUILDING'/></GameDependency>",196,9,168,"DO_NOT_PICK_ME_WHEN_BUILDING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DRAWABLE_ONLY'/></GameDependency>",196,9,37,"DRAWABLE_ONLY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='DRONE'/></GameDependency>",196,9,73,"DRONE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ECONOMY_STRUCTURE'/></GameDependency>",196,9,186,"ECONOMY_STRUCTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='EGG'/></GameDependency>",196,9,245,"EGG");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ENCLOSURE'/></GameDependency>",196,9,268,"ENCLOSURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ENGINEER'/></GameDependency>",196,9,243,"ENGINEER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ENVIRONMENT'/></GameDependency>",196,9,98,"ENVIRONMENT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='EXPANSION_PAD'/></GameDependency>",196,9,210,"EXPANSION_PAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='EXPANSION_UNIT'/></GameDependency>",196,9,244,"EXPANSION_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FIGHTER_AIRCRAFT'/></GameDependency>",196,9,241,"FIGHTER_AIRCRAFT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FORCEATTACKABLE'/></GameDependency>",196,9,68,"FORCEATTACKABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_AIR_FIELD'/></GameDependency>",196,9,223,"FS_AIR_FIELD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_BARRACKS'/></GameDependency>",196,9,221,"FS_BARRACKS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_BASE_DEFENSE'/></GameDependency>",196,9,64,"FS_BASE_DEFENSE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_CASH_PRODUCER'/></GameDependency>",196,9,133,"FS_CASH_PRODUCER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_FACTORY'/></GameDependency>",196,9,63,"FS_FACTORY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_MONEY_STORAGE'/></GameDependency>",196,9,213,"FS_MONEY_STORAGE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_POWER'/></GameDependency>",196,9,62,"FS_POWER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_RADAR'/></GameDependency>",196,9,222,"FS_RADAR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_TECHNOLOGY'/></GameDependency>",196,9,65,"FS_TECHNOLOGY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_TECH_CENTER'/></GameDependency>",196,9,224,"FS_TECH_CENTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='FS_WAR_FACTORY'/></GameDependency>",196,9,220,"FS_WAR_FACTORY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GARRISON'/></GameDependency>",196,9,116,"GARRISON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GARRISONABLE_UNTIL_DESTROYED'/></GameDependency>",196,9,79,"GARRISONABLE_UNTIL_DESTROYED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GRABBABLE'/></GameDependency>",196,9,206,"GRABBABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GRAB_AND_DROP'/></GameDependency>",196,9,140,"GRAB_AND_DROP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GRAB_AND_KILL'/></GameDependency>",196,9,96,"GRAB_AND_KILL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='GRANTS_VETERANCY'/></GameDependency>",196,9,273,"GRANTS_VETERANCY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HARVESTABLE'/></GameDependency>",196,9,205,"HARVESTABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HARVESTER'/></GameDependency>",196,9,17,"HARVESTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HAS_HEALTH_BAR'/></GameDependency>",196,9,161,"HAS_HEALTH_BAR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HEAL_PAD'/></GameDependency>",196,9,33,"HEAL_PAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HEAVY_MELEE_HITTER'/></GameDependency>",196,9,202,"HEAVY_MELEE_HITTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HIDE_IF_FOGGED'/></GameDependency>",196,9,170,"HIDE_IF_FOGGED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HORDE'/></GameDependency>",196,9,106,"HORDE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HUGE_VEHICLE'/></GameDependency>",196,9,15,"HUGE_VEHICLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HULK'/></GameDependency>",196,9,82,"HULK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='HUSK'/></GameDependency>",196,9,248,"HUSK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORED_IN_FINDPOSITIONAROUND'/></GameDependency>",196,9,105,"IGNORED_IN_FINDPOSITIONAROUND");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORED_IN_GUI'/></GameDependency>",196,9,48,"IGNORED_IN_GUI");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORES_SELECT_ALL'/></GameDependency>",196,9,90,"IGNORES_SELECT_ALL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORE_CAN_NOT_BUILD'/></GameDependency>",196,9,234,"IGNORE_CAN_NOT_BUILD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORE_FORCE_MOVE'/></GameDependency>",196,9,249,"IGNORE_FORCE_MOVE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORE_FOR_EVA_SPEECH_POSITION'/></GameDependency>",196,9,155,"IGNORE_FOR_EVA_SPEECH_POSITION");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORE_FOR_VICTORY'/></GameDependency>",196,9,146,"IGNORE_FOR_VICTORY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IGNORE_IN_AI_HUNT_TACTIC'/></GameDependency>",196,9,162,"IGNORE_IN_AI_HUNT_TACTIC");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IMMOBILE'/></GameDependency>",196,9,4,"IMMOBILE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IMMUNE_TO_CAPTURE'/></GameDependency>",196,9,81,"IMMUNE_TO_CAPTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='IMMUNE_TO_CYCLONIC_SHOCKWAVE'/></GameDependency>",196,9,228,"IMMUNE_TO_CYCLONIC_SHOCKWAVE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='INERT'/></GameDependency>",196,9,89,"INERT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='INERT_SHROUD_REVEALER'/></GameDependency>",196,9,119,"INERT_SHROUD_REVEALER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='INFANTRY'/></GameDependency>",196,9,10,"INFANTRY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='INFILTRATOR'/></GameDependency>",196,9,218,"INFILTRATOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='KEEP_CLASSIFIED_WHEN_DEAD'/></GameDependency>",196,9,129,"KEEP_CLASSIFIED_WHEN_DEAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LARGE_RECTANGLE_PATHFIND'/></GameDependency>",196,9,176,"LARGE_RECTANGLE_PATHFIND");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LEAVE_PARKING_ON_BUILD_COMPLETE'/></GameDependency>",196,9,267,"LEAVE_PARKING_ON_BUILD_COMPLETE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LIMITED_PRODUCTION_AIRCRAFT'/></GameDependency>",196,9,266,"LIMITED_PRODUCTION_AIRCRAFT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LINE_OF_SIGHT_IGNORES_BUILDINGS'/></GameDependency>",196,9,250,"LINE_OF_SIGHT_IGNORES_BUILDINGS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LINKED_TO_FLAG'/></GameDependency>",196,9,51,"LINKED_TO_FLAG");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='LOW_OVERLAPPABLE'/></GameDependency>",196,9,67,"LOW_OVERLAPPABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MACHINE'/></GameDependency>",196,9,13,"MACHINE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MCV'/></GameDependency>",196,9,260,"MCV");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MELEE_HORDE'/></GameDependency>",196,9,117,"MELEE_HORDE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MINE'/></GameDependency>",196,9,56,"MINE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MINE_TRIGGER'/></GameDependency>",196,9,138,"MINE_TRIGGER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MONSTER'/></GameDependency>",196,9,12,"MONSTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MOVE_FOR_NOONE'/></GameDependency>",196,9,172,"MOVE_FOR_NOONE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MOVE_ONLY'/></GameDependency>",196,9,132,"MOVE_ONLY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='MP_COUNT_FOR_VICTORY'/></GameDependency>",196,9,38,"MP_COUNT_FOR_VICTORY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NEED_BASE_FOUNDATION'/></GameDependency>",196,9,102,"NEED_BASE_FOUNDATION");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NEUTRAL_TECH'/></GameDependency>",196,9,232,"NEUTRAL_TECH");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NEVER_CULL_FOR_MP'/></GameDependency>",196,9,199,"NEVER_CULL_FOR_MP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NONOCCLUDING'/></GameDependency>",196,9,108,"NONOCCLUDING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NOTIFY_OF_PREATTACK'/></GameDependency>",196,9,115,"NOTIFY_OF_PREATTACK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NOT_AUTOACQUIRABLE'/></GameDependency>",196,9,128,"NOT_AUTOACQUIRABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NOT_SELLABLE'/></GameDependency>",196,9,151,"NOT_SELLABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_BASE_CAPTURE'/></GameDependency>",196,9,125,"NO_BASE_CAPTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_COLLIDE'/></GameDependency>",196,9,31,"NO_COLLIDE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_FORMATION_MOVEMENT'/></GameDependency>",196,9,124,"NO_FORMATION_MOVEMENT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_FREEWILL_ENTER'/></GameDependency>",196,9,109,"NO_FREEWILL_ENTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_GARRISON'/></GameDependency>",196,9,28,"NO_GARRISON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_HEAL_ICON'/></GameDependency>",196,9,43,"NO_HEAL_ICON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='NO_SHADOW'/></GameDependency>",196,9,175,"NO_SHADOW");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='OBSTACLE'/></GameDependency>",196,9,0,"OBSTACLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='OCL_BIT'/></GameDependency>",196,9,120,"OCL_BIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='OPTIMIZED_PROP'/></GameDependency>",196,9,97,"OPTIMIZED_PROP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='OPTIMIZED_SOUND'/></GameDependency>",196,9,182,"OPTIMIZED_SOUND");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ORE_NODE'/></GameDependency>",196,9,159,"ORE_NODE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ORIENTS_TO_CAMERA'/></GameDependency>",196,9,198,"ORIENTS_TO_CAMERA");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='OUTPOST'/></GameDependency>",196,9,246,"OUTPOST");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PARACHUTABLE'/></GameDependency>",196,9,45,"PARACHUTABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PARACHUTE'/></GameDependency>",196,9,78,"PARACHUTE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PASS_EXPERIENCE_TO_CONTAINED'/></GameDependency>",196,9,183,"PASS_EXPERIENCE_TO_CONTAINED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PASS_EXPERIENCE_TO_CONTAINER'/></GameDependency>",196,9,196,"PASS_EXPERIENCE_TO_CONTAINER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PASS_EXPERIENCE_TO_PRODUCER'/></GameDependency>",196,9,209,"PASS_EXPERIENCE_TO_PRODUCER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PASS_EXPERIENCE_TO_SLAVER'/></GameDependency>",196,9,160,"PASS_EXPERIENCE_TO_SLAVER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PATH_THROUGH_EACH_OTHER'/></GameDependency>",196,9,114,"PATH_THROUGH_EACH_OTHER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PATH_THROUGH_INFANTRY'/></GameDependency>",196,9,123,"PATH_THROUGH_INFANTRY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PORT'/></GameDependency>",196,9,178,"PORT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='POWERED'/></GameDependency>",196,9,71,"POWERED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='POWERED_POWERS_ONLY'/></GameDependency>",196,9,166,"POWERED_POWERS_ONLY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PRELOAD'/></GameDependency>",196,9,27,"PRELOAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PRODUCED_AT_HELIPAD'/></GameDependency>",196,9,72,"PRODUCED_AT_HELIPAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PROJECTILE'/></GameDependency>",196,9,26,"PROJECTILE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='PROJECT_BUILDABILITY_FOR_ALLIES'/></GameDependency>",196,9,262,"PROJECT_BUILDABILITY_FOR_ALLIES");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='RADAR_SCRAMBLER'/></GameDependency>",196,9,269,"RADAR_SCRAMBLER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='REACT_WHEN_SELECTED'/></GameDependency>",196,9,103,"REACT_WHEN_SELECTED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='REBUILD_HOLE'/></GameDependency>",196,9,39,"REBUILD_HOLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='REFINERY'/></GameDependency>",196,9,131,"REFINERY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='REPAIR_PAD'/></GameDependency>",196,9,32,"REPAIR_PAD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='RESIST_EMP'/></GameDependency>",196,9,197,"RESIST_EMP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='REVEAL_TO_ALL'/></GameDependency>",196,9,87,"REVEAL_TO_ALL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ROCK'/></GameDependency>",196,9,94,"ROCK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='ROCK_VENDOR'/></GameDependency>",196,9,134,"ROCK_VENDOR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SALVAGER'/></GameDependency>",196,9,20,"SALVAGER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SCALEABLE_WALL'/></GameDependency>",196,9,191,"SCALEABLE_WALL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SCARY'/></GameDependency>",196,9,142,"SCARY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SCORE'/></GameDependency>",196,9,40,"SCORE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SCORE_CREATE'/></GameDependency>",196,9,41,"SCORE_CREATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SCORE_DESTROY'/></GameDependency>",196,9,42,"SCORE_DESTROY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SELECTABLE'/></GameDependency>",196,9,1,"SELECTABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SHADOW_ULTRA_HIGH_ONLY'/></GameDependency>",196,9,30,"SHADOW_ULTRA_HIGH_ONLY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SHIP'/></GameDependency>",196,9,181,"SHIP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SHOW_BEHIND_OCCLUDERS'/></GameDependency>",196,9,212,"SHOW_BEHIND_OCCLUDERS");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SHRUBBERY'/></GameDependency>",196,9,8,"SHRUBBERY");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SIEGE_TOWER'/></GameDependency>",196,9,92,"SIEGE_TOWER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SIEGE_WEAPON'/></GameDependency>",196,9,25,"SIEGE_WEAPON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SIMPLE_OBJECT_PROP'/></GameDependency>",196,9,180,"SIMPLE_OBJECT_PROP");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIP_DEFECT_IF_UNCOMBINED'/></GameDependency>",196,9,254,"SKIP_DEFECT_IF_UNCOMBINED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIP_IDLE_WHEN_CAPTURED'/></GameDependency>",196,9,255,"SKIP_IDLE_WHEN_CAPTURED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_ATTEMPT_TO_DODGE'/></GameDependency>",196,9,238,"SKIRMISH_AI_ATTEMPT_TO_DODGE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME'/></GameDependency>",196,9,80,"SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_DONT_CONSIDER_THREAT'/></GameDependency>",196,9,235,"SKIRMISH_AI_DONT_CONSIDER_THREAT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_DONT_GARRISON'/></GameDependency>",196,9,236,"SKIRMISH_AI_DONT_GARRISON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_DONT_MULTI_TARGET'/></GameDependency>",196,9,150,"SKIRMISH_AI_DONT_MULTI_TARGET");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKIRMISH_AI_DONT_REPAIR'/></GameDependency>",196,9,237,"SKIRMISH_AI_DONT_REPAIR");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SKYBOX'/></GameDependency>",196,9,192,"SKYBOX");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SLAVE_OWNER'/></GameDependency>",196,9,259,"SLAVE_OWNER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SMALL_MISSILE'/></GameDependency>",196,9,53,"SMALL_MISSILE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SNIPER'/></GameDependency>",196,9,252,"SNIPER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SPELL_BOOK'/></GameDependency>",196,9,121,"SPELL_BOOK");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='STEALTH_GARRISON'/></GameDependency>",196,9,34,"STEALTH_GARRISON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='STICK_TO_TERRAIN_SLOPE'/></GameDependency>",196,9,6,"STICK_TO_TERRAIN_SLOPE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='STRUCTURE'/></GameDependency>",196,9,9,"STRUCTURE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUBMARINE'/></GameDependency>",196,9,177,"SUBMARINE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUMMONED'/></GameDependency>",196,9,169,"SUMMONED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUPER_WEAPON'/></GameDependency>",196,9,233,"SUPER_WEAPON");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUPPLY_GATHERING_CENTER'/></GameDependency>",196,9,35,"SUPPLY_GATHERING_CENTER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUPPLY_SOURCE'/></GameDependency>",196,9,86,"SUPPLY_SOURCE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SUPPLY_SOURCE_ON_PREVIEW'/></GameDependency>",196,9,77,"SUPPLY_SOURCE_ON_PREVIEW");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='SURPRISE_ATTACKER'/></GameDependency>",196,9,216,"SURPRISE_ATTACKER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='T1_UNIT'/></GameDependency>",196,9,277,"T1_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='T2_UNIT'/></GameDependency>",196,9,278,"T2_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='T3_UNIT'/></GameDependency>",196,9,279,"T3_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TACTICAL_MARKER'/></GameDependency>",196,9,113,"TACTICAL_MARKER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TARGETABLE'/></GameDependency>",196,9,2,"TARGETABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TERRAIN_LIKE_GROUND_OBJECT'/></GameDependency>",196,9,283,"TERRAIN_LIKE_GROUND_OBJECT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='THREAT_FINDER'/></GameDependency>",196,9,185,"THREAT_FINDER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='THROWN_OBJECT'/></GameDependency>",196,9,95,"THROWN_OBJECT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TIBERIUM'/></GameDependency>",196,9,226,"TIBERIUM");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TIBERIUM_BASED'/></GameDependency>",196,9,225,"TIBERIUM_BASED");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TIBERIUM_FIELD'/></GameDependency>",196,9,110,"TIBERIUM_FIELD");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TIME_BOMB'/></GameDependency>",196,9,93,"TIME_BOMB");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TRANSFORMER'/></GameDependency>",196,9,141,"TRANSFORMER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='TRANSPORT'/></GameDependency>",196,9,22,"TRANSPORT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='UNATTACKABLE'/></GameDependency>",196,9,55,"UNATTACKABLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='UNIQUE_UNIT'/></GameDependency>",196,9,215,"UNIQUE_UNIT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='UNPACKS_INTO_BUILDING'/></GameDependency>",196,9,158,"UNPACKS_INTO_BUILDING");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='VALID_TARGET_FOR_C4'/></GameDependency>",196,9,214,"VALID_TARGET_FOR_C4");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='VEHICLE'/></GameDependency>",196,9,204,"VEHICLE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='VITAL_FOR_BASE_SURVIVAL'/></GameDependency>",196,9,167,"VITAL_FOR_BASE_SURVIVAL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALK_ON_TOP_OF_WALL'/></GameDependency>",196,9,60,"WALK_ON_TOP_OF_WALL");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALL_GATE'/></GameDependency>",196,9,193,"WALL_GATE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALL_HUB'/></GameDependency>",196,9,153,"WALL_HUB");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALL_PIECE'/></GameDependency>",196,9,152,"WALL_PIECE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALL_SEGMENT'/></GameDependency>",196,9,179,"WALL_SEGMENT");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WALL_UPGRADE'/></GameDependency>",196,9,148,"WALL_UPGRADE");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WB_DISPLAY_SCRIPT_NAME'/></GameDependency>",196,9,173,"WB_DISPLAY_SCRIPT_NAME");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WEAPON_SALVAGER'/></GameDependency>",196,9,21,"WEAPON_SALVAGER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WORKING_PASSENGER'/></GameDependency>",196,9,100,"WORKING_PASSENGER");
        BitCase(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Include='WORLDBUILDER_SNAP_TO_GRID'/></GameDependency>",196,9,156,"WORLDBUILDER_SNAP_TO_GRID");
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Rule='ALL'/></GameDependency>","filter Rule ALL");Require(Word(b.InstanceData,184,p!=TargetPlatform.Win32)==unchecked((uint)1),"independent filter Rule");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Rule='ANY'/></GameDependency>","filter Rule ANY");Require(Word(b.InstanceData,184,p!=TargetPlatform.Win32)==unchecked((uint)2),"independent filter Rule");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Rule='NONE'/></GameDependency>","filter Rule NONE");Require(Word(b.InstanceData,184,p!=TargetPlatform.Win32)==unchecked((uint)3),"independent filter Rule");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Rule='UNPARSED'/></GameDependency>","filter Rule UNPARSED");Require(Word(b.InstanceData,184,p!=TargetPlatform.Win32)==unchecked((uint)0),"independent filter Rule");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Alignment='EVIL'/></GameDependency>","filter Alignment EVIL");Require(Word(b.InstanceData,192,p!=TargetPlatform.Win32)==unchecked((uint)1),"independent filter Alignment");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Alignment='GOOD'/></GameDependency>","filter Alignment GOOD");Require(Word(b.InstanceData,192,p!=TargetPlatform.Win32)==unchecked((uint)2),"independent filter Alignment");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Alignment='NONE'/></GameDependency>","filter Alignment NONE");Require(Word(b.InstanceData,192,p!=TargetPlatform.Win32)==unchecked((uint)0),"independent filter Alignment");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Relationship='ALLIES'/></GameDependency>","filter Relationship ALLIES");Require(Word(b.InstanceData,188,p!=TargetPlatform.Win32)==unchecked((uint)1),"independent filter Relationship");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Relationship='ENEMIES'/></GameDependency>","filter Relationship ENEMIES");Require(Word(b.InstanceData,188,p!=TargetPlatform.Win32)==unchecked((uint)2),"independent filter Relationship");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Relationship='NEUTRAL'/></GameDependency>","filter Relationship NEUTRAL");Require(Word(b.InstanceData,188,p!=TargetPlatform.Win32)==unchecked((uint)4),"independent filter Relationship");layouts++; }
        { var b=Generic(plugin,p,"UpgradeTemplate","","<GameDependency><ObjectFilter Relationship='SAME_PLAYER'/></GameDependency>","filter Relationship SAME_PLAYER");Require(Word(b.InstanceData,188,p!=TargetPlatform.Win32)==unchecked((uint)8),"independent filter Relationship");layouts++; }
    }


    static void Prefixes(IAssetBuilderPlugin plugin,TargetPlatform p) {
        foreach(string type in new[]{"LogicCommandSet","AttributeModifier","UpgradeTemplate"}) {
            string attrs=type=="AttributeModifier"?"Duration='2s'":type=="UpgradeTemplate"?"BuildTime='2s'":"";
            string children=type=="LogicCommandSet"?"<Cmd>Command_A</Cmd>":type=="AttributeModifier"?"<Modifier Value='125'/>":"<GameDependency/>";
            var canonical=new XmlDocument();canonical.LoadXml("<"+type+" xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</"+type+">");
            var ns=new XmlNamespaceManager(canonical.NameTable);ns.AddNamespace("ea",Ns);
            var expected=plugin.ProcessInstance(Declaration(canonical,ns));
            string prefixedChildren=children.Replace("<Cmd","<q:Cmd").Replace("</Cmd","</q:Cmd").Replace("<Modifier","<q:Modifier").Replace("<GameDependency","<q:GameDependency");
            var prefixed=new XmlDocument();prefixed.LoadXml("<q:"+type+" xmlns:q='"+Ns+"' id='Oracle' "+attrs+">"+prefixedChildren+"</q:"+type+">");
            var pns=new XmlNamespaceManager(prefixed.NameTable);pns.AddNamespace("ea",Ns);
            AssetBuffer actual=type=="LogicCommandSet"?LogicCommandSetCompiler.CompileXml(prefixed.DocumentElement,pns,p!=TargetPlatform.Win32):
                type=="AttributeModifier"?AttributeModifierCompiler.CompileXml(prefixed.DocumentElement,pns,p!=TargetPlatform.Win32):
                UpgradeTemplateCompiler.CompileXml(prefixed.DocumentElement,pns,p!=TargetPlatform.Win32);
            Equal(type+"/prefix/instance",expected.InstanceData,actual.InstanceData);
            Equal(type+"/prefix/relocations",expected.RelocationData,actual.RelocationData);
            Equal(type+"/prefix/imports",expected.ImportsData,actual.ImportsData);
            fixtures++;
            // XmlNode-derived prefixed handles are deliberately not routed as canonical types.
            var d=Declaration(prefixed,pns);
            try {
                if(type=="LogicCommandSet") LogicCommandSetCompiler.ProcessInstance(d,p);
                else Compile(d,p,type);
                throw new Exception(type+" accepted prefixed unknown TypeId");
            } catch(NotSupportedException) { rejections++; }
        }
    }

    static void BabMetadata(IAssetBuilderPlugin plugin,TargetPlatform p) {
        string[] types={"LogicCommandSet","AttributeModifier","UpgradeTemplate"};
        string[] bodies={
            "<Cmd>Command_A\\7</Cmd><Cmd>Command_B</Cmd>",
            "<Modifier Type='SPEED' Value='125'/><Modifier Type='ARMOR' Value='-25'/>",
            "<GameDependency><RequiredObject>Tank\\1</RequiredObject><ForbiddenUpgrade>Upg\\2</ForbiddenUpgrade><NeededUpgrade>Upg\\3</NeededUpgrade><ObjectFilter Rule='ANY' Include='INFANTRY' StatusBitFlags='STEALTHED'><IncludeThing>Thing\\4</IncludeThing><ExcludeThing>Thing\\5</ExcludeThing></ObjectFilter></GameDependency>"
        };
        for(int i=0;i<types.Length;++i)foreach(string value in new[]{"0","4294967295","metadata-not-a-number",""}) {
            var d=new XmlDocument();d.LoadXml("<"+types[i]+" xmlns='"+Ns+"' id='Oracle'>"+bodies[i]+"</"+types[i]+">");
            var ns=new XmlNamespaceManager(d.NameTable);ns.AddNamespace("ea",Ns);
            AssetBuffer baseline=plugin.ProcessInstance(Declaration(d,ns));
            foreach(XmlElement node in d.SelectNodes("//*"))node.SetAttribute("TypeId",value);
            var declaration=Declaration(d,ns);
            AssetBuffer original=plugin.ProcessInstance(declaration);
            AssetBuffer actual=types[i]=="LogicCommandSet"?LogicCommandSetCompiler.ProcessInstance(declaration,p):Compile(declaration,p,types[i]);
            string label=types[i]+"/BAB-TypeId-"+value;
            Equal(label+"/original-metadata-instance",baseline.InstanceData,original.InstanceData);
            Equal(label+"/original-metadata-relocations",baseline.RelocationData,original.RelocationData);
            Equal(label+"/original-metadata-imports",baseline.ImportsData,original.ImportsData);
            Equal(label+"/instance",original.InstanceData,actual.InstanceData);
            Equal(label+"/relocations",original.RelocationData,actual.RelocationData);
            Equal(label+"/imports",original.ImportsData,actual.ImportsData);++fixtures;
        }
        Reject(Xml("xmlns:q='urn:metadata' q:TypeId='1'",""),p);
        Reject(Xml("","<Cmd xmlns:q='urn:metadata' q:TypeId='1'>Command</Cmd>"),p);
        RejectGeneric("AttributeModifier","xmlns:q='urn:metadata' q:TypeId='1'","",p);
        RejectGeneric("AttributeModifier","","<Modifier xmlns:q='urn:metadata' q:TypeId='1'/>",p);
        RejectGeneric("UpgradeTemplate","TypeId='1' Bogus='1'","",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><ObjectFilter TypeId='1' Bogus='1'/></GameDependency>",p);
        RejectGeneric("UpgradeTemplate","","<GameDependency><ObjectFilter><IncludeThing xmlns:q='urn:metadata' q:TypeId='1'>Thing</IncludeThing></ObjectFilter></GameDependency>",p);
    }

    static int Main(string[] args) {
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            var asm=Assembly.LoadFrom(args[0]);
            var plugin=(IAssetBuilderPlugin)Activator.CreateInstance(asm.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            foreach(TargetPlatform p in new[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                plugin.Initialize(null,p);
                int f=fixtures,c=comparisons,r=rejections,l=layouts;
                Check(plugin,p,"empty",new string[0],new uint?[0]);
                Check(plugin,p,"plain",new[]{"Command_A","", "  ","A&amp;B"},new uint?[]{null,null,null,null});
                Check(plugin,p,"resources",new[]{"LogicCommand:A\\0","A\\1","B\\17","C\\-1","D\\+2","E\\ 9tail","F\\","G\\junk","H\\2147483648","I\\-2147483649"},new uint?[]{0,1,17,uint.MaxValue,2,9,0,0,2147483647,2147483648});
                Check(plugin,p,"duplicates",new[]{"A\\5","B\\5","C\\0","D\\5","NoSlash","Z\\-2"},new uint?[]{5,5,0,5,null,unchecked((uint)-2)});
                foreach(int count in new[]{1,2,3,27,28,29,31,64,129}) {
                    var commands=new string[count];var expected=new uint?[count];
                    for(int i=0;i<count;i++) { int value=(i*37)%131; commands[i]="Command_"+i+"\\"+value.ToString(CultureInfo.InvariantCulture); expected[i]=(uint)value; }
                    Check(plugin,p,"list-"+count,commands,expected);
                }
                Reject(Xml("Bogus='1'",""),p);
                Reject(Xml("Default='1'",""),p);
                Reject(Xml("","<Unknown/>"),p);
                Reject(Xml("","<Cmd Value='1'/>"),p);
                Reject(Xml("","<Cmd><Nested/></Cmd>"),p);
                Reject(Xml("","<Cmd xmlns='urn:other'>A</Cmd>"),p);
                Reject(Xml("","unexpected text"),p);
                Reject("<LogicCommandSet id='x'/>",p);
                Reject("<UpgradeTemplate xmlns='"+Ns+"' id='x'/>",p);
                var wrong=new XmlDocument();wrong.LoadXml("<ArmorTemplate xmlns='"+Ns+"' id='x'/>");
                var wns=new XmlNamespaceManager(wrong.NameTable);wns.AddNamespace("ea",Ns);
                try { LogicCommandSetCompiler.ProcessInstance(Declaration(wrong,wns),p); throw new Exception("wrong TypeId accepted"); }
                catch(NotSupportedException) { rejections++; }
                OtherTypes(plugin,p);
                Prefixes(plugin,p);
                BabMetadata(plugin,p);
                Console.WriteLine("SUMMARY platform={0} fixtures={1} threeArrayComparisons={2} rejected={3} independentLayouts={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            Console.WriteLine("TOTAL fixtures={0} threeArrayComparisons={1} rejected={2} independentLayouts={3}",fixtures,comparisons,rejections,layouts);
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
