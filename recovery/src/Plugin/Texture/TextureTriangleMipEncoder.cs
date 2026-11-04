using System;
using System.Collections.Generic;

namespace Recovered.Compiler.Texture
{
    // Original nvDDS raw 2D mip chain. This is a separate component so the
    // previously accepted no-mipmap encoder keeps its explicit true rejection.
    public static class TextureTriangleMipEncoder
    {
        private sealed class Contribution
        {
            public int First;
            public double[] Weights;
        }
        public static byte[] Encode(uint width, uint height, byte[] rgba, string format)
        {
            // Retain the accepted raw encoder's precise input/rejection domain.
            byte[] levelZero = TextureRawDdsEncoder.Encode(width, height, rgba, format, false);
            int sourceWidth = checked((int)width), sourceHeight = checked((int)height);
            int stride = format == "D3DFMT_R5G6B5" ? 2 : 4;
            int mipCount = 1, targetWidth = sourceWidth, targetHeight = sourceHeight;
            long totalLength = levelZero.LongLength;
            while (targetWidth != 1 || targetHeight != 1)
            {
                targetWidth = Math.Max(1, targetWidth / 2); targetHeight = Math.Max(1, targetHeight / 2);
                totalLength = checked(totalLength + (long)targetWidth * targetHeight * stride); mipCount++;
            }
            byte[] output = new byte[checked((int)totalLength)];
            Buffer.BlockCopy(levelZero, 0, output, 0, levelZero.Length);
            Put(output, 8, 0x000a1007u); Put(output, 28, (uint)mipCount); Put(output, 108, 0x00401008u);
            if (mipCount == 1) return output;
            float[] original = Normalize(rgba);
            targetWidth = sourceWidth; targetHeight = sourceHeight;
            int offset = levelZero.Length;
            while (targetWidth != 1 || targetHeight != 1)
            {
                targetWidth = Math.Max(1, targetWidth / 2); targetHeight = Math.Max(1, targetHeight / 2);
                float[] filtered = ResizeLevel(original, sourceWidth, sourceHeight, targetWidth, targetHeight);
                offset = WriteLevel(output, offset, filtered, format);
            }
            if (offset != output.Length) throw new InvalidOperationException("Raw mipmap output size mismatch.");
            return output;
        }
        // Each 2D mip is filtered from the original normalized image. Both raw
        // and compressed consumers retain these float32 pass/tap stores; callers
        // must not quantize the result before feeding a compressed block.
        internal static float[] Normalize(byte[] rgba)
        {
            float[] original = new float[rgba.Length];
            for (int i = 0; i < rgba.Length; i++) original[i] = (float)((double)rgba[i] / 255.0);
            return original;
        }
        internal static float[] ResizeLevel(float[] original, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            if (targetWidth == sourceWidth && targetHeight == sourceHeight) return original;
            float[] filtered;
            // First pass minimizes intermediate pixel count; equal ratios
            // take the vertical pass first in the original resampler.
            if ((long)targetWidth * sourceHeight < (long)sourceWidth * targetHeight)
            {
                float[] horizontal = Horizontal(original, sourceWidth, sourceHeight, targetWidth);
                filtered = Vertical(horizontal, targetWidth, sourceHeight, targetHeight);
            }
            else
            {
                float[] vertical = Vertical(original, sourceWidth, sourceHeight, targetHeight);
                filtered = Horizontal(vertical, sourceWidth, targetHeight, targetWidth);
            }
            return filtered;
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


