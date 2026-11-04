using System;
using System.Runtime.CompilerServices;

namespace BinaryAssetBuilder.AudioCompiler
{
    // Native encoder reconstruction: RVA 0xf700 (predictor/quantizer),
    // 0xf690 (four-subblock layout), 0xfa60/0xfba0 (128-frame buffering).
    // Kept separate from Plugin routing until original-DLL differential passes.
    public static class Xas1Encoder
    {
        private static readonly float[] Previous = { 0.0f, -0.9375f, -1.796875f, -1.53125f };
        private static readonly float[] Older = { 0.0f, 0.0f, 0.8125f, 0.859375f };

        // Original 0xf700 uses SSE binary32 after every mulss/addss. Legacy
        // x86 Release JIT otherwise retains extended x87 temporaries, which
        // can cross an XAS quantization threshold even for exact signed16
        // imported/resampled samples. Preserve each original float boundary.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float F(float value) { return value; }

        public static byte[] EncodeBlock128(float[] samples, int channels)
        {
            if (samples == null) throw new ArgumentNullException("samples");
            if ((channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8) || samples.Length != 128 * channels)
                throw new ArgumentException("A 1/2/4/6/8-channel block of exactly 128 frames is required");
            byte[] output = new byte[76 * channels];
            EncodeBlock128(samples, 0, channels, output, 0, new float[30]);
            return output;
        }

        internal static void EncodeBlock128(float[] samples, int sampleOffset, int channels,
            byte[] output, int outputOffset, float[] source)
        {
            for (int channel = 0; channel < channels; ++channel)
                for (int group = 0; group < 4; ++group)
                    EncodeGroup(samples, channels, sampleOffset + channel + group * 32 * channels,
                        output, outputOffset + channel * 76 + group * 4,
                        outputOffset + channel * 76 + 16 + group, source);
        }

        private static void EncodeGroup(float[] samples, int channels, int at,
            byte[] output, int headerOffset, int dataOffset, float[] source)
        {
            int first = Math.Min(32752, ((int)F(F(samples[at] * 32767.0f) + 8.0f)) & ~15);
            int second = Math.Min(32752, ((int)F(F(samples[at + channels] * 32767.0f) + 8.0f)) & ~15);
            float best = 1000000000.0f;
            int filter = 0;
            for (int i = 0; i < 30; ++i)
                source[i] = F(samples[at + (i + 2) * channels] * 32767.0f);
            for (int f = 0; f < 4; ++f)
            {
                int old = first, previous = second;
                float maximum = 0.0f;
                for (int i = 0; i < 30; ++i)
                {
                    float value = source[i];
                    float prediction = F(previous * Previous[f]);
                    prediction = F(prediction + F(old * Older[f]));
                    float residual = F(prediction + value);
                    maximum = Math.Max(maximum, residual > 0 ? residual : -residual);
                    old = previous;
                    previous = (int)value;
                }
                if (maximum < best) { best = maximum; filter = f; }
                if (f == 0 && maximum <= 7.0f) { filter = 0; break; }
            }
            int magnitude = Math.Max(-32768, Math.Min(32767, (int)best));
            int shift = 0, mask = 0x4000;
            while (shift < 12 && ((magnitude + (mask >> 3)) & mask) == 0)
            { ++shift; mask >>= 1; }
            int firstHeader = first | filter, secondHeader = second | shift;
            output[headerOffset] = (byte)firstHeader; output[headerOffset + 1] = (byte)(firstHeader >> 8);
            output[headerOffset + 2] = (byte)secondHeader; output[headerOffset + 3] = (byte)(secondHeader >> 8);
            float h1 = second, h2 = first;
            float gain = 1 << shift;
            for (int i = 0; i < 30; ++i)
            {
                float p2 = F(Older[filter] * h2);
                float p1 = F(Previous[filter] * h1);
                float prediction = F(p2 + p1);
                float residual = F(prediction + source[i]);
                residual = F(residual * gain);
                int quantized = ((int)residual + 0x800) & ~0xfff;
                quantized = Math.Max(-32768, Math.Min(32767, quantized));
                int destination = dataOffset + (i / 2) * 4;
                if ((i & 1) == 0) output[destination] = (byte)((quantized >> 8) & 0xf0);
                else output[destination] |= (byte)((quantized >> 12) & 15);
                float reconstructed = F((quantized >> shift) - prediction);
                h2 = h1; h1 = reconstructed;
            }
        }

