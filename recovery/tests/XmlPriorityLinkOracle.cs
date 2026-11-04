using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Web.Script.Serialization;
using BinaryAssetBuilder.Core;

// Combined assembly smoke. Group differential matrices remain mandatory.
class XmlPriorityLinkOracle
{
    const string AssetNamespace = "uri:ea.com:eala:asset";
    sealed class Root
    {
        public readonly string Name;
        public readonly uint TypeId;
        public readonly string Compiler;
        public Root(string name, uint typeId, bool ai)
        {
            Name = name; TypeId = typeId;
            Compiler = ai ? "Recovered.XmlaiCompiler" : "Recovered." + name + "Compiler";
        }
    }
    static readonly Root[] Roots = new Root[] {
        new Root("AttributeModifier", 3319822471u, false),
        new Root("FXList", 2254974584u, false),
        new Root("SpecialPowerTemplate", 2178440954u, false),
        new Root("ObjectCreationList", 3899542881u, false),
        new Root("LocomotorTemplate", 3972178387u, false),
        new Root("LogicCommandSet", 3959844197u, false),
        new Root("FXParticleSystemTemplate", 686292351u, false),
        new Root("UpgradeTemplate", 3786401627u, false),
        new Root("AIPersonalityDefinition", 3604279694u, true),
        new Root("AIStrategicStateDefinition", 607123156u, true),
        new Root("AIMicroManagerData", 2423780215u, true),
        new Root("AIBudgetStateDefinition", 2893598307u, true),
        new Root("AIStateDefinition", 3239935619u, true),
        new Root("AITargetingHeuristic", 926814458u, true),
        new Root("LogicCommand", 2101756272u, false),
        new Root("PlayerTech", 2131237081u, false),
        new Root("PlayerTechRank", 1802643394u, false),
        new Root("PlayerTechUpgradeBinding", 2140592493u, false),
        new Root("ExperienceLevelTemplate", 1874610847u, false),
        new Root("PlayerTechStoreTemplate", 1213037816u, false),
        new Root("PurchasePlayerTechButtonTemplate", 1566941920u, false),
        new Root("ButtonSingleStateData", 3048039928u, false),
        new Root("UICommandButton", 134745024u, false),
        new Root("FontDefaultSetting", 1371131236u, false),
        new Root("FontTranslation", 1318336226u, false),
        new Root("FontSubstitution", 91719417u, false),
        new Root("Weather", 2430161325u, false),
        new Root("WaterTransparency", 1479531562u, false),
        new Root("WaterSetting", 2728605516u, false),
        new Root("Environment", 680780553u, false),
        new Root("LightingSettings", 3169690211u, false),
        new Root("WaterTextureList", 2995571269u, false),
        new Root("TerrainParticleMapping", 1469498551u, false),
        new Root("BounceKickTerrainMapping", 2368878637u, false),
        new Root("ShadowMap", 4157475773u, false),
        new Root("OrdersManagerSettings", 2996766068u, false),
        new Root("TerrainVisualSettings", 1184276792u, false),
        new Root("CameraSettings", 3898802054u, false),
        new Root("AIData", 3827723072u, false),
        new Root("UnitAISettings", 2964891936u, false),
        new Root("PlayerTemplate", 4063352333u, false),
        new Root("MultiplayerSettings", 191082224u, false),
        new Root("AIBuildStateDefinition", 1644972043u, false),
        new Root("AIStrategicStateName", 2010562192u, false),
        new Root("MultiplayerColor", 2384988189u, false),
        new Root("AIPersonalityToAchievementEventMapping", 4086175864u, false),
        new Root("FormationAssistantUnitDefinition", 838130914u, false),
        new Root("FormationAssistantTemplate", 2912385462u, false),
        new Root("FormationSelectionTemplate", 3374519264u, false),
        new Root("FormationAssistant", 1963196508u, false),
        new Root("GameStatsModeData", 3249689110u, false),
        new Root("SkirmishSettings", 402082255u, false),
        new Root("DamageFX", 2905958645u, false),
        new Root("DamageFXSettings", 75989905u, false),
        new Root("GeneralHotKeySlots", 417176643u, false),
        new Root("DefaultHotKeys", 3741098742u, false),
        new Root("DebugHotKeys", 3359870461u, false),
        new Root("MappableKey", 1350608344u, false),
        new Root("AudioEventLimitGroup", 3906227957u, false),
        new Root("AudioLOD", 299416263u, false),
        new Root("AudioSettings", 1443425905u, false),
        new Root("Video", 587347702u, false),
        new Root("VideoEventList", 1447728787u, false),
        new Root("MicrophoneSettings", 1826140078u, false),
        new Root("BootupDisplaySequence", 3370391592u, false),
        new Root("HotKeySlot", 2800139175u, false),
        new Root("MouseCursor", 3784442503u, false),
        new Root("BasicMouseCursors", 611271731u, false),
        new Root("UIMouseDragSelectionFeedback", 611126373u, false),
        new Root("UIFloatingTextDisplay", 1559937240u, false),
        new Root("UIMessageDisplay", 1697313181u, false),
        new Root("UIMilitaryCaptionDisplay", 1043989897u, false),
        new Root("UISubtitleDisplay", 165611890u, false),
        new Root("UITimerDisplay", 951036436u, false),
        new Root("DynamicGameLOD", 2901356964u, false),
        new Root("GameLODPreset", 354222972u, false),
        new Root("StaticGameLOD", 531798225u, false),
        new Root("UICoopTinting", 4243804314u, false),
        new Root("RadiusCursor", 544776545u, false),
        new Root("AnimationSoundSettings", 1302035577u, false),
        new Root("MiscAudio", 2070603733u, false),
        new Root("LargeGroupAudioMap", 534008255u, false),
        new Root("UIMouseTacticalSelectionDetailsButtonHotKeys", 1296329066u, false),
        new Root("UIMouseTacticalMainCommandBarHotKeys", 2591692061u, false),
        new Root("UIMouseTacticalPlayerPowerHotKeys", 2283998413u, false),
        new Root("UIMouseTacticalMiscHotKeys", 2014061824u, false),
        new Root("UIMouseHUDElementHotKeyGate", 2738273474u, false),
        new Root("UIQuickChatMessage", 4098431913u, false),
        new Root("UIMouseTacticalQuickChatDisplay", 2891878770u, false),
        new Root("UIMouseVoiceChat", 3746130610u, false),
        new Root("UIDecalGrid", 2599418799u, false),
        new Root("UIDecalCloud", 2524893660u, false),
        new Root("UIDecalDestinationMarker", 1532214660u, false),
        new Root("UIWorldBuilderDecalGrid", 1238702511u, false),
        new Root("BaseAudioEventInfo", 1079232276u, false),
        new Root("BaseSingleSound", 3228425025u, false),
        new Root("AudioEvent", 2219670431u, false),
        new Root("MusicTrack", 1883691512u, false),
        new Root("MP3MusicTrack", 1013434420u, false),
        new Root("DialogEvent", 3558134211u, false),
        new Root("MP3DialogEvent", 1479988269u, false),
        new Root("AmbientStream", 3928762264u, false),
        new Root("MP3AmbientStream", 1813598662u, false),
        new Root("Multisound", 2745675575u, false),
        new Root("PathMusicGameDynamicState", 97402485u, false),
        new Root("PathMusicGameDynamicStateSet", 343094706u, false),
        new Root("PathMusicGameDynamicTransition", 2343360052u, false),
        new Root("PathMusicEventRuntime", 1122327197u, false),
        new Root("PathMusicEventSet", 3339496789u, false),
        new Root("PathMusicMapRuntime", 2026048286u, false),
        new Root("MusicPalette", 2407383451u, false),
        new Root("MusicScriptConditionNuggetBase", 2292378780u, false),
        new Root("MusicScriptConditionExpensiveNuggetBase", 849624115u, false),
        new Root("MusicScriptConditionNugget_LocalPlayerIsObserver", 188286714u, false),
        new Root("MusicScriptConditionNugget_UnitsFarFromBase", 2620791304u, false),
        new Root("MusicScriptConditionNugget_TimeFromStartOfLevel", 1464425282u, false),
        new Root("MusicScriptConditionNugget_TrackPlayedCount", 57370491u, false),
        new Root("MusicScriptConditionNugget_SpecificTrackTypePlaying", 739280456u, false),
        new Root("MusicScriptConditionNugget_AnyTrackPlaying", 3402992833u, false),
        new Root("MusicScriptConditionNugget_ObjectsOfTypeExist", 4286308287u, false),
        new Root("MusicScriptConditionNugget_EvaEventPlayedRecently", 2743973749u, false),
        new Root("MusicScriptConditionNugget_ObjectsNearEvaEvent", 3386715757u, false),
        new Root("MusicScriptConditionNugget_ScoredKillCount", 3620764004u, false),
        new Root("MusicScriptConditionNugget_Not", 4007043648u, false),
        new Root("MusicScriptConditionNugget_Or", 2281773838u, false),
        new Root("MusicScriptConditionNugget_And", 1343682755u, false),
        new Root("MusicScriptConditionNugget_PathMusicStateLastActive", 2912435838u, false),
        new Root("MusicScriptConditionNugget_PathMusicStateCurrentlyActive", 3727134806u, false),
        new Root("MusicScriptConditionNugget_EngagementCount", 3476397162u, false),
        new Root("MusicScriptConditionNugget_LocalPlayerKillRatio", 2818967523u, false),
        new Root("MusicScriptConditionNugget_HitBySuperWeapon", 1634186921u, false),
        new Root("MusicScriptTrack", 1038067901u, false),
        new Root("UIMouseTacticalCoOpAIButtonsHelpButton", 104126924u, false),
        new Root("UIMouseTacticalCommandCommunicateWithAllies", 113029208u, false),
        new Root("UIMouseTacticalToggleRepairModeButton", 346077409u, false),
        new Root("UIMouseTacticalAttackMoveButton", 442957564u, false),
        new Root("UIMouseTacticalCommandPlaceWall", 485977567u, false),
        new Root("UIMouseTacticalForceAttackMoveButton", 694838069u, false),
        new Root("UIMouseTacticalCoOpAIButtonsHoldObjectButton", 772462768u, false),
        new Root("UIMouseTacticalExitContainerButton", 1014034471u, false),
        new Root("UIMouseTacticalPlayerTechStoreButton", 1138847078u, false),
        new Root("UIMouseTacticalCommandMove", 1234485619u, false),
        new Root("UIMouseTacticalCommandSell", 1249197161u, false),
        new Root("UIMouseTacticalUnitIntroButton", 1277109486u, false),
        new Root("UIMouseTacticalCommandSelect", 1334543376u, false),
        new Root("UIMouseTacticalCoOpAIButtonsNormalButton", 1371977384u, false),
        new Root("UIMouseTacticalCommandPlaceRallyPoint", 1474300054u, false),
        new Root("UIMouseTacticalCommandHarvest", 1499648070u, false),
        new Root("UIMouseTacticalCommandToggleSelfRepair", 1956336137u, false),
        new Root("UIMouseTacticalBuildQueueTab", 1974922393u, false),
        new Root("UIMouseTacticalCommandGetCrate", 2067763016u, false),
        new Root("UIMouseTacticalDeactivateMissionHotSpotsButton", 2047841880u, false),
        new Root("UIMouseTacticalCommandForceAttack", 2074164365u, false),
        new Root("UIMouseTacticalPlanningModeButton", 2278083954u, false),
        new Root("UIMouseTacticalCommandMisc", 2505151835u, false),
        new Root("UIMouseTacticalSelectionRefinementTab", 2635272840u, false),
        new Root("UIMouseTacticalCommandForceMove", 2697587777u, false),
        new Root("UIMouseTacticalOpenAdvancedCommandsButton", 2808773267u, false),
        new Root("UIMouseTacticalSetStanceButton", 2873949396u, false),
        new Root("UIMouseTacticalCoOpAIButtonsHoldPositionButton", 2914137727u, false),
        new Root("UIMouseTacticalCommandMineOre", 2957071942u, false),
        new Root("UIMouseTacticalWaypointModeButton", 2958905921u, false),
        new Root("UIMouseTacticalReverseMoveButton", 2987234945u, false),
        new Root("UIMouseTacticalCommandEnter", 3057018816u, false),
        new Root("UIMouseTacticalCoOpAIButtonsAssaultButton", 3094553884u, false),
        new Root("UIMouseTacticalPauseButton", 3420020972u, false),
        new Root("UIMouseTacticalCommandAttack", 3548787763u, false),
        new Root("UIMouseTacticalCommandAttackMove", 3575834349u, false),
        new Root("UIMouseTacticalForceMoveButton", 3716369729u, false),
        new Root("UIMouseTacticalForceAttackButton", 3756168709u, false),
        new Root("UIMouseTacticalCommandPlaceAIBeacon", 3809842710u, false),
        new Root("UIMouseTacticalCommandDock", 3976885676u, false),
        new Root("UIMouseTacticalCommandReverseMove", 4026878150u, false),
        new Root("UIMouseTacticalBuildQueueTypeTab", 4094578721u, false),
        new Root("UIMouseTacticalSellModeButton", 4102242385u, false),
        new Root("UIMouseTacticalStopButton", 4140422551u, false),
        new Root("UIMouseTacticalCommandSetRallyPoint", 4180354873u, false),
        new Root("UIMouseTacticalFastForwardButton", 4232364483u, false),
        new Root("UIMouseTacticalGroupSelection", 241329492u, false),
        new Root("UIMouseTacticalLookAt", 1795213437u, false),
        new Root("UIMouseTacticalRadarTelestrator", 3366599054u, false),
        new Root("UIMouseTacticalTelestrator", 1921726501u, false),
        new Root("UIMouseTacticalThreatMeter", 4143989686u, false),
        new Root("BaseConditionData", 3899256902u, false),
        new Root("ObjectConditionData", 519519951u, false),
        new Root("UpgradeConditionData", 76901015u, false),
        new Root("GameplayConditionData", 2309012900u, false),
        new Root("TargetingBaseCompare", 3600209407u, false),
        new Root("TargetingDistanceCompare", 2359666647u, false),
        new Root("TargetingCombatChainCompare", 89767986u, false),
        new Root("TargetingInTurretArcCompare", 2007776008u, false),
        new Root("TargetingCompareList", 3188107749u, false),
        new Root("SiegeEvaEvent", 1268003905u, false),
        new Root("EvaSettings", 2361666546u, false),
        new Root("EvaEvent", 357806494u, false),
        new Root("MiscEvaEvents", 1807540972u, false),
        new Root("AchievementEvent", 4230032093u, false),
        new Root("MapNameToAchievementEventMapping", 3594283852u, false),
        new Root("GameplayAchievementTriggerData", 348670903u, false),
        new Root("HealthBarTemplate", 1507490649u, false),
        new Root("HealthBarSettings", 3919360587u, false),
        new Root("UnitOverlayIconSettings", 2421413379u, false),
        new Root("ObjectFilterAsset", 1151702845u, false),
        new Root("RegisteredObjectSet", 814796096u, false),
        new Root("ObjectVisionRangeItem", 772930644u, false),
        new Root("InvisibilityTemplate", 1431290504u, false),
        new Root("InvisibilityManagerSettings", 254471188u, false),
        new Root("StanceTemplate", 2525284163u, false),
        new Root("EmotionTemplate", 2836737428u, false),
        new Root("ProjectilePath", 1710207315u, false),
        new Root("ForceFeedbackEvent", 474482543u, false),
        new Root("EngagingContent", 3058114834u, false),
        new Root("EngagingContentGroup", 11280974u, false),
        new Root("EngagingContentSequence", 3799634821u, false),
        new Root("IntelDB", 530081230u, false),
        new Root("UnitIntro", 1870180772u, false),
        new Root("BreezeInfo", 1626415288u, false),
        new Root("ModelSettings", 1968811301u, false),
        new Root("AutoParticleSystemSettings", 3278684478u, false),
        new Root("CrowdResponse", 400896388u, false),
        new Root("RadarTerrainImage", 1099769729u, false),
        new Root("UIRadarObjectsLayer", 4039592522u, false),
        new Root("W3DDeviceSettings", 1469055344u, false),
        new Root("ScorchMarkSettings", 2383722691u, false),
        new Root("Language", 2915726794u, false),
        new Root("HeaderTemplate", 1218703046u, false),
        new Root("FontDirectory", 3569007303u, false),
        new Root("TheVersion", 4236434438u, false),
        new Root("Credits", 3978455785u, false),
        new Root("CommandSet", 2584759047u, false),
        new Root("Road", 3516764358u, false),
        new Root("ShaderOverride", 3166846828u, false),
        new Root("ShaderOverrideRule", 3648907125u, false),
        new Root("ImageSequence", 565855655u, false),
        new Root("UnitTypeIcon", 3614134471u, false),
        new Root("DrawGroupInfo", 2913856066u, false),
        new Root("OnlineChatColors", 1345252658u, false),
        new Root("ArchivableMovie", 3741496377u, false),
        new Root("ArchiveMovieList", 2099472558u, false),
        new Root("NetworkSettings", 4272453293u, false),
        new Root("PathfindSettings", 970663416u, false),
        new Root("ResourceSettings", 3279338544u, false),
        new Root("GraphicsHardware", 2846460739u, false),
        new Root("CampaignTemplate", 1482556238u, false),
        new Root("CampaignEnumerator", 731899443u, false),
        new Root("MissionTemplate", 3477794083u, false),
        new Root("MissionHotSpotSettings", 3175516876u, false),
        new Root("TutorialMapData", 3408790761u, false),
        new Root("LocalBuildListMonitor", 315614191u, false),
        new Root("BuildSystemSettings", 3974617950u, false),
        new Root("GiftManagerSettings", 1047799468u, false),
        new Root("MpGameRules", 741706624u, false),
        new Root("DynamicsContactTagMapping", 1044178106u, false),
        new Root("ConnectionLineManager", 105587076u, false),
        new Root("RedAlertTemplate", 4141306833u, false),
        new Root("ConquerCardDefinitionBase", 3258480561u, false),
        new Root("ConquerCardDefinitionUnit", 3562795973u, false),
        new Root("ConquerCardDefinitionStructure", 4289687760u, false),
        new Root("ConquerCardDefinitionPower", 4164089898u, false),
        new Root("ConquerCardPrebuiltDeck", 2806522526u, false),
        new Root("ArmyDefinition", 3587539190u, false),
        new Root("SkirmishOpeningMove", 3971904488u, false),
        new Root("DLContent", 212695140u, false),
        new Root("AssetReportTable", 255713734u, false),
        new Root("StringHashTable", 300273508u, false),
        new Root("MeshInstanceTemplate", 815686049u, false),
        new Root("PackedTextureImage", 1449288224u, false),
        new Root("OnDemandTextureImage", 4262364347u, false),
        new Root("AptAptData", 1816258268u, false),
        new Root("AptConstData", 983359664u, false),
        new Root("AptDatData", 1110676961u, false),
        new Root("AptGeometryData", 1034587168u, false),
        new Root("BaseRenderAssetType", 2989459401u, false),
        new Root("AudioFileRuntime", 584092973u, false),
        new Root("Mouse", 2525492603u, false),
        new Root("UnitAbilityButtonTemplate", 3517887782u, false),
        new Root("PlayerPowerButtonTemplate", 4044322798u, false),
        new Root("TestTexture", 2826773070u, false),
        new Root("TestTextureCollection", 49556361u, false),
        new Root("TerrainAssetCollection", 582251659u, false),
        new Root("TerrainTextureAtlasRuntime", 3878948534u, false),
        new Root("W3DHierarchy", 1641540160u, false),
        new Root("W3DContainer", 4042295058u, false),
        new Root("W3DAnimationRuntime", 3534264507u, false),
        new Root("W3DCollisionBox", 3810008068u, false),
        new Root("MapMetaData", 1603703110u, false),
        new Root("GameScriptList", 3737094024u, false),
        new Root("GameMap", 3575309793u, false),
        new Root("UIConfigList", 3072160547u, false),
        new Root("UIJoypadCommandBarMixedGroup", 1276968974u, false),
        new Root("UIJoypadCommandBarHomogenousGroup", 1546953756u, false),
        new Root("UIJoypadCommandBarCoopCommunicator", 1654297531u, false),
        new Root("JoypadCommandBarButtonTemplate", 1804891371u, false),
        new Root("UIJoypadCommandBarStances", 1887864852u, false),
        new Root("JoypadCommandBarTemplate", 1931769760u, false),
        new Root("UIJoypadCommandBarTopMenu", 2170044043u, false),
        new Root("UIJoypadCommandBarPlayerPowers", 2742786440u, false),
        new Root("UIJoypadCommandBarButtonBuild", 3423839194u, false),
        new Root("UIJoypadCommandBarControlGroups", 3645205636u, false),
        new Root("UIJoypadCommandBarSingleUnit", 3901433122u, false),
    };
    static readonly int[] Platforms = new int[] { 0, 1, 2, 99 };
    static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();
    static readonly Dictionary<string, int> Coverage = new Dictionary<string, int>();
    static int fixtures, comparisons, completionFixtures, completionComparisons;
    static string current = "metadata preflight";

