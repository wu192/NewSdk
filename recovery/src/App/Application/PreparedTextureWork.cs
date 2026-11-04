#if NEWSdk_MONOLITHIC
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered.Compiler.Texture;

namespace NewModCompiler.Application
{
    internal sealed partial class PreparedBackendWork
    {
        // Input headers bound the decoded image, float filtering passes and final
        // DDS/AssetBuffer copies. No decoding or full-file copy runs on the owner.
        const long TextureScratchBytes = 256L * 1024L;
        sealed class TextureInput
        {
            internal long Bytes;
        }

        static bool PrepareTexture(InstanceDeclaration original, List<FileStream> leases,
            long minimum, out long retained, out string reason)
        {
            retained = minimum; reason = null;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return Reject("unsupported-platform", out reason);
            var inputs = new Dictionary<string, TextureInput>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (original.Handle.TypeId != TextureCatalog.TerrainTextureAtlasTypeId)
                {
                    var file = original.Node.Attributes["File"];
                    TextureInput input;
                    if (!TextureLease(file == null ? null : file.Value, leases, inputs, directories, out input, out reason)) return false;
                    retained = checked(minimum + input.Bytes);
                    return true;
                }

                int tiles = 0; long largestInput = 0;
                foreach (XmlNode tile in original.Node.ChildNodes)
                {
                    if (tile.LocalName != "Tile" || tile.NamespaceURI != "uri:ea.com:eala:asset") continue;
                    tiles++;
                    foreach (string field in new[] { "BaseTexture", "NormalTexture" })
                    {
                        var file = tile.Attributes[field]; TextureInput input;
                        if (!TextureLease(file == null ? null : file.Value, leases, inputs, directories, out input, out reason)) return false;
                        largestInput = Math.Max(largestInput, input.Bytes);
                    }
                }
                if (tiles == 0) return true;
                if (tiles > 49) { retained = long.MaxValue; return true; }
                long canvas = tiles <= 1 ? 512L * 512 : tiles <= 3 ? 1024L * 512 :
                    tiles <= 7 ? 2048L * 512 : tiles <= 9 ? 1024L * 1024 :
                    tiles <= 21 ? 2048L * 1024 : 2048L * 2048;
                // Packed base/normal tiles, both atlas images, their float mip
                // passes and returned DDS buffers overlap during atlas encoding.
                retained = checked(minimum + TextureScratchBytes + largestInput +
                    canvas * 96L + tiles * 2L * 256L * 256L * 4L);
                return true;
            }
            catch (OverflowException) { retained = long.MaxValue; return true; }
            catch (IOException) { return Reject("external-input-unavailable", out reason); }
            catch (UnauthorizedAccessException) { return Reject("external-input-unavailable", out reason); }
            catch (ArgumentException) { return Reject("external-input-path-invalid", out reason); }
            catch (NotSupportedException) { return Reject("external-input-path-invalid", out reason); }
        }

        static bool TextureLease(string value, List<FileStream> leases, Dictionary<string, TextureInput> inputs,
            HashSet<string> directories, out TextureInput input, out string reason)
        {
            input = null; reason = null;
            if (String.IsNullOrEmpty(value)) return Reject("external-input-path-missing", out reason);
            string path = Path.GetFullPath(value);
            if (inputs.TryGetValue(path, out input)) return true;
            if (!LeaseInputPath(path, leases, out reason, directories)) return false;
            var stream = leases[leases.Count - 1];
            long bytes;
            if (String.Equals(Path.GetExtension(path), ".dds", StringComparison.OrdinalIgnoreCase))
                bytes = checked(TextureScratchBytes + stream.Length * 2L);
            else
            {
                int width, height;
                if (!TextureDimensions(stream, out width, out height)) return Reject("texture-budget-unknown", out reason);
                long pixels = checked(TexturePowerOfTwo(width) * TexturePowerOfTwo(height));
                bytes = pixels >= 16777216L ? long.MaxValue : checked(TextureScratchBytes + stream.Length * 2L + pixels * 96L);
            }
            input = new TextureInput { Bytes = bytes };
            inputs.Add(path, input);
            return true;
        }

