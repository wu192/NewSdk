using System;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder.Utility;

namespace BinaryAssetBuilder.Core;

public class OutputManager
{
	private const int LinkedBufferSize = 65536;

	private static Tracer _Tracer = Tracer.GetTracer("Output Manager", "Provides manifest serialization functionality");

	private IDictionary<string, OutputAsset> _OldOutputInstances = new Dictionary<string, OutputAsset>(StringComparer.Ordinal);

	// FileBase is a fixed hexadecimal content identity. Output order comes
	// from document.OutputInstances, not the order of these lookup tables.
	private IDictionary<string, BinaryAsset> _Assets = new Dictionary<string, BinaryAsset>(StringComparer.Ordinal);

	private int _AssetFileCount;

	private int _CustomDataFileCount;

	private string _OutputDirectory;

	private string _IntermediateOutputDirectory;

	private string _AssetDirectory;

	private string _CustomDataDirectory;

	private TargetPlatform _Platform;

	private bool _IsLinked;

	private bool _ValidOldOutputInstances;

	private string _TargetPlatformCacheRoot;

	private string _ManifestFile;

	private string _OldManifestFile;
	private string _CachedLinkedOutput;
	private bool _ReuseLinked;

	private string _VersionFile;

	private DocumentProcessor _DocumentProcessor;

	private string _BasePatchStreamRelativePath = "";

	private string _BasePatchStream;

	private IDictionary<string, AssetHeader> _BasePatchStreamAssets;

	private Manifest _BasePatchStreamManifest;

	private ManifestHeader _Header;

	private static byte[] _LinkedBuffer = new byte[65536];

	public IDictionary<string, BinaryAsset> Assets => _Assets;

	public DocumentProcessor DocumentProcessor => _DocumentProcessor;

	public string IntermediateOutputDirectory => _IntermediateOutputDirectory;

	public string OutputDirectory => _OutputDirectory;

	public string TargetPlatformCacheRoot => _TargetPlatformCacheRoot;

	public IDictionary<string, AssetHeader> BasePatchStreamAssets => _BasePatchStreamAssets;

	public Manifest BasePatchStreamManifest => _BasePatchStreamManifest;

	public string BasePatchStream => _BasePatchStream;

	public OutputManager(DocumentProcessor documentProcessor, List2<OutputAsset> lastOutputInstances, string outputDirectory, string intermediateOutputDirectory, string basePatchStream, string baseStreamRelativePath, string[] baseStreamSearchPaths)
	{
		_DocumentProcessor = documentProcessor;
		_OutputDirectory = outputDirectory;
		_IntermediateOutputDirectory = intermediateOutputDirectory;
		_Platform = Settings.Current.TargetPlatform;
		_IsLinked = Settings.Current.LinkedStreams;
		_AssetDirectory = Path.Combine(_IntermediateOutputDirectory, "assets");
		_CustomDataDirectory = Path.Combine(_OutputDirectory, "cdata");
		_TargetPlatformCacheRoot = (Settings.Current.BuildCache ? Path.Combine(Settings.Current.BuildCacheDirectory, _Platform.ToString()) : null);
		_ManifestFile = _OutputDirectory + ".manifest";
		_OldManifestFile = _OutputDirectory + ".old.manifest";
		_VersionFile = _OutputDirectory.Remove(_OutputDirectory.LastIndexOf(Settings.Current.CustomPostfix)) + ".version";
        Directory.CreateDirectory(Path.GetDirectoryName(_OutputDirectory));
        Settings.Current.StreamOutputStarted?.Invoke(_OutputDirectory);
        bool complete = true;
        foreach (string extension in new[] { ".manifest", ".bin", ".imp", ".relo" })
            if (!File.Exists(_OutputDirectory + extension)) { complete = false; break; }
        _CachedLinkedOutput = complete ? _OutputDirectory : null;
        if (!complete)
            foreach (string extension in new[] { ".bin", ".imp", ".relo" }) File.Delete(_OutputDirectory + extension);
		if (_CachedLinkedOutput != null)
		{
			_OldManifestFile = _CachedLinkedOutput + ".manifest";
			_Header = new ManifestHeader();
			using (var input = File.OpenRead(_OldManifestFile)) _Header.LoadFromStream(input, Settings.Current.BigEndian);
		}
		if (_CachedLinkedOutput == null && File.Exists(_OldManifestFile))
		{
			File.Delete(_OldManifestFile);
		}
		if (_CachedLinkedOutput == null && File.Exists(_ManifestFile))
		{
			try
			{
				_Header = new ManifestHeader();
				using (FileStream input = File.OpenRead(_ManifestFile))
				{
					_Header.LoadFromStream(input, Settings.Current.BigEndian);
				}
				File.Move(_ManifestFile, _OldManifestFile);
			}
			catch (Exception innerException)
			{
				throw new BinaryAssetBuilderException(innerException, ErrorCode.LockedFile, "Unable to delete '{0}'. Make sure no other application is writing to or reading from this file while the data build is running.", _ManifestFile);
			}
		}
		if (Directory.Exists(_IntermediateOutputDirectory) && lastOutputInstances != null)
		{
			_ValidOldOutputInstances = true;
			foreach (OutputAsset lastOutputInstance in lastOutputInstances)
			{
				_OldOutputInstances.Add(lastOutputInstance.Handle.FileBase, lastOutputInstance);
			}
		}
		if (basePatchStream != null)
		{
			ProcessBasePatchStream(basePatchStream, baseStreamRelativePath, baseStreamSearchPaths);
		}
	}