    // Default to the full manual smoke; workflow callers select affected roots explicitly.
    static readonly HashSet<string> SelectedRoots = new HashSet<string>(StringComparer.Ordinal);
    static int ExpectedCompletionPerPlatform(string name)
    {
        switch (name) {
        case "FXParticleSystemTemplate": return 9;
        case "FXList": return 1;
        case "GameplayConditionData": return 1;
        case "TargetingCompareList": return 1;
        case "EvaEvent": return 1;
        case "HealthBarSettings": return 1;
        case "UnitOverlayIconSettings": return 1;
        case "ObjectFilterAsset": return 1;
        case "InvisibilityManagerSettings": return 1;
        case "StanceTemplate": return 1;
        case "ProjectilePath": return 1;
        case "ForceFeedbackEvent": return 1;
        case "EngagingContentGroup": return 1;
        case "AutoParticleSystemSettings": return 1;
        case "CrowdResponse": return 1;
        case "UIRadarObjectsLayer": return 1;
        case "W3DDeviceSettings": return 1;
        case "Credits": return 1;
        case "CommandSet": return 1;
        case "ShaderOverride": return 1;
        case "ImageSequence": return 1;
        case "OnlineChatColors": return 1;
        case "ArchiveMovieList": return 1;
        case "GraphicsHardware": return 1;
        case "TutorialMapData": return 1;
        case "LocalBuildListMonitor": return 1;
        case "DynamicsContactTagMapping": return 1;
        case "ConnectionLineManager": return 1;
        case "RedAlertTemplate": return 1;
        case "ConquerCardPrebuiltDeck": return 1;
        case "ArmyDefinition": return 1;
        case "DLContent": return 1;
        case "MeshInstanceTemplate": return 1;
        case "AptGeometryData": return 1;
        case "AudioFileRuntime": return 1;
        case "Mouse": return 1;
        case "UnitAbilityButtonTemplate": return 1;
        case "TestTextureCollection": return 1;
        case "TerrainTextureAtlasRuntime": return 1;
        case "W3DHierarchy": return 1;
        case "MapMetaData": return 1;
        case "GameScriptList": return 1;
        case "GameMap": return 1;
        case "UIConfigList": return 1;
        default: return 0;
        }
    }
    static int ExpectedCompletionFixtures()
    {
        int count = 0;
        foreach (string name in SelectedRoots) count += ExpectedCompletionPerPlatform(name);
        return count * Platforms.Length;
    }
    static void SelectRoots(string specification)
    {
        IEnumerable<string> requested;
        if (specification == null) {
            List<string> all = new List<string>();
            foreach (Root root in Roots) all.Add(root.Name);
            requested = all;
        } else if (specification.TrimStart().StartsWith("[", StringComparison.Ordinal)) {
            requested = new JavaScriptSerializer().Deserialize<string[]>(specification);
            if (requested == null) throw new Exception("Selected root JSON must be an array");
        } else {
            if (specification.Length == 0) throw new Exception("Use [] for metadata-only validation");
            requested = specification.Split(',');
        }
        foreach (string name in requested) {
            bool known = false;
            foreach (Root root in Roots) if (String.Equals(root.Name, name, StringComparison.Ordinal)) { known = true; break; }
            if (!known || !SelectedRoots.Add(name)) throw new Exception("Unknown or duplicate selected root: " + name);
        }
    }
    static string Json(string value)
    {
        if (value == null) return "null";
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }
    static void WriteResult(string path, bool succeeded, string sourceCommit, string runId, string failure)
    {
        StringBuilder text = new StringBuilder();
        text.Append("{\"succeeded\":").Append(succeeded ? "true" : "false");
        text.Append(",\"sourceCommit\":").Append(Json(sourceCommit)).Append(",\"runId\":").Append(Json(runId));
        text.Append(",\"scope\":\"all-route metadata preflight and selected-root default/completion smoke only\"");
        text.Append(",\"fullMetadataRoutes\":").Append(Methods.Count);
        text.Append(",\"testedTypes\":").Append(SelectedRoots.Count);
        text.Append(",\"testedRootNames\":[");
        bool firstSelected = true;
        foreach (Root route in Roots) if (SelectedRoots.Contains(route.Name)) {
            if (!firstSelected) text.Append(','); firstSelected = false; text.Append(Json(route.Name));
        }
        text.Append(']');
        text.Append(",\"expectedFixtures\":").Append(SelectedRoots.Count * Platforms.Length);
        text.Append(",\"expectedComparisons\":").Append(SelectedRoots.Count * Platforms.Length * 3);
        text.Append(",\"expectedCompletionFixtures\":").Append(ExpectedCompletionFixtures());
        text.Append(",\"expectedCompletionComparisons\":").Append(ExpectedCompletionFixtures() * 3);
        text.Append(",\"types\":").Append(Methods.Count).Append(",\"fixtures\":").Append(fixtures);
        text.Append(",\"comparisons\":").Append(comparisons).Append(",\"platforms\":[0,1,2,99]");
        text.Append(",\"completionFixtures\":").Append(completionFixtures).Append(",\"completionComparisons\":").Append(completionComparisons);
        text.Append(",\"typeCoverage\":{");
        for (int i = 0; i < Roots.Length; ++i) {
            if (i != 0) text.Append(',');
            text.Append(Json(Roots[i].Name)).Append(':').Append(Coverage.ContainsKey(Roots[i].Name) ? Coverage[Roots[i].Name] : 0);
        }
        text.Append("},\"rootTypeIds\":{");
        for (int i = 0; i < Roots.Length; ++i) {
            if (i != 0) text.Append(',');
            text.Append(Json(Roots[i].Name)).Append(':').Append(Roots[i].TypeId.ToString(CultureInfo.InvariantCulture));
        }
        text.Append("},\"compilerClasses\":{");
        for (int i = 0; i < Roots.Length; ++i) {
            if (i != 0) text.Append(',');
            text.Append(Json(Roots[i].Name)).Append(':').Append(Json(Roots[i].Compiler));
        }
        text.Append("},\"failure\":").Append(Json(failure)).Append('}');
        File.WriteAllText(path, text.ToString());
    }
    static void Manifest(string path)
    {
        Dictionary<string, uint> supplied = new Dictionary<string, uint>();
        foreach (string line in File.ReadAllLines(path)) {
            if (line.Trim().Length == 0) continue;
            string[] pair = line.Split('\t');
            if (pair.Length != 2) throw new Exception("Invalid expected-root-types manifest row");
            supplied.Add(pair[0], UInt32.Parse(pair[1], CultureInfo.InvariantCulture));
        }
        if (Roots.Length != 300 || supplied.Count != 300) throw new Exception("Exactly 300 recovered entries are required");
        foreach (Root root in Roots) {
            uint value;
            if (!supplied.TryGetValue(root.Name, out value) || value != root.TypeId)
                throw new Exception("Expected TypeId map mismatch: " + root.Name);
            Coverage.Add(root.Name, 0);
        }
    }
    static void Preflight(Assembly recovered)
    {
        // Force CLR metadata loading for the entire combined image, not only the first route.
        Type[] exported = recovered.GetExportedTypes();
        HashSet<string> publicNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in exported)
            if (!publicNames.Add(type.FullName)) throw new Exception("Duplicate exported type: " + type.FullName);
        foreach (Root root in Roots) {
            Type compiler = recovered.GetType(root.Compiler, false, false);
            if (compiler == null || !compiler.IsVisible)
                throw new Exception("Missing required formal compiler class: " + root.Compiler + " for " + root.Name + "; Draft/#if0 snapshots do not count");
            MethodInfo method = compiler.GetMethod("ProcessInstance", BindingFlags.Public | BindingFlags.Static,
                null, new Type[] { typeof(InstanceDeclaration), typeof(TargetPlatform) }, null);
            if (method == null || method.ContainsGenericParameters || method.ReturnType != typeof(AssetBuffer))
                throw new Exception("Required exact signature absent: AssetBuffer " + root.Compiler + ".ProcessInstance(InstanceDeclaration, TargetPlatform)");
            if (method.DeclaringType != compiler) throw new Exception("Inherited compiler route is not an exact declared interface: " + root.Compiler);
            Methods.Add(root.Name, method);
            Console.WriteLine("ROUTE root={0} TypeId={1} compiler={2}", root.Name, root.TypeId, root.Compiler);
        }
        if (Methods.Count != 300) throw new Exception("Metadata preflight did not resolve all 300 entries");
    }
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager namespaces)
    {
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration declaration = new InstanceDeclaration(document);
        declaration.XmlNode = xml.DocumentElement;
        return declaration;
    }
    static void Equal(string buffer, byte[] original, byte[] recovered, bool completion)
    {
        if (original == null || recovered == null || original.Length != recovered.Length)
            throw new Exception(current + "/" + buffer + " lengths original=" + (original == null ? -1 : original.Length) + " recovered=" + (recovered == null ? -1 : recovered.Length));
        for (int i = 0; i < original.Length; ++i)
            if (original[i] != recovered[i])
                throw new Exception(current + "/" + buffer + " byte=" + i + " original=" + original[i] + " recovered=" + recovered[i]);
        if (completion) ++completionComparisons; else ++comparisons;
    }
    static void Test(IAssetBuilderPlugin original, Root root, int platform, string body = null, string attributes = "")
    {
        if (root == null) throw new Exception("Completion root is missing from the route manifest");
        // The guard covers every default and completion path before any XML/original DLL invocation.
        if (!SelectedRoots.Contains(root.Name)) return;
        bool completion = body != null;
        current = root.Name + (completion ? " completion" : " defaults") + " platform=" + platform;
        XmlDocument xml = new XmlDocument();
        // Unprefixed canonical root is essential: InstanceDeclaration may hash root.Name.
        xml.LoadXml("<" + root.Name + " xmlns='" + AssetNamespace + "' id='PriorityLinkSmoke' " + attributes + ">" + (body ?? "") + "</" + root.Name + ">");
        XmlNamespaceManager namespaces = new XmlNamespaceManager(xml.NameTable);
        namespaces.AddNamespace("ea", AssetNamespace);
        InstanceDeclaration declaration = Declaration(xml, namespaces);
        if (declaration.Handle.TypeId != root.TypeId)
            throw new Exception(current + " canonical declaration Handle.TypeId=" + declaration.Handle.TypeId + " expected=" + root.TypeId);
        original.Initialize(null, (TargetPlatform)platform);
        AssetBuffer expected = original.ProcessInstance(declaration);
        AssetBuffer actual = (AssetBuffer)Methods[root.Name].Invoke(null, new object[] { declaration, (TargetPlatform)platform });
        if (expected == null || actual == null) throw new Exception(current + " null AssetBuffer");
        // Reject an original unknown-TypeId empty fallback masquerading as a successful route.
        if (expected.InstanceData == null || expected.InstanceData.Length == 0)
            throw new Exception(current + " original route returned an empty instance");
        Equal("InstanceData", expected.InstanceData, actual.InstanceData, completion);
        Equal("RelocationData", expected.RelocationData, actual.RelocationData, completion);
        Equal("ImportsData", expected.ImportsData, actual.ImportsData, completion);
        if (completion) ++completionFixtures; else { ++fixtures; ++Coverage[root.Name]; }
        Console.WriteLine("PASS root={0} TypeId={1} compiler={2} platform={3} fixtures=1 comparisons=3 instance={4} relocations={5} imports={6}",
            root.Name, root.TypeId, root.Compiler, platform, actual.InstanceData.Length, actual.RelocationData.Length, actual.ImportsData.Length);
    }
    static void Completion(IAssetBuilderPlugin original, int platform)
    {
        Root particle = Roots[6], effects = Roots[1];
        // All eight newly restored module TypeIds must resolve in the combined image.
        foreach (uint id in new uint[] { 2693838876u, 2933826200u, 234581578u, 2248017887u,
            3036920908u, 3408220251u, 1075723135u, 3465979181u })
            Test(original, particle, platform, "<Physics><Anything TypeId='" + id.ToString(CultureInfo.InvariantCulture) + "'/></Physics>");
        Test(original, effects, platform,
            "<NuggetList><Anything TypeId='99'><SecondaryObjectFilter/><SourceObjectFilter/></Anything>" +
            "<Anything TypeId='503892219' Value='Asset\\17'><SourceObjectFilter/></Anything></NuggetList>");
        Test(original, particle, platform,
            "<Physics><Anything TypeId='1075723135' EventFX='Physics\\2'/></Physics>" +
            "<Draw><Anything TypeId='2248017887'/></Draw>" +
            "<Event><Anything TypeId='2693838876' EventFX='Event\\3'/>" +
            "<Anything TypeId='3465979181'><EventTime Low='1' High='5'/></Anything></Event>" +
            "<Update><Anything TypeId='234581578'/></Update>");
        Test(original, Array.Find(Roots, r => r.Name == "GameplayConditionData"), platform,
            "<PrimaryObject Count='2'><ObjectFilter Include='INFANTRY'><IncludeThing>T</IncludeThing></ObjectFilter></PrimaryObject><RequiredUpgradeCondition><UpgradeRequired>UpgradeA</UpgradeRequired></RequiredUpgradeCondition>");
        Test(original, Array.Find(Roots, r => r.Name == "TargetingCompareList"), platform, "<Compare>T\\7</Compare><Compare>T\\1</Compare>");
        Test(original, Array.Find(Roots, r => r.Name == "EvaEvent"), platform, "<SideSound Sound='S\\3' Side='Allies'/><OtherEvaEventToBlock>Blocked</OtherEvaEventToBlock>");
        Test(original, Array.Find(Roots, r => r.Name == "HealthBarSettings"), platform, "<ObjectMapping HealthBar='H\\2'><ObjectFilter Include='VEHICLE'><IncludeThing>Tank</IncludeThing></ObjectFilter></ObjectMapping>");
        Test(original, Array.Find(Roots, r => r.Name == "UnitOverlayIconSettings"), platform, "<FactionVeterancy Faction='Soviet'><RankIcon Rank='1' Image='I\\9'/></FactionVeterancy>");
        Test(original, Array.Find(Roots, r => r.Name == "ObjectFilterAsset"), platform, "<Filter Include='INFANTRY'><IncludeThing>Unit</IncludeThing></Filter>", "");
        Test(original, Array.Find(Roots, r => r.Name == "InvisibilityManagerSettings"), platform, "<CamouflageDetectorObjectFilter Include='VEHICLE'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "StanceTemplate"), platform, "<StanceDefinition Type='AGGRESSIVE' AttributeModifier='A\\3'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ProjectilePath"), platform, "<ComponentScale x='1'/><Point x='2' y='3'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ForceFeedbackEvent"), platform, "", "LeftMotorUse='50%' RightMotorUse='25%' Duration='1s' PlayCount='2'");
        Test(original, Array.Find(Roots, r => r.Name == "EngagingContentGroup"), platform, "<EngagingContent>C\\2</EngagingContent>", "");
        Test(original, Array.Find(Roots, r => r.Name == "AutoParticleSystemSettings"), platform, "<Aflame ParticlePrefix='AB' ParticleSystemId='Effect' ParticleMax='17'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "CrowdResponse"), platform, "<Threshold NumUnits='3'><AudioArrayVoice><NamedEntry Name='Command' Sound='S\\2' EvaEvent='E\\1'/></AudioArrayVoice></Threshold>", "");
        Test(original, Array.Find(Roots, r => r.Name == "UIRadarObjectsLayer"), platform, "<SpecialIcon Image='I\\1'><ObjectFilter Include='INFANTRY'/></SpecialIcon>", "");
        Test(original, Array.Find(Roots, r => r.Name == "W3DDeviceSettings"), platform, "<StandardPublicBone>Bone</StandardPublicBone><TreeFadeObjectFilter Include='VEHICLE'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "Credits"), platform, "<Style Name='TITLE'><Color R='1' G='2' B='3'/></Style><CreditItem><Text>Author</Text></CreditItem>", "");
        Test(original, Array.Find(Roots, r => r.Name == "CommandSet"), platform, "<CommandButton Name='Command' Index='2'><InitialDelay Low='1' High='3'/></CommandButton>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ShaderOverride"), platform, "<Rule IfOriginalShaderIs='Original' ReplaceShaderName='Replacement' ReplaceTechniqueName='Technique'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ImageSequence"), platform, "<Image>I\\2</Image><Image>I\\1</Image>", "");
        Test(original, Array.Find(Roots, r => r.Name == "OnlineChatColors"), platform, "<Default R='1' G='2' B='3'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ArchiveMovieList"), platform, "<Movie ProgressLock='2' MovieImage='I\\1' FileName='intro.bik' OnScreenName='Intro' Description='Description'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "GraphicsHardware"), platform, "<Device VendorId='17' DeviceId='2' Name='GPU' Level='HIGH' MacLevel='LOW'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "TutorialMapData"), platform, "<Lesson Map='Map' Summary='Summary' ImageName='Image'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "LocalBuildListMonitor"), platform, "<ProducerObjectFilter Include='VEHICLE'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "DynamicsContactTagMapping"), platform, "<Mapping ContactA='NORMAL' ContactB='TERRAIN' FX='FX\\1'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ConnectionLineManager"), platform, "<FXShader ShaderName='Shader' TechniqueName='Technique' TechniqueIndex='1'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "RedAlertTemplate"), platform, "<YellowAreaEffect>FX\\2</YellowAreaEffect>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ConquerCardPrebuiltDeck"), platform, "<Cards><Entry TypeId='849812912' Card='C\\1' Count='2'><Upgrade Upgrade='U\\2'/></Entry></Cards>", "");
        Test(original, Array.Find(Roots, r => r.Name == "ArmyDefinition"), platform, "<TechStructure>Unit</TechStructure>", "Side='Allies' MCVTemplate='Tank'");
        Test(original, Array.Find(Roots, r => r.Name == "DLContent"), platform, "<License LicenseMask='3'><Asset>Unit</Asset></License><Patch Name='Patch' PatchNumber='2'/>", "PackageNumber='1'");
        Test(original, Array.Find(Roots, r => r.Name == "MeshInstanceTemplate"), platform, "<Rule MeshID='Mesh' Instances='3'/>", "");
        Test(original, Array.Find(Roots, r => r.Name == "AptGeometryData"), platform, "", "AptID='17' File='nonexistent-priority-link-fixture.bin'");
        Test(original, Array.Find(Roots, r => r.Name == "AudioFileRuntime"), platform, "ignored-runtime-value", "NumberOfSamples='17'");
        Test(original, Array.Find(Roots, r => r.Name == "Mouse"), platform, "", "DragTolerance='17' DragThreshold='250ms'");
        Test(original, Array.Find(Roots, r => r.Name == "UnitAbilityButtonTemplate"), platform, "<Data><Anything TypeId='988387268'/></Data>", "LogicCommand='L\\2'");
        Test(original, Array.Find(Roots, r => r.Name == "TestTextureCollection"), platform, "<Texture>T\\9</Texture><Texture>T\\1</Texture>", "");
        Test(original, Array.Find(Roots, r => r.Name == "TerrainTextureAtlasRuntime"), platform, "<Tile/> ");
        Test(original, Array.Find(Roots, r => r.Name == "W3DHierarchy"), platform, "<Pivot Name='Bone' Parent='-1'><Translation X='1' Y='2' Z='3'/></Pivot>");
        Test(original, Array.Find(Roots, r => r.Name == "MapMetaData"), platform, "<MapMetaData/>");
        Test(original, Array.Find(Roots, r => r.Name == "GameScriptList"), platform, "<ScriptSet/>");
        Test(original, Array.Find(Roots, r => r.Name == "GameMap"), platform, "<Unit Faction='Allies' ThingTemplate='Unit\\2'><Position x='1' y='2'/></Unit><WorldDict><AssetIdListProperty Key='Assets' Value='one two'/><BoolProperty Value='true'/></WorldDict><EnvironmentData Cloud='Cloud\\3'/><MapAnalysis WaterAreas='1'><WaterAreaType>INTERIOR</WaterAreaType></MapAnalysis>");
        Test(original, Array.Find(Roots, r => r.Name == "UIConfigList"), platform, "<UIDynamicConfig Name='MixedCase' Type='SHELL'><ComponentList><Anything TypeId='133731258' MovieComponentName='intro' MaxNumberOfStream='7'/><Anything TypeId='1498808370'><DefaultBrightness>0.5</DefaultBrightness></Anything></ComponentList></UIDynamicConfig>");
    }
    static int Main(string[] args)
    {
        bool succeeded = false;
        string failure = null;
        try {
            if (args.Length != 6 && args.Length != 7) throw new Exception("Usage: XmlPriorityLinkOracle original.dll recovered.dll result.json expected.tsv sourceCommit runId [selected-roots CSV/JSON]");
            if (IntPtr.Size != 4) throw new Exception("Priority link oracle requires an x86 process");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Manifest(args[3]);
            SelectRoots(args.Length == 7 ? args[6] : null);
            Assembly recovered = Assembly.LoadFrom(args[1]);
            Preflight(recovered);
            IAssetBuilderPlugin original = null;
            if (SelectedRoots.Count != 0) {
                Assembly source = Assembly.LoadFrom(args[0]);
                Type pluginType = source.GetType("BinaryAssetBuilder.XmlCompiler.Plugin", true, false);
                original = (IAssetBuilderPlugin)Activator.CreateInstance(pluginType);
            }
            foreach (int platform in Platforms) {
                int beforeFixtures = fixtures, beforeComparisons = comparisons;
                foreach (Root root in Roots) Test(original, root, platform);
                if (SelectedRoots.Count != 0) Completion(original, platform);
                Console.WriteLine("PASS platform={0} metadataRoutes=300 testedTypes={1} fixtures={2} comparisons={3}", platform, SelectedRoots.Count, fixtures-beforeFixtures, comparisons-beforeComparisons);
            }
            int expectedFixtures = SelectedRoots.Count * Platforms.Length;
            int expectedCompletion = ExpectedCompletionFixtures();
            if (fixtures != expectedFixtures || comparisons != expectedFixtures * 3 || completionFixtures != expectedCompletion || completionComparisons != expectedCompletion * 3 || Methods.Count != 300) throw new Exception("Incomplete selected-root priority smoke matrix");
            foreach (Root root in Roots) if (Coverage[root.Name] != (SelectedRoots.Contains(root.Name) ? 4 : 0)) throw new Exception("Incorrect selected/unselected root coverage: " + root.Name);
            succeeded = true;
            Console.WriteLine("PASS XmlPriorityLink metadataRoutes=300 testedTypes={0} fixtures={1} comparisons={2} completionFixtures={3} completionComparisons={4} platforms=0/1/2/99; selected-root link smoke only", SelectedRoots.Count, fixtures, comparisons, completionFixtures, completionComparisons);
            return 0;
        } catch (Exception error) {
            failure = current + ": " + error.ToString();
            Console.Error.WriteLine("FAIL " + failure);
            ReflectionTypeLoadException metadata = error as ReflectionTypeLoadException;
            if (metadata != null) foreach (Exception loader in metadata.LoaderExceptions) Console.Error.WriteLine(loader);
            TargetInvocationException invocation = error as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null) Console.Error.WriteLine(invocation.InnerException);
            return 1;
        } finally {
            if (args.Length >= 3) WriteResult(args[2], succeeded, args.Length > 4 ? args[4] : "", args.Length > 5 ? args[5] : "", failure);
        }
    }
}



