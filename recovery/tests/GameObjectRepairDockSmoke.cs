using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Schema;
using BinaryAssetBuilder.Core;

// Exercise the production XML bridge against the native three-buffer fixtures.
// Core expressions are covered separately; this harness supplies its TypeId
// annotation only after validating the real derived schema identity.
static class GameObjectRepairDockSmoke
{
    const string Namespace = "uri:ea.com:eala:asset";
    static XmlSchemaSet schemas;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static InstanceDeclaration Declaration(bool filter, bool repair, bool derived)
    {
        string name = repair && !derived ? "RepairDockUpdate" : "DockUpdate";
        string type = derived ? " xsi:type='RepairDockUpdateModuleData'" : "";
        string harbor = filter ? "<ForVoiceRetreatThisIsASafeHarborToObjectFilter Rule='ANY' Relationship='SAME_PLAYER' Include='VEHICLE' StatusBitFlags='DAMAGED'><IncludeThing>SovietConscript</IncludeThing><ExcludeThing>JapanCommando</ExcludeThing></ForVoiceRetreatThisIsASafeHarborToObjectFilter>" : "";
        var xml = new XmlDocument();
        xml.LoadXml("<AssetDeclaration xmlns='" + Namespace + "' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'><GameObject id='RepairSmoke'><Behaviors><" + name + type +
            " id='Repair' NumberApproachPositions='2' AllowsPassthrough='false' GoToRallyPointAfterDock='false' ShouldDockInReverse='true' MinDockTime='0s' ObjectsInLineWeight='1500ms'" +
            (repair ? " FramesForFullHeal='150.5'" : "") + ">" + harbor + "</" + name + "></Behaviors><Body><ActiveBody id='Body' MaxHealth='100'/></Body></GameObject></AssetDeclaration>");
        if (schemas == null)
        {
            schemas = new XmlSchemaSet(); schemas.XmlResolver = new XmlUrlResolver();
            schemas.Add(null, Environment.GetEnvironmentVariable("NEWSdk_PLUGIN_TEST_SCHEMA") ?? Path.GetFullPath("SDK/Schemas/xsd/CnC3Types.xsd")); schemas.Compile();
        }
        xml.Schemas = schemas; xml.Validate(null);
        var root = (XmlElement)xml.DocumentElement.FirstChild;
        var module = (XmlElement)root.GetElementsByTagName(name, Namespace)[0];
        Require(module.SchemaInfo.SchemaType.Name == (repair ? "RepairDockUpdateModuleData" : "DockUpdateModuleData"), "Actual schema type includes xsi:type derivation");
        module.SetAttribute("TypeId", repair ? "347729765" : "3651459681");
        ((XmlElement)root.GetElementsByTagName("ActiveBody", Namespace)[0]).SetAttribute("TypeId", "1510882715");
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", Namespace);
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType); stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var result = new InstanceDeclaration(document); result.XmlNode = root; return result;
    }
    static void Equal(byte[] expected, byte[] actual, string message)
    {
        Require(expected.Length == actual.Length, message + " length");
        for (int i = 0; i < expected.Length; ++i) Require(expected[i] == actual[i], message + " byte " + i);
    }
    static void EqualBuffers(AssetBuffer expected, AssetBuffer actual, string message)
    {
        Equal(expected.InstanceData, actual.InstanceData, message + " instance");
        Equal(expected.RelocationData, actual.RelocationData, message + " relocation");
        Equal(expected.ImportsData, actual.ImportsData, message + " imports");
    }
    static int Main(string[] args)
    {
        try
        {
            var original = (IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[1]).GetType("BinaryAssetBuilder.Tokenizer.Plugin"));
            foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 })
            {
                bool big = platform != TargetPlatform.Win32; original.Initialize(null, platform);
                foreach (bool repair in new[] { false, true }) foreach (bool filter in new[] { false, true })
                {
                    var input = Declaration(filter, repair, false);
                    var result = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
                    string stem = Path.Combine(args[0], (repair ? "repair-dock" : "dock") + (filter ? "-filter" : "-null") + (big ? ".be" : ".le"));
                    Equal(File.ReadAllBytes(stem + ".bin"), result.InstanceData, "Native/XML instance");
                    Equal(File.ReadAllBytes(stem + ".relo"), result.RelocationData, "Native/XML relocation");
                    Equal(File.ReadAllBytes(stem + ".imp"), result.ImportsData, "Native/XML imports");
                    if (repair) EqualBuffers(result, Recovered.GameObjectCompiler.ProcessInstance(Declaration(filter, true, true), platform), "Named/derived RepairDock");
                    else EqualBuffers(original.ProcessInstance(input), result, "Unchanged Dock original DLL");
                }
            }
            Console.WriteLine("PASS RepairDock XML bridge: actual named/xsi:type schema identity; four platforms; optional filter native three-buffer equality; unchanged Dock original-DLL equality");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
