using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NewModCompiler.Diagnostics;

namespace NewModCompiler.Application
{
    // Per-request directory metadata only. BAB FileHashItem persists its own
    // metadata beside the original FastHash; no cross-process hash receipt.
    internal sealed class FileHashCache
    {
        internal int Reads { get; private set; }
        internal int Reuses { get; private set; }
        internal long BytesRead { get; private set; }
        readonly BuildPerformance performance;
        readonly Dictionary<string, Dictionary<string, FileIdentity>> directories =
            new Dictionary<string, Dictionary<string, FileIdentity>>(StringComparer.OrdinalIgnoreCase);
        internal FileHashCache(BuildPerformance performance = null) { this.performance = performance; }
        internal void RecordRead(long length) { Reads++; Reuses--; BytesRead += length; }
#if NEWSdk_MONOLITHIC
        internal BinaryAssetBuilder.Core.FileHashInfo CoreIdentity(string path)
        {
            path = Path.GetFullPath(path);
            FileIdentity file;
            if (!DirectoryEntries(Path.GetDirectoryName(path)).TryGetValue(path, out file))
                return new BinaryAssetBuilder.Core.FileHashInfo();
            Reuses++;
            return new BinaryAssetBuilder.Core.FileHashInfo { Exists = true,
                LastWriteTime = DateTime.FromFileTimeUtc(file.LastWriteTime).ToLocalTime(), Identity = Key(file) };
        }
#endif
        internal IDictionary<string, string> SchemaIdentities(IEnumerable<string> paths)
        {
            // Standalone schema reload explicitly refreshes the per-request snapshot.
            foreach (string path in paths) directories.Remove(Path.GetDirectoryName(path));
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                FileIdentity file;
                result[path] = DirectoryEntries(Path.GetDirectoryName(path)).TryGetValue(path, out file) ? Key(file) : null;
            }
            return result;
        }
        Dictionary<string, FileIdentity> DirectoryEntries(string parent)
        {
            Dictionary<string, FileIdentity> files;
            if (!directories.TryGetValue(parent, out files))
            {
                files = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase);
                try { using (performance == null ? null : performance.Measure("identity.directory",true))
                    foreach (FileIdentity file in FileIdentity.ReadDirectory(parent)) files.Add(file.Path, file); }
                catch (DirectoryNotFoundException) { }
                directories.Add(parent, files);
            }
            return files;
        }

        internal static string Key(FileIdentity file)
        {
            string identity = file.VolumeSerial.ToString("x16", CultureInfo.InvariantCulture) + "|" +
                file.FileId.ToString("x16", CultureInfo.InvariantCulture) + "|" +
                file.LastWriteTime.ToString("x16", CultureInfo.InvariantCulture) + "|" + file.Size.ToString("x16", CultureInfo.InvariantCulture);
            return identity + "|" +
                file.ChangeTime.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
