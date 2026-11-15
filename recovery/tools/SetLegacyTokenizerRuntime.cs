using System;
using System.IO;
using System.Reflection;
using System.Text;

// Build-only metadata finishing step, compiled/run with the CLR 4 tools. The
// input IL was compiled by the real CLR 2 csc.exe; modern LINK merges that IL
// with native /MT objects but stamps its own CLR 4 metadata version string.
// This tool changes that string only after checking the generated image. It
// does not turn CLR 4 IL or CLR 4 framework references into CLR 2 code.
class SetLegacyTokenizerRuntime
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static ushort U16(byte[] image, int offset) { return BitConverter.ToUInt16(image, offset); }
    static uint U32(byte[] image, int offset) { return BitConverter.ToUInt32(image, offset); }

    static int FileOffset(byte[] image, int sections, int count, uint rva)
    {
        for (int i = 0; i < count; ++i)
        {
            int section = sections + 40 * i;
            uint address = U32(image, section + 12);
            uint length = U32(image, section + 16);
            if (rva >= address && rva - address < length)
                return checked((int)(U32(image, section + 20) + rva - address));
        }
        throw new InvalidOperationException("Generated image RVA is outside its file sections.");
    }

    static void VerifyReferences(Assembly assembly)
    {
        const string identity = "BinaryAssetBuilder.Tokenizer, Version=1.0.3492.22559, Culture=neutral, PublicKeyToken=null";
        Require(assembly.FullName == identity, "Unexpected generated assembly identity: " + assembly.FullName);
        bool mscorlib = false;
        bool core = false;
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            bool framework = reference.Name == "mscorlib" || reference.Name == "System" || reference.Name == "System.Xml";
            bool originalCore = reference.Name == "BinaryAssetBuilder.Core";
            Require((framework && reference.Version == new Version(2, 0, 0, 0)) ||
                (originalCore && reference.Version == new Version(1, 0, 0, 0)),
                "Cannot target CLR 2: unexpected managed reference " + reference.FullName);
            if (reference.Name == "mscorlib") mscorlib = true;
            if (originalCore) core = true;
            Console.WriteLine("Verified managed reference: " + reference.FullName);
        }
        Require(mscorlib && core, "Expected original CLR 2 mscorlib and SDK Core references.");
    }

    static int Main(string[] args)
    {
        Require(args.Length == 1, "Usage: SetLegacyTokenizerRuntime GENERATED_DLL");
        string path = Path.GetFullPath(args[0]);
        byte[] image = File.ReadAllBytes(path);
        // Byte-array loading inspects exactly these bytes without retaining a
        // file handle, so the build output can be written after verification.
        Assembly assembly = Assembly.ReflectionOnlyLoad(image);
        VerifyReferences(assembly);
        Require(assembly.ImageRuntimeVersion == "v4.0.30319", "Unexpected LINK runtime stamp: " + assembly.ImageRuntimeVersion);

        Require(U16(image, 0) == 0x5a4d, "Generated image has no DOS header.");
        int pe = checked((int)U32(image, 0x3c));
        Require(U32(image, pe) == 0x00004550 && U16(image, pe + 4) == 0x014c, "Expected generated x86 PE image.");
        int count = U16(image, pe + 6);
        int optional = pe + 24;
        Require(U16(image, optional) == 0x010b, "Expected generated PE32 image.");
        int sections = optional + U16(image, pe + 20);
        int cli = FileOffset(image, sections, count, U32(image, optional + 96 + 14 * 8));
        Require(U32(image, cli) == 72 && U16(image, cli + 4) == 2 && U16(image, cli + 6) == 5,
            "Generated image has an unexpected CLI header version.");
        int metadata = FileOffset(image, sections, count, U32(image, cli + 8));
        Require(U32(image, metadata) == 0x424a5342 && U16(image, metadata + 4) == 1 && U16(image, metadata + 6) == 1,
            "Generated image has an unexpected metadata root.");
        int length = checked((int)U32(image, metadata + 12));
        int version = metadata + 16;
        string current = Encoding.ASCII.GetString(image, version, length).TrimEnd('\0');
        Require(current == "v4.0.30319", "Metadata root differs from the reflected runtime stamp.");
        byte[] target = Encoding.ASCII.GetBytes("v2.0.50727");
        Require(length >= target.Length + 1 && length % 4 == 0, "Unexpected metadata version slot.");

        int streamHeader = version + length;
        int streams = U16(image, streamHeader + 2);
        int cursor = streamHeader + 4;
        int tables = -1;
        for (int i = 0; i < streams; ++i)
        {
            uint offset = U32(image, cursor);
            int nameStart = cursor + 8;
            int end = nameStart;
            while (image[end] != 0) ++end;
            string name = Encoding.ASCII.GetString(image, nameStart, end - nameStart);
            if (name == "#~" || name == "#-") tables = checked(metadata + (int)offset);
            cursor = (end + 4) & ~3;
        }
        Require(tables >= 0 && image[tables + 4] == 2 && image[tables + 5] == 0,
            "Generated metadata tables do not use the CLR 2 schema.");
        ulong valid = BitConverter.ToUInt64(image, tables + 8);
        Require((valid >> 45) == 0, "Generated metadata uses tables outside the CLR 2 ECMA table set.");

        // Keep the existing slot length, all stream offsets, all IL, and every
        // native PE section intact. Actual CLR 2 loading is tested next by the
        // original-SDK oracle and is required before any compatibility claim.
        Array.Clear(image, version, length);
        Array.Copy(target, 0, image, version, target.Length);
        File.WriteAllBytes(path, image);
        Console.WriteLine("Set generated Tokenizer metadata runtime to v2.0.50727; CLR 2 load/regression tests remain required.");
        return 0;
    }
}
