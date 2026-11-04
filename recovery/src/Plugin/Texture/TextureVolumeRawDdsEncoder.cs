using System;

namespace Recovered.Compiler.Texture
{
    // Raw volume DDS from tightly packed RGBA slices in z/y/x order.
    // This independent encoder deliberately rejects generated volume mips.
    public static class TextureVolumeRawDdsEncoder
    {
        public static byte[] Encode(uint width, uint height, uint depth, byte[] rgba, string format, bool generateMipMaps)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if (width == 0 || height == 0 || depth == 0 ||
                (width & (width - 1)) != 0 || (height & (height - 1)) != 0 || (depth & (depth - 1)) != 0)
                throw new ArgumentException("Raw volume DDS requires positive power-of-two dimensions.");
            ulong slicePixels = (ulong)width * height;
            if (slicePixels >= 16777216ul || depth >= 16777216ul || slicePixels * depth >= 16777216ul)
                throw new ArgumentOutOfRangeException("width", "Volume exceeds the safe RGBA buffer range.");
            if ((ulong)rgba.LongLength != slicePixels * depth * 4)
                throw new ArgumentException("RGBA storage must exactly match width * height * depth * 4.", "rgba");
            if (generateMipMaps) throw new NotSupportedException("Volume mip filtering has not been recovered.");
            int sliceBytes = checked((int)slicePixels * 4);
            byte[] slice = new byte[sliceBytes];
            Buffer.BlockCopy(rgba, 0, slice, 0, sliceBytes);
            byte[] first = TextureRawDdsEncoder.Encode(width, height, slice, format, false);
            int payload = first.Length - 128;
            byte[] result = new byte[checked(128 + payload * (int)depth)];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Put(result, 8, 0x00881007u); Put(result, 24, depth);
            Put(result, 108, 0x1000); Put(result, 112, 0x00200000);
            for (uint z = 1; z < depth; z++)
            {
                Buffer.BlockCopy(rgba, checked((int)z * sliceBytes), slice, 0, sliceBytes);
                byte[] encoded = TextureRawDdsEncoder.Encode(width, height, slice, format, false);
                Buffer.BlockCopy(encoded, 128, result, checked(128 + (int)z * payload), payload);
            }
            return result;
        }
        public static byte[] EncodeStrip(uint width, uint height, byte[] rgba, string format, bool generateMipMaps)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if ((rgba.Length & 3) != 0) throw new ArgumentException("RGBA byte count must be divisible by four.", "rgba");
            uint[] pixels = new uint[rgba.Length / 4];
            Buffer.BlockCopy(rgba, 0, pixels, 0, rgba.Length);
            TextureVolume volume = new TextureVolume();
            volume.CreateVolumeRGBAImage(width, height, pixels);
            byte[] ordered = new byte[rgba.Length];
            int offset = 0;
            foreach (TextureVolume.Slice slice in volume.Levels[0].Slices)
            {
                int bytes = checked((int)slice.PixelCount * 4);
                Buffer.BlockCopy(slice.Pixels, 0, ordered, offset, bytes); offset += bytes;
            }
            return Encode(height, height, checked(width / height), ordered, format, generateMipMaps);
        }
        private static void Put(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16); data[offset + 3] = (byte)(value >> 24);
        }
    }
}
