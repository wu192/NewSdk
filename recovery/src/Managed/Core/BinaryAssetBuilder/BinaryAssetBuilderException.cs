using System;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Schema;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder;

[Serializable]
public class BinaryAssetBuilderException : ApplicationException
{
	private ErrorCode _ErrorCode;

	public ErrorCode ErrorCode => _ErrorCode;

	// Add metadata where the caller already owns the document/asset. The first
	// (innermost) context wins when an include failure unwinds through parents.
	public static void AttachSource(Exception error, string file, string assetType = null, string assetId = null, int line = 0, int column = 0)
	{
		if (!string.IsNullOrEmpty(file) && !error.Data.Contains("NewSdk.SourceFile")) error.Data["NewSdk.SourceFile"] = file;
		if (!string.IsNullOrEmpty(assetType) && !error.Data.Contains("NewSdk.AssetType")) error.Data["NewSdk.AssetType"] = assetType;
		if (!string.IsNullOrEmpty(assetId) && !error.Data.Contains("NewSdk.AssetId")) error.Data["NewSdk.AssetId"] = assetId;
		if (line > 0 && !error.Data.Contains("NewSdk.SourceLine")) error.Data["NewSdk.SourceLine"] = line;
		if (column > 0 && !error.Data.Contains("NewSdk.SourceColumn")) error.Data["NewSdk.SourceColumn"] = column;
	}

	public BinaryAssetBuilderException(SerializationInfo info, StreamingContext context)
		: base(info, context)
	{
		_ErrorCode = (ErrorCode)info.GetValue("_ErrorCode", typeof(ErrorCode));
	}

	public override void GetObjectData(SerializationInfo info, StreamingContext context)
	{
		info.AddValue("_ErrorCode", _ErrorCode);
		base.GetObjectData(info, context);
	}

	public BinaryAssetBuilderException(ErrorCode e, string message, params object[] args)
		: base(string.Format(message, args))
	{
		_ErrorCode = e;
	}

	public BinaryAssetBuilderException(Exception innerException, ErrorCode e, string message, params object[] args)
		: base(string.Format(message, args), innerException)
	{
		_ErrorCode = e;
	}

	public BinaryAssetBuilderException(Exception innerException, ErrorCode e)
		: base(e.ToString(), innerException)
	{
		_ErrorCode = e;
	}

	public void Trace(Tracer tracer)
	{
		if (tracer == null)
		{
			tracer = Tracer.GetTracer("Default Tracer", string.Empty);
		}
		if (base.InnerException != null)
		{
			if (base.InnerException is XmlSchemaValidationException)
			{
				XmlSchemaValidationException ex = base.InnerException as XmlSchemaValidationException;
				tracer.TraceException("{4}:\n   XML validation error encountered in {0} (line {1}, position {2}):\n      {3}", ex.SourceUri.ToString(), ex.LineNumber, ex.LinePosition, ex.Message, Message);
			}
			else if (base.InnerException is XmlException)
			{
				XmlException ex2 = base.InnerException as XmlException;
				tracer.TraceException("{4}:\n   XML formatting error encountered in {0} (line {1}, position {2}):\n      {3}", ex2.SourceUri.ToString(), ex2.LineNumber, ex2.LinePosition, ex2.Message, Message);
			}
			else if (base.InnerException is XmlSchemaException)
			{
				XmlSchemaException ex3 = base.InnerException as XmlSchemaException;
				string text = "<Name Not Available>";
				if (ex3.SourceSchemaObject != null)
				{
					XmlSchemaAttribute xmlSchemaAttribute = ex3.SourceSchemaObject as XmlSchemaAttribute;
					XmlSchemaElement xmlSchemaElement = ex3.SourceSchemaObject as XmlSchemaElement;
					if (xmlSchemaAttribute != null)
					{
						text = xmlSchemaAttribute.Name;
					}
					else if (xmlSchemaElement != null)
					{
						text = xmlSchemaElement.Name;
					}
				}
				tracer.TraceException("{5}:\n   Schema error encountered in {0} (object: {4}, line {1}, position {2}):\n      {3}", ex3.SourceUri.ToString(), ex3.LineNumber, ex3.LinePosition, ex3.Message, text, Message);
			}
			else
			{
				tracer.TraceException($"{Message}:\n   {base.InnerException.Message}");
			}
		}
		else
		{
			tracer.TraceException(Message);
		}
	}
}
