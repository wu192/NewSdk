using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;

class XmlpowersOracle
{
    const string Ns = "uri:ea.com:eala:asset";
    static IAssetBuilderPlugin original;
    static int fixtures, comparisons, rejections, independent;
    static string current = "";
    static string repo;
    static readonly Dictionary<string, XmlElement> definitions = new Dictionary<string, XmlElement>();
    static readonly Dictionary<string, string[]> enumNames = new Dictionary<string, string[]>();

    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager namespaces)
    {
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration declaration = new InstanceDeclaration(document);
        declaration.XmlNode = xml.DocumentElement;
        return declaration;
    }
    static XmlNamespaceManager Namespaces(XmlDocument xml)
    {
        XmlNamespaceManager namespaces = new XmlNamespaceManager(xml.NameTable);
        namespaces.AddNamespace("ea", Ns);
        return namespaces;
    }
    static AssetBuffer Recovered(XmlDocument xml, int platform)
    {
        InstanceDeclaration declaration = Declaration(xml, Namespaces(xml));
        return xml.DocumentElement.LocalName == "SpecialPowerTemplate"
            ? SpecialPowerTemplateCompiler.ProcessInstance(declaration, (TargetPlatform)platform)
            : ObjectCreationListCompiler.ProcessInstance(declaration, (TargetPlatform)platform);
    }
    static void Equal(string name, byte[] expected, byte[] actual)
    {
        if (expected == null || actual == null || expected.Length != actual.Length)
            throw new Exception(current + "/" + name + " lengths " + (expected == null ? -1 : expected.Length) + "/" + (actual == null ? -1 : actual.Length));
        for (int i = 0; i < actual.Length; ++i)
            if (actual[i] != expected[i]) throw new Exception(current + "/" + name + " byte " + i + " original=" + expected[i] + " recovered=" + actual[i]);
        ++comparisons;
    }
    static uint Word(byte[] bytes, int at, bool big)
    {
        if (at < 0 || at + 4 > bytes.Length) throw new Exception(current + " word outside buffer");
        if (!big) return BitConverter.ToUInt32(bytes, at);
        return ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3];
    }
    static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(current + "/layout " + name);
        ++independent;
    }
    static void Bookmarks(AssetBuffer b, bool big)
    {
        foreach (byte[] table in new byte[][] { b.RelocationData, b.ImportsData }) {
            Assert(table.Length % 4 == 0, "bookmark word size");
            if (table.Length == 0) continue;
            Assert(Word(table, table.Length - 4, big) == UInt32.MaxValue, "bookmark terminator");
            for (int i = 0; i + 4 < table.Length; i += 4) {
                uint offset = Word(table, i, big);
                Assert(offset % 4 == 0 && offset + 4 <= b.InstanceData.Length, "bookmark location");
                if (Object.ReferenceEquals(table, b.RelocationData))
                    Assert(Word(b.InstanceData, (int)offset, big) < b.InstanceData.Length, "relative pointer destination");
            }
        }
    }
    static AssetBuffer Test(string label, string xmlText, int platform)
    {
        current = label + " platform=" + platform;
        XmlDocument xml = new XmlDocument(); xml.LoadXml(xmlText);
        original.Initialize(null, (TargetPlatform)platform);
        AssetBuffer expected = original.ProcessInstance(Declaration(xml, Namespaces(xml)));
        AssetBuffer actual = Recovered(xml, platform);
        Equal("instance", expected.InstanceData, actual.InstanceData);
        Equal("relocations", expected.RelocationData, actual.RelocationData);
        Equal("imports", expected.ImportsData, actual.ImportsData);
        ++fixtures;
        Bookmarks(actual, platform != 0);
        return actual;
    }
    static string Escape(string value)
    {
        return value.Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
    static string Root(string type, string attributes, string children)
    {
        return "<" + type + " xmlns='" + Ns + "' id='Oracle' " + attributes + ">" + children + "</" + type + ">";
    }
    static string Power(string attributes, string children) { return Root("SpecialPowerTemplate", attributes, children); }
    static string Ocl(string children) { return Root("ObjectCreationList", "", children); }
    static void Reject(string xmlText, int platform)
    {
        current = "rejection platform=" + platform + " " + xmlText;
        XmlDocument xml = new XmlDocument(); xml.LoadXml(xmlText);
        try { PluginSchemaBoundary.Validate(xml);  Recovered(xml, platform); }
        catch (NotSupportedException) { ++rejections; return; }
        throw new Exception("Unsupported input accepted: " + current);
    }
    static void LoadSchemas()
    {
        foreach (string file in new string[] { "AssetTypeSpecialPowerTemplate.xsd", "AssetTypeObjectCreationList.xsd" }) {
            XmlDocument schema = new XmlDocument();
            schema.Load(Path.Combine(repo, "SDK/Schemas/xsd/" + file));
            XmlNamespaceManager manager = new XmlNamespaceManager(schema.NameTable);
            manager.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
            foreach (XmlElement type in schema.SelectNodes("//xs:complexType", manager)) definitions[type.GetAttribute("name")] = type;
            foreach (XmlElement type in schema.SelectNodes("//xs:simpleType", manager)) {
                List<string> values = new List<string>();
                foreach (XmlElement item in type.SelectNodes(".//xs:enumeration", manager)) values.Add(item.GetAttribute("value"));
                if (values.Count != 0) enumNames[type.GetAttribute("name")] = values.ToArray();
            }
        }
        enumNames["WeaponSlotType"] = new string[] { "PRIMARY_WEAPON", "SECONDARY_WEAPON", "TERTIARY_WEAPON", "QUATERNARY_WEAPON", "QUINARY_WEAPON", "NO_WEAPON" };
        // Recovered dependency tables are independently swept against XmlCompiler,
        // including names not used by either of these root schemas.
        string enumSource = File.ReadAllText(Path.Combine(repo, "recovery/src/Native/XmlpowersEnums.h"));
        foreach (string entry in new string[] { "modelConditions:ModelConditionFlagType", "objectStatus:ObjectStatusType" }) {
            string[] pair = entry.Split(':');
            int begin = enumSource.IndexOf("const EnumNameValue " + pair[0] + "[]");
            int end = enumSource.IndexOf("};", begin);
            string body = enumSource.Substring(begin, end - begin);
            List<string> values = new List<string>();
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(body, "\\{\\s*\"([^\"]+)\""))
                values.Add(match.Groups[1].Value);
            enumNames[pair[1]] = values.ToArray();
        }
    }
    static IEnumerable<string> Values(string type)
    {
        string baseEnum = type;
        if (type == "SpecialPowerTemplateBitFlag") baseEnum = "SpecialPowerTemplateFlag";
        if (type == "OCLBitFlags") baseEnum = "OCLType";
        if (type == "OCNuggetOptionFlag") baseEnum = "OCNuggetOption";
        if (type == "DispositionTypeFlag") baseEnum = "DispositionType";
        if (type == "ParabolicOptionFlag") baseEnum = "ParabolicOption";
        if (type == "ObjectStatusBitFlags") baseEnum = "ObjectStatusType";
        if (type == "ModelConditionBitFlags") baseEnum = "ModelConditionFlagType";
        string[] values;
        if (enumNames.TryGetValue(baseEnum, out values)) {
            foreach (string v in values) yield return v;
            foreach (string v in new string[] { "", "unknown", "ALL", "all", "-ALL", "ALL -ALL", " " + values[0], values[0] + " ", "+" + values[0], "-" + values[0], "-" + values[0] + " -" + values[0], values[0] + " unknown", values[0].ToLowerInvariant() }) yield return v;
            yield break;
        }
        if (type.EndsWith("Ref", StringComparison.Ordinal)) {
            foreach (string v in new string[] { "", "SomeAsset", "Asset\\0", "Asset\\1", "Asset\\27", "Asset\\-1", "Asset\\0x10", "Asset\\junk", "Asset\\2147483647", "Asset\\2147483648", "Other\\3\\4" }) yield return v;
            yield break;
        }
        if (type == "SageBool") {
            foreach (string v in new string[] { "", "true", "false", "1", "0", "True", "TRUE", "turtle", "2", " true", "garbage" }) yield return v;
            yield break;
        }
        if (type == "xs:string" || type == "StringHash") {
            foreach (string v in new string[] { "", "Alpha", "alpha", " Alpha ", "abcd", "abcde", "abcdefgh", "A&amp;B" }) yield return v;
            yield break;
        }
        foreach (string v in new string[] { "", "0", "-0", "1", "-1", "12.375", "16777217", "1e-7", "2d1", "1e30", "1e-30", " 25.75junk", "25,5", "garbage", "0x10", "077", "-4294967296", "4294967296", "2147483648" }) yield return v;
        if (type == "Time") foreach (string v in new string[] { "125ms", "2s", "2ms s", "1s ms" }) yield return v;
        if (type == "Angle") foreach (string v in new string[] { "90d", "-180d", "2r", "2d1", "359d" }) yield return v;
        if (type == "Percentage") foreach (string v in new string[] { "33.33333%", "100%", "-12.5%" }) yield return v;
    }
    static void Attributes(string typeName, string elementName, int platform)
    {
        XmlElement definition = definitions[typeName];
        XmlNamespaceManager manager = new XmlNamespaceManager(definition.OwnerDocument.NameTable);
        manager.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
        foreach (XmlElement attribute in definition.SelectNodes(".//xs:attribute", manager)) {
            string name = attribute.GetAttribute("name");
            foreach (string value in Values(attribute.GetAttribute("type"))) {
                string attrs = name + "='" + Escape(value) + "'";
                string xml = typeName == "SpecialPowerTemplate" ? Power(attrs, "") :
                    typeName == "ObjectCreationList" ? Root("ObjectCreationList", attrs, "") :
                    Ocl("<" + elementName + " " + attrs + "/>");
                Test(typeName + "." + name + "=" + value, xml, platform);
            }
            string bad = "Unrecovered" + name + "='x'";
            Reject(typeName == "SpecialPowerTemplate" ? Power(bad, "") :
                typeName == "ObjectCreationList" ? Root("ObjectCreationList", bad, "") : Ocl("<" + elementName + " " + bad + "/>"), platform);
        }
    }
    static void Layouts(int platform)
    {
        bool big = platform != 0;
        AssetBuffer b = Test("power/defaults", Power("", ""), platform);
        Assert(b.InstanceData.Length == 552 && b.RelocationData.Length == 0 && b.ImportsData.Length == 0, "power root no tokens");
        Assert(Word(b.InstanceData, 24, big) == 1 && Word(b.InstanceData, 228, big) == 0x49742400 && b.InstanceData[548] == 1, "power defaults");
        b = Test("ocl/defaults", Ocl(""), platform);
        Assert(b.InstanceData.Length == 56 && Word(b.InstanceData, 4, big) == 1 && b.RelocationData.Length == 0 && b.ImportsData.Length == 0, "ocl root no tokens");
        b = Test("power/string-layout", Power("DisplayName='abc'", ""), platform);
        Assert(b.InstanceData.Length == 556 && Word(b.InstanceData, 4, big) == 3 && Word(b.InstanceData, 8, big) == 552 && b.InstanceData[552] == 97 && b.InstanceData[555] == 0, "string count pointer bytes");
        Assert(b.RelocationData.Length == 8 && Word(b.RelocationData, 0, big) == 8, "string relocation");
        b = Test("power/import-layout", Power("InitiateSound='Audio\\0' RequiredPlayerTech='Tech\\7'", ""), platform);
        Assert(b.InstanceData.Length == 552 && Word(b.InstanceData, 36, big) == 1 && Word(b.InstanceData, 308, big) == 8 && b.ImportsData.Length == 12, "strong imports preserved");
        Assert(Word(b.ImportsData, 0, big) == 36 && Word(b.ImportsData, 4, big) == 308, "strong import sites");
        b = Test("ocl/all-default-nuggets", Ocl("<CreateObject/><Attack/><FireWeapon/><FlingObject/><StrafeAreaSquad/><ParabolicCurve/>"), platform);
        int offset = 56;
        int[] sizes = new int[] {248,16,8,52,32,48};
        for (int group = 0; group < 6; ++group) {
            Assert(Word(b.InstanceData, 8 + 8 * group, big) == 1 && Word(b.InstanceData, 12 + 8 * group, big) == offset, "nugget list stride " + group);
            Assert(Word(b.InstanceData, offset, big) == 0, "nugget base zero");
            offset += sizes[group];
        }
        Assert(b.InstanceData.Length == offset && b.RelocationData.Length == 28 && b.ImportsData.Length == 0, "all nugget sizes");
        b = Test("power/voices", Power("NameOfVoiceNameToUseAsInitiateIntendToDoVoice='Alpha' NameOfVoiceNameToUseAsInitiateIntendToDoAfterMoveVoice='alpha'", ""), platform);
        Assert(b.InstanceData.Length == 560 && Word(b.InstanceData, 260, big) == 552 && Word(b.InstanceData, 264, big) == 556, "nullable voice hashes");
        Assert(Word(b.InstanceData, 552, big) != Word(b.InstanceData, 556, big), "voice hashes preserve case");
    }
    static void Children(int platform)
    {
        string filter = "<ObjectFilter Rule='ALL' Relationship='ALLIES' Include='INFANTRY' StatusBitFlags='UNSELECTABLE'><IncludeThing>Tank</IncludeThing><ExcludeThing>Jet</ExcludeThing></ObjectFilter>";
        Test("power/full-filters", Power("", filter + filter.Replace("ObjectFilter", "ForbiddenObjectFilter")), platform);
        foreach(string id in new[]{"Dependency_Metadata","MiXeD_Id",""})
            Test("power/dependency-pipeline-id/"+id, Power("", "<GameDependency id='"+id+"'><RequiredObject>Tank</RequiredObject></GameDependency>"), platform);
        Test("power/dependency", Power("", "<GameDependency RequiredModelConditionsAny='MOVING' ForbiddenModelConditions='DAMAGED' RequiredObjectStatusAny='UNSELECTABLE'><RequiredObject>Tank</RequiredObject><RequiredObject>tank</RequiredObject><ForbiddenUpgrade>Upg</ForbiddenUpgrade><NeededUpgrade>Other</NeededUpgrade>" + filter + "</GameDependency><ForbiddenPlayerTech>Tech\\3</ForbiddenPlayerTech><ForbiddenPlayerTech>Tech\\0</ForbiddenPlayerTech>"), platform);
        foreach (string attr in new string[] { "RequiredModelConditionsAny", "ForbiddenModelConditions", "RequiredObjectStatusAny" })
            foreach (string value in new string[] { "", "ALL", "unknown", "+MOVING -MOVING -MOVING" })
                Test("dependency-mask/" + attr + "/" + value, Power("", "<GameDependency " + attr + "='" + value + "'/>"), platform);
        string fullCreate = "<CreateObject DestinationPlayer='Alpha' WaypointSpawn='Here' CreateFX='Fx\\0' FadeSound='Audio\\2' FireSpecialPowerTemplate='Power\\9' StatusToCopyFromParent='UNSELECTABLE'><Offset x='1.25' y='-2.5' z='3.75'/><RequiredUpgrade>Upg\\0</RequiredUpgrade><ForbiddenUpgrade>Upg\\4</ForbiddenUpgrade><CreateObject>Tank</CreateObject><CreateObject>tank</CreateObject></CreateObject>";
        Test("ocl/create/full", Ocl(fullCreate), platform);
        Test("ocl/nested", Ocl("<FlingObject CreateObject='Tank' Speed='12.5'><Offset x='2' y='3' z='4'/><DistanceFromTarget Low='-5' High='8'/><AngleAroundTarget Low='0' High='6'/><DistanceAtTarget Low='2' High='3'/></FlingObject><StrafeAreaSquad CreateObject='Plane'><InterOffset x='1' y='2' z='3'/><IntraOffset x='4' y='5' z='6'/></StrafeAreaSquad><ParabolicCurve CreateObject='Plane'><Offset x='7' y='8' z='9'/></ParabolicCurve>"), platform);
        foreach (int count in new int[] { 1,2,27,28,29,64,257 }) {
            StringBuilder children = new StringBuilder();
            for (int i = 0; i < count; ++i) children.Append(i % 3 == 0 ? fullCreate : i % 3 == 1 ? "<Attack NumberOfShots='3' WeaponSlot='SECONDARY_WEAPON'/>" : "<FireWeapon Weapon='Weapon\\3'/>");
            Test("ocl/lists-" + count, Ocl(children.ToString()), platform);
            StringBuilder refs = new StringBuilder();
            for (int i = 0; i < count; ++i) refs.Append("<ForbiddenPlayerTech>Tech\\").Append(i % 7).Append("</ForbiddenPlayerTech>");
            Test("power/list-" + count, Power("DisplayName='list'", refs.ToString()), platform);
        }
        foreach (string typeId in new string[] { "0", "1", "2178440954", "3899542881", "4294967295", "unknown" }) {
            Test("power/TypeId-" + typeId, Power("TypeId='" + typeId + "'", ""), platform);
            foreach (string nugget in new string[] { "CreateObject", "Attack", "FireWeapon", "FlingObject", "StrafeAreaSquad", "ParabolicCurve" })
                Test("ocl/typed-list-TypeId-" + nugget + "/" + typeId, Ocl("<" + nugget + " TypeId='" + typeId + "'/>"), platform);
        }
        foreach (string child in new string[] { "<Unknown/>", "<ObjectFilter/><ObjectFilter/>", "<GameDependency/><GameDependency/>", "<ForbiddenPlayerTech Bad='1'/>", "<ObjectFilter Wrong='1'/>", "<ObjectFilter><Unknown/></ObjectFilter>", "<ObjectFilter><IncludeThing><X/></IncludeThing></ObjectFilter>", "<GameDependency Bad='1'/>", "<GameDependency><Unknown/></GameDependency>", "<GameDependency><ObjectFilter/><ObjectFilter/></GameDependency>", "<GameDependency><NeededUpgrade Bad='1'/></GameDependency>", "<ObjectFilter xmlns='other'/>", "mixed" })
            Reject(Power("", child), platform);
        foreach (string child in new string[] { "<Unknown/>", "<Attack Bad='1'/>", "<Attack><Offset/></Attack>", "<CreateObject><Offset/><Offset/></CreateObject>", "<CreateObject><Offset q='1'/></CreateObject>", "<CreateObject><RequiredUpgrade Bad='1'/></CreateObject>", "<FlingObject><DistanceAtTarget/><DistanceAtTarget/></FlingObject>", "<StrafeAreaSquad><InterOffset Bad='1'/></StrafeAreaSquad>", "<ParabolicCurve><Offset><X/></Offset></ParabolicCurve>", "<Attack xmlns=''/>", "mixed" })
            Reject(Ocl(child), platform);
        Reject(Power("unknown='1'", ""), platform);
        Reject("<SpecialPowerTemplate xmlns='other' id='Oracle'/>", platform);
        Reject("<ObjectCreationList xmlns='" + Ns + "' id='Oracle' ea:Flags='NONE' xmlns:ea='" + Ns + "'/>", platform);
    }
    static void BabMetadata(int platform) {
        string[] sources={
            Power("","<ObjectFilter Rule='ANY' Include='INFANTRY'><IncludeThing>Thing\\1</IncludeThing><ExcludeThing>Thing\\2</ExcludeThing></ObjectFilter><GameDependency><RequiredObject>Tank\\3</RequiredObject><NeededUpgrade>Upg\\4</NeededUpgrade><ObjectFilter Rule='ALL'><IncludeThing>Thing\\5</IncludeThing></ObjectFilter></GameDependency><ForbiddenPlayerTech>Tech\\6</ForbiddenPlayerTech>"),
            Ocl("<CreateObject><Offset x='1' y='2' z='3'/><RequiredUpgrade>Upg\\1</RequiredUpgrade><ForbiddenUpgrade>Upg\\2</ForbiddenUpgrade><CreateObject>Tank</CreateObject></CreateObject><Attack/><FireWeapon/><FlingObject><Offset x='4' y='5' z='6'/><DistanceFromTarget Low='1' High='2'/><AngleAroundTarget Low='3' High='4'/><DistanceAtTarget Low='5' High='6'/></FlingObject><StrafeAreaSquad><InterOffset x='1' y='2' z='3'/><IntraOffset x='4' y='5' z='6'/></StrafeAreaSquad><ParabolicCurve><Offset x='7' y='8' z='9'/></ParabolicCurve>")
        };
        foreach(string source in sources)foreach(string value in new[]{"0","4294967295","metadata-not-a-number",""}) {
            var d=new XmlDocument();d.LoadXml(source);
            foreach(XmlElement node in d.SelectNodes("//*"))node.SetAttribute("TypeId",value);
            Test("BAB/all-named-element-TypeId-"+value,d.OuterXml,platform);
        }
        Reject(Power("xmlns:q='urn:metadata' q:TypeId='1'",""),platform);
        Reject(Power("","<ObjectFilter TypeId='1' Bad='1'/>"),platform);
        Reject(Power("","<GameDependency><NeededUpgrade xmlns:q='urn:metadata' q:TypeId='1'>Upg</NeededUpgrade></GameDependency>"),platform);
        Reject(Ocl("<CreateObject><Offset xmlns:q='urn:metadata' q:TypeId='1'/></CreateObject>"),platform);
    }
    static int Main(string[] args)
    {
        bool succeeded = false;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            repo = args[2];
            Assembly assembly = Assembly.LoadFrom(args[0]);
            original = (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            LoadSchemas();
            foreach (int platform in new int[] {0,1,2,99}) {
                int startFixtures = fixtures, startComparisons = comparisons, startRejections = rejections, startIndependent = independent;
                Layouts(platform);
                Attributes("SpecialPowerTemplate", "", platform);
                Attributes("ObjectCreationList", "", platform);
                foreach (string pair in new string[] { "CreateObjectNugget:CreateObject", "AttackNugget:Attack", "FireWeaponNugget:FireWeapon", "FlingObjectNugget:FlingObject", "StrafeAreaNugget:StrafeAreaSquad", "ParabolicCurveNugget:ParabolicCurve" }) {
                    string[] parts = pair.Split(':'); Attributes(parts[0], parts[1], platform);
                }
                Children(platform);
                BabMetadata(platform);
                Console.WriteLine("PASS platform={0} fixtures={1} comparisons={2} rejections={3} independent={4}", platform, fixtures-startFixtures, comparisons-startComparisons, rejections-startRejections, independent-startIndependent);
            }
            succeeded = true;
            Console.WriteLine("PASS Xmlpowers fixtures={0} comparisons={1} rejections={2} independent={3}; original XmlCompiler ProcessInstance for every fixture", fixtures, comparisons, rejections, independent);
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine("FAIL " + current); Console.Error.WriteLine(error);
            return 1;
        } finally {
            if (args.Length > 1) File.WriteAllText(args[1], "{\"succeeded\":" + (succeeded ? "true" : "false") + ",\"fixtures\":" + fixtures + ",\"comparisons\":" + comparisons + ",\"rejections\":" + rejections + ",\"independent\":" + independent + ",\"platforms\":[0,1,2,99]}");
        }
    }
}
