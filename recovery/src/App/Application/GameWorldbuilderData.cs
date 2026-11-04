using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace NewModCompiler.Application
{
    // Original ModAssetResolver: Install Dir -> Data/WBData_12.big. Extraction
    // stays in one SDK-wide reference cache instead of writing SDK inputs.
    public static class GameWorldbuilderData
    {
        static readonly string[] Extensions = {".manifest", ".bin", ".imp", ".relo"};
        const string GameKey = @"SOFTWARE\Electronic Arts\Electronic Arts\Red Alert 3";
        sealed class Member {public long Offset, Bytes;}
        public sealed class CachedFile {public string name {get;set;} public long bytes {get;set;}}
        public sealed class Receipt
        {
            public int schemaVersion {get;set;}
            public string sourcePath {get;set;}
            public long sourceBytes {get;set;}
            public long sourceModifiedUtcTicks {get;set;}
            public CachedFile[] files {get;set;}
        }

        public static string FindArchive(string gameRoot)
        {
            if (!string.IsNullOrEmpty(gameRoot)) return RequireArchive(gameRoot);
            foreach (RegistryView view in new[] {RegistryView.Registry32, RegistryView.Registry64}) {
                try {
                    using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    foreach (string key in new[] {GameKey, @"SOFTWARE\Wow6432Node\Electronic Arts\Electronic Arts\Red Alert 3"})
                    using (var installed = machine.OpenSubKey(key)) {
                        string root = installed == null ? null : installed.GetValue("Install Dir") as string;
                        if (!string.IsNullOrWhiteSpace(root)) {
                            string candidate = Path.Combine(root, "Data", "WBData_12.big");
                            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                        }
                    }
                }
                catch (SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
            // The original tool also falls back to a relative Data directory.
            string relative = Path.GetFullPath(Path.Combine("Data", "WBData_12.big"));
            if (File.Exists(relative)) return relative;
            throw Missing("Game Data/WBData_12.big was not found. Install the SDK WorldBuilder data in the registered Red Alert 3 installation, or set --game-root to that installation directory.");
        }

        static string RequireArchive(string root)
        {
            string path = Path.Combine(Path.GetFullPath(root), "Data", "WBData_12.big");
            if (!File.Exists(path)) throw Missing("Neutral repair requires the game's SDK WorldBuilder archive: " + path);
            return path;
        }

        static BuildFailure Missing(string message) {return new BuildFailure(ExitCode.Environment, "PACKAGE_NEUTRAL_REFERENCE_MISSING", message);}

        public static void Validate(string archive)
        {
            using (var input = OpenArchive(archive)) ReadMembers(input);
        }

        public static string Prepare(BuildRequest request, string archive, CancellationToken cancellation)
        {
            using (Acquire(request, cancellation)) return PrepareLocked(request, archive, cancellation);
        }

        internal static IDisposable Acquire(BuildRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            CacheLayout layout = CacheLayout.ForRequest(request);
            if (layout == null) throw new ArgumentException("Game WorldBuilder extraction requires a MOD build request.");
            layout.PrepareSharedReferenceDirectory(layout.SharedReferences);
            string path = Path.Combine(layout.SharedReferences, "neutral-worldbuilder.lock");
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                try {return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);}
                catch (IOException error) {
                    int code = error.HResult & 0xffff;
                    if (code != 32 && code != 33 && code != 11) throw;
                    cancellation.WaitHandle.WaitOne(50);
                }
            }
        }

        // Caller holds the shared lease until the reference consumer finishes.
        internal static string PrepareLocked(BuildRequest request, string archive, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            CacheLayout layout = CacheLayout.ForRequest(request);
            if (layout == null) throw new ArgumentException("Game WorldBuilder extraction requires a MOD build request.");
            string cache = Path.Combine(layout.SharedReferences, "neutral-worldbuilder");
            archive = Path.GetFullPath(archive);
            using (var input = OpenArchive(archive)) {
                long modified = File.GetLastWriteTimeUtc(archive).Ticks;
                string legacy = Path.Combine(layout.CacheRoot, "neutral-worldbuilder");
                if (Reusable(cache, archive, input.Length, modified)) {
                    if (Reusable(legacy, archive, input.Length, modified)) Directory.Delete(legacy, true);
                    return Path.Combine(cache, "Worldbuilder.manifest");
                }
                if (Reusable(legacy, archive, input.Length, modified)) {
                    Install(cache, legacy);
                    return Path.Combine(cache, "Worldbuilder.manifest");
                }
                var members = ReadMembers(input);
                string staging = cache + ".import-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(staging);
                try {
                    var files = new List<CachedFile>();
                    foreach (string extension in Extensions) {
                        cancellation.ThrowIfCancellationRequested();
                        string name = "Worldbuilder" + extension;
                        Member member = members[extension];
                        input.Position = member.Offset;
                        using (var output = new FileStream(Path.Combine(staging, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024))
                            Extract(input, member, output, cancellation);
                        files.Add(new CachedFile {name = name, bytes = new FileInfo(Path.Combine(staging, name)).Length});
                    }
                    cancellation.ThrowIfCancellationRequested();
                    var receipt = new Receipt {schemaVersion = 1, sourcePath = archive, sourceBytes = input.Length, sourceModifiedUtcTicks = modified, files = files.ToArray()};
                    File.WriteAllText(Path.Combine(staging, "source.json"), new JavaScriptSerializer().Serialize(receipt));
                    Install(cache, staging);
                    return Path.Combine(cache, "Worldbuilder.manifest");
                }
                finally {if (Directory.Exists(staging)) Directory.Delete(staging, true);}
            }
        }

        static void Install(string cache, string source)
        {
            string previous = cache + ".previous-" + Guid.NewGuid().ToString("N");
            bool moved = Directory.Exists(cache);
            if (moved) Directory.Move(cache, previous);
            try {Directory.Move(source, cache);}
            catch {if (moved) Directory.Move(previous, cache);throw;}
            if (moved) Directory.Delete(previous, true);
        }

        static bool Reusable(string cache, string archive, long size, long modified)
        {
            string path = Path.Combine(cache, "source.json");
            if (!File.Exists(path)) return false;
            Receipt receipt;
            try {receipt = new JavaScriptSerializer().Deserialize<Receipt>(File.ReadAllText(path));}
            catch (ArgumentException) {return false;}
            catch (InvalidOperationException) {return false;}
            if (receipt == null || receipt.schemaVersion != 1 || !string.Equals(receipt.sourcePath, archive, StringComparison.OrdinalIgnoreCase) || receipt.sourceBytes != size || receipt.sourceModifiedUtcTicks != modified || receipt.files == null || receipt.files.Length != 4) return false;
            for (int i = 0; i < Extensions.Length; i++) {
                string expected = "Worldbuilder" + Extensions[i];CachedFile file = receipt.files[i];
                if (file == null || file.name != expected) return false;
                var actual = new FileInfo(Path.Combine(cache, expected));
                if (!actual.Exists || actual.Length != file.bytes) return false;
            }
            return true;
        }

        static FileStream OpenArchive(string path) {return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024);}
        static uint BigEndian(BinaryReader reader)
        {
            byte[] data = reader.ReadBytes(4);
            if (data.Length != 4) throw new EndOfStreamException("WorldBuilder BIG header ended early.");
            return ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        }

        static Dictionary<string, Member> ReadMembers(FileStream input)
        {
            input.Position = 0;
            var reader = new BinaryReader(input, Encoding.ASCII, true);
            string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != "BIG4" && magic != "BIGF") throw new InvalidDataException("WorldBuilder archive must be BIG4/BIGF.");
            BigEndian(reader);uint count = BigEndian(reader), tableEnd = BigEndian(reader);
            if (tableEnd < 16 || tableEnd > input.Length || count > (tableEnd - 16) / 9) throw new InvalidDataException("WorldBuilder BIG table length differs.");
            var result = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++) {
                long offset = BigEndian(reader), size = BigEndian(reader);
                var name = new StringBuilder();bool terminated = false;
                while (input.Position < tableEnd) {
                    byte value = reader.ReadByte();
                    if (value == 0) {terminated = true;break;}
                    name.Append((char)value);
                }
                if (!terminated || offset < tableEnd || offset + size > input.Length) throw new InvalidDataException("WorldBuilder BIG member range differs.");
                string normalized = name.ToString().Replace('/', '\\');
                foreach (string extension in Extensions) if (string.Equals(normalized, "Data\\Worldbuilder" + extension, StringComparison.OrdinalIgnoreCase)) {
                    if (result.ContainsKey(extension)) throw new InvalidDataException("WorldBuilder BIG contains duplicate stream members.");
                    result.Add(extension, new Member {Offset = offset, Bytes = size});
                }
            }
            foreach (string extension in Extensions) if (!result.ContainsKey(extension)) throw Missing("Game WorldBuilder archive is missing Data\\Worldbuilder" + extension + ".");
            return result;
        }

        static void Extract(FileStream input, Member member, FileStream output, CancellationToken cancellation)
        {
            byte[] prefix = new byte[10];int prefixSize = (int)Math.Min(prefix.Length, member.Bytes);
            if (input.Read(prefix, 0, prefixSize) != prefixSize) throw new EndOfStreamException("WorldBuilder BIG member ended early.");
            int flags = prefixSize >= 2 ? (prefix[0] << 8) | prefix[1] : 0;
            long unpacked = 0;
            if (prefixSize >= 5 && (flags & 0x3eff) == 0x10fb) {
                int width = (flags & 0x8000) != 0 ? 4 : 3;
                int sizeOffset = 2 + ((flags & 0x100) != 0 ? width : 0);
                if(prefixSize<sizeOffset+width)throw new InvalidDataException("RefPack size header ended early.");
                for (int i = 0; i < width; i++) unpacked = (unpacked << 8) | prefix[sizeOffset + i];
            }
            input.Position = member.Offset;
            if (unpacked > member.Bytes) {new RefPack(input, member.Offset + member.Bytes, output, cancellation).Decode(flags, unpacked);return;}
            byte[] buffer = new byte[128 * 1024];long remaining = member.Bytes;
            while (remaining > 0) {
                cancellation.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException("WorldBuilder BIG member ended early.");
                output.Write(buffer, 0, read);remaining -= read;
            }
        }

        sealed class RefPack
        {
            readonly FileStream input, output;readonly long end;readonly CancellationToken cancellation;
            readonly byte[] history = new byte[131072], buffer = new byte[65536];
            int buffered;long produced, expected;
            public RefPack(FileStream input, long end, FileStream output, CancellationToken cancellation) {this.input = input;this.end = end;this.output = output;this.cancellation = cancellation;}
            int Read() {if (input.Position >= end) throw new InvalidDataException("RefPack input ended early.");return input.ReadByte();}
            void Emit(int value)
            {
                if (produced >= expected) throw new InvalidDataException("RefPack output exceeds its declared length.");
                history[(int)(produced++ & 131071)] = (byte)value;buffer[buffered++] = (byte)value;
                if (buffered == buffer.Length) Flush();
            }
            void Flush() {cancellation.ThrowIfCancellationRequested();output.Write(buffer, 0, buffered);buffered = 0;}
            void Literal(int count) {for (int i = 0; i < count; i++) Emit(Read());}
            void Match(int distance, int count)
            {
                if (distance <= 0 || distance > produced || distance > history.Length) throw new InvalidDataException("RefPack back-reference range differs.");
                for (int i = 0; i < count; i++) Emit(history[(int)((produced - distance) & 131071)]);
            }
            public void Decode(int flags, long length)
            {
                expected = length;int width = (flags & 0x8000) != 0 ? 4 : 3;
                for (int i = 0; i < 2 + width + ((flags & 0x100) != 0 ? width : 0); i++) Read();
                while (true) {
                    int code = Read(), b, c, d;
                    if (code < 0x80) {b = Read();Literal(code & 3);Match(1 + b + ((code & 0x60) << 3), 3 + ((code & 0x1c) >> 2));}
                    else if (code < 0xc0) {b = Read();c = Read();Literal(b >> 6);Match(1 + ((b & 0x3f) << 8) + c, 4 + (code & 0x3f));}
                    else if (code < 0xe0) {b = Read();c = Read();d = Read();Literal(code & 3);Match(1 + ((code & 0x10) << 12) + (b << 8) + c, 5 + ((code & 0x0c) << 6) + d);}
                    else if (code < 0xfc) Literal(4 + ((code & 0x1f) << 2));
                    else {Literal(code & 3);break;}
                }
                if (produced != expected) throw new InvalidDataException("RefPack output length differs.");
                Flush();
            }
        }
    }
}
