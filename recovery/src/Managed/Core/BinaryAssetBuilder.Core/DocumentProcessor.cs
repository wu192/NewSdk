using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using BinaryAssetBuilder.Project;
using BinaryAssetBuilder.Utility;
using EALA.Metrics;

namespace BinaryAssetBuilder.Core;

public class DocumentProcessor
{
	private class TypeCompileData
	{
		public TimeSpan TotalProcessTime = new TimeSpan(0L);

		public TimeSpan LongestProcessTime = new TimeSpan(0L);

		public uint InstancesProcessed;

		public readonly string TypeName;

		public string LongestProcessInstance = "";

		public TypeCompileData(string typeName)
		{
			TypeName = typeName;
		}
	}

	public class ProcessOptions
	{
		public string BasePatchStream;

		public string[] BaseStreamSearchPaths = new string[0];

		public StreamReference[] StreamReferences = new StreamReference[0];

		public string RelativeBasePath = "";

		public bool UsePrecompiled;

		public bool GenerateOutput;

		public string Configuration;
	}

	[NonSerialized]
	public const uint Version = 11u;

	private static Tracer _Tracer = Tracer.GetTracer("DocumentProcessor", "Provides XML processing functionality");

	private static InstanceHandleSet _MissingReferences = new InstanceHandleSet();

	private List2<string> _DocumentStack = new List2<string>();

	private ISessionCache _Cache;

	private SchemaSet _SchemaSet;

	private InstanceHandleSet _RequiredInheritFromSources = new InstanceHandleSet();

	private PluginRegistry _PluginRegistry;

	private VerifierPluginRegistry _VerifierPluginRegistry;

	private int _InstancesProcessedCount;

	private int _FilesProcessedCount;

	private int _FilesParsedCount;

	private int _InstancesCopiedFromCacheCount;

	private int _InstancesCompiledCount;

	private static TimeSpan _TotalProcInstancesTime = new TimeSpan(0L);

	private static TimeSpan _TotalPrepareOutputTime = new TimeSpan(0L);

	private static TimeSpan _TotalPrepareSourceTime = new TimeSpan(0L);

	private static TimeSpan _TotalPostProcTime = new TimeSpan(0L);

	private static TimeSpan _TotalValidateTime = new TimeSpan(0L);

	private static IDictionary<uint, TypeCompileData> _TypeProcessingTime = new SortedDictionary<uint, TypeCompileData>();

	private long _MaxTotalMemory;

	private static Stack<string> _CurrentDocumentStack = new Stack<string>();

	private static Stack<string> _CurrentStreamStack = new Stack<string>();

	private bool _CompilingProject;

	private Dictionary<string, string> _ProjectDefaultConfigurations;

	[NonSerialized]
	private IDictionary<string, AssetLocationInfo> _LastWrittenAssets = new SortedDictionary<string, AssetLocationInfo>();

	// Actual work counters, independent of package/asset directory availability.
	public int DocumentReuseCount { get; private set; }
	public int XmlParseCount { get; private set; }
	public int InstanceCompileCount { get; private set; }
	// Integer deltas only: completed assets, parsed XML, reused documents.
	// The host keeps this callback allocation-free and independent of rendering.
	public Action<int, int, int> Progress { get; set; }
	public IAssetExecution AssetExecution { get; set; }
	// A version owns its stable declarations until every output subscription has
	// returned. A replacement document receives a new version instead of reusing
	// the DOM and declarations held by an earlier worker.
	private sealed class ExecutionDocumentVersion
	{
		internal readonly Dictionary<OutputManager, AssetExecutionDocument> Outputs = new Dictionary<OutputManager, AssetExecutionDocument>();
		internal readonly HashSet<AssetExecutionDocument> CompletedOutputs = new HashSet<AssetExecutionDocument>();
		internal bool FrontendPrepared, CompletionRequested, Finalized, ResetRequested, Failed;
		internal string Configuration;
		internal TargetPlatform Platform;
	}
	private readonly Dictionary<AssetDeclarationDocument, ExecutionDocumentVersion> _ExecutionDocuments = new Dictionary<AssetDeclarationDocument, ExecutionDocumentVersion>();

	private ExecutionDocumentVersion GetDocumentVersion(AssetDeclarationDocument document, string configuration = null)
	{
		if (!_ExecutionDocuments.TryGetValue(document, out var version))
		{
			version = new ExecutionDocumentVersion { Configuration = configuration, Platform = Settings.Current.TargetPlatform };
			_ExecutionDocuments.Add(document, version);
		}
		return version;
	}

	internal AssetExecutionDocument GetExecutionDocument(AssetDeclarationDocument document, OutputManager outputManager)
	{
		if (AssetExecution == null) return null;
		var version = GetDocumentVersion(document);
		if (!version.Outputs.TryGetValue(outputManager, out var scope))
		{
			version.Finalized = false;
			scope = AssetExecution.BeginDocument(document, outputManager);
			version.Outputs.Add(outputManager, scope);
		}
		return scope;
	}

	private void CompleteExecutionDocument(AssetDeclarationDocument document, ExecutionDocumentVersion version, AssetExecutionDocument scope)
	{
		if (scope == null) return;
		AssetExecution.CompleteDocument(scope, () => {
			version.CompletedOutputs.Add(scope);
			TryCompleteDocumentVersion(document, version);
		});
	}

	private static void FailDocumentVersion(ExecutionDocumentVersion version)
	{
		version.Failed = true;
		foreach (var scope in version.Outputs.Values)
		{
			if (!scope.IsCompleted) scope.IsFailed = true;
			scope.Completions.Clear();
		}
	}

