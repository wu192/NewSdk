using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using EALA.Metrics;

namespace BinaryAssetBuilder.Core;

[Serializable]
public class SessionCache : ISessionCache
{
	private class CurrentState
	{
		public IDictionary<string, FileItem> Files;

		public DateTime Created;

		public uint AssetCompilersVersion;

		public string SchemaIdentity;

		public void FromScratch()
		{
			Files = new SortedDictionary<string, FileItem>();
			Created = DateTime.Now;
			AssetCompilersVersion = 0u;
		}

		public void FromLast(LastState last)
		{
			using var performance = PerformanceScope.Measure("cache.from-last");
			Files = new SortedDictionary<string, FileItem>();
			Created = last.Created;
			AssetCompilersVersion = last.AssetCompilersVersion;
			SchemaIdentity = last.SchemaIdentity;
			FileItem[] files = last.Files;
			foreach (FileItem fileItem in files)
			{
				string text = fileItem.HashItem.Path.ToLower();
				string postfix = GetPostfix(fileItem.HashItem.BuildConfiguration, fileItem.HashItem.TargetPlatform);
				Files[text + postfix] = fileItem;
			}
		}
	}

	[Serializable]
	private class LastState
	{
		public string SchemaIdentity;
		private FileItem[] _files;

		private uint _version;

		private uint _assetCompilersVersion;

		private DateTime _created;

		private uint _documentProcessorVersion;

		public FileItem[] Files
		{
			get
			{
				return _files;
			}
			set
			{
				_files = value;
			}
		}

		public uint Version
		{
			get
			{
				return _version;
			}
			set
			{
				_version = value;
			}
		}

		public uint AssetCompilersVersion => _assetCompilersVersion;

		public DateTime Created
		{
			get
			{
				return _created;
			}
			set
			{
				_created = value;
			}
		}

		public uint DocumentProcessorVersion
		{
			get
			{
				return _documentProcessorVersion;
			}
			set
			{
				_documentProcessorVersion = value;
			}
		}

		public void FromCurrent(CurrentState current)
		{
			if (current.Files.Count > 0)
			{
				List<FileItem> list = new List<FileItem>();
				foreach (FileItem value in current.Files.Values)
				{
					if (value.HashItem.Exists)
					{
						list.Add(value);
					}
				}
				Files = list.ToArray();
			}
			else
			{
				Files = null;
			}
			Created = current.Created;
			Version = 18u;
			DocumentProcessorVersion = 11u;
			_assetCompilersVersion = current.AssetCompilersVersion;
			SchemaIdentity = current.SchemaIdentity;
		}

		public void ReadXml(XmlReader reader)
		{
			SchemaIdentity = reader.GetAttribute("schema");
			reader.MoveToAttribute("d");
			string[] array = reader.Value.Split(';');
			Created = Convert.ToDateTime(array[0]);
			Version = Convert.ToUInt32(array[1]);
			DocumentProcessorVersion = Convert.ToUInt32(array[2]);
			_assetCompilersVersion = Convert.ToUInt32(array[3]);
			reader.MoveToElement();
			reader.Read();
			Files = XmlHelper.ReadCollection(reader, "fic", typeof(FileItem)) as FileItem[];
			reader.Read();
		}

		public void WriteXml(XmlWriter writer)
		{
			writer.WriteStartElement("sc");
			if (SchemaIdentity != null) writer.WriteAttributeString("schema", SchemaIdentity);
			writer.WriteAttributeString("d", $"{Created};{Version};{DocumentProcessorVersion};{AssetCompilersVersion}");
			XmlHelper.WriteCollection(writer, "fic", Files);
			writer.WriteEndElement();
		}
	}

	[NonSerialized]
	private const uint CacheVersion = 18u;

	[NonSerialized]
	private Tracer _Tracer = Tracer.GetTracer("SessionCache", "Provides caching functionality");

	[NonSerialized]
	private CurrentState _Current;

	private LastState _Last;

	private List<string> _dirtyStreams;

	private string _cacheFileName;

	private TimeSpan Age => DateTime.Now - _Last.Created;

	public virtual List<string> DirtyStreams => _dirtyStreams;

	public virtual string CacheFileName => _cacheFileName;

	// Initialization can reject an obsolete serialized session without throwing.
	public bool HasCachedState => _Last != null;

	public bool HasChanges { get; private set; }
	public bool LoadedCompressed { get; private set; }
	private bool _ReadOnlySource;

	public void LoadCacheFrom(string source, string output)
	{
		_ReadOnlySource = true;
		try { LoadCache(source); }
		finally { _ReadOnlySource = false; _cacheFileName = output; }
	}

	public virtual uint AssetCompilersVersion
	{
		get
		{
			return _Current.AssetCompilersVersion;
		}
		set
		{
			AssetCompilersVersionChanged |= _Current.AssetCompilersVersion != value;
			if (_Current.AssetCompilersVersion != value) HasChanges = true;
			_Current.AssetCompilersVersion = value;
		}
	}

