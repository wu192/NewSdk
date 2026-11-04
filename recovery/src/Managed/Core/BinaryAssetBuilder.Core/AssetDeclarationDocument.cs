using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
#if NEWSdk_MONOLITHIC
using System.Linq;
#endif
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Xml.XPath;
using BinaryAssetBuilder.Utility;
using EALA.Hash;

namespace BinaryAssetBuilder.Core;

[Serializable]
public class AssetDeclarationDocument : IXmlSerializable
{
	private class XmlNodeWithMetaData
	{
		public XmlNode Node;

		public int LineNumber;

		public XmlNodeWithMetaData(XmlNode node, int line)
		{
			Node = node;
			LineNumber = line;
		}

		public bool Equals(XmlNodeWithMetaData other)
		{
			return Node.Equals(other.Node);
		}
	}

	private class DependencyComparer : IComparer<InstanceDeclaration>
	{
		public int Compare(InstanceDeclaration left, InstanceDeclaration right)
		{
			if (left == null || right == null)
			{
				throw new ArgumentNullException();
			}
			if (right.AllDependentInstances.Count != left.AllDependentInstances.Count)
			{
				return left.AllDependentInstances.Count.CompareTo(right.AllDependentInstances.Count);
			}
			bool flag = right.AllDependentInstances != null && right.AllDependentInstances.Contains(left.Handle);
			bool flag2 = left.AllDependentInstances != null && left.AllDependentInstances.Contains(right.Handle);
			if (flag)
			{
				if (flag2)
				{
					throw new BinaryAssetBuilderException(ErrorCode.CircularDependency, $"Illegal circular dependency found between {left} and {right}.  This should have been caught during parsing.");
				}
				return -1;
			}
			if (flag2)
			{
				return 1;
			}
			return string.Compare(left.Handle.TypeName, right.Handle.TypeName);
		}
	}

	private class TypeDepCompare : IComparer<InstanceDeclaration>
	{
		private IDictionary<uint, int> _TypeDependencies;

		public int Compare(InstanceDeclaration left, InstanceDeclaration right)
		{
			int num = _TypeDependencies[left.Handle.TypeId].CompareTo(_TypeDependencies[right.Handle.TypeId]);
			if (num == 0)
			{
				int num2 = string.Compare(left.Handle.TypeName, right.Handle.TypeName);
				if (num2 == 0)
				{
					return string.Compare(left.Handle.InstanceName, right.Handle.InstanceName);
				}
				return num2;
			}
			return num;
		}

		public TypeDepCompare(IDictionary<uint, int> typeDependencies)
		{
			_TypeDependencies = typeDependencies;
		}
	}

	[Serializable]
	private class LastState
	{
		public uint DocumentHash;

		public uint DependentFileHash;

		public List2<string> DependentFiles;

		public List2<string> UiSourceDirectories;

		public uint UiCompilerHash;

		public uint IncludePathHash;

		public List2<InclusionItem> InclusionItems;

		public List2<OutputAsset> OutputAssets;

		public List2<Definition> SelfDefines;

		public List2<InstanceDeclaration> SelfInstances;

		public List2<DefinitionPair> UsedDefines;

		public List2<string> StreamHints;

		public LastState(CurrentState current)
		{
			DocumentHash = current.DocumentHash;
			IncludePathHash = current.IncludePathHash;
			InclusionItems = ((current.InclusionItems.Count > 0) ? new List2<InclusionItem>(current.InclusionItems) : null);
			DependentFileHash = current.DependentFileHash;
			DependentFiles = ((current.DependentFiles.Count > 0) ? new List2<string>(current.DependentFiles) : null);
			UiSourceDirectories = current.UiSourceDirectories.Count > 0 ? new List2<string>(current.UiSourceDirectories) : null;
			UiCompilerHash = current.UiCompilerHash;
			OutputAssets = ((current.OutputAssets.Count > 0) ? new List2<OutputAsset>(current.OutputAssets) : null);
			SelfDefines = ((current.SelfDefines.Count > 0) ? new List2<Definition>(current.SelfDefines) : null);
			SelfInstances = ((current.SelfInstances.Count > 0) ? new List2<InstanceDeclaration>(current.SelfInstances) : null);
			List2<DefinitionPair> list = new List2<DefinitionPair>();
			foreach (KeyValuePair<string, string> usedDefine in current.UsedDefines)
			{
				list.Add(new DefinitionPair
				{
					Name = usedDefine.Key,
					EvaluatedValue = usedDefine.Value
				});
				UsedDefines = new List2<DefinitionPair>(list);
			}
			StreamHints = ((current.StreamHints.Count > 0) ? new List2<string>(current.StreamHints) : null);
		}

		public LastState(XmlReader reader)
		{
			string uiCompiler = reader.GetAttribute("uiv");
			if (uiCompiler != null) UiCompilerHash = Convert.ToUInt32(uiCompiler);
			reader.MoveToAttribute("d");
			string[] array = reader.Value.Split(';');
			DocumentHash = Convert.ToUInt32(array[0]);
			DependentFileHash = Convert.ToUInt32(array[1]);
			IncludePathHash = Convert.ToUInt32(array[2]);
			reader.Read();
			if (reader.IsStartElement())
			{
				ReadOldStrings(reader);
				object obj = XmlHelper.ReadStringArrayElement(reader, "sf");
				DependentFiles = ((obj == null) ? null : new List2<string>(obj as string[]));
				obj = XmlHelper.ReadStringArrayElement(reader, "uidir");
				UiSourceDirectories = obj == null ? null : new List2<string>(obj as string[]);
				obj = XmlHelper.ReadStringArrayElement(reader, "dsf");
				StreamHints = ((obj == null) ? null : new List2<string>(obj as string[]));
				obj = XmlHelper.ReadCollection(reader, "iic", typeof(InclusionItem));
				InclusionItems = ((obj == null) ? null : new List2<InclusionItem>(obj as InclusionItem[]));
				obj = XmlHelper.ReadCollection(reader, "oac", typeof(OutputAsset));
				OutputAssets = ((obj == null) ? null : new List2<OutputAsset>(obj as OutputAsset[]));
				obj = XmlHelper.ReadCollection(reader, "sdc", typeof(Definition));
				SelfDefines = ((obj == null) ? null : new List2<Definition>(obj as Definition[]));
				obj = XmlHelper.ReadCollection(reader, "sic", typeof(InstanceDeclaration));
				SelfInstances = ((obj == null) ? null : new List2<InstanceDeclaration>(obj as InstanceDeclaration[]));
				obj = XmlHelper.ReadCollection(reader, "udc", typeof(DefinitionPair));
				UsedDefines = ((obj == null) ? null : new List2<DefinitionPair>(obj as DefinitionPair[]));
				reader.Read();
			}
		}

		private void ReadOldStrings(XmlReader reader)
		{
			string[] array = XmlHelper.ReadStringArrayElement(reader, "s");
			string[] array2 = XmlHelper.ReadStringArrayElement(reader, "p");
			if (array != null)
			{
				string[] array3 = array;
				foreach (string str in array3)
				{
					HashProvider.RecordHash("STRINGHASH", str);
				}
			}
			if (array2 != null)
			{
				string[] array4 = array2;
				foreach (string str2 in array4)
				{
					HashProvider.RecordHash("POID", str2);
				}
			}
		}

		public void WriteXml(XmlWriter writer)
		{
			writer.WriteStartElement("ad");
			writer.WriteAttributeString("d", $"{DocumentHash};{DependentFileHash};{IncludePathHash}");
			if (UiCompilerHash != 0) writer.WriteAttributeString("uiv", UiCompilerHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
			XmlHelper.WriteStringArrayElement(writer, "sf", (DependentFiles == null) ? null : DependentFiles.ToArray());
			XmlHelper.WriteStringArrayElement(writer, "uidir", UiSourceDirectories == null ? null : UiSourceDirectories.ToArray());
			XmlHelper.WriteStringArrayElement(writer, "dsf", (StreamHints == null) ? null : StreamHints.ToArray());
			XmlHelper.WriteCollection(writer, "iic", InclusionItems);
			XmlHelper.WriteCollection(writer, "oac", OutputAssets);
			XmlHelper.WriteCollection(writer, "sdc", SelfDefines);
			XmlHelper.WriteCollection(writer, "sic", SelfInstances);
			XmlHelper.WriteCollection(writer, "udc", UsedDefines);
			writer.WriteEndElement();
		}
	}

	private class CurrentState
	{
		public uint DocumentHash;

		public string SourcePath;

		public string SourceDirectory;

		public string SourcePathFromRoot;

		public string LogicalSourcePath;

		public uint DependentFileHash;

		public uint IncludePathHash;

		public uint OutputChecksum;

		public bool IsLoaded;
		public bool SchemaValidated;

		public bool Processing;

		public string ChangeReason;

		public bool Changed;

		public DocumentState State;

		public List2<Definition> SelfDefines;

		public InstanceSet SelfInstances;

		public List2<string> DependentFiles;

		public List2<string> UiSourceDirectories;

		public uint UiCompilerHash;

		public List2<InclusionItem> InclusionItems;

		public IDictionary<string, string> UsedDefines;

		public bool VerificationErrors;

		public FileHashItem HashItem;

		public List2<InstanceDeclaration> OutputInstances = new List2<InstanceDeclaration>();

		public DefinitionSet AllDefines = new DefinitionSet();

		public InstanceSet Instances = new InstanceSet();

		public InstanceSet AllInstances = new InstanceSet();

		public InstanceSet TentativeInstances = new InstanceSet();

		public InstanceSet ReferenceInstances = new InstanceSet();

		public StringDictionary Tags = new StringDictionary();

		public XmlDocument XmlDocument;

		public XmlNamespaceManager NamespaceManager;

		public DocumentProcessor DocumentProcessor;

		public List2<OutputAsset> OutputAssets = new List2<OutputAsset>();

		public List2<string> StreamHints;

		public LinkedList<XmlNodeWithMetaData> NodeSourceInfoSet;

		public IDictionary<InstanceHandle, InstanceDeclaration> OutputInstanceSet;

		public XmlReader XmlReader;

		public string ConfigurationName;

		public List2<OutputAsset> LastOutputAssets;

		public uint LastDocumentHash;

		public uint LastDependentFileHash;

		public uint LastIncludePathHash;

		public CurrentState()
		{
		}

		public CurrentState(DocumentProcessor documentProcessor, FileHashItem hashItem, string logicalSourcePath, string configuration)
		{
			DocumentProcessor = documentProcessor;
			HashItem = hashItem;
			SourcePath = hashItem.Path;
			LogicalSourcePath = logicalSourcePath;
			SourceDirectory = Path.GetDirectoryName(SourcePath);
			DocumentHash = HashItem.Hash;
			ConfigurationName = configuration;
			string dataRoot = FileNameResolver.GetDataRoot(SourcePath);
			if (!string.IsNullOrEmpty(dataRoot))
			{
				string text = FileNameResolver.GetSafeRelativePath(dataRoot, SourcePath);
				string directoryName = Path.GetDirectoryName(text);
				SourcePathFromRoot = Path.Combine(directoryName, Path.GetFileNameWithoutExtension(text));
				LogicalSourcePath = "DATA:" + text.Replace('\\', '/');
			}
			else
			{
				SourcePathFromRoot = Path.GetFileNameWithoutExtension(SourcePath);
			}
		}

