using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BinaryAssetBuilder.Utility;

namespace BinaryAssetBuilder.Core;

public class BinaryAsset
{
	private enum Availability
	{
		Invalid,
		Missing,
		Present
	}

	private static Tracer _Tracer = Tracer.GetTracer("BinaryAsset", "Provides output management for assets");

	private InstanceDeclaration _Instance;

	private string _CacheDirectory;
	private bool _CacheDirectoryInitialized;

	private string _AssetOutputDirectory;

	private string _CustomDataOutputDirectory;

	private string _AssetFileName;

	private string _CustomDataFileName;

	private AssetBuffer _Buffer;

	private bool _IsOutputAsset;

	private Availability _MemoryAvailability;

	private Availability _CacheAvailability;

	private Availability _OutputAvailability;

	private AssetLocationInfo _LastLocationInfo;

	private Availability _LocalAvailability;

	private Availability _BasePatchStreamAvailability;

	private AssetHeader _AssetHeader;

	private OutputManager _Parent;


	public AssetBuffer Buffer
	{
		get
		{
			return _Buffer;
		}
		set
		{
			_Buffer = value;
			UpdateMemoryAvailability(forceUpdate: true);
		}
	}

	public bool IsOutputAsset
	{
		get
		{
			return _IsOutputAsset;
		}
		set
		{
			_IsOutputAsset = value;
		}
	}

	public string FileBase => _Instance.Handle.FileBase;

	public InstanceDeclaration Instance
	{
		get
		{
			return _Instance;
		}
		set
		{
			_Instance = value;
		}
	}

	public int InstanceFileSize
	{
		get
		{
			if (_AssetHeader == null)
			{
				UpdateAssetHeader(forceUpdate: false);
			}
			return _AssetHeader.InstanceDataSize;
		}
	}

	public int RelocationFileSize
	{
		get
		{
			if (_AssetHeader == null)
			{
				UpdateAssetHeader(forceUpdate: false);
			}
			return _AssetHeader.RelocationDataSize;
		}
	}

	public int ImportsFileSize
	{
		get
		{
			if (_AssetHeader == null)
			{
				UpdateAssetHeader(forceUpdate: false);
			}
			return _AssetHeader.ImportsDataSize;
		}
	}

	public string AssetFileName => _AssetFileName;

	public string AssetOutputDirectory => _AssetOutputDirectory;

	public string CustomDataOutputDirectory => _CustomDataOutputDirectory;

	public string CustomDataPath
	{
		get
		{
			string path = Path.Combine(_CustomDataOutputDirectory, _CustomDataFileName);
			return File.Exists(path) ? path : Settings.Current.FindCustomData?.Invoke(path) ?? path;
		}
	}

	private void UpdateMemoryAvailability(bool forceUpdate)
	{
		if (_MemoryAvailability == Availability.Invalid || forceUpdate)
		{
			if (_Buffer != null && _Buffer.InstanceData != null && _Buffer.RelocationData != null && _Buffer.ImportsData != null)
			{
				_MemoryAvailability = Availability.Present;
			}
			else
			{
				_MemoryAvailability = Availability.Missing;
			}
		}
	}

	private void UpdateCacheAvailability(bool forceUpdate)
	{
		if (_CacheAvailability != Availability.Invalid && !forceUpdate)
		{
			return;
		}
		EnsureCacheDirectory();
		if (string.IsNullOrEmpty(_CacheDirectory))
		{
			_CacheAvailability = Availability.Missing;
			return;
		}
		string path = Path.Combine(_CacheDirectory, _AssetFileName);
		string path2 = Path.Combine(_CacheDirectory, _CustomDataFileName);
		if (File.Exists(path) && (!_Instance.HasCustomData || File.Exists(path2)))
		{
			_CacheAvailability = Availability.Present;
		}
		else
		{
			_CacheAvailability = Availability.Missing;
		}
	}

