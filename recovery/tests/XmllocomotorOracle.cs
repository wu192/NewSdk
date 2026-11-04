using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;

class XmllocomotorOracle {
    const string NS = "uri:ea.com:eala:asset";
    sealed class Spec {
        public string Name, Kind; public int Offset;
        public Spec(string n, string k, int o) { Name=n; Kind=k; Offset=o; }
    }
    static readonly Spec[] Fields = {
        new Spec("Surfaces", "Surface", 4),
        new Spec("LookAheadMult", "Float", 8),
        new Spec("MakeTransformNonDirty", "Boolean", 368),
        new Spec("MaxSpeedDamaged", "Percent", 12),
        new Spec("MinSpeed", "Percent", 16),
        new Spec("TurnTimeSeconds", "Time", 20),
        new Spec("TurnTimeDamagedSeconds", "Time", 24),
        new Spec("SlowTurnRadius", "Float", 28),
        new Spec("FastTurnRadius", "Float", 32),
        new Spec("MinRandomTurnRadiusScale", "Float", 36),
        new Spec("MaxRandomTurnRadiusScale", "Float", 40),
        new Spec("TurnThreshold", "Angle", 44),
        new Spec("TurnThresholdHS", "Angle", 48),
        new Spec("AccelerationSeconds", "Time", 52),
        new Spec("Lift", "Percent", 56),
        new Spec("LiftDamaged", "Percent", 60),
        new Spec("BrakingSeconds", "Time", 64),
        new Spec("MinTurnSpeed", "Percent", 68),
        new Spec("PreferredHeight", "Float", 72),
        new Spec("PreferredAttackHeight", "Float", 76),
        new Spec("PreferredHeightDamping", "Float", 80),
        new Spec("PreferredHeightPitchingEpsilon", "Float", 84),
        new Spec("CirclingRadius", "Float", 88),
        new Spec("CirclingSpeed", "Percent", 92),
        new Spec("BehaviorZ", "Behavior", 96),
        new Spec("Appearance", "Appearance", 100),
        new Spec("FormationPriority", "Formation", 104),
        new Spec("ActiveModelConditions", "Model", 108),
        new Spec("ActiveObjectStatus", "Status", 112),
        new Spec("EnteringModelConditions", "Model", 116),
        new Spec("EnteringModelConditionsTime", "Time", 120),
        new Spec("AccDecTrigger", "Float", 124),
        new Spec("WalkDistance", "Float", 128),
        new Spec("MaxTurnWithoutReform", "Angle", 132),
        new Spec("AccelPitchLimit", "Angle", 136),
        new Spec("BounceKick", "Angle", 140),
        new Spec("PitchStiffness", "Float", 144),
        new Spec("RollStiffness", "Float", 148),
        new Spec("PitchDamping", "Float", 152),
        new Spec("RollDamping", "Float", 156),
        new Spec("PitchByZVelCoef", "Float", 160),
        new Spec("YawStiffness", "Float", 164),
        new Spec("YawDamping", "Float", 168),
        new Spec("MaxOverlappedHeight", "Float", 172),
        new Spec("ForwardVelocityPitchFactor", "Float", 176),
        new Spec("LateralVelocityRollFactor", "Float", 180),
        new Spec("ForwardAccelerationPitchFactor", "Float", 184),
        new Spec("LateralAccelerationRollFactor", "Float", 188),
        new Spec("UniformAxialDamping", "Float", 192),
        new Spec("TurnPivotOffset", "Float", 196),
        new Spec("AirborneTargetingHeight", "Signed", 200),
        new Spec("CloseEnoughDist", "Float", 204),
        new Spec("IsCloseEnoughDist3D", "Boolean", 369),
        new Spec("DontNegateDeceleratePitchFactor", "Boolean", 370),
        new Spec("LocomotorWorksWhenDead", "Boolean", 371),
        new Spec("AllowMotiveForceWhileAirborne", "Boolean", 372),
        new Spec("Apply2DFrictionWhenAirborne", "Boolean", 373),
        new Spec("DownhillOnly", "Boolean", 374),
        new Spec("StickToGround", "Boolean", 375),
        new Spec("CanMoveBackward", "Boolean", 376),
        new Spec("CanReverseMove", "Boolean", 377),
        new Spec("ReverseMoveSpeed", "Percent", 208),
        new Spec("UpdateWaterWadingConditions", "Boolean", 378),
        new Spec("HasSuspension", "Boolean", 379),
        new Spec("MaximumWheelExtension", "Float", 212),
        new Spec("MaximumWheelCompression", "Float", 216),
        new Spec("WheelTurnAngle", "Angle", 220),
        new Spec("UseTerrainSmoothing", "Boolean", 380),
        new Spec("WanderWidthFactor", "Float", 224),
        new Spec("WanderLengthFactor", "Float", 228),
        new Spec("WanderAboutPointRadius", "Float", 232),
        new Spec("BurniningDeathRadius", "Float", 236),
        new Spec("BurniningDeathIsCavalry", "Boolean", 381),
        new Spec("ChargeMaxSpeed", "Percent", 240),
        new Spec("ChargeAvailable", "Boolean", 382),
        new Spec("ChargeIgnoresCondition", "Boolean", 383),
        new Spec("EnableHighSpeedTurnFlags", "Boolean", 384),
        new Spec("WaitForFormation", "Boolean", 385),
        new Spec("RudderCorrectionDegree", "Float", 244),
        new Spec("RudderCorrectionRate", "Float", 248),
        new Spec("ElevatorCorrectionDegree", "Float", 252),
        new Spec("ElevatorCorrectionRate", "Float", 256),
        new Spec("AeleronCorrectionDegree", "Float", 260),
        new Spec("AeleronCorrectionRate", "Float", 264),
        new Spec("SwoopStandoffRadius", "Float", 268),
        new Spec("SwoopStandoffHeight", "Float", 272),
        new Spec("SwoopTerminalVelocity", "Float", 276),
        new Spec("SwoopAccelerationRate", "Float", 280),
        new Spec("SwoopSpeedTuningFactor", "Float", 284),
        new Spec("BackingUpSpeed", "Percent", 288),
        new Spec("BackingUpStopWhenTurning", "Boolean", 386),
        new Spec("BackingUpDistanceMin", "Float", 292),
        new Spec("BackingUpDistanceMax", "Float", 296),
        new Spec("BackingUpAngle", "Float", 300),
        new Spec("RiverModifier", "Percent", 304),
        new Spec("ScalesWalls", "Boolean", 387),
        new Spec("TurnWhileMoving", "Boolean", 388),
        new Spec("ClampOrientationToPathTangent", "Boolean", 389),
        new Spec("ReorientIfTurnTooSharp", "Boolean", 390),
        new Spec("BrakeBeforeReorienting", "Boolean", 391),
        new Spec("TakeOffAndLandingSpeed", "Float", 308),
        new Spec("TakeOffAndLandingSlowDownDelta", "Float", 312),
        new Spec("TakeOffAndLandingSlowDownTime", "Time", 316),
        new Spec("EasingTakeOffAndLanding", "Boolean", 392),
        new Spec("EasingPercentage", "Percent", 320),
        new Spec("AttackPathTrailDistance", "Float", 324),
        new Spec("AttackPathTrailDistanceMinScale", "Float", 328),
        new Spec("AttackPathTrailDistanceMaxScale", "Float", 332),
        new Spec("AbsoluteMinHeightWorldSpace", "Float", 336),
        new Spec("SpeedBasedHeightOffset", "Float", 340),
        new Spec("ResubmergeDelay", "Time", 344),
        new Spec("WaterToAirTransitionFX", "Reference", 348),
        new Spec("WaterSurfaceHeightOffset", "Float", 352),
        new Spec("ForbiddenObjectStatus", "Status", 356),
    };
    static readonly string[] ModelNames = { "INVALID", "ABOUT_TO_HIT", "ACCELERATE", "ACTIVELY_BEING_CONSTRUCTED", "ACTIVELY_CONSTRUCTING", "AFFECTED_BY_EMP", "AFLAME", "AIM_FAR", "AIM_HIGH", "AIM_LOW", "AIM_NEAR", "AIM_STRAIGHT", "AIR_POWER_EFFECT", "ALL", "ALTERNATE_FORMATION", "ARMORSET_ALTERNATE_FORMATION", "ARMORSET_ELITE", "ARMORSET_HERO", "ARMORSET_MOUNTED", "ARMORSET_PLAYER_UPGRADE", "ARMORSET_PLAYER_UPGRADE_2", "ARMORSET_PLAYER_UPGRADE_3", "ARMORSET_UNBESIEGEABLE", "ARMORSET_VETERAN", "ARMORSET_WEAK_VERSUS_BASEDEFENSES", "ATTACHED", "ATTACKING", "ATTACKING_POSITION", "ATTACKING_STRUCTURE", "ATTACK_MOVING", "AWAITING_CONSTRUCTION", "BACKCRUSHED", "BACKING_UP", "BASE_BUILD", "BEING_MAULED", "BETWEEN_FIRING_SHOTS_A", "BETWEEN_FIRING_SHOTS_B", "BETWEEN_FIRING_SHOTS_C", "BETWEEN_FIRING_SHOTS_D", "BETWEEN_FIRING_SHOTS_E", "BUILD_PLACEMENT_CURSOR", "BUILD_PLACEMENT_HINT", "BUILD_VARIATION_ONE", "BUILD_VARIATION_TWO", "BURNED", "BURNINGDEATH", "BURNT_MODEL", "BURNT_TEXTURE", "CANCEL_CAPTURE", "CAPTURED", "CAPTURE_0", "CAPTURE_100", "CAPTURE_25", "CAPTURE_50", "CAPTURE_75", "CAPTURING", "CARRYING", "CHARGING", "CHRONORIFT", "CLIMBING", "COLLAPSING", "COMBINED_CHILD", "COMBINED_PARENT", "COMING_OUT_OF_FACTORY", "CONTINUOUS_FIRE_FAST", "CONTINUOUS_FIRE_MEAN", "CONTINUOUS_FIRE_SLOW", "COVER", "DAMAGED", "DEATH_1", "DEATH_10", "DEATH_11", "DEATH_12", "DEATH_13", "DEATH_14", "DEATH_15", "DEATH_2", "DEATH_3", "DEATH_4", "DEATH_5", "DEATH_6", "DEATH_7", "DEATH_8", "DEATH_9", "DEBUG", "DECAY", "DECELERATE", "DEFLECT_SPECIAL_POWER", "DEPLOYED", "DESTROYED_BACK", "DESTROYED_FRONT", "DESTROYED_LEFT", "DESTROYED_RIGHT", "DESTROYED_WEAPON", "DESTROYED_WHILST_BEING_CONSTRUCTED", "DISGUISED", "DIVING", "DOCKING", "DOCKING_ACTIVE", "DOCKING_BEGINNING", "DOCKING_ENDING", "DOCKING_EXTRACT", "DOCKING_FILL", "DOCKING_MOVEBACK", "DOCKING_PRE_DOCK", "DOCKING_RETURN", "DOOR_1_CLOSING", "DOOR_1_OPENING", "DOOR_1_WAITING_OPEN", "DOOR_1_WAITING_TO_CLOSE", "DOOR_2_CLOSING", "DOOR_2_OPENING", "DOOR_2_WAITING_OPEN", "DOOR_2_WAITING_TO_CLOSE", "DOOR_3_CLOSING", "DOOR_3_OPENING", "DOOR_3_WAITING_OPEN", "DOOR_3_WAITING_TO_CLOSE", "DOOR_4_CLOSING", "DOOR_4_OPENING", "DOOR_4_WAITING_OPEN", "DOOR_4_WAITING_TO_CLOSE", "DOOR_HELIPAD_CLOSING", "DOOR_HELIPAD_OPENING", "DOOR_HELIPAD_WAITING_OPEN", "DOOR_HELIPAD_WAITING_TO_CLOSE", "DRAFTED", "DYING", "DYING_WASATTACKING", "DYING_WASMOVING", "EATING", "EMOTION_AFRAID", "EMOTION_ALERT", "EMOTION_AMUSED", "EMOTION_BRACE_FOR_BEING_CRUSHED", "EMOTION_CELEBRATING", "EMOTION_CHEER_FOR_ABOUT_TO_CRUSH", "EMOTION_COWER", "EMOTION_DISSIDENT", "EMOTION_DOOM", "EMOTION_GUNG_HO", "EMOTION_LOOK_TO_SKY", "EMOTION_MORALE_HIGH", "EMOTION_MORALE_LOW", "EMOTION_PANIC", "EMOTION_POINTING", "EMOTION_TAUNTING", "EMOTION_TERROR", "EMOTION_UNCONTROLLABLY_AFRAID", "ENGAGED", "EXPLODED_BOUNCING", "EXPLODED_FLAILING", "FIRING_A", "FIRING_B", "FIRING_C", "FIRING_D", "FIRING_E", "FIRING_OR_PREATTACK_A", "FIRING_OR_PREATTACK_B", "FIRING_OR_PREATTACK_C", "FIRING_OR_PREATTACK_D", "FIRING_OR_PREATTACK_E", "FIRING_OR_RELOADING_A", "FIRING_OR_RELOADING_B", "FIRING_OR_RELOADING_C", "FIRING_OR_RELOADING_D", "FIRING_OR_RELOADING_E", "FLOODED", "FLYING", "FORMATION_PREVIEW", "FREEFALL", "FRONTCRUSHED", "FROZEN", "GARRISONED", "GUARDING", "HARVEST_ACTION", "HARVEST_PREPARATION", "HEALTH_PERCENT_0", "HEALTH_PERCENT_100", "HEALTH_PERCENT_25", "HEALTH_PERCENT_50", "HEALTH_PERCENT_75", "HIDDEN", "HIGH_TECH_EFFECT", "HIT_LEVEL_1", "HIT_LEVEL_2", "HIT_LEVEL_3", "HIT_REACTION", "HORDE_EMPTY", "IMPENDING_DOOM", "INFILTRATED_DISABLE", "INFILTRATED_ENERGY", "INFILTRATED_RADAR_FREEZE", "INFILTRATED_RESET", "INFILTRATED_STEAL_MONEY", "INFILTRATED_VISION", "INFILTRATED_VISION_UNITS", "INSIDE_GARRISON", "INVISIBLE_CAMOUFLAGE", "INVISIBLE_STEALTH", "INVULNERABLE", "IRONCURTAIN", "JETAFTERBURNER", "JETEXHAUST", "JUST_BUILT", "LANDING", "LAUNCHED", "LEASHED_RETURNING", "LEVELED", "LOADED", "LOCOMOTOR_BURNINGDEATH", "LOCOMOTOR_CHANGING_FIRINGARC", "LOCOMOTOR_COMBO", "LOCOMOTOR_COMBO2", "LOCOMOTOR_COMBO3", "LOCOMOTOR_CONTAINED", "LOCOMOTOR_ENRAGED", "LOCOMOTOR_FREEFALL", "LOCOMOTOR_MOUNTED", "LOCOMOTOR_NORMAL_UPGRADED", "LOCOMOTOR_PANIC", "LOCOMOTOR_SCARED", "LOCOMOTOR_SUPERSONIC", "LOCOMOTOR_TAXIING", "LOCOMOTOR_WALL_SCALING", "LOCOMOTOR_WANDER", "MARCHING", "MARKED_FOR_HUNT_TACTIC", "MARKED_FOR_NO_SKIRMISH_RECRUIT", "MONEY_STORED_AMOUNT_1", "MONEY_STORED_AMOUNT_2", "MONEY_STORED_AMOUNT_3", "MONEY_STORED_AMOUNT_4", "MOUNTED", "MOVING", "MOVING_OUT_OF_THE_WAY", "NIGHT", "OVER_WATER", "PACKING", "PACKING_TYPE_1", "PACKING_TYPE_2", "PACKING_TYPE_3", "PACKING_TYPE_4", "PACKING_TYPE_5", "PACKING_TYPE_6", "PANICKING", "PARACHUTE_LAND", "PARACHUTING", "PARALYZED", "PARKINGPLACE_1_DOOR_CLOSED", "PARKINGPLACE_1_DOOR_CLOSING", "PARKINGPLACE_1_DOOR_OPEN", "PARKINGPLACE_1_DOOR_OPENING", "PARKINGPLACE_2_DOOR_CLOSED", "PARKINGPLACE_2_DOOR_CLOSING", "PARKINGPLACE_2_DOOR_OPEN", "PARKINGPLACE_2_DOOR_OPENING", "PARKINGPLACE_3_DOOR_CLOSED", "PARKINGPLACE_3_DOOR_CLOSING", "PARKINGPLACE_3_DOOR_OPEN", "PARKINGPLACE_3_DOOR_OPENING", "PARKINGPLACE_4_DOOR_CLOSED", "PARKINGPLACE_4_DOOR_CLOSING", "PARKINGPLACE_4_DOOR_OPEN", "PARKINGPLACE_4_DOOR_OPENING", "PARKINGPLACE_HELIPAD_DOOR_CLOSED", "PARKINGPLACE_HELIPAD_DOOR_CLOSING", "PARKINGPLACE_HELIPAD_DOOR_OPEN", "PARKINGPLACE_HELIPAD_DOOR_OPENING", "PARTIALLY_CONSTRUCTED", "PASSENGER", "PASSENGER_VARIATION_1", "PASSENGER_VARIATION_2", "PASSENGER_VARIATION_3", "PASSENGER_VARIATION_4", "PASSENGER_VARIATION_5", "PHANTOM_STRUCTURE", "PLAYER_POWER_1", "POST_COLLAPSE", "POST_RUBBLE", "POWER_PLANT_UPGRADED", "POWER_PLANT_UPGRADING", "PREATTACK_A", "PREATTACK_B", "PREATTACK_C", "PREATTACK_D", "PREATTACK_E", "PREORDER", "PREPARING", "PRIMARY_FORMATION", "PRONE", "RADAR_EXTENDING", "RADAR_UPGRADED", "RAISING_FLAG", "RAPPELLING", "REACT_1", "REACT_2", "REACT_3", "REACT_4", "REACT_5", "REACT_6", "REALLYDAMAGED", "RELOADING_A", "RELOADING_B", "RELOADING_C", "RELOADING_D", "RELOADING_E", "REPAIRING_DISABLED", "RESURRECTED", "RIDER1", "RIDER2", "RIDER3", "RIDER4", "RIDER5", "RIDER6", "RIDER7", "RIDER8", "RIDERLESS", "RUBBLE", "RUNNING_OFF_MAP", "SAIL_BLOWN_LEFT", "SAIL_BLOWN_RIGHT", "SAIL_FLAPPING", "SCALING_WALL", "SCALING_WALL_HORDE", "SELECTED", "SHRINK_EFFECT", "SIEGE_CONTAIN", "SMOLDERING", "SNOW", "SOLD", "SPECIALPOWER1_READY", "SPECIALPOWER2_READY", "SPECIALPOWER3_READY", "SPECIALPOWER4_READY", "SPECIALPOWER5_READY", "SPECIALPOWER6_READY", "SPECIALPOWER7_READY", "SPECIALPOWER8_READY", "SPECIALPOWER9_READY", "SPECIAL_CHEERING", "SPECIAL_DAMAGED", "SPECIAL_ENEMY_NEAR", "SPECIAL_POWER_1", "SPECIAL_POWER_2", "SPECIAL_POWER_3", "SPECIAL_WEAPON_FIVE", "SPECIAL_WEAPON_FOUR", "SPECIAL_WEAPON_ONE", "SPECIAL_WEAPON_SIX", "SPECIAL_WEAPON_THREE", "SPECIAL_WEAPON_TWO", "SPLATTED", "START_CAPTURE", "STRUCTURE_UNPACKING", "STUNNED", "STUNNED_FLAILING", "STUNNED_STANDING_UP", "SUCKED_UP_HIGH", "SUPPRESSED", "SWAPPING_TO_WEAPONSET_1", "SWAPPING_TO_WEAPONSET_2", "SWAPPING_TO_WEAPONSET_3", "SWIMMING", "SWOOPING", "TAKING_OFF", "THROWN_PROJECTILE", "TIBERIUM_CRYSTAL_TYPE1", "TIBERIUM_CRYSTAL_TYPE2", "TIBERIUM_CRYSTAL_TYPE3", "TIBERIUM_CRYSTAL_TYPE4", "TIBERIUM_CRYSTAL_TYPE5", "TIBERIUM_GROWING", "TOPPLED", "TRANSPORT_MOVING", "TRANSPORT_STOPPED", "TURN_LEFT", "TURN_LEFT_HIGH_SPEED", "TURN_RIGHT", "TURN_RIGHT_HIGH_SPEED", "TURRET_ANGLE_0", "TURRET_ANGLE_180", "TURRET_ANGLE_270", "TURRET_ANGLE_90", "TURRET_ROTATE", "UNCONTROLLABLE", "UNDERPOWERED", "UNIT_ACTIVELY_BEING_CONSTRUCTED", "UNPACKING", "UPGRADED_ARMOR", "USER_1", "USER_10", "USER_2", "USER_3", "USER_4", "USER_5", "USER_6", "USER_7", "USER_8", "USER_9", "USING_COMBO_LOCOMOTOR", "USING_SPECIAL_ABILITY", "USING_WEAPON_A", "USING_WEAPON_B", "USING_WEAPON_C", "USING_WEAPON_D", "USING_WEAPON_E", "VEHICLE_CRUSH_CENTER", "VEHICLE_CRUSH_LEFT", "VEHICLE_CRUSH_RIGHT", "WADING", "WALKING", "WALL_SEGMENT", "WANDER", "WEAPONLOCK_PRIMARY", "WEAPONLOCK_QUATERNARY", "WEAPONLOCK_QUINARY", "WEAPONLOCK_SECONDARY", "WEAPONLOCK_TERTIARY", "WEAPONSET_CLOSE_RANGE", "WEAPONSET_CONTAINED", "WEAPONSET_CONTESTING_BUILDING", "WEAPONSET_ELITE", "WEAPONSET_GARRISONED", "WEAPONSET_HERO", "WEAPONSET_MOUNTED", "WEAPONSET_PASSENGER_TYPE_ONE", "WEAPONSET_PASSENGER_TYPE_TWO", "WEAPONSET_PLAYER_UPGRADE", "WEAPONSET_RIDER1", "WEAPONSET_RIDER2", "WEAPONSET_RIDER3", "WEAPONSET_RIDER4", "WEAPONSET_SPECIAL_ONE", "WEAPONSET_SPECIAL_TWO", "WEAPONSET_SPECIAL_UPGRADE", "WEAPONSET_TOGGLE_1", "WEAPONSET_TOGGLE_2", "WEAPONSET_TOGGLE_3", "WEAPONSET_VETERAN", "WEAPONSLOTID_01", "WEAPONSLOTID_02", "WEAPONSLOTID_03", "WEAPONSLOTID_04", "WEAPONSLOTID_05", "WEAPONSLOTID_06", "WEAPONSLOTID_07", "WEAPONSLOTID_08", "WEAPONSLOTID_09", "WEAPONSLOTID_10", "WEAPONSTATE_CLOSE_RANGE", "WEAPONSTATE_CONTAINED", "WEAPONSTATE_ONE", "WEAPONSTATE_THREE", "WEAPONSTATE_TWO", "WEAPON_TOGGLING", "WORLD_BUILDER" };
    static readonly string[] StatusNames = { "ABSORBED_DAMAGE", "ADVANCED_MISSILE_PACKS", "AFLAME", "AIRBORNE_TARGET", "AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE", "AIR_LOCOMOTOR_ACTIVE", "ANTI_GARRISON", "ATTACHED", "ATTACKING_GARRISONED_STRUCTURE", "BOOBY_TRAPPED", "BOOBY_TRAP_EXPLODE", "BOOKENDING", "BRIDGE_IMPASSABLE", "BROADCASTING_INVISIBILITY", "BUILD_BEING_CANCELED", "BURNED", "CANNOT_BE_SOLD", "CANNOT_LAND_AT_AIRFIELD", "CAN_ATTACK", "CAN_ATTACK_WHILE_STEALTHED", "CAN_CONTEST_GARRISON", "CAN_ENTER_ANYTHING", "CAN_NOT_WALK_ON", "CAN_STEALTH", "CAN_STEALTH_FROM_PRODUCER", "CARRYING_FLAG", "CHANTING", "CHARGING_BASE_DEFENSES", "CHARGING_WEAPON", "CLEARED_FOR_LANDING", "CLONED", "COMBINED_ATTACHED", "COMBINED_CHILD", "COMBINED_PARENT", "COMMAND_BUTTON_TOGGLED", "CONTAINER_OCCUPIED", "CONTESTED", "CONTESTING_BUILDING", "DAMAGED", "DEATH_1", "DEATH_2", "DEATH_3", "DEATH_4", "DEATH_5", "DEFLECT_INCOMING_FIRE", "DELAYED_ENTER_STRUCTURE", "DEPART_FROM_BOARDING", "DEPLOYED", "DEPLOYING", "DESTROYED", "DETECTED", "DOCKING", "DOES_CONTAIN_TIBERIUM", "DONT_CLEAR_FOR_BUILD", "DO_NOT_AVOID_TALLBUILDING", "DO_NOT_PICK_ME", "DO_NOT_SCORE", "ENABLE_TRACER_DRAW", "ENCLOSED", "END_FIRE_STATE", "ENRAGED", "ENTERING_GOAL", "EXCEEDED_PATH_NEIGHBOR_COUNT", "EXECUTING_STORED_COMMAND", "EXITING_COMBINED", "EXPLOSIVES_ATTACHED", "EXPLOSIVES_DETONATING", "FLEE_OFF_MAP", "FOLLOWING_THROUGH", "FORCE_ATTACKING", "FORCE_ATTACK_MOVING", "GARRISONED", "GARRISON_CAN_STEALTH", "GENERIC_TOGGLE_STATE", "GRAPPLE_UPDATE_ACTIVE", "GRAPPLE_UPDATE_BEING_MOVED", "GROUND_LOCOMOTOR_ACTIVE", "HARVESTING", "HAS_SECONDARY_DAMAGE", "HAS_TIBERIUM_GROWTH_MOD", "HAS_TIBERIUM_UPGRADE", "HEALTH_PERCENT_0", "HEALTH_PERCENT_100", "HEALTH_PERCENT_25", "HEALTH_PERCENT_50", "HEALTH_PERCENT_75", "HIDDEN", "HIJACKED", "HIT_SHIELD_SPHERE", "HORDE_MEMBER", "IGNORE_AI_COMMAND", "IGNORING_POWER_DOWN", "IGNORING_STEALTH", "IMMOBILE", "IMMOBILE_ALLOW_ROTATE", "IMMUNE_TO_BARK", "INAUDIBLE", "INHERITED_FROM_ALLY_TEAM", "INSIDE_GARRISON", "INVISIBLE_DETECTED", "INVISIBLE_DETECTED_BY_FRIEND", "IN_COVER", "IN_FORMATION_TEMPLATE", "IN_NANOHIVE", "IN_SHIELD_SPHERE", "IN_SPIDER_HOLE", "IN_STASIS", "IRRADIATED", "IS_AIMING_WEAPON", "IS_ATTACKING", "IS_BEING_DRAGGED", "IS_BEING_HARVESTED", "IS_BRAKING", "IS_ENGAGED", "IS_FIRING_WEAPON", "IS_HIDEOUT", "IS_LEAVING_FACTORY", "IS_MELEE_ATTACKING", "IS_MOVING_TO_RALLY_POINT", "IS_RELOADING_WEAPON", "LANDING_IN_PROGRESS", "LEASHED_RETURNING", "LEECHED_TARGET_ACTIVE", "LIFTED_INTO_AIR", "LINE_SEGMENT_DAMAGE_ACTIVE", "LOADED_FROM_MAP", "MAGNETIZED", "MARCH_OF_DEATH", "MATCH_TARGETS_SPEED", "MOVING_TO_DISMOUNT", "NEXT_MOVE_IS_FORCE_ATTACK_MOVE", "NEXT_MOVE_IS_REVERSE", "NON_AUTOACQUIRABLE", "NOT_IN_WORLD", "NO_ATTACK", "NO_ATTACK_FROM_AI", "NO_AUTO_ACQUIRE", "NO_BRIBE", "NO_COLLISIONS", "NO_HERO_PROPERTIES", "NO_REFUND", "NO_SHADOW", "NO_SPECIAL_ABILITY", "NO_SQUISHCOLLIDE_DELAY", "OCLMONITOR_COMPLETED_TASK", "OCLMONITOR_MONITOR_RELEASED", "OUT_OF_PHASE", "OVERCHARGING_WEAPON", "OVER_WATER", "PARACHUTING", "PARKED_AT_AIRFIELD", "PAUSING", "PENDING_CONSTRUCTION", "PHANTOM_STRUCTURE", "PLACED_BY_PLAYER", "PLAYER_POWER_1", "PLAYER_POWER_2", "PLAYER_POWER_3", "PLAYER_POWER_4", "PLAYER_POWER_5", "POINT_DEFENSE_DRONE_ATTACHED", "PORTER_TAGGED", "POWERED_DOWN_EMP", "PREEMPT_PRE_ATTACK", "RAMPAGING", "REALLYDAMAGED", "RECONSTRUCTING", "REPAIR_ALLIES_WHEN_IDLE", "REPULSOR", "RESET_GOAL_POSITION", "RIDER_IS_PILOT", "RIFT_OCCUPIED", "RIOT_SHIELDED", "RUBBLE", "RUNNING_DOWN_FROM_BEHIND", "SCARED_CIVILIAN_CAR", "SCRAMBLED", "SHIELDBODY_ENABLED", "SHROUD_REVEAL_TO_ALL", "SHRUNK", "SINKING", "SKIRMISH_AI_DO_NOT_ATTACK", "SKIRMISH_AI_DO_NOT_TAKE", "SOLD", "SPECIALABILITY_ACTIVE", "SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING", "SPECIAL_ARMOR_ACTIVE", "STAND_GROUND", "STEALTHED", "STEAL_NEXT_UNIT_TRAPPED", "SUBMERGED", "SURFACED", "SURFACED_IMMOBILE", "SWITCHED_WEAPONS", "TAGGED", "TARGETED_FOR_REPAIR", "TEMPORARILY_DEFECTED", "TIBERIUM_VIBRATING", "TOPPLING", "TRANSFORMATION_TOGGLE_STATE", "TRANSPORT_MOVING_TO_CONTAIN", "UNATTACKABLE", "UNCONTROLLABLY_SCARED", "UNDERGOING_REPAIR", "UNDER_CONSTRUCTION", "UNDER_FROZEN", "UNDER_IRON_CURTAIN", "UNDER_RUSH_ATTACK", "UNIT_WANTS_TO_REGARRISON", "UNPACKING", "UNSELECTABLE", "UPDATING_AI", "USER_PARALYZED", "USER_POWERED_DOWN", "USING_ABILITY", "VEHICLE_ATTACHED", "WAITING_FOR_BOARDING", "WALL_SEGMENT", "WATER_LOCOMOTOR_ACTIVE", "WEAPON_UPGRADED_01", "WEAPON_UPGRADED_02", "WEAPON_UPGRADED_03", "WONT_RIDE_WITH_YOU" };
    static readonly string[] AppearanceNames = { "TWO_LEGS", "FOUR_WHEELS", "HOVER", "HOVER_TANK", "WINGS", "HORDE", "HUGE_TWO_LEGS", "TREADS", "SHIP", "OTHER" };
    static readonly string[] LocoZNames = { "NO_MOTIVE_FORCE", "SEA_LEVEL", "SURFACE_RELATIVE_HEIGHT_ALLOW_ZERO_HEIGHT", "SURFACE_RELATIVE_HEIGHT", "ABSOLUTE_HEIGHT", "FIXED_SURFACE_RELATIVE_HEIGHT", "FIXED_ABSOLUTE_HEIGHT", "RELATIVE_TO_GROUND_AND_BUILDINGS", "SMOOTH_RELATIVE_TO_HIGHEST_LAYER", "FLOATING_Z", "SCALING_WALLS", "DO_NOT_MODIFY_HEIGHT", "SEA_LEVEL_SMOOTH_Z" };
    static readonly string[] LocoFNames = { "NO_FORMATION", "CAVALRY_1", "CAVALRY_2", "CAVALRY_3", "MELEE_1", "MELEE_2", "MELEE_3", "RANGED_1", "RANGED_2", "RANGED_3", "ARTILLERY_1", "ARTILLERY_2", "ARTILLERY_3" };
    static readonly string[] SurfaceNames = { "GROUND", "WATER", "CLIFF", "AIR", "RUBBLE", "OBSTACLE", "IMPASSABLE", "DEEP_WATER", "WALL_RAILING", "CRUSHABLE_OBSTACLE", "CRUSHABLE_WALL" };
    static readonly string[] JetLocomotorDataOptionNames = { "NO_CIRCLE_WHILE_USING_SPECIALPOWER" };
    static readonly string[] Numbers = { "0", "-0", "1", "-1", "12.25", "-12.5%", "1e-7", "1e30",
        "1e-30", "16777217", "2d1", "2D-1", "0x10", "garbage", "", " 25.75junk", "25,5",
        "100%", "1.25ms", "2s", "90d", "1r", "3rd", "100MS", "3ds", "1e+99", "-1e+99" };
    static readonly string[] Bools = { "true", "false", "TRUE", "True", "1", "0", "t", "T", " true", "yes", "", "10", "falsejunk" };
    static readonly string[] References = { "", "Name", "Name\\0", "Name\\1", "Name\\27", "Name\\-1",
        "Name\\2147483648", "Name\\-2147483649", "Name\\ +17junk", "Name\\0x10", "Name\\17\\99", "资源\\7" };
    static int fixtures, comparisons, rejections, layouts;
    static string Escape(string value) { return value.Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace("\"", "&quot;"); }
    static string Attr(string name, string value) { return name+"='"+Escape(value)+"' "; }
    static string Xml(string attrs, string children) { return "<LocomotorTemplate xmlns='"+NS+"' id='Oracle' "+attrs+">"+children+"</LocomotorTemplate>"; }
    static XmlDocument Parse(string text) { XmlDocument d = new XmlDocument(); d.LoadXml(text); return d; }
    static XmlNamespaceManager Namespaces(XmlDocument d) { XmlNamespaceManager n = new XmlNamespaceManager(d.NameTable); n.AddNamespace("ea", NS); return n; }
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager namespaces) {
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration d = new InstanceDeclaration(document); d.XmlNode=xml.DocumentElement; return d;
    }
    static uint Word(byte[] data, int at, bool big) {
        if (!big) return BitConverter.ToUInt32(data, at);
        return ((uint)data[at]<<24)|((uint)data[at+1]<<16)|((uint)data[at+2]<<8)|data[at+3];
    }
    static void Equal(string name, byte[] a, byte[] b) {
        if (a==null || b==null || a.Length!=b.Length) throw new Exception(name+" length original="+(a==null?-1:a.Length)+" recovered="+(b==null?-1:b.Length));
        for (int i=0;i<a.Length;++i) if(a[i]!=b[i]) throw new Exception(name+" byte "+i+" original="+a[i]+" recovered="+b[i]);
        ++comparisons;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static XmlNode Child(XmlNode root, string name) {
        foreach(XmlNode n in root.ChildNodes) if(n.NodeType==XmlNodeType.Element && n.LocalName==name && n.NamespaceURI==NS) return n;
        return null;
    }
    static bool Imported(string text) { return text!=null && text.IndexOf('\\')>=0; }
    static void Layout(string label, XmlDocument doc, AssetBuffer buffer, bool big) {
        // Independent ABI expectations: root, nullable flag blocks, nullable
        // reference block, then nullable Jet block. There is no token table.
        XmlElement root=doc.DocumentElement;
        List<int> pointerFields=new List<int>(); List<int> pointerTargets=new List<int>();
        List<int> imports=new List<int>(); int end=396;
        string[] names={"ActiveModelConditions","ActiveObjectStatus","EnteringModelConditions","ForbiddenObjectStatus"};
        int[] offsets={108,112,116,356}; int[] sizes={60,28,60,28};
        for(int i=0;i<names.Length;++i) {
            if(root.HasAttribute(names[i])) { pointerFields.Add(offsets[i]); pointerTargets.Add(end); end+=sizes[i]; }
            else Check(Word(buffer.InstanceData, offsets[i], big)==0,label+" absent pointer "+names[i]);
        }
        if(Imported(root.HasAttribute("WaterToAirTransitionFX")?root.GetAttribute("WaterToAirTransitionFX"):null)) imports.Add(348);
        XmlNode bounce=Child(root,"BounceKickTerrainMap");
        if(bounce!=null) {
            pointerFields.Add(360); pointerTargets.Add(end);
            if(Imported(bounce.InnerText)) imports.Add(end);
            end+=4;
        } else Check(Word(buffer.InstanceData,360,big)==0,label+" absent bounce");
        XmlNode jet=Child(root,"JetLocomotorData");
        if(jet!=null) { pointerFields.Add(364); pointerTargets.Add(end); end+=20; }
        else Check(Word(buffer.InstanceData,364,big)==0,label+" absent jet");
        Check(buffer.InstanceData.Length==end,label+" root/blocks size");
        Check(Word(buffer.InstanceData,0,big)==0,label+" reserved word");
        for(int i=393;i<396;++i) Check(buffer.InstanceData[i]==0,label+" ABI padding");
        for(int i=0;i<pointerFields.Count;++i) Check(Word(buffer.InstanceData,pointerFields[i],big)==pointerTargets[i],label+" pointer target @"+pointerFields[i]);
        Check(buffer.RelocationData.Length==(pointerFields.Count==0?0:4*(pointerFields.Count+1)),label+" relocation size");
        for(int i=0;i<pointerFields.Count;++i) Check(Word(buffer.RelocationData,4*i,big)==pointerFields[i],label+" relocation offset");
        if(pointerFields.Count!=0) Check(Word(buffer.RelocationData,buffer.RelocationData.Length-4,big)==UInt32.MaxValue,label+" relocation sentinel");
        imports.Sort();
        Check(buffer.ImportsData.Length==(imports.Count==0?0:4*(imports.Count+1)),label+" import size");
        for(int i=0;i<imports.Count;++i) Check(Word(buffer.ImportsData,4*i,big)==imports[i],label+" import offset");
        if(imports.Count!=0) Check(Word(buffer.ImportsData,buffer.ImportsData.Length-4,big)==UInt32.MaxValue,label+" import sentinel");
        ++layouts;
    }
    static void Test(IAssetBuilderPlugin original, TargetPlatform platform, string label, string text) {
        XmlDocument d=Parse(text); XmlNamespaceManager ns=Namespaces(d);
        InstanceDeclaration declaration=Declaration(d,ns);
        AssetBuffer a;
        try { a=original.ProcessInstance(declaration); }
        catch(Exception e) { throw new Exception(label+" original platform="+(int)platform,e); }
        AssetBuffer b;
        try { b=LocomotorTemplateCompiler.ProcessInstance(declaration,platform); }
        catch(Exception e) { throw new Exception(label+" recovered platform="+(int)platform,e); }
        string prefix=label+"/platform="+(int)platform;
        Equal(prefix+"/instance",a.InstanceData,b.InstanceData);
        Equal(prefix+"/relocations",a.RelocationData,b.RelocationData);
        Equal(prefix+"/imports",a.ImportsData,b.ImportsData);
        Layout(prefix,d,b,platform!=TargetPlatform.Win32); ++fixtures;
    }
    static void PrefixNavigation(IAssetBuilderPlugin original, TargetPlatform platform) {
        string canonical=Xml("LookAheadMult='3.125'","<JetLocomotorData AttackPathClimbDistance='7.5'/>");
        string prefixed="<e:LocomotorTemplate xmlns:e='"+NS+"' id='Oracle' LookAheadMult='3.125'><e:JetLocomotorData AttackPathClimbDistance='7.5'/></e:LocomotorTemplate>";
        XmlDocument d=Parse(prefixed); XmlNamespaceManager ns=Namespaces(d);
        InstanceDeclaration unknown=Declaration(d,ns);
        // Core's XmlNode setter hashes node.Name, including its prefix.
        // The original XmlCompiler default dispatch returns null for that TypeId.
        Check(unknown.Handle.TypeId!=3972178387u,"prefixed declaration unexpectedly normalized");
        Check(original.ProcessInstance(unknown)==null,"original unknown TypeId fallback changed");
        try { LocomotorTemplateCompiler.ProcessInstance(unknown,platform); throw new Exception("prefixed wrong TypeId accepted"); }
        catch(NotSupportedException) { ++rejections; }
        XmlDocument c=Parse(canonical);
        AssetBuffer a=original.ProcessInstance(Declaration(c,Namespaces(c)));
        AssetBuffer b=LocomotorTemplateCompiler.CompileXml(d.DocumentElement,ns,platform!=TargetPlatform.Win32);
        string label="prefix-navigation/platform="+(int)platform;
        Equal(label+"/instance",a.InstanceData,b.InstanceData);
        Equal(label+"/relocations",a.RelocationData,b.RelocationData);
        Equal(label+"/imports",a.ImportsData,b.ImportsData);
        Layout(label,d,b,platform!=TargetPlatform.Win32); ++fixtures;
        Console.WriteLine("PASS prefixDispatch platform={0} original=null recovered=reject; canonical ProcessInstance vs prefixed CompileXml buffers/layout matched",(int)platform);
    }
    static void Reject(string text, TargetPlatform platform) {
        XmlDocument d=Parse(text); XmlNamespaceManager n=Namespaces(d);
        try { PluginSchemaBoundary.Validate(d);  LocomotorTemplateCompiler.ProcessInstance(Declaration(d,n),platform); }
        catch(NotSupportedException) { ++rejections; return; }
        throw new Exception("Unsupported XML accepted: "+text);
    }
    static string[] Values(Spec field) {
        if(field.Kind=="Boolean") return Bools;
        if(field.Kind=="Reference") return References;
        if(field.Kind=="Behavior") return Append(LocoZNames,new string[]{"","UNKNOWN","sea_level"," SEA_LEVEL","SEA_LEVEL "});
        if(field.Kind=="Appearance") return Append(AppearanceNames,new string[]{"","UNKNOWN","four_wheels"," FOUR_WHEELS","FOUR_WHEELS "});
        if(field.Kind=="Formation") return Append(LocoFNames,new string[]{"","UNKNOWN","melee_1"," MELEE_1","MELEE_1 "});
        if(field.Kind=="Surface") return Append(SurfaceNames,new string[]{"","UNKNOWN","GROUND WATER","-GROUND -GROUND","+WATER -WATER","ALL","all","-ALL GROUND","ALL -GROUND","ground"," GROUND  WATER "});
        if(field.Kind=="Model") return Append(ModelNames,new string[]{"","UNKNOWN","ATTACKING MOVING","-ATTACKING -ATTACKING","ALL","all","-ALL MOVING","ALL -MOVING","attacking","-INVALID","INVALID MOVING","ALL INVALID"});
        if(field.Kind=="Status") return Append(StatusNames,new string[]{"","UNKNOWN","STEALTHED MOVING","-STEALTHED -STEALTHED","ALL","all","-ALL MOVING","ALL -MOVING","stealthed"});
        if(field.Kind=="Signed") return Append(Numbers,new string[]{"2147483647","2147483648","-2147483648","-2147483649","077","0xff"});
        return Numbers;
    }
    static string[] Append(string[] a,string[] b) { string[] c=new string[a.Length+b.Length]; Array.Copy(a,c,a.Length); Array.Copy(b,0,c,a.Length,b.Length);return c; }
    static void Matrix(IAssetBuilderPlugin original,TargetPlatform p) {
        original.Initialize(null,p);
        Test(original,p,"defaults",Xml("",""));
        Test(original,p,"empty-nullables",Xml("ActiveModelConditions='' ActiveObjectStatus='' EnteringModelConditions='' ForbiddenObjectStatus='' WaterToAirTransitionFX=''","<BounceKickTerrainMap/><JetLocomotorData/>"));
        foreach(Spec field in Fields) {
            int i=0; foreach(string value in Values(field)) Test(original,p,field.Name+"-"+(i++),Xml(Attr(field.Name,value),""));
        }
        string[] jetAttrs={"AttackPathStartRunDistance","AttackPathClimbDistance","AttackPathDiveDistanceStart","AttackPathDiveDistanceEnd"};
        foreach(string name in jetAttrs) { int i=0; foreach(string value in Numbers) Test(original,p,"jet-"+name+"-"+(i++),Xml("","<JetLocomotorData "+Attr(name,value)+"/>")); }
        foreach(string value in new string[]{"","NO_CIRCLE_WHILE_USING_SPECIALPOWER","UNKNOWN","all","ALL","-ALL NO_CIRCLE_WHILE_USING_SPECIALPOWER","-NO_CIRCLE_WHILE_USING_SPECIALPOWER -NO_CIRCLE_WHILE_USING_SPECIALPOWER"})
            Test(original,p,"jet-options-"+value,Xml("","<JetLocomotorData "+Attr("Options",value)+"/>"));
        foreach(string value in References) {
            Test(original,p,"bounce-"+value,Xml("","<BounceKickTerrainMap>"+Escape(value)+"</BounceKickTerrainMap>"));
            Test(original,p,"two-imports-"+value,Xml(Attr("WaterToAirTransitionFX",value),"<BounceKickTerrainMap>"+Escape(value)+"</BounceKickTerrainMap>"));
        }
        string attrs="";
        foreach(Spec field in Fields) {
            string value="17.125";
            if(field.Kind=="Boolean") value="true";
            else if(field.Kind=="Surface") value="GROUND WATER CLIFF";
            else if(field.Kind=="Model") value="ATTACKING MOVING";
            else if(field.Kind=="Status") value="STEALTHED";
            else if(field.Kind=="Behavior") value="SEA_LEVEL";
            else if(field.Kind=="Appearance") value="WINGS";
            else if(field.Kind=="Formation") value="ARTILLERY_3";
            else if(field.Kind=="Reference") value="FX\\15";
            else if(field.Kind=="Time") value="1250ms";
            else if(field.Kind=="Angle") value="45d";
            attrs+=Attr(field.Name,value);
        }
        string jet="<JetLocomotorData Options='ALL' AttackPathStartRunDistance='1.25' AttackPathClimbDistance='2.5' AttackPathDiveDistanceStart='3.75' AttackPathDiveDistanceEnd='5'/>";
        Test(original,p,"all-fields",Xml(attrs,"<BounceKickTerrainMap>Terrain\\29</BounceKickTerrainMap>"+jet));
        Test(original,p,"child-order",Xml(attrs,jet+"<BounceKickTerrainMap>Terrain\\29</BounceKickTerrainMap>"));
        PrefixNavigation(original,p);
        foreach(string name in new string[]{"Surfaces","ActiveModelConditions","ActiveObjectStatus","EnteringModelConditions","ForbiddenObjectStatus"}) {
            string value=""; for(int i=0;i<257;++i) value+=(i%2==0?"+UNKNOWN ":"-UNKNOWN ");
            Test(original,p,"many-tokens-"+name,Xml(Attr(name,value),""));
        }
        // Reject scope outside the supported schema surface; original Marshal may
        // ignore some of these. They are deliberate recovered-component guards.
        Reject(Xml("UnknownField='1'",""),p);
        foreach(Spec field in Fields) {
            Reject(Xml(Attr("x"+field.Name,"1"),""),p);
            Reject(Xml("xmlns:q='urn:other' "+Attr("q:"+field.Name,"1"),""),p);
        }
        foreach(string child in new string[]{"<Unknown/>","<JetLocomotorData Unknown='1'/>","<JetLocomotorData><Unknown/></JetLocomotorData>",
            "<BounceKickTerrainMap Unknown='1'/>","<BounceKickTerrainMap><Unknown/></BounceKickTerrainMap>",
            "<JetLocomotorData/><JetLocomotorData/>","<BounceKickTerrainMap/><BounceKickTerrainMap/>",
            "<JetLocomotorData xmlns=''/>","<JetLocomotorData xmlns='urn:other'/>","text","<JetLocomotorData>text</JetLocomotorData>"})
            Reject(Xml("",child),p);
        Reject("<Other xmlns='"+NS+"' id='Oracle'/>",p);
        Reject("<LocomotorTemplate xmlns='urn:other' id='Oracle'/>",p);
        Reject("<BounceKickTerrainMapping xmlns='"+NS+"' id='Oracle'/>",p);
        try { LocomotorTemplateCompiler.ProcessInstance(null,p); throw new Exception("null declaration accepted"); }
        catch(ArgumentNullException) { ++rejections; }
        try { LocomotorTemplateCompiler.CompileXml(null,new XmlNamespaceManager(new NameTable()),false); throw new Exception("null node accepted"); }
        catch(ArgumentNullException) { ++rejections; }
        XmlDocument doc=Parse(Xml("",""));
        try { LocomotorTemplateCompiler.CompileXml(doc.DocumentElement,null,false); throw new Exception("null namespaces accepted"); }
        catch(ArgumentNullException) { ++rejections; }
        // Fixed fields at byte 8, 20, 44, 96, and bool packing are independent
        // of the generated descriptor table and catch accidental Tokenizer data.
        XmlDocument fixedDoc=Parse(Xml("LookAheadMult='2.5' TurnTimeSeconds='2500ms' TurnThreshold='1r' BehaviorZ='SEA_LEVEL' MakeTransformNonDirty='true'",""));
        AssetBuffer fixedBuffer=LocomotorTemplateCompiler.CompileXml(fixedDoc.DocumentElement,Namespaces(fixedDoc),p!=TargetPlatform.Win32);
        bool big=p!=TargetPlatform.Win32;
        Check(Word(fixedBuffer.InstanceData,8,big)==0x40200000u,"LookAheadMult layout");
        Check(Word(fixedBuffer.InstanceData,20,big)==0x40200000u,"Time layout");
        Check(Word(fixedBuffer.InstanceData,44,big)==0x3f800000u,"Angle layout");
        Check(Word(fixedBuffer.InstanceData,96,big)==1u,"BehaviorZ layout");
        Check(fixedBuffer.InstanceData[368]==1 && fixedBuffer.InstanceData[384]==1 && fixedBuffer.InstanceData[388]==1,"packed bools/defaults");
        ++layouts;
    }
    static void BabMetadata(IAssetBuilderPlugin original,TargetPlatform p) {
        foreach(string value in new[]{"0","4294967295","metadata-not-a-number",""}) {
            string tag=Attr("TypeId",value);
            Test(original,p,"BAB/root-jet-bounce-TypeId-"+value,Xml(tag+" LookAheadMult='2.5' WaterToAirTransitionFX='Fx\\1'",
                "<JetLocomotorData "+tag+" AttackPathStartRunDistance='17.5'/><BounceKickTerrainMap "+tag+">Terrain\\3</BounceKickTerrainMap>"));
        }
        Reject(Xml("xmlns:q='urn:metadata' q:TypeId='1'",""),p);
        Reject(Xml("","<JetLocomotorData xmlns:q='urn:metadata' q:TypeId='1'/>"),p);
        Reject(Xml("","<BounceKickTerrainMap xmlns:q='urn:metadata' q:TypeId='1'/>"),p);
        Reject(Xml("TypeId='1' UnknownField='1'",""),p);
        Reject(Xml("","<JetLocomotorData TypeId='1' Unknown='1'/>"),p);
        Reject(Xml("","<BounceKickTerrainMap TypeId='1' Unknown='1'/>"),p);
    }
    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();
        result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            Assembly assembly=Assembly.LoadFrom(args[0]);
            IAssetBuilderPlugin original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            Type rootType=assembly.GetType("SageBinaryData.LocomotorTemplate");
            Check(rootType!=null && Marshal.SizeOf(rootType)==396,"original root size");
            using(SHA256 sha=SHA256.Create()) {
                string originalHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");
                Check(originalHash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","original DLL fingerprint differs from fixed PE evidence");
                result["originalDllSha256"]=originalHash; Console.WriteLine("originalDllSha256="+originalHash);
            }
            List<object> platformResults=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;
                Matrix(original,p);
                BabMetadata(original,p);
                Dictionary<string,object> row=new Dictionary<string,object>();
                row["platform"]=(int)p; row["fixtures"]=fixtures-f; row["arrayComparisons"]=comparisons-c; row["rejections"]=rejections-r; row["layoutAssertions"]=layouts-l;
                platformResults.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=platformResults; result["fixtures"]=fixtures; result["arrayComparisons"]=comparisons; result["rejections"]=rejections; result["layoutAssertions"]=layouts;
            result["succeeded"]=true;
            Console.WriteLine("PASS LocomotorTemplate fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);
            return 0;
        } catch(Exception e) {
            result["error"]=e.ToString(); Console.Error.WriteLine(e); return 1;
        } finally {
            if(args.Length>1) File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));
        }
    }
}
