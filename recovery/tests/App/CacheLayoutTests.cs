using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NewModCompiler.Application;
using NewModCompiler.Cli;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Tests
{
    internal static class CacheLayoutTests
    {
        static int checks;
        static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Cache layout: " + message); }
        static void Reject(Action action)
        {
            try { action(); throw new Exception("Accepted a cache path outside the MOD partition"); }
            catch (BuildFailure failure) { Check(failure.Code == "CLI_CACHE_PATH", "Explicit cache boundary error: " + failure.Code); }
        }
        static void Junction(string path, string target)
        {
            using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + path + "\" \"" + target + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })) {
                string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
                process.WaitForExit(); Check(process.ExitCode == 0, "Junction creation failed: " + output + error);
            }
        }
        static string[] Snapshot(string root)
        {
            var directories = new[] { root }.Concat(Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                .Select(path => "directory:" + path.Substring(root.Length) + ":" + Directory.GetLastWriteTimeUtc(path).Ticks);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(path => {
                using (var hash = SHA256.Create()) return "file:" + path.Substring(root.Length) + ":" + File.GetLastWriteTimeUtc(path).Ticks + ":" +
                    BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
            });
            return directories.Concat(files).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
        static void PhysicalPaths(string root, string sdk, string mod, CacheLayout layout, BuildTask task)
        {
            File.WriteAllText(Path.Combine(mod, "source.keep"), "MOD source bytes must remain unchanged");
            string[] original = Snapshot(mod);
            Func<string, string, string, BuildRequest> parse = (sdkPath, modPath, output) => Options.Parse(new[] {
                "plan", "--sdk-root", sdkPath, "--mod", modPath, "--out", Path.Combine(root, output), "--mod-version", "1.0"
            });
            string sdkAlias = Path.Combine(root, "sdk-alias"), modAlias = Path.Combine(root, "mod-alias");
            try {
                Junction(sdkAlias, sdk); Junction(modAlias, mod);
                var aliased = parse(sdkAlias, modAlias, "alias-output").CacheLayout;
                Check(aliased.ModKey == layout.ModKey && aliased.CacheRoot == layout.CacheRoot, "SDK and MOD aliases retain the physical partition identity");
                Check(original.SequenceEqual(Snapshot(mod)), "Planning through input aliases changed MOD sources");
                Check(!Directory.Exists(layout.CacheBase), "Planning through input aliases created cache directories");
            } finally {
                if (Directory.Exists(modAlias)) Directory.Delete(modAlias);
                if (Directory.Exists(sdkAlias)) Directory.Delete(sdkAlias);
            }
            Junction(layout.CacheBase, mod);
            try {
                Reject(() => parse(sdk, mod, "cache-base-junction-output"));
                Check(original.SequenceEqual(Snapshot(mod)), "Redirected cache base changed MOD sources");
            } finally { Directory.Delete(layout.CacheBase); }
            Directory.CreateDirectory(layout.CacheRoot);
            Junction(layout.Session, mod);
            try {
                Reject(() => parse(sdk, mod, "session-junction-output"));
                Check(original.SequenceEqual(Snapshot(mod)), "Redirected session cache changed MOD sources");
            } finally { Directory.Delete(layout.Session); }
            Junction(layout.Streams, mod);
            try {
                bool rejected = false;
                try { layout.PrepareDirectory(layout.StreamDirectory(task)); }
                catch (IOException) { rejected = true; }
                catch (BuildFailure failure) { Check(failure.Code == "CLI_CACHE_PATH", "Explicit redirected stream boundary error"); rejected = true; }
                Check(rejected, "Stream preparation accepted a junction into MOD sources");
                Check(original.SequenceEqual(Snapshot(mod)), "Redirected stream workspace changed MOD sources");
            } finally { Directory.Delete(layout.Streams); }
        }
        internal static int Run(string root)
        {
            checks = 0;
            string sdk = Path.Combine(root, "sdk"), mod = Path.Combine(root, "one", "same-mod"), other = Path.Combine(root, "two", "same-mod");
            Directory.CreateDirectory(sdk); Directory.CreateDirectory(mod); Directory.CreateDirectory(other);
            Func<string, string, string[], BuildRequest> parse = (source, output, extra) => Options.Parse(new[] {
                "plan", "--mod", source, "--sdk-root", sdk, "--out", Path.Combine(root, output), "--mod-version", "1.0"
            }.Concat(extra).ToArray());
            var request = parse(mod, "out-a", new string[0]); var layout = request.CacheLayout;
            Check(layout.CacheBase == Path.Combine(sdk, "SuperCompilerCache"), "Uses the supplied SDK installation");
            Check(layout.ModKey.StartsWith("same-mod_", StringComparison.Ordinal) && layout.ModKey.Length == 8 + 1 + 32, "Short label plus 16-byte hexadecimal identity");
            string physical = DiagnosticDirectory.ResolveInputDirectory(mod).TrimEnd('\\', '/').ToUpperInvariant() + Path.DirectorySeparatorChar;
            using (var hash = SHA256.Create()) Check(layout.ModKey.EndsWith(BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(physical)), 0, 16).Replace("-", "").ToLowerInvariant()), "Hashes normalized physical path once");
            Check(parse(mod, "out-b", new string[0]).CacheLayout.CacheRoot == layout.CacheRoot, "Changing output retains cache identity");
            var changedVersion = Options.Parse(new[] {"plan", "--mod", mod, "--sdk-root", sdk, "--out", Path.Combine(root, "out-c"), "--mod-version", "2.7"});
            Check(changedVersion.CacheLayout.CacheRoot == layout.CacheRoot, "Version is not a cache identity input");
            Check(parse(other, "out-d", new string[0]).CacheLayout.CacheRoot != layout.CacheRoot, "Same-name MODs have independent partitions");
            Check(!Directory.Exists(layout.CacheBase) && !Directory.Exists(request.OutputRoot), "Planning creates no cache or output directory");
            Check(object.ReferenceEquals(layout, CacheLayout.ForRequest(request)), "One immutable layout per request");
            var task = new BuildTask {Content = "base", InputPath = Path.Combine(mod, "Data", "Mod.xml")};
            Check(layout.StreamDirectory(task) == Path.Combine(layout.Streams, "base", "Mod"), "Shared stream layout");
            Check(layout.BuildTempDirectory("build-a") != layout.BuildTempDirectory("build-b"), "Temporary inputs are scoped to a build ID");
            Reject(() => layout.BuildTempDirectory("../escape"));
            foreach (string key in new[] {"scd", "bcd", "iod"}) {
                string directory = Path.Combine(layout.CacheRoot, "custom-" + key);
                var custom = parse(mod, "custom-output", new[] {"/" + key + ":" + directory});
                Check((key == "scd" ? custom.CacheLayout.Session : key == "bcd" ? custom.CacheLayout.BuildCache : custom.CacheLayout.Intermediate) == directory, "Partition-local override " + key);
                foreach (string outside in new[] {mod, sdk, Path.Combine(root, "legacy-out"), Path.Combine(layout.CacheBase, "another-mod")})
                    Reject(() => parse(mod, "rejected-output", new[] {"/" + key + ":" + outside}));
            }
            string preset = Path.Combine(mod, "newmodcompiler.json");
            File.WriteAllText(preset, "{\"scd\":\"cache-in-source\"}");
            Reject(() => parse(mod, "preset-output", new string[0])); File.Delete(preset);
            string inside = Path.Combine(sdk, "Mods", "Inside"); Directory.CreateDirectory(inside);
            var nested = parse(inside, "inside-output", new string[0]);
            Check(Paths.Contains(Path.Combine(sdk, "SuperCompilerCache"), nested.CacheLayout.CacheRoot) && !Paths.Contains(inside, nested.CacheLayout.CacheRoot), "SDK/Mods sources remain separate from writable partition");
            Reject(() => parse(sdk, "sdk-is-mod", new string[0]));
            Check(Options.Parse(new[] {"doctor", "--sdk-root", sdk}).CacheLayout == null && !Directory.Exists(layout.CacheBase), "Doctor without MOD creates no cache");
            PhysicalPaths(root, sdk, mod, layout, task);
            return checks;
        }
    }
}
