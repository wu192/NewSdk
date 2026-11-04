using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace BinaryAssetBuilder.UiCompiler
{
    /// <summary>Linked SAGE v6 stream set; no intermediate SWF/APT/XML files.</summary>
    public sealed class ModStreams
    {
        public byte[] Manifest { get; }
        public byte[] Binary { get; }
        public byte[] Relocations { get; }
        public byte[] Imports { get; }
        public IReadOnlyList<CompiledUiAsset> Assets { get; }
        public IReadOnlyList<string> InputFiles { get; }
        internal ModStreams(IReadOnlyList<CompiledUiAsset> assets)
        {
            Assets = assets; InputFiles = (assets as UiAssetGroup)?.InputFiles ?? Array.Empty<string>();
            var seen = new HashSet<ulong>(); foreach (var a in assets) if (!seen.Add((ulong)a.TypeId << 32 | a.InstanceId)) throw new InvalidDataException("Duplicate/colliding UI asset " + a.TypeName + ":" + a.Name);
            using (var names = new MemoryStream()) using (var sources = new MemoryStream()) using (var entries = new MemoryStream()) using (var writer = new BinaryWriter(entries, Encoding.UTF8, true))
            {
                var sourcePositions = new Dictionary<string, int>(StringComparer.Ordinal); uint checksum = UiCatalog.AllTypesHash; int total = 0, max = 0, maxRelo = 0, maxImp = 0;
                foreach (var a in assets)
                {
                    byte[] n = Encoding.ASCII.GetBytes(a.TypeName + ":" + a.Name); int namePosition = (int)names.Length; names.Write(n, 0, n.Length); names.WriteByte(0);
                    if (!sourcePositions.TryGetValue(a.Source, out int sourcePosition)) { sourcePosition = (int)sources.Length; byte[] s = Encoding.UTF8.GetBytes(a.Source); sources.Write(s, 0, s.Length); sources.WriteByte(0); sourcePositions.Add(a.Source, sourcePosition); }
                    var b = a.Buffer; writer.Write(a.TypeId); writer.Write(a.InstanceId); writer.Write(a.TypeHash); writer.Write(a.InstanceHash); writer.Write(0); writer.Write(0); writer.Write(namePosition); writer.Write(sourcePosition); writer.Write(b.InstanceData.Length); writer.Write(b.RelocationData.Length); writer.Write(b.ImportsData.Length); writer.Write(0);
                    total = checked(total + b.InstanceData.Length); max = Math.Max(max, b.InstanceData.Length); maxRelo = Math.Max(maxRelo, b.RelocationData.Length); maxImp = Math.Max(maxImp, b.ImportsData.Length);
                    checksum = SageHash.Combine(checksum, b.InstanceData); checksum = SageHash.Combine(checksum, BitConverter.GetBytes(a.TypeId)); checksum = SageHash.Combine(checksum, BitConverter.GetBytes(a.InstanceId));
                }
                using (var manifest = new MemoryStream()) using (var w = new BinaryWriter(manifest, Encoding.UTF8, true))
                {
                    w.Write((byte)0); w.Write((byte)1); w.Write((ushort)6); w.Write(checksum); w.Write(UiCatalog.AllTypesHash); w.Write(assets.Count); w.Write(total); w.Write(max); w.Write(maxRelo); w.Write(maxImp); w.Write(0); w.Write(0); w.Write((int)names.Length); w.Write((int)sources.Length); w.Write(entries.ToArray()); w.Write(names.ToArray()); w.Write(sources.ToArray()); Manifest = manifest.ToArray();
                }
                Binary = Linked(checksum, assets.Select(a => a.Buffer.InstanceData)); Relocations = Linked(checksum, assets.Select(a => a.Buffer.RelocationData)); Imports = Linked(checksum, assets.Select(a => a.Buffer.ImportsData));
            }
        }
        private static byte[] Linked(uint checksum, IEnumerable<byte[]> chunks) { using (var output = new MemoryStream()) using (var w = new BinaryWriter(output)) { w.Write(checksum); foreach (var chunk in chunks) w.Write(chunk); return output.ToArray(); } }
        /// <summary>Writes exactly four final stream files under the given basename.</summary>
        public void WriteTo(string outputBase)
        {
            if (string.IsNullOrWhiteSpace(outputBase)) throw new ArgumentException("An output basename is required.", nameof(outputBase));
            string path = Path.GetFullPath(outputBase); Directory.CreateDirectory(Path.GetDirectoryName(path));
            // All conversion and validation finish before touching an existing stream set.
            string suffix = "." + Guid.NewGuid().ToString("N"); string[] extensions = { ".bin", ".relo", ".imp", ".manifest" }; byte[][] data = { Binary, Relocations, Imports, Manifest }; bool[] installed = new bool[4], replaced = new bool[4]; bool preserveBackups = false;
            foreach (string extension in extensions) if (Directory.Exists(path + extension)) throw new IOException("Stream output is a directory: " + path + extension);
            try
            {
                for (int i = 0; i < extensions.Length; i++) File.WriteAllBytes(path + extensions[i] + suffix + ".new", data[i]);
                for (int i = 0; i < extensions.Length; i++) { string target = path + extensions[i]; if (File.Exists(target)) { File.Replace(target + suffix + ".new", target, target + suffix + ".old"); replaced[i] = true; } else File.Move(target + suffix + ".new", target); installed[i] = true; }
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                for (int i = extensions.Length - 1; i >= 0; i--) if (installed[i])
                { string target = path + extensions[i]; try { if (replaced[i]) { if (File.Exists(target)) File.Replace(target + suffix + ".old", target, null); else File.Move(target + suffix + ".old", target); } else File.Delete(target); } catch (Exception rollback) { failures.Add(rollback); } }
                if (failures.Count > 1) { preserveBackups = true; throw new AggregateException("Could not restore the previous stream set; retained .old backups.", failures); } throw;
            }
            finally { foreach (string extension in extensions) { string temporary = path + extension + suffix + ".new", backup = path + extension + suffix + ".old"; if (File.Exists(temporary)) File.Delete(temporary); if (!preserveBackups && File.Exists(backup)) File.Delete(backup); } }
        }
    }
}
