using System;

namespace Recovered.Compiler.Texture
{
    // Deterministic managed range-fit BC1/BC2/BC3 encoding. The block layout and
    // interpolation rules follow the public Direct3D DDS block-compression format.
    // This is a portable production encoder, not an emulation of nvDXT's search
    // or its byte output. No original TextureCompiler or native helper is called.
    internal static class TextureBlockCompression
    {
        internal static byte[] Encode(byte[] rgba, int width, int height, string format)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
                throw new ArgumentException("RGBA storage does not match the image dimensions.");
            bool dxt1 = format == "DXT1" || format == "DXT1A";
            if (!dxt1 && format != "DXT3" && format != "DXT5")
                throw new ArgumentException("Unsupported block-compression format: " + format, "format");
            int blocksWide = (width - 1) / 4 + 1, blocksHigh = (height - 1) / 4 + 1;
            byte[] result = new byte[checked(blocksWide * blocksHigh * (dxt1 ? 8 : 16))];
            byte[] block = new byte[64];
            int[] palette = new int[16];
            int output = 0;
            for (int by = 0; by < blocksHigh; by++)
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    // Extending the last texel produces well-defined partial edge
                    // blocks without introducing a transparent or black border.
                    for (int y = 0; y < 4; y++)
                    {
                        int sourceY = Math.Min(by * 4 + y, height - 1);
                        for (int x = 0; x < 4; x++)
                        {
                            int source = (sourceY * width + Math.Min(bx * 4 + x, width - 1)) * 4;
                            Buffer.BlockCopy(rgba, source, block, (y * 4 + x) * 4, 4);
                        }
                    }
                    if (format == "DXT3") WriteDxt3Alpha(block, result, output);
                    else if (format == "DXT5") WriteDxt5Alpha(block, result, output, palette);
                    WriteColour(block, result, output + (dxt1 ? 0 : 8), format == "DXT1A", palette);
                    output += dxt1 ? 8 : 16;
                }
            return result;
        }

        private static void WriteDxt3Alpha(byte[] block, byte[] output, int at)
        {
            for (int i = 0; i < 8; i++)
            {
                int first = (block[i * 8 + 3] * 15 + 127) / 255;
                int second = (block[i * 8 + 7] * 15 + 127) / 255;
                output[at + i] = (byte)(first | (second << 4));
            }
        }

        private static void WriteDxt5Alpha(byte[] block, byte[] output, int at, int[] palette)
        {
            int low = 255, high = 0;
            for (int i = 3; i < 64; i += 4)
            {
                low = Math.Min(low, block[i]);
                high = Math.Max(high, block[i]);
            }
            output[at] = (byte)high;
            output[at + 1] = (byte)low;
            palette[0] = high; palette[1] = low;
            if (high > low)
            {
                for (int i = 1; i <= 6; i++) palette[i + 1] = ((7 - i) * high + i * low) / 7;
            }
            else
            {
                for (int i = 1; i <= 4; i++) palette[i + 1] = ((5 - i) * high + i * low) / 5;
                palette[6] = 0; palette[7] = 255;
            }
            ulong indices = 0;
            for (int i = 0; i < 16; i++)
            {
                int alpha = block[i * 4 + 3], best = 0, distance = Int32.MaxValue;
                for (int p = 0; p < 8; p++)
                {
                    int candidate = Math.Abs(alpha - palette[p]);
                    if (candidate < distance) { distance = candidate; best = p; }
                }
                indices |= (ulong)best << (i * 3);
            }
            for (int i = 0; i < 6; i++) output[at + 2 + i] = (byte)(indices >> (i * 8));
        }

        private static void WriteColour(byte[] block, byte[] output, int at, bool allowTransparency, int[] palette)
        {
            int firstColour = 0, secondColour = 0, widest = -1;
            bool transparent = false, hasColour = false;
            for (int i = 0; i < 64; i += 4)
            {
                if (allowTransparency && block[i + 3] < 128) { transparent = true; continue; }
                if (!hasColour) { firstColour = secondColour = i; hasColour = true; }
                // Use an actual colour pair as the range axis. A channel-wise
                // box diagonal would collapse anti-correlated red/blue blocks
                // onto unrelated black/magenta endpoints.
                for (int j = 0; j < i; j += 4)
                {
                    if (allowTransparency && block[j + 3] < 128) continue;
                    int dr = block[i] - block[j], dg = block[i + 1] - block[j + 1], db = block[i + 2] - block[j + 2];
                    int distance = dr * dr + dg * dg + db * db;
                    if (distance > widest) { widest = distance; firstColour = i; secondColour = j; }
                }
            }
            if (!hasColour)
            {
                // Canonical all-transparent BC1 block.
                for (int i = 0; i < 4; i++) output[at + i] = 0;
                for (int i = 4; i < 8; i++) output[at + i] = 255;
                return;
            }
            int end0 = Pack565(block[firstColour], block[firstColour + 1], block[firstColour + 2]);
            int end1 = Pack565(block[secondColour], block[secondColour + 1], block[secondColour + 2]);
            if ((transparent && end0 > end1) || (!transparent && end0 < end1))
            {
                int temporary = end0; end0 = end1; end1 = temporary;
            }
            if (!transparent && end0 == end1)
            {
                // BC1 requires end0 > end1 for opaque four-colour mode.
                if (end0 == 0) end0 = 1; else end1--;
            }
            Write16(output, at, end0); Write16(output, at + 2, end1);
            Unpack565(end0, palette, 0); Unpack565(end1, palette, 4);
            for (int channel = 0; channel < 3; channel++)
            {
                palette[8 + channel] = transparent
                    ? (palette[channel] + palette[4 + channel]) / 2
                    : (2 * palette[channel] + palette[4 + channel]) / 3;
                palette[12 + channel] = transparent ? 0 : (palette[channel] + 2 * palette[4 + channel]) / 3;
            }
            uint indices = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 3;
                if (!transparent || block[i * 4 + 3] >= 128)
                {
                    int distance = Int32.MaxValue;
                    for (int p = 0; p < (transparent ? 3 : 4); p++)
                    {
                        int dr = block[i * 4] - palette[p * 4];
                        int dg = block[i * 4 + 1] - palette[p * 4 + 1];
                        int db = block[i * 4 + 2] - palette[p * 4 + 2];
                        int candidate = dr * dr + dg * dg + db * db;
                        if (candidate < distance) { distance = candidate; best = p; }
                    }
                }
                indices |= (uint)best << (i * 2);
            }
            for (int i = 0; i < 4; i++) output[at + 4 + i] = (byte)(indices >> (i * 8));
        }

        private static int Pack565(int r, int g, int b)
        {
            return ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);
        }

        private static void Unpack565(int value, int[] palette, int at)
        {
            int r = (value >> 11) & 31, g = (value >> 5) & 63, b = value & 31;
            palette[at] = (r << 3) | (r >> 2);
            palette[at + 1] = (g << 2) | (g >> 4);
            palette[at + 2] = (b << 3) | (b >> 2);
        }

        private static void Write16(byte[] output, int at, int value)
        {
            output[at] = (byte)value; output[at + 1] = (byte)(value >> 8);
        }
    }
}
