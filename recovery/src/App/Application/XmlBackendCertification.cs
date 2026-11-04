#if NEWSdk_MONOLITHIC
using System.Collections.Generic;

namespace NewModCompiler.Application
{
    // Closed, module-wide source certification at c807fd1b. See the full
    // XML_BACKEND_REENTRY.json audit, including the later Joypad source audit.
    // New route IDs are owner-only until
    // their module read/write and allocation contracts are explicitly reviewed.
    // This registry is not generated from live routes at runtime. APT leases
    // remain separate; TestTexture external reads are deliberately excluded.
    internal static class XmlBackendCertification
    {
        static readonly Dictionary<uint,string> families = Build();
        public static int TypeCount { get { return families.Count; } }
        public static bool IsCertified(uint type) { return families.ContainsKey(type); }
        public static string FamilyFor(uint type) { string family; return families.TryGetValue(type, out family) ? family : null; }
        static Dictionary<uint,string> Build()
        {
            var result = new Dictionary<uint,string>();
            // UnitAbilityButtonTemplate, PlayerPowerButtonTemplate
            Add(result, "Xmlabilitybuttons", 3517887782u, 4044322798u);
            // AIPersonalityDefinition, AIStrategicStateDefinition, AIMicroManagerData, AIBudgetStateDefinition, AIStateDefinition, AITargetingHeuristic
            Add(result, "Xmlai", 3604279694u, 607123156u, 2423780215u, 2893598307u, 3239935619u, 926814458u);
            // ArmyDefinition, SkirmishOpeningMove
            Add(result, "Xmlarmyopening", 3587539190u, 3971904488u);
            // DLContent, AssetReportTable, StringHashTable
            Add(result, "Xmlassetindexes", 212695140u, 255713734u, 300273508u);
            // LogicCommandSet, AttributeModifier, UpgradeTemplate
            Add(result, "Xmlattributes", 3959844197u, 3319822471u, 3786401627u);
            // AudioEventLimitGroup, AudioLOD, AudioSettings, Video, VideoEventList, MicrophoneSettings, BootupDisplaySequence
            Add(result, "Xmlaudio", 3906227957u, 299416263u, 1443425905u, 587347702u, 1447728787u, 1826140078u, 3370391592u);
            // LocalBuildListMonitor, BuildSystemSettings, GiftManagerSettings, MpGameRules
            Add(result, "Xmlbuildeconomy", 315614191u, 3974617950u, 1047799468u, 741706624u);
            // CampaignTemplate, CampaignEnumerator, MissionTemplate, MissionHotSpotSettings, TutorialMapData
            Add(result, "Xmlcampaign", 1482556238u, 731899443u, 3477794083u, 3175516876u, 3408790761u);
            // OnlineChatColors
            Add(result, "Xmlchatcolors", 1345252658u);
            // CommandSet
            Add(result, "Xmlcommandset", 2584759047u);
            // BaseConditionData, ObjectConditionData, UpgradeConditionData, GameplayConditionData
            Add(result, "Xmlconditions", 3899256902u, 519519951u, 76901015u, 2309012900u);
            // ConnectionLineManager
            Add(result, "Xmlconnections", 105587076u);
            // ConquerCardDefinitionBase, ConquerCardDefinitionUnit, ConquerCardDefinitionStructure, ConquerCardDefinitionPower, ConquerCardPrebuiltDeck
            Add(result, "Xmlconquercards", 3258480561u, 3562795973u, 4289687760u, 4164089898u, 2806522526u);
            // EngagingContent, EngagingContentGroup, EngagingContentSequence, IntelDB, UnitIntro
            Add(result, "Xmlcontent", 3058114834u, 11280974u, 3799634821u, 530081230u, 1870180772u);
            // CrowdResponse
            Add(result, "Xmlcrowd", 400896388u);
            // DamageFX, DamageFXSettings
            Add(result, "Xmldamagefx", 2905958645u, 75989905u);
            // DynamicsContactTagMapping
            Add(result, "Xmldynamics", 1044178106u);
            // FXList
            Add(result, "Xmleffects", 2254974584u);
            // StanceTemplate, EmotionTemplate
            Add(result, "Xmlemotions", 2525284163u, 2836737428u);
            // Weather, WaterTransparency, WaterSetting, Environment, LightingSettings, WaterTextureList, TerrainParticleMapping, BounceKickTerrainMapping, ShadowMap, OrdersManagerSettings, TerrainVisualSettings, CameraSettings
            Add(result, "Xmlenvironment", 2430161325u, 1479531562u, 2728605516u, 680780553u, 3169690211u, 2995571269u, 1469498551u, 2368878637u, 4157475773u, 2996766068u, 1184276792u, 3898802054u);
            // SiegeEvaEvent, EvaSettings, EvaEvent, MiscEvaEvents, AchievementEvent, MapNameToAchievementEventMapping, GameplayAchievementTriggerData
            Add(result, "Xmlevaachievement", 1268003905u, 2361666546u, 357806494u, 1807540972u, 4230032093u, 3594283852u, 348670903u);
            // ForceFeedbackEvent
            Add(result, "Xmlforcefeedback", 474482543u);
            // FXParticleSystemTemplate
            Add(result, "Xmlfxparticle", 686292351u);
            // GameMap
            Add(result, "Xmlgamemap", 3575309793u);
            // GameScriptList
            Add(result, "Xmlgamescripts", 3737094024u);
            // HealthBarTemplate, HealthBarSettings
            Add(result, "Xmlhealthbars", 1507490649u, 3919360587u);
            // GeneralHotKeySlots, DefaultHotKeys, DebugHotKeys, MappableKey
            Add(result, "Xmlhotkeymaps", 417176643u, 3741098742u, 3359870461u, 1350608344u);
            // The eleven Joypad roots use immutable layouts and per-call bridge,
            // Builder/Tracker, endian-word and ObjectFilter storage; no file reads.
            Add(result, "Xmljoypad", 1276968974u, 1546953756u, 1654297531u, 1804891371u, 1887864852u, 1931769760u, 2170044043u, 2742786440u, 3423839194u, 3645205636u, 3901433122u);
            // Language, HeaderTemplate, FontDirectory, TheVersion, Credits
            Add(result, "Xmllocalization", 2915726794u, 1218703046u, 3569007303u, 4236434438u, 3978455785u);
            // LocomotorTemplate
            Add(result, "Xmllocomotor", 3972178387u);
            // MapMetaData
            Add(result, "Xmlmapmetadata", 1603703110u);
            // Road, ShaderOverride, ShaderOverrideRule
            Add(result, "Xmlmaterialrules", 3516764358u, 3166846828u, 3648907125u);
            // Mouse
            Add(result, "Xmlmouse", 2525492603u);
            // ArchivableMovie, ArchiveMovieList
            Add(result, "Xmlmoviearchive", 3741496377u, 2099472558u);
            // MusicScriptConditionNuggetBase, MusicScriptConditionExpensiveNuggetBase, MusicScriptConditionNugget_LocalPlayerIsObserver, MusicScriptConditionNugget_UnitsFarFromBase, MusicScriptConditionNugget_TimeFromStartOfLevel, MusicScriptConditionNugget_TrackPlayedCount, MusicScriptConditionNugget_SpecificTrackTypePlaying, MusicScriptConditionNugget_AnyTrackPlaying, MusicScriptConditionNugget_ObjectsOfTypeExist, MusicScriptConditionNugget_EvaEventPlayedRecently, MusicScriptConditionNugget_ObjectsNearEvaEvent, MusicScriptConditionNugget_ScoredKillCount, MusicScriptConditionNugget_Not, MusicScriptConditionNugget_Or, MusicScriptConditionNugget_And, MusicScriptConditionNugget_PathMusicStateLastActive, MusicScriptConditionNugget_PathMusicStateCurrentlyActive, MusicScriptConditionNugget_EngagementCount, MusicScriptConditionNugget_LocalPlayerKillRatio, MusicScriptConditionNugget_HitBySuperWeapon, MusicScriptTrack
            Add(result, "Xmlmusicscript", 2292378780u, 849624115u, 188286714u, 2620791304u, 1464425282u, 57370491u, 739280456u, 3402992833u, 4286308287u, 2743973749u, 3386715757u, 3620764004u, 4007043648u, 2281773838u, 1343682755u, 2912435838u, 3727134806u, 3476397162u, 2818967523u, 1634186921u, 1038067901u);
            // ObjectFilterAsset, RegisteredObjectSet, ObjectVisionRangeItem
            Add(result, "Xmlobjectsets", 1151702845u, 814796096u, 772930644u);
            // PathMusicGameDynamicState, PathMusicGameDynamicStateSet, PathMusicGameDynamicTransition, PathMusicEventRuntime, PathMusicEventSet, PathMusicMapRuntime, MusicPalette
            Add(result, "Xmlpathmusic", 97402485u, 343094706u, 2343360052u, 1122327197u, 3339496789u, 2026048286u, 2407383451u);
            // AIData, UnitAISettings, PlayerTemplate, MultiplayerSettings, AIBuildStateDefinition, AIStrategicStateName, MultiplayerColor, AIPersonalityToAchievementEventMapping, FormationAssistantUnitDefinition, FormationAssistantTemplate, FormationSelectionTemplate, FormationAssistant, GameStatsModeData, SkirmishSettings
            Add(result, "Xmlplayers", 3827723072u, 2964891936u, 4063352333u, 191082224u, 1644972043u, 2010562192u, 2384988189u, 4086175864u, 838130914u, 2912385462u, 3374519264u, 1963196508u, 3249689110u, 402082255u);
            // SpecialPowerTemplate, ObjectCreationList
            Add(result, "Xmlpowers", 2178440954u, 3899542881u);
            // LogicCommand, PlayerTech, PlayerTechRank, PlayerTechUpgradeBinding, ExperienceLevelTemplate, PlayerTechStoreTemplate, PurchasePlayerTechButtonTemplate, ButtonSingleStateData, UICommandButton, FontSubstitution, FontTranslation, FontDefaultSetting
            Add(result, "Xmlprogression", 2101756272u, 2131237081u, 1802643394u, 2140592493u, 1874610847u, 1213037816u, 1566941920u, 3048039928u, 134745024u, 91719417u, 1318336226u, 1371131236u);
            // ProjectilePath
            Add(result, "Xmlprojectilepath", 1710207315u);
            // RadarTerrainImage, UIRadarObjectsLayer
            Add(result, "Xmlradar", 1099769729u, 4039592522u);
            // RedAlertTemplate
            Add(result, "Xmlredalert", 4141306833u);
            // MeshInstanceTemplate, PackedTextureImage, OnDemandTextureImage
            Add(result, "Xmlrenderassets", 815686049u, 1449288224u, 4262364347u);
            // W3DDeviceSettings, ScorchMarkSettings
            Add(result, "Xmlrenderdevice", 1469055344u, 2383722691u);
            // BaseRenderAssetType, AudioFileRuntime
            Add(result, "Xmlruntimeopaque", 2989459401u, 584092973u);
            // BaseAudioEventInfo, BaseSingleSound, AudioEvent, MusicTrack, MP3MusicTrack, DialogEvent, MP3DialogEvent, AmbientStream, MP3AmbientStream, Multisound
            Add(result, "Xmlsinglesound", 1079232276u, 3228425025u, 2219670431u, 1883691512u, 1013434420u, 3558134211u, 1479988269u, 3928762264u, 1813598662u, 2745675575u);
            // AnimationSoundSettings, MiscAudio, LargeGroupAudioMap
            Add(result, "Xmlsoundmaps", 1302035577u, 2070603733u, 534008255u);
            // NetworkSettings, PathfindSettings, ResourceSettings, GraphicsHardware
            Add(result, "Xmlsystemsettings", 4272453293u, 970663416u, 3279338544u, 2846460739u);
            // UIMouseTacticalSelectionDetailsButtonHotKeys, UIMouseTacticalMainCommandBarHotKeys, UIMouseTacticalPlayerPowerHotKeys, UIMouseTacticalMiscHotKeys, UIMouseHUDElementHotKeyGate
            Add(result, "Xmltacticalhotkeys", 1296329066u, 2591692061u, 2283998413u, 2014061824u, 2738273474u);
            // TargetingBaseCompare, TargetingDistanceCompare, TargetingCombatChainCompare, TargetingInTurretArcCompare, TargetingCompareList
            Add(result, "Xmltargeting", 3600209407u, 2359666647u, 89767986u, 2007776008u, 3188107749u);
            // TerrainAssetCollection, TerrainTextureAtlasRuntime
            Add(result, "Xmlterrain", 582251659u, 3878948534u);
            // TestTextureCollection
            Add(result, "Xmltesttextures", 49556361u);
            // UIQuickChatMessage, UIMouseTacticalQuickChatDisplay, UIMouseVoiceChat
            Add(result, "Xmluichat", 4098431913u, 2891878770u, 3746130610u);
            // UIConfigList
            Add(result, "Xmluiconfig", 3072160547u);
            // UIDecalGrid, UIDecalCloud, UIDecalDestinationMarker, UIWorldBuilderDecalGrid
            Add(result, "Xmluidecals", 2599418799u, 2524893660u, 1532214660u, 1238702511u);
            // ImageSequence, UnitTypeIcon, DrawGroupInfo
            Add(result, "Xmluiimages", 565855655u, 3614134471u, 2913856066u);
            // HotKeySlot, MouseCursor, BasicMouseCursors, UIMouseDragSelectionFeedback, UIFloatingTextDisplay, UIMessageDisplay, UIMilitaryCaptionDisplay, UISubtitleDisplay, UITimerDisplay, DynamicGameLOD, GameLODPreset, StaticGameLOD, UICoopTinting, RadiusCursor
            Add(result, "Xmluisettings", 2800139175u, 3784442503u, 611271731u, 611126373u, 1559937240u, 1697313181u, 1043989897u, 165611890u, 951036436u, 2901356964u, 354222972u, 531798225u, 4243804314u, 544776545u);
            // UIMouseTacticalCoOpAIButtonsHelpButton, UIMouseTacticalCommandCommunicateWithAllies, UIMouseTacticalToggleRepairModeButton, UIMouseTacticalAttackMoveButton, UIMouseTacticalCommandPlaceWall, UIMouseTacticalForceAttackMoveButton, UIMouseTacticalCoOpAIButtonsHoldObjectButton, UIMouseTacticalExitContainerButton, UIMouseTacticalPlayerTechStoreButton, UIMouseTacticalCommandMove, UIMouseTacticalCommandSell, UIMouseTacticalUnitIntroButton, UIMouseTacticalCommandSelect, UIMouseTacticalCoOpAIButtonsNormalButton, UIMouseTacticalCommandPlaceRallyPoint, UIMouseTacticalCommandHarvest, UIMouseTacticalCommandToggleSelfRepair, UIMouseTacticalBuildQueueTab, UIMouseTacticalCommandGetCrate, UIMouseTacticalDeactivateMissionHotSpotsButton, UIMouseTacticalCommandForceAttack, UIMouseTacticalPlanningModeButton, UIMouseTacticalCommandMisc, UIMouseTacticalSelectionRefinementTab, UIMouseTacticalCommandForceMove, UIMouseTacticalOpenAdvancedCommandsButton, UIMouseTacticalSetStanceButton, UIMouseTacticalCoOpAIButtonsHoldPositionButton, UIMouseTacticalCommandMineOre, UIMouseTacticalWaypointModeButton, UIMouseTacticalReverseMoveButton, UIMouseTacticalCommandEnter, UIMouseTacticalCoOpAIButtonsAssaultButton, UIMouseTacticalPauseButton, UIMouseTacticalCommandAttack, UIMouseTacticalCommandAttackMove, UIMouseTacticalForceMoveButton, UIMouseTacticalForceAttackButton, UIMouseTacticalCommandPlaceAIBeacon, UIMouseTacticalCommandDock, UIMouseTacticalCommandReverseMove, UIMouseTacticalBuildQueueTypeTab, UIMouseTacticalSellModeButton, UIMouseTacticalStopButton, UIMouseTacticalCommandSetRallyPoint, UIMouseTacticalFastForwardButton, UIMouseTacticalGroupSelection, UIMouseTacticalLookAt, UIMouseTacticalRadarTelestrator, UIMouseTacticalTelestrator, UIMouseTacticalThreatMeter
            Add(result, "Xmluitactical", 104126924u, 113029208u, 346077409u, 442957564u, 485977567u, 694838069u, 772462768u, 1014034471u, 1138847078u, 1234485619u, 1249197161u, 1277109486u, 1334543376u, 1371977384u, 1474300054u, 1499648070u, 1956336137u, 1974922393u, 2067763016u, 2047841880u, 2074164365u, 2278083954u, 2505151835u, 2635272840u, 2697587777u, 2808773267u, 2873949396u, 2914137727u, 2957071942u, 2958905921u, 2987234945u, 3057018816u, 3094553884u, 3420020972u, 3548787763u, 3575834349u, 3716369729u, 3756168709u, 3809842710u, 3976885676u, 4026878150u, 4094578721u, 4102242385u, 4140422551u, 4180354873u, 4232364483u, 241329492u, 1795213437u, 3366599054u, 1921726501u, 4143989686u);
            // UnitOverlayIconSettings
            Add(result, "Xmlunitoverlay", 2421413379u);
            // InvisibilityTemplate, InvisibilityManagerSettings
            Add(result, "Xmlvisibility", 1431290504u, 254471188u);
            // BreezeInfo, ModelSettings, AutoParticleSystemSettings
            Add(result, "Xmlvisualsystems", 1626415288u, 1968811301u, 3278684478u);
            // W3DHierarchy, W3DContainer, W3DAnimationRuntime, W3DCollisionBox
            Add(result, "Xmlw3d", 1641540160u, 4042295058u, 3534264507u, 3810008068u);
            return result;
        }
        static void Add(Dictionary<uint,string> result, string family, params uint[] types)
        { foreach (uint type in types) result.Add(type, family); }
    }
}
#endif
