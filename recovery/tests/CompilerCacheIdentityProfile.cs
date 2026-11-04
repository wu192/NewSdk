using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// HISTORICAL pre-cleanup filesystem microbenchmark, NOT the current production path.
// The mixed production EXE is intentionally never loaded or modified.
public static class CompilerCacheIdentityProfile
{
    static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static double Time(Action action)
    {
        Stopwatch clock = Stopwatch.StartNew(); action(); clock.Stop();
        return clock.Elapsed.TotalMilliseconds;
    }
    static string Hex(byte[] value) { return BitConverter.ToString(value).Replace("-", "").ToLowerInvariant(); }
    static List<string> Files(string sdk, string runtime)
    {
        List<string> files = new List<string>();
        files.Add(Path.Combine(sdk, "tools", "BinaryAssetBuilder.exe.config"));
        files.AddRange(Directory.GetFiles(Path.Combine(sdk, "Schemas"), "*", SearchOption.AllDirectories));
        foreach (string path in Directory.GetFiles(runtime))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".exe" || extension == ".dll" || extension == ".config") files.Add(path);
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }
    static bool Same(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!String.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }
    static void Guard(string path)
    {
        // Healthy-path algorithm matches CoreStreamCompiler.RejectReparseAncestors.
        for (DirectoryInfo directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Identity input ancestor is a reparse point");
    }
    static string Identity(string sdk, string runtime)
    {
        using (MemoryStream bytes = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8))
        {
            writer.Write("NewModCompiler.LegacyCache.Contract.v1");
            writer.Write(sdk); // Same DataRoot as the paired benchmark.
            writer.Write(true); writer.Write(true); writer.Write(true); // ss, pc, slowclean.
            foreach (string file in Files(sdk, runtime))
            {
                Guard(file); writer.Write(file);
                using (SHA256 algorithm = SHA256.Create())
                using (FileStream input = File.OpenRead(file)) writer.Write(algorithm.ComputeHash(input));
            }
            writer.Flush(); bytes.Position = 0;
            using (SHA256 algorithm = SHA256.Create()) return Hex(algorithm.ComputeHash(bytes));
        }
    }
    public static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Expected runtime directory, SDK root and profile.json"); return 2; }
        Dictionary<string, object> result = new Dictionary<string, object>();
        result["schemaVersion"] = 1; result["status"] = "started";
        result["productionMethodDirectlyMeasured"] = false;
        result["preflightMeasured"] = false;
        result["algorithmSourceCommit"] = "39039961847cd1ca9cb93743fd51741a4e5c9f49";
        result["method"] = "source-equivalent filesystem microbenchmark, NOT production method invocation";
        int exit = 1;
        try
        {
            Need(IntPtr.Size == 4, "Diagnostic requires x86 CLR4");
            string runtime = Path.GetFullPath(args[0]), sdk = Path.GetFullPath(args[1]);
            string[] names = Directory.GetFiles(runtime);
            HashSet<string> required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "NewModCompiler.exe", "NewModCompiler.exe.config", "antlr.runtime.dll" };
            // The pinned diagnostic driver can still supply the historical DLL package.
            if (names.Length == 5)
            {
                required.Add("BinaryAssetBuilder.TextureCompiler.dll");
                required.Add("BinaryAssetBuilder.TextureCompiler.dll.patch.json");
            }
            Need(names.Length == required.Count, "Accepted runtime directory must contain exactly its three source-runtime or five historical files");
            foreach (string path in names) Need(required.Remove(Path.GetFileName(path)), "Unexpected runtime file");
            result["diagnosticOrder"] = "Source-equivalent identity first+5warm; enumerate/guard/hash components 5each. First means first call within this standalone helper, not a production method, cold process or disk.";
            string first = null;
            // First invocation deliberately precedes diagnostic enumeration and
            // component passes, so those do not prewarm this first sample.
            result["firstIdentityMs"] = Time(delegate { first = Identity(sdk, runtime); });
            Need(first != null && first.Length == 64, "Source-equivalent identity did not return a SHA256 digest");
            result["identityDigest"] = first;
            double[] warm = new double[5]; bool stable = true;
            for (int i = 0; i < warm.Length; i++)
            {
                string current = null;
                warm[i] = Time(delegate { current = Identity(sdk, runtime); });
                stable &= String.Equals(first, current, StringComparison.Ordinal);
            }
            result["warmIdentityMs"] = warm; result["identityStable"] = stable;
            Need(stable, "Source-equivalent identity changed between read-only calls");
            List<string> files = Files(sdk, runtime);
            long totalBytes = 0, schemaBytes = 0, runtimeBytes = 0;
            int schemaCount = 0, runtimeCount = 0;
            string schemas = Path.Combine(sdk, "Schemas") + Path.DirectorySeparatorChar;
            foreach (string path in files)
            {
                long bytes = new FileInfo(path).Length; totalBytes += bytes;
                if (path.StartsWith(schemas, StringComparison.OrdinalIgnoreCase)) { schemaCount++; schemaBytes += bytes; }
                if (String.Equals(Path.GetDirectoryName(path), runtime, StringComparison.OrdinalIgnoreCase)) { runtimeCount++; runtimeBytes += bytes; }
            }
            result["fileCount"] = files.Count; result["fileBytes"] = totalBytes;
            result["schemaFileCount"] = schemaCount; result["schemaFileBytes"] = schemaBytes;
            result["runtimeFileCount"] = runtimeCount; result["runtimeFileBytes"] = runtimeBytes;
            result["sdkConfigurationFileCount"] = 1;
            double[] hashing = new double[5], guarding = new double[5], enumeration = new double[5];
            string hashOnlyDigest = null;
            for (int repeat = 0; repeat < 5; repeat++)
            {
                enumeration[repeat] = Time(delegate { Need(Same(files, Files(sdk, runtime)), "Enumerated source-equivalent file set changed"); });
                guarding[repeat] = Time(delegate { foreach (string file in files) Guard(file); });
                List<byte[]> contentHashes = new List<byte[]>(files.Count);
                hashing[repeat] = Time(delegate {
                    foreach (string file in files)
                        using (SHA256 algorithm = SHA256.Create())
                        using (FileStream input = File.OpenRead(file)) contentHashes.Add(algorithm.ComputeHash(input));
                });
                // Summarize the digests outside the hash-only measurement.
                using (MemoryStream hashes = new MemoryStream())
                {
                    foreach (byte[] value in contentHashes) hashes.Write(value, 0, value.Length);
                    hashes.Position = 0;
                    using (SHA256 algorithm = SHA256.Create()) hashOnlyDigest = Hex(algorithm.ComputeHash(hashes));
                }
            }
            result["enumerateAndOrdinalSortMs"] = enumeration;
            result["guardOnlyMs"] = guarding; result["hashOnlyMs"] = hashing;
            result["hashOnlyDigest"] = hashOnlyDigest;
            result["componentsAreAdditive"] = false;
            result["scope"] = "Source-equivalent filesystem microbenchmark, NOT production method invocation. No Assembly.Load, reflection or PreflightInputRoots measurement. Runtime parameter substitutes for the production assembly directory; DataRoot=sdk, ss/pc/slowclean=true. Components separately repeat the same file set with equivalent DirectoryInfo ancestor checks. Different scheduling/page-cache/JIT conditions mean component timings must not be added or treated as production or end-to-end compile time. No input/runtime/cache writes.";
            result["status"] = "passed"; exit = 0;
        }
        catch (Exception error) { result["status"] = "failed"; result["error"] = error.ToString(); }
        string json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(result);
        File.WriteAllText(Path.GetFullPath(args[2]), json + Environment.NewLine);
        Console.WriteLine(json);
        return exit;
    }
}
