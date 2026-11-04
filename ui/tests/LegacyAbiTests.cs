#if NETFRAMEWORK
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using BinaryAssetBuilder.UiCompiler;

internal static class LegacyAbiTests
{
    private sealed class Collector : IUiAssetCollector
    {
        internal UiAssetGroup Group;
        public void Collect(UiAssetGroup assets) { if (Group != null) throw new Exception("Duplicate sibling collection"); Group = assets; }
    }
    internal static void Verify(string folder, ModStreams expected, Action<bool, string> check)
    {
        // Populate the original Core declaration's two required fields without
        // running its unrelated document/XSD/cache engine. This tests the real
        // SDK interface and property ABI, not a replacement interface or stub.
        var declaration = (InstanceDeclaration)FormatterServices.GetUninitializedObject(typeof(InstanceDeclaration));
        Type stateType = typeof(InstanceDeclaration).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        var handle = (InstanceHandle)FormatterServices.GetUninitializedObject(typeof(InstanceHandle));
        typeof(InstanceHandle).GetField("_TypeId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, 1816258268u);
        var xml = new XmlDocument(); var node = xml.CreateElement("AptAptData", "uri:ea.com:eala:asset"); node.SetAttribute("id", "Main_apt"); node.SetAttribute("File", folder); xml.AppendChild(node);
        stateType.GetField("Handle").SetValue(state, handle); stateType.GetField("XmlNode").SetValue(state, node);
        typeof(InstanceDeclaration).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(declaration, state);
        var collector = new Collector(); using (var owner = new Plugin())
        {
            IAssetBuilderPlugin plugin = owner;
            plugin.Initialize(new UiCompilerConfiguration { Collector = collector }, TargetPlatform.Win32);
            plugin.ReInitialize(new UiCompilerConfiguration { Collector = collector }, TargetPlatform.Win32);
            var information = plugin.GetExtendedTypeInformation(1816258268u); check(information.TypeHash == 3795385027u && information.TypeName == "AptAptData", "real SDK plugin metadata ABI");
            AssetBuffer primary = plugin.ProcessInstance(declaration);
            check(primary.InstanceData.SequenceEqual(expected.Assets[0].Buffer.InstanceData), "real SDK ProcessInstance primary AssetBuffer");
            check(collector.Group.Count == expected.Assets.Count - 1 && collector.Group.InputFiles.Count == 4, "real SDK sibling and dependency collection");
            ((IDisposable)plugin).Dispose();
        }
    }
}
#endif
