using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace NewModCompiler.Application
{
    // The base manifest is consumed by Core. Only package names and LoD's
    // dependency leaf acquire the script's independent patch stream version.
    public static class PatchStreams
    {
        static BuildFailure Bad(string code, string message) { return new BuildFailure(ExitCode.Arguments, code, message); }
        static bool Explicit(BuildRequest request, string key)
        {
            EffectiveOption option;
            return request.Options.TryGetValue(key, out option) && option.Source != "default";
        }
        public static void Validate(BuildRequest request)
        {
            if (string.IsNullOrEmpty(request.BasePatchStream)) {
                if (request.PatchConfig != null || Explicit(request,"patch-version"))
                    throw Bad("PATCH_BASE_REQUIRED", "patch-version and patch-config require --base-patch-stream.");
                return;
            }
            if (request.PackageFormat != "big") throw Bad("PATCH_PACKAGE_FORMAT", "Patch streams require --package-format big.");
            if (request.FixNeutralAssets) throw Bad("PATCH_NEUTRAL_UNSUPPORTED", "Neutral-asset repair cannot be combined with a base patch stream.");
            string version = request.PatchVersion;
            if (!string.Equals(version,"none",StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(version) || version.Length > 64 || !AlphaNumeric(version[0]) || version.Any(c => !AlphaNumeric(c) && c != '.' && c != '_' && c != '-') || version.EndsWith(".",StringComparison.Ordinal)))
                throw Bad("PATCH_VERSION_INVALID", "patch-version must be none or an ASCII letter/digit followed by letters, digits, dots, underscores or hyphens (maximum 64 characters, no trailing dot).");
            ValidateConfig(request.PatchConfig);
            if (request.Command == "doctor") return;
            if (request.Content == null || !request.Content.Contains("base"))
                throw Bad("PATCH_BASE_CONTENT", "Patch compilation requires base content and Data/Mod.xml.");
            string path = request.BasePatchStream;
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path),".manifest",StringComparison.OrdinalIgnoreCase))
                throw Bad("PATCH_BASE_INVALID", "base-patch-stream must name an absolute .manifest file.");
            string stem = path.Substring(0,path.Length-Path.GetExtension(path).Length);
            foreach (string extension in new[] {".manifest",".bin",".imp",".relo"}) {
                string source = stem + extension;
                if ((!string.IsNullOrEmpty(request.OutputRoot) && Paths.Contains(request.OutputRoot,source)) ||
                    new[] {request.EventsPath,request.ReportPath,request.LogPath}.Any(output => !string.IsNullOrEmpty(output) && Paths.Contains(output,source)))
                    throw Bad("PATCH_BASE_OVERLAP", "Base patch stream inputs must remain outside output and diagnostics: " + source);
            }
            if (new[] {"mod","mod_m","mod_l"}.Any(name => string.Equals(Path.GetFileName(path),name + Suffix(request) + ".manifest",StringComparison.OrdinalIgnoreCase)))
                throw Bad("PATCH_BASE_SELF_REFERENCE", "The base manifest filename must differ from the packaged patch and LoD manifests; otherwise their runtime references would form a cycle.");
            if (request.PatchConfig != null) {
                string name = request.SkudefName ?? Path.GetFileName(request.ModRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
                if (string.Equals(request.PatchConfig,name + "_" + request.ModVersion + ".skudef",StringComparison.OrdinalIgnoreCase))
                    throw Bad("PATCH_CONFIG_SELF", "patch-config must name the base SKUDEF, not this patch's own SKUDEF.");
            }
        }
        // CLI input probing happens once. Core loads the actual base stream;
        // packaging consumes already accepted stream artifacts.
        public static void ValidateInput(BuildRequest request)
        {
            if (request.Command == "doctor" || string.IsNullOrEmpty(request.BasePatchStream)) return;
            if (!File.Exists(Path.Combine(request.ModRoot,"Data","Mod.xml")))
                throw Bad("PATCH_BASE_CONTENT", "Patch compilation requires Data/Mod.xml.");
            string path = request.BasePatchStream;
            string stem = Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path));
            foreach (string extension in new[] {".manifest",".bin",".imp",".relo"})
                if (!File.Exists(stem + extension)) throw Bad("PATCH_BASE_MISSING", "The base patch stream requires all four linked files: " + stem + extension);
            using (var input = File.OpenRead(path)) {
                byte[] header = new byte[48];
                if (input.Read(header,0,header.Length) != header.Length || header[0] != 0 || header[1] != 1 || header[2] != 6 || header[3] != 0)
                    throw Bad("PATCH_BASE_INVALID", "The base patch stream must be a linked little-endian RA3 v6 manifest.");
            }
        }
        internal static void ValidateConfig(string value)
        {
            if (value == null) return;
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || !value.EndsWith(".skudef",StringComparison.OrdinalIgnoreCase) || value.Length <= 7 || value.Any(c => c < 32 || c > 126 || "\\/:*?\"<>|".IndexOf(c) >= 0))
                throw Bad("PATCH_CONFIG_INVALID", "patch-config must be one ASCII .skudef filename without directories or control characters.");
        }
        static bool AlphaNumeric(char value) { return value >= '0' && value <= '9' || value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z'; }
        internal static string Suffix(BuildRequest request)
        {
            return string.IsNullOrEmpty(request.BasePatchStream) || string.Equals(request.PatchVersion,"none",StringComparison.OrdinalIgnoreCase) ? "" : "_" + request.PatchVersion;
        }
        internal static string EntryName(BuildRequest request, BuildTask task, string stem, string extension)
        { return stem + (task.Content == "base" ? Suffix(request) : "") + extension; }
        internal static IEnumerable<BigArchiveEntry> LoD(BuildRequest request, string manifest, string temporary, CancellationToken cancellation)
        {
            string suffix = Suffix(request);
            if (suffix.Length == 0) {
                foreach (string source in LoDStreamBuilder.Create(Path.Combine(Path.GetDirectoryName(manifest),"mod.manifest"),cancellation,Path.Combine(temporary,"package-lod")))
                    yield return new BigArchiveEntry { Name = "Data\\" + Path.GetFileName(source), SourcePath = source };
                yield break;
            }
            string directory = Path.Combine(temporary,"package-patch");
            string input = Path.Combine(directory,"mod" + suffix + ".manifest");
            FileTransfer.Snapshot(manifest,input);
            foreach (string source in LoDStreamBuilder.Create(input,cancellation,Path.Combine(temporary,"package-lod"))) {
                string leaf = Path.GetFileNameWithoutExtension(source);
                string lod = leaf.Substring(leaf.Length-2);
                yield return new BigArchiveEntry { Name = "Data\\mod" + lod + suffix + Path.GetExtension(source), SourcePath = source };
            }
            foreach (string name in new[] {"mod","mod_m","mod_l"}) {
                cancellation.ThrowIfCancellationRequested();
                string source = Path.Combine(directory,name + ".version");
                File.WriteAllBytes(source,Encoding.ASCII.GetBytes(suffix));
                yield return new BigArchiveEntry { Name = "Data\\" + name + ".version", SourcePath = source };
            }
        }
    }
}