		public void FromLast(AssetDeclarationDocument document, LastState last)
		{
			InclusionItems = ((last.InclusionItems != null) ? new List2<InclusionItem>(last.InclusionItems) : new List2<InclusionItem>());
			DependentFiles = ((last.DependentFiles != null) ? new List2<string>(last.DependentFiles) : new List2<string>());
			UiSourceDirectories = last.UiSourceDirectories != null ? new List2<string>(last.UiSourceDirectories) : new List2<string>();
			UiCompilerHash = last.UiCompilerHash;
			StreamHints = ((last.StreamHints != null) ? new List2<string>(last.StreamHints) : new List2<string>());
			UsedDefines = new SortedDictionary<string, string>();
			if (last.UsedDefines != null)
			{
				foreach (DefinitionPair usedDefine in last.UsedDefines)
				{
					UsedDefines[usedDefine.Name] = usedDefine.EvaluatedValue;
				}
			}
			SelfDefines = ((last.SelfDefines != null) ? new List2<Definition>(last.SelfDefines) : new List2<Definition>());
			foreach (Definition selfDefine in SelfDefines)
			{
				selfDefine.Document = document;
			}
			SelfInstances = ((last.SelfInstances != null) ? new InstanceSet(document, last.SelfInstances) : new InstanceSet());
			Changed = last.DocumentHash != DocumentHash;
			LastOutputAssets = last.OutputAssets;
			LastDocumentHash = last.DocumentHash;
			LastIncludePathHash = last.IncludePathHash;
			LastDependentFileHash = last.DependentFileHash;
			State = DocumentState.Shallow;
		}

		public void FromScratch()
		{
			InclusionItems = new List2<InclusionItem>();
			DependentFiles = new List2<string>();
			UiSourceDirectories = new List2<string>();
			UiCompilerHash = 0;
			UsedDefines = new SortedDictionary<string, string>();
			SelfDefines = new List2<Definition>();
			SelfInstances = new InstanceSet();
			StreamHints = new List2<string>();
			State = DocumentState.None;
		}
	}

	private enum LoadType
	{
		InplaceLoad,
		FromScratch
	}

	private static Tracer _Tracer = Tracer.GetTracer("DocumentProcessor", "Provides XML processing functionality");

	private LastState _Last;

	[NonSerialized]
	private CurrentState _Current;

	[NonSerialized]
	private bool _processing;

	[NonSerialized]
	public bool ReloadedForInheritance;

	public FileHashItem HashItem => _Current.HashItem;

	public List2<OutputAsset> LastOutputAssets => _Current.LastOutputAssets;

	public string SourcePathFromRoot => _Current.SourcePathFromRoot;

	public string SourcePath => _Current.SourcePath;

	public InstanceSet SelfInstances => _Current.SelfInstances;

	public List2<Definition> SelfDefines => _Current.SelfDefines;

	public List2<InclusionItem> InclusionItems => _Current.InclusionItems;

	public string LogicalSourcePath => _Current.LogicalSourcePath;

	public uint OutputChecksum => _Current.OutputChecksum;

	public bool IsLoaded => _Current.IsLoaded;

	public DefinitionSet AllDefines => _Current.AllDefines;

	public IDictionary<string, string> UsedDefines => _Current.UsedDefines;

	public List2<InstanceDeclaration> OutputInstances => _Current.OutputInstances;

	public InstanceSet Instances => _Current.Instances;

	public InstanceSet AllInstances => _Current.AllInstances;

	public InstanceSet TentativeInstances => _Current.TentativeInstances;

	public InstanceSet ReferenceInstances => _Current.ReferenceInstances;

	public DocumentState State
	{
		get
		{
			if (_Current != null)
			{
				return _Current.State;
			}
			return DocumentState.None;
		}
		set
		{
			_Current.State = value;
		}
	}

	public StringDictionary Tags => _Current.Tags;

	public XmlDocument XmlDocument => _Current.XmlDocument;

	public bool HasValidatedSchema => _Current != null && _Current.SchemaValidated;

	public XmlNamespaceManager NamespaceManager => _Current.NamespaceManager;

	public string SourceDirectory => _Current.SourceDirectory;

	public bool VerificationErrors => _Current.VerificationErrors;

	public List2<string> StreamHints => _Last.StreamHints;

	public bool Processing
	{
		get
		{
			return _processing;
		}
		set
		{
			_processing = value;
		}
	}

	public void WriteXml(XmlWriter writer)
	{
		if (_Last != null)
		{
			_Last.WriteXml(writer);
		}
	}

	public void ReadXml(XmlReader reader)
	{
		_Last = new LastState(reader);
	}

	public XmlSchema GetSchema()
	{
		return null;
	}

	public bool CacheChanged { get; private set; }
	// Request-local observations only; excluded from the explicit XML cache format.
	internal bool CacheInvalidated { get; private set; }
	internal bool DependencyAffected { get; private set; }
	private bool _DependencyInvalidated;

	internal void RecordDependencyAffected()
	{
		if (!PerformanceScope.CountersEnabled || DependencyAffected) return;
		DependencyAffected = true;
		PerformanceScope.Count("dependency.include.affectedDocuments");
	}

	private void RecordReload(string reason)
	{
		if (!PerformanceScope.CountersEnabled) return;
		string counter;
		switch (reason)
		{
		case "New Document": PerformanceScope.Count("cache.document.new"); return;
		case "content changed": counter = "cache.invalidation.contentChanged"; break;
		case "dependent file changed": counter = "cache.invalidation.dependentFileChanged"; break;
		case "previous output missing": counter = "cache.invalidation.outputMissing"; break;
		case "type hash changed": counter = "cache.invalidation.typeChanged"; break;
		case "plugin output changed": counter = "cache.invalidation.pluginChanged"; break;
		case "inheritFrom target changed": counter = "cache.reload.inheritanceTarget"; break;
		case "inheritFrom source changed": counter = "cache.invalidation.inheritanceChanged"; break;
		case "used definitions changed": counter = "cache.invalidation.definesChanged"; break;
		default: counter = "cache.invalidation.other"; break;
		}
		PerformanceScope.Count(counter);
		// Reloading a cached inheritance source to obtain its XML is preparation,
		// not evidence that its contents or output have become invalid.
		if (reason == "inheritFrom target changed") return;
		PerformanceScope.Count("cache.invalidation.reloads");
		if (!CacheInvalidated) {
			CacheInvalidated = true;
			PerformanceScope.Count("cache.invalidation.documents");
			PerformanceScope.Count("cache.invalidation.selfInstances", _Current.SelfInstances.Count);
		}
		if (reason == "dependent file changed" || reason == "inheritFrom source changed" || reason == "used definitions changed")
		{
			PerformanceScope.Count("dependency.propagation.reloads");
			if (!_DependencyInvalidated) {
				_DependencyInvalidated = true;
				PerformanceScope.Count("dependency.propagation.documents");
				PerformanceScope.Count("dependency.propagation.selfInstances", _Current.SelfInstances.Count);
			}
		}
	}

	public void SetChanged(string reason)
	{
		CacheChanged = true;
		_Current.ChangeReason = ((reason == null && _Current.ChangeReason == null) ? string.Empty : reason);
		_Current.Changed = true;
	}

	public void Reset()
	{
		_Current.XmlDocument = null;
		if (ReloadedForInheritance)
		{
			State = DocumentState.Complete;
			ReloadedForInheritance = false;
		}
	}

	public AssetDeclarationDocument()
	{
	}

	// Private worker view has no semantic processor or cache.
	public static AssetDeclarationDocument CreateBackendView(XmlNamespaceManager namespaces, bool hasValidatedSchema = false)
	{
		if (namespaces == null) throw new ArgumentNullException(nameof(namespaces));
		return new AssetDeclarationDocument { _Current = new CurrentState { NamespaceManager = namespaces, SchemaValidated = hasValidatedSchema } };
	}

	public AssetDeclarationDocument(DocumentProcessor documentProcessor, FileHashItem hashItem, string logicalPath, string configuration)
	{
		Open(documentProcessor, hashItem, logicalPath, configuration);
	}

	public void Reinitialize(OutputManager outputManager)
	{
		using var performance = PerformanceScope.Measure("core.cache.reinitialize");
		if (_Current.HashItem == null)
		{
			throw new BinaryAssetBuilderException(ErrorCode.DependencyCacheFailure, "File hashing information for cached document missing: {0}", _Current.SourcePath);
		}
		string text = null;
		if (_Current.LastDocumentHash != _Current.DocumentHash)
		{
			text = "content changed";
		}
#if NEWSdk_MONOLITHIC
		else if (_Current.UiCompilerHash != 0 && _Current.UiCompilerHash != UiCompilerInputHash())
		{
			text = "plugin output changed";
		}
#endif
		else
		{
			UpdateDocumentHashes();
			if (_Current.LastDependentFileHash != _Current.DependentFileHash)
			{
				text = "dependent file changed";
			}
			else
			{
				foreach (InstanceDeclaration selfInstance in _Current.SelfInstances)
				{
#if NEWSdk_MONOLITHIC
					// Older hosts treated .fla as an opaque AptAptData blob. Upgrade
					// those cached declarations through the source expansion stage.
					if (_Current.UiCompilerHash == 0 && selfInstance.Handle.TypeName == "AptAptData" &&
						selfInstance.ReferencedFiles.Any(file => String.Equals(Path.GetExtension(file), ".fla", StringComparison.OrdinalIgnoreCase)))
					{
						text = "plugin output changed";
						break;
					}
#endif
					BinaryAsset binaryAsset = outputManager.GetBinaryAsset(selfInstance, isOutputAsset: false);
					if (binaryAsset != null && binaryAsset.GetLocation(AssetLocation.All, AssetLocationOption.None) == AssetLocation.None)
					{
						text = "previous output missing";
						break;
					}
					ExtendedTypeInformation extendedTypeInformation = _Current.DocumentProcessor.Plugins.GetExtendedTypeInformation(selfInstance.Handle.TypeId);
					if (selfInstance.Handle.TypeHash != extendedTypeInformation.TypeHash)
					{
						text = "type hash changed";
						break;
					}
					if (!_Current.DocumentProcessor.Plugins.IsProcessingHashCurrent(selfInstance,
						(_Current.DocumentProcessor.Cache as SessionCache)?.AssetCompilersVersionChanged == true))
					{
						text = "plugin output changed";
						break;
					}
				}
			}
		}
		if (text != null)
		{
			Load(text);
		}
		else if (_Current.LastIncludePathHash != _Current.IncludePathHash)
		{
			foreach (InclusionItem inclusionItem in _Current.InclusionItems)
			{
				inclusionItem.PhysicalPath = FileNameResolver.ResolvePath(SourceDirectory, inclusionItem.LogicalPath).ToLower();
			}
		}
		_Current.StreamHints.Clear();
	}

