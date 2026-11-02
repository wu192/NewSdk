using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    // Opt-in disk-backed regression: the ordinary App suite stays small.
    public static class LargeBigTests
    {
        const long PayloadBytes = (1L << 31) + 4096;
        const uint Checksum = 0x11223344;
        static readonly byte[] Original = Encoding.ASCII.GetBytes("ORIGINAL");
        static readonly byte[] Boundary = Encoding.ASCII.GetBytes("CROSS-2G");
        static readonly byte[] Tail = Encoding.ASCII.GetBytes("PAYLOAD-END");
        static readonly byte[] Inserted = { 0x41, 0x42, 0x43, 0x44 };
        static int checks;
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);
        static void Check(bool value, string message) { checks++; if (!value) throw new Exception("Large BIG: " + message); }
        static void At(Stream output, long offset, byte[] bytes) { output.Position = offset; output.Write(bytes, 0, bytes.Length); }
        static void Match(Stream input, long offset, byte[] expected, string message)
        {
            input.Position = offset;
            foreach (byte value in expected) Check(input.ReadByte() == value, message);
        }
        static void Manifest(string stem, bool world, bool reference)
        {
            byte[] name = Encoding.ASCII.GetBytes(world ? "Test:world\0" : "Test:mod\0"), source = Encoding.ASCII.GetBytes("Data/item.xml\0");
            uint binary = world ? 4u : checked((uint)(PayloadBytes - 4));
            using (var writer = new BinaryWriter(File.Create(stem + ".manifest"))) {
                writer.Write((byte)0); writer.Write((byte)1); writer.Write((ushort)6);
                foreach (uint value in new uint[] { Checksum, 0, 1, binary, binary, 0, 0, reference ? 8u : 0u, 0, (uint)name.Length, (uint)source.Length }) writer.Write(value);
                foreach (uint value in new uint[] { world ? 1u : 9u, world ? 2u : 10u, 3, 7, 0, reference ? 1u : 0u, 0, 0, binary, 0, 0, 0 }) writer.Write(value);
                if (reference) { writer.Write(1u); writer.Write(2u); }
                writer.Write(name); writer.Write(source);
            }
        }
        static void Empty(string stem)
        {
            using (var writer = new BinaryWriter(File.Create(stem + ".manifest"))) {
                writer.Write((byte)0); writer.Write((byte)1); writer.Write((ushort)6);
                for (int i = 0; i < 11; i++) writer.Write(i == 0 ? Checksum : 0u);
            }
        }
        static void Fixture(string root, out string mod, out string built)
        {
            built = Path.Combine(root, "references"); Directory.CreateDirectory(built);
            foreach (string name in new[] { "Static", "Global", "Audio" }) Empty(Path.Combine(built, name));
            string world = Path.Combine(built, "Worldbuilder"); Manifest(world, true, false);
            foreach (string ext in new[] { ".bin", ".imp", ".relo" }) using (var writer = new BinaryWriter(File.Create(world + ext))) { writer.Write(Checksum); if (ext == ".bin") writer.Write(Inserted); }
            mod = Path.Combine(root, "Mod"); Manifest(mod, false, false);
            foreach (string ext in new[] { ".imp", ".relo" }) using (var writer = new BinaryWriter(File.Create(mod + ext))) writer.Write(Checksum);
            using (var output = new FileStream(mod + ".bin", FileMode.CreateNew, FileAccess.ReadWrite)) {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                    int returned;
                    if (!DeviceIoControl(output.SafeFileHandle, 0x900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out returned, IntPtr.Zero)) throw new IOException("Cannot mark regression fixture sparse: " + Marshal.GetLastWin32Error());
                }
                output.SetLength(PayloadBytes);
                At(output, 0, BitConverter.GetBytes(Checksum)); At(output, 4, Original);
                At(output, (1L << 31) - 3, Boundary); At(output, PayloadBytes - Tail.Length, Tail);
            }
        }
        static void ValidatePayload(string file, int prefix)
        {
            using (var input = File.OpenRead(file)) {
                Check(input.Length == PayloadBytes + prefix, "payload length changed");
                Match(input, 0, BitConverter.GetBytes(Checksum), "checksum changed");
                if (prefix != 0) Match(input, 4, Inserted, "inserted Worldbuilder bytes differ");
                Match(input, 4 + prefix, Original, "original payload head changed");
                Match(input, (1L << 31) - 3 + prefix, Boundary, "bytes crossing 2 GiB changed");
                Match(input, PayloadBytes - Tail.Length + prefix, Tail, "original payload tail changed");
            }
        }
        static uint Be(BinaryReader reader)
        {
            uint a = reader.ReadByte(), b = reader.ReadByte(), c = reader.ReadByte(), d = reader.ReadByte();
            return (a << 24) | (b << 16) | (c << 8) | d;
        }
        static void Pack(string root, string mod)
        {
            string tail = Path.Combine(root, "tail"), archive = Path.Combine(root, "large.big"); File.WriteAllBytes(tail, Tail);
            var entries = new List<BigArchiveEntry>();
            foreach (string ext in new[] { ".manifest", ".bin", ".imp", ".relo" }) entries.Add(new BigArchiveEntry { Name = "Data/mod" + ext, SourcePath = mod + ext });
            entries.Add(new BigArchiveEntry { Name = "Data/z.tail", SourcePath = tail });
            BigArchiveWriter.Write(archive, entries, CancellationToken.None);
            using (var input = File.OpenRead(archive)) using (var reader = new BinaryReader(input)) {
                Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "BIG4", "BIG magic differs");
                Check(reader.ReadUInt32() == input.Length && input.Length > int.MaxValue, "BIG total size wrapped at 2 GiB");
                Check(Be(reader) == 5, "BIG entry count differs"); uint header = Be(reader);
                long expected = header, binaryOffset = 0, tailOffset = 0;
                for (int i = 0; i < 5; i++) {
                    uint offset = Be(reader), size = Be(reader); var name = new StringBuilder(); int value;
                    while ((value = reader.ReadByte()) != 0) name.Append((char)value);
                    Check(offset == expected, "BIG entry offset wrapped or introduced padding"); expected += size;
                    string logical = name.ToString();
                    if (logical == "data\\mod.bin") { binaryOffset = offset; Check(size == PayloadBytes + 4, "BIG large member size wrapped"); }
                    if (logical == "data\\z.tail") { tailOffset = offset; Check(offset > int.MaxValue && size == Tail.Length, "BIG final member offset wrapped"); }
                }
                Check(reader.ReadBytes(8).SequenceEqual(new byte[] { 76, 50, 49, 57, 0, 0, 0, 0 }) && input.Position == header, "BIG directory footer differs");
                Check(expected == input.Length && binaryOffset > 0 && tailOffset > 0, "BIG exact inventory length differs");
                Match(input, binaryOffset, BitConverter.GetBytes(Checksum), "BIG checksum differs");
                Match(input, binaryOffset + 4, Inserted, "BIG inserted prefix differs");
                Match(input, binaryOffset + 8, Original, "BIG original head differs");
                Match(input, binaryOffset + (1L << 31) + 1, Boundary, "BIG bytes crossing 2 GiB differ");
                Match(input, binaryOffset + PayloadBytes + 4 - Tail.Length, Tail, "BIG original tail differs");
                Match(input, tailOffset, Tail, "BIG final member differs");
            }
            File.Delete(archive);
        }
        public static int Main(string[] args)
        {
            string root = Path.GetFullPath(args.Length == 0 ? "large-big-work" : args[0]); bool pack = !args.Contains("--resolver-only");
            if (Directory.Exists(root)) throw new IOException("Regression directory must be absent: " + root);
            Directory.CreateDirectory(root); var timer = Stopwatch.StartNew();
            try {
                string mod, built; Fixture(root, out mod, out built);
                Check(ModAssetResolver.Apply(mod + ".manifest", built, CancellationToken.None) == 0, "zero-reference fixture inserted assets"); ValidatePayload(mod + ".bin", 0);
                Console.WriteLine("PASS >2GiB resolver without insertion");
                Manifest(mod, false, true);
                Check(ModAssetResolver.Apply(mod + ".manifest", built, CancellationToken.None) == 1, "Worldbuilder asset was not inserted"); ValidatePayload(mod + ".bin", 4);
                Console.WriteLine("PASS >2GiB resolver with Worldbuilder insertion");
                if (pack) { Pack(root, mod); Console.WriteLine("PASS >2GiB BIG header, member sizes, offsets and payload samples"); }
                timer.Stop(); Console.WriteLine("PASS " + checks + " checks; payloadBytes=" + PayloadBytes + "; elapsedMs=" + timer.ElapsedMilliseconds + "; peakWorkingSetBytes=" + Process.GetCurrentProcess().PeakWorkingSet64);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