	private void UpdateOutputAvailability(bool forceUpdate)
	{
		if (_OutputAvailability == Availability.Invalid || forceUpdate)
		{
			string path = Path.Combine(_AssetOutputDirectory, _AssetFileName);
			string path2 = _Instance.HasCustomData ? CustomDataPath : null;
			if (File.Exists(path) && (File.Exists(path2) || !_Instance.HasCustomData))
			{
				_OutputAvailability = Availability.Present;
			}
			else
			{
				_OutputAvailability = Availability.Missing;
			}
		}
	}

	private void UpdateLocalAvailability(bool forceUpdate)
	{
		if (_LocalAvailability == Availability.Invalid || forceUpdate)
		{
			_LastLocationInfo = _Parent.DocumentProcessor.GetLastWrittenAsset(FileBase);
			if (_LastLocationInfo != null)
			{
				_LocalAvailability = Availability.Present;
			}
			else
			{
				_LocalAvailability = Availability.Missing;
			}
		}
	}

	private void UpdateBasePatchStreamAvailability(bool forceUpdate)
	{
		if (_BasePatchStreamAvailability == Availability.Invalid || forceUpdate)
		{
			if (_Parent.BasePatchStreamAssets != null && _Parent.BasePatchStreamAssets.TryGetValue(FileBase, out _AssetHeader))
			{
				_BasePatchStreamAvailability = Availability.Present;
			}
			else
			{
				_BasePatchStreamAvailability = Availability.Missing;
			}
		}
	}

	public AssetLocation GetLocation(AssetLocation locationFilter, AssetLocationOption options)
	{
		AssetLocation assetLocation = AssetLocation.None;
		bool forceUpdate = (options & AssetLocationOption.ForceUpdate) != 0;
		bool flag = (options & AssetLocationOption.ReturnAll) == 0;
		if ((locationFilter & AssetLocation.BasePatchStream) != AssetLocation.None)
		{
			UpdateBasePatchStreamAvailability(forceUpdate);
			if (_BasePatchStreamAvailability == Availability.Present)
			{
				if (flag)
				{
					return AssetLocation.BasePatchStream;
				}
				assetLocation |= AssetLocation.BasePatchStream;
			}
		}
		if ((locationFilter & AssetLocation.Output) != AssetLocation.None)
		{
			UpdateOutputAvailability(forceUpdate);
			if (_OutputAvailability == Availability.Present)
			{
				if (flag)
				{
					return AssetLocation.Output;
				}
				assetLocation |= AssetLocation.Output;
			}
		}
		if ((locationFilter & AssetLocation.Memory) != AssetLocation.None)
		{
			UpdateMemoryAvailability(forceUpdate);
			if (_MemoryAvailability == Availability.Present)
			{
				if (flag)
				{
					return AssetLocation.Memory;
				}
				assetLocation |= AssetLocation.Memory;
			}
		}
		if ((locationFilter & AssetLocation.Local) != AssetLocation.None)
		{
			UpdateLocalAvailability(forceUpdate);
			if (_LocalAvailability == Availability.Present)
			{
				if (flag)
				{
					return AssetLocation.Local;
				}
				assetLocation |= AssetLocation.Local;
			}
		}
		if ((locationFilter & AssetLocation.Cache) != AssetLocation.None)
		{
			UpdateCacheAvailability(forceUpdate);
			if (_CacheAvailability == Availability.Present)
			{
				if (flag)
				{
					return AssetLocation.Cache;
				}
				assetLocation |= AssetLocation.Cache;
			}
		}
		return assetLocation;
	}