	public void ReloadIfRequired(InstanceHandleSet requiredOverrideSources)
	{
		string text = null;
		InstanceHandleSet instanceHandleSet = new InstanceHandleSet();
		foreach (InstanceHandle requiredOverrideSource in requiredOverrideSources)
		{
			if (_Current.SelfInstances.Contains(requiredOverrideSource))
			{
				instanceHandleSet.Add(requiredOverrideSource);
			}
		}
		if (instanceHandleSet.Count > 0)
		{
			if (text == null)
			{
				text = "inheritFrom target changed";
			}
			foreach (InstanceHandle item in instanceHandleSet)
			{
				requiredOverrideSources.Remove(item);
			}
		}
		if (text != null)
		{
			if (State == DocumentState.Complete && !IsLoaded)
			{
				PrevalidatedLoad(text);
			}
			else
			{
				Load(text);
			}
		}
	}

	public void UpdateOutputAssets(OutputManager outputManager)
	{
		foreach (BinaryAsset value in outputManager.Assets.Values)
		{
			if (value.IsOutputAsset)
			{
				_Current.OutputAssets.Add(new OutputAsset(value));
			}
		}
	}

	private void UpdateDocumentHashes()
	{
		using var performance = PerformanceScope.Measure("core.dependencies.files");
		if (_Current.DependentFiles.Count == 0 && _Current.UiSourceDirectories.Count == 0)
		{
			_Current.DependentFileHash = 0u;
		}
		else
		{
			using MemoryStream memoryStream = new MemoryStream();
			BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
			foreach (string dependentFile in _Current.DependentFiles)
			{
				PerformanceScope.Count("dependency.file.checks");
				FileHashItem item = null;
				if (TryGetFileHashItem(dependentFile, out item))
				{
					binaryWriter.Write(HashProvider.GetTextHash(item.Path.ToLower()));
					binaryWriter.Write(item.Hash);
				}
			}
			// SourceProject discovers all AS files under the movie folder. The
			// inventory is a dependency too: adding a class or timeline sidecar
			// must invalidate a previously cached FLA group.
			foreach (string directory in _Current.UiSourceDirectories)
			{
				binaryWriter.Write(directory);
				if (Directory.Exists(directory))
					foreach (string file in EnumerateUiSources(directory)) binaryWriter.Write(file.ToLowerInvariant());
			}
			byte[] array = memoryStream.ToArray();
			if (array.Length > 0)
			{
				_Current.DependentFileHash = FastHash.GetHashCode(array);
			}
			else
			{
				_Current.DependentFileHash = 0u;
			}
		}
		if (_Current.InclusionItems.Count == 0)
		{
			_Current.IncludePathHash = 0u;
			return;
		}
		_Current.IncludePathHash = (uint)_Current.InclusionItems.Count;
		foreach (InclusionItem inclusionItem in _Current.InclusionItems)
		{
			FileHashItem item2 = null;
			if (TryGetFileHashItem(inclusionItem.LogicalPath, out item2))
			{
				_Current.IncludePathHash = FastHash.GetHashCode(_Current.IncludePathHash, item2.Path.ToLower());
			}
		}
	}

	public void LoadXml(bool updateDependentFiles)
	{
		using var performance = PerformanceScope.Measure("core.xml.parse");
		if (_Current.XmlDocument != null)
		{
			throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Document {0} attempted to load twice.", _Current.SourcePath);
		}
		_Current.SchemaValidated = false;
		_Current.DocumentProcessor.RecordXmlParse();
		FileNameXmlResolver xmlResolver = FileNameResolver.GetXmlResolver(SourceDirectory);
		if (!File.Exists(_Current.SourcePath))
		{
			string s = "<?xml version='1.0' encoding='UTF-8'?>\n<AssetDeclaration xmlns=\"uri:ea.com:eala:asset\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n</AssetDeclaration>";
			_Current.XmlReader = XmlReader.Create(new StringReader(s));
		}
		else
		{
			_Current.XmlReader = XmlReader.Create(_Current.SourcePath);
		}
		_Current.XmlDocument = new XmlDocument();
		XmlReader xmlReader = new Mvp.Xml.XInclude.XIncludingReader(_Current.XmlReader)
		{
			XmlResolver = xmlResolver
		};
		_Current.NodeSourceInfoSet = new LinkedList<XmlNodeWithMetaData>();
		try
		{
			bool flag = _Current.LogicalSourcePath.EndsWith(".xml");
			if (flag)
			{
				_Current.XmlDocument.NodeInserted += NodeInsertedHandler;
			}
			_Current.XmlDocument.Load(xmlReader);
			if (flag)
			{
				_Current.XmlDocument.NodeInserted -= NodeInsertedHandler;
			}
		}
		catch (XmlSchemaValidationException innerException)
		{
			throw new BinaryAssetBuilderException(innerException, ErrorCode.SchemaValidation);
		}
		catch (XmlException innerException2)
		{
			throw new BinaryAssetBuilderException(innerException2, ErrorCode.XmlFormattingError);
		}
		finally
		{
			_Current.XmlReader.Close();
		}
		if (updateDependentFiles)
		{
			foreach (PathMapItem path in xmlResolver.Paths)
			{
				_Current.DependentFiles.Add(path.SourceUri.ToLower());
			}
		}
		_Current.NamespaceManager = new XmlNamespaceManager(xmlReader.NameTable);
		_Current.NamespaceManager.AddNamespace("ea", "uri:ea.com:eala:asset");
		_Current.NamespaceManager.AddNamespace("ms", "urn:schemas-microsoft-com:xslt");
	}

	public void InplaceLoad(string reason)
	{
		RecordReload(reason);
		InternalLoad(reason, LoadType.InplaceLoad);
	}

	private void Load(string reason)
	{
		RecordReload(reason);
		_Current.FromScratch();
		InternalLoad(reason, LoadType.FromScratch);
	}

	private void InternalLoad(string reason, LoadType type)
	{
		SetChanged(reason);
		if (State == DocumentState.Shallow)
		{
			_Tracer.TraceInfo("Reloading 'file://{0}'. Reason: {1}", SourcePath, _Current.ChangeReason);
		}
		else
		{
			_Tracer.TraceInfo("Loading 'file://{0}'.", SourcePath);
		}
		LoadXml(type == LoadType.FromScratch);
		GatherUnvalidatedTags();
		GatherDefines();
		if (type != LoadType.InplaceLoad)
		{
			GatherUnvalidatedIncludes();
		}
		GatherUnvalidatedInstances();
		_Current.State = DocumentState.Loaded;
		_Current.IsLoaded = true;
	}

	private void PrevalidatedLoad(string reason)
	{
		if (_Current.State != DocumentState.Complete)
		{
			throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Prevalidation load for {0} was called because of '{1}' when document has not been previously validated", SourcePath, reason);
		}
		RecordReload(reason);
		LoadXml(updateDependentFiles: false);
		GatherUnvalidatedTags();
		GatherDefines();
		GatherUnvalidatedIncludes();
		GatherUnvalidatedInstances();
		_Current.State = DocumentState.Loaded;
		_Current.IsLoaded = true;
		ReloadedForInheritance = true;
	}

	private void NodeInsertedHandler(object sender, XmlNodeChangedEventArgs e)
	{
		IXmlLineInfo xmlLineInfo = _Current.XmlReader as IXmlLineInfo;
		if (xmlLineInfo.HasLineInfo())
		{
			_Current.NodeSourceInfoSet.AddFirst(new XmlNodeWithMetaData(e.Node, xmlLineInfo.LineNumber));
		}
	}

	public void FromLastHack()
	{
		if (_Current == null)
		{
			_Current = new CurrentState();
			_Current.FromLast(this, _Last);
		}
	}

	public void Open(DocumentProcessor documentProcessor, FileHashItem hashItem, string logicalPath, string configuration)
	{
		if (_Current == null || _Current.State != DocumentState.Complete)
		{
			_Current = new CurrentState(documentProcessor, hashItem, logicalPath, configuration);
			if (_Last != null)
			{
				_Current.FromLast(this, _Last);
			}
			else
			{
				Load("New Document");
			}
		}
	}


	// Capture the small document context read by certified backends. The DOM
	// remains shared and read-only; changing the owner's current version cannot
	// redirect an in-flight declaration to a different namespace/path context.
	public AssetDeclarationDocument CreateBackendView()
	{
		return new AssetDeclarationDocument { _Current = new CurrentState {
			NamespaceManager = _Current.NamespaceManager, SchemaValidated = _Current.SchemaValidated,
			IsLoaded = _Current.IsLoaded, XmlDocument = _Current.XmlDocument,
			SourcePath = _Current.SourcePath, SourceDirectory = _Current.SourceDirectory,
			SourcePathFromRoot = _Current.SourcePathFromRoot, LogicalSourcePath = _Current.LogicalSourcePath,
			ConfigurationName = _Current.ConfigurationName, State = _Current.State
		} };
	}

	[NonSerialized]
	private Dictionary<InstanceDeclaration, AssetBuffer> _ExpandedBuffers;

	public void ResetState()
	{
		// Match the transient state of an XML-deserialized session when the
		// saved object graph is reused by the next stream in this request.
		if (_Last?.SelfInstances != null)
			foreach (InstanceDeclaration instance in _Last.SelfInstances) instance.ResetState();
		if (_Last?.SelfDefines != null)
			foreach (Definition definition in _Last.SelfDefines) { definition.OriginalValue = null; definition.Document = null; }
		if (_Last?.InclusionItems != null)
			foreach (InclusionItem inclusion in _Last.InclusionItems) inclusion.Document = null;
		_ExpandedBuffers = null;
		_Current = null;
		CacheChanged = false;
		CacheInvalidated = DependencyAffected = false;
		_DependencyInvalidated = false;
	}

	public override string ToString()
	{
		return $"{Path.GetFileName(SourcePath)} {State}";
	}

	public void MakeCacheable()
	{
		if (_Current == null || _Last != null)
		{
			return;
		}
		_Last = new LastState(_Current);
		foreach (InstanceDeclaration selfInstance in _Current.SelfInstances)
		{
			selfInstance.MakeCacheable();
		}
	}

	public void MakeComplete()
	{
		_ExpandedBuffers = null;
		if (_Last != null)
		{
			if (_Last.IncludePathHash != _Current.IncludePathHash || (_Last.StreamHints == null ? 0 : _Last.StreamHints.Count) != _Current.StreamHints.Count) CacheChanged = true;
			else for (int i = 0; i < _Current.StreamHints.Count; i++)
				if (_Last.StreamHints[i] != _Current.StreamHints[i]) { CacheChanged = true; break; }
		}
		_Current.State = DocumentState.Complete;
		foreach (InstanceDeclaration selfInstance in SelfInstances)
		{
			if (!selfInstance.IsInheritable) selfInstance.XmlNode = null;
			selfInstance.MakeComplete();
		}
		_Last = null;
	}