	private void TryCompleteDocumentVersion(AssetDeclarationDocument document, ExecutionDocumentVersion version)
	{
		if (version.Failed || document.Processing || !version.CompletionRequested ||
			version.CompletedOutputs.Count != version.Outputs.Count) return;
		foreach (var scope in version.Outputs.Values) if (scope.IsFailed) return;
		if (!version.Finalized)
		{
			document.MakeComplete();
			document.MakeCacheable();
			// A superseded version may finish after its replacement. It must
			// never publish its old successful state over the current document.
			Cache.TryGetDocument(document.SourcePath, version.Configuration, version.Platform, false, out var current);
			if (Object.ReferenceEquals(current, document))
				Cache.SaveDocumentToCache(document.SourcePath, version.Configuration, version.Platform, document);
			version.Finalized = true;
		}
		if (version.ResetRequested) { document.Reset(); version.ResetRequested = false; }
	}

	private void ResetExecutionDocument(AssetDeclarationDocument document)
	{
		if (_ExecutionDocuments.TryGetValue(document, out var version))
		{
			version.ResetRequested = true;
			TryCompleteDocumentVersion(document, version);
		}
		else document.Reset();
	}

	private void WaitForExecutionDocument(AssetDeclarationDocument document)
	{
		if (AssetExecution == null || document == null || !_ExecutionDocuments.TryGetValue(document, out var version)) return;
		foreach (var scope in version.Outputs.Values) AssetExecution.WaitDocument(scope);
		TryCompleteDocumentVersion(document, version);
	}

	private AssetDeclarationDocument ReplaceExecutionDocument(AssetDeclarationDocument document, string configuration)
	{
		Cache.TryGetFile(document.SourcePath, configuration, Settings.Current.TargetPlatform, out var hashItem);
		var replacement = new AssetDeclarationDocument(this, hashItem, document.LogicalSourcePath, configuration);
		if (!Cache.ReplaceDocumentForExecution(document.SourcePath, configuration, Settings.Current.TargetPlatform, document, replacement))
			throw new InvalidOperationException("The active document version changed during owner processing.");
		bool processing = document.Processing;
		document.Processing = false;
		if (_ExecutionDocuments.TryGetValue(document, out var previous)) TryCompleteDocumentVersion(document, previous);
		replacement.Processing = processing;
		return replacement;
	}

	private bool NeedsBackendXml(AssetDeclarationDocument document, OutputManager outputManager)
	{
		if (outputManager == null) return false;
		foreach (var declaration in document.SelfInstances)
		{
			if (declaration.XmlNode != null) continue;
			var asset = outputManager.GetBinaryAsset(declaration, false);
			if (asset != null && asset.GetLocation(AssetLocation.All, AssetLocationOption.None) == AssetLocation.None) return true;
		}
		return false;
	}

	private bool NeedsInheritanceXml(AssetDeclarationDocument document)
	{
		if (document.IsLoaded && document.XmlDocument != null) return false;
		foreach (var handle in _RequiredInheritFromSources)
			if (document.SelfInstances.Contains(handle)) return true;
		return false;
	}
	public void RecordExecutionCompile() { InstanceCompileCount++; }
	internal void RecordExecutionCommit(AssetLocation source)
	{
		if (source == AssetLocation.Memory) _InstancesCompiledCount++;
		else if (source == AssetLocation.Local || source == AssetLocation.Cache) _InstancesCopiedFromCacheCount++;
	}
	private readonly HashSet<uint> _ProcessedTypeIds = new HashSet<uint>();
	public IEnumerable<uint> ProcessedTypeIds => _ProcessedTypeIds;
	internal void RecordXmlParse() { XmlParseCount++; Progress?.Invoke(0, 1, 0); }
	internal void RecordInstanceCompile() { InstanceCompileCount++; }

	// Local/intermediate reuse is excluded from persistent asset-cache counters.
	public int BuildCacheHits { get; private set; }

	public int BuildCacheMisses { get; private set; }

	internal void RecordBuildCacheUse(bool hit)
	{
		if (hit) BuildCacheHits++;
		else { BuildCacheMisses++; PerformanceScope.Count("cache.asset.keyMiss"); }
	}

	public ISessionCache Cache
	{
		get
		{
			return _Cache;
		}
		set
		{
			_Cache = value;
		}
	}

	public InstanceHandleSet MissingReferences => _MissingReferences;

	public InstanceHandleSet ChangedInheritFromReferences => _RequiredInheritFromSources;

	public SchemaSet SchemaSet
	{
		get
		{
			return _SchemaSet;
		}
		set
		{
			_SchemaSet = value;
		}
	}

	public PluginRegistry Plugins => _PluginRegistry;

	public VerifierPluginRegistry VerifierPlugins => _VerifierPluginRegistry;

	// Deferred owner callbacks retain the originating diagnostic context.
	internal void InDocumentContext(string sourcePath, Action action)
	{
		_CurrentDocumentStack.Push(Path.GetFileName(sourcePath) + ":");
		try { action(); }
		finally { _CurrentDocumentStack.Pop(); }
	}

	public static string CurrentDocument => _CurrentDocumentStack.Peek();

    private static readonly object StateScopeGate = new object();
    private static ProcessorStateScope _CurrentStateScope;

