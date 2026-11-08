using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.XPath;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

// Focused source-built Core check: run the real expression traversal/evaluator,
// schema defaults and actual derived type metadata, then the production semantic
// gate. This does not claim a complete MOD/Core linking or SDK acceptance run.
static class GameObjectRepairDockCore
{
    const string Namespace = "uri:ea.com:eala:asset";
    static SchemaSet schemas;
    static readonly MethodInfo Semantic = typeof(AssetDeclarationDocument).GetMethod("ValidateRepairDock", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo Instances = typeof(AssetDeclarationDocument).GetMethod("ValidateInstances", BindingFlags.NonPublic | BindingFlags.Instance);
    sealed class MetadataPlugin : IAssetBuilderPlugin
    {
        public void Initialize(object config, TargetPlatform target) { }
        public void ReInitialize(object config, TargetPlatform target) { }
        public uint GetAllTypesHash() { return 1424942948u; }
        public uint GetVersionNumber() { return 4106u; }
        public AssetBuffer ProcessInstance(InstanceDeclaration instance) { throw new NotSupportedException("Frontend test does not run a backend"); }
        public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            return new ExtendedTypeInformation { TypeId=typeId, TypeName="GameObject", TypeHash=3415017856u, ProcessingHash=unchecked(3415017856u + 4106u + 0x52445001u) };
        }
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Check(string frames, string positions, bool derived, bool accepted)
    {
        var xml = new XmlDocument();
        string element = derived ? "DockUpdate" : "RepairDockUpdate";
        string type = derived ? " xsi:type='RepairDockUpdateModuleData'" : "";
        xml.LoadXml("<AssetDeclaration xmlns='" + Namespace + "' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'><GameObject id='CoreRepair'><Behaviors><" + element + type + " id='Repair' AllowsPassthrough='false'" +
            (frames == null ? "" : " FramesForFullHeal='" + frames + "'") +
            (positions == null ? "" : " NumberApproachPositions='" + positions + "'") + "/></Behaviors></GameObject></AssetDeclaration>");
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType);
        var definitions = new DefinitionSet();
        definitions["REPAIR_FRAMES"] = new Definition { Name="REPAIR_FRAMES", EvaluatedValue="300", Document=document };
        stateType.GetField("AllDefines").SetValue(state, definitions);
        stateType.GetField("UsedDefines").SetValue(state, new Dictionary<string,string>());
        var registry = new PluginRegistry(new PluginDescriptor[0], TargetPlatform.Win32);
        registry.AddPlugin(2486173485u, new MetadataPlugin());
        var processor = new DocumentProcessor(Settings.Current, registry, null); processor.SchemaSet = schemas;
        stateType.GetField("DocumentProcessor").SetValue(state, processor);
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", Namespace);
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var declaration = new InstanceDeclaration(document); declaration.XmlNode = xml.DocumentElement.FirstChild;
        var instances = new InstanceSet(); instances.Add(declaration);
        stateType.GetField("SelfInstances").SetValue(state, instances);
        bool rejected = false;
        try
        {
            Require(ExpressionEvaluatorWrapper.GetEvaluator(document) != null, "Real expression evaluator must be loaded");
            document.ProcessExpressions();
            xml.Schemas = schemas.Schemas; xml.Validate(null);
            var module = (XmlElement)xml.GetElementsByTagName(element, Namespace)[0];
            var schemaType = module.SchemaInfo.SchemaType;
            Require(schemaType.Name == "RepairDockUpdateModuleData", "Actual named/xsi:type SchemaType");
            var metadata = typeof(SchemaSet).GetMethod("GetInstanceTypeMetadata", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(schemas, new object[] {schemaType});
            string typeId = (string)metadata.GetType().GetField("TypeIdText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(metadata);
            Require(typeId == "347729765", "Core schema metadata emits the native RepairDock TypeId");
            // Execute the production frontend's actual semantic dispatch and
            // TypeId injection, rather than recreating its XML annotation.
            Instances.Invoke(document, null);
            Require(module.GetAttribute("TypeId") == "347729765", "Production Core injects actual derived RepairDock TypeId");
            if (frames == "=$REPAIR_FRAMES / 2")
                Require(Single.Parse(module.GetAttribute("FramesForFullHeal"), CultureInfo.InvariantCulture) == 150.0f, "Real define arithmetic evaluated before semantic gate");
        }
        catch (TargetInvocationException error)
        {
            if (!(error.InnerException is BinaryAssetBuilderException)) throw;
            rejected = true;
        }
        catch (System.Xml.Schema.XmlSchemaValidationException) { rejected = true; }
        catch (BinaryAssetBuilderException) { rejected = true; }
        Require(rejected != accepted, "Unexpected Core result for frames=" + frames + " positions=" + positions + " derived=" + derived);
    }
    static int Main()
    {
        try
        {
            Require(Semantic != null && Instances != null, "Tests must use newly source-built Core");
            Settings.Current = new Settings();
            Settings.Current.SchemaPath = Path.GetFullPath("SDK/Schemas/xsd/CnC3Types.xsd");
            Settings.Current.StringHashBinDescriptors = new StringHashBinDescriptor[0];
            HashProvider.InitializeStringHashes(Path.GetTempPath());
            schemas = new SchemaSet(false); ExpressionEvaluatorWrapper.LoadAssembly();
            foreach (bool derived in new[] {false,true})
            {
                Check("150.5", "2", derived, true);
                Check("0.5", "1", derived, true);
                Check("=$REPAIR_FRAMES / 2", "=1 + 1", derived, true);
                foreach (string value in new[] { null, "0", "-1", "NaN", "INF", "-INF", "1e39", "1e-99", "150s", "=0 - 1", "=$MISSING_DEFINE" })
                    Check(value, "2", derived, false);
                foreach (string value in new[] { null, "0", "-1", "1.5", "2147483648", "=1 - 1" })
                    Check("150", value, derived, false);
            }
            Console.WriteLine("PASS source Core: real define/expression traversal before schema/semantic checks, production named/xsi:type metadata and TypeId injection, positive finite float32 and Int32 waiting count, defaults/malformed/overflow/underflow failures");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