	private void UpdateAssetHeader(bool forceUpdate)
	{
		if (_AssetHeader != null && !forceUpdate)
		{
			return;
		}
		AssetLocation location = GetLocation(AssetLocation.Memory | AssetLocation.Output, AssetLocationOption.ReturnAll);
		if (location == AssetLocation.None)
		{
			_AssetHeader = null;
		}
		else if ((location & AssetLocation.Memory) != AssetLocation.None)
		{
			_AssetHeader = new AssetHeader();
			_AssetHeader.ImportsDataSize = _Buffer.ImportsData.Length;
			_AssetHeader.InstanceDataSize = _Buffer.InstanceData.Length;
			_AssetHeader.RelocationDataSize = _Buffer.RelocationData.Length;
			_AssetHeader.TypeId = Instance.Handle.TypeId;
			_AssetHeader.TypeHash = Instance.Handle.TypeHash;
			_AssetHeader.InstanceHash = Instance.Handle.InstanceHash;
			_AssetHeader.InstanceId = Instance.Handle.InstanceId;
		}
		else if ((location & AssetLocation.Output) != AssetLocation.None)
		{
			long fileLength = 0L;
			string text = Path.Combine(_AssetOutputDirectory, _AssetFileName);
			using (FileStream fileStream = File.OpenRead(text))
			{
				_AssetHeader = new AssetHeader();
				_AssetHeader.LoadFromStream(fileStream, Settings.Current.BigEndian);
				fileLength = fileStream.Length;
			}
			if (!_AssetHeader.IsValidFileLength(fileLength))
			{
				_Tracer.TraceError("Invalid asset file detected. Deleting {0}.", text);
				File.Delete(text);
				location = GetLocation(AssetLocation.Cache, AssetLocationOption.None);
				if ((location & AssetLocation.Cache) == 0)
				{
					throw new BinaryAssetBuilderException(ErrorCode.UnexpectedSize, "Can't recover from last error. Please restart to rebuild asset.");
				}
				_Tracer.TraceInfo("Checking build cache copy of asset.");
				string text2 = Path.Combine(_CacheDirectory, _AssetFileName);
				using (FileStream fileStream2 = File.OpenRead(text2))
				{
					_AssetHeader = new AssetHeader();
					_AssetHeader.LoadFromStream(fileStream2, Settings.Current.BigEndian);
					fileLength = fileStream2.Length;
				}
				if (!_AssetHeader.IsValidFileLength(fileLength))
				{
					_Tracer.TraceError("Invalid cached asset file detected. Deleting {0}.", text2);
					File.Delete(text2);
					throw new BinaryAssetBuilderException(ErrorCode.UnexpectedSize, "Can't recover from last error. Please restart to rebuild asset.");
				}
                BinaryAssetCommitWork recovery = PrepareCommit(AssetLocation.Cache);
                try { recovery.ExecuteRecoveryIO(); }
                finally { recovery.FlushDiagnostics(); }
                _AssetHeader = recovery.Header;
                _OutputAvailability = recovery.OutputPresent ? Availability.Present : Availability.Missing;
			}
		}
		else
		{
			_AssetHeader = null;
		}
	}

	private void UpdateAssetHeaderFromOldInstance(OutputAsset oldInstance)
	{
		if (_AssetHeader == null)
		{
			if (oldInstance.InstanceFileSize == 0 && oldInstance.RelocationFileSize == 0 && oldInstance.ImportsFileSize == 0)
			{
				_Tracer.TraceWarning("Suspicious file sizes for {0}. Reloading info from disk.", _Instance);
				UpdateAssetHeader(forceUpdate: true);
				return;
			}
			_AssetHeader = new AssetHeader();
			_AssetHeader.ImportsDataSize = oldInstance.ImportsFileSize;
			_AssetHeader.InstanceDataSize = oldInstance.InstanceFileSize;
			_AssetHeader.RelocationDataSize = oldInstance.RelocationFileSize;
			_AssetHeader.TypeHash = oldInstance.Handle.TypeHash;
			_AssetHeader.TypeId = oldInstance.Handle.TypeId;
			_AssetHeader.InstanceId = oldInstance.Handle.InstanceId;
			_AssetHeader.InstanceHash = oldInstance.Handle.InstanceHash;
		}
	}

	public BinaryAsset(OutputManager parent, OutputAsset oldInstance, InstanceDeclaration instance)
	{
		_Parent = parent;
		if (Settings.Current.OldOutputFormat)
		{
			throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Old Output Format no longer supported");
		}
		_Instance = instance;
		_CustomDataOutputDirectory = Path.Combine(_Parent.OutputDirectory, "cdata");
		_AssetOutputDirectory = Path.Combine(_Parent.IntermediateOutputDirectory, "assets");
		_CustomDataFileName = FileBase + ".cdata";
		_AssetFileName = FileBase + ".asset";
		if (oldInstance != null && GetLocation(AssetLocation.All, AssetLocationOption.None) != AssetLocation.None)
			UpdateAssetHeaderFromOldInstance(oldInstance);
	}

