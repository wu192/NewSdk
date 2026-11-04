#if NEWSdk_MONOLITHIC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using EALA.Metrics;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // A CLI preflight belongs to one effective request. Reuse its parsed SDK
    // settings and physical-root validation after diagnostic sinks have opened.
    internal sealed class CoreInputPreparation
    {
        internal string Signature;
        internal Settings Configuration, Inputs;
        internal object Report;
    }
    // Experimental in-process backend. CLI enablement and SDK/G3 acceptance are
    // separate gates; this type never launches BAB, a worker, or another EXE.
    public sealed class CoreStreamCompiler : IStreamCompiler, IBuildCacheCompiler, IInputRootsCompiler
    {
        static readonly object LeaseGate = new object();
        static CoreStreamCompiler leaseOwner;
        readonly TraceWriteHandler traceWriter;
        readonly BuildProgressState progress;
        BuildRequest request;
        string sdkRoot, modRoot, outputRoot, configurationXml;
        Settings configurationSettings;
        CoreInputPreparation inputPreparation;
        int threadId, traceLevel = 3;
        bool initialized, initializing, disposed, compiling, buildCacheEnabled, sessionCacheEnabled;
        bool stableSortEnabled = true, usePrecompiled = true, forceSlowCleanup = true;
        ParallelBackendMetrics parallelMetrics;
        bool compressedSessionCache = true, partialSessionCache = true, freezeSessionCache, alwaysTouchCache;
        int partialSessionsSaved;
        LegacyCacheStore legacyStore;
        CacheLayout cacheLayout;
        CacheLock ownedCacheLock;
        SessionCache savedSessionCache;
        int buildCacheHits, buildCacheMisses, sessionPackageHits, sessionPackageMisses, documentReuse, xmlParse, instanceCompile;
        public object InputRootsReport { get; private set; }
        BuiltinCompilers compilers;
        SchemaSet schemas;
        VerifierPluginRegistry verifiers;
        PluginDescriptor[] verifierDescriptors;
        public CoreStreamCompiler(TraceWriteHandler traceWriter = null, BuildProgressState progress = null) { this.traceWriter = traceWriter; this.progress = progress; }
        void ProgressPhase(string id) { if (progress != null) progress.SetPhase(id); }
        public string ProviderIdentity { get { return sessionCacheEnabled ? "NewModCompiler.Core.Win32.LegacySessionCache" : buildCacheEnabled ? "NewModCompiler.Core.Win32.LegacyBuildCache" : "NewModCompiler.Core.Experimental.Win32.NoCache"; } }
        public object CacheReport { get { return new { status = sessionCacheEnabled ? "SessionCache" : buildCacheEnabled ? "BuildAssetCache" : "Disabled", hits = buildCacheHits, misses = buildCacheMisses, sessionCache = sessionCacheEnabled, sessionPackageHits, sessionPackageMisses, documentReuse, xmlParse, instanceCompile, linkedStreamsRestored = linkedStreamsRestored, fileHashReads = request == null || request.FileIdentities == null ? 0 : request.FileIdentities.Reads, fileHashReuses = request == null || request.FileIdentities == null ? 0 : request.FileIdentities.Reuses, fileHashBytesRead = request == null || request.FileIdentities == null ? 0L : request.FileIdentities.BytesRead, textureCache = buildCacheEnabled || sessionCacheEnabled, compressedSessionCache, partialSessionCache, freezeSessionCache, alwaysTouchCache, partialSessionsSaved, parallel = parallelMetrics == null ? null : parallelMetrics.Snapshot(request), sessionCacheDirectory = legacyStore == null ? null : legacyStore.SessionRoot }; } }
        int linkedStreamsRestored;

        public void Initialize(BuildRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            lock (LeaseGate)
            {
                if (disposed) throw new ObjectDisposedException("CoreStreamCompiler");
                if (initialized || initializing) throw new InvalidOperationException("The backend is already initialized or initializing.");
                if (leaseOwner != null) throw new BuildFailure(ExitCode.Environment, "CORE_SESSION_BUSY", "Only one in-process Core session may own the process state.");
                leaseOwner = this;
                initializing = true;
                threadId = Thread.CurrentThread.ManagedThreadId;
            }
            try
            {
                ProgressPhase("initialize");
                ValidateOptions(request);
                sdkRoot = Path.GetFullPath(request.SdkRoot);
                modRoot = Path.GetFullPath(request.ModRoot);
                outputRoot = Path.GetFullPath(request.OutputRoot);
                if (!Directory.Exists(sdkRoot) || !Directory.Exists(modRoot))
                    throw new BuildFailure(ExitCode.Environment, "CORE_INPUT_ROOT_MISSING", "The SDK and MOD directories must exist.");
                if (Paths.Contains(sdkRoot, outputRoot) || Paths.Contains(outputRoot, sdkRoot) || Paths.Contains(modRoot, outputRoot) || Paths.Contains(outputRoot, modRoot))
                    throw new BuildFailure(ExitCode.Arguments, "CORE_OUTPUT_OVERLAP", "Core output must be separate from SDK and MOD inputs.");
                cacheLayout = CacheLayout.ForRequest(request);
                // BuildSession owns this lock through packaging/export. Standalone
                // Core hosts acquire the same partition lock for their lifetime.
                if (request.BuildCacheLock == null) ownedCacheLock = CacheLock.Acquire(cacheLayout.CacheRoot);
                inputPreparation = request.InputPreparation;
                if (inputPreparation != null && inputPreparation.Signature == InputSignature(request))
                    configurationSettings = inputPreparation.Configuration;
                else { inputPreparation = null; LoadSdkConfiguration(); }
                Settings configured = ReadSettings();
                EffectiveOption configuredCache;
                if (!request.Options.TryGetValue("bc", out configuredCache) || configuredCache.Source == "default" || configuredCache.Source == "sdkConfig")
                {
                    buildCacheEnabled = configured.BuildCache;
                    request.Options["bc"] = new EffectiveOption { Value = buildCacheEnabled ? "true" : "false", Source = "sdkConfig" };
                }
                if (request.FileIdentities == null) request.FileIdentities = new FileHashCache(request.Performance);
                this.request = request;
                lock (LeaseGate) { initialized = true; initializing = false; }
            }
            catch (Exception error)
            {
                try { if (ownedCacheLock != null) { ownedCacheLock.Dispose(); ownedCacheLock = null; } }
                finally { lock (LeaseGate) { initializing = false; ReleaseLease(); } }
                if (error is BuildFailure || error is OperationCanceledException) throw;
                throw Failure(ExitCode.Environment, "CORE_INITIALIZE_FAILED", error);
            }
        }

        void LoadSdkConfiguration()
        {
            string path = Path.Combine(sdkRoot, "tools", "BinaryAssetBuilder.exe.config");
            if (!File.Exists(path)) throw new BuildFailure(ExitCode.Environment, "CORE_CONFIG_MISSING", "The SDK BinaryAssetBuilder configuration is missing: " + path);
            var configuration = new XmlDocument { XmlResolver = null };
            configuration.Load(path);
            XmlNode section = configuration.SelectSingleNode("/configuration/assetbuilder");
            if (section == null) throw new BuildFailure(ExitCode.Environment, "CORE_CONFIG_MISSING", "The SDK configuration has no assetbuilder section.");
            configurationXml = section.OuterXml;
        }

        // Read-only environment initialization, not a synthetic MOD build. The
        // unique hash namespace is never created or finalized. Keep the lease
        // until every scope and provider has restored the caller's process state.
        public static void ProbeEnvironment(string sdkDirectory)
        {
            var probe = new CoreStreamCompiler();
            lock (LeaseGate)
            {
                if (leaseOwner != null) throw new BuildFailure(ExitCode.Environment, "CORE_SESSION_BUSY", "A Core session already owns the process state.");
                leaseOwner = probe;
                probe.threadId = Thread.CurrentThread.ManagedThreadId;
                probe.initializing = true;
            }
            Settings previousSettings = null;
            bool previousMetrics = false, capturedState = false;
            IDisposable tracing = null, state = null, report = null, hashes = null;
            Exception failure = null;
            try
            {
                previousSettings = Settings.Current;
                previousMetrics = MetricManager.Enabled;
                capturedState = true;
                probe.sdkRoot = Path.GetFullPath(sdkDirectory);
                probe.LoadSdkConfiguration();
                Settings settings = probe.ReadSettings();
                settings.DataRoot = probe.sdkRoot;
                settings.SchemaPath = Path.Combine(probe.sdkRoot, "Schemas", "xsd", "CnC3Types.xsd");
                settings.TargetPlatform = TargetPlatform.Win32;
                settings.SessionCache = settings.BuildCache = settings.PartialSessionCache = settings.CompressedSessionCache = false;
                settings.GuiMode = settings.Resident = settings.MetricsReporting = settings.OutputAssetReport = settings.OutputStringHashes = false;
                settings.BuildConfigurationName = null;
                Settings.Current = settings;
                settings = SettingsLoader.GetSettingsForConfiguration(null);
                settings.BigEndian = false;
                Settings.Current = settings;
                tracing = Tracer.BeginScope(null,3);
                state = DocumentProcessor.BeginStateScope();
                report = AssetReport.BeginScope();
                string namespaceOnly = Path.Combine(Path.GetTempPath(), "NewModCompiler-doctor-" + Guid.NewGuid().ToString("N"));
                settings.SessionCacheDirectory = namespaceOnly;
                hashes = HashProvider.BeginStringHashScope(namespaceOnly);
                MetricManager.Enabled = false;
                new SchemaSet(settings.StableSort);
                ExpressionEvaluatorWrapper.LoadAssembly();
                XIncludingReaderWrapper.LoadAssembly();
                probe.EnsureProviders(); // Includes the built-in texture compiler and verifier.
            }
            catch (Exception error)
            {
                failure = error is BuildFailure ? error : Failure(ExitCode.Environment, "CORE_ENVIRONMENT_INVALID", error);
            }
            finally
            {
                try { probe.DisposeProviders(); } catch (Exception error) { AddCleanupFailure(ref failure,error,ExitCode.Environment,"CORE_ENVIRONMENT_CLEANUP_FAILED"); }
                foreach (IDisposable scope in new[] {report,hashes,state,tracing})
                    try { if (scope != null) scope.Dispose(); } catch (Exception error) { AddCleanupFailure(ref failure,error,ExitCode.Environment,"CORE_ENVIRONMENT_CLEANUP_FAILED"); }
                if (capturedState) { Settings.Current = previousSettings; MetricManager.Enabled = previousMetrics; }
                lock (LeaseGate) { probe.disposed = true; probe.initializing = false; probe.ReleaseLease(); }
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        void ValidateOptions(BuildRequest candidate)
        {
            NewModCompiler.Cli.Options.ValidateCapabilities(candidate);
            traceLevel = 3; buildCacheEnabled = sessionCacheEnabled = true;
            compressedSessionCache = partialSessionCache = true; freezeSessionCache = alwaysTouchCache = false;
            stableSortEnabled = usePrecompiled = forceSlowCleanup = true;
            BigPackage.ValidateRequest(candidate);
            if(candidate.Command=="build" && candidate.FixNeutralAssets) BasePostProcessor.ValidateReferences(candidate);
            foreach (var option in candidate.Options)
            {
                string key = option.Key, value = option.Value.Value;
                if (key == "bc") { buildCacheEnabled = bool.Parse(value); continue; }
                if (key == "sc") { sessionCacheEnabled = bool.Parse(value); continue; }
                if (key == "csc" || key == "psc" || key == "fsc" || key == "atc")
                {
                    bool enabled = bool.Parse(value);
                    if (key == "csc") compressedSessionCache = enabled;
                    else if (key == "psc") partialSessionCache = enabled;
                    else if (key == "fsc") freezeSessionCache = enabled;
                    else alwaysTouchCache = enabled;
                    continue;
                }
                if (key == "ss" || key == "pc" || key == "slowclean")
                {
                    bool enabled = bool.Parse(value);
                    if (key == "ss") stableSortEnabled = enabled;
                    else if (key == "pc") usePrecompiled = enabled;
                    else forceSlowCleanup = enabled;
                    continue;
                }
                if (key == "tl") traceLevel = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Settings ReadSettings()
        {
            if (configurationSettings != null) return (Settings)configurationSettings.Clone();
            var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml(configurationXml);
            var settings = new SerializedSectionHandler().Create(null, null, xml.DocumentElement) as Settings;
            if (settings == null) throw new BuildFailure(ExitCode.Environment, "CORE_CONFIG_INVALID", "SDK assetbuilder settings could not be read.");
            // Deserialization retains configuration data; old targetType entries
            // must never reach PluginRegistry or Type.GetType.
            settings.Plugins = new PluginDescriptor[0];
            settings.VerifierPlugins = new PluginDescriptor[0];
            configurationSettings = settings;
            return (Settings)settings.Clone();
        }

        Settings StreamSettings(StreamContext context, string output)
        {
            Settings settings = ReadSettings();
            settings.InputPath = StreamInputPath(context);
            settings.SchemaPath = Path.Combine(sdkRoot, "Schemas", "xsd", "CnC3Types.xsd");
            settings.DataRoot = RootOption("dr", sdkRoot);
            settings.OutputDirectory = Path.Combine(output, "streams");
            settings.IntermediateOutputDirectory = cacheLayout.Intermediate;
            settings.SessionCacheDirectory = cacheLayout.Session;
            // SDK buildCacheRoot is configuration input, never a writable override
            // of the current SDK/MOD partition. CLI overrides were bounded once.
            settings.BuildCacheDirectory = cacheLayout.BuildCache;
            settings.DefaultDataPaths = RootOption("data", () => Prepend(Path.Combine(modRoot, "Data"), settings.DefaultDataPaths));
            settings.DefaultArtPaths = RootOption("art", () => Prepend(Path.Combine(modRoot, "Art"), settings.DefaultArtPaths));
            settings.DefaultAudioPaths = RootOption("audio", () => Prepend(Path.Combine(modRoot, "Audio"), settings.DefaultAudioPaths));
            settings.BuildConfigurationName = null;
            settings.BuildConfigurations = new BuildConfiguration[0];
            settings.TargetPlatform = TargetPlatform.Win32;
            // Use BAB's persistent cache layout directly; it is independent of MOD output.
            settings.SessionCache = sessionCacheEnabled;
            settings.PartialSessionCache = partialSessionCache;
            settings.FreezeSessionCache = freezeSessionCache;
            settings.CompressedSessionCache = compressedSessionCache;
            settings.BuildCache = buildCacheEnabled;
            settings.AlwaysTouchCache = alwaysTouchCache;
            settings.StreamHints = false;
            // SDK/defaultscript.cs steps 3/4/5 enable these independently of caching.
            // In particular /pc changes whether referenced streams emit output.
            settings.StableSort = stableSortEnabled;
            settings.UsePrecompiled = usePrecompiled;
            settings.ForceSlowCleanup = forceSlowCleanup;
            settings.GuiMode = settings.Resident = settings.PauseOnError = settings.MetricsReporting = false;
            settings.OldOutputFormat = settings.VersionFiles = settings.OutputAssetReport = settings.OutputStringHashes = settings.OutputIntermediateXml = false;
            settings.SingleFile = false; settings.LinkedStreams = true;
            settings.CustomPostfix = ""; settings.StreamPostfix = ""; settings.BasePatchStream = null;
            settings.TraceLevel = traceLevel; settings.ErrorLevel = 0;
            Settings.Current = settings;
            string signature = InputSignature(request);
            if (inputPreparation == null || inputPreparation.Signature != signature)
                inputPreparation = new CoreInputPreparation { Signature = signature, Configuration = configurationSettings };
            if (inputPreparation.Inputs == null)
            {
                SettingsLoader.PostProcessSettings(Path.Combine(sdkRoot, "tools"));
                ValidateInputSettings(settings);
                inputPreparation.Inputs = (Settings)settings.Clone();
                inputPreparation.Report = InputRootsReport;
            }
            else
            {
                Settings inputs = inputPreparation.Inputs;
                settings.DataRoot = inputs.DataRoot;
                settings.DataPaths = inputs.DataPaths; settings.ArtPaths = inputs.ArtPaths; settings.AudioPaths = inputs.AudioPaths;
                settings.ProcessedMonitorPaths = inputs.ProcessedMonitorPaths;
                settings.Postfix = inputs.Postfix;
                settings.BigEndian = inputs.BigEndian;
                InputRootsReport = inputPreparation.Report;
            }
            if (string.Equals(Path.GetExtension(context.Task.InputPath), ".fla", StringComparison.OrdinalIgnoreCase))
            {
                // Keep a stable DATA:movie.xml source name in the manifest.
                // The wrapper now lives inside the SDK. Its exact DATA root must
                // precede SDK roots so no cache partition enters source identity.
                // Only this FLA stream changes priority; shared inputs stay intact.
                settings.DataPaths = new[] { Path.GetDirectoryName(settings.InputPath) }.Concat(settings.DataPaths).ToArray();
            }
            return settings;
        }

        string StreamInputPath(StreamContext context)
        {
            string source = Path.GetFullPath(context.Task.InputPath);
            if (!string.Equals(Path.GetExtension(source), ".fla", StringComparison.OrdinalIgnoreCase)) return source;
            // A stable synthetic declaration lets the normal Core document and
            // file-dependency caches own a bare FLA exactly like a hand-written
            // AptAptData entry. Source projects are never changed by the host.
            string directory = Path.Combine(cacheLayout.Generated, "fla");
            cacheLayout.PrepareDirectory(directory);
            string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + ".xml");
            var document = new XmlDocument();
            XmlElement root = document.CreateElement("AssetDeclaration", "uri:ea.com:eala:asset");
            document.AppendChild(root);
            XmlElement movie = document.CreateElement("AptAptData", root.NamespaceURI);
            movie.SetAttribute("id", Path.GetFileNameWithoutExtension(source) + "_apt");
            movie.SetAttribute("File", source);
            root.AppendChild(movie);
            string xml = document.OuterXml;
            NewModCompiler.Diagnostics.DiagnosticFiles.RejectReparseFile(path);
            if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), xml, StringComparison.Ordinal)) File.WriteAllText(path, xml);
            return path;
        }
        static string InputSignature(BuildRequest candidate)
        {
            using (var text = new StringWriter())
            {
                // Length prefixes retain empty values and option boundaries.
                Action<string> add = value => { value = value ?? ""; text.Write(value.Length); text.Write(':'); text.Write(value); };
                add(Directory.GetCurrentDirectory()); add(candidate.SdkRoot); add(candidate.ModRoot);
                add(candidate.OutputRoot); add(candidate.EventsPath); add(candidate.ReportPath); add(candidate.LogPath);
                foreach (string name in new[] { "dr", "data", "art", "audio" })
                {
                    EffectiveOption option;
                    add(name); add(candidate.Options.TryGetValue(name, out option) && option != null ? option.Value : null);
                    add(option == null ? null : option.Source);
                }
                return text.ToString();
            }
        }
        string RootOption(string name, string fallback) { return RootOption(name, () => fallback); }
        string RootOption(string name, Func<string> fallback)
        {
            EffectiveOption option;
            if (!request.Options.TryGetValue(name, out option)) return fallback();
            if (option == null || string.IsNullOrWhiteSpace(option.Value))
                throw new BuildFailure(ExitCode.Arguments, "CORE_INPUT_ROOT_INVALID", "Input root option has no effective value: " + name);
            return option.Value;
        }
        string RootSource(string name, string fallback)
        {
            EffectiveOption option;
            return request.Options.TryGetValue(name, out option) && option != null ? option.Source : fallback;
        }
        void ValidateInputSettings(Settings settings)
        {
            string[] data = (string[])settings.DataPaths.Clone(), art = (string[])settings.ArtPaths.Clone(), audio = (string[])settings.AudioPaths.Clone();
            InputRootsReport = new {
                dataRoot = settings.DataRoot, schemaPath = settings.SchemaPath,
                dataPaths = data, artPaths = art, audioPaths = audio,
                sources = new { dataRoot = RootSource("dr", "sdk-root"), dataPaths = RootSource("data", "mod-prepend+sdk-config"),
                    artPaths = RootSource("art", "mod-prepend+sdk-config"), audioPaths = RootSource("audio", "mod-prepend+sdk-config") }
            };
            // An input override is another input boundary. Check it before any
            // compiler output, including diagnostics, can replace its contents.
            var roots = new List<string> { settings.DataRoot };
            roots.AddRange(data); roots.AddRange(art); roots.AddRange(audio);
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Func<string, string> resolve = path => {
                string full = Path.GetFullPath(path), physical;
                if (!resolved.TryGetValue(full, out physical)) resolved.Add(full, physical = ResolveInputRoot(full));
                return physical;
            };
            var outputs = new List<string>();
            foreach (string target in new[] { request.OutputRoot, request.EventsPath, request.ReportPath, request.LogPath })
                if (!string.IsNullOrEmpty(target))
                    outputs.Add(Directory.Exists(target) ? resolve(target) : Path.Combine(resolve(Path.GetDirectoryName(target)), Path.GetFileName(target)));
            foreach (string root in roots)
            {
                if (root == "*") continue;
                string input = resolve(root);
                foreach (string output in outputs)
                {
                    if (Paths.Contains(input, output) || Paths.Contains(output, input))
                        throw new BuildFailure(ExitCode.Arguments, "CORE_INPUT_OUTPUT_OVERLAP", "Effective input root overlaps output or diagnostics: " + root);
                }
            }
        }
        static string ResolveInputRoot(string root)
        {
            string full = Path.GetFullPath(root);
            var missing = new Stack<string>(); var current = new DirectoryInfo(full);
            while (!current.Exists)
            {
                if (File.Exists(current.FullName)) throw new IOException("An effective input root is a file: " + current.FullName);
                missing.Push(current.Name); current = current.Parent;
                if (current == null) throw new IOException("An effective input root has no existing ancestor: " + full);
            }
            string physical = NewModCompiler.Diagnostics.DiagnosticDirectory.ResolveInputDirectory(current.FullName);
            while (missing.Count != 0) physical = Path.Combine(physical, missing.Pop());
            return physical;
        }
        // Pure read preflight before Program opens reports/events/logs. This
        // uses the same settings loader as the actual stream and restores its
        // caller's Settings.Current without initializing any native providers.
        public static void PreflightInputRoots(BuildRequest candidate)
        {
            if (candidate == null) throw new ArgumentNullException("candidate");
            if (!new[] { "dr", "data", "art", "audio" }.Any(candidate.Options.ContainsKey)) return;
            Settings previous = Settings.Current;
            try
            {
                var probe = new CoreStreamCompiler(); probe.request = candidate;
                probe.sdkRoot = Path.GetFullPath(candidate.SdkRoot); probe.modRoot = Path.GetFullPath(candidate.ModRoot);
                probe.outputRoot = Path.GetFullPath(candidate.OutputRoot);
                probe.cacheLayout = CacheLayout.ForRequest(candidate);
                // Preflight protects input bytes before output sinks open. Cache
                // coverage rejection belongs to the backend/report lifecycle.
                probe.buildCacheEnabled = false;
                probe.LoadSdkConfiguration();
                var task = new BuildTask { Content = "base", InputPath = Path.Combine(probe.modRoot, "Data", "mod.xml") };
                probe.StreamSettings(new StreamContext { Task = task }, probe.cacheLayout.StreamDirectory(task));
                candidate.InputPreparation = probe.inputPreparation;
            }
            catch (BuildFailure) { throw; }
            catch (Exception error) { throw Failure(ExitCode.Arguments, "CORE_INPUT_ROOT_INVALID", error); }
            finally { Settings.Current = previous; }
        }

        static string Prepend(string path, string configured)
        {
            if (path.IndexOf(';') >= 0) throw new BuildFailure(ExitCode.Arguments, "CORE_PATH_UNSUPPORTED", "Semicolons in MOD paths cannot be represented by the legacy path resolver.");
            return path + ";" + configured;
        }

        void EnsureProviders()
        {
            try
            {
                if (compilers == null)
                {
                    BuiltinCompilers candidate = request == null ? BuiltinCompilers.CreateWithTexture(TargetPlatform.Win32) : BuiltinCompilers.CreateLazyWithTexture(TargetPlatform.Win32);
                    try
                    {
                        // The old SDK XML handler was #all, so Core reads its global
                        // hash/version via DefaultPlugin, not via a type route. Keep
                        // those exact metadata semantics without reopening fallback
                        // compilation for unknown types in the new explicit registry.
                        // Original XmlCompiler/Plugin.cs:GetAllTypesHash returns
                        // 1424942948; GetVersionNumber returns 4 for Win32. DoBuildData
                        // uses the registry initialized from the SDK #all descriptor.
                        IAssetBuilderPlugin xml = candidate.Registry.GetPlugin(InstanceHandle.GetTypeId("AttributeModifier"));
                        candidate.Registry.DefaultPlugin = new DefaultMetadataPlugin(candidate.Registry.DefaultPlugin, xml);
                        compilers = candidate;
                    }
                    catch (Exception primary)
                    {
                        Exception failure = primary;
                        try { candidate.Dispose(); } catch (Exception cleanup) { AddCleanupFailure(ref failure, cleanup, ExitCode.Internal, "CORE_PROVIDER_CLEANUP_FAILED"); }
                        ExceptionDispatchInfo.Capture(failure).Throw();
                        throw;
                    }
                }
                else compilers.ReInitialize(TargetPlatform.Win32);
                if (verifiers == null)
                {
                    verifierDescriptors = new[] { PluginDescriptor.CreateBuiltin("#all", false, () => new BinaryAssetBuilder.GameDataVerifier.Plugin()) };
                    verifiers = new VerifierPluginRegistry(verifierDescriptors, TargetPlatform.Win32);
                }
                else verifiers.ReInitialize(verifierDescriptors, TargetPlatform.Win32);
            }
            catch (BuildFailure) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { throw Failure(ExitCode.Environment, "CORE_PROVIDER_INITIALIZE_FAILED", error); }
        }

        sealed class DefaultMetadataPlugin : IAssetBuilderPlugin
        {
            readonly IAssetBuilderPlugin rejection, metadata;
            public DefaultMetadataPlugin(IAssetBuilderPlugin rejection, IAssetBuilderPlugin metadata)
            {
                this.rejection = rejection; this.metadata = metadata;
            }
            // BuiltinCompilers owns initialization and disposal of the actual
            // XML instance. This view must not initialize it a second time.
            public void Initialize(object configuration, TargetPlatform platform) { }
            public void ReInitialize(object configuration, TargetPlatform platform) { }
            public uint GetAllTypesHash() { return metadata.GetAllTypesHash(); }
            public uint GetVersionNumber() { return metadata.GetVersionNumber(); }
            public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId) { return rejection.GetExtendedTypeInformation(typeId); }
            public AssetBuffer ProcessInstance(InstanceDeclaration instance) { return rejection.ProcessInstance(instance); }
        }

        public IList<Artifact> Compile(StreamContext context, CancellationToken cancellation)
        {
            CheckOwner();
            if (compiling) throw new InvalidOperationException("Core compilation cannot be reentered.");
            if (context == null || context.Task == null || !Object.ReferenceEquals(context.Request, request))
                throw new BuildFailure(ExitCode.Arguments, "CORE_CONTEXT_INVALID", "Stream context must belong to the initialized request.");
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(context.OutputDirectory) || !Path.IsPathRooted(context.OutputDirectory))
                throw new BuildFailure(ExitCode.Arguments, "CORE_OUTPUT_INVALID", "The stream workspace must be an absolute directory.");
            string output = Path.GetFullPath(context.OutputDirectory);
            cacheLayout.ValidateStreamDirectory(context.Task, output);
            if (string.IsNullOrEmpty(context.Task.InputPath) || !Path.IsPathRooted(context.Task.InputPath) || !Paths.Contains(modRoot, context.Task.InputPath) || !File.Exists(context.Task.InputPath) ||
                !(string.Equals(Path.GetExtension(context.Task.InputPath), ".xml", StringComparison.OrdinalIgnoreCase) || context.Task.Content == "apt" && string.Equals(Path.GetExtension(context.Task.InputPath), ".fla", StringComparison.OrdinalIgnoreCase)))
                throw new BuildFailure(ExitCode.Arguments, "CORE_STREAM_INPUT_INVALID", "Core requires an existing XML stream entry or APT FLA source.");
            Settings previousSettings = Settings.Current;
            bool previousMetrics = MetricManager.Enabled;
            IDisposable hashes = null, tracing = null, report = null, processorState = null;
            Exception failure = null;
            IList<Artifact> artifacts = null;
            DocumentProcessor processor = null;
            ParallelBackendScheduler execution = null;
            SessionCache cache = null; Settings streamSettings = null; bool processingStarted = false, sessionSaved = false;
            var currentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var startedStreams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            compiling = true;
            try
            {
                ProgressPhase("initialize");
                cacheLayout.PrepareDirectory(output);
                cacheLayout.PrepareDirectory(cacheLayout.Session);
                cacheLayout.PrepareDirectory(cacheLayout.BuildCache);
                cacheLayout.PrepareDirectory(cacheLayout.Intermediate);
                tracing = Tracer.BeginScope(traceWriter, traceLevel);
                MetricManager.Enabled = false; // No listeners or legacy telemetry are installed.
                Settings settings;
                using (request.Performance.Measure("core.settings")) settings = streamSettings = StreamSettings(context, output);
                settings.MeasurePerformance = request.Performance.Detailed ? (Func<string, IDisposable>)(name => request.Performance.Measure(name, true)) : null;
                settings.RecordPerformanceCounter = request.Performance.Enabled ? (Action<string, long, bool>)request.Performance.RecordCounter : null;
                settings.FileIdentity = request.FileIdentities.CoreIdentity;
                settings.FileHashRead = request.FileIdentities.RecordRead;
                settings.SchemaFileIdentities = request.FileIdentities.SchemaIdentities;
                settings.StreamOutputStarted = stem => startedStreams.Add(stem);
                settings.StreamOutputCompleted = (stem, reused) => {
                    foreach (string extension in new[] { ".manifest", ".bin", ".imp", ".relo" }) currentFiles.Add(stem + extension);
                    if (reused) linkedStreamsRestored++;
                };
                settings.CustomDataOutput = path => currentFiles.Add(path);
                processorState = DocumentProcessor.BeginStateScope();
                report = AssetReport.BeginScope();
                using (request.Performance.Measure("core.providers")) EnsureProviders();
                bool packageRestored = false;
                if (schemas == null)
                    using (request.Performance.Measure("core.schema")) schemas = new SchemaSet(settings.StableSort);
                // All streams in this request share the same schema snapshot.
                // A new backend/request loads a fresh closure; explicit Core
                // ReloadIfChanged remains available to mutable standalone hosts.
                ProgressPhase("cache-load");
                using (request.Performance.Measure("cache.prepare"))
                if (sessionCacheEnabled)
                {
                    if (legacyStore == null) legacyStore = new LegacyCacheStore(settings.SessionCacheDirectory);
                    packageRestored = legacyStore.CanLoad();
                    if (!packageRestored) request.Performance.RecordCounter(legacyStore.PrepareMissReason);
                    if (packageRestored)
                        settings.FindCustomData = path => legacyStore.RestoreData(path.Substring(settings.OutputDirectory.Length + 1), path, cancellation);

                }
                using (request.Performance.Measure("core.string-hashes")) hashes = HashProvider.BeginStringHashScope(settings.SessionCacheDirectory);
                using (request.Performance.Measure("cache.load")) {
                if (sessionCacheEnabled)
                {
                    string sessionPath = Path.Combine(settings.SessionCacheDirectory, LegacyCacheStore.SessionName);
                    try
                    {
                        // The session lock and path belong to this request. After
                        // a successful stream, reuse exactly the saved BAB state
                        // instead of reparsing the same full session for each APT
                        // and metadata stream. InitializeCache still resets every
                        // document and checks file/schema/processor invalidation.
                        if (packageRestored && savedSessionCache != null)
                        {
                            cache = savedSessionCache;
                            request.Performance.RecordCounter("cache.session.memoryReuse");
                        }
                        else
                        {
                            cache = new SessionCache();
                            cache.LoadCacheFrom(packageRestored ? legacyStore.SessionPath : null, sessionPath);
                        }
                        cache.InitializeCache(new List<string>(), schemas.ContentIdentity);
                        if (packageRestored && !cache.HasCachedState)
                            throw new InvalidDataException("The restored session has an incompatible format.");
                        if (packageRestored)
                        {
                            AssetDeclarationDocument previousDocument;
                            cache.TryGetDocument(settings.InputPath, null, settings.TargetPlatform, false, out previousDocument);
                            packageRestored = previousDocument != null;
                            if (!packageRestored) request.Performance.RecordCounter("cache.session.documentMissing");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        // Preserve conditional recovery while avoiding exception-
                        // filter IL in the mixed C++/CLI link input.
                        if (!packageRestored) throw;
                        request.Performance.RecordCounter("cache.session.invalidState");
                        savedSessionCache = null;
                        // Treat malformed serialized state as one cold miss. No
                        // intermediate/cdata bytes from the bad package survive.
                        // Loading older document data can register strings before
                        // a later malformed node fails. Recreate that scope too.
                        hashes.Dispose(); hashes = null;
                        hashes = HashProvider.BeginStringHashScope(settings.SessionCacheDirectory);
                        packageRestored = false;
                        settings.FindCustomData = null;
                        cache = new SessionCache();
                        cache.LoadCacheFrom(packageRestored ? legacyStore.SessionPath : null, sessionPath);
                        cache.InitializeCache(new List<string>(), schemas.ContentIdentity);
                    }
                    if (packageRestored) sessionPackageHits++; else sessionPackageMisses++;
                }
                else { request.Performance.RecordCounter("cache.session.disabled"); cache = new SessionCache(); cache.InitializeCache(new List<string>(), schemas.ContentIdentity); }
                }
                cache.AssetCompilersVersion = compilers.Registry.AssetBuilderPluginsVersion;
                processor = new DocumentProcessor(settings, compilers.Registry, verifiers) { Cache = cache, SchemaSet = schemas };
                if (progress != null) processor.Progress = progress.RecordWork;
                if (parallelMetrics == null) parallelMetrics = new ParallelBackendMetrics(request.Performance);
                execution = new ParallelBackendScheduler(processor, output, request.JobsAuto ? Math.Max(1, Environment.ProcessorCount) : request.RequestedJobs, parallelMetrics, request.Performance, cancellation);
                processor.AssetExecution = execution;
                ProgressPhase("process");
                bool success; processingStarted = true;
                using (request.Performance.Measure("core.process"))
                    processor.ProcessDocument(settings.InputPath, true, false, out success);
                execution.Flush();
                if (!success) throw new BuildFailure(ExitCode.Compilation, "CORE_COMPILE_FAILED", "Core did not complete the stream.");
                cancellation.ThrowIfCancellationRequested();
                if (sessionCacheEnabled && !freezeSessionCache)
                {
                    SaveLegacySession(cache, settings, currentFiles, cancellation); sessionSaved = true;
                }
                using (request.Performance.Measure("artifacts.collect"))
                    artifacts = currentFiles.OrderBy(path => path, StringComparer.Ordinal).Select(path => new Artifact {
                        Path = path, Kind = path.EndsWith(".cdata", StringComparison.OrdinalIgnoreCase) ? "custom-data" : "stream" + Path.GetExtension(path),
                        Size = new FileInfo(path).Length }).ToList();
                if (artifacts.Count == 0) throw new BuildFailure(ExitCode.Output, "CORE_OUTPUT_MISSING", "Core produced no stream output.");
                cancellation.ThrowIfCancellationRequested();
            }
            catch (Exception error)
            {
                savedSessionCache = null;
                failure = error is BuildFailure || error is OperationCanceledException ? error :
                    Failure(ExitCode.Compilation, "CORE_COMPILE_FAILED", error);
                bool savePartial = processingStarted && !sessionSaved && sessionCacheEnabled && partialSessionCache && !freezeSessionCache;
                if (savePartial && execution != null)
                {
                    // An owner XML error can follow an independent included
                    // document whose accepted workers are still committing.
                    // Confirm successful closed documents before saving their
                    // partial session; no output stream is published here.
                    try { execution.CompleteAcceptedWorkOnFailure(); }
                    catch (Exception completionError) { AddCleanupFailure(ref failure, completionError, ExitCode.Compilation, "CORE_PARTIAL_BACKEND_COMPLETE_FAILED"); }
                }
                try { if (execution != null) execution.Dispose(); }
                catch (Exception joinError) { AddCleanupFailure(ref failure, joinError, ExitCode.Compilation, "CORE_BACKEND_JOIN_FAILED"); }
                if (savePartial)
                {
                    try { SaveLegacySession(cache, streamSettings, currentFiles, CancellationToken.None); partialSessionsSaved++; }
                    catch (Exception saveError) { AddCleanupFailure(ref failure, saveError, ExitCode.Output, "CORE_PARTIAL_CACHE_SAVE_FAILED"); }
                }
            }
            finally
            {
                if (processor != null)
                {
                    buildCacheHits += processor.BuildCacheHits; buildCacheMisses += processor.BuildCacheMisses;
                    documentReuse += processor.DocumentReuseCount;
                    xmlParse += processor.XmlParseCount;
                    instanceCompile += processor.InstanceCompileCount;
                }
                using (request.Performance.Measure("core.cleanup")) {
                var cleanup = new List<Exception>();
                try { if (execution != null) execution.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                try { if (report != null) report.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                try { if (hashes != null) hashes.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                try { if (processorState != null) processorState.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                try { if (tracing != null) tracing.Dispose(); } catch (Exception error) { cleanup.Add(error); }
                Settings.Current = previousSettings;
                MetricManager.Enabled = previousMetrics;
                compiling = false;
                foreach (Exception error in cleanup) AddCleanupFailure(ref failure, error, ExitCode.Internal, "CORE_CLEANUP_FAILED");
                if (failure != null)
                {
                    // A failed stream must never authorize a later four-file hit.
                    // Keep intermediate assets and useful partial BAB cache work.
                    foreach (string stem in startedStreams)
                        foreach (string extension in new[] { ".manifest", ".bin", ".imp", ".relo" })
                            try { File.Delete(stem + extension); }
                            catch (Exception error) { AddCleanupFailure(ref failure, error, ExitCode.Output, "CORE_OUTPUT_CLEANUP_FAILED"); }
                }
                }
            }
            if (failure != null) { savedSessionCache = null; ExceptionDispatchInfo.Capture(failure).Throw(); }
            // Frozen sessions retain their on-disk snapshot. Failed/partial
            // streams never authorize the next stream's in-memory reuse.
            savedSessionCache = sessionSaved ? cache : null;
            return artifacts;
        }

        void SaveLegacySession(SessionCache cache, Settings settings, IEnumerable<string> currentFiles, CancellationToken cancellation)
        {
            ProgressPhase("cache-save");
            using (request.Performance.Measure("cache.save"))
            {
            if (!cache.HasChanges && cache.LoadedCompressed == compressedSessionCache) return;
            legacyStore.SaveData(settings.OutputDirectory, currentFiles.Where(path => path.EndsWith(".cdata", StringComparison.OrdinalIgnoreCase)), cancellation);
            using (request.Performance.Measure("cache.serialize")) cache.SaveCache(compressedSessionCache);
            legacyStore.Saved(compressedSessionCache);
            cache.AcceptSavedChanges(compressedSessionCache);
            }
        }
        static BuildFailure Failure(ExitCode exitCode, string code, Exception error)
        {
            var failure = new BuildFailure(exitCode, code, error.ToString());
            failure.Data["OriginalException"] = error;
            return failure;
        }
        static void AddCleanupFailure(ref Exception primary, Exception secondary, ExitCode exitCode, string code)
        {
            if (primary == null) { primary = Failure(exitCode, code, secondary); return; }
            // Preserve cancellation, the original diagnostic code and its stack.
            // The caller can report secondary failures without losing the cause.
            var failures = primary.Data["CoreCleanupExceptions"] as List<Exception>;
            if (failures == null) { failures = new List<Exception>(); primary.Data["CoreCleanupExceptions"] = failures; }
            failures.Add(secondary);
        }

        void CheckOwner()
        {
            lock (LeaseGate)
            {
                if (disposed) throw new ObjectDisposedException("CoreStreamCompiler");
                if (!initialized) throw new InvalidOperationException("The backend has not been initialized.");
                if (!Object.ReferenceEquals(leaseOwner, this) || threadId != Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("The Core session must be used by its owning thread.");
            }
        }
        void ReleaseLease() { lock (LeaseGate) { if (Object.ReferenceEquals(leaseOwner, this)) leaseOwner = null; } }
        public void Dispose()
        {
            lock (LeaseGate)
            {
                if (disposed) return;
                if ((initialized || initializing) && threadId != Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("The Core session must be disposed by its owning thread.");
                if (compiling || initializing) throw new InvalidOperationException("Cannot dispose a running or initializing Core session.");
                disposed = true;
            }
            try { DisposeProviders(); }
            finally {
                try {
                    savedSessionCache = null;
                    if (legacyStore != null) legacyStore.Dispose();
                } finally {
                    try { if (ownedCacheLock != null) { ownedCacheLock.Dispose(); ownedCacheLock = null; } }
                    finally { ReleaseLease(); }
                }
            }
        }
        void DisposeProviders()
        {
            var failures = new List<Exception>();
            if (verifierDescriptors != null) foreach (PluginDescriptor descriptor in verifierDescriptors)
                try { var owned = descriptor.Plugin as IDisposable; if (owned != null) owned.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { if (compilers != null) compilers.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Core provider cleanup failed.", failures);
        }
    }
}
#endif
