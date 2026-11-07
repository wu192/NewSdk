using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class ShaderCompilerTests
    {
        static int checks;
        static void Check(bool value, string message) { ++checks; if (!value) throw new Exception("FX compiler: " + message); }
        static void Put(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, value); }
        public static bool IsFxcProbe(string[] args) { return args.Length != 0 && args[0] == "/nologo"; }

        // The test executable doubles as a controlled external compiler. This
        // verifies process arguments, both pipes, failures and cancellation.
        public static int FxcProbe(string[] args)
        {
            int outputIndex = Array.IndexOf(args, "/Fo");
            if (outputIndex < 0 || !args.Contains("/O3") || !args.Contains("/WX") || !args.Contains("fx_2_0")) return 91;
            string output = args[outputIndex + 1], source = args[args.Length - 1], text = File.ReadAllText(source);
            if (text.Contains("FAIL")) { Console.Out.Write("controlled stdout"); Console.Error.Write("controlled stderr"); return 17; }
            if (text.Contains("NOOUTPUT")) return 0;
            if (text.Contains("BLOCK"))
            {
                File.WriteAllText(output + ".started.tmp", Process.GetCurrentProcess().Id.ToString());
                File.Move(output + ".started.tmp", output + ".started");
                Thread.Sleep(30000);
            }
            if (text.Contains("/// fxc-wrapper-disable-preshader") && !args.Contains("/Op")) return 92;
            foreach (Match include in Regex.Matches(text, @"#include ""([^""]+)"""))
            {
                string path = Path.Combine(Path.GetDirectoryName(source), include.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { Console.Error.Write("missing include " + path); return 93; }
                text += "\ninclude:" + File.ReadAllText(path);
            }
            // More than a pipe buffer on both channels catches serial draining.
            Console.Out.Write(new string('o', 100000)); Console.Error.Write(new string('e', 100000));
            File.WriteAllText(output, "compiled:" + text);
            return 0;
        }

        static string Compiler(string root)
        {
            string assembly = Assembly.GetExecutingAssembly().Location;
            if (Environment.OSVersion.Platform != PlatformID.Unix) return assembly;
            string helper = Path.Combine(root, "controlled fxc");
            Put(helper, "#!/bin/sh\nexec mono '" + assembly.Replace("'", "'\\''") + "' \"$@\"\n");
            using (var process = Process.Start(new ProcessStartInfo("chmod", "+x \"" + helper + "\"") { UseShellExecute = false })) process.WaitForExit();
            return helper;
        }

        static void Reject(Action action, string code)
        {
            try { action(); throw new Exception("FX compiler accepted " + code); }
            catch (BuildFailure failure) { Check(failure.Code == code, "expected " + code + ", got " + failure.Code); }
        }

        public static int Run(string root)
        {
            checks = 0;
            string sdk = Path.Combine(root, "sdk"), mod = Path.Combine(root, "mod"), temporary = Path.Combine(root, "work with spaces");
            Directory.CreateDirectory(sdk); Directory.CreateDirectory(mod); Directory.CreateDirectory(temporary);
            var request = new BuildRequest { SdkRoot = sdk, ModRoot = mod, CompileFx = true, FxcPath = Compiler(root) };
            Put(Path.Combine(sdk, "Additional", "Shaders", "sample.fx"), "#include \"common.fxh\"\n#include \"../shared/extra.inc\"\n/// fxc-wrapper-disable-preshader\n");
            Put(Path.Combine(sdk, "Additional", "Shaders", "common.fxh"), "SDK header");
            Put(Path.Combine(mod, "Additional", "Shaders", "common.fxh"), "MOD header");
            Put(Path.Combine(mod, "Additional", "shared", "extra.inc"), "external include");
            Put(Path.Combine(mod, "Additional", "Shaders", "Compiled", "sample.fxo"), "stale bytecode");
            Put(Path.Combine(mod, "Additional", "Shaders", "nested", "unused.fx"), "FAIL nested source is not a top-level shader");
            var entries = new List<BigArchiveEntry>();
            AdditionalFiles.Merge(sdk, mod, entries, CancellationToken.None);
            ShaderCompiler.Compile(request, entries, temporary, CancellationToken.None);
            var compiled = entries.Single(entry => entry.Name == "shaders\\compiled\\sample.fxo");
            string bytes = File.ReadAllText(compiled.SourcePath);
            Check(bytes.StartsWith("compiled:") && bytes.Contains("MOD header") && !bytes.Contains("SDK header") && bytes.Contains("external include"), "merged source/include precedence or relative include path changed");
            Check(entries.All(entry => !entry.Name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) && !entry.Name.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)), "source files reached the BIG inventory");
            Check(File.ReadAllText(Path.Combine(mod, "Additional", "Shaders", "Compiled", "sample.fxo")) == "stale bytecode", "compiler overwrote source Additional bytecode");
            Check(Paths.Contains(temporary, compiled.SourcePath), "compiled bytecode escaped the package workspace");
            Check(request.ExternalTools.Count == 1 && request.ExternalTools[0] == request.FxcPath, "launched FX compiler was not recorded in the build report");

            foreach (string failure in new[] { "FAIL", "NOOUTPUT" })
            {
                string source = Path.Combine(root, failure + ".fx"); Put(source, failure);
                var failed = new List<BigArchiveEntry> { new BigArchiveEntry { Name = "shaders\\failed.fx", SourcePath = source } };
                Reject(() => ShaderCompiler.Compile(request, failed, Path.Combine(root, failure), CancellationToken.None), failure == "FAIL" ? "FX_COMPILATION_FAILED" : "FX_OUTPUT_MISSING");
                Check(failed.Count == 1 && failed[0].Name.EndsWith(".fx"), "failed compilation changed the accepted inventory");
            }

            var missing = new BuildRequest { SdkRoot = sdk, CompileFx = true, FxcPath = Path.Combine(root, "missing-fxc.exe") };
            ShaderCompiler.Compile(missing, new List<BigArchiveEntry>(), Path.Combine(root, "empty"), CancellationToken.None);
            Check(!Directory.Exists(Path.Combine(root, "empty")), "absent shaders required an external tool or workspace");
            string plain = Path.Combine(root, "plain.fx"); Put(plain, "plain");
            var disabled = new List<BigArchiveEntry> { new BigArchiveEntry { Name = "Shaders\\plain.fx", SourcePath = plain } };
            missing.CompileFx = false;
            ShaderCompiler.Compile(missing, disabled, Path.Combine(root, "disabled"), CancellationToken.None);
            Check(disabled.Count == 0 && !Directory.Exists(Path.Combine(root, "disabled")), "disabled compiler required the tool or retained source files");
            missing.CompileFx = true;
            Reject(() => ShaderCompiler.Compile(missing, new List<BigArchiveEntry> { new BigArchiveEntry { Name = "Shaders\\plain.fx", SourcePath = plain } }, Path.Combine(root, "missing"), CancellationToken.None), "FX_COMPILER_MISSING");

            if (Environment.OSVersion.Platform != PlatformID.Unix)
            {
                string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fxc.exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, bundled);
                try
                {
                    var automatic = new BuildRequest { SdkRoot = sdk, CompileFx = true };
                    var bundledEntries = new List<BigArchiveEntry> { new BigArchiveEntry { Name = "Shaders\\plain.fx", SourcePath = plain } };
                    ShaderCompiler.Compile(automatic, bundledEntries, Path.Combine(root, "bundled"), CancellationToken.None);
                    Check(automatic.ExternalTools.Count == 1 && automatic.ExternalTools[0] == bundled, "default compiler did not resolve beside the executable");
                    Check(File.ReadAllText(bundledEntries.Single().SourcePath) == "compiled:plain", "bundled default did not compile shader bytecode");
                }
                finally { File.Delete(bundled); }
            }

            string waiting = Path.Combine(root, "waiting.fx"), cancelRoot = Path.Combine(root, "cancel"); Put(waiting, "BLOCK");
            var cancelled = new List<BigArchiveEntry> { new BigArchiveEntry { Name = "shaders\\waiting.fx", SourcePath = waiting } };
            Exception workerFailure = null;
            using (var cancellation = new CancellationTokenSource())
            {
                var worker = new Thread(() => { try { ShaderCompiler.Compile(request, cancelled, cancelRoot, cancellation.Token); } catch (Exception error) { workerFailure = error; } });
                worker.Start();
                string marker = Path.Combine(cancelRoot, "fx", "shaders", "compiled", "waiting.fxo.started");
                var clock = Stopwatch.StartNew();
                while (!File.Exists(marker) && worker.IsAlive && clock.ElapsedMilliseconds < 10000) Thread.Sleep(20);
                bool started = File.Exists(marker);
                int processId = started ? int.Parse(File.ReadAllText(marker)) : 0;
                cancellation.Cancel();
                Check(worker.Join(5000), "cancellation did not wait for the FX process to terminate");
                Check(started && workerFailure is OperationCanceledException, "cancelled external compilation did not report cancellation");
                bool alive = false;
                try { using (var child = Process.GetProcessById(processId)) alive = !child.HasExited; } catch (ArgumentException) { }
                Check(!alive && cancelled.Count == 1, "cancelled compiler survived or changed accepted inventory");
                Directory.Delete(cancelRoot, true);
            }
            return checks;
        }
    }
}
