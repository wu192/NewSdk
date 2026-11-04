using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Schema;
using BinaryAssetBuilder.Core;

class XmlPrioritySdkOracle {
    const string NS="uri:ea.com:eala:asset";
    // This route table is generated from the formal component manifest by GenerateXmlSdkRoutes.py.
    // BEGIN GENERATED SDK ROUTES
    static readonly Dictionary<string,uint> Supported=new Dictionary<string,uint>(StringComparer.Ordinal) {
        {"AttributeModifier",3319822471u},
        {"FXList",2254974584u},
        {"SpecialPowerTemplate",2178440954u},
        {"ObjectCreationList",3899542881u},
        {"LocomotorTemplate",3972178387u},
        {"LogicCommandSet",3959844197u},
        {"FXParticleSystemTemplate",686292351u},
        {"UpgradeTemplate",3786401627u},
        {"AIPersonalityDefinition",3604279694u},
        {"AIStrategicStateDefinition",607123156u},
        {"AIMicroManagerData",2423780215u},
        {"AIBudgetStateDefinition",2893598307u},
        {"AIStateDefinition",3239935619u},
        {"AITargetingHeuristic",926814458u},
        {"LogicCommand",2101756272u},
        {"PlayerTech",2131237081u},
        {"PlayerTechRank",1802643394u},
        {"PlayerTechUpgradeBinding",2140592493u},
        {"ExperienceLevelTemplate",1874610847u},
        {"PlayerTechStoreTemplate",1213037816u},
        {"PurchasePlayerTechButtonTemplate",1566941920u},
        {"ButtonSingleStateData",3048039928u},
        {"UICommandButton",134745024u},
        {"FontDefaultSetting",1371131236u},
        {"FontTranslation",1318336226u},
        {"FontSubstitution",91719417u},
        {"Weather",2430161325u},
        {"WaterTransparency",1479531562u},
        {"WaterSetting",2728605516u},
        {"Environment",680780553u},
        {"LightingSettings",3169690211u},
        {"WaterTextureList",2995571269u},
        {"TerrainParticleMapping",1469498551u},
        {"BounceKickTerrainMapping",2368878637u},
        {"ShadowMap",4157475773u},
        {"OrdersManagerSettings",2996766068u},
        {"TerrainVisualSettings",1184276792u},
        {"CameraSettings",3898802054u},
        {"AIData",3827723072u},
        {"UnitAISettings",2964891936u},
        {"PlayerTemplate",4063352333u},
        {"MultiplayerSettings",191082224u},
        {"AIBuildStateDefinition",1644972043u},
        {"AIStrategicStateName",2010562192u},
        {"MultiplayerColor",2384988189u},
        {"AIPersonalityToAchievementEventMapping",4086175864u},
        {"FormationAssistantUnitDefinition",838130914u},
        {"FormationAssistantTemplate",2912385462u},
        {"FormationSelectionTemplate",3374519264u},
        {"FormationAssistant",1963196508u},
        {"GameStatsModeData",3249689110u},
        {"SkirmishSettings",402082255u},
        {"DamageFX",2905958645u},
        {"DamageFXSettings",75989905u},
        {"GeneralHotKeySlots",417176643u},
        {"DefaultHotKeys",3741098742u},
        {"DebugHotKeys",3359870461u},
        {"MappableKey",1350608344u},
        {"AudioEventLimitGroup",3906227957u},
        {"AudioLOD",299416263u},
        {"AudioSettings",1443425905u},
        {"Video",587347702u},
        {"VideoEventList",1447728787u},
        {"MicrophoneSettings",1826140078u},
        {"BootupDisplaySequence",3370391592u},
        {"HotKeySlot",2800139175u},
        {"MouseCursor",3784442503u},
        {"BasicMouseCursors",611271731u},
        {"UIMouseDragSelectionFeedback",611126373u},
        {"UIFloatingTextDisplay",1559937240u},
        {"UIMessageDisplay",1697313181u},
        {"UIMilitaryCaptionDisplay",1043989897u},
        {"UISubtitleDisplay",165611890u},
        {"UITimerDisplay",951036436u},
        {"DynamicGameLOD",2901356964u},
        {"GameLODPreset",354222972u},
        {"StaticGameLOD",531798225u},
        {"UICoopTinting",4243804314u},
        {"RadiusCursor",544776545u},
        {"AnimationSoundSettings",1302035577u},
        {"MiscAudio",2070603733u},
        {"LargeGroupAudioMap",534008255u},
        {"UIMouseTacticalSelectionDetailsButtonHotKeys",1296329066u},
        {"UIMouseTacticalMainCommandBarHotKeys",2591692061u},
        {"UIMouseTacticalPlayerPowerHotKeys",2283998413u},
        {"UIMouseTacticalMiscHotKeys",2014061824u},
        {"UIMouseHUDElementHotKeyGate",2738273474u},
        {"UIQuickChatMessage",4098431913u},
        {"UIMouseTacticalQuickChatDisplay",2891878770u},
        {"UIMouseVoiceChat",3746130610u},
        {"UIDecalGrid",2599418799u},
        {"UIDecalCloud",2524893660u},
        {"UIDecalDestinationMarker",1532214660u},
        {"UIWorldBuilderDecalGrid",1238702511u},
        {"BaseAudioEventInfo",1079232276u},
        {"BaseSingleSound",3228425025u},
        {"AudioEvent",2219670431u},
        {"MusicTrack",1883691512u},
        {"MP3MusicTrack",1013434420u},
        {"DialogEvent",3558134211u},
        {"MP3DialogEvent",1479988269u},
        {"AmbientStream",3928762264u},
        {"MP3AmbientStream",1813598662u},
        {"Multisound",2745675575u},
        {"PathMusicGameDynamicState",97402485u},
        {"PathMusicGameDynamicStateSet",343094706u},
        {"PathMusicGameDynamicTransition",2343360052u},
        {"PathMusicEventRuntime",1122327197u},
        {"PathMusicEventSet",3339496789u},
        {"PathMusicMapRuntime",2026048286u},
        {"MusicPalette",2407383451u},
        {"MusicScriptConditionNuggetBase",2292378780u},
        {"MusicScriptConditionExpensiveNuggetBase",849624115u},
        {"MusicScriptConditionNugget_LocalPlayerIsObserver",188286714u},
        {"MusicScriptConditionNugget_UnitsFarFromBase",2620791304u},
        {"MusicScriptConditionNugget_TimeFromStartOfLevel",1464425282u},
        {"MusicScriptConditionNugget_TrackPlayedCount",57370491u},
        {"MusicScriptConditionNugget_SpecificTrackTypePlaying",739280456u},
        {"MusicScriptConditionNugget_AnyTrackPlaying",3402992833u},
        {"MusicScriptConditionNugget_ObjectsOfTypeExist",4286308287u},
        {"MusicScriptConditionNugget_EvaEventPlayedRecently",2743973749u},
        {"MusicScriptConditionNugget_ObjectsNearEvaEvent",3386715757u},
        {"MusicScriptConditionNugget_ScoredKillCount",3620764004u},
        {"MusicScriptConditionNugget_Not",4007043648u},
        {"MusicScriptConditionNugget_Or",2281773838u},
        {"MusicScriptConditionNugget_And",1343682755u},
        {"MusicScriptConditionNugget_PathMusicStateLastActive",2912435838u},
        {"MusicScriptConditionNugget_PathMusicStateCurrentlyActive",3727134806u},
        {"MusicScriptConditionNugget_EngagementCount",3476397162u},
        {"MusicScriptConditionNugget_LocalPlayerKillRatio",2818967523u},
        {"MusicScriptConditionNugget_HitBySuperWeapon",1634186921u},
        {"MusicScriptTrack",1038067901u},
        {"UIMouseTacticalCoOpAIButtonsHelpButton",104126924u},
        {"UIMouseTacticalCommandCommunicateWithAllies",113029208u},
        {"UIMouseTacticalToggleRepairModeButton",346077409u},
        {"UIMouseTacticalAttackMoveButton",442957564u},
        {"UIMouseTacticalCommandPlaceWall",485977567u},
        {"UIMouseTacticalForceAttackMoveButton",694838069u},
        {"UIMouseTacticalCoOpAIButtonsHoldObjectButton",772462768u},
        {"UIMouseTacticalExitContainerButton",1014034471u},
        {"UIMouseTacticalPlayerTechStoreButton",1138847078u},
        {"UIMouseTacticalCommandMove",1234485619u},
        {"UIMouseTacticalCommandSell",1249197161u},
        {"UIMouseTacticalUnitIntroButton",1277109486u},
        {"UIMouseTacticalCommandSelect",1334543376u},
        {"UIMouseTacticalCoOpAIButtonsNormalButton",1371977384u},
        {"UIMouseTacticalCommandPlaceRallyPoint",1474300054u},
        {"UIMouseTacticalCommandHarvest",1499648070u},
        {"UIMouseTacticalCommandToggleSelfRepair",1956336137u},
        {"UIMouseTacticalBuildQueueTab",1974922393u},
        {"UIMouseTacticalCommandGetCrate",2067763016u},
        {"UIMouseTacticalDeactivateMissionHotSpotsButton",2047841880u},
        {"UIMouseTacticalCommandForceAttack",2074164365u},
        {"UIMouseTacticalPlanningModeButton",2278083954u},
        {"UIMouseTacticalCommandMisc",2505151835u},
        {"UIMouseTacticalSelectionRefinementTab",2635272840u},
        {"UIMouseTacticalCommandForceMove",2697587777u},
        {"UIMouseTacticalOpenAdvancedCommandsButton",2808773267u},
        {"UIMouseTacticalSetStanceButton",2873949396u},
        {"UIMouseTacticalCoOpAIButtonsHoldPositionButton",2914137727u},
        {"UIMouseTacticalCommandMineOre",2957071942u},
        {"UIMouseTacticalWaypointModeButton",2958905921u},
        {"UIMouseTacticalReverseMoveButton",2987234945u},
        {"UIMouseTacticalCommandEnter",3057018816u},
        {"UIMouseTacticalCoOpAIButtonsAssaultButton",3094553884u},
        {"UIMouseTacticalPauseButton",3420020972u},
        {"UIMouseTacticalCommandAttack",3548787763u},
        {"UIMouseTacticalCommandAttackMove",3575834349u},
        {"UIMouseTacticalForceMoveButton",3716369729u},
        {"UIMouseTacticalForceAttackButton",3756168709u},
        {"UIMouseTacticalCommandPlaceAIBeacon",3809842710u},
        {"UIMouseTacticalCommandDock",3976885676u},
        {"UIMouseTacticalCommandReverseMove",4026878150u},
        {"UIMouseTacticalBuildQueueTypeTab",4094578721u},
        {"UIMouseTacticalSellModeButton",4102242385u},
        {"UIMouseTacticalStopButton",4140422551u},
        {"UIMouseTacticalCommandSetRallyPoint",4180354873u},
        {"UIMouseTacticalFastForwardButton",4232364483u},
        {"UIMouseTacticalGroupSelection",241329492u},
        {"UIMouseTacticalLookAt",1795213437u},
        {"UIMouseTacticalRadarTelestrator",3366599054u},
        {"UIMouseTacticalTelestrator",1921726501u},
        {"UIMouseTacticalThreatMeter",4143989686u},
        {"BaseConditionData",3899256902u},
        {"ObjectConditionData",519519951u},
        {"UpgradeConditionData",76901015u},
        {"GameplayConditionData",2309012900u},
        {"TargetingBaseCompare",3600209407u},
        {"TargetingDistanceCompare",2359666647u},
        {"TargetingCombatChainCompare",89767986u},
        {"TargetingInTurretArcCompare",2007776008u},
        {"TargetingCompareList",3188107749u},
        {"SiegeEvaEvent",1268003905u},
        {"EvaSettings",2361666546u},
        {"EvaEvent",357806494u},
        {"MiscEvaEvents",1807540972u},
        {"AchievementEvent",4230032093u},
        {"MapNameToAchievementEventMapping",3594283852u},
        {"GameplayAchievementTriggerData",348670903u},
        {"HealthBarTemplate",1507490649u},
        {"HealthBarSettings",3919360587u},
        {"UnitOverlayIconSettings",2421413379u},
        {"ObjectFilterAsset",1151702845u},
        {"RegisteredObjectSet",814796096u},
        {"ObjectVisionRangeItem",772930644u},
        {"InvisibilityTemplate",1431290504u},
        {"InvisibilityManagerSettings",254471188u},
        {"StanceTemplate",2525284163u},
        {"EmotionTemplate",2836737428u},
        {"ProjectilePath",1710207315u},
        {"ForceFeedbackEvent",474482543u},
        {"EngagingContent",3058114834u},
        {"EngagingContentGroup",11280974u},
        {"EngagingContentSequence",3799634821u},
        {"IntelDB",530081230u},
        {"UnitIntro",1870180772u},
        {"BreezeInfo",1626415288u},
        {"ModelSettings",1968811301u},
        {"AutoParticleSystemSettings",3278684478u},
        {"CrowdResponse",400896388u},
        {"RadarTerrainImage",1099769729u},
        {"UIRadarObjectsLayer",4039592522u},
        {"W3DDeviceSettings",1469055344u},
        {"ScorchMarkSettings",2383722691u},
        {"Language",2915726794u},
        {"HeaderTemplate",1218703046u},
        {"FontDirectory",3569007303u},
        {"TheVersion",4236434438u},
        {"Credits",3978455785u},
        {"CommandSet",2584759047u},
        {"Road",3516764358u},
        {"ShaderOverride",3166846828u},
        {"ShaderOverrideRule",3648907125u},
        {"ImageSequence",565855655u},
        {"UnitTypeIcon",3614134471u},
        {"DrawGroupInfo",2913856066u},
        {"OnlineChatColors",1345252658u},
        {"ArchivableMovie",3741496377u},
        {"ArchiveMovieList",2099472558u},
        {"NetworkSettings",4272453293u},
        {"PathfindSettings",970663416u},
        {"ResourceSettings",3279338544u},
        {"GraphicsHardware",2846460739u},
        {"CampaignTemplate",1482556238u},
        {"CampaignEnumerator",731899443u},
        {"MissionTemplate",3477794083u},
        {"MissionHotSpotSettings",3175516876u},
        {"TutorialMapData",3408790761u},
        {"LocalBuildListMonitor",315614191u},
        {"BuildSystemSettings",3974617950u},
        {"GiftManagerSettings",1047799468u},
        {"MpGameRules",741706624u},
        {"DynamicsContactTagMapping",1044178106u},
        {"ConnectionLineManager",105587076u},
        {"RedAlertTemplate",4141306833u},
        {"ConquerCardDefinitionBase",3258480561u},
        {"ConquerCardDefinitionUnit",3562795973u},
        {"ConquerCardDefinitionStructure",4289687760u},
        {"ConquerCardDefinitionPower",4164089898u},
        {"ConquerCardPrebuiltDeck",2806522526u},
        {"ArmyDefinition",3587539190u},
        {"SkirmishOpeningMove",3971904488u},
        {"DLContent",212695140u},
        {"AssetReportTable",255713734u},
        {"StringHashTable",300273508u},
        {"MeshInstanceTemplate",815686049u},
        {"PackedTextureImage",1449288224u},
        {"OnDemandTextureImage",4262364347u},
        {"AptAptData",1816258268u},
        {"AptConstData",983359664u},
        {"AptDatData",1110676961u},
        {"AptGeometryData",1034587168u},
        {"BaseRenderAssetType",2989459401u},
        {"AudioFileRuntime",584092973u},
        {"Mouse",2525492603u},
        {"UnitAbilityButtonTemplate",3517887782u},
        {"PlayerPowerButtonTemplate",4044322798u},
        {"TestTexture",2826773070u},
        {"TestTextureCollection",49556361u},
        {"TerrainAssetCollection",582251659u},
        {"TerrainTextureAtlasRuntime",3878948534u},
        {"W3DHierarchy",1641540160u},
        {"W3DContainer",4042295058u},
        {"W3DAnimationRuntime",3534264507u},
        {"W3DCollisionBox",3810008068u},
        {"MapMetaData",1603703110u},
        {"GameScriptList",3737094024u},
        {"GameMap",3575309793u},
        {"UIConfigList",3072160547u},
        {"UIJoypadCommandBarMixedGroup",1276968974u},
        {"UIJoypadCommandBarHomogenousGroup",1546953756u},
        {"UIJoypadCommandBarCoopCommunicator",1654297531u},
        {"JoypadCommandBarButtonTemplate",1804891371u},
        {"UIJoypadCommandBarStances",1887864852u},
        {"JoypadCommandBarTemplate",1931769760u},
        {"UIJoypadCommandBarTopMenu",2170044043u},
        {"UIJoypadCommandBarPlayerPowers",2742786440u},
        {"UIJoypadCommandBarButtonBuild",3423839194u},
        {"UIJoypadCommandBarControlGroups",3645205636u},
        {"UIJoypadCommandBarSingleUnit",3901433122u},
    };
    static readonly string[] NonGlobalRoots=new[]{"GameStatsModeData","MicrophoneSettings","BaseAudioEventInfo","BaseSingleSound","PathMusicEventRuntime","PathMusicMapRuntime","MusicScriptConditionNuggetBase","MusicScriptConditionExpensiveNuggetBase","BaseConditionData","ObjectConditionData","UpgradeConditionData","TargetingBaseCompare","ObjectVisionRangeItem","CommandSet","ShaderOverrideRule","ArchivableMovie","ArchiveMovieList","ConquerCardDefinitionBase","BaseRenderAssetType","AudioFileRuntime","TerrainTextureAtlasRuntime","W3DAnimationRuntime"};
    static readonly uint[] ExcludedTypeIds=new uint[]{26632491u};
    // END GENERATED SDK ROUTES
    static readonly Dictionary<string,string> Required=new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly Dictionary<string,string> SupportAssets=new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly HashSet<string> RequiredTypes=new HashSet<string>(StringComparer.Ordinal);
    static uint TypeId(string name) {
        uint value;if(Supported.TryGetValue(name,out value))return value;
        throw new Exception("Unexpected SDK trace type: "+name);
    }
    static InstanceDeclaration Declaration(XmlDocument xml) {
        var namespaces=new XmlNamespaceManager(xml.NameTable);namespaces.AddNamespace("ea",NS);
        var document=new AssetDeclarationDocument();
        Type type=typeof(AssetDeclarationDocument).GetNestedType("CurrentState",BindingFlags.NonPublic);
        object state=FormatterServices.GetUninitializedObject(type);
        type.GetField("NamespaceManager").SetValue(state,namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(document,state);
        var instance=new InstanceDeclaration(document);instance.XmlNode=xml.DocumentElement;return instance;
    }
    static void Equal(byte[] actual,byte[] expected,string label) {
        if(actual==null || expected==null || actual.Length!=expected.Length)throw new Exception(label+": array length");
        for(int i=0;i<actual.Length;++i)if(actual[i]!=expected[i])throw new Exception(label+": byte "+i+" actual="+actual[i]+" expected="+expected[i]);
    }
    static bool Contains(byte[] haystack,byte[] value) {
        if(value.Length==0)throw new Exception("Cannot prove SDK output contains an empty asset");
        for(int i=0;i<=haystack.Length-value.Length;++i) {
            int j=0;while(j<value.Length && haystack[i+j]==value[j])++j;
            if(j==value.Length)return true;
        }
        return false;
    }
    static string Hash(string path) {
        using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    }
    static void NoOriginalFiles(string directory) {
        foreach(string name in new[]{"BinaryAssetBuilder.Tokenizer.dll","BinaryAssetBuilder.XmlCompiler.dll"})
            if(File.Exists(Path.Combine(directory,name)))throw new Exception("Original compiler physically present in isolated host: "+Path.Combine(directory,name));
    }
    static void NoOriginalAssemblies() {
        foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            string name=assembly.GetName().Name;
            if(name=="BinaryAssetBuilder.Tokenizer" || name=="BinaryAssetBuilder.XmlCompiler")
                throw new Exception("Original compiler loaded in isolated host: "+assembly.FullName);
        }
    }
    static XmlNamespaceManager Namespaces(XmlDocument d) {
        var n=new XmlNamespaceManager(d.NameTable);n.AddNamespace("ea",NS);return n;
    }
    static void RequireTag(XmlElement element,string label,uint id) {
        uint actual;
        if(element==null || element.NamespaceURI!=NS || !UInt32.TryParse(element.GetAttribute("TypeId"),NumberStyles.None,CultureInfo.InvariantCulture,out actual) || actual!=id)
            throw new Exception("Missing or incorrect BAB numeric TypeId: "+label);
    }
    static void CheckPreprocessing(XmlDocument d,string id,string type) {
        // BAB adds root TypeId during ValidateInstances; the declaration handle must independently agree.
        RequireTag(d.DocumentElement,id,TypeId(type));
        if(Declaration(d).Handle.TypeId!=TypeId(type))throw new Exception("Incorrect BAB root handle TypeId: "+id);
        XmlNamespaceManager ns=Namespaces(d);
        foreach(XmlElement e in d.SelectNodes("/ea:FXList/ea:NuggetList/* | /ea:FXParticleSystemTemplate/ea:Physics/* | /ea:FXParticleSystemTemplate/ea:Draw/* | /ea:FXParticleSystemTemplate/ea:Volume/* | /ea:FXParticleSystemTemplate/ea:Velocity/* | /ea:FXParticleSystemTemplate/ea:Update/* | /ea:FXParticleSystemTemplate/ea:Event/*",ns)) {
            uint tag;
            if(e.NamespaceURI!=NS || !UInt32.TryParse(e.GetAttribute("TypeId"),NumberStyles.None,CultureInfo.InvariantCulture,out tag) || tag==0)
                throw new Exception("Missing BAB polymorphic TypeId: "+id+"/"+e.LocalName);
        }
        if(id=="RecoveryXmlPriority_FX_Filters") {
            if(d.SelectNodes("//ea:SourceObjectFilter",ns).Count==0 || d.SelectNodes("//ea:SecondaryObjectFilter",ns).Count==0)
                throw new Exception("SDK did not call both FXList filter paths");
        } else if(id=="RecoveryXmlPriority_Particle_Gpu") {
            RequireTag((XmlElement)d.SelectSingleNode("/ea:FXParticleSystemTemplate/ea:Draw/*",ns),id+"/DrawGpu",2248017887u);
            RequireTag((XmlElement)d.SelectSingleNode("/ea:FXParticleSystemTemplate/ea:Update/*",ns),id+"/UpdateDefault",2933826200u);
            bool life=false,collision=false;
            foreach(XmlElement e in d.SelectNodes("/ea:FXParticleSystemTemplate/ea:Event/*",ns)) {
                if(e.GetAttribute("TypeId")=="3465979181")life=true;
                if(e.GetAttribute("TypeId")=="1075723135")collision=true;
            }
            if(!life || !collision)throw new Exception("SDK did not call both concrete Event paths");
        } else if(id=="RecoveryXmlPriority_Particle_Lightning") {
            RequireTag((XmlElement)d.SelectSingleNode("/ea:FXParticleSystemTemplate/ea:Draw/*",ns),id+"/DrawLightning",3036920908u);
            if(d.SelectNodes("/ea:FXParticleSystemTemplate/ea:Draw/*/ea:RandomSet",ns).Count!=2)
                throw new Exception("SDK Lightning probe must contain two RandomSet entries");
        } else if(id=="RecoveryXmlPriority_Particle_RenderObject") {
            RequireTag((XmlElement)d.SelectSingleNode("/ea:FXParticleSystemTemplate/ea:Draw/*",ns),id+"/DrawRenderObject",3408220251u);
            RequireTag((XmlElement)d.SelectSingleNode("/ea:FXParticleSystemTemplate/ea:Update/*",ns),id+"/UpdateRenderObject",234581578u);
            if(d.SelectNodes("/ea:FXParticleSystemTemplate/ea:Draw/*/ea:ObjectSet",ns).Count!=3)
                throw new Exception("SDK RenderObject probe must contain three ObjectSet entries");
        }
    }
    static void ValidateProbe(string path) {
        var d=new XmlDocument();d.Load(path);
        if(d.DocumentElement.LocalName!="AssetDeclaration" || d.DocumentElement.NamespaceURI!=NS)throw new Exception("Expected source AssetDeclaration");
        Required.Clear();SupportAssets.Clear();RequiredTypes.Clear();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(XmlNode node in d.DocumentElement.ChildNodes) {
            if(node.NodeType!=XmlNodeType.Element)continue;
            XmlElement e=(XmlElement)node;
            if(e.NamespaceURI!=NS)throw new Exception("Foreign source probe namespace");
            if(e.LocalName=="Includes" || e.LocalName=="Tags" || e.LocalName=="Defines")continue;
            string id=e.GetAttribute("id");
            if(String.IsNullOrEmpty(id) || !seen.Add(id))throw new Exception("Empty or duplicated source asset id: "+id);
            if(Supported.ContainsKey(e.LocalName)) {
                Required.Add(id,e.LocalName);RequiredTypes.Add(e.LocalName);
            } else SupportAssets.Add(id,e.LocalName);
        }
        if(Required.Count==0)throw new Exception("Source probe has no in-scope recovered XmlCompiler assets");
    }
    static void WriteResult(string path,object value) {
        File.WriteAllText(path,new JavaScriptSerializer().Serialize(value));
    }
    static void Schema(string sdk,string probe,string report) {
        ValidateProbe(probe);
        var schemas=new XmlSchemaSet();schemas.XmlResolver=new XmlUrlResolver();
        schemas.Add(NS,Path.Combine(sdk,"Schemas/xsd/CnC3Types.xsd"));schemas.Compile();
        var source=new XmlDocument();source.Load(probe);source.Schemas=schemas;
        var schemaErrors=new List<string>();
        source.Validate(delegate(object sender,ValidationEventArgs e){
            XmlSchemaValidationException invalid=e.Exception as XmlSchemaValidationException;
            XmlNode failed=invalid==null?null:invalid.SourceObject as XmlNode;
            XmlNode asset=failed;
            if(asset is XmlAttribute)asset=((XmlAttribute)asset).OwnerElement;
            while(asset!=null && asset.ParentNode!=source.DocumentElement)asset=asset.ParentNode;
            string identity=asset is XmlElement ? ((XmlElement)asset).GetAttribute("id") : "unknown-asset";
            schemaErrors.Add(identity+": "+e.Message);
        });
        if(schemaErrors.Count!=0)throw new Exception("Legal SDK probe schema errors ("+schemaErrors.Count+"): \n"+String.Join("\n",schemaErrors.ToArray()));
        int rejected=0;
        // General negatives apply to every profile. The mandatory-Physics boundary is
        // additionally retained whenever the profile contains a particle template.
        var negatives=new List<string>(new[]{"missing-id","unknown-attribute"});
        var originalNamespaces=Namespaces(source);
        if(source.SelectSingleNode("/ea:AssetDeclaration/ea:FXParticleSystemTemplate/ea:Physics",originalNamespaces)!=null)negatives.Add("missing-physics");
        foreach(string kind in negatives) {
            var invalid=new XmlDocument();invalid.Load(probe);invalid.Schemas=schemas;var ns=Namespaces(invalid);
            if(kind=="missing-id") {
                foreach(XmlNode assetNode in invalid.DocumentElement.ChildNodes){XmlElement asset=assetNode as XmlElement;if(asset!=null && Required.ContainsKey(asset.GetAttribute("id"))){asset.RemoveAttribute("id");break;}}
            } else if(kind=="missing-physics") {
                XmlNode physics=invalid.SelectSingleNode("/ea:AssetDeclaration/ea:FXParticleSystemTemplate/ea:Physics",ns);
                if(physics==null)throw new Exception("Probe has no mandatory Physics child to test");
                physics.ParentNode.RemoveChild(physics);
            } else { foreach(XmlNode assetNode in invalid.DocumentElement.ChildNodes) { XmlElement asset=assetNode as XmlElement;if(asset!=null && Required.ContainsKey(asset.GetAttribute("id"))){asset.SetAttribute("RecoveryInvalidAttribute","1");break;} } }
            bool failed=false;
            try { invalid.Validate(delegate(object sender,ValidationEventArgs e){throw new Exception(e.Message);}); }
            catch(XmlSchemaValidationException){failed=true;}
            catch(Exception){failed=true;}
            if(!failed)throw new Exception("Full SDK XSD accepted invalid "+kind);
            ++rejected;
        }
        WriteResult(report,new{succeeded=true,mode="schema",validatedDocuments=1,negativeFixtures=rejected,expectedAssets=Required.Count,expectedTypes=RequiredTypes.Count,supportAssets=SupportAssets,registeredTypes=Supported.Count,nonGlobalRegisteredRoots=NonGlobalRoots,probeSha256=Hash(probe),schemaEntry="Schemas/xsd/CnC3Types.xsd"});
        Console.WriteLine("PASS SDK full XSD: {0} in-scope assets/{1} root types, {2} support assets; {3} negative fixtures rejected.",Required.Count,RequiredTypes.Count,SupportAssets.Count,rejected);
    }
    static void CompareMetadata(IAssetBuilderPlugin original,IAssetBuilderPlugin recovered,int platform,ref int fields) {
        if(original.GetVersionNumber()!=recovered.GetVersionNumber())throw new Exception("SDK version differs on platform "+platform);
        if(original.GetAllTypesHash()!=recovered.GetAllTypesHash())throw new Exception("SDK all-types hash differs on platform "+platform);
        fields+=2;
        foreach(uint id in Supported.Values) {
            object a=original.GetExtendedTypeInformation(id),b=recovered.GetExtendedTypeInformation(id);
            if(a==null || b==null)throw new Exception("Null ExtendedTypeInformation");
            foreach(FieldInfo field in typeof(ExtendedTypeInformation).GetFields(BindingFlags.Public|BindingFlags.Instance)) {
                if(!Object.Equals(field.GetValue(a),field.GetValue(b)))throw new Exception("Metadata field differs: "+id+"/"+field.Name+"/platform="+platform);
                ++fields;
            }
            foreach(PropertyInfo property in typeof(ExtendedTypeInformation).GetProperties(BindingFlags.Public|BindingFlags.Instance)) {
                if(!property.CanRead || property.GetIndexParameters().Length!=0)continue;
                if(!Object.Equals(property.GetValue(a,null),property.GetValue(b,null)))throw new Exception("Metadata property differs: "+id+"/"+property.Name+"/platform="+platform);
                ++fields;
            }
        }
    }
    static void Metadata(string originalPath,string recoveredPath,string report) {
        Environment.SetEnvironmentVariable("RA3_RECOVERED_XML_PRIORITY_TRACE",null);
        var original=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(originalPath).GetType("BinaryAssetBuilder.XmlCompiler.Plugin",true));
        var recovered=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(recoveredPath).GetType("Recovered.XmlPriority.Plugin",true));
        int fields=0,initializations=0,reinitializations=0,rejections=0;
        foreach(int platform in new[]{0,1,2,99}) {
            original.Initialize(null,(TargetPlatform)platform);recovered.Initialize(null,(TargetPlatform)platform);
            CompareMetadata(original,recovered,platform,ref fields);++initializations;
            int next=platform==0?1:platform==1?2:platform==2?99:0;
            original.ReInitialize(null,(TargetPlatform)next);recovered.ReInitialize(null,(TargetPlatform)next);
            CompareMetadata(original,recovered,next,ref fields);++reinitializations;
            bool refused=false;
            try { recovered.GetExtendedTypeInformation(4294967294u); }catch(NotSupportedException){refused=true;}
            if(!refused)throw new Exception("Recovered SDK plugin accepts unknown type metadata");++rejections;
            var xml=new XmlDocument();xml.LoadXml("<Unsupported xmlns='"+NS+"' id='Unsupported' TypeId='4294967294'/>");
            refused=false;
            try { recovered.ProcessInstance(Declaration(xml)); }catch(NotSupportedException){refused=true;}
            if(!refused)throw new Exception("Recovered SDK plugin accepts unknown ProcessInstance type");++rejections;
            foreach(uint excludedId in ExcludedTypeIds) {
                refused=false;try{recovered.GetExtendedTypeInformation(excludedId);}catch(NotSupportedException){refused=true;}
                if(!refused)throw new Exception("Recovered SDK plugin accepts an excluded root: "+excludedId);++rejections;
            }
        }
        if(fields<16)throw new Exception("No metadata fields were compared");
        WriteResult(report,new{succeeded=true,mode="metadata",platforms=new[]{0,1,2,99},supportedTypes=Supported.Count,nonGlobalRegisteredRoots=NonGlobalRoots,excludedTypes=ExcludedTypeIds.Length,initializations=initializations,reinitializations=reinitializations,fieldComparisons=fields,unknownTypeRejections=rejections,originalSha256=Hash(originalPath),recoveredSha256=Hash(recoveredPath)});
        Console.WriteLine("PASS SDK metadata: four platforms, Initialize/ReInitialize, all public metadata fields, unknown type rejection.");
    }
    // Proof for previously accepted traces whose path cannot enter the new SDK-only
    // metadata normalization branch. This never invokes ProcessInstance or a compiler.
    static void ReuseGuard(string recoveredPath,string traceDirectory,string report) {
        string dll=Path.GetFullPath(recoveredPath),trace=Path.GetFullPath(traceDirectory);
        Environment.SetEnvironmentVariable("RA3_RECOVERED_XML_PRIORITY_TRACE",null);
        NoOriginalFiles(Path.GetDirectoryName(dll));
        NoOriginalFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        NoOriginalAssemblies();
        Assembly library=Assembly.LoadFrom(dll);
        Type pluginType=library.GetType("Recovered.XmlPriority.Plugin",true);
        MethodInfo normalize=pluginType.GetMethod("CompilerInput",BindingFlags.NonPublic|BindingFlags.Static);
        if(normalize==null || normalize.ReturnType!=typeof(InstanceDeclaration))throw new Exception("Missing exact SDK normalization guard");
        ParameterInfo[] parameters=normalize.GetParameters();
        if(parameters.Length!=1 || parameters[0].ParameterType!=typeof(InstanceDeclaration))throw new Exception("Incorrect SDK normalization guard signature");
        string manifest=Path.Combine(trace,"calls.tsv");
        var stems=new HashSet<string>(StringComparer.Ordinal);
        var xmlHashes=new Dictionary<string,string>(StringComparer.Ordinal);
        var typeChecks=new Dictionary<string,int>(StringComparer.Ordinal);
        int checks=0;
        foreach(string row in File.ReadAllLines(manifest)) {
            string[] fields=row.Split('\t');
            if(fields.Length!=4)throw new Exception("Malformed previously accepted SDK call row");
            string type=fields[0],id=fields[1],stem=fields[2];
            TypeId(type);
            if(String.IsNullOrEmpty(stem) || stem!=Path.GetFileName(stem) || stem.IndexOfAny(new[]{'/','\\',':'})>=0 || !stems.Add(stem))throw new Exception("Invalid or duplicate previous trace stem");
            string path=Path.Combine(trace,stem+".xml");
            var xml=new XmlDocument();xml.Load(path);
            if(xml.DocumentElement.LocalName!=type || xml.DocumentElement.GetAttribute("id")!=id)throw new Exception("Previous trace identity mismatch");
            if(xml.DocumentElement.HasAttribute("buildRule") || xml.DocumentElement.HasAttribute("typeHashCode"))throw new Exception("Previous trace can enter changed metadata normalization branch: "+id);
            InstanceDeclaration original=Declaration(xml);
            XmlNode originalNode=original.Node;
            string originalXml=originalNode.OuterXml;
            object normalized=normalize.Invoke(null,new object[]{original});
            if(!Object.ReferenceEquals(normalized,original) || !Object.ReferenceEquals(((InstanceDeclaration)normalized).Node,originalNode) || originalNode.OuterXml!=originalXml)throw new Exception("Metadata-absent guard changed prior SDK input: "+id);
            NoOriginalAssemblies();
            xmlHashes.Add(stem,Hash(path));
            if(!typeChecks.ContainsKey(type))typeChecks[type]=0;++typeChecks[type];
            ++checks;
        }
        if(checks==0)throw new Exception("No prior SDK calls available for guard proof");
        NoOriginalFiles(Path.GetDirectoryName(dll));NoOriginalAssemblies();
        WriteResult(report,new{succeeded=true,mode="reuse",guardChecks=checks,nativeCompilerCalls=0,processInstanceCalls=0,rootMetadataAbsent=true,originalDeclarationIdentityPreserved=true,originalNodeIdentityPreserved=true,originalXmlPreserved=true,originalDllFilesAbsent=true,originalCompilerAssembliesAbsent=true,typeChecks=typeChecks,traceManifestSha256=Hash(manifest),xmlSha256=xmlHashes,pluginSha256=Hash(dll),scope="Metadata-absent identity-guard proof only; reuses previous SDK acceptance and does not rerun compilers"});
        Console.WriteLine("PASS SDK reuse guard: {0} original declarations/nodes preserved; ProcessInstance/native compiler calls=0.",checks);
    }
    static int Main(string[] args) {
        string report=args.Length>5?args[5]:(args.Length==4?args[3]:null);
        try {
            if(args.Length==4 && args[0]=="reuse") { ReuseGuard(args[1],args[2],args[3]);return 0; }
            if(args.Length==4 && args[0]=="schema") { Schema(args[1],args[2],args[3]);return 0; }
            if(args.Length==4 && args[0]=="metadata") { Metadata(args[1],args[2],args[3]);return 0; }
            if(args.Length!=6 && args.Length!=7 && args.Length!=8)throw new Exception("Usage: replay|isolated plugin.dll traceDir mod.bin probe.xml result.json [platform=0] [profile=MOD-name|FullMymod]");
            bool isolated=args[0]=="isolated";
            if(!isolated && args[0]!="replay")throw new Exception("Unknown replay mode");
            int platform=args.Length>=7?Int32.Parse(args[6],CultureInfo.InvariantCulture):0;
            if(platform!=0 && platform!=1 && platform!=2 && platform!=99)throw new Exception("Unsupported platform selector");
            string profile=args.Length==8?args[7]:"Reduced";
            if(String.IsNullOrWhiteSpace(profile))throw new Exception("SDK profile name is empty");
            string dll=Path.GetFullPath(args[1]),trace=Path.GetFullPath(args[2]);
            string manifest=Path.Combine(trace,"calls.tsv");
            ValidateProbe(args[4]);
            Environment.SetEnvironmentVariable("RA3_RECOVERED_XML_PRIORITY_TRACE",null);
            if(isolated) {
                NoOriginalFiles(Path.GetDirectoryName(dll));
                NoOriginalFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                NoOriginalAssemblies();
            }
            string pluginName=isolated?"Recovered.XmlPriority.Plugin":"BinaryAssetBuilder.XmlCompiler.Plugin";
            Assembly library=Assembly.LoadFrom(dll);
            Type pluginType=library.GetType(pluginName,true);
            var plugin=(IAssetBuilderPlugin)Activator.CreateInstance(pluginType);
            plugin.Initialize(null,(TargetPlatform)platform);
            if(isolated)NoOriginalAssemblies();
            byte[] mod=File.ReadAllBytes(args[3]);
            if(mod.Length==0)throw new Exception("Final mod.bin is empty");
            var stems=new HashSet<string>(StringComparer.Ordinal);
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var typeCalls=new Dictionary<string,int>(StringComparer.Ordinal);
            var idCalls=new Dictionary<string,int>(StringComparer.Ordinal);
            int calls=0,comparisons=0;
            foreach(string row in File.ReadAllLines(manifest)) {
                if(String.IsNullOrWhiteSpace(row))throw new Exception("Empty SDK trace manifest row");
                string[] fields=row.Split('\t');
                if(fields.Length!=4)throw new Exception("Malformed SDK trace manifest row");
                string type=fields[0],id=fields[1],stem=fields[2],expectedType;
                TypeId(type); // Reject every type outside the registered recovered priority types.
                if(String.IsNullOrEmpty(id))throw new Exception("SDK trace asset id is empty");
                if(Required.TryGetValue(id,out expectedType)) {
                    if(type!=expectedType)throw new Exception("Wrong SDK probe type: "+type+"/"+id);
                } else if(profile!="FullMymod")throw new Exception("Unexpected SDK profile call: "+type+"/"+id);
                int recordedPlatform;
                if(!Int32.TryParse(fields[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out recordedPlatform) || recordedPlatform!=platform)throw new Exception("SDK trace platform mismatch");
                if(String.IsNullOrEmpty(stem) || stem!=Path.GetFileName(stem) || stem.IndexOfAny(new[]{'/','\\',':'})>=0 || !stems.Add(stem))
                    throw new Exception("Invalid or duplicated SDK trace stem");
                string xmlPath=Path.Combine(trace,stem+".xml");
                var xml=new XmlDocument();xml.Load(xmlPath);
                if(xml.DocumentElement.LocalName!=type || xml.DocumentElement.GetAttribute("id")!=id)throw new Exception("Trace XML identity differs from manifest");
                CheckPreprocessing(xml,id,type);
                var data=plugin.ProcessInstance(Declaration(xml));
                byte[] instance=File.ReadAllBytes(Path.Combine(trace,stem+".bin"));
                Equal(data.InstanceData,instance,id+"/instance");
                Equal(data.RelocationData,File.ReadAllBytes(Path.Combine(trace,stem+".relo")),id+"/relocations");
                Equal(data.ImportsData,File.ReadAllBytes(Path.Combine(trace,stem+".imp")),id+"/imports");
                comparisons+=3;
                if(!Contains(mod,instance))throw new Exception(id+" instance payload not found in final SDK mod.bin");
                if(Required.ContainsKey(id))seen.Add(id);++calls;
                if(!typeCalls.ContainsKey(type))typeCalls[type]=0;++typeCalls[type];
                if(!idCalls.ContainsKey(id))idCalls[id]=0;++idCalls[id];
                Console.WriteLine("PASS {0} SDK replay {1}/{2}, bytes={3}",isolated?"isolated recovered":"original",type,id,instance.Length);
                if(isolated)NoOriginalAssemblies();
            }
            if(calls<Required.Count || seen.Count!=Required.Count)throw new Exception("Incomplete SDK plugin asset call coverage");
            foreach(string requiredType in RequiredTypes)if(!typeCalls.ContainsKey(requiredType))throw new Exception("Missing required SDK root type: "+requiredType);
            if(profile!="FullMymod" && typeCalls.Count!=RequiredTypes.Count)throw new Exception("Unexpected SDK plugin type coverage");
            foreach(string id in Required.Keys)if(!seen.Contains(id))throw new Exception("No real SDK plugin call for "+id);
            foreach(string extension in new[]{"*.xml","*.bin","*.relo","*.imp"}) {
                string[] files=Directory.GetFiles(trace,extension,SearchOption.TopDirectoryOnly);
                if(files.Length!=calls)throw new Exception("Trace file count differs from manifest for "+extension);
                foreach(string file in files)if(!stems.Contains(Path.GetFileNameWithoutExtension(file)))throw new Exception("Orphan SDK trace file: "+file);
            }
            if(isolated) {
                NoOriginalFiles(Path.GetDirectoryName(dll));
                NoOriginalFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                NoOriginalAssemblies();
            }
            var result=new {
                succeeded=true,mode=args[0],profile=profile,platform=platform,sourceCommit=Environment.GetEnvironmentVariable("GITHUB_SHA"),
                runId=Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),expectedAssets=Required.Count,requiredAssetsVerified=seen.Count,additionalAssets=idCalls.Count-Required.Count,verifiedCalls=calls,
                comparisons=comparisons,typeCalls=typeCalls,idCalls=idCalls,requiredTypeCount=RequiredTypes.Count,supportAssets=SupportAssets,registeredTypes=Supported.Count,
                nonGlobalRegisteredRoots=NonGlobalRoots,nonGlobalRootEntryCoverageClaimed=false,traceManifestSha256=Hash(manifest),
                probeSha256=Hash(args[4]),modSha256=Hash(args[3]),compilerSha256=Hash(dll),
                originalDllFilesAbsent=isolated,originalCompilerAssembliesAbsent=isolated,
                sdkScope="Actual MOD source assets in this profile; only direct in-scope root calls are counted. Non-global nested/runtime root registrations require separate reachability evidence."
            };
            File.WriteAllText(report,new JavaScriptSerializer().Serialize(result));
            Console.WriteLine("PASS {0}: {1} SDK calls, {2} exact buffer comparisons, {3} required assets across {4} root types, isolated={5}",args[0],calls,comparisons,Required.Count,RequiredTypes.Count,isolated);
            return 0;
        } catch(Exception e) {
            Console.Error.WriteLine(e);
            if(report!=null)try { File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{succeeded=false,error=e.ToString()})); }catch(Exception){}
            return 1;
        }
    }
}



