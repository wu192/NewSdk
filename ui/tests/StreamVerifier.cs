using BinaryAssetBuilder.UiCompiler;
using System.Text;

internal static class StreamVerifier
{
    // An independent reader for the documented recovery v6 stream layout.
    internal static void Verify(ModStreams streams, Action<bool, string> check)
    {
        uint U(byte[] data, int at) => BitConverter.ToUInt32(data, at);
        byte[] m = streams.Manifest; check(m.Length >= 48 && m[0] == 0 && m[1] == 1 && BitConverter.ToUInt16(m, 2) == 6, "manifest Win32/linked/v6");
        uint checksum = U(m, 4); check(U(streams.Binary, 0) == checksum && U(streams.Relocations, 0) == checksum && U(streams.Imports, 0) == checksum, "four stream checksums");
        int count = checked((int)U(m, 12)); check(count == streams.Assets.Count, "manifest asset count");
        int names = 48 + count * 48 + checked((int)U(m, 32) + (int)U(m, 36)); int sourceNames = names + checked((int)U(m, 40));
        check(sourceNames + U(m, 44) == m.Length, "manifest buffer lengths");
        int binary = 4, relocations = 4, imports = 4, total = 0, max = 0, maxRelo = 0, maxImp = 0;
        var seen = new HashSet<ulong>();
        for (int i = 0; i < count; i++)
        {
            int e = 48 + i * 48; var asset = streams.Assets[i];
            check(seen.Add((ulong)U(m, e) << 32 | U(m, e + 4)), "unique asset identity");
            check(U(m, e) == asset.TypeId && U(m, e + 4) == asset.InstanceId && U(m, e + 8) == asset.TypeHash, "asset identity and game type hash");
            string CString(int at) { int end = Array.IndexOf(m, (byte)0, at); if (end < 0) throw new Exception("Unterminated manifest string"); return Encoding.UTF8.GetString(m, at, end - at); }
            check(CString(names + (int)U(m, e + 24)) == asset.TypeName + ":" + asset.Name, "manifest asset name");
            check(CString(sourceNames + (int)U(m, e + 28)) == asset.Source, "manifest source name");
            int size = checked((int)U(m, e + 32)), rsize = checked((int)U(m, e + 36)), isize = checked((int)U(m, e + 40));
            check(binary + size <= streams.Binary.Length && relocations + rsize <= streams.Relocations.Length && imports + isize <= streams.Imports.Length, "stream chunk bounds");
            int root = asset.TypeName == "AptGeometryData" ? 16 : 12; check(U(streams.Binary, binary) == 0 && U(streams.Binary, binary + 4) == root, "DataBlob ABI");
            int payload = checked((int)U(streams.Binary, binary + 8)); check(size == root + ((payload + 3) & ~3), "DataBlob size/alignment");
            check(rsize == 8 && U(streams.Relocations, relocations) == 4 && U(streams.Relocations, relocations + 4) == uint.MaxValue && isize == 0, "relocation offset, sentinel, empty imports");
            for (int p = root + payload; p < size; p++) check(streams.Binary[binary + p] == 0, "zero tail padding");
            if (asset.TypeName == "Texture")
            { int dds = binary + root; check(Encoding.ASCII.GetString(streams.Binary, dds, 4) == "DDS " && U(streams.Binary, dds + 76) == 32, "inline DDS header"); int width = checked((int)U(streams.Binary, dds + 16)), height = checked((int)U(streams.Binary, dds + 12)); check(payload == 128 + width * height * 4 && U(streams.Binary, dds + 88) == 32 && U(streams.Binary, dds + 104) == 0xff000000, "A8R8G8B8 inline texture payload"); }
            binary += size; relocations += rsize; imports += isize; total += size; max = Math.Max(max, size); maxRelo = Math.Max(maxRelo, rsize); maxImp = Math.Max(maxImp, isize);
        }
        check(binary == streams.Binary.Length && relocations == streams.Relocations.Length && imports == streams.Imports.Length, "no missing or trailing stream data");
        check(U(m, 16) == total && U(m, 20) == max && U(m, 24) == maxRelo && U(m, 28) == maxImp, "manifest chunk totals/maxima");
    }
}
