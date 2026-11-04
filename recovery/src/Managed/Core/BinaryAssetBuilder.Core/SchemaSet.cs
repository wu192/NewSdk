using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using EALA.Hash;
using System.Xml;
using System.Xml.Schema;

namespace BinaryAssetBuilder.Core;

public class SchemaSet
{
	internal enum InstanceValueKind { None, WeakReference, AssetReference, FileReference, Hashable }

	internal sealed class InstanceTypeMetadata
	{
		internal InstanceValueKind Kind;
		internal string TypeIdText;
	}

	private class SchemaHashTable : SortedDictionary<XmlSchema, uint>
	{
	}

	private class DependencyList
	{
		public string TypeName;

		public List<uint> Children = new List<uint>();

		public IList<DependencyList> FixedUpChildren;

		public Set<uint> GrandChildren;

		public DependencyList(string typeName)
		{
			TypeName = typeName;
		}
	}

	public const string XmlNamespace = "uri:ea.com:eala:asset";

	private XmlSchemaSet _Schemas;

	private XmlSchemaType _XmlBaseAssetType;

	private XmlSchemaType _XmlBaseInheritableAsset;

	private XmlSchemaType _XmlAssetReferenceType;

	private XmlSchemaType _XmlWeakReferenceType;

	private XmlSchemaType _XmlFileReferenceType;

	private IDictionary<XmlSchemaType, bool> _HashableTypes;
	private Dictionary<XmlSchemaType, InstanceTypeMetadata> _InstanceTypeMetadata;
	private Dictionary<XmlSchemaAnnotated, string> _ReferenceTypeNames;

	private IDictionary<string, string> _SchemaPaths;

	private IDictionary<string, string> _SchemaContentHashes;

	private string _LoadedSchemaPath;
	private string _ContentIdentity;

	private string _CurrentSchema = "<unknown>";

	private IDictionary<string, StringCollection> _InheritanceMap;

	private IDictionary<uint, int> _AssetDependencies;

	private IDictionary<uint, DependencyList> _DependencyTree;

	private Set<uint> _CircularCheck = new Set<uint>();

	public XmlSchemaSet Schemas => _Schemas;

	public string ContentIdentity
	{
		get
		{
			if (_ContentIdentity != null) return _ContentIdentity;
			using (var bytes = new MemoryStream())
			using (var writer = new BinaryWriter(bytes))
			{
				foreach (var input in _SchemaContentHashes) { writer.Write(input.Key); writer.Write(input.Value); }
				writer.Flush(); bytes.Position = 0;
				return _ContentIdentity = FastHash.GetHashCode(bytes.ToArray()).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
			}
		}
	}

	public XmlSchemaType XmlBaseAssetType => _XmlBaseAssetType;

	public XmlSchemaType XmlBaseInheritableAsset => _XmlBaseInheritableAsset;

	public XmlSchemaType XmlAssetReferenceType => _XmlAssetReferenceType;

	public XmlSchemaType XmlWeakReferenceType => _XmlWeakReferenceType;

	public XmlSchemaType XmlFileReferenceType => _XmlFileReferenceType;

	public IDictionary<uint, int> AssetDependencies => _AssetDependencies;

	public bool IsHashableType(XmlSchemaType type)
	{
		return _HashableTypes.ContainsKey(type);
	}

	// These facts belong to the compiled schema snapshot, not to an XML node.
	// Most nodes share a small set of types; classify each inheritance chain once.
	internal InstanceTypeMetadata GetInstanceTypeMetadata(XmlSchemaType type)
	{
		if (_InstanceTypeMetadata.TryGetValue(type, out var metadata)) return metadata;
		var kind = InstanceValueKind.None;
		if (XmlSchemaType.IsDerivedFrom(type, _XmlWeakReferenceType, XmlSchemaDerivationMethod.None)) kind = InstanceValueKind.WeakReference;
		else if (XmlSchemaType.IsDerivedFrom(type, _XmlAssetReferenceType, XmlSchemaDerivationMethod.None)) kind = InstanceValueKind.AssetReference;
		else if (XmlSchemaType.IsDerivedFrom(type, _XmlFileReferenceType, XmlSchemaDerivationMethod.None)) kind = InstanceValueKind.FileReference;
		else if (IsHashableType(type)) kind = InstanceValueKind.Hashable;
		metadata = new InstanceTypeMetadata {
			Kind = kind,
			TypeIdText = type.Name == null ? null : HashProvider.GetCaseSensitiveSymbolHash(type.Name).ToString()
		};
		_InstanceTypeMetadata.Add(type, metadata);
		return metadata;
	}

