using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using BinaryAssetBuilder.Core;

// Component measurement only: parsed, BAB-style annotated XML; no Core cache,
// dependency preparation, file I/O or XML parsing inside the measured interval.
static class TokenizerPluginPerformance
{
    delegate AssetBuffer Compile(XmlNode node, XmlNamespaceManager namespaces, bool bigEndian);
    sealed class Fixture
    {
        public string Name, Type;
        public XmlDocument Document;
        public XmlNamespaceManager Namespaces;
        public int Iterations;
        public Fixture(string name, string type, string attributes, string children, int iterations)
        {
            Name = name; Type = type; Iterations = iterations;
            Document = new XmlDocument();
            Document.LoadXml("<" + type + " xmlns='uri:ea.com:eala:asset' id='Performance' " + attributes + ">" + children + "</" + type + ">");
            Namespaces = new XmlNamespaceManager(Document.NameTable);
            Namespaces.AddNamespace("ea", "uri:ea.com:eala:asset");
        }
    }
    static Fixture SchemaDefaults(string type)
    {
        var fixture = new Fixture(type == "GameObject" ? "gameobject-schema-defaults" : "weapon-schema-defaults", type, "", "", 4000);
        var schemas = new XmlSchemaSet(); schemas.XmlResolver = new XmlUrlResolver();
        schemas.Add(null, Path.GetFullPath("SDK/Schemas/xsd/CnC3Types.xsd")); schemas.Compile();
        var definition = (XmlSchemaComplexType)schemas.GlobalTypes[new XmlQualifiedName(type, "uri:ea.com:eala:asset")];
        if (definition == null) throw new Exception("Missing schema type " + type);
        foreach (XmlSchemaAttribute attribute in definition.AttributeUses.Values)
            if (attribute.DefaultValue != null && attribute.QualifiedName.Namespace.Length == 0)
                fixture.Document.DocumentElement.SetAttribute(attribute.QualifiedName.Name, attribute.DefaultValue);
        return fixture;
    }
    static Compile Load(string directory, string revision, string type)
    {
        string component = type == "GameObject" ? "GameObject" : "Weapon";
        var assembly = Assembly.LoadFrom(Path.Combine(directory, revision + "." + component + ".dll"));
        var method = assembly.GetType("Recovered." + component + "Compiler", true).GetMethod("CompileXml");
        return (Compile)Delegate.CreateDelegate(typeof(Compile), method);
    }
    static void Equal(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) throw new Exception("Buffer length differs");
        for (int i = 0; i < left.Length; ++i)
            if (left[i] != right[i]) throw new Exception("Buffer byte differs at " + i);
    }
    static void Equal(AssetBuffer left, AssetBuffer right)
    {
        Equal(left.InstanceData, right.InstanceData);
        Equal(left.RelocationData, right.RelocationData);
        Equal(left.ImportsData, right.ImportsData);
    }
    static InstanceDeclaration Declaration(Fixture fixture)
    {
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, fixture.Namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var declaration = new InstanceDeclaration(document); declaration.XmlNode = fixture.Document.DocumentElement;
        return declaration;
    }
    static AssetBuffer Measure(StreamWriter csv, Fixture fixture, Compile compile, string revision, int round, AssetBuffer expected)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        var timer = Stopwatch.StartNew();
        AssetBuffer result = null;
        for (int i = 0; i < fixture.Iterations; ++i)
            result = compile(fixture.Document.DocumentElement, fixture.Namespaces, false);
        timer.Stop();
        long bytes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocated;
        Equal(expected, result);
        string row = String.Join(",", fixture.Name, revision, round, fixture.Iterations,
            timer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture), bytes,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
        csv.WriteLine(row); csv.Flush(); Console.WriteLine(row);
        return result;
    }
    static int Main(string[] args)
    {
        try {
            if (args.Length != 2 || IntPtr.Size != 4) throw new Exception("Expected DLL directory, CSV path and x86 CLR");
            AppDomain.MonitoringIsEnabled = true;
            var fixtures = new List<Fixture>();
            fixtures.Add(new Fixture("gameobject-default", "GameObject", "", "", 4000));
            var modules = new StringBuilder("<Draws>");
            for (int module = 0; module < 8; ++module) {
                modules.Append("<ScriptedModelDraw TypeId='1322199627' id='Draw" + module + "'>");
                for (int state = 0; state < 8; ++state)
                    modules.Append("<ModelConditionState Name='State" + state + "' ConditionsYes='DAMAGED'><Model Name='model\\1'/><Texture Original='texture\\2' New='texture\\3'/><SubObject>Turret</SubObject></ModelConditionState>");
                modules.Append("<AnimationState Name='Idle'><Animation AnimationName='animation\\4'/><Script>return true</Script></AnimationState></ScriptedModelDraw>");
            }
            modules.Append("</Draws>");
            fixtures.Add(new Fixture("gameobject-nested-draws", "GameObject", "KindOf='VEHICLE SELECTABLE'", modules.ToString(), 160));
            var wide = new StringBuilder("<Draws><ScriptedModelDraw TypeId='1322199627' id='Wide'>");
            for (int state = 0; state < 8; ++state) {
                wide.Append("<ModelConditionState Name='Wide" + state + "' ConditionsYes='DAMAGED'>");
                for (int item = 0; item < 12; ++item)
                    wide.Append("<Model Name='model\\1'/><Texture Original='texture\\2' New='texture\\3'/><SubObject>Turret</SubObject>");
                wide.Append("</ModelConditionState>");
            }
            wide.Append("</ScriptedModelDraw></Draws>");
            fixtures.Add(new Fixture("gameobject-wide-state", "GameObject", "", wide.ToString(), 160));
            fixtures.Add(new Fixture("weapon-default", "WeaponTemplate", "", "", 4000));
            var nuggets = new StringBuilder("<Nuggets>");
            for (int i = 0; i < 32; ++i)
                nuggets.Append("<DamageNugget TypeId='3237297817' Damage='17.25' Radius='5' ForbiddenTargetObjectStatus='AFLAME'><SpecialObjectFilter Include='VEHICLE'><IncludeThing>Tank</IncludeThing></SpecialObjectFilter><DamageScalarDetails Scalar='1.25'><Filter Include='STRUCTURE'/></DamageScalarDetails></DamageNugget>");
            nuggets.Append("</Nuggets>");
            fixtures.Add(new Fixture("weapon-nuggets", "WeaponTemplate", "AttackRange='125' FireSound='sound\\5' Flags='LENGTH_SCATTER'", nuggets.ToString() +
                "<ClipReloadTime MinSeconds='4s'/><FiringDuration MinSeconds='2s'/><PreAttackDelay MinSeconds='1s'/><CoolDownDelayBetweenShots MinSeconds='3s'/><OverrideVoiceAttackSound Sound='sound\\6'/><WeaponAiHintInfo MaxSpeedOfTarget='33.5'/><ScatterRadiusVsType Radius='1.25'><Filter Include='VEHICLE'/></ScatterRadiusVsType><IncompatibleAttributeModifier>modifier\\7</IncompatibleAttributeModifier>", 240));
            fixtures.Add(SchemaDefaults("GameObject"));
            fixtures.Add(SchemaDefaults("WeaponTemplate"));
            var original = (IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(Path.Combine(args[0], "BinaryAssetBuilder.Tokenizer.dll")).GetType("BinaryAssetBuilder.Tokenizer.Plugin", true));
            using (var csv = new StreamWriter(args[1], false, new UTF8Encoding(false))) {
                csv.WriteLine("fixture,revision,round,iterations,milliseconds,managedAllocatedBytes,gen0,gen1,gen2");
                foreach (var fixture in fixtures) {
                    var before = Load(args[0], "Before", fixture.Type);
                    var after = Load(args[0], "After", fixture.Type);
                    AssetBuffer expected = null;
                    foreach (bool big in new[] { false, true }) {
                        original.Initialize(null, big ? TargetPlatform.Xbox360 : TargetPlatform.Win32);
                        var reference = original.ProcessInstance(Declaration(fixture));
                        Equal(reference, before(fixture.Document.DocumentElement, fixture.Namespaces, big));
                        Equal(reference, after(fixture.Document.DocumentElement, fixture.Namespaces, big));
                        if (!big) expected = reference;
                    }
                    // Warm both implementations before alternating the sample order.
                    for (int i = 0; i < 10; ++i) {
                        before(fixture.Document.DocumentElement, fixture.Namespaces, false);
                        after(fixture.Document.DocumentElement, fixture.Namespaces, false);
                    }
                    for (int round = 0; round < 5; ++round) {
                        if ((round & 1) == 0) Measure(csv, fixture, before, "before", round, expected);
                        Measure(csv, fixture, after, "after", round, expected);
                        if ((round & 1) != 0) Measure(csv, fixture, before, "before", round, expected);
                    }
                }
            }
            Console.WriteLine("PASS original/before/after three-buffer equivalence for all seven fixtures, both byte orders; five paired Win32 rounds.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
