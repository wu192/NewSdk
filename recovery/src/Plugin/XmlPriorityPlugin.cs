using System;
using System.Globalization;
using System.IO;
using BinaryAssetBuilder.Core;

namespace Recovered.XmlPriority
{
    // Generated 300 recovered entries; see catalog/XmlSdkTypes.json and GenerateXmlSdkRoutes.py. Every payload is generated
    // by the recovered bridge; this assembly does not load the original compiler.
    public sealed class Plugin : IAssetBuilderPlugin
    {
        private TargetPlatform platform;
        private string trace;
        private static readonly object TraceGate = new object();

        public void Initialize(object configObject, TargetPlatform target) { platform = target; trace = Environment.GetEnvironmentVariable("RA3_RECOVERED_XML_PRIORITY_TRACE"); }
        public void ReInitialize(object configObject, TargetPlatform target) { Initialize(configObject, target); }
        public uint GetAllTypesHash() { return 1424942948u; }
        public uint GetVersionNumber()
        {
            return platform == TargetPlatform.PlayStation3 ? 7u :
                platform == TargetPlatform.Xbox360 ? 6u : 4u;
        }
        public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            ExtendedTypeInformation info = new ExtendedTypeInformation();
            info.TypeId = typeId;
            // Original XmlCompiler leaves Tokenized and UseBuildCache false.
            info.HasCustomData = false;
            info.Tokenized = false;
            info.UseBuildCache = false;
            switch (typeId)
            {
                case 3319822471u: info.TypeName = "AttributeModifier"; info.TypeHash = 4177657499u; break;
                case 2254974584u: info.TypeName = "FXList"; info.TypeHash = 2601721382u; break;
                case 2178440954u: info.TypeName = "SpecialPowerTemplate"; info.TypeHash = 984303644u; break;
                case 3899542881u: info.TypeName = "ObjectCreationList"; info.TypeHash = 578005457u; break;
                case 3972178387u: info.TypeName = "LocomotorTemplate"; info.TypeHash = 3392338269u; break;
                case 3959844197u: info.TypeName = "LogicCommandSet"; info.TypeHash = 2330506892u; break;
                case 686292351u: info.TypeName = "FXParticleSystemTemplate"; info.TypeHash = 1465656250u; break;
                case 3786401627u: info.TypeName = "UpgradeTemplate"; info.TypeHash = 1334651241u; break;
                case 3604279694u: info.TypeName = "AIPersonalityDefinition"; info.TypeHash = 3409747243u; break;
                case 607123156u: info.TypeName = "AIStrategicStateDefinition"; info.TypeHash = 3594381428u; break;
                case 2423780215u: info.TypeName = "AIMicroManagerData"; info.TypeHash = 1392447443u; break;
                case 2893598307u: info.TypeName = "AIBudgetStateDefinition"; info.TypeHash = 2583631670u; break;
                case 3239935619u: info.TypeName = "AIStateDefinition"; info.TypeHash = 50334453u; break;
                case 926814458u: info.TypeName = "AITargetingHeuristic"; info.TypeHash = 2460454737u; break;
                case 2101756272u: info.TypeName = "LogicCommand"; info.TypeHash = 1451688104u; break;
                case 2131237081u: info.TypeName = "PlayerTech"; info.TypeHash = 903715207u; break;
                case 1802643394u: info.TypeName = "PlayerTechRank"; info.TypeHash = 304552910u; break;
                case 2140592493u: info.TypeName = "PlayerTechUpgradeBinding"; info.TypeHash = 3288487200u; break;
                case 1874610847u: info.TypeName = "ExperienceLevelTemplate"; info.TypeHash = 550595366u; break;
                case 1213037816u: info.TypeName = "PlayerTechStoreTemplate"; info.TypeHash = 981786901u; break;
                case 1566941920u: info.TypeName = "PurchasePlayerTechButtonTemplate"; info.TypeHash = 2999033148u; break;
                case 3048039928u: info.TypeName = "ButtonSingleStateData"; info.TypeHash = 3780991768u; break;
                case 134745024u: info.TypeName = "UICommandButton"; info.TypeHash = 2853539982u; break;
                case 1371131236u: info.TypeName = "FontDefaultSetting"; info.TypeHash = 197748432u; break;
                case 1318336226u: info.TypeName = "FontTranslation"; info.TypeHash = 1815171461u; break;
                case 91719417u: info.TypeName = "FontSubstitution"; info.TypeHash = 2789100025u; break;
                case 2430161325u: info.TypeName = "Weather"; info.TypeHash = 3795761625u; break;
                case 1479531562u: info.TypeName = "WaterTransparency"; info.TypeHash = 525928939u; break;
                case 2728605516u: info.TypeName = "WaterSetting"; info.TypeHash = 469423908u; break;
                case 680780553u: info.TypeName = "Environment"; info.TypeHash = 1276225790u; break;
                case 3169690211u: info.TypeName = "LightingSettings"; info.TypeHash = 616355229u; break;
                case 2995571269u: info.TypeName = "WaterTextureList"; info.TypeHash = 1905565848u; break;
                case 1469498551u: info.TypeName = "TerrainParticleMapping"; info.TypeHash = 2736882942u; break;
                case 2368878637u: info.TypeName = "BounceKickTerrainMapping"; info.TypeHash = 2109862985u; break;
                case 4157475773u: info.TypeName = "ShadowMap"; info.TypeHash = 903854585u; break;
                case 2996766068u: info.TypeName = "OrdersManagerSettings"; info.TypeHash = 2164060776u; break;
                case 1184276792u: info.TypeName = "TerrainVisualSettings"; info.TypeHash = 1336753171u; break;
                case 3898802054u: info.TypeName = "CameraSettings"; info.TypeHash = 4258032094u; break;
                case 3827723072u: info.TypeName = "AIData"; info.TypeHash = 1092854186u; break;
                case 2964891936u: info.TypeName = "UnitAISettings"; info.TypeHash = 4050675268u; break;
                case 4063352333u: info.TypeName = "PlayerTemplate"; info.TypeHash = 891373896u; break;
                case 191082224u: info.TypeName = "MultiplayerSettings"; info.TypeHash = 2620092030u; break;
                case 1644972043u: info.TypeName = "AIBuildStateDefinition"; info.TypeHash = 3400281710u; break;
                case 2010562192u: info.TypeName = "AIStrategicStateName"; info.TypeHash = 76343204u; break;
                case 2384988189u: info.TypeName = "MultiplayerColor"; info.TypeHash = 320610464u; break;
                case 4086175864u: info.TypeName = "AIPersonalityToAchievementEventMapping"; info.TypeHash = 2395097450u; break;
                case 838130914u: info.TypeName = "FormationAssistantUnitDefinition"; info.TypeHash = 1672393125u; break;
                case 2912385462u: info.TypeName = "FormationAssistantTemplate"; info.TypeHash = 156475083u; break;
                case 3374519264u: info.TypeName = "FormationSelectionTemplate"; info.TypeHash = 87133761u; break;
                case 1963196508u: info.TypeName = "FormationAssistant"; info.TypeHash = 1061339997u; break;
                case 3249689110u: info.TypeName = "GameStatsModeData"; info.TypeHash = 4003247819u; break;
                case 402082255u: info.TypeName = "SkirmishSettings"; info.TypeHash = 3814569307u; break;
                case 2905958645u: info.TypeName = "DamageFX"; info.TypeHash = 187609259u; break;
                case 75989905u: info.TypeName = "DamageFXSettings"; info.TypeHash = 2052546860u; break;
                case 417176643u: info.TypeName = "GeneralHotKeySlots"; info.TypeHash = 655411104u; break;
                case 3741098742u: info.TypeName = "DefaultHotKeys"; info.TypeHash = 1229544125u; break;
                case 3359870461u: info.TypeName = "DebugHotKeys"; info.TypeHash = 73026796u; break;
                case 1350608344u: info.TypeName = "MappableKey"; info.TypeHash = 2226564801u; break;
                case 3906227957u: info.TypeName = "AudioEventLimitGroup"; info.TypeHash = 2503036394u; break;
                case 299416263u: info.TypeName = "AudioLOD"; info.TypeHash = 21805907u; break;
                case 1443425905u: info.TypeName = "AudioSettings"; info.TypeHash = 3631141684u; break;
                case 587347702u: info.TypeName = "Video"; info.TypeHash = 2217996198u; break;
                case 1447728787u: info.TypeName = "VideoEventList"; info.TypeHash = 2530876419u; break;
                case 1826140078u: info.TypeName = "MicrophoneSettings"; info.TypeHash = 1624658388u; break;
                case 3370391592u: info.TypeName = "BootupDisplaySequence"; info.TypeHash = 3238903012u; break;
                case 2800139175u: info.TypeName = "HotKeySlot"; info.TypeHash = 2481075826u; break;
                case 3784442503u: info.TypeName = "MouseCursor"; info.TypeHash = 334318473u; break;
                case 611271731u: info.TypeName = "BasicMouseCursors"; info.TypeHash = 1517904520u; break;
                case 611126373u: info.TypeName = "UIMouseDragSelectionFeedback"; info.TypeHash = 549522065u; break;
                case 1559937240u: info.TypeName = "UIFloatingTextDisplay"; info.TypeHash = 2264980354u; break;
                case 1697313181u: info.TypeName = "UIMessageDisplay"; info.TypeHash = 3604410282u; break;
                case 1043989897u: info.TypeName = "UIMilitaryCaptionDisplay"; info.TypeHash = 779899080u; break;
                case 165611890u: info.TypeName = "UISubtitleDisplay"; info.TypeHash = 1129081393u; break;
                case 951036436u: info.TypeName = "UITimerDisplay"; info.TypeHash = 2082140658u; break;
                case 2901356964u: info.TypeName = "DynamicGameLOD"; info.TypeHash = 1764459959u; break;
                case 354222972u: info.TypeName = "GameLODPreset"; info.TypeHash = 3645429389u; break;
                case 531798225u: info.TypeName = "StaticGameLOD"; info.TypeHash = 1251212385u; break;
                case 4243804314u: info.TypeName = "UICoopTinting"; info.TypeHash = 89086670u; break;
                case 544776545u: info.TypeName = "RadiusCursor"; info.TypeHash = 2695845168u; break;
                case 1302035577u: info.TypeName = "AnimationSoundSettings"; info.TypeHash = 3170370516u; break;
                case 2070603733u: info.TypeName = "MiscAudio"; info.TypeHash = 1484343325u; break;
                case 534008255u: info.TypeName = "LargeGroupAudioMap"; info.TypeHash = 1045439046u; break;
                case 1296329066u: info.TypeName = "UIMouseTacticalSelectionDetailsButtonHotKeys"; info.TypeHash = 1187295208u; break;
                case 2591692061u: info.TypeName = "UIMouseTacticalMainCommandBarHotKeys"; info.TypeHash = 1944261033u; break;
                case 2283998413u: info.TypeName = "UIMouseTacticalPlayerPowerHotKeys"; info.TypeHash = 2326182616u; break;
                case 2014061824u: info.TypeName = "UIMouseTacticalMiscHotKeys"; info.TypeHash = 3544626863u; break;
                case 2738273474u: info.TypeName = "UIMouseHUDElementHotKeyGate"; info.TypeHash = 3442548366u; break;
                case 4098431913u: info.TypeName = "UIQuickChatMessage"; info.TypeHash = 743017490u; break;
                case 2891878770u: info.TypeName = "UIMouseTacticalQuickChatDisplay"; info.TypeHash = 884927254u; break;
                case 3746130610u: info.TypeName = "UIMouseVoiceChat"; info.TypeHash = 3018771479u; break;
                case 2599418799u: info.TypeName = "UIDecalGrid"; info.TypeHash = 4073471u; break;
                case 2524893660u: info.TypeName = "UIDecalCloud"; info.TypeHash = 325319211u; break;
                case 1532214660u: info.TypeName = "UIDecalDestinationMarker"; info.TypeHash = 4106878882u; break;
                case 1238702511u: info.TypeName = "UIWorldBuilderDecalGrid"; info.TypeHash = 1469314420u; break;
                case 1079232276u: info.TypeName = "BaseAudioEventInfo"; info.TypeHash = 1604640258u; break;
                case 3228425025u: info.TypeName = "BaseSingleSound"; info.TypeHash = 2950045545u; break;
                case 2219670431u: info.TypeName = "AudioEvent"; info.TypeHash = 1443638853u; break;
                case 1883691512u: info.TypeName = "MusicTrack"; info.TypeHash = 130352738u; break;
                case 1013434420u: info.TypeName = "MP3MusicTrack"; info.TypeHash = 2141170629u; break;
                case 3558134211u: info.TypeName = "DialogEvent"; info.TypeHash = 3525217272u; break;
                case 1479988269u: info.TypeName = "MP3DialogEvent"; info.TypeHash = 2018565112u; break;
                case 3928762264u: info.TypeName = "AmbientStream"; info.TypeHash = 1830167803u; break;
                case 1813598662u: info.TypeName = "MP3AmbientStream"; info.TypeHash = 525549002u; break;
                case 2745675575u: info.TypeName = "Multisound"; info.TypeHash = 4154219145u; break;
                case 97402485u: info.TypeName = "PathMusicGameDynamicState"; info.TypeHash = 3806481831u; break;
                case 343094706u: info.TypeName = "PathMusicGameDynamicStateSet"; info.TypeHash = 1360895287u; break;
                case 2343360052u: info.TypeName = "PathMusicGameDynamicTransition"; info.TypeHash = 2196796046u; break;
                case 1122327197u: info.TypeName = "PathMusicEventRuntime"; info.TypeHash = 692705748u; break;
                case 3339496789u: info.TypeName = "PathMusicEventSet"; info.TypeHash = 1644774436u; break;
                case 2026048286u: info.TypeName = "PathMusicMapRuntime"; info.TypeHash = 290548124u; break;
                case 2407383451u: info.TypeName = "MusicPalette"; info.TypeHash = 3647979872u; break;
                case 2292378780u: info.TypeName = "MusicScriptConditionNuggetBase"; info.TypeHash = 3971458299u; break;
                case 849624115u: info.TypeName = "MusicScriptConditionExpensiveNuggetBase"; info.TypeHash = 2261613772u; break;
                case 188286714u: info.TypeName = "MusicScriptConditionNugget_LocalPlayerIsObserver"; info.TypeHash = 1810715368u; break;
                case 2620791304u: info.TypeName = "MusicScriptConditionNugget_UnitsFarFromBase"; info.TypeHash = 3492519332u; break;
                case 1464425282u: info.TypeName = "MusicScriptConditionNugget_TimeFromStartOfLevel"; info.TypeHash = 1158381935u; break;
                case 57370491u: info.TypeName = "MusicScriptConditionNugget_TrackPlayedCount"; info.TypeHash = 4012559760u; break;
                case 739280456u: info.TypeName = "MusicScriptConditionNugget_SpecificTrackTypePlaying"; info.TypeHash = 3851927382u; break;
                case 3402992833u: info.TypeName = "MusicScriptConditionNugget_AnyTrackPlaying"; info.TypeHash = 1748534571u; break;
                case 4286308287u: info.TypeName = "MusicScriptConditionNugget_ObjectsOfTypeExist"; info.TypeHash = 2841937089u; break;
                case 2743973749u: info.TypeName = "MusicScriptConditionNugget_EvaEventPlayedRecently"; info.TypeHash = 1875528998u; break;
                case 3386715757u: info.TypeName = "MusicScriptConditionNugget_ObjectsNearEvaEvent"; info.TypeHash = 1958333769u; break;
                case 3620764004u: info.TypeName = "MusicScriptConditionNugget_ScoredKillCount"; info.TypeHash = 3267267860u; break;
                case 4007043648u: info.TypeName = "MusicScriptConditionNugget_Not"; info.TypeHash = 855988878u; break;
                case 2281773838u: info.TypeName = "MusicScriptConditionNugget_Or"; info.TypeHash = 4074957085u; break;
                case 1343682755u: info.TypeName = "MusicScriptConditionNugget_And"; info.TypeHash = 532337471u; break;
                case 2912435838u: info.TypeName = "MusicScriptConditionNugget_PathMusicStateLastActive"; info.TypeHash = 2349512303u; break;
                case 3727134806u: info.TypeName = "MusicScriptConditionNugget_PathMusicStateCurrentlyActive"; info.TypeHash = 3386360116u; break;
                case 3476397162u: info.TypeName = "MusicScriptConditionNugget_EngagementCount"; info.TypeHash = 3050031378u; break;
                case 2818967523u: info.TypeName = "MusicScriptConditionNugget_LocalPlayerKillRatio"; info.TypeHash = 3311890413u; break;
                case 1634186921u: info.TypeName = "MusicScriptConditionNugget_HitBySuperWeapon"; info.TypeHash = 607961670u; break;
                case 1038067901u: info.TypeName = "MusicScriptTrack"; info.TypeHash = 3151900516u; break;
                case 104126924u: info.TypeName = "UIMouseTacticalCoOpAIButtonsHelpButton"; info.TypeHash = 3160055664u; break;
                case 113029208u: info.TypeName = "UIMouseTacticalCommandCommunicateWithAllies"; info.TypeHash = 2342677506u; break;
                case 346077409u: info.TypeName = "UIMouseTacticalToggleRepairModeButton"; info.TypeHash = 2270316023u; break;
                case 442957564u: info.TypeName = "UIMouseTacticalAttackMoveButton"; info.TypeHash = 1448492760u; break;
                case 485977567u: info.TypeName = "UIMouseTacticalCommandPlaceWall"; info.TypeHash = 1542859068u; break;
                case 694838069u: info.TypeName = "UIMouseTacticalForceAttackMoveButton"; info.TypeHash = 4192036989u; break;
                case 772462768u: info.TypeName = "UIMouseTacticalCoOpAIButtonsHoldObjectButton"; info.TypeHash = 875756023u; break;
                case 1014034471u: info.TypeName = "UIMouseTacticalExitContainerButton"; info.TypeHash = 2969006564u; break;
                case 1138847078u: info.TypeName = "UIMouseTacticalPlayerTechStoreButton"; info.TypeHash = 1373916399u; break;
                case 1234485619u: info.TypeName = "UIMouseTacticalCommandMove"; info.TypeHash = 3237545121u; break;
                case 1249197161u: info.TypeName = "UIMouseTacticalCommandSell"; info.TypeHash = 2873907483u; break;
                case 1277109486u: info.TypeName = "UIMouseTacticalUnitIntroButton"; info.TypeHash = 4038536099u; break;
                case 1334543376u: info.TypeName = "UIMouseTacticalCommandSelect"; info.TypeHash = 3689232783u; break;
                case 1371977384u: info.TypeName = "UIMouseTacticalCoOpAIButtonsNormalButton"; info.TypeHash = 301556505u; break;
                case 1474300054u: info.TypeName = "UIMouseTacticalCommandPlaceRallyPoint"; info.TypeHash = 1098794956u; break;
                case 1499648070u: info.TypeName = "UIMouseTacticalCommandHarvest"; info.TypeHash = 1611277776u; break;
                case 1956336137u: info.TypeName = "UIMouseTacticalCommandToggleSelfRepair"; info.TypeHash = 693782642u; break;
                case 1974922393u: info.TypeName = "UIMouseTacticalBuildQueueTab"; info.TypeHash = 276486244u; break;
                case 2067763016u: info.TypeName = "UIMouseTacticalCommandGetCrate"; info.TypeHash = 2390364070u; break;
                case 2047841880u: info.TypeName = "UIMouseTacticalDeactivateMissionHotSpotsButton"; info.TypeHash = 3936894160u; break;
                case 2074164365u: info.TypeName = "UIMouseTacticalCommandForceAttack"; info.TypeHash = 3494287844u; break;
                case 2278083954u: info.TypeName = "UIMouseTacticalPlanningModeButton"; info.TypeHash = 3993871439u; break;
                case 2505151835u: info.TypeName = "UIMouseTacticalCommandMisc"; info.TypeHash = 96366804u; break;
                case 2635272840u: info.TypeName = "UIMouseTacticalSelectionRefinementTab"; info.TypeHash = 4048847353u; break;
                case 2697587777u: info.TypeName = "UIMouseTacticalCommandForceMove"; info.TypeHash = 129995987u; break;
                case 2808773267u: info.TypeName = "UIMouseTacticalOpenAdvancedCommandsButton"; info.TypeHash = 2375239281u; break;
                case 2873949396u: info.TypeName = "UIMouseTacticalSetStanceButton"; info.TypeHash = 4072122752u; break;
                case 2914137727u: info.TypeName = "UIMouseTacticalCoOpAIButtonsHoldPositionButton"; info.TypeHash = 1938882503u; break;
                case 2957071942u: info.TypeName = "UIMouseTacticalCommandMineOre"; info.TypeHash = 3017648610u; break;
                case 2958905921u: info.TypeName = "UIMouseTacticalWaypointModeButton"; info.TypeHash = 2908721247u; break;
                case 2987234945u: info.TypeName = "UIMouseTacticalReverseMoveButton"; info.TypeHash = 1863152725u; break;
                case 3057018816u: info.TypeName = "UIMouseTacticalCommandEnter"; info.TypeHash = 1130654826u; break;
                case 3094553884u: info.TypeName = "UIMouseTacticalCoOpAIButtonsAssaultButton"; info.TypeHash = 3862996978u; break;
                case 3420020972u: info.TypeName = "UIMouseTacticalPauseButton"; info.TypeHash = 1007090371u; break;
                case 3548787763u: info.TypeName = "UIMouseTacticalCommandAttack"; info.TypeHash = 45961792u; break;
                case 3575834349u: info.TypeName = "UIMouseTacticalCommandAttackMove"; info.TypeHash = 3529865568u; break;
                case 3716369729u: info.TypeName = "UIMouseTacticalForceMoveButton"; info.TypeHash = 2581745695u; break;
                case 3756168709u: info.TypeName = "UIMouseTacticalForceAttackButton"; info.TypeHash = 144321893u; break;
                case 3809842710u: info.TypeName = "UIMouseTacticalCommandPlaceAIBeacon"; info.TypeHash = 4187711090u; break;
                case 3976885676u: info.TypeName = "UIMouseTacticalCommandDock"; info.TypeHash = 2900176681u; break;
                case 4026878150u: info.TypeName = "UIMouseTacticalCommandReverseMove"; info.TypeHash = 3889487240u; break;
                case 4094578721u: info.TypeName = "UIMouseTacticalBuildQueueTypeTab"; info.TypeHash = 4253066583u; break;
                case 4102242385u: info.TypeName = "UIMouseTacticalSellModeButton"; info.TypeHash = 3000504919u; break;
                case 4140422551u: info.TypeName = "UIMouseTacticalStopButton"; info.TypeHash = 3882873771u; break;
                case 4180354873u: info.TypeName = "UIMouseTacticalCommandSetRallyPoint"; info.TypeHash = 3463571609u; break;
                case 4232364483u: info.TypeName = "UIMouseTacticalFastForwardButton"; info.TypeHash = 3130369778u; break;
                case 241329492u: info.TypeName = "UIMouseTacticalGroupSelection"; info.TypeHash = 1267843369u; break;
                case 1795213437u: info.TypeName = "UIMouseTacticalLookAt"; info.TypeHash = 1676252299u; break;
                case 3366599054u: info.TypeName = "UIMouseTacticalRadarTelestrator"; info.TypeHash = 2875609897u; break;
                case 1921726501u: info.TypeName = "UIMouseTacticalTelestrator"; info.TypeHash = 916455758u; break;
                case 4143989686u: info.TypeName = "UIMouseTacticalThreatMeter"; info.TypeHash = 472971368u; break;
                case 3899256902u: info.TypeName = "BaseConditionData"; info.TypeHash = 2796907385u; break;
                case 519519951u: info.TypeName = "ObjectConditionData"; info.TypeHash = 3621799508u; break;
                case 76901015u: info.TypeName = "UpgradeConditionData"; info.TypeHash = 2863617245u; break;
                case 2309012900u: info.TypeName = "GameplayConditionData"; info.TypeHash = 3176926574u; break;
                case 3600209407u: info.TypeName = "TargetingBaseCompare"; info.TypeHash = 4157686172u; break;
                case 2359666647u: info.TypeName = "TargetingDistanceCompare"; info.TypeHash = 3222028787u; break;
                case 89767986u: info.TypeName = "TargetingCombatChainCompare"; info.TypeHash = 3054734349u; break;
                case 2007776008u: info.TypeName = "TargetingInTurretArcCompare"; info.TypeHash = 1553493937u; break;
                case 3188107749u: info.TypeName = "TargetingCompareList"; info.TypeHash = 1300486550u; break;
                case 1268003905u: info.TypeName = "SiegeEvaEvent"; info.TypeHash = 277018796u; break;
                case 2361666546u: info.TypeName = "EvaSettings"; info.TypeHash = 2947372396u; break;
                case 357806494u: info.TypeName = "EvaEvent"; info.TypeHash = 1889946602u; break;
                case 1807540972u: info.TypeName = "MiscEvaEvents"; info.TypeHash = 3038881679u; break;
                case 4230032093u: info.TypeName = "AchievementEvent"; info.TypeHash = 2866528179u; break;
                case 3594283852u: info.TypeName = "MapNameToAchievementEventMapping"; info.TypeHash = 1078828637u; break;
                case 348670903u: info.TypeName = "GameplayAchievementTriggerData"; info.TypeHash = 478718253u; break;
                case 1507490649u: info.TypeName = "HealthBarTemplate"; info.TypeHash = 2857307522u; break;
                case 3919360587u: info.TypeName = "HealthBarSettings"; info.TypeHash = 4130115945u; break;
                case 2421413379u: info.TypeName = "UnitOverlayIconSettings"; info.TypeHash = 3893526655u; break;
                case 1151702845u: info.TypeName = "ObjectFilterAsset"; info.TypeHash = 1129453918u; break;
                case 814796096u: info.TypeName = "RegisteredObjectSet"; info.TypeHash = 235028480u; break;
                case 772930644u: info.TypeName = "ObjectVisionRangeItem"; info.TypeHash = 1028521956u; break;
                case 1431290504u: info.TypeName = "InvisibilityTemplate"; info.TypeHash = 419230569u; break;
                case 254471188u: info.TypeName = "InvisibilityManagerSettings"; info.TypeHash = 1688533139u; break;
                case 2525284163u: info.TypeName = "StanceTemplate"; info.TypeHash = 1210708834u; break;
                case 2836737428u: info.TypeName = "EmotionTemplate"; info.TypeHash = 1088041681u; break;
                case 1710207315u: info.TypeName = "ProjectilePath"; info.TypeHash = 955128953u; break;
                case 474482543u: info.TypeName = "ForceFeedbackEvent"; info.TypeHash = 3791548904u; break;
                case 3058114834u: info.TypeName = "EngagingContent"; info.TypeHash = 725292512u; break;
                case 11280974u: info.TypeName = "EngagingContentGroup"; info.TypeHash = 1507896455u; break;
                case 3799634821u: info.TypeName = "EngagingContentSequence"; info.TypeHash = 226620221u; break;
                case 530081230u: info.TypeName = "IntelDB"; info.TypeHash = 751047049u; break;
                case 1870180772u: info.TypeName = "UnitIntro"; info.TypeHash = 1460143826u; break;
                case 1626415288u: info.TypeName = "BreezeInfo"; info.TypeHash = 890675481u; break;
                case 1968811301u: info.TypeName = "ModelSettings"; info.TypeHash = 2503358751u; break;
                case 3278684478u: info.TypeName = "AutoParticleSystemSettings"; info.TypeHash = 1247287143u; break;
                case 400896388u: info.TypeName = "CrowdResponse"; info.TypeHash = 129803148u; break;
                case 1099769729u: info.TypeName = "RadarTerrainImage"; info.TypeHash = 4223091486u; break;
                case 4039592522u: info.TypeName = "UIRadarObjectsLayer"; info.TypeHash = 2176451698u; break;
                case 1469055344u: info.TypeName = "W3DDeviceSettings"; info.TypeHash = 2638871790u; break;
                case 2383722691u: info.TypeName = "ScorchMarkSettings"; info.TypeHash = 1770851834u; break;
                case 2915726794u: info.TypeName = "Language"; info.TypeHash = 3221193174u; break;
                case 1218703046u: info.TypeName = "HeaderTemplate"; info.TypeHash = 1075836011u; break;
                case 3569007303u: info.TypeName = "FontDirectory"; info.TypeHash = 3617141059u; break;
                case 4236434438u: info.TypeName = "TheVersion"; info.TypeHash = 2782889071u; break;
                case 3978455785u: info.TypeName = "Credits"; info.TypeHash = 282482670u; break;
                case 2584759047u: info.TypeName = "CommandSet"; info.TypeHash = 4174095999u; break;
                case 3516764358u: info.TypeName = "Road"; info.TypeHash = 738011492u; break;
                case 3166846828u: info.TypeName = "ShaderOverride"; info.TypeHash = 1029381398u; break;
                case 3648907125u: info.TypeName = "ShaderOverrideRule"; info.TypeHash = 279003514u; break;
                case 565855655u: info.TypeName = "ImageSequence"; info.TypeHash = 2717629172u; break;
                case 3614134471u: info.TypeName = "UnitTypeIcon"; info.TypeHash = 4192635734u; break;
                case 2913856066u: info.TypeName = "DrawGroupInfo"; info.TypeHash = 1181471134u; break;
                case 1345252658u: info.TypeName = "OnlineChatColors"; info.TypeHash = 1378748105u; break;
                case 3741496377u: info.TypeName = "ArchivableMovie"; info.TypeHash = 291064791u; break;
                case 2099472558u: info.TypeName = "ArchiveMovieList"; info.TypeHash = 131545626u; break;
                case 4272453293u: info.TypeName = "NetworkSettings"; info.TypeHash = 2097642646u; break;
                case 970663416u: info.TypeName = "PathfindSettings"; info.TypeHash = 2629630449u; break;
                case 3279338544u: info.TypeName = "ResourceSettings"; info.TypeHash = 1560001831u; break;
                case 2846460739u: info.TypeName = "GraphicsHardware"; info.TypeHash = 1118458536u; break;
                case 1482556238u: info.TypeName = "CampaignTemplate"; info.TypeHash = 3502529123u; break;
                case 731899443u: info.TypeName = "CampaignEnumerator"; info.TypeHash = 118801026u; break;
                case 3477794083u: info.TypeName = "MissionTemplate"; info.TypeHash = 3533302842u; break;
                case 3175516876u: info.TypeName = "MissionHotSpotSettings"; info.TypeHash = 4275530952u; break;
                case 3408790761u: info.TypeName = "TutorialMapData"; info.TypeHash = 2508525962u; break;
                case 315614191u: info.TypeName = "LocalBuildListMonitor"; info.TypeHash = 3578044627u; break;
                case 3974617950u: info.TypeName = "BuildSystemSettings"; info.TypeHash = 4161179766u; break;
                case 1047799468u: info.TypeName = "GiftManagerSettings"; info.TypeHash = 3376391493u; break;
                case 741706624u: info.TypeName = "MpGameRules"; info.TypeHash = 886642248u; break;
                case 1044178106u: info.TypeName = "DynamicsContactTagMapping"; info.TypeHash = 898281629u; break;
                case 105587076u: info.TypeName = "ConnectionLineManager"; info.TypeHash = 1802638191u; break;
                case 4141306833u: info.TypeName = "RedAlertTemplate"; info.TypeHash = 133543122u; break;
                case 3258480561u: info.TypeName = "ConquerCardDefinitionBase"; info.TypeHash = 343077480u; break;
                case 3562795973u: info.TypeName = "ConquerCardDefinitionUnit"; info.TypeHash = 3660672133u; break;
                case 4289687760u: info.TypeName = "ConquerCardDefinitionStructure"; info.TypeHash = 3391723256u; break;
                case 4164089898u: info.TypeName = "ConquerCardDefinitionPower"; info.TypeHash = 587170934u; break;
                case 2806522526u: info.TypeName = "ConquerCardPrebuiltDeck"; info.TypeHash = 808620711u; break;
                case 3587539190u: info.TypeName = "ArmyDefinition"; info.TypeHash = 213154423u; break;
                case 3971904488u: info.TypeName = "SkirmishOpeningMove"; info.TypeHash = 1878382208u; break;
                case 212695140u: info.TypeName = "DLContent"; info.TypeHash = 1654379548u; break;
                case 255713734u: info.TypeName = "AssetReportTable"; info.TypeHash = 1788767883u; break;
                case 300273508u: info.TypeName = "StringHashTable"; info.TypeHash = 3959342059u; break;
                case 815686049u: info.TypeName = "MeshInstanceTemplate"; info.TypeHash = 1966615229u; break;
                case 1449288224u: info.TypeName = "PackedTextureImage"; info.TypeHash = 4177308976u; break;
                case 4262364347u: info.TypeName = "OnDemandTextureImage"; info.TypeHash = 333131708u; break;
                case 1816258268u: info.TypeName = "AptAptData"; info.TypeHash = 3795385027u; break;
                case 983359664u: info.TypeName = "AptConstData"; info.TypeHash = 3876251416u; break;
                case 1110676961u: info.TypeName = "AptDatData"; info.TypeHash = 230038576u; break;
                case 1034587168u: info.TypeName = "AptGeometryData"; info.TypeHash = 1390234602u; break;
                case 2989459401u: info.TypeName = "BaseRenderAssetType"; info.TypeHash = 3866869572u; break;
                case 584092973u: info.TypeName = "AudioFileRuntime"; info.TypeHash = 2941751938u; break;
                case 2525492603u: info.TypeName = "Mouse"; info.TypeHash = 1246977355u; break;
                case 3517887782u: info.TypeName = "UnitAbilityButtonTemplate"; info.TypeHash = 2961656853u; break;
                case 4044322798u: info.TypeName = "PlayerPowerButtonTemplate"; info.TypeHash = 2789252562u; break;
                case 2826773070u: info.TypeName = "TestTexture"; info.TypeHash = 275489259u; break;
                case 49556361u: info.TypeName = "TestTextureCollection"; info.TypeHash = 2883151314u; break;
                case 582251659u: info.TypeName = "TerrainAssetCollection"; info.TypeHash = 339527263u; break;
                case 3878948534u: info.TypeName = "TerrainTextureAtlasRuntime"; info.TypeHash = 1182599100u; break;
                case 1641540160u: info.TypeName = "W3DHierarchy"; info.TypeHash = 3985956449u; break;
                case 4042295058u: info.TypeName = "W3DContainer"; info.TypeHash = 1637720895u; break;
                case 3534264507u: info.TypeName = "W3DAnimationRuntime"; info.TypeHash = 3595590881u; break;
                case 3810008068u: info.TypeName = "W3DCollisionBox"; info.TypeHash = 1848047257u; break;
                case 1603703110u: info.TypeName = "MapMetaData"; info.TypeHash = 1209903334u; break;
                case 3737094024u: info.TypeName = "GameScriptList"; info.TypeHash = 2724763994u; break;
                case 3575309793u: info.TypeName = "GameMap"; info.TypeHash = 4100291602u; break;
                case 3072160547u: info.TypeName = "UIConfigList"; info.TypeHash = 3852040349u; break;
                case 1276968974u: info.TypeName = "UIJoypadCommandBarMixedGroup"; info.TypeHash = 3983232506u; break;
                case 1546953756u: info.TypeName = "UIJoypadCommandBarHomogenousGroup"; info.TypeHash = 2225127171u; break;
                case 1654297531u: info.TypeName = "UIJoypadCommandBarCoopCommunicator"; info.TypeHash = 2835608034u; break;
                case 1804891371u: info.TypeName = "JoypadCommandBarButtonTemplate"; info.TypeHash = 689163324u; break;
                case 1887864852u: info.TypeName = "UIJoypadCommandBarStances"; info.TypeHash = 1145979021u; break;
                case 1931769760u: info.TypeName = "JoypadCommandBarTemplate"; info.TypeHash = 1959031257u; break;
                case 2170044043u: info.TypeName = "UIJoypadCommandBarTopMenu"; info.TypeHash = 360514692u; break;
                case 2742786440u: info.TypeName = "UIJoypadCommandBarPlayerPowers"; info.TypeHash = 1752851487u; break;
                case 3423839194u: info.TypeName = "UIJoypadCommandBarButtonBuild"; info.TypeHash = 1141570868u; break;
                case 3645205636u: info.TypeName = "UIJoypadCommandBarControlGroups"; info.TypeHash = 3852026165u; break;
                case 3901433122u: info.TypeName = "UIJoypadCommandBarSingleUnit"; info.TypeHash = 1804668011u; break;
                default: throw new NotSupportedException("XmlPriority supports only its generated recovered scope; excluded roots remain unsupported.");
            }
            info.ProcessingHash = info.TypeHash ^ GetVersionNumber();
            return info;
        }
        // buildRule/typeHashCode are legal BaseAssetType metadata, not XmlCompiler payload fields.
        // Original Module reads neither attribute. Normalize a detached copy only at the
        // SDK boundary: diagnostics and successful replay traces retain the BAB input verbatim.
        private static InstanceDeclaration CompilerInput(InstanceDeclaration instance)
        {
            System.Xml.XmlNode node = instance.Node;
            if (node == null || node.Attributes == null ||
                (node.Attributes["buildRule"] == null && node.Attributes["typeHashCode"] == null)) return instance;
            System.Xml.XmlNode clone = node.CloneNode(true);
            if (clone.Attributes["buildRule"] != null) clone.Attributes.RemoveNamedItem("buildRule");
            if (clone.Attributes["typeHashCode"] != null) clone.Attributes.RemoveNamedItem("typeHashCode");
            InstanceDeclaration normalized = new InstanceDeclaration(instance.Document);
            normalized.XmlNode = clone;
            return normalized;
        }
        public AssetBuffer ProcessInstance(InstanceDeclaration instance)
        {
            if (instance == null) throw new ArgumentNullException("instance");
            uint typeId = instance.Handle.TypeId;
            string typeName;
            AssetBuffer result;
            InstanceDeclaration compilationInstance = CompilerInput(instance);
            switch (typeId)
            {
                case 3319822471u:
                    typeName = "AttributeModifier";
                    result = global::Recovered.AttributeModifierCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2254974584u:
                    typeName = "FXList";
                    result = global::Recovered.FXListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2178440954u:
                    typeName = "SpecialPowerTemplate";
                    result = global::Recovered.SpecialPowerTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3899542881u:
                    typeName = "ObjectCreationList";
                    result = global::Recovered.ObjectCreationListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3972178387u:
                    typeName = "LocomotorTemplate";
                    result = global::Recovered.LocomotorTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3959844197u:
                    typeName = "LogicCommandSet";
                    result = global::Recovered.LogicCommandSetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 686292351u:
                    typeName = "FXParticleSystemTemplate";
                    result = global::Recovered.FXParticleSystemTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3786401627u:
                    typeName = "UpgradeTemplate";
                    result = global::Recovered.UpgradeTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3604279694u:
                    typeName = "AIPersonalityDefinition";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 607123156u:
                    typeName = "AIStrategicStateDefinition";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2423780215u:
                    typeName = "AIMicroManagerData";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2893598307u:
                    typeName = "AIBudgetStateDefinition";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3239935619u:
                    typeName = "AIStateDefinition";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 926814458u:
                    typeName = "AITargetingHeuristic";
                    result = global::Recovered.XmlaiCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2101756272u:
                    typeName = "LogicCommand";
                    result = global::Recovered.LogicCommandCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2131237081u:
                    typeName = "PlayerTech";
                    result = global::Recovered.PlayerTechCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1802643394u:
                    typeName = "PlayerTechRank";
                    result = global::Recovered.PlayerTechRankCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2140592493u:
                    typeName = "PlayerTechUpgradeBinding";
                    result = global::Recovered.PlayerTechUpgradeBindingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1874610847u:
                    typeName = "ExperienceLevelTemplate";
                    result = global::Recovered.ExperienceLevelTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1213037816u:
                    typeName = "PlayerTechStoreTemplate";
                    result = global::Recovered.PlayerTechStoreTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1566941920u:
                    typeName = "PurchasePlayerTechButtonTemplate";
                    result = global::Recovered.PurchasePlayerTechButtonTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3048039928u:
                    typeName = "ButtonSingleStateData";
                    result = global::Recovered.ButtonSingleStateDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 134745024u:
                    typeName = "UICommandButton";
                    result = global::Recovered.UICommandButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1371131236u:
                    typeName = "FontDefaultSetting";
                    result = global::Recovered.FontDefaultSettingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1318336226u:
                    typeName = "FontTranslation";
                    result = global::Recovered.FontTranslationCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 91719417u:
                    typeName = "FontSubstitution";
                    result = global::Recovered.FontSubstitutionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2430161325u:
                    typeName = "Weather";
                    result = global::Recovered.WeatherCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1479531562u:
                    typeName = "WaterTransparency";
                    result = global::Recovered.WaterTransparencyCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2728605516u:
                    typeName = "WaterSetting";
                    result = global::Recovered.WaterSettingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 680780553u:
                    typeName = "Environment";
                    result = global::Recovered.EnvironmentCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3169690211u:
                    typeName = "LightingSettings";
                    result = global::Recovered.LightingSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2995571269u:
                    typeName = "WaterTextureList";
                    result = global::Recovered.WaterTextureListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1469498551u:
                    typeName = "TerrainParticleMapping";
                    result = global::Recovered.TerrainParticleMappingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2368878637u:
                    typeName = "BounceKickTerrainMapping";
                    result = global::Recovered.BounceKickTerrainMappingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4157475773u:
                    typeName = "ShadowMap";
                    result = global::Recovered.ShadowMapCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2996766068u:
                    typeName = "OrdersManagerSettings";
                    result = global::Recovered.OrdersManagerSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1184276792u:
                    typeName = "TerrainVisualSettings";
                    result = global::Recovered.TerrainVisualSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3898802054u:
                    typeName = "CameraSettings";
                    result = global::Recovered.CameraSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3827723072u:
                    typeName = "AIData";
                    result = global::Recovered.AIDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2964891936u:
                    typeName = "UnitAISettings";
                    result = global::Recovered.UnitAISettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4063352333u:
                    typeName = "PlayerTemplate";
                    result = global::Recovered.PlayerTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 191082224u:
                    typeName = "MultiplayerSettings";
                    result = global::Recovered.MultiplayerSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1644972043u:
                    typeName = "AIBuildStateDefinition";
                    result = global::Recovered.AIBuildStateDefinitionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2010562192u:
                    typeName = "AIStrategicStateName";
                    result = global::Recovered.AIStrategicStateNameCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2384988189u:
                    typeName = "MultiplayerColor";
                    result = global::Recovered.MultiplayerColorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4086175864u:
                    typeName = "AIPersonalityToAchievementEventMapping";
                    result = global::Recovered.AIPersonalityToAchievementEventMappingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 838130914u:
                    typeName = "FormationAssistantUnitDefinition";
                    result = global::Recovered.FormationAssistantUnitDefinitionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2912385462u:
                    typeName = "FormationAssistantTemplate";
                    result = global::Recovered.FormationAssistantTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3374519264u:
                    typeName = "FormationSelectionTemplate";
                    result = global::Recovered.FormationSelectionTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1963196508u:
                    typeName = "FormationAssistant";
                    result = global::Recovered.FormationAssistantCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3249689110u:
                    typeName = "GameStatsModeData";
                    result = global::Recovered.GameStatsModeDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 402082255u:
                    typeName = "SkirmishSettings";
                    result = global::Recovered.SkirmishSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2905958645u:
                    typeName = "DamageFX";
                    result = global::Recovered.DamageFXCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 75989905u:
                    typeName = "DamageFXSettings";
                    result = global::Recovered.DamageFXSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 417176643u:
                    typeName = "GeneralHotKeySlots";
                    result = global::Recovered.GeneralHotKeySlotsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3741098742u:
                    typeName = "DefaultHotKeys";
                    result = global::Recovered.DefaultHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3359870461u:
                    typeName = "DebugHotKeys";
                    result = global::Recovered.DebugHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1350608344u:
                    typeName = "MappableKey";
                    result = global::Recovered.MappableKeyCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3906227957u:
                    typeName = "AudioEventLimitGroup";
                    result = global::Recovered.AudioEventLimitGroupCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 299416263u:
                    typeName = "AudioLOD";
                    result = global::Recovered.AudioLODCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1443425905u:
                    typeName = "AudioSettings";
                    result = global::Recovered.AudioSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 587347702u:
                    typeName = "Video";
                    result = global::Recovered.VideoCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1447728787u:
                    typeName = "VideoEventList";
                    result = global::Recovered.VideoEventListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1826140078u:
                    typeName = "MicrophoneSettings";
                    result = global::Recovered.MicrophoneSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3370391592u:
                    typeName = "BootupDisplaySequence";
                    result = global::Recovered.BootupDisplaySequenceCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2800139175u:
                    typeName = "HotKeySlot";
                    result = global::Recovered.HotKeySlotCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3784442503u:
                    typeName = "MouseCursor";
                    result = global::Recovered.MouseCursorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 611271731u:
                    typeName = "BasicMouseCursors";
                    result = global::Recovered.BasicMouseCursorsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 611126373u:
                    typeName = "UIMouseDragSelectionFeedback";
                    result = global::Recovered.UIMouseDragSelectionFeedbackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1559937240u:
                    typeName = "UIFloatingTextDisplay";
                    result = global::Recovered.UIFloatingTextDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1697313181u:
                    typeName = "UIMessageDisplay";
                    result = global::Recovered.UIMessageDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1043989897u:
                    typeName = "UIMilitaryCaptionDisplay";
                    result = global::Recovered.UIMilitaryCaptionDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 165611890u:
                    typeName = "UISubtitleDisplay";
                    result = global::Recovered.UISubtitleDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 951036436u:
                    typeName = "UITimerDisplay";
                    result = global::Recovered.UITimerDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2901356964u:
                    typeName = "DynamicGameLOD";
                    result = global::Recovered.DynamicGameLODCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 354222972u:
                    typeName = "GameLODPreset";
                    result = global::Recovered.GameLODPresetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 531798225u:
                    typeName = "StaticGameLOD";
                    result = global::Recovered.StaticGameLODCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4243804314u:
                    typeName = "UICoopTinting";
                    result = global::Recovered.UICoopTintingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 544776545u:
                    typeName = "RadiusCursor";
                    result = global::Recovered.RadiusCursorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1302035577u:
                    typeName = "AnimationSoundSettings";
                    result = global::Recovered.AnimationSoundSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2070603733u:
                    typeName = "MiscAudio";
                    result = global::Recovered.MiscAudioCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 534008255u:
                    typeName = "LargeGroupAudioMap";
                    result = global::Recovered.LargeGroupAudioMapCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1296329066u:
                    typeName = "UIMouseTacticalSelectionDetailsButtonHotKeys";
                    result = global::Recovered.UIMouseTacticalSelectionDetailsButtonHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2591692061u:
                    typeName = "UIMouseTacticalMainCommandBarHotKeys";
                    result = global::Recovered.UIMouseTacticalMainCommandBarHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2283998413u:
                    typeName = "UIMouseTacticalPlayerPowerHotKeys";
                    result = global::Recovered.UIMouseTacticalPlayerPowerHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2014061824u:
                    typeName = "UIMouseTacticalMiscHotKeys";
                    result = global::Recovered.UIMouseTacticalMiscHotKeysCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2738273474u:
                    typeName = "UIMouseHUDElementHotKeyGate";
                    result = global::Recovered.UIMouseHUDElementHotKeyGateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4098431913u:
                    typeName = "UIQuickChatMessage";
                    result = global::Recovered.UIQuickChatMessageCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2891878770u:
                    typeName = "UIMouseTacticalQuickChatDisplay";
                    result = global::Recovered.UIMouseTacticalQuickChatDisplayCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3746130610u:
                    typeName = "UIMouseVoiceChat";
                    result = global::Recovered.UIMouseVoiceChatCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2599418799u:
                    typeName = "UIDecalGrid";
                    result = global::Recovered.UIDecalGridCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2524893660u:
                    typeName = "UIDecalCloud";
                    result = global::Recovered.UIDecalCloudCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1532214660u:
                    typeName = "UIDecalDestinationMarker";
                    result = global::Recovered.UIDecalDestinationMarkerCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1238702511u:
                    typeName = "UIWorldBuilderDecalGrid";
                    result = global::Recovered.UIWorldBuilderDecalGridCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1079232276u:
                    typeName = "BaseAudioEventInfo";
                    result = global::Recovered.BaseAudioEventInfoCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3228425025u:
                    typeName = "BaseSingleSound";
                    result = global::Recovered.BaseSingleSoundCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2219670431u:
                    typeName = "AudioEvent";
                    result = global::Recovered.AudioEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1883691512u:
                    typeName = "MusicTrack";
                    result = global::Recovered.MusicTrackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1013434420u:
                    typeName = "MP3MusicTrack";
                    result = global::Recovered.MP3MusicTrackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3558134211u:
                    typeName = "DialogEvent";
                    result = global::Recovered.DialogEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1479988269u:
                    typeName = "MP3DialogEvent";
                    result = global::Recovered.MP3DialogEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3928762264u:
                    typeName = "AmbientStream";
                    result = global::Recovered.AmbientStreamCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1813598662u:
                    typeName = "MP3AmbientStream";
                    result = global::Recovered.MP3AmbientStreamCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2745675575u:
                    typeName = "Multisound";
                    result = global::Recovered.MultisoundCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 97402485u:
                    typeName = "PathMusicGameDynamicState";
                    result = global::Recovered.PathMusicGameDynamicStateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 343094706u:
                    typeName = "PathMusicGameDynamicStateSet";
                    result = global::Recovered.PathMusicGameDynamicStateSetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2343360052u:
                    typeName = "PathMusicGameDynamicTransition";
                    result = global::Recovered.PathMusicGameDynamicTransitionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1122327197u:
                    typeName = "PathMusicEventRuntime";
                    result = global::Recovered.PathMusicEventRuntimeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3339496789u:
                    typeName = "PathMusicEventSet";
                    result = global::Recovered.PathMusicEventSetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2026048286u:
                    typeName = "PathMusicMapRuntime";
                    result = global::Recovered.PathMusicMapRuntimeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2407383451u:
                    typeName = "MusicPalette";
                    result = global::Recovered.MusicPaletteCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2292378780u:
                    typeName = "MusicScriptConditionNuggetBase";
                    result = global::Recovered.MusicScriptConditionNuggetBaseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 849624115u:
                    typeName = "MusicScriptConditionExpensiveNuggetBase";
                    result = global::Recovered.MusicScriptConditionExpensiveNuggetBaseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 188286714u:
                    typeName = "MusicScriptConditionNugget_LocalPlayerIsObserver";
                    result = global::Recovered.MusicScriptConditionNugget_LocalPlayerIsObserverCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2620791304u:
                    typeName = "MusicScriptConditionNugget_UnitsFarFromBase";
                    result = global::Recovered.MusicScriptConditionNugget_UnitsFarFromBaseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1464425282u:
                    typeName = "MusicScriptConditionNugget_TimeFromStartOfLevel";
                    result = global::Recovered.MusicScriptConditionNugget_TimeFromStartOfLevelCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 57370491u:
                    typeName = "MusicScriptConditionNugget_TrackPlayedCount";
                    result = global::Recovered.MusicScriptConditionNugget_TrackPlayedCountCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 739280456u:
                    typeName = "MusicScriptConditionNugget_SpecificTrackTypePlaying";
                    result = global::Recovered.MusicScriptConditionNugget_SpecificTrackTypePlayingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3402992833u:
                    typeName = "MusicScriptConditionNugget_AnyTrackPlaying";
                    result = global::Recovered.MusicScriptConditionNugget_AnyTrackPlayingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4286308287u:
                    typeName = "MusicScriptConditionNugget_ObjectsOfTypeExist";
                    result = global::Recovered.MusicScriptConditionNugget_ObjectsOfTypeExistCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2743973749u:
                    typeName = "MusicScriptConditionNugget_EvaEventPlayedRecently";
                    result = global::Recovered.MusicScriptConditionNugget_EvaEventPlayedRecentlyCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3386715757u:
                    typeName = "MusicScriptConditionNugget_ObjectsNearEvaEvent";
                    result = global::Recovered.MusicScriptConditionNugget_ObjectsNearEvaEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3620764004u:
                    typeName = "MusicScriptConditionNugget_ScoredKillCount";
                    result = global::Recovered.MusicScriptConditionNugget_ScoredKillCountCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4007043648u:
                    typeName = "MusicScriptConditionNugget_Not";
                    result = global::Recovered.MusicScriptConditionNugget_NotCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2281773838u:
                    typeName = "MusicScriptConditionNugget_Or";
                    result = global::Recovered.MusicScriptConditionNugget_OrCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1343682755u:
                    typeName = "MusicScriptConditionNugget_And";
                    result = global::Recovered.MusicScriptConditionNugget_AndCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2912435838u:
                    typeName = "MusicScriptConditionNugget_PathMusicStateLastActive";
                    result = global::Recovered.MusicScriptConditionNugget_PathMusicStateLastActiveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3727134806u:
                    typeName = "MusicScriptConditionNugget_PathMusicStateCurrentlyActive";
                    result = global::Recovered.MusicScriptConditionNugget_PathMusicStateCurrentlyActiveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3476397162u:
                    typeName = "MusicScriptConditionNugget_EngagementCount";
                    result = global::Recovered.MusicScriptConditionNugget_EngagementCountCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2818967523u:
                    typeName = "MusicScriptConditionNugget_LocalPlayerKillRatio";
                    result = global::Recovered.MusicScriptConditionNugget_LocalPlayerKillRatioCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1634186921u:
                    typeName = "MusicScriptConditionNugget_HitBySuperWeapon";
                    result = global::Recovered.MusicScriptConditionNugget_HitBySuperWeaponCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1038067901u:
                    typeName = "MusicScriptTrack";
                    result = global::Recovered.MusicScriptTrackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 104126924u:
                    typeName = "UIMouseTacticalCoOpAIButtonsHelpButton";
                    result = global::Recovered.UIMouseTacticalCoOpAIButtonsHelpButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 113029208u:
                    typeName = "UIMouseTacticalCommandCommunicateWithAllies";
                    result = global::Recovered.UIMouseTacticalCommandCommunicateWithAlliesCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 346077409u:
                    typeName = "UIMouseTacticalToggleRepairModeButton";
                    result = global::Recovered.UIMouseTacticalToggleRepairModeButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 442957564u:
                    typeName = "UIMouseTacticalAttackMoveButton";
                    result = global::Recovered.UIMouseTacticalAttackMoveButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 485977567u:
                    typeName = "UIMouseTacticalCommandPlaceWall";
                    result = global::Recovered.UIMouseTacticalCommandPlaceWallCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 694838069u:
                    typeName = "UIMouseTacticalForceAttackMoveButton";
                    result = global::Recovered.UIMouseTacticalForceAttackMoveButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 772462768u:
                    typeName = "UIMouseTacticalCoOpAIButtonsHoldObjectButton";
                    result = global::Recovered.UIMouseTacticalCoOpAIButtonsHoldObjectButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1014034471u:
                    typeName = "UIMouseTacticalExitContainerButton";
                    result = global::Recovered.UIMouseTacticalExitContainerButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1138847078u:
                    typeName = "UIMouseTacticalPlayerTechStoreButton";
                    result = global::Recovered.UIMouseTacticalPlayerTechStoreButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1234485619u:
                    typeName = "UIMouseTacticalCommandMove";
                    result = global::Recovered.UIMouseTacticalCommandMoveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1249197161u:
                    typeName = "UIMouseTacticalCommandSell";
                    result = global::Recovered.UIMouseTacticalCommandSellCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1277109486u:
                    typeName = "UIMouseTacticalUnitIntroButton";
                    result = global::Recovered.UIMouseTacticalUnitIntroButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1334543376u:
                    typeName = "UIMouseTacticalCommandSelect";
                    result = global::Recovered.UIMouseTacticalCommandSelectCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1371977384u:
                    typeName = "UIMouseTacticalCoOpAIButtonsNormalButton";
                    result = global::Recovered.UIMouseTacticalCoOpAIButtonsNormalButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1474300054u:
                    typeName = "UIMouseTacticalCommandPlaceRallyPoint";
                    result = global::Recovered.UIMouseTacticalCommandPlaceRallyPointCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1499648070u:
                    typeName = "UIMouseTacticalCommandHarvest";
                    result = global::Recovered.UIMouseTacticalCommandHarvestCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1956336137u:
                    typeName = "UIMouseTacticalCommandToggleSelfRepair";
                    result = global::Recovered.UIMouseTacticalCommandToggleSelfRepairCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1974922393u:
                    typeName = "UIMouseTacticalBuildQueueTab";
                    result = global::Recovered.UIMouseTacticalBuildQueueTabCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2067763016u:
                    typeName = "UIMouseTacticalCommandGetCrate";
                    result = global::Recovered.UIMouseTacticalCommandGetCrateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2047841880u:
                    typeName = "UIMouseTacticalDeactivateMissionHotSpotsButton";
                    result = global::Recovered.UIMouseTacticalDeactivateMissionHotSpotsButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2074164365u:
                    typeName = "UIMouseTacticalCommandForceAttack";
                    result = global::Recovered.UIMouseTacticalCommandForceAttackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2278083954u:
                    typeName = "UIMouseTacticalPlanningModeButton";
                    result = global::Recovered.UIMouseTacticalPlanningModeButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2505151835u:
                    typeName = "UIMouseTacticalCommandMisc";
                    result = global::Recovered.UIMouseTacticalCommandMiscCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2635272840u:
                    typeName = "UIMouseTacticalSelectionRefinementTab";
                    result = global::Recovered.UIMouseTacticalSelectionRefinementTabCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2697587777u:
                    typeName = "UIMouseTacticalCommandForceMove";
                    result = global::Recovered.UIMouseTacticalCommandForceMoveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2808773267u:
                    typeName = "UIMouseTacticalOpenAdvancedCommandsButton";
                    result = global::Recovered.UIMouseTacticalOpenAdvancedCommandsButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2873949396u:
                    typeName = "UIMouseTacticalSetStanceButton";
                    result = global::Recovered.UIMouseTacticalSetStanceButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2914137727u:
                    typeName = "UIMouseTacticalCoOpAIButtonsHoldPositionButton";
                    result = global::Recovered.UIMouseTacticalCoOpAIButtonsHoldPositionButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2957071942u:
                    typeName = "UIMouseTacticalCommandMineOre";
                    result = global::Recovered.UIMouseTacticalCommandMineOreCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2958905921u:
                    typeName = "UIMouseTacticalWaypointModeButton";
                    result = global::Recovered.UIMouseTacticalWaypointModeButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2987234945u:
                    typeName = "UIMouseTacticalReverseMoveButton";
                    result = global::Recovered.UIMouseTacticalReverseMoveButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3057018816u:
                    typeName = "UIMouseTacticalCommandEnter";
                    result = global::Recovered.UIMouseTacticalCommandEnterCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3094553884u:
                    typeName = "UIMouseTacticalCoOpAIButtonsAssaultButton";
                    result = global::Recovered.UIMouseTacticalCoOpAIButtonsAssaultButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3420020972u:
                    typeName = "UIMouseTacticalPauseButton";
                    result = global::Recovered.UIMouseTacticalPauseButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3548787763u:
                    typeName = "UIMouseTacticalCommandAttack";
                    result = global::Recovered.UIMouseTacticalCommandAttackCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3575834349u:
                    typeName = "UIMouseTacticalCommandAttackMove";
                    result = global::Recovered.UIMouseTacticalCommandAttackMoveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3716369729u:
                    typeName = "UIMouseTacticalForceMoveButton";
                    result = global::Recovered.UIMouseTacticalForceMoveButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3756168709u:
                    typeName = "UIMouseTacticalForceAttackButton";
                    result = global::Recovered.UIMouseTacticalForceAttackButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3809842710u:
                    typeName = "UIMouseTacticalCommandPlaceAIBeacon";
                    result = global::Recovered.UIMouseTacticalCommandPlaceAIBeaconCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3976885676u:
                    typeName = "UIMouseTacticalCommandDock";
                    result = global::Recovered.UIMouseTacticalCommandDockCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4026878150u:
                    typeName = "UIMouseTacticalCommandReverseMove";
                    result = global::Recovered.UIMouseTacticalCommandReverseMoveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4094578721u:
                    typeName = "UIMouseTacticalBuildQueueTypeTab";
                    result = global::Recovered.UIMouseTacticalBuildQueueTypeTabCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4102242385u:
                    typeName = "UIMouseTacticalSellModeButton";
                    result = global::Recovered.UIMouseTacticalSellModeButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4140422551u:
                    typeName = "UIMouseTacticalStopButton";
                    result = global::Recovered.UIMouseTacticalStopButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4180354873u:
                    typeName = "UIMouseTacticalCommandSetRallyPoint";
                    result = global::Recovered.UIMouseTacticalCommandSetRallyPointCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4232364483u:
                    typeName = "UIMouseTacticalFastForwardButton";
                    result = global::Recovered.UIMouseTacticalFastForwardButtonCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 241329492u:
                    typeName = "UIMouseTacticalGroupSelection";
                    result = global::Recovered.UIMouseTacticalGroupSelectionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1795213437u:
                    typeName = "UIMouseTacticalLookAt";
                    result = global::Recovered.UIMouseTacticalLookAtCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3366599054u:
                    typeName = "UIMouseTacticalRadarTelestrator";
                    result = global::Recovered.UIMouseTacticalRadarTelestratorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1921726501u:
                    typeName = "UIMouseTacticalTelestrator";
                    result = global::Recovered.UIMouseTacticalTelestratorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4143989686u:
                    typeName = "UIMouseTacticalThreatMeter";
                    result = global::Recovered.UIMouseTacticalThreatMeterCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3899256902u:
                    typeName = "BaseConditionData";
                    result = global::Recovered.BaseConditionDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 519519951u:
                    typeName = "ObjectConditionData";
                    result = global::Recovered.ObjectConditionDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 76901015u:
                    typeName = "UpgradeConditionData";
                    result = global::Recovered.UpgradeConditionDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2309012900u:
                    typeName = "GameplayConditionData";
                    result = global::Recovered.GameplayConditionDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3600209407u:
                    typeName = "TargetingBaseCompare";
                    result = global::Recovered.TargetingBaseCompareCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2359666647u:
                    typeName = "TargetingDistanceCompare";
                    result = global::Recovered.TargetingDistanceCompareCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 89767986u:
                    typeName = "TargetingCombatChainCompare";
                    result = global::Recovered.TargetingCombatChainCompareCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2007776008u:
                    typeName = "TargetingInTurretArcCompare";
                    result = global::Recovered.TargetingInTurretArcCompareCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3188107749u:
                    typeName = "TargetingCompareList";
                    result = global::Recovered.TargetingCompareListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1268003905u:
                    typeName = "SiegeEvaEvent";
                    result = global::Recovered.SiegeEvaEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2361666546u:
                    typeName = "EvaSettings";
                    result = global::Recovered.EvaSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 357806494u:
                    typeName = "EvaEvent";
                    result = global::Recovered.EvaEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1807540972u:
                    typeName = "MiscEvaEvents";
                    result = global::Recovered.MiscEvaEventsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4230032093u:
                    typeName = "AchievementEvent";
                    result = global::Recovered.AchievementEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3594283852u:
                    typeName = "MapNameToAchievementEventMapping";
                    result = global::Recovered.MapNameToAchievementEventMappingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 348670903u:
                    typeName = "GameplayAchievementTriggerData";
                    result = global::Recovered.GameplayAchievementTriggerDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1507490649u:
                    typeName = "HealthBarTemplate";
                    result = global::Recovered.HealthBarTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3919360587u:
                    typeName = "HealthBarSettings";
                    result = global::Recovered.HealthBarSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2421413379u:
                    typeName = "UnitOverlayIconSettings";
                    result = global::Recovered.UnitOverlayIconSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1151702845u:
                    typeName = "ObjectFilterAsset";
                    result = global::Recovered.ObjectFilterAssetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 814796096u:
                    typeName = "RegisteredObjectSet";
                    result = global::Recovered.RegisteredObjectSetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 772930644u:
                    typeName = "ObjectVisionRangeItem";
                    result = global::Recovered.ObjectVisionRangeItemCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1431290504u:
                    typeName = "InvisibilityTemplate";
                    result = global::Recovered.InvisibilityTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 254471188u:
                    typeName = "InvisibilityManagerSettings";
                    result = global::Recovered.InvisibilityManagerSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2525284163u:
                    typeName = "StanceTemplate";
                    result = global::Recovered.StanceTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2836737428u:
                    typeName = "EmotionTemplate";
                    result = global::Recovered.EmotionTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1710207315u:
                    typeName = "ProjectilePath";
                    result = global::Recovered.ProjectilePathCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 474482543u:
                    typeName = "ForceFeedbackEvent";
                    result = global::Recovered.ForceFeedbackEventCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3058114834u:
                    typeName = "EngagingContent";
                    result = global::Recovered.EngagingContentCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 11280974u:
                    typeName = "EngagingContentGroup";
                    result = global::Recovered.EngagingContentGroupCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3799634821u:
                    typeName = "EngagingContentSequence";
                    result = global::Recovered.EngagingContentSequenceCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 530081230u:
                    typeName = "IntelDB";
                    result = global::Recovered.IntelDBCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1870180772u:
                    typeName = "UnitIntro";
                    result = global::Recovered.UnitIntroCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1626415288u:
                    typeName = "BreezeInfo";
                    result = global::Recovered.BreezeInfoCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1968811301u:
                    typeName = "ModelSettings";
                    result = global::Recovered.ModelSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3278684478u:
                    typeName = "AutoParticleSystemSettings";
                    result = global::Recovered.AutoParticleSystemSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 400896388u:
                    typeName = "CrowdResponse";
                    result = global::Recovered.CrowdResponseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1099769729u:
                    typeName = "RadarTerrainImage";
                    result = global::Recovered.RadarTerrainImageCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4039592522u:
                    typeName = "UIRadarObjectsLayer";
                    result = global::Recovered.UIRadarObjectsLayerCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1469055344u:
                    typeName = "W3DDeviceSettings";
                    result = global::Recovered.W3DDeviceSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2383722691u:
                    typeName = "ScorchMarkSettings";
                    result = global::Recovered.ScorchMarkSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2915726794u:
                    typeName = "Language";
                    result = global::Recovered.LanguageCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1218703046u:
                    typeName = "HeaderTemplate";
                    result = global::Recovered.HeaderTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3569007303u:
                    typeName = "FontDirectory";
                    result = global::Recovered.FontDirectoryCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4236434438u:
                    typeName = "TheVersion";
                    result = global::Recovered.TheVersionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3978455785u:
                    typeName = "Credits";
                    result = global::Recovered.CreditsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2584759047u:
                    typeName = "CommandSet";
                    result = global::Recovered.CommandSetCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3516764358u:
                    typeName = "Road";
                    result = global::Recovered.RoadCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3166846828u:
                    typeName = "ShaderOverride";
                    result = global::Recovered.ShaderOverrideCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3648907125u:
                    typeName = "ShaderOverrideRule";
                    result = global::Recovered.ShaderOverrideRuleCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 565855655u:
                    typeName = "ImageSequence";
                    result = global::Recovered.ImageSequenceCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3614134471u:
                    typeName = "UnitTypeIcon";
                    result = global::Recovered.UnitTypeIconCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2913856066u:
                    typeName = "DrawGroupInfo";
                    result = global::Recovered.DrawGroupInfoCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1345252658u:
                    typeName = "OnlineChatColors";
                    result = global::Recovered.OnlineChatColorsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3741496377u:
                    typeName = "ArchivableMovie";
                    result = global::Recovered.ArchivableMovieCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2099472558u:
                    typeName = "ArchiveMovieList";
                    result = global::Recovered.ArchiveMovieListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4272453293u:
                    typeName = "NetworkSettings";
                    result = global::Recovered.NetworkSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 970663416u:
                    typeName = "PathfindSettings";
                    result = global::Recovered.PathfindSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3279338544u:
                    typeName = "ResourceSettings";
                    result = global::Recovered.ResourceSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2846460739u:
                    typeName = "GraphicsHardware";
                    result = global::Recovered.GraphicsHardwareCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1482556238u:
                    typeName = "CampaignTemplate";
                    result = global::Recovered.CampaignTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 731899443u:
                    typeName = "CampaignEnumerator";
                    result = global::Recovered.CampaignEnumeratorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3477794083u:
                    typeName = "MissionTemplate";
                    result = global::Recovered.MissionTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3175516876u:
                    typeName = "MissionHotSpotSettings";
                    result = global::Recovered.MissionHotSpotSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3408790761u:
                    typeName = "TutorialMapData";
                    result = global::Recovered.TutorialMapDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 315614191u:
                    typeName = "LocalBuildListMonitor";
                    result = global::Recovered.LocalBuildListMonitorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3974617950u:
                    typeName = "BuildSystemSettings";
                    result = global::Recovered.BuildSystemSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1047799468u:
                    typeName = "GiftManagerSettings";
                    result = global::Recovered.GiftManagerSettingsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 741706624u:
                    typeName = "MpGameRules";
                    result = global::Recovered.MpGameRulesCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1044178106u:
                    typeName = "DynamicsContactTagMapping";
                    result = global::Recovered.DynamicsContactTagMappingCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 105587076u:
                    typeName = "ConnectionLineManager";
                    result = global::Recovered.ConnectionLineManagerCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4141306833u:
                    typeName = "RedAlertTemplate";
                    result = global::Recovered.RedAlertTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3258480561u:
                    typeName = "ConquerCardDefinitionBase";
                    result = global::Recovered.ConquerCardDefinitionBaseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3562795973u:
                    typeName = "ConquerCardDefinitionUnit";
                    result = global::Recovered.ConquerCardDefinitionUnitCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4289687760u:
                    typeName = "ConquerCardDefinitionStructure";
                    result = global::Recovered.ConquerCardDefinitionStructureCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4164089898u:
                    typeName = "ConquerCardDefinitionPower";
                    result = global::Recovered.ConquerCardDefinitionPowerCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2806522526u:
                    typeName = "ConquerCardPrebuiltDeck";
                    result = global::Recovered.ConquerCardPrebuiltDeckCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3587539190u:
                    typeName = "ArmyDefinition";
                    result = global::Recovered.ArmyDefinitionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3971904488u:
                    typeName = "SkirmishOpeningMove";
                    result = global::Recovered.SkirmishOpeningMoveCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 212695140u:
                    typeName = "DLContent";
                    result = global::Recovered.DLContentCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 255713734u:
                    typeName = "AssetReportTable";
                    result = global::Recovered.AssetReportTableCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 300273508u:
                    typeName = "StringHashTable";
                    result = global::Recovered.StringHashTableCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 815686049u:
                    typeName = "MeshInstanceTemplate";
                    result = global::Recovered.MeshInstanceTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1449288224u:
                    typeName = "PackedTextureImage";
                    result = global::Recovered.PackedTextureImageCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4262364347u:
                    typeName = "OnDemandTextureImage";
                    result = global::Recovered.OnDemandTextureImageCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1816258268u:
                    typeName = "AptAptData";
                    result = global::Recovered.AptAptDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 983359664u:
                    typeName = "AptConstData";
                    result = global::Recovered.AptConstDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1110676961u:
                    typeName = "AptDatData";
                    result = global::Recovered.AptDatDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1034587168u:
                    typeName = "AptGeometryData";
                    result = global::Recovered.AptGeometryDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2989459401u:
                    typeName = "BaseRenderAssetType";
                    result = global::Recovered.BaseRenderAssetTypeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 584092973u:
                    typeName = "AudioFileRuntime";
                    result = global::Recovered.AudioFileRuntimeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2525492603u:
                    typeName = "Mouse";
                    result = global::Recovered.MouseCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3517887782u:
                    typeName = "UnitAbilityButtonTemplate";
                    result = global::Recovered.UnitAbilityButtonTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4044322798u:
                    typeName = "PlayerPowerButtonTemplate";
                    result = global::Recovered.PlayerPowerButtonTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2826773070u:
                    typeName = "TestTexture";
                    result = global::Recovered.TestTextureCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 49556361u:
                    typeName = "TestTextureCollection";
                    result = global::Recovered.TestTextureCollectionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 582251659u:
                    typeName = "TerrainAssetCollection";
                    result = global::Recovered.TerrainAssetCollectionCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3878948534u:
                    typeName = "TerrainTextureAtlasRuntime";
                    result = global::Recovered.TerrainTextureAtlasRuntimeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1641540160u:
                    typeName = "W3DHierarchy";
                    result = global::Recovered.W3DHierarchyCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 4042295058u:
                    typeName = "W3DContainer";
                    result = global::Recovered.W3DContainerCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3534264507u:
                    typeName = "W3DAnimationRuntime";
                    result = global::Recovered.W3DAnimationRuntimeCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3810008068u:
                    typeName = "W3DCollisionBox";
                    result = global::Recovered.W3DCollisionBoxCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1603703110u:
                    typeName = "MapMetaData";
                    result = global::Recovered.MapMetaDataCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3737094024u:
                    typeName = "GameScriptList";
                    result = global::Recovered.GameScriptListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3575309793u:
                    typeName = "GameMap";
                    result = global::Recovered.GameMapCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3072160547u:
                    typeName = "UIConfigList";
                    result = global::Recovered.UIConfigListCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1276968974u:
                    typeName = "UIJoypadCommandBarMixedGroup";
                    result = global::Recovered.UIJoypadCommandBarMixedGroupCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1546953756u:
                    typeName = "UIJoypadCommandBarHomogenousGroup";
                    result = global::Recovered.UIJoypadCommandBarHomogenousGroupCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1654297531u:
                    typeName = "UIJoypadCommandBarCoopCommunicator";
                    result = global::Recovered.UIJoypadCommandBarCoopCommunicatorCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1804891371u:
                    typeName = "JoypadCommandBarButtonTemplate";
                    result = global::Recovered.JoypadCommandBarButtonTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1887864852u:
                    typeName = "UIJoypadCommandBarStances";
                    result = global::Recovered.UIJoypadCommandBarStancesCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 1931769760u:
                    typeName = "JoypadCommandBarTemplate";
                    result = global::Recovered.JoypadCommandBarTemplateCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2170044043u:
                    typeName = "UIJoypadCommandBarTopMenu";
                    result = global::Recovered.UIJoypadCommandBarTopMenuCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 2742786440u:
                    typeName = "UIJoypadCommandBarPlayerPowers";
                    result = global::Recovered.UIJoypadCommandBarPlayerPowersCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3423839194u:
                    typeName = "UIJoypadCommandBarButtonBuild";
                    result = global::Recovered.UIJoypadCommandBarButtonBuildCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3645205636u:
                    typeName = "UIJoypadCommandBarControlGroups";
                    result = global::Recovered.UIJoypadCommandBarControlGroupsCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                case 3901433122u:
                    typeName = "UIJoypadCommandBarSingleUnit";
                    result = global::Recovered.UIJoypadCommandBarSingleUnitCompiler.ProcessInstance(compilationInstance, platform);
                    break;
                default: throw new NotSupportedException("XmlPriority supports only its generated recovered scope; excluded roots remain unsupported.");
            }
            if (!String.IsNullOrEmpty(trace))
            {
                lock (TraceGate)
                {
                    Directory.CreateDirectory(trace);
                    string stem = Guid.NewGuid().ToString("N");
                    string path = Path.Combine(trace, stem);
                    File.WriteAllText(path + ".xml", instance.Node.OuterXml);
                    File.WriteAllBytes(path + ".bin", result.InstanceData);
                    File.WriteAllBytes(path + ".relo", result.RelocationData);
                    File.WriteAllBytes(path + ".imp", result.ImportsData);
                    System.Xml.XmlAttribute id = instance.Node.Attributes["id"];
                    File.AppendAllText(Path.Combine(trace, "calls.tsv"), typeName + "\t" +
                        (id == null ? "" : id.Value) + "\t" + stem + "\t" +
                        ((int)platform).ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
                }
            }
            return result;
        }
    }
}