    // Own the legacy process-wide traversal/diagnostic state sequentially.
    // This guards scope ownership, not arbitrary concurrent legacy Core calls.
    public static IDisposable BeginStateScope()
    {
        // Prepare every allocation before changing shared state so allocation
        // failure cannot partially replace the caller's traversal frames.
        var missing = new InstanceHandleSet();
        var documents = new Stack<string>();
        var streams = new Stack<string>();
        var timings = new Dictionary<uint, TypeCompileData>();
        lock (StateScopeGate)
        {
            if (_CurrentStateScope != null && _CurrentStateScope.ThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Document processor state scopes may only be used on their owning thread.");
            var scope = new ProcessorStateScope();
            _MissingReferences = missing;
            _CurrentDocumentStack = documents;
            _CurrentStreamStack = streams;
            _TotalProcInstancesTime = TimeSpan.Zero;
            _TotalPrepareOutputTime = TimeSpan.Zero;
            _TotalPrepareSourceTime = TimeSpan.Zero;
            _TotalPostProcTime = TimeSpan.Zero;
            _TotalValidateTime = TimeSpan.Zero;
            _TypeProcessingTime = timings;
            _CurrentStateScope = scope;
            return scope;
        }
    }

    private sealed class ProcessorStateScope : IDisposable
    {
        internal readonly int ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private readonly ProcessorStateScope parent = _CurrentStateScope;
        private readonly InstanceHandleSet missing = _MissingReferences;
        private readonly Stack<string> documents = _CurrentDocumentStack;
        private readonly Stack<string> streams = _CurrentStreamStack;
        private readonly TimeSpan instances = _TotalProcInstancesTime;
        private readonly TimeSpan output = _TotalPrepareOutputTime;
        private readonly TimeSpan source = _TotalPrepareSourceTime;
        private readonly TimeSpan post = _TotalPostProcTime;
        private readonly TimeSpan validate = _TotalValidateTime;
        private readonly IDictionary<uint, TypeCompileData> timings = _TypeProcessingTime;
        private bool disposed;

        public void Dispose()
        {
            lock (StateScopeGate)
            {
                if (disposed) return;
                if (ThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("Document processor state scopes may only be disposed on their owning thread.");
                if (!Object.ReferenceEquals(_CurrentStateScope, this))
                    throw new InvalidOperationException("Document processor state scopes must be disposed in reverse creation order.");
                _MissingReferences = missing;
                _CurrentDocumentStack = documents;
                _CurrentStreamStack = streams;
                _TotalProcInstancesTime = instances;
                _TotalPrepareOutputTime = output;
                _TotalPrepareSourceTime = source;
                _TotalPostProcTime = post;
                _TotalValidateTime = validate;
                _TypeProcessingTime = timings;
                _CurrentStateScope = parent;
                disposed = true;
            }
        }
    }

	public static void ResetTimers()
	{
		_TotalProcInstancesTime = new TimeSpan(0L);
		_TotalPrepareOutputTime = new TimeSpan(0L);
		_TotalPrepareSourceTime = new TimeSpan(0L);
		_TotalPostProcTime = new TimeSpan(0L);
		_TotalValidateTime = new TimeSpan(0L);
		_TypeProcessingTime = new Dictionary<uint, TypeCompileData>();
	}

	public DocumentProcessor(Settings settings, PluginRegistry pluginRegistry, VerifierPluginRegistry verifierPluginRegistry)
	{
		_PluginRegistry = pluginRegistry;
		_VerifierPluginRegistry = verifierPluginRegistry;
		if (Settings.Current.SingleFile)
		{
			_Tracer.TraceInfo("Single file mode enabled.");
			Settings.Current.BuildCache = false;
		}
		_Tracer.TraceData("Build Cache Status: {0}", Settings.Current.BuildCache ? "Disabled" : "Active");
		MetricManager.Submit("BAB.NetworkCacheEnabled", Settings.Current.BuildCache);
		if (Settings.Current.BuildCache)
		{
			_Tracer.TraceInfo("Network caching enabled ('{0}').", Settings.Current.BuildCacheDirectory);
		}
	}

	public void ProcessDocument(string fileName, bool generateOutput, bool __outputStringHashes, out bool success)
	{
		ExpressionEvaluatorWrapper.LoadAssembly();
		DateTime now = DateTime.Now;
		MetricTimer timer = new MetricTimer("BAB.ProcessingTime");
		Exception failure = null;
		try
		{
			if (!File.Exists(fileName))
			{
				throw new BinaryAssetBuilderException(ErrorCode.InputXmlFileNotFound, "File {0} not found", fileName);
			}
			MetricManager.Submit("BAB.MapName", Path.GetFileName(Path.GetDirectoryName(fileName)));
			MetricManager.Submit("BAB.StartupTime", DateTime.Now - now);
			now = DateTime.Now;
			success = false;
			try
			{
				if (Path.GetExtension(fileName).ToLower().Equals(".babproj", StringComparison.CurrentCultureIgnoreCase))
				{
					ProcessProjectDocument(fileName, generateOutput);
				}
				else
				{
					ProcessOptions processOptions = new ProcessOptions();
					processOptions.GenerateOutput = generateOutput;
					processOptions.BasePatchStream = Settings.Current.BasePatchStream;
					processOptions.UsePrecompiled = Settings.Current.UsePrecompiled;
					ProcessDocumentInternal(fileName, fileName, null, processOptions);
				}
				success = true;
			}
			catch (Exception exception)
			{
				failure = exception;
				throw;
			}
			finally
			{
				try
				{
					MetricManager.Submit("BAB.PrepSourceTime", _TotalPrepareSourceTime);
					MetricManager.Submit("BAB.ProcIncludesTime", _TotalPostProcTime);
					MetricManager.Submit("BAB.ValidateTime", _TotalValidateTime);
					MetricManager.Submit("BAB.InstanceProcessingTime", _TotalProcInstancesTime);
					MetricManager.Submit("BAB.OutputPrepTime", _TotalPrepareOutputTime);
					MetricManager.Submit("BAB.DocumentProcessingTime", DateTime.Now - now);
					now = DateTime.Now;
					MetricManager.Submit("BAB.LinkedStreamsEnabled", Settings.Current.LinkedStreams);
					MetricManager.Submit("BAB.MaxMemorySize", _MaxTotalMemory);
					MetricManager.Submit("BAB.FilesProcessedCount", _FilesProcessedCount);
					MetricManager.Submit("BAB.FilesParsedCount", _FilesParsedCount);
					MetricManager.Submit("BAB.InstancesProcessedCount", _InstancesProcessedCount);
					MetricManager.Submit("BAB.InstancesCompiledCount", _InstancesCompiledCount);
					MetricManager.Submit("BAB.InstancesCopiedFromCacheCount", _InstancesCopiedFromCacheCount);
					MetricManager.Submit("BAB.ShutdownTime", DateTime.Now - now);
					MetricManager.Submit("BAB.BuildSuccessful", success);
				}
				catch (Exception metricFailure)
				{
					if (failure == null) throw;
					failure.Data["BinaryAssetBuilder.ProcessDocument.FinalMetricsFailure"] = metricFailure;
				}
			}
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			try { timer.Dispose(); }
			catch (Exception metricFailure)
			{
				if (failure == null) throw;
				failure.Data["BinaryAssetBuilder.ProcessDocument.ProcessingTimeFailure"] = metricFailure;
			}
		}
	}

	public void ProcessProjectDocument(string projectPath, bool generateOutput)
	{
		Settings current = Settings.Current;
		bool compilingProject = _CompilingProject;
		Dictionary<string, string> defaultConfigurations = _ProjectDefaultConfigurations;
		try
		{
			_CompilingProject = true;
			_ProjectDefaultConfigurations = new Dictionary<string, string>();
			XmlSchema schema;
			using (Stream schemaStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BinaryAssetBuilderProject.xsd"))
			{
				schema = XmlSchema.Read(schemaStream, null);
			}
			BinaryAssetBuilderProject binaryAssetBuilderProject = null;
			using (XmlReader xmlReader = XmlReader.Create(projectPath))
			{
				xmlReader.Settings.Schemas.Add(schema);
				try
				{
					XmlSerializer xmlSerializer = new XmlSerializer(typeof(BinaryAssetBuilderProject));
					binaryAssetBuilderProject = xmlSerializer.Deserialize(xmlReader) as BinaryAssetBuilderProject;
				}
				catch (InvalidOperationException ex)
				{
					_Tracer.TraceError("There is an error in '{0}': {1}\n", projectPath, ex.InnerException.Message);
				}
			}
			if (binaryAssetBuilderProject != null)
			{
				IDictionary<string, bool> dictionary = new SortedDictionary<string, bool>();
				BinaryStream[] stream = binaryAssetBuilderProject.Stream;
				foreach (BinaryStream binaryStream in stream)
				{
					string path = ((!Path.IsPathRooted(binaryStream.Source)) ? Path.Combine(Path.GetDirectoryName(projectPath), binaryStream.Source) : binaryStream.Source);
					path = Path.GetFullPath(path).ToLower();
					if (dictionary.TryGetValue(path, out var value) && value)
					{
						_Tracer.TraceError("{0} was specified twice in {1}, skipping duplicates", path, projectPath);
					}
					else if (binaryStream.Configuration != null && binaryStream.Configuration.Length > 0)
					{
						StreamConfiguration streamConfiguration = null;
						StreamConfiguration[] configuration = binaryStream.Configuration;
						foreach (StreamConfiguration streamConfiguration2 in configuration)
						{
							if (streamConfiguration2.Default)
							{
								if (streamConfiguration != null)
								{
									_Tracer.TraceWarning("Stream {0} has multiple default configurations. Using {1}.", path, streamConfiguration.Name);
								}
								else
								{
									streamConfiguration = streamConfiguration2;
								}
							}
						}
						if (streamConfiguration == null)
						{
							_Tracer.TraceInfo("No default configuration specified for {0}. Using {1}", binaryStream.Source, binaryStream.Configuration[0].Name);
							streamConfiguration = binaryStream.Configuration[0];
						}
						_ProjectDefaultConfigurations[path] = streamConfiguration.Name;
						StreamConfiguration[] configuration2 = binaryStream.Configuration;
						foreach (StreamConfiguration streamConfiguration3 in configuration2)
						{
							try
							{
								Settings.Current = SettingsLoader.GetSettingsForConfiguration(streamConfiguration3.Name);
								Settings.Current.BuildConfigurationName = streamConfiguration3.Name;
								ProcessOptions processOptions = new ProcessOptions();
								processOptions.GenerateOutput = true;
								processOptions.UsePrecompiled = true;
								processOptions.Configuration = streamConfiguration3.Name;
								processOptions.BasePatchStream = streamConfiguration3.PatchStream;
								processOptions.BaseStreamSearchPaths = streamConfiguration3.BaseStreamSearchPath;
								processOptions.RelativeBasePath = streamConfiguration3.RelativeBasePath;
								if (streamConfiguration3.StreamReference != null)
								{
									processOptions.StreamReferences = streamConfiguration3.StreamReference;
								}
								ProcessDocumentInternal(path, path, null, processOptions);
							}
							finally
							{
								Settings.Current = current;
							}
						}
					}
					else
					{
						_ProjectDefaultConfigurations[path] = "";
						ProcessOptions processOptions2 = new ProcessOptions();
						processOptions2.GenerateOutput = true;
						processOptions2.UsePrecompiled = true;
						ProcessDocumentInternal(path, path, null, processOptions2);
					}
				}
			}
		}
		finally
		{
			Settings.Current = current;
			_CompilingProject = compilingProject;
			_ProjectDefaultConfigurations = defaultConfigurations;
		}
	}

	public AssetDeclarationDocument ProcessDocumentInternal(string logicalPath, string sourcePath, OutputManager outputManager, ProcessOptions op)
	{
		using var performance = PerformanceScope.Measure("core.document");
		DateTime now = DateTime.Now;
		AssetDeclarationDocument assetDeclarationDocument = OpenDocument(sourcePath, logicalPath, op.GenerateOutput, op.Configuration);
		int documentDepth = _CurrentDocumentStack.Count;
		int streamDepth = _CurrentStreamStack.Count;
		int inclusionDepth = _DocumentStack.Count;
		bool processing = assetDeclarationDocument.Processing;
		try
		{
			if (op.GenerateOutput)
			{
				if (Settings.Current.StreamHints && Cache.DirtyStreams != null && !Cache.DirtyStreams.Contains(sourcePath) && LoadPrecompiledReference(assetDeclarationDocument, GetExpectedOutputManifest(assetDeclarationDocument.SourcePath), op.BaseStreamSearchPaths))
				{
					return assetDeclarationDocument;
				}
				if (assetDeclarationDocument.State == DocumentState.Complete)
				{
					return assetDeclarationDocument;
				}
				if (!Settings.Current.SingleFile && string.IsNullOrEmpty(assetDeclarationDocument.SourcePathFromRoot))
				{
					throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "{0} is a stream (.manifest) but does not have have {1} as its root!", sourcePath, Settings.Current.DataRoot);
				}
				string text = ShPath.Canonicalize(Path.Combine(Settings.Current.IntermediateOutputDirectory, assetDeclarationDocument.SourcePathFromRoot));
				string text2 = ShPath.Canonicalize(Path.Combine(Settings.Current.OutputDirectory, assetDeclarationDocument.SourcePathFromRoot));
				text2 = text2 + Settings.Current.StreamPostfix + Settings.Current.CustomPostfix;
				text = text + Settings.Current.StreamPostfix + Settings.Current.CustomPostfix;
				outputManager = new OutputManager(this, assetDeclarationDocument.LastOutputAssets, text2, text, op.BasePatchStream, op.RelativeBasePath, op.BaseStreamSearchPaths);
				_CurrentStreamStack.Push(sourcePath);
			}
			string fileName = Path.GetFileName(sourcePath);
			_CurrentDocumentStack.Push($"{fileName}:");
			if (_ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var existingVersion) && existingVersion.FrontendPrepared && !existingVersion.Failed)
			{
				bool newOutput = outputManager != null && !existingVersion.Outputs.ContainsKey(outputManager);
				if (!existingVersion.Finalized && (NeedsInheritanceXml(assetDeclarationDocument) ||
					(newOutput && NeedsBackendXml(assetDeclarationDocument, outputManager))))
					assetDeclarationDocument = ReplaceExecutionDocument(assetDeclarationDocument, op.Configuration);
				else if (!op.GenerateOutput && outputManager != null && existingVersion.Outputs.ContainsKey(outputManager))
				{
					assetDeclarationDocument.AddStreamsHints(_CurrentStreamStack.ToArray());
					return assetDeclarationDocument;
				}
			}
			if (assetDeclarationDocument.State == DocumentState.Complete && assetDeclarationDocument.IsLoaded && assetDeclarationDocument.XmlDocument == null)
			{
				foreach (InstanceDeclaration instance in assetDeclarationDocument.Instances)
				{
					BinaryAsset binaryAsset = outputManager.GetBinaryAsset(instance, isOutputAsset: false);
					if (binaryAsset.GetLocation(AssetLocation.All, AssetLocationOption.None) == AssetLocation.None)
					{
						_Tracer.TraceInfo("Reloading 'file://{0}' for new stream", assetDeclarationDocument.SourcePath);
						assetDeclarationDocument.State = DocumentState.Shallow;
						break;
					}
				}
			}
			bool frontendPublished = _ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var publishedVersion) &&
				publishedVersion.FrontendPrepared && !publishedVersion.Finalized && !publishedVersion.Failed;
			if (assetDeclarationDocument.State == DocumentState.Shallow && !frontendPublished)
			{
				_ExecutionDocuments.Remove(assetDeclarationDocument);
				assetDeclarationDocument.Reinitialize(outputManager);
			}
			if (!assetDeclarationDocument.IsLoaded && NeedsInheritanceXml(assetDeclarationDocument))
			{
				if (_ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var priorVersion) && !priorVersion.Finalized)
					assetDeclarationDocument = ReplaceExecutionDocument(assetDeclarationDocument, op.Configuration);
				else _ExecutionDocuments.Remove(assetDeclarationDocument);
				assetDeclarationDocument.ReloadIfRequired(_RequiredInheritFromSources);
			}
			_DocumentStack.Add(sourcePath);
			assetDeclarationDocument.Processing = true;
			_TotalPrepareSourceTime += DateTime.Now - now;
			ProcessIncludedDocuments(assetDeclarationDocument, outputManager, op, op.UsePrecompiled);
			bool newOutputSubscription = AssetExecution != null && outputManager != null &&
				(!_ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var outputVersion) || !outputVersion.Outputs.ContainsKey(outputManager));
			if (assetDeclarationDocument.State != DocumentState.Complete || newOutputSubscription)
			{
				ProcessDocumentContents(ref assetDeclarationDocument, outputManager, op, op.GenerateOutput);
			}
			_MaxTotalMemory = Math.Max(_MaxTotalMemory, GC.GetTotalMemory(forceFullCollection: false));
			return assetDeclarationDocument;
		}
		catch
		{
			if (_ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var failedVersion)) FailDocumentVersion(failedVersion);
			throw;
		}
		finally
		{
			assetDeclarationDocument.Processing = processing;
			try
			{
				if (_ExecutionDocuments.TryGetValue(assetDeclarationDocument, out var version))
				{
					try { TryCompleteDocumentVersion(assetDeclarationDocument, version); }
					catch { FailDocumentVersion(version); throw; }
				}
			}
			finally
			{
				// Completion can fail while saving the session. Its exception
				// must not retain this invocation's traversal/diagnostic frames.
				while (_DocumentStack.Count > inclusionDepth) _DocumentStack.RemoveAt(_DocumentStack.Count - 1);
				while (_CurrentDocumentStack.Count > documentDepth) _CurrentDocumentStack.Pop();
				while (_CurrentStreamStack.Count > streamDepth) _CurrentStreamStack.Pop();
			}
		}
	}

	private void ProcessDocumentContents(ref AssetDeclarationDocument document, OutputManager outputManager, ProcessOptions op, bool generateOutput)
	{
		string configuration = op.Configuration;
		var version = GetDocumentVersion(document, configuration);
		if (!version.FrontendPrepared)
		{
			if (document.State != DocumentState.Loaded && !document.ValidateInheritFromSources())
			{
				document.InplaceLoad("inheritFrom source changed");
				ProcessIncludedDocuments(document, outputManager, op, usePrecompiled: false);
			}
			if (document.State != DocumentState.Loaded && !document.ValidateCachedDefines())
			{
				document.InplaceLoad("used definitions changed");
				ProcessIncludedDocuments(document, outputManager, op, usePrecompiled: false);
			}
			if (document.State == DocumentState.Loaded)
			{
				DateTime now = DateTime.Now;
				_FilesParsedCount++;
				document.ProcessExpressions();
				document.ProcessOverrides();
				document.Validate();
				_TotalValidateTime += DateTime.Now - now;
			}
			version.FrontendPrepared = true;
		}
		if (document.State != DocumentState.Loaded)
		{
			DocumentReuseCount++;
			Progress?.Invoke(0, 0, 1);
		}
		foreach (InstanceDeclaration instance in document.SelfInstances) _ProcessedTypeIds.Add(instance.Handle.TypeId);
		_InstancesProcessedCount += document.SelfInstances.Count;
		document.RecordStringHashes();
		document.MergeInstances();
		AssetExecutionDocument executionDocument = outputManager == null ? null : GetExecutionDocument(document, outputManager);
		if (outputManager != null)
		{
			DateTime now2 = DateTime.Now;
			document.ProcessInstances(outputManager, ref _InstancesCompiledCount, ref _InstancesCopiedFromCacheCount);
			_TotalProcInstancesTime += DateTime.Now - now2;
		}
		document.AddStreamsHints(_CurrentStreamStack.ToArray());
		CompleteExecutionDocument(document, version, executionDocument);
		if (generateOutput)
		{
			if (AssetExecution != null) AssetExecution.WaitOutput(outputManager);
			DateTime now3 = DateTime.Now;
			_Tracer.Message("Resolving references: {0}", document.SourcePathFromRoot);
			using (PerformanceScope.Measure("core.output.references")) document.PrepareOutputInstances(outputManager);
			_Tracer.Message("Generating stream: {0}", document.SourcePathFromRoot);
			outputManager.CommitManifest(document);
			outputManager.CleanOutput();
			document.UpdateOutputAssets(outputManager);
			if (Settings.Current.LinkedStreams)
			{
				outputManager.LinkStream(document);
			}
			if (Settings.Current.VersionFiles)
			{
				outputManager.CreateVersionFile(document, Settings.Current.CustomPostfix);
			}
			outputManager = null;
			_Tracer.Message("{0} Stream complete", document.SourcePathFromRoot);
			_TotalPrepareOutputTime += DateTime.Now - now3;
		}
		version.CompletionRequested = true;
		TryCompleteDocumentVersion(document, version);
	}

	private AssetDeclarationDocument OpenDocument(string sourcePath, string logicalPath, bool generateOutput, string configuration)
	{
		using var performance = PerformanceScope.Measure("core.document.open");
		if (!Path.IsPathRooted(sourcePath))
		{
			throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Path for document {0} is not rooted.", sourcePath);
		}
		Cache.TryGetFile(sourcePath, configuration, Settings.Current.TargetPlatform, out var hashItem);
		Cache.TryGetDocument(sourcePath, configuration, Settings.Current.TargetPlatform, autoCreateDocument: true, out var document);
		if (document.Processing)
		{
			StringBuilder stringBuilder = new StringBuilder("Illegal circular document inclusion detected. Inclusion chain as follows:");
			foreach (string item in _DocumentStack)
			{
				stringBuilder.AppendFormat("\n   {0}", item);
			}
			stringBuilder.AppendFormat("\n   {0}", sourcePath);
			throw new BinaryAssetBuilderException(ErrorCode.CircularDependency, stringBuilder.ToString());
		}
		if (_ExecutionDocuments.TryGetValue(document, out var version))
		{
			foreach (var scope in version.Outputs.Values)
				if (scope.IsFailed) { FailDocumentVersion(version); break; }
			if (version.FrontendPrepared && !version.Finalized && !version.Failed && document.State != DocumentState.None)
				return document;
		}
		// Open replaces Current whenever the document is incomplete. A saved
		// wrapper can also have been ResetState by cache initialization; neither
		// current may reuse the preceding version's front-end completion receipt.
		if (document.State != DocumentState.Complete) _ExecutionDocuments.Remove(document);
		document.Open(this, hashItem, logicalPath, configuration);
		if (document.State != DocumentState.Complete)
		{
			_FilesProcessedCount++;
		}
		return document;
	}

	public string GetExpectedOutputManifest(string path)
	{
		string dataRoot = FileNameResolver.GetDataRoot(path);
		string text;
		if (!string.IsNullOrEmpty(dataRoot))
		{
			string path2 = FileNameResolver.GetSafeRelativePath(dataRoot, path);
			string directoryName = Path.GetDirectoryName(path2);
			text = Path.Combine(directoryName, Path.GetFileNameWithoutExtension(path2));
		}
		else
		{
			text = Path.GetFileNameWithoutExtension(path);
		}
		return text + ".manifest";
	}

	private bool LoadPrecompiledReference(AssetDeclarationDocument parentDoc, string sourcePathFromRoot, string[] baseStreamSearchPaths)
	{
		string text = ShPath.Canonicalize(Path.Combine(Settings.Current.OutputDirectory, sourcePathFromRoot));
		if (File.Exists(text))
		{
			Manifest manifest = new Manifest();
			List<string> list = new List<string>();
			if (baseStreamSearchPaths != null)
			{
				list.AddRange(baseStreamSearchPaths);
			}
			list.Add(Settings.Current.OutputDirectory);
			try
			{
				manifest.Load(text, list.ToArray());
			}
			catch (Exception ex)
			{
				_Tracer.TraceError("Could not load {0}: {1}\n\r{2}", text, ex.Message, ex.StackTrace);
				return false;
			}
			try
			{
				ManifestHeader manifestHeader = new ManifestHeader();
				using (FileStream input = File.OpenRead(text))
				{
					manifestHeader.LoadFromStream(input, Settings.Current.BigEndian);
				}
				if (manifestHeader.IsLinked != Settings.Current.LinkedStreams || manifestHeader.Version != ManifestHeader.LatestVersion || manifestHeader.AllTypesHash != Plugins.DefaultPlugin.GetAllTypesHash())
				{
					_Tracer.TraceWarning("Could not load precompiled manifest {0}, manifest is incompatible", text);
					return false;
				}
			}
			catch (Exception innerException)
			{
				throw new BinaryAssetBuilderException(innerException, ErrorCode.LockedFile, "Unable to open '{0}'. Make sure no other application is writing to or reading from this file while the data build is running.", text);
			}
			Asset[] assets = manifest.Assets;
			foreach (Asset asset in assets)
			{
				InstanceDeclaration instanceDeclaration = new InstanceDeclaration();
				instanceDeclaration.InitializePrecompiled(asset);
				parentDoc.ReferenceInstances.Add(instanceDeclaration);
			}
			return true;
		}
		_Tracer.TraceWarning("Could not load precompiled manifest {0}, file not found", text);
		return false;
	}

	private void ProcessIncludedDocuments(AssetDeclarationDocument document, OutputManager outputManager, ProcessOptions op, bool usePrecompiled)
	{
		using var performance = PerformanceScope.Measure("core.includes");
		DateTime now = DateTime.Now;
		string configuration = op.Configuration;
		document.TentativeInstances.Clear();
		document.AllInstances.Clear();
		document.ReferenceInstances.Clear();
		document.AllDefines.Clear();
		_ = document.SourceDirectory;
		if (document.IsLoaded)
		{
			foreach (InstanceDeclaration selfInstance in document.SelfInstances)
			{
				if (selfInstance.InheritFromHandle != null)
				{
					_RequiredInheritFromSources.TryAdd(selfInstance.InheritFromHandle);
				}
			}
		}
		foreach (InclusionItem inclusionItem in document.InclusionItems)
		{
			PerformanceScope.Count("dependency.include.visits");
			PerformanceScope.Count("dependency.include.maxDepth", _DocumentStack.Count, maximum: true);
			StreamReference streamReference = null;
			if (inclusionItem.Type == InclusionType.Reference && !Settings.Current.SingleFile && Settings.Current.DataRoot == null)
			{
				throw new BinaryAssetBuilderException(ErrorCode.NoDataRootSpecified, "DataRoot must be specified if not doing /singleFile");
			}
			string text = configuration;
			bool flag = false;
			AssetDeclarationDocument document2;
			if (inclusionItem.Type == InclusionType.Reference)
			{
				StreamReference[] streamReferences = op.StreamReferences;
				foreach (StreamReference streamReference2 in streamReferences)
				{
					if (inclusionItem.LogicalPath.EndsWith(streamReference2.ReferenceName))
					{
						text = streamReference2.ReferenceConfiguration;
						streamReference = streamReference2;
						_Tracer.TraceInfo("Attempting use of referenced stream {0} with configuration {1}", streamReference2.ReferenceName, streamReference2.ReferenceConfiguration);
						break;
					}
				}
				flag = Cache.TryGetDocument(inclusionItem.PhysicalPath, text, Settings.Current.TargetPlatform, autoCreateDocument: false, out document2);
				WaitForExecutionDocument(document2);
				if (streamReference != null && document2 != null && document2.State == DocumentState.None)
				{
					bool flag2 = false;
					BuildConfiguration[] buildConfigurations = Settings.Current.BuildConfigurations;
					foreach (BuildConfiguration buildConfiguration in buildConfigurations)
					{
						if (string.IsNullOrEmpty(text) || buildConfiguration.Name.ToLower().Equals(text.ToLower()))
						{
							string path = null;
							if (string.IsNullOrEmpty(streamReference.ReferenceManifest))
							{
								GetExpectedOutputManifest(inclusionItem.PhysicalPath);
								path = Path.GetFileNameWithoutExtension(path) + buildConfiguration.StreamPostfix + ".manifest";
							}
							else
							{
								path = streamReference.ReferenceManifest;
							}
							document2.FromLastHack();
							if (!LoadPrecompiledReference(document2, path, op.BaseStreamSearchPaths))
							{
								throw new BinaryAssetBuilderException(ErrorCode.ReferencingError, "Explicitly referenced external stream not found with desired build configuration.  Halting.");
							}
							flag2 = true;
							break;
						}
					}
					if (!flag2)
					{
						throw new BinaryAssetBuilderException(ErrorCode.ReferencingError, "Explicitly referenced stream was not found built with configuration {0}", text);
					}
				}
				else if (!flag || document2 == null || document2.State != DocumentState.Complete)
				{
					if (_CompilingProject && _ProjectDefaultConfigurations.TryGetValue(inclusionItem.PhysicalPath.ToLower(), out var value))
					{
						FileHashItem hashItem = null;
						text = value;
						flag = Cache.TryGetFile(inclusionItem.PhysicalPath, text, Settings.Current.TargetPlatform, out hashItem);
						_Tracer.TraceInfo("Stream {0} references stream {1} which does not have a '{2}' configuration, using default configuration '{3}'", document.SourcePath, inclusionItem.LogicalPath, configuration, text);
					}
					else if (usePrecompiled)
					{
						_Tracer.TraceInfo("Stream {0} references stream {1} which has not been compiled, attempting to use precompiled", document.SourcePath, inclusionItem.PhysicalPath);
						if (LoadPrecompiledReference(document, GetExpectedOutputManifest(inclusionItem.PhysicalPath), op.BaseStreamSearchPaths) || _CompilingProject)
						{
							continue;
						}
					}
				}
			}
			else
			{
				flag = Cache.TryGetDocument(inclusionItem.PhysicalPath, configuration, Settings.Current.TargetPlatform, autoCreateDocument: false, out document2);
			}
			if (!flag)
			{
				if (Settings.Current.ErrorLevel > 0)
				{
					throw new BinaryAssetBuilderException(ErrorCode.FileNotFound, "Input file '{0}' not found (referenced from file://{1}). Treating it as empty.", inclusionItem.LogicalPath, document.SourcePath);
				}
				_Tracer.TraceError("Input file '{0}' not found (referenced from file://{1}). Treating it as empty.", inclusionItem.LogicalPath, document.SourcePath);
			}
			bool flag3 = inclusionItem.Type == InclusionType.Reference && !usePrecompiled;
			_TotalPostProcTime += DateTime.Now - now;
			ProcessOptions processOptions = new ProcessOptions();
			processOptions.GenerateOutput = flag3;
			processOptions.UsePrecompiled = usePrecompiled;
			processOptions.Configuration = text;
			if (streamReference == null)
			{
				document2 = ProcessDocumentInternal(inclusionItem.LogicalPath, inclusionItem.PhysicalPath, flag3 ? null : outputManager, processOptions);
				now = DateTime.Now;
				inclusionItem.Document = document2;
				if (document2.CacheInvalidated || document2.DependencyAffected) {
					PerformanceScope.Count("dependency.include.affectedVisits");
					document.RecordDependencyAffected();
				}
				document.AllDefines.AddDefinitions(document2.AllDefines);
			}
			switch (inclusionItem.Type)
			{
			case InclusionType.Reference:
				document.ReferenceInstances.Add(document2.ReferenceInstances);
				document.ReferenceInstances.Add(document2.Instances);
				break;
			case InclusionType.All:
				document.ReferenceInstances.Add(document2.ReferenceInstances);
				document.AllInstances.Add(document2.Instances);
				document.TentativeInstances.Add(document2.TentativeInstances);
				break;
			case InclusionType.Instance:
				document.TentativeInstances.Add(document2.Instances);
				document.TentativeInstances.Add(document2.TentativeInstances);
				break;
			}
			ResetExecutionDocument(document2);
			_TotalPostProcTime += DateTime.Now - now;
		}
		foreach (InstanceDeclaration referenceInstance in document.ReferenceInstances)
		{
			if (document.AllInstances.TryGetValue(referenceInstance.Handle, out var value2) && value2.Document == referenceInstance.Document)
			{
				document.AllInstances.Remove(value2);
			}
			if (document.TentativeInstances.TryGetValue(referenceInstance.Handle, out value2) && value2.Document == referenceInstance.Document)
			{
				document.TentativeInstances.Remove(value2);
			}
		}
		document.EvaluateDefinitions();
	}

	private void TryDeleteFile(string filePath)
	{
		if (File.Exists(filePath))
		{
			try
			{
				File.Delete(filePath);
			}
			catch (Exception innerException)
			{
				throw new BinaryAssetBuilderException(innerException, ErrorCode.LockedFile, "Unable to delete '{0}'. Make sure no other application is writing to or reading from this file while the data build is running.", filePath);
			}
		}
	}

	public void AddLastWrittenAsset(BinaryAsset asset)
	{
		if (!_LastWrittenAssets.ContainsKey(asset.FileBase))
		{
			AssetLocationInfo assetLocationInfo = new AssetLocationInfo();
			assetLocationInfo.AssetOutputDirectory = asset.AssetOutputDirectory;
			assetLocationInfo.CustomDataOutputDirectory = asset.Instance.HasCustomData ? Path.GetDirectoryName(asset.CustomDataPath) : asset.CustomDataOutputDirectory;
			_LastWrittenAssets.Add(asset.FileBase, assetLocationInfo);
			// RecordCommit reaches this only after publication succeeds. Reuse
			// its existing identity table so repeated includes count just once.
			Progress?.Invoke(1, 0, 0);
		}
	}

	public AssetLocationInfo GetLastWrittenAsset(string key)
	{
		AssetLocationInfo value = null;
		_LastWrittenAssets.TryGetValue(key, out value);
		return value;
	}

	public void AddCompileTime(InstanceHandle handle, TimeSpan instanceCompileTime)
	{
		if (!_TypeProcessingTime.TryGetValue(handle.TypeId, out var value))
		{
			value = new TypeCompileData(handle.TypeName);
			_TypeProcessingTime.Add(handle.TypeId, value);
		}
		value.InstancesProcessed++;
		if (instanceCompileTime > value.LongestProcessTime)
		{
			value.LongestProcessTime = instanceCompileTime;
			value.LongestProcessInstance = handle.InstanceName;
		}
		value.TotalProcessTime += instanceCompileTime;
	}

	public void OutputTypeCompileTimes()
	{
		TimeSpan timeSpan = new TimeSpan(0L);
		foreach (KeyValuePair<uint, TypeCompileData> item in _TypeProcessingTime)
		{
			TypeCompileData value = item.Value;
			_Tracer.Message("{0}:", value.TypeName);
			_Tracer.Message("      Instances Processed: {0}", value.InstancesProcessed);
			_Tracer.Message("      Total Processing Time: {0}", value.TotalProcessTime);
			_Tracer.Message("      Longest Processing Time: {0} ({1})", value.LongestProcessTime, value.LongestProcessInstance);
			_Tracer.Message("      Average Processing Time: {0} seconds", value.TotalProcessTime.TotalSeconds / (double)value.InstancesProcessed);
			timeSpan += value.TotalProcessTime;
		}
		_Tracer.Message("Total Processing Time for All Types: {0}", timeSpan);
	}
}