	public bool AssetCompilersVersionChanged { get; private set; }

	public virtual void LoadCache(string sessionCachePath)
	{
		LoadedCompressed = !string.IsNullOrEmpty(sessionCachePath) && File.Exists(sessionCachePath + ".deflate");
		if (!string.IsNullOrEmpty(sessionCachePath))
		{
			try
			{
				if (File.Exists(sessionCachePath + ".deflate"))
				{
					_Tracer.TraceInfo("Loading compressed XML cache");
					using Stream stream = new FileStream(sessionCachePath + ".deflate", FileMode.Open, FileAccess.Read, FileShare.Read);
					DeflateStream input = new DeflateStream(stream, CompressionMode.Decompress);
					XmlReader xmlReader = XmlReader.Create(input);
					xmlReader.Read();
					_Last = new LastState();
					_Last.ReadXml(xmlReader);
				}
				else if (File.Exists(sessionCachePath))
				{
					_Tracer.TraceInfo("Loading XML cache");
					using Stream input2 = new FileStream(sessionCachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
					XmlReader xmlReader2 = XmlReader.Create(input2);
					xmlReader2.Read();
					_Last = new LastState();
					_Last.ReadXml(xmlReader2);
				}
			}
			catch (Exception ex)
			{
				try
				{
					if (!_ReadOnlySource) File.Move(sessionCachePath, sessionCachePath + ".corrupt");
				}
				catch
				{
				}
				try
				{
					if (!_ReadOnlySource) File.Move(sessionCachePath + ".deflate", sessionCachePath + ".deflate.corrupt");
				}
				catch
				{
				}
				_Tracer.TraceInfo("Session cache file {1} could not be opened: {0} \nPlease rebuild", ex.Message, sessionCachePath);
				throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Session cache could not be opened. \nPlease rebuild");
			}
		}
		_cacheFileName = sessionCachePath;
		if (_Last != null)
		{
			_Tracer.TraceInfo("Session cache age is {0} days, {1} hours, {2} minutes.", Age.Days, Age.Hours, Age.Minutes);
		}
	}

	public virtual void InitializeCache(List<string> knownChangedFiles)
	{
		InitializeCache(knownChangedFiles, null);
	}

	public void InitializeCache(List<string> knownChangedFiles, string schemaIdentity)
	{
		using var performance = PerformanceScope.Measure("cache.initialize");
		// Completed documents can skip XSD validation while plugin TypeHash /
		// ProcessingHash stay constant. Revalidate changed schema constraints;
		// original BAB imports have no schema attribute.
		if (_Last?.SchemaIdentity != null && schemaIdentity != null && _Last.SchemaIdentity != schemaIdentity)
		{
			PerformanceScope.Count("cache.session.schemaChanged");
			_Last = null;
		}
		HasChanges = false;
		AssetCompilersVersionChanged = false;
		_Current = new CurrentState();
		_dirtyStreams = new List<string>();
		checkFiles(knownChangedFiles);
		if (_Last != null)
		{
			_Tracer.TraceInfo("Cached session data available.");
			_Current.FromLast(_Last);
		}
		else
		{
			_Tracer.TraceInfo("Cached session data not available.");
			_Current.FromScratch();
			HasChanges = true;
			_dirtyStreams = null;
		}

		if (_Current.SchemaIdentity != schemaIdentity) HasChanges = true;
		_Current.SchemaIdentity = schemaIdentity;
	}

	private void checkFiles(List<string> knownChangedFiles)
	{
		using var performance = PerformanceScope.Measure("cache.check-files");
		bool flag = knownChangedFiles.Count == 0;
		if (_Last == null)
		{
			return;
		}
		if (_Last.Version != 18)
		{
			PerformanceScope.Count("cache.session.formatChanged");
			_Tracer.TraceInfo("Session cache outdated. Version is {0}. Expected version is {1}.", _Last.Version, 18u);
			_Last = null;
			return;
		}
		if (_Last.DocumentProcessorVersion != 11)
		{
			PerformanceScope.Count("cache.session.processorChanged");
			_Tracer.TraceInfo("DocumentProcessor version mismatch. Version is {0}. Expected version is {1}.", _Last.DocumentProcessorVersion, 11u);
			_Last = null;
			return;
		}
		_Tracer.TraceInfo("Checking {0} files for updates.", _Last.Files.Length);
		int num = 0;
		FileItem[] files = _Last.Files;
		foreach (FileItem fileItem in files)
		{
			if (fileItem.Document != null)
			{
				fileItem.Document.ResetState();
			}
			if (fileItem.HashItem != null)
			{
				fileItem.HashItem.Reset();
			}
			if (!fileItem.HashItem.IsDirty)
			{
				continue;
			}
			if (_dirtyStreams != null)
			{
				if (fileItem.Document == null || fileItem.Document.StreamHints.Count == 0)
				{
					_Tracer.TraceInfo("Building all streams because {0} has no stream hints.", fileItem.HashItem.Path);
					_dirtyStreams = null;
				}
				else
				{
					foreach (string streamHint in fileItem.Document.StreamHints)
					{
						if (!_dirtyStreams.Contains(streamHint))
						{
							_dirtyStreams.Add(streamHint);
						}
					}
				}
			}
			HasChanges = true;
			num++;
			if (!flag && !knownChangedFiles.Contains(fileItem.HashItem.Path))
			{
				throw new BinaryAssetBuilderException(ErrorCode.PathMonitor, "Change went undetected by experimental PathMonitor.  Please notify the Sage Pipeline alias. File: " + fileItem.HashItem.Path);
			}
		}
	}

	public virtual void SaveCache(bool compressed)
	{
		MakeCacheable();
		string directoryName = Path.GetDirectoryName(CacheFileName);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = CacheFileName;
		if (compressed)
		{
			text += ".deflate";
		}
		using (Stream stream = new FileStream(text + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
		{
			Stream w = stream;
			if (compressed)
			{
				w = new DeflateStream(stream, CompressionMode.Compress);
			}
			XmlTextWriter xmlTextWriter = new XmlTextWriter(w, Encoding.UTF8);
			_Last.WriteXml(xmlTextWriter);
			xmlTextWriter.Flush();
			MetricManager.Submit("BAB.SessionCacheSize", stream.Position);
			xmlTextWriter.Close();
		}
		if (File.Exists(text))
		{
			if (File.Exists(text + ".old"))
			{
				File.Delete(text + ".old");
			}
			File.Move(text, text + ".old");
		}
		File.Move(text + ".tmp", text);

	}

	public void AcceptSavedChanges(bool compressed) { HasChanges = false; LoadedCompressed = compressed; }

	public virtual bool TryGetFile(string path, string configuration, TargetPlatform platform, out FileHashItem hashItem)
	{
		FileItem documentItem = null;
		TryGetFileItem(path, configuration, platform, out documentItem, autoCreateDocument: false);
		hashItem = documentItem.HashItem;
		return hashItem.Exists;
	}

	public virtual bool TryGetDocument(string path, string configuration, TargetPlatform platform, bool autoCreateDocument, out AssetDeclarationDocument document)
	{
		FileItem documentItem = null;
		TryGetFileItem(path, configuration, platform, out documentItem, autoCreateDocument);
		document = documentItem.Document;
		return documentItem.HashItem.Exists;
	}

	public virtual void SaveDocumentToCache(string path, string configuration, TargetPlatform platform, AssetDeclarationDocument document)
	{
		path = path.ToLower();
		configuration = configuration?.ToLower();
		string key = path + GetPostfix(configuration, platform);
		if (_Current.Files.TryGetValue(key, out var value))
		{
			if (!Object.ReferenceEquals(value.Document, document) || document.CacheChanged) HasChanges = true;
			value.Document = document;
		}
	}

	// Owner-only version replacement. The previous document can remain alive in
	// execution scopes while a new version gets its own declarations and DOM.
	// This does not make the replacement cacheable or record compilation success;
	// only the completed version's SaveDocumentToCache call does that.
	public virtual bool ReplaceDocumentForExecution(string path, string configuration, TargetPlatform platform,
		AssetDeclarationDocument expected, AssetDeclarationDocument replacement)
	{
		if (replacement == null) throw new ArgumentNullException(nameof(replacement));
		string key = path.ToLower() + GetPostfix(configuration?.ToLower(), platform);
		if (!_Current.Files.TryGetValue(key, out var item) || !Object.ReferenceEquals(item.Document, expected))
			return false;
		item.Document = replacement;
		return true;
	}

	private static string GetPostfix(string configuration, TargetPlatform platform)
	{
		string text = (string.IsNullOrEmpty(configuration) ? "" : (":" + configuration));
		if (platform != TargetPlatform.Win32)
		{
			text = text + ":" + (int)platform;
		}
		return text;
	}

	private bool TryGetFileItem(string path, string configuration, TargetPlatform platform, out FileItem documentItem, bool autoCreateDocument)
	{
		path = path.ToLower();
		configuration = configuration?.ToLower();
		string key = path + GetPostfix(configuration, platform);
		documentItem = null;
		if (!_Current.Files.TryGetValue(key, out documentItem))
		{
			documentItem = new FileItem();
			documentItem.HashItem = new FileHashItem(path, configuration, platform);
			if (documentItem.HashItem.Exists)
			{
				_Current.Files[key] = documentItem;
				HasChanges = true;
			}
		}
		if (autoCreateDocument && documentItem.Document == null)
		{
			documentItem.Document = new AssetDeclarationDocument();
			HasChanges = true;
		}
		return documentItem.HashItem.Exists;
	}

	private void MakeCacheable()
	{
		_Last = new LastState();
		_Last.FromCurrent(_Current);
	}
}

