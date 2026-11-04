using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// Independent DDS reader/BC decoder. Runs in the test-only entry host using
// the exact production object inputs; no original texture DLL is deployed.
public static class TextureCommonFormats
{
    static readonly List<object> Cases = new List<object>();
    static readonly List<object> Failures = new List<object>();
    static int Checks;
    static string Root;
    static IAssetBuilderPlugin Plugin;
    static void Need(bool value, string message) { ++Checks; if (!value) throw new Exception(message); }
    static void Run(string name, Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            Failures.Add(new { name, error = error.ToString() });
            Console.Error.WriteLine("FAIL common-format case " + name + ": " + error.Message);
        }
    }
    static uint U32(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
    static ushort U16(byte[] b, int at) { return BitConverter.ToUInt16(b, at); }
    static string Hash(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static string Escape(string text) { return System.Security.SecurityElement.Escape(text); }
    static InstanceDeclaration Declare(string file, string format, bool mipmaps, bool demand, string textureType)
    {
        string type = demand ? "OnDemandTexture" : "Texture";
        var xml = new XmlDocument();
        xml.LoadXml("<" + type + " xmlns='uri:ea.com:eala:asset' id='CommonFormat' File='" + Escape(file) +
            "' Type='" + textureType + "' OutputFormat='" + format + "' GenerateMipMaps='" + mipmaps.ToString().ToLowerInvariant() +
            "' AllowAutomaticResize='true'/>");
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var doc = new AssetDeclarationDocument();
        Type current = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(current);
        current.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(doc, state);
        return new InstanceDeclaration(doc) { XmlNode = xml.DocumentElement, CustomDataPath = Path.Combine(Root, "common.custom") };
    }
    static byte[] Compile(string file, string format, bool mipmaps, bool demand, string textureType)
    {
        var declaration = Declare(Path.Combine(Root, file), format, mipmaps, demand, textureType);
        if (File.Exists(declaration.CustomDataPath)) File.Delete(declaration.CustomDataPath);
        AssetBuffer buffer = Plugin.ProcessInstance(declaration);
        Need(buffer != null && buffer.InstanceData != null, "Missing asset buffer: " + file);
        byte[] data = buffer.InstanceData, reloc = buffer.RelocationData;
        Need(buffer.ImportsData != null && buffer.ImportsData.Length == 0, "Unexpected texture imports");
        int pointer = 4, root = 12;
        if (demand)
        {
            Need(data.Length == 8 && U32(data, 0) == 0 && U32(data, 4) == 0 && reloc.Length == 0, "OnDemandTexture root layout");
            byte[] custom = File.ReadAllBytes(declaration.CustomDataPath);
            Need(custom.Length >= 48 && U32(custom, 0) == declaration.Handle.TypeId && U32(custom, 4) == declaration.Handle.InstanceId,
                "OnDemandTexture custom identity");
            int size = checked((int)U32(custom, 16)), relocationSize = checked((int)U32(custom, 20));
            Need(custom.Length == 32 + size + relocationSize && U32(custom, 24) == 0, "OnDemandTexture custom length");
            data = custom.Skip(32).Take(size).ToArray(); reloc = custom.Skip(32 + size).ToArray(); pointer = 0; root = 8;
        }
        Need(reloc.Length == 8 && U32(reloc, 0) == pointer && U32(reloc, 4) == uint.MaxValue, "Texture relocation layout");
        Need(U32(data, pointer) == root, "Texture payload pointer");
        int length = checked((int)U32(data, pointer + 4));
        Need(length > 0 && data.Length == root + ((length + 3) & ~3), "Texture payload allocation length");
        return data.Skip(root).Take(length).ToArray();
    }
    static void Dds(byte[] dds, string format, bool mipmaps, int width, int height)
    {
        Need(dds.Length >= 128 && U32(dds, 0) == 0x20534444 && U32(dds, 4) == 124 && U32(dds, 76) == 32, "DDS header");
        Need(U32(dds, 16) == width && U32(dds, 12) == height, "DDS dimensions");
        uint fourCC = format == "DXT1A" || format == "DXT1" ? 0x31545844u : format == "DXT3" ? 0x33545844u : format == "DXT5" ? 0x35545844u : 0;
        Need(U32(dds, 84) == fourCC, "DDS output format: " + format);
        if (fourCC == 0) Need(U32(dds, 88) == (format == "R5G6B5" ? 16u : 32u), "DDS raw bit count");
        int expectedMips = 1;
        if (mipmaps) for (int w = width, h = height; w > 1 || h > 1; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) ++expectedMips;
        Need(Math.Max(1, U32(dds, 28)) == expectedMips, "DDS mip count");
        int expectedLength = 128, levelWidth = width, levelHeight = height;
        for (int level = 0; level < expectedMips; ++level)
        {
            expectedLength += fourCC != 0 ? Math.Max(1, (levelWidth + 3) / 4) * Math.Max(1, (levelHeight + 3) / 4) *
                (fourCC == 0x31545844 ? 8 : 16) : levelWidth * levelHeight * (format == "R5G6B5" ? 2 : 4);
            levelWidth = Math.Max(1, levelWidth / 2); levelHeight = Math.Max(1, levelHeight / 2);
        }
        Need(dds.Length == expectedLength, "DDS complete mip payload length: " + format);
    }
    static byte[] Color565(ushort value)
    {
        int r = (value >> 11) & 31, g = (value >> 5) & 63, b = value & 31;
        return new byte[] { (byte)((r << 3) | (r >> 2)), (byte)((g << 2) | (g >> 4)), (byte)((b << 3) | (b >> 2)), 255 };
    }
    static byte[] Decode(byte[] dds, string format)
    {
        int width = (int)U32(dds, 16), height = (int)U32(dds, 12); byte[] result = new byte[width * height * 4];
        bool compressed = format.StartsWith("DXT", StringComparison.Ordinal);
        if (!compressed)
        {
            for (int pixel = 0; pixel < width * height; ++pixel)
            {
                byte[] color;
                if (format == "R5G6B5") color = Color565(U16(dds, 128 + pixel * 2));
                else { int at = 128 + pixel * 4; color = new byte[] { dds[at + 2], dds[at + 1], dds[at], format == "A8R8G8B8" ? dds[at + 3] : (byte)255 }; }
                Buffer.BlockCopy(color, 0, result, pixel * 4, 4);
            }
            return result;
        }
        int blockSize = format == "DXT1" || format == "DXT1A" ? 8 : 16, atBlock = 128;
        for (int by = 0; by < height; by += 4) for (int bx = 0; bx < width; bx += 4, atBlock += blockSize)
        {
            int atColor = atBlock + (blockSize == 8 ? 0 : 8);
            ushort c0 = U16(dds, atColor), c1 = U16(dds, atColor + 2);
            byte[][] colors = { Color565(c0), Color565(c1), new byte[4], new byte[4] };
            for (int c = 0; c < 3; ++c)
            {
                colors[2][c] = (byte)(c0 > c1 || blockSize != 8 ? (2 * colors[0][c] + colors[1][c]) / 3 : (colors[0][c] + colors[1][c]) / 2);
                colors[3][c] = (byte)(c0 > c1 || blockSize != 8 ? (colors[0][c] + 2 * colors[1][c]) / 3 : 0);
            }
            colors[2][3] = 255; colors[3][3] = (byte)(c0 > c1 || blockSize != 8 ? 255 : 0);
            uint indices = U32(dds, atColor + 4); byte[] alpha = new byte[8]; ulong alphaBits = 0;
            if (format == "DXT5")
            {
                alpha[0] = dds[atBlock]; alpha[1] = dds[atBlock + 1];
                if (alpha[0] > alpha[1]) for (int i = 1; i <= 6; ++i) alpha[i + 1] = (byte)(((7 - i) * alpha[0] + i * alpha[1]) / 7);
                else { for (int i = 1; i <= 4; ++i) alpha[i + 1] = (byte)(((5 - i) * alpha[0] + i * alpha[1]) / 5); alpha[6] = 0; alpha[7] = 255; }
                for (int i = 0; i < 6; ++i) alphaBits |= (ulong)dds[atBlock + 2 + i] << (8 * i);
            }
            for (int py = 0; py < 4; ++py) for (int px = 0; px < 4; ++px)
            {
                int p = py * 4 + px; if (bx + px >= width || by + py >= height) continue;
                byte[] color = colors[(indices >> (2 * p)) & 3]; int target = ((by + py) * width + bx + px) * 4;
                Buffer.BlockCopy(color, 0, result, target, 4);
                if (format == "DXT3") result[target + 3] = (byte)(((dds[atBlock + p / 2] >> ((p & 1) * 4)) & 15) * 17);
                if (format == "DXT5") result[target + 3] = alpha[(alphaBits >> (p * 3)) & 7];
            }
        }
        return result;
    }
    static void Pixels(byte[] expected, byte[] actual, int colorLimit, int alphaLimit, string name)
    {
        Need(expected.Length == actual.Length, "Decoded pixel dimensions: " + name);
        int colorError = 0, alphaError = 0;
        for (int i = 0; i < expected.Length; ++i)
        {
            int error = Math.Abs(expected[i] - actual[i]);
            if (i % 4 == 3) alphaError = Math.Max(alphaError, error);
            else if (expected[(i & ~3) + 3] != 0) colorError = Math.Max(colorError, error);
        }
        Need(colorError <= colorLimit && alphaError <= alphaLimit, name + " decoded max errors color=" + colorError + ", alpha=" + alphaError);
    }
    static void Check(string file, string format, string expectedPixels, bool mipmaps, bool demand, int colorLimit, int alphaLimit)
    {
        byte[] dds = Compile(file, format, mipmaps, demand, "StandardTexture"); Dds(dds, format, mipmaps, 16, 16);
        Pixels(File.ReadAllBytes(Path.Combine(Root, expectedPixels)), Decode(dds, format), colorLimit, alphaLimit, file + "/" + format);
        byte[] again = Compile(file, format, mipmaps, demand, "StandardTexture");
        Need(dds.SequenceEqual(again), "Nondeterministic output: " + file + "/" + format);
        if (file == "binary-alpha.png" && format == "DXT1A" && mipmaps)
        {
            // This symmetric half-opaque source filters to alpha 0.5 in its
            // final 1x1 mip. The original one-bit cutoff includes equality.
            byte[] lastMip = new byte[136]; Buffer.BlockCopy(dds, 0, lastMip, 0, 128);
            Buffer.BlockCopy(dds, dds.Length - 8, lastMip, 128, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, lastMip, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, lastMip, 16, 4);
            Need(Decode(lastMip, "DXT1A")[3] == 0, "DXT1A filtered alpha exactly 0.5 must remain transparent");
        }
        Cases.Add(new { file, outputFormat = format, mipmaps, onDemand = demand, ddsSha256 = Hash(dds), bytes = dds.Length,
            decodedColorErrorLimit = colorLimit, decodedAlphaErrorLimit = alphaLimit });
    }
    static void Reject(string file, string textureType)
    {
        bool rejected = false;
        try { Plugin.ProcessInstance(Declare(Path.Combine(Root, file), "A8R8G8B8", false, false, textureType)); } catch (Exception) { rejected = true; }
        Need(rejected, "Invalid input was accepted: " + file + "/" + textureType);
    }
    static void SolidMips(string format)
    {
        byte[] dds = Compile("solid.png", format, true, false, "StandardTexture"); Dds(dds, format, true, 16, 16);
        bool compressed = format.StartsWith("DXT", StringComparison.Ordinal);
        int at = 128, levels = 0;
        for (int width = 16, height = 16; ; width = Math.Max(1, width / 2), height = Math.Max(1, height / 2))
        {
            int length = compressed ? Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format == "DXT1" ? 8 : 16) : width * height * 4;
            byte[] view = new byte[128 + length]; Buffer.BlockCopy(dds, 0, view, 0, 128); Buffer.BlockCopy(dds, at, view, 128, length);
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, view, 16, 4); Buffer.BlockCopy(BitConverter.GetBytes(height), 0, view, 12, 4);
            byte[] expected = new byte[width * height * 4];
            for (int i = 0; i < expected.Length; i += 4) { expected[i] = 71; expected[i + 1] = 131; expected[i + 2] = 191; expected[i + 3] = 255; }
            Pixels(expected, Decode(view, format), compressed ? 8 : 0, 0, "solid mip " + levels + "/" + format);
            at += length; ++levels;
            if (width == 1 && height == 1) break;
        }
        Need(at == dds.Length, "Solid mip decoder consumed complete DDS");
        Cases.Add(new { file = "solid.png", outputFormat = format, decodedMipLevels = levels, ddsSha256 = Hash(dds) });
    }
    public static int Main(string[] args)
    {
        var state = new Dictionary<string, object>();
        state["status"] = "started"; state["scope"] = "production-object common input/output contract; BC output decoded independently, not legacy compressed byte equality";
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected fixture-directory output.json");
            Root = Path.GetFullPath(args[0]);
            Assembly own = typeof(IAssetBuilderPlugin).Assembly;
            Need(own == Assembly.GetEntryAssembly(), "Core must belong to validation entry EXE");
            Need(typeof(Recovered.Compiler.Texture.Plugin).Assembly == own, "Texture plugin must belong to validation entry EXE");
            Need(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BinaryAssetBuilder.TextureCompiler.dll")), "Legacy texture DLL deployed");
            Plugin = new Recovered.Compiler.Texture.Plugin(); Plugin.Initialize(null, TargetPlatform.Win32);
            try
            {
                Run("jpeg-fixture-preparation", delegate { using (var bitmap = new Bitmap(Path.Combine(Root, "solid.bmp"))) bitmap.Save(Path.Combine(Root, "solid.jpg"), ImageFormat.Jpeg); });
                foreach (string file in new[] { "opaque.png", "opaque.tga", "rle.tga", "opaque.bmp", "opaque.dib", "indexed.png", "indexed.bmp", "indexed.dib" })
                    Run(file + "/A8R8G8B8", delegate { Check(file, "A8R8G8B8", "opaque.rgba", false, false, 0, 0); });
                Run("alpha.png/raw-on-demand", delegate { Check("alpha.png", "A8R8G8B8", "alpha.rgba", true, true, 0, 0); });
                foreach (string format in new[] { "X8R8G8B8", "R5G6B5", "DXT1", "DXT3", "DXT5" })
                    Run("opaque.png/" + format, delegate { Check("opaque.png", format, "opaque.rgba", true, false, 8, 0); });
                Run("alpha.png/DXT3", delegate { Check("alpha.png", "DXT3", "alpha.rgba", true, false, 8, 17); });
                Run("alpha.png/DXT5-on-demand", delegate { Check("alpha.png", "DXT5", "alpha.rgba", true, true, 8, 32); });
                Run("binary-alpha.png/DXT1A", delegate { Check("binary-alpha.png", "DXT1A", "binary-alpha.rgba", true, false, 0, 0); });
                Run("solid.jpg/A8R8G8B8", delegate { Check("solid.jpg", "A8R8G8B8", "solid.rgba", false, false, 8, 0); });
                foreach (string format in new[] { "A8R8G8B8", "DXT1", "DXT3", "DXT5" })
                    Run("solid.png/all-mips/" + format, delegate { SolidMips(format); });
                foreach (string file in new[] { "preserve.dds", "preserve.DDS" }) foreach (bool demand in new[] { false, true })
                Run(file + "/passthrough/on-demand=" + demand, delegate {
                    byte[] input = File.ReadAllBytes(Path.Combine(Root, file));
                    Need(input.SequenceEqual(Compile(file, "DXT5", true, demand, "StandardTexture")), "DDS passthrough changed bytes");
                    Cases.Add(new { file, onDemand = demand, preservedBytes = input.Length, ddsSha256 = Hash(input) });
                });
                Run("npot.png/DXT1", delegate {
                    byte[] npot = Compile("npot.png", "DXT1", true, false, "StandardTexture");
                    int w = (int)U32(npot, 16), h = (int)U32(npot, 12);
                    Need(w == 16 && h == 16, "13x9 NPOT source must round up to 16x16"); Dds(npot, "DXT1", true, w, h);
                    Cases.Add(new { file = "npot.png", resizedWidth = w, resizedHeight = h, ddsSha256 = Hash(npot) });
                });
                foreach (var pair in new[] { new[] { "opaque.png", "DXT1" }, new[] { "binary-alpha.png", "DXT3" }, new[] { "alpha.png", "DXT5" } })
                Run(pair[0] + "/default-format-inference", delegate {
                    byte[] inferred = Compile(pair[0], "DXT1", false, false, "StandardTexture"); Dds(inferred, pair[1], false, 16, 16);
                    Cases.Add(new { file = pair[0], requestedFormat = "DXT1", inferredFormat = pair[1] });
                });
                Run("missing-input-rejection", delegate { Reject("missing.tga", "StandardTexture"); });
                Run("corrupt-input-rejection", delegate { Reject("broken.png", "StandardTexture"); });
                Run("raster-cube-rejection", delegate { Reject("opaque.png", "CubeTexture"); });
                Run("reuse-after-errors", delegate { Plugin.ReInitialize(null, TargetPlatform.Win32); Check("opaque.tga", "A8R8G8B8", "opaque.rgba", false, false, 0, 0); });
                Run("assembly-ownership", delegate { Need(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BinaryAssetBuilder.TextureCompiler"), "Legacy texture assembly loaded"); });
            }
            finally { ((IDisposable)Plugin).Dispose(); }
            Need(Failures.Count == 0, "Common-format matrix failed in " + Failures.Count + " independent cases; see failures array");
            state["status"] = "passed"; state["cases"] = Cases; state["assertions"] = Checks;
            Console.WriteLine("PASS built-in texture common formats: " + Cases.Count + " cases, " + Checks + " assertions"); return 0;
        }
        catch (Exception error) { state["status"] = "failed"; state["error"] = error.ToString(); Console.Error.WriteLine(error); return 1; }
        finally { state["cases"] = Cases; state["failures"] = Failures; state["assertions"] = Checks; if (args.Length == 2) File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(state), new UTF8Encoding(false)); }
    }
}
