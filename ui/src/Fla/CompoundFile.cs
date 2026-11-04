using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BinaryAssetBuilder.UiCompiler.Fla
{
    /// <summary>Managed CFB v3/v4 reader, including DIFAT, MiniFAT and storage trees.</summary>
    internal sealed class CompoundFile
    {
        private const uint End = 0xfffffffe, Free = 0xffffffff;
        private readonly byte[] file;
        private readonly int sectorSize, sectorCount;
        private readonly uint[] fat, miniFat;
        private readonly byte[] miniStream;
        internal readonly Dictionary<string, byte[]> Streams = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private sealed class Entry { internal string Name; internal byte Type; internal uint Left, Right, Child, First; internal long Size; }

        internal CompoundFile(byte[] file)
        {
            this.file = file;
            byte[] magic = { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 };
            if (file == null || file.Length < 512) throw new InvalidDataException("Expected an OLE/CFB Flash 8 FLA file.");
            for (int i = 0; i < 8; i++) if (file[i] != magic[i]) throw new InvalidDataException("ZIP/XFL and renamed SWF files are not Flash 8 FLA.");
            int version = U16(file, 26), shift = U16(file, 30);
            if (U16(file, 28) != 0xfffe || (version != 3 || shift != 9) && (version != 4 || shift != 12) || U16(file, 32) != 6)
                throw new InvalidDataException("Unsupported CFB sector layout.");
            sectorSize = 1 << shift; sectorCount = file.Length / sectorSize - 1;
            if (sectorCount < 1 || file.Length % sectorSize != 0) throw new InvalidDataException("Truncated CFB sectors.");
            int fatCount = checked((int)U32(file, 44)); var difat = new List<uint>(); var seen = new HashSet<uint>();
            for (int i = 0; i < 109; i++) { uint id = U32(file, 76 + 4 * i); if (id != Free) difat.Add(id); }
            uint next = U32(file, 68), difatCount = U32(file, 72);
            if (difatCount > sectorCount || fatCount > sectorCount) throw new InvalidDataException("Invalid CFB FAT size.");
            for (uint i = 0; i < difatCount; i++)
            {
                if (!seen.Add(next)) throw new InvalidDataException("Cyclic CFB DIFAT.");
                byte[] block = Sector(next);
                for (int j = 0; j < sectorSize / 4 - 1; j++) { uint id = U32(block, j * 4); if (id != Free) difat.Add(id); }
                next = U32(block, sectorSize - 4);
            }
            if (difat.Count != fatCount) throw new InvalidDataException("CFB FAT count mismatch.");
            fat = new uint[checked(fatCount * sectorSize / 4)]; seen.Clear();
            for (int i = 0; i < fatCount; i++)
            {
                if (!seen.Add(difat[i])) throw new InvalidDataException("Duplicate CFB FAT sector.");
                byte[] sector = Sector(difat[i]); for (int j = 0; j < sectorSize / 4; j++) fat[i * sectorSize / 4 + j] = U32(sector, j * 4);
            }
            byte[] directory = Chain(U32(file, 48), fat, sectorSize, null, -1);
            var entries = new List<Entry>(); Entry root = null;
            for (int at = 0; at + 128 <= directory.Length; at += 128)
            {
                int length = U16(directory, at + 64); byte type = directory[at + 66];
                if (type == 0) { entries.Add(null); continue; }
                if (length < 2 || length > 64 || (length & 1) != 0) throw new InvalidDataException("Invalid CFB directory name.");
                var entry = new Entry { Name = Encoding.Unicode.GetString(directory, at, length - 2), Type = type,
                    Left = U32(directory, at + 68), Right = U32(directory, at + 72), Child = U32(directory, at + 76),
                    First = U32(directory, at + 116), Size = U32(directory, at + 120) };
                if (version == 4) entry.Size |= (long)U32(directory, at + 124) << 32;
                if (entry.Size < 0 || entry.Size > file.Length) throw new InvalidDataException("Invalid CFB stream size.");
                entries.Add(entry);
                if (type == 5) { if (root != null) throw new InvalidDataException("Multiple CFB roots."); root = entry; }
            }
            if (root == null) throw new InvalidDataException("Missing CFB root.");
            miniStream = Chain(root.First, fat, sectorSize, null, root.Size);
            byte[] miniBytes = Chain(U32(file, 60), fat, sectorSize, null, checked((long)U32(file, 64) * sectorSize));
            miniFat = new uint[miniBytes.Length / 4]; for (int i = 0; i < miniFat.Length; i++) miniFat[i] = U32(miniBytes, i * 4);
            var visited = new HashSet<uint>();
            Visit(root.Child, "", entries, visited, 0);
        }
        private void Visit(uint id, string parent, List<Entry> entries, HashSet<uint> visited, int depth)
        {
            if (id == Free) return;
            if (id >= entries.Count || !visited.Add(id) || depth > 256 || entries[(int)id] == null) throw new InvalidDataException("Invalid CFB directory tree.");
            Entry e = entries[(int)id]; Visit(e.Left, parent, entries, visited, depth + 1);
            if (e.Name.Length == 0 || e.Name.Contains("/") || e.Name.Contains("\\")) throw new InvalidDataException("Invalid CFB stream path.");
            string path = parent.Length == 0 ? e.Name : parent + "/" + e.Name;
            if (e.Type == 1) Visit(e.Child, path, entries, visited, depth + 1);
            else if (e.Type == 2)
            {
                if (Streams.ContainsKey(path)) throw new InvalidDataException("Duplicate CFB path.");
                Streams.Add(path, e.Size < 4096 ? Chain(e.First, miniFat, 64, miniStream, e.Size) : Chain(e.First, fat, sectorSize, null, e.Size));
            }
            else throw new InvalidDataException("Invalid CFB child type.");
            Visit(e.Right, parent, entries, visited, depth + 1);
        }
        private byte[] Sector(uint id)
        {
            if (id >= sectorCount) throw new InvalidDataException("CFB sector out of range.");
            byte[] result = new byte[sectorSize]; Buffer.BlockCopy(file, checked(((int)id + 1) * sectorSize), result, 0, sectorSize); return result;
        }
        private byte[] Chain(uint first, uint[] table, int size, byte[] container, long expected)
        {
            if (expected == 0) return Array.Empty<byte>();
            var seen = new HashSet<uint>(); using (var output = new MemoryStream())
            {
                uint id = first;
                while (id != End)
                {
                    if (id == Free || id >= table.Length || !seen.Add(id)) throw new InvalidDataException("Invalid or cyclic CFB chain.");
                    byte[] sector;
                    if (container == null) sector = Sector(id);
                    else { long at = (long)id * size; if (at > container.Length - size) throw new InvalidDataException("Mini sector out of range."); sector = new byte[size]; Buffer.BlockCopy(container, (int)at, sector, 0, size); }
                    output.Write(sector, 0, size); id = table[id];
                    if (output.Length > file.Length + sectorSize) throw new InvalidDataException("CFB chain exceeds file.");
                }
                if (expected > output.Length) throw new InvalidDataException("Truncated CFB stream.");
                if (expected >= 0) output.SetLength(expected); return output.ToArray();
            }
        }
        internal static ushort U16(byte[] data, int p) => (ushort)(data[p] | data[p + 1] << 8);
        internal static uint U32(byte[] data, int p) => (uint)(data[p] | data[p + 1] << 8 | data[p + 2] << 16 | data[p + 3] << 24);
    }
}
