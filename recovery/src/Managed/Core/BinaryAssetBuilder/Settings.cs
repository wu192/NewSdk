using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Xml.Serialization;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder;

public class Settings : ICloneable
{
	public static Settings Current;

    [XmlIgnore]
    public Func<string, IDisposable> MeasurePerformance { get; set; }

    [XmlIgnore]
    public Action<string, long, bool> RecordPerformanceCounter { get; set; }

    [XmlIgnore]
    public Action<string> StreamOutputStarted { get; set; }
    [XmlIgnore]
    public Action<string, bool> StreamOutputCompleted { get; set; }
    [XmlIgnore]
    public Action<string> CustomDataOutput { get; set; }
	[XmlIgnore]
	public Func<string, FileHashInfo> FileIdentity { get; set; }
	[XmlIgnore]
	public Action<long> FileHashRead { get; set; }
	[XmlIgnore]
	public Func<IEnumerable<string>, IDictionary<string, string>> SchemaFileIdentities { get; set; }
	[XmlIgnore]
	public Func<string, string> FindCustomData { get; set; }

	private string _SchemaPath;

	private string _BuildCacheDirectory;

	private bool _SessionCache = true;

	private bool _BuildCache = true;

	private string _SessionCacheDirectory;

	private bool _PartialSessionCache = true;

	private TargetPlatform _TargetPlatform;

	private bool _CompressedSessionCache = true;

	private bool _AlwaysTouchCache;

	private bool _MetricsReporting;

	private bool _OldOutputFormat;

	private string _DataRoot;

	private int _TraceLevel = 3;

	private int _ErrorLevel;

	private string _BuildConfigurationName;

	private bool _PauseOnError;

	private bool _SingleFile;

	private bool _FreezeSessionCache;

	private bool _LinkedStreams;

	private string _IntermediateOutputDirectory;

	private bool _OutputIntermediateXml;

	private bool _GuiMode;

	private bool _ForceSlowCleanup;

	private string _OutputDirectory;

	private string _InputPath;

	private string[] _DataPaths;

	private string[] _ArtPaths;

	private string[] _AudioPaths;

	private string[] _ProcessedMonitorPaths;

	private PluginDescriptor[] _Plugins;

	private PluginDescriptor[] _VerifierPlugins;

	private BuildConfiguration[] _BuildConfigurations;

	private string _DefaultArtPaths;

	private string _DefaultAudioPaths;

	private string _DefaultDataPaths;

	private string _MonitorPaths;

	private string _Postfix;

	private string _StreamPostfix;

	private bool _BigEndian;

	private bool _StableSort;

	private string _BasePatchStream;

	private bool _UsePrecompiled;

	private bool _VersionFiles;

	private string _CustomPostfix = "";

	private bool _Resident;

	private bool _EnableStreamHints;

	private string _AssetNamespace = "uri:ea.com:eala:asset";

	private bool _OutputAssetReport;

	private bool _OutputStringHashes = true;

	private StringHashBinDescriptor[] _StringHashBinDescriptors;

	[XmlAttribute("schema")]
	[OptionalCommandLineOption("sp")]
	[Description("Schema describing the XML file processed")]
	public string SchemaPath
	{
		get
		{
			return _SchemaPath;
		}
		set
		{
			_SchemaPath = value;
		}
	}

	[OptionalCommandLineOption("bcp,bcd")]
	[XmlAttribute("buildCacheRoot")]
	[Description("Directory used for caching assets on the network")]
	public string BuildCacheDirectory
	{
		get
		{
			return _BuildCacheDirectory;
		}
		set
		{
			_BuildCacheDirectory = value;
		}
	}

	[XmlIgnore]
	[OptionalCommandLineOption("sc")]
	[Description("Enable session cache")]
	public bool SessionCache
	{
		get
		{
			return _SessionCache;
		}
		set
		{
			_SessionCache = value;
		}
	}

	[XmlAttribute("buildCache")]
	[Description("Enable build cache")]
	[OptionalCommandLineOption("bc")]
	public bool BuildCache
	{
		get
		{
			return _BuildCache;
		}
		set
		{
			_BuildCache = value;
		}
	}

