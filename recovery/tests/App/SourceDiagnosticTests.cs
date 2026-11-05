using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Schema;
using NewModCompiler.Application;
using NewModCompiler.Cli;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    internal static class SourceDiagnosticTests
    {
        static int assertions;
        static void Require(bool value, string message)
        {
            assertions++;
            if (!value) throw new Exception("Source diagnostics: " + message);
        }
        static XmlException ParseFailure(string path)
        {
            try {
                using (var reader = XmlReader.Create(new StringReader("<Asset>\n  <Bad></Asset>"), new XmlReaderSettings(), new Uri(path).AbsoluteUri))
                    while (reader.Read()) { }
            } catch (XmlException error) { return error; }
            throw new Exception("Malformed XML fixture unexpectedly parsed.");
        }
        sealed class Recorder : IBuildEventSink
        {
            internal readonly List<IDictionary<string, object>> Values = new List<IDictionary<string, object>>();
            public void Write(IDictionary<string, object> value) { Values.Add(value); }
            public void Dispose() { }
        }
        sealed class FailingCompiler : IStreamCompiler
        {
            readonly Exception error;
            internal FailingCompiler(Exception error) { this.error = error; }
            public string ProviderIdentity { get { return "TEST_ONLY_SOURCE_DIAGNOSTICS"; } }
            public void Initialize(BuildRequest request) { }
            public IList<Artifact> Compile(StreamContext context, CancellationToken cancellation)
            {
                var failure = new BuildFailure(ExitCode.Compilation, "TEST_SOURCE_FAILURE", "Unmodified compiler failure message.");
                failure.Data["OriginalException"] = error;
                throw failure;
            }
            public void Dispose() { }
        }
        static void SameLocation(IDictionary<string, object> expected, IDictionary<string, object> actual, string description)
        {
            foreach (string key in new[] { "file", "line", "column", "assetType", "assetId" }) {
                Require(expected.ContainsKey(key) == actual.ContainsKey(key), description + " optional " + key);
                if (expected.ContainsKey(key)) Require(Convert.ToString(expected[key]) == Convert.ToString(actual[key]), description + " " + key);
            }
        }
        internal static int Run(string root, string mod, string sdk)
        {
            assertions = 0;
            Directory.CreateDirectory(root);
            string xmlPath = Path.Combine(mod, "Data", "source 中文 # percent%.xml");
            var xml = ParseFailure(xmlPath);
            var location = DiagnosticSource.FromException(new InvalidOperationException("wrapper has no source", xml));
            Require((string)location["file"] == xmlPath, "file URI must decode spaces, Unicode and percent once");
            Require(Convert.ToInt32(location["line"]) == xml.LineNumber && Convert.ToInt32(location["column"]) == xml.LinePosition,
                "XML coordinates retain the real one-based location");

            var wrapped = new BuildFailure(ExitCode.Compilation, "TEST_WRAPPER", "No source in this message");
            wrapped.Data["OriginalException"] = xml;
            SameLocation(location, DiagnosticSource.FromException(wrapped), "OriginalException mapping");
            var absent = DiagnosticSource.FromException(new Exception("Asset GameObject:Fake in file:///fake.xml (line 99, position 12)"));
            Require(absent.Count == 0, "error message text must never create a source location or asset identity");
            var zero = DiagnosticSource.FromException(new XmlException("No reliable location"));
            Require(!zero.ContainsKey("line") && !zero.ContainsKey("column"), "zero XML coordinates omitted");

            var known = new Exception("Plugin rejected this asset without a source row.");
            known.Data["NewSdk.SourceFile"] = xmlPath;
            known.Data["NewSdk.AssetType"] = "GameObject";
            known.Data["NewSdk.AssetId"] = "Example";
            known.Data["NewSdk.SourceLine"] = 0;
            known.Data["NewSdk.SourceColumn"] = -1;
            var assetLocation = DiagnosticSource.FromException(known);
            Require((string)assetLocation["file"] == xmlPath && (string)assetLocation["assetType"] == "GameObject" && (string)assetLocation["assetId"] == "Example",
                "already-known asset identity and file forwarded");
            Require(!assetLocation.ContainsKey("line") && !assetLocation.ContainsKey("column"), "unknown plugin row stays unknown");

            string schemaPath = Path.Combine(sdk, "Schemas", "source diagnostic.xsd");
            XmlSchemaException schemaError = null;
            try {
                using (var reader = XmlReader.Create(new StringReader("<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema'>\n<xs:element name='Bad' type='xs:NotAType'/></xs:schema>"), new XmlReaderSettings(), new Uri(schemaPath).AbsoluteUri)) {
                    var schemas = new XmlSchemaSet();
                    schemas.Add(null, reader);
                    schemas.Compile();
                }
            } catch (XmlSchemaException error) { schemaError = error; }
            Require(schemaError != null, "SDK schema fixture must fail compilation");
            var schemaLocation = DiagnosticSource.FromException(schemaError);
            Require((string)schemaLocation["file"] == schemaPath, "SDK XSD failure keeps its real source file");
            Require(Convert.ToInt32(schemaLocation["line"]) == schemaError.LineNumber, "SDK XSD line retained");
            Require(!schemaLocation.ContainsKey("assetId"), "schema failure does not invent a MOD asset");
            string missingPath = Path.Combine(sdk, "builtmods", "missing.manifest");
            var missing = DiagnosticSource.FromException(new FileNotFoundException("Missing SDK manifest", missingPath));
            Require((string)missing["file"] == missingPath && !missing.ContainsKey("line") && !missing.ContainsKey("column"),
                "missing manifest reports the actual file without a fictitious row");
            var missingInDocument = new Exception("document context", new FileNotFoundException("Missing resource", missingPath));
            missingInDocument.Data["NewSdk.SourceFile"] = xmlPath;
            Require((string)DiagnosticSource.FromException(missingInDocument)["file"] == missingPath,
                "actual missing resource takes precedence over enclosing document context");
            Require(DiagnosticSource.FromException(new FileNotFoundException("Missing assembly", "Dependency, Version=1.0.0.0, Culture=neutral")).Count == 0,
                "assembly identity is not a source file path");

            var request = Options.Parse(new[] { "build", "--mod", mod, "--sdk-root", sdk, "--out", Path.Combine(root, "build"), "--package-format", "loose", "--package-content", "base" });
            var recorder = new Recorder();
            using (var events = new EventDispatcher("source-diagnostics-contract", new IBuildEventSink[] { recorder }))
                Require(new BuildSession(request, new FailingCompiler(xml), events).Run(CancellationToken.None) == ExitCode.Compilation,
                    "source metadata must preserve the compilation exit code");
            var diagnostic = recorder.Values.Single(value => (string)value["eventType"] == "Diagnostic" && (string)value["diagnosticCode"] == "TEST_SOURCE_FAILURE");
            Require((string)diagnostic["message"] == "Unmodified compiler failure message.", "original diagnostic code and message preserved");
            Require((string)diagnostic["streamId"] == "base/Mod" && (string)diagnostic["stageId"] == "compile", "source metadata retains stream and stage correlation");
            SameLocation(location, (IDictionary<string, object>)diagnostic["payload"], "event payload");
            var report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(request.ReportPath));
            var diagnostics = (System.Collections.IEnumerable)report["diagnostics"];
            IDictionary<string, object> reportDiagnostic = null;
            foreach (IDictionary<string, object> item in diagnostics)
                if ((string)item["code"] == "TEST_SOURCE_FAILURE") reportDiagnostic = item;
            Require(reportDiagnostic != null, "final report retains the primary compiler failure");
            SameLocation(location, reportDiagnostic, "report source");
            Require((string)report["schemaVersion"] == "1.0" && (string)report["buildId"] == "source-diagnostics-contract", "source fields retain protocol version and build identity");
            Require(recorder.Values.Count(value => (string)value["eventType"] == "BuildFinished") == 1, "source failure has one terminal event");
            Console.WriteLine("PASS Source diagnostic contract: real XML/XSD coordinates, known assets, missing manifest, event/report correlation.");
            return assertions;
        }
    }
}