	private void EnsureCacheDirectory()
	{
		if (_CacheDirectoryInitialized) return;
		_CacheDirectoryInitialized = true;
		if (!string.IsNullOrEmpty(_Parent.TargetPlatformCacheRoot))
		{
			ExtendedTypeInformation extendedTypeInformation = _Parent.DocumentProcessor.Plugins.GetExtendedTypeInformation(_Instance.Handle.TypeId);
			if (extendedTypeInformation.UseBuildCache)
			{
				_CacheDirectory = Path.Combine(_Parent.TargetPlatformCacheRoot, $"{_Instance.Handle.TypeName}\\{_Instance.Handle.TypeHash:x8}\\{(byte)(_Instance.Handle.InstanceHash >> 24):x2}");
			}
		}
	}

    public AssetLocation Commit()
    {
        return Commit(GetLocation(AssetLocation.All, AssetLocationOption.None));
    }

    internal AssetLocation Commit(AssetLocation location)
    {
        BinaryAssetCommitWork work = PrepareCommit(location);
        try { work.ExecuteIO(); }
        finally { work.FlushDiagnostics(); }
        return PublishCommit(work);
    }

    // Captures the owner-only dictionaries, paths and identity before any IO
    // starts. Workers only mutate the returned private result envelope.
    public BinaryAssetCommitWork PrepareCommit(AssetLocation location, AssetBuffer buffer = null, string customDataPath = null)
    {
        if (location == AssetLocation.Memory)
        {
            buffer = buffer ?? _Buffer;
            if (buffer == null || buffer.InstanceData == null || buffer.RelocationData == null || buffer.ImportsData == null)
                throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Failure to commit asset {0} from memory", _Instance);
        }
        string sourceAssetPath = null, sourceCustomDataPath = null;
        if (location == AssetLocation.Local)
        {
            UpdateLocalAvailability(forceUpdate: false);
            if (_LocalAvailability == Availability.Missing)
                throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Failure to commit asset {0} from last output", _Instance);
            sourceAssetPath = Path.Combine(_LastLocationInfo.AssetOutputDirectory, _AssetFileName);
            sourceCustomDataPath = Path.Combine(_LastLocationInfo.CustomDataOutputDirectory, _CustomDataFileName);
        }
        else if (location == AssetLocation.Cache)
        {
            UpdateCacheAvailability(forceUpdate: false);
            if (_CacheAvailability == Availability.Missing)
                throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Asset not available. Failure to commit asset {0} from network", _Instance);
            sourceAssetPath = Path.Combine(_CacheDirectory, _AssetFileName);
            sourceCustomDataPath = Path.Combine(_CacheDirectory, _CustomDataFileName);
        }
        else if (location != AssetLocation.Output && location != AssetLocation.Memory && location != AssetLocation.BasePatchStream)
            throw new BinaryAssetBuilderException(ErrorCode.DependencyCacheFailure, "Attempted to commit non-existing asset {0}", _Instance);

        if (location == AssetLocation.Memory || location == AssetLocation.Cache ||
            (location == AssetLocation.Output && _AssetHeader == null) || Settings.Current.AlwaysTouchCache)
            EnsureCacheDirectory();
        // GetLocation has already resolved host-provided custom data on the
        // owner. IO uses only the captured destination, without re-probing it.
        string outputCustomDataPath = Path.Combine(_CustomDataOutputDirectory, _CustomDataFileName);
        return new BinaryAssetCommitWork(location, buffer, customDataPath ?? _Instance.CustomDataPath,
            _Instance.Handle, _Instance.ToString(), _Instance.HasCustomData, _AssetOutputDirectory,
            _CustomDataOutputDirectory, _AssetFileName, _CustomDataFileName, outputCustomDataPath,
            sourceAssetPath, sourceCustomDataPath, _CacheDirectory, _AssetHeader,
            Settings.Current.BigEndian, Settings.Current.AlwaysTouchCache);
    }

