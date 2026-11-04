using System;
using System.IO;
using System.Text;

namespace BinaryAssetBuilder.AudioCompiler
{
    public sealed class PcmEncodingResult
    {
        public readonly byte[] HeaderData;
        public readonly byte[] CustomData;
        public readonly int SampleRate;
        public readonly int Channels;
        public readonly int Frames;
        internal PcmEncodingResult(byte[] header, byte[] data, int rate, int channels, int frames)
        { HeaderData = header; CustomData = data; SampleRate = rate; Channels = channels; Frames = frames; }
    }

    // Managed replacement for the native P6B0 encoder and SIMEX format 39
    // container writer. This narrow entry point accepts PCM8/16 RIFF/WAVE;
    // AIFF, loop metadata and resampling are not inferred. WAVE 4/6-channel
    // remapping follows the original importer and is applied exactly once.
    public static class Pcm16Encoder
    {
        public static PcmEncodingResult EncodeWave(byte[] wave, bool streamed)
        {
            PcmWaveInput input = ReadWave(wave);
            return EncodeSamples(input.Samples, input.Channels, input.SampleRate, streamed);
        }

        internal sealed class PcmWaveInput
        {
            internal float[] Samples;
            internal int Channels;
            internal int SampleRate;
        }

        internal static PcmWaveInput ReadWave(byte[] wave)
        {
            if (wave == null) throw new ArgumentNullException("wave");
            if (wave.Length < 12 || Tag(wave, 0) != "RIFF" || Tag(wave, 8) != "WAVE")
                throw new NotSupportedException("PCM8/16 RIFF/WAVE input is required");
            int channels = 0, rate = 0, bits = 0, format = 0, align = 0;
            int dataOffset = -1, dataLength = 0;
            long end = 8L + U32(wave, 4);
            if (end > wave.Length) throw new InvalidDataException("Truncated RIFF data");
            for (long at = 12; at + 8 <= end; )
            {
                int offset = checked((int)at);
                uint size = U32(wave, offset + 4);
                long next = at + 8 + size;
                if (next > end) throw new InvalidDataException("Truncated RIFF chunk");
                string tag = Tag(wave, offset);
                if (tag == "fmt ")
                {
                    if (size < 16) throw new InvalidDataException("Truncated WAVE format");
                    format = U16(wave, offset + 8);
                    channels = U16(wave, offset + 10);
                    rate = checked((int)U32(wave, offset + 12));
                    align = U16(wave, offset + 20);
                    bits = U16(wave, offset + 22);
                }
                else if (tag == "data") { dataOffset = offset + 8; dataLength = checked((int)size); }
                else if (tag == "smpl") throw new NotSupportedException("WAVE loop metadata is not supported by this entry point");
                at = next + (size & 1);
            }
            if (format != 1 || (bits != 8 && bits != 16) || (channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8))
                throw new NotSupportedException("Only 1/2/4/6/8-channel PCM8/16 WAVE is supported by this entry point");
            int bytesPerSample = bits / 8;
            if (rate <= 0 || rate > 0x3ffff || align != channels * bytesPerSample || dataOffset < 0 || dataLength % align != 0)
                throw new InvalidDataException("Invalid PCM WAVE format");
            int frames = dataLength / align;
            if (frames == 0 || frames > 0x1fffffff) throw new NotSupportedException("Unsupported PCM frame count");
            float[] samples = new float[dataLength / bytesPerSample];
            for (int i = 0; i < samples.Length; ++i)
            {
                if (bits == 8)
                {
                    // P8U0 native decoder, RVA 0x10390: zero-extend byte,
                    // subtract 128, multiply by float constant 1/128.
                    samples[i] = (wave[dataOffset + i] - 128) * 0.0078125f;
                }
                else
                {
                    short sample = unchecked((short)U16(wave, dataOffset + i * 2));
                    samples[i] = sample / 32768.0f;
                }
            }
            RemapWaveChannels(samples, channels);
            PcmWaveInput input = new PcmWaveInput();
            input.Samples = samples; input.Channels = channels; input.SampleRate = rate;
            return input;
        }

        // Original ProcessInstance oracle40249960201 confirms output->input
        // [0,1,3,2] and [0,2,1,4,5,3] for PCM8/16/24. SIMEX changes
        // destination track pointers before import; these are inverse maps.
        // Original eight-channel oracle41258221174 confirms identity order.
        // WAVE_EXTENSIBLE speaker masks do not request another remapping.
        internal static void RemapWaveChannels(float[] samples, int channels)
        {
            if (channels != 4 && channels != 6) return;
            int[] source = channels == 4 ? new int[] { 0, 1, 3, 2 } : new int[] { 0, 2, 1, 4, 5, 3 };
            float[] frame = new float[channels];
            for (int at = 0; at < samples.Length; at += channels)
            {
                Array.Copy(samples, at, frame, 0, channels);
                for (int channel = 0; channel < channels; ++channel)
                    samples[at + channel] = frame[source[channel]];
            }
        }

