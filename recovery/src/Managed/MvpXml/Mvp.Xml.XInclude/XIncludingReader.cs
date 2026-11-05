using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security;
using System.Xml;
using System.Xml.Schema;
using Mvp.Xml.Common;
using Mvp.Xml.XPointer;

namespace Mvp.Xml.XInclude;

public class XIncludingReader : XmlReader, IXmlLineInfo
{
	private XIncludeKeywords _keywords;

	private XmlReader _reader;

	private Stack<XmlReader> _readers;

	private Uri _topBaseUri;

	private bool _topLevel;

	private bool _gotTopIncludedElem;

	private bool _gotElement;

	private XIncludingReaderState _state;

	private XmlNameTable _nameTable;

	private bool _normalization;

	private WhitespaceHandling _whiteSpaceHandling;

	private bool _relativeBaseUri = true;

	private FallbackState _fallbackState;

	private FallbackState _prevFallbackState;

	private XmlResolver _xmlResolver;

	private bool _exposeTextAsCDATA;

	private bool _differentLang;

	private static IDictionary<string, WeakReference> _cache;

	public override int AttributeCount
	{
		get
		{
			if (_topLevel)
			{
				int num = _reader.AttributeCount;
				if (_reader.GetAttribute(_keywords.XmlBase) == null)
				{
					num++;
				}
				if (_differentLang)
				{
					num++;
				}
				return num;
			}
			return _reader.AttributeCount;
		}
	}

	public override string BaseURI => _reader.BaseURI;

	// Forward the active included reader's location. An XPointer/cache reader
	// without source coordinates reports no line information rather than the
	// suspended parent's xi:include position.
	public bool HasLineInfo() => _reader is IXmlLineInfo info && info.HasLineInfo();

	public int LineNumber => (_reader as IXmlLineInfo)?.LineNumber ?? 0;

	public int LinePosition => (_reader as IXmlLineInfo)?.LinePosition ?? 0;

	public override bool HasValue
	{
		get
		{
			if (_state == XIncludingReaderState.Default)
			{
				return _reader.HasValue;
			}
			return true;
		}
	}

	public override bool IsDefault
	{
		get
		{
			if (_state == XIncludingReaderState.Default)
			{
				return _reader.IsDefault;
			}
			return false;
		}
	}