        static long TexturePowerOfTwo(int value)
        {
            if (value <= 0 || value > 16384) return 16777216L;
            long result = 1; while (result < value) result <<= 1;
            return result;
        }
        static uint TextureU32(byte[] data, int at)
        { return (uint)data[at] | (uint)data[at + 1] << 8 | (uint)data[at + 2] << 16 | (uint)data[at + 3] << 24; }
        static int TextureU16(byte[] data, int at) { return data[at] | data[at + 1] << 8; }
        static int TextureBig32(byte[] data, int at)
        { return data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3]; }

        static bool TextureDimensions(FileStream stream, out int width, out int height)
        {
            width = height = 0;
            byte[] header = new byte[128]; int count = stream.Read(header, 0, header.Length);
            stream.Position = 0;
            // The image loader recognizes PPM before its TGA fallback. A P6
            // newline is also TGA type 10, so never infer its size as a TGA.
            if (count >= 2 && header[0] == 'P' && (header[1] == '3' || header[1] == '6')) return false;
            if (count >= 24 && header[0] == 137 && header[1] == 80 && header[2] == 78 && header[3] == 71)
            { width = TextureBig32(header, 16); height = TextureBig32(header, 20); }
            else if (count >= 26 && header[0] == 66 && header[1] == 77)
            {
                uint dib = TextureU32(header, 14);
                if (dib == 12) { width = TextureU16(header, 18); height = TextureU16(header, 20); }
                else if (dib >= 40 && count >= 54)
                { width = unchecked((int)TextureU32(header, 18)); height = unchecked((int)TextureU32(header, 22)); }
            }
            else if (count >= 12 && TextureDibDecoder.IsDib(header))
            {
                if (TextureU32(header, 0) == 12) { width = TextureU16(header, 4); height = TextureU16(header, 6); }
                else if (count >= 40)
                { width = unchecked((int)TextureU32(header, 4)); height = unchecked((int)TextureU32(header, 8)); }
            }
            else if (count >= 10 && header[0] == 71 && header[1] == 73 && header[2] == 70)
            { width = TextureU16(header, 6); height = TextureU16(header, 8); }
            else if (count >= 2 && header[0] == 255 && header[1] == 216)
                return TextureJpegDimensions(stream, out width, out height);
            else if (count >= 18 && (header[2] == 1 || header[2] == 2 || header[2] == 3 ||
                header[2] == 9 || header[2] == 10 || header[2] == 11))
            { width = TextureU16(header, 12); height = TextureU16(header, 14); }
            if (height == Int32.MinValue) return false;
            height = Math.Abs(height);
            return width > 0 && height > 0;
        }

        static bool TextureJpegDimensions(FileStream stream, out int width, out int height)
        {
            width = height = 0;
            try
            {
                stream.Position = 2;
                for (int segments = 0; segments < 1024; segments++)
                {
                    if (stream.ReadByte() != 255) return false;
                    int marker; do { marker = stream.ReadByte(); } while (marker == 255);
                    if (marker < 0 || marker == 217 || marker == 218 || marker == 0) return false;
                    if (marker == 216 || marker == 1 || marker >= 208 && marker <= 215) continue;
                    int high = stream.ReadByte(), low = stream.ReadByte();
                    if (high < 0 || low < 0) return false;
                    int length = high << 8 | low;
                    if (length < 2 || stream.Position + length - 2 > stream.Length) return false;
                    if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                    {
                        if (length < 8) return false;
                        stream.ReadByte();
                        height = stream.ReadByte() << 8 | stream.ReadByte();
                        width = stream.ReadByte() << 8 | stream.ReadByte();
                        return width > 0 && height > 0;
                    }
                    stream.Position += length - 2;
                }
                return false;
            }
            finally { stream.Position = 0; }
        }
    }
}
#endif
