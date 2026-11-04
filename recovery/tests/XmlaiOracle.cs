using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
class XmlaiOracle {
    const string Ns="uri:ea.com:eala:asset";
    static int fixtures,comparisons,rejections,layouts;
    static IAssetBuilderPlugin original;
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager namespaces)
    {
        // Supply the same Node and namespace manager that BAB supplies after
        // schema processing, without initializing unrelated build/cache services.
        var document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var declaration = new InstanceDeclaration(document);
        declaration.XmlNode = xml.DocumentElement;
        return declaration;
    }

    static void Equal(string label,byte[] a,byte[] b) {
        if(a==null||b==null||a.Length!=b.Length)throw new Exception(label+" lengths "+(a==null?-1:a.Length)+"/"+(b==null?-1:b.Length));
        for(int i=0;i<a.Length;++i)if(a[i]!=b[i])throw new Exception(label+" byte "+i+" expected "+a[i]+" actual "+b[i]);
        ++comparisons;
    }
    static uint Word(byte[] b,int at,bool big) {
        if(at<0||at+4>b.Length)throw new Exception("Word outside instance: "+at);
        return big?((uint)b[at]<<24)|((uint)b[at+1]<<16)|((uint)b[at+2]<<8)|b[at+3]:BitConverter.ToUInt32(b,at);
    }
    static void Assert(bool v,string label){if(!v)throw new Exception(label);++layouts;}
    static XmlDocument Xml(string text){XmlDocument x=new XmlDocument();x.LoadXml(text);return x;}
    static XmlNamespaceManager Manager(XmlDocument x){XmlNamespaceManager n=new XmlNamespaceManager(x.NameTable);n.AddNamespace("ea",Ns);return n;}
    static string Escape(string s){return s.Replace("&","&amp;").Replace("'","&apos;").Replace("<","&lt;");}
    static string Root(string type,string attrs,string children){return "<"+type+" xmlns='"+Ns+"' id='Oracle' "+attrs+">"+children+"</"+type+">";}
    static AssetBuffer Test(string label,string text,TargetPlatform platform) {
        XmlDocument x=Xml(text);XmlNamespaceManager n=Manager(x);InstanceDeclaration d=Declaration(x,n);
        AssetBuffer a=original.ProcessInstance(d);
        AssetBuffer b=XmlaiCompiler.ProcessInstance(d,platform);
        Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
        AssetBuffer direct=XmlaiCompiler.CompileXml(x.DocumentElement,n,platform!=TargetPlatform.Win32);
        Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
        ++fixtures;return b;
    }
    static void Reject(string label,string text) {
        XmlDocument x=Xml(text);bool rejected=false;
        try{ PluginSchemaBoundary.Validate(x); XmlaiCompiler.CompileXml(x.DocumentElement,Manager(x),false);}catch(NotSupportedException){rejected=true;}
        if(!rejected)throw new Exception("Expected rejection: "+label);++rejections;
    }
    static uint Hash(string text,bool lower) {
        if(lower)text=text.ToLowerInvariant();byte[] p=System.Text.Encoding.ASCII.GetBytes(text);uint h=(uint)p.Length;int i=0;
        unchecked {
            for(;i+4<=p.Length;i+=4){h+=(uint)(p[i]|p[i+1]<<8);h^=((uint)(p[i+2]|p[i+3]<<8)^(h<<5))<<11;h+=h>>11;}
            switch(p.Length-i){case 3:h+=(uint)(p[i]|p[i+1]<<8);h^=(((uint)(sbyte)p[i+2]<<2)^h)<<16;h+=h>>11;break;case 2:h+=(uint)(p[i]|p[i+1]<<8);h^=h<<11;h+=h>>17;break;case 1:h+=(uint)(sbyte)p[i];h^=h<<10;h+=h>>1;break;}
            h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
        }
    }
    class Field {
        public string Type,Name,Kind,Value;public int Offset,Word;public bool Pointer;public uint Expected;
        public Field(string t,string n,string k,int o,bool p,string v,uint e,int w){Type=t;Name=n;Kind=k;Offset=o;Pointer=p;Value=v;Expected=e;Word=w;}
    }
    static Field[] Fields=new Field[]{
        new Field("AIPersonalityDefinition","PersonalityType","String",4,false,"AbC",3u,0),
        new Field("AIPersonalityDefinition","PersonalityUIName","String",12,false,"AbC",3u,0),
        new Field("AIPersonalityDefinition","SkirmishPersonality","Bool",508,false,"true",1u,0),
        new Field("AIPersonalityDefinition","CoopPersonality","Bool",509,false,"true",1u,0),
        new Field("AIPersonalityDefinition","SecondsTillTargetsExpire","Float",20,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","DesiredExcessPowerBuffer","Signed",24,false,"2",2u,0),
        new Field("AIPersonalityDefinition","BaseCompactness","Float",28,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","DefaultThreatFindRadius","Float",32,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","UnitBuilderCostEffectivenessWeight","Float",36,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","UnitBuilderMoneyVersusTimePreference","Float",40,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","UnitBuilderOverallOffensivePreference","Percent",44,false,"50%",1056964608u,0),
        new Field("AIPersonalityDefinition","UnitBuilderOverallDefensivePreference","Percent",48,false,"50%",1056964608u,0),
        new Field("AIPersonalityDefinition","UnitBuilderEnemyTowerWeight","Percent",52,false,"50%",1056964608u,0),
        new Field("AIPersonalityDefinition","UnitBuilderEnemyNearbyDistance","Float",56,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","UnitBuilderEnemyNearbyMultiplier","Unsigned",60,false,"2",2u,0),
        new Field("AIPersonalityDefinition","BuildTechEfficiencyThreshold","Float",64,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","BuildTechMinPowerAdvantage","Float",68,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","BuildExtraFactoryMinTime","Time",72,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","ResourceNodeSearchRadius","Float",76,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","ResourceNodeValueRemainingForExpansion","Signed",80,false,"2",2u,0),
        new Field("AIPersonalityDefinition","RefineryPlacementStartDistanceDelta","Float",84,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RefineryPlacementIncreaseTickAmount","Float",88,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RefineryPlacementNumberOfIncreaseTicksBeforeGivingUp","Signed",92,false,"2",2u,0),
        new Field("AIPersonalityDefinition","ReactiveDefenseRadius","Float",96,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RepairBuildingsAtDifficulty","Flags",100,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","UsePlayerPowersAtDifficulty","Flags",104,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","EmergencyManagerHandleNoPowerAtDifficulty","Flags",108,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","EmergencyManagerHandleNoIncomeAtDifficulty","Flags",112,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","EmergencyManagerHandleNoConyardAtDifficulty","Flags",116,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","EmergencyManagerHandleUnderAttackAtDifficulty","Flags",120,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","EmergencyManagerHandleConyardInDangerAtDifficulty","Flags",124,false,"BRUTAL",8u,0),
        new Field("AIPersonalityDefinition","ConyardInDangerEmergencyUnderAttackTime","Time",128,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","ConyardInDangerEmergencyHealthPercent","Percent",132,false,"50%",1056964608u,0),
        new Field("AIPersonalityDefinition","ConyardInDangerEmergencyEngineerDistance","Float",136,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","EnemySortingBrutalDifficulty","Enum",140,false,"Flat",2u,0),
        new Field("AIPersonalityDefinition","EnemySortingHardDifficulty","Enum",144,false,"Flat",2u,0),
        new Field("AIPersonalityDefinition","EnemySortingMediumDifficulty","Enum",148,false,"Flat",2u,0),
        new Field("AIPersonalityDefinition","EnemySortingEasyDifficulty","Enum",152,false,"Flat",2u,0),
        new Field("AIPersonalityDefinition","EnemyResortingTime","Time",156,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","CleanupRefineries","Bool",510,false,"true",1u,0),
        new Field("AIPersonalityDefinition","CanBuildInAlliedBuildRadii","Bool",511,false,"true",1u,0),
        new Field("AIPersonalityDefinition","StructureSaveChanceEasy","Float",160,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","StructureSaveChanceMedium","Float",164,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","StructureSaveChanceHard","Float",168,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","BaseBuilderKeepUnderAttackFlagOnTime","Time",172,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","BaseBuilderUpdateUnderAttackTime","Time",176,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","BaseBuilderUpdateEnemiesInBaseTime","Time",180,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","MinHarvesters","Unsigned",184,false,"2",2u,0),
        new Field("AIPersonalityDefinition","MaxStrategicTargets","Signed",188,false,"2",2u,0),
        new Field("AIPersonalityDefinition","AssaultBeaconRadius","Float",192,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","HoldObjectBeaconRadius","Float",196,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","HoldPositionBeaconRadius","Float",200,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RedAlertPowerAdvantage","Float",204,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RedAlertIncome","Float",208,false,"2",1073741824u,0),
        new Field("AIPersonalityDefinition","RedAlertProductionBuildings","Signed",212,false,"2",2u,0),
        new Field("AIPersonalityDefinition","RedAlertAttackTime","Time",216,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","MaxWallDestroyTimeToCountAsGoThroughWalls","Time",220,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","CommanderPortrait","Hash",224,false,"AbC",0u,0),
        new Field("AIPersonalityDefinition","SkirmishTimeTillGreeting","Time",228,false,"250ms",1048576000u,0),
        new Field("AIPersonalityDefinition","CoopMonitorUpdateTime","Time",232,false,"250ms",1048576000u,0),
        new Field("AIStrategicStateDefinition","MaxTargets","Unsigned",12,false,"2",2u,0),
        new Field("AIStrategicStateDefinition","MaxRunningCopies","Unsigned",16,true,"2",2u,0),
        new Field("AIStrategicStateDefinition","MaxInactiveCopies","Unsigned",20,true,"2",2u,0),
        new Field("AIStrategicStateDefinition","PathfindingCheckType","Enum",24,true,"None",2u,0),
        new Field("AIStrategicStateDefinition","CannotAttackTargetReaction","Enum",28,true,"Disband",1u,0),
        new Field("AIStrategicStateDefinition","TeamGatherBeginFeedbackKey","CaseHash",32,true,"AbC",0u,0),
        new Field("AIStrategicStateDefinition","ExecutionBeginFeedbackKey","CaseHash",36,true,"AbC",0u,0),
        new Field("AIStrategicStateDefinition","ExecutionCanceledFeedbackKey","CaseHash",40,true,"AbC",0u,0),
        new Field("AIStrategicStateDefinition","ExecutionCompleteFeedbackKey","CaseHash",44,true,"AbC",0u,0),
        new Field("AIMicroManagerData","UpdateTime","Time",4,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","MaxTeamIdleTime","Time",8,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","MaxTeamIdleDistanceChange","Float",12,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","MaxUnitIdleTime","Time",16,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","MaxUnitIdleDistanceChange","Float",20,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","EngageHostileTargets","Bool",160,false,"true",1u,0),
        new Field("AIMicroManagerData","EngagePassiveTargets","Bool",161,false,"true",1u,0),
        new Field("AIMicroManagerData","EngageTacticalTarget","Bool",162,false,"true",1u,0),
        new Field("AIMicroManagerData","PassiveTargetSearchDistance","Float",24,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","EnemySearchDistanceVisionMultiplier","Float",28,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","MinTimeToKeepTempVisionRangeBonus","Time",32,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","TimeToKeepTargetMin","Time",36,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","TimeToKeepTargetMax","Time",40,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","RandomTargetScoreModifier","Float",44,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","CleanupPowerAdvantage","Float",48,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","UseGatherState","Bool",163,false,"true",1u,0),
        new Field("AIMicroManagerData","GatherTempVisionRangeBonus","Float",52,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","RetreatPowerAdvantage","Float",56,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","RetreatMinDistanceFromBase","Float",60,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","RetreatMinDistanceToFallback","Float",64,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","RetreatSpeedBuffer","Float",68,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","RetreatTempVisionRangeBonus","Float",72,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","MaxDistanceFromTeamToIncludeInArmy","Float",76,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","AlphaStrikeTargetHealthPct","Float",80,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","AlphaStrikeTargetTimeToKill","Float",84,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","AlphaStrikeEngineerDistanceFromStructure","Float",88,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","SiegeWeaponBuffer","Float",92,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","CanUseSiegeState","Bool",164,false,"true",1u,0),
        new Field("AIMicroManagerData","Crush","Bool",165,false,"true",1u,0),
        new Field("AIMicroManagerData","CrushPreference","Float",96,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","EnterGarrisons","Bool",166,false,"true",1u,0),
        new Field("AIMicroManagerData","EnterGarrisonableStructureDistance","Float",100,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","ClearGarrisons","Bool",167,false,"true",1u,0),
        new Field("AIMicroManagerData","PullbackInjured","Bool",168,false,"true",1u,0),
        new Field("AIMicroManagerData","PullbackInjuredHealthPct","Float",104,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","ReverseMove","Bool",169,false,"true",1u,0),
        new Field("AIMicroManagerData","DodgeThreats","Bool",170,false,"true",1u,0),
        new Field("AIMicroManagerData","DodgeThreatsReactionTimeMin","Time",108,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","DodgeThreatsReactionTimeMax","Time",112,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","UseSpecialPowers","Bool",171,false,"true",1u,0),
        new Field("AIMicroManagerData","SpecialPowerDelayMinimum","Time",116,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","SpecialPowerDelayMaximum","Time",120,false,"250ms",1048576000u,0),
        new Field("AIMicroManagerData","EnterBaseDefenses","Bool",172,false,"true",1u,0),
        new Field("AIMicroManagerData","EnterBaseDefensesWhenIdle","Bool",173,false,"true",1u,0),
        new Field("AIMicroManagerData","IsHarvesterManager","Bool",174,false,"true",1u,0),
        new Field("AIMicroManagerData","MinDistanceFromBaseToIgnoreTargets","Float",124,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","MaxSecondaryDamageFraction","Float",128,false,"2",1073741824u,0),
        new Field("AIMicroManagerData","CanCollectCratesWhenIdle","Bool",175,false,"true",1u,0),
        new Field("AIMicroManagerData","FractionBlockedToDestroyWalls","Float",132,false,"2",1073741824u,0),
        new Field("AITargetingHeuristic","HeuristicType","Enum",4,false,"Beacon",12u,0),
        new Field("AITargetingHeuristic","SortType","Enum",8,false,"ClosestToEnemy",5u,0),
        new Field("AITargetingHeuristic","VitalKindOf","Flags",12,false,"ACTIVATE_AFTER_UNPACK",2097152u,4),
        new Field("AITargetingHeuristic","ForbiddenKindOf","Flags",48,false,"ACTIVATE_AFTER_UNPACK",2097152u,4),
        new Field("AITargetingHeuristic","VitalObjectStatus","Status",84,false,"DESTROYED",0u,0),
        new Field("AITargetingHeuristic","ForbiddenObjectStatus","Status",112,false,"DESTROYED",0u,0),
        new Field("AITargetingHeuristic","Destroyed","Bool",192,false,"true",1u,0),
        new Field("AITargetingHeuristic","HighestPriorityEnemyPlayersOnly","Bool",193,false,"true",1u,0),
        new Field("AITargetingHeuristic","ThreatFinderRange","Float",140,true,"2",1073741824u,0),
        new Field("AITargetingHeuristic","SearchRange","Float",144,false,"2",1073741824u,0),
        new Field("AITargetingHeuristic","Beacon","Enum",148,true,"Assault",0u,0),
        new Field("AITargetingHeuristic","Name","String",152,true,"AbC",3u,0),
        new Field("AITargetingHeuristic","Location","Flags",156,false,"Land",2u,0),
        new Field("AITargetingHeuristic","MaxDistanceToShore","Float",160,false,"2",1073741824u,0),
        new Field("AITargetingHeuristic","Relationship","Flags",164,true,"ALLIES",1u,0),
        new Field("AITargetingHeuristic","UnderAttack","Bool",194,false,"true",1u,0),
        new Field("AITargetingHeuristic","RequireUniqueTarget","Bool",195,false,"true",1u,0),
        new Field("AITargetingHeuristic","MinDistanceFromEnemy","Float",168,false,"2",1073741824u,0),
        new Field("AITargetingHeuristic","MinDistanceFromConyardEntrance","Float",172,false,"2",1073741824u,0),
        new Field("AITargetingHeuristic","IgnoreMinDistanceFromConyardEntranceForTime","Time",176,false,"250ms",1048576000u,0),
    };
    static string[] Types=new string[]{"AIPersonalityDefinition","AIStrategicStateDefinition","AIMicroManagerData","AIBudgetStateDefinition","AIStateDefinition","AITargetingHeuristic"};
    static int[] Sizes=new int[]{512,64,176,20,12,196};
    static uint[] HeuristicIds=new uint[]{2269801993u,75667102u,278936256u,2225927560u,263056479u,2665825158u,3152351395u,3416369467u,951319894u,1268468166u,121779834u,3826170916u,1740211623u,2156949943u,4037692679u,2915089289u,2128923060u,1278812460u,1170787490u,3371681655u,2386854964u,1390340504u,1375432082u,2174347641u,3195463252u,4054901427u,4090667391u,2115602688u,1801850452u};
    static string[] HeuristicNames=new string[]{"AIStateResourceNodeShareHeuristic","AIStateMoneyHeuristic","AIStateTimerHeuristic","AIStateOpenNodeHeuristic","AIStateEnergyHeuristic","AIStateCanBuildBuildingsOnSurfaceHeuristic","AIStateBridgeExistsHeuristic","AIStateTechBuildingShareHeuristic","AIStateAllNearbyNodesFullHeuristic","AIStateProductionAdvantageHeuristic","AIStateLinearCombinationHeuristic","AIStateTimeSinceLastExpansionHeuristic","AIStateEnemyNearbyHeuristic","AIStateScriptedFlagHeuristic","AIStateOpponentFundsHeuristic","AIStatePowerAdvantageHeuristic","AIStateOpeningMoveHeuristic","AIStateGarrisonHeuristic","AIStateIsBuildingHeuristic","AIRedAlertHeuristic","AIStateHarvesterCapHeuristic","AIStateIncomeAdvantageHeuristic","AIStateConstantHeuristic","AIStateIntervalHeuristic","AIStateObjectOfTypeExistsHeuristic","AIStatePathToTargetHeuristic","AIStrategicDirectiveHeuristic","AIAlliedBeaconExistsHeuristic","AIStateStructureOrHarvesterUnderAttackHeuristic"};
    static int[] HeuristicSizes=new int[]{12,16,16,12,16,16,12,12,4,4,12,12,20,12,4,84,8,16,44,8,8,12,8,16,124,20,12,12,12};
    static void MetadataCases(TargetPlatform platform) {
        uint[] ids={3604279694u,607123156u,2423780215u,2893598307u,3239935619u,926814458u};
        for(int i=0;i<Types.Length;++i) {
            Test("BAB-TypeId/root/"+Types[i],Root(Types[i],"TypeId='"+ids[i]+"'",""),platform);
            Reject("BAB-TypeId/qualified/"+Types[i],Root(Types[i],"xmlns:q='urn:other' q:TypeId='"+ids[i]+"'",""));
        }
        // Root dispatch follows the canonical root name/Handle; the original root
        // Marshal ignores the redundant XML TypeId metadata.
        Test("BAB-TypeId/root-ignored",Root("AIStateDefinition","TypeId='99'",""),platform);
        Test("BAB-TypeId/reference-leaf",Root("AIPersonalityDefinition","TypeId='3604279694'","<Side TypeId='1'>First\\7</Side>"),platform);
#if XMLAI_PERSONALITY_EXTENSION
        Test("BAB-TypeId/personality-structures",Root("AIPersonalityDefinition","TypeId='3604279694'",
            "<OpeningMove TypeId='1' Name='Move'><Heuristic TypeId='2'><H TypeId='1375432082' Weight='2'/></Heuristic></OpeningMove>"+
            "<CountAsStructureForBuildPurposesFilter TypeId='3' Include='STRUCTURE'><IncludeThing TypeId='4'>Tank</IncludeThing></CountAsStructureForBuildPurposesFilter>"),platform);
#endif
        Test("BAB-TypeId/tactic-team-filter",Root("AIStrategicStateDefinition","TypeId='607123156'",
            "<Tactic TypeId='1' Tactic='SimpleAttack'><TeamTemplate TypeId='2' MaxUnits='7'><CreateUnits TypeId='3' UnitName='Tank' MinUnits='2'/>"+
            "<ObjectFilter TypeId='4' Include='INFANTRY'><IncludeThing TypeId='5'>Tank</IncludeThing><ExcludeThing TypeId='6'>Air</ExcludeThing></ObjectFilter></TeamTemplate></Tactic>"),platform);
        Test("BAB-TypeId/micro-structures-filter",Root("AIMicroManagerData","TypeId='2423780215'",
            "<TargetBias TypeId='1' UnitName='Tank'/><SpecificSpecialPowerDelay TypeId='2' MinimumDelay='250ms'/><RetreatBaseCriteria TypeId='3' RequiredBaseTypes='MAIN_BASE'/>"+
            "<IgnoreTargets TypeId='4' Include='INFANTRY'><IncludeThing TypeId='5'>Tank</IncludeThing></IgnoreTargets>"),platform);
        Test("BAB-TypeId/budget-wrapper",Root("AIBudgetStateDefinition","TypeId='2893598307'",
            "<Budget TypeId='1'><AccountShare TypeId='2' Account='PRODUCTION' Percent='50'/></Budget><Heuristic TypeId='3'><H TypeId='1375432082' Weight='2'/></Heuristic>"),platform);
        Test("BAB-TypeId/heuristic-dispatch",Root("AIStateDefinition","TypeId='3239935619'",
            "<Heuristic TypeId='1'><H TypeId='121779834'><WeightedHeuristic TypeId='2' Weight='0.75'><Heuristic TypeId='3'><H TypeId='1375432082' Weight='2'/></Heuristic></WeightedHeuristic></H>"+
            "<H TypeId='1740211623'><EnemyFilter TypeId='4' Include='INFANTRY'><IncludeThing TypeId='5'>Tank</IncludeThing></EnemyFilter></H></Heuristic>"),platform);
        Test("BAB-TypeId/target-structures",Root("AITargetingHeuristic","TypeId='926814458'",
            "<BaseSelection TypeId='1'/><BuildingPlacementDistance TypeId='2' Distance='3'/><BuildingPlacementDistance TypeId='3' Distance='1'/>"),platform);
        Reject("BAB-TypeId/leaf-other-attribute",Root("AIPersonalityDefinition","","<Side TypeId='1' Unknown='1'>Tank</Side>"));
        Reject("BAB-TypeId/filter-other-attribute",Root("AIMicroManagerData","","<IgnoreTargets TypeId='1' Unknown='1'/>"));
        Reject("BAB-TypeId/wrapper-other-attribute",Root("AIBudgetStateDefinition","","<Budget TypeId='1' Unknown='1'/>"));
        Reject("BAB-TypeId/heuristic-still-unknown",Root("AIStateDefinition","TypeId='3239935619'","<Heuristic TypeId='1'><H TypeId='999'/></Heuristic>"));
        Reject("BAB-TypeId/heuristic-still-required",Root("AIStateDefinition","TypeId='3239935619'","<Heuristic TypeId='1'><H/></Heuristic>"));
    }
    static void Run(TargetPlatform platform) {
        MetadataCases(platform);
        bool big=platform!=TargetPlatform.Win32;
        foreach(string type in Types){
            int ix=Array.IndexOf(Types,type);AssetBuffer b=Test(type+"/defaults",Root(type,"",""),platform);
            Assert(b.InstanceData.Length==Sizes[ix],type+" default root size without Tokenizer header");
            Assert(Word(b.InstanceData,0,big)==0,type+" reserved");
            Assert(b.RelocationData.Length==0&&b.ImportsData.Length==0,type+" default side arrays");
            Reject(type+"/unknown-attribute",Root(type,"Unknown='1'",""));
            Reject(type+"/unknown-child",Root(type,"","<Unknown/>"));
            Reject(type+"/qualified-attribute",Root(type,"xmlns:q='urn:other' q:UpdateTime='1'",""));
            Reject(type+"/foreign-child",Root(type,"","<q:Unknown xmlns:q='urn:other'/>"));
        }
        foreach(Field f in Fields){
            AssetBuffer b=Test(f.Type+"/"+f.Name,Root(f.Type,f.Name+"='"+Escape(f.Value)+"'",""),platform);
            int at=f.Offset;if(f.Pointer){at=(int)Word(b.InstanceData,at,big);Assert(at>=Sizes[Array.IndexOf(Types,f.Type)]&&at<b.InstanceData.Length,f.Name+" optional pointer");}
            if(f.Kind=="Bool")Assert(b.InstanceData[at]==1,f.Name+" bool");
            else if(f.Kind=="String"){Assert(Word(b.InstanceData,at,big)==3,f.Name+" string length");int p=(int)Word(b.InstanceData,at+4,big);Assert(b.InstanceData[p]==65&&b.InstanceData[p+1]==98&&b.InstanceData[p+2]==67&&b.InstanceData[p+3]==0,f.Name+" string bytes");}
            else if(f.Kind=="Hash"||f.Kind=="CaseHash")Assert(Word(b.InstanceData,at,big)==Hash("AbC",f.Kind=="Hash"),f.Name+" hash case");
            else if(f.Kind!="Status")Assert(Word(b.InstanceData,at+f.Word*4,big)==f.Expected,f.Name+" independent scalar offset");
            string[] values=f.Kind=="Bool"?new string[]{"false","TRUE","True","yes","1","0","garbage",""}:
                (f.Kind=="Float"||f.Kind=="Time"||f.Kind=="Percent"||f.Kind=="Signed"||f.Kind=="Unsigned")?new string[]{"0","-0","-25.5","2d1","2D-1","1e-7","16777217","4294967296","0x10","garbage",""," 2 "}:
                (f.Kind=="Flags"||f.Kind=="Status")?new string[]{"","ALL","NONE","+ALL -ALL","garbage","ALL -garbage","garbage garbage"}:
                f.Kind=="Enum"?new string[]{"garbage",""," "+f.Value,f.Value.ToLowerInvariant()}:
                new string[]{"","a","ABC","Any\\17","a:b"};
            foreach(string value in values)Test(f.Type+"/"+f.Name+"/boundary="+value,Root(f.Type,f.Name+"='"+Escape(value)+"'",""),platform);
        }
        Test("tactic/AIStateTactic/id/MixedCase",Root("AIStrategicStateDefinition","","<Tactic id='MixedCase'/>"),platform);
        Test("tactic/AIStateTactic/id/",Root("AIStrategicStateDefinition","","<Tactic id=''/>"),platform);
        Test("tactic/AIStateTactic/id/ABC",Root("AIStrategicStateDefinition","","<Tactic id='ABC'/>"),platform);
        Test("tactic/AIStateTactic/Tactic/DefenseAvoidanceAttack",Root("AIStrategicStateDefinition","","<Tactic Tactic='DefenseAvoidanceAttack'/>"),platform);
        Test("tactic/AIStateTactic/Tactic/garbage",Root("AIStrategicStateDefinition","","<Tactic Tactic='garbage'/>"),platform);
        Test("tactic/AIStateTactic/Tactic/",Root("AIStrategicStateDefinition","","<Tactic Tactic=''/>"),platform);
        Test("tactic/AIStateTactic/Targetless/true",Root("AIStrategicStateDefinition","","<Tactic Targetless='true'/>"),platform);
        Test("tactic/AIStateTactic/Targetless/false",Root("AIStrategicStateDefinition","","<Tactic Targetless='false'/>"),platform);
        Test("tactic/AIStateTactic/Targetless/garbage",Root("AIStrategicStateDefinition","","<Tactic Targetless='garbage'/>"),platform);
        Test("tactic/AIStateTactic/EndBehavior/DISBAND",Root("AIStrategicStateDefinition","","<Tactic EndBehavior='DISBAND'/>"),platform);
        Test("tactic/AIStateTactic/EndBehavior/garbage",Root("AIStrategicStateDefinition","","<Tactic EndBehavior='garbage'/>"),platform);
        Test("tactic/AIStateTactic/EndBehavior/",Root("AIStrategicStateDefinition","","<Tactic EndBehavior=''/>"),platform);
        Test("tactic/AIStateTactic/DisbandAfterRetreatTeamSize/2",Root("AIStrategicStateDefinition","","<Tactic DisbandAfterRetreatTeamSize='2'/>"),platform);
        Test("tactic/AIStateTactic/DisbandAfterRetreatTeamSize/garbage",Root("AIStrategicStateDefinition","","<Tactic DisbandAfterRetreatTeamSize='garbage'/>"),platform);
        Test("tactic/AIStateTactic/DisbandAfterRetreatTeamSize/",Root("AIStrategicStateDefinition","","<Tactic DisbandAfterRetreatTeamSize=''/>"),platform);
        Test("tactic/AIStateTactic/EndIfStateTurnsOff/true",Root("AIStrategicStateDefinition","","<Tactic EndIfStateTurnsOff='true'/>"),platform);
        Test("tactic/AIStateTactic/EndIfStateTurnsOff/false",Root("AIStrategicStateDefinition","","<Tactic EndIfStateTurnsOff='false'/>"),platform);
        Test("tactic/AIStateTactic/EndIfStateTurnsOff/garbage",Root("AIStrategicStateDefinition","","<Tactic EndIfStateTurnsOff='garbage'/>"),platform);
        Test("tactic/AIStateTactic/UseAestheticsManager/true",Root("AIStrategicStateDefinition","","<Tactic UseAestheticsManager='true'/>"),platform);
        Test("tactic/AIStateTactic/UseAestheticsManager/false",Root("AIStrategicStateDefinition","","<Tactic UseAestheticsManager='false'/>"),platform);
        Test("tactic/AIStateTactic/UseAestheticsManager/garbage",Root("AIStrategicStateDefinition","","<Tactic UseAestheticsManager='garbage'/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerPatrolIdleTime/250ms",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerPatrolIdleTime='250ms'/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerPatrolIdleTime/2d1",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerPatrolIdleTime='2d1'/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerPatrolIdleTime/",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerPatrolIdleTime=''/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerMinDistanceFromHarvester/2",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerMinDistanceFromHarvester='2'/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerMinDistanceFromHarvester/garbage",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerMinDistanceFromHarvester='garbage'/>"),platform);
        Test("tactic/AIStateTactic/AestheticsManagerMinDistanceFromHarvester/",Root("AIStrategicStateDefinition","","<Tactic AestheticsManagerMinDistanceFromHarvester=''/>"),platform);
        Test("tactic/AIStateTactic/Difficulty/BRUTAL",Root("AIStrategicStateDefinition","","<Tactic Difficulty='BRUTAL'/>"),platform);
        Test("tactic/AIStateTactic/Difficulty/ALL",Root("AIStrategicStateDefinition","","<Tactic Difficulty='ALL'/>"),platform);
        Test("tactic/AIStateTactic/Difficulty/garbage",Root("AIStrategicStateDefinition","","<Tactic Difficulty='garbage'/>"),platform);
        Test("tactic/AIStateTactic/Distance/2",Root("AIStrategicStateDefinition","","<Tactic Distance='2'/>"),platform);
        Test("tactic/AIStateTactic/Distance/garbage",Root("AIStrategicStateDefinition","","<Tactic Distance='garbage'/>"),platform);
        Test("tactic/AIStateTactic/Distance/",Root("AIStrategicStateDefinition","","<Tactic Distance=''/>"),platform);
        Test("tactic/AIStateTactic/CombatLeashSlack/2",Root("AIStrategicStateDefinition","","<Tactic CombatLeashSlack='2'/>"),platform);
        Test("tactic/AIStateTactic/CombatLeashSlack/garbage",Root("AIStrategicStateDefinition","","<Tactic CombatLeashSlack='garbage'/>"),platform);
        Test("tactic/AIStateTactic/CombatLeashSlack/",Root("AIStrategicStateDefinition","","<Tactic CombatLeashSlack=''/>"),platform);
        Test("tactic/AIStateTactic/TargetDispositionToDisplay/Defensive",Root("AIStrategicStateDefinition","","<Tactic TargetDispositionToDisplay='Defensive'/>"),platform);
        Test("tactic/AIStateTactic/TargetDispositionToDisplay/garbage",Root("AIStrategicStateDefinition","","<Tactic TargetDispositionToDisplay='garbage'/>"),platform);
        Test("tactic/AIStateTactic/TargetDispositionToDisplay/",Root("AIStrategicStateDefinition","","<Tactic TargetDispositionToDisplay=''/>"),platform);
        Test("tactic/AIStateTactic/GatherWaypoint/MixedCase",Root("AIStrategicStateDefinition","","<Tactic GatherWaypoint='MixedCase'/>"),platform);
        Test("tactic/AIStateTactic/GatherWaypoint/",Root("AIStrategicStateDefinition","","<Tactic GatherWaypoint=''/>"),platform);
        Test("tactic/AIStateTactic/GatherWaypoint/ABC",Root("AIStrategicStateDefinition","","<Tactic GatherWaypoint='ABC'/>"),platform);
        Test("tactic/AIStateTactic/UseTransports/true",Root("AIStrategicStateDefinition","","<Tactic UseTransports='true'/>"),platform);
        Test("tactic/AIStateTactic/UseTransports/false",Root("AIStrategicStateDefinition","","<Tactic UseTransports='false'/>"),platform);
        Test("tactic/AIStateTactic/UseTransports/garbage",Root("AIStrategicStateDefinition","","<Tactic UseTransports='garbage'/>"),platform);
        Test("tactic/AIStateTactic/UpdateTime/250ms",Root("AIStrategicStateDefinition","","<Tactic UpdateTime='250ms'/>"),platform);
        Test("tactic/AIStateTactic/UpdateTime/2d1",Root("AIStrategicStateDefinition","","<Tactic UpdateTime='2d1'/>"),platform);
        Test("tactic/AIStateTactic/UpdateTime/",Root("AIStrategicStateDefinition","","<Tactic UpdateTime=''/>"),platform);
        Test("tactic/AITeamTemplate/MinUnits/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinUnits='2'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MinUnits/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinUnits='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MinUnits/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinUnits=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxUnits/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxUnits='2'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxUnits/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxUnits='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxUnits/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxUnits=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MinPowerAdvantage/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinPowerAdvantage='2'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MinPowerAdvantage/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinPowerAdvantage='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MinPowerAdvantage/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MinPowerAdvantage=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxTypesOfUnits/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxTypesOfUnits='2'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxTypesOfUnits/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxTypesOfUnits='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MaxTypesOfUnits/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MaxTypesOfUnits=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IncludeKindOf/ACTIVATE_AFTER_UNPACK",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IncludeKindOf='ACTIVATE_AFTER_UNPACK'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IncludeKindOf/ALL",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IncludeKindOf='ALL'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IncludeKindOf/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IncludeKindOf='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ExcludeKindOf/ACTIVATE_AFTER_UNPACK",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ExcludeKindOf='ACTIVATE_AFTER_UNPACK'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ExcludeKindOf/ALL",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ExcludeKindOf='ALL'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ExcludeKindOf/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ExcludeKindOf='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/Priority/COOP",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate Priority='COOP'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/Priority/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate Priority='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/Priority/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate Priority=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ReleaseType/ALWAYS_RELEASE",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ReleaseType='ALWAYS_RELEASE'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ReleaseType/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ReleaseType='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/ReleaseType/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate ReleaseType=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AlwaysRecruit/true",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AlwaysRecruit='true'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AlwaysRecruit/false",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AlwaysRecruit='false'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AlwaysRecruit/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AlwaysRecruit='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowRecruitUnitsInCombat/true",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowRecruitUnitsInCombat='true'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowRecruitUnitsInCombat/false",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowRecruitUnitsInCombat='false'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowRecruitUnitsInCombat/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowRecruitUnitsInCombat='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RequireCanAttackTarget/true",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RequireCanAttackTarget='true'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RequireCanAttackTarget/false",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RequireCanAttackTarget='false'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RequireCanAttackTarget/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RequireCanAttackTarget='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MicroManager/Any\\17",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MicroManager='Any\\17'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MicroManager/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MicroManager=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/MicroManager/Unresolved",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate MicroManager='Unresolved'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowedLocomotorTypes/AIR",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowedLocomotorTypes='AIR'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowedLocomotorTypes/ALL",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowedLocomotorTypes='ALL'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/AllowedLocomotorTypes/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate AllowedLocomotorTypes='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/SortByDistance/true",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate SortByDistance='true'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/SortByDistance/false",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate SortByDistance='false'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/SortByDistance/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate SortByDistance='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/DistanceSortPoint/HOME_BASE",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate DistanceSortPoint='HOME_BASE'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/DistanceSortPoint/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate DistanceSortPoint='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/DistanceSortPoint/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate DistanceSortPoint=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RegisteredObjectSet/AIRCRAFT",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RegisteredObjectSet='AIRCRAFT'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RegisteredObjectSet/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RegisteredObjectSet='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/RegisteredObjectSet/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate RegisteredObjectSet=''/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IsBaseDefenseTeam/true",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IsBaseDefenseTeam='true'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IsBaseDefenseTeam/false",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IsBaseDefenseTeam='false'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/IsBaseDefenseTeam/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate IsBaseDefenseTeam='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/BaseDefenseMaxDistance/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate BaseDefenseMaxDistance='2'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/BaseDefenseMaxDistance/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate BaseDefenseMaxDistance='garbage'/></Tactic>"),platform);
        Test("tactic/AITeamTemplate/BaseDefenseMaxDistance/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate BaseDefenseMaxDistance=''/></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MinUnits/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MinUnits='2'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MinUnits/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MinUnits='garbage'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MinUnits/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MinUnits=''/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MaxUnits/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MaxUnits='2'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MaxUnits/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MaxUnits='garbage'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/MaxUnits/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits MaxUnits=''/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/UnitName/MixedCase",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits UnitName='MixedCase'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/UnitName/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits UnitName=''/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/UnitName/ABC",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits UnitName='ABC'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/ExperienceLevel/2",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits ExperienceLevel='2'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/ExperienceLevel/garbage",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits ExperienceLevel='garbage'/></TeamTemplate></Tactic>"),platform);
        Test("tactic/CreateUnitInfo/ExperienceLevel/",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate><CreateUnits ExperienceLevel=''/></TeamTemplate></Tactic>"),platform);
        AssetBuffer tacticLayout=Test("tactic/default",Root("AIStrategicStateDefinition","","<Tactic/>"),platform);
        Assert(tacticLayout.InstanceData.Length==132&&Word(tacticLayout.InstanceData,56,big)==1&&Word(tacticLayout.InstanceData,60,big)==64,"Tactic 68-byte list independent ABI");
        AssetBuffer teamLayout=Test("tactic/team-default",Root("AIStrategicStateDefinition","","<Tactic><TeamTemplate/></Tactic>"),platform);
        Assert(teamLayout.InstanceData.Length==268&&Word(teamLayout.InstanceData,120,big)==1&&Word(teamLayout.InstanceData,124,big)==132&&Word(teamLayout.InstanceData,132,big)==1,"TeamTemplate 136-byte list independent ABI");
        Test("tactic/teams",Root("AIStrategicStateDefinition","","<Tactic id='MixedCase' Tactic='SimpleAttack' GatherWaypoint='WayPoint'><TargetBaseCriteria RequiredBaseTypes='MAIN_BASE'/><HomeBaseCriteria ExcludedBaseTypes='EXPANSION'/><TeamTemplate MaxUnits='7' MicroManager='Micro\\11'><CreateUnits UnitName='Tank' MinUnits='2' MaxUnits='4'/><CreateUnits UnitName='Air' ExperienceLevel='3'/><ObjectFilter Include='INFANTRY'><IncludeThing>Unit</IncludeThing></ObjectFilter></TeamTemplate><TeamTemplate Priority='garbage'/></Tactic>"),platform);
        AssetBuffer sideEmpty=Test("personality/side-empty",Root("AIPersonalityDefinition","","<Side/>"),platform);
        Assert(Word(sideEmpty.InstanceData,236,big)==1&&Word(sideEmpty.InstanceData,240,big)==512&&sideEmpty.InstanceData.Length==516,"Side independent list layout");
        AssetBuffer sideReferences=Test("personality/side-references",Root("AIPersonalityDefinition","","<Side>First\\\\7</Side><Side>Second\\\\11</Side><Side>Unresolved</Side>"),platform);
        Assert(Word(sideReferences.InstanceData,236,big)==3&&Word(sideReferences.InstanceData,240,big)==512&&sideReferences.ImportsData.Length!=0,"Side reference imports preserved");
        Reject("side attributes",Root("AIPersonalityDefinition","","<Side Unknown='1'/>"));
        Reject("side nested children",Root("AIPersonalityDefinition","","<Side><Unknown/></Side>"));
        Test("micro/children",Root("AIMicroManagerData","","<TargetBias UnitName='MixedCase' HealthMultiplier='2'/><TargetBias UnitName='Other' DamageOverride='3'/><SpecificSpecialPowerDelay PowerType='garbage' MinimumDelay='250ms'/><RetreatBaseCriteria RequiredBaseTypes='ALL -EXPANSION'/><IgnoreTargets Include='INFANTRY' StatusBitFlags='DESTROYED'><IncludeThing>Tank</IncludeThing><ExcludeThing>Air</ExcludeThing></IgnoreTargets>"),platform);
        Test("target/sorted",Root("AITargetingHeuristic","Name='N' Beacon='garbage' Relationship='ALL'","<BaseSelection/><BuildingPlacementDistance Distance='3'/><BuildingPlacementDistance Distance='1'/><BuildingPlacementDistance Distance='2'/>"),platform);
        foreach(int count in new int[]{0,1,2,27,28,29,56,57,64,129,257}) {
            foreach(string order in new string[]{"ascending","descending","ties","signed-zero"}) {
                string items="";
                for(int j=0;j<count;++j){
                    string distance=order=="ascending"?j.ToString(CultureInfo.InvariantCulture):order=="descending"?(count-j).ToString(CultureInfo.InvariantCulture):
                        order=="ties"?(j%3).ToString(CultureInfo.InvariantCulture):(j%2==0?"-0":"0");
                    items+="<BuildingPlacementDistance Distance='"+distance+"'/>";
                }
                Test("target/sort/"+order+"/"+count,Root("AITargetingHeuristic","",items),platform);
            }
        }
        AssetBuffer imports=Test("strategic/import",Root("AIStrategicStateDefinition","TeamGatherBeginFeedbackKey='ABC'","<TargetHeuristic TargetHeuristic='Anything\\17' Priority='-3'/>"),platform);
        Assert(imports.ImportsData.Length!=0,"strong reference preserves imports");
        Test("budget/entries",Root("AIBudgetStateDefinition","","<Budget><AccountShare Account='INVESTMENT' Percent='25%'/><AccountShare Account='garbage' Percent='75'/></Budget>"),platform);
        for(int i=0;i<HeuristicIds.Length;++i){
            string body="<Heuristic><"+HeuristicNames[i]+" TypeId='"+HeuristicIds[i]+"'/></Heuristic>";
            AssetBuffer b=Test("state/"+HeuristicNames[i],Root("AIStateDefinition","",body),platform);
            Assert(Word(b.InstanceData,4,big)==1&&Word(b.InstanceData,8,big)==12&&Word(b.InstanceData,12,big)==16,"heuristic list layout");
            Assert(Word(b.InstanceData,16,big)==HeuristicIds[i],"heuristic discriminator");
            Assert(b.InstanceData.Length>=16+HeuristicSizes[i],"heuristic ABI size");
            Test("budget/"+HeuristicNames[i],Root("AIBudgetStateDefinition","","<Budget><AccountShare Account='PRODUCTION' Percent='50'/></Budget>"+body),platform);
            Test("strategic/"+HeuristicNames[i],Root("AIStrategicStateDefinition","MaxTargets='3'",body),platform);
        }
        AssetBuffer hf0=Test("heuristic/AIStateResourceNodeShareHeuristic/PercentOfFairShare",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2269801993\' PercentOfFairShare=\'50%\'/></Heuristic>"),platform);
        Assert(Word(hf0.InstanceData,20,big)==0x3f000000u,"AIStateResourceNodeShareHeuristic/PercentOfFairShare independent layout");
        AssetBuffer hf1=Test("heuristic/AIStateResourceNodeShareHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2269801993\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf1.InstanceData[24]==0,"AIStateResourceNodeShareHeuristic/Above independent layout");
        AssetBuffer hf2=Test("heuristic/AIStateMoneyHeuristic/Money",Root("AIStateDefinition","","<Heuristic><H TypeId=\'75667102\' Money=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf2.InstanceData,20,big)==2u,"AIStateMoneyHeuristic/Money independent layout");
        AssetBuffer hf3=Test("heuristic/AIStateMoneyHeuristic/Account",Root("AIStateDefinition","","<Heuristic><H TypeId=\'75667102\' Account=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf3.InstanceData,24,big)==2u,"AIStateMoneyHeuristic/Account independent layout");
        AssetBuffer hf4=Test("heuristic/AIStateMoneyHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'75667102\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf4.InstanceData[28]==0,"AIStateMoneyHeuristic/Above independent layout");
        AssetBuffer hf5=Test("heuristic/AIStateTimerHeuristic/StartTime",Root("AIStateDefinition","","<Heuristic><H TypeId=\'278936256\' StartTime=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf5.InstanceData,20,big)==0x3e800000u,"AIStateTimerHeuristic/StartTime independent layout");
        AssetBuffer hf6=Test("heuristic/AIStateTimerHeuristic/EndTime",Root("AIStateDefinition","","<Heuristic><H TypeId=\'278936256\' EndTime=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf6.InstanceData,24,big)==0x3e800000u,"AIStateTimerHeuristic/EndTime independent layout");
        AssetBuffer hf7=Test("heuristic/AIStateTimerHeuristic/CountFrom",Root("AIStateDefinition","","<Heuristic><H TypeId=\'278936256\' CountFrom=\'AI_START\'/></Heuristic>"),platform);
        Assert(Word(hf7.InstanceData,28,big)==1u,"AIStateTimerHeuristic/CountFrom independent layout");
        AssetBuffer hf8=Test("heuristic/AIStateOpenNodeHeuristic/Exists",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2225927560\' Exists=\'false\'/></Heuristic>"),platform);
        Assert(hf8.InstanceData[24]==0,"AIStateOpenNodeHeuristic/Exists independent layout");
        AssetBuffer hf9=Test("heuristic/AIStateOpenNodeHeuristic/MinDistanceFromEnemy",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2225927560\' MinDistanceFromEnemy=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf9.InstanceData,20,big)==0x40000000u,"AIStateOpenNodeHeuristic/MinDistanceFromEnemy independent layout");
        AssetBuffer hf10=Test("heuristic/AIStateEnergyHeuristic/ExcessEnergy",Root("AIStateDefinition","","<Heuristic><H TypeId=\'263056479\' ExcessEnergy=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf10.InstanceData,20,big)==2u,"AIStateEnergyHeuristic/ExcessEnergy independent layout");
        AssetBuffer hf11=Test("heuristic/AIStateEnergyHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'263056479\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf11.InstanceData[28]==0,"AIStateEnergyHeuristic/Above independent layout");
        AssetBuffer hf12=Test("heuristic/AIStateEnergyHeuristic/Relationship",Root("AIStateDefinition","","<Heuristic><H TypeId=\'263056479\' Relationship=\'ALLIES\'/></Heuristic>"),platform);
        Assert(Word(hf12.InstanceData,24,big)==0u,"AIStateEnergyHeuristic/Relationship independent layout");
        AssetBuffer hf13=Test("heuristic/AIStateCanBuildBuildingsOnSurfaceHeuristic/MinPlacementGridCells",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2665825158\' MinPlacementGridCells=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf13.InstanceData,20,big)==2u,"AIStateCanBuildBuildingsOnSurfaceHeuristic/MinPlacementGridCells independent layout");
        AssetBuffer hf14=Test("heuristic/AIStateCanBuildBuildingsOnSurfaceHeuristic/MaxPlacementGridCells",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2665825158\' MaxPlacementGridCells=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf14.InstanceData,(int)Word(hf14.InstanceData,24,big),big)==2u,"AIStateCanBuildBuildingsOnSurfaceHeuristic/MaxPlacementGridCells independent layout");
        AssetBuffer hf15=Test("heuristic/AIStateCanBuildBuildingsOnSurfaceHeuristic/Surfaces",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2665825158\' Surfaces=\'DEEP_WATER\'/></Heuristic>"),platform);
        Assert(Word(hf15.InstanceData,28,big)==4u,"AIStateCanBuildBuildingsOnSurfaceHeuristic/Surfaces independent layout");
        AssetBuffer hf16=Test("heuristic/AIStateBridgeExistsHeuristic/BridgeHealthMinimumPercentage",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3152351395\' BridgeHealthMinimumPercentage=\'50%\'/></Heuristic>"),platform);
        Assert(Word(hf16.InstanceData,20,big)==0x3f000000u,"AIStateBridgeExistsHeuristic/BridgeHealthMinimumPercentage independent layout");
        AssetBuffer hf17=Test("heuristic/AIStateBridgeExistsHeuristic/BridgeHealthMaximumPercentage",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3152351395\' BridgeHealthMaximumPercentage=\'50%\'/></Heuristic>"),platform);
        Assert(Word(hf17.InstanceData,24,big)==0x3f000000u,"AIStateBridgeExistsHeuristic/BridgeHealthMaximumPercentage independent layout");
        AssetBuffer hf18=Test("heuristic/AIStateTechBuildingShareHeuristic/PercentOfFairShare",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3416369467\' PercentOfFairShare=\'50%\'/></Heuristic>"),platform);
        Assert(Word(hf18.InstanceData,20,big)==0x3f000000u,"AIStateTechBuildingShareHeuristic/PercentOfFairShare independent layout");
        AssetBuffer hf19=Test("heuristic/AIStateTechBuildingShareHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3416369467\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf19.InstanceData[24]==0,"AIStateTechBuildingShareHeuristic/Above independent layout");
        AssetBuffer hf20=Test("heuristic/AIStateTimeSinceLastExpansionHeuristic/Time",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3826170916\' Time=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf20.InstanceData,20,big)==0x3e800000u,"AIStateTimeSinceLastExpansionHeuristic/Time independent layout");
        AssetBuffer hf21=Test("heuristic/AIStateTimeSinceLastExpansionHeuristic/Past",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3826170916\' Past=\'false\'/></Heuristic>"),platform);
        Assert(hf21.InstanceData[24]==0,"AIStateTimeSinceLastExpansionHeuristic/Past independent layout");
        AssetBuffer hf22=Test("heuristic/AIStateEnemyNearbyHeuristic/Distance",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1740211623\' Distance=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf22.InstanceData,20,big)==0x40000000u,"AIStateEnemyNearbyHeuristic/Distance independent layout");
        AssetBuffer hf23=Test("heuristic/AIStateEnemyNearbyHeuristic/EnemyNearby",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1740211623\' EnemyNearby=\'false\'/></Heuristic>"),platform);
        Assert(hf23.InstanceData[32]==0,"AIStateEnemyNearbyHeuristic/EnemyNearby independent layout");
        AssetBuffer hf24=Test("heuristic/AIStateEnemyNearbyHeuristic/MinEnemyValue",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1740211623\' MinEnemyValue=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf24.InstanceData,24,big)==2u,"AIStateEnemyNearbyHeuristic/MinEnemyValue independent layout");
        AssetBuffer hf25=Test("heuristic/AIStateScriptedFlagHeuristic/FlagName",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2156949943\' FlagName=\'MixedCase\'/></Heuristic>"),platform);
        Assert(Word(hf25.InstanceData,20,big)==9u,"AIStateScriptedFlagHeuristic/FlagName independent layout");
        AssetBuffer hf26=Test("heuristic/AIStatePowerAdvantageHeuristic/Value",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' Value=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf26.InstanceData,20,big)==0x40000000u,"AIStatePowerAdvantageHeuristic/Value independent layout");
        AssetBuffer hf27=Test("heuristic/AIStatePowerAdvantageHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf27.InstanceData[96]==0,"AIStatePowerAdvantageHeuristic/Above independent layout");
        AssetBuffer hf28=Test("heuristic/AIStatePowerAdvantageHeuristic/IncludeOwnDefenses",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' IncludeOwnDefenses=\'false\'/></Heuristic>"),platform);
        Assert(hf28.InstanceData[97]==0,"AIStatePowerAdvantageHeuristic/IncludeOwnDefenses independent layout");
        AssetBuffer hf29=Test("heuristic/AIStatePowerAdvantageHeuristic/IncludeEnemyDefenses",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' IncludeEnemyDefenses=\'false\'/></Heuristic>"),platform);
        Assert(hf29.InstanceData[98]==0,"AIStatePowerAdvantageHeuristic/IncludeEnemyDefenses independent layout");
        AssetBuffer hf30=Test("heuristic/AIStatePowerAdvantageHeuristic/OwnKindOf",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' OwnKindOf=\'ACTIVATE_AFTER_UNPACK\'/></Heuristic>"),platform);
        Assert(Word(hf30.InstanceData,40,big)==2097152u,"AIStatePowerAdvantageHeuristic/OwnKindOf independent layout");
        AssetBuffer hf31=Test("heuristic/AIStatePowerAdvantageHeuristic/EnemyKindOf",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2915089289\' EnemyKindOf=\'ACTIVATE_AFTER_UNPACK\'/></Heuristic>"),platform);
        Assert(Word(hf31.InstanceData,76,big)==2097152u,"AIStatePowerAdvantageHeuristic/EnemyKindOf independent layout");
        AssetBuffer hf32=Test("heuristic/AIStateOpeningMoveHeuristic/Complete",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2128923060\' Complete=\'false\'/></Heuristic>"),platform);
        Assert(hf32.InstanceData[20]==0,"AIStateOpeningMoveHeuristic/Complete independent layout");
        AssetBuffer hf33=Test("heuristic/AIStateGarrisonHeuristic/NumGarrisons",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1278812460\' NumGarrisons=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf33.InstanceData,20,big)==2u,"AIStateGarrisonHeuristic/NumGarrisons independent layout");
        AssetBuffer hf34=Test("heuristic/AIStateGarrisonHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1278812460\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf34.InstanceData[28]==0,"AIStateGarrisonHeuristic/Above independent layout");
        AssetBuffer hf35=Test("heuristic/AIStateGarrisonHeuristic/Relationship",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1278812460\' Relationship=\'ALLIES\'/></Heuristic>"),platform);
        Assert(Word(hf35.InstanceData,24,big)==0u,"AIStateGarrisonHeuristic/Relationship independent layout");
        AssetBuffer hf36=Test("heuristic/AIStateIsBuildingHeuristic/KindOf",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1170787490\' KindOf=\'ACTIVATE_AFTER_UNPACK\'/></Heuristic>"),platform);
        Assert(Word(hf36.InstanceData,36,big)==2097152u,"AIStateIsBuildingHeuristic/KindOf independent layout");
        AssetBuffer hf37=Test("heuristic/AIStateIsBuildingHeuristic/PassIfBuilding",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1170787490\' PassIfBuilding=\'false\'/></Heuristic>"),platform);
        Assert(hf37.InstanceData[56]==0,"AIStateIsBuildingHeuristic/PassIfBuilding independent layout");
        AssetBuffer hf38=Test("heuristic/AIRedAlertHeuristic/On",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3371681655\' On=\'false\'/></Heuristic>"),platform);
        Assert(hf38.InstanceData[20]==0,"AIRedAlertHeuristic/On independent layout");
        AssetBuffer hf39=Test("heuristic/AIStateHarvesterCapHeuristic/MaxHarvesters",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2386854964\' MaxHarvesters=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf39.InstanceData,20,big)==2u,"AIStateHarvesterCapHeuristic/MaxHarvesters independent layout");
        AssetBuffer hf40=Test("heuristic/AIStateIncomeAdvantageHeuristic/Value",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1390340504\' Value=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf40.InstanceData,20,big)==0x40000000u,"AIStateIncomeAdvantageHeuristic/Value independent layout");
        AssetBuffer hf41=Test("heuristic/AIStateIncomeAdvantageHeuristic/Above",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1390340504\' Above=\'false\'/></Heuristic>"),platform);
        Assert(hf41.InstanceData[24]==0,"AIStateIncomeAdvantageHeuristic/Above independent layout");
        AssetBuffer hf42=Test("heuristic/AIStateConstantHeuristic/Weight",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1375432082\' Weight=\'2\'/></Heuristic>"),platform);
        Assert(Word(hf42.InstanceData,20,big)==0x40000000u,"AIStateConstantHeuristic/Weight independent layout");
        AssetBuffer hf43=Test("heuristic/AIStateIntervalHeuristic/IntervalTime",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2174347641\' IntervalTime=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf43.InstanceData,20,big)==0x3e800000u,"AIStateIntervalHeuristic/IntervalTime independent layout");
        AssetBuffer hf44=Test("heuristic/AIStateIntervalHeuristic/ActiveTime",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2174347641\' ActiveTime=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf44.InstanceData,24,big)==0x3e800000u,"AIStateIntervalHeuristic/ActiveTime independent layout");
        AssetBuffer hf45=Test("heuristic/AIStateIntervalHeuristic/CountFrom",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2174347641\' CountFrom=\'AI_START\'/></Heuristic>"),platform);
        Assert(Word(hf45.InstanceData,28,big)==1u,"AIStateIntervalHeuristic/CountFrom independent layout");
        AssetBuffer hf46=Test("heuristic/AIStateObjectOfTypeExistsHeuristic/Location",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3195463252\' Location=\'Land\'/></Heuristic>"),platform);
        Assert(Word(hf46.InstanceData,20,big)==2u,"AIStateObjectOfTypeExistsHeuristic/Location independent layout");
        AssetBuffer hf47=Test("heuristic/AIStateObjectOfTypeExistsHeuristic/PassIfExists",Root("AIStateDefinition","","<Heuristic><H TypeId=\'3195463252\' PassIfExists=\'false\'/></Heuristic>"),platform);
        Assert(hf47.InstanceData[136]==0,"AIStateObjectOfTypeExistsHeuristic/PassIfExists independent layout");
        AssetBuffer hf48=Test("heuristic/AIStatePathToTargetHeuristic/Surface",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4054901427\' Surface=\'Land\'/></Heuristic>"),platform);
        Assert(Word(hf48.InstanceData,20,big)==1u,"AIStatePathToTargetHeuristic/Surface independent layout");
        AssetBuffer hf49=Test("heuristic/AIStatePathToTargetHeuristic/ObjectTypeToUseForTest",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4054901427\' ObjectTypeToUseForTest=\'MixedCase\'/></Heuristic>"),platform);
        Assert(Word(hf49.InstanceData,24,big)==Hash("MixedCase",true),"AIStatePathToTargetHeuristic/ObjectTypeToUseForTest independent layout");
        AssetBuffer hf50=Test("heuristic/AIStatePathToTargetHeuristic/LocomotorConditionToUseForTest",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4054901427\' LocomotorConditionToUseForTest=\'ATTACKING\'/></Heuristic>"),platform);
        Assert(Word(hf50.InstanceData,28,big)==12u,"AIStatePathToTargetHeuristic/LocomotorConditionToUseForTest independent layout");
        AssetBuffer hf51=Test("heuristic/AIStatePathToTargetHeuristic/PathExists",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4054901427\' PathExists=\'false\'/></Heuristic>"),platform);
        Assert(hf51.InstanceData[32]==0,"AIStatePathToTargetHeuristic/PathExists independent layout");
        AssetBuffer hf52=Test("heuristic/AIStrategicDirectiveHeuristic/On",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4090667391\' On=\'false\'/></Heuristic>"),platform);
        Assert(hf52.InstanceData[24]==0,"AIStrategicDirectiveHeuristic/On independent layout");
        AssetBuffer hf53=Test("heuristic/AIStrategicDirectiveHeuristic/Directive",Root("AIStateDefinition","","<Heuristic><H TypeId=\'4090667391\' Directive=\'Assault\'/></Heuristic>"),platform);
        Assert(Word(hf53.InstanceData,20,big)==1u,"AIStrategicDirectiveHeuristic/Directive independent layout");
        AssetBuffer hf54=Test("heuristic/AIAlliedBeaconExistsHeuristic/Exists",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2115602688\' Exists=\'false\'/></Heuristic>"),platform);
        Assert(hf54.InstanceData[24]==0,"AIAlliedBeaconExistsHeuristic/Exists independent layout");
        AssetBuffer hf55=Test("heuristic/AIAlliedBeaconExistsHeuristic/Type",Root("AIStateDefinition","","<Heuristic><H TypeId=\'2115602688\' Type=\'Assault\'/></Heuristic>"),platform);
        Assert(Word(hf55.InstanceData,20,big)==0u,"AIAlliedBeaconExistsHeuristic/Type independent layout");
        AssetBuffer hf56=Test("heuristic/AIStateStructureOrHarvesterUnderAttackHeuristic/UnderAttack",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1801850452\' UnderAttack=\'false\'/></Heuristic>"),platform);
        Assert(hf56.InstanceData[24]==0,"AIStateStructureOrHarvesterUnderAttackHeuristic/UnderAttack independent layout");
        AssetBuffer hf57=Test("heuristic/AIStateStructureOrHarvesterUnderAttackHeuristic/TimeSinceAttacked",Root("AIStateDefinition","","<Heuristic><H TypeId=\'1801850452\' TimeSinceAttacked=\'250ms\'/></Heuristic>"),platform);
        Assert(Word(hf57.InstanceData,20,big)==0x3e800000u,"AIStateStructureOrHarvesterUnderAttackHeuristic/TimeSinceAttacked independent layout");
        Test("state/nested",Root("AIStateDefinition","","<Heuristic><LinearCombinationHeuristic TypeId='121779834'><WeightedHeuristic Weight='0.75'><Heuristic><ConstantHeuristic TypeId='1375432082' Weight='2'/></Heuristic></WeightedHeuristic><WeightedHeuristic Weight='0.25'><Heuristic><TimerHeuristic TypeId='278936256' EndTime='3s'/></Heuristic></WeightedHeuristic></LinearCombinationHeuristic></Heuristic>"),platform);
        Test("state/filters",Root("AIStateDefinition","","<Heuristic><EnemyNearbyHeuristic TypeId='1740211623'><EnemyFilter Include='INFANTRY'/></EnemyNearbyHeuristic><ObjectOfTypeExistsHeuristic TypeId='3195463252'><ObjectFilter Rule='ALL' Include='STRUCTURE'/></ObjectOfTypeExistsHeuristic></Heuristic>"),platform);
        foreach(string legacyFloat in new string[]{"nan","NaN","INF","Infinity","-INF"})
            Test("target/legacy-numeric-prefix/"+legacyFloat,Root("AITargetingHeuristic","","<BuildingPlacementDistance Distance='"+legacyFloat+"'/><BuildingPlacementDistance Distance='0'/>"),platform);
        foreach(string overflow in new string[]{"1e400","-1e400","1e39"})
            Reject("nonfinite parsed sorted distance",Root("AITargetingHeuristic","","<BuildingPlacementDistance Distance='"+overflow+"'/>"));
        Reject("unknown heuristic",Root("AIStateDefinition","","<Heuristic><H TypeId='999'/></Heuristic>"));
        Reject("heuristic missing TypeId",Root("AIStateDefinition","","<Heuristic><ConstantHeuristic/></Heuristic>"));
        Reject("duplicate wrapper",Root("AIStateDefinition","","<Heuristic/><Heuristic/>"));
        Reject("unknown budget child",Root("AIBudgetStateDefinition","","<Budget><Unknown/></Budget>"));
        Reject("unknown nested metadata",Root("AIMicroManagerData","","<TargetBias id='Unsupported'/>"));
        Reject("filter text",Root("AIMicroManagerData","","<IgnoreTargets>unknown</IgnoreTargets>"));
        Reject("unknown tactic child",Root("AIStrategicStateDefinition","","<Tactic><Unknown/></Tactic>"));
#if XMLAI_PERSONALITY_EXTENSION
        Test("personality/opening-no-heuristic",Root("AIPersonalityDefinition","","<OpeningMove/>"),platform);
        AssetBuffer openingEmpty=Test("personality/opening-empty-heuristic",Root("AIPersonalityDefinition","","<OpeningMove><Heuristic/></OpeningMove>"),platform);
        Assert(Word(openingEmpty.InstanceData,332,big)==1&&Word(openingEmpty.InstanceData,336,big)==512&&Word(openingEmpty.InstanceData,524,big)==528&&Word(openingEmpty.InstanceData,528,big)==0,"Opening nullable list header independent ABI");
        Test("personality/opening-known",Root("AIPersonalityDefinition","","<OpeningMove Name='Move\\\\13'><Heuristic><H TypeId='1375432082' Weight='2'/><H TypeId='278936256' StartTime='250ms'/></Heuristic></OpeningMove>"),platform);
        Reject("opening unknown heuristic",Root("AIPersonalityDefinition","","<OpeningMove><Heuristic><H TypeId='999'/></Heuristic></OpeningMove>"));
        Reject("opening duplicate heuristic wrapper",Root("AIPersonalityDefinition","","<OpeningMove><Heuristic/><Heuristic/></OpeningMove>"));
        foreach(string extensionXml in XmlaiPersonalityCases.Cases())
            Test("personality/extension",extensionXml,platform);
#else
        Reject("unsupported personality child",Root("AIPersonalityDefinition","","<UnitCap/>"));
#endif
        // Prefix navigation uses a canonical original declaration because BAB's
        // InstanceDeclaration may hash the qualified Name into Handle.TypeId.
        XmlDocument canonical=Xml(Root("AIMicroManagerData","UpdateTime='250ms'","<TargetBias UnitName='Tank'/>"));
        AssetBuffer expected=original.ProcessInstance(Declaration(canonical,Manager(canonical)));
        XmlDocument prefixed=Xml("<p:AIMicroManagerData xmlns:p='"+Ns+"' id='Oracle' UpdateTime='250ms'><p:TargetBias UnitName='Tank'/></p:AIMicroManagerData>");
        AssetBuffer actual=XmlaiCompiler.CompileXml(prefixed.DocumentElement,Manager(prefixed),big);
        Equal("prefix/instance",expected.InstanceData,actual.InstanceData);Equal("prefix/relocation",expected.RelocationData,actual.RelocationData);Equal("prefix/imports",expected.ImportsData,actual.ImportsData);++fixtures;
        InstanceDeclaration wrong=Declaration(prefixed,Manager(prefixed));
        if(wrong.Handle.TypeId!=2423780215u){bool rejected=false;try{XmlaiCompiler.ProcessInstance(wrong,platform);}catch(NotSupportedException){rejected=true;}Assert(rejected,"wrong qualified TypeId rejected");++rejections;}
        Reject("wrong root namespace","<AIMicroManagerData xmlns='urn:wrong'/>");
        Reject("unknown root",Root("Unknown","",""));
    }
    static int Main(string[] args) {
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            original=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[0]).GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}){
                int f=fixtures,c=comparisons,r=rejections,l=layouts;original.Initialize(null,p);Run(p);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            Console.WriteLine("PASS total fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}

