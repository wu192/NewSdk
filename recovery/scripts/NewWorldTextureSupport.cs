using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// A preparation tool, separate from the immutable accepted compiler runtime.
// Links the unchanged accepted production decoder source; never rewrites pixels.
internal static class NewWorldTextureSupport
{
    static int U16(byte[] b, int at) { return b[at] | b[at + 1] << 8; }
    static void Need(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    static int PixelEnd(byte[] b)
    {
        Need(b.Length >= 18, "Truncated TGA header");
        if (b[1] != 0 || (b[2] != 2 && b[2] != 10)) throw new NotSupportedException("Unmapped true-color TGA required");
        for (int i = 3; i < 8; i++) if (b[i] != 0) throw new NotSupportedException("Unused color-map metadata is outside the accepted subset");
        int width = U16(b, 12), height = U16(b, 14), depth = b[16], attributes = b[17] & 15;
        if ((depth != 24 && depth != 32) || (b[17] & 192) != 0 ||
            (depth == 24 && attributes != 0) || (depth == 32 && attributes != 0 && attributes != 8))
            throw new NotSupportedException("TGA depth/attribute/interleaving metadata is outside the accepted subset");
        long count64 = (long)width * height;
        Need(width > 0 && height > 0, "Empty TGA dimensions");
        if (count64 >= 16777216L) throw new NotSupportedException("TGA exceeds the accepted 64 MiB image limit");
        int at = 18 + b[0], count = (int)count64, stride = depth / 8;
        if (b[2] == 2) { Need((long)at + (long)count * stride <= b.Length, "Truncated TGA pixels"); return at + count * stride; }
        int position = 0;
        while (position < count)
        {
            Need(at < b.Length, "Truncated TGA RLE packet");
            int packet = b[at++], length = (packet & 127) + 1;
            if (length > width - position % width) throw new NotSupportedException("TGA RLE packet crosses a row boundary");
            at += (packet & 128) != 0 ? stride : length * stride;
            Need(at <= b.Length, "Truncated TGA RLE pixels");
            position += length;
        }
        return at;
    }
    static void ValidateTail(MethodInfo method, byte[] bytes, int offset)
    {
        try { method.Invoke(null, new object[] { bytes, offset }); }
        catch (TargetInvocationException error)
        {
            if (error.InnerException is NotSupportedException) throw (NotSupportedException)error.InnerException;
            if (error.InnerException is InvalidDataException) throw (InvalidDataException)error.InnerException;
            throw;
        }
    }
    static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("compiler-exe mod-root report-json required");
        string exe = Path.GetFullPath(args[0]), mod = Path.GetFullPath(args[1]).TrimEnd('\\', '/');
        Type type = typeof(Recovered.Compiler.Texture.TextureTgaDecoder);
        MethodInfo validator = type.GetMethod("ValidateMetadataTail", BindingFlags.Static | BindingFlags.NonPublic);
        if (validator == null) throw new MissingMethodException(type.FullName, "ValidateMetadataTail");
        ValidateTail(validator, new byte[22], 22);
        bool rejected = false;
        try { ValidateTail(validator, new byte[1], 0); } catch (NotSupportedException) { rejected = true; }
        Need(rejected, "Accepted validator control did not reject unknown metadata");
        var unsupported = new List<object>();
        int scanned = 0;
        foreach (string path in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
        {
            if (!String.Equals(Path.GetExtension(path), ".tga", StringComparison.OrdinalIgnoreCase)) continue;
            string relative = path.Substring(mod.Length + 1).Replace('\\', '/');
            if (!(relative.StartsWith("Art/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) ||
                relative.StartsWith("Data/AptUI/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Data/AdditionalMaps/", StringComparison.OrdinalIgnoreCase)) continue;
            scanned++;
            byte[] data = File.ReadAllBytes(path);
            try { ValidateTail(validator, data, PixelEnd(data)); }
            catch (NotSupportedException error) { unsupported.Add(new { path = relative, reason = error.Message, bytes = data.Length }); }
            catch (InvalidDataException error) { unsupported.Add(new { path = relative, reason = error.Message, bytes = data.Length }); }
        }
        string hash;
        using (var algorithm = SHA256.Create()) hash = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(exe))).Replace("-", "").ToLowerInvariant();
        var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        var report = new { compilerSha256 = hash, scanned = scanned, unsupported = unsupported,
            decoderSourceCommit = "8050f22de81539c65aeca3903e9b48f53a572068",
            basis = "Unchanged accepted production decoder source compiled separately (the mixed EXE cannot be loaded as a DLL), plus header/RLE byte bounds. Full pixel processing is checked by the subsequent actual CLI preflight." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2])));
        File.WriteAllText(args[2], serializer.Serialize(report), new UTF8Encoding(false));
        Console.WriteLine("Base TGA inputs scanned: " + scanned + "; outside accepted subset: " + unsupported.Count);
        return 0;
    }
}

// Only the private metadata method is executed. These pixel-only dependencies
// deliberately fail if called, rather than substitute a pixel implementation.
namespace Recovered.Compiler.Texture
{
    public static class TexturePacker
    {
        public sealed class Image
        {
            public Image(int width, int height, uint[] pixels, bool alpha) { throw new InvalidOperationException("Metadata-only probe"); }
        }
    }
    public static class TextureNpotRgba8Resampler
    {
        public static TexturePacker.Image Resize(TexturePacker.Image image) { throw new InvalidOperationException("Metadata-only probe"); }
    }
}
namespace BinaryAssetBuilder
{
    public enum ErrorCode { BadTextureFile }
    public sealed class BinaryAssetBuilderException : Exception
    {
        public BinaryAssetBuilderException(ErrorCode code, string message, params object[] values) : base(String.Format(message, values)) { }
    }
}
