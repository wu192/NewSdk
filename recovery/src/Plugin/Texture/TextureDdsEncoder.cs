using System;
using System.IO;

namespace Recovered.Compiler.Texture
{
    // Retains the recovered DDS headers/raw encoders and triangle filtering.
    // DXT3/5 use the embedded public S3 codec; DXT1/1A use managed BC, with no codec DLL.
    public static class TextureDdsEncoder
    {
        public static byte[] Encode2D(int width, int height, byte[] rgba, string format, bool mipmaps)
        {
            format = NormalizeFormat(format);
            if (!IsCompressed(format))
                return mipmaps ? TextureTriangleMipEncoder.Encode((uint)width, (uint)height, rgba, "D3DFMT_" + format)
                    : TextureRawDdsEncoder.Encode((uint)width, (uint)height, rgba, "D3DFMT_" + format, false);
            Validate(width, height, rgba);
            int levels = mipmaps ? (int)TextureVolume.MipCount((uint)width, (uint)height, 1) : 1;
            using (var output = new MemoryStream())
            {
                byte[] header = Header(width, height, 1, format, mipmaps, levels);
                output.Write(header, 0, header.Length);
                float[] original = TextureTriangleMipEncoder.Normalize(rgba);
                int w = width, h = height;
                for (int level = 0; level < levels; level++)
                {
                    float[] samples = TextureTriangleMipEncoder.ResizeLevel(original, width, height, w, h);
                    byte[] encoded = EncodeLevel(samples, w, h, format);
                    output.Write(encoded, 0, encoded.Length);
                    w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                }
                return output.ToArray();
            }
        }
        public static byte[] EncodeVolumeStrip(int width, int height, byte[] rgba, string format, bool mipmaps)
        {
            format = NormalizeFormat(format);
            if (!IsCompressed(format))
                return mipmaps ? TextureVolumeMipEncoder.EncodeStrip((uint)width, (uint)height, rgba, "D3DFMT_" + format)
                    : TextureVolumeRawDdsEncoder.EncodeStrip((uint)width, (uint)height, rgba, "D3DFMT_" + format, false);
            Validate(width, height, rgba);
            uint[] pixels = new uint[rgba.Length / 4]; Buffer.BlockCopy(rgba, 0, pixels, 0, rgba.Length);
            TextureVolume volume = TextureVolume.FromStrip((uint)width, (uint)height, pixels);
            int depth = volume.Levels[0].Slices.Length;
            byte[] ordered = new byte[rgba.Length]; int at = 0;
            foreach (TextureVolume.Slice slice in volume.Levels[0].Slices)
            {
                int count = checked((int)slice.PixelCount * 4);
                Buffer.BlockCopy(slice.Pixels, 0, ordered, at, count); at += count;
            }
            int w = height, h = height, d = depth;
            int levels = mipmaps ? (int)TextureVolume.MipCount((uint)w, (uint)h, (uint)d) : 1;
            using (var output = new MemoryStream())
            {
                byte[] header = Header(w, h, d, format, mipmaps, mipmaps ? (int)TextureVolume.MipCount((uint)w, (uint)h, 1) : 1);
                // A one-slice VolumeTexture is still a DDS volume.
                Put(header, 8, mipmaps ? 0x8a1007u : 0x881007u); Put(header, 24, (uint)d); Put(header, 112, 0x200000u);
                output.Write(header, 0, header.Length);
                float[] current = TextureTriangleMipEncoder.Normalize(ordered);
                for (int level = 0; level < levels; level++)
                {
                    int sliceLength = checked(w * h * 4);
                    for (int z = 0; z < d; z++)
                    {
                        float[] slice = new float[sliceLength]; Array.Copy(current, z * sliceLength, slice, 0, sliceLength);
                        byte[] encoded = EncodeLevel(slice, w, h, format); output.Write(encoded, 0, encoded.Length);
                    }
                    if (level + 1 < levels) current = TextureVolumeMipEncoder.NextLevel(current, w, h, d);
                    w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); d = Math.Max(1, d / 2);
                }
                return output.ToArray();
            }
        }
        internal static byte[] EncodeLevel(float[] samples, int width, int height, string format)
        {
            if (format != "DXT3" && format != "DXT5")
            {
                byte[] rgba = ToBytes(samples);
                // Keep the recovered <= 0.5 cut-off for filtered one-bit alpha;
                // rounding a half-value to byte 128 would make it opaque.
                if (format == "DXT1A")
                    for (int i = 3; i < rgba.Length; i += 4) rgba[i] = samples[i] <= 0.5f ? (byte)0 : (byte)255;
                return TextureBlockCompression.Encode(rgba, width, height, format);
            }
            // Preserve the accepted native float inputs and partial-block mapping.
            int bw = Math.Min(4, width), bh = Math.Min(4, height);
            byte[] output = new byte[checked(((width + 3) / 4) * ((height + 3) / 4) * 16)];
            float[] block = new float[bw * bh * 4]; int at = 0;
            for (int y = 0; y < height; y += 4) for (int x = 0; x < width; x += 4)
            {
                for (int row = 0; row < bh; row++) Array.Copy(samples, ((y + row) * width + x) * 4, block, row * bw * 4, bw * 4);
                byte[] encoded = TextureDxtSampleBlockEncoder.EncodeFloatBlock(block, bw, bh, "D3DFMT_" + format);
                Buffer.BlockCopy(encoded, 0, output, at, encoded.Length); at += encoded.Length;
            }
            return output;
        }
        internal static byte[] ToBytes(float[] samples)
        {
            byte[] result = new byte[samples.Length];
            for (int i = 0; i < samples.Length; i++) result[i] = (byte)Math.Max(0, Math.Min(255, (int)((double)samples[i] * 255 + 0.5)));
            return result;
        }
        private static string NormalizeFormat(string format)
        {
            if (format != null && format.StartsWith("D3DFMT_", StringComparison.Ordinal)) format = format.Substring(7);
            switch (format)
            {
                case "A8R8G8B8": case "X8R8G8B8": case "R5G6B5":
                case "DXT1": case "DXT1A": case "DXT3": case "DXT5": return format;
                default: throw new NotSupportedException("Unsupported texture output format: " + format);
            }
        }
        private static bool IsCompressed(string format) { return format.StartsWith("DXT", StringComparison.Ordinal); }
        private static void Validate(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0 || (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
                throw new ArgumentException("DDS encoding requires positive power-of-two dimensions.");
            if ((long)width * height >= 16777216L) throw new ArgumentOutOfRangeException("width", "Texture exceeds the 64 MiB RGBA limit.");
            if (rgba == null || rgba.LongLength != (long)width * height * 4) throw new ArgumentException("RGBA storage must exactly match image dimensions.", "rgba");
        }
        private static byte[] Header(int width, int height, int depth, string format, bool mipmaps, int levels)
        {
            byte[] result = new byte[128];
            Put(result, 0, 0x20534444); Put(result, 4, 124); Put(result, 8, mipmaps ? 0xa1007u : 0x81007u);
            Put(result, 12, (uint)height); Put(result, 16, (uint)width);
            Put(result, 20, checked((uint)(((width + 3) / 4) * ((height + 3) / 4) * (format == "DXT1" || format == "DXT1A" ? 8 : 16))));
            if (mipmaps) Put(result, 28, (uint)levels);
            Put(result, 76, 32); Put(result, 80, 4); Put(result, 84, format == "DXT1" || format == "DXT1A" ? 0x31545844u : format == "DXT3" ? 0x33545844u : 0x35545844u);
            Put(result, 108, mipmaps ? 0x401008u : 0x1000u);
            return result;
        }
        private static void Put(byte[] output, int at, uint value) { for (int i = 0; i < 4; i++) output[at + i] = (byte)(value >> (i * 8)); }
    }
}
