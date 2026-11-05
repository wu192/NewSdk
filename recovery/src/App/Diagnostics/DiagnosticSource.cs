using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;

namespace NewModCompiler.Diagnostics
{
    // Consume locations already held by Core/the XML reader. Never derive a
    // source location from human-readable error text or re-open source files.
    internal static class DiagnosticSource
    {
        internal static Dictionary<string,object> FromException(Exception error)
        {
            var result = new Dictionary<string,object>();
            var pending = new Queue<Exception>();
            var seen = new HashSet<Exception>();
            if (error != null) pending.Enqueue(error);
            while (pending.Count != 0 && seen.Count < 24)
            {
                Exception current = pending.Dequeue();
                if (!seen.Add(current)) continue;
                var xml = current as XmlException;
                var schema = current as XmlSchemaException;
                var missing = current as FileNotFoundException;
                // XML exception coordinates belong to SourceUri, which may be
                // an SDK XSD or an included XML rather than the MOD entry.
                if (xml != null && !string.IsNullOrEmpty(xml.SourceUri))
                    SetLocation(result,xml.SourceUri,xml.LineNumber,xml.LinePosition,true);
                // A DOM validation URI may name the containing document rather
                // than an expanded xi:include node. Its zero coordinates cannot
                // displace a location captured from that node's actual reader.
                else if (schema != null && !string.IsNullOrEmpty(schema.SourceUri) &&
                    (!(schema is XmlSchemaValidationException) || schema.LineNumber > 0 || !result.ContainsKey("line")))
                    SetLocation(result,schema.SourceUri,schema.LineNumber,schema.LinePosition,true);
                else if (missing != null && IsFilePath(missing.FileName))
                    SetLocation(result,missing.FileName,0,0,true);
                else if (!result.ContainsKey("file"))
                {
                    string file = current.Data["NewSdk.SourceFile"] as string;
                    SetLocation(result,file,Positive(current.Data["NewSdk.SourceLine"]),Positive(current.Data["NewSdk.SourceColumn"]),false);
                }
                CopyIdentity(current,result,"NewSdk.AssetType","assetType");
                CopyIdentity(current,result,"NewSdk.AssetId","assetId");
                if (current.InnerException != null) pending.Enqueue(current.InnerException);
                var original = current.Data["OriginalException"] as Exception;
                if (original != null) pending.Enqueue(original);
            }
            return result;
        }
        static bool IsFilePath(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            Uri uri;
            return Path.IsPathRooted(file) || (Uri.TryCreate(file,UriKind.Absolute,out uri) && uri.IsFile);
        }
        static int Positive(object value) { return value is int && (int)value > 0 ? (int)value : 0; }
        static void CopyIdentity(Exception error,IDictionary<string,object> result,string key,string field)
        {
            string value = error.Data[key] as string;
            if (!result.ContainsKey(field) && !string.IsNullOrEmpty(value)) result[field] = value;
        }
        static void SetLocation(IDictionary<string,object> result,string file,int line,int column,bool replace)
        {
            if (string.IsNullOrEmpty(file)) return;
            Uri uri;
            if (Uri.TryCreate(file,UriKind.Absolute,out uri) && uri.IsFile) file = uri.LocalPath;
            // DOM validation retains its document URI but commonly has no
            // coordinates. Do not erase a location captured while loading the
            // same document. When selecting another real source, its unknown
            // coordinates must not inherit the previous file's coordinates.
            object previousFile;
            bool sameFile = result.TryGetValue("file",out previousFile) &&
                string.Equals(previousFile as string,file,StringComparison.OrdinalIgnoreCase);
            if (replace && (!sameFile || line > 0)) { result.Remove("line"); result.Remove("column"); }
            result["file"] = file;
            if (line > 0) result["line"] = line;
            if (line > 0 && column > 0) result["column"] = column;
        }
        internal static void Apply(IDictionary<string,object> diagnostic,IDictionary<string,object> source)
        {
            if (source == null || source.Count == 0) return;
            foreach (var field in source) diagnostic[field.Key] = field.Value;
            diagnostic["source"] = source;
        }
    }
}