    // This is the only publication point. No Worker writes BinaryAsset state,
    // calls host callbacks or updates the processor's statistics.
    public AssetLocation PublishCommit(BinaryAssetCommitWork work)
    {
        using var performance = PerformanceScope.Measure("core.asset.commit");
        work.FlushDiagnostics();
        if (work.Header != null) _AssetHeader = work.Header;
        if (work.Location != AssetLocation.BasePatchStream)
            _OutputAvailability = work.OutputPresent ? Availability.Present : Availability.Missing;
        if (work.Location == AssetLocation.Memory)
        {
            _Buffer = null;
            _MemoryAvailability = Availability.Missing;
        }
        if (_Instance.HasCustomData && work.Location != AssetLocation.BasePatchStream)
        {
            _Instance.CustomDataPath = Path.Combine(_CustomDataOutputDirectory, _CustomDataFileName);
            Settings.Current.CustomDataOutput?.Invoke(_Instance.CustomDataPath);
        }
        return work.Location;
    }

    internal static void PublishCommitDiagnostic(int kind, string message)
    {
        if (kind == 2) _Tracer.TraceError("{0}", message);
        else if (kind == 1) _Tracer.TraceInfo("{0}", message);
        else _Tracer.Message("{0}", message);
    }
}

// An asset owns one physical writer. Its input and result remain private until
// the scheduler joins the IO task and publishes it on the semantic owner.
public sealed class BinaryAssetCommitWork
{
    AssetBuffer buffer;
    byte[] copyBuffer;
    readonly string privateCustomDataPath, instanceText;
    readonly string assetDirectory, customDataDirectory, assetName, customDataName;
    readonly string outputCustomDataPath, sourceAssetPath, sourceCustomDataPath, cacheDirectory;
    readonly uint typeId, typeHash, instanceId, instanceHash;
    readonly bool hasCustomData, bigEndian, alwaysTouchCache;
    readonly List<KeyValuePair<int, string>> diagnostics = new List<KeyValuePair<int, string>>();
    internal AssetHeader Header;
    internal bool OutputPresent;
    public AssetLocation Location { get; private set; }
    public long RetainedBytes { get; private set; }

    internal BinaryAssetCommitWork(AssetLocation location, AssetBuffer buffer, string privateCustomDataPath,
        InstanceHandle handle, string instanceText, bool hasCustomData, string assetDirectory,
        string customDataDirectory, string assetName, string customDataName, string outputCustomDataPath,
        string sourceAssetPath, string sourceCustomDataPath, string cacheDirectory, AssetHeader header,
        bool bigEndian, bool alwaysTouchCache)
    {
        Location = location; this.buffer = buffer; this.privateCustomDataPath = privateCustomDataPath;
        typeId = handle.TypeId; typeHash = handle.TypeHash; instanceId = handle.InstanceId; instanceHash = handle.InstanceHash;
        this.instanceText = instanceText; this.hasCustomData = hasCustomData;
        this.assetDirectory = assetDirectory; this.customDataDirectory = customDataDirectory;
        this.assetName = assetName; this.customDataName = customDataName; this.outputCustomDataPath = outputCustomDataPath;
        this.sourceAssetPath = sourceAssetPath; this.sourceCustomDataPath = sourceCustomDataPath;
        this.cacheDirectory = cacheDirectory; Header = header; this.bigEndian = bigEndian; this.alwaysTouchCache = alwaysTouchCache;
        if (buffer != null)
            RetainedBytes = (long)buffer.InstanceData.Length + buffer.RelocationData.Length + buffer.ImportsData.Length;
    }

    void Message(int kind, string format, params object[] values)
    {
        diagnostics.Add(new KeyValuePair<int, string>(kind, string.Format(format, values)));
    }

    // Also safe to call after a failed IO task, once that task has joined.
    public void FlushDiagnostics()
    {
        foreach (var message in diagnostics) BinaryAsset.PublishCommitDiagnostic(message.Key, message.Value);
        diagnostics.Clear();
    }

    public void ExecuteIO() { ExecuteIO(updateCache: true); }

    internal void ExecuteRecoveryIO() { ExecuteIO(updateCache: false); }