	internal string GetReferenceTypeName(XmlSchemaAnnotated declaration)
	{
		if (_ReferenceTypeNames.TryGetValue(declaration, out var typeName)) return typeName;
		if (declaration.UnhandledAttributes != null)
			foreach (XmlAttribute attribute in declaration.UnhandledAttributes)
				if (attribute.NamespaceURI == "uri:ea.com:eala:asset:schema" && attribute.LocalName == "refType")
				{ typeName = attribute.Value; break; }
		// Absence is a schema fact too; do not rescan the same declaration.
		_ReferenceTypeNames.Add(declaration, typeName);
		return typeName;
	}

	private void ReadSchema(string baseDirectory, string schemaFile)
	{
		schemaFile = (Path.IsPathRooted(schemaFile) ? Path.GetFullPath(schemaFile) : Path.GetFullPath(Path.Combine(baseDirectory, schemaFile)));
		_CurrentSchema = schemaFile;
		if (_SchemaPaths.ContainsKey(schemaFile)) return;
		byte[] contents = File.ReadAllBytes(schemaFile);
		_SchemaPaths.Add(schemaFile, Settings.Current.FileIdentity?.Invoke(schemaFile).Identity);
		_SchemaContentHashes.Add(schemaFile, HashSchema(contents));
		XmlSchema xmlSchema;
		using (var input = new MemoryStream(contents, false))
		using (var reader = new StreamReader(input))
			xmlSchema = XmlSchema.Read(reader, null);
		foreach (XmlSchemaInclude include in xmlSchema.Includes)
		{
			ReadSchema(Path.GetDirectoryName(schemaFile), include.SchemaLocation);
		}
		xmlSchema.Includes.Clear();
		_Schemas.Add(xmlSchema);
	}

	private static string HashSchema(byte[] contents)
	{
		return FastHash.GetHashCode(contents).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
	}

