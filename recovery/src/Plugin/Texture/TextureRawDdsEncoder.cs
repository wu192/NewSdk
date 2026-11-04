using System;

namespace Recovered.Compiler.Texture
{
    // Original nvDDS uncompressed 2D DDS output. Encoding never invokes the
    // original compiler or a substituted compression library.
    public static class TextureRawDdsEncoder
    {
        // Source bytes are tightly packed RGBA, matching nvImage<rgba_t>.
        // Native mip filtering is intentionally outside this initial adapter.
        public static byte[] Encode(uint width, uint height, byte[] rgba, string format, bool generateMipMaps)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if (width == 0 || height == 0 || (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
                throw new ArgumentException("Raw DDS encoding requires positive power-of-two dimensions.");
            ulong pixels = (ulong)width * height;
            if (pixels >= 16777216ul)
                throw new ArgumentOutOfRangeException("width", "Image exceeds the original plugin's safe RGBA buffer range.");
            if ((ulong)rgba.LongLength != pixels * 4)
                throw new ArgumentException("RGBA storage must exactly match width * height * 4.", "rgba");
            bool alpha = format == "D3DFMT_A8R8G8B8";
            bool opaque = format == "D3DFMT_X8R8G8B8";
            bool rgb565 = format == "D3DFMT_R5G6B5";
            if (!alpha && !opaque && !rgb565)
                throw new NotSupportedException("Only A8R8G8B8, X8R8G8B8 and R5G6B5 raw DDS formats have been recovered.");
            if (generateMipMaps)
                throw new NotSupportedException("Original native mipmap filtering has not been accepted for this encoder.");

            int stride = rgb565 ? 2 : 4;
            uint size = checked((uint)pixels * (uint)stride);
            byte[] result = new byte[checked(128 + (int)size)];
            Put(result, 0, 0x20534444u);
            Put(result, 4, 124);
            // Unlike modern generic DDS writers, the original nvDDS writer
            // uses DDSD_LINEARSIZE even for these uncompressed formats.
            Put(result, 8, 0x00081007u);
            Put(result, 12, height); Put(result, 16, width); Put(result, 20, size);
            Put(result, 76, 32);
            Put(result, 80, alpha ? 0x41u : 0x40u);
            Put(result, 88, rgb565 ? 16u : 32u);
            Put(result, 92, rgb565 ? 0xf800u : 0xff0000u);
            Put(result, 96, rgb565 ? 0x07e0u : 0x00ff00u);
            Put(result, 100, rgb565 ? 0x001fu : 0x0000ffu);
            Put(result, 104, alpha ? 0xff000000u : 0u);
            Put(result, 108, 0x1000);
            for (int pixel = 0, output = 128; pixel < (int)pixels; pixel++, output += stride)
            {
                int source = pixel * 4;
                if (rgb565)
                {
                    int packed = (((rgba[source] * 31 + 127) / 255) << 11) | (((rgba[source + 1] * 63 + 127) / 255) << 5) | ((rgba[source + 2] * 31 + 127) / 255);
                    result[output] = (byte)packed; result[output + 1] = (byte)(packed >> 8);
                }
                else
                {
                    result[output] = rgba[source + 2]; result[output + 1] = rgba[source + 1]; result[output + 2] = rgba[source];
                    result[output + 3] = alpha ? rgba[source + 3] : (byte)0;
                }
            }
            return result;
        }
        private static void Put(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16); data[offset + 3] = (byte)(value >> 24);
        }
    }
}
