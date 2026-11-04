using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;

unsafe partial class WeaponOracle
{
    const string AssetNamespace = "uri:ea.com:eala:asset";
    static int comparisons;
    static int annotatedFixtures;
    static IAssetBuilderPlugin formalPlugin;
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
    static void Equal(string name, byte[] original, byte[] recovered)
    {
        if (original == null || recovered == null || original.Length != recovered.Length)
            throw new Exception(name + " lengths: original=" + (original == null ? -1 : original.Length) + " recovered=" + (recovered == null ? -1 : recovered.Length));
        for (int i = 0; i < original.Length; ++i)
            if (original[i] != recovered[i]) throw new Exception(name + " byte " + i + ": original=" + original[i] + " recovered=" + recovered[i]);
        ++comparisons;
    }
    static uint Word(byte[] bytes, int offset, bool big)
    {
        if (!big) return BitConverter.ToUInt32(bytes, offset);
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset+1] << 16) | ((uint)bytes[offset+2] << 8) | bytes[offset+3];
    }
    static void Test(IAssetBuilderPlugin plugin, string label, string xmlText, TargetPlatform platform, int count)
    {
        plugin.Initialize(null, platform);
        var xml = new XmlDocument(); xml.LoadXml(xmlText);
        var namespaces = new XmlNamespaceManager(xml.NameTable); namespaces.AddNamespace("ea", AssetNamespace);
        var declaration = Declaration(xml, namespaces);
        AssetBuffer original = plugin.ProcessInstance(declaration);
        AssetBuffer recovered = WeaponCompiler.ProcessInstance(declaration, platform);
        Equal(label + "/instance", original.InstanceData, recovered.InstanceData);
        Equal(label + "/relocations", original.RelocationData, recovered.RelocationData);
        Equal(label + "/imports", original.ImportsData, recovered.ImportsData);
        // Exercise the BAB annotation boundary across the whole existing matrix.
        // Preserve the supplied polymorphic dispatch tags (including missing tags).
        foreach(XmlElement element in xml.SelectNodes("//*")) {
            if(element.ParentNode is XmlElement && element.ParentNode.LocalName=="Nuggets") continue;
            element.SetAttribute("TypeId", "314159265");
        }
        AssetBuffer annotatedOriginal=plugin.ProcessInstance(declaration);
        AssetBuffer annotatedRecovered=WeaponCompiler.ProcessInstance(declaration,platform);
        Equal(label+"/annotation-original-instance",original.InstanceData,annotatedOriginal.InstanceData);
        Equal(label+"/annotation-original-relo",original.RelocationData,annotatedOriginal.RelocationData);
        Equal(label+"/annotation-original-imp",original.ImportsData,annotatedOriginal.ImportsData);
        Equal(label+"/annotation-recovered-instance",annotatedOriginal.InstanceData,annotatedRecovered.InstanceData);
        Equal(label+"/annotation-recovered-relo",annotatedOriginal.RelocationData,annotatedRecovered.RelocationData);
        Equal(label+"/annotation-recovered-imp",annotatedOriginal.ImportsData,annotatedRecovered.ImportsData);
        formalPlugin.Initialize(null,platform);
        AssetBuffer formal=formalPlugin.ProcessInstance(declaration);
        Equal(label+"/formal-instance",annotatedOriginal.InstanceData,formal.InstanceData);
        Equal(label+"/formal-relo",annotatedOriginal.RelocationData,formal.RelocationData);
        Equal(label+"/formal-imp",annotatedOriginal.ImportsData,formal.ImportsData);
        ++annotatedFixtures;
        if (count < 0) return; // Resource fixtures have their own independent layout checks.
        bool big = platform != TargetPlatform.Win32;
        int tokenSize = 1296 + 36*count;
        if (recovered.InstanceData.Length != 1624 + 44*count || recovered.RelocationData.Length != (count == 0 ? 0 : (count+1)*4) || recovered.ImportsData.Length != 0 ||
            Word(recovered.InstanceData, 8, big) != tokenSize+4 || Word(recovered.InstanceData, 104*12, big) != 0 ||
            Word(recovered.InstanceData, 98*12+8, big) != 1260+36*count || Word(recovered.InstanceData, 1268+36*count, big) != tokenSize+256)
            throw new Exception(label + " independent scalar-only layout assertion");
        int present = 0;
        for (int i = 0; i < 4; ++i) {
            uint pointer = Word(recovered.InstanceData, tokenSize + 236 + i*4, big);
            if (pointer == 0) continue;
            if (pointer != tokenSize+328+present*8 ||
                Word(recovered.RelocationData, present*4, big) != tokenSize+236+i*4 ||
                Word(recovered.InstanceData, (93+i)*12+8, big) != 1260+present*36)
                throw new Exception(label + " independent duration pointer/relocation assertion");
            ++present;
        }
        if (present != count || (count != 0 && Word(recovered.RelocationData, count*4, big) != uint.MaxValue))
            throw new Exception(label + " duration count/sentinel assertion");
        Console.WriteLine("PASS {0} platform={1} bytes={2}", label, (int)platform, recovered.InstanceData.Length);
    }
    static string Weapon(string attrs) { return "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Oracle' " + attrs + "/>"; }
    static void Reject(string xmlText) {
#if WEAPON_TRANSCRIPT_ORACLE
        WeaponTranscript.Rejection(xmlText); ++rejections; return;
#else
#if WEAPON_TRANSCRIPT_PRODUCT
        WeaponTranscript.Rejection(xmlText);
#endif
        var xml = new XmlDocument(); xml.LoadXml(xmlText);
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", AssetNamespace);
        try { PluginSchemaBoundary.Validate(xml);  WeaponCompiler.CompileXml(xml.DocumentElement, ns, false); }
        catch (NotSupportedException) { ++rejections; return; }
        throw new Exception("Unsupported input was accepted: " + xmlText);
#endif
    }
    static int fixtures, rejections;
    static void Check(IAssetBuilderPlugin plugin, string label, string attrs, TargetPlatform platform) {
        Test(plugin, label, Weapon(attrs), platform, 0); ++fixtures;
    }

    static string Attribute(string name, string value) {
        return name + "='" + System.Security.SecurityElement.Escape(value) + "'";
    }
    static void Resources(IAssetBuilderPlugin plugin, TargetPlatform platform) {
        string[] refs = { "FireSound", "FireSoundPerClip", "FiringLoopSound", "FireFX", "FireVeteranFX", "FireFlankFX", "PreAttackFX", "SingleAmmoReloadedNotFullSound", "ClipReloadedSound", "PreAttackWeapon" };
        string[] values = { "", "NONE", "event", @"event\0", @"event\1", @"event\123", @"event\-1", @"event\-2", @"event\+17", @"event\  42tail", @"event\010", @"event\0x10", @"event\", @"\9", @"event\bad", @"event\1\999", @"event\2147483647", @"event\2147483648", @"event\4294967295", @"event\-2147483648", @"event\-2147483649", @"event\999999999999999999999", @"event\--1", "event/7" };
        foreach (string name in refs) foreach (string value in values) {
            Test(plugin, "resource/" + name + "/" + value, Weapon(Attribute(name, value)), platform, -1); ++fixtures;
        }
        foreach (string name in new[] { "PreferredTargetBone", "ProjectileStreamName" }) {
            foreach (string value in new[] { "", "a", "AB", "ABC", "AbCd", "AbCdE", "BONE_TURRET", " bone & <Turret> ", "é", "Éa", "abé", "中", "a中", "abc中", new string('X', 257) }) {
                Test(plugin, "text/" + name + "/" + value, Weapon(Attribute(name, value)), platform, -1); ++fixtures;
            }
            for (int n = 0; n <= 65; ++n) {
                Test(plugin, "text-length/" + name + "/" + n, Weapon(Attribute(name, new string('Q', n))), platform, -1); ++fixtures;
            }
        }
        string attrs = "PreferredTargetBone='ABC' FireSound='s\\0' FireSoundPerClip='s\\17' FiringLoopSound='' FireFX='fx\\9' FireVeteranFX='fx\\-1' FireFlankFX='fx\\123' PreAttackFX='fx\\1' SingleAmmoReloadedNotFullSound='s\\2' ClipReloadedSound='s\\3' PreAttackWeapon='w\\4'";
        string xml = "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Resources' " + attrs + "><ClipReloadTime MinSeconds='4s'/><PreAttackDelay MinSeconds='1s'/></WeaponTemplate>";
        Test(plugin, "all-resources-and-durations", xml, platform, -1); ++fixtures;
        var doc = new XmlDocument(); doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("ea", AssetNamespace);
        var result = WeaponCompiler.CompileXml(doc.DocumentElement, ns, platform != TargetPlatform.Win32);
        bool big = platform != TargetPlatform.Win32;
        const int tokens = 1368, root = tokens, tail = tokens+328;
        // Four-byte string including terminator, four pointer-reference words,
        // then the two duration payloads; token tables are only for durations.
        if (result.InstanceData.Length != tokens+328+4+16+16 || result.ImportsData.Length != 0 ||
            Word(result.InstanceData, root+76, big) != 3 || Word(result.InstanceData, root+80, big) != tail ||
            result.InstanceData[tail] != 65 || result.InstanceData[tail+3] != 0 ||
            Word(result.InstanceData, root+84, big) != tail+4 || Word(result.InstanceData, root+88, big) != tail+8 ||
            Word(result.InstanceData, root+92, big) != tail+12 || Word(result.InstanceData, root+224, big) != tail+16 ||
            Word(result.InstanceData, tail+4, big) != 1 || Word(result.InstanceData, tail+8, big) != 18 ||
            Word(result.InstanceData, tail+12, big) != 0 || Word(result.InstanceData, tail+16, big) != 5 ||
            Word(result.InstanceData, root+96, big) != 10 || Word(result.InstanceData, root+100, big) != 0 ||
            Word(result.InstanceData, root+236, big) != tail+20 || Word(result.InstanceData, root+248, big) != tail+28 ||
            Word(result.InstanceData, 26*12+8, big) != tail+4 || Word(result.InstanceData, 88*12+8, big) != tail+16)
            throw new Exception("Independent combined resource layout assertion");
        uint[] relocationOffsets = { 80, 84, 88, 92, 224, 236, 248 };
        if (result.RelocationData.Length != 32) throw new Exception("Resource relocation count");
        for (int i = 0; i < relocationOffsets.Length; ++i)
            if (Word(result.RelocationData, i*4, big) != root+relocationOffsets[i]) throw new Exception("Resource relocation order");
        if (Word(result.RelocationData, 28, big) != uint.MaxValue) throw new Exception("Resource relocation sentinel");
    }


    static string WithChild(string name, string attrs) {
        return "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Optional'><" + name + " " + attrs + "/></WeaponTemplate>";
    }
    static void OptionalBlocks(IAssetBuilderPlugin plugin, TargetPlatform platform) {
        string[] soundNames = { "OverrideVoiceAttackSound", "OverrideVoiceEnterStateAttackSound" };
        foreach (string name in soundNames) {
            foreach (string attrs in new[] { "", "Sound=''", "EvaEvent=''", "Sound='NONE'", @"Sound='s\0'", @"Sound='s\17' EvaEvent='e\9'", @"Sound='s\-1' EvaEvent='e\-2'", @"EvaEvent='e\2147483648'" }) {
                Test(plugin, name + "/" + attrs, WithChild(name, attrs), platform, -1); ++fixtures;
            }
            foreach (string value in new[] { "", @"s\0", @"s\1", @"s\-1", @"s\4294967295", "NONE", @"s\bad" }) {
                Test(plugin, name+"/sound/"+value, WithChild(name, Attribute("Sound", value)), platform, -1); ++fixtures;
                Test(plugin, name+"/eva/"+value, WithChild(name, Attribute("EvaEvent", value)), platform, -1); ++fixtures;
            }
            Test(plugin, name+"/first-duplicate", "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Optional'><"+name+" Sound='a\\2'/><"+name+" Sound='b\\99'/></WeaponTemplate>", platform, -1); ++fixtures;
        }
        foreach (string attrs in new[] { "", "IsAntiGarrisonWeapon='true'", "UseLongLockOnTimeCode='TRUE'", "IsAntiGarrisonWeapon='yes' UseLongLockOnTimeCode='0'", "MaxSpeedOfTarget=''", "MaxSpeedOfTarget='-0'", "MaxSpeedOfTarget='2d1'", "MaxSpeedOfTarget='1e-40'", "MaxSpeedOfTarget='1e40'", "MaxSpeedOfTarget='0x1p2'", "UseAsWarheadForDamageCalculations=''", @"UseAsWarheadForDamageCalculations='w\9'", @"IsAntiGarrisonWeapon='1' MaxSpeedOfTarget='-33.5' UseLongLockOnTimeCode='t' UseAsWarheadForDamageCalculations='w\-2'" }) {
            Test(plugin, "AI/"+attrs, WithChild("WeaponAiHintInfo", attrs), platform, -1); ++fixtures;
        }
        string[] blockNames = { "OverrideVoiceAttackSound", "OverrideVoiceEnterStateAttackSound", "PreAttackDelay", "FiringDuration", "CoolDownDelayBetweenShots", "ClipReloadTime", "WeaponAiHintInfo" };
        for (int mask = 0; mask < 128; ++mask) {
            string mixed = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Powerset' PreferredTargetBone='Turret' FireSound='s\\9' FireFX='f\\17' PreAttackWeapon='w\\33'>";
            for (int i = 6; i >= 0; --i) if ((mask & (1 << i)) != 0) {
                string attrs = i < 2 ? "Sound='s\\2' EvaEvent='e\\3'" : i < 6 ? "MinSeconds='1s' MaxSeconds='2s'" : "MaxSpeedOfTarget='3.5' UseAsWarheadForDamageCalculations='w\\7'";
                mixed += "<"+blockNames[i]+" "+attrs+"/>";
            }
            mixed += "</WeaponTemplate>";
            Test(plugin, "optional-powerset-and-resources/"+mask, mixed, platform, -1); ++fixtures;
        }
        string xml = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Combined'><WeaponAiHintInfo MaxSpeedOfTarget='33.5' IsAntiGarrisonWeapon='true' UseLongLockOnTimeCode='true' UseAsWarheadForDamageCalculations='w\\3'/><ClipReloadTime MinSeconds='4s'/><OverrideVoiceEnterStateAttackSound Sound='s\\2' EvaEvent='e\\3'/><FiringDuration MinSeconds='2s'/><OverrideVoiceAttackSound Sound='s\\0' EvaEvent='e\\1'/><CoolDownDelayBetweenShots MinSeconds='3s'/><PreAttackDelay MinSeconds='1s'/></WeaponTemplate>";
        Test(plugin, "all-optional-blocks-reverse-order", xml, platform, -1); ++fixtures;
        var doc = new XmlDocument(); doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("ea", AssetNamespace);
        bool big = platform != TargetPlatform.Win32;
        var result = WeaponCompiler.CompileXml(doc.DocumentElement, ns, big);
        const int tokens = 1572, tail = tokens+328;
        if (result.InstanceData.Length != tail+72 || result.ImportsData.Length != 0 || result.RelocationData.Length != 44 ||
            Word(result.InstanceData, tokens+228, big) != tail || Word(result.InstanceData, tail, big) != tail+8 ||
            Word(result.InstanceData, tail+4, big) != 2 || Word(result.InstanceData, tail+8, big) != 1 ||
            Word(result.InstanceData, tokens+232, big) != tail+12 || Word(result.InstanceData, tail+12, big) != tail+20 ||
            Word(result.InstanceData, tokens+236, big) != tail+24 || Word(result.InstanceData, tokens+248, big) != tail+48 ||
            Word(result.InstanceData, tokens+252, big) != tail+56 || Word(result.InstanceData, tail+56, big) != tail+68 ||
            Word(result.InstanceData, tail+60, big) != 4 || result.InstanceData[tail+64] != 1 || result.InstanceData[tail+65] != 1 ||
            Word(result.InstanceData, tail+68, big) != 0x42060000u || Word(result.RelocationData, 40, big) != uint.MaxValue)
            throw new Exception("Independent optional-block allocation/layout assertion");
    }

    // Explicit original PE enum ordinals; the oracle never reads the recovered map.
    static readonly string[] StatusNames = {
        "DESTROYED",
        "CAN_ATTACK",
        "UNDER_CONSTRUCTION",
        "UNSELECTABLE",
        "NO_COLLISIONS",
        "NO_ATTACK",
        "NO_SPECIAL_ABILITY",
        "AIRBORNE_TARGET",
        "PARACHUTING",
        "REPULSOR",
        "HIJACKED",
        "AFLAME",
        "BURNED",
        "CANNOT_BE_SOLD",
        "IS_FIRING_WEAPON",
        "IS_RELOADING_WEAPON",
        "IS_BRAKING",
        "STEALTHED",
        "HIDDEN",
        "DETECTED",
        "CAN_STEALTH",
        "SOLD",
        "UNDERGOING_REPAIR",
        "RECONSTRUCTING",
        "IS_ATTACKING",
        "NO_AUTO_ACQUIRE",
        "USING_ABILITY",
        "IS_AIMING_WEAPON",
        "NO_ATTACK_FROM_AI",
        "IGNORING_STEALTH",
        "IS_MELEE_ATTACKING",
        "FOLLOWING_THROUGH",
        "LEASHED_RETURNING",
        "DEATH_1",
        "DEATH_2",
        "DEATH_3",
        "DEATH_4",
        "DEATH_5",
        "CONTESTED",
        "CONTESTING_BUILDING",
        "HORDE_MEMBER",
        "TRANSPORT_MOVING_TO_CONTAIN",
        "RIDER_IS_PILOT",
        "DAMAGED",
        "REALLYDAMAGED",
        "RUBBLE",
        "IRRADIATED",
        "NO_SHADOW",
        "IN_STASIS",
        "OUT_OF_PHASE",
        "NEXT_MOVE_IS_REVERSE",
        "IMMOBILE",
        "IMMOBILE_ALLOW_ROTATE",
        "PAUSING",
        "FLEE_OFF_MAP",
        "NOT_IN_WORLD",
        "INAUDIBLE",
        "CHANTING",
        "ENRAGED",
        "SINKING",
        "RAMPAGING",
        "INSIDE_GARRISON",
        "DEPLOYED",
        "UNATTACKABLE",
        "ENCLOSED",
        "TEMPORARILY_DEFECTED",
        "TAGGED",
        "DEPLOYING",
        "TOPPLING",
        "PORTER_TAGGED",
        "NO_SQUISHCOLLIDE_DELAY",
        "STAND_GROUND",
        "UNCONTROLLABLY_SCARED",
        "SPECIAL_ABILITY_PACKING_UNPACKING_OR_USING",
        "TIBERIUM_VIBRATING",
        "UPDATING_AI",
        "CLONED",
        "IGNORE_AI_COMMAND",
        "RUNNING_DOWN_FROM_BEHIND",
        "DO_NOT_SCORE",
        "CAN_NOT_WALK_ON",
        "MARCH_OF_DEATH",
        "DO_NOT_PICK_ME",
        "INHERITED_FROM_ALLY_TEAM",
        "SWITCHED_WEAPONS",
        "END_FIRE_STATE",
        "BOOKENDING",
        "GENERIC_TOGGLE_STATE",
        "CAN_CONTEST_GARRISON",
        "BUILD_BEING_CANCELED",
        "PENDING_CONSTRUCTION",
        "PHANTOM_STRUCTURE",
        "IN_FORMATION_TEMPLATE",
        "IS_LEAVING_FACTORY",
        "MOVING_TO_DISMOUNT",
        "NO_HERO_PROPERTIES",
        "CAN_ENTER_ANYTHING",
        "ENTERING_GOAL",
        "CHARGING_BASE_DEFENSES",
        "INVISIBLE_DETECTED_BY_FRIEND",
        "INVISIBLE_DETECTED",
        "SHRUNK",
        "ATTACHED",
        "WONT_RIDE_WITH_YOU",
        "COMMAND_BUTTON_TOGGLED",
        "OCLMONITOR_COMPLETED_TASK",
        "OCLMONITOR_MONITOR_RELEASED",
        "USER_POWERED_DOWN",
        "GARRISONED",
        "BOOBY_TRAPPED",
        "IS_HIDEOUT",
        "EXECUTING_STORED_COMMAND",
        "UNIT_WANTS_TO_REGARRISON",
        "IN_COVER",
        "DONT_CLEAR_FOR_BUILD",
        "MATCH_TARGETS_SPEED",
        "HAS_TIBERIUM_GROWTH_MOD",
        "HAS_TIBERIUM_UPGRADE",
        "STEAL_NEXT_UNIT_TRAPPED",
        "DOES_CONTAIN_TIBERIUM",
        "IS_BEING_HARVESTED",
        "NO_REFUND",
        "SHIELDBODY_ENABLED",
        "HEALTH_PERCENT_0",
        "HEALTH_PERCENT_25",
        "HEALTH_PERCENT_50",
        "HEALTH_PERCENT_75",
        "HEALTH_PERCENT_100",
        "WEAPON_UPGRADED_01",
        "WEAPON_UPGRADED_02",
        "WEAPON_UPGRADED_03",
        "COMBINED_PARENT",
        "COMBINED_CHILD",
        "COMBINED_ATTACHED",
        "LOADED_FROM_MAP",
        "ATTACKING_GARRISONED_STRUCTURE",
        "EXITING_COMBINED",
        "DELAYED_ENTER_STRUCTURE",
        "DOCKING",
        "HARVESTING",
        "BRIDGE_IMPASSABLE",
        "POWERED_DOWN_EMP",
        "FORCE_ATTACKING",
        "FORCE_ATTACK_MOVING",
        "SCARED_CIVILIAN_CAR",
        "USER_PARALYZED",
        "BOOBY_TRAP_EXPLODE",
        "IS_ENGAGED",
        "CARRYING_FLAG",
        "IS_MOVING_TO_RALLY_POINT",
        "SPECIAL_ARMOR_ACTIVE",
        "AIRCRAFT_IGNORE_SAMEPLAYER_HANGAR_RULE",
        "NEXT_MOVE_IS_FORCE_ATTACK_MOVE",
        "SPECIALABILITY_ACTIVE",
        "CAN_STEALTH_FROM_PRODUCER",
        "GARRISON_CAN_STEALTH",
        "CAN_ATTACK_WHILE_STEALTHED",
        "RESET_GOAL_POSITION",
        "RIFT_OCCUPIED",
        "WATER_LOCOMOTOR_ACTIVE",
        "GROUND_LOCOMOTOR_ACTIVE",
        "AIR_LOCOMOTOR_ACTIVE",
        "UNPACKING",
        "OVER_WATER",
        "CONTAINER_OCCUPIED",
        "PLACED_BY_PLAYER",
        "DEFLECT_INCOMING_FIRE",
        "DEPART_FROM_BOARDING",
        "WAITING_FOR_BOARDING",
        "HAS_SECONDARY_DAMAGE",
        "IS_BEING_DRAGGED",
        "CLEARED_FOR_LANDING",
        "VEHICLE_ATTACHED",
        "PARKED_AT_AIRFIELD",
        "IN_SHIELD_SPHERE",
        "HIT_SHIELD_SPHERE",
        "NON_AUTOACQUIRABLE",
        "CHARGING_WEAPON",
        "OVERCHARGING_WEAPON",
        "ENABLE_TRACER_DRAW",
        "PREEMPT_PRE_ATTACK",
        "LINE_SEGMENT_DAMAGE_ACTIVE",
        "ADVANCED_MISSILE_PACKS",
        "LEECHED_TARGET_ACTIVE",
        "PLAYER_POWER_1",
        "PLAYER_POWER_2",
        "PLAYER_POWER_3",
        "PLAYER_POWER_4",
        "PLAYER_POWER_5",
        "GRAPPLE_UPDATE_ACTIVE",
        "SHROUD_REVEAL_TO_ALL",
        "SURFACED",
        "SUBMERGED",
        "SKIRMISH_AI_DO_NOT_ATTACK",
        "SKIRMISH_AI_DO_NOT_TAKE",
        "TRANSFORMATION_TOGGLE_STATE",
        "REPAIR_ALLIES_WHEN_IDLE",
        "DO_NOT_AVOID_TALLBUILDING",
        "ABSORBED_DAMAGE",
        "NO_BRIBE",
        "IGNORING_POWER_DOWN",
        "EXCEEDED_PATH_NEIGHBOR_COUNT",
        "CANNOT_LAND_AT_AIRFIELD",
        "WALL_SEGMENT",
        "IMMUNE_TO_BARK",
        "UNDER_RUSH_ATTACK",
        "UNDER_FROZEN",
        "LANDING_IN_PROGRESS",
        "UNDER_IRON_CURTAIN",
        "EXPLOSIVES_ATTACHED",
        "EXPLOSIVES_DETONATING",
        "LIFTED_INTO_AIR",
        "IN_NANOHIVE",
        "SURFACED_IMMOBILE",
        "BROADCASTING_INVISIBILITY",
        "SCRAMBLED",
        "MAGNETIZED",
        "RIOT_SHIELDED",
        "POINT_DEFENSE_DRONE_ATTACHED",
        "GRAPPLE_UPDATE_BEING_MOVED",
        "TARGETED_FOR_REPAIR",
        "IN_SPIDER_HOLE",
        "ANTI_GARRISON",
    };
    static uint[] StatusBit(int bit) {
        var words = new uint[7]; if (bit >= 0) words[bit/32] = 1u << (bit%32); return words;
    }
    static void StatusFixture(IAssetBuilderPlugin plugin, TargetPlatform platform, string label,
        string required, string forbidden, uint[] requiredWords, uint[] forbiddenWords) {
        string attrs = (required == null ? "" : Attribute("RequiredFiringObjectStatus", required)) + " " +
            (forbidden == null ? "" : Attribute("ForbiddenFiringObjectStatus", forbidden));
        Test(plugin, "status-"+label, Weapon(attrs), platform, -1); ++fixtures;
        var xml = new XmlDocument(); xml.LoadXml(Weapon(attrs));
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", AssetNamespace);
        var result = WeaponCompiler.ProcessInstance(Declaration(xml, ns), platform);
        bool big = platform != TargetPlatform.Win32;
        int count = (required == null ? 0 : 1) + (forbidden == null ? 0 : 1), present = 0;
        if (result.InstanceData.Length != 1624+28*count || result.RelocationData.Length != (count == 0 ? 0 : 4*(count+1)) || result.ImportsData.Length != 0)
            throw new Exception("Status independent allocation sizes");
        for (int field = 0; field < 2; ++field) {
            bool exists = field == 0 ? required != null : forbidden != null;
            uint target = Word(result.InstanceData, 1296+136+4*field, big);
            uint tokenTarget = Word(result.InstanceData, (41+field)*12+8, big);
            if (!exists) { if (target != 0 || tokenTarget != 0) throw new Exception("Absent status pointer"); continue; }
            if (target != 1624+28*present || tokenTarget != target || Word(result.RelocationData, 4*present, big) != 1296+136+4*field)
                throw new Exception("Status independent pointer/member bookmark/relocation");
            var expected = field == 0 ? requiredWords : forbiddenWords;
            for (int word = 0; word < 7; ++word)
                if (Word(result.InstanceData, (int)target+4*word, big) != expected[word]) throw new Exception("Status independent bit word: "+label+" word="+word);
            ++present;
        }
        if (count != 0 && Word(result.RelocationData, 4*count, big) != uint.MaxValue) throw new Exception("Status relocation sentinel");
    }
    static void StatusMasks(IAssetBuilderPlugin plugin, TargetPlatform platform) {
        var zero = new uint[7]; var all = new uint[7]; for (int i = 0; i < 7; ++i) all[i] = uint.MaxValue;
        StatusFixture(plugin, platform, "absent", null, null, null, null);
        StatusFixture(plugin, platform, "empty", "", "", zero, zero);
        for (int bit = 0; bit < StatusNames.Length; ++bit) {
            string name = StatusNames[bit]; var one = StatusBit(bit);
            StatusFixture(plugin, platform, "single-"+bit, name, null, one, null);
            StatusFixture(plugin, platform, "forbidden-"+bit, null, name, null, one);
            StatusFixture(plugin, platform, "prefix-"+bit, "+"+name, "-"+name, one, one);
            StatusFixture(plugin, platform, "toggle-"+bit, name+" -"+name, "-"+name+" -"+name, zero, zero);
            StatusFixture(plugin, platform, "duplicate-"+bit, name+" "+name, "+"+name+" +"+name, one, one);
            StatusFixture(plugin, platform, "lowercase-"+bit, name.ToLowerInvariant(), null, StatusBit(198), null);
        }
        foreach (string text in new[] { "unknown", "NONE", "+", "-", "++"+StatusNames[0], "--"+StatusNames[222], "0", "222", ",", "AFLAME,ATTACHED" })
            StatusFixture(plugin, platform, "unknown-"+text, text, null, StatusBit(198), null);
        foreach (string text in new[] { "ALL", "all", "AlL", "+ALL", "-ALL", "ALL -AFLAME", "AFLAME -all ATTACHED", "ALL unknown", "-ALL -ALL" })
            StatusFixture(plugin, platform, "all-"+text, text, null, all, null);
        StatusFixture(plugin, platform, "spaces", " \t\r\n ", "\t"+StatusNames[31]+"\n\r", zero, StatusBit(31));
        // Every word boundary and the highest valid bit. ALL above also checks bit 223.
        string boundary = ""; var boundaryWords = new uint[7];
        foreach (int bit in new[] { 0,31,32,63,64,95,96,127,128,159,160,191,192,222 }) { boundary += " "+StatusNames[bit]; boundaryWords[bit/32] |= 1u << (bit%32); }
        StatusFixture(plugin, platform, "boundaries", boundary, boundary, boundaryWords, boundaryWords);
        var validAll = new uint[7]; for (int i = 0; i < 7; ++i) validAll[i] = uint.MaxValue; validAll[6] = 0x7fffffff;
        StatusFixture(plugin, platform, "all-names", string.Join(" ", StatusNames), null, validAll, null);
        // Mix status payloads with earlier/later pointer allocations and every optional block.
        for (int mask = 0; mask < 128; ++mask) {
            string[] blocks = { "OverrideVoiceAttackSound Sound='s\\2'", "OverrideVoiceEnterStateAttackSound EvaEvent='e\\3'", "PreAttackDelay MinSeconds='1s'", "FiringDuration MaxSeconds='2s'", "CoolDownDelayBetweenShots MinSeconds='3s'", "ClipReloadTime MaxSeconds='4s'", "WeaponAiHintInfo MaxSpeedOfTarget='5' UseAsWarheadForDamageCalculations='w\\4'" };
            string xml = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='StatusMixed' PreferredTargetBone='bone' FireSound='s\\0' PreAttackWeapon='w\\1' RequiredFiringObjectStatus='"+boundary+"' ForbiddenFiringObjectStatus='ALL'>";
            for (int i = 6; i >= 0; --i) if ((mask & (1<<i)) != 0) xml += "<"+blocks[i]+"/>";
            xml += "</WeaponTemplate>";
            Test(plugin, "status-mixed-"+mask, xml, platform, -1); ++fixtures;
        }
    }

    static readonly string[] FilterNames = { "SurpriseAttackObjectFilter", "CombinedAttackObjectFilter", "HitStoredObjectFilter" };
    static readonly string[] KindNames = {
        "OBSTACLE",
        "SELECTABLE",
        "TARGETABLE",
        "ALLY_TARGETABLE",
        "IMMOBILE",
        "CAN_ATTACK",
        "STICK_TO_TERRAIN_SLOPE",
        "CAN_CAST_REFLECTIONS",
        "SHRUBBERY",
        "STRUCTURE",
        "INFANTRY",
        "CAVALRY",
        "MONSTER",
        "MACHINE",
        "AIRCRAFT",
        "HUGE_VEHICLE",
        "DOZER",
        "HARVESTER",
        "COMMANDCENTER",
        "CASTLE_CENTER",
        "SALVAGER",
        "WEAPON_SALVAGER",
        "TRANSPORT",
        "BRIDGE",
        "CANT_TOGGLE_POWER",
        "SIEGE_WEAPON",
        "PROJECTILE",
        "PRELOAD",
        "NO_GARRISON",
        "CASTLE_KEEP",
        "SHADOW_ULTRA_HIGH_ONLY",
        "NO_COLLIDE",
        "REPAIR_PAD",
        "HEAL_PAD",
        "STEALTH_GARRISON",
        "SUPPLY_GATHERING_CENTER",
        "AIRFIELD",
        "DRAWABLE_ONLY",
        "MP_COUNT_FOR_VICTORY",
        "REBUILD_HOLE",
        "SCORE",
        "SCORE_CREATE",
        "SCORE_DESTROY",
        "NO_HEAL_ICON",
        "CAN_RAPPEL",
        "PARACHUTABLE",
        "CAN_BE_REPULSED",
        "COMMANDO",
        "IGNORED_IN_GUI",
        "CRATE",
        "CAPTURABLE",
        "LINKED_TO_FLAG",
        "CLEARED_BY_BUILD",
        "SMALL_MISSILE",
        "ALWAYS_VISIBLE",
        "UNATTACKABLE",
        "MINE",
        "CAN_PLACE_CHARGE",
        "CLASSIFY_ENTIRE_FOOTPRINT",
        "ALWAYS_SELECTABLE",
        "WALK_ON_TOP_OF_WALL",
        "DEFENSIVE_WALL",
        "FS_POWER",
        "FS_FACTORY",
        "FS_BASE_DEFENSE",
        "FS_TECHNOLOGY",
        "AIRCRAFT_PATH_AROUND",
        "LOW_OVERLAPPABLE",
        "FORCEATTACKABLE",
        "AUTO_RALLYPOINT",
        "CAN_CAPTURE",
        "POWERED",
        "PRODUCED_AT_HELIPAD",
        "DRONE",
        "CAN_SEE_THROUGH_STRUCTURE",
        "BALLISTIC_MISSILE",
        "CLICK_THROUGH",
        "SUPPLY_SOURCE_ON_PREVIEW",
        "PARACHUTE",
        "GARRISONABLE_UNTIL_DESTROYED",
        "SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME",
        "IMMUNE_TO_CAPTURE",
        "HULK",
        "CAN_PLACE_MANIPULATOR",
        "DONT_SUBMERGE_BY_WAVES",
        "CANNOT_BUILD_NEAR_SUPPLIES",
        "SUPPLY_SOURCE",
        "REVEAL_TO_ALL",
        "DISGUISER",
        "INERT",
        "IGNORES_SELECT_ALL",
        "DONT_AUTO_CRUSH_INFANTRY",
        "SIEGE_TOWER",
        "TIME_BOMB",
        "ROCK",
        "THROWN_OBJECT",
        "GRAB_AND_KILL",
        "OPTIMIZED_PROP",
        "ENVIRONMENT",
        "DEFLECT_BY_SPECIAL_POWER",
        "WORKING_PASSENGER",
        "BASE_FOUNDATION",
        "NEED_BASE_FOUNDATION",
        "REACT_WHEN_SELECTED",
        "CAN_BE_CAPTURED",
        "IGNORED_IN_FINDPOSITIONAROUND",
        "HORDE",
        "COMBO_HORDE",
        "NONOCCLUDING",
        "NO_FREEWILL_ENTER",
        "TIBERIUM_FIELD",
        "BEAM_TARGET",
        "BEAM_TARGET_REFLECTOR",
        "TACTICAL_MARKER",
        "PATH_THROUGH_EACH_OTHER",
        "NOTIFY_OF_PREATTACK",
        "GARRISON",
        "MELEE_HORDE",
        "BASE_SITE",
        "INERT_SHROUD_REVEALER",
        "OCL_BIT",
        "SPELL_BOOK",
        "DEPRECATED",
        "PATH_THROUGH_INFANTRY",
        "NO_FORMATION_MOVEMENT",
        "NO_BASE_CAPTURE",
        "ARMY_SUMMARY",
        "CIVILIAN_UNIT",
        "NOT_AUTOACQUIRABLE",
        "KEEP_CLASSIFIED_WHEN_DEAD",
        "CHUNK_VENDOR",
        "REFINERY",
        "MOVE_ONLY",
        "FS_CASH_PRODUCER",
        "ROCK_VENDOR",
        "BLOCKING_GATE",
        "CRANE",
        "AI_NEEDS_PLAYER_POWER_UPDATE",
        "MINE_TRIGGER",
        "BUFF",
        "GRAB_AND_DROP",
        "TRANSFORMER",
        "SCARY",
        "AI_CAN_UNPACK_NEAR_CONYARD",
        "BROADCASTS_INVISIBILITY",
        "CAN_ATTACK_WALLS",
        "IGNORE_FOR_VICTORY",
        "DO_NOT_CLASSIFY",
        "WALL_UPGRADE",
        "ACTIVATE_AFTER_UNPACK",
        "SKIRMISH_AI_DONT_MULTI_TARGET",
        "NOT_SELLABLE",
        "WALL_PIECE",
        "WALL_HUB",
        "BUILD_FOR_FREE",
        "IGNORE_FOR_EVA_SPEECH_POSITION",
        "WORLDBUILDER_SNAP_TO_GRID",
        "BANNER",
        "UNPACKS_INTO_BUILDING",
        "ORE_NODE",
        "PASS_EXPERIENCE_TO_SLAVER",
        "HAS_HEALTH_BAR",
        "IGNORE_IN_AI_HUNT_TACTIC",
        "DEPLOYED_MINE",
        "CANNOT_RETALIATE",
        "CREEP",
        "POWERED_POWERS_ONLY",
        "VITAL_FOR_BASE_SURVIVAL",
        "DO_NOT_PICK_ME_WHEN_BUILDING",
        "SUMMONED",
        "HIDE_IF_FOGGED",
        "ALWAYS_SHOW_HOUSE_COLOR",
        "MOVE_FOR_NOONE",
        "WB_DISPLAY_SCRIPT_NAME",
        "CAN_CLIMB_WALLS",
        "NO_SHADOW",
        "LARGE_RECTANGLE_PATHFIND",
        "SUBMARINE",
        "PORT",
        "WALL_SEGMENT",
        "SIMPLE_OBJECT_PROP",
        "SHIP",
        "OPTIMIZED_SOUND",
        "PASS_EXPERIENCE_TO_CONTAINED",
        "DOZER_FACTORY",
        "THREAT_FINDER",
        "ECONOMY_STRUCTURE",
        "ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES",
        "CAN_TOPPLE",
        "CYCLE_SELECTION",
        "CAN_SKIP_SHADOW",
        "SCALEABLE_WALL",
        "SKYBOX",
        "WALL_GATE",
        "CAPTUREFLAG",
        "CANNOT_STOP_ON_WATER",
        "PASS_EXPERIENCE_TO_CONTAINER",
        "RESIST_EMP",
        "ORIENTS_TO_CAMERA",
        "NEVER_CULL_FOR_MP",
        "DONT_USE_CANCEL_BUILD_BUTTON",
        "CIVILIAN_BUILDING",
        "HEAVY_MELEE_HITTER",
        "DONT_HIDE_IF_FOGGED",
        "VEHICLE",
        "HARVESTABLE",
        "GRABBABLE",
        "CONSTRUCTION_YARD",
        "CAN_SHOOT_OVER_WALLS",
        "PASS_EXPERIENCE_TO_PRODUCER",
        "EXPANSION_PAD",
        "AMPHIBIOUS",
        "SHOW_BEHIND_OCCLUDERS",
        "FS_MONEY_STORAGE",
        "VALID_TARGET_FOR_C4",
        "UNIQUE_UNIT",
        "SURPRISE_ATTACKER",
        "COVER",
        "INFILTRATOR",
        "BRIDGE_SEGMENT",
        "FS_WAR_FACTORY",
        "FS_BARRACKS",
        "FS_RADAR",
        "FS_AIR_FIELD",
        "FS_TECH_CENTER",
        "TIBERIUM_BASED",
        "TIBERIUM",
        "CAN_ATTACK_STEALTHED",
        "IMMUNE_TO_CYCLONIC_SHOCKWAVE",
        "CANNOT_BE_DETECTED",
        "AUTO_ACQUIRABLE_BY_AI",
        "CRUSHABLE_OBSTACLE",
        "NEUTRAL_TECH",
        "SUPER_WEAPON",
        "IGNORE_CAN_NOT_BUILD",
        "SKIRMISH_AI_DONT_CONSIDER_THREAT",
        "SKIRMISH_AI_DONT_GARRISON",
        "SKIRMISH_AI_DONT_REPAIR",
        "SKIRMISH_AI_ATTEMPT_TO_DODGE",
        "ASSAULT_AIRCRAFT",
        "BOMBER_AIRCRAFT",
        "FIGHTER_AIRCRAFT",
        "ANTI_GARRISON",
        "ENGINEER",
        "EXPANSION_UNIT",
        "EGG",
        "OUTPOST",
        "BEACON",
        "HUSK",
        "IGNORE_FORCE_MOVE",
        "LINE_OF_SIGHT_IGNORES_BUILDINGS",
        "AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED",
        "SNIPER",
        "ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED",
        "SKIP_DEFECT_IF_UNCOMBINED",
        "SKIP_IDLE_WHEN_CAPTURED",
        "BRIDGE_ENDCAP",
        "BRIDGE_GATEHOUSE",
        "CAN_BE_FAVORITE_UNIT",
        "SLAVE_OWNER",
        "MCV",
        "CAN_HEAL_ALLIES",
        "PROJECT_BUILDABILITY_FOR_ALLIES",
        "CAN_NOT_BUILD_ON_LAND",
        "CAN_BUILD_ON_WATER",
        "CAN_BUILD_ON_DEEP_WATER",
        "LIMITED_PRODUCTION_AIRCRAFT",
        "LEAVE_PARKING_ON_BUILD_COMPLETE",
        "ENCLOSURE",
        "RADAR_SCRAMBLER",
        "CANNOT_HIJACK",
        "CRUSHABLE_BUILDING",
        "CRUSH_CRUSHABLE_BUILDINGS",
        "GRANTS_VETERANCY",
        "ALWAYS_VISIBLE_IN_RADAR",
        "ATTACH_ATTACK",
        "DONT_UNATTACH_WHEN_HEALING",
        "T1_UNIT",
        "T2_UNIT",
        "T3_UNIT",
        "DEBRIS",
        "DONT_DESTROY_IF_ON_BRIDGE",
        "CARPET_BOMBER",
        "TERRAIN_LIKE_GROUND_OBJECT",
    };
    static void FilterFixture(IAssetBuilderPlugin plugin, TargetPlatform platform, string label, string content) {
        Test(plugin, "filter-"+label, "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Filters'>"+content+"</WeaponTemplate>", platform, -1); ++fixtures;
    }
    static void Filters(IAssetBuilderPlugin plugin, TargetPlatform platform) {
        foreach (string field in FilterNames) {
            FilterFixture(plugin, platform, field+"-empty", "<"+field+"/>");
            foreach (string attr in new[] { "id", "Rule", "Alignment", "Relationship", "Include", "Exclude", "StatusBitFlags", "StatusBitFlagsExclude" })
                foreach (string value in new[] { "", "NONE", "unknown", "ALL", "-ALL", "all -ALL", "+", "-", "AFLAME", "AFLAME -AFLAME", "Aircraft", "0" })
                    FilterFixture(plugin, platform, field+"-"+attr+"-"+value, "<"+field+" "+Attribute(attr,value)+"/>");
            foreach (string value in new[] { "ALL", "ANY", "NONE", "UNPARSED", "all", "ANY ALL", " ALL" })
                FilterFixture(plugin, platform, field+"-rule-"+value, "<"+field+" "+Attribute("Rule",value)+"/>");
            foreach (string value in new[] { "NONE", "GOOD", "EVIL", "good", " EVIL" })
                FilterFixture(plugin, platform, field+"-alignment-"+value, "<"+field+" "+Attribute("Alignment",value)+"/>");
            foreach (string value in new[] { "ALLIES", "ENEMIES", "NEUTRAL", "SAME_PLAYER", "ALLIES -ENEMIES NEUTRAL SAME_PLAYER", "ALL -ENEMIES" })
                FilterFixture(plugin, platform, field+"-relationship-"+value, "<"+field+" "+Attribute("Relationship",value)+"/>");
            for (int bit = 0; bit < KindNames.Length; ++bit) {
                string name = KindNames[bit];
                FilterFixture(plugin, platform, field+"-kind-"+bit, "<"+field+" "+Attribute("Include",name)+" "+Attribute("Exclude","-"+name)+"/>");
                FilterFixture(plugin, platform, field+"-toggle-"+bit, "<"+field+" "+Attribute("Include",name+" -"+name)+" "+Attribute("Exclude",name.ToLowerInvariant())+"/>");
            }
            FilterFixture(plugin, platform, field+"-all-kinds", "<"+field+" "+Attribute("Include",string.Join(" ", KindNames))+" Exclude='ALL'/>");
            foreach (int count in new[] { 0,1,2,7,16,17,32,33,127,128,129,256,257 }) {
                string content = "<"+field+" id='FiLtEr' Rule='ANY' Alignment='EVIL' Include='AIRCRAFT' Exclude='ALL' StatusBitFlags='AFLAME' StatusBitFlagsExclude='ALL'>";
                for (int i = 0; i < count; ++i) content += "<IncludeThing>Object_"+(i%7)+"</IncludeThing><ExcludeThing><![CDATA[Other_"+i+"]]></ExcludeThing>";
                content += "</"+field+">";
                FilterFixture(plugin, platform, field+"-things-"+count, content);
            }
            // First duplicate wins, independent of sibling order.
            FilterFixture(plugin, platform, field+"-duplicate", "<"+field+" Rule='ANY' Include='AIRCRAFT'/><"+field+" Rule='NONE' Exclude='ALL'/>");
        }
        for (int mask = 0; mask < 8; ++mask) {
            string content = "";
            for (int i = 2; i >= 0; --i) if ((mask & (1<<i)) != 0) content += "<"+FilterNames[i]+"/>";
            FilterFixture(plugin, platform, "subset-"+mask, content);
            var xml = new XmlDocument(); xml.LoadXml("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Layout'>"+content+"</WeaponTemplate>");
            var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea",AssetNamespace);
            var buffer = WeaponCompiler.ProcessInstance(Declaration(xml,ns),platform);
            bool big = platform != TargetPlatform.Win32; int count = 0; for(int i=0;i<3;++i) if((mask & (1<<i)) != 0) ++count;
            int tokens = 1296+156*count, present = 0;
            if(buffer.InstanceData.Length != 1624+268*count || buffer.RelocationData.Length != (count==0 ? 0 : 4*(count+1))) throw new Exception("Filter independent allocation size");
            for(int i=0;i<3;++i) {
                uint pointer = Word(buffer.InstanceData,tokens+264+4*i,big), token = Word(buffer.InstanceData,(99+i)*12+8,big);
                if((mask & (1<<i)) == 0) { if(pointer!=0 || token!=0) throw new Exception("Absent filter pointers"); continue; }
                int at = tokens+328+112*present, table = 1272+156*present;
                if(pointer!=at || token!=table || Word(buffer.RelocationData,4*present,big)!=tokens+264+4*i || Word(buffer.InstanceData,at+4,big)!=3 || Word(buffer.InstanceData,at+12,big)!=0)
                    throw new Exception("Filter independent pointer/default layout");
                if(Word(buffer.InstanceData,table+8,big)!=at || Word(buffer.InstanceData,table+4*12+8,big)!=at+16 || Word(buffer.InstanceData,table+5*12+8,big)!=at+52 ||
                    Word(buffer.InstanceData,table+8*12+8,big)!=table+132 || Word(buffer.InstanceData,table+9*12+8,big)!=table+144 ||
                    Word(buffer.InstanceData,table+140,big)!=at+96 || Word(buffer.InstanceData,table+152,big)!=at+104) throw new Exception("Filter independent nested tokens");
                ++present;
            }
            if(count!=0 && Word(buffer.RelocationData,4*count,big)!=uint.MaxValue) throw new Exception("Filter relocation sentinel");
        }
        for(int mask=0;mask<128;++mask) {
            string[] blocks = { "OverrideVoiceAttackSound Sound='s\\2'", "OverrideVoiceEnterStateAttackSound EvaEvent='e\\3'", "PreAttackDelay MinSeconds='1s'", "FiringDuration MaxSeconds='2s'", "CoolDownDelayBetweenShots MinSeconds='3s'", "ClipReloadTime MaxSeconds='4s'", "WeaponAiHintInfo MaxSpeedOfTarget='5'" };
            string xml = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Mixed' RequiredFiringObjectStatus='ALL' FireSound='s\\0'><IncompatibleAttributeModifier>m\\1</IncompatibleAttributeModifier>";
            for(int i=6;i>=0;--i) if((mask & (1<<i))!=0) xml+="<"+blocks[i]+"/>";
            foreach(string name in FilterNames) xml+="<"+name+" Include='AIRCRAFT' StatusBitFlags='AFLAME' StatusBitFlagsExclude='ALL'><IncludeThing>Obj</IncludeThing><ExcludeThing>other</ExcludeThing></"+name+">";
            xml+="</WeaponTemplate>";
            Test(plugin,"filter-mixed-"+mask,xml,platform,-1);++fixtures;
        }
    }

    static void ModifierLists(IAssetBuilderPlugin plugin, TargetPlatform platform) {
        string[] values = { "", "none", "mod\\0", "mod\\7", "mod\\-1", "mod\\2147483648", "mod\\-2147483649", "mod\\12tail", "mod\\garbage", "mod\\+4", " mod\\ 3", "mod\\2\\9" };
        uint[] expected = { 0,0,1,8,0,0x80000000,0x80000001,13,1,5,4,3 };
        foreach (int count in new[] { 0,1,2,3,7,16,17,31,32,33,64,127,128,129,255,256,257 }) {
            string xmlText = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='Modifiers'>";
            for (int i = 0; i < count; ++i) xmlText += "<IncompatibleAttributeModifier>"+System.Security.SecurityElement.Escape(values[i%values.Length])+"</IncompatibleAttributeModifier>";
            xmlText += "</WeaponTemplate>";
            Test(plugin, "modifier-list-"+count, xmlText, platform, -1); ++fixtures;
            var xml = new XmlDocument(); xml.LoadXml(xmlText);
            var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", AssetNamespace);
            var buffer = WeaponCompiler.ProcessInstance(Declaration(xml, ns), platform);
            bool big = platform != TargetPlatform.Win32;
            int tokens = 1296+12*count, payload = tokens+328;
            if (buffer.InstanceData.Length != 1624+16*count || Word(buffer.InstanceData, tokens+284, big) != count ||
                Word(buffer.InstanceData, 103*12+8, big) != 1284 || Word(buffer.InstanceData, 1292, big) != tokens+284 ||
                Word(buffer.InstanceData, tokens+288, big) != (count == 0 ? 0 : payload) ||
                buffer.RelocationData.Length != (count == 0 ? 0 : 8) || buffer.ImportsData.Length != 0)
                throw new Exception("Modifier list independent layout");
            if (count != 0 && (Word(buffer.RelocationData, 0, big) != tokens+288 || Word(buffer.RelocationData, 4, big) != uint.MaxValue))
                throw new Exception("Modifier list pointer relocation");
            for (int i = 0; i < count; ++i) {
                int entry = 1296+12*i;
                if (Word(buffer.InstanceData, entry, big) != 2974916843u || Word(buffer.InstanceData, entry+4, big) != 0 || Word(buffer.InstanceData, entry+8, big) != payload+4*i ||
                    Word(buffer.InstanceData, payload+4*i, big) != expected[i%expected.Length]) throw new Exception("Modifier element token/value/order");
            }
        }
        foreach (string value in values) {
            string xml = "<WeaponTemplate xmlns='"+AssetNamespace+"' id='MixedModifiers' RequiredFiringObjectStatus='ALL' ForbiddenFiringObjectStatus='AFLAME' FireSound='s\\0' PreferredTargetBone='bone'><IncompatibleAttributeModifier>"+System.Security.SecurityElement.Escape(value)+"</IncompatibleAttributeModifier><PreAttackDelay MinSeconds='1s'/><WeaponAiHintInfo MaxSpeedOfTarget='2'/><IncompatibleAttributeModifier><![CDATA[mod\\3]]></IncompatibleAttributeModifier></WeaponTemplate>";
            Test(plugin, "modifier-mixed-"+value, xml, platform, -1); ++fixtures;
        }
    }

    static int directStatusComparisons;
    static void DirectStatus(Assembly original) {
#if !WEAPON_TRANSCRIPT_PRODUCT
        MethodInfo parser = null;
        foreach (var method in original.GetModules()[0].GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (method.Name == "Marshaler.Marshal<enum ObjectStatusType,223>") parser = method;
        if (parser == null) throw new Exception("Original ObjectStatus inline parser missing");
#endif
        var values = new List<string>(StatusNames);
        foreach (string name in StatusNames) { values.Add("-"+name); values.Add(name+" -"+name); }
        values.AddRange(new[] { "", " ", "\v\f\t\r\n", "AFLAME\vATTACHED\fANTI_GARRISON", "AFLAME\0ATTACHED", "\0ALL", "ALL", "all -AFLAME", "-ALL unknown", "AFLAME -ALL -ATTACHED", "+", "-", "unknown", "unknown -unknown", "AFLAME,ATTACHED", "++AFLAME", "--AFLAME", "ＡＬＬ", "é", "状态", "AFLAME\u00a0ATTACHED" });
        uint[][] seeds = { new uint[7], new uint[] { 0x01234567,0x89abcdef,0x80000000,1,0xaaaaaaaa,0x55555555,0xffffffff }, new uint[] { 0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff,0xffffffff } };
        foreach (var seed in seeds) foreach (string text in values) {
#if WEAPON_TRANSCRIPT_ORACLE || WEAPON_TRANSCRIPT_PRODUCT
            var expected = WeaponTranscript.ExpectedMask(original, "Marshaler.Marshal<enum ObjectStatusType,223>", text, seed);
#else
            var expected = (uint[])seed.Clone();
            IntPtr ansi = Marshal.StringToHGlobalAnsi(text);
            try {
                fixed (uint* target = expected) {
                    var types = parser.GetParameters();
                    parser.Invoke(null, new object[] { Pointer.Box(ansi.ToPointer(), types[0].ParameterType), Pointer.Box(target, types[1].ParameterType), Pointer.Box(null, types[2].ParameterType) });
                }
            } finally { Marshal.FreeHGlobal(ansi); }
#endif
            var actual = WeaponCompiler.MarshalObjectStatus(text, seed);
            for (int i = 0; i < 7; ++i) if (expected[i] != actual[i]) throw new Exception("Direct ObjectStatus mismatch: "+text+" word="+i);
            ++directStatusComparisons;
        }
        Console.WriteLine("PASS direct ObjectStatus inline parsing: "+directStatusComparisons+" seven-word comparisons");
    }

    static int Main(string[] args) {
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
#if WEAPON_TRANSCRIPT_ORACLE
            if (args.Length != 2) throw new ArgumentException("Expected original Tokenizer path and transcript output.");
            var assembly = Assembly.LoadFrom(args[0]);
            WeaponTranscript.Open(args[1], true);
            var originalPlugin = (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.Tokenizer.Plugin", true));
            WeaponCompiler.SetOriginal(originalPlugin, assembly);
            formalPlugin = originalPlugin;
#elif WEAPON_TRANSCRIPT_PRODUCT
            if (args.Length != 1) throw new ArgumentException("Expected original transcript path.");
            Assembly assembly = null;
            WeaponTranscript.Open(args[0], false);
            formalPlugin = new Recovered.Weapon.Plugin();
            WeaponTranscript.VerifyProduct(typeof(InstanceDeclaration).Assembly, typeof(WeaponCompiler).Assembly, formalPlugin.GetType().Assembly);
#else
            var assembly = Assembly.LoadFrom(args[0]);
            formalPlugin=(IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[1]).GetType("Recovered.Weapon.Plugin"));
#endif
            DirectStatus(assembly);
            DirectModel(assembly);
#if WEAPON_TRANSCRIPT_ORACLE
            IAssetBuilderPlugin plugin = new WeaponTranscript.OriginalPlugin(originalPlugin);
#elif WEAPON_TRANSCRIPT_PRODUCT
            IAssetBuilderPlugin plugin = new WeaponTranscript.OriginalPlugin(null);
#else
            var plugin = (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.Tokenizer.Plugin"));
#endif
            foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 }) {
                plugin.Initialize(null,platform);formalPlugin.Initialize(null,platform);
                var expectedInfo=plugin.GetExtendedTypeInformation(2496977262u);
                var actualInfo=formalPlugin.GetExtendedTypeInformation(2496977262u);
                if(expectedInfo.TypeId!=actualInfo.TypeId || expectedInfo.TypeName!=actualInfo.TypeName || expectedInfo.TypeHash!=actualInfo.TypeHash || expectedInfo.ProcessingHash!=actualInfo.ProcessingHash || expectedInfo.Tokenized!=actualInfo.Tokenized || plugin.GetVersionNumber()!=formalPlugin.GetVersionNumber() || plugin.GetAllTypesHash()!=formalPlugin.GetAllTypesHash())
                    throw new Exception("Formal Weapon plugin metadata mismatch: "+platform);
                Filters(plugin, platform);
                Scatter(plugin, platform);
                Nuggets(plugin, platform);
                ProjectileNuggets(plugin, platform);
                ActivationNuggets(plugin, platform);
                ScalarNuggets(plugin, platform);
                EffectNuggets(plugin, platform);
                FinalNuggets(plugin, platform);
                ModifierLists(plugin, platform);
                StatusMasks(plugin, platform);
                OptionalBlocks(plugin, platform);
                Resources(plugin, platform);
                Check(plugin, "defaults", "", platform);
                Check(plugin, "AttackRange-0", "AttackRange='-0'", platform);
                Check(plugin, "AttackRange-1", "AttackRange='-23.75'", platform);
                Check(plugin, "AttackRange-2", "AttackRange='33.3333333333'", platform);
                Check(plugin, "AttackRange-3", "AttackRange='2d1'", platform);
                Check(plugin, "AttackRange-4", "AttackRange='1e-40'", platform);
                Check(plugin, "AttackRange-5", "AttackRange='1e40'", platform);
                Check(plugin, "AttackRange-6", "AttackRange='0x1p2'", platform);
                Check(plugin, "AttackRange-7", "AttackRange='NaN'", platform);
                Check(plugin, "AttackRange-8", "AttackRange=''", platform);
                Check(plugin, "AttackRange-9", "AttackRange='garbage'", platform);
                Check(plugin, "MinimumAttackRange-0", "MinimumAttackRange='-0'", platform);
                Check(plugin, "MinimumAttackRange-1", "MinimumAttackRange='-23.75'", platform);
                Check(plugin, "MinimumAttackRange-2", "MinimumAttackRange='33.3333333333'", platform);
                Check(plugin, "MinimumAttackRange-3", "MinimumAttackRange='2d1'", platform);
                Check(plugin, "MinimumAttackRange-4", "MinimumAttackRange='1e-40'", platform);
                Check(plugin, "MinimumAttackRange-5", "MinimumAttackRange='1e40'", platform);
                Check(plugin, "MinimumAttackRange-6", "MinimumAttackRange='0x1p2'", platform);
                Check(plugin, "MinimumAttackRange-7", "MinimumAttackRange='NaN'", platform);
                Check(plugin, "MinimumAttackRange-8", "MinimumAttackRange=''", platform);
                Check(plugin, "MinimumAttackRange-9", "MinimumAttackRange='garbage'", platform);
                Check(plugin, "RangeBonusMinHeight-0", "RangeBonusMinHeight='-0'", platform);
                Check(plugin, "RangeBonusMinHeight-1", "RangeBonusMinHeight='-23.75'", platform);
                Check(plugin, "RangeBonusMinHeight-2", "RangeBonusMinHeight='33.3333333333'", platform);
                Check(plugin, "RangeBonusMinHeight-3", "RangeBonusMinHeight='2d1'", platform);
                Check(plugin, "RangeBonusMinHeight-4", "RangeBonusMinHeight='1e-40'", platform);
                Check(plugin, "RangeBonusMinHeight-5", "RangeBonusMinHeight='1e40'", platform);
                Check(plugin, "RangeBonusMinHeight-6", "RangeBonusMinHeight='0x1p2'", platform);
                Check(plugin, "RangeBonusMinHeight-7", "RangeBonusMinHeight='NaN'", platform);
                Check(plugin, "RangeBonusMinHeight-8", "RangeBonusMinHeight=''", platform);
                Check(plugin, "RangeBonusMinHeight-9", "RangeBonusMinHeight='garbage'", platform);
                Check(plugin, "RangeBonus-0", "RangeBonus='-0'", platform);
                Check(plugin, "RangeBonus-1", "RangeBonus='-23.75'", platform);
                Check(plugin, "RangeBonus-2", "RangeBonus='33.3333333333'", platform);
                Check(plugin, "RangeBonus-3", "RangeBonus='2d1'", platform);
                Check(plugin, "RangeBonus-4", "RangeBonus='1e-40'", platform);
                Check(plugin, "RangeBonus-5", "RangeBonus='1e40'", platform);
                Check(plugin, "RangeBonus-6", "RangeBonus='0x1p2'", platform);
                Check(plugin, "RangeBonus-7", "RangeBonus='NaN'", platform);
                Check(plugin, "RangeBonus-8", "RangeBonus=''", platform);
                Check(plugin, "RangeBonus-9", "RangeBonus='garbage'", platform);
                Check(plugin, "RangeBonusPerFoot-0", "RangeBonusPerFoot='-0'", platform);
                Check(plugin, "RangeBonusPerFoot-1", "RangeBonusPerFoot='-23.75'", platform);
                Check(plugin, "RangeBonusPerFoot-2", "RangeBonusPerFoot='33.3333333333'", platform);
                Check(plugin, "RangeBonusPerFoot-3", "RangeBonusPerFoot='2d1'", platform);
                Check(plugin, "RangeBonusPerFoot-4", "RangeBonusPerFoot='1e-40'", platform);
                Check(plugin, "RangeBonusPerFoot-5", "RangeBonusPerFoot='1e40'", platform);
                Check(plugin, "RangeBonusPerFoot-6", "RangeBonusPerFoot='0x1p2'", platform);
                Check(plugin, "RangeBonusPerFoot-7", "RangeBonusPerFoot='NaN'", platform);
                Check(plugin, "RangeBonusPerFoot-8", "RangeBonusPerFoot=''", platform);
                Check(plugin, "RangeBonusPerFoot-9", "RangeBonusPerFoot='garbage'", platform);
                Check(plugin, "RequestAssistRange-0", "RequestAssistRange='-0'", platform);
                Check(plugin, "RequestAssistRange-1", "RequestAssistRange='-23.75'", platform);
                Check(plugin, "RequestAssistRange-2", "RequestAssistRange='33.3333333333'", platform);
                Check(plugin, "RequestAssistRange-3", "RequestAssistRange='2d1'", platform);
                Check(plugin, "RequestAssistRange-4", "RequestAssistRange='1e-40'", platform);
                Check(plugin, "RequestAssistRange-5", "RequestAssistRange='1e40'", platform);
                Check(plugin, "RequestAssistRange-6", "RequestAssistRange='0x1p2'", platform);
                Check(plugin, "RequestAssistRange-7", "RequestAssistRange='NaN'", platform);
                Check(plugin, "RequestAssistRange-8", "RequestAssistRange=''", platform);
                Check(plugin, "RequestAssistRange-9", "RequestAssistRange='garbage'", platform);
                Check(plugin, "AcceptableAimDelta-0", "AcceptableAimDelta='-180d'", platform);
                Check(plugin, "AcceptableAimDelta-1", "AcceptableAimDelta='90d'", platform);
                Check(plugin, "AcceptableAimDelta-2", "AcceptableAimDelta='3.1415926535'", platform);
                Check(plugin, "AcceptableAimDelta-3", "AcceptableAimDelta='2d1'", platform);
                Check(plugin, "AcceptableAimDelta-4", "AcceptableAimDelta='45deg'", platform);
                Check(plugin, "AcceptableAimDelta-5", "AcceptableAimDelta=''", platform);
                Check(plugin, "AcceptableAimDelta-6", "AcceptableAimDelta='garbage'", platform);
                Check(plugin, "AimDirection-0", "AimDirection='-180d'", platform);
                Check(plugin, "AimDirection-1", "AimDirection='90d'", platform);
                Check(plugin, "AimDirection-2", "AimDirection='3.1415926535'", platform);
                Check(plugin, "AimDirection-3", "AimDirection='2d1'", platform);
                Check(plugin, "AimDirection-4", "AimDirection='45deg'", platform);
                Check(plugin, "AimDirection-5", "AimDirection=''", platform);
                Check(plugin, "AimDirection-6", "AimDirection='garbage'", platform);
                Check(plugin, "ScatterRadius-0", "ScatterRadius='-0'", platform);
                Check(plugin, "ScatterRadius-1", "ScatterRadius='-23.75'", platform);
                Check(plugin, "ScatterRadius-2", "ScatterRadius='33.3333333333'", platform);
                Check(plugin, "ScatterRadius-3", "ScatterRadius='2d1'", platform);
                Check(plugin, "ScatterRadius-4", "ScatterRadius='1e-40'", platform);
                Check(plugin, "ScatterRadius-5", "ScatterRadius='1e40'", platform);
                Check(plugin, "ScatterRadius-6", "ScatterRadius='0x1p2'", platform);
                Check(plugin, "ScatterRadius-7", "ScatterRadius='NaN'", platform);
                Check(plugin, "ScatterRadius-8", "ScatterRadius=''", platform);
                Check(plugin, "ScatterRadius-9", "ScatterRadius='garbage'", platform);
                Check(plugin, "ScatterLength-0", "ScatterLength='-0'", platform);
                Check(plugin, "ScatterLength-1", "ScatterLength='-23.75'", platform);
                Check(plugin, "ScatterLength-2", "ScatterLength='33.3333333333'", platform);
                Check(plugin, "ScatterLength-3", "ScatterLength='2d1'", platform);
                Check(plugin, "ScatterLength-4", "ScatterLength='1e-40'", platform);
                Check(plugin, "ScatterLength-5", "ScatterLength='1e40'", platform);
                Check(plugin, "ScatterLength-6", "ScatterLength='0x1p2'", platform);
                Check(plugin, "ScatterLength-7", "ScatterLength='NaN'", platform);
                Check(plugin, "ScatterLength-8", "ScatterLength=''", platform);
                Check(plugin, "ScatterLength-9", "ScatterLength='garbage'", platform);
                Check(plugin, "ScatterIndependently-0", "ScatterIndependently='true'", platform);
                Check(plugin, "ScatterIndependently-1", "ScatterIndependently='false'", platform);
                Check(plugin, "ScatterIndependently-2", "ScatterIndependently='1'", platform);
                Check(plugin, "ScatterIndependently-3", "ScatterIndependently='0'", platform);
                Check(plugin, "ScatterIndependently-4", "ScatterIndependently='TRUE'", platform);
                Check(plugin, "ScatterIndependently-5", "ScatterIndependently='t'", platform);
                Check(plugin, "ScatterIndependently-6", "ScatterIndependently='yes'", platform);
                Check(plugin, "ScatterIndependently-7", "ScatterIndependently=''", platform);
                Check(plugin, "ScatterIndependently-8", "ScatterIndependently=' true'", platform);
                Check(plugin, "WeaponSpeed-0", "WeaponSpeed='-0'", platform);
                Check(plugin, "WeaponSpeed-1", "WeaponSpeed='-23.75'", platform);
                Check(plugin, "WeaponSpeed-2", "WeaponSpeed='33.3333333333'", platform);
                Check(plugin, "WeaponSpeed-3", "WeaponSpeed='2d1'", platform);
                Check(plugin, "WeaponSpeed-4", "WeaponSpeed='1e-40'", platform);
                Check(plugin, "WeaponSpeed-5", "WeaponSpeed='1e40'", platform);
                Check(plugin, "WeaponSpeed-6", "WeaponSpeed='0x1p2'", platform);
                Check(plugin, "WeaponSpeed-7", "WeaponSpeed='NaN'", platform);
                Check(plugin, "WeaponSpeed-8", "WeaponSpeed=''", platform);
                Check(plugin, "WeaponSpeed-9", "WeaponSpeed='garbage'", platform);
                Check(plugin, "MinWeaponSpeed-0", "MinWeaponSpeed='-0'", platform);
                Check(plugin, "MinWeaponSpeed-1", "MinWeaponSpeed='-23.75'", platform);
                Check(plugin, "MinWeaponSpeed-2", "MinWeaponSpeed='33.3333333333'", platform);
                Check(plugin, "MinWeaponSpeed-3", "MinWeaponSpeed='2d1'", platform);
                Check(plugin, "MinWeaponSpeed-4", "MinWeaponSpeed='1e-40'", platform);
                Check(plugin, "MinWeaponSpeed-5", "MinWeaponSpeed='1e40'", platform);
                Check(plugin, "MinWeaponSpeed-6", "MinWeaponSpeed='0x1p2'", platform);
                Check(plugin, "MinWeaponSpeed-7", "MinWeaponSpeed='NaN'", platform);
                Check(plugin, "MinWeaponSpeed-8", "MinWeaponSpeed=''", platform);
                Check(plugin, "MinWeaponSpeed-9", "MinWeaponSpeed='garbage'", platform);
                Check(plugin, "MaxWeaponSpeed-0", "MaxWeaponSpeed='-0'", platform);
                Check(plugin, "MaxWeaponSpeed-1", "MaxWeaponSpeed='-23.75'", platform);
                Check(plugin, "MaxWeaponSpeed-2", "MaxWeaponSpeed='33.3333333333'", platform);
                Check(plugin, "MaxWeaponSpeed-3", "MaxWeaponSpeed='2d1'", platform);
                Check(plugin, "MaxWeaponSpeed-4", "MaxWeaponSpeed='1e-40'", platform);
                Check(plugin, "MaxWeaponSpeed-5", "MaxWeaponSpeed='1e40'", platform);
                Check(plugin, "MaxWeaponSpeed-6", "MaxWeaponSpeed='0x1p2'", platform);
                Check(plugin, "MaxWeaponSpeed-7", "MaxWeaponSpeed='NaN'", platform);
                Check(plugin, "MaxWeaponSpeed-8", "MaxWeaponSpeed=''", platform);
                Check(plugin, "MaxWeaponSpeed-9", "MaxWeaponSpeed='garbage'", platform);
                Check(plugin, "ScaleWeaponSpeed-0", "ScaleWeaponSpeed='true'", platform);
                Check(plugin, "ScaleWeaponSpeed-1", "ScaleWeaponSpeed='false'", platform);
                Check(plugin, "ScaleWeaponSpeed-2", "ScaleWeaponSpeed='1'", platform);
                Check(plugin, "ScaleWeaponSpeed-3", "ScaleWeaponSpeed='0'", platform);
                Check(plugin, "ScaleWeaponSpeed-4", "ScaleWeaponSpeed='TRUE'", platform);
                Check(plugin, "ScaleWeaponSpeed-5", "ScaleWeaponSpeed='t'", platform);
                Check(plugin, "ScaleWeaponSpeed-6", "ScaleWeaponSpeed='yes'", platform);
                Check(plugin, "ScaleWeaponSpeed-7", "ScaleWeaponSpeed=''", platform);
                Check(plugin, "ScaleWeaponSpeed-8", "ScaleWeaponSpeed=' true'", platform);
                Check(plugin, "IgnoresContactPoints-0", "IgnoresContactPoints='true'", platform);
                Check(plugin, "IgnoresContactPoints-1", "IgnoresContactPoints='false'", platform);
                Check(plugin, "IgnoresContactPoints-2", "IgnoresContactPoints='1'", platform);
                Check(plugin, "IgnoresContactPoints-3", "IgnoresContactPoints='0'", platform);
                Check(plugin, "IgnoresContactPoints-4", "IgnoresContactPoints='TRUE'", platform);
                Check(plugin, "IgnoresContactPoints-5", "IgnoresContactPoints='t'", platform);
                Check(plugin, "IgnoresContactPoints-6", "IgnoresContactPoints='yes'", platform);
                Check(plugin, "IgnoresContactPoints-7", "IgnoresContactPoints=''", platform);
                Check(plugin, "IgnoresContactPoints-8", "IgnoresContactPoints=' true'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-0", "ScaleAttackRangeByAmmoRemaining='true'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-1", "ScaleAttackRangeByAmmoRemaining='false'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-2", "ScaleAttackRangeByAmmoRemaining='1'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-3", "ScaleAttackRangeByAmmoRemaining='0'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-4", "ScaleAttackRangeByAmmoRemaining='TRUE'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-5", "ScaleAttackRangeByAmmoRemaining='t'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-6", "ScaleAttackRangeByAmmoRemaining='yes'", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-7", "ScaleAttackRangeByAmmoRemaining=''", platform);
                Check(plugin, "ScaleAttackRangeByAmmoRemaining-8", "ScaleAttackRangeByAmmoRemaining=' true'", platform);
                Check(plugin, "CanBeDodged-0", "CanBeDodged='true'", platform);
                Check(plugin, "CanBeDodged-1", "CanBeDodged='false'", platform);
                Check(plugin, "CanBeDodged-2", "CanBeDodged='1'", platform);
                Check(plugin, "CanBeDodged-3", "CanBeDodged='0'", platform);
                Check(plugin, "CanBeDodged-4", "CanBeDodged='TRUE'", platform);
                Check(plugin, "CanBeDodged-5", "CanBeDodged='t'", platform);
                Check(plugin, "CanBeDodged-6", "CanBeDodged='yes'", platform);
                Check(plugin, "CanBeDodged-7", "CanBeDodged=''", platform);
                Check(plugin, "CanBeDodged-8", "CanBeDodged=' true'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-0", "IdleAfterFiringDelaySeconds='1500ms'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-1", "IdleAfterFiringDelaySeconds='-2s'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-2", "IdleAfterFiringDelaySeconds='2d1s'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-3", "IdleAfterFiringDelaySeconds='1e-7'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-4", "IdleAfterFiringDelaySeconds='1ms'", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-5", "IdleAfterFiringDelaySeconds=''", platform);
                Check(plugin, "IdleAfterFiringDelaySeconds-6", "IdleAfterFiringDelaySeconds='garbage'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-0", "HoldAfterFiringDelaySeconds='1500ms'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-1", "HoldAfterFiringDelaySeconds='-2s'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-2", "HoldAfterFiringDelaySeconds='2d1s'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-3", "HoldAfterFiringDelaySeconds='1e-7'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-4", "HoldAfterFiringDelaySeconds='1ms'", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-5", "HoldAfterFiringDelaySeconds=''", platform);
                Check(plugin, "HoldAfterFiringDelaySeconds-6", "HoldAfterFiringDelaySeconds='garbage'", platform);
                Check(plugin, "HoldDuringReload-0", "HoldDuringReload='true'", platform);
                Check(plugin, "HoldDuringReload-1", "HoldDuringReload='false'", platform);
                Check(plugin, "HoldDuringReload-2", "HoldDuringReload='1'", platform);
                Check(plugin, "HoldDuringReload-3", "HoldDuringReload='0'", platform);
                Check(plugin, "HoldDuringReload-4", "HoldDuringReload='TRUE'", platform);
                Check(plugin, "HoldDuringReload-5", "HoldDuringReload='t'", platform);
                Check(plugin, "HoldDuringReload-6", "HoldDuringReload='yes'", platform);
                Check(plugin, "HoldDuringReload-7", "HoldDuringReload=''", platform);
                Check(plugin, "HoldDuringReload-8", "HoldDuringReload=' true'", platform);
                Check(plugin, "CanFireWhileMoving-0", "CanFireWhileMoving='true'", platform);
                Check(plugin, "CanFireWhileMoving-1", "CanFireWhileMoving='false'", platform);
                Check(plugin, "CanFireWhileMoving-2", "CanFireWhileMoving='1'", platform);
                Check(plugin, "CanFireWhileMoving-3", "CanFireWhileMoving='0'", platform);
                Check(plugin, "CanFireWhileMoving-4", "CanFireWhileMoving='TRUE'", platform);
                Check(plugin, "CanFireWhileMoving-5", "CanFireWhileMoving='t'", platform);
                Check(plugin, "CanFireWhileMoving-6", "CanFireWhileMoving='yes'", platform);
                Check(plugin, "CanFireWhileMoving-7", "CanFireWhileMoving=''", platform);
                Check(plugin, "CanFireWhileMoving-8", "CanFireWhileMoving=' true'", platform);
                Check(plugin, "WeaponRecoil-0", "WeaponRecoil='-180d'", platform);
                Check(plugin, "WeaponRecoil-1", "WeaponRecoil='90d'", platform);
                Check(plugin, "WeaponRecoil-2", "WeaponRecoil='3.1415926535'", platform);
                Check(plugin, "WeaponRecoil-3", "WeaponRecoil='2d1'", platform);
                Check(plugin, "WeaponRecoil-4", "WeaponRecoil='45deg'", platform);
                Check(plugin, "WeaponRecoil-5", "WeaponRecoil=''", platform);
                Check(plugin, "WeaponRecoil-6", "WeaponRecoil='garbage'", platform);
                Check(plugin, "MinTargetPitch-0", "MinTargetPitch='-180d'", platform);
                Check(plugin, "MinTargetPitch-1", "MinTargetPitch='90d'", platform);
                Check(plugin, "MinTargetPitch-2", "MinTargetPitch='3.1415926535'", platform);
                Check(plugin, "MinTargetPitch-3", "MinTargetPitch='2d1'", platform);
                Check(plugin, "MinTargetPitch-4", "MinTargetPitch='45deg'", platform);
                Check(plugin, "MinTargetPitch-5", "MinTargetPitch=''", platform);
                Check(plugin, "MinTargetPitch-6", "MinTargetPitch='garbage'", platform);
                Check(plugin, "MaxTargetPitch-0", "MaxTargetPitch='-180d'", platform);
                Check(plugin, "MaxTargetPitch-1", "MaxTargetPitch='90d'", platform);
                Check(plugin, "MaxTargetPitch-2", "MaxTargetPitch='3.1415926535'", platform);
                Check(plugin, "MaxTargetPitch-3", "MaxTargetPitch='2d1'", platform);
                Check(plugin, "MaxTargetPitch-4", "MaxTargetPitch='45deg'", platform);
                Check(plugin, "MaxTargetPitch-5", "MaxTargetPitch=''", platform);
                Check(plugin, "MaxTargetPitch-6", "MaxTargetPitch='garbage'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-0", "FiringLoopSoundContinuesDuringReload='true'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-1", "FiringLoopSoundContinuesDuringReload='false'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-2", "FiringLoopSoundContinuesDuringReload='1'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-3", "FiringLoopSoundContinuesDuringReload='0'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-4", "FiringLoopSoundContinuesDuringReload='TRUE'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-5", "FiringLoopSoundContinuesDuringReload='t'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-6", "FiringLoopSoundContinuesDuringReload='yes'", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-7", "FiringLoopSoundContinuesDuringReload=''", platform);
                Check(plugin, "FiringLoopSoundContinuesDuringReload-8", "FiringLoopSoundContinuesDuringReload=' true'", platform);
                Check(plugin, "ClipSize-0", "ClipSize='-1'", platform);
                Check(plugin, "ClipSize-1", "ClipSize='0'", platform);
                Check(plugin, "ClipSize-2", "ClipSize='2147483648'", platform);
                Check(plugin, "ClipSize-3", "ClipSize='-2147483649'", platform);
                Check(plugin, "ClipSize-4", "ClipSize='010'", platform);
                Check(plugin, "ClipSize-5", "ClipSize='0x2a'", platform);
                Check(plugin, "ClipSize-6", "ClipSize='garbage'", platform);
                Check(plugin, "ClipSize-7", "ClipSize=''", platform);
                Check(plugin, "ContinuousFireOne-0", "ContinuousFireOne='-1'", platform);
                Check(plugin, "ContinuousFireOne-1", "ContinuousFireOne='0'", platform);
                Check(plugin, "ContinuousFireOne-2", "ContinuousFireOne='2147483648'", platform);
                Check(plugin, "ContinuousFireOne-3", "ContinuousFireOne='-2147483649'", platform);
                Check(plugin, "ContinuousFireOne-4", "ContinuousFireOne='010'", platform);
                Check(plugin, "ContinuousFireOne-5", "ContinuousFireOne='0x2a'", platform);
                Check(plugin, "ContinuousFireOne-6", "ContinuousFireOne='garbage'", platform);
                Check(plugin, "ContinuousFireOne-7", "ContinuousFireOne=''", platform);
                Check(plugin, "ContinuousFireTwo-0", "ContinuousFireTwo='-1'", platform);
                Check(plugin, "ContinuousFireTwo-1", "ContinuousFireTwo='0'", platform);
                Check(plugin, "ContinuousFireTwo-2", "ContinuousFireTwo='2147483648'", platform);
                Check(plugin, "ContinuousFireTwo-3", "ContinuousFireTwo='-2147483649'", platform);
                Check(plugin, "ContinuousFireTwo-4", "ContinuousFireTwo='010'", platform);
                Check(plugin, "ContinuousFireTwo-5", "ContinuousFireTwo='0x2a'", platform);
                Check(plugin, "ContinuousFireTwo-6", "ContinuousFireTwo='garbage'", platform);
                Check(plugin, "ContinuousFireTwo-7", "ContinuousFireTwo=''", platform);
                Check(plugin, "ContinuousFireCoastSeconds-0", "ContinuousFireCoastSeconds='1500ms'", platform);
                Check(plugin, "ContinuousFireCoastSeconds-1", "ContinuousFireCoastSeconds='-2s'", platform);
                Check(plugin, "ContinuousFireCoastSeconds-2", "ContinuousFireCoastSeconds='2d1s'", platform);
                Check(plugin, "ContinuousFireCoastSeconds-3", "ContinuousFireCoastSeconds='1e-7'", platform);
                Check(plugin, "ContinuousFireCoastSeconds-4", "ContinuousFireCoastSeconds='1ms'", platform);
                Check(plugin, "ContinuousFireCoastSeconds-5", "ContinuousFireCoastSeconds=''", platform);
                Check(plugin, "ContinuousFireCoastSeconds-6", "ContinuousFireCoastSeconds='garbage'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-0", "AutoReloadWhenIdleSeconds='1500ms'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-1", "AutoReloadWhenIdleSeconds='-2s'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-2", "AutoReloadWhenIdleSeconds='2d1s'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-3", "AutoReloadWhenIdleSeconds='1e-7'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-4", "AutoReloadWhenIdleSeconds='1ms'", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-5", "AutoReloadWhenIdleSeconds=''", platform);
                Check(plugin, "AutoReloadWhenIdleSeconds-6", "AutoReloadWhenIdleSeconds='garbage'", platform);
                Check(plugin, "ShotsPerBarrel-0", "ShotsPerBarrel='-1'", platform);
                Check(plugin, "ShotsPerBarrel-1", "ShotsPerBarrel='0'", platform);
                Check(plugin, "ShotsPerBarrel-2", "ShotsPerBarrel='2147483648'", platform);
                Check(plugin, "ShotsPerBarrel-3", "ShotsPerBarrel='-2147483649'", platform);
                Check(plugin, "ShotsPerBarrel-4", "ShotsPerBarrel='010'", platform);
                Check(plugin, "ShotsPerBarrel-5", "ShotsPerBarrel='0x2a'", platform);
                Check(plugin, "ShotsPerBarrel-6", "ShotsPerBarrel='garbage'", platform);
                Check(plugin, "ShotsPerBarrel-7", "ShotsPerBarrel=''", platform);
                Check(plugin, "DamageDealtAtSelfPosition-0", "DamageDealtAtSelfPosition='true'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-1", "DamageDealtAtSelfPosition='false'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-2", "DamageDealtAtSelfPosition='1'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-3", "DamageDealtAtSelfPosition='0'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-4", "DamageDealtAtSelfPosition='TRUE'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-5", "DamageDealtAtSelfPosition='t'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-6", "DamageDealtAtSelfPosition='yes'", platform);
                Check(plugin, "DamageDealtAtSelfPosition-7", "DamageDealtAtSelfPosition=''", platform);
                Check(plugin, "DamageDealtAtSelfPosition-8", "DamageDealtAtSelfPosition=' true'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-0", "CheckStatusFlagsInRangeChecks='true'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-1", "CheckStatusFlagsInRangeChecks='false'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-2", "CheckStatusFlagsInRangeChecks='1'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-3", "CheckStatusFlagsInRangeChecks='0'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-4", "CheckStatusFlagsInRangeChecks='TRUE'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-5", "CheckStatusFlagsInRangeChecks='t'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-6", "CheckStatusFlagsInRangeChecks='yes'", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-7", "CheckStatusFlagsInRangeChecks=''", platform);
                Check(plugin, "CheckStatusFlagsInRangeChecks-8", "CheckStatusFlagsInRangeChecks=' true'", platform);
                Check(plugin, "ProjectileSelf-0", "ProjectileSelf='true'", platform);
                Check(plugin, "ProjectileSelf-1", "ProjectileSelf='false'", platform);
                Check(plugin, "ProjectileSelf-2", "ProjectileSelf='1'", platform);
                Check(plugin, "ProjectileSelf-3", "ProjectileSelf='0'", platform);
                Check(plugin, "ProjectileSelf-4", "ProjectileSelf='TRUE'", platform);
                Check(plugin, "ProjectileSelf-5", "ProjectileSelf='t'", platform);
                Check(plugin, "ProjectileSelf-6", "ProjectileSelf='yes'", platform);
                Check(plugin, "ProjectileSelf-7", "ProjectileSelf=''", platform);
                Check(plugin, "ProjectileSelf-8", "ProjectileSelf=' true'", platform);
                Check(plugin, "MeleeWeapon-0", "MeleeWeapon='true'", platform);
                Check(plugin, "MeleeWeapon-1", "MeleeWeapon='false'", platform);
                Check(plugin, "MeleeWeapon-2", "MeleeWeapon='1'", platform);
                Check(plugin, "MeleeWeapon-3", "MeleeWeapon='0'", platform);
                Check(plugin, "MeleeWeapon-4", "MeleeWeapon='TRUE'", platform);
                Check(plugin, "MeleeWeapon-5", "MeleeWeapon='t'", platform);
                Check(plugin, "MeleeWeapon-6", "MeleeWeapon='yes'", platform);
                Check(plugin, "MeleeWeapon-7", "MeleeWeapon=''", platform);
                Check(plugin, "MeleeWeapon-8", "MeleeWeapon=' true'", platform);
                Check(plugin, "ChaseWeapon-0", "ChaseWeapon='true'", platform);
                Check(plugin, "ChaseWeapon-1", "ChaseWeapon='false'", platform);
                Check(plugin, "ChaseWeapon-2", "ChaseWeapon='1'", platform);
                Check(plugin, "ChaseWeapon-3", "ChaseWeapon='0'", platform);
                Check(plugin, "ChaseWeapon-4", "ChaseWeapon='TRUE'", platform);
                Check(plugin, "ChaseWeapon-5", "ChaseWeapon='t'", platform);
                Check(plugin, "ChaseWeapon-6", "ChaseWeapon='yes'", platform);
                Check(plugin, "ChaseWeapon-7", "ChaseWeapon=''", platform);
                Check(plugin, "ChaseWeapon-8", "ChaseWeapon=' true'", platform);
                Check(plugin, "LeechRangeWeapon-0", "LeechRangeWeapon='true'", platform);
                Check(plugin, "LeechRangeWeapon-1", "LeechRangeWeapon='false'", platform);
                Check(plugin, "LeechRangeWeapon-2", "LeechRangeWeapon='1'", platform);
                Check(plugin, "LeechRangeWeapon-3", "LeechRangeWeapon='0'", platform);
                Check(plugin, "LeechRangeWeapon-4", "LeechRangeWeapon='TRUE'", platform);
                Check(plugin, "LeechRangeWeapon-5", "LeechRangeWeapon='t'", platform);
                Check(plugin, "LeechRangeWeapon-6", "LeechRangeWeapon='yes'", platform);
                Check(plugin, "LeechRangeWeapon-7", "LeechRangeWeapon=''", platform);
                Check(plugin, "LeechRangeWeapon-8", "LeechRangeWeapon=' true'", platform);
                Check(plugin, "HitStoredTarget-0", "HitStoredTarget='true'", platform);
                Check(plugin, "HitStoredTarget-1", "HitStoredTarget='false'", platform);
                Check(plugin, "HitStoredTarget-2", "HitStoredTarget='1'", platform);
                Check(plugin, "HitStoredTarget-3", "HitStoredTarget='0'", platform);
                Check(plugin, "HitStoredTarget-4", "HitStoredTarget='TRUE'", platform);
                Check(plugin, "HitStoredTarget-5", "HitStoredTarget='t'", platform);
                Check(plugin, "HitStoredTarget-6", "HitStoredTarget='yes'", platform);
                Check(plugin, "HitStoredTarget-7", "HitStoredTarget=''", platform);
                Check(plugin, "HitStoredTarget-8", "HitStoredTarget=' true'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-0", "CapableOfFollowingWaypoints='true'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-1", "CapableOfFollowingWaypoints='false'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-2", "CapableOfFollowingWaypoints='1'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-3", "CapableOfFollowingWaypoints='0'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-4", "CapableOfFollowingWaypoints='TRUE'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-5", "CapableOfFollowingWaypoints='t'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-6", "CapableOfFollowingWaypoints='yes'", platform);
                Check(plugin, "CapableOfFollowingWaypoints-7", "CapableOfFollowingWaypoints=''", platform);
                Check(plugin, "CapableOfFollowingWaypoints-8", "CapableOfFollowingWaypoints=' true'", platform);
                Check(plugin, "ShowsAmmoPips-0", "ShowsAmmoPips='true'", platform);
                Check(plugin, "ShowsAmmoPips-1", "ShowsAmmoPips='false'", platform);
                Check(plugin, "ShowsAmmoPips-2", "ShowsAmmoPips='1'", platform);
                Check(plugin, "ShowsAmmoPips-3", "ShowsAmmoPips='0'", platform);
                Check(plugin, "ShowsAmmoPips-4", "ShowsAmmoPips='TRUE'", platform);
                Check(plugin, "ShowsAmmoPips-5", "ShowsAmmoPips='t'", platform);
                Check(plugin, "ShowsAmmoPips-6", "ShowsAmmoPips='yes'", platform);
                Check(plugin, "ShowsAmmoPips-7", "ShowsAmmoPips=''", platform);
                Check(plugin, "ShowsAmmoPips-8", "ShowsAmmoPips=' true'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-0", "AllowAttackGarrisonedBldgs='true'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-1", "AllowAttackGarrisonedBldgs='false'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-2", "AllowAttackGarrisonedBldgs='1'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-3", "AllowAttackGarrisonedBldgs='0'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-4", "AllowAttackGarrisonedBldgs='TRUE'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-5", "AllowAttackGarrisonedBldgs='t'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-6", "AllowAttackGarrisonedBldgs='yes'", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-7", "AllowAttackGarrisonedBldgs=''", platform);
                Check(plugin, "AllowAttackGarrisonedBldgs-8", "AllowAttackGarrisonedBldgs=' true'", platform);
                Check(plugin, "PlayFXWhenStealthed-0", "PlayFXWhenStealthed='true'", platform);
                Check(plugin, "PlayFXWhenStealthed-1", "PlayFXWhenStealthed='false'", platform);
                Check(plugin, "PlayFXWhenStealthed-2", "PlayFXWhenStealthed='1'", platform);
                Check(plugin, "PlayFXWhenStealthed-3", "PlayFXWhenStealthed='0'", platform);
                Check(plugin, "PlayFXWhenStealthed-4", "PlayFXWhenStealthed='TRUE'", platform);
                Check(plugin, "PlayFXWhenStealthed-5", "PlayFXWhenStealthed='t'", platform);
                Check(plugin, "PlayFXWhenStealthed-6", "PlayFXWhenStealthed='yes'", platform);
                Check(plugin, "PlayFXWhenStealthed-7", "PlayFXWhenStealthed=''", platform);
                Check(plugin, "PlayFXWhenStealthed-8", "PlayFXWhenStealthed=' true'", platform);
                Check(plugin, "ContinueAttackRange-0", "ContinueAttackRange='-0'", platform);
                Check(plugin, "ContinueAttackRange-1", "ContinueAttackRange='-23.75'", platform);
                Check(plugin, "ContinueAttackRange-2", "ContinueAttackRange='33.3333333333'", platform);
                Check(plugin, "ContinueAttackRange-3", "ContinueAttackRange='2d1'", platform);
                Check(plugin, "ContinueAttackRange-4", "ContinueAttackRange='1e-40'", platform);
                Check(plugin, "ContinueAttackRange-5", "ContinueAttackRange='1e40'", platform);
                Check(plugin, "ContinueAttackRange-6", "ContinueAttackRange='0x1p2'", platform);
                Check(plugin, "ContinueAttackRange-7", "ContinueAttackRange='NaN'", platform);
                Check(plugin, "ContinueAttackRange-8", "ContinueAttackRange=''", platform);
                Check(plugin, "ContinueAttackRange-9", "ContinueAttackRange='garbage'", platform);
                Check(plugin, "SuspendFXDelaySeconds-0", "SuspendFXDelaySeconds='1500ms'", platform);
                Check(plugin, "SuspendFXDelaySeconds-1", "SuspendFXDelaySeconds='-2s'", platform);
                Check(plugin, "SuspendFXDelaySeconds-2", "SuspendFXDelaySeconds='2d1s'", platform);
                Check(plugin, "SuspendFXDelaySeconds-3", "SuspendFXDelaySeconds='1e-7'", platform);
                Check(plugin, "SuspendFXDelaySeconds-4", "SuspendFXDelaySeconds='1ms'", platform);
                Check(plugin, "SuspendFXDelaySeconds-5", "SuspendFXDelaySeconds=''", platform);
                Check(plugin, "SuspendFXDelaySeconds-6", "SuspendFXDelaySeconds='garbage'", platform);
                Check(plugin, "IsAimingWeapon-0", "IsAimingWeapon='true'", platform);
                Check(plugin, "IsAimingWeapon-1", "IsAimingWeapon='false'", platform);
                Check(plugin, "IsAimingWeapon-2", "IsAimingWeapon='1'", platform);
                Check(plugin, "IsAimingWeapon-3", "IsAimingWeapon='0'", platform);
                Check(plugin, "IsAimingWeapon-4", "IsAimingWeapon='TRUE'", platform);
                Check(plugin, "IsAimingWeapon-5", "IsAimingWeapon='t'", platform);
                Check(plugin, "IsAimingWeapon-6", "IsAimingWeapon='yes'", platform);
                Check(plugin, "IsAimingWeapon-7", "IsAimingWeapon=''", platform);
                Check(plugin, "IsAimingWeapon-8", "IsAimingWeapon=' true'", platform);
                Check(plugin, "NoVictimNeeded-0", "NoVictimNeeded='true'", platform);
                Check(plugin, "NoVictimNeeded-1", "NoVictimNeeded='false'", platform);
                Check(plugin, "NoVictimNeeded-2", "NoVictimNeeded='1'", platform);
                Check(plugin, "NoVictimNeeded-3", "NoVictimNeeded='0'", platform);
                Check(plugin, "NoVictimNeeded-4", "NoVictimNeeded='TRUE'", platform);
                Check(plugin, "NoVictimNeeded-5", "NoVictimNeeded='t'", platform);
                Check(plugin, "NoVictimNeeded-6", "NoVictimNeeded='yes'", platform);
                Check(plugin, "NoVictimNeeded-7", "NoVictimNeeded=''", platform);
                Check(plugin, "NoVictimNeeded-8", "NoVictimNeeded=' true'", platform);
                Check(plugin, "HitPercentage-0", "HitPercentage='-0'", platform);
                Check(plugin, "HitPercentage-1", "HitPercentage='-23.75'", platform);
                Check(plugin, "HitPercentage-2", "HitPercentage='33.3333333333'", platform);
                Check(plugin, "HitPercentage-3", "HitPercentage='2d1'", platform);
                Check(plugin, "HitPercentage-4", "HitPercentage='1e-40'", platform);
                Check(plugin, "HitPercentage-5", "HitPercentage='1e40'", platform);
                Check(plugin, "HitPercentage-6", "HitPercentage='0x1p2'", platform);
                Check(plugin, "HitPercentage-7", "HitPercentage='NaN'", platform);
                Check(plugin, "HitPercentage-8", "HitPercentage=''", platform);
                Check(plugin, "HitPercentage-9", "HitPercentage='garbage'", platform);
                Check(plugin, "HitPassengerPercentage-0", "HitPassengerPercentage='-0'", platform);
                Check(plugin, "HitPassengerPercentage-1", "HitPassengerPercentage='-23.75'", platform);
                Check(plugin, "HitPassengerPercentage-2", "HitPassengerPercentage='33.3333333333'", platform);
                Check(plugin, "HitPassengerPercentage-3", "HitPassengerPercentage='2d1'", platform);
                Check(plugin, "HitPassengerPercentage-4", "HitPassengerPercentage='1e-40'", platform);
                Check(plugin, "HitPassengerPercentage-5", "HitPassengerPercentage='1e40'", platform);
                Check(plugin, "HitPassengerPercentage-6", "HitPassengerPercentage='0x1p2'", platform);
                Check(plugin, "HitPassengerPercentage-7", "HitPassengerPercentage='NaN'", platform);
                Check(plugin, "HitPassengerPercentage-8", "HitPassengerPercentage=''", platform);
                Check(plugin, "HitPassengerPercentage-9", "HitPassengerPercentage='garbage'", platform);
                Check(plugin, "PassengerProportionalAttack-0", "PassengerProportionalAttack='true'", platform);
                Check(plugin, "PassengerProportionalAttack-1", "PassengerProportionalAttack='false'", platform);
                Check(plugin, "PassengerProportionalAttack-2", "PassengerProportionalAttack='1'", platform);
                Check(plugin, "PassengerProportionalAttack-3", "PassengerProportionalAttack='0'", platform);
                Check(plugin, "PassengerProportionalAttack-4", "PassengerProportionalAttack='TRUE'", platform);
                Check(plugin, "PassengerProportionalAttack-5", "PassengerProportionalAttack='t'", platform);
                Check(plugin, "PassengerProportionalAttack-6", "PassengerProportionalAttack='yes'", platform);
                Check(plugin, "PassengerProportionalAttack-7", "PassengerProportionalAttack=''", platform);
                Check(plugin, "PassengerProportionalAttack-8", "PassengerProportionalAttack=' true'", platform);
                Check(plugin, "HealthProportionalResolution-0", "HealthProportionalResolution='-0'", platform);
                Check(plugin, "HealthProportionalResolution-1", "HealthProportionalResolution='-23.75'", platform);
                Check(plugin, "HealthProportionalResolution-2", "HealthProportionalResolution='33.3333333333'", platform);
                Check(plugin, "HealthProportionalResolution-3", "HealthProportionalResolution='2d1'", platform);
                Check(plugin, "HealthProportionalResolution-4", "HealthProportionalResolution='1e-40'", platform);
                Check(plugin, "HealthProportionalResolution-5", "HealthProportionalResolution='1e40'", platform);
                Check(plugin, "HealthProportionalResolution-6", "HealthProportionalResolution='0x1p2'", platform);
                Check(plugin, "HealthProportionalResolution-7", "HealthProportionalResolution='NaN'", platform);
                Check(plugin, "HealthProportionalResolution-8", "HealthProportionalResolution=''", platform);
                Check(plugin, "HealthProportionalResolution-9", "HealthProportionalResolution='garbage'", platform);
                Check(plugin, "MaxAttackPassengers-0", "MaxAttackPassengers='-1'", platform);
                Check(plugin, "MaxAttackPassengers-1", "MaxAttackPassengers='0'", platform);
                Check(plugin, "MaxAttackPassengers-2", "MaxAttackPassengers='2147483648'", platform);
                Check(plugin, "MaxAttackPassengers-3", "MaxAttackPassengers='-2147483649'", platform);
                Check(plugin, "MaxAttackPassengers-4", "MaxAttackPassengers='010'", platform);
                Check(plugin, "MaxAttackPassengers-5", "MaxAttackPassengers='0x2a'", platform);
                Check(plugin, "MaxAttackPassengers-6", "MaxAttackPassengers='garbage'", platform);
                Check(plugin, "MaxAttackPassengers-7", "MaxAttackPassengers=''", platform);
                Check(plugin, "FinishAttackOnceStarted-0", "FinishAttackOnceStarted='true'", platform);
                Check(plugin, "FinishAttackOnceStarted-1", "FinishAttackOnceStarted='false'", platform);
                Check(plugin, "FinishAttackOnceStarted-2", "FinishAttackOnceStarted='1'", platform);
                Check(plugin, "FinishAttackOnceStarted-3", "FinishAttackOnceStarted='0'", platform);
                Check(plugin, "FinishAttackOnceStarted-4", "FinishAttackOnceStarted='TRUE'", platform);
                Check(plugin, "FinishAttackOnceStarted-5", "FinishAttackOnceStarted='t'", platform);
                Check(plugin, "FinishAttackOnceStarted-6", "FinishAttackOnceStarted='yes'", platform);
                Check(plugin, "FinishAttackOnceStarted-7", "FinishAttackOnceStarted=''", platform);
                Check(plugin, "FinishAttackOnceStarted-8", "FinishAttackOnceStarted=' true'", platform);
                Check(plugin, "RestrictedHeightRange-0", "RestrictedHeightRange='-0'", platform);
                Check(plugin, "RestrictedHeightRange-1", "RestrictedHeightRange='-23.75'", platform);
                Check(plugin, "RestrictedHeightRange-2", "RestrictedHeightRange='33.3333333333'", platform);
                Check(plugin, "RestrictedHeightRange-3", "RestrictedHeightRange='2d1'", platform);
                Check(plugin, "RestrictedHeightRange-4", "RestrictedHeightRange='1e-40'", platform);
                Check(plugin, "RestrictedHeightRange-5", "RestrictedHeightRange='1e40'", platform);
                Check(plugin, "RestrictedHeightRange-6", "RestrictedHeightRange='0x1p2'", platform);
                Check(plugin, "RestrictedHeightRange-7", "RestrictedHeightRange='NaN'", platform);
                Check(plugin, "RestrictedHeightRange-8", "RestrictedHeightRange=''", platform);
                Check(plugin, "RestrictedHeightRange-9", "RestrictedHeightRange='garbage'", platform);
                Check(plugin, "CannotTargetCastleVictims-0", "CannotTargetCastleVictims='true'", platform);
                Check(plugin, "CannotTargetCastleVictims-1", "CannotTargetCastleVictims='false'", platform);
                Check(plugin, "CannotTargetCastleVictims-2", "CannotTargetCastleVictims='1'", platform);
                Check(plugin, "CannotTargetCastleVictims-3", "CannotTargetCastleVictims='0'", platform);
                Check(plugin, "CannotTargetCastleVictims-4", "CannotTargetCastleVictims='TRUE'", platform);
                Check(plugin, "CannotTargetCastleVictims-5", "CannotTargetCastleVictims='t'", platform);
                Check(plugin, "CannotTargetCastleVictims-6", "CannotTargetCastleVictims='yes'", platform);
                Check(plugin, "CannotTargetCastleVictims-7", "CannotTargetCastleVictims=''", platform);
                Check(plugin, "CannotTargetCastleVictims-8", "CannotTargetCastleVictims=' true'", platform);
                Check(plugin, "RequireFollowThru-0", "RequireFollowThru='true'", platform);
                Check(plugin, "RequireFollowThru-1", "RequireFollowThru='false'", platform);
                Check(plugin, "RequireFollowThru-2", "RequireFollowThru='1'", platform);
                Check(plugin, "RequireFollowThru-3", "RequireFollowThru='0'", platform);
                Check(plugin, "RequireFollowThru-4", "RequireFollowThru='TRUE'", platform);
                Check(plugin, "RequireFollowThru-5", "RequireFollowThru='t'", platform);
                Check(plugin, "RequireFollowThru-6", "RequireFollowThru='yes'", platform);
                Check(plugin, "RequireFollowThru-7", "RequireFollowThru=''", platform);
                Check(plugin, "RequireFollowThru-8", "RequireFollowThru=' true'", platform);
                Check(plugin, "ShareTimers-0", "ShareTimers='true'", platform);
                Check(plugin, "ShareTimers-1", "ShareTimers='false'", platform);
                Check(plugin, "ShareTimers-2", "ShareTimers='1'", platform);
                Check(plugin, "ShareTimers-3", "ShareTimers='0'", platform);
                Check(plugin, "ShareTimers-4", "ShareTimers='TRUE'", platform);
                Check(plugin, "ShareTimers-5", "ShareTimers='t'", platform);
                Check(plugin, "ShareTimers-6", "ShareTimers='yes'", platform);
                Check(plugin, "ShareTimers-7", "ShareTimers=''", platform);
                Check(plugin, "ShareTimers-8", "ShareTimers=' true'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-0", "ShouldPlayUnderAttackEvaEvent='true'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-1", "ShouldPlayUnderAttackEvaEvent='false'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-2", "ShouldPlayUnderAttackEvaEvent='1'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-3", "ShouldPlayUnderAttackEvaEvent='0'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-4", "ShouldPlayUnderAttackEvaEvent='TRUE'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-5", "ShouldPlayUnderAttackEvaEvent='t'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-6", "ShouldPlayUnderAttackEvaEvent='yes'", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-7", "ShouldPlayUnderAttackEvaEvent=''", platform);
                Check(plugin, "ShouldPlayUnderAttackEvaEvent-8", "ShouldPlayUnderAttackEvaEvent=' true'", platform);
                Check(plugin, "InstantLoadClipOnActivate-0", "InstantLoadClipOnActivate='true'", platform);
                Check(plugin, "InstantLoadClipOnActivate-1", "InstantLoadClipOnActivate='false'", platform);
                Check(plugin, "InstantLoadClipOnActivate-2", "InstantLoadClipOnActivate='1'", platform);
                Check(plugin, "InstantLoadClipOnActivate-3", "InstantLoadClipOnActivate='0'", platform);
                Check(plugin, "InstantLoadClipOnActivate-4", "InstantLoadClipOnActivate='TRUE'", platform);
                Check(plugin, "InstantLoadClipOnActivate-5", "InstantLoadClipOnActivate='t'", platform);
                Check(plugin, "InstantLoadClipOnActivate-6", "InstantLoadClipOnActivate='yes'", platform);
                Check(plugin, "InstantLoadClipOnActivate-7", "InstantLoadClipOnActivate=''", platform);
                Check(plugin, "InstantLoadClipOnActivate-8", "InstantLoadClipOnActivate=' true'", platform);
                Check(plugin, "Flags-0", "Flags='ATTACK_NEEDS_LINE_OF_SIGHT'", platform);
                Check(plugin, "Flags-1", "Flags='CRUSH_VEHICLE'", platform);
                Check(plugin, "Flags-2", "Flags='FORCE_EMPTY_ENTIRE_CLIP'", platform);
                Check(plugin, "Flags-3", "Flags='IGNORE_ENCLOSURE_CHECK'", platform);
                Check(plugin, "Flags-4", "Flags='IGNORE_TARGET_AS_OBSTACLE'", platform);
                Check(plugin, "Flags-5", "Flags='IGNORE_WALL_RELATIONSHIP'", platform);
                Check(plugin, "Flags-6", "Flags='LENGTH_SCATTER'", platform);
                Check(plugin, "Flags-7", "Flags='NONE'", platform);
                Check(plugin, "Flags-8", "Flags='NOT_ATTRACTED_BY_MAGNETS'", platform);
                Check(plugin, "Flags-9", "Flags='ORTHOGONAL_SCATTER'", platform);
                Check(plugin, "Flags-10", "Flags='RELOAD_WHEN_ATTACK_STOPS'", platform);
                Check(plugin, "Flags-11", "Flags='SYNC_AMMO_ON_ACTIVATE'", platform);
                Check(plugin, "Flags-12", "Flags=''", platform);
                Check(plugin, "Flags-13", "Flags='unknown'", platform);
                Check(plugin, "Flags-14", "Flags='attack_needs_line_of_sight'", platform);
                Check(plugin, "Flags-15", "Flags='ALL'", platform);
                Check(plugin, "Flags-16", "Flags='all'", platform);
                Check(plugin, "Flags-17", "Flags='-ALL'", platform);
                Check(plugin, "Flags-18", "Flags='ALL -ATTACK_NEEDS_LINE_OF_SIGHT'", platform);
                Check(plugin, "Flags-19", "Flags='-ATTACK_NEEDS_LINE_OF_SIGHT'", platform);
                Check(plugin, "Flags-20", "Flags='ATTACK_NEEDS_LINE_OF_SIGHT -ATTACK_NEEDS_LINE_OF_SIGHT -ATTACK_NEEDS_LINE_OF_SIGHT'", platform);
                Check(plugin, "Flags-21", "Flags='ATTACK_NEEDS_LINE_OF_SIGHT\t+CRUSH_VEHICLE\t+FORCE_EMPTY_ENTIRE_CLIP\t+IGNORE_ENCLOSURE_CHECK\t+IGNORE_TARGET_AS_OBSTACLE\t+IGNORE_WALL_RELATIONSHIP\t+LENGTH_SCATTER\t+NONE\t+NOT_ATTRACTED_BY_MAGNETS\t+ORTHOGONAL_SCATTER\t+RELOAD_WHEN_ATTACK_STOPS\t+SYNC_AMMO_ON_ACTIVATE'", platform);
                Check(plugin, "Flags-22", "Flags='ATTACK_NEEDS_LINE_OF_SIGHT,SYNC_AMMO_ON_ACTIVATE'", platform);
                Check(plugin, "LockWhenUsing-0", "LockWhenUsing='true'", platform);
                Check(plugin, "LockWhenUsing-1", "LockWhenUsing='false'", platform);
                Check(plugin, "LockWhenUsing-2", "LockWhenUsing='1'", platform);
                Check(plugin, "LockWhenUsing-3", "LockWhenUsing='0'", platform);
                Check(plugin, "LockWhenUsing-4", "LockWhenUsing='TRUE'", platform);
                Check(plugin, "LockWhenUsing-5", "LockWhenUsing='t'", platform);
                Check(plugin, "LockWhenUsing-6", "LockWhenUsing='yes'", platform);
                Check(plugin, "LockWhenUsing-7", "LockWhenUsing=''", platform);
                Check(plugin, "LockWhenUsing-8", "LockWhenUsing=' true'", platform);
                Check(plugin, "BombardType-0", "BombardType='true'", platform);
                Check(plugin, "BombardType-1", "BombardType='false'", platform);
                Check(plugin, "BombardType-2", "BombardType='1'", platform);
                Check(plugin, "BombardType-3", "BombardType='0'", platform);
                Check(plugin, "BombardType-4", "BombardType='TRUE'", platform);
                Check(plugin, "BombardType-5", "BombardType='t'", platform);
                Check(plugin, "BombardType-6", "BombardType='yes'", platform);
                Check(plugin, "BombardType-7", "BombardType=''", platform);
                Check(plugin, "BombardType-8", "BombardType=' true'", platform);
                Check(plugin, "UseInnateAttributes-0", "UseInnateAttributes='true'", platform);
                Check(plugin, "UseInnateAttributes-1", "UseInnateAttributes='false'", platform);
                Check(plugin, "UseInnateAttributes-2", "UseInnateAttributes='1'", platform);
                Check(plugin, "UseInnateAttributes-3", "UseInnateAttributes='0'", platform);
                Check(plugin, "UseInnateAttributes-4", "UseInnateAttributes='TRUE'", platform);
                Check(plugin, "UseInnateAttributes-5", "UseInnateAttributes='t'", platform);
                Check(plugin, "UseInnateAttributes-6", "UseInnateAttributes='yes'", platform);
                Check(plugin, "UseInnateAttributes-7", "UseInnateAttributes=''", platform);
                Check(plugin, "UseInnateAttributes-8", "UseInnateAttributes=' true'", platform);
                Check(plugin, "PreAttackType-0", "PreAttackType='PER_BURST'", platform);
                Check(plugin, "PreAttackType-1", "PreAttackType='PER_CLIP'", platform);
                Check(plugin, "PreAttackType-2", "PreAttackType='PER_POSITION'", platform);
                Check(plugin, "PreAttackType-3", "PreAttackType='PER_SHOT'", platform);
                Check(plugin, "PreAttackType-4", "PreAttackType='PER_TARGET'", platform);
                Check(plugin, "PreAttackType-5", "PreAttackType='unknown'", platform);
                Check(plugin, "PreAttackType-6", "PreAttackType=''", platform);
                Check(plugin, "PreAttackType-7", "PreAttackType=' PER_BURST'", platform);
                Check(plugin, "PreAttackType-8", "PreAttackType='per_burst'", platform);
                Check(plugin, "ReAcquireDetailType-0", "ReAcquireDetailType='PER_ATTACK'", platform);
                Check(plugin, "ReAcquireDetailType-1", "ReAcquireDetailType='PER_CLIP'", platform);
                Check(plugin, "ReAcquireDetailType-2", "ReAcquireDetailType='PER_SHOT'", platform);
                Check(plugin, "ReAcquireDetailType-3", "ReAcquireDetailType='POST_FIRE'", platform);
                Check(plugin, "ReAcquireDetailType-4", "ReAcquireDetailType='PRE_FIRE'", platform);
                Check(plugin, "ReAcquireDetailType-5", "ReAcquireDetailType='PRE_SHOT'", platform);
                Check(plugin, "ReAcquireDetailType-6", "ReAcquireDetailType='unknown'", platform);
                Check(plugin, "ReAcquireDetailType-7", "ReAcquireDetailType=''", platform);
                Check(plugin, "ReAcquireDetailType-8", "ReAcquireDetailType=' PER_ATTACK'", platform);
                Check(plugin, "ReAcquireDetailType-9", "ReAcquireDetailType='per_attack'", platform);
                Check(plugin, "AutoReloadsClip-0", "AutoReloadsClip='AUTO'", platform);
                Check(plugin, "AutoReloadsClip-1", "AutoReloadsClip='NONE'", platform);
                Check(plugin, "AutoReloadsClip-2", "AutoReloadsClip='RETURN_TO_BASE'", platform);
                Check(plugin, "AutoReloadsClip-3", "AutoReloadsClip='unknown'", platform);
                Check(plugin, "AutoReloadsClip-4", "AutoReloadsClip=''", platform);
                Check(plugin, "AutoReloadsClip-5", "AutoReloadsClip=' AUTO'", platform);
                Check(plugin, "AutoReloadsClip-6", "AutoReloadsClip='auto'", platform);
                Check(plugin, "RadiusDamageAffects-0", "RadiusDamageAffects='ALLIES'", platform);
                Check(plugin, "RadiusDamageAffects-1", "RadiusDamageAffects='ENEMIES'", platform);
                Check(plugin, "RadiusDamageAffects-2", "RadiusDamageAffects='MINES'", platform);
                Check(plugin, "RadiusDamageAffects-3", "RadiusDamageAffects='NEUTRALS'", platform);
                Check(plugin, "RadiusDamageAffects-4", "RadiusDamageAffects='NOT_AIRBORNE'", platform);
                Check(plugin, "RadiusDamageAffects-5", "RadiusDamageAffects='NOT_SIMILAR'", platform);
                Check(plugin, "RadiusDamageAffects-6", "RadiusDamageAffects='PROJECTILES'", platform);
                Check(plugin, "RadiusDamageAffects-7", "RadiusDamageAffects='SAME_HEIGHT_ONLY'", platform);
                Check(plugin, "RadiusDamageAffects-8", "RadiusDamageAffects='SELF'", platform);
                Check(plugin, "RadiusDamageAffects-9", "RadiusDamageAffects='SUICIDE'", platform);
                Check(plugin, "RadiusDamageAffects-10", "RadiusDamageAffects=''", platform);
                Check(plugin, "RadiusDamageAffects-11", "RadiusDamageAffects='unknown'", platform);
                Check(plugin, "RadiusDamageAffects-12", "RadiusDamageAffects='allies'", platform);
                Check(plugin, "RadiusDamageAffects-13", "RadiusDamageAffects='ALL'", platform);
                Check(plugin, "RadiusDamageAffects-14", "RadiusDamageAffects='all'", platform);
                Check(plugin, "RadiusDamageAffects-15", "RadiusDamageAffects='-ALL'", platform);
                Check(plugin, "RadiusDamageAffects-16", "RadiusDamageAffects='ALL -ALLIES'", platform);
                Check(plugin, "RadiusDamageAffects-17", "RadiusDamageAffects='-ALLIES'", platform);
                Check(plugin, "RadiusDamageAffects-18", "RadiusDamageAffects='ALLIES -ALLIES -ALLIES'", platform);
                Check(plugin, "RadiusDamageAffects-19", "RadiusDamageAffects='ALLIES\t+ENEMIES\t+MINES\t+NEUTRALS\t+NOT_AIRBORNE\t+NOT_SIMILAR\t+PROJECTILES\t+SAME_HEIGHT_ONLY\t+SELF\t+SUICIDE'", platform);
                Check(plugin, "RadiusDamageAffects-20", "RadiusDamageAffects='ALLIES,SUICIDE'", platform);
                Check(plugin, "FXTrigger-0", "FXTrigger='CATAPULT_ROCK'", platform);
                Check(plugin, "FXTrigger-1", "FXTrigger='NONE'", platform);
                Check(plugin, "FXTrigger-2", "FXTrigger='TREBUCHET_ROCK'", platform);
                Check(plugin, "FXTrigger-3", "FXTrigger='unknown'", platform);
                Check(plugin, "FXTrigger-4", "FXTrigger=''", platform);
                Check(plugin, "FXTrigger-5", "FXTrigger=' CATAPULT_ROCK'", platform);
                Check(plugin, "FXTrigger-6", "FXTrigger='catapult_rock'", platform);
                Check(plugin, "ProjectileCollidesWith-0", "ProjectileCollidesWith='ALLIES'", platform);
                Check(plugin, "ProjectileCollidesWith-1", "ProjectileCollidesWith='BALLISTIC_MISSILES'", platform);
                Check(plugin, "ProjectileCollidesWith-2", "ProjectileCollidesWith='CONTROLLED_STRUCTURES'", platform);
                Check(plugin, "ProjectileCollidesWith-3", "ProjectileCollidesWith='ENEMIES'", platform);
                Check(plugin, "ProjectileCollidesWith-4", "ProjectileCollidesWith='MONSTERS'", platform);
                Check(plugin, "ProjectileCollidesWith-5", "ProjectileCollidesWith='NEUTRAL'", platform);
                Check(plugin, "ProjectileCollidesWith-6", "ProjectileCollidesWith='PROJECTILES'", platform);
                Check(plugin, "ProjectileCollidesWith-7", "ProjectileCollidesWith='SHRUBBERY'", platform);
                Check(plugin, "ProjectileCollidesWith-8", "ProjectileCollidesWith='SMALL_MISSILES'", platform);
                Check(plugin, "ProjectileCollidesWith-9", "ProjectileCollidesWith='STRUCTURES'", platform);
                Check(plugin, "ProjectileCollidesWith-10", "ProjectileCollidesWith='WALLS'", platform);
                Check(plugin, "ProjectileCollidesWith-11", "ProjectileCollidesWith=''", platform);
                Check(plugin, "ProjectileCollidesWith-12", "ProjectileCollidesWith='unknown'", platform);
                Check(plugin, "ProjectileCollidesWith-13", "ProjectileCollidesWith='allies'", platform);
                Check(plugin, "ProjectileCollidesWith-14", "ProjectileCollidesWith='ALL'", platform);
                Check(plugin, "ProjectileCollidesWith-15", "ProjectileCollidesWith='all'", platform);
                Check(plugin, "ProjectileCollidesWith-16", "ProjectileCollidesWith='-ALL'", platform);
                Check(plugin, "ProjectileCollidesWith-17", "ProjectileCollidesWith='ALL -ALLIES'", platform);
                Check(plugin, "ProjectileCollidesWith-18", "ProjectileCollidesWith='-ALLIES'", platform);
                Check(plugin, "ProjectileCollidesWith-19", "ProjectileCollidesWith='ALLIES -ALLIES -ALLIES'", platform);
                Check(plugin, "ProjectileCollidesWith-20", "ProjectileCollidesWith='ALLIES\t+BALLISTIC_MISSILES\t+CONTROLLED_STRUCTURES\t+ENEMIES\t+MONSTERS\t+NEUTRAL\t+PROJECTILES\t+SHRUBBERY\t+SMALL_MISSILES\t+STRUCTURES\t+WALLS'", platform);
                Check(plugin, "ProjectileCollidesWith-21", "ProjectileCollidesWith='ALLIES,WALLS'", platform);
                Check(plugin, "RequiredAntiMask-0", "RequiredAntiMask='ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "RequiredAntiMask-1", "RequiredAntiMask='ANTI_AIRBORNE_VEHICLE'", platform);
                Check(plugin, "RequiredAntiMask-2", "RequiredAntiMask='ANTI_BALLISTIC_MISSILE'", platform);
                Check(plugin, "RequiredAntiMask-3", "RequiredAntiMask='ANTI_GROUND'", platform);
                Check(plugin, "RequiredAntiMask-4", "RequiredAntiMask='ANTI_INFANTRY'", platform);
                Check(plugin, "RequiredAntiMask-5", "RequiredAntiMask='ANTI_LIFTED_GROUND_UNIT'", platform);
                Check(plugin, "RequiredAntiMask-6", "RequiredAntiMask='ANTI_MINE'", platform);
                Check(plugin, "RequiredAntiMask-7", "RequiredAntiMask='ANTI_PARACHUTE'", platform);
                Check(plugin, "RequiredAntiMask-8", "RequiredAntiMask='ANTI_PROJECTILE'", platform);
                Check(plugin, "RequiredAntiMask-9", "RequiredAntiMask='ANTI_SMALL_MISSILE'", platform);
                Check(plugin, "RequiredAntiMask-10", "RequiredAntiMask='ANTI_STRUCTURE'", platform);
                Check(plugin, "RequiredAntiMask-11", "RequiredAntiMask='ANTI_SUBMERGED'", platform);
                Check(plugin, "RequiredAntiMask-12", "RequiredAntiMask='ANTI_VEHICLE'", platform);
                Check(plugin, "RequiredAntiMask-13", "RequiredAntiMask='ANTI_WATER'", platform);
                Check(plugin, "RequiredAntiMask-14", "RequiredAntiMask=''", platform);
                Check(plugin, "RequiredAntiMask-15", "RequiredAntiMask='unknown'", platform);
                Check(plugin, "RequiredAntiMask-16", "RequiredAntiMask='anti_airborne_infantry'", platform);
                Check(plugin, "RequiredAntiMask-17", "RequiredAntiMask='ALL'", platform);
                Check(plugin, "RequiredAntiMask-18", "RequiredAntiMask='all'", platform);
                Check(plugin, "RequiredAntiMask-19", "RequiredAntiMask='-ALL'", platform);
                Check(plugin, "RequiredAntiMask-20", "RequiredAntiMask='ALL -ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "RequiredAntiMask-21", "RequiredAntiMask='-ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "RequiredAntiMask-22", "RequiredAntiMask='ANTI_AIRBORNE_INFANTRY -ANTI_AIRBORNE_INFANTRY -ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "RequiredAntiMask-23", "RequiredAntiMask='ANTI_AIRBORNE_INFANTRY\t+ANTI_AIRBORNE_VEHICLE\t+ANTI_BALLISTIC_MISSILE\t+ANTI_GROUND\t+ANTI_INFANTRY\t+ANTI_LIFTED_GROUND_UNIT\t+ANTI_MINE\t+ANTI_PARACHUTE\t+ANTI_PROJECTILE\t+ANTI_SMALL_MISSILE\t+ANTI_STRUCTURE\t+ANTI_SUBMERGED\t+ANTI_VEHICLE\t+ANTI_WATER'", platform);
                Check(plugin, "RequiredAntiMask-24", "RequiredAntiMask='ANTI_AIRBORNE_INFANTRY,ANTI_WATER'", platform);
                Check(plugin, "ForbiddenAntiMask-0", "ForbiddenAntiMask='ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "ForbiddenAntiMask-1", "ForbiddenAntiMask='ANTI_AIRBORNE_VEHICLE'", platform);
                Check(plugin, "ForbiddenAntiMask-2", "ForbiddenAntiMask='ANTI_BALLISTIC_MISSILE'", platform);
                Check(plugin, "ForbiddenAntiMask-3", "ForbiddenAntiMask='ANTI_GROUND'", platform);
                Check(plugin, "ForbiddenAntiMask-4", "ForbiddenAntiMask='ANTI_INFANTRY'", platform);
                Check(plugin, "ForbiddenAntiMask-5", "ForbiddenAntiMask='ANTI_LIFTED_GROUND_UNIT'", platform);
                Check(plugin, "ForbiddenAntiMask-6", "ForbiddenAntiMask='ANTI_MINE'", platform);
                Check(plugin, "ForbiddenAntiMask-7", "ForbiddenAntiMask='ANTI_PARACHUTE'", platform);
                Check(plugin, "ForbiddenAntiMask-8", "ForbiddenAntiMask='ANTI_PROJECTILE'", platform);
                Check(plugin, "ForbiddenAntiMask-9", "ForbiddenAntiMask='ANTI_SMALL_MISSILE'", platform);
                Check(plugin, "ForbiddenAntiMask-10", "ForbiddenAntiMask='ANTI_STRUCTURE'", platform);
                Check(plugin, "ForbiddenAntiMask-11", "ForbiddenAntiMask='ANTI_SUBMERGED'", platform);
                Check(plugin, "ForbiddenAntiMask-12", "ForbiddenAntiMask='ANTI_VEHICLE'", platform);
                Check(plugin, "ForbiddenAntiMask-13", "ForbiddenAntiMask='ANTI_WATER'", platform);
                Check(plugin, "ForbiddenAntiMask-14", "ForbiddenAntiMask=''", platform);
                Check(plugin, "ForbiddenAntiMask-15", "ForbiddenAntiMask='unknown'", platform);
                Check(plugin, "ForbiddenAntiMask-16", "ForbiddenAntiMask='anti_airborne_infantry'", platform);
                Check(plugin, "ForbiddenAntiMask-17", "ForbiddenAntiMask='ALL'", platform);
                Check(plugin, "ForbiddenAntiMask-18", "ForbiddenAntiMask='all'", platform);
                Check(plugin, "ForbiddenAntiMask-19", "ForbiddenAntiMask='-ALL'", platform);
                Check(plugin, "ForbiddenAntiMask-20", "ForbiddenAntiMask='ALL -ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "ForbiddenAntiMask-21", "ForbiddenAntiMask='-ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "ForbiddenAntiMask-22", "ForbiddenAntiMask='ANTI_AIRBORNE_INFANTRY -ANTI_AIRBORNE_INFANTRY -ANTI_AIRBORNE_INFANTRY'", platform);
                Check(plugin, "ForbiddenAntiMask-23", "ForbiddenAntiMask='ANTI_AIRBORNE_INFANTRY\t+ANTI_AIRBORNE_VEHICLE\t+ANTI_BALLISTIC_MISSILE\t+ANTI_GROUND\t+ANTI_INFANTRY\t+ANTI_LIFTED_GROUND_UNIT\t+ANTI_MINE\t+ANTI_PARACHUTE\t+ANTI_PROJECTILE\t+ANTI_SMALL_MISSILE\t+ANTI_STRUCTURE\t+ANTI_SUBMERGED\t+ANTI_VEHICLE\t+ANTI_WATER'", platform);
                Check(plugin, "ForbiddenAntiMask-24", "ForbiddenAntiMask='ANTI_AIRBORNE_INFANTRY,ANTI_WATER'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-0", "StopFiringOnCanBeInvisible='true'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-1", "StopFiringOnCanBeInvisible='false'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-2", "StopFiringOnCanBeInvisible='1'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-3", "StopFiringOnCanBeInvisible='0'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-4", "StopFiringOnCanBeInvisible='TRUE'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-5", "StopFiringOnCanBeInvisible='t'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-6", "StopFiringOnCanBeInvisible='yes'", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-7", "StopFiringOnCanBeInvisible=''", platform);
                Check(plugin, "StopFiringOnCanBeInvisible-8", "StopFiringOnCanBeInvisible=' true'", platform);
                Check(plugin, "ContactWeapon-0", "ContactWeapon='true'", platform);
                Check(plugin, "ContactWeapon-1", "ContactWeapon='false'", platform);
                Check(plugin, "ContactWeapon-2", "ContactWeapon='1'", platform);
                Check(plugin, "ContactWeapon-3", "ContactWeapon='0'", platform);
                Check(plugin, "ContactWeapon-4", "ContactWeapon='TRUE'", platform);
                Check(plugin, "ContactWeapon-5", "ContactWeapon='t'", platform);
                Check(plugin, "ContactWeapon-6", "ContactWeapon='yes'", platform);
                Check(plugin, "ContactWeapon-7", "ContactWeapon=''", platform);
                Check(plugin, "ContactWeapon-8", "ContactWeapon=' true'", platform);
                Check(plugin, "UseCenterForRangeCheck-0", "UseCenterForRangeCheck='true'", platform);
                Check(plugin, "UseCenterForRangeCheck-1", "UseCenterForRangeCheck='false'", platform);
                Check(plugin, "UseCenterForRangeCheck-2", "UseCenterForRangeCheck='1'", platform);
                Check(plugin, "UseCenterForRangeCheck-3", "UseCenterForRangeCheck='0'", platform);
                Check(plugin, "UseCenterForRangeCheck-4", "UseCenterForRangeCheck='TRUE'", platform);
                Check(plugin, "UseCenterForRangeCheck-5", "UseCenterForRangeCheck='t'", platform);
                Check(plugin, "UseCenterForRangeCheck-6", "UseCenterForRangeCheck='yes'", platform);
                Check(plugin, "UseCenterForRangeCheck-7", "UseCenterForRangeCheck=''", platform);
                Check(plugin, "UseCenterForRangeCheck-8", "UseCenterForRangeCheck=' true'", platform);
                Check(plugin, "VirtualDamage-0", "VirtualDamage='NONE'", platform);
                Check(plugin, "VirtualDamage-1", "VirtualDamage='SHARE'", platform);
                Check(plugin, "VirtualDamage-2", "VirtualDamage='SOLO'", platform);
                Check(plugin, "VirtualDamage-3", "VirtualDamage='unknown'", platform);
                Check(plugin, "VirtualDamage-4", "VirtualDamage=''", platform);
                Check(plugin, "VirtualDamage-5", "VirtualDamage=' NONE'", platform);
                Check(plugin, "VirtualDamage-6", "VirtualDamage='none'", platform);
                Check(plugin, "RevealShroudOnFire-0", "RevealShroudOnFire='true'", platform);
                Check(plugin, "RevealShroudOnFire-1", "RevealShroudOnFire='false'", platform);
                Check(plugin, "RevealShroudOnFire-2", "RevealShroudOnFire='1'", platform);
                Check(plugin, "RevealShroudOnFire-3", "RevealShroudOnFire='0'", platform);
                Check(plugin, "RevealShroudOnFire-4", "RevealShroudOnFire='TRUE'", platform);
                Check(plugin, "RevealShroudOnFire-5", "RevealShroudOnFire='t'", platform);
                Check(plugin, "RevealShroudOnFire-6", "RevealShroudOnFire='yes'", platform);
                Check(plugin, "RevealShroudOnFire-7", "RevealShroudOnFire=''", platform);
                Check(plugin, "RevealShroudOnFire-8", "RevealShroudOnFire=' true'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-0", "ShouldPlayTargetDeadEvaEvent='true'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-1", "ShouldPlayTargetDeadEvaEvent='false'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-2", "ShouldPlayTargetDeadEvaEvent='1'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-3", "ShouldPlayTargetDeadEvaEvent='0'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-4", "ShouldPlayTargetDeadEvaEvent='TRUE'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-5", "ShouldPlayTargetDeadEvaEvent='t'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-6", "ShouldPlayTargetDeadEvaEvent='yes'", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-7", "ShouldPlayTargetDeadEvaEvent=''", platform);
                Check(plugin, "ShouldPlayTargetDeadEvaEvent-8", "ShouldPlayTargetDeadEvaEvent=' true'", platform);
                Check(plugin, "mixed-fields", "AttackRange='-23.75' MinimumAttackRange='-23.75' RangeBonusMinHeight='-23.75' RangeBonus='-23.75' RangeBonusPerFoot='-23.75' RequestAssistRange='-23.75' AcceptableAimDelta='90d' AimDirection='90d' ScatterRadius='-23.75' ScatterLength='-23.75' ScatterIndependently='false' WeaponSpeed='-23.75' MinWeaponSpeed='-23.75' MaxWeaponSpeed='-23.75' ScaleWeaponSpeed='false' IgnoresContactPoints='false' ScaleAttackRangeByAmmoRemaining='false' CanBeDodged='false' IdleAfterFiringDelaySeconds='-2s' HoldAfterFiringDelaySeconds='-2s' HoldDuringReload='false' CanFireWhileMoving='false' WeaponRecoil='90d' MinTargetPitch='90d' MaxTargetPitch='90d' FiringLoopSoundContinuesDuringReload='false' ClipSize='0' ContinuousFireOne='0' ContinuousFireTwo='0' ContinuousFireCoastSeconds='-2s' AutoReloadWhenIdleSeconds='-2s' ShotsPerBarrel='0' DamageDealtAtSelfPosition='false' CheckStatusFlagsInRangeChecks='false' ProjectileSelf='false' MeleeWeapon='false' ChaseWeapon='false' LeechRangeWeapon='false' HitStoredTarget='false' CapableOfFollowingWaypoints='false' ShowsAmmoPips='false' AllowAttackGarrisonedBldgs='false' PlayFXWhenStealthed='false' ContinueAttackRange='-23.75' SuspendFXDelaySeconds='-2s' IsAimingWeapon='false' NoVictimNeeded='false' HitPercentage='-23.75' HitPassengerPercentage='-23.75' PassengerProportionalAttack='false' HealthProportionalResolution='-23.75' MaxAttackPassengers='0' FinishAttackOnceStarted='false' RestrictedHeightRange='-23.75' CannotTargetCastleVictims='false' RequireFollowThru='false' ShareTimers='false' ShouldPlayUnderAttackEvaEvent='false' InstantLoadClipOnActivate='false' Flags='CRUSH_VEHICLE' LockWhenUsing='false' BombardType='false' UseInnateAttributes='false' PreAttackType='PER_CLIP' ReAcquireDetailType='PER_CLIP' AutoReloadsClip='NONE' RadiusDamageAffects='ENEMIES' FXTrigger='NONE' ProjectileCollidesWith='BALLISTIC_MISSILES' RequiredAntiMask='ANTI_AIRBORNE_VEHICLE' ForbiddenAntiMask='ANTI_AIRBORNE_VEHICLE' StopFiringOnCanBeInvisible='false' ContactWeapon='false' UseCenterForRangeCheck='false' VirtualDamage='SHARE' RevealShroudOnFire='false' ShouldPlayTargetDeadEvaEvent='false'", platform);
                string[] durations = { "PreAttackDelay", "FiringDuration", "CoolDownDelayBetweenShots", "ClipReloadTime" };
                foreach (string duration in durations) {
                    foreach (string attrs in new[] { "", "MinSeconds='1s'", "MaxSeconds='1500ms'", "MinSeconds='-2s' MaxSeconds='-3s'", "MinSeconds='2d1s' MaxSeconds='1e-7s'", "MinSeconds='' MaxSeconds='garbage'", "MinSeconds='1e40' MaxSeconds='1e-40'" }) {
                        Test(plugin, duration + "/" + attrs, "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Oracle' AttackRange='42'><" + duration + " " + attrs + "/></WeaponTemplate>", platform, 1); ++fixtures;
                    }
                }
                Test(plugin, "all-durations", "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Oracle'><ClipReloadTime MinSeconds='4s'/><CoolDownDelayBetweenShots MinSeconds='3s'/><FiringDuration MinSeconds='2s'/><PreAttackDelay MinSeconds='1s'/></WeaponTemplate>", platform, 4); ++fixtures;
                Test(plugin, "first-duplicate-duration", "<WeaponTemplate xmlns='" + AssetNamespace + "' id='Oracle'><PreAttackDelay MinSeconds='1s'/><PreAttackDelay MinSeconds='9s'/></WeaponTemplate>", platform, 1); ++fixtures;

            }
            Reject("<WeaponTemplate xmlns=\"" + AssetNamespace + "\" id=\"Reject\"><PreAttackDelay Unknown=\"1\"/></WeaponTemplate>");
            Reject("<WeaponTemplate xmlns=\"" + AssetNamespace + "\" id=\"Reject\"><FiringDuration Unknown=\"1\"/></WeaponTemplate>");
            Reject("<WeaponTemplate xmlns=\"" + AssetNamespace + "\" id=\"Reject\"><CoolDownDelayBetweenShots Unknown=\"1\"/></WeaponTemplate>");
            Reject("<WeaponTemplate xmlns=\"" + AssetNamespace + "\" id=\"Reject\"><ClipReloadTime Unknown=\"1\"/></WeaponTemplate>");



            Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'><IncompatibleAttributeModifier Unknown='1'/></WeaponTemplate>");
            Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'><IncompatibleAttributeModifier><Unknown/></IncompatibleAttributeModifier></WeaponTemplate>");
            foreach(string body in new[] {"<ScatterRadiusVsType Unknown='1'/>","<ScatterRadiusVsType xmlns:x='urn:other' x:Radius='1'/>","<ScatterRadiusVsType><Unknown/></ScatterRadiusVsType>","<ScatterRadiusVsType><Filter Unknown='1'/></ScatterRadiusVsType>","<ScatterRadiusVsType><Filter><IncludeThing Unknown='1'/></Filter></ScatterRadiusVsType>","<ScatterRadiusVsType><Filter xmlns='other'/></ScatterRadiusVsType>"})
                Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'>"+body+"</WeaponTemplate>");
            Reject(Weapon("UnknownField='1'"));
            Reject("<WeaponTemplate xmlns='" + AssetNamespace + "' id='Reject'><Unknown/></WeaponTemplate>");
            foreach (string name in new[] { "OverrideVoiceAttackSound", "OverrideVoiceEnterStateAttackSound", "WeaponAiHintInfo" }) {
                Reject(WithChild(name, "Unknown='1'"));
                Reject(WithChild(name, "xmlns:x='urn:other' x:Sound='1'"));
                Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'><"+name+"><Unknown/></"+name+"></WeaponTemplate>");
                Reject(Weapon(Attribute(name, "")));
            }
            foreach (string name in FilterNames) {
                Reject(WithChild(name, "Unknown='1'"));
                Reject(WithChild(name, "xmlns:x='urn:other' x:Rule='ANY'"));
                Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'><"+name+"><Unknown/></"+name+"></WeaponTemplate>");
                Reject("<WeaponTemplate xmlns='"+AssetNamespace+"' id='Reject'><"+name+"><IncludeThing Unknown='1'/></"+name+"></WeaponTemplate>");
            }
            Console.WriteLine("PASS WeaponTemplate scalar/duration/resource/status subset: fixtures=" + fixtures + " bufferComparisons=" + comparisons + " unsupportedRejections=" + rejections + " directStatusComparisons=" + directStatusComparisons + " directModelComparisons=" + directModelComparisons + " annotatedFixtures=" + annotatedFixtures);
#if WEAPON_TRANSCRIPT_PRODUCT
            WeaponTranscript.VerifyProduct(typeof(InstanceDeclaration).Assembly, typeof(WeaponCompiler).Assembly, formalPlugin.GetType().Assembly);
#endif
#if WEAPON_TRANSCRIPT_ORACLE || WEAPON_TRANSCRIPT_PRODUCT
            WeaponTranscript.Complete(fixtures, comparisons, rejections, directStatusComparisons, directModelComparisons, annotatedFixtures);
#endif
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
#if WEAPON_TRANSCRIPT_ORACLE || WEAPON_TRANSCRIPT_PRODUCT
        finally { WeaponTranscript.Close(); }
#endif
    }
}





