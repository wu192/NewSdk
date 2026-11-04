using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Application;
using EALA.Metrics;

// Runs inside the independently linked production-object validation host.
// This fixture uses real SDK schemas, Core, recovered Armor and verifier code.
internal static class CompilerCoreLifecycle
{
    const string Ns = "uri:ea.com:eala:asset";
    static readonly List<string> Tests = new List<string>();
    static string sdk, work, probe;
    static int checks;
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static void Require(bool value, string message) { ++checks; if (!value) throw new Exception(message); }
    static object Field(object target, string name) { return typeof(DocumentProcessor).GetField(name, Private).GetValue(target); }
    static Stack<string> Documents { get { return (Stack<string>)Field(null, "_CurrentDocumentStack"); } }
    static Stack<string> Streams { get { return (Stack<string>)Field(null, "_CurrentStreamStack"); } }
    static IList Inclusions(DocumentProcessor p) { return (IList)Field(p, "_DocumentStack"); }
    static string Write(string relative, string contents)
    {
        string path = Path.GetFullPath(Path.Combine(work, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, contents); return path;
    }
    static string Asset(string body) { return "<AssetDeclaration xmlns='" + Ns + "'>" + body + "</AssetDeclaration>"; }
    static string Include(string source) { return "<Includes><Include type='all' source='" + source + "'/></Includes>"; }
    static Settings Configure(string name)
    {
        var config = new XmlDocument(); config.Load(Path.Combine(sdk, "tools/BinaryAssetBuilder.exe.config"));
        var s = (Settings)new SerializedSectionHandler().Create(null, null, config.SelectSingleNode("/configuration/assetbuilder"));
        s.DataRoot = work; s.SchemaPath = Path.Combine(sdk, "Schemas/xsd/CnC3Types.xsd");
        s.InputPath = Path.Combine(work, name, "root.xml");
        s.OutputDirectory = Path.Combine(work, name, "output");
        s.IntermediateOutputDirectory = Path.Combine(work, name, "intermediate");
        s.SessionCacheDirectory = Path.Combine(work, name, "cache");
        s.DefaultDataPaths = work; s.DefaultArtPaths = work; s.DefaultAudioPaths = work;
        s.TargetPlatform = TargetPlatform.Win32; s.BuildCache = false; s.SessionCache = false;
        s.LinkedStreams = true; s.OutputAssetReport = false; s.OutputStringHashes = false;
        s.ErrorLevel = 1; s.UsePrecompiled = false; s.StreamHints = false; s.StableSort = false;
        Settings.Current = s; SettingsLoader.PostProcessSettings(work);
        Directory.CreateDirectory(s.OutputDirectory); Directory.CreateDirectory(s.IntermediateOutputDirectory);
        Directory.CreateDirectory(s.SessionCacheDirectory);
        return s;
    }
    static DocumentProcessor Processor(BuiltinCompilers plugins)
    {
        var schemas = new SchemaSet(Settings.Current.StableSort);
        HashProvider.InitializeStringHashes(Settings.Current.SessionCacheDirectory);
        var cache = new SessionCache(); cache.InitializeCache(new List<string>());
        var verifiers = new VerifierPluginRegistry(new PluginDescriptor[0], TargetPlatform.Win32);
        verifiers.DefaultPlugin = (IAssetBuilderVerifierPlugin)Activator.CreateInstance(
            typeof(DocumentProcessor).Assembly.GetType("BinaryAssetBuilder.GameDataVerifier.Plugin", true), true);
        verifiers.DefaultPlugin.Initialize(null, TargetPlatform.Win32);
        return new DocumentProcessor(Settings.Current, plugins.Registry, verifiers) { Cache = cache, SchemaSet = schemas };
    }
    static void Run(DocumentProcessor p, string input)
    {
        bool success; p.ProcessDocument(input, true, false, out success); Require(success, "Core did not report success: " + input);
    }
    static void Failure(DocumentProcessor p, string input, ErrorCode expected)
    {
        try { Run(p, input); }
        catch (BinaryAssetBuilderException e) { Require(e.ErrorCode == expected, "Expected " + expected + ", got " + e.ErrorCode + ": " + e); return; }
        throw new Exception("Core accepted rejected input: " + input);
    }
    static void Clean(DocumentProcessor p, string configuration, params string[] paths)
    {
        Require(Documents.Count == 0 && Streams.Count == 0 && Inclusions(p).Count == 0, "Core traversal frames leaked");
        Require(!(bool)Field(p, "_CompilingProject"), "Project flag leaked");
        Require(Field(p, "_ProjectDefaultConfigurations") == null, "Project configuration table leaked");
        foreach (string path in paths)
        {
            AssetDeclarationDocument document;
            p.Cache.TryGetDocument(Path.GetFullPath(path), configuration, TargetPlatform.Win32, false, out document);
            if (document != null) Require(!document.Processing, "Processing flag leaked: " + path);
        }
    }
    static Dictionary<string, string> Outputs(string root)
    {
        var result = new Dictionary<string, string>();
        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path);
            if (extension != ".manifest" && extension != ".bin" && extension != ".imp" && extension != ".relo") continue;
            using (var sha = SHA256.Create()) result.Add(path.Substring(root.Length), BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))));
        }
        return result;
    }
    static void Equal(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        Require(expected.Count == actual.Count && expected.Count == 4, "Expected four linked stream output files");
        foreach (var pair in expected) Require(actual.ContainsKey(pair.Key) && actual[pair.Key] == pair.Value, "Output changed: " + pair.Key);
    }
    static void RetryCase(BuiltinCompilers plugins, string name, bool inheritance)
    {
        var s = Configure(name); var p = Processor(plugins);
        string root = Write(name + "/root.xml", Asset(Include("middle.xml")));
        string middle = Write(name + "/middle.xml", Asset(Include("leaf.xml")));
        string leaf = Path.Combine(work, name, "leaf.xml");
        if (inheritance) Write(name + "/leaf.xml", Asset("<ArmorTemplate id='Broken' inheritFrom='MissingParent'/>"));
        // Seed pre-existing frames to ensure this call never clears a caller's state.
        Documents.Push("parent-document"); Streams.Push("parent-stream"); Inclusions(p).Add("parent-inclusion");
        try
        {
            Failure(p, root, inheritance ? ErrorCode.InheritFromError : ErrorCode.FileNotFound);
            Require(Documents.Count == 1 && Documents.Peek() == "parent-document", "Document parent frame lost");
            Require(Streams.Count == 1 && Streams.Peek() == "parent-stream", "Stream parent frame lost");
            Require(Inclusions(p).Count == 1 && (string)Inclusions(p)[0] == "parent-inclusion", "Inclusion parent frame lost");
        }
        finally { Documents.Pop(); Streams.Pop(); Inclusions(p).RemoveAt(0); }
        Clean(p, null, root, middle, leaf);
        AssetDeclarationDocument failed; p.Cache.TryGetDocument(root, null, TargetPlatform.Win32, false, out failed);
        Require(failed.State != DocumentState.Complete, "Failed root was completed/cached as successful");
        Write(name + "/leaf.xml", probe);
        Run(p, root); Clean(p, null, root, middle, leaf);
        Dictionary<string, string> repaired = Outputs(s.OutputDirectory);
        // Repeat a completed stream: early return must neither leak frames nor change output.
        Run(p, root); Clean(p, null, root, middle, leaf); Equal(repaired, Outputs(s.OutputDirectory));
        // New root includes an already complete document with GenerateOutput=false.
        string reused = Write(name + "/reuse.xml", Asset(Include("leaf.xml")));
        Run(p, reused); Clean(p, null, reused, leaf);
        // Cold execution uses the same source paths, a fresh real cache and clean output roots.
        string oldOutput = s.OutputDirectory, oldIntermediate = s.IntermediateOutputDirectory;
        s.OutputDirectory = Path.Combine(work, name, "cold-output");
        s.IntermediateOutputDirectory = Path.Combine(work, name, "cold-intermediate");
        var cold = Processor(plugins); Run(cold, root); Clean(cold, null, root, middle, leaf);
        Equal(repaired, Outputs(s.OutputDirectory)); s.OutputDirectory = oldOutput; s.IntermediateOutputDirectory = oldIntermediate;
        Tests.Add(name + ":real-core-failure-same-processor-cache-retry-parent-frames-complete-reuse-cold-output-equality");
    }
    static void CycleCase(BuiltinCompilers plugins)
    {
        Configure("cycle"); var p = Processor(plugins);
        string a = Write("cycle/root.xml", Asset(Include("b.xml")));
        string b = Write("cycle/b.xml", Asset(Include("root.xml")));
        Failure(p, a, ErrorCode.CircularDependency); Clean(p, null, a, b);
        Write("cycle/b.xml", probe); Run(p, a); Clean(p, null, a, b);
        Tests.Add("real-core-circular-include-rejected-then-fixed-same-processor-cache");
    }
    static void SchemaReceiptCase(BuiltinCompilers plugins)
    {
        Configure("schema-receipt"); var p = Processor(plugins);
        string commands = "<LogicCommandSet xmlns='" + Ns + "' id='Commands'><Cmd>DoIt</Cmd><Cmd>DoIt</Cmd></LogicCommandSet>";
        string root = Write("schema-receipt/root.xml", Asset("<LogicCommand id='DoIt'/>" + commands));
        Run(p,root); Clean(p,null,root);
        AssetDeclarationDocument document; p.Cache.TryGetDocument(root,null,TargetPlatform.Win32,false,out document);
        Require(document.HasValidatedSchema,"Completed Core validation did not issue its schema receipt");
        var xml = new XmlDocument(); xml.LoadXml(commands);
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea",Ns);
        AssetBuffer checkedResult = Recovered.LogicCommandSetCompiler.CompileXml(xml.DocumentElement,ns,false);
        var instance = new InstanceDeclaration(document) { XmlNode = xml.DocumentElement };
        AssetBuffer validatedResult = Recovered.LogicCommandSetCompiler.ProcessInstance(instance,TargetPlatform.Win32);
        Require(checkedResult.InstanceData.SequenceEqual(validatedResult.InstanceData) &&
            checkedResult.RelocationData.SequenceEqual(validatedResult.RelocationData) && checkedResult.ImportsData.SequenceEqual(validatedResult.ImportsData),
            "Validated Core dispatch changed LogicCommandSet payload bytes");
        string bad = Write("schema-receipt/bad.xml",Asset("<LogicCommandSet id='Bad'><Cmd Bogus='1'>DoIt</Cmd></LogicCommandSet>"));
        int compiled = p.InstanceCompileCount;
        Failure(p,bad,ErrorCode.SchemaValidation); Clean(p,null,bad);
        AssetDeclarationDocument rejected; p.Cache.TryGetDocument(bad,null,TargetPlatform.Win32,false,out rejected);
        Require(!rejected.HasValidatedSchema && p.InstanceCompileCount==compiled,"Invalid XSD input reached a compiler or retained a schema receipt");
        var invalid = new XmlDocument(); invalid.LoadXml("<LogicCommandSet xmlns='"+Ns+"'><Cmd Bogus='1'>DoIt</Cmd></LogicCommandSet>");
        AssetBuffer direct = Recovered.LogicCommandSetCompiler.CompileXml(invalid.DocumentElement,ns,false);
        invalid.DocumentElement.FirstChild.Attributes.RemoveNamedItem("Bogus");
        AssetBuffer plain = Recovered.LogicCommandSetCompiler.CompileXml(invalid.DocumentElement,ns,false);
        Require(direct.InstanceData.SequenceEqual(plain.InstanceData) && direct.RelocationData.SequenceEqual(plain.RelocationData) &&
            direct.ImportsData.SequenceEqual(plain.ImportsData),"Plugin repeated Core schema validation or changed the payload");
        string afterFailure = Write("schema-receipt/after-failure.xml",Asset("<LogicCommand id='AfterFailure'/>"));
        Run(p,afterFailure); Clean(p,null,afterFailure);
        AssetDeclarationDocument recovered; p.Cache.TryGetDocument(afterFailure,null,TargetPlatform.Win32,false,out recovered);
        Require(recovered.HasValidatedSchema && p.SchemaSet.Schemas.IsCompiled,
            "A rejected document damaged the processor's shared compiled schema snapshot");
        Tests.Add("Core-XSD-invalid-input-rejected-before-dispatch-plugin-serializes-without-repeating-schema-validation");
    }
    static void SavedSessionMemoryCase(BuiltinCompilers plugins)
    {
        var settings = Configure("memory-session"); settings.SessionCache = true;
        var p = Processor(plugins);
        var cache = new SessionCache();
        cache.LoadCacheFrom(null, Path.Combine(settings.SessionCacheDirectory, "session.xml"));
        cache.InitializeCache(new List<string>(), p.SchemaSet.ContentIdentity); p.Cache = cache;
        string leaf = Write("memory-session/leaf.xml", probe);
        string root = Write("memory-session/root.xml", Asset(Include("leaf.xml") + "<Defines><Define name='MEMORY_VALUE' value='17'/></Defines>"));
        Run(p, root); Clean(p, null, root, leaf); cache.SaveCache(false);
        var expected = Outputs(settings.OutputDirectory);
        AssetDeclarationDocument document; cache.TryGetDocument(leaf,null,TargetPlatform.Win32,false,out document);
        document.SelfInstances[0].CustomDataPath = "previous-stream-only";
        document.SelfInstances[0].ReferencedInstances.Add(new InstanceHandle("ArmorTemplate", "PreviousStreamOnly"));
        int compiled = p.InstanceCompileCount;
        cache.InitializeCache(new List<string>(), p.SchemaSet.ContentIdentity);
        Run(p, root); Clean(p, null, root, leaf);
        cache.TryGetDocument(leaf,null,TargetPlatform.Win32,false,out document);
        Require(document.SelfInstances[0].CustomDataPath != "previous-stream-only" &&
            !document.SelfInstances[0].ReferencedInstances.Any(value => value.InstanceName == "PreviousStreamOnly"),
            "Saved in-memory session retained a previous stream's transient asset state");
        Require(p.InstanceCompileCount == compiled, "Reentering a saved in-memory session recompiled unchanged assets");
        Equal(expected, Outputs(settings.OutputDirectory));
        Tests.Add("saved-session-memory-reentry-clears-transient-references-and-cdata-preserves-bytes");
    }
    static uint LegacyXmlTextHash(uint seed, string text)
    {
        for (int offset=0; offset<text.Length; offset+=512)
            seed=HashProvider.GetTextHash(seed,text.Substring(offset,Math.Min(512,text.Length-offset)));
        return seed;
    }
    sealed class XmlText : StringWriter
    {
        public override Encoding Encoding { get { return Encoding.Default; } }
    }
    static void XmlHashBlocksCase()
    {
        Type type=typeof(HashProvider).Assembly.GetType("BinaryAssetBuilder.Core.HashingWriter",true);
        MethodInfo final=type.GetMethod("GetFinalHash");
        foreach (uint seed in new[] {0u,1u,0xdeadbeefu})
        foreach (int length in new[] {0,1,511,512,513,1023,1024,1025})
        foreach (string tail in new[] {"", "\0", "é汉", "\ud83d\ude00"})
        {
            string text=new string('x',length)+tail;
            var writer=(TextWriter)Activator.CreateInstance(type,new object[] {seed});
            for(int offset=0; offset<text.Length;)
            {
                int count=Math.Min(37,text.Length-offset);
                if(offset%3==0) { writer.Write(text[offset]); count=1; }
                else if(offset%3==1) writer.Write(text.Substring(offset,count));
                else writer.Write(text.ToCharArray(),offset,count);
                offset+=count;
            }
            uint expected=LegacyXmlTextHash(seed,text);
            Require((uint)final.Invoke(writer,null)==expected && (uint)final.Invoke(writer,null)==expected,
                "XML hash changed ANSI/NUL/512-character block or repeated-finalization behavior");
            writer.Write("append");
            Require((uint)final.Invoke(writer,null)==LegacyXmlTextHash(seed,text+"append"),
                "XML hash finalization consumed the pending block");
            writer.Dispose();
        }
        foreach(string text in new[] {new string('a',511)+"汉字",new string('b',511)+"\ud83d\ude00",new string('c',1025)+"é"})
        {
            var xml=new XmlDocument();var node=xml.CreateElement("HashCase");node.SetAttribute("text",text);node.InnerText=text;
            var serialized=new XmlText();using(var writer=XmlWriter.Create(serialized)) node.WriteTo(writer);
            XmlNode hashNode=node;
            Require(HashProvider.GetXmlHash(0x12345678u,ref hashNode)==LegacyXmlTextHash(0x12345678u,serialized.ToString()),
                "XML hash differs from legacy XML serialization and ANSI block hashing");
        }
        Tests.Add("legacy-XML-hash-seeds-ANSI-NUL-512-boundaries-fragmented-writes-finalize-append");
    }
    sealed class SaveFaultCache : SessionCache
    {
        public bool Failed;
        public override void SaveDocumentToCache(string path, string configuration, TargetPlatform platform, AssetDeclarationDocument document)
        {
            if (!Failed) { Failed = true; throw new IOException("Injected real-cache save failure"); }
            base.SaveDocumentToCache(path, configuration, platform, document);
        }
    }
    static void SaveFailureCase(BuiltinCompilers plugins)
    {
        Configure("cache-save"); var p = Processor(plugins);
        string input = Write("cache-save/root.xml", probe);
        var fault = new SaveFaultCache(); fault.InitializeCache(new List<string>()); p.Cache = fault;
        bool rejected = false;
        try { Run(p, input); }
        catch (IOException e) { rejected = e.Message == "Injected real-cache save failure"; }
        Require(rejected && fault.Failed, "SaveDocumentToCache fault was not reached/propagated");
        Clean(p, null, input);
        // Cache persistence failure is not acceptance of its half-committed document state.
        // Production must discard this cache; verify the same processor can run with fresh real state.
        var fresh = new SessionCache(); fresh.InitializeCache(new List<string>()); p.Cache = fresh;
        Run(p, input); Clean(p, null, input);
        Tests.Add("real-cache-save-fault-propagates-cleans-frames-then-same-processor-fresh-cache-success");
    }
    sealed class VersionSaveCache : SessionCache
    {
        internal readonly List<AssetDeclarationDocument> Saved = new List<AssetDeclarationDocument>();
        public override void SaveDocumentToCache(string path, string configuration, TargetPlatform platform, AssetDeclarationDocument document)
        {
            Saved.Add(document); base.SaveDocumentToCache(path, configuration, platform, document);
        }
    }
    static void DeferredVersionCacheCase(BuiltinCompilers plugins)
    {
        Configure("deferred-version"); var p = Processor(plugins);
        var cache = new VersionSaveCache(); cache.InitializeCache(new List<string>()); p.Cache = cache;
        string input = Write("deferred-version/root.xml", probe); Run(p, input);
        AssetDeclarationDocument previous;
        Require(cache.TryGetDocument(input, null, TargetPlatform.Win32, false, out previous) && previous != null,
            "Completed document missing before deferred version fixture");
        XmlDocument retained = previous.XmlDocument;
        Require(retained != null, "Deferred fixture must hold a real completed document DOM");
        var versions = (IDictionary)Field(p, "_ExecutionDocuments"); object version = versions[previous];
        Type versionType = version.GetType();
        Action<string,object> setVersion = (name,value) => versionType.GetField(name, BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(version,value);
        var outputs = (IDictionary)versionType.GetField("Outputs", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(version);
        object completed = versionType.GetField("CompletedOutputs", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(version);
        var addCompleted = completed.GetType().GetMethod("Add");
        var firstOutput = new OutputManager(p, null, Path.Combine(work,"deferred-version/one/stream"), Path.Combine(work,"deferred-version/one/intermediate"), null,null,null);
        var secondOutput = new OutputManager(p, null, Path.Combine(work,"deferred-version/two/stream"), Path.Combine(work,"deferred-version/two/intermediate"), null,null,null);
        var first = new AssetExecutionDocument(); var second = new AssetExecutionDocument();
        Action<AssetExecutionDocument,string,object> setScope = (scope,name,value) => typeof(AssetExecutionDocument).GetProperty(name).SetValue(scope,value,null);
        foreach(var scope in new[]{first,second})
        {
            setScope(scope,"Document",previous);setScope(scope,"IsClosed",true);
            setScope(scope,"PendingAssetWorks",1);setScope(scope,"ReadOnlyInputReferences",1);
        }
        outputs.Add(firstOutput,first);outputs.Add(secondOutput,second);
        setVersion("Finalized",false);setVersion("ResetRequested",true);
        setScope(first,"PendingAssetWorks",0);setScope(first,"ReadOnlyInputReferences",0);setScope(first,"IsCompleted",true);
        addCompleted.Invoke(completed,new object[]{first});
        MethodInfo finish=typeof(DocumentProcessor).GetMethod("TryCompleteDocumentVersion",Private);
        finish.Invoke(p,new[]{(object)previous,version});
        Require(Object.ReferenceEquals(previous.XmlDocument,retained) && second.ReadOnlyInputReferences==1,
            "One completed output must not reset a document still borrowed by another output");
        FileHashItem hash;cache.TryGetFile(input,null,TargetPlatform.Win32,out hash);
        var replacement=new AssetDeclarationDocument(p,hash,input,null);
        Require(cache.ReplaceDocumentForExecution(input,null,TargetPlatform.Win32,previous,replacement),
            "Owner could not register a distinct mutable document version");
        cache.Saved.Clear();cache.SaveDocumentToCache(input,null,TargetPlatform.Win32,replacement);
        setScope(second,"PendingAssetWorks",0);setScope(second,"ReadOnlyInputReferences",0);setScope(second,"IsCompleted",true);
        addCompleted.Invoke(completed,new object[]{second});finish.Invoke(p,new[]{(object)previous,version});
        AssetDeclarationDocument current;cache.TryGetDocument(input,null,TargetPlatform.Win32,false,out current);
        Require(Object.ReferenceEquals(current,replacement) && cache.Saved.Count==1 && Object.ReferenceEquals(cache.Saved[0],replacement),
            "Late completion of an old version overwrote the replacement SessionCache entry");
        Require(previous.XmlDocument==null,"Old DOM must release after all output users have completed");
        Tests.Add("two-output-document-retention-old-version-late-completion-preserves-current-cache-identity");
    }
    sealed class CompletionMetricFault : IMetricsListener
    {
        internal bool FailFinal, FailTimer;
        internal readonly Exception FinalFailure = new IOException("Injected final BuildSuccessful metric failure");
        internal readonly Exception TimerFailure = new IOException("Injected ProcessingTime Dispose metric failure");
        internal int FinalCalls, TimerCalls;
        public void Open() { }
        public void Close() { }
        public void SubmitMetrics(Metric metric)
        {
            if (metric.Name == "BAB.BuildSuccessful")
            {
                FinalCalls++;
                if (FailFinal) throw FinalFailure;
            }
            if (metric.Name == "BAB.ProcessingTime")
            {
                TimerCalls++;
                if (FailTimer) throw TimerFailure;
            }
        }
    }
    static void MetricsFailureCase(BuiltinCompilers plugins, bool failFinal, bool failTimer)
    {
        const string finalKey = "BinaryAssetBuilder.ProcessDocument.FinalMetricsFailure";
        const string timerKey = "BinaryAssetBuilder.ProcessDocument.ProcessingTimeFailure";
        string name = "metrics-failure-" + failFinal + "-" + failTimer;
        Configure(name); var p = Processor(plugins);
        string bad = Write(name + "/bad.xml", Asset("<ArmorTemplate id='BadMetrics' inheritFrom='MissingParent'/>"));
        string good = Write(name + "/good.xml", probe);
        MetricManager.GetDescriptor("BAB.BuildSuccessful", MetricType.Success, "Build successful");
        MetricManager.GetDescriptor("BAB.ProcessingTime", MetricType.Duration, "Processing time");
        var listener = new CompletionMetricFault { FailFinal = failFinal, FailTimer = failTimer };
        bool oldEnabled = MetricManager.Enabled, opened = false;
        MetricManager.AddListener(listener);
        try
        {
            MetricManager.OpenSession(); opened = true;
            BinaryAssetBuilderException primary = null;
            bool success = false;
            try { p.ProcessDocument(bad, true, false, out success); }
            catch (BinaryAssetBuilderException error) { primary = error; }
            Require(primary != null && primary.ErrorCode == ErrorCode.InheritFromError && !success,
                "Completion metrics replaced the real inheritance failure");
            Require(primary.StackTrace != null && primary.StackTrace.Contains("ProcessDocumentInternal"),
                "Original real Core compilation stack was not preserved");
            Require(primary.Data.Contains(finalKey) == failFinal && primary.Data.Contains(timerKey) == failTimer,
                "Secondary metrics error attachments are missing or unexpected");
            if (failFinal) Require(Object.ReferenceEquals(primary.Data[finalKey], listener.FinalFailure),
                "Final metrics secondary exception identity was lost");
            if (failTimer) Require(Object.ReferenceEquals(primary.Data[timerKey], listener.TimerFailure),
                "MetricTimer.Dispose secondary exception identity was lost");
            Clean(p, null, bad);

            // Without a compilation failure, keep the metrics exception as the
            // primary error. A later Dispose error must not replace it either.
            Exception metricFailure = null; success = false;
            try { p.ProcessDocument(good, true, false, out success); }
            catch (Exception error) { metricFailure = error; }
            Require(success && Object.ReferenceEquals(metricFailure, failFinal ? listener.FinalFailure : listener.TimerFailure),
                "Successful compilation swallowed or replaced its metrics failure");
            if (failFinal && failTimer)
                Require(Object.ReferenceEquals(metricFailure.Data[timerKey], listener.TimerFailure),
                    "Timer failure replaced the first successful-build metrics error");
            Require(listener.FinalCalls == 2 && listener.TimerCalls == 2, "Completion metric fault paths were not both reached");
            Clean(p, null, bad, good);
        }
        finally
        {
            try { if (opened) MetricManager.CloseSession(); }
            finally
            {
                Require(MetricManager.RemoveListener(listener), "Fault listener was not removable after completion failure");
                MetricManager.Enabled = oldEnabled;
            }
        }
        string retry = Write(name + "/retry.xml", probe);
        Run(p, retry); Clean(p, null, bad, good, retry);
        Tests.Add(name + ":real-inheritance-primary-and-stack-preserved-secondary-errors-attached-metrics-primary-on-success-listener-removed-retry-success");
    }
    static void VerifyProjectOutput(Settings settings)
    {
        string prefix = Path.Combine(settings.OutputDirectory, "project", "root_G");
        var expected = new HashSet<uint>();
        var source = new XmlDocument(); source.LoadXml(probe);
        foreach (XmlNode child in source.DocumentElement.ChildNodes)
            if (child is XmlElement) expected.Add(InstanceHandle.GetInstanceId(((XmlElement)child).GetAttribute("id")));
        using (var manifest = new BinaryAssetBuilder.Utility.Manifest())
        {
            // Linked payloads are the three stream files, not loose .asset files.
            Require(manifest.Load(prefix + ".manifest", new[] { settings.OutputDirectory }, false), "Good project manifest did not load");
            Require(manifest.Assets.Length == expected.Count && expected.Count == 3, "Good project manifest asset set incomplete");
            int instance = 4, relocation = 4, imports = 4;
            foreach (var asset in manifest.Assets)
            {
                Require(asset.TypeId == InstanceHandle.GetTypeId("ArmorTemplate") && expected.Remove(asset.InstanceId),
                    "Good project manifest contains an unexpected/duplicate asset");
                Require(asset.InstanceDataSize > 0 && asset.LinkedInstanceOffset == instance &&
                    asset.LinkedRelocationOffset == relocation && asset.LinkedImportsOffset == imports,
                    "Good project manifest has invalid linked payload sizes/offsets");
                instance += asset.InstanceDataSize; relocation += asset.RelocationDataSize; imports += asset.ImportsDataSize;
            }
            Require(expected.Count == 0 && manifest.TotalInstanceDataSize == instance - 4, "Good project manifest omitted probe payloads");
            string[] extensions = { ".bin", ".relo", ".imp" }; int[] sizes = { instance, relocation, imports };
            for (int i = 0; i < extensions.Length; i++)
            {
                byte[] bytes = File.ReadAllBytes(prefix + extensions[i]);
                Require(bytes.Length == sizes[i] && BitConverter.ToUInt32(bytes, 0) == manifest.StreamChecksum,
                    "Good project linked stream size/checksum does not match its manifest: " + extensions[i]);
            }
        }
    }
    static readonly string[] ProcessorStateFields = {
        "_MissingReferences", "_CurrentDocumentStack", "_CurrentStreamStack", "_TypeProcessingTime",
        "_TotalProcInstancesTime", "_TotalPrepareOutputTime", "_TotalPrepareSourceTime", "_TotalPostProcTime", "_TotalValidateTime"
    };
    static object[] ProcessorState() { return ProcessorStateFields.Select(name => Field(null, name)).ToArray(); }
    static void SameProcessorState(object[] expected)
    {
        object[] actual = ProcessorState();
        for (int i = 0; i < actual.Length; i++)
            Require(i < 4 ? Object.ReferenceEquals(actual[i], expected[i]) : Object.Equals(actual[i], expected[i]),
                "Processor state did not restore " + ProcessorStateFields[i]);
    }
    static void EmptyProcessorState(object[] parent)
    {
        object[] actual = ProcessorState();
        for (int i = 0; i < 4; i++) Require(!Object.ReferenceEquals(actual[i], parent[i]), "Processor child state reused " + ProcessorStateFields[i]);
        Require(((InstanceHandleSet)actual[0]).Count == 0 && Documents.Count == 0 && Streams.Count == 0 &&
            ((IDictionary)actual[3]).Count == 0, "Processor scope did not isolate reference/traversal/type tables");
        for (int i = 4; i < actual.Length; i++) Require((TimeSpan)actual[i] == TimeSpan.Zero, "Processor scope inherited a parent timer");
    }
    static void RejectProcessorState(Action action, string message)
    {
        bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }
    static void RejectProcessorStateWorker(Action action, string message)
    {
        Exception failure = null;
        var thread = new Thread(delegate() { try { action(); } catch (Exception error) { failure = error; } });
        thread.IsBackground = true; thread.Start();
        Require(thread.Join(5000), message + " (worker blocked)");
        Require(failure is InvalidOperationException, message);
    }
    static void ProcessorStateCase(BuiltinCompilers plugins)
    {
        Settings oldSettings = Settings.Current;
        Configure("processor-state-parent"); var parentProcessor = Processor(plugins);
        object[] original = ProcessorState();
        try
        {
            using (DocumentProcessor.BeginStateScope())
            {
                var missing = new InstanceHandle("ArmorTemplate", "SameMissingReferenceAcrossBuilds");
                Require(parentProcessor.MissingReferences.TryAdd(missing), "Parent missing-reference sentinel duplicate");
                Documents.Push("state-parent-document"); Streams.Push("state-parent-stream");
                for (int i = 4; i < ProcessorStateFields.Length; i++)
                    typeof(DocumentProcessor).GetField(ProcessorStateFields[i], Private).SetValue(null, TimeSpan.FromTicks(100 + i));
                parentProcessor.AddCompileTime(missing, TimeSpan.FromTicks(9876));
                var parentTypes = (IDictionary)Field(null, "_TypeProcessingTime");
                object parentType = parentTypes[missing.TypeId];
                object[] parent = ProcessorState();
                for (int round = 0; round < 2; round++)
                {
                    using (var child = DocumentProcessor.BeginStateScope())
                    {
                        EmptyProcessorState(parent);
                        string name = "processor-state-child-" + round;
                        Configure(name); var processor = Processor(plugins);
                        Require(processor.MissingReferences.TryAdd(new InstanceHandle(missing)) &&
                            !processor.MissingReferences.TryAdd(new InstanceHandle(missing)),
                            "Missing-reference warning deduplication leaked across processor scopes");
                        string bad = Write(name + "/bad.xml", Asset("<ArmorTemplate id='StateBad' inheritFrom='MissingParent'/>"));
                        string good = Write(name + "/good.xml", probe);
                        Failure(processor, bad, ErrorCode.InheritFromError); Clean(processor, null, bad);
                        Run(processor, good); Clean(processor, null, bad, good);
                        object[] childState = ProcessorState();
                        var nested = DocumentProcessor.BeginStateScope();
                        try
                        {
                            EmptyProcessorState(childState);
                            Require(processor.MissingReferences.TryAdd(new InstanceHandle(missing)), "Nested scope retained missing-reference deduplication");
                            RejectProcessorState(child.Dispose, "Out-of-order processor scope disposal accepted");
                            RejectProcessorStateWorker(nested.Dispose, "Foreign thread disposed processor scope");
                            RejectProcessorStateWorker(delegate { using (DocumentProcessor.BeginStateScope()) { } }, "Foreign thread entered processor scope");
                        }
                        finally { nested.Dispose(); }
                        nested.Dispose(); SameProcessorState(childState);
                        Require(processor.MissingReferences.Count == 1 && !processor.MissingReferences.TryAdd(new InstanceHandle(missing)),
                            "Nested processor scope did not restore its parent's warning set");
                    }
                    SameProcessorState(parent);
                    Require(Documents.Count == 1 && Documents.Peek() == "state-parent-document" &&
                        Streams.Count == 1 && Streams.Peek() == "state-parent-stream", "Child processor consumed parent traversal sentinels");
                    Require(parentProcessor.MissingReferences.Count == 1 && !parentProcessor.MissingReferences.TryAdd(new InstanceHandle(missing)),
                        "Child processor changed the parent missing-reference set");
                    Require(parentTypes.Count == 1 && Object.ReferenceEquals(parentTypes[missing.TypeId], parentType) &&
                        (uint)parentType.GetType().GetField("InstancesProcessed").GetValue(parentType) == 1 &&
                        (TimeSpan)parentType.GetType().GetField("TotalProcessTime").GetValue(parentType) == TimeSpan.FromTicks(9876),
                        "Child compile times mutated the parent's existing type entry");
                }
            }
            SameProcessorState(original);
            Tests.Add("processor-scopes:real-core-failure-success-warning-dedup-fresh-per-build-parent-reference-stack-five-timers-type-entries-restored-nested-thread-lifo-guards");
        }
        finally { Settings.Current = oldSettings; }
    }
    static void UnicodePathCase()
    {
        const string ascii = @"C:\Sdk Root\Data\Mod.xml";
        Require(ShPath.Canonicalize(ascii) == ascii, "ASCII canonical path changed");
        Require(ShPath.Canonicalize(@"C:\Sdk Root\Data\.\Child\..\Mod.xml") == ascii,
            "ASCII dot-segment canonicalization changed");
        Require(ShPath.RemoveFileSpec(ascii) == @"C:\Sdk Root\Data", "ASCII parent directory changed");
        const string unicode = @"C:\SDK 中文\Mods\外部 MOD\Data\模组 文档.xml";
        Require(ShPath.Canonicalize(unicode) == unicode, "Unicode canonical path changed under the process ANSI code page");
        string normalized = ShPath.Canonicalize(@"C:\SDK 中文\Mods\外部 MOD\Data\.\贴图\..\模组 文档.xml");
        Require(normalized == unicode, "Unicode/space/dot-segment path was damaged");
        Require(ShPath.RemoveFileSpec(unicode) == @"C:\SDK 中文\Mods\外部 MOD\Data" &&
            ShPath.RemoveFileSpec(normalized) == @"C:\SDK 中文\Mods\外部 MOD\Data",
            "Unicode/space parent directory was damaged");
        Tests.Add("Win32-shell-W-paths:ASCII-unchanged-Unicode-space-dot-segments-and-parent-preserved-existing-MAX_PATH-contract-only");
    }
    static void ProjectCase(BuiltinCompilers plugins)
    {
        Settings s = Configure("project");
        string good = Path.Combine(work, "project/good"), bad = Path.Combine(work, "project/bad");
        Write("project/good/leaf.xml", probe);
        Write("project/bad/leaf.xml", Asset("<ArmorTemplate id='BadConfig' inheritFrom='MissingParent'/>"));
        s.BuildConfigurations = new[] {
            new BuildConfiguration { Name="Good", Postfix="G", DataPaths=work+";"+good },
            new BuildConfiguration { Name="Bad", Postfix="B", DataPaths=work+";"+bad }
        };
        var p = Processor(plugins);
        string root = Write("project/root.xml", Asset(Include("data:leaf.xml")));
        string project = Write("project/test.babproj", "<BinaryAssetBuilderProject xmlns='urn:xmlns:ea.com:babproject'><Stream Source='root.xml'><Configuration Name='Good' Default='true'/><Configuration Name='Bad'/></Stream></BinaryAssetBuilderProject>");
        Failure(p, project, ErrorCode.InheritFromError);
        Require(Object.ReferenceEquals(Settings.Current, s), "Settings.Current not restored after project failure");
        Require(s.BuildConfigurationName == null && s.Postfix == null && s.StreamPostfix == null, "Original Settings values mutated");
        Clean(p, "Good", root, Path.Combine(good, "leaf.xml"));
        Clean(p, "Bad", root, Path.Combine(bad, "leaf.xml"));
        // The project normalizes Source through GetFullPath, while SessionCache
        // keys only lowercase paths. Write returns the same canonical form;
        // a mixed '/' query otherwise misses an existing '\\' Windows cache key.
        AssetDeclarationDocument first; p.Cache.TryGetDocument(root, "Good", TargetPlatform.Win32, false, out first);
        Require(first != null && first.State == DocumentState.Complete,
            "First project configuration cache state incorrect: " + root + " state=" + (first == null ? "missing" : first.State.ToString()));
        VerifyProjectOutput(s);
        using (File.Open(project, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        string normal = Write("project/normal.xml", probe); Run(p, normal); Clean(p, null, normal);
        Tests.Add("real-project-first-config-complete-three-assets-four-linked-streams-second-inheritance-failure-restores-settings-reader-then-plain-xml-success");
    }
    public static int Main(string[] args)
    {
        object result; int code = 0; Settings original = Settings.Current;
        try
        {
            if (args.Length != 5 || IntPtr.Size != 4) throw new Exception("Expected work result sourceCommit runId sdk (x86)");
            work = Path.GetFullPath(args[0]); sdk = Path.GetFullPath(args[4]); Directory.CreateDirectory(work);
            probe = File.ReadAllText(Path.Combine(sdk, "Mods/mymod/Data/RecoveryArmorProbe.xml"));
            UnicodePathCase(); XmlHashBlocksCase();
            Require(typeof(DocumentProcessor).Assembly == typeof(BuiltinCompilers).Assembly, "Core is outside product assembly");
            // Configuration precedes built-in initialization, just as a real build requires.
            Configure("initialize");
            using (var plugins = BuiltinCompilers.Create(TargetPlatform.Win32))
            {
                RetryCase(plugins, "include", false); RetryCase(plugins, "inheritance", true);
                CycleCase(plugins); SchemaReceiptCase(plugins); SavedSessionMemoryCase(plugins); ProjectCase(plugins); SaveFailureCase(plugins); DeferredVersionCacheCase(plugins);
                MetricsFailureCase(plugins, true, false); MetricsFailureCase(plugins, false, true);
                MetricsFailureCase(plugins, true, true); ProcessorStateCase(plugins);
            }
            result = new { succeeded=true, sourceCommit=args[2], runId=args[3], checks=checks, tests=Tests.ToArray(), longPathsValidated=false, scope="Real Core lifecycle only; production CLI backend, other global scopes, SDK/G3 and persistent cache not accepted" };
            Console.WriteLine("PASS CoreLifecycle: " + Tests.Count + " groups; " + checks + " checks");
        }
        catch (Exception e)
        {
            code=1; result = new { succeeded=false, sourceCommit=args.Length>2?args[2]:null, runId=args.Length>3?args[3]:null, checks=checks, tests=Tests.ToArray(), longPathsValidated=false, error=e.ToString() };
            Console.Error.WriteLine(e);
        }
        finally { Settings.Current = original; }
        if (args.Length>1) File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(result));
        return code;
    }
}