	public void AddStreamsHints(string[] streams)
	{
		foreach (string item in streams)
		{
			if (!_Current.StreamHints.Contains(item))
			{
				_Current.StreamHints.Add(item);
			}
		}
	}

	public void Validate()
	{
		using var performance = PerformanceScope.Measure("core.xml.validate");
		_Current.SchemaValidated = false;
		// The processor owns one compiled schema snapshot. Validation only reads
		// it; copying its global tables into every document repeats that work.
		using (PerformanceScope.Measure("core.xml.schema.attach"))
			_Current.XmlDocument.Schemas = _Current.DocumentProcessor.SchemaSet.Schemas;
		using (PerformanceScope.Measure("core.xml.schema.validate"))
			_Current.XmlDocument.Validate(HandleValidationEvents);
		_Current.NodeSourceInfoSet.Clear();
		_Current.NodeSourceInfoSet = null;
		using (PerformanceScope.Measure("core.xml.instances.validate")) ValidateInstances();
#if NEWSdk_MONOLITHIC
		using (PerformanceScope.Measure("core.xml.ui.expand")) ExpandUiSources();
#endif
		using (PerformanceScope.Measure("core.xml.hashes")) UpdateDocumentHashes();
		_Current.SchemaValidated = true;
	}

	private bool TryGetFileHashItem(string logicalPath, out FileHashItem item)
	{
		string path = FileNameResolver.ResolvePath(SourceDirectory, logicalPath);
		return _Current.DocumentProcessor.Cache.TryGetFile(path, _Current.ConfigurationName, Settings.Current.TargetPlatform, out item);
	}

	public void HandleValidationEvents(object sender, ValidationEventArgs e)
	{
		if (e.Exception is XmlSchemaValidationException ex)
		{
			XmlNode node = ex.SourceObject as XmlNode;
			LinkedListNode<XmlNodeWithMetaData> linkedListNode = _Current.NodeSourceInfoSet.Find(new XmlNodeWithMetaData(node, 0));
			if (linkedListNode != null && linkedListNode.Value != null)
			{
				throw new BinaryAssetBuilderException(ErrorCode.SchemaValidation, "XML validation error encountered in '{0}' near line {1}:\n   {2}", SourcePath, linkedListNode.Value.LineNumber, e.Message);
			}
		}
		throw new BinaryAssetBuilderException(e.Exception, ErrorCode.SchemaValidation, "XML validation error encountered in '{0}'", SourcePath);
	}

	public void EvaluateDefinitions()
	{
		IExpressionEvaluator evaluator = ExpressionEvaluatorWrapper.GetEvaluator(this);
		foreach (Definition selfDefine in SelfDefines)
		{
			if (selfDefine.OriginalValue != null)
			{
				selfDefine.EvaluatedValue = null;
				evaluator.EvaluateDefinition(selfDefine);
			}
			Definition value = null;
			if (AllDefines.TryGetValue(selfDefine.Name, out value) && selfDefine.Document != value.Document && !selfDefine.IsOverride)
			{
				throw new BinaryAssetBuilderException(ErrorCode.DuplicateDefine, "Definition {0} defined in {1} is already defined in {2}", selfDefine.Name, selfDefine.Document.SourcePath, value.Document.SourcePath);
			}
			AllDefines[selfDefine.Name] = selfDefine;
		}
	}

	public bool ValidateCachedDefines()
	{
		using var performance = PerformanceScope.Measure("core.dependencies.defines");
		foreach (KeyValuePair<string, string> usedDefine in UsedDefines)
		{
			PerformanceScope.Count("dependency.define.checks");
			if (usedDefine.Value != AllDefines.GetEvaluatedValue(usedDefine.Key))
			{
				PerformanceScope.Count("dependency.define.changes");
				return false;
			}
		}
		return true;
	}

