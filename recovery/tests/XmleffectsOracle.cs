using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
class XmleffectsOracle {
    const string Ns = "uri:ea.com:eala:asset";
    static int fixtures, comparisons, rejections, layouts;
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager ns) {
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration declaration = new InstanceDeclaration(document);
        declaration.XmlNode = xml.DocumentElement;
        return declaration;
    }
    static XmlDocument Xml(string text) { XmlDocument d = new XmlDocument(); d.LoadXml(text); return d; }
    static XmlNamespaceManager Namespaces(XmlDocument d) {
        XmlNamespaceManager ns = new XmlNamespaceManager(d.NameTable); ns.AddNamespace("ea", Ns); return ns;
    }
    static string FX(string attrs, string children) {
        return "<FXList xmlns='" + Ns + "' id='Oracle' " + attrs + ">" + children + "</FXList>";
    }
    static void Equal(string label, byte[] a, byte[] b) {
        if (a == null || b == null || a.Length != b.Length) throw new Exception(label + " length mismatch");
        for (int i = 0; i < a.Length; ++i) if (a[i] != b[i])
            throw new Exception(label + " byte=" + i + " original=" + a[i] + " recovered=" + b[i]);
        ++comparisons;
    }
    static uint Word(byte[] b, int at, bool big) {
        return big ? ((uint)b[at] << 24) | ((uint)b[at+1] << 16) | ((uint)b[at+2] << 8) | b[at+3] : BitConverter.ToUInt32(b, at);
    }
    static uint FloatBits(float x) { return BitConverter.ToUInt32(BitConverter.GetBytes(x), 0); }
    static AssetBuffer Test(IAssetBuilderPlugin original, string label, string text, TargetPlatform platform) {
        XmlDocument d = Xml(text); XmlNamespaceManager ns = Namespaces(d);
        InstanceDeclaration declaration = Declaration(d, ns);
        InstanceDeclaration originalDeclaration = declaration;
        bool prefixed = d.DocumentElement.Prefix.Length != 0;
        if (prefixed) {
            XmlDocument canonical = new XmlDocument();
            XmlElement root = canonical.CreateElement(d.DocumentElement.LocalName, Ns);
            foreach (XmlAttribute attr in d.DocumentElement.Attributes) root.Attributes.Append((XmlAttribute)canonical.ImportNode(attr, true));
            foreach (XmlNode child in d.DocumentElement.ChildNodes) root.AppendChild(canonical.ImportNode(child, true));
            canonical.AppendChild(root);
            originalDeclaration = Declaration(canonical, Namespaces(canonical));
            if (declaration.Handle.TypeId != 2254974584u) {
                try { FXListCompiler.ProcessInstance(declaration, platform); throw new Exception("Prefixed wrong TypeId was accepted"); }
                catch (NotSupportedException) { ++rejections; }
            }
        }
        original.Initialize(null, platform);
        AssetBuffer a = original.ProcessInstance(originalDeclaration);
        AssetBuffer b = prefixed ? FXListCompiler.CompileXml(d.DocumentElement, ns, platform != TargetPlatform.Win32) : FXListCompiler.ProcessInstance(declaration, platform);
        Equal(label + "/instance", a.InstanceData, b.InstanceData);
        Equal(label + "/relocations", a.RelocationData, b.RelocationData);
        Equal(label + "/imports", a.ImportsData, b.ImportsData);
        AssetBuffer c = FXListCompiler.CompileXml(d.DocumentElement, ns, platform != TargetPlatform.Win32);
        Equal(label + "/direct-instance", b.InstanceData, c.InstanceData);
        Equal(label + "/direct-relocations", b.RelocationData, c.RelocationData);
        Equal(label + "/direct-imports", b.ImportsData, c.ImportsData);
        bool big = platform != TargetPlatform.Win32;
        int expectedCount = 0;
        XmlNode listNode = null;
        foreach (XmlNode child in d.DocumentElement.ChildNodes) if (child.NodeType == XmlNodeType.Element && child.LocalName == "NuggetList") listNode = child;
        if (listNode != null) foreach (XmlNode child in listNode.ChildNodes) if (child.NodeType == XmlNodeType.Element) ++expectedCount;
        if (b.InstanceData.Length < 28 || Word(b.InstanceData, 0, big) != 0 ||
            Word(b.InstanceData, 16, big) != expectedCount || Word(b.InstanceData, 20, big) != (expectedCount == 0 ? 0u : 28u) ||
            b.InstanceData[26] != 0 || b.InstanceData[27] != 0)
            throw new Exception(label + " independent FXList root layout failed");
        if (expectedCount == 0 && (b.InstanceData.Length != 28 || b.RelocationData.Length != 0 || b.ImportsData.Length != 0))
            throw new Exception(label + " independent empty FXList layout failed");
        if (expectedCount > 0) {
            for (int n=0;n<expectedCount;++n) {
                uint at=Word(b.InstanceData,28+4*n,big);
                if (at < 28+4*expectedCount || at > b.InstanceData.Length-44)
                    throw new Exception(label + " independent nugget pointer/type layout failed");
            }
        }
        Tables(label,b,big);
        ++fixtures; ++layouts; return b;
    }

    static void Tables(string label, AssetBuffer buffer, bool big) {
        foreach (byte[] table in new byte[][] {buffer.RelocationData,buffer.ImportsData}) {
            if (table.Length == 0) continue;
            if ((table.Length & 3) != 0 || Word(table,table.Length-4,big) != UInt32.MaxValue) throw new Exception(label + " table terminator");
            for(int i=0;i<table.Length-4;i+=4) {
                uint at=Word(table,i,big);
                if (at > buffer.InstanceData.Length-4) throw new Exception(label + " table location");
                if (Object.ReferenceEquals(table,buffer.RelocationData) && Word(buffer.InstanceData,(int)at,big)>=buffer.InstanceData.Length)
                    throw new Exception(label + " relocation target");
            }
        }
    }


    static string Nugget(uint typeId, string attrs, string children) {
        return "<Nugget TypeId='"+typeId.ToString(CultureInfo.InvariantCulture)+"' "+attrs+">"+children+"</Nugget>";
    }
    static void Layout(AssetBuffer b, bool big, float cull, int min, int max, byte play, byte tail) {
        if (Word(b.InstanceData, 4, big) != FloatBits(cull) ||
            Word(b.InstanceData, 8, big) != unchecked((uint)min) ||
            Word(b.InstanceData, 12, big) != unchecked((uint)max) ||
            b.InstanceData[24] != play || b.InstanceData[25] != tail)
            throw new Exception("Independent scalar/endian layout mismatch");
        ++layouts;
    }
    static void Reject(string text, bool particle) {
        XmlDocument d = Xml(text);
        try { PluginSchemaBoundary.Validate(d);
            FXListCompiler.CompileXml(d.DocumentElement, Namespaces(d), false);
        } catch (NotSupportedException) { ++rejections; return; }
        throw new Exception("Expected explicit refusal: " + text);
    }

    static void FilterCases(IAssetBuilderPlugin original, TargetPlatform platform, uint[] ids) {
        bool big=platform!=TargetPlatform.Win32;
        foreach(uint id in ids) {
            foreach(string body in new string[]{"<SecondaryObjectFilter/>","<SourceObjectFilter/>",
                "<SourceObjectFilter id='source' Rule='ANY' Relationship='ALLIES -NEUTRAL ENEMIES' Alignment='GOOD' Include='VEHICLE STRUCTURE' Exclude='INFANTRY' StatusBitFlags='STEALTHED' StatusBitFlagsExclude='UNDER_CONSTRUCTION'><IncludeThing>Tank</IncludeThing><ExcludeThing>Tree</ExcludeThing></SourceObjectFilter><SecondaryObjectFilter id='secondary' Rule='ALL' Relationship='SAME_PLAYER'><ExcludeThing/><IncludeThing>Alpha</IncludeThing><IncludeThing>Beta</IncludeThing></SecondaryObjectFilter>"}) {
                Test(original,"filter-branch:"+id+":"+body,FX("","<NuggetList>"+Nugget(id,"",body)+"</NuggetList>"),platform);
            }
        }
        foreach(uint id in new uint[]{0u,1u,UInt32.MaxValue,4293352610u}) {
            AssetBuffer b=Test(original,"unknown-base:"+id,FX("","<NuggetList>"+Nugget(id,"RequiredSecondaryModelConditions='MOVING' RequiredSourceModelConditions='ATTACKING' StopIfPlayed='true' Weather='RAINY' OnlyIfOnLand='true' PlayIfSourceIsStealthed='true'","<SourceObjectFilter/><SourceMustNotHaveBeenDisabledThisFrameByType>EMP</SourceMustNotHaveBeenDisabledThisFrameByType>")+"</NuggetList>"),platform);
            if(Word(b.InstanceData,(int)Word(b.InstanceData,28,big),big)!=0) throw new Exception("Unknown base TypeId must be zero");
        }
        Test(original,"missing-typeid",FX("","<NuggetList><Nugget/></NuggetList>"),platform);
        foreach(string attr in new string[]{"TypeId='not-a-number'","TypeId=''","TypeId='0xFFFFFFFF'","TypeId='-1'"})
            Test(original,"unknown-typeid:"+attr,FX("","<NuggetList><Nugget "+attr+"/></NuggetList>"),platform);
        foreach(string name in new string[]{"SecondaryObjectFilter","SourceObjectFilter"}) {
            foreach(string attr in new string[]{"Rule","Alignment","Relationship","Include","Exclude","StatusBitFlags","StatusBitFlagsExclude","id"})
                foreach(string value in new string[]{"","unknown","ALL","-ALL","ALL -VEHICLE","VEHICLE -VEHICLE -VEHICLE","ALLIES -ALLIES","STEALTHED&#9;UNDER_CONSTRUCTION"," +","value with spaces","MiXeD"})
                    Test(original,"filter-boundary:"+name+":"+attr+":"+value,FX("","<NuggetList>"+Nugget(503892219u,"","<"+name+" "+attr+"='"+value+"'/>")+"</NuggetList>"),platform);
            foreach(int count in new int[]{0,1,2,27,28,29,64,257}) {
                string children=""; for(int i=0;i<count;++i) children+="<IncludeThing>GameObject\\\\Tank"+i+"</IncludeThing><ExcludeThing>"+(i%2==0?"":"TREE")+"</ExcludeThing>";
                Test(original,"filter-things:"+name+":"+count,FX("","<NuggetList>"+Nugget(503892219u,"","<"+name+">"+children+"</"+name+">")+"</NuggetList>"),platform);
            }
            foreach(string body in new string[]{"<"+name+" Unknown='x'/>","<"+name+" xmlns='other'/>","<"+name+"/><"+name+"/>","<"+name+"><Unknown/></"+name+">","<"+name+">text</"+name+">","<"+name+"><IncludeThing Bad='x'>Tank</IncludeThing></"+name+">","<"+name+"><ExcludeThing><Nested/></ExcludeThing></"+name+">","<"+name+" xmlns:x='other' x:Include='VEHICLE'/>","<"+name+"><IncludeThing xmlns='other'/></"+name+">"})
                Reject(FX("","<NuggetList>"+Nugget(503892219u,"",body)+"</NuggetList>"),false);
        }
        string[] kindNames={"ACTIVATE_AFTER_UNPACK","AIRCRAFT","AIRCRAFT_PATH_AROUND","AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED","AIRFIELD","AI_CAN_UNPACK_NEAR_CONYARD","AI_NEEDS_PLAYER_POWER_UPDATE","ALLY_TARGETABLE","ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES","ALWAYS_SELECTABLE","ALWAYS_SHOW_HOUSE_COLOR","ALWAYS_VISIBLE","ALWAYS_VISIBLE_IN_RADAR","AMPHIBIOUS","ANTI_GARRISON","ARMY_SUMMARY","ASSAULT_AIRCRAFT","ATTACH_ATTACK","ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED","AUTO_ACQUIRABLE_BY_AI","AUTO_RALLYPOINT","BALLISTIC_MISSILE","BANNER","BASE_FOUNDATION","BASE_SITE","BEACON","BEAM_TARGET","BEAM_TARGET_REFLECTOR","BLOCKING_GATE","BOMBER_AIRCRAFT","BRIDGE","BRIDGE_ENDCAP","BRIDGE_GATEHOUSE","BRIDGE_SEGMENT","BROADCASTS_INVISIBILITY","BUFF","BUILD_FOR_FREE","CANNOT_BE_DETECTED","CANNOT_BUILD_NEAR_SUPPLIES","CANNOT_HIJACK","CANNOT_RETALIATE","CANNOT_STOP_ON_WATER","CANT_TOGGLE_POWER","CAN_ATTACK","CAN_ATTACK_STEALTHED","CAN_ATTACK_WALLS","CAN_BE_CAPTURED","CAN_BE_FAVORITE_UNIT","CAN_BE_REPULSED","CAN_BUILD_ON_DEEP_WATER","CAN_BUILD_ON_WATER","CAN_CAPTURE","CAN_CAST_REFLECTIONS","CAN_CLIMB_WALLS","CAN_HEAL_ALLIES","CAN_NOT_BUILD_ON_LAND","CAN_PLACE_CHARGE","CAN_PLACE_MANIPULATOR","CAN_RAPPEL","CAN_SEE_THROUGH_STRUCTURE","CAN_SHOOT_OVER_WALLS","CAN_SKIP_SHADOW","CAN_TOPPLE","CAPTURABLE","CAPTUREFLAG","CARPET_BOMBER","CASTLE_CENTER","CASTLE_KEEP","CAVALRY","CHUNK_VENDOR","CIVILIAN_BUILDING","CIVILIAN_UNIT","CLASSIFY_ENTIRE_FOOTPRINT","CLEARED_BY_BUILD","CLICK_THROUGH","COMBO_HORDE","COMMANDCENTER","COMMANDO","CONSTRUCTION_YARD","COVER","CRANE","CRATE","CREEP","CRUSHABLE_BUILDING","CRUSHABLE_OBSTACLE","CRUSH_CRUSHABLE_BUILDINGS","CYCLE_SELECTION","DEBRIS","DEFENSIVE_WALL","DEFLECT_BY_SPECIAL_POWER","DEPLOYED_MINE","DEPRECATED","DISGUISER","DONT_AUTO_CRUSH_INFANTRY","DONT_DESTROY_IF_ON_BRIDGE","DONT_HIDE_IF_FOGGED","DONT_SUBMERGE_BY_WAVES","DONT_UNATTACH_WHEN_HEALING","DONT_USE_CANCEL_BUILD_BUTTON","DOZER","DOZER_FACTORY","DO_NOT_CLASSIFY","DO_NOT_PICK_ME_WHEN_BUILDING","DRAWABLE_ONLY","DRONE","ECONOMY_STRUCTURE","EGG","ENCLOSURE","ENGINEER","ENVIRONMENT","EXPANSION_PAD","EXPANSION_UNIT","FIGHTER_AIRCRAFT","FORCEATTACKABLE","FS_AIR_FIELD","FS_BARRACKS","FS_BASE_DEFENSE","FS_CASH_PRODUCER","FS_FACTORY","FS_MONEY_STORAGE","FS_POWER","FS_RADAR","FS_TECHNOLOGY","FS_TECH_CENTER","FS_WAR_FACTORY","GARRISON","GARRISONABLE_UNTIL_DESTROYED","GRABBABLE","GRAB_AND_DROP","GRAB_AND_KILL","GRANTS_VETERANCY","HARVESTABLE","HARVESTER","HAS_HEALTH_BAR","HEAL_PAD","HEAVY_MELEE_HITTER","HIDE_IF_FOGGED","HORDE","HUGE_VEHICLE","HULK","HUSK","IGNORED_IN_FINDPOSITIONAROUND","IGNORED_IN_GUI","IGNORES_SELECT_ALL","IGNORE_CAN_NOT_BUILD","IGNORE_FORCE_MOVE","IGNORE_FOR_EVA_SPEECH_POSITION","IGNORE_FOR_VICTORY","IGNORE_IN_AI_HUNT_TACTIC","IMMOBILE","IMMUNE_TO_CAPTURE","IMMUNE_TO_CYCLONIC_SHOCKWAVE","INERT","INERT_SHROUD_REVEALER","INFANTRY","INFILTRATOR","KEEP_CLASSIFIED_WHEN_DEAD","LARGE_RECTANGLE_PATHFIND","LEAVE_PARKING_ON_BUILD_COMPLETE","LIMITED_PRODUCTION_AIRCRAFT","LINE_OF_SIGHT_IGNORES_BUILDINGS","LINKED_TO_FLAG","LOW_OVERLAPPABLE","MACHINE","MCV","MELEE_HORDE","MINE","MINE_TRIGGER","MONSTER","MOVE_FOR_NOONE","MOVE_ONLY","MP_COUNT_FOR_VICTORY","NEED_BASE_FOUNDATION","NEUTRAL_TECH","NEVER_CULL_FOR_MP","NONOCCLUDING","NOTIFY_OF_PREATTACK","NOT_AUTOACQUIRABLE","NOT_SELLABLE","NO_BASE_CAPTURE","NO_COLLIDE","NO_FORMATION_MOVEMENT","NO_FREEWILL_ENTER","NO_GARRISON","NO_HEAL_ICON","NO_SHADOW","OBSTACLE","OCL_BIT","OPTIMIZED_PROP","OPTIMIZED_SOUND","ORE_NODE","ORIENTS_TO_CAMERA","OUTPOST","PARACHUTABLE","PARACHUTE","PASS_EXPERIENCE_TO_CONTAINED","PASS_EXPERIENCE_TO_CONTAINER","PASS_EXPERIENCE_TO_PRODUCER","PASS_EXPERIENCE_TO_SLAVER","PATH_THROUGH_EACH_OTHER","PATH_THROUGH_INFANTRY","PORT","POWERED","POWERED_POWERS_ONLY","PRELOAD","PRODUCED_AT_HELIPAD","PROJECTILE","PROJECT_BUILDABILITY_FOR_ALLIES","RADAR_SCRAMBLER","REACT_WHEN_SELECTED","REBUILD_HOLE","REFINERY","REPAIR_PAD","RESIST_EMP","REVEAL_TO_ALL","ROCK","ROCK_VENDOR","SALVAGER","SCALEABLE_WALL","SCARY","SCORE","SCORE_CREATE","SCORE_DESTROY","SELECTABLE","SHADOW_ULTRA_HIGH_ONLY","SHIP","SHOW_BEHIND_OCCLUDERS","SHRUBBERY","SIEGE_TOWER","SIEGE_WEAPON","SIMPLE_OBJECT_PROP","SKIP_DEFECT_IF_UNCOMBINED","SKIP_IDLE_WHEN_CAPTURED","SKIRMISH_AI_ATTEMPT_TO_DODGE","SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME","SKIRMISH_AI_DONT_CONSIDER_THREAT","SKIRMISH_AI_DONT_GARRISON","SKIRMISH_AI_DONT_MULTI_TARGET","SKIRMISH_AI_DONT_REPAIR","SKYBOX","SLAVE_OWNER","SMALL_MISSILE","SNIPER","SPELL_BOOK","STEALTH_GARRISON","STICK_TO_TERRAIN_SLOPE","STRUCTURE","SUBMARINE","SUMMONED","SUPER_WEAPON","SUPPLY_GATHERING_CENTER","SUPPLY_SOURCE","SUPPLY_SOURCE_ON_PREVIEW","SURPRISE_ATTACKER","T1_UNIT","T2_UNIT","T3_UNIT","TACTICAL_MARKER","TARGETABLE","TERRAIN_LIKE_GROUND_OBJECT","THREAT_FINDER","THROWN_OBJECT","TIBERIUM","TIBERIUM_BASED","TIBERIUM_FIELD","TIME_BOMB","TRANSFORMER","TRANSPORT","UNATTACKABLE","UNIQUE_UNIT","UNPACKS_INTO_BUILDING","VALID_TARGET_FOR_C4","VEHICLE","VITAL_FOR_BASE_SURVIVAL","WALK_ON_TOP_OF_WALL","WALL_GATE","WALL_HUB","WALL_PIECE","WALL_SEGMENT","WALL_UPGRADE","WB_DISPLAY_SCRIPT_NAME","WEAPON_SALVAGER","WORKING_PASSENGER","WORLDBUILDER_SNAP_TO_GRID"};
        int[] kindValues={149,14,66,251,36,143,137,3,187,59,171,54,274,211,242,126,239,275,253,230,69,75,157,101,118,247,111,112,135,240,23,256,257,219,144,139,154,229,85,270,164,195,24,5,227,145,104,258,46,265,264,70,7,174,261,263,57,83,44,74,208,190,188,50,194,282,19,29,11,130,201,127,58,52,76,107,18,47,207,217,136,49,165,271,231,272,189,280,61,99,163,122,88,91,281,203,84,276,200,16,184,147,168,37,73,186,245,268,243,98,210,244,241,68,223,221,64,133,63,213,62,222,65,224,220,116,79,206,140,96,273,205,17,161,33,202,170,106,15,82,248,105,48,90,234,249,155,146,162,4,81,228,89,119,10,218,129,176,267,266,250,51,67,13,260,117,56,138,12,172,132,38,102,232,199,108,115,128,151,125,31,124,109,28,43,175,0,120,97,182,159,198,246,45,78,183,196,209,160,114,123,178,71,166,27,72,26,262,269,103,39,131,32,197,87,94,134,20,191,142,40,41,42,1,30,181,212,8,92,25,180,254,255,238,80,235,236,150,237,192,259,53,252,121,34,6,9,177,169,233,35,86,77,216,277,278,279,113,2,283,185,95,226,225,110,93,141,22,55,215,158,214,204,167,60,193,153,152,179,148,173,21,100,156};
        string[] statusNames={"ABSORBED_DAMAGE","ADVANCED_MISSILE_PACKS","AFLAME","AIRBORNE_TARGET","AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE","AIR_LOCOMOTOR_ACTIVE","ANTI_GARRISON","ATTACHED","ATTACKING_GARRISONED_STRUCTURE","BOOBY_TRAPPED","BOOBY_TRAP_EXPLODE","BOOKENDING","BRIDGE_IMPASSABLE","BROADCASTING_INVISIBILITY","BUILD_BEING_CANCELED","BURNED","CANNOT_BE_SOLD","CANNOT_LAND_AT_AIRFIELD","CAN_ATTACK","CAN_ATTACK_WHILE_STEALTHED","CAN_CONTEST_GARRISON","CAN_ENTER_ANYTHING","CAN_NOT_WALK_ON","CAN_STEALTH","CAN_STEALTH_FROM_PRODUCER","CARRYING_FLAG","CHANTING","CHARGING_BASE_DEFENSES","CHARGING_WEAPON","CLEARED_FOR_LANDING","CLONED","COMBINED_ATTACHED","COMBINED_CHILD","COMBINED_PARENT","COMMAND_BUTTON_TOGGLED","CONTAINER_OCCUPIED","CONTESTED","CONTESTING_BUILDING","DAMAGED","DEATH_1","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEFLECT_INCOMING_FIRE","DELAYED_ENTER_STRUCTURE","DEPART_FROM_BOARDING","DEPLOYED","DEPLOYING","DESTROYED","DETECTED","DOCKING","DOES_CONTAIN_TIBERIUM","DONT_CLEAR_FOR_BUILD","DO_NOT_AVOID_TALLBUILDING","DO_NOT_PICK_ME","DO_NOT_SCORE","ENABLE_TRACER_DRAW","ENCLOSED","END_FIRE_STATE","ENRAGED","ENTERING_GOAL","EXCEEDED_PATH_NEIGHBOR_COUNT","EXECUTING_STORED_COMMAND","EXITING_COMBINED","EXPLOSIVES_ATTACHED","EXPLOSIVES_DETONATING","FLEE_OFF_MAP","FOLLOWING_THROUGH","FORCE_ATTACKING","FORCE_ATTACK_MOVING","GARRISONED","GARRISON_CAN_STEALTH","GENERIC_TOGGLE_STATE","GRAPPLE_UPDATE_ACTIVE","GRAPPLE_UPDATE_BEING_MOVED","GROUND_LOCOMOTOR_ACTIVE","HARVESTING","HAS_SECONDARY_DAMAGE","HAS_TIBERIUM_GROWTH_MOD","HAS_TIBERIUM_UPGRADE","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIJACKED","HIT_SHIELD_SPHERE","HORDE_MEMBER","IGNORE_AI_COMMAND","IGNORING_POWER_DOWN","IGNORING_STEALTH","IMMOBILE","IMMOBILE_ALLOW_ROTATE","IMMUNE_TO_BARK","INAUDIBLE","INHERITED_FROM_ALLY_TEAM","INSIDE_GARRISON","INVISIBLE_DETECTED","INVISIBLE_DETECTED_BY_FRIEND","IN_COVER","IN_FORMATION_TEMPLATE","IN_NANOHIVE","IN_SHIELD_SPHERE","IN_SPIDER_HOLE","IN_STASIS","IRRADIATED","IS_AIMING_WEAPON","IS_ATTACKING","IS_BEING_DRAGGED","IS_BEING_HARVESTED","IS_BRAKING","IS_ENGAGED","IS_FIRING_WEAPON","IS_HIDEOUT","IS_LEAVING_FACTORY","IS_MELEE_ATTACKING","IS_MOVING_TO_RALLY_POINT","IS_RELOADING_WEAPON","LANDING_IN_PROGRESS","LEASHED_RETURNING","LEECHED_TARGET_ACTIVE","LIFTED_INTO_AIR","LINE_SEGMENT_DAMAGE_ACTIVE","LOADED_FROM_MAP","MAGNETIZED","MARCH_OF_DEATH","MATCH_TARGETS_SPEED","MOVING_TO_DISMOUNT","NEXT_MOVE_IS_FORCE_ATTACK_MOVE","NEXT_MOVE_IS_REVERSE","NON_AUTOACQUIRABLE","NOT_IN_WORLD","NO_ATTACK","NO_ATTACK_FROM_AI","NO_AUTO_ACQUIRE","NO_BRIBE","NO_COLLISIONS","NO_HERO_PROPERTIES","NO_REFUND","NO_SHADOW","NO_SPECIAL_ABILITY","NO_SQUISHCOLLIDE_DELAY","OCLMONITOR_COMPLETED_TASK","OCLMONITOR_MONITOR_RELEASED","OUT_OF_PHASE","OVERCHARGING_WEAPON","OVER_WATER","PARACHUTING","PARKED_AT_AIRFIELD","PAUSING","PENDING_CONSTRUCTION","PHANTOM_STRUCTURE","PLACED_BY_PLAYER","PLAYER_POWER_1","PLAYER_POWER_2","PLAYER_POWER_3","PLAYER_POWER_4","PLAYER_POWER_5","POINT_DEFENSE_DRONE_ATTACHED","PORTER_TAGGED","POWERED_DOWN_EMP","PREEMPT_PRE_ATTACK","RAMPAGING","REALLYDAMAGED","RECONSTRUCTING","REPAIR_ALLIES_WHEN_IDLE","REPULSOR","RESET_GOAL_POSITION","RIDER_IS_PILOT","RIFT_OCCUPIED","RIOT_SHIELDED","RUBBLE","RUNNING_DOWN_FROM_BEHIND","SCARED_CIVILIAN_CAR","SCRAMBLED","SHIELDBODY_ENABLED","SHROUD_REVEAL_TO_ALL","SHRUNK","SINKING","SKIRMISH_AI_DO_NOT_ATTACK","SKIRMISH_AI_DO_NOT_TAKE","SOLD","SPECIALABILITY_ACTIVE","SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING","SPECIAL_ARMOR_ACTIVE","STAND_GROUND","STEALTHED","STEAL_NEXT_UNIT_TRAPPED","SUBMERGED","SURFACED","SURFACED_IMMOBILE","SWITCHED_WEAPONS","TAGGED","TARGETED_FOR_REPAIR","TEMPORARILY_DEFECTED","TIBERIUM_VIBRATING","TOPPLING","TRANSFORMATION_TOGGLE_STATE","TRANSPORT_MOVING_TO_CONTAIN","UNATTACKABLE","UNCONTROLLABLY_SCARED","UNDERGOING_REPAIR","UNDER_CONSTRUCTION","UNDER_FROZEN","UNDER_IRON_CURTAIN","UNDER_RUSH_ATTACK","UNIT_WANTS_TO_REGARRISON","UNPACKING","UNSELECTABLE","UPDATING_AI","USER_PARALYZED","USER_POWERED_DOWN","USING_ABILITY","VEHICLE_ATTACHED","WAITING_FOR_BOARDING","WALL_SEGMENT","WATER_LOCOMOTOR_ACTIVE","WEAPON_UPGRADED_01","WEAPON_UPGRADED_02","WEAPON_UPGRADED_03","WONT_RIDE_WITH_YOU"};
        int[] statusValues={198,182,11,7,151,161,222,102,135,109,146,86,140,214,89,12,13,202,1,156,88,96,80,20,154,148,57,98,177,171,76,133,132,131,104,164,38,39,43,33,34,35,36,37,166,137,167,62,67,0,19,138,119,114,197,82,79,179,64,85,58,97,201,111,136,209,210,54,31,142,143,108,155,87,189,219,160,139,169,116,117,123,127,124,125,126,18,10,175,40,77,200,29,51,52,204,56,83,61,100,99,113,92,212,174,221,48,46,27,24,170,120,16,147,14,110,93,30,149,15,207,32,183,211,181,134,216,81,115,94,152,50,176,55,5,28,25,199,4,95,121,47,6,70,105,106,49,178,163,8,173,53,90,91,165,184,185,186,187,188,218,69,141,180,60,44,23,196,9,157,42,158,217,45,78,144,215,122,190,101,59,193,194,21,153,73,150,71,17,118,192,191,213,84,66,220,65,74,68,195,41,63,72,22,2,206,208,205,112,162,3,75,145,107,26,172,168,203,159,128,129,130,103};
        for(int mode=0;mode<2;++mode) {
            string[] names=mode==0?kindNames:statusNames; int[] values=mode==0?kindValues:statusValues;
            for(int i=0;i<names.Length;++i) {
                string attrs=mode==0 ? "Include='"+names[i]+"' Exclude='"+names[i]+"'" : "StatusBitFlags='"+names[i]+"' StatusBitFlagsExclude='"+names[i]+"'";
                AssetBuffer b=Test(original,"filter-bits:"+mode+":"+names[i],FX("","<NuggetList>"+Nugget(503892219u,"","<SecondaryObjectFilter "+attrs+"/><SourceObjectFilter "+attrs+"/>")+"</NuggetList>"),platform);
                int nugget=(int)Word(b.InstanceData,28,big);
                for(int f=0;f<2;++f) {
                    int filter=(int)Word(b.InstanceData,nugget+24+4*f,big);
                    if(filter<32+44 || filter>b.InstanceData.Length-112) throw new Exception("Filter pointer layout");
                    for(int field=0;field<2;++field) {
                        int at=mode==0?filter+16+36*field:(int)Word(b.InstanceData,filter+88+4*field,big);
                        for(int word=0;word<(mode==0?9:7);++word) {
                            uint expected=names[i]=="ALL"?UInt32.MaxValue:(values[i]>=0&&values[i]/32==word?1u<<(values[i]&31):0u);
                            if(Word(b.InstanceData,at+4*word,big)!=expected) throw new Exception("Independent filter bit layout: "+mode+":"+names[i]);
                        }
                    }
                }
            }
        }
        foreach(string rule in new string[]{"ALL","ANY","NONE","UNPARSED","unknown",""})
            foreach(string align in new string[]{"EVIL","GOOD","NONE","unknown",""})
                Test(original,"filter-enums:"+rule+":"+align,FX("","<NuggetList>"+Nugget(503892219u,"","<SourceObjectFilter Rule='"+rule+"' Alignment='"+align+"'/>")+"</NuggetList>"),platform);
    }

    static int Main(string[] args) {
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assembly assembly = Assembly.LoadFrom(args[0]);
            IAssetBuilderPlugin original = (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            foreach (int platformId in new int[] {0,1,2,99}) {
                TargetPlatform platform = (TargetPlatform)platformId;
                int startFixtures=fixtures, startComparisons=comparisons, startRejections=rejections, startLayouts=layouts;
                bool big = platformId != 0;
                Layout(Test(original, "default", FX("", ""), platform), big, 0, 0, 0, 0, 0);
                foreach(string typeId in new string[]{"2254974584","0x86682e78"}) {
                    Layout(Test(original,"BAB-root-typeid-default:"+typeId,FX("TypeId='"+typeId+"'","<NuggetList TypeId='123'/>"),platform),big,0,0,0,0,0);
                    Test(original,"BAB-root-typeid-nugget:"+typeId,FX("TypeId='"+typeId+"'","<NuggetList TypeId='123'>"+Nugget(503892219u,"","<SecondaryObjectFilter TypeId='456'><IncludeThing TypeId='789'>Tank</IncludeThing><ExcludeThing TypeId='789'>Tree</ExcludeThing></SecondaryObjectFilter><SourceObjectFilter TypeId='456'/><SourceMustNotHaveBeenDisabledThisFrameByType TypeId='111'>EMP</SourceMustNotHaveBeenDisabledThisFrameByType>")+"</NuggetList>"),platform);
                }
                Test(original,"BAB-derived-child-typeid",FX("TypeId='2254974584'","<NuggetList TypeId='123'>"+Nugget(751572338u,"","<Offset TypeId='222' x='1.25' y='2.5' z='3.75'/><TargetOffset TypeId='222' x='4.5' y='5.25' z='6.75'/><Radius TypeId='333' Low='1.25' High='2.5'/>")+"</NuggetList>"),platform);
                foreach(string typeId in new string[]{"0","1","686292351","0x86682e79","","not-a-number"})
                    Reject(FX("TypeId='"+typeId+"'","<NuggetList/>"),false);
                Reject(FX("xmlns:x='other' x:TypeId='2254974584'","<NuggetList/>"),false);
                Layout(Test(original, "distinct", FX("CullTracking='1500ms' CullTrackingMin='-17' CullTrackingMax='0x23' PlayEvenIfShrouded='true' Tailorable='1'", "<NuggetList/>"), platform), big,
                    (float)((double)1500.0f * (double)0.001f), -17, 35, 1, 1);
                Layout(Test(original, "seconds", FX("CullTracking='2.25s' CullTrackingMin='010' CullTrackingMax='-2147483648'", ""), platform), big, 2.25f, 8, Int32.MinValue, 0, 0);
                string[] values = {"", "0", "-0", "1", "-17.25", "33.3333333333", "1e-30", "1e30", "2d1", "2D-1", "garbage", "25,5", " 25.75junk", "1.5s", "1500ms", "-25ms", "ms2", "1s2ms", "0x10", "010", "2147483648", "-2147483649"};
                foreach (string value in values) {
                    Test(original, "time:" + value, FX("CullTracking='" + value + "'", ""), platform);
                    Test(original, "min:" + value, FX("CullTrackingMin='" + value + "'", ""), platform);
                    Test(original, "max:" + value, FX("CullTrackingMax='" + value + "'", ""), platform);
                }
                foreach (string value in new string[] {"", "1", "0", "true", "True", "TRUE", "t", "T", "false", "yes", " true", "10", "trailing"}) {
                    Test(original, "play:" + value, FX("PlayEvenIfShrouded='" + value + "'", ""), platform);
                    Test(original, "tail:" + value, FX("Tailorable='" + value + "'", ""), platform);
                }
                Test(original, "prefix", "<e:FXList xmlns:e='" + Ns + "' id='Oracle' CullTracking='3s'><e:NuggetList/></e:FXList>", platform);
                Test(original, "comments", FX("", "<!-- comment -->\n<NuggetList> </NuggetList>"), platform);
                Reject(FX("Unknown='1'", ""), false);
                Reject(FX("tailorable='true'", ""), false);
                Reject(FX("xmlns:x='other' x:CullTracking='1'", ""), false);
                Reject(FX("", "<Unknown/>"), false);
                Reject(FX("", "<NuggetList><Sound Type='SoundFXNugget'/></NuggetList>"), false);
                Reject(FX("", "<NuggetList/><NuggetList/>"), false);
                Reject(FX("", "<NuggetList Unknown='1'/>"), false);
                Reject(FX("", "<NuggetList xmlns=''/>"), false);
                Reject(FX("", "<NuggetList>text</NuggetList>"), false);
                Reject(FX("", "text"), false);
                Reject("<FXList xmlns='other'/>", false);
                Reject("<Wrong xmlns='" + Ns + "'/>", false);

                uint[] nuggetIds={503892219u,638391350u,4011332978u,2424288100u,4234415617u,2263385183u,904475169u,2386148390u,4293352609u,2106748504u,3768725989u,138194854u,3121289073u,866573550u,751572338u};
                string[] nuggetAttrs={
                    "Value='Audio\\0'",
                    "Event='Force\\17'",
                    "Type='STRONG'",
                    "EvaEventOwner='Eva\\0' EvaEventAlly='Eva\\1' EvaEventEnemy='Eva\\2'",
                    "FX='Fx\\-1' Bone='Bone_A'",
                    "Model='Model\\5' RandomlyRotate='true' ExpireTimer='1500ms'",
                    "LaserTemplate='MiXeDObject' LaserBackwards='true'",
                    "Thing='RayObject'",
                    "Radius='12.5' RadiusAsPercentOfObjectSize='33.3' IncreaseTime='0x10' DecreaseTime='-1'",
                    "PreColorTime='250ms' PostColorTime='1.5s' SustainedColorTime='2.25s' Frequency='3.5' Amplitude='4.75'",
                    "Radius='32.5' Duration='200ms' Amplitude='90d'",
                    "Type='SCORCH_13' Radius='42.5'",
                    "Bone='Bone_P' RequireFrequentUpdate='true' TriggerType='TREBUCHET_ROCK' HoldBetweenStateID='-17' ActionType='SPAWN' Particle='Fx\\7'",
                    "Decal='Decal_A' Shader='ADDITIVE' Size='24.5' OrientToObject='false' OpacityStart='0.2' OpacityPeak='0.8' OpacityEnd='0.1' OpacityFadeTimeOne='250ms' OpacityFadeTimeTwo='1.5s' OpacityPeakTime='2s' StartingDelay='100ms' Lifetime='4s' AllowOnObjects='false'",
                    "Particle='Fx\\9' Count='-3' OrientToObject='true' Ricochet='1' CreateAtGroundHeight='T' CreateOverrideBone='Create' UseTarget='true' TargetOverrideBone='Target' TargetCoeff='1.75' SystemLife='1500ms' SetTargetMatrix='true' OnlyIfOnWater='true'"
                };
                string[] nuggetChildren={"","","","","","","<Offset x='1.25' y='-2.5' z='3.75'/>","<PrimaryOffset x='1' y='2' z='3'/><SecondaryOffset x='4' y='5' z='6'/>","<Color R='0.1' G='0.2' B='0.3'/>","<Color R='0.4' G='0.5' B='0.6'/>","","<RandomRange Low='SCORCH_2' High='SCORCH_15'/>","<Offset x='1.25' y='2.5' z='3.75'/>","<Color R='0x12' G='0x34' B='0x56' A='0x78'/><Offset x='1.25' y='-2.5'/>","<Rotate X='90d' Y='0.5r' Z='-45d'/><Offset x='1' y='2' z='3'/><TargetOffset x='4' y='5' z='6'/><Radius Type='GAUSSIAN' Low='1.25' High='2.5'/><Height Type='TRIANGULAR' Low='3.5' High='4.75'/><InitialDelay Type='CONSTANT' Low='5' High='6'/>"};
                for(int i=0;i<nuggetIds.Length;++i) {
                    AssetBuffer defaults=Test(original,"nugget-default-"+i,FX("","<NuggetList>"+Nugget(nuggetIds[i],"","")+"</NuggetList>"),platform);
                    if(Word(defaults.InstanceData,(int)Word(defaults.InstanceData,28,big),big)!=nuggetIds[i]) throw new Exception("Known nugget TypeId layout");
                    if(i==14) {
                        uint item=Word(defaults.InstanceData,28,big);
                        if(Word(defaults.InstanceData,(int)item+128,big)!=0 || Word(defaults.InstanceData,(int)item+132,big)!=0xbf800000u || Word(defaults.InstanceData,(int)item+136,big)!=0xbf800000u)
                            throw new Exception("Particle InitialDelay independent default layout");
                        ++layouts;
                    }
                    AssetBuffer distinct=Test(original,"nugget-fields-"+i,FX("","<NuggetList>"+Nugget(nuggetIds[i],nuggetAttrs[i]+" StopIfPlayed='true' Weather='RAINY' OnlyIfOnLand='1' PlayIfSourceIsStealthed='T'",nuggetChildren[i])+"</NuggetList>"),platform);
                    uint payload=Word(distinct.InstanceData,28,big);
                    if(Word(distinct.InstanceData,(int)payload+20,big)!=2 || distinct.InstanceData[(int)payload+40]!=1 || distinct.InstanceData[(int)payload+41]!=1 || distinct.InstanceData[(int)payload+42]!=1) throw new Exception("Nugget base scalar offsets");
                    if(i==0 && (Word(distinct.InstanceData,(int)payload+44,big)!=1 || distinct.ImportsData.Length!=8 || Word(distinct.ImportsData,0,big)!=payload+44)) throw new Exception("Sound resource/import layout");
                    if(i==12 && (Word(distinct.InstanceData,(int)payload+52,big)!=2 || Word(distinct.InstanceData,(int)payload+56,big)!=unchecked((uint)-17) || Word(distinct.InstanceData,(int)payload+60,big)!=3)) throw new Exception("Particle bone enum/signed layout");
                    if(i==13 && (Word(distinct.InstanceData,(int)payload+92,big)!=0x78123456u || distinct.InstanceData[(int)payload+104]!=0 || distinct.InstanceData[(int)payload+105]!=0)) throw new Exception("Dynamic decal packed color/bool layout");
                    if(i==14 && (Word(distinct.InstanceData,(int)payload+48,big)!=unchecked((uint)-3) || Word(distinct.InstanceData,(int)payload+44,big)!=10 || Word(distinct.InstanceData,(int)payload+100,big)==0)) throw new Exception("Particle system signed/reference/nullable vector layout");
                    ++layouts;

                    Test(original,"nugget-disabled-"+i,FX("","<NuggetList>"+Nugget(nuggetIds[i],"Weather='unknown'","<SourceMustNotHaveBeenDisabledThisFrameByType>EMP</SourceMustNotHaveBeenDisabledThisFrameByType><SourceMustNotHaveBeenDisabledThisFrameByType>unknown</SourceMustNotHaveBeenDisabledThisFrameByType>")+"</NuggetList>"),platform);
                }
                foreach(string value in values) {
                    Test(original,"bone:"+value,FX("","<NuggetList>"+Nugget(4234415617u,"Bone='"+value+"'","")+"</NuggetList>"),platform);
                    Test(original,"camera:"+value,FX("","<NuggetList>"+Nugget(3768725989u,"Radius='"+value+"' Duration='"+value+"' Amplitude='"+value+"'","")+"</NuggetList>"),platform);
                    Test(original,"typed:"+value,FX("","<NuggetList>"+Nugget(904475169u,"LaserTemplate='"+value+"' LaserBackwards='"+value+"'","")+"</NuggetList>"),platform);
                }
                foreach(string value in new string[]{"","unknown"," CINE_EXTREME","CINE_EXTREME ","cine_extreme","SUBTLE","NORMAL","STRONG","SEVERE","CINE_EXTREME","CINE_INSANE"})
                    Test(original,"shake:"+value,FX("","<NuggetList>"+Nugget(4011332978u,"Type='"+value+"'","")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"CLOUDY","CLOUDYRAINY","INVALID","NORMAL","RAINY","SNOWY","SUNNY","rainy","","unknown"})
                    Test(original,"weather:"+value,FX("","<NuggetList>"+Nugget(503892219u,"Weather='"+value+"'","")+"</NuggetList>"),platform);
                foreach(int count in new int[]{2,27,28,29,64,257}) {
                    string children="";
                    for(int i=0;i<count;++i) children+=Nugget(nuggetIds[i%nuggetIds.Length],nuggetAttrs[i%nuggetIds.Length],nuggetChildren[i%nuggetIds.Length]);
                    Test(original,"nugget-list-"+count,FX("","<NuggetList>"+children+"</NuggetList>"),platform);
                }
                foreach(uint id in new uint[]{0u,1u,UInt32.MaxValue}) {
                    AssetBuffer unknown=Test(original,"unknown-default:"+id,FX("","<NuggetList>"+Nugget(id,"","")+"</NuggetList>"),platform);
                    if(unknown.InstanceData.Length!=76 || Word(unknown.InstanceData,32,big)!=0) throw new Exception("Unknown base allocation");
                }
                Reject(FX("","<NuggetList>"+Nugget(503892219u,"Unknown='x'","")+"</NuggetList>"),false);
                Reject(FX("","<NuggetList>"+Nugget(503892219u,"RequiredSourceModelCondition='MOVING'","")+"</NuggetList>"),false);
                FilterCases(original,platform,nuggetIds);

                string[] conditionAttrs={"RequiredSecondaryModelConditions","ExcludedSecondaryModelConditions","RequiredSourceModelConditions","ExcludedSourceModelConditions"};
                string[] modelNames={"ABOUT_TO_HIT","ACCELERATE","ACTIVELY_BEING_CONSTRUCTED","ACTIVELY_CONSTRUCTING","AFFECTED_BY_EMP","AFLAME","AIM_FAR","AIM_HIGH","AIM_LOW","AIM_NEAR","AIM_STRAIGHT","AIR_POWER_EFFECT","ALL","ALTERNATE_FORMATION","ARMORSET_ALTERNATE_FORMATION","ARMORSET_ELITE","ARMORSET_HERO","ARMORSET_MOUNTED","ARMORSET_PLAYER_UPGRADE","ARMORSET_PLAYER_UPGRADE_2","ARMORSET_PLAYER_UPGRADE_3","ARMORSET_UNBESIEGEABLE","ARMORSET_VETERAN","ARMORSET_WEAK_VERSUS_BASEDEFENSES","ATTACHED","ATTACKING","ATTACKING_POSITION","ATTACKING_STRUCTURE","ATTACK_MOVING","AWAITING_CONSTRUCTION","BACKCRUSHED","BACKING_UP","BASE_BUILD","BEING_MAULED","BETWEEN_FIRING_SHOTS_A","BETWEEN_FIRING_SHOTS_B","BETWEEN_FIRING_SHOTS_C","BETWEEN_FIRING_SHOTS_D","BETWEEN_FIRING_SHOTS_E","BUILD_PLACEMENT_CURSOR","BUILD_PLACEMENT_HINT","BUILD_VARIATION_ONE","BUILD_VARIATION_TWO","BURNED","BURNINGDEATH","BURNT_MODEL","BURNT_TEXTURE","CANCEL_CAPTURE","CAPTURED","CAPTURE_0","CAPTURE_100","CAPTURE_25","CAPTURE_50","CAPTURE_75","CAPTURING","CARRYING","CHARGING","CHRONORIFT","CLIMBING","COLLAPSING","COMBINED_CHILD","COMBINED_PARENT","COMING_OUT_OF_FACTORY","CONTINUOUS_FIRE_FAST","CONTINUOUS_FIRE_MEAN","CONTINUOUS_FIRE_SLOW","COVER","DAMAGED","DEATH_1","DEATH_10","DEATH_11","DEATH_12","DEATH_13","DEATH_14","DEATH_15","DEATH_2","DEATH_3","DEATH_4","DEATH_5","DEATH_6","DEATH_7","DEATH_8","DEATH_9","DEBUG","DECAY","DECELERATE","DEFLECT_SPECIAL_POWER","DEPLOYED","DESTROYED_BACK","DESTROYED_FRONT","DESTROYED_LEFT","DESTROYED_RIGHT","DESTROYED_WEAPON","DESTROYED_WHILST_BEING_CONSTRUCTED","DISGUISED","DIVING","DOCKING","DOCKING_ACTIVE","DOCKING_BEGINNING","DOCKING_ENDING","DOCKING_EXTRACT","DOCKING_FILL","DOCKING_MOVEBACK","DOCKING_PRE_DOCK","DOCKING_RETURN","DOOR_1_CLOSING","DOOR_1_OPENING","DOOR_1_WAITING_OPEN","DOOR_1_WAITING_TO_CLOSE","DOOR_2_CLOSING","DOOR_2_OPENING","DOOR_2_WAITING_OPEN","DOOR_2_WAITING_TO_CLOSE","DOOR_3_CLOSING","DOOR_3_OPENING","DOOR_3_WAITING_OPEN","DOOR_3_WAITING_TO_CLOSE","DOOR_4_CLOSING","DOOR_4_OPENING","DOOR_4_WAITING_OPEN","DOOR_4_WAITING_TO_CLOSE","DOOR_HELIPAD_CLOSING","DOOR_HELIPAD_OPENING","DOOR_HELIPAD_WAITING_OPEN","DOOR_HELIPAD_WAITING_TO_CLOSE","DRAFTED","DYING","DYING_WASATTACKING","DYING_WASMOVING","EATING","EMOTION_AFRAID","EMOTION_ALERT","EMOTION_AMUSED","EMOTION_BRACE_FOR_BEING_CRUSHED","EMOTION_CELEBRATING","EMOTION_CHEER_FOR_ABOUT_TO_CRUSH","EMOTION_COWER","EMOTION_DISSIDENT","EMOTION_DOOM","EMOTION_GUNG_HO","EMOTION_LOOK_TO_SKY","EMOTION_MORALE_HIGH","EMOTION_MORALE_LOW","EMOTION_PANIC","EMOTION_POINTING","EMOTION_TAUNTING","EMOTION_TERROR","EMOTION_UNCONTROLLABLY_AFRAID","ENGAGED","EXPLODED_BOUNCING","EXPLODED_FLAILING","FIRING_A","FIRING_B","FIRING_C","FIRING_D","FIRING_E","FIRING_OR_PREATTACK_A","FIRING_OR_PREATTACK_B","FIRING_OR_PREATTACK_C","FIRING_OR_PREATTACK_D","FIRING_OR_PREATTACK_E","FIRING_OR_RELOADING_A","FIRING_OR_RELOADING_B","FIRING_OR_RELOADING_C","FIRING_OR_RELOADING_D","FIRING_OR_RELOADING_E","FLOODED","FLYING","FORMATION_PREVIEW","FREEFALL","FRONTCRUSHED","FROZEN","GARRISONED","GUARDING","HARVEST_ACTION","HARVEST_PREPARATION","HEALTH_PERCENT_0","HEALTH_PERCENT_100","HEALTH_PERCENT_25","HEALTH_PERCENT_50","HEALTH_PERCENT_75","HIDDEN","HIGH_TECH_EFFECT","HIT_LEVEL_1","HIT_LEVEL_2","HIT_LEVEL_3","HIT_REACTION","HORDE_EMPTY","IMPENDING_DOOM","INFILTRATED_DISABLE","INFILTRATED_ENERGY","INFILTRATED_RADAR_FREEZE","INFILTRATED_RESET","INFILTRATED_STEAL_MONEY","INFILTRATED_VISION","INFILTRATED_VISION_UNITS","INSIDE_GARRISON","INVALID","INVISIBLE_CAMOUFLAGE","INVISIBLE_STEALTH","INVULNERABLE","IRONCURTAIN","JETAFTERBURNER","JETEXHAUST","JUST_BUILT","LANDING","LAUNCHED","LEASHED_RETURNING","LEVELED","LOADED","LOCOMOTOR_BURNINGDEATH","LOCOMOTOR_CHANGING_FIRINGARC","LOCOMOTOR_COMBO","LOCOMOTOR_COMBO2","LOCOMOTOR_COMBO3","LOCOMOTOR_CONTAINED","LOCOMOTOR_ENRAGED","LOCOMOTOR_FREEFALL","LOCOMOTOR_MOUNTED","LOCOMOTOR_NORMAL_UPGRADED","LOCOMOTOR_PANIC","LOCOMOTOR_SCARED","LOCOMOTOR_SUPERSONIC","LOCOMOTOR_TAXIING","LOCOMOTOR_WALL_SCALING","LOCOMOTOR_WANDER","MARCHING","MARKED_FOR_HUNT_TACTIC","MARKED_FOR_NO_SKIRMISH_RECRUIT","MONEY_STORED_AMOUNT_1","MONEY_STORED_AMOUNT_2","MONEY_STORED_AMOUNT_3","MONEY_STORED_AMOUNT_4","MOUNTED","MOVING","MOVING_OUT_OF_THE_WAY","NIGHT","OVER_WATER","PACKING","PACKING_TYPE_1","PACKING_TYPE_2","PACKING_TYPE_3","PACKING_TYPE_4","PACKING_TYPE_5","PACKING_TYPE_6","PANICKING","PARACHUTE_LAND","PARACHUTING","PARALYZED","PARKINGPLACE_1_DOOR_CLOSED","PARKINGPLACE_1_DOOR_CLOSING","PARKINGPLACE_1_DOOR_OPEN","PARKINGPLACE_1_DOOR_OPENING","PARKINGPLACE_2_DOOR_CLOSED","PARKINGPLACE_2_DOOR_CLOSING","PARKINGPLACE_2_DOOR_OPEN","PARKINGPLACE_2_DOOR_OPENING","PARKINGPLACE_3_DOOR_CLOSED","PARKINGPLACE_3_DOOR_CLOSING","PARKINGPLACE_3_DOOR_OPEN","PARKINGPLACE_3_DOOR_OPENING","PARKINGPLACE_4_DOOR_CLOSED","PARKINGPLACE_4_DOOR_CLOSING","PARKINGPLACE_4_DOOR_OPEN","PARKINGPLACE_4_DOOR_OPENING","PARKINGPLACE_HELIPAD_DOOR_CLOSED","PARKINGPLACE_HELIPAD_DOOR_CLOSING","PARKINGPLACE_HELIPAD_DOOR_OPEN","PARKINGPLACE_HELIPAD_DOOR_OPENING","PARTIALLY_CONSTRUCTED","PASSENGER","PASSENGER_VARIATION_1","PASSENGER_VARIATION_2","PASSENGER_VARIATION_3","PASSENGER_VARIATION_4","PASSENGER_VARIATION_5","PHANTOM_STRUCTURE","PLAYER_POWER_1","POST_COLLAPSE","POST_RUBBLE","POWER_PLANT_UPGRADED","POWER_PLANT_UPGRADING","PREATTACK_A","PREATTACK_B","PREATTACK_C","PREATTACK_D","PREATTACK_E","PREORDER","PREPARING","PRIMARY_FORMATION","PRONE","RADAR_EXTENDING","RADAR_UPGRADED","RAISING_FLAG","RAPPELLING","REACT_1","REACT_2","REACT_3","REACT_4","REACT_5","REACT_6","REALLYDAMAGED","RELOADING_A","RELOADING_B","RELOADING_C","RELOADING_D","RELOADING_E","REPAIRING_DISABLED","RESURRECTED","RIDER1","RIDER2","RIDER3","RIDER4","RIDER5","RIDER6","RIDER7","RIDER8","RIDERLESS","RUBBLE","RUNNING_OFF_MAP","SAIL_BLOWN_LEFT","SAIL_BLOWN_RIGHT","SAIL_FLAPPING","SCALING_WALL","SCALING_WALL_HORDE","SELECTED","SHRINK_EFFECT","SIEGE_CONTAIN","SMOLDERING","SNOW","SOLD","SPECIALPOWER1_READY","SPECIALPOWER2_READY","SPECIALPOWER3_READY","SPECIALPOWER4_READY","SPECIALPOWER5_READY","SPECIALPOWER6_READY","SPECIALPOWER7_READY","SPECIALPOWER8_READY","SPECIALPOWER9_READY","SPECIAL_CHEERING","SPECIAL_DAMAGED","SPECIAL_ENEMY_NEAR","SPECIAL_POWER_1","SPECIAL_POWER_2","SPECIAL_POWER_3","SPECIAL_WEAPON_FIVE","SPECIAL_WEAPON_FOUR","SPECIAL_WEAPON_ONE","SPECIAL_WEAPON_SIX","SPECIAL_WEAPON_THREE","SPECIAL_WEAPON_TWO","SPLATTED","START_CAPTURE","STRUCTURE_UNPACKING","STUNNED","STUNNED_FLAILING","STUNNED_STANDING_UP","SUCKED_UP_HIGH","SUPPRESSED","SWAPPING_TO_WEAPONSET_1","SWAPPING_TO_WEAPONSET_2","SWAPPING_TO_WEAPONSET_3","SWIMMING","SWOOPING","TAKING_OFF","THROWN_PROJECTILE","TIBERIUM_CRYSTAL_TYPE1","TIBERIUM_CRYSTAL_TYPE2","TIBERIUM_CRYSTAL_TYPE3","TIBERIUM_CRYSTAL_TYPE4","TIBERIUM_CRYSTAL_TYPE5","TIBERIUM_GROWING","TOPPLED","TRANSPORT_MOVING","TRANSPORT_STOPPED","TURN_LEFT","TURN_LEFT_HIGH_SPEED","TURN_RIGHT","TURN_RIGHT_HIGH_SPEED","TURRET_ANGLE_0","TURRET_ANGLE_180","TURRET_ANGLE_270","TURRET_ANGLE_90","TURRET_ROTATE","UNCONTROLLABLE","UNDERPOWERED","UNIT_ACTIVELY_BEING_CONSTRUCTED","UNPACKING","UPGRADED_ARMOR","USER_1","USER_10","USER_2","USER_3","USER_4","USER_5","USER_6","USER_7","USER_8","USER_9","USING_COMBO_LOCOMOTOR","USING_SPECIAL_ABILITY","USING_WEAPON_A","USING_WEAPON_B","USING_WEAPON_C","USING_WEAPON_D","USING_WEAPON_E","VEHICLE_CRUSH_CENTER","VEHICLE_CRUSH_LEFT","VEHICLE_CRUSH_RIGHT","WADING","WALKING","WALL_SEGMENT","WANDER","WEAPONLOCK_PRIMARY","WEAPONLOCK_QUATERNARY","WEAPONLOCK_QUINARY","WEAPONLOCK_SECONDARY","WEAPONLOCK_TERTIARY","WEAPONSET_CLOSE_RANGE","WEAPONSET_CONTAINED","WEAPONSET_CONTESTING_BUILDING","WEAPONSET_ELITE","WEAPONSET_GARRISONED","WEAPONSET_HERO","WEAPONSET_MOUNTED","WEAPONSET_PASSENGER_TYPE_ONE","WEAPONSET_PASSENGER_TYPE_TWO","WEAPONSET_PLAYER_UPGRADE","WEAPONSET_RIDER1","WEAPONSET_RIDER2","WEAPONSET_RIDER3","WEAPONSET_RIDER4","WEAPONSET_SPECIAL_ONE","WEAPONSET_SPECIAL_TWO","WEAPONSET_SPECIAL_UPGRADE","WEAPONSET_TOGGLE_1","WEAPONSET_TOGGLE_2","WEAPONSET_TOGGLE_3","WEAPONSET_VETERAN","WEAPONSLOTID_01","WEAPONSLOTID_02","WEAPONSLOTID_03","WEAPONSLOTID_04","WEAPONSLOTID_05","WEAPONSLOTID_06","WEAPONSLOTID_07","WEAPONSLOTID_08","WEAPONSLOTID_09","WEAPONSLOTID_10","WEAPONSTATE_CLOSE_RANGE","WEAPONSTATE_CONTAINED","WEAPONSTATE_ONE","WEAPONSTATE_THREE","WEAPONSTATE_TWO","WEAPON_TOGGLING","WORLD_BUILDER"};
                int[] modelValues={233,236,169,171,380,175,282,278,280,281,279,425,454,323,345,342,343,346,347,348,349,350,341,344,261,64,66,65,153,167,2,157,267,427,93,94,95,96,97,211,443,377,378,177,180,178,179,217,219,328,324,327,326,325,218,192,149,430,158,10,420,419,152,223,222,221,334,3,251,448,449,450,451,452,453,252,253,254,255,444,445,446,447,215,8,237,311,265,244,242,245,243,62,9,329,154,181,183,182,188,187,186,185,189,184,104,103,105,106,108,107,109,110,112,111,113,114,116,115,117,118,120,119,121,122,262,144,146,145,227,367,366,358,364,357,365,361,362,353,355,356,359,360,369,354,352,368,363,67,235,234,78,79,80,81,82,73,74,75,76,77,83,84,85,86,87,263,164,213,16,1,428,20,257,191,190,414,418,415,416,417,330,424,275,276,277,274,321,226,431,435,432,436,433,434,437,21,-1,332,331,370,429,197,198,266,166,19,373,258,264,405,404,400,401,402,399,397,391,396,390,393,398,395,394,403,392,156,388,389,407,408,409,410,259,143,150,14,208,199,202,203,204,205,206,207,174,18,17,379,126,125,124,123,130,129,128,127,134,133,132,131,138,137,136,135,142,141,140,139,168,193,293,294,295,296,297,212,426,7,6,209,210,68,69,70,71,72,224,200,322,333,172,173,220,159,268,269,270,271,272,273,4,88,89,90,91,92,421,260,312,313,314,315,316,317,318,319,320,5,151,376,375,374,162,163,256,423,196,176,15,11,301,302,303,304,305,306,307,308,309,225,13,371,298,299,300,60,59,56,61,58,57,231,216,12,229,228,230,455,335,43,44,45,161,155,165,232,382,383,384,385,386,387,0,194,195,238,240,239,241,336,338,339,337,340,372,381,170,201,351,283,292,284,285,286,287,288,289,290,291,406,310,98,99,100,101,102,413,411,412,160,148,422,147,246,249,250,247,248,33,41,34,23,32,24,42,25,26,27,35,36,37,38,39,40,31,28,29,30,22,51,52,53,54,55,438,439,440,441,442,49,50,46,48,47,63,214};
                for(int i=0;i<modelNames.Length;++i) {
                    string attrs="";
                    foreach(string name in conditionAttrs) attrs+=name+"='"+modelNames[i]+"' ";
                    AssetBuffer bits=Test(original,"model-"+modelNames[i],FX("","<NuggetList>"+Nugget(503892219u,attrs,"")+"</NuggetList>"),platform);
                    uint item=Word(bits.InstanceData,28,big);
                    for(int field=0;field<4;++field) {
                        uint at=Word(bits.InstanceData,(int)item+4+4*field,big);
                        if(at==0 || at>bits.InstanceData.Length-60) throw new Exception("Model-condition allocation");
                        for(int word=0;word<15;++word) {
                            uint expected=modelNames[i]=="ALL" ? UInt32.MaxValue : (modelValues[i]>=0 && modelValues[i]/32==word ? 1u<<(modelValues[i]&31) : 0u);
                            uint actual=Word(bits.InstanceData,(int)at+4*word,big);
                            if(actual!=expected) throw new Exception("Model-condition independent bit layout: platform="+platformId+" name="+modelNames[i]+" field="+field+" word="+word+" expected="+expected+" actual="+actual);
                        }
                    }
                }
                foreach(string value in new string[]{"","unknown","unknown -unknown","-MOVING -MOVING","+MOVING -MOVING","ALL","ALL -MOVING","-ALL","aLl","MOVING,ATTACKING","MOVING&#9;ATTACKING","+"}) {
                    string attrs="";
                    foreach(string name in conditionAttrs) attrs+=name+"='"+value+"' ";
                    Test(original,"model-operations:"+value,FX("","<NuggetList>"+Nugget(503892219u,attrs,"")+"</NuggetList>"),platform);
                }
                foreach(string value in new string[]{"DEFAULT","EMP","FREEFALL","HELD","INFILTRATED","PARALYZED","SCRIPT_DISABLED","SCRIPT_UNDERPOWERED","TEMPORARILY_BUSY","UNDERPOWERED","UNMANNED","USER_PARALYZED","unknown","","EMP EMP"}) {
                    Test(original,"disabled-enum:"+value,FX("","<NuggetList>"+Nugget(503892219u,"","<SourceMustNotHaveBeenDisabledThisFrameByType>"+value+"</SourceMustNotHaveBeenDisabledThisFrameByType>")+"</NuggetList>"),platform);
                }
                foreach(string value in values) {
                    Test(original,"particle-nugget:"+value,FX("","<NuggetList>"+Nugget(751572338u,"Count='"+value+"' CreateOverrideBone='"+value+"' TargetOverrideBone='"+value+"' SystemLife='"+value+"'","<Rotate X='"+value+"' Y='"+value+"' Z='"+value+"'/><Radius Low='"+value+"' High='"+value+"'/><TargetOffset x='"+value+"' y='"+value+"' z='"+value+"'/>")+"</NuggetList>"),platform);
                    Test(original,"decal-nugget:"+value,FX("","<NuggetList>"+Nugget(866573550u,"Decal='"+value+"' Size='"+value+"' OpacityStart='"+value+"' OpacityPeak='"+value+"' OpacityEnd='"+value+"' Lifetime='"+value+"'","<Color R='"+value+"' G='"+value+"' B='"+value+"' A='"+value+"'/>")+"</NuggetList>"),platform);
                    Test(original,"bone-nugget:"+value,FX("","<NuggetList>"+Nugget(3121289073u,"Bone='"+value+"' HoldBetweenStateID='"+value+"' RequireFrequentUpdate='"+value+"'","")+"</NuggetList>"),platform);
                }

                foreach(string value in new string[]{"RANDOM","SCORCH_1","SCORCH_10","SCORCH_11","SCORCH_12","SCORCH_13","SCORCH_14","SCORCH_15","SCORCH_16","SCORCH_2","SCORCH_3","SCORCH_4","SCORCH_5","SCORCH_6","SCORCH_7","SCORCH_8","SCORCH_9","TERRAIN_MAPPED","","unknown","scorch_1"})
                    Test(original,"scorch-enum:"+value,FX("","<NuggetList>"+Nugget(138194854u,"Type='"+value+"'","<RandomRange Low='"+value+"' High='"+value+"'/>")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"HOLD","KILL","NONE","SPAWN","unknown",""})
                    Test(original,"action-enum:"+value,FX("","<NuggetList>"+Nugget(3121289073u,"ActionType='"+value+"'","")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"CATAPULT_ROCK","NONE","TREBUCHET_ROCK","unknown",""})
                    Test(original,"trigger-enum:"+value,FX("","<NuggetList>"+Nugget(3121289073u,"TriggerType='"+value+"'","")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"ADDITIVE","ALPHA","unknown","alpha",""})
                    Test(original,"decal-shader:"+value,FX("","<NuggetList>"+Nugget(866573550u,"Shader='"+value+"'","")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"CONSTANT","GAUSSIAN","HIGH_BIAS","LOW_BIAS","TRIANGULAR","UNIFORM","unknown","uniform",""})
                    Test(original,"distribution-enum:"+value,FX("","<NuggetList>"+Nugget(751572338u,"","<Radius Type='"+value+"' Low='1.25' High='2.75'/><Height Type='"+value+"'/><InitialDelay Type='"+value+"'/>")+"</NuggetList>"),platform);
                foreach(string value in new string[]{"0x1e08c8fb","03602144373"})
                    Test(original,"typeid-numeric:"+value,FX("","<NuggetList><Nugget TypeId='"+value+"' Value='Audio\\17'/></NuggetList>"),platform);

                foreach(string child in new string[]{"<InitialDelay/>","<InitialDelay Low='0'/>","<InitialDelay High='0'/>","<InitialDelay Type='GAUSSIAN'/>","<InitialDelay Type='unknown' Low='2.25'/>","<InitialDelay Low='-1' High='-1'/>"})
                    Test(original,"particle-initialdelay-partial:"+child,FX("","<NuggetList>"+Nugget(751572338u,"",child)+"</NuggetList>"),platform);
                Console.WriteLine("PASS platform={0} fixtures={1} threeArrayComparisons={2} directArrayComparisons={3} rejections={4} independentLayouts={5}",
                    platformId, fixtures-startFixtures, (comparisons-startComparisons)/2, (comparisons-startComparisons)/2, rejections-startRejections, layouts-startLayouts);
            }
            Console.WriteLine("RESULT fixtures={0} arrayComparisons={1} rejections={2} independentLayouts={3}", fixtures, comparisons, rejections, layouts);
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