	private void ProcessBasePatchStream(string basePatchStream, string baseStreamRelativePath, string[] baseStreamSearchPaths)
	{
		Manifest manifest = new Manifest();
		List<string> list = new List<string>();
		if (baseStreamSearchPaths != null)
		{
			list.AddRange(baseStreamSearchPaths);
		}
		list.Add(Settings.Current.OutputDirectory);
		string text = basePatchStream;
		if (!File.Exists(text))
		{
			foreach (string item in list)
			{
				string text2 = Path.Combine(item, basePatchStream);
				if (File.Exists(text2))
				{
					text = text2;
					break;
				}
			}
		}
		if (!File.Exists(text))
		{
			throw new BinaryAssetBuilderException(ErrorCode.FileNotFound, "Specified manifest could not be found: {0}", text);
		}
		try
		{
			manifest.Load(text, list.ToArray());
		}
		catch (Exception)
		{
			_Tracer.TraceError("Could not load {0}.", basePatchStream);
			return;
		}
		_BasePatchStream = basePatchStream;
		_BasePatchStreamRelativePath = baseStreamRelativePath;
		_BasePatchStreamAssets = new Dictionary<string, AssetHeader>(StringComparer.Ordinal);
		Asset[] assets = manifest.Assets;
		foreach (Asset asset in assets)
		{
			AssetHeader assetHeader = new AssetHeader();
			assetHeader.ImportsDataSize = asset.ImportsDataSize;
			assetHeader.InstanceDataSize = asset.InstanceDataSize;
			assetHeader.RelocationDataSize = asset.RelocationDataSize;
			assetHeader.TypeId = asset.TypeId;
			assetHeader.TypeHash = asset.TypeHash;
			assetHeader.InstanceHash = asset.InstanceHash;
			assetHeader.InstanceId = asset.InstanceId;
			_BasePatchStreamAssets.Add(Path.GetFileName(asset.FileBasePath), assetHeader);
		}
		_BasePatchStreamManifest = manifest;
	}

