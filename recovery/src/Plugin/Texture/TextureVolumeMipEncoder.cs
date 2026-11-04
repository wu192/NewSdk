using System;
using System.Collections.Generic;

namespace Recovered.Compiler.Texture
{
    // nvDDS volume mips are recursive: average adjacent Z slices first, then
    // apply the separable XY triangle filter. Float images remain unquantized.
    public static class TextureVolumeMipEncoder
    {
        private sealed class Contribution { public int First; public double[] Weights; }
        public static byte[] Encode(uint width, uint height, uint depth, byte[] rgba, string format)
        {
            byte[] levelZero = TextureVolumeRawDdsEncoder.Encode(width, height, depth, rgba, format, false);
            int stride = format == "D3DFMT_R5G6B5" ? 2 : 4;
            uint w = width, h = height, d = depth;
            int levels = checked((int)TextureVolume.MipCount(width, height, depth));
            long size = levelZero.Length;
            for (int level = 1; level < levels; level++)
            {
                w = Math.Max(w / 2, 1u); h = Math.Max(h / 2, 1u); d = Math.Max(d / 2, 1u);
                size = checked(size + (long)w * h * d * stride);
            }
            byte[] output = new byte[checked((int)size)];
            Buffer.BlockCopy(levelZero, 0, output, 0, levelZero.Length);
            Put(output, 8, 0x008a1007u); Put(output, 108, 0x00401008u);
            // Original header counts XY levels only, even if depth yields more
            // payload levels. Preserve this observable legacy writer behavior.
            uint headerLevels = TextureVolume.MipCount(width, height, 1);
            Put(output, 28, headerLevels);
            float[] current = new float[rgba.Length];
            for (int i = 0; i < rgba.Length; i++) current[i] = (float)((double)rgba[i] / 255.0);
            int offset = levelZero.Length;
            int cw = checked((int)width), ch = checked((int)height), cd = checked((int)depth);
            for (int level = 1; level < levels; level++)
            {
                int nw = Math.Max(cw / 2, 1), nh = Math.Max(ch / 2, 1), nd = Math.Max(cd / 2, 1);
                float[] next = NextLevel(current, cw, ch, cd);
                offset = WriteLevel(output, offset, next, format);
                current = next; cw = nw; ch = nh; cd = nd;
            }
            if (offset != output.Length) throw new InvalidOperationException("Volume mip output size mismatch.");
            return output;
        }
        // Volume mip filtering is recursive, unlike the base-relative 2D path.
        // Keep the original Z average and float stores before the XY passes.
        internal static float[] NextLevel(float[] current, int cw, int ch, int cd)
        {
            int nw = Math.Max(cw / 2, 1), nh = Math.Max(ch / 2, 1), nd = Math.Max(cd / 2, 1);
            float[] next = new float[checked(nw * nh * nd * 4)];
            int oldSlice = checked(cw * ch * 4), newSlice = checked(nw * nh * 4);
            for (int z = 0; z < nd; z++)
            {
                float[] averaged = new float[oldSlice];
                if (cd == 1) Array.Copy(current, averaged, oldSlice);
                else for (int i = 0; i < oldSlice; i++)
                    averaged[i] = (float)(((double)current[(z * 2) * oldSlice + i] + (double)current[(z * 2 + 1) * oldSlice + i]) * 0.5);
                float[] filtered;
                if ((long)nw * ch < (long)cw * nh)
                    filtered = Vertical(Horizontal(averaged, cw, ch, nw), nw, ch, nh);
                else filtered = Horizontal(Vertical(averaged, cw, ch, nh), cw, nh, nw);
                Array.Copy(filtered, 0, next, z * newSlice, newSlice);
            }
            return next;
        }
        public static byte[] EncodeStrip(uint width, uint height, byte[] rgba, string format)
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
            return Encode(height, height, checked(width / height), ordered, format);
        }
        private static Contribution[] Contributions(int source, int target)
        {
            double scale = (double)source / target;
            Contribution[] result = new Contribution[target];
            for (int output = 0; output < target; output++)
            {
                double center = (output + 0.5) * scale - 0.5;
                int first = Math.Max(0, (int)Math.Ceiling(center - scale));
                int last = Math.Min(source - 1, (int)Math.Floor(center + scale));
                List<double> weights = new List<double>();
                double sum = 0;
                for (int input = first; input <= last; input++)
                {
                    double value = 1.0 - Math.Abs(input - center) / scale;
                    // Keep zero end taps in contiguous storage. Zero products
                    // do not affect non-negative finite pixel accumulations.
                    weights.Add(value); sum += value;
                }
                double inverse = 1.0 / sum;
                double[] normalized = weights.ToArray();
                // Multiply by one stored reciprocal. Per-item division differs
                // at an observed float32 half-value boundary in the old library.
                // Native resampler (RVA 0x53930) corrects the nearest tap after
                // sequential normalization; omitting this changes threshold bytes.
                double normalizedSum = 0;
                for (int i = 0; i < normalized.Length; i++)
                {
                    normalized[i] *= inverse;
                    normalizedSum += normalized[i];
                }
                if (normalizedSum != 1.0)
                {
                    int nearest = Math.Min(last, Math.Max(first, (int)Math.Floor(center + 0.5)));
                    normalized[nearest - first] += 1.0 - normalizedSum;
                }
                result[output] = new Contribution { First = first, Weights = normalized };
            }
            return result;
        }
        private static float[] Horizontal(float[] source, int width, int height, int targetWidth)
        {
            Contribution[] contributions = Contributions(width, targetWidth);
            float[] output = new float[checked(targetWidth * height * 4)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < targetWidth; x++)
                {
                    Contribution contribution = contributions[x];
                    int destination = (y * targetWidth + x) * 4;
                    for (int tap = 0; tap < contribution.Weights.Length; tap++)
                    {
                        int input = (y * width + contribution.First + tap) * 4;
                        double weight = contribution.Weights[tap];
                        for (int channel = 0; channel < 4; channel++)
                            // Array store forces each accumulated tap to float32;
                            // rounding the product before addition is incorrect.
                            output[destination + channel] = (float)((double)output[destination + channel] + (double)source[input + channel] * weight);
                    }
                }
            return output;
        }
        private static float[] Vertical(float[] source, int width, int height, int targetHeight)
        {
            Contribution[] contributions = Contributions(height, targetHeight);
            float[] output = new float[checked(width * targetHeight * 4)];
            for (int y = 0; y < targetHeight; y++)
                for (int x = 0; x < width; x++)
                {
                    Contribution contribution = contributions[y];
                    int destination = (y * width + x) * 4;
                    for (int tap = 0; tap < contribution.Weights.Length; tap++)
                    {
                        int input = ((contribution.First + tap) * width + x) * 4;
                        double weight = contribution.Weights[tap];
                        for (int channel = 0; channel < 4; channel++)
                            output[destination + channel] = (float)((double)output[destination + channel] + (double)source[input + channel] * weight);
                    }
                }
            return output;
        }
        private static int Quantize(float value, int maximum)
        {
            int result = (int)((double)value * maximum + 0.5);
            if (result < 0) return 0;
            if (result > maximum) return maximum;
            return result;
        }
        private static int WriteLevel(byte[] output, int offset, float[] rgba, string format)
        {
            bool rgb565 = format == "D3DFMT_R5G6B5", alpha = format == "D3DFMT_A8R8G8B8";
            for (int input = 0; input < rgba.Length; input += 4)
            {
                if (rgb565)
                {
                    int packed = (Quantize(rgba[input], 31) << 11) | (Quantize(rgba[input + 1], 63) << 5) | Quantize(rgba[input + 2], 31);
                    output[offset++] = (byte)packed; output[offset++] = (byte)(packed >> 8);
                }
                else
                {
                    output[offset++] = (byte)Quantize(rgba[input + 2], 255);
                    output[offset++] = (byte)Quantize(rgba[input + 1], 255);
                    output[offset++] = (byte)Quantize(rgba[input], 255);
                    output[offset++] = alpha ? (byte)Quantize(rgba[input + 3], 255) : (byte)0;
                }
            }
            return offset;
        }
        private static void Put(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16); data[offset + 3] = (byte)(value >> 24);
        }
    }
}



