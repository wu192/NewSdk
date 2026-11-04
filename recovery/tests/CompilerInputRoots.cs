using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;
using NewModCompiler.Application;

// Small real backend builds for input-root option resolution and state lifetime.
// Original-BAB/cold-process comparisons are a separate CLI acceptance fixture.
public static class CompilerInputRoots
{
    static int checks;
    static string work, sdk, mod, output, anchor;
    static readonly List<string> tests = new List<string>();
    static void Require(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
    static void Bytes(string path, byte value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, new[] { value, (byte)(value + 1), (byte)(value + 2) }); }
    static string Asset(string body) { return "<AssetDeclaration xmlns='uri:ea.com:eala:asset'>" + body + "</AssetDeclaration>"; }
    static void Roots(string root, string selected, byte value)
    {
        Write(Path.Combine(root, "data", "choice.xml"), Asset("<ArmorTemplate id='" + selected + "'/>"));
        Bytes(Path.Combine(root, "art", "assets", "art.bin"), value);
        Bytes(Path.Combine(root, "audio", "audio.bin"), (byte)(value + 10));
    }
    static BuildRequest Request(string name)
    {
        var request = new BuildRequest { Command = "build", SdkRoot = sdk, ModRoot = mod, OutputRoot = Path.Combine(output, name), Content = new[] { "base" }, PackageFormat = "loose", Arguments = new string[0] };
        foreach (string key in new[] {"sc", "bc"}) request.Options[key] = new EffectiveOption {Value="false", Source="lifecycle-fixture"};
        return request;
    }
    static void Option(BuildRequest request, string key, string value) { request.Options[key] = new EffectiveOption { Value = value, Source = "input-roots-fixture" }; }
    static BuildRequest Explicit(string name, bool spaced = false)
    {
        var request = Request(name);
        Option(request, "dr", anchor);
        Option(request, "data", (spaced ? " first/data" : "first/data") + ";second/data");
        Option(request, "art", "first/art;second/art"); Option(request, "audio", "first/audio;second/audio");
        return request;
    }
    static StreamContext Context(BuildRequest request, string name)
    {
        var task = new BuildTask { Id = "base/Mod", Content = "base", InputPath = Path.Combine(mod, "Data", "Mod.xml"), OutputName = "base/Mod", Reason = "InputRootsFixture", Dependencies = new string[0] };
        string staging = CacheLayout.ForRequest(request).StreamDirectory(task); Directory.CreateDirectory(staging);
        var context = new StreamContext();
        foreach (var pair in new Dictionary<string, object> { { "BuildId", "input-roots-fixture" }, { "Request", request }, { "Task", task }, { "OutputDirectory", staging } })
            typeof(StreamContext).GetProperty(pair.Key).GetSetMethod(true).Invoke(context, new[] { pair.Value });
        return context;
    }
    static Dictionary<string, object> Report(CoreStreamCompiler backend)
    {
        var serializer = new JavaScriptSerializer();
        return (Dictionary<string, object>)serializer.DeserializeObject(serializer.Serialize(backend.InputRootsReport));
    }
    static string[] Paths(Dictionary<string, object> report, string key) { return ((object[])report[key]).Select(Convert.ToString).ToArray(); }
    static void PathEqual(string actual, string expected, string label)
    {
        Require(string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase), label + ": expected [" + expected + "], got [" + actual + "]");
    }
    static IList<Artifact> Compile(CoreStreamCompiler backend, BuildRequest request, string staging, Settings sentinel)
    {
        IList<Artifact> artifacts = backend.Compile(Context(request, staging), CancellationToken.None);
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Successful stream must restore the exact caller Settings.Current reference");
        Require(artifacts.Count == 4 && artifacts.All(a => File.Exists(a.Path)), "Root-option stream must produce four real outputs");
        return artifacts;
    }
    static void Selected(IList<Artifact> artifacts, string name, params string[] rejected)
    {
        string manifest = Encoding.ASCII.GetString(File.ReadAllBytes(artifacts.Single(a => Path.GetExtension(a.Path) == ".manifest").Path));
        Require(manifest.Contains("ArmorTemplate:" + name + "\0"), "Ordered data root did not select " + name);
        foreach (string other in rejected) Require(!manifest.Contains("ArmorTemplate:" + other + "\0"), "Unselected root leaked " + other);
        Require(manifest.Contains("AptAptData:ArtPayload\0") && manifest.Contains("AptAptData:AudioPayload\0"), "Both art and audio file references must pass through real Core");
    }
    static void Defaults(Settings sentinel)
    {
        var request = Request("default");
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request); Require(Object.ReferenceEquals(Settings.Current, sentinel), "Initialize leaked settings");
            Selected(Compile(backend, request, "stream", sentinel), "SelectedMod", "SelectedFirst", "SelectedSecond");
            var report = Report(backend); var sources = (Dictionary<string, object>)report["sources"];
            PathEqual(Convert.ToString(report["dataRoot"]), sdk, "Default data root");
            PathEqual(Paths(report, "dataPaths")[0], Path.Combine(mod, "Data"), "Default MOD data priority");
            PathEqual(Paths(report, "artPaths")[0], Path.Combine(mod, "Art"), "Default MOD art priority");
            PathEqual(Paths(report, "audioPaths")[0], Path.Combine(mod, "Audio"), "Default MOD audio priority");
            Require(Convert.ToString(sources["dataRoot"]) == "sdk-root" && Convert.ToString(sources["dataPaths"]) == "mod-prepend+sdk-config", "Default root source report is wrong");
        }
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Default backend disposal leaked settings");
        tests.Add("default-MOD-data-art-audio-prepend-priority-and-effective-root-source-report-with-real-four-output-build");
    }
    static void ExplicitCases(Settings sentinel)
    {
        foreach (bool spaced in new[] { false, true })
        {
            var request = Explicit(spaced ? "spaced" : "explicit", spaced);
            using (var backend = new CoreStreamCompiler())
            {
                backend.Initialize(request);
                Selected(Compile(backend, request, "stream", sentinel), spaced ? "SelectedSpaced" : "SelectedFirst", "SelectedMod", "SelectedSecond");
                var report = Report(backend); var sources = (Dictionary<string, object>)report["sources"];
                PathEqual(Convert.ToString(report["dataRoot"]), anchor, "Explicit data root");
                foreach (string kind in new[] { "data", "art", "audio" })
                {
                    string[] roots = Paths(report, kind + "Paths");
                    Require(roots.Length == 2, "Explicit roots must replace defaults: " + kind);
                    PathEqual(roots[0], Path.Combine(anchor, spaced && kind == "data" ? " first" : "first", kind), "Ordered first root " + kind);
                    PathEqual(roots[1], Path.Combine(anchor, "second", kind), "Ordered second root " + kind);
                    Require(Convert.ToString(sources[kind + "Paths"]) == "input-roots-fixture", "Explicit root source was lost: " + kind);
                }
                Require(Convert.ToString(sources["dataRoot"]) == "input-roots-fixture", "Explicit dataRoot source was lost");
            }
            Require(Object.ReferenceEquals(Settings.Current, sentinel), "Explicit backend disposal leaked settings");
        }
        tests.Add("explicit-dataRoot-anchors-ordered-replacement-data-art-audio-lists-and-leading-space-not-trimmed-real-data-selection");
    }
    static void RelativeDataRoot(Settings sentinel)
    {
        var request = Request("relative-dr"); Option(request, "dr", "..");
        foreach (string kind in new[] { "data", "art", "audio" })
            Option(request, kind, Path.Combine(anchor, "first", kind) + ";" + Path.Combine(anchor, "second", kind));
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request);
            Selected(Compile(backend, request, "stream", sentinel), "SelectedFirst", "SelectedSecond", "SelectedMod");
            PathEqual(Convert.ToString(Report(backend)["dataRoot"]), sdk, "Relative /dr must anchor to SDK/tools before search roots resolve");
        }
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Relative /dr disposal leaked settings");
        tests.Add("relative-dataRoot-anchors-at-SDK-tools-and-absolute-ordered-search-roots-retain-their-identities");
    }
    static void FailureRetry(Settings sentinel)
    {
        var request = Explicit("retry");
        string first = Path.Combine(anchor, "first", "data", "choice.xml"), second = Path.Combine(anchor, "second", "data", "choice.xml");
        string a = File.ReadAllText(first), b = File.ReadAllText(second); File.WriteAllText(first, "<AssetDeclaration broken");
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request);
            bool failed = false;
            try { backend.Compile(Context(request, "failed-stream"), CancellationToken.None); }
            catch (BuildFailure error) { Require(error.ExitCode == ExitCode.Compilation, "Malformed selected input failed in the wrong category"); failed = true; }
            Require(failed && Object.ReferenceEquals(Settings.Current, sentinel), "Malformed selected input must fail and restore settings");
            Write(first, a); Write(second, b);
            Selected(Compile(backend, request, "retry-stream", sentinel), "SelectedFirst", "SelectedSecond", "SelectedMod");
        }
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Failure/retry backend disposal leaked settings");
        tests.Add("explicit-root-malformed-input-failure-restores-settings-and-same-backend-retry-after-repair-produces-complete-output");
    }
    static void PreflightPreservesInputs(Settings sentinel)
    {
        var request = Explicit("preflight");
        string protectedInput = Path.Combine(anchor, "first", "data");
        request.ReportPath = Path.Combine(protectedInput, "existing-report.json");
        request.LogPath = Path.Combine(protectedInput, "existing-build.log");
        byte[] reportBytes = Encoding.UTF8.GetBytes("existing report is an input sentinel\r\n");
        byte[] logBytes = Encoding.UTF8.GetBytes("existing log is an input sentinel\r\n");
        File.WriteAllBytes(request.ReportPath, reportBytes); File.WriteAllBytes(request.LogPath, logBytes);
        bool rejected = false;
        try { CoreStreamCompiler.PreflightInputRoots(request); }
        catch (BuildFailure error)
        {
            Require(error.ExitCode == ExitCode.Arguments && error.Code == "CORE_INPUT_OUTPUT_OVERLAP", "Preflight input/diagnostic overlap failed for another reason: " + error);
            rejected = true;
        }
        Require(rejected, "Preflight must reject an explicit data input root containing report/log destinations");
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Failed preflight must restore the exact caller Settings.Current reference");
        Require(File.ReadAllBytes(request.ReportPath).SequenceEqual(reportBytes), "Preflight must preserve every byte of an existing report inside input");
        Require(File.ReadAllBytes(request.LogPath).SequenceEqual(logBytes), "Preflight must preserve every byte of an existing log inside input");
        tests.Add("production-preflight-rejects-explicit-data-root-containing-report-and-log-before-output-open-preserves-sentinel-bytes-and-settings");
    }
    static void CacheBoundary(Settings sentinel)
    {
        var request = Explicit("cache-external"); Option(request, "bc", "true");
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request);
            Selected(Compile(backend, request, "stream", sentinel), "SelectedFirst", "SelectedMod", "SelectedSecond");
            Require(Object.ReferenceEquals(Settings.Current, sentinel), "External cached-root compilation leaked settings");
        }
        tests.Add("original-cache-supports-external-effective-roots-with-real-file-payloads-and-settings-restored");
    }
    static void PreflightReuse(Settings sentinel)
    {
        var request = Explicit("preflight-reuse");
        CoreStreamCompiler.PreflightInputRoots(request);
        Require(Object.ReferenceEquals(Settings.Current, sentinel), "Successful preflight leaked settings");
        string configuration = Path.Combine(sdk, "tools", "BinaryAssetBuilder.exe.config");
        using (var held = new FileStream(configuration, FileMode.Open, FileAccess.Read, FileShare.None))
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request);
            Selected(Compile(backend, request, "stream", sentinel), "SelectedFirst", "SelectedMod", "SelectedSecond");
        }
        // A changed effective boundary cannot reuse the earlier overlap result.
        request = Explicit("preflight-changed");
        CoreStreamCompiler.PreflightInputRoots(request);
        request.ReportPath = Path.Combine(anchor, "first", "data", "report.json");
        bool rejected = false;
        using (var backend = new CoreStreamCompiler())
        {
            backend.Initialize(request);
            try { backend.Compile(Context(request, "stream"), CancellationToken.None); }
            catch (BuildFailure error) { rejected = error.Code == "CORE_INPUT_OUTPUT_OVERLAP"; }
        }
        Require(rejected, "Changed diagnostic destination reused stale preflight validation");
        tests.Add("preflight-configuration-reused-without-reopen-and-changed-request-revalidates-input-output-boundary");
    }
    public static int Main(string[] args)
    {
        Settings previous = Settings.Current;
        try
        {
            if (args.Length != 5 || IntPtr.Size != 4) throw new Exception("Expected work result sourceCommit runId sdk (x86)");
            work = Path.GetFullPath(args[0]); sdk = Path.GetFullPath(args[4]); mod = Path.Combine(work, "MOD"); output = Path.Combine(work, "output"); anchor = Path.Combine(work, "external-root");
            Roots(Path.Combine(anchor, "first"), "SelectedFirst", 11); Roots(Path.Combine(anchor, "second"), "SelectedSecond", 21); Roots(Path.Combine(anchor, " first"), "SelectedSpaced", 31);
            Write(Path.Combine(mod, "Data", "choice.xml"), Asset("<ArmorTemplate id='SelectedMod'/>"));
            Bytes(Path.Combine(mod, "Art", "assets", "art.bin"), 41); Bytes(Path.Combine(mod, "Audio", "audio.bin"), 51);
            Write(Path.Combine(mod, "Data", "Mod.xml"), Asset("<Includes><Include type='all' source='data:choice.xml'/></Includes><AptAptData id='ArtPayload' File='art:assets/art.bin'/><AptAptData id='AudioPayload' File='audio:audio.bin'/>"));
            var sentinel = new Settings(); Settings.Current = sentinel;
            Defaults(sentinel); ExplicitCases(sentinel); RelativeDataRoot(sentinel); FailureRetry(sentinel); PreflightPreservesInputs(sentinel); PreflightReuse(sentinel); CacheBoundary(sentinel);
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { succeeded = true, sourceCommit = args[2], runId = args[3], checks, tests }));
            Console.WriteLine("PASS InputRoots: " + checks + " assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Settings.Current = previous; }
    }
}
