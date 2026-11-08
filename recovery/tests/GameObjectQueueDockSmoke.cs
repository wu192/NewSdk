using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// Real GameObject XML -> existing ReadModules -> production native writer.
// TypeId is the annotation supplied by Core before the plugin is called.
static class GameObjectQueueDockSmoke
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static InstanceDeclaration Input(string capacity)
    {
        var xml = new XmlDocument();
        xml.LoadXml("<GameObject xmlns='uri:ea.com:eala:asset' id='QueueDockSmoke'><Behaviors><QueueDockBehavior TypeId='2941001505' id='QueueDock' DockStartBoneName='DockStart' DockActionBoneName='DockAction' DockEndBoneName='DockEnd' QueueBonePrefix='Que' TurnTableBoneName='TurnTable'" + (capacity == null ? "" : " MaxConcurrentMiners='" + capacity + "'") + "/></Behaviors></GameObject>");
        return Declaration(xml.DocumentElement);
    }
    static InstanceDeclaration Declaration(XmlElement element)
    {
        var ns = new XmlNamespaceManager(element.OwnerDocument.NameTable); ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var result = new InstanceDeclaration(document); result.XmlNode = element; return result;
    }
    static uint Word(byte[] bytes, int at, bool big)
    {
        return big ? ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3] : BitConverter.ToUInt32(bytes, at);
    }
    static void Equal(byte[] expected, byte[] actual, string label)
    {
        Require(expected.Length == actual.Length, label + " length");
        for (int i = 0; i < expected.Length; ++i) Require(expected[i] == actual[i], label + " byte " + i);
    }
    static AssetBuffer Compile(string capacity, TargetPlatform platform, uint expected)
    {
        var input = Input(capacity); PluginSchemaBoundary.Validate(input.Node);
        var result = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
        bool big = platform != TargetPlatform.Win32;
        int root = (int)Word(result.InstanceData, 8, big) - 4;
        int header = (int)Word(result.InstanceData, root + 468, big);
        int slots = (int)Word(result.InstanceData, header + 4, big);
        int data = (int)Word(result.InstanceData, slots, big);
        Require(Word(result.InstanceData, data, big) == 0xAF4C1F21u && Word(result.InstanceData, data + 52, big) == expected, "Original QueueDock type and capacity/endian");
        return result;
    }
    static void Reject(string capacity)
    {
        // Range 1..10 is checked after Core expansion by the production writer;
        // schema alone accepts 0/11 and is deliberately not the acceptance test.
        bool rejected = false;
        try { Recovered.GameObjectCompiler.ProcessInstance(Input(capacity), TargetPlatform.Win32); }
        catch (Exception) { rejected = true; }
        Require(rejected, "Production XML compiler accepted invalid capacity: " + capacity);
    }
    static void Examples(string path, TargetPlatform platform)
    {
        var xml = new XmlDocument(); xml.Load(path);
        int count = 0;
        foreach (XmlNode node in xml.DocumentElement.ChildNodes)
        {
            var element = node as XmlElement; if (element == null) continue;
            Example(element, platform); ++count;
        }
        Require(count == 4, "Standalone default/1/2/10 examples");
    }
    static void Example(XmlElement element, TargetPlatform platform)
        {
            PluginSchemaBoundary.Validate(element);
            var module = (XmlElement)element.GetElementsByTagName("QueueDockBehavior", "uri:ea.com:eala:asset")[0];
            module.SetAttribute("TypeId", "2941001505");
            var result = Recovered.GameObjectCompiler.ProcessInstance(Declaration(element), platform);
            bool big = platform != TargetPlatform.Win32;
            int root = (int)Word(result.InstanceData, 8, big) - 4;
            int list = (int)Word(result.InstanceData, root + 468, big);
            int slots = (int)Word(result.InstanceData, list + 4, big);
            int data = (int)Word(result.InstanceData, slots, big);
            uint capacity = module.HasAttribute("MaxConcurrentMiners") ? UInt32.Parse(module.GetAttribute("MaxConcurrentMiners")) : 1u;
            Require(Word(result.InstanceData, data, big) == 0xAF4C1F21u && Word(result.InstanceData, data + 52, big) == capacity, "Standalone real GameObject example capacity");
            Require(Word(result.InstanceData, data + 48, big) != 0, "Standalone example preserves DockingAnimation");
    }
    static void Legacy(IAssetBuilderPlugin original, TargetPlatform platform)
    {
        original.Initialize(null, platform);
        foreach (int count in new[] { 0, 1, 2, 10 })
        {
            var input = Input(null);
            var behaviors = (XmlElement)input.Node.ChildNodes[0];
            var template = (XmlElement)behaviors.FirstChild.CloneNode(true);
            behaviors.RemoveAll();
            for (int i = 0; i < count; ++i)
            {
                var module = (XmlElement)template.CloneNode(true); module.SetAttribute("id", "QueueDock" + i);
                behaviors.AppendChild(module);
            }
            var actual = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
            var projected = GameObjectOracle.QueueDockLegacyView(actual, platform != TargetPlatform.Win32);
            var expected = original.ProcessInstance(input);
            Equal(expected.InstanceData, projected.InstanceData, "Legacy QueueDock exact extension projection instance");
            Equal(expected.RelocationData, projected.RelocationData, "Legacy QueueDock exact extension projection relocations");
            Equal(expected.ImportsData, projected.ImportsData, "Legacy QueueDock exact extension projection imports");
        }
    }
    static int Main(string[] args)
    {
        try
        {
            var original = (IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[2]).GetType("BinaryAssetBuilder.Tokenizer.Plugin"));
            foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 })
            {
                var plugin = new Recovered.GameObject.Plugin(); plugin.Initialize(null, platform);
                uint version = platform == TargetPlatform.PlayStation3 ? 4111u : platform == TargetPlatform.Xbox360 ? 4110u : 4109u;
                Require(plugin.GetVersionNumber() == version && plugin.GetExtendedTypeInformation(2486173485u).ProcessingHash == unchecked(3415017856u + version), "New GameObject cache identity");
                bool big = platform != TargetPlatform.Win32;
                var defaults = Compile(null, platform, 1);
                var one = Compile("1", platform, 1);
                Equal(defaults.InstanceData, one.InstanceData, "Default and explicit one instance");
                Equal(defaults.RelocationData, one.RelocationData, "Default and explicit one relocations");
                foreach (uint capacity in new uint[] { 1, 2, 10 })
                {
                    var result = Compile(capacity.ToString(), platform, capacity);
                    string stem = Path.Combine(args[0], "queue-dock-" + capacity + (big ? ".be" : ".le"));
                    Equal(File.ReadAllBytes(stem + ".bin"), result.InstanceData, "Real XML/native instance");
                    Equal(File.ReadAllBytes(stem + ".relo"), result.RelocationData, "Real XML/native relocation");
                    Equal(File.ReadAllBytes(stem + ".imp"), result.ImportsData, "Real XML/native imports");
                }
                Compile("0008", platform, 8); Compile("010", platform, 10);
                Examples(args[1], platform);
                Legacy(original, platform);
            }
            foreach (string capacity in new[] { "0", "11", "-1", "4294967296", "9999999999999999999999999", "1.5", "text", "2junk", "0x2", "", "+", " " }) Reject(capacity);
            Console.WriteLine("PASS QueueDock real XML bridge: four platforms, default/1/2/10 native three-buffer equality, strict invalid/range errors, cache identity; production native token/ReloString/endian tests run separately");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
