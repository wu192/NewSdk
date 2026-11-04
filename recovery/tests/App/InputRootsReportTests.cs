using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using NewModCompiler.Application;
using NewModCompiler.Cli;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    // Tests option preservation and report transport only. Actual SettingsLoader
    // precedence and original-BAB byte equivalence belong to the Core/CLI matrix.
    internal static class InputRootsReportTests
    {
        static int checks;
        static void Check(bool value,string message) { ++checks; if (!value) throw new Exception("Input-root report: "+message); }
        sealed class Backend : IStreamCompiler, IInputRootsCompiler
        {
            public object Roots; public bool Initialized, FailCompile;
            public string ProviderIdentity { get { return "TEST_INPUT_ROOT_REPORT"; } }
            public object InputRootsReport { get { return Initialized ? Roots : null; } }
            public void Initialize(BuildRequest request) { Initialized=true; }
            public IList<Artifact> Compile(StreamContext context,CancellationToken cancellation)
            {
                if (FailCompile) throw new BuildFailure(ExitCode.Compilation,"TEST_ROOT_COMPILE","injected input failure");
                string file=Path.Combine(context.OutputDirectory,"input-root.bin"); File.WriteAllText(file,"input-root-report fixture");
                return new [] {new Artifact {Path=file,Kind="TestOnly"}};
            }
            public void Dispose() { }
        }
        static BuildRequest Request(string command,string output,string mod,string sdk,string[] extra)
        {
            var args=new List<string> {command,"--mod",mod,"--sdk-root",sdk,"--out",output,"--package-format","loose","--package-content","base"};
            args.AddRange(extra); return Options.Parse(args.ToArray());
        }
        static ExitCode Run(BuildRequest request,Backend backend)
        {
            using(var events=new EventDispatcher(Guid.NewGuid().ToString("N"),new IBuildEventSink[0]))
                return new BuildSession(request,backend,events).Run(CancellationToken.None);
        }
        static Dictionary<string,object> Read(BuildRequest request)
        { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(request.ReportPath)); }
        public static int Run(string work,string mod,string sdk)
        {
            checks=0;
            string[] switches={"/dr:../shared-root","/data:Data/First;*;Data/Second","/art:Art/Second;Art/First","/audio:Audio/Only;*"};
            var request=Request("build",Path.Combine(work,"explicit"),mod,sdk,switches);
            var raw=new Dictionary<string,string> {{"dr","../shared-root"},{"data","Data/First;*;Data/Second"},{"art","Art/Second;Art/First"},{"audio","Audio/Only;*"}};
            foreach(var pair in raw) {
                Check(request.Options[pair.Key].Value==pair.Value,"Parser flattened or reordered raw "+pair.Key);
                Check(request.Options[pair.Key].Source=="commandLine","Parser lost explicit source "+pair.Key);
            }
            string effectiveRoot=Path.Combine(work,"resolved-inputs");
            var roots=new {
                dataRoot=effectiveRoot,schemaPath=Path.Combine(sdk,"Schemas","xsd","CnC3Types.xsd"),
                dataPaths=new [] {Path.Combine(effectiveRoot,"Data","First"),"*",Path.Combine(effectiveRoot,"Data","Second")},
                artPaths=new [] {Path.Combine(effectiveRoot,"Art","Second"),Path.Combine(effectiveRoot,"Art","First")},
                audioPaths=new [] {Path.Combine(effectiveRoot,"Audio","Only"),"*"},
                sources=new {dataRoot="commandLine",dataPaths="commandLine",artPaths="commandLine",audioPaths="commandLine"}
            };
            var backend=new Backend {Roots=roots};
            Check(Run(request,backend)==ExitCode.Success,"Controlled build failed");
            var report=Read(request); var serializer=new JavaScriptSerializer();
            Check(serializer.Serialize(report["inputRoots"])==serializer.Serialize(roots),"Effective roots, array precedence or sources changed during report transport");
            var options=(Dictionary<string,object>)report["effectiveOptions"];
            foreach(var pair in raw) {
                var option=(Dictionary<string,object>)options[pair.Key];
                Check((string)option["Value"]==pair.Value && (string)option["Source"]=="commandLine","Raw effective option was replaced by resolved roots: "+pair.Key);
            }
            var defaults=new {
                dataRoot=sdk,schemaPath=Path.Combine(sdk,"Schemas","xsd","CnC3Types.xsd"),
                dataPaths=new [] {Path.Combine(mod,"Data"),"*",Path.Combine(sdk,"Data")},
                artPaths=new [] {Path.Combine(mod,"Art"),Path.Combine(sdk,"Art")},
                audioPaths=new [] {Path.Combine(mod,"Audio"),Path.Combine(sdk,"Audio")},
                sources=new {dataRoot="sdk-root",dataPaths="mod-prepend+sdk-config",artPaths="mod-prepend+sdk-config",audioPaths="mod-prepend+sdk-config"}
            };
            request=Request("build",Path.Combine(work,"defaults"),mod,sdk,new string[0]); backend=new Backend {Roots=defaults};
            Check(Run(request,backend)==ExitCode.Success,"Default-root controlled build failed");
            Check(serializer.Serialize(Read(request)["inputRoots"])==serializer.Serialize(defaults),"Default root provenance was lost");
            request=Request("build",Path.Combine(work,"failure"),mod,sdk,switches); backend=new Backend {Roots=roots,FailCompile=true};
            Check(Run(request,backend)==ExitCode.Compilation,"Injected input failure exit mismatch");
            Check(serializer.Serialize(Read(request)["inputRoots"])==serializer.Serialize(roots),"Configured roots disappeared from failed-build diagnosis");
            request=Request("plan",Path.Combine(work,"plan"),mod,sdk,switches); backend=new Backend {Roots=roots};
            Check(Run(request,backend)==ExitCode.Success && !backend.Initialized,"Plan initialized Core-style roots");
            Check(Read(request)["inputRoots"]==null,"Plan claimed resolved backend roots");
            bool rejected=false;
            try { Request("build",Path.Combine(work,"conflict"),mod,sdk,new [] {"/data:First;Second","--data","Second;First"}); }
            catch(BuildFailure error) { rejected=error.Code=="CLI_CONFLICT"; }
            Check(rejected,"Conflicting search-order overrides were accepted");
            return checks;
        }
    }
}
