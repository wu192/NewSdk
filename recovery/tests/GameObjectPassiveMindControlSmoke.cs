using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// Exercise the production C++/CLI bridge. TypeId is the annotation normally
// supplied by Core's schema preprocessing; the source examples omit it.
static class GameObjectPassiveMindControlSmoke
{
    static string outputDirectory;
    const string Filter = "<TargetFilter Rule='ANY' Relationship='ENEMIES' Include='INFANTRY VEHICLE'/>";
    const string ReferencedFilter = "<TargetFilter Rule='ANY' Relationship='ENEMIES' Include='INFANTRY VEHICLE' StatusBitFlags='DAMAGED'><IncludeThing>SovietConscript</IncludeThing><ExcludeThing>JapanCommando</ExcludeThing></TargetFilter>";
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static InstanceDeclaration Declaration(XmlElement element)
    {
        var ns = new XmlNamespaceManager(element.OwnerDocument.NameTable);
        ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var document = new AssetDeclarationDocument();
        var stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        var result = new InstanceDeclaration(document); result.XmlNode = element; return result;
    }
    static InstanceDeclaration Declaration(string attributes, string children)
    {
        var document = new XmlDocument();
        document.LoadXml("<GameObject xmlns='uri:ea.com:eala:asset' id='MindControlSmoke' " + attributes + ">" + children + "</GameObject>");
        return Declaration(document.DocumentElement);
    }
    static InstanceDeclaration Module(string attributes, string filter)
    {
        return Declaration("", "<Behaviors><PassiveMindControlUpdate TypeId='4000878923' id='ModuleTag_PassiveMindControl' " + attributes + ">" + filter + "</PassiveMindControlUpdate></Behaviors><Body><ActiveBody TypeId='1510882715' id='Body' MaxHealth='100'/></Body>");
    }
    static uint Word(byte[] b, int at, bool big)
    {
        return big ? ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3] : BitConverter.ToUInt32(b, at);
    }
    static int Data(byte[] b, bool big)
    {
        int root = (int)Word(b, 8, big) - 4;
        int header = (int)Word(b, root + 468, big);
        int slots = (int)Word(b, header + 4, big);
        return (int)Word(b, slots, big);
    }
    static int Token(byte[] b, int at, uint name, bool big)
    {
        for (; Word(b, at, big) != 0; at += 12) if (Word(b, at, big) == name) return at;
        throw new Exception("Missing token " + name);
    }
    static void Equal(byte[] expected, byte[] actual, string label)
    {
        Require(expected.Length == actual.Length, label + " length");
        for (int i = 0; i < expected.Length; ++i) Require(expected[i] == actual[i], label + " byte " + i);
    }
    static AssetBuffer Check(InstanceDeclaration input, TargetPlatform platform, uint capacity, uint interval)
    {
        PluginSchemaBoundary.Validate(input.Node);
        var result = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
        bool big = platform != TargetPlatform.Win32;
        byte[] b = result.InstanceData;
        int data = Data(b, big);
        Require(Word(b, data, big) == 0xEE78914Bu, "New native module recognized by XML bridge");
        Require(Word(b, data + 8, big) == capacity && Word(b, data + 12, big) == 0x43480000u && Word(b, data + 16, big) == interval, "XML scalars and defaults");
        Require(Word(b, data + 24, big) == 2 && Word(b, data + 28, big) == 2, "ObjectFilter attributes survive bridge");
        int root = (int)Word(b, 8, big) - 4, wrapper = -1;
        for (int at = 0; at + 12 < root; at += 12) if (Word(b, at, big) == 2 && Word(b, at + 4, big) == 0xEE78914Bu) wrapper = at;
        Require(wrapper >= 0, "Polymorphic wrapper");
        int table = (int)Word(b, wrapper + 8, big);
        int filterTable = (int)Word(b, Token(b, table, 0xB73A2D5Fu, big) + 8, big);
        Require(Word(b, Token(b, filterTable, 2363267224u, big) + 8, big) == data + 20, "Inline mode 8 filter");
        Require(result.ImportsData.Length == 0, "Tokenized imports");
        return result;
    }
    static void NativeEquality(AssetBuffer result, TargetPlatform platform, uint capacity)
    {
        bool big = platform != TargetPlatform.Win32;
        string stem = Path.Combine(outputDirectory, "mind-control-capacity-" + capacity + (big ? ".be" : ".le"));
        Equal(File.ReadAllBytes(stem + ".bin"), result.InstanceData, "Real XML/native instance");
        Equal(File.ReadAllBytes(stem + ".relo"), result.RelocationData, "Real XML/native relocation");
        Equal(File.ReadAllBytes(stem + ".imp"), result.ImportsData, "Real XML/native imports");
    }
    static void Legacy(IAssetBuilderPlugin original, TargetPlatform platform)
    {
        original.Initialize(null, platform);
        var inputs = new[] {
            Declaration("", ""),
            Declaration("Description='TowerText' Browser='a bb' BuildTime='23' CrowdResponse='Response\\23'", ""),
            Declaration("KindOf='STRUCTURE'", "<Body><ActiveBody TypeId='1510882715' id='Body' MaxHealth='100'/></Body>"),
            Declaration("", "<Behaviors><SlavedUpdate TypeId='3370197599' id='Slave' LeashRange='17' WeldingFXBone='Bone'><GuardPosOffset x='1' y='2' z='3'/></SlavedUpdate></Behaviors>")
        };
        foreach (var input in inputs)
        {
            var expected = original.ProcessInstance(input);
            var actual = Recovered.GameObjectCompiler.ProcessInstance(input, platform);
            Equal(expected.InstanceData, actual.InstanceData, "Legacy original instance");
            Equal(expected.RelocationData, actual.RelocationData, "Legacy original relocation");
            Equal(expected.ImportsData, actual.ImportsData, "Legacy original imports");
        }
    }
    static void Reject(InstanceDeclaration input, bool requireSchemaRejection = true)
    {
        bool schemaRejected = false, compilerRejected = false;
        try { PluginSchemaBoundary.Validate(input.Node); } catch (NotSupportedException) { schemaRejected = true; }
        try { Recovered.GameObjectCompiler.ProcessInstance(input, TargetPlatform.Win32); } catch (Exception) { compilerRejected = true; }
        Require((schemaRejected || !requireSchemaRejection) && compilerRejected, "Invalid module accepted at schema/native XML boundary (schemaRejected=" + schemaRejected + ", compilerRejected=" + compilerRejected + "): " + input.Node.OuterXml);
    }
    static void EqualBuffers(AssetBuffer expected, AssetBuffer actual, string label)
    {
        Equal(expected.InstanceData, actual.InstanceData, label + " instance");
        Equal(expected.RelocationData, actual.RelocationData, label + " relocation");
        Equal(expected.ImportsData, actual.ImportsData, label + " imports");
    }
    static void AnnotateProduction(XmlElement root)
    {
        // The schema preprocessor supplies these existing native module TypeIds
        // in production. Keep the example itself free of compiler annotations.
        string[] names = { "PassiveMindControlUpdate", "WeaponSetUpdate", "AIUpdate", "ActiveBody", "ScriptedModelDraw", "DestroyDie", "StancesBehavior", "DamageNugget" };
        string[] types = { "4000878923", "1125589673", "148619278", "1510882715", "1322199627", "259966977", "3231835197", "3237297817" };
        for (int i = 0; i < names.Length; ++i)
            foreach (XmlElement module in root.GetElementsByTagName(names[i], "uri:ea.com:eala:asset"))
                module.SetAttribute("TypeId", types[i]);
    }
    static XmlElement PreparedExample(XmlElement source)
    {
        var prepared = (XmlElement)source.CloneNode(true);
        AnnotateProduction(prepared);
        // This component harness bypasses Core's dependency resolution as well
        // as its TypeId annotation. Supply deterministic zero-based dependency
        // indexes on a clone; source examples must retain actual asset names.
        foreach (XmlElement hint in prepared.GetElementsByTagName("WeaponAiHintInfo", "uri:ea.com:eala:asset"))
        {
            Require(hint.GetAttribute("UseAsWarheadForDamageCalculations") == "PassiveMindControlDamageEstimateOnly", "Dedicated estimate reference");
            hint.SetAttribute("UseAsWarheadForDamageCalculations", "PassiveMindControlDamageEstimateOnly\\0");
        }
        foreach (XmlElement weapon in prepared.GetElementsByTagName("Weapon", "uri:ea.com:eala:asset"))
        {
            Require(weapon.GetAttribute("Template") == "PassiveMindControlAssignedTargetWeapon", "Only harmless weapon is mounted");
            weapon.SetAttribute("Template", "PassiveMindControlAssignedTargetWeapon\\1");
        }
        foreach (XmlElement model in prepared.GetElementsByTagName("Model", "uri:ea.com:eala:asset"))
        {
            Require(model.GetAttribute("Name") == "ABBaseDefenseAdvanced_SKN", "Stock sample model");
            model.SetAttribute("Name", "ABBaseDefenseAdvanced_SKN\\2");
        }
        foreach (XmlElement stance in prepared.GetElementsByTagName("StancesBehavior", "uri:ea.com:eala:asset"))
        {
            Require(stance.GetAttribute("StanceTemplate") == "OffensiveStructures", "Stock structure stance");
            stance.SetAttribute("StanceTemplate", "OffensiveStructures\\3");
        }
        return prepared;
    }
    static void ProductionWeapon(XmlElement node, IAssetBuilderPlugin original, TargetPlatform platform)
    {
        PluginSchemaBoundary.Validate(node);
        var input = Declaration(PreparedExample(node));
        var result = Recovered.WeaponCompiler.ProcessInstance(input, platform);
        EqualBuffers(original.ProcessInstance(input), result, "Production harmless/estimate weapon original DLL");
        bool big = platform != TargetPlatform.Win32;
        byte[] b = result.InstanceData;
        int root = (int)Word(b, 8, big) - 4;
        Require(Word(b, root + 4, big) == 0x43480000u, "Weapon range matches capture radius");
        if (node.GetAttribute("id") == "PassiveMindControlAssignedTargetWeapon")
        {
            Require(node.GetElementsByTagName("Nuggets", "uri:ea.com:eala:asset")[0].ChildNodes.Count == 0, "Mounted weapon has no effects");
            Require(Word(b, root + 256, big) == 0 && Word(b, root + 260, big) == 0, "Mounted native Nugget count/pointer stay zero");
            var hintNode = (XmlElement)node.GetElementsByTagName("WeaponAiHintInfo", "uri:ea.com:eala:asset")[0];
            Require(hintNode.GetAttribute("UseAsWarheadForDamageCalculations") == "PassiveMindControlDamageEstimateOnly", "Dedicated estimate reference");
            int hint = (int)Word(b, root + 252, big);
            Require(hint > root && Word(b, hint + 4, big) == 1, "Native damage estimate resolves dependency index zero as index+1");
            int hintTable = (int)Word(b, Token(b, 0, 0xD3A7C780u, big) + 8, big);
            int warheadToken = Token(b, hintTable, 3539729073u, big);
            Require(Word(b, warheadToken + 4, big) == 3494305058u && Word(b, warheadToken + 8, big) == hint + 4, "Native weapon-reference token points at the resolved estimate dependency");
        }
        else
        {
            Require(node.GetAttribute("id") == "PassiveMindControlDamageEstimateOnly", "Only dedicated sample weapons");
            Require(Word(b, root + 256, big) == 1, "One estimate-only Nugget");
            int list = (int)Word(b, root + 260, big), nugget = (int)Word(b, list, big);
            Require(Word(b, nugget, big) == 3237297817u && Word(b, nugget + 40, big) == 0x3f800000u, "Estimate-only damage is nonzero");
        }
        string stem = Path.Combine(outputDirectory, node.GetAttribute("id") + "-" + (int)platform);
        File.WriteAllBytes(stem + ".bin", result.InstanceData);
        File.WriteAllBytes(stem + ".relo", result.RelocationData);
        File.WriteAllBytes(stem + ".imp", result.ImportsData);
    }
    static void ProductionExamples(string path, IAssetBuilderPlugin original, TargetPlatform platform)
    {
        var document = new XmlDocument(); document.Load(path);
        original.Initialize(null, platform);
        int count = 0, weapons = 0;
        foreach (XmlNode node in document.DocumentElement.ChildNodes)
        {
            var objectNode = node as XmlElement;
            if (objectNode == null) continue;
            if (objectNode.LocalName == "WeaponTemplate")
            {
                ProductionWeapon(objectNode, original, platform); ++weapons; continue;
            }
            if (objectNode.LocalName != "GameObject") continue;
            var module = (XmlElement)objectNode.GetElementsByTagName("PassiveMindControlUpdate", "uri:ea.com:eala:asset")[0];
            uint capacity = UInt32.Parse(module.GetAttribute("Capacity"));
            PluginSchemaBoundary.Validate(objectNode);
            Require(objectNode.GetAttribute("CommandSet") == "GenericCommandSet", "Ordinary Stop command set");
            var mounted = objectNode.GetElementsByTagName("Weapon", "uri:ea.com:eala:asset");
            Require(mounted.Count == 1 && ((XmlElement)mounted[0]).GetAttribute("Template") == "PassiveMindControlAssignedTargetWeapon", "Estimate-only weapon is never mounted");
            Require(objectNode.GetElementsByTagName("AIUpdate", "uri:ea.com:eala:asset")[0].ParentNode.LocalName == "AI", "Ordinary AI node");
            var prepared = PreparedExample(objectNode);
            var result = Check(Declaration(prepared), platform, capacity, 15);
            bool big = platform != TargetPlatform.Win32;
            byte[] b = result.InstanceData;
            int root = (int)Word(b, 8, big) - 4;
            Require((Word(b, root + 4, big) & (1u << 5)) != 0, "Native CAN_ATTACK flag");
            int behaviors = (int)Word(b, root + 468, big), behaviorList = (int)Word(b, behaviors + 4, big), weaponSet = 0;
            for (int i = 0; i < Word(b, behaviors, big); ++i)
            {
                int moduleData = (int)Word(b, behaviorList + 4*i, big);
                if (Word(b, moduleData, big) == 1125589673u) weaponSet = moduleData;
            }
            Require(weaponSet != 0 && Word(b, weaponSet + 8, big) == 1, "One native ordinary hardpoint");
            Require(Word(b, weaponSet + 16, big) == 0 && Word(b, weaponSet + 24, big) == 0 && Word(b, weaponSet + 32, big) == 0, "No extra native weapon slots");
            int hardpoint = (int)Word(b, weaponSet + 12, big), mountedData = (int)Word(b, hardpoint + 20, big);
            Require(Word(b, hardpoint + 16, big) == 1 && Word(b, mountedData, big) == 0 && Word(b, mountedData + 4, big) == 2, "Native primary weapon resolves harmless dependency index one; estimate is not mounted");
            Require(Word(b, root + 472, big) == 1, "One ordinary AI update");
            int aiList = (int)Word(b, root + 476, big), ai = (int)Word(b, aiList, big);
            Require(Word(b, ai, big) == 148619278u && Word(b, ai + 16, big) == 4 && Word(b, ai + 60, big) == 5, "Native AI disables idle acquisition and uses offensive structure state machine");
            string stem = Path.Combine(outputDirectory, "example-" + capacity + "-" + (int)platform);
            File.WriteAllBytes(stem + ".bin", result.InstanceData);
            File.WriteAllBytes(stem + ".relo", result.RelocationData);
            File.WriteAllBytes(stem + ".imp", result.ImportsData);
            // The unextended attack/model/body/geometry backbone is native SDK
            // behavior: compare all three buffers with the original DLL too.
            var legacy = (XmlElement)prepared.CloneNode(true);
            var extension = legacy.GetElementsByTagName("PassiveMindControlUpdate", "uri:ea.com:eala:asset")[0];
            extension.ParentNode.RemoveChild(extension);
            var legacyInput = Declaration(legacy);
            EqualBuffers(original.ProcessInstance(legacyInput), Recovered.GameObjectCompiler.ProcessInstance(legacyInput, platform), "Complete native tower backbone original DLL");
            ++count;
        }
        Require(count == 2 && weapons == 2, "Production capacity 3/8 towers and harmless/estimate weapon pair");
    }
    static int Main(string[] args)
    {
        try
        {
            outputDirectory = args[0];
            var original = (IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(args[1]).GetType("BinaryAssetBuilder.Tokenizer.Plugin"));
            foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 })
            {
                var plugin = new Recovered.GameObject.Plugin(); plugin.Initialize(null, platform);
                var info = plugin.GetExtendedTypeInformation(2486173485u);
                uint version = platform == TargetPlatform.PlayStation3 ? 4105u : platform == TargetPlatform.Xbox360 ? 4104u : 4103u;
                Require(plugin.GetVersionNumber() == version && info.TypeId == 2486173485u && info.TypeHash == 3415017856u && info.ProcessingHash == unchecked(3415017856u + version), "GameObject cache version changes without TypeId/TypeHash changes");
                foreach (uint capacity in new uint[] { 1, 3, 8, UInt32.MaxValue })
                    Check(Module("Capacity='" + capacity + "' AcquireRadius='200'", Filter), platform, capacity, 15);
                Check(Module("AcquireRadius='200'", Filter), platform, 3, 15);
                Check(Module("Capacity='0008' AcquireRadius='200' CaptureIntervalFrames='1073741822'", Filter), platform, 8, 1073741822);
                foreach (uint capacity in new uint[] { 3, 8 })
                    NativeEquality(Check(Module("Capacity='" + capacity + "' AcquireRadius='200'", ReferencedFilter), platform, capacity, 15), platform, capacity);
                ProductionExamples(args[2], original, platform);
                Legacy(original, platform);
            }
            foreach (string value in new[] { "0", "-1", "4294967296", "1.5", "text" }) Reject(Module("AcquireRadius='200' Capacity='" + value + "'", Filter));
            foreach (string value in new[] { "0", "-1", "NaN", "INF", "-INF", "1e39" }) Reject(Module("AcquireRadius='" + value + "'", Filter));
            foreach (string value in new[] { "1junk", "1e", "0x1p1", "1D2" }) Reject(Module("AcquireRadius='" + value + "'", Filter), false);
            foreach (string value in new[] { "0", "-1", "1073741823", "4294967296" }) Reject(Module("AcquireRadius='200' CaptureIntervalFrames='" + value + "'", Filter));
            Reject(Module("", Filter)); Reject(Module("AcquireRadius='200'", "")); Reject(Module("AcquireRadius='200'", Filter + Filter));
            Console.WriteLine("PASS passive mind control real XML bridge: four platforms; capacities/defaults/invalid values; template filters match native three buffers; complete capacity 3/8 ordinary-attack towers; empty effects and estimate-only weapons; native backbone/weapons and legacy original DLL three-buffer compatibility");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
