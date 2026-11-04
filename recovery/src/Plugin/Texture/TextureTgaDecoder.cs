using System;
using System.IO;

namespace Recovered.Compiler.Texture
{
    // Narrow CPU decoder candidate, deliberately opt-in until its exact Windows
    // original-LoadImageA differential receipt is accepted. No original DLL calls.
    public static class TextureTgaDecoder
    {
        public static TexturePacker.Image Load(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            return Decode(File.ReadAllBytes(path));
        }
        public static TexturePacker.Image Decode(byte[] source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.Length < 18) throw new InvalidDataException("TGA header is truncated.");
            int idLength = source[0], colorMap = source[1], type = source[2];
            if (colorMap != 0 || (type != 2 && type != 10))
                throw new NotSupportedException("Only unmapped true-color raw/RLE TGA images are supported.");
            // Real unmapped 7.1 writers leave entry depth=32 with no palette.
            // Keep the existing origin/count and other-depth subset boundaries;
            // nonzero palette regions are rejected by the original loader.
            for (int i = 3; i < 7; i++)
                if (source[i] != 0) throw new NotSupportedException("TGA palette/origin metadata is outside the recovered subset.");
            if (source[7] != 0 && source[7] != 32)
                throw new NotSupportedException("TGA palette/origin metadata is outside the recovered subset.");
            int width = source[12] | source[13] << 8;
            int height = source[14] | source[15] << 8;
            int depth = source[16], descriptor = source[17], attributes = descriptor & 15;
            if (depth != 24 && depth != 32)
                throw new NotSupportedException("Only 24-bit and 32-bit true-color TGA images are supported.");
            if ((descriptor & 0xC0) != 0 || (depth == 24 && attributes != 0) ||
                (depth == 32 && attributes != 0 && attributes != 8))
                throw new NotSupportedException("TGA attribute/interleaving metadata is outside the recovered subset.");
            if (width == 0 || height == 0) throw new InvalidDataException("TGA dimensions must be positive.");
            long count64 = (long)width * height;
            if (count64 >= 16777216L)
                throw new NotSupportedException("TGA images at or above the original 64 MiB processing limit are outside this decoder subset.");
            int count = (int)count64, bytesPerPixel = depth / 8, offset = 18 + idLength;
            if (offset > source.Length) throw new InvalidDataException("TGA image ID is truncated.");
            uint[] pixels = new uint[count];
            int position = 0;
            while (position < count)
            {
                int packet = type == 10 ? ReadByte(source, ref offset) : 0;
                int length = type == 10 ? (packet & 127) + 1 : count - position;
                bool repeat = type == 10 && (packet & 128) != 0;
                if (type == 10 && length > width - position % width)
                    throw new BinaryAssetBuilder.BinaryAssetBuilderException(BinaryAssetBuilder.ErrorCode.BadTextureFile,
                        "D3DX cannot load the texture from disk.", new object[0]);
                if (length > count - position) throw new InvalidDataException("TGA RLE packet exceeds the declared image.");
                uint repeated = repeat ? ReadPixel(source, ref offset, bytesPerPixel) : 0;
                for (int i = 0; i < length; i++)
                {
                    uint pixel = repeat ? repeated : ReadPixel(source, ref offset, bytesPerPixel);
                    int fileY = position / width, fileX = position % width;
                    int x = (descriptor & 0x10) != 0 ? width - 1 - fileX : fileX;
                    int y = (descriptor & 0x20) != 0 ? fileY : height - 1 - fileY;
                    pixels[y * width + x] = pixel;
                    position++;
                }
            }
            ValidateMetadataTail(source, offset);
            bool hasAlpha = depth == 32;
            if (hasAlpha)
            {
                bool anyNonzeroAlpha = false;
                for (int i = 0; i < pixels.Length; i++)
                    if ((pixels[i] & 0xFF000000u) != 0) { anyNonzeroAlpha = true; break; }
                if (!anyNonzeroAlpha)
                {
                    for (int i = 0; i < pixels.Length; i++) pixels[i] |= 0xFF000000u;
                    hasAlpha = false;
                }
            }
            TexturePacker.Image image = new TexturePacker.Image(width, height, pixels, hasAlpha);
            return ((width & (width - 1)) != 0 || (height & (height - 1)) != 0)
                ? TextureNpotRgba8Resampler.Resize(image) : image;
        }
        private static bool BytesZero(byte[] source, int start, int count)
        {
            if (start < 0 || count < 0 || start > source.Length - count) return false;
            for (int i = 0; i < count; i++) if (source[start + i] != 0) return false;
            return true;
        }
        private static bool AsciiField(byte[] source, int start, string text, int length)
        {
            if (start < 0 || start > source.Length - length) return false;
            for (int i = 0; i < length; i++)
                if (source[start + i] != (i < text.Length ? (byte)text[i] : (byte)0)) return false;
            return true;
        }
        private static uint ReadUInt32(byte[] source, int offset)
        {
            return (uint)source[offset] | (uint)source[offset + 1] << 8 |
                (uint)source[offset + 2] << 16 | (uint)source[offset + 3] << 24;
        }
        private static void ValidateMetadataTail(byte[] source, int offset)
        {
            if(TextureTgaMetadata.TryValidateTail(source,offset))return;
            ValidateLegacyMetadataTail(source,offset);
        }
        private static void ValidateLegacyMetadataTail(byte[] source, int offset)
        {
            while (offset < source.Length)
            {
                int extensionStart = offset;
                bool extension = source.Length - offset >= 2 && source[offset] == 239 && source[offset + 1] == 1;
                bool devil = false;
                if (extension)
                {
                    devil = AsciiField(source, offset + 438, "Developer's Image Library (DevIL)", 41);
                    int size = devil ? 507 : 495;
                    if (source.Length - offset < size + 25 ||
                        (devil ? !BytesZero(source, offset + 379, 12) || !BytesZero(source, offset + 482, 24) || source[offset + 506] != 3
                               : !BytesZero(source, offset + 470, 25)))
                        throw new NotSupportedException("TGA rendering/offset metadata is outside the recovered extension subset.");
                    offset += size;
                }
                if (source.Length - offset < 25 || ReadUInt32(source, offset + 4) != 0 ||
                    !AsciiField(source, offset + 8, "TRUEVISION-XFILE.", 17))
                    throw new NotSupportedException("TGA metadata must end in a recognized extension/footer chain.");
                uint extensionPointer = ReadUInt32(source, offset);
                if (extensionPointer != 0 && (!extension || extensionPointer != (uint)extensionStart))
                    throw new NotSupportedException("TGA extension footer pointer does not reference its validated extension.");
                offset += 25;
                if (offset < source.Length && source[offset] == 0) offset++;
                else if (!(devil && offset == source.Length))
                    throw new NotSupportedException("Only the bounded DevIL extension dialect permits an omitted footer NUL.");
            }
        }
        private static int ReadByte(byte[] source, ref int offset)
        {
            if (offset >= source.Length) throw new InvalidDataException("TGA pixel/RLE data is truncated.");
            return source[offset++];
        }
        private static uint ReadPixel(byte[] source, ref int offset, int bytesPerPixel)
        {
            uint blue = (uint)ReadByte(source, ref offset), green = (uint)ReadByte(source, ref offset);
            uint red = (uint)ReadByte(source, ref offset);
            uint alpha = bytesPerPixel == 4 ? (uint)ReadByte(source, ref offset) : 255u;
            return red | green << 8 | blue << 16 | alpha << 24;
        }
    }
}

