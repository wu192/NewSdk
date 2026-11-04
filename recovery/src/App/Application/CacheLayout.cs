using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // One immutable, physically checked partition per SDK installation and MOD.
    // Computing a layout is read-only, including for plan and doctor.
    public sealed class CacheLayout
    {
        public string ModKey { get; private set; }
        public string CacheBase { get; private set; }
        public string CacheRoot { get; private set; }
        public string SharedReferences { get; private set; }
        public string Session { get; private set; }
        public string BuildCache { get; private set; }
        public string Intermediate { get; private set; }
        public string Streams { get; private set; }
        public string Generated { get; private set; }
        public string Temp { get; private set; }
        readonly string sourceMod, publicationRoot;

        CacheLayout(BuildRequest request)
        {
            string sdk = DiagnosticDirectory.ResolveInputDirectory(request.SdkRoot);
            string mod = DiagnosticDirectory.ResolveInputDirectory(request.ModRoot);
            sourceMod = mod;
            string normalized = mod.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd('\\', '/').ToUpperInvariant() + Path.DirectorySeparatorChar;
            string identity;
            using (var hash = SHA256.Create())
                identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(normalized)), 0, 16).Replace("-", "").ToLowerInvariant();
            string name = Path.GetFileName(mod.TrimEnd('\\', '/')).ToLowerInvariant();
            var label = new StringBuilder();
            foreach (char value in name) {
                if (label.Length == 12) break;
                label.Append(char.IsLetterOrDigit(value) || value == '-' || value == '_' ? value : '_');
            }
            if (label.Length == 0) label.Append("Mod");
            ModKey = label + "_" + identity;
            CacheBase = Path.Combine(sdk, "SuperCompilerCache");
            SharedReferences = Path.Combine(CacheBase, "shared");
            CacheRoot = Path.Combine(CacheBase, ModKey);
            ValidatePath(CacheRoot);
            if (File.Exists(CacheRoot)) throw Invalid("Cache partition is an existing file: " + CacheRoot);
            string physical = DiagnosticDirectory.ResolveOutput(CacheRoot);
            if (!string.Equals(physical, CacheRoot, StringComparison.OrdinalIgnoreCase) ||
                Paths.Contains(mod, physical) || Paths.Contains(physical, mod))
                throw Invalid("The SDK cache partition must not alias or overlap MOD sources.");
            if (!string.IsNullOrEmpty(request.OutputRoot)) {
                string output = DiagnosticDirectory.ResolveOutput(request.OutputRoot);
                publicationRoot = output;
                if (Paths.Contains(output, physical) || Paths.Contains(physical, output))
                    throw Invalid("The SDK cache partition must not alias or overlap output.");
            }
            Session = CacheOption(request, "session-cache-directory", "session");
            BuildCache = CacheOption(request, "build-cache-directory", "build-cache");
            Intermediate = CacheOption(request, "iod", "intermediate");
            Streams = Path.Combine(CacheRoot, "streams");
            Generated = Path.Combine(CacheRoot, "generated");
            Temp = Path.Combine(CacheRoot, "temp");
        }

        public static CacheLayout ForRequest(BuildRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (request.CacheLayout != null) return request.CacheLayout;
            if (string.IsNullOrEmpty(request.ModRoot)) return null; // doctor without a MOD
            try { return request.CacheLayout = new CacheLayout(request); }
            // Explicit handlers avoid exception-filter IL in the mixed-mode
            // application's MSVC /LTCG link, while retaining the same failures.
            catch (IOException error) { throw Invalid(error.Message); }
            catch (UnauthorizedAccessException error) { throw Invalid(error.Message); }
            catch (ArgumentException error) { throw Invalid(error.Message); }
            catch (NotSupportedException error) { throw Invalid(error.Message); }
        }

        string CacheOption(BuildRequest request, string key, string directory)
        {
            EffectiveOption option;
            string path = request.Options.TryGetValue(key, out option) ? Path.GetFullPath(option.Value) : Path.Combine(CacheRoot, directory);
            ValidatePath(path);
            if (!Paths.Contains(CacheRoot, path)) throw Invalid("Option " + key + " must point inside this MOD's SDK cache partition: " + CacheRoot);
            DiagnosticDirectory.CheckDirectory(path, CacheRoot);
            if (File.Exists(path)) throw Invalid("Cache directory is an existing file: " + path);
            return path;
        }

        public string StreamDirectory(BuildTask task)
        {
            if (task == null) throw new ArgumentNullException("task");
            string content = task.Content;
            if (content != "base" && content != "apt" && content != "maps") throw Invalid("Unknown stream content: " + content);
            string name = Path.GetFileNameWithoutExtension(task.InputPath);
            ValidateSegment(name);
            string result = Path.Combine(Streams, content, name);
            ValidatePath(result);
            return result;
        }

        public void ValidateStreamDirectory(BuildTask task, string path)
        {
            if (string.IsNullOrEmpty(path) || !string.Equals(StreamDirectory(task), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                throw new BuildFailure(ExitCode.Output, "CORE_OUTPUT_INVALID", "The stream workspace must be the current MOD's CacheLayout task directory.");
        }

        public string BuildTempDirectory(string buildId)
        {
            ValidateSegment(buildId);
            string result = Path.Combine(Temp, buildId);
            ValidatePath(result);
            return result;
        }

        public void PrepareDirectory(string path)
        {
            path = Path.GetFullPath(path);
            ValidatePath(path);
            if (!Paths.Contains(CacheRoot, path)) throw Invalid("Compiler writes must remain in this MOD's SDK cache partition.");
            // Check all existing ancestors, including the partition itself, once
            // at each writable boundary before handing it to BAB.
            DiagnosticDirectory.PrepareDirectory(path);
        }

        // Only read-only reference material may use the SDK-wide shared area;
        // regular compilation outputs retain their per-MOD boundary above.
        public void PrepareSharedReferenceDirectory(string path)
        {
            path = Path.GetFullPath(path);
            ValidatePath(path);
            if (!Paths.Contains(SharedReferences, path)) throw Invalid("Reference cache writes must remain in the SDK shared directory.");
            string physical = DiagnosticDirectory.ResolveOutput(path);
            if (!string.Equals(physical, path, StringComparison.OrdinalIgnoreCase) ||
                Paths.Contains(sourceMod, physical) || Paths.Contains(physical, sourceMod) ||
                publicationRoot != null && (Paths.Contains(publicationRoot, physical) || Paths.Contains(physical, publicationRoot)))
                throw Invalid("The SDK shared reference cache must not alias or overlap MOD sources or output.");
            DiagnosticDirectory.PrepareDirectory(path);
        }

        static void ValidateSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == ".." || value.IndexOfAny(new[] {'\\', '/', ':'}) >= 0)
                throw Invalid("Cache path requires a single directory name.");
            DiagnosticDirectory.ValidateSyntax(value);
        }
        static void ValidatePath(string path)
        {
            DiagnosticDirectory.ValidateSyntax(path);
            if (path.Length >= 248) throw Invalid("Cache directory exceeds the supported Win32/MAX_PATH limit: " + path);
        }
        static BuildFailure Invalid(string message) { return new BuildFailure(ExitCode.Arguments, "CLI_CACHE_PATH", message); }
    }
}
