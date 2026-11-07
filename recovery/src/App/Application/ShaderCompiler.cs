using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NewModCompiler.Application
{
    // SDK-X compiles the merged Additional/Shaders tree before MakeBig. Keep
    // sources private so relative includes see the same SDK-then-MOD overlay.
    public static class ShaderCompiler
    {
        static readonly Regex Include = new Regex(@"^\s*#\s*include\s*[""<]([^"">]+)["">]", RegexOptions.Multiline);
        static readonly HashSet<string> TextExtensions = new HashSet<string>(new[] { ".fx", ".fxh", ".h", ".hlsl", ".inc" }, StringComparer.OrdinalIgnoreCase);

        public static void Compile(BuildRequest request, IList<BigArchiveEntry> entries, string temporary, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var shaders = request.CompileFx ? entries.Where(entry => entry.Name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
                .Select(entry => Canonical(entry.Name)).Where(name => name.StartsWith("shaders\\", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf('\\', 8) < 0).OrderBy(name => name, StringComparer.Ordinal).ToArray() : new string[0];
            if (shaders.Length == 0)
            {
                for (int i = entries.Count - 1; i >= 0; --i)
                {
                    string name = entries[i].Name;
                    if (name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)) entries.RemoveAt(i);
                }
                return;
            }
            var inputs = entries.ToDictionary(entry => Canonical(entry.Name), StringComparer.OrdinalIgnoreCase);
            var outputs = new List<BigArchiveEntry>();
            string compiler = FindCompiler(request), workspace = Path.Combine(temporary, "fx");
            Directory.CreateDirectory(workspace);
            Materialize(inputs, workspace, cancellation);
            string destination = Path.Combine(workspace, "shaders", "compiled");
            Directory.CreateDirectory(destination);
            foreach (string shader in shaders)
            {
                cancellation.ThrowIfCancellationRequested();
                string source = LocalPath(workspace, shader), output = Path.Combine(destination, Path.GetFileName(source) + "o");
                string flags = "/nologo /O3 /WX /T fx_2_0";
                if (File.ReadLines(source).Any(line => line.Trim() == "/// fxc-wrapper-disable-preshader")) flags += " /Op";
                Run(request, compiler, flags + " /Fo " + Quote(output) + " " + Quote(source), Path.GetDirectoryName(source), cancellation);
                if (!File.Exists(output)) throw new BuildFailure(ExitCode.Compilation, "FX_OUTPUT_MISSING", "FX compiler did not create " + output);
                outputs.Add(new BigArchiveEntry { Name = "shaders\\compiled\\" + Path.GetFileName(output), SourcePath = output });
            }
            cancellation.ThrowIfCancellationRequested();
            var replacements = outputs.ToDictionary(entry => Canonical(entry.Name), StringComparer.OrdinalIgnoreCase);
            // Commit inventory changes only after every shader has succeeded.
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                string name = Canonical(entries[i].Name);
                if (name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)) entries.RemoveAt(i);
                else if (replacements.ContainsKey(name)) { entries[i] = replacements[name]; replacements.Remove(name); }
            }
            foreach (var output in replacements.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal)) entries.Add(output);
        }

        static void Materialize(IDictionary<string, BigArchiveEntry> inputs, string workspace, CancellationToken cancellation)
        {
            var pending = new Queue<string>(inputs.Keys.Where(name => name.StartsWith("shaders\\", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("shaders\\compiled\\", StringComparison.OrdinalIgnoreCase)));
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = pending.Dequeue();
                string target = LocalPath(workspace, name);
                if (copied.Add(name))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(inputs[name].SourcePath, target);
                }
                string extension = Path.GetExtension(name);
                if ((!TextExtensions.Contains(extension) && !included.Contains(name)) || !scanned.Add(name)) continue;
                // Follow literal includes outside Shaders as well, preserving
                // their archive-relative location and overlay precedence.
                foreach (Match match in Include.Matches(File.ReadAllText(target)))
                {
                    string include = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    foreach (string parent in new[] { Path.GetDirectoryName(target), Path.Combine(workspace, "shaders"), workspace })
                    {
                        string candidate = Path.GetFullPath(Path.Combine(parent, include));
                        if (!Paths.Contains(workspace, candidate)) continue;
                        string relative = Canonical(candidate.Substring(workspace.Length + 1));
                        if (!inputs.ContainsKey(relative)) continue;
                        included.Add(relative);
                        pending.Enqueue(relative);
                        break;
                    }
                }
            }
        }

        static string FindCompiler(BuildRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.FxcPath))
            {
                string configured = FindExecutable(request.FxcPath);
                if (configured != null) return configured;
                throw new BuildFailure(ExitCode.Environment, "FX_COMPILER_MISSING", "Configured FX compiler was not found: " + request.FxcPath);
            }
            foreach (string candidate in new[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fxc.exe"), Path.Combine(request.SdkRoot, "ARModExtraTools", "fxc.exe"), Path.Combine(request.SdkRoot, "tools", "fxc.exe"), "fxc.exe" })
            {
                string compiler = FindExecutable(candidate);
                if (compiler != null) return compiler;
            }
            throw new BuildFailure(ExitCode.Environment, "FX_COMPILER_MISSING", "FX sources require the bundled fxc.exe, or an explicit --fxc path.");
        }

        static string FindExecutable(string name)
        {
            if (File.Exists(name)) return Path.GetFullPath(name);
            if (Path.IsPathRooted(name) || name.IndexOfAny(new[] { '\\', '/' }) >= 0) return null;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (directory.Length == 0) continue;
                string candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        static void Run(BuildRequest request, string compiler, string arguments, string directory, CancellationToken cancellation)
        {
            var start = new ProcessStartInfo(compiler, arguments) { WorkingDirectory = directory, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = new Process { StartInfo = start })
            {
                try { process.Start(); }
                catch (System.ComponentModel.Win32Exception error) { throw new BuildFailure(ExitCode.Environment, "FX_COMPILER_START", "Cannot start FX compiler: " + error.Message); }
                if (!request.ExternalTools.Contains(compiler, StringComparer.OrdinalIgnoreCase)) request.ExternalTools.Add(compiler);
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    while (!process.WaitForExit(50)) cancellation.ThrowIfCancellationRequested();
                    cancellation.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    // The package workspace cannot be removed while fxc still
                    // owns input/output handles. Kill and reap before returning.
                    if (!process.HasExited)
                    {
                        try { process.Kill(); }
                        catch (InvalidOperationException) { if (!process.HasExited) throw; }
                    }
                    process.WaitForExit();
                    Task.WaitAll(stdout, stderr);
                    throw;
                }
                string output = stdout.GetAwaiter().GetResult(), errorOutput = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new BuildFailure(ExitCode.Compilation, "FX_COMPILATION_FAILED", "FX compiler exited with " + process.ExitCode + ": " + output + errorOutput);
            }
        }

        static string Canonical(string name) { return name.Replace('/', '\\').ToLowerInvariant(); }
        static string LocalPath(string workspace, string name) { return Path.Combine(workspace, name.Replace('\\', Path.DirectorySeparatorChar)); }
        static string Quote(string value) { return "\"" + value + "\""; }
    }
}
