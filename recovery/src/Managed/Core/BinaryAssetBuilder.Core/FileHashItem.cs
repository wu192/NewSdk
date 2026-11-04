using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using EALA.Hash;

namespace BinaryAssetBuilder.Core;

// Host metadata, obtained without opening the file body.
public sealed class FileHashInfo
{
	public bool Exists;
	public DateTime LastWriteTime;
	public string Identity;
}

[Serializable]
public class FileHashItem : IXmlSerializable
{
	public delegate void KeepAliveDelegate();

	public static KeepAliveDelegate _KeepAliveDelegate;

	private string _Path;

	private string _BuildConfiguration;

	private TargetPlatform _TargetPlatform;

	[NonSerialized]
	private FileState _State;

	private uint _Hash;

	private DateTime _LastDate = DateTime.MinValue;

	// Persist metadata in the existing BAB record. Imported original caches
	// lack this field and are hashed once before establishing a baseline.
	[OptionalField]
	private string _LastIdentity;

	[NonSerialized]
	private bool _Dirty;

	[NonSerialized]
	public DateTime _CurrentDate = DateTime.MaxValue;

	[NonSerialized]
	private bool _Exists;

	[NonSerialized]
	private FileHashInfo _Identity;

	[NonSerialized]
	private uint _CurrentHash;

	[NonSerialized]
	private bool _CurrentHashValid;

	public string Path => _Path;

	public string BuildConfiguration => _BuildConfiguration;

	public TargetPlatform TargetPlatform => _TargetPlatform;

	public uint Hash
	{
		get
		{
			if (_State < FileState.HashValid)
			{
				if (IsDirty)
				{
					if (Exists)
					{
						if (!_CurrentHashValid) CalculateHash();
						_Hash = _CurrentHash;
						_LastIdentity = _Identity?.Identity;
						_LastDate = _CurrentDate;
						_Dirty = false;
					}
					else
					{
						_Hash = 0u;
						// The missing-file result no longer has the previous
						// metadata identity. A same-time recreation must
						// recompute FastHash rather than reuse this zero value.
						_LastIdentity = null;
						_Identity = null;
						_CurrentHashValid = false;
						_LastDate = DateTime.MinValue;
					}
				}
				_State |= FileState.HashValid;
			}
			return _Hash;
		}
	}

	public bool IsDirty
	{
		get
		{
			if (_State < FileState.DateValid)
			{
				if (Exists)
				{
					_CurrentDate = _Identity == null ? File.GetLastWriteTime(_Path) : _Identity.LastWriteTime;
					if (_Identity == null && _LastDate == _CurrentDate) CalculateHash();
				}
				_Dirty = !Exists || _LastDate != _CurrentDate ||
					(_Identity != null ? _LastIdentity != _Identity.Identity : _CurrentHash != _Hash);
				_State = FileState.DateValid;
			}
			return _Dirty;
		}
	}

	public bool Exists
	{
		get
		{
			if (_State < FileState.ExistsValid)
			{
				_Identity = Settings.Current?.FileIdentity?.Invoke(_Path);
				_Exists = _Identity == null ? File.Exists(_Path) : _Identity.Exists;
				_State = FileState.ExistsValid;
			}
			return _Exists;
		}
	}

	public XmlSchema GetSchema()
	{
		return null;
	}

	public void WriteXml(XmlWriter writer)
	{
		writer.WriteStartElement("hi");
		writer.WriteAttributeString("d", $"{_Path};{_Hash};{_LastDate.ToBinary()};{_BuildConfiguration};{(int)_TargetPlatform}");
		if (_LastIdentity != null) writer.WriteAttributeString("identity", _LastIdentity);
		writer.WriteEndElement();
	}

	public void ReadXml(XmlReader reader)
	{
		_LastIdentity = reader.GetAttribute("identity");
		_State = FileState.AllInvalid;
		_Identity = null;
		_CurrentHashValid = false;
		reader.MoveToAttribute("d");
		string[] array = reader.Value.Split(';');
		_Path = array[0];
		_Hash = Convert.ToUInt32(array[1]);
		_LastDate = DateTime.FromBinary(Convert.ToInt64(array[2]));
		if (array.Length > 3)
		{
			if (array.Length > 4)
			{
				_TargetPlatform = (TargetPlatform)int.Parse(array[4]);
			}
			else
			{
				_TargetPlatform = TargetPlatform.Win32;
			}
			_BuildConfiguration = array[3];
		}
		else
		{
			_BuildConfiguration = "";
			_TargetPlatform = TargetPlatform.Win32;
		}
		_CurrentDate = DateTime.MaxValue;
		reader.Read();
	}

	public FileHashItem()
	{
	}

	public FileHashItem(string path, string configuration, TargetPlatform platform)
	{
		_Path = path;
		_BuildConfiguration = configuration;
		_TargetPlatform = platform;
	}

	public void Reset()
	{
		_State = FileState.AllInvalid;
		_CurrentDate = DateTime.MaxValue;
		_Identity = null;
		_CurrentHashValid = false;
		_Dirty = false;
	}

	private void CalculateHash()
	{
		using var performance = PerformanceScope.Measure("core.filehash.read");
		AsynchronousFileReader asynchronousFileReader = new AsynchronousFileReader(_Path);
		uint size = asynchronousFileReader.FileSize;
		uint hash = size;
		while (asynchronousFileReader.BeginRead())
		{
			hash = FastHash.GetHashCode(hash, asynchronousFileReader.CurrentChunk.Data, asynchronousFileReader.CurrentChunk.BytesRead);
			asynchronousFileReader.EndRead();
		}
		_CurrentHash = hash;
		_CurrentHashValid = true;
		Settings.Current?.FileHashRead?.Invoke(size);
	}
}
