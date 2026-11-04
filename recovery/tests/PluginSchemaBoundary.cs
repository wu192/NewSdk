using System;
using System.IO;
using System.Xml;
using System.Xml.Schema;

// Component oracles bypass Core. Schema rejection cases explicitly exercise the
// SDK document boundary here; valid-input byte comparisons still call plugins directly.
static class PluginSchemaBoundary
{
    const string AssetNamespace = "uri:ea.com:eala:asset";
    static XmlSchemaSet schemas;

    public static void Validate(XmlNode input)
    {
        if (schemas == null)
        {
            schemas = new XmlSchemaSet();
            schemas.XmlResolver = new XmlUrlResolver();
            schemas.Add(null, Environment.GetEnvironmentVariable("NEWSdk_PLUGIN_TEST_SCHEMA") ?? Path.GetFullPath("SDK/Schemas/xsd/CnC3Types.xsd"));
            schemas.Compile();
        }
        var document = new XmlDocument();
        var root = document.CreateElement("AssetDeclaration", AssetNamespace);
        document.AppendChild(root);
        var source = input is XmlDocument ? ((XmlDocument)input).DocumentElement : input;
        var asset = (XmlElement)document.ImportNode(source, true);
        if (!asset.HasAttribute("id")) asset.SetAttribute("id", "SchemaBoundary");
        root.AppendChild(asset);
        RemoveAnnotations(asset);
        document.Schemas = schemas;
        try { document.Validate(null); }
        catch (XmlSchemaValidationException error)
        {
            throw new NotSupportedException("SDK schema rejected component input.", error);
        }
    }

    static void RemoveAnnotations(XmlElement element)
    {
        element.RemoveAttribute("TypeId", "");
        foreach (XmlNode child in element.ChildNodes)
            if (child is XmlElement) RemoveAnnotations((XmlElement)child);
    }
}