	public XmlSchemaType GetXmlType(string typeName)
	{
		try
		{
			return Schemas.GlobalTypes[new XmlQualifiedName(typeName, "uri:ea.com:eala:asset")] as XmlSchemaType;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void BuildGrandchildren(DependencyList l)
	{
		if (l.FixedUpChildren != null)
		{
			return;
		}
		l.FixedUpChildren = new List<DependencyList>();
		l.GrandChildren = new Set<uint>();
		foreach (uint child in l.Children)
		{
			if (_DependencyTree.TryGetValue(child, out var value))
			{
				l.FixedUpChildren.Add(value);
			}
			l.GrandChildren.Add(child);
		}
		foreach (DependencyList fixedUpChild in l.FixedUpChildren)
		{
			BuildGrandchildren(fixedUpChild);
			if (fixedUpChild == l)
			{
				continue;
			}
			foreach (uint grandChild in fixedUpChild.GrandChildren)
			{
				l.GrandChildren.Add(grandChild);
			}
		}
	}

	private void CountReference(uint typeId, XmlAttribute[] unhandled)
	{
		foreach (XmlAttribute xmlAttribute in unhandled)
		{
			if (xmlAttribute.LocalName == "refType")
			{
				_DependencyTree[typeId].Children.Add(InstanceHandle.GetTypeId(xmlAttribute.Value));
				break;
			}
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaObjectCollection items)
	{
		foreach (XmlSchemaObject item in items)
		{
			if (item is XmlSchemaElement element)
			{
				BuildAssetDependencies(typeId, element);
			}
			if (item is XmlSchemaAttribute simple)
			{
				BuildAssetDependencies(typeId, simple);
			}
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaComplexContentExtension complex)
	{
		if (complex.Attributes != null)
		{
			BuildAssetDependencies(typeId, complex.Attributes);
		}
		if (complex.Particle != null)
		{
			BuildAssetDependencies(typeId, complex.Particle);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaSimpleContentExtension simple)
	{
		if (simple.Attributes != null)
		{
			BuildAssetDependencies(typeId, simple.Attributes);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaContentModel contentModel)
	{
		if (contentModel.Content is XmlSchemaComplexContentExtension complex)
		{
			BuildAssetDependencies(typeId, complex);
		}
		XmlSchemaSimpleContentExtension simple = contentModel.Content as XmlSchemaSimpleContentExtension;
		if (contentModel.Content is XmlSchemaSimpleContentExtension)
		{
			BuildAssetDependencies(typeId, simple);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaParticle particle)
	{
		if (particle is XmlSchemaChoice xmlSchemaChoice)
		{
			BuildAssetDependencies(typeId, xmlSchemaChoice.Items);
		}
		if (particle is XmlSchemaSequence xmlSchemaSequence)
		{
			BuildAssetDependencies(typeId, xmlSchemaSequence.Items);
		}
		if (particle is XmlSchemaAll xmlSchemaAll)
		{
			BuildAssetDependencies(typeId, xmlSchemaAll.Items);
		}
		if (particle is XmlSchemaElement element)
		{
			BuildAssetDependencies(typeId, element);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaComplexType complex)
	{
		if (complex.Attributes != null)
		{
			BuildAssetDependencies(typeId, complex.Attributes);
		}
		if (complex.Particle != null)
		{
			BuildAssetDependencies(typeId, complex.Particle);
		}
		if (complex.ContentModel != null)
		{
			BuildAssetDependencies(typeId, complex.ContentModel);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaAttribute simple)
	{
		bool flag = false;
		if (simple.AttributeSchemaType != null)
		{
			BuildAssetDependencies(typeId, simple.AttributeSchemaType);
			flag = XmlSchemaType.IsDerivedFrom(simple.AttributeSchemaType, _XmlAssetReferenceType, XmlSchemaDerivationMethod.None);
		}
		if (flag && simple.UnhandledAttributes != null)
		{
			CountReference(typeId, simple.UnhandledAttributes);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaElement element)
	{
		bool flag = false;
		if (element.ElementSchemaType != null)
		{
			BuildAssetDependencies(typeId, element.ElementSchemaType);
			flag = XmlSchemaType.IsDerivedFrom(element.ElementSchemaType, _XmlAssetReferenceType, XmlSchemaDerivationMethod.None);
		}
		if (flag && element.UnhandledAttributes != null)
		{
			CountReference(typeId, element.UnhandledAttributes);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaSimpleType simple)
	{
		if (XmlSchemaType.IsDerivedFrom(simple, _XmlAssetReferenceType, XmlSchemaDerivationMethod.None) && simple.UnhandledAttributes != null)
		{
			CountReference(typeId, simple.UnhandledAttributes);
		}
	}

	private void BuildAssetDependencies(uint typeId, XmlSchemaType type)
	{
		uint num = 0u;
		if (type.Name != null)
		{
			num = InstanceHandle.GetTypeId(type.Name);
			if (_CircularCheck.Contains(num))
			{
				return;
			}
			_CircularCheck.Add(num);
		}
		if (type.BaseXmlSchemaType != null)
		{
			BuildAssetDependencies(typeId, type.BaseXmlSchemaType);
		}
		if (type is XmlSchemaComplexType complex)
		{
			BuildAssetDependencies(typeId, complex);
		}
		else if (type is XmlSchemaSimpleType simple)
		{
			BuildAssetDependencies(typeId, simple);
		}
		else if (num != 0)
		{
			_CircularCheck.Remove(num);
		}
	}

	private void LoadSchemas(bool countDependencies)
	{
		var previousPaths = _SchemaPaths == null ? new List<string>() : new List<string>(_SchemaPaths.Keys);
		// A failed load must not make its partially read inputs appear reusable.
		_LoadedSchemaPath = null;
		_ContentIdentity = null;
		_AssetDependencies = null;
		_CircularCheck = new Set<uint>();
		_SchemaPaths = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		_SchemaContentHashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		_Schemas = new XmlSchemaSet();
		_InheritanceMap = new SortedDictionary<string, StringCollection>();
		_DependencyTree = new SortedDictionary<uint, DependencyList>();
		_HashableTypes = new Dictionary<XmlSchemaType, bool>();
		_InstanceTypeMetadata = new Dictionary<XmlSchemaType, InstanceTypeMetadata>();
		_ReferenceTypeNames = new Dictionary<XmlSchemaAnnotated, string>();
		try
		{
			previousPaths.Add(Path.GetFullPath(Settings.Current.SchemaPath));
			Settings.Current.SchemaFileIdentities?.Invoke(previousPaths);
			using (PerformanceScope.Measure("core.schema.read"))
			ReadSchema(Directory.GetCurrentDirectory(), Settings.Current.SchemaPath);

		}
		catch (Exception innerException)
		{
			throw new BinaryAssetBuilderException(innerException, ErrorCode.ReadingSchema, "Error encountered in schema '{0}'", _CurrentSchema);
		}
		using (PerformanceScope.Measure("core.schema.compile")) _Schemas.Compile();
		using var performance = PerformanceScope.Measure("core.schema.metadata");
		SetupHelperSchemaTypes();
		IDictionary<string, StringCollection> dictionary = new SortedDictionary<string, StringCollection>();
		dictionary.Add("BaseAssetType", new StringCollection());
		foreach (XmlSchemaType value2 in Schemas.GlobalTypes.Values)
		{
			if (!XmlSchemaType.IsDerivedFrom(value2, _XmlBaseAssetType, XmlSchemaDerivationMethod.None))
			{
				continue;
			}
			if (countDependencies)
			{
				uint typeId = InstanceHandle.GetTypeId(value2.Name);
				_DependencyTree[typeId] = new DependencyList(value2.Name);
				BuildAssetDependencies(typeId, value2);
			}
			if (value2.BaseXmlSchemaType != null && !string.IsNullOrEmpty(value2.BaseXmlSchemaType.Name))
			{
				StringCollection value = null;
				if (!dictionary.TryGetValue(value2.BaseXmlSchemaType.Name, out value))
				{
					value = new StringCollection();
					dictionary.Add(value2.BaseXmlSchemaType.Name, value);
				}
				value.Add(value2.Name);
			}
		}
		if (countDependencies)
		{
			_AssetDependencies = new SortedDictionary<uint, int>();
			foreach (KeyValuePair<uint, DependencyList> item in _DependencyTree)
			{
				BuildGrandchildren(item.Value);
				_AssetDependencies[item.Key] = item.Value.GrandChildren.Count;
			}
			_DependencyTree.Clear();
			_DependencyTree = null;
		}
		foreach (string key in dictionary.Keys)
		{
			FlattenInheritanceTree(key, dictionary);
		}
		// Reuse the load's directory snapshot; do not enumerate every input a
		// second time. Standalone hosts may supply only the batch callback.
		if (Settings.Current.FileIdentity == null && Settings.Current.SchemaFileIdentities != null)
			_SchemaPaths = Settings.Current.SchemaFileIdentities(_SchemaPaths.Keys);
		_LoadedSchemaPath = Path.GetFullPath(Settings.Current.SchemaPath);
	}

	private void SetupHelperSchemaTypes()
	{
		_XmlBaseAssetType = Schemas.GlobalTypes[new XmlQualifiedName("BaseAssetType", "uri:ea.com:eala:asset")] as XmlSchemaType;
		_XmlBaseInheritableAsset = Schemas.GlobalTypes[new XmlQualifiedName("BaseInheritableAsset", "uri:ea.com:eala:asset")] as XmlSchemaType;
		_XmlAssetReferenceType = Schemas.GlobalTypes[new XmlQualifiedName("AssetReference", "uri:ea.com:eala:asset")] as XmlSchemaType;
		_XmlWeakReferenceType = Schemas.GlobalTypes[new XmlQualifiedName("WeakReference", "uri:ea.com:eala:asset")] as XmlSchemaType;
		_XmlFileReferenceType = Schemas.GlobalTypes[new XmlQualifiedName("FileReference", "uri:ea.com:eala:asset")] as XmlSchemaType;
		SetupHashableTypes();
	}

	private void SetupHashableTypes()
	{
		StringHashBinDescriptor[] stringHashBinDescriptors = Settings.Current.StringHashBinDescriptors;
		foreach (StringHashBinDescriptor stringHashBinDescriptor in stringHashBinDescriptors)
		{
			_HashableTypes.Add(Schemas.GlobalTypes[new XmlQualifiedName(stringHashBinDescriptor.SchemaTypeName, "uri:ea.com:eala:asset")] as XmlSchemaType, value: true);
		}
	}

	public SchemaSet(bool countDependencies)
	{
		LoadSchemas(countDependencies);
	}

	public void ReloadIfChanged(bool countDependencies)
	{
		bool changed = countDependencies && _AssetDependencies == null;
		string requested = Path.GetFullPath(Settings.Current.SchemaPath);
		if (!string.Equals(requested, _LoadedSchemaPath, StringComparison.OrdinalIgnoreCase) ||
			_SchemaPaths == null || _SchemaContentHashes == null)
			changed = true;
		if (!changed)
		{
			var identities = Settings.Current.SchemaFileIdentities?.Invoke(_SchemaPaths.Keys);
			var refreshed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var schemaPath in _SchemaPaths)
			{
				// The host stamp includes file ID, size, write time and change time.
				// Standalone Core without an identity provider retains content checks.
				string stamp = identities == null ? null : identities[schemaPath.Key];
				if (stamp != null && stamp == schemaPath.Value) continue;
				if (!File.Exists(schemaPath.Key)) { changed = true; break; }
				string content = HashSchema(File.ReadAllBytes(schemaPath.Key));
				if (!string.Equals(content, _SchemaContentHashes[schemaPath.Key], StringComparison.Ordinal))
				{
					changed = true;
					break;
				}
				refreshed.Add(schemaPath.Key, stamp);
			}
			if (!changed) foreach (var input in refreshed) _SchemaPaths[input.Key] = input.Value;
		}
		if (changed) LoadSchemas(countDependencies);
	}

	public StringCollection GetDerivedTypes(string typeName)
	{
		StringCollection value = null;
		_InheritanceMap.TryGetValue(typeName, out value);
		return value;
	}

	private bool FlattenInheritanceTree(string typeName, IDictionary<string, StringCollection> dictionary)
	{
		if (_InheritanceMap.ContainsKey(typeName))
		{
			return true;
		}
		StringCollection value = null;
		if (!dictionary.TryGetValue(typeName, out value))
		{
			return false;
		}
		StringCollection stringCollection = new StringCollection();
		StringEnumerator enumerator = value.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				string current = enumerator.Current;
				stringCollection.Add(current);
				bool flag = true;
				if (!_InheritanceMap.ContainsKey(current))
				{
					flag = FlattenInheritanceTree(current, dictionary);
				}
				if (!flag)
				{
					continue;
				}
				StringEnumerator enumerator2 = _InheritanceMap[current].GetEnumerator();
				try
				{
					while (enumerator2.MoveNext())
					{
						string current2 = enumerator2.Current;
						stringCollection.Add(current2);
					}
				}
				finally
				{
					if (enumerator2 is IDisposable disposable)
					{
						disposable.Dispose();
					}
				}
			}
		}
		finally
		{
			if (enumerator is IDisposable disposable2)
			{
				disposable2.Dispose();
			}
		}
		_InheritanceMap.Add(typeName, stringCollection);
		return true;
	}
}
