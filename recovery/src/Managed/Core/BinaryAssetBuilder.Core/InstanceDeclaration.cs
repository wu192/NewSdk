using System;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using BinaryAssetBuilder.Utility;

namespace BinaryAssetBuilder.Core;

[Serializable]
public class InstanceDeclaration : IXmlSerializable
{
	[Serializable]
	private class LastState
	{
		public InstanceHandle Handle;

		public uint ProcessingHash;

		public List2<InstanceHandle> ReferencedInstances;

		public List2<InstanceHandle> WeakReferencedInstances;

		public List2<string> ReferencedFiles;

		public InstanceHandle InheritFromHandle;

		public uint InheritFromXmlHash;

		public uint PrevalidationXmlHash;

		public bool IsInheritable;

		public bool HasCustomData;

		public LastState(CurrentState current)
		{
			Handle = current.Handle;
			ProcessingHash = current.ProcessingHash;
			ReferencedInstances = ((current.ReferencedInstances.Count > 0) ? new List2<InstanceHandle>(current.ReferencedInstances) : null);
			WeakReferencedInstances = ((current.WeakReferencedInstances.Count > 0) ? new List2<InstanceHandle>(current.WeakReferencedInstances) : null);
			ReferencedFiles = ((current.ReferencedFiles.Count > 0) ? new List2<string>(current.ReferencedFiles) : null);
			IsInheritable = current.IsInheritable;
			HasCustomData = current.HasCustomData;
			InheritFromHandle = current.InheritFromHandle;
			InheritFromXmlHash = current.InheritFromXmlHash;
			PrevalidationXmlHash = current.PrevalidationXmlHash;
		}

		public LastState(XmlReader reader)
		{
			reader.MoveToAttribute("d");
			string[] array = reader.Value.Split(';');
			ProcessingHash = Convert.ToUInt32(array[0]);
			InheritFromXmlHash = Convert.ToUInt32(array[1]);
			PrevalidationXmlHash = Convert.ToUInt32(array[2]);
			IsInheritable = Convert.ToBoolean(array[3]);
			HasCustomData = Convert.ToBoolean(array[4]);
			reader.MoveToElement();
			reader.Read();
			Handle = new InstanceHandle();
			Handle.ReadXml(reader);
			if (reader.Name == "ih")
			{
				InheritFromHandle = new InstanceHandle();
				InheritFromHandle.ReadXml(reader);
			}
			object obj = XmlHelper.ReadStringArrayElement(reader, "rf");
			ReferencedFiles = ((obj == null) ? null : new List2<string>(obj as string[]));
			obj = XmlHelper.ReadCollection(reader, "ri", typeof(InstanceHandle));
			ReferencedInstances = ((obj == null) ? null : new List2<InstanceHandle>(obj as InstanceHandle[]));
			obj = XmlHelper.ReadCollection(reader, "wri", typeof(InstanceHandle));
			WeakReferencedInstances = ((obj == null) ? null : new List2<InstanceHandle>(obj as InstanceHandle[]));
			reader.Read();
		}

		public void WriteXml(XmlWriter writer)
		{
			writer.WriteStartElement("id");
			writer.WriteAttributeString("d", $"{ProcessingHash};{InheritFromXmlHash};{PrevalidationXmlHash};{IsInheritable};{HasCustomData}");
			Handle.WriteXml(writer);
			if (InheritFromHandle != null)
			{
				InheritFromHandle.WriteXml(writer);
			}
			XmlHelper.WriteStringArrayElement(writer, "rf", (ReferencedFiles == null) ? null : ReferencedFiles.ToArray());
			XmlHelper.WriteCollection(writer, "ri", ReferencedInstances);
			XmlHelper.WriteCollection(writer, "wri", WeakReferencedInstances);
			writer.WriteEndElement();
		}
	}

	private class CurrentState
	{
		public AssetDeclarationDocument Document;

		public InstanceHandle Handle;

		public uint ProcessingHash;

		public XmlNode XmlNode;

		public List2<InstanceHandle> ReferencedInstances;

		public List2<InstanceHandle> WeakReferencedInstances;

		public List2<string> ReferencedFiles;

		public bool IsInheritable;

		public bool HasCustomData;

		public string CustomDataPath;

		public InstanceHandle InheritFromHandle;

		public uint InheritFromXmlHash;

		public uint PrevalidationXmlHash;

		public List2<InstanceHandle> ValidatedReferencedInstances;

		public InstanceHandleSet AllDependentInstances;

		public CurrentState(AssetDeclarationDocument document)
		{
			Document = document;
		}

		public CurrentState CreateBackendView()
		{
			var view = (CurrentState)MemberwiseClone();
			view.Document = Document.CreateBackendView();
			return view;
		}

		public void FromLast(LastState last)
		{
			Handle = last.Handle;
			ProcessingHash = last.ProcessingHash;
			ReferencedInstances = ((last.ReferencedInstances != null) ? new List2<InstanceHandle>(last.ReferencedInstances) : new List2<InstanceHandle>());
			WeakReferencedInstances = ((last.WeakReferencedInstances != null) ? new List2<InstanceHandle>(last.WeakReferencedInstances) : new List2<InstanceHandle>());
			ReferencedFiles = ((last.ReferencedFiles != null) ? new List2<string>(last.ReferencedFiles) : new List2<string>());
			IsInheritable = last.IsInheritable;
			HasCustomData = last.HasCustomData;
			InheritFromHandle = last.InheritFromHandle;
			InheritFromXmlHash = last.InheritFromXmlHash;
			PrevalidationXmlHash = last.PrevalidationXmlHash;
		}

