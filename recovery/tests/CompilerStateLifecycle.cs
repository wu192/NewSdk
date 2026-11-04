using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

// Loaded by the production-object validation host: every contract below is the
// real type from its entry EXE, with no substitute Core assembly or static stub.
public static class CompilerStateLifecycle
{
    static object Field(Type type, string name) { return type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }
    static void RejectOtherThread(Action action, string message)
    {
        Exception result = null;
        var thread = new Thread(delegate() { try { action(); } catch (Exception error) { result = error; } });
        thread.IsBackground = true; thread.Start();
        Require(thread.Join(5000), message + " (worker blocked)");
        Require(result is InvalidOperationException, message);
    }
    static object[] HashState()
    {
        return new[] { Field(typeof(HashProvider), "_SchemaNameToBinName"), Field(typeof(HashProvider), "_StringHashBins"), (object)HashProvider.GetOutputDirectory(), HashProvider.StringHashesFile };
    }
    static void SameHashState(object[] expected)
    {
        object[] actual = HashState();
        Require(Object.ReferenceEquals(expected[0], actual[0]) && Object.ReferenceEquals(expected[1], actual[1]), "Hash dictionary identity was not restored");
        Require(Object.Equals(expected[2], actual[2]) && Object.Equals(expected[3], actual[3]), "Hash directory or filename was not restored");
    }
    static StringHashBinDescriptor[] Descriptors()
    {
        return new[] { new StringHashBinDescriptor { SchemaTypeName = "Poid", BinName = "POID" } };
    }
    static void HashChecks(string output)
    {
        Settings previous = Settings.Current;
        object[] before = HashState();
        try
        {
            Settings.Current = new Settings { StringHashBinDescriptors = Descriptors(), SessionCacheDirectory = output, OutputAssetReport = false };
            string baseline = Path.Combine(output, "baseline");
            using (HashProvider.BeginStringHashScope(baseline))
            {
                HashProvider.RecordHash("POID", "BaselineOnly");
                HashProvider.StringHashesFile = "baseline-custom.xml";
                object[] expected = HashState();
                for (int round = 0; round < 2; round++)
                {
                    string inner = Path.Combine(output, "sequential-" + round);
                    using (HashProvider.BeginStringHashScope(inner))
                    {
                        Require(HashProvider.StringHashesFile == "StringHashes.xml", "A new hash scope inherited its parent's filename");
                        HashProvider.RecordHash("POID", "InnerOnly" + round);
                        HashProvider.StringHashesFile = "inner.xml";
                    }
                    SameHashState(expected);
                    Require(!Directory.Exists(inner), "Disposing hash scope published output");
                }
                try
                {
                    using (HashProvider.BeginStringHashScope(Path.Combine(output, "failed-stream")))
                    {
                        HashProvider.RecordHash("POID", "FailedStreamOnly");
                        throw new ApplicationException("controlled stream failure");
                    }
                }
                catch (ApplicationException) { }
                SameHashState(expected);
                Require(!Directory.Exists(Path.Combine(output, "failed-stream")), "Failed scope published hash output");

                Settings.Current.StringHashBinDescriptors = new[] { Descriptors()[0], Descriptors()[0] };
                bool rejected = false;
                try { HashProvider.InitializeStringHashes(Path.Combine(output, "invalid-config")); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Duplicate hash configuration was accepted");
                SameHashState(expected);
                Settings.Current.StringHashBinDescriptors = Descriptors();
                string malformed = Path.Combine(output, "malformed"); Directory.CreateDirectory(malformed);
                File.WriteAllText(Path.Combine(malformed, "StringHashes.xml"), "<AssetDeclaration");
                rejected = false;
                try { using (HashProvider.BeginStringHashScope(malformed)) { } }
                catch (System.Xml.XmlException) { rejected = true; }
                Require(rejected, "Malformed hash file was accepted"); SameHashState(expected);

                // Invalid XML content forces a write failure after the file is open.
                // The handle must close immediately, without waiting for a finalizer.
                string writeFailure = Path.Combine(output, "writer-failure");
                using (HashProvider.BeginStringHashScope(writeFailure))
                {
                    HashProvider.RecordHash("POID", "invalid\u0000xml");
                    rejected = false;
                    try { HashProvider.FinalizeStringHashes(); }
                    catch (ArgumentException) { rejected = true; }
                    Require(rejected, "Invalid XML hash text did not exercise a write failure");
                    using (File.Open(Path.Combine(writeFailure, "StringHashes.xml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                }
                SameHashState(expected);
                File.Delete(Path.Combine(writeFailure, "StringHashes.xml"));
                using (HashProvider.BeginStringHashScope(writeFailure))
                {
                    HashProvider.RecordHash("POID", "RecoveredAfterWriteFailure");
                    HashProvider.FinalizeStringHashes();
                    var valid = new System.Xml.XmlDocument(); valid.Load(Path.Combine(writeFailure, "StringHashes.xml"));
                    Require(valid.OuterXml.Contains("RecoveredAfterWriteFailure"), "Hash output did not recover after write failure");
                }
                SameHashState(expected);

                var outer = HashProvider.BeginStringHashScope(Path.Combine(output, "nested-outer"));
                object[] outerState = HashState();
                var innerScope = HashProvider.BeginStringHashScope(Path.Combine(output, "nested-inner"));
                object[] innerState = HashState();
                Reject(delegate { outer.Dispose(); }, "Out of order hash disposal was accepted"); SameHashState(innerState);
                RejectOtherThread(delegate { innerScope.Dispose(); }, "Foreign thread disposed hash scope"); SameHashState(innerState);
                RejectOtherThread(delegate { using (HashProvider.BeginStringHashScope(output)) { } }, "Concurrent hash scope was accepted");
                innerScope.Dispose(); SameHashState(outerState); innerScope.Dispose(); SameHashState(outerState);
                outer.Dispose(); SameHashState(expected);

                // Explicit finalization remains supported, and proves that the
                // original tables retained their contents without inner assets.
                HashProvider.FinalizeStringHashes();
                string text = File.ReadAllText(Path.Combine(baseline, "StringHashes.xml"));
                Require(text.Contains("BaselineOnly") && !text.Contains("InnerOnly") && !text.Contains("FailedStreamOnly"), "Hash contents leaked between streams");
                int preparingCallbacks = 0;
                using (Tracer.BeginScope(delegate(string source, TraceEventType kind, string message)
                {
                    if (source != "HashProvider") return;
                    preparingCallbacks++;
                    SameHashState(expected);
                    Reject(delegate { HashProvider.InitializeStringHashes(output); }, "Trace callback reentered initialization");
                    Reject(delegate { using (HashProvider.BeginStringHashScope(output)) { } }, "Trace callback reentered scope creation");
                    RejectOtherThread(delegate { HashProvider.InitializeStringHashes(output); }, "Trace callback worker initialized hashes");
                    RejectOtherThread(delegate { using (HashProvider.BeginStringHashScope(output)) { } }, "Trace callback worker entered hash scope");
                }, 9))
                using (HashProvider.BeginStringHashScope(baseline))
                {
                    HashProvider.FinalizeStringHashes();
                    Require(File.ReadAllText(Path.Combine(baseline, "StringHashes.xml")) == text, "Loading prior valid string hashes changed their contents");
                }
                Require(preparingCallbacks == 1, "Expected the prior hash load to exercise one trace callback");
                SameHashState(expected);
            }
            SameHashState(before);
        }
        finally { Settings.Current = previous; }
    }
    static void TraceChecks()
    {
        TraceWriteHandler previous = Tracer.TraceWrite;
        object previousMask = Field(typeof(Tracer), "_TraceMask");
        string environment = Environment.GetEnvironmentVariable("BabEnableTrace");
        try
        {
            Environment.SetEnvironmentVariable("BabEnableTrace", "False");
            Tracer tracer = Tracer.GetTracer("Lifecycle-" + Guid.NewGuid().ToString("N"), "Lifecycle fixture");
            int baselineCalls = 0, firstCalls = 0, secondCalls = 0;
            TraceWriteHandler baseline = delegate { baselineCalls++; };
            Tracer.TraceWrite = baseline;
            // A nonstandard mask ensures restoration preserves the exact mask,
            // rather than guessing its nearest public verbosity level.
            typeof(Tracer).GetField("_TraceMask", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, TraceKind.Warning | TraceKind.Data);
            object baselineMask = Field(typeof(Tracer), "_TraceMask");
            using (Tracer.BeginScope(delegate { firstCalls++; }, 9))
            {
                tracer.Message("first");
                var nested = Tracer.BeginScope(delegate { secondCalls++; }, 0);
                tracer.Message("suppressed");
                nested.Dispose(); nested.Dispose();
                tracer.Message("first again");
            }
            Require(Object.ReferenceEquals(Tracer.TraceWrite, baseline) && baselineMask.Equals(Field(typeof(Tracer), "_TraceMask")), "Trace parent state was not restored exactly");
            try
            {
                using (Tracer.BeginScope(delegate { secondCalls++; }, 9))
                { tracer.Message("second"); throw new ApplicationException("controlled trace failure"); }
            }
            catch (ApplicationException) { }
            Require(Object.ReferenceEquals(Tracer.TraceWrite, baseline) && baselineMask.Equals(Field(typeof(Tracer), "_TraceMask")), "Trace state leaked after failure");
            tracer.TraceWarning("baseline");
            Require(firstCalls == 2 && secondCalls == 1 && baselineCalls == 1, "Trace callback was duplicated or escaped its scope");

            var outer = Tracer.BeginScope(null, 0);
            var inner = Tracer.BeginScope(baseline, 9);
            Reject(delegate { outer.Dispose(); }, "Out of order trace disposal was accepted");
            Require(Object.ReferenceEquals(Tracer.TraceWrite, baseline), "Rejected trace disposal changed current callback");
            RejectOtherThread(delegate { inner.Dispose(); }, "Foreign thread disposed trace scope");
            RejectOtherThread(delegate { using (Tracer.BeginScope(null, 0)) { } }, "Concurrent trace scope was accepted");
            inner.Dispose(); Require(Tracer.TraceWrite == null && Field(typeof(Tracer), "_TraceMask").Equals(TraceKind.None), "Nested trace parent was not restored");
            outer.Dispose();
            Require(Object.ReferenceEquals(Tracer.TraceWrite, baseline) && baselineMask.Equals(Field(typeof(Tracer), "_TraceMask")), "Final trace state differs");
            bool invalid = false;
            try { using (Tracer.BeginScope(null, -1)) { } } catch (IndexOutOfRangeException) { invalid = true; }
            Require(invalid && Object.ReferenceEquals(Tracer.TraceWrite, baseline) && baselineMask.Equals(Field(typeof(Tracer), "_TraceMask")), "Invalid trace level changed state");

            // The old switch is latched by each Tracer at construction. A new
            // explicit build scope must still own its callback; outside that
            // scope, keep the original latched-switch behavior unchanged.
            Environment.SetEnvironmentVariable("BabEnableTrace", "True");
            Tracer legacyEnabled = Tracer.GetTracer("LegacyEnabledLifecycle-" + Guid.NewGuid().ToString("N"), "Legacy switch scope override fixture");
            int priorBaselineCalls = baselineCalls, scopedCalls = 0, nestedCalls = 0;
            legacyEnabled.TraceWarning("legacy outside before scope");
            Require(baselineCalls == priorBaselineCalls, "Legacy enabled tracer changed its old outside-scope behavior");
            TraceWriteHandler scopedWriter = delegate(string source, TraceEventType kind, string message)
            {
                Require(source.StartsWith("LegacyEnabledLifecycle-", StringComparison.Ordinal), "Legacy-enabled scoped trace lost its source");
                Require(kind == TraceEventType.Warning || kind == TraceEventType.Information, "Legacy-enabled scoped trace lost its severity");
                scopedCalls++;
            };
            using (Tracer.BeginScope(scopedWriter, 9))
            {
                Require(Environment.GetEnvironmentVariable("BabEnableTrace") == "True", "Explicit trace scope changed the legacy environment switch");
                legacyEnabled.TraceWarning("legacy scope warning");
                legacyEnabled.TraceInfo("legacy scope info");
                using (Tracer.BeginScope(delegate { nestedCalls++; }, 0))
                    legacyEnabled.TraceWarning("legacy nested suppressed warning");
                Require(Object.ReferenceEquals(Tracer.TraceWrite, scopedWriter), "Nested legacy-enabled scope did not restore its callback");
                legacyEnabled.TraceInfo("legacy parent scope info restored");
            }
            Require(scopedCalls == 3 && nestedCalls == 0, "Explicit trace scope did not override legacy switch or preserve its mask");
            Require(Object.ReferenceEquals(Tracer.TraceWrite, baseline) && baselineMask.Equals(Field(typeof(Tracer), "_TraceMask")), "Legacy-enabled trace scope did not restore exact delegate and mask");
            Require(Environment.GetEnvironmentVariable("BabEnableTrace") == "True", "Leaving a trace scope changed the legacy environment switch");
            legacyEnabled.TraceWarning("legacy outside after scope");
            Require(scopedCalls == 3 && baselineCalls == priorBaselineCalls, "Legacy-enabled trace callback escaped its scope");
        }
        finally
        {
            Tracer.TraceWrite = previous;
            typeof(Tracer).GetField("_TraceMask", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousMask);
            Environment.SetEnvironmentVariable("BabEnableTrace", environment);
        }
    }
    static void WorkerVerificationChecks()
    {
        const int workers = 4, repetitions = 64;
        var verifier = (IConcurrentAssetBuilderVerifierPlugin)Activator.CreateInstance(
            typeof(Tracer).Assembly.GetType("BinaryAssetBuilder.GameDataVerifier.Plugin", true), true);
        var declarations = new InstanceDeclaration[workers];
        var captures = new Tracer.DiagnosticCapture[workers];
        var tasks = new Task[workers];
        int owner = Thread.CurrentThread.ManagedThreadId, callbacks = 0;
        var messages = new List<string>();
        for (int i = 0; i < workers; i++)
        {
            string ns = "urn:verification:" + i;
            var xml = new XmlDocument();
            xml.LoadXml("<GameObject xmlns='" + ns + "' id='Concurrent" + i + "'><Behaviors><B id='same'/><B id='same'/></Behaviors></GameObject>");
            var namespaces = new XmlNamespaceManager(xml.NameTable);
            namespaces.AddNamespace("ea", ns);
            declarations[i] = new InstanceDeclaration(AssetDeclarationDocument.CreateBackendView(namespaces));
            declarations[i].XmlNode = xml.DocumentElement;
            declarations[i] = declarations[i].CreateBackendView();
        }
        using (Tracer.BeginScope(delegate(string source, TraceEventType kind, string message)
        {
            Require(Thread.CurrentThread.ManagedThreadId == owner, "Worker invoked the owner diagnostic callback");
            Require(source == "GameDataVerifier" && kind == TraceEventType.Error, "Worker verification lost diagnostic source or severity");
            callbacks++; messages.Add(message);
        }, 9))
        using (var ready = new CountdownEvent(workers))
        using (var begin = new ManualResetEventSlim())
        {
            for (int i = 0; i < workers; i++)
            {
                int index = i;
                tasks[i] = Task.Factory.StartNew(delegate
                {
                    captures[index] = Tracer.CaptureDiagnostics();
                    using (captures[index])
                    {
                        ready.Signal(); begin.Wait();
                        for (int round = 0; round < repetitions; round++)
                        {
                            Require(!verifier.VerifyInstance(declarations[index]), "Concurrent verifier used another document's namespace bindings");
                            Thread.Yield();
                        }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            ready.Wait(); begin.Set(); Task.WaitAll(tasks);
            Require(callbacks == 0, "Worker verification delivered diagnostics before owner replay");
            for (int i = 0; i < workers; i++)
            {
                int start = messages.Count;
                captures[i].Replay(); captures[i].Replay();
                Require(messages.Count == start + repetitions, "Owner replay lost or duplicated worker diagnostics");
                for (int j = start; j < messages.Count; j++)
                    Require(messages[j].Contains("GameObject Concurrent" + i + " has 2 modules"), "Diagnostic capture leaked between workers");
            }
            Require(callbacks == workers * repetitions, "Concurrent verifier diagnostic count differs");
        }
    }
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Require(typeof(HashProvider).Assembly == Assembly.GetEntryAssembly() && typeof(Tracer).Assembly == Assembly.GetEntryAssembly(), "State tests must use real production EXE types");
        string environment = Environment.GetEnvironmentVariable("BabEnableTrace");
        try
        {
            Environment.SetEnvironmentVariable("BabEnableTrace", "False");
            HashChecks(outputDirectory); TraceChecks(); WorkerVerificationChecks();
        }
        finally { Environment.SetEnvironmentVariable("BabEnableTrace", environment); }
        Console.WriteLine("PASS Hash/Tracer state lifecycle");
    }
    public static int Main(string[] args)
    {
        try
        {
            Run(args[0]);
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { succeeded = true, sourceCommit = args[2], runId = args[3], checks = new[] { "hash-scope-restoration", "atomic-hash-initialization", "hash-write-failure-releases-file", "hash-callback-worker-no-deadlock", "hash-lifo-and-thread-rejection", "trace-scope-restoration", "trace-no-duplicate-callbacks", "trace-lifo-and-thread-rejection", "trace-explicit-scope-overrides-latched-legacy-switch-only-within-scope", "parallel-verifier-private-namespace-and-diagnostics", "owner-diagnostic-replay-once" } }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