	public BinaryAsset GetBinaryAsset(InstanceDeclaration instance, bool isOutputAsset)
	{
		if (instance.Handle.TypeHash == 0)
		{
			return null;
		}
		string fileBase = instance.Handle.FileBase;
		BinaryAsset value = null;
		if (!_Assets.TryGetValue(fileBase, out value))
		{
			OutputAsset value2 = null;
			_OldOutputInstances.TryGetValue(fileBase, out value2);
			value = new BinaryAsset(this, value2, instance);
			_Assets.Add(fileBase, value);
		}
		if (isOutputAsset && !value.IsOutputAsset)
		{
			// Availability probes can precede an XML reload. Bind the actual
			// declaration once at first output registration, before any worker
			// exists; later requests retain this stable physical input.
			value.Instance = instance;
			_OldOutputInstances.Remove(value.FileBase);
			value.IsOutputAsset = true;
			_AssetFileCount++;
			if (value.Instance.HasCustomData)
			{
				_CustomDataFileCount++;
			}
		}
		return value;
	}

	public void CreateVersionFile(AssetDeclarationDocument document, string streamPostfix)
	{
		using StreamWriter streamWriter = File.CreateText(_VersionFile);
		if (!string.IsNullOrEmpty(streamPostfix))
		{
			streamWriter.Write(streamPostfix);
		}
		else
		{
			streamWriter.Write("  ");
		}
	}