		public void FromScratch()
		{
			ReferencedInstances = new List2<InstanceHandle>();
			WeakReferencedInstances = new List2<InstanceHandle>();
			ReferencedFiles = new List2<string>();
		}
	}

	private LastState _Last;

	[NonSerialized]
	private CurrentState _Current;

	public List2<InstanceHandle> ValidatedReferencedInstances
	{
		get
		{
			return _Current.ValidatedReferencedInstances;
		}
		set
		{
			_Current.ValidatedReferencedInstances = value;
		}
	}

	public InstanceHandleSet AllDependentInstances
	{
		get
		{
			return _Current.AllDependentInstances;
		}
		set
		{
			_Current.AllDependentInstances = value;
		}
	}

	public List2<InstanceHandle> ReferencedInstances => _Current.ReferencedInstances;

	public List2<InstanceHandle> WeakReferencedInstances => _Current.WeakReferencedInstances;

	public List2<string> ReferencedFiles => _Current.ReferencedFiles;

	public AssetDeclarationDocument Document => _Current.Document;

	public InstanceHandle Handle => _Current.Handle;

	public uint ProcessingHash
	{
		get
		{
			return _Current.ProcessingHash;
		}
		set
		{
			_Current.ProcessingHash = value;
		}
	}

	public XmlNode Node => _Current.XmlNode;

	public XmlNode XmlNode
	{
		get
		{
			return _Current.XmlNode;
		}
		set
		{
			_Current.XmlNode = value;
			if (value == null)
			{
				return;
			}
			XmlAttribute xmlAttribute = value.Attributes["id"];
			if (xmlAttribute != null)
			{
				_Current.Handle = new InstanceHandle(value.Name, xmlAttribute.Value);
				xmlAttribute = value.Attributes["inheritFrom"];
				if (xmlAttribute != null && xmlAttribute.Value != "")
				{
					if (!xmlAttribute.Value.Contains(":"))
					{
						_Current.InheritFromHandle = new InstanceHandle(value.Name, xmlAttribute.Value);
					}
					else
					{
						_Current.InheritFromHandle = new InstanceHandle(xmlAttribute.Value);
					}
				}
				value.Attributes.Remove(xmlAttribute);
				return;
			}
			throw new BinaryAssetBuilderException(ErrorCode.NoIdAttributeForAsset, "Node of type {0} in file://{1} has no id attribute", value.Name, _Current.Document.SourcePath);
		}
	}

	public bool IsInheritable
	{
		get
		{
			return _Current.IsInheritable;
		}
		set
		{
			_Current.IsInheritable = value;
		}
	}

	public bool HasCustomData
	{
		get
		{
			return _Current.HasCustomData;
		}
		set
		{
			_Current.HasCustomData = value;
		}
	}

	public string CustomDataPath
	{
		get
		{
			return _Current.CustomDataPath;
		}
		set
		{
			_Current.CustomDataPath = value;
		}
	}

	public InstanceHandle InheritFromHandle => _Current.InheritFromHandle;

	public uint InheritFromXmlHash
	{
		get
		{
			return _Current.InheritFromXmlHash;
		}
		set
		{
			_Current.InheritFromXmlHash = value;
		}
	}

	public uint PrevalidationXmlHash
	{
		get
		{
			return _Current.PrevalidationXmlHash;
		}
		set
		{
			_Current.PrevalidationXmlHash = value;
		}
	}

	public XmlSchema GetSchema()
	{
		return null;
	}

	public void WriteXml(XmlWriter writer)
	{
		if (_Last == null)
		{
			MakeCacheable();
		}
		_Last.WriteXml(writer);
	}

	public void ReadXml(XmlReader reader)
	{
		_Last = new LastState(reader);
	}

	public InstanceDeclaration()
	{
	}

	public InstanceDeclaration(AssetDeclarationDocument document)
	{
		Initialize(document);
	}

	// Only certified read-only backends may borrow this finalized input. A shallow
	// state view isolates the CDATA pathname and snapshots document metadata; XML,
	// handles and reference collections remain retained by the document version.
	// Bypass XmlNode's semantic setter (which removes inheritFrom and rehashes IDs).
	public InstanceDeclaration CreateBackendView()
	{
		return new InstanceDeclaration { _Current = _Current.CreateBackendView() };
	}

	public void Initialize(AssetDeclarationDocument document)
	{
		if (_Current == null)
		{
			_Current = new CurrentState(document);
			if (_Last != null)
			{
				_Current.FromLast(_Last);
			}
			else
			{
				_Current.FromScratch();
			}
		}
	}

	// Reenter a saved session through the same lazy initialization used after
	// ReadXml. Per-stream XML, resolved references and CDATA paths are transient.
	internal void ResetState() { _Current = null; }

	public void InitializePrecompiled(Asset asset)
	{
		_Current = new CurrentState(null);
		_Current.FromScratch();
		_Current.Handle = new InstanceHandle(asset.TypeName, asset.InstanceName);
		_Current.Handle.TypeHash = asset.TypeHash;
		_Current.Handle.InstanceHash = asset.InstanceHash;
		_Current.IsInheritable = false;
		_Current.HasCustomData = false;
	}

	public void MakeComplete()
	{
		_Last = null;
	}

	public void MakeCacheable()
	{
		if (_Last == null)
		{
			_Last = new LastState(_Current);
		}
	}

	public override bool Equals(object obj)
	{
		return ((InstanceDeclaration)obj).Handle == Handle;
	}

	public override int GetHashCode()
	{
		return _Current.Handle.GetHashCode();
	}

	public override string ToString()
	{
		return _Current.Handle.ToString();
	}

	public void CacheFromInstance(InstanceDeclaration current)
	{
		_Last = new LastState(current._Current);
	}
}
