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
class XmlsoundmapsOracle {
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"AnimationSoundSettings","MiscAudio","LargeGroupAudioMap"};
    static readonly uint[] TypeIds={1302035577u,2070603733u,534008255u};
    static readonly int[] Sizes={8,384,48};
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","4294967295","4294967296","077","0xff","1e400","-1e400"};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7","MIXEDCase"};
    static readonly string[] Bools={"true","false","TRUE","1","0","t","T"," true","yes","","10"};
    static readonly string[] TimeValues={"0s","1s","250ms","1.25s","0.5m","2d1s","-1s","1s junk","2ms","1000ms","garbage","","1e30s","2147483648ms","25,5s"};
    static readonly string[] ModelFlagValues={"ABOUT_TO_HIT","ACCELERATE","ACTIVELY_BEING_CONSTRUCTED","ACTIVELY_CONSTRUCTING","AFFECTED_BY_EMP","AFLAME","AIM_FAR","AIM_HIGH","AIM_LOW","AIM_NEAR","AIM_STRAIGHT","AIR_POWER_EFFECT","ALL","ALTERNATE_FORMATION","ARMORSET_ALTERNATE_FORMATION","ARMORSET_ELITE","ARMORSET_HERO","ARMORSET_MOUNTED","ARMORSET_PLAYER_UPGRADE","ARMORSET_PLAYER_UPGRADE_2","ARMORSET_PLAYER_UPGRADE_3","ARMORSET_UNBESIEGEABLE","ARMORSET_VETERAN","ARMORSET_WEAK_VERSUS_BASEDEFENSES","ATTACHED","ATTACKING","ATTACKING_POSITION","ATTACKING_STRUCTURE","ATTACK_MOVING","AWAITING_CONSTRUCTION","BACKCRUSHED","BACKING_UP","BASE_BUILD","BEING_MAULED","BETWEEN_FIRING_SHOTS_A","BETWEEN_FIRING_SHOTS_B","BETWEEN_FIRING_SHOTS_C","BETWEEN_FIRING_SHOTS_D","BETWEEN_FIRING_SHOTS_E","BUILD_PLACEMENT_CURSOR","BUILD_PLACEMENT_HINT","BUILD_VARIATION_ONE","BUILD_VARIATION_TWO","BURNED","BURNINGDEATH","BURNT_MODEL","BURNT_TEXTURE","CANCEL_CAPTURE","CAPTURED","CAPTURE_0","CAPTURE_100","CAPTURE_25","CAPTURE_50","CAPTURE_75","CAPTURING","CARRYING","CHARGING","CHRONORIFT","CLIMBING","COLLAPSING","COMBINED_CHILD","COMBINED_PARENT","COMING_OUT_OF_FACTORY","CONTINUOUS_FIRE_FAST","CONTINUOUS_FIRE_MEAN","CONTINUOUS_FIRE_SLOW","COVER","DAMAGED","DEATH_1","DEATH_10","DEATH_11","DEATH_12","DEATH_13","DEATH_14","DEATH_15","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEATH_6","DEATH_7","DEATH_8","DEATH_9","DEBUG","DECAY","DECELERATE","DEFLECT_SPECIAL_POWER","DEPLOYED","DESTROYED_BACK","DESTROYED_FRONT","DESTROYED_LEFT","DESTROYED_RIGHT","DESTROYED_WEAPON","DESTROYED_WHILST_BEING_CONSTRUCTED","DISGUISED","DIVING","DOCKING","DOCKING_ACTIVE","DOCKING_BEGINNING","DOCKING_ENDING","DOCKING_EXTRACT","DOCKING_FILL","DOCKING_MOVEBACK","DOCKING_PRE_DOCK","DOCKING_RETURN","DOOR_1_CLOSING","DOOR_1_OPENING","DOOR_1_WAITING_OPEN","DOOR_1_WAITING_TO_CLOSE","DOOR_2_CLOSING","DOOR_2_OPENING","DOOR_2_WAITING_OPEN","DOOR_2_WAITING_TO_CLOSE","DOOR_3_CLOSING","DOOR_3_OPENING","DOOR_3_WAITING_OPEN","DOOR_3_WAITING_TO_CLOSE","DOOR_4_CLOSING","DOOR_4_OPENING","DOOR_4_WAITING_OPEN","DOOR_4_WAITING_TO_CLOSE","DOOR_HELIPAD_CLOSING","DOOR_HELIPAD_OPENING","DOOR_HELIPAD_WAITING_OPEN","DOOR_HELIPAD_WAITING_TO_CLOSE","DRAFTED","DYING","DYING_WASATTACKING","DYING_WASMOVING","EATING","EMOTION_AFRAID","EMOTION_ALERT","EMOTION_AMUSED","EMOTION_BRACE_FOR_BEING_CRUSHED","EMOTION_CELEBRATING","EMOTION_CHEER_FOR_ABOUT_TO_CRUSH","EMOTION_COWER","EMOTION_DISSIDENT","EMOTION_DOOM","EMOTION_GUNG_HO","EMOTION_LOOK_TO_SKY","EMOTION_MORALE_HIGH","EMOTION_MORALE_LOW","EMOTION_PANIC","EMOTION_POINTING","EMOTION_TAUNTING","EMOTION_TERROR","EMOTION_UNCONTROLLABLY_AFRAID","ENGAGED","EXPLODED_BOUNCING","EXPLODED_FLAILING","FIRING_A","FIRING_B","FIRING_C","FIRING_D","FIRING_E","FIRING_OR_PREATTACK_A","FIRING_OR_PREATTACK_B","FIRING_OR_PREATTACK_C","FIRING_OR_PREATTACK_D","FIRING_OR_PREATTACK_E","FIRING_OR_RELOADING_A","FIRING_OR_RELOADING_B","FIRING_OR_RELOADING_C","FIRING_OR_RELOADING_D","FIRING_OR_RELOADING_E","FLOODED","FLYING","FORMATION_PREVIEW","FREEFALL","FRONTCRUSHED","FROZEN","GARRISONED","GUARDING","HARVEST_ACTION","HARVEST_PREPARATION","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIGH_TECH_EFFECT","HIT_LEVEL_1","HIT_LEVEL_2","HIT_LEVEL_3","HIT_REACTION","HORDE_EMPTY","IMPENDING_DOOM","INFILTRATED_DISABLE","INFILTRATED_ENERGY","INFILTRATED_RADAR_FREEZE","INFILTRATED_RESET","INFILTRATED_STEAL_MONEY","INFILTRATED_VISION","INFILTRATED_VISION_UNITS","INSIDE_GARRISON","INVALID","INVISIBLE_CAMOUFLAGE","INVISIBLE_STEALTH","INVULNERABLE","IRONCURTAIN","JETAFTERBURNER","JETEXHAUST","JUST_BUILT","LANDING","LAUNCHED","LEASHED_RETURNING","LEVELED","LOADED","LOCOMOTOR_BURNINGDEATH","LOCOMOTOR_CHANGING_FIRINGARC","LOCOMOTOR_COMBO","LOCOMOTOR_COMBO2","LOCOMOTOR_COMBO3","LOCOMOTOR_CONTAINED","LOCOMOTOR_ENRAGED","LOCOMOTOR_FREEFALL","LOCOMOTOR_MOUNTED","LOCOMOTOR_NORMAL_UPGRADED","LOCOMOTOR_PANIC","LOCOMOTOR_SCARED","LOCOMOTOR_SUPERSONIC","LOCOMOTOR_TAXIING","LOCOMOTOR_WALL_SCALING","LOCOMOTOR_WANDER","MARCHING","MARKED_FOR_HUNT_TACTIC","MARKED_FOR_NO_SKIRMISH_RECRUIT","MONEY_STORED_AMOUNT_1","MONEY_STORED_AMOUNT_2","MONEY_STORED_AMOUNT_3","MONEY_STORED_AMOUNT_4","MOUNTED","MOVING","MOVING_OUT_OF_THE_WAY","NIGHT","OVER_WATER","PACKING","PACKING_TYPE_1","PACKING_TYPE_2","PACKING_TYPE_3","PACKING_TYPE_4","PACKING_TYPE_5","PACKING_TYPE_6","PANICKING","PARACHUTE_LAND","PARACHUTING","PARALYZED","PARKINGPLACE_1_DOOR_CLOSED","PARKINGPLACE_1_DOOR_CLOSING","PARKINGPLACE_1_DOOR_OPEN","PARKINGPLACE_1_DOOR_OPENING","PARKINGPLACE_2_DOOR_CLOSED","PARKINGPLACE_2_DOOR_CLOSING","PARKINGPLACE_2_DOOR_OPEN","PARKINGPLACE_2_DOOR_OPENING","PARKINGPLACE_3_DOOR_CLOSED","PARKINGPLACE_3_DOOR_CLOSING","PARKINGPLACE_3_DOOR_OPEN","PARKINGPLACE_3_DOOR_OPENING","PARKINGPLACE_4_DOOR_CLOSED","PARKINGPLACE_4_DOOR_CLOSING","PARKINGPLACE_4_DOOR_OPEN","PARKINGPLACE_4_DOOR_OPENING","PARKINGPLACE_HELIPAD_DOOR_CLOSED","PARKINGPLACE_HELIPAD_DOOR_CLOSING","PARKINGPLACE_HELIPAD_DOOR_OPEN","PARKINGPLACE_HELIPAD_DOOR_OPENING","PARTIALLY_CONSTRUCTED","PASSENGER","PASSENGER_VARIATION_1","PASSENGER_VARIATION_2","PASSENGER_VARIATION_3","PASSENGER_VARIATION_4","PASSENGER_VARIATION_5","PHANTOM_STRUCTURE","PLAYER_POWER_1","POST_COLLAPSE","POST_RUBBLE","POWER_PLANT_UPGRADED","POWER_PLANT_UPGRADING","PREATTACK_A","PREATTACK_B","PREATTACK_C","PREATTACK_D","PREATTACK_E","PREORDER","PREPARING","PRIMARY_FORMATION","PRONE","RADAR_EXTENDING","RADAR_UPGRADED","RAISING_FLAG","RAPPELLING","REACT_1","REACT_2","REACT_3","REACT_4","REACT_5","REACT_6","REALLYDAMAGED","RELOADING_A","RELOADING_B","RELOADING_C","RELOADING_D","RELOADING_E","REPAIRING_DISABLED","RESURRECTED","RIDER1","RIDER2","RIDER3","RIDER4","RIDER5","RIDER6","RIDER7","RIDER8","RIDERLESS","RUBBLE","RUNNING_OFF_MAP","SAIL_BLOWN_LEFT","SAIL_BLOWN_RIGHT","SAIL_FLAPPING","SCALING_WALL","SCALING_WALL_HORDE","SELECTED","SHRINK_EFFECT","SIEGE_CONTAIN","SMOLDERING","SNOW","SOLD","SPECIALPOWER1_READY","SPECIALPOWER2_READY","SPECIALPOWER3_READY","SPECIALPOWER4_READY","SPECIALPOWER5_READY","SPECIALPOWER6_READY","SPECIALPOWER7_READY","SPECIALPOWER8_READY","SPECIALPOWER9_READY","SPECIAL_CHEERING","SPECIAL_DAMAGED","SPECIAL_ENEMY_NEAR","SPECIAL_POWER_1","SPECIAL_POWER_2","SPECIAL_POWER_3","SPECIAL_WEAPON_FIVE","SPECIAL_WEAPON_FOUR","SPECIAL_WEAPON_ONE","SPECIAL_WEAPON_SIX","SPECIAL_WEAPON_THREE","SPECIAL_WEAPON_TWO","SPLATTED","START_CAPTURE","STRUCTURE_UNPACKING","STUNNED","STUNNED_FLAILING","STUNNED_STANDING_UP","SUCKED_UP_HIGH","SUPPRESSED","SWAPPING_TO_WEAPONSET_1","SWAPPING_TO_WEAPONSET_2","SWAPPING_TO_WEAPONSET_3","SWIMMING","SWOOPING","TAKING_OFF","THROWN_PROJECTILE","TIBERIUM_CRYSTAL_TYPE1","TIBERIUM_CRYSTAL_TYPE2","TIBERIUM_CRYSTAL_TYPE3","TIBERIUM_CRYSTAL_TYPE4","TIBERIUM_CRYSTAL_TYPE5","TIBERIUM_GROWING","TOPPLED","TRANSPORT_MOVING","TRANSPORT_STOPPED","TURN_LEFT","TURN_LEFT_HIGH_SPEED","TURN_RIGHT","TURN_RIGHT_HIGH_SPEED","TURRET_ANGLE_0","TURRET_ANGLE_180","TURRET_ANGLE_270","TURRET_ANGLE_90","TURRET_ROTATE","UNCONTROLLABLE","UNDERPOWERED","UNIT_ACTIVELY_BEING_CONSTRUCTED","UNPACKING","UPGRADED_ARMOR","USER_1","USER_10","USER_2","USER_3","USER_4","USER_5","USER_6","USER_7","USER_8","USER_9","USING_COMBO_LOCOMOTOR","USING_SPECIAL_ABILITY","USING_WEAPON_A","USING_WEAPON_B","USING_WEAPON_C","USING_WEAPON_D","USING_WEAPON_E","VEHICLE_CRUSH_CENTER","VEHICLE_CRUSH_LEFT","VEHICLE_CRUSH_RIGHT","WADING","WALKING","WALL_SEGMENT","WANDER","WEAPONLOCK_PRIMARY","WEAPONLOCK_QUATERNARY","WEAPONLOCK_QUINARY","WEAPONLOCK_SECONDARY","WEAPONLOCK_TERTIARY","WEAPONSET_CLOSE_RANGE","WEAPONSET_CONTAINED","WEAPONSET_CONTESTING_BUILDING","WEAPONSET_ELITE","WEAPONSET_GARRISONED","WEAPONSET_HERO","WEAPONSET_MOUNTED","WEAPONSET_PASSENGER_TYPE_ONE","WEAPONSET_PASSENGER_TYPE_TWO","WEAPONSET_PLAYER_UPGRADE","WEAPONSET_RIDER1","WEAPONSET_RIDER2","WEAPONSET_RIDER3","WEAPONSET_RIDER4","WEAPONSET_SPECIAL_ONE","WEAPONSET_SPECIAL_TWO","WEAPONSET_SPECIAL_UPGRADE","WEAPONSET_TOGGLE_1","WEAPONSET_TOGGLE_2","WEAPONSET_TOGGLE_3","WEAPONSET_VETERAN","WEAPONSLOTID_01","WEAPONSLOTID_02","WEAPONSLOTID_03","WEAPONSLOTID_04","WEAPONSLOTID_05","WEAPONSLOTID_06","WEAPONSLOTID_07","WEAPONSLOTID_08","WEAPONSLOTID_09","WEAPONSLOTID_10","WEAPONSTATE_CLOSE_RANGE","WEAPONSTATE_CONTAINED","WEAPONSTATE_ONE","WEAPONSTATE_THREE","WEAPONSTATE_TWO","WEAPON_TOGGLING","WORLD_BUILDER","UNKNOWN","moving",""," ","+MOVING","-MOVING","MOVING MOVING","MOVING -MOVING","ALL -MOVING","-ALL MOVING","all","NONE","MOVING\tATTACKING"};
    static readonly string[] ObjectStatusValues=new string[]{"ABSORBED_DAMAGE","ADVANCED_MISSILE_PACKS","AFLAME","AIRBORNE_TARGET","AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE","AIR_LOCOMOTOR_ACTIVE","ANTI_GARRISON","ATTACHED","ATTACKING_GARRISONED_STRUCTURE","BOOBY_TRAPPED","BOOBY_TRAP_EXPLODE","BOOKENDING","BRIDGE_IMPASSABLE","BROADCASTING_INVISIBILITY","BUILD_BEING_CANCELED","BURNED","CANNOT_BE_SOLD","CANNOT_LAND_AT_AIRFIELD","CAN_ATTACK","CAN_ATTACK_WHILE_STEALTHED","CAN_CONTEST_GARRISON","CAN_ENTER_ANYTHING","CAN_NOT_WALK_ON","CAN_STEALTH","CAN_STEALTH_FROM_PRODUCER","CARRYING_FLAG","CHANTING","CHARGING_BASE_DEFENSES","CHARGING_WEAPON","CLEARED_FOR_LANDING","CLONED","COMBINED_ATTACHED","COMBINED_CHILD","COMBINED_PARENT","COMMAND_BUTTON_TOGGLED","CONTAINER_OCCUPIED","CONTESTED","CONTESTING_BUILDING","DAMAGED","DEATH_1","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEFLECT_INCOMING_FIRE","DELAYED_ENTER_STRUCTURE","DEPART_FROM_BOARDING","DEPLOYED","DEPLOYING","DESTROYED","DETECTED","DOCKING","DOES_CONTAIN_TIBERIUM","DONT_CLEAR_FOR_BUILD","DO_NOT_AVOID_TALLBUILDING","DO_NOT_PICK_ME","DO_NOT_SCORE","ENABLE_TRACER_DRAW","ENCLOSED","END_FIRE_STATE","ENRAGED","ENTERING_GOAL","EXCEEDED_PATH_NEIGHBOR_COUNT","EXECUTING_STORED_COMMAND","EXITING_COMBINED","EXPLOSIVES_ATTACHED","EXPLOSIVES_DETONATING","FLEE_OFF_MAP","FOLLOWING_THROUGH","FORCE_ATTACKING","FORCE_ATTACK_MOVING","GARRISONED","GARRISON_CAN_STEALTH","GENERIC_TOGGLE_STATE","GRAPPLE_UPDATE_ACTIVE","GRAPPLE_UPDATE_BEING_MOVED","GROUND_LOCOMOTOR_ACTIVE","HARVESTING","HAS_SECONDARY_DAMAGE","HAS_TIBERIUM_GROWTH_MOD","HAS_TIBERIUM_UPGRADE","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIJACKED","HIT_SHIELD_SPHERE","HORDE_MEMBER","IGNORE_AI_COMMAND","IGNORING_POWER_DOWN","IGNORING_STEALTH","IMMOBILE","IMMOBILE_ALLOW_ROTATE","IMMUNE_TO_BARK","INAUDIBLE","INHERITED_FROM_ALLY_TEAM","INSIDE_GARRISON","INVISIBLE_DETECTED","INVISIBLE_DETECTED_BY_FRIEND","IN_COVER","IN_FORMATION_TEMPLATE","IN_NANOHIVE","IN_SHIELD_SPHERE","IN_SPIDER_HOLE","IN_STASIS","IRRADIATED","IS_AIMING_WEAPON","IS_ATTACKING","IS_BEING_DRAGGED","IS_BEING_HARVESTED","IS_BRAKING","IS_ENGAGED","IS_FIRING_WEAPON","IS_HIDEOUT","IS_LEAVING_FACTORY","IS_MELEE_ATTACKING","IS_MOVING_TO_RALLY_POINT","IS_RELOADING_WEAPON","LANDING_IN_PROGRESS","LEASHED_RETURNING","LEECHED_TARGET_ACTIVE","LIFTED_INTO_AIR","LINE_SEGMENT_DAMAGE_ACTIVE","LOADED_FROM_MAP","MAGNETIZED","MARCH_OF_DEATH","MATCH_TARGETS_SPEED","MOVING_TO_DISMOUNT","NEXT_MOVE_IS_FORCE_ATTACK_MOVE","NEXT_MOVE_IS_REVERSE","NON_AUTOACQUIRABLE","NOT_IN_WORLD","NO_ATTACK","NO_ATTACK_FROM_AI","NO_AUTO_ACQUIRE","NO_BRIBE","NO_COLLISIONS","NO_HERO_PROPERTIES","NO_REFUND","NO_SHADOW","NO_SPECIAL_ABILITY","NO_SQUISHCOLLIDE_DELAY","OCLMONITOR_COMPLETED_TASK","OCLMONITOR_MONITOR_RELEASED","OUT_OF_PHASE","OVERCHARGING_WEAPON","OVER_WATER","PARACHUTING","PARKED_AT_AIRFIELD","PAUSING","PENDING_CONSTRUCTION","PHANTOM_STRUCTURE","PLACED_BY_PLAYER","PLAYER_POWER_1","PLAYER_POWER_2","PLAYER_POWER_3","PLAYER_POWER_4","PLAYER_POWER_5","POINT_DEFENSE_DRONE_ATTACHED","PORTER_TAGGED","POWERED_DOWN_EMP","PREEMPT_PRE_ATTACK","RAMPAGING","REALLYDAMAGED","RECONSTRUCTING","REPAIR_ALLIES_WHEN_IDLE","REPULSOR","RESET_GOAL_POSITION","RIDER_IS_PILOT","RIFT_OCCUPIED","RIOT_SHIELDED","RUBBLE","RUNNING_DOWN_FROM_BEHIND","SCARED_CIVILIAN_CAR","SCRAMBLED","SHIELDBODY_ENABLED","SHROUD_REVEAL_TO_ALL","SHRUNK","SINKING","SKIRMISH_AI_DO_NOT_ATTACK","SKIRMISH_AI_DO_NOT_TAKE","SOLD","SPECIALABILITY_ACTIVE","SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING","SPECIAL_ARMOR_ACTIVE","STAND_GROUND","STEALTHED","STEAL_NEXT_UNIT_TRAPPED","SUBMERGED","SURFACED","SURFACED_IMMOBILE","SWITCHED_WEAPONS","TAGGED","TARGETED_FOR_REPAIR","TEMPORARILY_DEFECTED","TIBERIUM_VIBRATING","TOPPLING","TRANSFORMATION_TOGGLE_STATE","TRANSPORT_MOVING_TO_CONTAIN","UNATTACKABLE","UNCONTROLLABLY_SCARED","UNDERGOING_REPAIR","UNDER_CONSTRUCTION","UNDER_FROZEN","UNDER_IRON_CURTAIN","UNDER_RUSH_ATTACK","UNIT_WANTS_TO_REGARRISON","UNPACKING","UNSELECTABLE","UPDATING_AI","USER_PARALYZED","USER_POWERED_DOWN","USING_ABILITY","VEHICLE_ATTACHED","WAITING_FOR_BOARDING","WALL_SEGMENT","WATER_LOCOMOTOR_ACTIVE","WEAPON_UPGRADED_01","WEAPON_UPGRADED_02","WEAPON_UPGRADED_03","WONT_RIDE_WITH_YOU","UNKNOWN","destroyed","","ALL","all","-ALL","ALL -DESTROYED","-DESTROYED -DESTROYED","DESTROYED -DESTROYED","DESTROYED UNKNOWN","DESTROYED\tHIDDEN"};
    static readonly string[] MiscNames=new string[]{"RadarNotifyHarvesterUnderAttackSound","RadarNotifyStructureUnderAttackSound","RadarNotifyInfiltrationSound","RadarNotifyOnlineSound","RadarNotifyOfflineSound","GenericRadarEvent","BeaconPlacedSound","BeaconPlacementFailed","DefectorTimerTickSound","DefectorTimerDingSound","AllCheerSound","NoCanDoSound","StealthDiscoveredSound","StealthNeutralizedSound","MoneyDepositSound","MoneyWithdrawSound","LowPower","SufficientPower","BuildingDisabled","BuildingReenabled","VehicleDisabled","VehicleReenabled","SplatterVehiclePilotsBrain","UnitPromoted","RepairSparks","EnterCloseCombat","ExitCloseCombat","IncomingChatNotification","PrivateMessageNotification","BuddyMessageNotification","EnabledHotKeyPressed","DisabledHotKeyPressed","DisabledButtonClicked","LowLODShellMusic","HighLODShellMusic","ScoreScreenMusic","ShellMapLoadMusic","FullScreenSubMenuMusic","SaveFileLoadMusic","CreditsMusic","VolumeSampleMusic","VolumeSampleSoundFX","VolumeSampleVoice","VolumeSampleAmbient","VolumeSampleMovie","MissionBriefingCharacterClick","ComboBoxClick","GameSpyCommunicatorOpen","MPSecondsCountdownBeep","RIFThingTemplateReloadedSound","RIFObjectsRefreshedSound","FastForwardModeOn","FastForwardModeOff","RallyPointSet","UnableToSetRallyPoint","PlanningModeOrderGiven","BuildingPlacementSound","BadBuildingPlacementSound","WallPlacementSound","AircraftWheelScreech","LockonTickSound","TargetObjectWithSpecialPowerSound","ObjectiveCompletedSound","CameraBookmarkPlacedSound","GuiFixedButtonClickedSound","GuiBuildQueueButtonClickedSound","GuiSelectionRefinementButtonClickedSound","GuiFactorySelectionButtonClickedSound","GuiTogglePowerObjectClickedSound","GuiToggleRepairObjectClickedSound","GuiSellObjectClickedSound","GuiCommandButtonClickedSound","GuiCommandButtonRightClickedSound","GuiControlGroupSetSound","GuiDisabledCommandButtonClickedSound","GuiDisabledCommandButtonRightClickedSound","GuiPlayerPowerButtonClickedSound","GuiPauseDialogButtonClickedSound","GuiPauseDialogButtonMouseOverSound","GuiPauseDialogButtonMouseOutSound","GuiYesNoDialogButtonClickedSound","GuiYesNoDialogButtonMouseOverSound","GuiYesNoDialogButtonMouseOutSound","GuiTextChatDialogButtonClickedSound","GuiTextChatDialogButtonMouseOverSound","GuiTextChatDialogButtonMouseOutSound","GuiTechStoreDialogOpenButtonClicked","GuiTechStoreDialogOpenButtonMouseOver","GuiTechStoreDialogPowerButtonSelected","GuiTechStoreDialogPowerButtonDeselected","GuiTechStoreDialogPowerButtonMouseOver","GuiTechStoreDialogConfirmButtonClicked","GuiTechStoreDialogCancelButtonClicked","GuiTechStoreDialogConfirmButtonMouseOver","GuiTechStoreDialogCancelButtonMouseOver"};
    static readonly string[] Strings={"","a","A","abc","ABC","abcd","abcde","MixedCase","资源","é","😀","e\u0301","A&B<key>","a\0tail","\0leading","资源\0尾部"};
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
        uint previous=0;for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at+4<=instance.Length,label+" field bounds");Check(i==0||at>previous,label+" sorted unique offsets");previous=at;if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlsoundmapsCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlsoundmapsCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}

    static XmlDocument Set(string root,string childPath,string attr,string value) {
        XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;
        if(childPath.Length!=0)foreach(string part in childPath.Split('/')){XmlElement c=d.CreateElement(part,NS);node.AppendChild(c);node=c;}
        if(attr=="#text")node.InnerText=value;else node.SetAttribute(attr,value);
        return d;
    }
    static ushort Short(byte[] b,int at,bool big){return big?(ushort)((b[at]<<8)|b[at+1]):BitConverter.ToUInt16(b,at);}
    static void Abi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer animation=Test("ABI/animation-signed",Root("AnimationSoundSettings","MinMicrophoneDistanceToDirty='-2'",""),p);
        Check(animation.InstanceData.Length==8&&Word(animation.InstanceData,4,big)==0xfffffffeu,"animation signed offset");
        StringBuilder attrs=new StringBuilder();for(int i=0;i<MiscNames.Length;++i)attrs.Append(MiscNames[i]+"='Audio\\"+i+"' ");
        AssetBuffer misc=Test("ABI/misc-all-95",Root("MiscAudio",attrs.ToString(),""),p);
        Check(misc.InstanceData.Length==384&&misc.ImportsData.Length==384&&misc.RelocationData.Length==0,"Misc exact size and95imports/sentinel");
        for(int i=0;i<95;++i){Check(Word(misc.InstanceData,4+4*i,big)==i+1,"Misc exact reference word");Check(Word(misc.ImportsData,4*i,big)==4+4*i,"Misc exact import offset");}
        AssetBuffer defaults=Test("ABI/Large-default",Root("LargeGroupAudioMap","",""),p);
        Check(defaults.InstanceData.Length==48&&Word(defaults.InstanceData,20,big)==1000&&Word(defaults.InstanceData,24,big)==0x3f800000u&&Word(defaults.InstanceData,28,big)==0x41a00000u,"Large default root/size/time/velocity");
        Check(defaults.InstanceData[44]==1&&defaults.InstanceData[45]==0&&defaults.InstanceData[46]==0&&defaults.InstanceData[47]==0,"Large bool and padding");
        string flags="RequiredModelConditionFlags='MOVING' ExcludedModelConditionFlags='ALL' RequiredObjectStatusBits='UNKNOWN' ExcludedObjectStatusBits='DESTROYED' ";
        AssetBuffer full=Test("ABI/Large-full",Root("LargeGroupAudioMap",flags+"Size='0x12345678' StartThreshold='0x1234' StopThreshold='0x5678' HandOffModeTime='250ms' MaximumAudioSpeed='25' IgnoreStealthedUnits='false'",
            "<Sound Name='A' Sound='Audio\\7'><Key>A</Key><Key>a</Key><Duck AudioMap='Map\\13' SoundRef='A' VolumeMultiplier='25%'/></Sound><Sound Name='a' Sound='Audio\\11'><Key/></Sound>"),p);
        Check(full.InstanceData.Length==296,"Large full independent size");
        int[] at={4,8,12,16,36,236,244,260};uint[] targets={48u,108u,168u,196u,224u,272u,280u,292u};
        for(int i=0;i<at.Length;++i)Check(Word(full.InstanceData,at[i],big)==targets[i],"Large allocation order andpointer targets");
        Check(Word(full.InstanceData,64,big)==0x8000u&&Word(full.InstanceData,192,big)==64u&&Word(full.InstanceData,196,big)==1,"Large model/status known andunknown bits");
        for(int i=0;i<15;++i)Check(Word(full.InstanceData,108+4*i,big)==UInt32.MaxValue,"ALL preserves model padding");
        Check(Word(full.InstanceData,20,big)==0x12345678u&&Short(full.InstanceData,40,big)==0x1234&&Short(full.InstanceData,42,big)==0x5678,"Large uint and ushort exact offsets");
        Check(Word(full.InstanceData,24,big)==0x3e800000u&&Word(full.InstanceData,28,big)==0x40a00000u&&full.InstanceData[44]==0,"Large time/velocity/bool exact offsets");
        Check(Word(full.InstanceData,32,big)==2&&Word(full.InstanceData,232,big)==2&&Word(full.InstanceData,240,big)==1&&Word(full.InstanceData,256,big)==1,"Large nested counts");
        Check(Word(full.InstanceData,224,big)==1214652683u&&Word(full.InstanceData,248,big)==1214652683u&&Word(full.InstanceData,284,big)==1214652683u,"Name andSoundRef lower-case typed hash");
        Check(Word(full.InstanceData,272,big)==3701560732u&&Word(full.InstanceData,276,big)==1214652683u&&Word(full.InstanceData,292,big)==0,"Keys raw case-preserving string hash");
        Check(Word(full.InstanceData,288,big)==0x3e800000u,"Duck percentage");
        Check(full.ImportsData.Length==16&&Word(full.ImportsData,0,big)==228&&Word(full.ImportsData,4,big)==252&&Word(full.ImportsData,8,big)==280,"Sound/Duck imports sorted byfield location");
        Check(Word(full.InstanceData,228,big)==8&&Word(full.InstanceData,252,big)==12&&Word(full.InstanceData,280,big)==14,"Sound/Duck import indices");
        Check(full.RelocationData.Length==36,"eight relocations plus sentinel");
        AssetBuffer emptyFlags=Test("ABI/empty-flags",Root("LargeGroupAudioMap","RequiredModelConditionFlags='' ExcludedModelConditionFlags='' RequiredObjectStatusBits='' ExcludedObjectStatusBits=''",""),p);
        Check(emptyFlags.InstanceData.Length==224&&Word(emptyFlags.InstanceData,4,big)==48&&Word(emptyFlags.InstanceData,8,big)==108&&Word(emptyFlags.InstanceData,12,big)==168&&Word(emptyFlags.InstanceData,16,big)==196,"present-empty flags allocate all four nullable blocks");
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i) {
            AssetBuffer a=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(a.InstanceData.Length==Sizes[i],"independent root size");
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown root attr",Root(Types[i],"Unknown='1'",""));
            Reject("qualified root attr",Root(Types[i],"xmlns:q='urn:other' q:TypeId='1'",""));
            Reject("unknown child",Root(Types[i],"","<Unknown/>"));
            Reject("foreign child",Root(Types[i],"","<Sound xmlns='urn:other'/>"));
            Reject("root text",Root(Types[i],"","payload"));
        }
        foreach(string value in Numbers)TestDoc("animation-numeric/"+value,Set("AnimationSoundSettings","","MinMicrophoneDistanceToDirty",value),p);
        List<string> references=new List<string>(Refs);references.AddRange(new string[]{"Name\0\\7","Name\\7\0tail","Name\\2147483647","Name\\-2147483648","Name\\99999999999999999999"});
        foreach(string name in MiscNames)foreach(string value in references)TestDoc("Misc/"+name+"/"+value.Length,Set("MiscAudio","",name,value),p);
        foreach(string name in new string[]{"RequiredModelConditionFlags","ExcludedModelConditionFlags"})foreach(string value in ModelFlagValues)TestDoc("model/"+name+"/"+value,Set("LargeGroupAudioMap","",name,value),p);
        foreach(string name in new string[]{"RequiredObjectStatusBits","ExcludedObjectStatusBits"})foreach(string value in ObjectStatusValues)TestDoc("status/"+name+"/"+value,Set("LargeGroupAudioMap","",name,value),p);
        foreach(string name in new string[]{"Size","StartThreshold","StopThreshold","MaximumAudioSpeed"})foreach(string value in Numbers)TestDoc("large-numeric/"+name+"/"+value,Set("LargeGroupAudioMap","",name,value),p);
        foreach(string name in new string[]{"StartThreshold","StopThreshold"})foreach(string value in new string[]{"65534","65535","65536","65537","-65535","-65536","0xffff","0x10000","0x10001","0200000"})
            TestDoc("ushort-wrap/"+name+"/"+value,Set("LargeGroupAudioMap","",name,value),p);
        foreach(string value in TimeValues)TestDoc("large-time/"+value,Set("LargeGroupAudioMap","","HandOffModeTime",value),p);
        foreach(string value in Bools)TestDoc("large-bool/"+value,Set("LargeGroupAudioMap","","IgnoreStealthedUnits",value),p);
        foreach(string value in references) {
            TestDoc("sound-reference/"+value.Length,Set("LargeGroupAudioMap","Sound","Sound",value),p);
            TestDoc("duck-reference/"+value.Length,Set("LargeGroupAudioMap","Sound/Duck","AudioMap",value),p);
        }
        foreach(string value in Strings) {
            TestDoc("sound-name/"+value.Length,Set("LargeGroupAudioMap","Sound","Name",value),p);
            TestDoc("duck-soundref/"+value.Length,Set("LargeGroupAudioMap","Sound/Duck","SoundRef",value),p);
            TestDoc("raw-key/"+value.Length,Set("LargeGroupAudioMap","Sound/Key","#text",value),p);
        }
        foreach(string value in Numbers)TestDoc("duck-percent/"+value,Set("LargeGroupAudioMap","Sound/Duck","VolumeMultiplier",value),p);
        foreach(string value in new string[]{"MOVING\0ATTACKING","ALL\0-MOVING","MOVING\vATTACKING\fSNOW"})TestDoc("native-model-separators",Set("LargeGroupAudioMap","","RequiredModelConditionFlags",value),p);
        foreach(string value in new string[]{"DESTROYED\0HIDDEN","ALL\0-DESTROYED","HIDDEN\vDESTROYED"})TestDoc("native-status-separators",Set("LargeGroupAudioMap","","RequiredObjectStatusBits",value),p);
        foreach(int count in new int[]{0,1,2,3,27,28,29,64,257}) {
            StringBuilder sounds=new StringBuilder(),keys=new StringBuilder(),ducks=new StringBuilder();
            for(int i=0;i<count;++i) {
                sounds.Append("<Sound Name='Mixed"+i+"' Sound='Audio\\"+i+"'><Key>AbC"+i+"</Key><Duck AudioMap='Map\\"+i+"' SoundRef='Mixed"+i+"' VolumeMultiplier='50%'/></Sound>");
                keys.Append("<Key>AbC"+i+"</Key>");ducks.Append("<Duck AudioMap='Map\\"+i+"' SoundRef='Mixed"+i+"' VolumeMultiplier='-12.5%'/>");
            }
            AssetBuffer many=Test("sound-list/"+count,Root("LargeGroupAudioMap","",sounds.ToString()),p);bool big=p!=TargetPlatform.Win32;
            Check(many.InstanceData.Length==48+40*count&&Word(many.InstanceData,32,big)==count,"nested Sound list size andcount");
            AssetBuffer keylist=Test("key-list/"+count,Root("LargeGroupAudioMap","","<Sound>"+keys+"</Sound>"),p);
            Check(keylist.InstanceData.Length==72+4*count&&Word(keylist.InstanceData,56,big)==count,"key list size/count");
            AssetBuffer ducklist=Test("duck-list/"+count,Root("LargeGroupAudioMap","","<Sound>"+ducks+"</Sound>"),p);
            Check(ducklist.InstanceData.Length==72+12*count&&Word(ducklist.InstanceData,64,big)==count,"duck list size/count");
        }
        Test("out-of-XML-order-lists",Root("LargeGroupAudioMap","","<Sound><Duck/><Key>A</Key><Duck/><Key>B</Key></Sound>"),p);
        Test("nested TypeId",Root("LargeGroupAudioMap","","<Sound TypeId='1'><Key TypeId='1'>AbC</Key><Duck TypeId='1'/></Sound>"),p);
        foreach(string path in new string[]{"Sound","Sound/Key","Sound/Duck"}) {
            Reject("unknown nested attr",Set("LargeGroupAudioMap",path,"Unknown","1").OuterXml);
            XmlDocument foreign=Set("LargeGroupAudioMap",path,"TypeId","1");XmlNode nested=foreign.DocumentElement;
            foreach(string part in path.Split('/'))nested=nested.FirstChild;
            XmlAttribute qualified=foreign.CreateAttribute("q","Unknown","urn:q");qualified.Value="1";nested.Attributes.Append(qualified);
            Reject("foreign nested attr",foreign.OuterXml);
        }
        Reject("Sound text",Root("LargeGroupAudioMap","","<Sound>payload</Sound>"));
        Reject("Sound unknown child",Root("LargeGroupAudioMap","","<Sound><Unknown/></Sound>"));
        Reject("Key child",Root("LargeGroupAudioMap","","<Sound><Key><Unknown/></Key></Sound>"));
        Reject("Duck text",Root("LargeGroupAudioMap","","<Sound><Duck>payload</Duck></Sound>"));
        Reject("Duck child",Root("LargeGroupAudioMap","","<Sound><Duck><Unknown/></Duck></Sound>"));
        Reject("unknown root",Root("Unknown","",""));
        bool nullInput=false;try{XmlsoundmapsCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){nullInput=true;}Check(nullInput,"null declaration rejected");++rejections;
        XmlDocument wrong=Parse(Root("MiscAudio","",""));InstanceDeclaration declaration=Declaration(wrong);declaration.Handle.TypeName="AnimationSoundSettings";
        bool mismatch=false;try{XmlsoundmapsCompiler.ProcessInstance(declaration,p);}catch(NotSupportedException){mismatch=true;}Check(mismatch,"root/handle mismatch rejected");++rejections;
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
            Console.WriteLine("PASS Soundmaps fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}