	public void CommitManifest(AssetDeclarationDocument document)
	{
		using var performance = PerformanceScope.Measure("core.output.manifest");
		uint allTypesHash = _DocumentProcessor.Plugins.DefaultPlugin.GetAllTypesHash();
		if (_Header != null)
		{
			if (_Header.IsLinked == Settings.Current.LinkedStreams && _Header.Version == ManifestHeader.LatestVersion && _Header.StreamChecksum == document.OutputChecksum && _Header.AllTypesHash == allTypesHash)
			{
				if (_CachedLinkedOutput != null && LinkedChecksumsMatch(document.OutputChecksum))
				{
					_ReuseLinked = true;
					_Tracer.TraceInfo("Old manifest is up to date.");
					return;
				}
				if (_CachedLinkedOutput == null) { File.Move(_OldManifestFile, _ManifestFile); return; }
				_Tracer.TraceInfo("Cached linked data requires regeneration.");
			}
			if (_CachedLinkedOutput == null) File.Delete(_OldManifestFile);
			Directory.CreateDirectory(Path.GetDirectoryName(_ManifestFile));
			_Tracer.TraceInfo("Regenerating manifest.");
		}
		else
		{
			string directoryName = Path.GetDirectoryName(_ManifestFile);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
		}
        // If any checksum differs, regenerate all linked payloads together.
        // Import/relocation buffers are populated while linking the binary file.
        foreach (string extension in new[] { ".bin", ".imp", ".relo" }) File.Delete(_OutputDirectory + extension);
		_Header = new ManifestHeader();
		MemoryStream memoryStream = new MemoryStream();
		uint num = 0u;
		uint num2 = 0u;
		uint num3 = 0u;
		uint num4 = 0u;
		uint num5 = 0u;
		int num6 = -1;
		new SortedDictionary<string, int>();
		AssetEntry assetEntry = new AssetEntry();
		NameBuffer nameBuffer = new NameBuffer();
		NameBuffer nameBuffer2 = new NameBuffer();
		ReferencedFileBuffer referencedFileBuffer = new ReferencedFileBuffer();
		UInt32Buffer uInt32Buffer = new UInt32Buffer();
		foreach (InstanceDeclaration outputInstance in document.OutputInstances)
		{
			ExtendedTypeInformation extendedTypeInformation = _DocumentProcessor.Plugins.GetExtendedTypeInformation(outputInstance.Handle.TypeId);
			BinaryAsset binaryAsset = GetBinaryAsset(outputInstance, isOutputAsset: true);
			num++;
			int length = uInt32Buffer.Length;
			foreach (InstanceHandle validatedReferencedInstance in outputInstance.ValidatedReferencedInstances)
			{
				uInt32Buffer.AddValue(validatedReferencedInstance.TypeId);
				uInt32Buffer.AddValue(validatedReferencedInstance.InstanceId);
			}
			num2 += (uint)binaryAsset.InstanceFileSize;
			num5 = (uint)Math.Max(binaryAsset.InstanceFileSize, num5);
			num3 = (uint)Math.Max(binaryAsset.RelocationFileSize, num3);
			num4 = (uint)Math.Max(binaryAsset.ImportsFileSize, num4);
			assetEntry.TypeId = outputInstance.Handle.TypeId;
			assetEntry.InstanceId = outputInstance.Handle.InstanceId;
			assetEntry.TypeHash = outputInstance.Handle.TypeHash;
			assetEntry.InstanceHash = outputInstance.Handle.InstanceHash;
			assetEntry.AssetReferenceOffset = length;
			assetEntry.AssetReferenceCount = outputInstance.ReferencedInstances.Count;
			assetEntry.NameOffset = nameBuffer.AddName(outputInstance.Handle.Name);
			assetEntry.SourceFileNameOffset = nameBuffer2.AddName(outputInstance.Document.LogicalSourcePath);
			assetEntry.Tokenized = (extendedTypeInformation.Tokenized ? 1u : 0u);
			int num7 = ((_BasePatchStreamManifest == null) ? (-1) : _BasePatchStreamManifest.GetBaseStreamPosition(assetEntry));
			AssetLocation location = binaryAsset.GetLocation(AssetLocation.BasePatchStream, AssetLocationOption.None);
			if (AssetLocation.BasePatchStream == location && num6 >= num7 && num7 >= 0 && Settings.Current.LinkedStreams)
			{
				_Tracer.TraceInfo("Duplicating base stream asset {0} in patch stream due to ordering change.", outputInstance.Handle.FullName);
			}
			if (AssetLocation.BasePatchStream == location && num6 < num7 && num7 >= 0 && Settings.Current.LinkedStreams)
			{
				assetEntry.InstanceDataSize = 0;
				assetEntry.RelocationDataSize = 0;
				assetEntry.ImportsDataSize = 0;
				num6 = num7;
			}
			else
			{
				assetEntry.InstanceDataSize = binaryAsset.InstanceFileSize;
				assetEntry.RelocationDataSize = binaryAsset.RelocationFileSize;
				assetEntry.ImportsDataSize = binaryAsset.ImportsFileSize;
			}
			assetEntry.SaveToStream(memoryStream, Settings.Current.BigEndian);
		}
		if (_BasePatchStream != null)
		{
			referencedFileBuffer.AddReference(_BasePatchStreamRelativePath + Path.GetFileName(_BasePatchStream), isPatch: true);
		}
		foreach (InclusionItem inclusionItem in document.InclusionItems)
		{
			if (inclusionItem.Type == InclusionType.Reference)
			{
				string text = ((inclusionItem.Document == null) ? _DocumentProcessor.GetExpectedOutputManifest(inclusionItem.PhysicalPath) : (inclusionItem.Document.SourcePathFromRoot + ".manifest"));
				if (Path.IsPathRooted(text))
				{
					throw new InvalidOperationException("Reference File Paths must be relative!");
				}
				text.ToLower();
				text.Replace('/', '\\');
				referencedFileBuffer.AddReference(text, isPatch: false);
			}
		}
		byte[] buffer = memoryStream.GetBuffer();
		using (FileStream fileStream = new FileStream(_ManifestFile, FileMode.Create))
		{
			ManifestHeader manifestHeader = new ManifestHeader();
			manifestHeader.StreamChecksum = document.OutputChecksum;
			manifestHeader.AllTypesHash = allTypesHash;
			manifestHeader.IsLinked = _IsLinked;
			manifestHeader.AssetCount = num;
			manifestHeader.TotalInstanceDataSize = num2;
			manifestHeader.MaxInstanceChunkSize = num5;
			manifestHeader.MaxRelocationChunkSize = num3;
			manifestHeader.MaxImportsChunkSize = num4;
			manifestHeader.AssetReferenceBufferSize = (uint)uInt32Buffer.Length;
			manifestHeader.ReferencedManifestNameBufferSize = (uint)referencedFileBuffer.Length;
			manifestHeader.AssetNameBufferSize = (uint)nameBuffer.Length;
			manifestHeader.SourceFileNameBufferSize = (uint)nameBuffer2.Length;
			manifestHeader.SaveToStream(fileStream, Settings.Current.BigEndian);
			fileStream.Write(buffer, 0, (int)memoryStream.Length);
			uInt32Buffer.SaveToStream(fileStream, Settings.Current.BigEndian);
			referencedFileBuffer.SaveToStream(fileStream);
			nameBuffer.SaveToStream(fileStream);
			nameBuffer2.SaveToStream(fileStream);
		}
		memoryStream.Close();
	}