	public override string Name
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
				return _keywords.XmlBase;
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return string.Empty;
			case XIncludingReaderState.ExposingXmlLangAttr:
				return _keywords.XmlLang;
			default:
				return _reader.Name;
			}
		}
	}

	public override string LocalName
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
				return _keywords.Base;
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return string.Empty;
			case XIncludingReaderState.ExposingXmlLangAttr:
				return _keywords.Lang;
			default:
				return _reader.LocalName;
			}
		}
	}

	public override string NamespaceURI
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlLangAttr:
				return _keywords.XmlNamespace;
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return string.Empty;
			default:
				return _reader.NamespaceURI;
			}
		}
	}

	public override XmlNameTable NameTable => _nameTable;

	public override XmlNodeType NodeType
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlLangAttr:
				return XmlNodeType.Attribute;
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return XmlNodeType.Text;
			default:
				return _reader.NodeType;
			}
		}
	}

	public override string Prefix
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlLangAttr:
				return _keywords.Xml;
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return string.Empty;
			default:
				return _reader.Prefix;
			}
		}
	}

	public override char QuoteChar
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlLangAttr:
				return '"';
			default:
				return _reader.QuoteChar;
			}
		}
	}

	public override int Depth
	{
		get
		{
			if (_readers.Count == 0)
			{
				return _reader.Depth;
			}
			return _readers.Peek().Depth + _reader.Depth;
		}
	}

	public override bool EOF => _reader.EOF;

	public override bool IsEmptyElement => _reader.IsEmptyElement;

	public override ReadState ReadState => _reader.ReadState;

	public override string this[int i] => GetAttribute(i);

	public override string this[string name] => GetAttribute(name);

	public override string this[string name, string namespaceURI] => GetAttribute(name, namespaceURI);

	public override string XmlLang => _reader.XmlLang;

	public override XmlSpace XmlSpace => _reader.XmlSpace;

	public override string Value
	{
		get
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
				if (_reader.BaseURI == string.Empty)
				{
					return string.Empty;
				}
				if (_relativeBaseUri)
				{
					Uri uri = new Uri(_reader.BaseURI);
					return _topBaseUri.MakeRelativeUri(uri).ToString();
				}
				return _reader.BaseURI;
			case XIncludingReaderState.ExposingXmlLangAttr:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				return _reader.XmlLang;
			default:
				return _reader.Value;
			}
		}
	}

	public bool Normalization
	{
		get
		{
			return _normalization;
		}
		set
		{
			_normalization = value;
		}
	}

	public WhitespaceHandling WhitespaceHandling
	{
		get
		{
			return _whiteSpaceHandling;
		}
		set
		{
			_whiteSpaceHandling = value;
		}
	}

	public XmlResolver XmlResolver
	{
		set
		{
			_xmlResolver = value;
		}
	}

	public bool MakeRelativeBaseUri
	{
		get
		{
			return _relativeBaseUri;
		}
		set
		{
			_relativeBaseUri = value;
		}
	}

	public bool ExposeTextInclusionsAsCDATA
	{
		get
		{
			return _exposeTextAsCDATA;
		}
		set
		{
			_exposeTextAsCDATA = value;
		}
	}

	public XIncludingReader(XmlReader reader)
	{
		if (reader is XmlTextReader xmlTextReader)
		{
			XmlValidatingReader xmlValidatingReader = new XmlValidatingReader(reader);
			xmlValidatingReader.ValidationType = ValidationType.None;
			xmlValidatingReader.EntityHandling = EntityHandling.ExpandEntities;
			xmlValidatingReader.ValidationEventHandler += ValidationCallback;
			_normalization = xmlTextReader.Normalization;
			_whiteSpaceHandling = xmlTextReader.WhitespaceHandling;
			_reader = xmlValidatingReader;
		}
		else
		{
			_reader = reader;
		}
		_nameTable = reader.NameTable;
		_keywords = new XIncludeKeywords(NameTable);
		if (_reader.BaseURI != "")
		{
			_topBaseUri = new Uri(_reader.BaseURI);
		}
		else
		{
			_relativeBaseUri = false;
			_topBaseUri = new Uri(Assembly.GetExecutingAssembly().Location);
		}
		_readers = new Stack<XmlReader>();
		_state = XIncludingReaderState.Default;
	}

	public XIncludingReader(string url)
		: this(new XmlBaseAwareXmlTextReader(url))
	{
	}

	public XIncludingReader(string url, XmlNameTable nt)
		: this(new XmlBaseAwareXmlTextReader(url, nt))
	{
	}

	public XIncludingReader(TextReader reader)
		: this(new XmlBaseAwareXmlTextReader(reader))
	{
	}

	public XIncludingReader(string url, TextReader reader)
		: this(new XmlBaseAwareXmlTextReader(url, reader))
	{
	}

	public XIncludingReader(TextReader reader, XmlNameTable nt)
		: this(new XmlBaseAwareXmlTextReader(reader, nt))
	{
	}

	public XIncludingReader(string url, TextReader reader, XmlNameTable nt)
		: this(new XmlBaseAwareXmlTextReader(url, reader, nt))
	{
	}

	public XIncludingReader(Stream input)
		: this(new XmlBaseAwareXmlTextReader(input))
	{
	}

	public XIncludingReader(string url, Stream input)
		: this(new XmlBaseAwareXmlTextReader(url, input))
	{
	}

	public XIncludingReader(Stream input, XmlNameTable nt)
		: this(new XmlBaseAwareXmlTextReader(input, nt))
	{
	}

	public XIncludingReader(string url, Stream input, XmlNameTable nt)
		: this(new XmlBaseAwareXmlTextReader(url, input, nt))
	{
	}

	public override void Close()
	{
		if (_reader != null)
		{
			_reader.Close();
		}
		while (_readers.Count > 0)
		{
			_reader = _readers.Pop();
			if (_reader != null)
			{
				_reader.Close();
			}
		}
	}

	public override string GetAttribute(int i)
	{
		return _reader.GetAttribute(i);
	}

	public override string GetAttribute(string name)
	{
		if (_topLevel)
		{
			if (XIncludeKeywords.Equals(name, _keywords.XmlBase))
			{
				return _reader.BaseURI;
			}
			if (XIncludeKeywords.Equals(name, _keywords.XmlLang))
			{
				return _reader.XmlLang;
			}
		}
		return _reader.GetAttribute(name);
	}

	public override string GetAttribute(string name, string namespaceURI)
	{
		if (_topLevel)
		{
			if (XIncludeKeywords.Equals(name, _keywords.Base) && XIncludeKeywords.Equals(namespaceURI, _keywords.XmlNamespace))
			{
				return _reader.BaseURI;
			}
			if (XIncludeKeywords.Equals(name, _keywords.Lang) && XIncludeKeywords.Equals(namespaceURI, _keywords.XmlNamespace))
			{
				return _reader.XmlLang;
			}
		}
		return _reader.GetAttribute(name, namespaceURI);
	}

	public override string LookupNamespace(string prefix)
	{
		return _reader.LookupNamespace(prefix);
	}

	public override void MoveToAttribute(int i)
	{
		_reader.MoveToAttribute(i);
	}

	public override bool MoveToAttribute(string name)
	{
		if (_topLevel)
		{
			if (XIncludeKeywords.Equals(name, _keywords.XmlBase))
			{
				_state = XIncludingReaderState.ExposingXmlBaseAttr;
				return true;
			}
			if (XIncludeKeywords.Equals(name, _keywords.XmlLang))
			{
				_state = XIncludingReaderState.ExposingXmlLangAttr;
				return true;
			}
		}
		return _reader.MoveToAttribute(name);
	}

	public override bool MoveToAttribute(string name, string ns)
	{
		if (_topLevel)
		{
			if (XIncludeKeywords.Equals(name, _keywords.Base) && XIncludeKeywords.Equals(ns, _keywords.XmlNamespace))
			{
				_state = XIncludingReaderState.ExposingXmlBaseAttr;
				return true;
			}
			if (XIncludeKeywords.Equals(name, _keywords.Lang) && XIncludeKeywords.Equals(ns, _keywords.XmlNamespace))
			{
				_state = XIncludingReaderState.ExposingXmlLangAttr;
				return true;
			}
		}
		return _reader.MoveToAttribute(name, ns);
	}

	public override bool MoveToElement()
	{
		return _reader.MoveToElement();
	}

	public override bool MoveToFirstAttribute()
	{
		if (_topLevel)
		{
			bool flag = _reader.MoveToFirstAttribute();
			if (flag)
			{
				if (_reader.Name == _keywords.XmlBase || _reader.Name == _keywords.XmlLang)
				{
					return MoveToNextAttribute();
				}
				return flag;
			}
			_state = XIncludingReaderState.ExposingXmlBaseAttr;
			return true;
		}
		return _reader.MoveToFirstAttribute();
	}

	public override bool MoveToNextAttribute()
	{
		if (_topLevel)
		{
			switch (_state)
			{
			case XIncludingReaderState.ExposingXmlBaseAttr:
			case XIncludingReaderState.ExposingXmlBaseAttrValue:
				if (_differentLang)
				{
					_state = XIncludingReaderState.ExposingXmlLangAttr;
					return true;
				}
				_state = XIncludingReaderState.Default;
				return false;
			case XIncludingReaderState.ExposingXmlLangAttr:
			case XIncludingReaderState.ExposingXmlLangAttrValue:
				_state = XIncludingReaderState.Default;
				return false;
			default:
			{
				bool flag = _reader.MoveToNextAttribute();
				if (flag)
				{
					if (_reader.Name == _keywords.XmlBase || _reader.Name == _keywords.XmlLang)
					{
						return MoveToNextAttribute();
					}
					return flag;
				}
				_state = XIncludingReaderState.ExposingXmlBaseAttr;
				return true;
			}
			}
		}
		return _reader.MoveToNextAttribute();
	}

	public override bool ReadAttributeValue()
	{
		switch (_state)
		{
		case XIncludingReaderState.ExposingXmlBaseAttr:
			_state = XIncludingReaderState.ExposingXmlBaseAttrValue;
			return true;
		case XIncludingReaderState.ExposingXmlBaseAttrValue:
			return false;
		case XIncludingReaderState.ExposingXmlLangAttr:
			_state = XIncludingReaderState.ExposingXmlLangAttrValue;
			return true;
		case XIncludingReaderState.ExposingXmlLangAttrValue:
			return false;
		default:
			return _reader.ReadAttributeValue();
		}
	}

	public override void ResolveEntity()
	{
		_reader.ResolveEntity();
	}

	public override string ReadInnerXml()
	{
		switch (_state)
		{
		case XIncludingReaderState.ExposingXmlBaseAttr:
			return _reader.BaseURI;
		case XIncludingReaderState.ExposingXmlBaseAttrValue:
			return string.Empty;
		case XIncludingReaderState.ExposingXmlLangAttr:
			return _reader.XmlLang;
		case XIncludingReaderState.ExposingXmlLangAttrValue:
			return string.Empty;
		default:
			if (NodeType == XmlNodeType.Element)
			{
				int depth = Depth;
				if (Read())
				{
					StringWriter stringWriter = new StringWriter();
					XmlTextWriter xmlTextWriter = new XmlTextWriter(stringWriter);
					while (Depth > depth)
					{
						xmlTextWriter.WriteNode(this, defattr: false);
					}
					xmlTextWriter.Close();
					return stringWriter.ToString();
				}
				return string.Empty;
			}
			if (NodeType == XmlNodeType.Attribute)
			{
				return Value;
			}
			return string.Empty;
		}
	}

	public override string ReadOuterXml()
	{
		switch (_state)
		{
		case XIncludingReaderState.ExposingXmlBaseAttr:
			return "xml:base=\" + _reader.BaseURI + @\"";
		case XIncludingReaderState.ExposingXmlBaseAttrValue:
			return string.Empty;
		case XIncludingReaderState.ExposingXmlLangAttr:
			return "xml:lang=\" + _reader.XmlLang + @\"";
		case XIncludingReaderState.ExposingXmlLangAttrValue:
			return string.Empty;
		default:
			if (NodeType == XmlNodeType.Element)
			{
				StringWriter stringWriter = new StringWriter();
				XmlTextWriter xmlTextWriter = new XmlTextWriter(stringWriter);
				xmlTextWriter.WriteNode(this, defattr: false);
				xmlTextWriter.Close();
				return stringWriter.ToString();
			}
			if (NodeType == XmlNodeType.Attribute)
			{
				return string.Format("{0=\"{1}\"}", Name, Value);
			}
			return string.Empty;
		}
	}

	public override string ReadString()
	{
		return _state switch
		{
			XIncludingReaderState.ExposingXmlBaseAttr => string.Empty, 
			XIncludingReaderState.ExposingXmlBaseAttrValue => _reader.BaseURI, 
			XIncludingReaderState.ExposingXmlLangAttr => string.Empty, 
			XIncludingReaderState.ExposingXmlLangAttrValue => _reader.XmlLang, 
			_ => _reader.ReadString(), 
		};
	}

	public override bool Read()
	{
		bool flag = _reader.Read();
		if (flag)
		{
			_topLevel = ((_readers.Count > 0 && _reader.Depth == 0) ? true : false);
			if (_topLevel)
			{
				_differentLang = AreDifferentLangs(_reader.XmlLang, _readers.Peek().XmlLang);
			}
			if (_topLevel && _reader.NodeType == XmlNodeType.Attribute)
			{
				throw new AttributeOrNamespaceInIncludeLocationError(SR.AttributeOrNamespaceInIncludeLocationError);
			}
			if (_topLevel && _readers.Peek().Depth == 0 && _reader.NodeType == XmlNodeType.Element)
			{
				if (_gotTopIncludedElem)
				{
					throw new MalformedXInclusionResultError(SR.MalformedXInclusionResult);
				}
				_gotTopIncludedElem = true;
			}
			switch (_reader.NodeType)
			{
			case XmlNodeType.Document:
			case XmlNodeType.DocumentType:
			case XmlNodeType.DocumentFragment:
			case XmlNodeType.XmlDeclaration:
				if (_readers.Count <= 0)
				{
					return flag;
				}
				return Read();
			case XmlNodeType.Element:
				if (IsIncludeElement(_reader))
				{
					XmlReader reader = _reader;
					try
					{
						return ProcessIncludeElement();
					}
					catch (FatalException ex)
					{
						throw ex;
					}
					catch (Exception e)
					{
						if (!reader.Equals(_reader))
						{
							_reader.Close();
							_reader = reader;
						}
						_prevFallbackState = _fallbackState;
						return ProcessFallback(_reader.Depth, e);
					}
				}
				if (IsFallbackElement(_reader))
				{
					if (_reader is IXmlLineInfo xmlLineInfo && xmlLineInfo.HasLineInfo())
					{
						throw new XIncludeSyntaxError(SR.GetString("FallbackNotChildOfIncludeLong", _reader.BaseURI.ToString(), xmlLineInfo.LineNumber, xmlLineInfo.LinePosition));
					}
					throw new XIncludeSyntaxError(SR.GetString("FallbackNotChildOfInclude", _reader.BaseURI.ToString()));
				}
				_gotElement = true;
				break;
			case XmlNodeType.EndElement:
				if (_fallbackState.Fallbacking && _reader.Depth == _fallbackState.FallbackDepth && IsFallbackElement(_reader))
				{
					_fallbackState.FallbackProcessed = true;
					return ProcessFallback(_reader.Depth - 1, null);
				}
				break;
			}
			return flag;
		}
		if (_topLevel)
		{
			_topLevel = false;
		}
		if (_readers.Count > 0)
		{
			_reader.Close();
			_reader = _readers.Pop();
			if (!_reader.IsEmptyElement)
			{
				CheckAndSkipContent();
			}
			return Read();
		}
		if (!_gotElement)
		{
			throw new MalformedXInclusionResultError(SR.MalformedXInclusionResult);
		}
		return false;
	}

	private static void ValidationCallback(object sender, ValidationEventArgs args)
	{
	}

	private bool IsIncludeElement(XmlReader r)
	{
		if ((XIncludeKeywords.Equals(_reader.NamespaceURI, _keywords.XIncludeNamespace) || XIncludeKeywords.Equals(_reader.NamespaceURI, _keywords.OldXIncludeNamespace)) && XIncludeKeywords.Equals(_reader.LocalName, _keywords.Include))
		{
			return true;
		}
		return false;
	}

	private bool IsFallbackElement(XmlReader r)
	{
		if ((XIncludeKeywords.Equals(_reader.NamespaceURI, _keywords.XIncludeNamespace) || XIncludeKeywords.Equals(_reader.NamespaceURI, _keywords.OldXIncludeNamespace)) && XIncludeKeywords.Equals(_reader.LocalName, _keywords.Fallback))
		{
			return true;
		}
		return false;
	}

	internal static Stream GetResource(string includeLocation, string accept, string acceptLanguage, out WebResponse response)
	{
		WebRequest webRequest;
		try
		{
			webRequest = WebRequest.Create(includeLocation);
		}
		catch (NotSupportedException innerException)
		{
			throw new ResourceException(SR.GetString("URISchemaNotSupported", includeLocation), innerException);
		}
		catch (SecurityException innerException2)
		{
			throw new ResourceException(SR.GetString("SecurityException", includeLocation), innerException2);
		}
		if (webRequest is HttpWebRequest httpWebRequest)
		{
			if (accept != null)
			{
				TextUtils.CheckAcceptValue(accept);
				if (httpWebRequest.Accept == null || httpWebRequest.Accept == string.Empty)
				{
					httpWebRequest.Accept = accept;
				}
				else
				{
					httpWebRequest.Accept = httpWebRequest.Accept + "," + accept;
				}
			}
			if (acceptLanguage != null)
			{
				if (httpWebRequest.Headers["Accept-Language"] == null)
				{
					httpWebRequest.Headers.Add("Accept-Language", acceptLanguage);
				}
				else
				{
					NameValueCollection headers;
					(headers = httpWebRequest.Headers)["Accept-Language"] = headers["Accept-Language"] + "," + acceptLanguage;
				}
			}
		}
		try
		{
			response = webRequest.GetResponse();
		}
		catch (WebException innerException3)
		{
			throw new ResourceException(SR.GetString("ResourceError", includeLocation), innerException3);
		}
		return response.GetResponseStream();
	}

	private bool ProcessIncludeElement()
	{
		string attribute = _reader.GetAttribute(_keywords.Href);
		string attribute2 = _reader.GetAttribute(_keywords.Xpointer);
		string attribute3 = _reader.GetAttribute(_keywords.Parse);
		if (attribute == null || attribute == string.Empty)
		{
			if (attribute3 == null || attribute3.Equals(_keywords.Xml))
			{
				if (attribute2 == null)
				{
					if (_reader is IXmlLineInfo xmlLineInfo && xmlLineInfo.HasLineInfo())
					{
						throw new XIncludeSyntaxError(SR.GetString("MissingHrefAndXpointerExceptionLong", _reader.BaseURI.ToString(), xmlLineInfo.LineNumber, xmlLineInfo.LinePosition));
					}
					throw new XIncludeSyntaxError(SR.GetString("MissingHrefAndXpointerException", _reader.BaseURI.ToString()));
				}
				throw new InvalidOperationException(SR.IntradocumentReferencesNotSupported);
			}
			if (attribute3.Equals(_keywords.Text))
			{
				throw new InvalidOperationException(SR.IntradocumentReferencesNotSupported);
			}
		}
		else
		{
			if (attribute3 == null || attribute3.Equals(_keywords.Xml))
			{
				return ProcessInterDocXMLInclusion(attribute, attribute2);
			}
			if (attribute3.Equals(_keywords.Text))
			{
				return ProcessInterDocTextInclusion(attribute);
			}
		}
		if (_reader is IXmlLineInfo xmlLineInfo2 && xmlLineInfo2.HasLineInfo())
		{
			throw new XIncludeSyntaxError(SR.GetString("UnknownParseAttrValueLong", attribute3, _reader.BaseURI.ToString(), xmlLineInfo2.LineNumber, xmlLineInfo2.LinePosition));
		}
		throw new XIncludeSyntaxError(SR.GetString("UnknownParseAttrValue", attribute3));
	}

	private Uri ResolveHref(string href)
	{
		try
		{
			Uri baseUri = ((_reader.BaseURI == "") ? _topBaseUri : new Uri(_reader.BaseURI));
			if (_xmlResolver == null)
			{
				return new Uri(baseUri, href);
			}
			return _xmlResolver.ResolveUri(baseUri, href);
		}
		catch (UriFormatException innerException)
		{
			throw new ResourceException(SR.GetString("InvalidURI", href), innerException);
		}
		catch (Exception innerException2)
		{
			throw new ResourceException(SR.GetString("UnresolvableURI", href), innerException2);
		}
	}

	private void SkipContent()
	{
		if (!_reader.IsEmptyElement)
		{
			int depth = _reader.Depth;
			while (_reader.Read() && depth < _reader.Depth)
			{
			}
		}
	}

	private bool ProcessFallback(int depth, Exception e)
	{
		while (_reader.Read() && depth < _reader.Depth)
		{
			XmlNodeType nodeType = _reader.NodeType;
			if (nodeType != XmlNodeType.Element)
			{
				continue;
			}
			if (IsIncludeElement(_reader))
			{
				if (_reader is IXmlLineInfo xmlLineInfo && xmlLineInfo.HasLineInfo())
				{
					throw new XIncludeSyntaxError(SR.GetString("IncludeChildOfIncludeLong", BaseURI.ToString(), xmlLineInfo.LineNumber, xmlLineInfo.LinePosition));
				}
				throw new XIncludeSyntaxError(SR.GetString("IncludeChildOfInclude", BaseURI.ToString()));
			}
			if (IsFallbackElement(_reader))
			{
				if (_fallbackState.FallbackProcessed)
				{
					if (_reader is IXmlLineInfo xmlLineInfo2 && xmlLineInfo2.HasLineInfo())
					{
						throw new XIncludeSyntaxError(SR.GetString("TwoFallbacksLong", BaseURI.ToString(), xmlLineInfo2.LineNumber, xmlLineInfo2.LinePosition));
					}
					throw new XIncludeSyntaxError(SR.GetString("TwoFallbacks", BaseURI.ToString()));
				}
				if (!_reader.IsEmptyElement)
				{
					_fallbackState.Fallbacking = true;
					_fallbackState.FallbackDepth = _reader.Depth;
					return Read();
				}
				_fallbackState.FallbackProcessed = true;
			}
			else
			{
				SkipContent();
			}
		}
		if (!_fallbackState.FallbackProcessed)
		{
			throw new FatalResourceException(e);
		}
		_fallbackState = _prevFallbackState;
		return Read();
	}

	private void CheckAndSkipContent()
	{
		int depth = _reader.Depth;
		bool flag = false;
		while (_reader.Read() && depth < _reader.Depth)
		{
			XmlNodeType nodeType = _reader.NodeType;
			if (nodeType != XmlNodeType.Element)
			{
				continue;
			}
			if (IsIncludeElement(_reader))
			{
				if (_reader is IXmlLineInfo xmlLineInfo && xmlLineInfo.HasLineInfo())
				{
					throw new XIncludeSyntaxError(SR.GetString("IncludeChildOfIncludeLong", _reader.BaseURI.ToString(), xmlLineInfo.LineNumber, xmlLineInfo.LinePosition));
				}
				throw new XIncludeSyntaxError(SR.GetString("IncludeChildOfInclude", _reader.BaseURI.ToString()));
			}
			if (IsFallbackElement(_reader))
			{
				if (flag)
				{
					if (_reader is IXmlLineInfo xmlLineInfo2 && xmlLineInfo2.HasLineInfo())
					{
						throw new XIncludeSyntaxError(SR.GetString("TwoFallbacksLong", _reader.BaseURI.ToString(), xmlLineInfo2.LineNumber, xmlLineInfo2.LinePosition));
					}
					throw new XIncludeSyntaxError(SR.GetString("TwoFallbacks", _reader.BaseURI.ToString()));
				}
				flag = true;
				SkipContent();
			}
			else
			{
				if (XIncludeKeywords.Equals(_reader.NamespaceURI, _keywords.XIncludeNamespace))
				{
					throw new XIncludeSyntaxError(SR.GetString("UnknownXIncludeElement", _reader.Name));
				}
				SkipContent();
			}
		}
	}

	private void ThrowCircularInclusionError(XmlReader reader, Uri url)
	{
		if (reader is IXmlLineInfo xmlLineInfo && xmlLineInfo.HasLineInfo())
		{
			throw new CircularInclusionException(url, BaseURI.ToString(), xmlLineInfo.LineNumber, xmlLineInfo.LinePosition);
		}
		throw new CircularInclusionException(url);
	}

	private bool AreDifferentLangs(string lang1, string lang2)
	{
		return lang1.ToLower() != lang2.ToLower();
	}

	private string CreateAcquiredInfoset(Uri includeLocation)
	{
		if (_cache == null)
		{
			_cache = new Dictionary<string, WeakReference>();
		}
		if (_cache.TryGetValue(includeLocation.AbsoluteUri, out var value) && value.IsAlive)
		{
			return (string)value.Target;
		}
		WebResponse response;
		Stream resource = GetResource(includeLocation.AbsoluteUri, _reader.GetAttribute(_keywords.Accept), _reader.GetAttribute(_keywords.AcceptLanguage), out response);
		XIncludingReader xIncludingReader = new XIncludingReader(response.ResponseUri.AbsoluteUri, resource, _nameTable);
		xIncludingReader.Normalization = _normalization;
		xIncludingReader.WhitespaceHandling = _whiteSpaceHandling;
		StringWriter stringWriter = new StringWriter();
		XmlTextWriter xmlTextWriter = new XmlTextWriter(stringWriter);
		try
		{
			while (xIncludingReader.Read())
			{
				xmlTextWriter.WriteNode(xIncludingReader, defattr: false);
			}
		}
		finally
		{
			xIncludingReader?.Close();
			xmlTextWriter?.Close();
		}
		string text = stringWriter.ToString();
		lock (_cache)
		{
			if (!_cache.ContainsKey(includeLocation.AbsoluteUri))
			{
				_cache.Add(includeLocation.AbsoluteUri, new WeakReference(text));
			}
		}
		return text;
	}

	private string CreateAcquiredInfoset(Uri includeLocation, TextReader reader)
	{
		return CreateAcquiredInfoset(new XmlBaseAwareXmlTextReader(includeLocation.AbsoluteUri, reader, _nameTable));
	}

	private string CreateAcquiredInfoset(XmlReader reader)
	{
		XIncludingReader xIncludingReader = new XIncludingReader(reader);
		xIncludingReader.XmlResolver = _xmlResolver;
		StringWriter stringWriter = new StringWriter();
		XmlTextWriter xmlTextWriter = new XmlTextWriter(stringWriter);
		try
		{
			while (xIncludingReader.Read())
			{
				xmlTextWriter.WriteNode(xIncludingReader, defattr: false);
			}
		}
		finally
		{
			xIncludingReader?.Close();
			xmlTextWriter?.Close();
		}
		return stringWriter.ToString();
	}

	private bool ProcessInterDocXMLInclusion(string href, string xpointer)
	{
		Uri uri = ResolveHref(href);
		if (uri.Fragment != string.Empty)
		{
			throw new XIncludeSyntaxError(SR.FragmentIDInHref);
		}
		CheckLoops(uri, xpointer);
		if (_xmlResolver == null)
		{
			if (xpointer != null)
			{
				_readers.Push(_reader);
				_reader = new XPointerReader(uri.AbsoluteUri, CreateAcquiredInfoset(uri), xpointer);
			}
			else
			{
				WebResponse response;
				Stream resource = GetResource(uri.AbsoluteUri, _reader.GetAttribute(_keywords.Accept), _reader.GetAttribute(_keywords.AcceptLanguage), out response);
				_readers.Push(_reader);
				XmlTextReader xmlTextReader = new XmlBaseAwareXmlTextReader(response.ResponseUri.AbsoluteUri, resource, _nameTable);
				xmlTextReader.Normalization = _normalization;
				xmlTextReader.WhitespaceHandling = _whiteSpaceHandling;
				_reader = xmlTextReader;
			}
			return Read();
		}
		object obj;
		try
		{
			obj = _xmlResolver.GetEntity(uri, null, null);
		}
		catch (Exception innerException)
		{
			throw new ResourceException(SR.CustomXmlResolverError, innerException);
		}
		if (obj == null)
		{
			throw new ResourceException(SR.CustomXmlResolverReturnedNull);
		}
		_readers.Push(_reader);
		if (obj is Stream)
		{
			obj = new StreamReader((Stream)obj);
		}
		if (xpointer != null)
		{
			if (obj is TextReader)
			{
				_reader = new XPointerReader(uri.AbsoluteUri, CreateAcquiredInfoset(uri, (TextReader)obj), xpointer);
			}
			else
			{
				if (!(obj is XmlReader))
				{
					throw new ResourceException(SR.GetString("CustomXmlResolverReturnedUnsupportedType", obj.GetType().ToString()));
				}
				XmlReader xmlReader = (XmlReader)obj;
				_reader = new XPointerReader(xmlReader.BaseURI, CreateAcquiredInfoset(xmlReader), xpointer);
			}
		}
		else if (obj is TextReader)
		{
			_reader = new XmlBaseAwareXmlTextReader(uri.AbsoluteUri, (TextReader)obj, _nameTable);
		}
		else
		{
			if (!(obj is XmlReader))
			{
				throw new ResourceException(SR.GetString("CustomXmlResolverReturnedUnsupportedType", obj.GetType().ToString()));
			}
			_reader = (XmlReader)obj;
		}
		return Read();
	}

	private bool ProcessInterDocTextInclusion(string href)
	{
		string attribute = GetAttribute(_keywords.Encoding);
		Uri includeLocation = ResolveHref(href);
		_readers.Push(_reader);
		_reader = new TextIncludingReader(includeLocation, attribute, _reader.GetAttribute(_keywords.Accept), _reader.GetAttribute(_keywords.AcceptLanguage), _exposeTextAsCDATA);
		return Read();
	}

	private void CheckLoops(Uri url, string xpointer)
	{
		Uri uri = ((_reader.BaseURI == "") ? _topBaseUri : new Uri(_reader.BaseURI));
		if (uri.Equals(url))
		{
			ThrowCircularInclusionError(_reader, url);
		}
		foreach (XmlReader reader in _readers)
		{
			uri = ((reader.BaseURI == "") ? _topBaseUri : new Uri(reader.BaseURI));
			if (uri.Equals(url))
			{
				ThrowCircularInclusionError(_reader, url);
			}
		}
	}
}
