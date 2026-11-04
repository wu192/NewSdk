using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Application;

// Executed by the independent host linked from the actual production modules.
// Reflection keeps the prepared representation an internal application detail.
public static class CompilerPreparedBackend
{
    static int checks;
    static readonly List<object> lifecycleEvidence = new List<object>();
    static readonly Type contract = typeof(CoreStreamCompiler).Assembly.GetType("NewModCompiler.Application.PreparedBackendWork", true);
    static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    static object Prepare(InstanceDeclaration declaration, IAssetBuilderPlugin plugin)
    {
        var arguments = new object[] { declaration, plugin, null };
        bool accepted = (bool)contract.GetMethod("TryPrepare", BindingFlags.Public | BindingFlags.Static).Invoke(null, arguments);
        return accepted ? arguments[2] : null;
    }
    static object PrepareWithReason(InstanceDeclaration declaration, IAssetBuilderPlugin plugin, long budget, out string reason)
    {
        var arguments = new object[] { declaration, plugin, budget, null, null };
        bool accepted = (bool)contract.GetMethod("TryPrepareWithReason", BindingFlags.Public | BindingFlags.Static).Invoke(null, arguments);
        reason = (string)arguments[4];
        Require(accepted == (arguments[3] != null), "Preparation acceptance must match retained envelope presence");
        Require(accepted ? reason == null : !string.IsNullOrEmpty(reason), "Exactly refused preparations must have a reason");
        return arguments[3];
    }
    static void RejectedPreparation(InstanceDeclaration declaration, IAssetBuilderPlugin plugin, long budget, string expected)
    {
        string reason;
        object prepared = PrepareWithReason(declaration, plugin, budget, out reason);
        try { Require(prepared == null && reason == expected, "Expected exact preparation refusal " + expected + ", actual " + reason); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
    }
    static T Value<T>(object prepared, string property)
    {
        return (T)contract.GetProperty(property, BindingFlags.Public | BindingFlags.Instance).GetValue(prepared, null);
    }
    static AssetBuffer Execute(object prepared)
    {
        return (AssetBuffer)contract.GetMethod("Execute").Invoke(prepared, null);
    }
    static InstanceDeclaration Declaration(string type, string id, XmlNamespaceManager namespaces)
    {
        var xml = new XmlDocument { XmlResolver = null };
        var root = xml.CreateElement(type, "uri:ea.com:eala:asset");
        root.SetAttribute("id", id); xml.AppendChild(root);
        var declaration = new InstanceDeclaration(AssetDeclarationDocument.CreateBackendView(namespaces));
        declaration.XmlNode = root;
        return declaration;
    }
    static XmlNamespaceManager Namespaces()
    {
        var namespaces = new XmlNamespaceManager(new NameTable());
        namespaces.AddNamespace("ea", "uri:ea.com:eala:asset");
        return namespaces;
    }
    static void Metadata(InstanceDeclaration instance, IAssetBuilderPlugin plugin)
    {
        var information = plugin.GetExtendedTypeInformation(instance.Handle.TypeId);
        instance.Handle.TypeHash = information.TypeHash;
        instance.ProcessingHash = information.ProcessingHash;
        instance.HasCustomData = information.HasCustomData;
    }
    static bool Contains(byte[] data, byte[] expected)
    {
        for (int i = 0; i <= data.Length - expected.Length; ++i)
        {
            int match = 0;
            while (match < expected.Length && data[i + match] == expected[match]) match++;
            if (match == expected.Length) return true;
        }
        return false;
    }
    static void Denied(Action action, string operation)
    {
        bool denied = false;
        try { action(); }
        catch (IOException) { denied = true; }
        catch (UnauthorizedAccessException) { denied = true; }
        Require(denied, "Prepared input must reject " + operation + " while its content lease is held");
    }
    static void AptLeaseAndBorrow(string work)
    {
        string inputDirectory = Path.Combine(work, "prepared-input-lease");
        Directory.CreateDirectory(inputDirectory);
        string path = Path.Combine(inputDirectory, "prepared-blob.bin");
        string alternate = Path.Combine(work, "alternate-blob.bin");
        byte[] first = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOP");
        byte[] second = Encoding.ASCII.GetBytes("QRSTUVWXYZabcdef");
        File.WriteAllBytes(path, first); File.WriteAllBytes(alternate, second);
        var plugin = new Recovered.XmlPriority.Plugin();
        plugin.Initialize(null, TargetPlatform.Win32);
        var namespaces = Namespaces();
        var instance = Declaration("AptAptData", "PreparedApt", namespaces);
        ((XmlElement)instance.Node).SetAttribute("File", path); Metadata(instance, plugin);
        object prepared = Prepare(instance, plugin);
        Require(prepared != null, "Existing APT blob input must be parallel eligible");
        try
        {
            Require(prepared != null, "Bounded leased APT XML must be certified for independent backend execution");
            Denied(delegate { using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }, "a writer");
            Denied(delegate { File.Delete(path); }, "deletion");
            Denied(delegate { File.Move(path, path + ".moved"); }, "rename");
            Denied(delegate { Directory.Move(inputDirectory, inputDirectory + ".moved"); }, "ancestor directory rename");
            // Core finalizes XML before enqueue and joins before reuse/mutation.
            // Borrow that tree and its namespace table without a DOM copy.
            var frozen = Value<InstanceDeclaration>(prepared, "Declaration");
            Require(!Object.ReferenceEquals(frozen, instance) && !Object.ReferenceEquals(frozen.Document, instance.Document),
                "APT must retain private declaration and document metadata views");
            Require(Object.ReferenceEquals(frozen.Node, instance.Node), "APT must borrow the original XML node");
            Require(Object.ReferenceEquals(frozen.Document.NamespaceManager, namespaces), "Finalized namespace bindings must be borrowed");
            var result = Execute(prepared);
            Require(result != null && Contains(result.InstanceData, first) && !Contains(result.InstanceData, second), "Native APT consumption must match the held version");
        }
        finally { ((IDisposable)prepared).Dispose(); }
        ((XmlElement)instance.Node).SetAttribute("File", path);
        namespaces.AddNamespace("ea", "uri:ea.com:eala:asset");
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, second); File.SetLastWriteTimeUtc(path, timestamp);
        Require(new FileInfo(path).Length == first.Length && File.GetLastWriteTimeUtc(path) == timestamp, "Equal-length/mtime fixture must be exact");
        prepared = Prepare(instance, plugin);
        Require(prepared != null, "A released input lease must allow a new version");
        try { Require(Contains(Execute(prepared).InstanceData, second), "A fresh lease must consume equal-length equal-mtime changed bytes"); }
        finally { ((IDisposable)prepared).Dispose(); }
        File.Delete(path);
        Require(Prepare(instance, plugin) == null, "A missing APT read must use the compatibility path rather than a stale input lease");
    }
    static void EqualBuffers(AssetBuffer expected, AssetBuffer actual, string type)
    {
        Require(expected != null && actual != null, "Backend buffers must exist for " + type);
        byte[][] left = { expected.InstanceData, expected.RelocationData, expected.ImportsData };
        byte[][] right = { actual.InstanceData, actual.RelocationData, actual.ImportsData };
        for (int array = 0; array < left.Length; array++)
        {
            Require(left[array] != null && right[array] != null && left[array].Length == right[array].Length, "Prepared three-buffer lengths differ for " + type);
            for (int i = 0; i < left[array].Length; i++)
                Require(left[array][i] == right[array][i], "Prepared three-buffer raw byte differs for " + type);
        }
    }
    sealed class UnknownAudioCompiler : BinaryAssetBuilder.AudioCompiler.IAudioFileCompiler
    {
        public AssetBuffer CompileAudioFile(InstanceDeclaration declaration, TargetPlatform platform)
        { throw new InvalidOperationException("Preparation must never execute an untrusted injected compiler"); }
    }
    static void ParallelQualification(string work)
    {
        string input = Path.Combine(work, "qualified-audio.wav");
        using (var stream = File.Create(input))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(52);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100);
            writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(16);
            for (int i = 0; i < 8; i++) writer.Write((short)(i * 127));
        }
        var audio = new BinaryAssetBuilder.AudioCompiler.Plugin(); audio.Initialize(null, TargetPlatform.Win32);
        var declaration = Declaration("AudioFile", "QualifiedAudio", Namespaces()); Metadata(declaration, audio);
        ((XmlElement)declaration.Node).SetAttribute("File", input);
        ((XmlElement)declaration.Node).SetAttribute("PCCompression", "XAS");
        object prepared = Prepare(declaration, audio);
        try { Require(prepared != null, "Known bounded recovered AudioFile must be parallel eligible"); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
        ((XmlElement)declaration.Node).SetAttribute("PCSampleRate", "22050");
        prepared = Prepare(declaration, audio);
        try { Require(prepared != null, "Explicit sample-rate processing must be parallel eligible"); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
        ((XmlElement)declaration.Node).SetAttribute("PCSampleRate", "44100");
        ((XmlElement)declaration.Node).SetAttribute("PCCompression", "EALAYER3");
        prepared = Prepare(declaration, audio);
        try { Require(prepared != null, "Rate conversion and compression diagnostics must be parallel eligible together"); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
        ((XmlElement)declaration.Node).RemoveAttribute("PCSampleRate");
        var unknown = new BinaryAssetBuilder.AudioCompiler.Plugin(new UnknownAudioCompiler()); unknown.Initialize(null, TargetPlatform.Win32);
        Require(Prepare(declaration, unknown) == null, "Injected AudioFile compiler must not inherit recovered cache or parallel certification");
        using (var stream = new FileStream(input, FileMode.Open, FileAccess.Write)) stream.SetLength(2L * 1024L * 1024L);
        const long MiB = 1024L * 1024L;
        prepared = PrepareWithReason(declaration, audio, 16L * MiB, out string largeReason);
        try { Require(prepared != null && Value<long>(prepared, "PreparedBytes") >= 8L * MiB && Value<long>(prepared, "PreparedBytes") < 9L * MiB, "A 2 MiB AudioFile must fit a 16 MiB preparation budget with its reduced working-set charge"); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
        RejectedPreparation(declaration, audio, 8L * MiB, "preparation-budget-exceeded");
        var xml = new Recovered.XmlPriority.Plugin(); xml.Initialize(null, TargetPlatform.Win32);
        foreach (string type in new[] { "AudioEventLimitGroup", "AudioLOD", "AudioSettings", "Video", "VideoEventList", "MicrophoneSettings", "BootupDisplaySequence" })
        {
            declaration = Declaration(type, "Qualified" + type, Namespaces()); Metadata(declaration, xml);
            prepared = Prepare(declaration, xml);
            try
            {
                Require(prepared != null, "Audited XML metadata root must retain independent backend eligibility: " + type);
                EqualBuffers(xml.ProcessInstance(declaration), Execute(prepared), type);
            }
            finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
        }
        // File length affects retention even though this fixture never executes
        // invalid large WAV contents. Only the lease/qualification is exercised.
        File.Delete(input);
    }
    static void EnvironmentQualification(string sdk)
    {
        // Use the existing source-derived, XSD-covered component fixtures rather
        // than empty roots that omit required fields and pointer/list payloads.
        var fixtures = new XmlDocument { XmlResolver = null };
        fixtures.Load(Path.Combine(sdk, "../recovery/tests/XmlenvironmentProbe.xml"));
        string[] roots = { "Weather", "WaterTransparency", "WaterSetting", "Environment", "LightingSettings",
            "WaterTextureList", "TerrainParticleMapping", "BounceKickTerrainMapping", "ShadowMap",
            "OrdersManagerSettings", "TerrainVisualSettings", "CameraSettings" };
        foreach (TargetPlatform platform in new[] { TargetPlatform.Win32, TargetPlatform.Xbox360, TargetPlatform.PlayStation3, (TargetPlatform)99 })
        {
            var plugin = new Recovered.XmlPriority.Plugin(); plugin.Initialize(null, platform);
            foreach (string type in roots)
            {
                int fixtureCount = 0;
                foreach (XmlNode source in fixtures.DocumentElement.ChildNodes)
                {
                    if (source.LocalName != type) continue;
                    fixtureCount++;
                    var declaration = Declaration(type, "Prepared" + type, Namespaces());
                    var document = new XmlDocument { XmlResolver = null };
                    declaration.XmlNode = document.ImportNode(source, true); document.AppendChild(declaration.Node);
                    Metadata(declaration, plugin);
                    object prepared = Prepare(declaration, plugin);
                    try
                    {
                        Require(prepared != null, "Complete environment family must be independently eligible: " + type);
                        EqualBuffers(plugin.ProcessInstance(declaration), Execute(prepared), type + "/" + platform);
                    }
                    finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
                }
                Require(fixtureCount > 0, "Environment qualification fixture missing: " + type);
            }
            var owner = Declaration("UncertifiedFutureXmlBackend", "StillOwner", Namespaces());
            object unqualified = Prepare(owner, plugin);
            try { Require(unqualified == null, "Unaudited XML roots must retain original owner input without cloning"); }
            finally { if (unqualified != null) ((IDisposable)unqualified).Dispose(); }
        }
    }
    static readonly Type resourcePolicy = typeof(CoreStreamCompiler).Assembly.GetType("NewModCompiler.Application.BackendResourcePolicy", true);
    static object ResourcePolicy(int jobs, int processors, long physical, long availableVirtual, long totalVirtual, bool queried)
    {
        return resourcePolicy.GetMethod("Calculate").Invoke(null, new object[] { jobs, processors, physical, availableVirtual, totalVirtual, queried });
    }
    static T ResourceValue<T>(object policy, string property)
    {
        return (T)resourcePolicy.GetProperty(property).GetValue(policy, null);
    }
    static void ResourceBudget(string work)
    {
        const long MiB = 1024L * 1024L, GiB = 1024L * MiB;
        object generous = ResourcePolicy(Int32.MaxValue, 8, 16 * GiB, 3 * GiB, 4 * GiB, true);
        Require(ResourceValue<int>(generous, "RequestedJobs") == Int32.MaxValue && ResourceValue<int>(generous, "ProcessorCount") == 8,
            "Resource policy must retain the requested jobs and detected CPU identity");
        Require(ResourceValue<int>(generous, "IoCapacity") > 0 && ResourceValue<int>(generous, "IoCapacity") <= 8, "IO concurrency must have a separately bounded allowance");
        Require(ResourceValue<int>(generous, "Capacity") == 8, "A sufficiently provisioned eight-CPU host must not remain hard-capped at two workers");
        Require(ResourceValue<long>(generous, "ByteBudget") == GiB && ResourceValue<long>(generous, "ByteBudget") / 2 == GiB / 2,
            "Available virtual address space and process reserve must bound aggregate/per-task retention");
        Require(ResourceValue<long>(generous, "ByteBudget") / 2 == GiB / 2, "Private input preparation must follow the observed resource budget");
        Require(ResourceValue<int>(ResourcePolicy(1, 8, 16 * GiB, 3 * GiB, 4 * GiB, true), "Capacity") == 1,
            "Explicit jobs=1 must remain serial on a large host");
        Require(ResourceValue<int>(ResourcePolicy(8, 1, 16 * GiB, 3 * GiB, 4 * GiB, true), "Capacity") == 1,
            "One logical processor must limit dispatch despite large RAM and jobs");
        Require(ResourceValue<int>(ResourcePolicy(Int32.MaxValue, 128, 32 * GiB, 6 * GiB, 8 * GiB, true), "Capacity") == 32,
            "Huge jobs and CPU counts must be bounded independently of requested queue allocation");
        object lowVirtual = ResourcePolicy(8, 8, 32 * GiB, 256 * MiB, 2 * GiB, true);
        Require(ResourceValue<int>(lowVirtual, "Capacity") == 1 && ResourceValue<long>(lowVirtual, "ByteBudget") == 0,
            "A Win32 process with exhausted virtual space must not admit workers merely because host RAM is plentiful");
        object lowPhysical = ResourcePolicy(8, 8, 600 * MiB, 1536 * MiB, 2 * GiB, true);
        Require(ResourceValue<long>(lowPhysical, "ByteBudget") == 44 * MiB && ResourceValue<int>(lowPhysical, "Capacity") == 2,
            "Physical availability must independently constrain the aggregate budget and worker count");
        object failed = ResourcePolicy(Int32.MaxValue, 8, 32 * GiB, 3 * GiB, 4 * GiB, false);
        Require(!ResourceValue<bool>(failed, "MemoryQuerySucceeded") && ResourceValue<int>(failed, "Capacity") == 1 &&
            ResourceValue<long>(failed, "ByteBudget") == 16 * MiB && ResourceValue<long>(failed, "ByteBudget") / 2 == 8 * MiB,
            "Failed memory observation must explicitly degrade to one worker and a conservative bounded budget");
        object invalid = ResourcePolicy(8, 0, -1, -1, 2 * GiB, true);
        Require(ResourceValue<int>(invalid, "Capacity") == 1 && ResourceValue<long>(invalid, "ByteBudget") == 0,
            "Invalid CPU/availability observations must not overflow into a large worker pool");
        object overflow = ResourcePolicy(Int32.MaxValue, Int32.MaxValue, Int64.MaxValue, Int64.MaxValue, 2 * GiB, true);
        Require(ResourceValue<int>(overflow, "Capacity") <= 32 && ResourceValue<long>(overflow, "ByteBudget") <= 512 * MiB,
            "Extreme observations must remain bounded by the Win32 virtual address-space identity");
        object tightened = ResourcePolicy(ResourceValue<int>(generous, "RequestedJobs"), ResourceValue<int>(generous, "ProcessorCount"), 600 * MiB, 1536 * MiB, 2 * GiB, true);
        Require(ResourceValue<int>(tightened, "ProcessorCount") == 8 && ResourceValue<int>(tightened, "RequestedJobs") == Int32.MaxValue &&
            ResourceValue<int>(tightened, "Capacity") == 2 && ResourceValue<long>(tightened, "ByteBudget") == 44 * MiB,
            "A new memory observation must tighten capacity without losing CPU/request identity");
        Require(ResourceValue<int>(generous, "Capacity") == 8 && ResourceValue<long>(generous, "ByteBudget") == GiB,
            "Refreshing resources must not silently mutate the prior observation");
    }
    static void W3xBudget()
    {
        var plugin = new Recovered.Compiler.W3x.NativePlugin();
        plugin.Initialize(null, TargetPlatform.Win32);
        var instance = Declaration("W3DAnimation", "PreparedAnimation", Namespaces());
        Metadata(instance, plugin);
        ((XmlElement)instance.Node).SetAttribute("NumFrames", "65536");
        var channels = instance.Node.OwnerDocument.CreateElement("Channels", "uri:ea.com:eala:asset");
        var channel = instance.Node.OwnerDocument.CreateElement("W3DAnimationChannelScalar", "uri:ea.com:eala:asset");
        channels.AppendChild(channel); instance.Node.AppendChild(channels);
        object prepared = Prepare(instance, plugin);
        Require(prepared != null, "W3X must have a certified preparation contract");
        try
        {
            Require(prepared != null, "A bounded 65536-frame single channel animation may run in the W3X pool");
            Require(Value<long>(prepared, "PreparedBytes") >= 65536L * 256L, "Declared frame expansion must be included in the lifecycle budget");
        }
        finally { ((IDisposable)prepared).Dispose(); }
        ((XmlElement)instance.Node).SetAttribute("NumFrames", "4294967295");
        prepared = Prepare(instance, plugin);
        try { Require(prepared == null, "Huge frame expansion must use owner compatibility before cloning"); }
        finally { if (prepared != null) ((IDisposable)prepared).Dispose(); }
    }
    static void SchemaDefaults()
    {
        var xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml("<AudioEventLimitGroup xmlns='uri:ea.com:eala:asset' id='PreparedArmor'/>");
        using (var reader = XmlReader.Create(new StringReader(
            "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='uri:ea.com:eala:asset'>" +
            "<xs:element name='AudioEventLimitGroup'><xs:complexType><xs:attribute name='id' type='xs:string'/>" +
            "<xs:attribute name='Limit' type='xs:string' default='100'/></xs:complexType></xs:element></xs:schema>")))
            xml.Schemas.Add(null, reader);
        xml.Validate(null);
        Require(xml.DocumentElement.Attributes["Limit"] != null && !xml.DocumentElement.Attributes["Limit"].Specified,
            "Fixture must contain a real schema-supplied unspecified default");
        var instance = new InstanceDeclaration(AssetDeclarationDocument.CreateBackendView(Namespaces()));
        instance.XmlNode = xml.DocumentElement;
        var plugin = new Recovered.XmlPriority.Plugin(); plugin.Initialize(null, TargetPlatform.Win32);
        Metadata(instance, plugin);
        object prepared = Prepare(instance, plugin);
        try
        {
            var frozen = Value<InstanceDeclaration>(prepared, "Declaration");
            Require(frozen.Node.Attributes["Limit"] != null && frozen.Node.Attributes["Limit"].Value == "100",
                "The borrowed backend input must preserve schema default values");
            Require(Object.ReferenceEquals(frozen.Node, xml.DocumentElement) && !frozen.Node.Attributes["Limit"].Specified,
                "Borrowing must preserve the original unspecified default attribute itself");
            Require(Execute(prepared) != null, "The default-bearing XML declaration must execute through the real backend");
        }
        finally { ((IDisposable)prepared).Dispose(); }
    }
    static long PreparationEstimate(InstanceDeclaration instance)
    {
        return (long)contract.GetMethod("EstimatePreparationBytes", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { instance });
    }
    static void PreparationBudget()
    {
        var plugin = new Recovered.XmlPriority.Plugin(); plugin.Initialize(null, TargetPlatform.Win32);
        var declaration = Declaration("AudioEventLimitGroup", "PreparationBudget", Namespaces());
        Metadata(declaration, plugin);
        long small = PreparationEstimate(declaration);
        var budgetArguments = new object[] { declaration, plugin, 0L, null };
        Require(!(bool)contract.GetMethod("TryPrepareWithinBudget").Invoke(null, budgetArguments) && budgetArguments[3] == null,
            "A zero observed preparation budget must reject before allocating a detached private representation");

        Require(small >= 4096, "Preparation estimate must cover even a small private DOM");
        // Use an otherwise ordinary metadata string. Preparation's size gate
        // precedes backend field validation and must not clone or serialize it.
        ((XmlElement)declaration.Node).SetAttribute("id", new string('x', 20 * 1024 * 1024));
        long large = PreparationEstimate(declaration);
        Require(large >= 20L * 1024L * 1024L * 8L && large > 128L * 1024L * 1024L,
            "A large single attribute must be included without relying on element count or source file size");
        Require(Prepare(declaration, plugin) == null, "Oversized preparation must use the owner compatibility path before cloning");
    }
    sealed class UncertifiedAudioPlugin : BinaryAssetBuilder.AudioCompiler.Plugin { }
    static void PreparationRejectionReasons(string work)
    {
        const long budget = 128L * 1024L * 1024L;
        var xml = new Recovered.XmlPriority.Plugin(); xml.Initialize(null, TargetPlatform.Win32);
        var audio = new BinaryAssetBuilder.AudioCompiler.Plugin(); audio.Initialize(null, TargetPlatform.Win32);
        var apt = Declaration("AptAptData", "RejectedApt", Namespaces()); Metadata(apt, xml);
        RejectedPreparation(null, xml, budget, "invalid-input");
        RejectedPreparation(apt, null, budget, "invalid-input");
        RejectedPreparation(apt, new UncertifiedAudioPlugin(), budget, "uncertified-plugin");
        RejectedPreparation(apt, xml, 0, "preparation-budget-exceeded");
        RejectedPreparation(apt, xml, budget, "external-input-path-missing");
        ((XmlElement)apt.Node).SetAttribute("File", "");
        RejectedPreparation(apt, xml, budget, "external-input-path-missing");
        ((XmlElement)apt.Node).SetAttribute("File", Path.Combine(work, "absent-prepared.bin"));
        RejectedPreparation(apt, xml, budget, "external-input-unavailable");
        ((XmlElement)apt.Node).SetAttribute("File", Path.Combine(work, "non-ascii-\u4e2d.bin"));
        RejectedPreparation(apt, xml, budget, "native-path-non-ascii");

        string path = Path.Combine(work, "reason-input.bin"); File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        ((XmlElement)apt.Node).SetAttribute("File", path);
        using (var denied = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            RejectedPreparation(apt, xml, budget, "external-input-unavailable");
        string reason;
        object accepted = PrepareWithReason(apt, xml, budget, out reason);
        try
        {
            Require(accepted != null && reason == null, "A successful retry must clear the previous refusal");
            Denied(delegate { File.Delete(path); }, "deletion after a successful reason-aware preparation");
        }
        finally { if (accepted != null) ((IDisposable)accepted).Dispose(); }

        var texture = Declaration("TestTexture", "RejectedTexture", Namespaces()); Metadata(texture, xml);
        RejectedPreparation(texture, xml, budget, "uncertified-xml-type");
        using (var released = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            Require(released.CanWrite, "Refused unreviewed XML must never retain external leases");

        string target = Path.Combine(work, "reason-junction-target"); Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "input.bin"), new byte[] { 5, 6 });
        string junction = Path.Combine(work, "reason-junction");
        var start = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + junction + "\" \"" + target + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(); Require(process.ExitCode == 0, "Preparation-reason junction fixture: " + stdout + stderr);
        }
        try
        {
            ((XmlElement)apt.Node).SetAttribute("File", Path.Combine(junction, "input.bin"));
            RejectedPreparation(apt, xml, budget, "external-input-reparse-point");
        }
        finally { Directory.Delete(junction); }

        var audioFile = Declaration("AudioFile", "RejectedAudio", Namespaces()); Metadata(audioFile, audio);
        ((XmlElement)audioFile.Node).SetAttribute("File", path);
        var unknown = new BinaryAssetBuilder.AudioCompiler.Plugin(new UnknownAudioCompiler()); unknown.Initialize(null, TargetPlatform.Win32);
        RejectedPreparation(audioFile, unknown, budget, "uncertified-audio-compiler");
        ((XmlElement)audioFile.Node).SetAttribute("PCCompression", "NONE");
        ((XmlElement)audioFile.Node).RemoveAttribute("File");
        RejectedPreparation(audioFile, audio, budget, "external-input-path-missing");
        RejectedPreparation(apt, audio, budget, "uncertified-audio-type");

        var music = Declaration("PathMusicEvent", "RejectedMusic", Namespaces()); Metadata(music, audio);
        RejectedPreparation(music, audio, budget, "uncertified-audio-type");
        File.Delete(path);
        lifecycleEvidence.Add(new { kind = "preparation-rejected-reasons", firstActualGate = true,
            reasons = new[] { "invalid-input", "uncertified-plugin", "preparation-budget-exceeded", "external-input-path-missing",
                "external-input-unavailable", "native-path-non-ascii", "external-input-reparse-point", "uncertified-audio-compiler",
                "uncertified-audio-type" },
            rejectedUnknownXmlWithoutLeases = true, successfulRetryReasonCleared = true,
            scope = "Windows production host; unsupported-platform remains a source-certified gate" });
    }
    sealed class BufferWork
    {
        public string Type;
        public IAssetBuilderPlugin Plugin;
        public InstanceDeclaration Owner;
        public object Prepared;
        public AssetBuffer Expected,Actual;
        public string XmlBefore;
        public long Start,End;
        public int ThreadId;
    }
    static void WorkerWave(List<BufferWork> items,string label)
    {
        int next=-1;
        int count=Math.Max(1,Math.Min(4,Environment.ProcessorCount));
        var tasks=new List<Task>();
        var intervals=new List<object>();
        using(var ready=new CountdownEvent(count))
        using(var begin=new ManualResetEventSlim())
        {
            try
            {
                for(int i=0;i<count;i++)
                    tasks.Add(Task.Factory.StartNew(delegate {
                        ready.Signal(); begin.Wait();
                        for(int index=Interlocked.Increment(ref next);index<items.Count;index=Interlocked.Increment(ref next))
                        {
                            var item=items[index]; item.ThreadId=Thread.CurrentThread.ManagedThreadId;
                            item.Start=Stopwatch.GetTimestamp();
                            try { item.Actual=Execute(item.Prepared); }
                            finally { item.End=Stopwatch.GetTimestamp(); }
                        }
                    },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default));
                ready.Wait(); begin.Set(); Task.WaitAll(tasks.ToArray());
                int maximum=0;
                foreach(var item in items)
                {
                    EqualBuffers(item.Expected,item.Actual,label+"/"+item.Type);
                    Require(item.Owner.Node.OuterXml == item.XmlBefore, "Certified worker mutated its borrowed DOM: " + item.Type);
                    Require(item.ThreadId!=Thread.CurrentThread.ManagedThreadId,"Backend invocation must run on a real worker");
                    if(item.Owner.HasCustomData)
                    {
                        string path=Value<InstanceDeclaration>(item.Prepared,"Declaration").CustomDataPath;
                        byte[] left=File.ReadAllBytes(item.Owner.CustomDataPath),right=File.ReadAllBytes(path);
                        Require(left.Length==right.Length,"CDATA length differs: "+item.Type);
                        for(int i=0;i<left.Length;i++) if(left[i]!=right[i]) throw new InvalidOperationException("CDATA raw byte differs: "+item.Type+"/"+i);
                        checks++;
                    }
                    int overlap=0;
                    foreach(var other in items) if(other.Start<=item.Start && other.End>item.Start) overlap++;
                    maximum=Math.Max(maximum,overlap);
                    intervals.Add(new {type=item.Type,startTicks=item.Start,endTicks=item.End,threadId=item.ThreadId});
                }
                if((label=="audio" || label=="texture") && count>1) Require(maximum>1,"Substantial "+label+" backend calls must show actual worker interval overlap on a multi-CPU host");
                lifecycleEvidence.Add(new {kind="prepared-worker-wave",family=label,workerCount=count,maximumConcurrency=maximum,intervals,clockFrequency=Stopwatch.Frequency});
            }
            finally
            {
                begin.Set();
                try { Task.WaitAll(tasks.ToArray()); }
                finally { foreach(var item in items) ((IDisposable)item.Prepared).Dispose(); }
            }
        }
    }
    static BufferWork BufferItem(string work,string label,IAssetBuilderPlugin plugin,InstanceDeclaration owner)
    {
        Metadata(owner,plugin);
        owner.CustomDataPath=Path.Combine(work,label+"-owner.cdata");
        object prepared=Prepare(owner,plugin);
        Require(prepared!=null,"Worker fixture must be certified: "+label);
        var input=Value<InstanceDeclaration>(prepared,"Declaration");
        Require(Object.ReferenceEquals(input.Node,owner.Node) && Object.ReferenceEquals(input.Handle,owner.Handle) &&
            Object.ReferenceEquals(input.Document.NamespaceManager,owner.Document.NamespaceManager) && Object.ReferenceEquals(input.ReferencedInstances,owner.ReferencedInstances),
            "Prepared input must borrow XML, namespace bindings, handle and references: " + label);
        Require(!Object.ReferenceEquals(input,owner) && !Object.ReferenceEquals(input.Document,owner.Document),
            "Every prepared work must retain private declaration and document metadata state: " + label);
        if(owner.HasCustomData) {
            input.CustomDataPath=Path.Combine(work,label+"-worker.cdata");
            Require(input.CustomDataPath != owner.CustomDataPath, "CDATA pathname must be private: " + label);
        }
        return new BufferWork {Type=owner.Handle.TypeName,Plugin=plugin,Owner=owner,Prepared=prepared,Expected=plugin.ProcessInstance(owner),XmlBefore=owner.Node.OuterXml};
    }
    static void TokenizerWorkers(string work)
    {
        foreach(var platform in new[]{TargetPlatform.Win32,TargetPlatform.Xbox360,TargetPlatform.PlayStation3,(TargetPlatform)99})
        {
            var items=new List<BufferWork>();
            IAssetBuilderPlugin[] plugins={new Recovered.GameObject.Plugin(),new Recovered.Weapon.Plugin(),new Recovered.Compiler.Plugin()};
            string[] types={"GameObject","WeaponTemplate","ArmorTemplate"};
            string[] variables={"RA3_RECOVERED_GAMEOBJECT_TRACE","RA3_RECOVERED_WEAPON_TRACE","RA3_RECOVERED_ARMOR_TRACE"};
            var saved=new string[3];
            try {
                var document=new XmlDocument {XmlResolver=null};
                document.LoadXml("<AssetDeclaration xmlns='uri:ea.com:eala:asset'/>");
                var namespaces=new XmlNamespaceManager(document.NameTable);namespaces.AddNamespace("ea","uri:ea.com:eala:asset");
                for(int type=0;type<3;type++) {
                    saved[type]=Environment.GetEnvironmentVariable(variables[type]);
                    string trace=Path.Combine(work,"token-trace-"+(int)platform+"-"+type);
                    Environment.SetEnvironmentVariable(variables[type],trace);
                    plugins[type].Initialize(null,platform);
                    for(int i=0;i<12;i++) {
                        var node=document.CreateElement(types[type],"uri:ea.com:eala:asset");
                        node.SetAttribute("id","Token"+type+"_"+i);document.DocumentElement.AppendChild(node);
                        if(type==0){node.SetAttribute("Description","shared DOM "+i);node.SetAttribute("Scale","1.25");}
                        else if(type==1){node.SetAttribute("AttackRange","42");node.InnerXml="<PreAttackDelay xmlns='uri:ea.com:eala:asset' MinSeconds='1s' MaxSeconds='2s'/>";}
                        else {node.SetAttribute("Default","50%");node.InnerXml="<Armor xmlns='uri:ea.com:eala:asset' Damage='GUN' Percent='25%'/>";}
                        var owner=new InstanceDeclaration(AssetDeclarationDocument.CreateBackendView(namespaces));owner.XmlNode=node;
                        items.Add(BufferItem(work,"token-"+(int)platform+"-"+type+"-"+i,plugins[type],owner));
                    }
                    var wrong=Declaration(types[(type+1)%3],"WrongType",Namespaces());
                    RejectedPreparation(wrong,plugins[type],128L*1024L*1024L,"uncertified-tokenizer-type");
                }
                WorkerWave(items,"tokenizers-"+(int)platform);
                for(int type=0;type<3;type++)
                    Require(File.ReadAllLines(Path.Combine(work,"token-trace-"+(int)platform+"-"+type,"calls.tsv")).Length==24,
                        "Concurrent optional tokenizer trace lost or interleaved index writes: "+types[type]);
            }
            finally {
                foreach(var item in items)((IDisposable)item.Prepared).Dispose();
                for(int type=0;type<3;type++)Environment.SetEnvironmentVariable(variables[type],saved[type]);
            }
        }
    }
    static void WorkerBuffers(string work,string sdk)
    {
        var xml=new Recovered.XmlPriority.Plugin(); xml.Initialize(null,TargetPlatform.Win32);
        var apt=new List<BufferWork>();
        string blob=Path.Combine(work,"all-apt-workers.bin");
        byte[] data=new byte[1024*1024];for(int i=0;i<data.Length;i++)data[i]=(byte)(i*73+19); File.WriteAllBytes(blob,data);
        foreach(string type in new[]{"AptAptData","AptConstData","AptDatData","AptGeometryData"})
        {
            var owner=Declaration(type,"Worker"+type,Namespaces()); ((XmlElement)owner.Node).SetAttribute("File",blob);
            apt.Add(BufferItem(work,type,xml,owner));
        }
        WorkerWave(apt,"apt");
        var w3xPlugin=new Recovered.Compiler.W3x.NativePlugin();w3xPlugin.Initialize(null,TargetPlatform.Win32);
        var w3x=new List<BufferWork>();
        for(int i=0;i<4;i++)
        {
            string type=i<2?"W3DAnimation":"W3DMesh";
            var owner=Declaration(type,"WorkerW3x"+i,Namespaces());
            if(i<2)
            {
                ((XmlElement)owner.Node).SetAttribute("NumFrames","4096"); ((XmlElement)owner.Node).SetAttribute("FrameRate","30");
                owner.Node.InnerXml="<Channels xmlns='uri:ea.com:eala:asset'><ChannelScalar Type='XTranslation' TypeId='1355976437' Pivot='0' FirstFrame='0'><Frame>0</Frame><Frame>1</Frame></ChannelScalar></Channels>";
            }
            else owner.Node.InnerXml="<Vertices xmlns='uri:ea.com:eala:asset'><V X='0' Y='0' Z='0'/><V X='1' Y='0' Z='0'/><V X='0' Y='1' Z='0'/></Vertices><Triangles xmlns='uri:ea.com:eala:asset'><T><V>0</V><V>1</V><V>2</V><Nrm X='0' Y='0' Z='1'/><Dist>0</Dist></T></Triangles>";
            w3x.Add(BufferItem(work,"w3x"+i,w3xPlugin,owner));
        }
        WorkerWave(w3x,"w3x");
        var audioPlugin=new BinaryAssetBuilder.AudioCompiler.Plugin();audioPlugin.Initialize(null,TargetPlatform.Win32);
        var audio=new List<BufferWork>();
        for(int i=0;i<8;i++)
        {
            string wave=Path.Combine(work,"worker"+i+".wav");
            using(var writer=new BinaryWriter(File.Create(wave)))
            {
                const int samples=262144;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);
                writer.Write(22050);writer.Write(44100);writer.Write((short)2);writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
                for(int n=0;n<samples;n++)writer.Write((short)(n*127+i));
            }
            var owner=Declaration("AudioFile","WorkerAudio"+i,Namespaces());
            var node=(XmlElement)owner.Node;
            node.SetAttribute("File",wave);
            string compression=new[]{"NONE","XAS","EALAYER3",null}[i%4];
            if(compression!=null) node.SetAttribute("PCCompression",compression);
            if(i>=4) {
                node.SetAttribute("PCSampleRate",i%2==0?"44100":"11025");
                node.SetAttribute("IsStreamedOnPC","true");
            }
            audio.Add(BufferItem(work,"audio"+i,audioPlugin,owner));
        }
        WorkerWave(audio,"audio");
        lifecycleEvidence.Add(new {kind="audio-parallel-settings",assets=audio.Count,compressionSettings=new[]{"NONE","XAS","EALAYER3","default"},sampleRates=new[]{22050,44100,11025},compressionWarnings=0});
        string fixture=File.ReadAllText(Path.Combine(sdk,"../recovery/tests/AudioMp3Fixtures.cs"));
        var match=Regex.Match(fixture,"public const string Mono = \\\"([^\\\"]+)\\\"");
        Require(match.Success,"Existing real MP3 fixture must be present");
        string mp3=Path.Combine(work,"worker-passthrough.mp3");File.WriteAllBytes(mp3,Convert.FromBase64String(match.Groups[1].Value));
        var mp3Owner=Declaration("AudioFileMP3Passthrough","WorkerMp3",Namespaces());((XmlElement)mp3Owner.Node).SetAttribute("File",mp3);
        WorkerWave(new List<BufferWork>{BufferItem(work,"mp3",audioPlugin,mp3Owner)},"mp3");
    }
    sealed class UncertifiedTexturePlugin : Recovered.Compiler.Texture.Plugin { }
    static void WriteTextureTga(string path,int width,int height,bool grayscale=false)
    {
        using(var writer=new BinaryWriter(File.Create(path)))
        {
            byte[] header=new byte[18];header[2]=2;header[12]=(byte)width;header[13]=(byte)(width>>8);
            header[14]=(byte)height;header[15]=(byte)(height>>8);header[16]=32;header[17]=0x28;writer.Write(header);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {byte level=(byte)(x*3+y*5);writer.Write(grayscale?level:(byte)(x^y));writer.Write(grayscale?level:(byte)(y*255/Math.Max(1,height-1)));writer.Write(grayscale?level:(byte)(x*255/Math.Max(1,width-1)));writer.Write((byte)(64+(x+y)%192));}
        }
    }
    static InstanceDeclaration TextureDeclaration(string type,string id,string file,string format,bool mipmaps)
    {
        var owner=Declaration(type,id,Namespaces());var node=(XmlElement)owner.Node;
        node.SetAttribute("File",file);node.SetAttribute("Type","StandardTexture");node.SetAttribute("OutputFormat",format);
        node.SetAttribute("GenerateMipMaps",mipmaps?"true":"false");node.SetAttribute("AllowAutomaticResize","true");return owner;
    }
    static void TextureWorkers(string work)
    {
        string directory=Path.Combine(work,"texture-workers");Directory.CreateDirectory(directory);
        string image=Path.Combine(directory,"worker.tga"),normal=Path.Combine(directory,"normal.tga"),gray=Path.Combine(directory,"gray.tga");
        WriteTextureTga(image,256,256);WriteTextureTga(normal,256,256);WriteTextureTga(gray,256,256,true);
        byte[] rgba=new byte[32*32*4];for(int i=0;i<rgba.Length;i++)rgba[i]=(byte)(i*73+19);
        string dds=Path.Combine(directory,"快照.dds");File.WriteAllBytes(dds,Recovered.Compiler.Texture.TextureRawDdsEncoder.Encode(32,32,rgba,"D3DFMT_A8R8G8B8",false));
        var plugin=new Recovered.Compiler.Texture.Plugin();plugin.Initialize(null,TargetPlatform.Win32);
        var items=new List<BufferWork>();
        try
        {
            var owner=TextureDeclaration("Texture","TextureLease",dds,"A8R8G8B8",false);Metadata(owner,plugin);
            byte[] before=File.ReadAllBytes(dds);object prepared=Prepare(owner,plugin);
            Require(prepared!=null,"Unicode DDS must be texture-worker eligible");
            try
            {
                Require(Value<string>(prepared,"Family")=="texture" && Value<long>(prepared,"ExternalReadBytes")==before.Length,"Texture preparation must retain its actual input lease");
                Denied(delegate{using(var writer=new FileStream(dds,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){}},"texture mutation");
                Denied(delegate{File.Delete(dds);},"texture deletion");
                EqualBuffers(plugin.ProcessInstance(owner),Execute(prepared),"leased-texture-dds");
            }finally{((IDisposable)prepared).Dispose();}
            DateTime stamp=File.GetLastWriteTimeUtc(dds);before[before.Length-1]^=127;File.WriteAllBytes(dds,before);File.SetLastWriteTimeUtc(dds,stamp);
            prepared=Prepare(owner,plugin);Require(prepared!=null,"Released texture lease must admit a new equal-length/mtime version");
            try{Require(Contains(Execute(prepared).InstanceData,before),"Texture worker consumed stale DDS bytes after a released lease");}finally{((IDisposable)prepared).Dispose();}
            RejectedPreparation(owner,plugin,1,"preparation-budget-exceeded");
            RejectedPreparation(owner,new UncertifiedTexturePlugin(),128L*1024L*1024L,"uncertified-plugin");
            RejectedPreparation(Declaration("TestTexture","UnsupportedTextureRoot",Namespaces()),plugin,128L*1024L*1024L,"uncertified-texture-type");
            var missing=TextureDeclaration("Texture","MissingTexture",Path.Combine(directory,"missing.tga"),"DXT5",true);
            RejectedPreparation(missing,plugin,128L*1024L*1024L,"external-input-unavailable");
            using(var writer=new FileStream(dds,FileMode.Open,FileAccess.Write,FileShare.None))Require(writer.CanWrite,"Texture rejection retained an input lease");

            // Fresh LongRunning workers have never called S3: the first wave
            // exercises concurrent lazy gray-table construction on four threads.
            for(int i=0;i<4;i++)items.Add(BufferItem(directory,"texture-s3-"+i,plugin,
                TextureDeclaration(i<2?"Texture":"OnDemandTexture","TextureS3"+i,gray,i%2==0?"DXT3":"DXT5",true)));
            items.Add(BufferItem(directory,"texture-dds",plugin,TextureDeclaration("Texture","TextureDds",dds,"DXT1",true)));
            items.Add(BufferItem(directory,"texture-raw",plugin,TextureDeclaration("Texture","TextureRaw",image,"R5G6B5",true)));
            items.Add(BufferItem(directory,"texture-bc1",plugin,TextureDeclaration("Texture","TextureBc1",image,"DXT1A",true)));
            var atlas=Declaration("TerrainTextureAtlas","WorkerAtlas",Namespaces());var atlasNode=(XmlElement)atlas.Node;
            atlasNode.SetAttribute("AtlasSize","2048");atlasNode.SetAttribute("AllowLossyCompression","false");
            var tile=atlasNode.OwnerDocument.CreateElement("Tile","uri:ea.com:eala:asset");tile.SetAttribute("TextureID","1");tile.SetAttribute("BaseTexture",image);tile.SetAttribute("NormalTexture",normal);atlasNode.AppendChild(tile);
            var cell=atlasNode.OwnerDocument.CreateElement("Cell","uri:ea.com:eala:asset");cell.SetAttribute("IndexX","0");cell.SetAttribute("IndexY","0");tile.AppendChild(cell);
            var atlasItem=BufferItem(directory,"texture-atlas",plugin,atlas);items.Add(atlasItem);
            Denied(delegate{using(var writer=new FileStream(normal,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){}},"atlas normal mutation");
            WorkerWave(items,"texture");
            using(var writer=new FileStream(normal,FileMode.Open,FileAccess.Write,FileShare.None))Require(writer.CanWrite,"Atlas normal lease survived worker drain");
            lifecycleEvidence.Add(new {kind="texture-parallel-settings",assets=items.Count,routes=new[]{"Texture","OnDemandTexture","TerrainTextureAtlas"},outputs=new[]{"DDS","A8R8G8B8","R5G6B5","DXT1A","DXT3","DXT5"},coldWorkerTables=true,unicodeInput=true,sameLengthMtimeLeaseRefresh=true,privateOnDemandOutputs=true});
        }
        finally{foreach(var item in items)((IDisposable)item.Prepared).Dispose();plugin.Dispose();}
    }
    static void TextureOwnerDiagnostics(string work)
    {
        string path=Path.Combine(work,"texture-owner-diagnostics.tga");WriteTextureTga(path,16,16);
        var plugin=new Recovered.Compiler.Texture.Plugin();plugin.Initialize(null,TargetPlatform.Win32);
        object prepared=null;
        try
        {
            var declaration=TextureDeclaration("Texture","PreparedTraceDefault",path,"DXT1",false);Metadata(declaration,plugin);
            prepared=Prepare(declaration,plugin);Require(prepared!=null,"Alpha-inference texture must prepare for diagnostic replay");
            MethodInfo flush=contract.GetMethod("FlushDiagnostics",BindingFlags.Instance|BindingFlags.NonPublic);
            Require(flush!=null,"Prepared work must expose the owner diagnostic flush");
            int ownerThread=Thread.CurrentThread.ManagedThreadId,workerThread=0,calls=0,callbackThread=0;
            string message=null;TraceEventType kind=0;
            using(Tracer.BeginScope(delegate(string source,TraceEventType type,string text)
            {
                if(source!="TextureCompiler")return;
                callbackThread=Thread.CurrentThread.ManagedThreadId;kind=type;message=text;Interlocked.Increment(ref calls);
            },9))
            {
                Task<AssetBuffer> task=Task.Factory.StartNew(delegate
                {
                    workerThread=Thread.CurrentThread.ManagedThreadId;return Execute(prepared);
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                AssetBuffer result=task.GetAwaiter().GetResult();
                Require(workerThread!=ownerThread,"Texture diagnostic fixture must execute on a real worker");
                Require(result!=null && result.InstanceData.Length>100 && BitConverter.ToUInt32(result.InstanceData,96)==0x35545844u,
                    "Alpha inference must actually produce DXT5 before replaying its information message");
                Require(calls==0,"Texture worker must record messages privately without calling the owner tracer");
                flush.Invoke(prepared,null);
                Require(calls==1 && callbackThread==ownerThread && kind==TraceEventType.Information && message!=null &&
                    message.Contains("PreparedTraceDefault") && message.StartsWith("Converting to DXT5",StringComparison.Ordinal),
                    "Joined texture information must be delivered once, unchanged, on the owner thread");
                flush.Invoke(prepared,null);
                Require(calls==1,"A repeated owner flush must not duplicate the same texture diagnostic");
                lifecycleEvidence.Add(new{kind="texture-owner-diagnostics",ownerThread,workerThread,callbacksBeforeFlush=0,callbacksAfterFlush=calls,
                    callbackThread,information=message,workerJoinedBeforeFlush=task.IsCompleted,duplicateFlushSuppressed=true});
            }
        }
        finally{if(prepared!=null)((IDisposable)prepared).Dispose();plugin.Dispose();}
    }
    static Func<T> TypedSampler<T>(Func<object> sampler) { return delegate {return (T)sampler();}; }
    sealed class SchedulerFixture : IDisposable
    {
        internal readonly object Metrics, Performance;
        internal readonly Type SchedulerType, PerformanceType;
        internal readonly IAssetExecution Execution;
        internal readonly DocumentProcessor Processor;
        internal readonly OutputManager Output;
        readonly Settings previous;
        internal const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        internal SchedulerFixture(string work, int jobs, CancellationToken cancellation)
        {
            previous = Settings.Current;
            Settings.Current = new Settings { BuildCache = false, TargetPlatform = TargetPlatform.Win32 };
            var assembly = typeof(CoreStreamCompiler).Assembly;
            PerformanceType = assembly.GetType("NewModCompiler.Diagnostics.BuildPerformance", true);
            Type metricsType = assembly.GetType("NewModCompiler.Application.ParallelBackendMetrics", true);
            SchedulerType = assembly.GetType("NewModCompiler.Application.ParallelBackendScheduler", true);
            Performance = Activator.CreateInstance(PerformanceType, Fields, null, new object[] { "detailed" }, null);
            Metrics = Activator.CreateInstance(metricsType, Fields, null, new[] { Performance }, null);
            object resources = ResourcePolicy(jobs, jobs, 16L*1024*1024*1024, 3L*1024*1024*1024, 4L*1024*1024*1024, true);
            object sampler = typeof(CompilerPreparedBackend).GetMethod("TypedSampler", BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(resourcePolicy).Invoke(null, new object[] { new Func<object>(() => resources) });
            Processor = (DocumentProcessor)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DocumentProcessor));
            Output = new OutputManager(Processor, null, Path.Combine(work, "output", "stream"), Path.Combine(work, "intermediate"), null, null, null);
            Execution = (IAssetExecution)Activator.CreateInstance(SchedulerType, Fields, null,
                new object[] { Processor, work, jobs, Metrics, Performance, cancellation, sampler }, null);
        }
        internal BinaryAsset Asset(InstanceDeclaration declaration) { return Output.GetBinaryAsset(declaration, true); }
        internal void Until(Func<bool> predicate, string message)
        {
            var timeout = Stopwatch.StartNew();
            while (!predicate() && timeout.ElapsedMilliseconds < 10000) { Execution.DrainReady(); Thread.Sleep(1); }
            Require(predicate(), message);
        }
        public void Dispose()
        {
            try { ((IDisposable)Execution).Dispose(); }
            finally { Settings.Current = previous; }
        }
    }
    static void SchedulerCompletion(string work)
    {
        var plugin = new Recovered.XmlPriority.Plugin(); plugin.Initialize(null, TargetPlatform.Win32);
        using (var slowGate = new ManualResetEventSlim())
        using (var slowStarted = new ManualResetEventSlim())
        using (var cacheGate = new ManualResetEventSlim())
        using (var cacheStarted = new CountdownEvent(2))
        using (var fixture = new SchedulerFixture(Path.Combine(work, "completion-driven"), 2, CancellationToken.None))
        {
            IAssetExecution execution = fixture.Execution;
            int fastVerified = 0, fastCommitted = 0, slowCommitted = 0, cacheCommitted = 0;
            var slow = Declaration("AudioLOD", "SlowFirst", Namespaces()); Metadata(slow, plugin);
            var fast = Declaration("AudioLOD", "FastLater", Namespaces()); Metadata(fast, plugin);
            var duplicate = Declaration("AudioLOD", "FastLater", Namespaces()); Metadata(duplicate, plugin);
            var slowScope = execution.BeginDocument(slow.Document, fixture.Output);
            var fastScope = execution.BeginDocument(fast.Document, fixture.Output);
            var duplicateScope = execution.BeginDocument(duplicate.Document, fixture.Output);
            BinaryAsset fastAsset = fixture.Asset(fast);
            try
            {
                Action unexpected = () => { throw new InvalidOperationException("Certified lightweight backend unexpectedly used owner compatibility"); };
                execution.Enqueue(slowScope, fixture.Asset(slow), slow, plugin, unexpected,
                    () => { slowStarted.Set(); slowGate.Wait(); return true; }, valid => Require(valid, "Slow declaration verification failed"), source => slowCommitted++);
                execution.CompleteDocument(slowScope, () => { });
                execution.Enqueue(fastScope, fastAsset, fast, plugin, unexpected,
                    () => { Interlocked.Increment(ref fastVerified); return true; }, valid => Require(valid, "Fast declaration verification failed"), source => fastCommitted++);
                execution.CompleteDocument(fastScope, () => { });
                execution.Enqueue(duplicateScope, fixture.Asset(duplicate), duplicate, plugin, unexpected,
                    () => { Interlocked.Increment(ref fastVerified); return true; }, valid => Require(valid, "Duplicate declaration verification failed"), source => fastCommitted++);
                execution.CompleteDocument(duplicateScope, () => { });
                Require(slowStarted.Wait(10000), "Slow first verification did not actually enter a worker");
                fixture.Until(() => fastScope.IsCompleted && duplicateScope.IsCompleted, "Completed later documents remained blocked by the slow first asset");
                Require(!slowScope.IsCompleted && slowCommitted == 0, "Slow document completed before its own worker released its input");
                Require(fastVerified == 2 && fastCommitted == 1 && fixture.Processor.InstanceCompileCount == 1,
                    "Same physical asset must verify both declarations but compile and publish only once: verify="+fastVerified+",commit="+fastCommitted+",compile="+fixture.Processor.InstanceCompileCount);
                Require(Object.ReferenceEquals(fastAsset, fixture.Asset(duplicate)) && Object.ReferenceEquals(fastAsset.Instance, fast),
                    "Physical deduplication must retain its first stable declaration");
                Require(File.Exists(Path.Combine(fastAsset.AssetOutputDirectory, fastAsset.AssetFileName)), "Fast later asset IO must finish before owner publication");
                Require(slowScope.ReadOnlyInputReferences > 0 && fastScope.ReadOnlyInputReferences == 0 && duplicateScope.ReadOnlyInputReferences == 0,
                    "Document input leases must follow actual users rather than discovery order");
                slowGate.Set(); execution.WaitDocument(slowScope);
                Require(slowCommitted == 1 && fixture.Processor.InstanceCompileCount == 2 && slowScope.PendingAssetWorks == 0 && slowScope.ReadOnlyInputReferences == 0,
                    "Document-specific wait must release its pending count and retained inputs");

                // Both encoding slots are occupied by real worker verification.
                // A fully reusable cache asset needs no fresh semantic validation.
                for (int i = 0; i < 2; i++)
                {
                    var blocked = Declaration("AudioLOD", "CacheOverlap"+i, Namespaces()); Metadata(blocked, plugin);
                    var scope = execution.BeginDocument(blocked.Document, fixture.Output);
                    execution.Enqueue(scope, fixture.Asset(blocked), blocked, plugin, unexpected,
                        () => { cacheStarted.Signal(); cacheGate.Wait(); return true; }, valid => Require(valid, "Blocked declaration verification failed"), source => { });
                    execution.CompleteDocument(scope, () => { });
                }
                Require(cacheStarted.Wait(10000), "Both CPU slots must be occupied before testing cache IO admission");
                var cached = Declaration("AudioLOD", "IndependentCache", Namespaces()); Metadata(cached, plugin);
                BinaryAsset cachedAsset = fixture.Asset(cached);
                BinaryAssetCommitWork prepared = cachedAsset.PrepareCommit(AssetLocation.Memory, plugin.ProcessInstance(cached));
                prepared.ExecuteIO(); cachedAsset.PublishCommit(prepared);
                var cachedScope = execution.BeginDocument(cached.Document, fixture.Output);
                execution.EnqueueCached(cachedScope, cachedAsset, cached, AssetLocation.Output, () => true,
                    valid => Require(valid, "Cached declaration validation failed"), source => cacheCommitted++, parallelVerification:false);
                execution.CompleteDocument(cachedScope, () => { });
                fixture.Until(() => cachedScope.IsCompleted, "Cached physical IO incorrectly waited for an encoding CPU slot");
                Require(cacheCommitted == 1 && !cacheGate.IsSet, "Cache IO must complete while both encoding slots remain occupied");
                cacheGate.Set(); execution.WaitOutput(fixture.Output);
                Require(!execution.HasPending, "Output-specific barrier retained completed work");
                lifecycleEvidence.Add(new { kind="completion-driven-scheduler", slowFirstReleasedAfterFast=true,
                    fastVerified,fastCommitted,onePhysicalCompile=true,cacheIoDuringCpuSaturation=true,documentInputLeasesReleased=true });
            }
            finally { slowGate.Set(); cacheGate.Set(); }
        }
    }
    static void SchedulerOwnerFailure(string work)
    {
        using(var fixture=new SchedulerFixture(Path.Combine(work,"owner-completion-failure"),2,CancellationToken.None))
        {
            var declaration=Declaration("AudioLOD","OwnerFailure",Namespaces());
            var scope=fixture.Execution.BeginDocument(declaration.Document,fixture.Output);
            var failure=new IOException("Controlled owner document completion failure");int later=0;
            fixture.Execution.DeferCompletion(scope,()=>{throw failure;});
            fixture.Execution.DeferCompletion(scope,()=>{later++;});
            Exception observed=null;try{fixture.Execution.CompleteDocument(scope,null);}catch(Exception error){observed=error;}
            Require(Object.ReferenceEquals(observed,failure),"Owner completion must preserve its first failure");
            Require(scope.IsFailed && !scope.IsCompleted && later==0,"Failed owner callback must not report a successful document or run later callbacks");
            lifecycleEvidence.Add(new{kind="owner-completion-failure",failedScopeNotCompleted=true,laterCallbacksSuppressed=true,originalFailurePreserved=true});
        }
    }
    static void SchedulerDrain(string work,bool cancel,bool textureFailure=false)
    {
        var plugin=new Recovered.Compiler.W3x.NativePlugin();plugin.Initialize(null,TargetPlatform.Win32);
        var xml=new Recovered.XmlPriority.Plugin();xml.Initialize(null,TargetPlatform.Win32);
        var texture=new Recovered.Compiler.Texture.Plugin();texture.Initialize(null,TargetPlatform.Win32);
        string textureInput=null,invalidTexture=null;
        if(textureFailure)
        {
            textureInput=Path.Combine(work,"drain-texture.tga");invalidTexture=Path.Combine(work,"drain-invalid-texture.tga");
            WriteTextureTga(textureInput,256,256);byte[] header=new byte[18];Buffer.BlockCopy(File.ReadAllBytes(textureInput),0,header,0,header.Length);File.WriteAllBytes(invalidTexture,header);
        }
        var privateTextureDirectories=new List<string>();
        var captured=new List<Task>(); var scopes=new List<AssetExecutionDocument>();
        int verified=0,committed=0,completed=0;
        string input=Path.Combine(work,cancel?"cancel-lease.bin":"failure-lease.bin");File.WriteAllBytes(input,new byte[1024*1024]);
        using(var dispatchGate=new ManualResetEventSlim())
        using(var cancellation=new CancellationTokenSource())
        using(var fixture=new SchedulerFixture(Path.Combine(work,textureFailure?"texture-drain":cancel?"cancel-drain":"native-drain"),4,cancellation.Token))
        {
            var execution=fixture.Execution;
            try
            {
                for(int i=0;i<4;i++)
                {
                    var declaration=textureFailure && i<3?TextureDeclaration("OnDemandTexture","TextureDrain"+i,i==0?invalidTexture:textureInput,i%2==0?"DXT3":"DXT5",true):Declaration(i==3?"AptAptData":"W3DAnimation","Drain"+i,Namespaces());
                    IAssetBuilderPlugin selected=i==3?(IAssetBuilderPlugin)xml:textureFailure?(IAssetBuilderPlugin)texture:plugin;
                    if(i==3) ((XmlElement)declaration.Node).SetAttribute("File",input);
                    else if(!textureFailure)
                    {
                        ((XmlElement)declaration.Node).SetAttribute("NumFrames","65536");((XmlElement)declaration.Node).SetAttribute("FrameRate","30");
                        declaration.Node.InnerXml="<CompressionSettings xmlns='uri:ea.com:eala:asset' AllowAdaptiveDelta='false' MaxTranslationError='-1' ForceReductionRate='1'/><Channels xmlns='uri:ea.com:eala:asset'><ChannelScalar Type='XTranslation' TypeId='"+
                            (!cancel && i==0?"0":"1355976437")+"' Pivot='0' FirstFrame='0'><Frame>0</Frame><Frame>1</Frame></ChannelScalar></Channels>";
                    }
                    Metadata(declaration,selected);
                    var scope=execution.BeginDocument(declaration.Document,fixture.Output);scopes.Add(scope);
                    execution.Enqueue(scope,fixture.Asset(declaration),declaration,selected,
                        ()=>{throw new InvalidOperationException("Controlled generous resources must admit each certified worker");},
                        ()=>{Interlocked.Increment(ref verified);dispatchGate.Wait();return true;}, valid=>Require(valid,"Drain declaration verification failed"),source=>{committed++;});
                    execution.CompleteDocument(scope,()=>{completed++;});
                }
                // Capture all registered native calls, including completed calls;
                // no queue order or active-list shape participates in assertions.
                var registry=(System.Collections.IDictionary)fixture.SchedulerType.GetField("registry",SchedulerFixture.Fields).GetValue(execution);
                foreach(object entry in registry.Values)
                {
                    Task compute=(Task)entry.GetType().GetField("Compute",SchedulerFixture.Fields).GetValue(entry);
                    if(compute!=null)captured.Add(compute);
                    string directory=(string)entry.GetType().GetField("Directory",SchedulerFixture.Fields).GetValue(entry);
                    if(directory!=null)privateTextureDirectories.Add(directory);
                }
                Require(captured.Count==4,"All four drain cases must dispatch production worker tasks");
                if(cancel)cancellation.Cancel();
                dispatchGate.Set();
                Exception observed=null;
                try {execution.Flush();} catch(Exception error){observed=error;}
                Require(observed!=null,"Failure or cancellation must reject the pending wave");
                if(cancel)Require(observed is OperationCanceledException,"Cancellation must retain its original exception contract");
                else if(textureFailure)Require(observed is BinaryAssetBuilderException,"Texture worker failure must preserve its original backend exception");
                else Require(observed is NotSupportedException,"Native worker failure must preserve its original backend exception");
                try { ((IDisposable)execution).Dispose(); }
                catch(Exception cleanup) { Require(Object.ReferenceEquals(cleanup,observed),"Drain must preserve the original worker failure identity"); }
                Require(captured.TrueForAll(t=>t.IsCompleted),"Disposal must join every previously dispatched native worker");
                Require(!execution.HasPending,"Disposal must drain active envelopes");
                Require(scopes.Exists(scope=>scope.IsFailed) && scopes.Exists(scope=>!scope.IsCompleted),"A failed document version must never become successfully complete");
                Require(committed<=3 && completed<=3,"A failed or cancelled physical asset may not publish its state or document");
                var serializer=new JavaScriptSerializer();
                var snapshot=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(fixture.PerformanceType.GetMethod("Snapshot",SchedulerFixture.Fields).Invoke(fixture.Performance,null)));
                int measured=0;
                foreach(Dictionary<string,object> entry in (System.Collections.IEnumerable)snapshot["entries"])
                    if((string)entry["name"]=="core.asset.compile")measured+=Convert.ToInt32(entry["count"]);
                Require(measured==4,"Failed/cancelled native calls must remain in Core performance counts after join");
                using(var writer=new FileStream(input,FileMode.Open,FileAccess.Write,FileShare.None))Require(writer.CanWrite,"All external leases must be released after native drain");
                if(textureFailure)
                {
                    Require(privateTextureDirectories.Count==3,"Texture failure wave must isolate all three CDATA paths");
                    foreach(string directory in privateTextureDirectories)Require(!Directory.Exists(directory),"Failed texture wave retained private CDATA output");
                    using(var writer=new FileStream(textureInput,FileMode.Open,FileAccess.Write,FileShare.None))Require(writer.CanWrite,"Texture failure drain retained source lease");
                    using(var writer=new FileStream(invalidTexture,FileMode.Open,FileAccess.Write,FileShare.None))Require(writer.CanWrite,"Texture failure drain retained failing source lease");
                }
                lifecycleEvidence.Add(new {kind=textureFailure?"scheduler-texture-failure-drain":cancel?"scheduler-cancellation-drain":"scheduler-native-failure-drain",dispatched=captured.Count,
                    allWorkersJoined=true,verified,committed,completed,failedDocumentNotCompleted=true});
            }
            finally {dispatchGate.Set();try{((IDisposable)execution).Dispose();}finally{texture.Dispose();}}
        }
    }
    public static int Main(string[] args)
    {
        var failures=new List<object>();
        try
        {
            Directory.CreateDirectory(args[0]);
            Action<string,Action> run=delegate(string name,Action action){try{action();}catch(Exception error){failures.Add(new{name,error=error.ToString()});Console.Error.WriteLine("FAIL prepared-backend "+name+": "+error);}};
            run("apt-leases",()=>AptLeaseAndBorrow(args[0]));run("qualification",()=>ParallelQualification(args[0]));run("environment",()=>EnvironmentQualification(args[4]));
            run("resource-budget",()=>ResourceBudget(args[0]));run("w3x-budget",W3xBudget);run("schema-defaults",SchemaDefaults);run("preparation-budget",PreparationBudget);run("rejection-reasons",()=>PreparationRejectionReasons(args[0]));
            run("worker-buffers",()=>WorkerBuffers(args[0],args[4]));run("tokenizer-workers",()=>TokenizerWorkers(args[0]));run("texture-workers",()=>TextureWorkers(args[0]));run("texture-owner-diagnostics",()=>TextureOwnerDiagnostics(args[0]));
            run("completion-driven",()=>SchedulerCompletion(args[0]));run("owner-completion-failure",()=>SchedulerOwnerFailure(args[0]));run("native-failure-drain",()=>SchedulerDrain(args[0],false));run("cancellation-drain",()=>SchedulerDrain(args[0],true));run("texture-failure-drain",()=>SchedulerDrain(args[0],false,true));
            File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new {
                succeeded=failures.Count==0,sourceCommit=args[2],runId=args[3],checks,lifecycleEvidence,failures,
                scope="Borrowed finalized XML/defaults/namespaces/metadata, Windows stable file leases, APT/W3X/Audio/Texture worker three-buffer equality, cold S3 gray-table overlap, isolated Audio/OnDemand CDATA, atlas source leases, native/texture failure cleanup, three tokenizers on four platforms, closed route qualification and resource budgets; component evidence only"
            }));
            if(failures.Count!=0){Console.Error.WriteLine("PreparedBackendWork failed in "+failures.Count+" independent groups");return 1;}
            Console.WriteLine("PASS PreparedBackendWork: "+checks+" assertions"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