	public bool ValidateInheritFromSources()
	{
		using var performance = PerformanceScope.Measure("core.dependencies.inheritance");
		foreach (InstanceDeclaration selfInstance in SelfInstances)
		{
			if (selfInstance.InheritFromHandle == null)
			{
				continue;
			}
			PerformanceScope.Count("dependency.inheritance.checks");
			FindLocation location = FindLocation.None;
			InstanceDeclaration instanceDeclaration = FindInstance(selfInstance.InheritFromHandle, (selfInstance.InheritFromHandle == selfInstance.Handle) ? FindLocation.Self : FindLocation.None, out location);
			if (instanceDeclaration == null)
			{
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0} attempts to inherit from non-existing instance {1}.", selfInstance, selfInstance.InheritFromHandle);
			}
			switch (location)
			{
			case FindLocation.Tentative:
			{
				bool flag = false;
				foreach (InclusionItem inclusionItem in _Current.InclusionItems)
				{
					if (inclusionItem.Document == instanceDeclaration.Document)
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0} attempts to inherit from instance {1} which is not directly included.", selfInstance, selfInstance.InheritFromHandle);
				}
				break;
			}
			default:
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0} attempts to inherit from instance {1} which is not included by 'instance'.", selfInstance, selfInstance.InheritFromHandle);
			case FindLocation.Self:
				break;
			}
			if (instanceDeclaration.PrevalidationXmlHash != selfInstance.InheritFromXmlHash)
			{
				PerformanceScope.Count("dependency.inheritance.changes");
				return false;
			}
		}
		return true;
	}

	public void ProcessExpressions()
	{
		using var performance = PerformanceScope.Measure("core.xml.expressions");
		IExpressionEvaluator evaluator = ExpressionEvaluatorWrapper.GetEvaluator(this);
		if (evaluator == null)
		{
			return;
		}
		foreach (InstanceDeclaration selfInstance in SelfInstances)
		{
			ProcessExpressionsInNode(evaluator, selfInstance.XmlNode);
		}
	}

	private void ProcessExpressionsInNode(IExpressionEvaluator eval, XmlNode node)
	{
		if (node.Attributes != null)
		{
			foreach (XmlAttribute attribute in node.Attributes)
			{
				if (attribute.Value.Length > 0 && attribute.Value[0] == '=')
				{
					attribute.Value = eval.Evaluate(attribute.Value);
				}
			}
		}
		if (node.Value != null && node.Value.Length > 0 && node.Value[0] == '=')
		{
			node.Value = eval.Evaluate(node.Value);
		}
		if (node.ChildNodes == null)
		{
			return;
		}
		foreach (XmlNode childNode in node.ChildNodes)
		{
			ProcessExpressionsInNode(eval, childNode);
		}
	}

	private void OverrideInstance(InstanceDeclaration instance)
	{
		if (instance.InheritFromHandle != null)
		{
			XmlNode xmlNode = null;
			FindLocation location = FindLocation.None;
			InstanceDeclaration instanceDeclaration = FindInstance(instance.InheritFromHandle, (instance.InheritFromHandle == instance.Handle) ? FindLocation.Self : FindLocation.None, out location);
			if (instanceDeclaration == null)
			{
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0} attempts to inherit from or override non-existing instance {1}", instance.Handle, instance.InheritFromHandle);
			}
			if (!instanceDeclaration.IsInheritable && location != FindLocation.Self)
			{
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {1} cannot be overriden because it is not of type BaseInheritableAsset", instance.Handle, instance.InheritFromHandle);
			}
			switch (location)
			{
			case FindLocation.Tentative:
				foreach (InclusionItem inclusionItem in _Current.InclusionItems)
				{
					if (inclusionItem.Document == instanceDeclaration.Document)
					{
						xmlNode = instanceDeclaration.XmlNode;
						break;
					}
				}
				break;
			case FindLocation.Self:
				if (instanceDeclaration.PrevalidationXmlHash == 0)
				{
					OverrideInstance(instanceDeclaration);
				}
				xmlNode = instanceDeclaration.XmlNode;
				break;
			case FindLocation.None:
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0} attempts to inherit from an instance {1} which could not be found.", instance.Handle, instance.InheritFromHandle);
			default:
				throw new BinaryAssetBuilderException(ErrorCode.InheritFromError, "Instance {0}\n  in document '{1}'\n  attempts to inherit from instance {2}\n  from document '{3}'\n  which does not appear to be included by 'instance'.", instance.Handle, instance.Document.SourcePath, instanceDeclaration, instanceDeclaration.Document.SourcePath);
			}
			if (xmlNode == null)
			{
				throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Instance {0} attempts to inherit from instance {1} but source XML is missing.", instance.Handle, instance.InheritFromHandle);
			}
			XmlNode xmlNode2 = NodeJoiner.Override(_Current.DocumentProcessor.SchemaSet.Schemas, XmlDocument, xmlNode, instance.XmlNode);
			XmlNode parentNode = instance.XmlNode.ParentNode;
			parentNode.RemoveChild(instance.XmlNode);
			parentNode.AppendChild(xmlNode2);
			instance.XmlNode = xmlNode2;
			instance.InheritFromXmlHash = instanceDeclaration.PrevalidationXmlHash;
		}
		XmlNode node = instance.XmlNode;
		instance.PrevalidationXmlHash = HashProvider.GetXmlHash(ref node);
	}

	public void MergeInstances()
	{
		Instances.Clear();
		Instances.Add(SelfInstances);
		Instances.Add(AllInstances);
	}

	public void ProcessInstances(OutputManager outputManager, ref int instancesCompiledCount, ref int instancesCopiedFromCacheCount)
	{
		using var performance = PerformanceScope.Measure("core.assets");
		if (ReloadedForInheritance) return;
		var processor = _Current.DocumentProcessor;
		var execution = processor.AssetExecution;
		var scope = execution == null ? null : processor.GetExecutionDocument(this, outputManager);
		foreach (InstanceDeclaration selfInstance in SelfInstances)
		{
			if (selfInstance.XmlNode != null && (_ExpandedBuffers == null || !_ExpandedBuffers.ContainsKey(selfInstance)))
				selfInstance.HasCustomData = processor.Plugins.GetExtendedTypeInformation(selfInstance.Handle.TypeId).HasCustomData;
			BinaryAsset binaryAsset = outputManager.GetBinaryAsset(selfInstance, isOutputAsset: true);
			if (binaryAsset == null) continue;
			AssetLocation location = binaryAsset.GetLocation(AssetLocation.All, AssetLocationOption.None);
			if (execution != null)
			{
				var declaration = selfInstance;
				var version = _Current;
				string sourcePath = version.SourcePath;
				bool verificationRequired = version.IsLoaded && version.State != DocumentState.Complete;
				var verifier = verificationRequired ? processor.VerifierPlugins.GetPlugin(declaration.Handle.TypeId) : null;
				bool parallelVerification = verificationRequired && verifier is IConcurrentAssetBuilderVerifierPlugin;
				var verificationInput = parallelVerification ? declaration.CreateBackendView() : declaration;
				Func<bool> verify = verificationRequired ? (Func<bool>)(() => {
					if (parallelVerification) return verifier.VerifyInstance(verificationInput);
					bool valid = false;
					processor.InDocumentContext(sourcePath, () => valid = verifier.VerifyInstance(declaration));
					return valid;
				}) : null;
				Action<bool> publishVerification = valid => processor.InDocumentContext(sourcePath, () => {
					if (!valid) {
						version.VerificationErrors = true;
						throw new BinaryAssetBuilderException(ErrorCode.GameDataVerification,
							"FATAL: An asset failed the Game Data Verification step.  See previous output.");
					}
				});
				Action<AssetLocation> committed = source => processor.InDocumentContext(sourcePath, () => {
					processor.RecordExecutionCommit(source);
					RecordCommit(binaryAsset, declaration, source);
				});
				if (location == AssetLocation.None)
					execution.Enqueue(scope, binaryAsset, declaration, processor.Plugins.GetPlugin(declaration.Handle.TypeId),
						() => processor.InDocumentContext(sourcePath, () => CompileInstance(binaryAsset, declaration)),
						verify, publishVerification, committed, parallelVerification,
						parallelCompilation: _ExpandedBuffers == null || !_ExpandedBuffers.ContainsKey(declaration));
				else
					execution.EnqueueCached(scope, binaryAsset, declaration, location, verify, publishVerification, committed, parallelVerification);
				continue;
			}
			VerifyInstance(binaryAsset, selfInstance);
			if (location == AssetLocation.None)
			{
				CompileInstance(binaryAsset, selfInstance);
				location = AssetLocation.Memory;
			}
			AssetLocation assetLocation = binaryAsset.Commit(location);
			CountCommitSource(assetLocation, ref instancesCompiledCount, ref instancesCopiedFromCacheCount);
			RecordCommit(binaryAsset, selfInstance, assetLocation);
		}
	}

	private void RecordCommit(BinaryAsset binaryAsset, InstanceDeclaration declaration, AssetLocation source)
	{
		var processor = _Current.DocumentProcessor;
		if (source == AssetLocation.Cache)
			processor.RecordBuildCacheUse(true);
		else if (source == AssetLocation.Memory && Settings.Current.BuildCache && processor.Plugins.GetExtendedTypeInformation(declaration.Handle.TypeId).UseBuildCache)
			processor.RecordBuildCacheUse(false);
		if (source != AssetLocation.BasePatchStream)
			// Both execution paths publish first; the processor's existing
			// FileBase table counts each successful logical asset only once.
			processor.AddLastWrittenAsset(binaryAsset);
	}

	private void CountCommitSource(AssetLocation commitSource, ref int instancesCompiledCount, ref int instancesCopiedFromCacheCount)
	{
		switch (commitSource)
		{
		case AssetLocation.Memory:
			instancesCompiledCount++;
			break;
		case AssetLocation.Local:
			instancesCopiedFromCacheCount++;
			break;
		case AssetLocation.Cache:
			instancesCopiedFromCacheCount++;
			break;
		}
	}

	private void VerifyInstance(BinaryAsset asset, InstanceDeclaration declaration)
	{
		using var performance = PerformanceScope.Measure("core.asset.verify");
		if (_Current.IsLoaded)
		{
			IAssetBuilderVerifierPlugin plugin = _Current.DocumentProcessor.VerifierPlugins.GetPlugin(declaration.Handle.TypeId);
			if (!plugin.VerifyInstance(declaration))
			{
				_Current.VerificationErrors = true;
				throw new BinaryAssetBuilderException(ErrorCode.GameDataVerification, "FATAL: An asset failed the Game Data Verification step.  See previous output.");
			}
		}
	}

	private void CompileInstance(BinaryAsset asset, InstanceDeclaration declaration)
	{
		using var performance = PerformanceScope.Measure("core.asset.compile");
		_Tracer.Message("{0}: Compiling {1}", Path.GetFileName(_Current.SourcePath), declaration.ToString());
		if (declaration.XmlNode == null)
		{
			throw new BinaryAssetBuilderException(ErrorCode.DependencyCacheFailure, "Need to compile instance {0} but XML is not loaded. This is a bug. Please notify cnc3technicalarchitecture@ea.com.", declaration);
		}
		if (declaration.HasCustomData)
		{
			declaration.CustomDataPath = Path.Combine(asset.CustomDataOutputDirectory, asset.FileBase + ".cdata");
			if (!Directory.Exists(asset.CustomDataOutputDirectory))
			{
				Directory.CreateDirectory(asset.CustomDataOutputDirectory);
			}
		}
		DateTime now = DateTime.Now;
		IAssetBuilderPlugin plugin = _Current.DocumentProcessor.Plugins.GetPlugin(declaration.Handle.TypeId);
		_Current.DocumentProcessor.RecordInstanceCompile();
		AssetBuffer expanded;
		if (_ExpandedBuffers != null && _ExpandedBuffers.TryGetValue(declaration, out expanded))
		{
			asset.Buffer = expanded;
			_Current.DocumentProcessor.AddCompileTime(declaration.Handle, DateTime.Now - now);
			return;
		}
		using (Settings.Current.MeasurePerformance == null ? null : PerformanceScope.Measure("core.compiler." + declaration.Handle.TypeName))
			asset.Buffer = _Current.DocumentProcessor.AssetExecution == null
				? plugin.ProcessInstance(declaration)
				: _Current.DocumentProcessor.AssetExecution.ExecuteCompatibility(plugin, declaration);
		_Current.DocumentProcessor.AddCompileTime(declaration.Handle, DateTime.Now - now);
	}

	public void ProcessOverrides()
	{
		using var performance = PerformanceScope.Measure("core.xml.overrides");
		foreach (InstanceDeclaration selfInstance in SelfInstances)
		{
			OverrideInstance(selfInstance);
		}
	}

	private void GatherUnvalidatedTags()
	{
		Tags.Clear();
		XPathNavigator xPathNavigator = _Current.XmlDocument.CreateNavigator();
		XPathNodeIterator xPathNodeIterator = xPathNavigator.Evaluate("/ea:AssetDeclaration/ea:Tags/child::*", _Current.NamespaceManager) as XPathNodeIterator;
		foreach (XPathNavigator item in xPathNodeIterator)
		{
			_Current.Tags.Add(item.GetAttribute("name", ""), item.GetAttribute("value", ""));
		}
	}

	private void GatherDefines()
	{
		SelfDefines.Clear();
		XPathNavigator xPathNavigator = _Current.XmlDocument.CreateNavigator();
		XPathNodeIterator xPathNodeIterator = xPathNavigator.Evaluate("/ea:AssetDeclaration/ea:Defines/child::*", _Current.NamespaceManager) as XPathNodeIterator;
		foreach (XPathNavigator item in xPathNodeIterator)
		{
			Definition definition = new Definition();
			definition.Document = this;
			definition.OriginalValue = item.GetAttribute("value", "");
			definition.Name = item.GetAttribute("name", "");
			definition.IsOverride = item.GetAttribute("override", "") == "true";
			_Current.SelfDefines.Add(definition);
		}
	}

	private void GatherUnvalidatedIncludes()
	{
		InclusionItems.Clear();
		XPathNavigator xPathNavigator = _Current.XmlDocument.CreateNavigator();
		XPathNodeIterator xPathNodeIterator = xPathNavigator.Evaluate("/ea:AssetDeclaration/ea:Includes/child::*", _Current.NamespaceManager) as XPathNodeIterator;
		foreach (XPathNavigator item in xPathNodeIterator)
		{
			string text = item.GetAttribute("source", "").Trim().ToLower();
			string physicalPath = FileNameResolver.ResolvePath(SourceDirectory, text).ToLower();
			InclusionItem inclusionItem = new InclusionItem(text, physicalPath, (InclusionType)Enum.Parse(typeof(InclusionType), item.GetAttribute("type", ""), ignoreCase: true));
			_Current.InclusionItems.Add(inclusionItem);
			if (inclusionItem.Type == InclusionType.Instance)
			{
				_Current.DependentFiles.Add(text);
			}
		}
	}

	private void GatherUnvalidatedInstances()
	{
		_ExpandedBuffers = null;
		SelfInstances.Clear();
		XmlNodeList xmlNodeList = _Current.XmlDocument.SelectNodes("/ea:AssetDeclaration/child::*", _Current.NamespaceManager);
		foreach (XmlNode item in xmlNodeList)
		{
			if (item.Name == "Includes" || item.Name == "Tags" || item.Name == "Defines")
			{
				continue;
			}
			InstanceDeclaration instanceDeclaration = new InstanceDeclaration(this);
			instanceDeclaration.XmlNode = item;
			if (!SelfInstances.TryAdd(instanceDeclaration))
			{
				InstanceDeclaration instanceDeclaration2 = SelfInstances[instanceDeclaration.Handle];
				if (instanceDeclaration.Handle.InstanceName == instanceDeclaration2.Handle.InstanceName)
				{
					throw new BinaryAssetBuilderException(ErrorCode.DuplicateInstance, "Duplicate Instance: {0}, in {1} and {2}", instanceDeclaration, instanceDeclaration.Document.SourcePath, instanceDeclaration2.Document.SourcePath);
				}
				throw new BinaryAssetBuilderException(ErrorCode.DuplicateInstance, "Duplicate Instance: {0} (other is {1})", instanceDeclaration, instanceDeclaration2);
			}
		}
	}

	// Frontend workers receive values and schema facts captured by the owner.
	// Only the initial XML hash reads DOM: the whole batch joins before any
	// reference rewrite or TypeId attribute is applied to this document.
	private sealed class FrontendReference
	{
		internal XPathNavigator Target;
		internal SchemaSet.InstanceValueKind Kind;
		internal XmlSchemaType SchemaType, ActualType, RequiredType;
		internal string Value, ReferenceType, TypeIdText;
		internal bool IsTypeId;
	}

	private sealed class FrontendReferenceResult
	{
		internal string Text;
		internal InstanceHandle Handle;
		internal uint ReferenceTypeHash;
	}

	private sealed class FrontendInput
	{
		internal InstanceDeclaration Declaration;
		internal XmlNode Node;
		internal string Name, SchemaTypeName;
		internal uint TypeHash, ProcessingHash;
		internal bool IsInheritable;
		internal readonly List<FrontendReference> References = new List<FrontendReference>();
	}

	private sealed class FrontendResult
	{
		internal uint XmlHash;
		internal FrontendReferenceResult[] References;
	}

	private FrontendReference CaptureFrontendReference(InstanceDeclaration declaration, XPathNavigator navigator)
	{
		var type = navigator.SchemaInfo?.SchemaType;
		if (type == null)
			throw new BinaryAssetBuilderException(ErrorCode.InternalError,
				"Element {0} in instance {1} doesn't have a schema type.", navigator.Name, declaration);
		var schemas = _Current.DocumentProcessor.SchemaSet;
		var kind = schemas.GetInstanceTypeMetadata(type).Kind;
		if (kind != SchemaSet.InstanceValueKind.WeakReference && kind != SchemaSet.InstanceValueKind.AssetReference &&
			kind != SchemaSet.InstanceValueKind.FileReference && kind != SchemaSet.InstanceValueKind.Hashable) return null;
		var result = new FrontendReference { Target = navigator.Clone(), Kind = kind, SchemaType = type, Value = navigator.Value };
		if (kind == SchemaSet.InstanceValueKind.AssetReference || kind == SchemaSet.InstanceValueKind.WeakReference)
		{
			string text = navigator.Value.Trim().Split('\\')[0];
			if (!string.IsNullOrEmpty(text))
			{
				result.ReferenceType = GetReferenceTypeName(navigator);
				if (kind == SchemaSet.InstanceValueKind.AssetReference)
				{
					string[] name = text.Split(':');
					if (name.Length == 2)
					{
						result.ActualType = schemas.GetXmlType(name[0]);
						if (result.ReferenceType != null) result.RequiredType = schemas.GetXmlType(result.ReferenceType);
					}
				}
			}
		}
		return result;
	}

	private string GetReferenceTypeName(XPathNavigator navigator)
	{
		var info = navigator.SchemaInfo;
		var schemas = _Current.DocumentProcessor.SchemaSet;
		string name = null;
		if (info.SchemaElement != null) name = schemas.GetReferenceTypeName(info.SchemaElement);
		else if (info.SchemaAttribute != null) name = schemas.GetReferenceTypeName(info.SchemaAttribute);
		if (name == null && info.SchemaType != null)
		{
			name = schemas.GetReferenceTypeName(info.SchemaType);
			if (name == null && info.SchemaType.DerivedBy == XmlSchemaDerivationMethod.Extension)
				name = schemas.GetReferenceTypeName(info.SchemaType.BaseXmlSchemaType);
		}
		return name;
	}

	private static FrontendResult ComputeFrontend(FrontendInput input)
	{
		var node = input.Node;
		var result = new FrontendResult {
			XmlHash = HashProvider.GetXmlHash(HashProvider.GetTextHash(input.ProcessingHash, 11u.ToString()), ref node),
			References = new FrontendReferenceResult[input.References.Count]
		};
		for (int i = 0; i < input.References.Count; i++)
		{
			var reference = input.References[i];
			if (reference.IsTypeId || (reference.Kind != SchemaSet.InstanceValueKind.AssetReference &&
				reference.Kind != SchemaSet.InstanceValueKind.WeakReference)) continue;
			string text = reference.Value.Trim().Split('\\')[0];
			if (string.IsNullOrEmpty(text)) continue;
			var handle = new InstanceHandle(text);
			if (reference.ReferenceType == null)
				throw new BinaryAssetBuilderException(ErrorCode.ReferencingError,
					"Asset reference to '{0}' in '{1}' does not have type (xas:refType missing in schema).", text, input.Name);
			uint referenceTypeHash = HashProvider.GetCaseSensitiveSymbolHash(reference.ReferenceType);
			if (handle.TypeId == 0) handle.TypeName = reference.ReferenceType;
			else if (reference.Kind == SchemaSet.InstanceValueKind.AssetReference)
			{
				if (reference.RequiredType == null)
					throw new BinaryAssetBuilderException(ErrorCode.ReferencingError,
						"Unable to establish schema type of underlying reference type '{0}'. Make sure it is defined and included in the schema set.", reference.ReferenceType);
				if (reference.ActualType == null)
					throw new BinaryAssetBuilderException(ErrorCode.ReferencingError,
						"Unable to establish schema type of referenced instance '{0}'. Make sure it is defined and included in the schema set.", handle.Name);
				if (!XmlSchemaType.IsDerivedFrom(reference.ActualType, reference.RequiredType, XmlSchemaDerivationMethod.None))
					throw new BinaryAssetBuilderException(ErrorCode.ReferencingError,
						"Type of instance '{0}' referenced from '{1}' does not appear to be equal to or derived from required reference type '{2}'.", handle.Name, input.Name, reference.ReferenceType);
			}
			result.References[i] = new FrontendReferenceResult { Text = text, Handle = handle, ReferenceTypeHash = referenceTypeHash };
		}
		return result;
	}

	private void ValidateInstances()
	{
		var processor = _Current.DocumentProcessor;
		StringCollection derivedTypes = processor.SchemaSet.GetDerivedTypes("BaseInheritableAsset");
		var inputs = new List<FrontendInput>();
		var work = new List<Func<FrontendResult>>();
		using (PerformanceScope.Measure("core.xml.instance.prepare"))
		foreach (InstanceDeclaration instance in _Current.SelfInstances)
		{
			var metadata = processor.Plugins.GetExtendedTypeInformation(instance.Handle.TypeId);
			var input = new FrontendInput { Declaration = instance, Node = instance.XmlNode, Name = instance.Handle.Name,
				TypeHash = metadata.TypeHash, ProcessingHash = processor.Plugins.GetProcessingHash(instance),
				SchemaTypeName = instance.XmlNode.SchemaInfo.SchemaType.Name,
				IsInheritable = derivedTypes == null ? instance.IsInheritable : derivedTypes.Contains(instance.XmlNode.SchemaInfo.SchemaType.Name) };
			var descendants = instance.XmlNode.CreateNavigator().SelectDescendants("", "uri:ea.com:eala:asset", matchSelf: true);
			foreach (XPathNavigator navigator in descendants)
			{
				var reference = CaptureFrontendReference(instance, navigator);
				if (reference != null) input.References.Add(reference);
				if (navigator.HasAttributes)
				{
					navigator.MoveToFirstAttribute();
					do {
						reference = CaptureFrontendReference(instance, navigator);
						if (reference != null) input.References.Add(reference);
					} while (navigator.MoveToNextAttribute());
					navigator.MoveToParent();
				}
				var schemaType = navigator.SchemaInfo?.SchemaType;
				if (schemaType != null && schemaType.Name != null)
					input.References.Add(new FrontendReference { Target = navigator.Clone(), IsTypeId = true,
						TypeIdText = processor.SchemaSet.GetInstanceTypeMetadata(schemaType).TypeIdText });
			}
			inputs.Add(input);
			work.Add(() => ComputeFrontend(input));
		}
		IList<FrontendResult> results;
		using (PerformanceScope.Measure("core.xml.instance.hash"))
		{
			if (processor.AssetExecution != null) results = processor.AssetExecution.ExecuteFrontend(work);
			else {
				var serial = new List<FrontendResult>(work.Count);
				foreach (var compute in work) serial.Add(compute());
				results = serial;
			}
		}
		// Owner-only publication preserves the original declaration, reference,
		// file dependency and DOM rewrite order. No worker is reading this DOM.
		var tailWork = new List<Func<uint>>();
		var tailInstances = new List<InstanceDeclaration>();
		using (PerformanceScope.Measure("core.xml.instance.references"))
		for (int i = 0; i < inputs.Count; i++)
		{
			var input = inputs[i];
			var result = results[i];
			var instance = input.Declaration;
			instance.Handle.TypeHash = input.TypeHash;
			instance.Handle.InstanceHash = result.XmlHash;
			instance.ProcessingHash = input.ProcessingHash;
			instance.IsInheritable = input.IsInheritable;
			if (instance.Handle.TypeName != input.SchemaTypeName)
				throw new BinaryAssetBuilderException(ErrorCode.SchemaValidation, "Type name and element name do not match for {0}.", instance.Handle);
			if (input.TypeHash == 0) _Tracer.TraceWarning("No type hash found for type {0}.", instance.Handle.TypeName);
			if (Settings.Current.OutputIntermediateXml) OutputIntermediateXml(instance);
			using var references = new MemoryStream();
			using var writer = new BinaryWriter(references);
			for (int n = 0; n < input.References.Count; n++)
			{
				var reference = input.References[n];
				if (reference.IsTypeId) reference.Target.CreateAttribute("", "TypeId", "", reference.TypeIdText);
				else if (reference.Kind == SchemaSet.InstanceValueKind.FileReference)
					HandleFileReferenceType(reference.Target, ref instance);
				else if (reference.Kind == SchemaSet.InstanceValueKind.Hashable)
					HashProvider.RecordHash(reference.SchemaType, reference.Value);
				else {
					var value = result.References[n];
					if (value == null) continue;
					writer.Write(value.ReferenceTypeHash);
					if (reference.Kind == SchemaSet.InstanceValueKind.AssetReference) {
						instance.ReferencedInstances.Add(value.Handle);
						reference.Target.SetValue($"{value.Text}\\{instance.ReferencedInstances.Count - 1}");
					} else instance.WeakReferencedInstances.Add(value.Handle);
				}
			}
			foreach (string file in instance.ReferencedFiles) {
				FileHashItem identity = null;
				TryGetFileHashItem(file, out identity);
				writer.Write(identity.Hash);
			}
			// Keep GetBuffer's capacity/zero padding, part of the SDK hash contract.
			if (references.Length > 0) {
				byte[] tail = references.GetBuffer();
				tailInstances.Add(instance);
				tailWork.Add(() => FastHash.GetHashCode(tail));
			}
		}
		IList<uint> tails;
		if (processor.AssetExecution != null) tails = processor.AssetExecution.ExecuteFrontend(tailWork);
		else {
			var serial = new List<uint>(tailWork.Count);
			foreach (var compute in tailWork) serial.Add(compute());
			tails = serial;
		}
		for (int i = 0; i < tailInstances.Count; i++) tailInstances[i].Handle.InstanceHash ^= tails[i];
	}

	private static IEnumerable<string> EnumerateUiSources(string directory)
	{
		string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
		Array.Sort(files, StringComparer.Ordinal);
		foreach (string file in files)
			if (String.Equals(Path.GetExtension(file), ".as", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(Path.GetExtension(file), ".fla", StringComparison.OrdinalIgnoreCase)) yield return file;
	}

#if NEWSdk_MONOLITHIC
	private uint UiCompilerInputHash()
	{
		return HashProvider.GetTextHash(BinaryAssetBuilder.UiCompiler.UiCatalog.ProcessingHash,
			_Current.DocumentProcessor.Plugins.GetPlugin(1816258268u).GetVersionNumber().ToString(System.Globalization.CultureInfo.InvariantCulture));
	}

	private void ExpandUiSources()
	{
		// Run after the ordinary schema/path validation, before dependency and
		// instance hashes become cache keys. No output is committed until every
		// source in this document has compiled successfully.
		_Current.UiCompilerHash = 0;
		var sources = new List<InstanceDeclaration>();
		foreach (InstanceDeclaration instance in SelfInstances)
			if (instance.Handle.TypeName == "AptAptData" &&
				String.Equals(Path.GetExtension(instance.XmlNode.Attributes["File"]?.Value), ".fla", StringComparison.OrdinalIgnoreCase)) sources.Add(instance);
		foreach (InstanceDeclaration source in sources)
		{
			var compiler = _Current.DocumentProcessor.Plugins.GetPlugin(source.Handle.TypeId) as BinaryAssetBuilder.UiCompiler.IUiFileCompiler;
			if (compiler == null) throw new InvalidOperationException("The AptAptData plugin does not support FLA expansion.");
			var group = compiler.CompileUiFile(source, Settings.Current.TargetPlatform);
			_Current.UiCompilerHash = UiCompilerInputHash();
			uint sourceHash = HashProvider.GetTextHash(source.Handle.InstanceHash, _Current.UiCompilerHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
			string directory = Path.GetDirectoryName(source.XmlNode.Attributes["File"].Value);
			foreach (string input in group.InputFiles)
			{
				FileHashItem identity;
				if (!TryGetFileHashItem(input, out identity)) throw new FileNotFoundException("UI source input is missing.", input);
				sourceHash = HashProvider.GetTextHash(sourceHash, FileNameResolver.GetSafeRelativePath(directory, identity.Path).Replace('\\', '/').ToLowerInvariant());
				sourceHash = HashProvider.GetTextHash(sourceHash, identity.Hash.ToString(System.Globalization.CultureInfo.InvariantCulture));
				if (!_Current.DependentFiles.Contains(input)) _Current.DependentFiles.Add(input);
				if (!source.ReferencedFiles.Contains(input)) source.ReferencedFiles.Add(input);
			}
			if (!_Current.UiSourceDirectories.Contains(directory)) _Current.UiSourceDirectories.Add(directory);
			if (_ExpandedBuffers == null) _ExpandedBuffers = new Dictionary<InstanceDeclaration, AssetBuffer>();
			bool foundSource = false;
			foreach (var compiled in group)
			{
				InstanceDeclaration declaration;
				if (compiled.TypeId == source.Handle.TypeId && compiled.InstanceId == source.Handle.InstanceId)
				{
					declaration = source;
					foundSource = true;
				}
				else
				{
					XmlElement node = _Current.XmlDocument.CreateElement(compiled.TypeName, Settings.Current.AssetNamespace);
					node.SetAttribute("id", compiled.Name);
					declaration = new InstanceDeclaration(this) { XmlNode = node };
					if (!SelfInstances.TryAdd(declaration)) throw new BinaryAssetBuilderException(ErrorCode.DuplicateInstance, "Duplicate generated UI instance: {0}", declaration.Handle);
					declaration.ProcessingHash = _Current.DocumentProcessor.Plugins.GetExtendedTypeInformation(compiled.TypeId).ProcessingHash;
				}
				declaration.HasCustomData = false;
				declaration.Handle.TypeHash = compiled.TypeHash;
				declaration.Handle.InstanceHash = HashProvider.GetTextHash(sourceHash, compiled.InstanceHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
				_ExpandedBuffers.Add(declaration, compiled.Buffer);
			}
			if (!foundSource) throw new InvalidDataException("The UI compiler did not return its source AptAptData asset.");
		}
	}
#endif

	private void OutputIntermediateXml(InstanceDeclaration instance)
	{
		XmlWriterSettings xmlWriterSettings = new XmlWriterSettings();
		xmlWriterSettings.NewLineOnAttributes = true;
		xmlWriterSettings.Encoding = Encoding.UTF8;
		xmlWriterSettings.Indent = true;
		xmlWriterSettings.IndentChars = "\t";
		string text = Path.Combine(Settings.Current.IntermediateOutputDirectory, "FinalXml_" + Settings.Current.TargetPlatform);
		string text2 = Path.Combine(instance.Handle.TypeName, instance.Handle.InstanceName);
		Directory.CreateDirectory(text);
		Directory.CreateDirectory(Path.Combine(text, instance.Handle.TypeName));
		string outputFileName = Path.Combine(text, text2 + Path.GetExtension(LogicalSourcePath));
		try
		{
			XmlWriter xmlWriter = XmlWriter.Create(outputFileName, xmlWriterSettings);
			XmlElement xmlElement = ReconstructAssetDeclaration(instance);
			xmlElement.WriteTo(xmlWriter);
			xmlWriter.Close();
		}
		catch (Exception ex)
		{
			_Tracer.TraceWarning("Warning: Unable to output {0}, reason: {1}", text2, ex.Message);
		}
	}

	private XmlElement ReconstructAssetDeclaration(InstanceDeclaration instance)
	{
		XmlElement xmlElement = XmlDocument.CreateElement("AssetDeclaration", Settings.Current.AssetNamespace);
		XmlElement xmlElement2 = XmlDocument.CreateElement("Tags", Settings.Current.AssetNamespace);
		XmlElement xmlElement3 = XmlDocument.CreateElement("Tag", Settings.Current.AssetNamespace);
		XmlAttribute xmlAttribute = XmlDocument.CreateAttribute("name");
		xmlAttribute.Value = "SourceXml";
		XmlAttribute xmlAttribute2 = XmlDocument.CreateAttribute("tag");
		xmlAttribute2.Value = LogicalSourcePath;
		xmlElement3.Attributes.Append(xmlAttribute);
		xmlElement3.Attributes.Append(xmlAttribute2);
		xmlElement2.AppendChild(xmlElement3);
		xmlElement.AppendChild(xmlElement2);
		xmlElement.AppendChild(instance.XmlNode);
		return xmlElement;
	}

	public void RecordStringHashes()
	{
		foreach (InstanceDeclaration selfInstance in _Current.SelfInstances)
		{
			HashProvider.RecordHash(selfInstance.Handle);
			foreach (InstanceHandle referencedInstance in selfInstance.ReferencedInstances)
			{
				HashProvider.RecordHash(referencedInstance);
			}
			foreach (InstanceHandle weakReferencedInstance in selfInstance.WeakReferencedInstances)
			{
				HashProvider.RecordHash(weakReferencedInstance);
			}
		}
	}

	private void HandleFileReferenceType(XPathNavigator navigator, ref InstanceDeclaration instance)
	{
		string text = navigator.Value.Trim().ToLower();
#if NEWSdk_MONOLITHIC
		if (instance.Handle.TypeName == "AptAptData" && navigator.LocalName == "File" &&
			_Current.DocumentProcessor.Plugins.GetPlugin(instance.Handle.TypeId) is BinaryAssetBuilder.UiCompiler.IUiFileCompiler)
		{
			string source = FileNameResolver.ResolvePath(SourceDirectory, text);
			if (Directory.Exists(source))
			{
				string[] movies = Directory.GetFiles(source).Where(p => Path.GetExtension(p).Equals(".fla", StringComparison.OrdinalIgnoreCase)).ToArray();
				if (movies.Length != 1) throw new InvalidDataException("A UI source folder must contain exactly one Flash 8 FLA.");
				text = movies[0];
			}
		}
#endif
		FileHashItem item = null;
		TryGetFileHashItem(text, out item);
		string value = item.Path.ToLower();
		_Current.DependentFiles.Add(text);
		navigator.SetValue(value);
		instance.ReferencedFiles.Add(text);
	}

	public InstanceDeclaration FindInstance(InstanceHandle handle, FindLocation skipLocation, out FindLocation location)
	{
		location = FindLocation.None;
		InstanceDeclaration value = null;
		if (_Current.SelfInstances != null && skipLocation != FindLocation.Self && _Current.SelfInstances.TryGetValue(handle, out value))
		{
			location = FindLocation.Self;
			return value;
		}
		if (_Current.AllInstances != null && skipLocation != FindLocation.All && _Current.AllInstances.TryGetValue(handle, out value))
		{
			location = FindLocation.All;
			return value;
		}
		if (_Current.TentativeInstances != null && skipLocation != FindLocation.Tentative && _Current.TentativeInstances.TryGetValue(handle, out value))
		{
			location = FindLocation.Tentative;
			return value;
		}
		if (_Current.ReferenceInstances != null && skipLocation != FindLocation.External && _Current.ReferenceInstances.TryGetValue(handle, out value))
		{
			location = FindLocation.External;
			return value;
		}
		return null;
	}

	private InstanceDeclaration ResolveReference(InstanceDeclaration parentInstance, InstanceHandle referenceHandle, out FindLocation location)
	{
		InstanceDeclaration instanceDeclaration = FindInstance(referenceHandle, FindLocation.None, out location);
		if (instanceDeclaration == null)
		{
			InstanceHandle instanceHandle = new InstanceHandle(referenceHandle.TypeName, referenceHandle.InstanceName);
			StringCollection derivedTypes = _Current.DocumentProcessor.SchemaSet.GetDerivedTypes(referenceHandle.TypeName);
			if (derivedTypes != null)
			{
				StringBuilder stringBuilder = new StringBuilder();
				int num = 0;
				StringEnumerator enumerator = derivedTypes.GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						string current = enumerator.Current;
						instanceHandle.TypeName = current;
						FindLocation location2;
						InstanceDeclaration instanceDeclaration2 = FindInstance(instanceHandle, FindLocation.None, out location2);
						if (instanceDeclaration2 != null)
						{
							instanceDeclaration = instanceDeclaration2;
							location = location2;
							if (num > 0)
							{
								stringBuilder.AppendFormat(", ");
							}
							stringBuilder.AppendFormat("'{0}'", instanceHandle.Name);
							num++;
						}
					}
				}
				finally
				{
					if (enumerator is IDisposable disposable)
					{
						disposable.Dispose();
					}
				}
				if (num > 1)
				{
					throw new BinaryAssetBuilderException(ErrorCode.ReferencingError, "Reference to instance '{0}' from '{1}' in 'file://{2}' is ambiguous. Possible matches: {3}.", referenceHandle.Name, parentInstance.Handle.Name, parentInstance.Document.SourcePath, stringBuilder);
				}
			}
		}
		return instanceDeclaration;
	}

	private void AddOutputInstance(InstanceDeclaration instance)
	{
		if (instance.Handle.TypeHash == 0 || _Current.OutputInstanceSet.ContainsKey(instance.Handle))
		{
			return;
		}
		_Current.OutputInstanceSet.Add(instance.Handle, instance);
		if (instance.ValidatedReferencedInstances != null)
		{
			foreach (InstanceHandle validatedReferencedInstance in instance.ValidatedReferencedInstances)
			{
				FindLocation location = FindLocation.None;
				InstanceDeclaration instanceDeclaration = ResolveReference(instance, validatedReferencedInstance, out location);
				if (instanceDeclaration != null && location != FindLocation.External)
				{
					AddOutputInstance(instanceDeclaration);
				}
			}
			return;
		}
		instance.ValidatedReferencedInstances = new List2<InstanceHandle>();
		instance.AllDependentInstances = new InstanceHandleSet();
		foreach (InstanceHandle referencedInstance in instance.ReferencedInstances)
		{
			FindLocation location2 = FindLocation.None;
			InstanceDeclaration instanceDeclaration2 = ResolveReference(instance, referencedInstance, out location2);
			if (instanceDeclaration2 != null)
			{
				if (location2 != FindLocation.External)
				{
					instance.AllDependentInstances.TryAdd(instanceDeclaration2.Handle);
					AddOutputInstance(instanceDeclaration2);
					if (instanceDeclaration2.AllDependentInstances != null)
					{
						foreach (InstanceHandle allDependentInstance in instanceDeclaration2.AllDependentInstances)
						{
							instance.AllDependentInstances.TryAdd(allDependentInstance);
						}
					}
				}
				instance.ValidatedReferencedInstances.Add(instanceDeclaration2.Handle);
			}
			else
			{
				if (Settings.Current.ErrorLevel > 0)
				{
					throw new BinaryAssetBuilderException(ErrorCode.UnknownReference, "Unknown referenced asset: {0}", referencedInstance);
				}
				if (_Current.DocumentProcessor.MissingReferences.TryAdd(referencedInstance))
				{
					_Tracer.TraceWarning("Unknown asset '{0}' referenced from '{1}' in 'file://{2}'", referencedInstance.Name, instance.Handle.Name, instance.Document.SourcePath);
				}
				instance.ValidatedReferencedInstances.Add(referencedInstance);
			}
		}
		foreach (InstanceHandle weakReferencedInstance in instance.WeakReferencedInstances)
		{
			FindLocation location3 = FindLocation.None;
			InstanceDeclaration instanceDeclaration3 = ResolveReference(instance, weakReferencedInstance, out location3);
			if (instanceDeclaration3 != null && location3 == FindLocation.Tentative)
			{
				AddOutputInstance(instanceDeclaration3);
				instance.ValidatedReferencedInstances.Add(instanceDeclaration3.Handle);
			}
		}
		foreach (string referencedFile in instance.ReferencedFiles)
		{
			FileHashItem item = null;
			if (!instance.Document.TryGetFileHashItem(referencedFile, out item))
			{
				if (Settings.Current.ErrorLevel > 0)
				{
					throw new BinaryAssetBuilderException(ErrorCode.FileNotFound, "Referenced file not found: {0}", referencedFile);
				}
				_Tracer.TraceWarning("Referenced file not found: {0}", referencedFile);
			}
		}
	}

	public void StableSort(OutputManager outputManager)
	{
		_Tracer.TraceInfo("Stable sorting assets");
		List2<InstanceDeclaration> finalList = new List2<InstanceDeclaration>();
		if (outputManager.BasePatchStreamManifest != null)
		{
			_Tracer.TraceInfo("Merging base stream assets from {0}", outputManager.BasePatchStream);
			StableSort_InitializeFromBaseManifest(outputManager, ref _Current.OutputInstances, finalList);
		}
		_Tracer.TraceInfo("Sorting by schema dependencies");
		TypeDepCompare typeDepCompare = new TypeDepCompare(_Current.DocumentProcessor.SchemaSet.AssetDependencies);
		_Current.OutputInstances.Sort(typeDepCompare);
		_Tracer.TraceInfo("Sorting by asset dependencies");
		StableSort_ByAssetDependencies(_Current.OutputInstances, finalList, typeDepCompare);
		_Current.OutputInstances = finalList;
	}

	private static void StableSort_ByAssetDependencies(List2<InstanceDeclaration> instances, List2<InstanceDeclaration> finalList, IComparer<InstanceDeclaration> comparer)
	{
		var dependencies = new Dictionary<InstanceDeclaration, HashSet<InstanceHandle>>();
		var requiredByPrevious = new HashSet<InstanceHandle>();
		var maximumNames = new Dictionary<uint, string>();
		InstanceDeclaration maximum = null;
		foreach (var instance in finalList)
		{
			var set = StableSort_Dependencies(instance);
			if (set != null) { dependencies.Add(instance, set); requiredByPrevious.UnionWith(set); }
			if (maximum == null || comparer.Compare(instance, maximum) > 0) maximum = instance;
			string name;
			if (!maximumNames.TryGetValue(instance.Handle.TypeId, out name) || string.Compare(instance.Handle.InstanceName, name) > 0)
				maximumNames[instance.Handle.TypeId] = instance.Handle.InstanceName;
		}
		foreach (var instance in instances)
		{
			var set = StableSort_Dependencies(instance);
			int order = maximum == null ? 1 : comparer.Compare(instance, maximum);
			string name;
			bool nameAfter = !maximumNames.TryGetValue(instance.Handle.TypeId, out name) || string.Compare(instance.Handle.InstanceName, name) > 0;
			// In this case every step of the original insertion scan advances to
			// the end. Equal culture keys and base-stream gaps keep its exact rules.
			if (order > 0 && nameAfter && !requiredByPrevious.Contains(instance.Handle)) finalList.Add(instance);
			else finalList.Insert(StableSort_CalcInsertPosition(finalList, instance, comparer, set, dependencies), instance);
			if (set != null) { dependencies.Add(instance, set); requiredByPrevious.UnionWith(set); }
			if (order > 0) maximum = instance;
			if (nameAfter) maximumNames[instance.Handle.TypeId] = instance.Handle.InstanceName;
		}
	}

	private static HashSet<InstanceHandle> StableSort_Dependencies(InstanceDeclaration instance)
	{
		var dependencies = instance.AllDependentInstances;
		return dependencies == null || dependencies.Count == 0 ? null : new HashSet<InstanceHandle>(dependencies);
	}

	private static ulong StableSort_Key(uint typeId, uint instanceId) => ((ulong)typeId << 32) | instanceId;

	private static void StableSort_InitializeFromBaseManifest(OutputManager outputManager, ref List2<InstanceDeclaration> outputInstances, List2<InstanceDeclaration> finalList)
	{
		Manifest basePatchStreamManifest = outputManager.BasePatchStreamManifest;
		Asset[] assets = basePatchStreamManifest.Assets;
		var instances = new Dictionary<ulong, InstanceDeclaration>();
		foreach (var instance in outputInstances) instances.Add(StableSort_Key(instance.Handle.TypeId, instance.Handle.InstanceId), instance);
		foreach (Asset baseAsset in assets)
		{
			ulong key = StableSort_Key(baseAsset.TypeId, baseAsset.InstanceId);
			if (instances.TryGetValue(key, out var instanceDeclaration))
			{
				if (outputManager.GetBinaryAsset(instanceDeclaration, isOutputAsset: false).GetLocation(AssetLocation.BasePatchStream, AssetLocationOption.None) == AssetLocation.BasePatchStream)
				{
					_Tracer.TraceInfo("Using base stream instance {0}:{1}", instanceDeclaration.Handle.TypeName, instanceDeclaration.Handle.InstanceName);
					finalList.Add(instanceDeclaration);
					instances.Remove(key);
				}
				else
				{
					_Tracer.TraceInfo("Using patched instance {0}:{1}", instanceDeclaration.Handle.TypeName, instanceDeclaration.Handle.InstanceName);
				}
			}
		}
		var remaining = new List2<InstanceDeclaration>();
		foreach (var instance in outputInstances)
			if (instances.ContainsKey(StableSort_Key(instance.Handle.TypeId, instance.Handle.InstanceId))) remaining.Add(instance);
		outputInstances = remaining;
	}

	private static int StableSort_CalcInsertPosition(List2<InstanceDeclaration> finalList, InstanceDeclaration instance, IComparer<InstanceDeclaration> depCompare,
		HashSet<InstanceHandle> instanceDependencies, Dictionary<InstanceDeclaration, HashSet<InstanceHandle>> dependencies)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		uint num5 = 0u;
		uint num6 = 0u;
		bool flag = false;
		foreach (InstanceDeclaration final in finalList)
		{
			if (final.Handle.TypeId != num6)
			{
				num3 = num2;
				num6 = final.Handle.TypeId;
			}
			if (instanceDependencies != null && instanceDependencies.Contains(final.Handle))
			{
				num = num2 + 1;
				num4 = num;
				if (instance.Handle.TypeId != final.Handle.TypeId)
				{
					flag = false;
					num5 = final.Handle.TypeId;
				}
			}
			else
			{
				if (dependencies.TryGetValue(final, out var finalDependencies) && finalDependencies.Contains(instance.Handle))
				{
					if (num3 > num4 && instance.Handle.TypeId != final.Handle.TypeId)
					{
						num = num3;
					}
					break;
				}
				if (instance.Handle.TypeId == final.Handle.TypeId)
				{
					if (!flag)
					{
						num = num2;
						flag = true;
					}
					if (string.Compare(instance.Handle.InstanceName, final.Handle.InstanceName) > 0)
					{
						num = num2 + 1;
					}
				}
				else if (final.Handle.TypeId == num5)
				{
					num = num2 + 1;
				}
				else
				{
					num5 = 0u;
					if (depCompare.Compare(instance, final) > 0)
					{
						num = num2 + 1;
					}
				}
			}
			num2++;
		}
		return num;
	}

	public void PrepareOutputInstances(OutputManager outputManager)
	{
		using (PerformanceScope.Measure("core.output.reference-closure"))
		{
			_Current.OutputInstanceSet = new SortedDictionary<InstanceHandle, InstanceDeclaration>();
			foreach (InstanceDeclaration instance in Instances)
			{
				AddOutputInstance(instance);
			}
		}
		using (PerformanceScope.Measure("core.output.availability"))
		{
			foreach (InstanceDeclaration value in _Current.OutputInstanceSet.Values)
			{
				BinaryAsset binaryAsset = outputManager.GetBinaryAsset(value, isOutputAsset: true);
				if (binaryAsset != null)
				{
					AssetLocation location = binaryAsset.GetLocation(AssetLocation.All, AssetLocationOption.None);
					if (location == AssetLocation.None)
					{
						location = binaryAsset.GetLocation(AssetLocation.Local | AssetLocation.Cache, AssetLocationOption.ForceUpdate);
					}
					if (location == AssetLocation.None)
					{
						throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Asset {0} not available.", value);
					}
					if ((location & AssetLocation.Output) == 0)
					{
						binaryAsset.Commit(location);
					}
					_Current.OutputInstances.Add(value);
					if (Settings.Current.OutputAssetReport)
					{
						AssetReport.RecordAsset(value, binaryAsset);
					}
				}
			}
		}
		using (PerformanceScope.Measure("core.output.order"))
		{
			if (Settings.Current.StableSort)
			{
				StableSort(outputManager);
			}
			else
			{
				_Current.OutputInstances.Sort(new DependencyComparer());
			}
		}
		MemoryStream memoryStream = new MemoryStream();
		BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		foreach (InstanceDeclaration outputInstance in _Current.OutputInstances)
		{
			binaryWriter.Write(outputInstance.Handle.TypeId);
			binaryWriter.Write(outputInstance.Handle.TypeHash);
			binaryWriter.Write(outputInstance.Handle.InstanceId);
			binaryWriter.Write(outputInstance.Handle.InstanceHash);
			binaryWriter.Write(outputInstance.ReferencedInstances.Count);
		}
		if (memoryStream.Length > 0)
		{
			_Current.OutputChecksum = FastHash.GetHashCode(memoryStream.GetBuffer());
		}
		else
		{
			_Current.OutputChecksum = 0u;
		}
		_Current.OutputInstanceSet = null;
	}

	public void CacheFromDocument(AssetDeclarationDocument other)
	{
		if (other._Last != null)
		{
			_Last = other._Last;
		}
		else
		{
			if (other._Current == null || _Last != null)
			{
				return;
			}
			_Last = new LastState(other._Current);
			List2<InstanceDeclaration> list = new List2<InstanceDeclaration>();
			foreach (InstanceDeclaration selfInstance in other._Current.SelfInstances)
			{
				InstanceDeclaration instanceDeclaration = new InstanceDeclaration();
				instanceDeclaration.CacheFromInstance(selfInstance);
				list.Add(instanceDeclaration);
			}
			_Last.SelfInstances = new List2<InstanceDeclaration>(list);
		}
	}
}

