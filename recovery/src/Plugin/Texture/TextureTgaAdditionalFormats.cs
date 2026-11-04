using System;
using System.IO;

namespace Recovered.Compiler.Texture
{
    // Additional format candidate. Its own original-load receipt is required;
    // the existing 24/32-bit decoder and its accepted fixture group are unchanged.
    public static class TextureTgaAdditionalFormats
    {
        public static bool CanDecode(byte[] source)
        {
            if (source == null || source.Length < 18) return false;
            int type = source[2], depth = source[16];
            return type == 1 || type == 9 || type == 3 || type == 11 ||
                ((type == 2 || type == 10) && depth == 16);
        }
        private static Exception Bad()
        {
            return new BinaryAssetBuilder.BinaryAssetBuilderException(BinaryAssetBuilder.ErrorCode.BadTextureFile,
                "D3DX cannot load the texture from disk.", new object[0]);
        }
        private static int Byte(byte[] source, ref int offset)
        { if (offset >= source.Length) throw Bad(); return source[offset++]; }
        private static int Word(byte[] source, ref int offset)
        { int low = Byte(source, ref offset); return low | Byte(source, ref offset) << 8; }
        private static uint Five(int value, bool palette) { return (uint)(palette ? (value << 3) | (value >> 2) : (value * 255 + 15) / 31); }
        private static uint Color(byte[] source, ref int offset, int depth, bool palette)
        {
            if (depth == 15 || depth == 16)
            {
                int value = Word(source, ref offset);
                uint color = Five((value >> 10) & 31, palette) | Five((value >> 5) & 31, palette) << 8 |
                    Five(value & 31, palette) << 16 | (depth == 16 ? (uint)((value & 0x8000) != 0 ? 255 : 0) : 255u) << 24;
                return palette ? SwapRedBlue(color) : color;
            }
            uint blue = (uint)Byte(source, ref offset), green = (uint)Byte(source, ref offset), red = (uint)Byte(source, ref offset);
            uint alpha = depth == 32 ? (uint)Byte(source, ref offset) : 255u;
            uint rgba = red | green << 8 | blue << 16 | alpha << 24;
            return palette ? SwapRedBlue(rgba) : rgba;
        }
        private static uint SwapRedBlue(uint value)
        { return (value & 0xFF00FF00u) | (value & 255u) << 16 | (value >> 16 & 255u); }
        private static uint Pixel(byte[] source, ref int offset, int depth, bool mapped, bool gray, uint[] palette, int first)
        {
            if (mapped)
            {
                int index = depth == 8 ? Byte(source, ref offset) : Word(source, ref offset);
                index -= first;
                if (index < 0 || index >= palette.Length) throw Bad();
                return palette[index];
            }
            if (gray)
            {
                uint intensity = (uint)Byte(source, ref offset);
                uint alpha = depth == 16 ? (uint)Byte(source, ref offset) : 255u;
                return intensity | intensity << 8 | intensity << 16 | alpha << 24;
            }
            return Color(source, ref offset, depth, false);
        }
        public static TexturePacker.Image Decode(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (!CanDecode(source)) throw new NotSupportedException("Not an additional TGA format.");
            int type = source[2], depth = source[16], descriptor = source[17];
            bool mapped = type == 1 || type == 9, gray = type == 3 || type == 11, rle = type >= 9;
            int first = source[3] | source[4] << 8, entries = source[5] | source[6] << 8, paletteDepth = source[7];
            int width = source[12] | source[13] << 8, height = source[14] | source[15] << 8;
            if (width == 0 || height == 0 || (long)width * height >= 16777216L) throw Bad();
            if ((descriptor & 0xC0) != 0)
                throw new NotSupportedException("Additional TGA interleaving metadata is not recovered.");
            if (mapped ? source[1] != 1 || entries == 0 || (depth != 8 && depth != 16) ||
                (paletteDepth != 15 && paletteDepth != 16 && paletteDepth != 24 && paletteDepth != 32)
                : source[1] != 0 || first != 0 || entries != 0 || paletteDepth != 0 || (gray && depth != 8 && depth != 16)) throw Bad();
            if ((mapped && depth != 8) || (gray && depth != 8)) throw Bad();
            if (mapped && first + entries > 256) throw Bad();
            int attributes = descriptor & 15;
            if (mapped || gray ? attributes != 0 : attributes != 0 && attributes != 1)
                throw new NotSupportedException("Additional TGA descriptor attribute counts outside the validated format subset are not recovered.");
            int offset = 18 + source[0];
            if (offset > source.Length) throw Bad();
            uint[] palette = mapped ? new uint[entries] : null;
            if (mapped)
            {
                bool anyPaletteAlpha = false;
                for (int i = 0; i < entries; i++)
                {
                    palette[i] = Color(source, ref offset, paletteDepth, true);
                    if ((palette[i] & 0xFF000000u) != 0) anyPaletteAlpha = true;
                }
                // The original pre-normalizes an entirely zero-alpha palette
                // before image pixels are expanded. This differs from truecolor.
                if (!anyPaletteAlpha)
                    for (int i = 0; i < entries; i++) palette[i] |= 0xFF000000u;
            }
            int count = checked(width * height), position = 0;
            uint[] pixels = new uint[count];
            while (position < count)
            {
                int header = rle ? Byte(source, ref offset) : 0;
                int length = rle ? (header & 127) + 1 : count - position;
                if (length > count - position || (rle && length > width - position % width)) throw Bad();
                bool repeat = rle && (header & 128) != 0;
                uint repeated = repeat ? Pixel(source, ref offset, depth, mapped, gray, palette, first) : 0;
                for (int i = 0; i < length; i++)
                {
                    uint value = repeat ? repeated : Pixel(source, ref offset, depth, mapped, gray, palette, first);
                    int fx = position % width, fy = position / width;
                    int x = (descriptor & 16) != 0 ? width - 1 - fx : fx;
                    int y = (descriptor & 32) != 0 ? fy : height - 1 - fy;
                    pixels[y * width + x] = value;
                    position++;
                }
            }
            if (!TextureTgaMetadata.TryValidateTail(source,offset))
                throw new NotSupportedException("Additional-format TGA metadata is outside the validated TGA 2.0 tail structures.");
            bool hasAlpha = mapped || depth == 16;
            if (hasAlpha)
            {
                bool anyAlpha = false;
                foreach (uint value in pixels) if ((value & 0xFF000000u) != 0) { anyAlpha = true; break; }
                if (!anyAlpha)
                {
                    for (int i = 0; i < pixels.Length; i++) pixels[i] |= 0xFF000000u;
                    hasAlpha = false;
                }
            }
            var image=new TexturePacker.Image(width,height,pixels,hasAlpha);
            if((width&(width-1))==0&&(height&(height-1))==0)return image;
            if(mapped)return TextureNpotRgba8Resampler.Resize(image);
            return TextureNpotStorageResampler.Resize(image,gray?TextureNpotStorageResampler.Format.L8:hasAlpha?TextureNpotStorageResampler.Format.Argb1555:TextureNpotStorageResampler.Format.Rgb555);
        }
    }
}