	private void AppendAsset(Stream source, Stream destination, int length)
	{
		while (length > 0)
		{
			int num = ((length > 65536) ? 65536 : length);
			int num2 = source.Read(_LinkedBuffer, 0, num);
			if (num2 != num)
			{
				throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Requested more bytes to read than available.");
			}
			destination.Write(_LinkedBuffer, 0, num2);
			length -= num2;
		}
	}

	private void MoveLinked(string linkPath, string linkPathExisting, string ext)
	{
		if (File.Exists(linkPathExisting + ext))
		{
			File.Delete(linkPathExisting + ext);
		}
		File.Move(linkPath + ext, linkPathExisting + ext);
	}

	private bool LinkedChecksumsMatch(uint checksum)
	{
		foreach (string extension in new[] { ".bin", ".imp", ".relo" })
			using (var input = File.OpenRead(_CachedLinkedOutput + extension))
			using (var reader = new BinaryReader(input))
			{
				uint expected = Settings.Current.BigEndian ? ((checksum & 255) << 24) | ((checksum & 65280) << 8) | ((checksum >> 8) & 65280) | (checksum >> 24) : checksum;
				if (input.Length < 4 || reader.ReadUInt32() != expected) return false;
			}
		return true;
	}

	public void LinkStream(AssetDeclarationDocument document)
	{
		using var performance = PerformanceScope.Measure("core.output.link");
		if (_ReuseLinked)
		{
			_Tracer.Message("{0} Linked binary data up to date", document.SourcePathFromRoot);
			_Tracer.Message("{0} Linked import data up to date", document.SourcePathFromRoot);
			_Tracer.Message("{0} Linked relocation data up to date", document.SourcePathFromRoot);
            Settings.Current.StreamOutputCompleted?.Invoke(_OutputDirectory, true);
			return;
		}
		string text = _OutputDirectory + ".temp";
		string outputDirectory = _OutputDirectory;
		uint num = document.OutputChecksum;
		if (Settings.Current.BigEndian)
		{
			num = ((num & 0xFF) << 24) | ((num & 0xFF00) << 8) | ((num & 0xFF0000) >> 8) | ((num >> 24) & 0xFF);
		}
		MemoryStream memoryStream = new MemoryStream();
		MemoryStream memoryStream2 = new MemoryStream();
		bool flag = true;
		using (FileStream fileStream = new FileStream(outputDirectory + ".bin", FileMode.OpenOrCreate, FileAccess.Read))
		{
			if (fileStream.Length >= 4)
			{
				BinaryReader binaryReader = new BinaryReader(fileStream);
				flag = binaryReader.ReadUInt32() != num;
			}
		}
		if (flag)
		{
			using (FileStream fileStream2 = new FileStream(text + ".bin", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write, 65536))
			{
				_Tracer.Message("{0} Linking binary data", document.SourcePathFromRoot);
				AssetHeader assetHeader = new AssetHeader();
				fileStream2.SetLength(0L);
				BinaryWriter binaryWriter = new BinaryWriter(fileStream2);
				binaryWriter.Write(num);
				foreach (InstanceDeclaration outputInstance in document.OutputInstances)
				{
					BinaryAsset binaryAsset = _Assets[outputInstance.Handle.FileBase];
					if (AssetLocation.BasePatchStream != binaryAsset.GetLocation(AssetLocation.BasePatchStream, AssetLocationOption.None))
					{
						using FileStream fileStream3 = File.OpenRead(Path.Combine(binaryAsset.AssetOutputDirectory, binaryAsset.AssetFileName));
						assetHeader.LoadFromStream(fileStream3, Settings.Current.BigEndian);
						AppendAsset(fileStream3, fileStream2, assetHeader.InstanceDataSize);
						AppendAsset(fileStream3, memoryStream2, assetHeader.RelocationDataSize);
						AppendAsset(fileStream3, memoryStream, assetHeader.ImportsDataSize);
					}
				}
				fileStream2.Flush();
			}
			MoveLinked(text, outputDirectory, ".bin");
		}
		else
		{
			_Tracer.Message("{0} Linked binary data up to date", document.SourcePathFromRoot);
		}
		flag = true;
		using (FileStream fileStream4 = new FileStream(outputDirectory + ".imp", FileMode.OpenOrCreate, FileAccess.Read))
		{
			if (fileStream4.Length >= 4)
			{
				BinaryReader binaryReader2 = new BinaryReader(fileStream4);
				flag = binaryReader2.ReadUInt32() != num;
			}
		}
		if (flag)
		{
			using (FileStream fileStream5 = new FileStream(text + ".imp", FileMode.OpenOrCreate, FileAccess.Write))
			{
				_Tracer.Message("{0} Linking import data", document.SourcePathFromRoot);
				fileStream5.SetLength(0L);
				BinaryWriter binaryWriter2 = new BinaryWriter(fileStream5);
				binaryWriter2.Write(num);
				binaryWriter2.Write(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
				fileStream5.Flush();
			}
			MoveLinked(text, outputDirectory, ".imp");
		}
		else
		{
			_Tracer.Message("{0} Linked import data up to date", document.SourcePathFromRoot);
		}
		flag = true;
		using (FileStream fileStream6 = new FileStream(outputDirectory + ".relo", FileMode.OpenOrCreate, FileAccess.Read))
		{
			if (fileStream6.Length >= 4)
			{
				BinaryReader binaryReader3 = new BinaryReader(fileStream6);
				flag = binaryReader3.ReadUInt32() != num;
			}
		}
		if (flag)
		{
			using (FileStream fileStream7 = new FileStream(text + ".relo", FileMode.OpenOrCreate, FileAccess.Write))
			{
				_Tracer.Message("{0} Linking relocation data", document.SourcePathFromRoot);
				fileStream7.SetLength(0L);
				BinaryWriter binaryWriter3 = new BinaryWriter(fileStream7);
				binaryWriter3.Write(num);
				binaryWriter3.Write(memoryStream2.GetBuffer(), 0, (int)memoryStream2.Length);
				fileStream7.Flush();
			}
			MoveLinked(text, outputDirectory, ".relo");
		}
		else
		{
			_Tracer.Message("{0} Linked relocation data up to date", document.SourcePathFromRoot);
		}
        Settings.Current.StreamOutputCompleted?.Invoke(_OutputDirectory, false);
		memoryStream2.Close();
		memoryStream.Close();
	}

	public void CleanOutput()
	{
		using var performance = PerformanceScope.Measure("core.output.clean");
		bool flag = Path.GetFullPath(_IntermediateOutputDirectory) == Path.GetFullPath(_OutputDirectory);
		DirectoryInfo directoryInfo = new DirectoryInfo(_OutputDirectory);
		if (directoryInfo.Exists)
		{
			FileInfo[] files = directoryInfo.GetFiles();
			FileInfo[] array = files;
			foreach (FileInfo fileInfo in array)
			{
				fileInfo.Delete();
			}
		}
		if (!flag)
		{
			DirectoryInfo directoryInfo2 = new DirectoryInfo(_IntermediateOutputDirectory);
			if (directoryInfo2.Exists)
			{
				FileInfo[] files2 = directoryInfo2.GetFiles();
				FileInfo[] array2 = files2;
				foreach (FileInfo fileInfo2 in array2)
				{
					fileInfo2.Delete();
				}
			}
		}
		int assetFileCount = _AssetFileCount;
		int num = _CustomDataFileCount;
		string text = "n/a";
		foreach (OutputAsset value2 in _OldOutputInstances.Values)
		{
			ExtendedTypeInformation extendedTypeInformation = _DocumentProcessor.Plugins.GetExtendedTypeInformation(value2.Handle.TypeId);
			if (extendedTypeInformation.HasCustomData)
			{
				num++;
			}
		}
		assetFileCount += _OldOutputInstances.Count;
		text = _OldOutputInstances.Count.ToString();
		DirectoryInfo directoryInfo3 = new DirectoryInfo(_AssetDirectory);
		if (directoryInfo3.Exists)
		{
			FileInfo[] files3 = directoryInfo3.GetFiles();
			if (_ValidOldOutputInstances && assetFileCount == files3.Length && !Settings.Current.ForceSlowCleanup)
			{
				if (_OldOutputInstances.Count == 0)
				{
					_Tracer.TraceInfo("No asset clean-up required.");
				}
				else
				{
					_Tracer.TraceInfo("Fast asset clean-up.");
				}
				foreach (OutputAsset value3 in _OldOutputInstances.Values)
				{
					File.Delete(Path.Combine(_AssetDirectory, value3.Handle.FileBase) + ".asset");
				}
			}
			else
			{
				_Tracer.TraceInfo("Slow asset clean-up (expected: {0}, actual: {1}, old: {2}, current: {3})", assetFileCount, files3.Length, text, _AssetFileCount);
				FileInfo[] array3 = files3;
				foreach (FileInfo fileInfo3 in array3)
				{
					string key = Path.GetFileNameWithoutExtension(fileInfo3.Name).ToLower();
					if (fileInfo3.Extension != ".asset")
					{
						fileInfo3.Delete();
						continue;
					}
					BinaryAsset value = null;
					if (_Assets.TryGetValue(key, out value) && !value.IsOutputAsset)
					{
						value = null;
					}
					if (value == null)
					{
						fileInfo3.Delete();
					}
				}
			}
		}
		DirectoryInfo directoryInfo4 = new DirectoryInfo(_CustomDataDirectory);
		if (!directoryInfo4.Exists)
		{
			return;
		}
		FileInfo[] files4 = directoryInfo4.GetFiles();
		if (_ValidOldOutputInstances && num == files4.Length && !Settings.Current.ForceSlowCleanup)
		{
			if (_OldOutputInstances.Count == 0)
			{
				_Tracer.TraceInfo("No custom data clean-up required.");
			}
			else
			{
				_Tracer.TraceInfo("Fast custom data clean-up.");
			}
			{
				foreach (OutputAsset value4 in _OldOutputInstances.Values)
				{
					ExtendedTypeInformation extendedTypeInformation2 = _DocumentProcessor.Plugins.GetExtendedTypeInformation(value4.Handle.TypeId);
					if (extendedTypeInformation2.HasCustomData)
					{
						File.Delete(Path.Combine(_AssetDirectory, value4.Handle.FileBase) + ".cdata");
					}
				}
				return;
			}
		}
		_Tracer.TraceInfo("Slow custom data clean-up (expected: {0}, actual: {1}, old asset count: {2})", num, files4.Length, text, _AssetFileCount);
		FileInfo[] array4 = files4;
		foreach (FileInfo fileInfo4 in array4)
		{
			string key2 = Path.GetFileNameWithoutExtension(fileInfo4.Name).ToLower();
			if (!_Assets.ContainsKey(key2) || fileInfo4.Extension != ".cdata")
			{
				fileInfo4.Delete();
			}
		}
	}
}
