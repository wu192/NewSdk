using System;
using System.IO;

namespace Recovered.Compiler.Texture
{
    // Standard BI_RGB indexed storage, separate from the byte-compatible
    // recovered direct-colour BMP decoder. Palette entries are BGR/BGR0;
    // the reserved fourth palette byte does not define transparency.
    public static class TextureIndexedBmpDecoder
    {
        public static bool CanDecode(byte[] data)
        {
            if (data == null || data.Length < 26 || data[0] != 66 || data[1] != 77) return false;
            uint header = U32(data, 14);
            if (header != 12 && header != 40 && header != 52 && header != 56 && header != 64 && header != 108 && header != 124) return false;
            if (header > data.Length - 14) return false;
            int depth = U16(data, header == 12 ? 24 : 28);
            return (depth == 1 || depth == 4 || depth == 8) && (header == 12 || U32(data, 30) == 0);
        }
        public static TexturePacker.Image Decode(byte[] data)
        {
            if (!CanDecode(data)) throw new InvalidDataException("Unsupported indexed BMP storage.");
            bool core = U32(data, 14) == 12;
            int width = core ? U16(data, 18) : unchecked((int)U32(data, 18));
            int signedHeight = core ? U16(data, 20) : unchecked((int)U32(data, 22));
            int planes = U16(data, core ? 22 : 26), depth = U16(data, core ? 24 : 28);
            if (planes != 1 || width <= 0 || signedHeight == 0 || signedHeight == Int32.MinValue ||
                width > 16384 || Math.Abs(signedHeight) > 16384) throw Invalid();
            int height = Math.Abs(signedHeight);
            if ((long)width * height >= 16777216L) throw Invalid();
            uint declaredColors = core ? 0 : U32(data, 46);
            if (declaredColors > (1u << depth)) throw Invalid();
            int colors = declaredColors == 0 ? 1 << depth : (int)declaredColors;
            int paletteAt = checked(14 + (int)U32(data, 14)), entryBytes = core ? 3 : 4;
            long paletteEnd = (long)paletteAt + colors * entryBytes;
            uint rasterAt = U32(data, 10);
            int stride = checked(((width * depth + 31) / 32) * 4);
            if (paletteEnd > data.Length || rasterAt < paletteEnd || rasterAt > data.Length ||
                (long)stride * height > data.Length - (long)rasterAt) throw Invalid();
            uint[] palette = new uint[colors];
            for (int i = 0; i < colors; i++)
            {
                int at = paletteAt + i * entryBytes;
                palette[i] = 0xff000000u | (uint)data[at + 2] | (uint)data[at + 1] << 8 | (uint)data[at] << 16;
            }
            uint[] pixels = new uint[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                int row = checked((int)rasterAt + (signedHeight < 0 ? y : height - 1 - y) * stride);
                for (int x = 0; x < width; x++)
                {
                    int index = depth == 8 ? data[row + x] : depth == 4
                        ? data[row + x / 2] >> ((1 - (x & 1)) * 4) & 15
                        : data[row + x / 8] >> (7 - (x & 7)) & 1;
                    if (index >= colors) throw Invalid();
                    pixels[y * width + x] = palette[index];
                }
            }
            return new TexturePacker.Image(width, height, pixels, false);
        }
        private static InvalidDataException Invalid() { return new InvalidDataException("Invalid indexed BMP image."); }
        private static int U16(byte[] data, int at) { return data[at] | data[at + 1] << 8; }
        private static uint U32(byte[] data, int at) { return (uint)data[at] | (uint)data[at + 1] << 8 | (uint)data[at + 2] << 16 | (uint)data[at + 3] << 24; }
    }
}
