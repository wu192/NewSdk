using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// Focused CLR/XML boundary coverage for the native Dozer restoration and the
// original GameObject token layout. The original DLL has no Dozer XML marshaler, so this
// test asserts the recovered contract rather than treating it as an oracle.
static class GameObjectDozerSmoke
{
    static string outputDirectory;
    static InstanceDeclaration Declaration(string setting, string fields)
    {
        var xml = new XmlDocument();
        xml.LoadXml("<GameObject xmlns='uri:ea.com:eala:asset' id='DozerSmoke'>" +
            "<AI><DozerAIUpdate TypeId='2366190371' id='DozerAI' " + fields +
            "><UnitAITargetChooserData TargetingCompareList='DefaultTargetingCompareList'/>" +
            "</DozerAIUpdate></AI><Body><ActiveBody TypeId='1510882715' id='Body' MaxHealth='100'/>" +
            "</Body>" + setting + "</GameObject>");
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var result = new InstanceDeclaration(document); result.XmlNode = xml.DocumentElement; return result;
    }
    static uint Word(byte[] bytes, int at, bool big)
    {
        return big ? ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) |
            ((uint)bytes[at + 2] << 8) | bytes[at + 3] : BitConverter.ToUInt32(bytes, at);
    }
    static int Token(byte[] bytes, int table, uint name, bool big)
    {
        for (int at = table; Word(bytes, at, big) != 0; at += 12)
            if (Word(bytes, at, big) == name) return at;
        throw new Exception("Missing token: " + name);
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Check(TargetPlatform platform, bool defaults)
    {
        bool big = platform != TargetPlatform.Win32;
        var input = Declaration("", defaults ? "" : "RepairHealthPercentPerSecond='2.5' BoredTime='3.25' BoredRange='64'");
        PluginSchemaBoundary.Validate(input.Node);
        var chunk = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
        var bytes = chunk.InstanceData;
        int list = (int)Word(bytes, Token(bytes, 0, 1376227828u, big) + 8, big);
        int wrapper = (int)Word(bytes, list + 20, big);
        Require(Word(bytes, wrapper, big) == 2 && Word(bytes, wrapper + 4, big) == 0x8D093323u, "Native Dozer choice");
        int table = (int)Word(bytes, wrapper + 8, big);
        Require(Word(bytes, table, big) == 1 && Word(bytes, table + 4, big) == 0x08DBC00Eu, "AIUpdate parent tokens");
        int repair = Token(bytes, table, 0x8229777Eu, big);
        int data = (int)Word(bytes, repair + 8, big) - 76;
        Require(Word(bytes, repair + 4, big) == 0x645864F3u, "Repair token is a raw float");
        Require(Word(bytes, data + 76, big) == (defaults ? 0u : 0x40200000u) &&
            Word(bytes, data + 80, big) == (defaults ? 0u : 0x40500000u) &&
            Word(bytes, data + 84, big) == (defaults ? 0u : 0x42800000u), "Native Dozer floats/defaults");
        Require(Word(bytes, data + 68, big) >= data + 88, "Inherited target chooser survives XML bridge");
        for (int i = 0; i < 12; ++i)
            Require(bytes[1512 + i] == 0, "Original root token sentinel");
        Require(chunk.ImportsData.Length == 0, "Tokenized imports");
        if (outputDirectory != null)
        {
            string name = Path.Combine(outputDirectory, "xml-" + (int)platform + "-" +
                (defaults ? "defaults" : "parameters"));
            File.WriteAllBytes(name + ".bin", bytes);
            File.WriteAllBytes(name + ".relo", chunk.RelocationData);
            File.WriteAllBytes(name + ".imp", chunk.ImportsData);
            File.WriteAllText(name + ".xml", input.Node.OuterXml);
        }
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0) { outputDirectory = args[0]; Directory.CreateDirectory(outputDirectory); }
            foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 })
            {
                Check(platform, true);
                Check(platform, false);
            }
            foreach (string bad in new[] { "<EnhancerSettings/>", "<EnhancerSettings DozerConstruction='true'/>" })
            {
                bool rejected = false;
                try { PluginSchemaBoundary.Validate(Declaration(bad, "").Node); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected, "Removed building extension accepted by SDK schema");
            }
            // A normal RA3 building requires no Dozer-specific field or token.
            var building = new XmlDocument();
            building.LoadXml("<GameObject xmlns='uri:ea.com:eala:asset' id='OrdinaryBuilding' KindOf='STRUCTURE'><Body><ActiveBody id='Body' MaxHealth='100'/></Body></GameObject>");
            PluginSchemaBoundary.Validate(building.DocumentElement);
            Console.WriteLine("PASS Dozer XML bridge: four platforms, inherited chooser, floats/defaults, ordinary building and obsolete-node schema refusals"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