    void ExecuteIO(bool updateCache)
    {
        try
        {
            switch (Location)
            {
                case AssetLocation.BasePatchStream:
                    if (updateCache && alwaysTouchCache) TouchCache();
                    return;
                case AssetLocation.Memory: CommitMemory(); break;
                case AssetLocation.Local:
                    if (!CopyAsset(sourceAssetPath, sourceCustomDataPath))
                        throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Failure to commit asset {0} from previous output", instanceText);
                    Message(0, "{0} copied from previous output", instanceText);
                    break;
                case AssetLocation.Cache: CommitCache(); break;
                case AssetLocation.Output: ReadOutputHeader(); break;
            }
            // Output was already selected from owner-captured availability.
            // Reusing its cached header must not re-probe every hot asset.
            OutputPresent = Location == AssetLocation.Output ||
                (File.Exists(Path.Combine(assetDirectory, assetName)) && (!hasCustomData || File.Exists(outputCustomDataPath)));
            if (!OutputPresent)
                throw new BinaryAssetBuilderException(ErrorCode.InternalError, "Failure to commit asset {0}", instanceText);
            if (updateCache)
            {
                if (Location == AssetLocation.Memory) CopyToCache();
                else if (Location == AssetLocation.Cache || alwaysTouchCache) TouchCache();
            }
        }
        finally { buffer = null; copyBuffer = null; }
    }

    void CommitMemory()
    {
        Directory.CreateDirectory(assetDirectory);
        string target = Path.Combine(assetDirectory, assetName), temporary = target + ".tmp";
        try
        {
            Header = new AssetHeader
            {
                ImportsDataSize = buffer.ImportsData.Length, InstanceDataSize = buffer.InstanceData.Length,
                RelocationDataSize = buffer.RelocationData.Length, TypeId = typeId, TypeHash = typeHash,
                InstanceHash = instanceHash, InstanceId = instanceId
            };
            using (FileStream output = File.Open(temporary, FileMode.Create, FileAccess.Write))
            {
                Header.SaveToStream(output, bigEndian);
                output.Write(buffer.InstanceData, 0, buffer.InstanceData.Length);
                output.Write(buffer.RelocationData, 0, buffer.RelocationData.Length);
                output.Write(buffer.ImportsData, 0, buffer.ImportsData.Length);
            }
            Replace(temporary, target);
            if (hasCustomData && !string.IsNullOrEmpty(privateCustomDataPath) &&
                !string.Equals(privateCustomDataPath, outputCustomDataPath, StringComparison.OrdinalIgnoreCase) && File.Exists(privateCustomDataPath))
            {
                Directory.CreateDirectory(customDataDirectory);
                Replace(privateCustomDataPath, outputCustomDataPath);
            }
        }
        catch (Exception error)
        {
            throw new BinaryAssetBuilderException(error, ErrorCode.InternalError, "Failure to commit asset {0} from memory", instanceText);
        }
    }

    static void Replace(string temporary, string target)
    {
        if (File.Exists(target)) File.Delete(target);
        File.Move(temporary, target);
    }

