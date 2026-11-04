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
class XmlenvironmentOracle {
    static readonly string[] ModelFlagValues={"ABOUT_TO_HIT","ACCELERATE","ACTIVELY_BEING_CONSTRUCTED","ACTIVELY_CONSTRUCTING","AFFECTED_BY_EMP","AFLAME","AIM_FAR","AIM_HIGH","AIM_LOW","AIM_NEAR","AIM_STRAIGHT","AIR_POWER_EFFECT","ALL","ALTERNATE_FORMATION","ARMORSET_ALTERNATE_FORMATION","ARMORSET_ELITE","ARMORSET_HERO","ARMORSET_MOUNTED","ARMORSET_PLAYER_UPGRADE","ARMORSET_PLAYER_UPGRADE_2","ARMORSET_PLAYER_UPGRADE_3","ARMORSET_UNBESIEGEABLE","ARMORSET_VETERAN","ARMORSET_WEAK_VERSUS_BASEDEFENSES","ATTACHED","ATTACKING","ATTACKING_POSITION","ATTACKING_STRUCTURE","ATTACK_MOVING","AWAITING_CONSTRUCTION","BACKCRUSHED","BACKING_UP","BASE_BUILD","BEING_MAULED","BETWEEN_FIRING_SHOTS_A","BETWEEN_FIRING_SHOTS_B","BETWEEN_FIRING_SHOTS_C","BETWEEN_FIRING_SHOTS_D","BETWEEN_FIRING_SHOTS_E","BUILD_PLACEMENT_CURSOR","BUILD_PLACEMENT_HINT","BUILD_VARIATION_ONE","BUILD_VARIATION_TWO","BURNED","BURNINGDEATH","BURNT_MODEL","BURNT_TEXTURE","CANCEL_CAPTURE","CAPTURED","CAPTURE_0","CAPTURE_100","CAPTURE_25","CAPTURE_50","CAPTURE_75","CAPTURING","CARRYING","CHARGING","CHRONORIFT","CLIMBING","COLLAPSING","COMBINED_CHILD","COMBINED_PARENT","COMING_OUT_OF_FACTORY","CONTINUOUS_FIRE_FAST","CONTINUOUS_FIRE_MEAN","CONTINUOUS_FIRE_SLOW","COVER","DAMAGED","DEATH_1","DEATH_10","DEATH_11","DEATH_12","DEATH_13","DEATH_14","DEATH_15","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEATH_6","DEATH_7","DEATH_8","DEATH_9","DEBUG","DECAY","DECELERATE","DEFLECT_SPECIAL_POWER","DEPLOYED","DESTROYED_BACK","DESTROYED_FRONT","DESTROYED_LEFT","DESTROYED_RIGHT","DESTROYED_WEAPON","DESTROYED_WHILST_BEING_CONSTRUCTED","DISGUISED","DIVING","DOCKING","DOCKING_ACTIVE","DOCKING_BEGINNING","DOCKING_ENDING","DOCKING_EXTRACT","DOCKING_FILL","DOCKING_MOVEBACK","DOCKING_PRE_DOCK","DOCKING_RETURN","DOOR_1_CLOSING","DOOR_1_OPENING","DOOR_1_WAITING_OPEN","DOOR_1_WAITING_TO_CLOSE","DOOR_2_CLOSING","DOOR_2_OPENING","DOOR_2_WAITING_OPEN","DOOR_2_WAITING_TO_CLOSE","DOOR_3_CLOSING","DOOR_3_OPENING","DOOR_3_WAITING_OPEN","DOOR_3_WAITING_TO_CLOSE","DOOR_4_CLOSING","DOOR_4_OPENING","DOOR_4_WAITING_OPEN","DOOR_4_WAITING_TO_CLOSE","DOOR_HELIPAD_CLOSING","DOOR_HELIPAD_OPENING","DOOR_HELIPAD_WAITING_OPEN","DOOR_HELIPAD_WAITING_TO_CLOSE","DRAFTED","DYING","DYING_WASATTACKING","DYING_WASMOVING","EATING","EMOTION_AFRAID","EMOTION_ALERT","EMOTION_AMUSED","EMOTION_BRACE_FOR_BEING_CRUSHED","EMOTION_CELEBRATING","EMOTION_CHEER_FOR_ABOUT_TO_CRUSH","EMOTION_COWER","EMOTION_DISSIDENT","EMOTION_DOOM","EMOTION_GUNG_HO","EMOTION_LOOK_TO_SKY","EMOTION_MORALE_HIGH","EMOTION_MORALE_LOW","EMOTION_PANIC","EMOTION_POINTING","EMOTION_TAUNTING","EMOTION_TERROR","EMOTION_UNCONTROLLABLY_AFRAID","ENGAGED","EXPLODED_BOUNCING","EXPLODED_FLAILING","FIRING_A","FIRING_B","FIRING_C","FIRING_D","FIRING_E","FIRING_OR_PREATTACK_A","FIRING_OR_PREATTACK_B","FIRING_OR_PREATTACK_C","FIRING_OR_PREATTACK_D","FIRING_OR_PREATTACK_E","FIRING_OR_RELOADING_A","FIRING_OR_RELOADING_B","FIRING_OR_RELOADING_C","FIRING_OR_RELOADING_D","FIRING_OR_RELOADING_E","FLOODED","FLYING","FORMATION_PREVIEW","FREEFALL","FRONTCRUSHED","FROZEN","GARRISONED","GUARDING","HARVEST_ACTION","HARVEST_PREPARATION","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIGH_TECH_EFFECT","HIT_LEVEL_1","HIT_LEVEL_2","HIT_LEVEL_3","HIT_REACTION","HORDE_EMPTY","IMPENDING_DOOM","INFILTRATED_DISABLE","INFILTRATED_ENERGY","INFILTRATED_RADAR_FREEZE","INFILTRATED_RESET","INFILTRATED_STEAL_MONEY","INFILTRATED_VISION","INFILTRATED_VISION_UNITS","INSIDE_GARRISON","INVALID","INVISIBLE_CAMOUFLAGE","INVISIBLE_STEALTH","INVULNERABLE","IRONCURTAIN","JETAFTERBURNER","JETEXHAUST","JUST_BUILT","LANDING","LAUNCHED","LEASHED_RETURNING","LEVELED","LOADED","LOCOMOTOR_BURNINGDEATH","LOCOMOTOR_CHANGING_FIRINGARC","LOCOMOTOR_COMBO","LOCOMOTOR_COMBO2","LOCOMOTOR_COMBO3","LOCOMOTOR_CONTAINED","LOCOMOTOR_ENRAGED","LOCOMOTOR_FREEFALL","LOCOMOTOR_MOUNTED","LOCOMOTOR_NORMAL_UPGRADED","LOCOMOTOR_PANIC","LOCOMOTOR_SCARED","LOCOMOTOR_SUPERSONIC","LOCOMOTOR_TAXIING","LOCOMOTOR_WALL_SCALING","LOCOMOTOR_WANDER","MARCHING","MARKED_FOR_HUNT_TACTIC","MARKED_FOR_NO_SKIRMISH_RECRUIT","MONEY_STORED_AMOUNT_1","MONEY_STORED_AMOUNT_2","MONEY_STORED_AMOUNT_3","MONEY_STORED_AMOUNT_4","MOUNTED","MOVING","MOVING_OUT_OF_THE_WAY","NIGHT","OVER_WATER","PACKING","PACKING_TYPE_1","PACKING_TYPE_2","PACKING_TYPE_3","PACKING_TYPE_4","PACKING_TYPE_5","PACKING_TYPE_6","PANICKING","PARACHUTE_LAND","PARACHUTING","PARALYZED","PARKINGPLACE_1_DOOR_CLOSED","PARKINGPLACE_1_DOOR_CLOSING","PARKINGPLACE_1_DOOR_OPEN","PARKINGPLACE_1_DOOR_OPENING","PARKINGPLACE_2_DOOR_CLOSED","PARKINGPLACE_2_DOOR_CLOSING","PARKINGPLACE_2_DOOR_OPEN","PARKINGPLACE_2_DOOR_OPENING","PARKINGPLACE_3_DOOR_CLOSED","PARKINGPLACE_3_DOOR_CLOSING","PARKINGPLACE_3_DOOR_OPEN","PARKINGPLACE_3_DOOR_OPENING","PARKINGPLACE_4_DOOR_CLOSED","PARKINGPLACE_4_DOOR_CLOSING","PARKINGPLACE_4_DOOR_OPEN","PARKINGPLACE_4_DOOR_OPENING","PARKINGPLACE_HELIPAD_DOOR_CLOSED","PARKINGPLACE_HELIPAD_DOOR_CLOSING","PARKINGPLACE_HELIPAD_DOOR_OPEN","PARKINGPLACE_HELIPAD_DOOR_OPENING","PARTIALLY_CONSTRUCTED","PASSENGER","PASSENGER_VARIATION_1","PASSENGER_VARIATION_2","PASSENGER_VARIATION_3","PASSENGER_VARIATION_4","PASSENGER_VARIATION_5","PHANTOM_STRUCTURE","PLAYER_POWER_1","POST_COLLAPSE","POST_RUBBLE","POWER_PLANT_UPGRADED","POWER_PLANT_UPGRADING","PREATTACK_A","PREATTACK_B","PREATTACK_C","PREATTACK_D","PREATTACK_E","PREORDER","PREPARING","PRIMARY_FORMATION","PRONE","RADAR_EXTENDING","RADAR_UPGRADED","RAISING_FLAG","RAPPELLING","REACT_1","REACT_2","REACT_3","REACT_4","REACT_5","REACT_6","REALLYDAMAGED","RELOADING_A","RELOADING_B","RELOADING_C","RELOADING_D","RELOADING_E","REPAIRING_DISABLED","RESURRECTED","RIDER1","RIDER2","RIDER3","RIDER4","RIDER5","RIDER6","RIDER7","RIDER8","RIDERLESS","RUBBLE","RUNNING_OFF_MAP","SAIL_BLOWN_LEFT","SAIL_BLOWN_RIGHT","SAIL_FLAPPING","SCALING_WALL","SCALING_WALL_HORDE","SELECTED","SHRINK_EFFECT","SIEGE_CONTAIN","SMOLDERING","SNOW","SOLD","SPECIALPOWER1_READY","SPECIALPOWER2_READY","SPECIALPOWER3_READY","SPECIALPOWER4_READY","SPECIALPOWER5_READY","SPECIALPOWER6_READY","SPECIALPOWER7_READY","SPECIALPOWER8_READY","SPECIALPOWER9_READY","SPECIAL_CHEERING","SPECIAL_DAMAGED","SPECIAL_ENEMY_NEAR","SPECIAL_POWER_1","SPECIAL_POWER_2","SPECIAL_POWER_3","SPECIAL_WEAPON_FIVE","SPECIAL_WEAPON_FOUR","SPECIAL_WEAPON_ONE","SPECIAL_WEAPON_SIX","SPECIAL_WEAPON_THREE","SPECIAL_WEAPON_TWO","SPLATTED","START_CAPTURE","STRUCTURE_UNPACKING","STUNNED","STUNNED_FLAILING","STUNNED_STANDING_UP","SUCKED_UP_HIGH","SUPPRESSED","SWAPPING_TO_WEAPONSET_1","SWAPPING_TO_WEAPONSET_2","SWAPPING_TO_WEAPONSET_3","SWIMMING","SWOOPING","TAKING_OFF","THROWN_PROJECTILE","TIBERIUM_CRYSTAL_TYPE1","TIBERIUM_CRYSTAL_TYPE2","TIBERIUM_CRYSTAL_TYPE3","TIBERIUM_CRYSTAL_TYPE4","TIBERIUM_CRYSTAL_TYPE5","TIBERIUM_GROWING","TOPPLED","TRANSPORT_MOVING","TRANSPORT_STOPPED","TURN_LEFT","TURN_LEFT_HIGH_SPEED","TURN_RIGHT","TURN_RIGHT_HIGH_SPEED","TURRET_ANGLE_0","TURRET_ANGLE_180","TURRET_ANGLE_270","TURRET_ANGLE_90","TURRET_ROTATE","UNCONTROLLABLE","UNDERPOWERED","UNIT_ACTIVELY_BEING_CONSTRUCTED","UNPACKING","UPGRADED_ARMOR","USER_1","USER_10","USER_2","USER_3","USER_4","USER_5","USER_6","USER_7","USER_8","USER_9","USING_COMBO_LOCOMOTOR","USING_SPECIAL_ABILITY","USING_WEAPON_A","USING_WEAPON_B","USING_WEAPON_C","USING_WEAPON_D","USING_WEAPON_E","VEHICLE_CRUSH_CENTER","VEHICLE_CRUSH_LEFT","VEHICLE_CRUSH_RIGHT","WADING","WALKING","WALL_SEGMENT","WANDER","WEAPONLOCK_PRIMARY","WEAPONLOCK_QUATERNARY","WEAPONLOCK_QUINARY","WEAPONLOCK_SECONDARY","WEAPONLOCK_TERTIARY","WEAPONSET_CLOSE_RANGE","WEAPONSET_CONTAINED","WEAPONSET_CONTESTING_BUILDING","WEAPONSET_ELITE","WEAPONSET_GARRISONED","WEAPONSET_HERO","WEAPONSET_MOUNTED","WEAPONSET_PASSENGER_TYPE_ONE","WEAPONSET_PASSENGER_TYPE_TWO","WEAPONSET_PLAYER_UPGRADE","WEAPONSET_RIDER1","WEAPONSET_RIDER2","WEAPONSET_RIDER3","WEAPONSET_RIDER4","WEAPONSET_SPECIAL_ONE","WEAPONSET_SPECIAL_TWO","WEAPONSET_SPECIAL_UPGRADE","WEAPONSET_TOGGLE_1","WEAPONSET_TOGGLE_2","WEAPONSET_TOGGLE_3","WEAPONSET_VETERAN","WEAPONSLOTID_01","WEAPONSLOTID_02","WEAPONSLOTID_03","WEAPONSLOTID_04","WEAPONSLOTID_05","WEAPONSLOTID_06","WEAPONSLOTID_07","WEAPONSLOTID_08","WEAPONSLOTID_09","WEAPONSLOTID_10","WEAPONSTATE_CLOSE_RANGE","WEAPONSTATE_CONTAINED","WEAPONSTATE_ONE","WEAPONSTATE_THREE","WEAPONSTATE_TWO","WEAPON_TOGGLING","WORLD_BUILDER","UNKNOWN","moving",""," ","+MOVING","-MOVING","MOVING MOVING","MOVING -MOVING","ALL -MOVING","-ALL MOVING","all","NONE","MOVING\tATTACKING"};
    const string NS="uri:ea.com:eala:asset";
    static readonly string[] Types={"Weather","WaterTransparency","WaterSetting","Environment","LightingSettings","WaterTextureList","TerrainParticleMapping","BounceKickTerrainMapping","ShadowMap","OrdersManagerSettings","TerrainVisualSettings","CameraSettings"};
    static readonly int[] Sizes={24,16,12,156,48,16,16,12,16,128,28,84};
    static readonly uint[] TypeIds={2430161325u,1479531562u,2728605516u,680780553u,3169690211u,2995571269u,1469498551u,2368878637u,4157475773u,2996766068u,1184276792u,3898802054u};
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
        uint previous=0;for(int i=0;i<bytes.Length-4;i+=4){uint at=Word(bytes,i,big);Check(at+4<=instance.Length,label+" field bounds");Check(i==0||at>previous,label+" sorted unique offsets");previous=at;if(relocation){uint target=Word(instance,(int)at,big);Check(target%4==0&&target<instance.Length,label+" target bounds/alignment");}}
    }
    static AssetBuffer TestDoc(string label,XmlDocument d,TargetPlatform p){
        try {
            InstanceDeclaration declaration=Declaration(d);AssetBuffer a=original.ProcessInstance(declaration);
            Type type=typeof(XmlenvironmentCompiler).Assembly.GetType("Recovered."+d.DocumentElement.LocalName+"Compiler");
            AssetBuffer b=Call(type,"ProcessInstance",new object[]{declaration,p});
            AssetBuffer direct=Call(type,"CompileXml",new object[]{d.DocumentElement,Namespaces(d),p!=TargetPlatform.Win32});
            Equal(label+"/instance",a.InstanceData,b.InstanceData);Equal(label+"/relocation",a.RelocationData,b.RelocationData);Equal(label+"/imports",a.ImportsData,b.ImportsData);
            Equal(label+"/direct-instance",a.InstanceData,direct.InstanceData);Equal(label+"/direct-relocation",a.RelocationData,direct.RelocationData);Equal(label+"/direct-imports",a.ImportsData,direct.ImportsData);
            bool big=p!=TargetPlatform.Win32;Check(Word(b.InstanceData,0,big)==0,label+" reserved root word");
            SideArray(b.RelocationData,b.InstanceData,big,true,label+" relocation");SideArray(b.ImportsData,b.InstanceData,big,false,label+" import");++fixtures;return b;
        }catch(Exception e){throw new Exception(label+" platform="+(int)p+" xml="+d.OuterXml,e);}
    }
    static AssetBuffer Test(string label,string xml,TargetPlatform p){return TestDoc(label,Parse(xml),p);}
    static void Reject(string label,string xml){XmlDocument d=Parse(xml);try{ PluginSchemaBoundary.Validate(d); XmlenvironmentCompiler.CompileXml(d.DocumentElement,Namespaces(d),false);}catch(NotSupportedException){++rejections;return;}throw new Exception("Expected rejection: "+label);}
    static XmlDocument FieldDocument(string root,string path,string name,string value){XmlDocument d=Parse(Root(root,"",""));XmlElement node=d.DocumentElement;if(path.Length!=0)foreach(string part in path.Split('/')){XmlElement child=d.CreateElement(part,NS);node.AppendChild(child);node=child;}node.SetAttribute(name,value);return d;}
    static string[] EnumValues(string name){switch(name){case "WeatherType":return new string[]{"CLOUDY","CLOUDYRAINY","INVALID","NORMAL","RAINY","SNOWY","SUNNY","unknown","normal","","0"};case "TimeOfDayType":return new string[]{"AFTERNOON","EVENING","INTERPOLATE","INVALID","MORNING","NIGHT","unknown","morning","","0"};case "WaterTextureType":return new string[]{"BUMP","FIRST","NOISE","NORMAL","RIVER","SKY","SPARKLE","unknown","river","","0"};case "TerrainClassType":return new string[]{"Cliff","Dirt","Grass","Misc","Mud","Road","Rock","Sand","Shrub","Snow","UNSPECIFIED","unknown","grass","","0"};case "ShadowType":return new string[]{"ADDITIVE_DECAL","ADDITIVE_DECAL_DYNAMIC","ALPHA_DECAL","ALPHA_DECAL_DYNAMIC","DECAL","MERGE_DECAL","NONE","unknown","none","","0"};case "DistributionType":return new string[]{"CONSTANT","GAUSSIAN","HIGH_BIAS","LOW_BIAS","TRIANGULAR","UNIFORM","unknown","uniform","","0"};default:throw new Exception("Unknown test enum");}}
    static string[][] FieldCases=new string[][] {
        new string[]{"Weather","Rain","RainTexture","Reference",""},
        new string[]{"Weather","Rain","IsRaining","Bool",""},
        new string[]{"Weather","Rain","NumRaindropsPerBox","Signed",""},
        new string[]{"Weather","Rain","RainBoxWidth","Float",""},
        new string[]{"Weather","Rain","RainBoxLength","Float",""},
        new string[]{"Weather","Rain","RainBoxHeight","Float",""},
        new string[]{"Weather","Rain","MinWidth","Float",""},
        new string[]{"Weather","Rain","MaxWidth","Float",""},
        new string[]{"Weather","Rain","MinHeight","Float",""},
        new string[]{"Weather","Rain","MaxHeight","Float",""},
        new string[]{"Weather","Rain","MinSpeed","Float",""},
        new string[]{"Weather","Rain","MaxSpeed","Float",""},
        new string[]{"Weather","Rain","MinAlpha","Float",""},
        new string[]{"Weather","Rain","MaxAlpha","Float",""},
        new string[]{"Weather","Rain","WindStrength","Float",""},
        new string[]{"Weather","Lightning","LightningDuration","Signed",""},
        new string[]{"Weather","Lightning","LightningChance","Float",""},
        new string[]{"Weather","Lightning/LightningFactor","Type","Enum","DistributionType"},
        new string[]{"Weather","Lightning/LightningFactor","Low","Float",""},
        new string[]{"Weather","Lightning/LightningFactor","High","Float",""},
        new string[]{"Weather","Cloud/CloudTextureSize","x","Float",""},
        new string[]{"Weather","Cloud/CloudTextureSize","y","Float",""},
        new string[]{"Weather","Cloud/CloudOffsetPerSecond","x","Float",""},
        new string[]{"Weather","Cloud/CloudOffsetPerSecond","y","Float",""},
        new string[]{"Weather","WeatherData","id","Enum","WeatherType"},
        new string[]{"Weather","WeatherData","HasLightning","Bool",""},
        new string[]{"Weather","WeatherData","Sound","Reference",""},
        new string[]{"WaterTransparency","","TransparentWaterDepth","Float",""},
        new string[]{"WaterTransparency","","TransparentWaterMinOpacity","Float",""},
        new string[]{"WaterTransparency","","StandingWaterTexture","Hash",""},
        new string[]{"WaterSetting","","TimeOfDay","Enum","TimeOfDayType"},
        new string[]{"WaterSetting","DiffuseColor","R","Unsigned",""},
        new string[]{"WaterSetting","DiffuseColor","G","Unsigned",""},
        new string[]{"WaterSetting","DiffuseColor","B","Unsigned",""},
        new string[]{"WaterSetting","DiffuseColor","A","Unsigned",""},
        new string[]{"Environment","CloudEffect","CloudTexture","Reference",""},
        new string[]{"Environment","CloudEffect","DarkCloudTexture","Reference",""},
        new string[]{"Environment","CloudEffect","AlphaTexture","Reference",""},
        new string[]{"Environment","CloudEffect","PropagateSpeed","Float",""},
        new string[]{"Environment","CloudEffect","Angle","Signed",""},
        new string[]{"Environment","CloudEffect","DarkeningRate","Signed",""},
        new string[]{"Environment","CloudEffect","LighteningRate","Signed",""},
        new string[]{"Environment","CloudEffect","CloudScrollSpeed","Float",""},
        new string[]{"Environment","CloudEffect","DissipateTexture","Reference",""},
        new string[]{"Environment","CloudEffect","DissipateStartLevel","Float",""},
        new string[]{"Environment","CloudEffect","DissipateSpeed","Float",""},
        new string[]{"Environment","CloudEffect","DissipateRateScale","Float",""},
        new string[]{"Environment","CloudEffect","LightningShadows","Bool",""},
        new string[]{"Environment","CloudEffect","JitterLightningLightIntensity","Bool",""},
        new string[]{"Environment","CloudEffect","JitterLightningLightPosition","Bool",""},
        new string[]{"Environment","CloudEffect","LightningChance","Float",""},
        new string[]{"Environment","CloudEffect","LightningFrequency","Float",""},
        new string[]{"Environment","CloudEffect","LightningShadowIntensity","Float",""},
        new string[]{"Environment","CloudEffect","LightningFX","Hash",""},
        new string[]{"Environment","CloudEffect/DarkeningFactor","R","Float",""},
        new string[]{"Environment","CloudEffect/DarkeningFactor","G","Float",""},
        new string[]{"Environment","CloudEffect/DarkeningFactor","B","Float",""},
        new string[]{"Environment","CloudEffect/DarkeningFactorRain","R","Float",""},
        new string[]{"Environment","CloudEffect/DarkeningFactorRain","G","Float",""},
        new string[]{"Environment","CloudEffect/DarkeningFactorRain","B","Float",""},
        new string[]{"Environment","CloudEffect/LightningShadowColor","R","Float",""},
        new string[]{"Environment","CloudEffect/LightningShadowColor","G","Float",""},
        new string[]{"Environment","CloudEffect/LightningShadowColor","B","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition1","x","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition1","y","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition2","x","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition2","y","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition3","x","Float",""},
        new string[]{"Environment","CloudEffect/LightningLightPosition3","y","Float",""},
        new string[]{"Environment","CloudEffect/LightningIntensity","Type","Enum","DistributionType"},
        new string[]{"Environment","CloudEffect/LightningIntensity","Low","Float",""},
        new string[]{"Environment","CloudEffect/LightningIntensity","High","Float",""},
        new string[]{"Environment","CloudEffect/LightningDuration","Type","Enum","DistributionType"},
        new string[]{"Environment","CloudEffect/LightningDuration","Low","Float",""},
        new string[]{"Environment","CloudEffect/LightningDuration","High","Float",""},
        new string[]{"LightingSettings","","TimeOfDay","Enum","TimeOfDayType"},
        new string[]{"LightingSettings","","OccludedLuminanceScale","Float",""},
        new string[]{"LightingSettings","","LoadAfter","Reference",""},
        new string[]{"LightingSettings","LightingMultiplierIfNoClouds","R","Float",""},
        new string[]{"LightingSettings","LightingMultiplierIfNoClouds","G","Float",""},
        new string[]{"LightingSettings","LightingMultiplierIfNoClouds","B","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings","TimeOfDay","Enum","TimeOfDayType"},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Ambient","R","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Ambient","G","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Ambient","B","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Diffuse","R","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Diffuse","G","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/Diffuse","B","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/LightPos","x","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/LightPos","y","Float",""},
        new string[]{"LightingSettings","CurrentLightingSettings/Lighting/LightPos","z","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup","TimeOfDay","Enum","TimeOfDayType"},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Ambient","R","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Ambient","G","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Ambient","B","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Diffuse","R","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Diffuse","G","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/Diffuse","B","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/LightPos","x","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/LightPos","y","Float",""},
        new string[]{"LightingSettings","TerrainLighting/LightingTimeGroup/Lighting/LightPos","z","Float",""},
        new string[]{"WaterTextureList","","Type","Enum","WaterTextureType"},
        new string[]{"TerrainParticleMapping","","AccelerationMultiplier","Percentage",""},
        new string[]{"TerrainParticleMapping","Mapping","TerrainClass","Enum","TerrainClassType"},
        new string[]{"TerrainParticleMapping","Mapping","ParticleSystem","Reference",""},
        new string[]{"TerrainParticleMapping","Mapping","RequiredModelConditions","ModelFlags",""},
        new string[]{"TerrainParticleMapping","Mapping","ForbiddenModelConditions","ModelFlags",""},
        new string[]{"BounceKickTerrainMapping","Mapping","TerrainClass","Enum","TerrainClassType"},
        new string[]{"BounceKickTerrainMapping","Mapping","BounceMultiplier","Percentage",""},
        new string[]{"ShadowMap","","MapSize","Signed",""},
        new string[]{"ShadowMap","","MaxViewDistance","Float",""},
        new string[]{"ShadowMap","","MinShadowedTerrainHeight","Float",""},
        new string[]{"OrdersManagerSettings","","LineFadeTime","Time",""},
        new string[]{"OrdersManagerSettings","","FormationPreviewShader","NullableReference",""},
        new string[]{"OrdersManagerSettings","","BuildPlacementShader","NullableReference",""},
        new string[]{"OrdersManagerSettings","ExecutingLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","ExecutingLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","ExecutingLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","ExecutingLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","PendingLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","PendingLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","PendingLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PendingLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","PatrolLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","PatrolLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","PatrolLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","PatrolPendingLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","PatrolPendingLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","PatrolPendingLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolPendingLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","TargetMoveLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","TargetMoveLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","TargetMoveLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","TargetMoveLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","TargetAttackLine","Width","Float",""},
        new string[]{"OrdersManagerSettings","TargetAttackLine","VOffsetRate","Float",""},
        new string[]{"OrdersManagerSettings","TargetAttackLine","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","TargetAttackLine","TextureTileSize","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","Type","Enum","ShadowType"},
        new string[]{"OrdersManagerSettings","PatrolNode","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolNode","AdditionalTexture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolNode","SizeX","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","SizeY","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OffsetX","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OffsetY","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OpacityStart","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OpacityFadeInTime","Time",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OpacityPeak","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OpacityFadeOutTime","Time",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OpacityEnd","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","MaxHeight","Float",""},
        new string[]{"OrdersManagerSettings","PatrolNode","OverrideLODVisibility","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolNode","UseHouseColor","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolNode","IsRotatingWithObject","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolNode","LocalPlayerOnly","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolNode","SunAngle","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","Type","Enum","ShadowType"},
        new string[]{"OrdersManagerSettings","PatrolStart","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolStart","AdditionalTexture","Reference",""},
        new string[]{"OrdersManagerSettings","PatrolStart","SizeX","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","SizeY","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OffsetX","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OffsetY","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OpacityStart","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OpacityFadeInTime","Time",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OpacityPeak","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OpacityFadeOutTime","Time",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OpacityEnd","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","MaxHeight","Float",""},
        new string[]{"OrdersManagerSettings","PatrolStart","OverrideLODVisibility","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolStart","UseHouseColor","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolStart","IsRotatingWithObject","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolStart","LocalPlayerOnly","Bool",""},
        new string[]{"OrdersManagerSettings","PatrolStart","SunAngle","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","Type","Enum","ShadowType"},
        new string[]{"OrdersManagerSettings","PendingNode","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PendingNode","AdditionalTexture","Reference",""},
        new string[]{"OrdersManagerSettings","PendingNode","SizeX","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","SizeY","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OffsetX","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OffsetY","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OpacityStart","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OpacityFadeInTime","Time",""},
        new string[]{"OrdersManagerSettings","PendingNode","OpacityPeak","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OpacityFadeOutTime","Time",""},
        new string[]{"OrdersManagerSettings","PendingNode","OpacityEnd","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","MaxHeight","Float",""},
        new string[]{"OrdersManagerSettings","PendingNode","OverrideLODVisibility","Bool",""},
        new string[]{"OrdersManagerSettings","PendingNode","UseHouseColor","Bool",""},
        new string[]{"OrdersManagerSettings","PendingNode","IsRotatingWithObject","Bool",""},
        new string[]{"OrdersManagerSettings","PendingNode","LocalPlayerOnly","Bool",""},
        new string[]{"OrdersManagerSettings","PendingNode","SunAngle","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","Type","Enum","ShadowType"},
        new string[]{"OrdersManagerSettings","PendingStart","Texture","Reference",""},
        new string[]{"OrdersManagerSettings","PendingStart","AdditionalTexture","Reference",""},
        new string[]{"OrdersManagerSettings","PendingStart","SizeX","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","SizeY","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OffsetX","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OffsetY","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OpacityStart","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OpacityFadeInTime","Time",""},
        new string[]{"OrdersManagerSettings","PendingStart","OpacityPeak","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OpacityFadeOutTime","Time",""},
        new string[]{"OrdersManagerSettings","PendingStart","OpacityEnd","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","MaxHeight","Float",""},
        new string[]{"OrdersManagerSettings","PendingStart","OverrideLODVisibility","Bool",""},
        new string[]{"OrdersManagerSettings","PendingStart","UseHouseColor","Bool",""},
        new string[]{"OrdersManagerSettings","PendingStart","IsRotatingWithObject","Bool",""},
        new string[]{"OrdersManagerSettings","PendingStart","LocalPlayerOnly","Bool",""},
        new string[]{"OrdersManagerSettings","PendingStart","SunAngle","Float",""},
        new string[]{"TerrainVisualSettings","","LoadAfter","Reference",""},
        new string[]{"TerrainVisualSettings","WaterPosition","x","Float",""},
        new string[]{"TerrainVisualSettings","WaterPosition","y","Float",""},
        new string[]{"TerrainVisualSettings","WaterPosition","z","Float",""},
        new string[]{"TerrainVisualSettings","WaterExtents","x","Float",""},
        new string[]{"TerrainVisualSettings","WaterExtents","y","Float",""},
        new string[]{"CameraSettings","","DefaultMinHeight","Float",""},
        new string[]{"CameraSettings","","DefaultMaxHeight","Float",""},
        new string[]{"CameraSettings","","DefaultPitchAngle","Float",""},
        new string[]{"CameraSettings","","DefaultYawAngle","Float",""},
        new string[]{"CameraSettings","","DefaultScrollSpeedScalar","Float",""},
        new string[]{"CameraSettings","","DefaultHeightMapSmoothnessScalar","Float",""},
        new string[]{"CameraSettings","","ScrollAmountCutoff","Float",""},
        new string[]{"CameraSettings","","ScrollPitchMultiplier","Float",""},
        new string[]{"CameraSettings","","CameraAdjustSpeed","Float",""},
        new string[]{"CameraSettings","","ShakeSubtleIntensity","Float",""},
        new string[]{"CameraSettings","","ShakeNormalIntensity","Float",""},
        new string[]{"CameraSettings","","ShakeStrongIntensity","Float",""},
        new string[]{"CameraSettings","","ShakeSevereIntensity","Float",""},
        new string[]{"CameraSettings","","ShakeCineExtremeIntensity","Float",""},
        new string[]{"CameraSettings","","ShakeCineInsaneIntensity","Float",""},
        new string[]{"CameraSettings","","MaxShakeIntensity","Float",""},
        new string[]{"CameraSettings","","MaxShakeRange","Float",""},
        new string[]{"CameraSettings","","CameraLockHeightDelta","Float",""},
        new string[]{"CameraSettings","","CameraTerrainSampleRadiusForHeight","Float",""},
        new string[]{"CameraSettings","","CameraEaseFactor","Float",""},
    };
    static void AbiCases(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer weather=Test("ABI/Weather",Root("Weather","","<Rain RainTexture='Name\\7' IsRaining='true'/><Lightning><LightningFactor Type='CONSTANT' Low='2' High='3'/></Lightning><Cloud><CloudTextureSize x='10' y='20'/><CloudOffsetPerSecond x='1' y='2'/></Cloud><WeatherData id='RAINY' Sound='Name\\11' HasLightning='true'/><WeatherData id='SUNNY' Sound='Name\\13'/ >".Replace("/ >","/>")),p);
        Check(weather.InstanceData.Length==156,"Weather independent full size");
        int[] at={4,8,12,20,92,108,112};uint[] target={24u,84u,108u,132u,96u,116u,124u};
        for(int i=0;i<at.Length;++i)Check(Word(weather.InstanceData,at[i],big)==target[i],"Weather independent pointer "+at[i]);
        Check(Word(weather.InstanceData,16,big)==2&&Word(weather.InstanceData,132,big)==2&&Word(weather.InstanceData,144,big)==4,"Weather independent list/enum");
        Check(weather.InstanceData[80]==1&&weather.InstanceData[81]==0&&weather.InstanceData[82]==0&&weather.InstanceData[83]==0,"Rain packed bool/padding");
        Check(Word(weather.ImportsData,0,big)==24&&Word(weather.ImportsData,4,big)==136&&Word(weather.ImportsData,8,big)==148,"Weather reference allocated field offsets");
        AssetBuffer color=Test("ABI/WaterSetting",Root("WaterSetting","TimeOfDay='NIGHT'","<DiffuseColor R='17' G='34' B='51' A='68'/>"),p);
        Check(Word(color.InstanceData,4,big)==4&&Word(color.InstanceData,8,big)==0x44112233u,"packed ARGB and enum independent");
        AssetBuffer emptyColor=Test("ABI/emptyColor",Root("WaterSetting","","<DiffuseColor/>"),p);Check(Word(emptyColor.InstanceData,8,big)==0xff000000u,"packed color default alpha");
        string light="<Lighting><Ambient R='0.1' G='0.2' B='0.3'/><Diffuse R='1' G='0.5' B='0.25'/><LightPos x='1' y='2' z='3'/></Lighting>";
        AssetBuffer lighting=Test("ABI/Lighting",Root("LightingSettings","LoadAfter='Name\\7' TimeOfDay='AFTERNOON'","<CurrentLightingSettings TimeOfDay='MORNING'>"+light+light+"</CurrentLightingSettings><TerrainLighting><LightingTimeGroup TimeOfDay='NIGHT'>"+light+"</LightingTimeGroup><LightingTimeGroup TimeOfDay='EVENING'>"+light+light+"</LightingTimeGroup></TerrainLighting>"),p);
        Check(lighting.InstanceData.Length==252,"Lighting nested array size");
        Check(Word(lighting.InstanceData,32,big)==2&&Word(lighting.InstanceData,36,big)==48&&Word(lighting.InstanceData,40,big)==2&&Word(lighting.InstanceData,44,big)==120,"Lighting inline headers");
        Check(Word(lighting.InstanceData,128,big)==144&&Word(lighting.InstanceData,140,big)==180,"Lighting nested pointer targets");
        Check(Word(lighting.ImportsData,0,big)==4,"Lighting LoadAfter import");
        AssetBuffer strings=Test("ABI/WaterTextureList",Root("WaterTextureList","Type='SKY'","<Texture>abc</Texture><Texture/><Texture>abcde</Texture>"),p);
        Check(strings.InstanceData.Length==56&&Word(strings.InstanceData,8,big)==3&&Word(strings.InstanceData,12,big)==16,"String list root size/count/target");
        Check(Word(strings.InstanceData,16,big)==3&&Word(strings.InstanceData,20,big)==40&&Word(strings.InstanceData,24,big)==0&&Word(strings.InstanceData,28,big)==44&&Word(strings.InstanceData,32,big)==5&&Word(strings.InstanceData,36,big)==48,"String item lengths/allocation order");
    }
    static void MappingAbi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer terrain=Test("ABI/TerrainParticleMapping",Root("TerrainParticleMapping","","<Mapping TerrainClass='Grass' ParticleSystem='Name\\7' RequiredModelConditions='MOVING' ForbiddenModelConditions='ALL'/><Mapping TerrainClass='unknown' ParticleSystem='Name\\11' RequiredModelConditions='UNKNOWN'/>"),p);
        Check(terrain.InstanceData.Length==272&&Word(terrain.InstanceData,4,big)==0x3f800000u&&Word(terrain.InstanceData,8,big)==2&&Word(terrain.InstanceData,12,big)==16,"terrain root/list/percentage independent");
        Check(Word(terrain.InstanceData,16,big)==4&&Word(terrain.InstanceData,40,big)==0x8000u&&Word(terrain.InstanceData,144,big)==3&&Word(terrain.InstanceData,180,big)==0x200u,"terrain enum/flag offsets and unknown fallbacks");
        for(int i=0;i<15;++i)Check(Word(terrain.InstanceData,84+4*i,big)==UInt32.MaxValue,"ALL preserves high padding bits");
        Check(Word(terrain.ImportsData,0,big)==20&&Word(terrain.ImportsData,4,big)==148,"terrain reference field offsets");
        AssetBuffer bounce=Test("ABI/BounceKickTerrainMapping",Root("BounceKickTerrainMapping","","<Mapping TerrainClass='Dirt' BounceMultiplier='50%'/><Mapping TerrainClass='unknown' BounceMultiplier='garbage'/>"),p);
        Check(bounce.InstanceData.Length==28&&Word(bounce.InstanceData,4,big)==2&&Word(bounce.InstanceData,8,big)==12&&Word(bounce.InstanceData,12,big)==2&&Word(bounce.InstanceData,16,big)==0x3f000000u&&Word(bounce.InstanceData,20,big)==3&&Word(bounce.InstanceData,24,big)==0,"bounce independent list and percentage");
    }
    static void OrdersAbi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer shadow=Test("ABI/ShadowMap",Root("ShadowMap","MapSize='2048' MaxViewDistance='512' MinShadowedTerrainHeight='-2.5'",""),p);
        Check(shadow.InstanceData.Length==16&&Word(shadow.InstanceData,4,big)==2048&&Word(shadow.InstanceData,8,big)==0x44000000u&&Word(shadow.InstanceData,12,big)==0xc0200000u,"ShadowMap independent signed/float words");
        string[] lines={"ExecutingLine","PendingLine","PatrolLine","PatrolPendingLine","TargetMoveLine","TargetAttackLine"};string[] nodes={"PatrolNode","PatrolStart","PendingNode","PendingStart"};StringBuilder body=new StringBuilder();
        for(int i=0;i<lines.Length;++i)body.Append("<"+lines[i]+" Texture='Name\\"+i+"'/>");
        for(int i=0;i<nodes.Length;++i)body.Append("<"+nodes[i]+" Texture='Name\\"+(10+i)+"' AdditionalTexture='Name\\"+(20+i)+"'/>");
        AssetBuffer orders=Test("ABI/Orders",Root("OrdersManagerSettings","FormationPreviewShader='Name\\30' BuildPlacementShader='Name\\31'",body.ToString()),p);
        Check(orders.InstanceData.Length==376&&Word(orders.InstanceData,4,big)==0x3e800000u,"Orders root size/time");
        for(int i=0;i<lines.Length;++i){Check(Word(orders.InstanceData,16+16*i,big)==0x3fc00000u&&Word(orders.InstanceData,28+16*i,big)==0x41f00000u,"line defaults and inline offsets");}
        for(int i=0;i<nodes.Length;++i){int at=136+60*i;Check(Word(orders.InstanceData,112+4*i,big)==at,"shadow pointer allocation order");Check(Word(orders.InstanceData,at+36,big)==0x3f800000u&&Word(orders.InstanceData,at+48,big)==0x41a00000u,"shadow initialized defaults");Check(orders.InstanceData[at+56]==0&&orders.InstanceData[at+57]==0&&orders.InstanceData[at+58]==1&&orders.InstanceData[at+59]==0,"shadow packed boolean defaults");}
        Check(Word(orders.InstanceData,8,big)==128&&Word(orders.InstanceData,12,big)==132,"nullable shader block allocation order");
        int[] imports={24,40,56,72,88,104,128,132,140,144,200,204,260,264,320,324};Check(orders.ImportsData.Length==4*(imports.Length+1),"Orders import count");for(int i=0;i<imports.Length;++i)Check(Word(orders.ImportsData,4*i,big)==imports[i],"Orders reference field offsets");
        Reject("duplicate order line",Root("OrdersManagerSettings","","<ExecutingLine/><ExecutingLine/>"));Reject("duplicate order shadow",Root("OrdersManagerSettings","","<PatrolNode/><PatrolNode/>"));
    }
    static void CameraAbi(TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;
        AssetBuffer terrain=Test("ABI/TerrainVisualSettings",Root("TerrainVisualSettings","LoadAfter='Name\\7'","<WaterPosition x='1' y='2' z='3'/><WaterExtents x='4' y='5'/>"),p);
        Check(terrain.InstanceData.Length==28&&Word(terrain.InstanceData,8,big)==0x3f800000u&&Word(terrain.InstanceData,12,big)==0x40000000u&&Word(terrain.InstanceData,16,big)==0x40400000u&&Word(terrain.InstanceData,20,big)==0x40800000u&&Word(terrain.InstanceData,24,big)==0x40a00000u,"TerrainVisual independent vectors");Check(terrain.RelocationData.Length==0&&Word(terrain.ImportsData,0,big)==4,"TerrainVisual direct LoadAfter import");
        string[] names=new string[]{"DefaultMinHeight","DefaultMaxHeight","DefaultPitchAngle","DefaultYawAngle","DefaultScrollSpeedScalar","DefaultHeightMapSmoothnessScalar","ScrollAmountCutoff","ScrollPitchMultiplier","CameraAdjustSpeed","ShakeSubtleIntensity","ShakeNormalIntensity","ShakeStrongIntensity","ShakeSevereIntensity","ShakeCineExtremeIntensity","ShakeCineInsaneIntensity","MaxShakeIntensity","MaxShakeRange","CameraLockHeightDelta","CameraTerrainSampleRadiusForHeight","CameraEaseFactor"};StringBuilder attributes=new StringBuilder();
        for(int i=0;i<names.Length;++i)attributes.Append(names[i]+"='"+(i+1)+"' ");
        AssetBuffer camera=Test("ABI/CameraSettings",Root("CameraSettings",attributes.ToString(),""),p);Check(camera.InstanceData.Length==84&&camera.RelocationData.Length==0&&camera.ImportsData.Length==0,"Camera independent fixed root size");
        for(int i=0;i<names.Length;++i)Check(Word(camera.InstanceData,4+4*i,big)==BitConverter.ToUInt32(BitConverter.GetBytes((float)(i+1)),0),"Camera field offset/no angle scaling "+names[i]);
        Reject("truncated camera literal",Root("CameraSettings","CameraTerrainSampleRadiusForHeig='1'",""));Reject("duplicate terrain water",Root("TerrainVisualSettings","","<WaterPosition/><WaterPosition/>"));
    }
    static void Matrix(TargetPlatform p) {
        original.Initialize(null,p);
        for(int i=0;i<Types.Length;++i) {
            AssetBuffer b=Test("defaults/"+Types[i],Root(Types[i],"",""),p);Check(b.InstanceData.Length==Sizes[i],"root independent size "+Types[i]);
            Test("metadata/"+Types[i],Root(Types[i],"TypeId='"+TypeIds[i]+"'",""),p);
            Reject("unknown attribute/"+Types[i],Root(Types[i],"Unknown='1'",""));Reject("qualified attribute/"+Types[i],Root(Types[i],"xmlns:q='urn:other' q:TypeId='1'",""));
            Reject("unknown child/"+Types[i],Root(Types[i],"","<Unknown/>"));Reject("foreign child/"+Types[i],Root(Types[i],"","<Unknown xmlns='urn:other'/>"));
            Reject("text/"+Types[i],Root(Types[i],"","payload"));
        }
        foreach(string[] f in FieldCases) {
            string[] values=f[3]=="Time"?TimeValues:f[3]=="ModelFlags"?ModelFlagValues:f[3]=="Enum"?EnumValues(f[4]):f[3]=="Bool"?Bools:(f[3]=="Reference"||f[3]=="NullableReference"||f[3]=="Hash")?Refs:Numbers;
            foreach(string value in values)TestDoc("field/"+f[0]+"/"+f[1]+"/"+f[2]+"/"+value,FieldDocument(f[0],f[1],f[2],value),p);
            XmlDocument metadata=FieldDocument(f[0],f[1],f[2],values[0]);foreach(XmlNode node in metadata.SelectNodes("//*"))((XmlElement)node).SetAttribute("TypeId","1");TestDoc("field-metadata/"+f[0]+"/"+f[1]+"/"+f[2],metadata,p);
            XmlDocument invalid=FieldDocument(f[0],f[1],f[2],values[0]);XmlNodeList nodes=invalid.SelectNodes("//*");((XmlElement)nodes[nodes.Count-1]).SetAttribute("Unknown","1");Reject("nested unknown",invalid.OuterXml);
        }
        foreach(int count in new int[]{0,1,2,3,27,28,29,64,257}) {
            StringBuilder data=new StringBuilder(),textures=new StringBuilder(),lights=new StringBuilder(),times=new StringBuilder();
            for(int i=0;i<count;++i) {
                data.Append("<WeatherData id='RAINY' Sound='Name\\"+i+"' HasLightning='true'/>");textures.Append("<Texture>Texture"+i+"</Texture>");
                lights.Append("<Lighting><Ambient R='0.1'/><Diffuse G='0.5'/><LightPos x='1'/></Lighting>");
                times.Append("<LightingTimeGroup TimeOfDay='NIGHT'><Lighting><Ambient/><Diffuse/><LightPos/></Lighting></LightingTimeGroup>");
            }
            StringBuilder terrain=new StringBuilder(),bounce=new StringBuilder();
            for(int i=0;i<count;++i){terrain.Append("<Mapping TerrainClass='Grass' ParticleSystem='Name\\"+i+"' RequiredModelConditions='MOVING -ATTACKING' ForbiddenModelConditions='SNOW'/>");bounce.Append("<Mapping TerrainClass='Sand' BounceMultiplier='25%'/>");}
            Test("terrain-list/"+count,Root("TerrainParticleMapping","AccelerationMultiplier='75%'",terrain.ToString()),p);
            Test("bounce-list/"+count,Root("BounceKickTerrainMapping","",bounce.ToString()),p);
            Test("weather-list/"+count,Root("Weather","",data.ToString()),p);Test("string-list/"+count,Root("WaterTextureList","Type='BUMP'",textures.ToString()),p);
            Test("lighting-list/"+count,Root("LightingSettings","","<CurrentLightingSettings TimeOfDay='MORNING'>"+lights+"</CurrentLightingSettings>"),p);
            Test("lighting-time-list/"+count,Root("LightingSettings","","<TerrainLighting>"+times+"</TerrainLighting>"),p);
        }
        foreach(string value in new string[]{"","a","abc","abcd","abcde","A&B<texture>","é","资源","😀","e\u0301","a\0tail","\0leading","多字节\0尾部"}) {
            XmlDocument d=Parse(Root("WaterTextureList","Type='RIVER'","<Texture/>"));d.DocumentElement.FirstChild.InnerText=value;TestDoc("ANSI-string/"+value.Length,d,p);
        }
        foreach(string type in new string[]{"Weather","Environment","LightingSettings"})Test("null-empty/"+type,Root(type,"",type=="Weather"?"<Rain/><Lightning><LightningFactor/></Lightning><Cloud><CloudTextureSize/><CloudOffsetPerSecond/></Cloud>":type=="Environment"?"<CloudEffect><DarkeningFactor/><LightningIntensity/><LightningDuration/></CloudEffect>":"<LightingMultiplierIfNoClouds/><CurrentLightingSettings/><TerrainLighting/>"),p);
        Reject("duplicate Rain",Root("Weather","","<Rain/><Rain/>"));Reject("duplicate color",Root("WaterSetting","","<DiffuseColor/><DiffuseColor/>"));Reject("string child",Root("WaterTextureList","","<Texture><Unknown/></Texture>"));Reject("unknown root",Root("Unknown","",""));
        AbiCases(p);
        MappingAbi(p);
        OrdersAbi(p);
        CameraAbi(p);
        bool rejected=false;try{XmlenvironmentCompiler.ProcessInstance(null,p);}catch(ArgumentNullException){rejected=true;}Check(rejected,"null declaration rejected");++rejections;
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
            Console.WriteLine("PASS Environment fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);return 0;
        }catch(Exception e){result["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}finally{if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));}
    }
}