        public static PcmEncodingResult EncodeSamples(float[] interleaved, int channels, int sampleRate, bool streamed)
        {
            if (interleaved == null) throw new ArgumentNullException("interleaved");
            if ((channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8) || interleaved.Length % channels != 0)
                throw new ArgumentOutOfRangeException("channels");
            if (sampleRate <= 0 || sampleRate > 0x3ffff) throw new ArgumentOutOfRangeException("sampleRate");
            int frames = interleaved.Length / channels;
            if (frames == 0 || frames > 0x1fffffff) throw new ArgumentOutOfRangeException("interleaved");
            byte[] header = CreateHeader(channels, sampleRate, frames, streamed);
            int maxFrames = streamed ? NativeBlockFrames(sampleRate * channels * 2.0f, sampleRate) : frames;
            byte[] custom = new byte[checked((streamed ? 0 : header.Length) + SegmentLength(frames, channels, maxFrames, streamed))];
            if (!streamed) Buffer.BlockCopy(header, 0, custom, 0, header.Length);
            EncodeSegment(interleaved, 0, frames, channels, maxFrames, streamed, custom, streamed ? 0 : header.Length);
            return new PcmEncodingResult(streamed ? header : new byte[0], custom, sampleRate, channels, frames);
        }

        internal static int SegmentLength(int frames, int channels, int maximum, bool streamed)
        {
            int bytes = checked(frames * channels * 2 + ((frames - 1) / maximum + 1) * 8);
            return streamed ? checked(bytes + 64 - bytes % 64) : bytes;
        }

        internal static void EncodeSegment(float[] interleaved, int sampleOffset, int frames,
            int channels, int maxFrames, bool streamed, byte[] output, int outputOffset)
        {
            int destination = outputOffset;
            for (int frame = 0; frame < frames; )
            {
                int count = Math.Min(maxFrames, frames - frame);
                bool last = frame + count == frames;
                int bytes = checked(8 + count * channels * 2);
                int padding = streamed && last ? 64 - ((destination - outputOffset + bytes) % 64) : 0;
                WriteBE(output, destination, (uint)(bytes + padding) | (last && streamed ? 0x80000000u : 0u));
                WriteBE(output, destination + 4, (uint)count);
                destination += 8;
                for (int i = sampleOffset + frame * channels; i < sampleOffset + (frame + count) * channels; ++i)
                {
                    float sample = interleaved[i];
                    if (Single.IsNaN(sample) || Single.IsInfinity(sample) || sample < -1.0f || sample > 1.0f)
                        throw new ArgumentOutOfRangeException("interleaved", "Normalized finite samples are required");
                    short encoded = unchecked((short)(int)(sample * 32767.0f));
                    output[destination++] = (byte)(encoded >> 8); output[destination++] = (byte)encoded;
                }
                destination += padding;
                frame += count;
            }
        }

        // RVA 0x2cb50 uses the MSB-first writer at 0x2c6a0. The first
        // 32 bits are version:4, codec:4, channels-minus-one:6, rate:18.
        public static byte[] CreateHeader(int channels, int rate, int frames, bool streamed)
        { return CreateHeader(2, channels, rate, frames, streamed); }

        internal static byte[] CreateHeader(int codec, int channels, int rate, int frames, bool streamed)
        {
            if (channels < 1 || channels > 64 || rate < 0 || rate > 0x3ffff || frames < 0 || frames > 0x1fffffff)
                throw new ArgumentOutOfRangeException();
            byte[] result = new byte[8];
            WriteBE(result, 0, ((uint)codec << 24) | ((uint)(channels - 1) << 18) | (uint)rate);
            WriteBE(result, 4, (streamed ? 0x40000000u : 0u) | (uint)frames);
            return result;
        }

        // RVA 0x2dad1 divides 2040 by the binary32 encoder bitrate, then
        // multiplies by the rate. The CLR's x87 control word has 53-bit
        // precision (0x027f), not the reset 64-bit precision (0x037f).
        // Both operations therefore round to binary64 before truncation.
        // Force those stores even on legacy x86 JITs that retain temporaries
        // in extended registers: 400 Hz mono PCM yields 1019, 431 yields 1020.
        internal static int NativeBlockFrames(float bytesPerSecond, int rate)
        {
            double quotient = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(2040.0 / bytesPerSecond));
            double product = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(quotient * rate));
            return (int)product;
        }

        internal static void WriteBE(byte[] output, int at, uint value)
        {
            output[at] = (byte)(value >> 24); output[at + 1] = (byte)(value >> 16);
            output[at + 2] = (byte)(value >> 8); output[at + 3] = (byte)value;
        }
        private static int U16(byte[] bytes, int at) { return bytes[at] | (bytes[at + 1] << 8); }
        private static uint U32(byte[] bytes, int at)
        { return (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24)); }
        private static string Tag(byte[] bytes, int at) { return Encoding.ASCII.GetString(bytes, at, 4); }
    }
}