	[Description("Directory used for storing the session cache")]
	[OptionalCommandLineOption("scd,scp")]
	[XmlIgnore]
	public string SessionCacheDirectory
	{
		get
		{
			return _SessionCacheDirectory;
		}
		set
		{
			_SessionCacheDirectory = value;
		}
	}

	[Description("Save session cache on aborted build")]
	[OptionalCommandLineOption("psc")]
	[XmlIgnore]
	public bool PartialSessionCache
	{
		get
		{
			return _PartialSessionCache;
		}
		set
		{
			_PartialSessionCache = value;
		}
	}

	[Description("Target platform for generated data")]
	[OptionalCommandLineOption("tp")]
	[XmlIgnore]
	public TargetPlatform TargetPlatform
	{
		get
		{
			return _TargetPlatform;
		}
		set
		{
			_TargetPlatform = value;
		}
	}

	[OptionalCommandLineOption("csc")]
	[Description("Generate compressed session cache")]
	[XmlIgnore]
	public bool CompressedSessionCache
	{
		get
		{
			return _CompressedSessionCache;
		}
		set
		{
			_CompressedSessionCache = value;
		}
	}

	[OptionalCommandLineOption("atc")]
	[Description("Touch files in build cache even when not copied.")]
	[XmlIgnore]
	public bool AlwaysTouchCache
	{
		get
		{
			return _AlwaysTouchCache;
		}
		set
		{
			_AlwaysTouchCache = value;
		}
	}

	[Description("Enables metrics reporting")]
	[XmlIgnore]
	[OptionalCommandLineOption("mr")]
	public bool MetricsReporting
	{
		get
		{
			return _MetricsReporting;
		}
		set
		{
			_MetricsReporting = value;
		}
	}

	[XmlIgnore]
	[OptionalCommandLineOption("oof")]
	[Description("Uses the old asset output format with three separate files")]
	public bool OldOutputFormat
	{
		get
		{
			return false;
		}
		set
		{
			_OldOutputFormat = value;
		}
	}

	[XmlAttribute("dataRoot")]
	[OptionalCommandLineOption("dr")]
	[Description("Directory used as a root for all stream XML files")]
	public string DataRoot
	{
		get
		{
			return _DataRoot;
		}
		set
		{
			_DataRoot = value;
		}
	}

	[XmlIgnore]
	[Description("Trace level for used for output")]
	[OptionalCommandLineOption("tl", 0, 9)]
	public int TraceLevel
	{
		get
		{
			return _TraceLevel;
		}
		set
		{
			_TraceLevel = value;
		}
	}

	[Description("Error level for reporting")]
	[XmlIgnore]
	[OptionalCommandLineOption("el", 0, 1)]
	public int ErrorLevel
	{
		get
		{
			return _ErrorLevel;
		}
		set
		{
			_ErrorLevel = value;
		}
	}

	[OptionalCommandLineOption("bcn")]
	[Description("Name of build configuration to use")]
	[XmlIgnore]
	public string BuildConfigurationName
	{
		get
		{
			return _BuildConfigurationName;
		}
		set
		{
			_BuildConfigurationName = value;
		}
	}

	[Description("Pause after build is complete if errors occurred")]
	[XmlIgnore]
	[OptionalCommandLineOption("poe")]
	public bool PauseOnError
	{
		get
		{
			return _PauseOnError;
		}
		set
		{
			_PauseOnError = value;
		}
	}

	[OptionalCommandLineOption("sf")]
	[XmlIgnore]
	[Description("Enable single file mode")]
	public bool SingleFile
	{
		get
		{
			return _SingleFile;
		}
		set
		{
			_SingleFile = value;
		}
	}

	[XmlIgnore]
	[Description("Prevents updating the session cache (used for debugging)")]
	[OptionalCommandLineOption("fsc")]
	public bool FreezeSessionCache
	{
		get
		{
			return _FreezeSessionCache;
		}
		set
		{
			_FreezeSessionCache = value;
		}
	}

