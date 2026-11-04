using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using NewModCompiler.Application;
using NewModCompiler.Cli;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    public static class AppTests
    {
        static int assertions; static string root,mod,sdk; static int serial;
        static void Check(bool condition,string message) { ++assertions; if (!condition) throw new Exception(message); }
        static void FileAt(string path,string content="<AssetDeclaration/>") { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,content); }
        static BuildRequest Request(string content="base,apt,maps",params string[] extra)
        {
            return Options.Parse(new [] {"plan","--mod",mod,"--sdk-root",sdk,"--out",Path.Combine(root,"out"+(++serial)),"--package-format","loose","--package-content",content}.Concat(extra).ToArray());
        }
        static void Reject(string code,params string[] extra)
        {
            try { Request("base",extra); throw new Exception("Expected rejection: " + code); }
            catch (BuildFailure e) { Check(e.Code==code,"Wrong diagnostic: " + e.Code + " expected " + code); }
        }
        sealed class Recorder : IBuildEventSink
        {
            public readonly List<IDictionary<string,object>> Values = new List<IDictionary<string,object>>();
            public void Write(IDictionary<string,object> value) { Values.Add(value); }
            public void Dispose() { }
        }
        sealed class Dependencies : IDependencyPlanner
        {
            public IEnumerable<BuildTask> Expand(BuildRequest request,IList<BuildTask> roots)
            {
                var extra = new BuildTask {Id="apt/dependency",Content="apt",InputPath=Path.Combine(request.ModRoot,"Data","AptUI","a.xml"),OutputName="apt/dependency",Reason="DependencyOf:"+roots[0].Id,Dependencies=new string[0]};
                roots[0].Dependencies=new [] {extra.Id};
                return roots.Concat(new [] {extra,extra});
            }
        }
        sealed class BrokenSink : IBuildEventSink
        {
            public void Write(IDictionary<string,object> value) { throw new IOException("injected output failure"); }
            public void Dispose() { }
        }
        sealed class TerminalFailureSink : IBuildEventSink
        {
            public void Write(IDictionary<string,object> value) { if ((string)value["eventType"]=="BuildFinished") throw new IOException("injected terminal output failure"); }
            public void Dispose() { }
        }
        sealed class ProbeFailureSink : IBuildEventSink
        {
            public void Write(IDictionary<string,object> value) { if ((string)value["eventType"]=="Diagnostic" && value.ContainsKey("diagnosticCode") && (string)value["diagnosticCode"]=="TEST_SWALLOWED_OUTPUT") throw new IOException("injected backend output failure"); }
            public void Dispose() { }
        }
        sealed class CloseFailureSink : IBuildEventSink
        {
            readonly string countPath;
            public CloseFailureSink(string countPath) { this.countPath=countPath; }
            public void Write(IDictionary<string,object> value) { }
            public void Dispose() { File.AppendAllText(countPath,"close\n"); throw new IOException("injected required close failure"); }
        }
        static int CloseFailureProcess()
        {
            string output=Environment.GetEnvironmentVariable("NEWSDK_APP_CLOSE_OUTPUT");
            var request=Options.Parse(new [] {"plan","--mod",Environment.GetEnvironmentVariable("NEWSDK_APP_CLOSE_MOD"),"--sdk-root",Environment.GetEnvironmentVariable("NEWSDK_APP_CLOSE_SDK"),"--out",output,"--package-format","loose","--package-content","base"});
            Directory.CreateDirectory(output);
            return Program.RunSession(request,new IBuildEventSink[] {new CloseFailureSink(Path.Combine(output,"close-count.txt")),new JsonLineSink(request.EventsPath),new TextLogSink(request.LogPath)});
        }
        sealed class Compiler : IStreamCompiler
        {
            public readonly List<string> Calls = new List<string>(); public bool Initialized,Disposed; public string BuildId;
            public Action<StreamContext> OnCompile; public bool Fail;
            public string ProviderIdentity { get { return "TEST_ONLY_CONTROLLED_EXECUTOR"; } }
            public void Initialize(BuildRequest request) { Initialized=true; }
            public IList<Artifact> Compile(StreamContext context,CancellationToken cancellation)
            {
                Check(Initialized && !Disposed,"Compiler lifecycle");
                if (BuildId==null) BuildId=context.BuildId; Check(BuildId==context.BuildId,"One session id");
                Calls.Add(context.Task.Id); if (OnCompile!=null) OnCompile(context);
                FileAt(Path.Combine(context.OutputDirectory,"partial.tmp"),"temporary");
                if (Fail) throw new BuildFailure(ExitCode.Compilation,"TEST_INPUT_FAILURE","controlled compile failure");
                string output=Path.Combine(context.OutputDirectory,"test-output.bin"); FileAt(output,"controlled test data");
                return new [] {new Artifact {Path=output,Kind="TestOnly"}};
            }
            public void Dispose() { Disposed=true; }
        }
        static void LateOutputFailures()
        {
            foreach (string failure in new [] {"report","terminal","close","swallowed"}) {
                var request=Request("base"); request.Command="build";
                var compiler=new Compiler(); var recorder=new Recorder();
                bool swallowed=false;
                var sinks=new List<IBuildEventSink> {recorder,new JsonLineSink(request.EventsPath),new TextLogSink(request.LogPath)};
                if (failure=="report") compiler.OnCompile=context=>Directory.CreateDirectory(request.ReportPath);
                if (failure=="terminal") sinks.Add(new TerminalFailureSink());
                if (failure=="close") sinks.Add(new CloseFailureSink(Path.Combine(request.OutputRoot,"close-count.txt")));
                if (failure=="swallowed") sinks.Add(new ProbeFailureSink());
                using (var events=new EventDispatcher(Guid.NewGuid().ToString("N"),sinks)) {
                    var progress=new BuildProgressState(()=>events.ElapsedMilliseconds);
                    var console=new StringWriter();
                    using (var renderer=new ConsoleProgressRenderer(progress,console,true,()=>120,false)) {
                        events.ConsoleOutput=renderer;
                        // A backend can catch a listener exception and still return
                        // successfully. Required sink failure remains session-fatal.
                        if(failure=="swallowed") compiler.OnCompile=context=>{
                            try { events.Emit("Diagnostic", "error", message:"controlled backend output failure", code:"TEST_SWALLOWED_OUTPUT"); }
                            catch(BuildFailure e) { Check(e.ExitCode==ExitCode.Output,"Backend probe failure classification"); swallowed=true; }
                        };
                        Check(new BuildSession(request,compiler,events,progress:progress).Run(CancellationToken.None)==ExitCode.Output,"Late "+failure+" failure must fail output");
                        Check(console.ToString().Contains("[失败] 编译失败") && !console.ToString().Contains("编译成功"),"Late "+failure+" console reported success before required output completed");
                        string terminal=console.ToString(); renderer.Refresh();
                        Check(console.ToString()==terminal && progress.Snapshot().Finished,"Late "+failure+" retained live progress");
                    }
                }
                if(failure=="swallowed") Check(swallowed,"Backend did not catch the required sink failure");
                Check(compiler.Calls.Count==1 && compiler.Disposed,"Late "+failure+" did not complete actual controlled compilation");
                Check(recorder.Values.Count(x=>(string)x["eventType"]=="ArtifactProduced")==1,"Late "+failure+" failed before accepting the compiled artifact");
                Check(recorder.Values.Any(x=>(string)x["eventType"]=="StreamCompleted" && new JavaScriptSerializer().Serialize(x["payload"]).Contains("\"status\":\"succeeded\"")),"Late "+failure+" failed before successful stream completion");
                Check(recorder.Values.Any(x=>(string)x["eventType"]=="StageCompleted" && (string)x["stageId"]=="compile" && new JavaScriptSerializer().Serialize(x["payload"]).Contains("\"status\":\"succeeded\"")),"Late "+failure+" failed before successful compile stage completion");
                Check(!Directory.Exists(Path.Combine(request.OutputRoot,".build",compiler.BuildId)),"Late "+failure+" retained staging");
                Check(recorder.Values.Count(x=>(string)x["eventType"]=="BuildFinished")==1,"Late "+failure+" repeated terminal event");
                if(failure=="close") Check(File.ReadAllLines(Path.Combine(request.OutputRoot,"close-count.txt")).Length==1,"Late close sink disposed more than once");
                if (failure!="report") {
                    var report=Report(request);
                    Check(Convert.ToInt32(report["exitCode"])==7 && (string)report["status"]=="failed","Late "+failure+" report did not fail");
                    Check(((System.Collections.ICollection)report["artifacts"]).Count==1 && File.Exists(Path.Combine(request.OutputRoot,"published","base","Mod","test-output.bin")),"Late "+failure+" lost successfully generated output");
                }
            }
        }
        static ExitCode Run(BuildRequest request,Compiler compiler,Recorder recorder,CancellationToken cancellation=default(CancellationToken),bool broken=false)
        {
            var sinks = new List<IBuildEventSink> {recorder,new JsonLineSink(request.EventsPath),new TextLogSink(request.LogPath)};
            if (broken) sinks.Add(new BrokenSink());
            using (var dispatcher=new EventDispatcher(Guid.NewGuid().ToString("N"),sinks)) return new BuildSession(request,compiler,dispatcher).Run(cancellation);
        }
        static Dictionary<string,object> Report(BuildRequest request) { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(request.ReportPath)); }
        static void Protocol(BuildRequest request,Recorder recorder,ExitCode expected)
        {
            Check(recorder.Values.Count(v => (string)v["eventType"]=="BuildFinished")==1,"Unique terminal event");
            Check((string)recorder.Values.Last()["eventType"]=="BuildFinished","Terminal event last");
            string buildId=(string)recorder.Values[0]["buildId"];
            for (int i=0;i<recorder.Values.Count;i++) { Check((long)recorder.Values[i]["sequence"]==i+1,"Monotonic sequence"); Check((string)recorder.Values[i]["buildId"]==buildId,"Consistent build id"); }
            var report=Report(request); Check(Convert.ToInt32(report["exitCode"])==(int)expected,"Report exit code"); Check((string)report["buildId"]==buildId,"Report correlation");
            var lines=File.ReadAllLines(request.EventsPath); Check(lines.Length==recorder.Values.Count,"Every complete JSONL line flushed");
            foreach (string line in lines) Check(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line).ContainsKey("schemaVersion"),"Parse JSONL");
        }
        public static int Main(string[] args)
        {
            try {
                if (args.Length==1 && args[0]=="--close-failure-probe") return CloseFailureProcess();
                root=Path.GetFullPath(args.Length==0 ? Path.Combine(Path.GetTempPath(),"NewModCompiler-App-"+Guid.NewGuid().ToString("N")) : args[0]);
                if (Directory.Exists(root)) Directory.Delete(root,true); Directory.CreateDirectory(root);
                assertions += PerformanceTests.Run();
                assertions += ConsoleProgressTests.Run();
                assertions += PlannerFlaTests.Run(Path.Combine(root,"fla-planner"));

                assertions += EventBufferingTests.Run(Path.Combine(root,"event-buffering"));


                assertions += LegacyCacheStoreTests.Run(Path.Combine(root,"legacy-cache"));

                assertions += FileIdentityTests.Run(Path.Combine(root,"file-identity"));
                assertions += BigArchiveWriterTests.Run(Path.Combine(root,"big-writer"));
                assertions += BuildDefaultsTests.Run(Path.Combine(root,"build-defaults"));
                assertions += CacheLayoutTests.Run(Path.Combine(root,"cache-layout"));
                assertions += AdditionalFilesTests.Run(Path.Combine(root,"additional-files"));
                assertions += MakeBigTests.Run(Path.Combine(root,"makebig"));
                assertions += BasePostProcessorTests.Run(Path.Combine(root,"neutral-assets"));
                mod=Path.Combine(root,"external mods","中文 MOD"); sdk=Path.Combine(root,"SDK"); Directory.CreateDirectory(sdk);
                FileAt(Path.Combine(mod,"Data","Mod.xml")); FileAt(Path.Combine(mod,"Data","AptUI","b.xml")); FileAt(Path.Combine(mod,"Data","AptUI","a.xml"));
                FileAt(Path.Combine(mod,"Data","AdditionalMaps","MapMetaData_z.xml")); FileAt(Path.Combine(mod,"Data","AdditionalMaps","Ignored.xml"));
                Reject("CLI_JOBS","--jobs","0"); Reject("CLI_JOBS","--jobs","-1"); Reject("CLI_JOBS","--jobs","2147483648");
                Check(Request().JobsAuto,"Jobs defaults to auto with original caches");
                Check(Request("base","--jobs","AUTO").JobsAuto,"Auto normalization");
                Check(!Request("base","--jobs","4").JobsAuto && Request("base","--jobs=4").RequestedJobs==4,"Explicit jobs parsing");
                Reject("CLI_PERFORMANCE","--performance","invalid");
                Check(Request().PerformanceLevel=="off","Performance disabled by default");
                Check(Request("base","--performance","SUMMARY").PerformanceLevel=="summary","Performance level normalization");
                foreach (string level in new[] {"off","summary","detailed"}) {
                    var perfRequest=Request("base","--performance",level);
                    var perfRecord=new Recorder();
                    Check(Run(perfRequest,null,perfRecord)==ExitCode.Success,"Profiled plan");
                    var perfReport=Report(perfRequest);
                    Check(perfReport.ContainsKey("performance")==(level!="off"),"Performance report switch");
                }
                foreach (bool cancelled in new[] {false,true}) {
                    var perfRequest=Request("base","--performance","detailed"); perfRequest.Command="build";
                    var perfCompiler=new Compiler {Fail=!cancelled}; var perfRecord=new Recorder();
                    var perfCancel=new CancellationTokenSource();
                    if(cancelled) perfCompiler.OnCompile=context=>perfCancel.Cancel();
                    Check(Run(perfRequest,perfCompiler,perfRecord,perfCancel.Token)==(cancelled ? ExitCode.Cancelled : ExitCode.Compilation),"Profiled failure outcome");
                    string profile=new JavaScriptSerializer().Serialize(Report(perfRequest)["performance"]);
                    Check(profile.Contains("stream.compile") && profile.Contains("stage.compile"),"Failure/cancel timing preserved");
                }
                var request=Request(); var plan=Planner.Resolve(request);
                foreach (string key in new[] {"ss","pc","slowclean"}) {
                    Check(request.Options[key].Value=="true" && request.Options[key].Source=="default","Legacy script default "+key);
                    var disabled=Request("base","/"+key+":false");
                    Check(disabled.Options[key].Value=="false" && disabled.Options[key].Source=="commandLine","Explicit legacy override "+key);
                }
                Check(plan.Tasks.Select(x=>x.Id).SequenceEqual(new [] {"base/Mod","apt/a","apt/b","maps/MapMetaData_z"}),"Deterministic selected stream discovery");
                Check(Planner.Resolve(Request("base")).Tasks.Length==1,"base does not compile apt/maps"); Check(Planner.Resolve(Request("apt")).Tasks.Length==2,"apt only"); Check(Planner.Resolve(Request("maps")).Tasks.Length==1,"maps only");
                Check(Request("maps,base,base").Content.SequenceEqual(new [] {"base","maps"}),"Selection normalization and deduplication");
                string aliasCache = request.CacheLayout.Session;
                var aliases=Request("base","/pc:false","/csc:true","/sc:false","/bc:true","/scp:"+aliasCache,"/scd:"+aliasCache);
                Check(aliases.Options["pc"].Value=="false" && aliases.Options["csc"].Value=="true" && aliases.Options["sc"].Value=="false" && aliases.Options["bc"].Value=="true","Distinct legacy meanings");
                Check(aliases.Options["session-cache-directory"].Source=="commandLine","Alias provenance");
                foreach (string session in new [] {"true","false"}) foreach (string asset in new [] {"true","false"}) {
                    var independent=Request("base","/sc:"+session,"/bc:"+asset);
                    Check(independent.Options["sc"].Value==session && independent.Options["bc"].Value==asset,"Session and build-asset switches remain independent");
                    Check(independent.Options["sc"].Source=="commandLine" && independent.Options["bc"].Source=="commandLine","Cache switches preserve provenance");
                }
                Check(Request("base").Options["sc"].Value=="true" && Request("base").Options["bc"].Value=="true","Original SDK session and asset caches must default on");
                Reject("CLI_CONFLICT","/sc:true","/sc:false"); Reject("CLI_BOOLEAN","/sc:cached");
                var paths=Request("base","/art:ART:shared;relative art","/audio:one;two","/data:relative;second","/dr:relative root","/oix:false","/mr:false");
                Check(paths.Options["art"].Value=="ART:shared;relative art" && paths.Options["audio"].Value=="one;two" && paths.Options["data"].Value=="relative;second" && paths.Options["dr"].Value=="relative root","Legacy ordered paths and DataRoot preserved for Core");
                Check(paths.Options["oix"].Value=="false" && paths.Options["mr"].Value=="false","Disabled legacy booleans remain compatible");
                var dependency=plan.Tasks[2]; var consumer=plan.Tasks[0]; consumer.Dependencies=new [] {dependency.Id};
                var ordered=Planner.OrderTasks(new [] {consumer,dependency,dependency}); Check(ordered.Length==2 && ordered[0].Id==dependency.Id,"Dependency order and exact task deduplication");
                dependency.Dependencies=new [] {consumer.Id}; try { Planner.OrderTasks(new [] {consumer,dependency}); throw new Exception("Cycle accepted"); } catch (BuildFailure e) { Check(e.Code=="PLAN_CYCLE","Dependency cycle rejected"); }
                dependency.Dependencies=new [] {"missing"}; try { Planner.OrderTasks(new [] {dependency}); throw new Exception("Missing dependency accepted"); } catch (BuildFailure e) { Check(e.Code=="PLAN_DEPENDENCY_MISSING","Missing dependency rejected"); }
                Reject("CLI_CONFLICT","/pc:true","/pc:false"); Reject("CLI_CONFLICT","/od:"+Path.Combine(root,"different"));
                Reject("CLI_UNKNOWN","--unknown","yes"); Reject("CLI_UNSUPPORTED","/gui:false"); Reject("CLI_UNSUPPORTED","/sh:true");
                Reject("CLI_CAPABILITY","--package-format","zip"); Reject("CLI_PLATFORM","/tp:Xbox360"); Reject("CLI_BOOLEAN","/csc:garbage"); Reject("CLI_RANGE","/tl:10"); Reject("CLI_RANGE","/el:2"); Reject("CLI_CONTENT","--package-format","loose","--package-content","bogus");
                Reject("CLI_STDOUT_EVENTS","--events","-"); Reject("CLI_LOG_COLLISION","--events",Path.Combine(root,"same"),"--report",Path.Combine(root,"same")); Reject("CLI_LOG_INPUT","--events",Path.Combine(mod,"input.log"));
                DiagnosticPathTests.Run(root,mod,sdk);


                assertions += InputRootsReportTests.Run(Path.Combine(root,"input-roots-report"),mod,sdk);
                assertions += LoDCanonicalTests.Run(Path.Combine(root,"lod-canonical"));
                assertions += BigPackageTests.Run(Path.Combine(root,"big-package-tests"),sdk);
                string project=Path.Combine(mod,"newmodcompiler.json"); FileAt(project,"{\"package-content\":\"apt\",\"tp\":\"Win32\"}");
                var preset=Options.Parse(new [] {"plan","--mod",mod,"--sdk-root",sdk,"--package-format","loose","--out",Path.Combine(root,"preset")}); Check(preset.Content.SequenceEqual(new [] {"apt"}) && preset.Options["package-content"].Source=="project","Project preset");
                Check(Request("base").Options["package-content"].Source=="commandLine","CLI overrides project"); File.Delete(project);
                var dependencyPlan=Planner.Resolve(Request("base"),new Dependencies()); Check(dependencyPlan.PlanKind=="ExplicitDependencyGraph" && dependencyPlan.Tasks.Length==2 && dependencyPlan.Tasks[0].Id=="apt/dependency","Injected explicit dependency expansion");
                var record=new Recorder(); Check(Run(request,null,record)==ExitCode.Success,"Plan success"); Protocol(request,record,ExitCode.Success); Check(!record.Values.Any(x=>(string)x["eventType"]=="StreamStarted"),"Plan does not compile");
                request=Request(); request.Command="build"; record=new Recorder(); Check(Run(request,null,record)==ExitCode.Environment,"Production skeleton refuses missing backend"); Protocol(request,record,ExitCode.Environment);
                request=Request(); request.Command="build"; var compiler=new Compiler(); record=new Recorder(); Check(Run(request,compiler,record)==ExitCode.Success,"Controlled executor success"); Protocol(request,record,ExitCode.Success);
                Check(compiler.Calls.Count==4 && compiler.Disposed,"Single compiler initialized once and disposed"); Check(record.Values.Count(x=>(string)x["eventType"]=="ArtifactProduced")==4,"Exact produced manifest");
                Check(!Report(request)["artifacts"].Equals(null),"Manifest available");
                request=Request(); request.Command="build"; compiler=new Compiler {Fail=true}; record=new Recorder(); FileAt(request.ReportPath,"stale previous report");
                Check(Run(request,compiler,record)==ExitCode.Compilation,"Compile failure"); Protocol(request,record,ExitCode.Compilation); Check(compiler.Disposed && compiler.Calls.Count==1,"Stop and cleanup after failure"); Check(record.Values.Any(x=>(string)x["eventType"]=="StageCompleted" && (string)x["stageId"]=="compile" && new JavaScriptSerializer().Serialize(x["payload"]).Contains("failed")),"Failed compile stage completed"); Check(!Directory.Exists(Path.Combine(request.OutputRoot,".build",compiler.BuildId)),"Failed staging removed");
                request=Request(); request.Command="build"; compiler=new Compiler(); record=new Recorder(); var cancel=new CancellationTokenSource(); cancel.Cancel();
                Check(Run(request,compiler,record,cancel.Token)==ExitCode.Cancelled,"Pre-start cancellation"); Protocol(request,record,ExitCode.Cancelled); Check(!compiler.Initialized && compiler.Disposed,"Cancelled ownership cleanup");
                request=Request(); request.Command="build"; compiler=new Compiler(); record=new Recorder(); cancel=new CancellationTokenSource(); compiler.OnCompile=context=>cancel.Cancel();
                Check(Run(request,compiler,record,cancel.Token)==ExitCode.Cancelled,"Cancellation after safe stream boundary"); Protocol(request,record,ExitCode.Cancelled); Check(record.Values.Any(x=>(string)x["eventType"]=="StageCompleted" && (string)x["stageId"]=="compile" && new JavaScriptSerializer().Serialize(x["payload"]).Contains("cancelled")),"Cancelled compile stage completed"); Check(!Directory.Exists(Path.Combine(request.OutputRoot,".build",compiler.BuildId)),"Cancelled staging removed");
                request=Request(); request.Command="build"; compiler=new Compiler(); record=new Recorder(); Check(Run(request,compiler,record,broken:true)==ExitCode.Output,"Required sink failure is fatal"); Check(Convert.ToInt32(Report(request)["exitCode"])==7,"Sink failure report");
                request=Request(); request.ReportPath=Path.Combine(root,"report-as-directory"); Directory.CreateDirectory(request.ReportPath); record=new Recorder(); Check(Run(request,null,record)==ExitCode.Output,"Report write failure fatal"); Check(record.Values.Count(x=>(string)x["eventType"]=="BuildFinished")==1,"Report failure terminal once");
                LateOutputFailures();
                string saved=mod; mod=Path.Combine(root,"SDK","Mods","second 中文"); FileAt(Path.Combine(mod,"Data","Mod.xml"));
                request=Request("base,apt,maps"); Check(Planner.Resolve(request).Skipped.Length==2,"Empty optional sets explicit"); request.Command="build"; compiler=new Compiler(); record=new Recorder(); Check(Run(request,compiler,record)==ExitCode.Success && compiler.Calls.Count==1,"Second MOD no stale streams"); Protocol(request,record,ExitCode.Success);
                File.Delete(Path.Combine(mod,"Data","Mod.xml")); try { Planner.Resolve(Request("base")); throw new Exception("Missing base accepted"); } catch (BuildFailure e) { Check(e.Code=="PLAN_BASE_MISSING","Missing selected input"); }
                mod=saved;
                // Run the same CLI session entry in a real child test process, so the
                // operating-system exit code is checked after every using/finally closes.
                string closeOutput=Path.Combine(root,"close-failure");
                var start=new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--close-failure-probe") {UseShellExecute=false};
                start.EnvironmentVariables["NEWSDK_APP_CLOSE_MOD"]=mod; start.EnvironmentVariables["NEWSDK_APP_CLOSE_SDK"]=sdk; start.EnvironmentVariables["NEWSDK_APP_CLOSE_OUTPUT"]=closeOutput;
                using (var process=System.Diagnostics.Process.Start(start)) {
                    if (!process.WaitForExit(30000)) { process.Kill(); throw new Exception("Close failure probe timed out"); }
                    Check(process.ExitCode==7,"Required close failure must return OS exit code 7 after CLI disposal");
                }
                var closeReport=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(closeOutput,"build-report.json")));
                Check(Convert.ToInt32(closeReport["exitCode"])==7 && (string)closeReport["status"]=="failed","Required close failure rewrites durable report failed/7");
                Check(new JavaScriptSerializer().Serialize(closeReport["diagnostics"]).Contains("EVENT_CLOSE_FAILED"),"Close failure diagnostic retained in report");
                Check(File.ReadAllLines(Path.Combine(closeOutput,"build.events.jsonl")).Select(line=>new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line)).Count(value=>(string)value["eventType"]=="BuildFinished")==1,"Close failure does not emit a second terminal event");
                Check(File.ReadAllLines(Path.Combine(closeOutput,"close-count.txt")).Length==1,"Dispatcher and CLI ownership dispose each sink once");
                Console.WriteLine("PASS App contract assertions="+assertions+"; controlled test executor only; no asset compilation claimed."); return 0;
            } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