    bool CopyAsset(string sourceAsset, string sourceCustomData)
    {
        string target = Path.Combine(assetDirectory, assetName), temporary = target + ".tmp";
        string customTarget = Path.Combine(customDataDirectory, customDataName);
        try
        {
            Directory.CreateDirectory(assetDirectory);
            // IO admission limits the number of these private buffers. No
            // process-wide copy lock serializes unrelated physical assets.
            if (copyBuffer == null) copyBuffer = new byte[1048576];
            AssetHeader header = null;
            using (FileStream input = File.OpenRead(sourceAsset))
            using (FileStream output = File.Open(temporary, FileMode.Create, FileAccess.Write))
            {
                long remaining = input.Length;
                while (remaining > 0)
                {
                    int read = input.Read(copyBuffer, 0, copyBuffer.Length);
                    if (read < 16) throw new IOException("Invalid asset copy length.");
                    if (header == null)
                    {
                        header = new AssetHeader();
                        header.LoadFromBuffer(copyBuffer, bigEndian);
                        if (header.InstanceId != instanceId || header.TypeId != typeId || header.InstanceHash != instanceHash || header.TypeHash != typeHash)
                            throw new IOException("Asset identity does not match its filename.");
                    }
                    output.Write(copyBuffer, 0, read);
                    remaining -= read;
                }
            }
            Replace(temporary, target);
            if (hasCustomData)
            {
                Directory.CreateDirectory(customDataDirectory);
                File.Copy(sourceCustomData, customTarget, overwrite: true);
            }
            Header = header;
            return true;
        }
        catch (Exception)
        {
            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(customTarget)) File.Delete(customTarget);
            return false;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    void CommitCache()
    {
        CopyCache(sourceAssetPath, sourceCustomDataPath);
        Message(0, "{0} copied from cache", instanceText);
    }

    void CopyCache(string cachedAsset, string cachedCustomData)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { if (CopyAsset(cachedAsset, cachedCustomData)) return; }
            catch (Exception error) { Message(0, "{0} in use ({1}), re-attempting grab", instanceText, error.Message); }
            Thread.Sleep(500);
        }
        throw new BinaryAssetBuilderException(ErrorCode.InternalError,
            "Asset copy failed. Failure to commit asset {0} from network ({1}, {2})", instanceText, cachedAsset, cachedCustomData);
    }

    void ReadOutputHeader()
    {
        if (Header != null) return;
        string path = Path.Combine(assetDirectory, assetName);
        long length;
        using (FileStream input = File.OpenRead(path))
        {
            Header = new AssetHeader(); Header.LoadFromStream(input, bigEndian); length = input.Length;
        }
        if (Header.IsValidFileLength(length)) return;
        Message(2, "Invalid asset file detected. Deleting {0}.", path);
        File.Delete(path);
        string cachedAsset = string.IsNullOrEmpty(cacheDirectory) ? null : Path.Combine(cacheDirectory, assetName);
        if (cachedAsset == null || !File.Exists(cachedAsset) || (hasCustomData && !File.Exists(Path.Combine(cacheDirectory, customDataName))))
            throw new BinaryAssetBuilderException(ErrorCode.UnexpectedSize, "Can't recover from last error. Please restart to rebuild asset.");
        Message(1, "Checking build cache copy of asset.");
        using (FileStream input = File.OpenRead(cachedAsset))
        {
            Header = new AssetHeader(); Header.LoadFromStream(input, bigEndian); length = input.Length;
        }
        if (!Header.IsValidFileLength(length))
        {
            Message(2, "Invalid cached asset file detected. Deleting {0}.", cachedAsset);
            File.Delete(cachedAsset);
            throw new BinaryAssetBuilderException(ErrorCode.UnexpectedSize, "Can't recover from last error. Please restart to rebuild asset.");
        }
        CopyCache(cachedAsset, Path.Combine(cacheDirectory, customDataName));
        Message(0, "{0} copied from cache", instanceText);
    }

    static void TouchFile(string path)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists) return;
        DateTime now = DateTime.Now;
        info.LastAccessTime = now; info.LastWriteTime = now; info.CreationTime = now;
    }

    void TouchCache()
    {
        if (string.IsNullOrEmpty(cacheDirectory)) return;
        try
        {
            TouchFile(Path.Combine(cacheDirectory, assetName));
            if (hasCustomData) TouchFile(Path.Combine(cacheDirectory, customDataName));
        }
        catch (Exception) { }
    }

    // Different streams can populate the same build-cache identity. Publish a
    // complete file so another stream never opens a partially copied payload.
    static void PublishCacheFile(string source, string target)
    {
        if (File.Exists(target)) { TouchFile(target); return; }
        string temporary = target + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            try { File.Move(temporary, target); }
            catch (IOException)
            {
                if (!File.Exists(target)) throw;
                TouchFile(target);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    void CopyToCache()
    {
        if (string.IsNullOrEmpty(cacheDirectory)) return;
        Message(0, "Submitting to cache {0}", instanceText);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            PublishCacheFile(Path.Combine(assetDirectory, assetName), Path.Combine(cacheDirectory, assetName));
            if (hasCustomData) PublishCacheFile(outputCustomDataPath, Path.Combine(cacheDirectory, customDataName));
        }
        catch (Exception) { Message(0, "Failed to copy asset {0} to network", instanceText); }
    }
}
