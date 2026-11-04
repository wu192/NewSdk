using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NewModCompiler.Application;
using NewModCompiler.Cli;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    internal static class DiagnosticPathTests
    {
        [DllImport("kernel32.dll",EntryPoint="CreateHardLinkW",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool CreateHardLink(string file,string existing,IntPtr reserved);
        static void Require(bool value,string message) { if (!value) throw new Exception("Diagnostic paths: "+message); }
        static void Link(string file,string existing)
        {
            if (!CreateHardLink(file,existing,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"CreateHardLink "+file);
        }
        static void Junction(string path,string target)
        {
            using (var process=Process.Start(new ProcessStartInfo("cmd.exe","/d /c mklink /J \""+path+"\" \""+target+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})) {
                string output=process.StandardOutput.ReadToEnd(), error=process.StandardError.ReadToEnd();
                process.WaitForExit(); Require(process.ExitCode==0,"junction creation: "+output+error);
            }
        }
        static void Reject(string mod,string sdk,string output,string code,params string[] more)
        {
            var arguments=new List<string> {"plan","--mod",mod,"--sdk-root",sdk,"--out",output}; arguments.AddRange(more);
            try { Options.Parse(arguments.ToArray()); throw new Exception("Accepted "+code); }
            catch (BuildFailure failure) { Require(failure.Code==code,"Expected "+code+", got "+failure.Code); }
        }
        static void Write(IBuildEventSink sink)
        {
            using (var events=new EventDispatcher("diagnostic-path-tests",new [] {sink})) events.Emit("Diagnostic",message:"fresh inode");
        }
        sealed class OutputCompiler : IStreamCompiler
        {
            internal bool Initialized;
            public string ProviderIdentity { get { return "STAGING_PATH_REJECTION_FIXTURE"; } }
            public void Initialize(BuildRequest request) { Initialized=true; throw new BuildFailure(ExitCode.Compilation,"TEST_UNSAFE_STAGING_REACHED","Unsafe output reached the backend."); }
            public IList<Artifact> Compile(StreamContext context,System.Threading.CancellationToken cancellation) { throw new Exception("Unsafe output compilation reached"); }
            public void Dispose() { }
        }
        static void OutputJunction(string work,string mod,string sdk,string target,string suffix)
        {
            string output=Path.Combine(work,"output-out-"+suffix), id="output-junction-"+suffix;
            var request=Options.Parse(new [] {"build","--mod",mod,"--sdk-root",sdk,"--out",output,"--package-format","loose","--package-content","base"});
            string inputDirectory=Path.Combine(target,id), sentinel=Path.Combine(inputDirectory,"sentinel.keep");
            Directory.CreateDirectory(inputDirectory); File.WriteAllText(sentinel,"preserve output alias input");
            Directory.CreateDirectory(output); string junction=Path.Combine(output,"published"); Junction(junction,target);
            try {
                var compiler=new OutputCompiler(); ExitCode code;
                using (var events=new EventDispatcher(id,new IBuildEventSink[0])) code=new BuildSession(request,compiler,events).Run(System.Threading.CancellationToken.None);
                Require(code==ExitCode.Output && !compiler.Initialized,"post-parse output junction reached backend or had wrong exit code");
                Require(File.Exists(sentinel) && File.ReadAllText(sentinel)=="preserve output alias input","output cleanup deleted input through junction ancestor");
                Require(!Directory.Exists(Path.Combine(inputDirectory,"base")),"output setup created stream directories inside input");
            } finally { Directory.Delete(junction); }
        }
        internal static void Run(string root,string mod,string sdk)
        {
            string work=Path.Combine(root,"diagnostic 中文 paths"), output=Path.Combine(work,"out"); Directory.CreateDirectory(work);
            string a=Path.Combine(work,"a"), descendant=Path.Combine(a,"report.json");
            Reject(mod,sdk,output,"CLI_LOG_COLLISION","--events",a,"--report",descendant);
            Reject(mod,sdk,output,"CLI_LOG_COLLISION","--report",a,"--log",descendant);
            Reject(mod,sdk,output,"CLI_LOG_COLLISION","--events",Path.Combine(output,"published","x.jsonl"));
            Reject(mod,sdk,output,"CLI_LOG_COLLISION","--report",output);
            Reject(mod,sdk,output,"CLI_LOG_PATH","--events",Path.Combine(work,"asset:stream"));
            Reject(mod,sdk,output,"CLI_LOG_PATH","--events","A:drive-relative.log");
            Reject(mod,sdk,output,"CLI_LOG_PATH","--events",Path.Combine(work,"trailing-space ","events.jsonl"));
            string existingDirectory=Path.Combine(work,"existing-directory"), existingFile=Path.Combine(work,"existing-file");
            Directory.CreateDirectory(existingDirectory); File.WriteAllText(existingFile,"preserve existing output file");
            Reject(mod,sdk,existingFile,"CLI_OUTPUT_PATH");
            foreach (string option in new [] {"--events","--report","--log"})
                Reject(mod,sdk,output,"CLI_LOG_PATH",option,existingDirectory);
            Require(File.ReadAllText(existingFile)=="preserve existing output file" && Directory.Exists(existingDirectory),"file/directory rejection modified paths");
            // A missing volume used to walk past its root and pass null to
            // Path.Combine. Check both the path helper and public CLI contract.
            string unavailable=null;
            for (char drive='Z';drive>='A';--drive) {
                string candidate=drive+@":\";
                if (!Directory.Exists(candidate)) { unavailable=candidate; break; }
            }
            Require(unavailable!=null,"test requires an unavailable drive letter");
            string missing=Path.Combine(unavailable,"diagnostic","events.jsonl");
            try { DiagnosticDirectory.ResolveOutput(missing); throw new Exception("Missing volume accepted"); }
            catch (IOException error) { Require(error.Message.Contains("unavailable"),"missing volume did not produce an explicit root failure"); }
            Reject(mod,sdk,output,"CLI_LOG_PATH","--events",missing);
            Require(Paths.Contains(Path.GetPathRoot(work),DiagnosticDirectory.ResolveOutput(Path.GetPathRoot(work))),"existing volume root resolution");
            string input=Path.Combine(mod,"Data","Mod.xml"), original=File.ReadAllText(input);
            string json=Path.Combine(work,"events.jsonl"), text=Path.Combine(work,"text.log"), report=Path.Combine(work,"report.json");
            Link(json,input); Write(new JsonLineSink(json)); Require(File.ReadAllText(input)==original,"JSONL hard link truncated input");
            Link(text,input); Write(new TextLogSink(text)); Require(File.ReadAllText(input)==original,"text hard link truncated input");
            Write(new TextLogSink(text)); Require(File.ReadAllText(input)==original,"repeat text log touched input");
            Link(report,input); Reports.RemoveStale(report); Require(!File.Exists(report) && File.ReadAllText(input)==original,"stale report hard link modified input");
            Link(report,input); Reports.WriteAtomic(report,new Dictionary<string,object> {{"test","atomic"}}); Require(File.ReadAllText(input)==original,"atomic report hard link modified input");
            Require(File.ReadAllText(json).Contains("fresh inode") && File.ReadAllText(text).Contains("fresh inode") && File.ReadAllText(report).Contains("atomic"),"fresh outputs missing");
            string active=Path.Combine(work,"active.jsonl");
            using (var first=new JsonLineSink(active)) {
                try { using (var second=new TextLogSink(active)) { } throw new Exception("Active sink was silently replaced"); } catch (IOException) { }
                try { Reports.RemoveStale(active); throw new Exception("Active sink was removed as a stale report"); } catch (IOException) { }
                try { Reports.WriteAtomic(active,new Dictionary<string,object>()); throw new Exception("Active sink was replaced by a report"); } catch (IOException) { }
                using (var events=new EventDispatcher("active-path-test",new [] {first})) events.Emit("Diagnostic",message:"active writer retained");
            }
            Require(File.ReadAllText(active).Contains("active writer retained"),"active sink lost its directory entry");
            string aliasOne=Path.Combine(work,"alias-one.log"), aliasTwo=Path.Combine(work,"alias-two.log"); Link(aliasOne,input); Link(aliasTwo,input);
            using (var one=new JsonLineSink(aliasOne)) using (var two=new TextLogSink(aliasTwo)) {
                using (var events=new EventDispatcher("hardlink-path-tests",new IBuildEventSink[] {one,two})) events.Emit("Diagnostic",message:"independent writers");
            }
            Require(File.ReadAllText(aliasOne).Contains("independent writers") && File.ReadAllText(aliasTwo).Contains("independent writers") && File.ReadAllText(input)==original,"distinct hardlink destinations did not become independent outputs");
            // A destination can become a directory after parsing. Failed atomic
            // publication must leave it intact and release every pinned parent.
            string failedParent=Path.Combine(work,"failed-publish"), failedTarget=Path.Combine(failedParent,"events.jsonl"), failedMoved=Path.Combine(work,"failed-publish-moved");
            Directory.CreateDirectory(failedTarget);
            try { using (var sink=new JsonLineSink(failedTarget)) { } throw new Exception("Directory destination accepted"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Require(Directory.Exists(failedTarget) && Directory.GetFiles(failedParent).Length==0,"failed publication changed directory or leaked temporary file");
            Directory.Move(failedParent,failedMoved); Require(Directory.Exists(failedMoved),"failed publication leaked directory handles");
            foreach (string target in new [] {mod,sdk}) {
                string junction=Path.Combine(work,"junction"), child=Path.Combine(junction,"forbidden.log"); Junction(junction,target);
                try {
                    Reject(mod,sdk,output,"CLI_LOG_PATH","--events",child);
                    try { using (var sink=new JsonLineSink(child)) { } throw new Exception("Sink accepted junction"); } catch (IOException) { }
                    try { Reports.RemoveStale(child); throw new Exception("Report removal accepted junction"); } catch (IOException) { }
                    try { Reports.WriteAtomic(child,new Dictionary<string,object>()); throw new Exception("Report write accepted junction"); } catch (IOException) { }
                    Require(!File.Exists(Path.Combine(target,"forbidden.log")),"junction write reached input");
                } finally { Directory.Delete(junction); }
            }
            string late=Path.Combine(work,"late"), lateOutput=Path.Combine(work,"late-out");
            Options.Parse(new [] {"plan","--mod",mod,"--sdk-root",sdk,"--out",lateOutput,"--package-format","loose","--events",Path.Combine(late,"events.jsonl")});
            Junction(late,mod);
            try { try { using (var sink=new JsonLineSink(Path.Combine(late,"events.jsonl"))) { } throw new Exception("Post-parse junction accepted"); } catch (IOException) { } }
            finally { Directory.Delete(late); }
            OutputJunction(work,mod,sdk,mod,"mod"); OutputJunction(work,mod,sdk,sdk,"sdk");
            Require(File.ReadAllText(input)==original,"input changed across diagnostic matrix");
            Console.WriteLine("PASS Diagnostic path matrix: ancestor/output collisions, Unicode, ADS rejection, JSONL/text/report hard links, MOD/SDK junctions, post-parse junction");
        }
    }
}