	[Description("Enables linked streams")]
	[XmlIgnore]
	[OptionalCommandLineOption("ls")]
	public bool LinkedStreams
	{
		get
		{
			return _LinkedStreams;
		}
		set
		{
			_LinkedStreams = value;
		}
	}

	[OptionalCommandLineOption("iod")]
	[Description("Directory for intermediate files")]
	[XmlIgnore]
	public string IntermediateOutputDirectory
	{
		get
		{
			return _IntermediateOutputDirectory;
		}
		set
		{
			_IntermediateOutputDirectory = value;
		}
	}

	[Description("Generates intermediate XML files for testing purposes")]
	[XmlIgnore]
	[OptionalCommandLineOption("oix")]
	public bool OutputIntermediateXml
	{
		get
		{
			return _OutputIntermediateXml;
		}
		set
		{
			_OutputIntermediateXml = value;
		}
	}

	[Description("Creates a new window for text output")]
	[OptionalCommandLineOption("gui")]
	[XmlIgnore]
	public bool GuiMode
	{
		get
		{
			return _GuiMode;
		}
		set
		{
			_GuiMode = value;
		}
	}

	[Description("Forces the slow asset and cdata cleanup")]
	[OptionalCommandLineOption("slowclean")]
	[XmlIgnore]
	public bool ForceSlowCleanup
	{
		get
		{
			return _ForceSlowCleanup;
		}
		set
		{
			_ForceSlowCleanup = value;
		}
	}

	[OptionalCommandLineOption("od")]
	[Description("Output directory for generated data")]
	[XmlIgnore]
	public string OutputDirectory
	{
		get
		{
			return _OutputDirectory;
		}
		set
		{
			_OutputDirectory = value;
		}
	}

	[XmlIgnore]
	[OrderedCommandLineOption(0)]
	[DisplayName("input_path")]
	[Description("XML file to process")]
	public string InputPath
	{
		get
		{
			return _InputPath;
		}
		set
		{
			_InputPath = value;
		}
	}

	[XmlIgnore]
	public string[] DataPaths
	{
		get
		{
			return _DataPaths;
		}
		set
		{
			_DataPaths = value;
		}
	}

	[XmlIgnore]
	public string[] ArtPaths
	{
		get
		{
			return _ArtPaths;
		}
		set
		{
			_ArtPaths = value;
		}
	}

	[XmlIgnore]
	public string[] AudioPaths
	{
		get
		{
			return _AudioPaths;
		}
		set
		{
			_AudioPaths = value;
		}
	}

	[XmlIgnore]
	public string[] ProcessedMonitorPaths
	{
		get
		{
			return _ProcessedMonitorPaths;
		}
		set
		{
			_ProcessedMonitorPaths = value;
		}
	}

	[XmlArray("plugins")]
	public PluginDescriptor[] Plugins
	{
		get
		{
			return _Plugins;
		}
		set
		{
			_Plugins = value;
		}
	}

	[XmlArray("verifiers")]
	public PluginDescriptor[] VerifierPlugins
	{
		get
		{
			return _VerifierPlugins;
		}
		set
		{
			_VerifierPlugins = value;
		}
	}

	[XmlArray("buildConfigurations")]
	public BuildConfiguration[] BuildConfigurations
	{
		get
		{
			return _BuildConfigurations;
		}
		set
		{
			_BuildConfigurations = value;
		}
	}

	[Description("Default search paths for ART: path alias")]
	[XmlAttribute("defaultArtPaths")]
	[OptionalCommandLineOption("art")]
	public string DefaultArtPaths
	{
		get
		{
			return _DefaultArtPaths;
		}
		set
		{
			_DefaultArtPaths = value;
		}
	}

	[OptionalCommandLineOption("audio")]
	[Description("Default search paths for AUDIO: path alias")]
	[XmlAttribute("defaultAudioPaths")]
	public string DefaultAudioPaths
	{
		get
		{
			return _DefaultAudioPaths;
		}
		set
		{
			_DefaultAudioPaths = value;
		}
	}

	[OptionalCommandLineOption("data")]
	[Description("Default search paths for DATA: path alias")]
	[XmlAttribute("defaultDataPaths")]
	public string DefaultDataPaths
	{
		get
		{
			return _DefaultDataPaths;
		}
		set
		{
			_DefaultDataPaths = value;
		}
	}