        public static PcmEncodingResult EncodeWave(byte[] wave, bool streamed)
        {
            Pcm16Encoder.PcmWaveInput input = Pcm16Encoder.ReadWave(wave);
            return EncodeSamples(input.Samples, input.Channels, input.SampleRate, streamed);
        }

        public static PcmEncodingResult EncodeSamples(float[] samples, int channels, int sampleRate, bool streamed)
        {
            if (samples == null) throw new ArgumentNullException("samples");
            if ((channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8) || samples.Length % channels != 0)
                throw new ArgumentOutOfRangeException("channels");
            int frames = samples.Length / channels;
            if (frames == 0 || frames > 0x1fffffff) throw new ArgumentOutOfRangeException("samples");
            ValidateSamples(samples, samples.Length);
            byte[] header = Pcm16Encoder.CreateHeader(4, channels, sampleRate, frames, streamed);
            int maximum = streamed ? Pcm16Encoder.NativeBlockFrames(sampleRate * channels * 0.59375f, sampleRate) : frames;
            byte[] custom = new byte[checked((streamed ? 0 : header.Length) + SegmentLength(frames, channels, maximum, streamed))];
            if (!streamed) Buffer.BlockCopy(header, 0, custom, 0, header.Length);
            EncodeSegment(samples, 0, frames, channels, maximum, streamed, custom,
                streamed ? 0 : header.Length, new float[30]);
            return new PcmEncodingResult(streamed ? header : new byte[0], custom, sampleRate, channels, frames);
        }

        internal static void ValidateSamples(float[] samples, int count)
        {
            for (int i = 0; i < count; ++i)
                if (Single.IsNaN(samples[i]) || Single.IsInfinity(samples[i]) || samples[i] < -1 || samples[i] > 1)
                    throw new ArgumentOutOfRangeException("samples");
        }

        internal static int SegmentLength(int frames, int channels, int maximum, bool streamed)
        {
            int bytes = checked(((frames + 127) / 128) * (76 * channels) + ((frames - 1) / maximum + 1) * 8);
            return streamed ? checked(bytes + 64 - bytes % 64) : bytes;
        }

        internal static void EncodeSegment(float[] samples, int sampleOffset, int frames,
            int channels, int maximum, bool streamed, byte[] output, int outputOffset, float[] source)
        {
            int frameOffset = 0, encodedOffset = 0, destination = outputOffset;
            while (frameOffset < frames)
            {
                int count = Math.Min(maximum, frames - frameOffset);
                bool last = frameOffset + count == frames;
                // Incomplete blocks carry across streamed chunks, but never
                // across independently flushed intro/loop segments.
                int fullEnd = ((frameOffset + count) / 128) * 128;
                int encodedFrames = (last ? frames : fullEnd) - encodedOffset;
                int blocks = (fullEnd - encodedOffset) / 128 + (last && frames % 128 != 0 ? 1 : 0);
                int bytes = blocks * (76 * channels) + 8;
                int padding = streamed && last ? 64 - ((destination - outputOffset + bytes) % 64) : 0;
                Pcm16Encoder.WriteBE(output, destination, (uint)(bytes + padding) | (last && streamed ? 0x80000000u : 0u));
                Pcm16Encoder.WriteBE(output, destination + 4, (uint)encodedFrames);
                destination += 8;
                while (encodedOffset < fullEnd)
                {
                    EncodeBlock128(samples, sampleOffset + encodedOffset * channels, channels, output, destination, source);
                    encodedOffset += 128;
                    destination += 76 * channels;
                }
                if (last && encodedOffset < frames)
                {
                    int remaining = frames - encodedOffset;
                    float[] block = new float[128 * channels];
                    Array.Copy(samples, sampleOffset + encodedOffset * channels, block, 0, remaining * channels);
                    for (int i = remaining; i < 128; ++i)
                        for (int c = 0; c < channels; ++c) block[i * channels + c] = block[(remaining - 1) * channels + c];
                    EncodeBlock128(block, 0, channels, output, destination, source);
                    destination += 76 * channels;
                }
                destination += padding;
                frameOffset += count;
            }
        }
    }
}
