using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using BinaryAssetBuilder.Core;

// Compile with the CLR 2.0 x86 csc.exe and the unchanged original Core DLL.
// Original and replacement run in different processes because their assembly
// identities must be identical. This host does not replace SDK integration.
class LegacyTokenizerOracle
{
    const string AssetNamespace = "uri:ea.com:eala:asset";
    const uint DozerType = 0x8D093323u;
    static readonly int[] Platforms = new int[] { 0, 1, 2, 99 };
    static readonly uint[] RootTypes = new uint[] { 2486173485u, 2496977262u, 980180622u };
    static readonly string[] RootNames = new string[] { "GameObject", "WeaponTemplate", "ArmorTemplate" };
    static readonly uint[] RootHashes = new uint[] { 3415017856u, 216405646u, 1679479598u };
    static readonly string Chooser = "<UnitAITargetChooserData TargetingCompareList='Target\\23' CheckVisionRange='true'>" +
        "<ObjectVisionRange id='Vision_Metadata' Range='25%'><Filter Include='ALL' Rule='ANY'><IncludeThing>TargetA</IncludeThing></Filter></ObjectVisionRange>" +
        "</UnitAITargetChooserData>";

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static string Asset(string name, string attributes, string children)
    {
        return "<" + name + " xmlns='" + AssetNamespace + "' id='LegacyProbe' " + attributes + ">" + children + "</" + name + ">";
    }
    static InstanceDeclaration Declaration(string source)
    {
        XmlDocument xml = new XmlDocument();
        xml.LoadXml(source);
        XmlNamespaceManager namespaces = new XmlNamespaceManager(xml.NameTable);
        namespaces.AddNamespace("ea", AssetNamespace);
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration declaration = new InstanceDeclaration(document);
        declaration.XmlNode = xml.DocumentElement;
        return declaration;
    }
    static IAssetBuilderPlugin Load(string path)
    {
        Require(Environment.Version.Major == 2, "Host must execute on CLR 2.0; actual=" + Environment.Version);
        Require(IntPtr.Size == 4, "Host must execute as x86");
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(path));
        Require(assembly.FullName == "BinaryAssetBuilder.Tokenizer, Version=1.0.3492.22559, Culture=neutral, PublicKeyToken=null", "Wrong Tokenizer assembly identity: " + assembly.FullName);
        Require(assembly.ImageRuntimeVersion == "v2.0.50727", "Tokenizer must target CLR 2.0: " + assembly.ImageRuntimeVersion);
        Module[] modules = assembly.GetModules();
        Require(modules.Length == 1 && assembly.GetFiles().Length == 1, "Tokenizer has an external module dependency");
        using (FileStream stream = File.OpenRead(path))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            stream.Position = 0x3c;
            uint pe = reader.ReadUInt32();
            stream.Position = pe;
            Require(reader.ReadUInt32() == 0x00004550u && reader.ReadUInt16() == 0x014cu, "Tokenizer PE must be I386");
        }
        return (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.Tokenizer.Plugin", true));
    }
    static string Metadata(IAssetBuilderPlugin plugin)
    {
        StringBuilder result = new StringBuilder();
        foreach (int target in Platforms)
        {
            plugin.Initialize(null, (TargetPlatform)target);
            uint version = target == 1 ? 4101u : target == 2 ? 4102u : 4100u;
            Require(plugin.GetVersionNumber() == version && plugin.GetAllTypesHash() == 1424942948u, "Plugin version/type hash changed for platform " + target);
            result.AppendFormat(CultureInfo.InvariantCulture, "platform={0};version={1};allTypesHash={2}\n", target, version, plugin.GetAllTypesHash());
            for (int i = 0; i < RootTypes.Length; ++i)
            {
                ExtendedTypeInformation info = plugin.GetExtendedTypeInformation(RootTypes[i]);
                Require(info.TypeId == RootTypes[i] && info.TypeName == RootNames[i] && info.TypeHash == RootHashes[i] &&
                    info.ProcessingHash == unchecked(RootHashes[i] + version) && info.Tokenized, "Plugin metadata changed: " + RootNames[i]);
                // Include all flag fields in the cross-process comparison.
                result.AppendFormat(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5};{6}\n", info.TypeName, info.TypeId,
                    info.TypeHash, info.ProcessingHash, info.HasCustomData, info.Tokenized, info.UseBuildCache);
            }
        }
        return result.ToString();
    }
    static AssetBuffer Compile(IAssetBuilderPlugin plugin, int platform, string xml)
    {
        plugin.ReInitialize(null, (TargetPlatform)platform);
        AssetBuffer result = plugin.ProcessInstance(Declaration(xml));
        Require(result != null && result.InstanceData != null && result.RelocationData != null && result.ImportsData != null, "Missing compiler buffers");
        return result;
    }
    static void Save(string directory, string name, AssetBuffer result)
    {
        File.WriteAllBytes(Path.Combine(directory, name + ".instance.bin"), result.InstanceData);
        File.WriteAllBytes(Path.Combine(directory, name + ".relocations.bin"), result.RelocationData);
        File.WriteAllBytes(Path.Combine(directory, name + ".imports.bin"), result.ImportsData);
    }
    static void Dump(IAssetBuilderPlugin plugin, string directory)
    {
        string[] names = new string[] { "game-defaults", "game-ai-chooser", "weapon-defaults", "weapon-values", "weapon-filter", "armor-defaults", "armor-values" };
        string[] fixtures = new string[] {
            Asset("GameObject", "", ""),
            Asset("GameObject", "KindOf='VEHICLE SELECTABLE' Browser='probe' BuildTime='10'", "<DisplayName>ProbeName</DisplayName>" +
                "<ArmorSet Armor='Armor\\23' Conditions='VETERAN'/><LocomotorSet Speed='50' Condition='NORMAL' Locomotor='Loco\\1'/>" +
                "<AI><AIUpdate TypeId='148619278' id='ModuleTag_AI' AttackPriority='abc' SpecialContactPoints='One Two' StopChaseDistance='12.5' MinCowerTime='500ms' StateMachine='UnitAIStateMachine' StandGround='true'>" + Chooser + "</AIUpdate></AI>"),
            Asset("WeaponTemplate", "", ""),
            Asset("WeaponTemplate", "AttackRange='125.5' WeaponSpeed='240' PreferredTargetBone='Turret' FireFX='FX\\17'", "<PreAttackDelay MinSeconds='500ms' MaxSeconds='2s'/><ClipReloadTime MinSeconds='1s' MaxSeconds='3s'/>"),
            Asset("WeaponTemplate", "", "<SurpriseAttackObjectFilter Rule='ANY' Include='AIRCRAFT VEHICLE' Exclude='STRUCTURE' StatusBitFlags='AFLAME'><IncludeThing>TargetA</IncludeThing><ExcludeThing>TargetB</ExcludeThing></SurpriseAttackObjectFilter>"),
            Asset("ArmorTemplate", "", ""),
            Asset("ArmorTemplate", "Default='12.25%' DamageScalar='23.5' SideDamageScalar='34.75' RearDamageScalar='45' FlankedPenalty='56.125'", "<Armor Damage='TESLA' Percent='-10'/><Armor Damage='GUN' Percent='225.5'/>")
        };
        foreach (int platform in Platforms)
            for (int i = 0; i < fixtures.Length; ++i)
                Save(directory, names[i] + "-p" + platform.ToString(CultureInfo.InvariantCulture), Compile(plugin, platform, fixtures[i]));
        Console.WriteLine("PASS dump: {0} fixtures, {1} raw buffers; metadata and CLR2/x86/single-module contract checked", fixtures.Length * Platforms.Length, fixtures.Length * Platforms.Length * 3);
    }
    static uint Word(byte[] data, int offset, bool big)
    {
        return big ? ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3] : BitConverter.ToUInt32(data, offset);
    }
    static int Token(byte[] data, int table, uint name, bool big)
    {
        for (int at = table; Word(data, at, big) != 0; at += 12)
            if (Word(data, at, big) == name) return at;
        throw new Exception("Missing token " + name.ToString("X8", CultureInfo.InvariantCulture));
    }
    static bool Relocation(AssetBuffer result, int offset, bool big)
    {
        for (int i = 0; i < result.RelocationData.Length; i += 4)
            if (Word(result.RelocationData, i, big) == (uint)offset) return true;
        return false;
    }
    static void CheckDozer(AssetBuffer result, int platform, bool values)
    {
        bool big = platform != 0;
        byte[] data = result.InstanceData;
        int root = (int)Word(data, 8, big) - 4;
        Require(Word(data, root + 472, big) == 1, "AI list count");
        int slots = (int)Word(data, root + 476, big);
        int module = (int)Word(data, slots, big);
        Require(Relocation(result, root + 476, big) && Relocation(result, slots, big), "AI list/polymorphic relocations");
        Require(Word(data, module, big) == DozerType, "Native Dozer type");
        int list = (int)Word(data, Token(data, 0, 1376227828u, big) + 8, big);
        int wrapper = (int)Word(data, list + 20, big);
        Require(Word(data, wrapper, big) == 2 && Word(data, wrapper + 4, big) == DozerType, "Dozer polymorphic token");
        int table = (int)Word(data, wrapper + 8, big);
        Require(Word(data, table, big) == 1 && Word(data, table + 4, big) == 0x08DBC00Eu, "Dozer AIUpdate inheritance token");
        int parent = (int)Word(data, table + 8, big);
        Require(Word(data, parent, big) == 1 && Word(data, parent + 4, big) == 4043318533u, "AIUpdate Update inheritance token");
        uint[] names = new uint[] { 0x8229777Eu, 0x18C1A7F3u, 0xC492A6B5u };
        uint[] bits = values ? new uint[] { 0x40200000u, 0x40500000u, 0x80000000u } : new uint[] { 0, 0, 0 };
        for (int i = 0; i < 3; ++i)
        {
            int at = table + 12 * (i + 1);
            Require(Word(data, at, big) == names[i] && Word(data, at + 4, big) == 0x645864F3u &&
                Word(data, at + 8, big) == module + 0x4Cu + (uint)(4 * i), "Dozer scalar token/offset");
            Require(Word(data, module + 0x4C + 4 * i, big) == bits[i], "Dozer raw float bits");
        }
        for (int i = 0; i < 12; ++i) Require(data[table + 48 + i] == 0, "Dozer token sentinel");
        for (int i = 0; i < 12; ++i) Require(data[126 * 12 + i] == 0, "Original GameObject root token sentinel");
        if (!values)
        {
            Require(data.Length == module + 0x58 && Word(data, module + 68, big) == 0, "Dozer default size 0x58/null chooser");
            Require(Word(data, module + 20, big) == 0x43FA0000u && Word(data, module + 36, big) == 0x42A00000u && Word(data, module + 52, big) == 1, "Inherited AIUpdate defaults");
        }
        else
        {
            Require(Word(data, module + 20, big) == 0x41480000u && Word(data, module + 24, big) == 0x3F000000u && data[module + 72] == 1, "Inherited AIUpdate scalar values");
            int chooser = (int)Word(data, module + 68, big);
            Require(chooser == module + 0x58 && Relocation(result, module + 68, big), "Dozer 0x58 allocation followed by chooser/relocation");
            Require(Word(data, chooser + 36, big) == 24 && Word(data, chooser + 20, big) == 1 && Relocation(result, chooser + 24, big), "Inherited chooser reference/range list relocation");
            int ranges = (int)Word(data, chooser + 24, big);
            Require(Word(data, ranges + 4, big) == 0x3E800000u, "Inherited chooser percent range");
            int chooserTable = (int)Word(data, Token(data, parent, 607907585u, big) + 8, big);
            Require(Word(data, chooserTable, big) == 1 && Word(data, chooserTable + 4, big) == 2856512412u, "Inherited chooser token parent");
        }
        Require(result.ImportsData.Length == 0, "Unexpected Dozer imports");
        Require(result.RelocationData.Length >= 4 && Word(result.RelocationData, result.RelocationData.Length - 4, big) == UInt32.MaxValue, "Relocation sentinel");
    }
    static void Dozer(IAssetBuilderPlugin plugin, string directory)
    {
        foreach (int platform in Platforms)
            for (int value = 0; value < 2; ++value)
            {
                bool values = value != 0;
                string attributes = values ? "RepairHealthPercentPerSecond='2.5' BoredTime='3.25' BoredRange='-0' StopChaseDistance='12.5' MinCowerTime='500ms' StandGround='true'" : "";
                string xml = Asset("GameObject", "", "<AI><DozerAIUpdate TypeId='" + DozerType.ToString(CultureInfo.InvariantCulture) + "' id='ModuleTag_AI' " + attributes + ">" + (values ? Chooser : "") + "</DozerAIUpdate></AI>");
                AssetBuffer result = Compile(plugin, platform, xml);
                CheckDozer(result, platform, values);
                Save(directory, "dozer-" + (values ? "values" : "defaults") + "-p" + platform.ToString(CultureInfo.InvariantCulture), result);
            }
        Console.WriteLine("PASS dozer: 8 fixtures; native type/0x58/raw floats/defaults/AI inheritance/chooser/tokens/relocations checked");
    }
    static void Compare(string expected, string actual)
    {
        string[] files = Directory.GetFiles(expected);
        Require(files.Length == Directory.GetFiles(actual).Length && files.Length > 1, "Fixture file count differs or is empty");
        int comparisons = 0;
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            byte[] a = File.ReadAllBytes(file), b = File.ReadAllBytes(Path.Combine(actual, name));
            Require(a.Length == b.Length, name + " buffer length differs");
            for (int i = 0; i < a.Length; ++i) Require(a[i] == b[i], name + " byte differs at " + i);
            if (name.EndsWith(".bin", StringComparison.Ordinal)) ++comparisons;
        }
        Require(comparisons == 84, "Expected 28 fixtures and 84 buffer comparisons");
        Console.WriteLine("PASS compare: {0} three-buffer byte comparisons and exact metadata", comparisons);
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Usage: LegacyTokenizerOracle dump|dozer DLL OUTPUT_DIR; compare BASELINE_DIR CANDIDATE_DIR");
            if (args[0] == "compare") { Compare(args[1], args[2]); return 0; }
            if (args[0] != "dump" && args[0] != "dozer") throw new ArgumentException("Unknown mode " + args[0]);
            IAssetBuilderPlugin plugin = Load(args[1]);
            Directory.CreateDirectory(args[2]);
            File.WriteAllText(Path.Combine(args[2], "metadata.txt"), Metadata(plugin), Encoding.UTF8);
            if (args[0] == "dump") Dump(plugin, args[2]); else Dozer(plugin, args[2]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