	[Description("Additional paths which should be monitored for changes in persistent mode")]
	[XmlAttribute("monitorPaths")]
	[OptionalCommandLineOption("mp")]
	public string MonitorPaths
	{
		get
		{
			return _MonitorPaths;
		}
		set
		{
			_MonitorPaths = value;
		}
	}

	[XmlIgnore]
	public string Postfix
	{
		get
		{
			return _Postfix;
		}
		set
		{
			_Postfix = value;
		}
	}

	[XmlIgnore]
	public string StreamPostfix
	{
		get
		{
			return _StreamPostfix;
		}
		set
		{
			_StreamPostfix = value;
		}
	}

	[XmlIgnore]
	public bool BigEndian
	{
		get
		{
			return _BigEndian;
		}
		set
		{
			_BigEndian = value;
		}
	}

	[XmlIgnore]
	[OptionalCommandLineOption("ss")]
	[Description("Sort assets in a manner that is stable, but slower")]
	public bool StableSort
	{
		get
		{
			return _StableSort;
		}
		set
		{
			_StableSort = value;
		}
	}

	[Description("Base stream upon which to do a patch")]
	[XmlIgnore]
	[OptionalCommandLineOption("bps")]
	public string BasePatchStream
	{
		get
		{
			return _BasePatchStream;
		}
		set
		{
			_BasePatchStream = value;
		}
	}

	[OptionalCommandLineOption("pc")]
	[Description("If true, referenced streams will not be compiled if their .manifest output is available")]
	[XmlAttribute("usePrecompiled")]
	public bool UsePrecompiled
	{
		get
		{
			return _UsePrecompiled;
		}
		set
		{
			_UsePrecompiled = value;
		}
	}

	[Description("If true, generates a .version file for each stream containing the stream suffix used")]
	[XmlAttribute("versionFiles")]
	[OptionalCommandLineOption("vf")]
	public bool VersionFiles
	{
		get
		{
			return _VersionFiles;
		}
		set
		{
			_VersionFiles = value;
		}
	}

	[OptionalCommandLineOption("cpf")]
	[Description("If specified, appends this postfix to the configuration-defined stream postfix.  Useful for versioning.")]
	public string CustomPostfix
	{
		get
		{
			return _CustomPostfix;
		}
		set
		{
			_CustomPostfix = value;
		}
	}

	[Description("If true, BAB runs as a background process to greatly reduce load/shutdown times")]
	[OptionalCommandLineOption("res,pers")]
	[XmlAttribute("residentBab")]
	public bool Resident
	{
		get
		{
			return false;
		}
		set
		{
			_Resident = value;
		}
	}

	[XmlAttribute("streamHints")]
	[Description("If true, the stream hints saved in the session cache will be used to only build the streams that have dirty assets in them.")]
	[OptionalCommandLineOption("sh")]
	public bool StreamHints
	{
		get
		{
			return false;
		}
		set
		{
			_EnableStreamHints = value;
		}
	}

	public string AssetNamespace
	{
		get
		{
			return _AssetNamespace;
		}
		set
		{
			_AssetNamespace = value;
		}
	}

	[Description("Specifies whether to compile an asset report")]
	[OptionalCommandLineOption("oar")]
	[XmlAttribute("OutputAssetReport")]
	public bool OutputAssetReport
	{
		get
		{
			return _OutputAssetReport;
		}
		set
		{
			_OutputAssetReport = value;
		}
	}

	[OptionalCommandLineOption("osh")]
	[XmlAttribute("OutputStringHashes")]
	public bool OutputStringHashes
	{
		get
		{
			return _OutputStringHashes;
		}
		set
		{
			_OutputStringHashes = value;
		}
	}

	[XmlArray("StringHashBins")]
	public StringHashBinDescriptor[] StringHashBinDescriptors
	{
		get
		{
			return _StringHashBinDescriptors;
		}
		set
		{
			_StringHashBinDescriptors = value;
		}
	}

	public object Clone()
	{
		return MemberwiseClone();
	}
}
