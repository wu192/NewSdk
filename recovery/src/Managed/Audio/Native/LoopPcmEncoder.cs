using System;
using System.IO;

namespace BinaryAssetBuilder.AudioCompiler
{
    // SNR loop segmentation recovered from the original format39 writer:
    // RVA 0x2e3c2 splits RAM/streamed PCM and XAS at loopStart, flushes
    // each segment, and discards source frames beyond the inclusive end.
    internal static class LoopPcmEncoder
    {
        internal static bool TryReadWaveLoop(byte[] wave, out int start, out int end)
        {
            start = end = -1;
            if (wave == null) throw new ArgumentNullException("wave");
            if (wave.Length < 12 || !Tag(wave, 0, "RIFF") || !Tag(wave, 8, "WAVE"))
                return false;
            long riffEnd = 8L + ReadLE(wave, 4);
            if (riffEnd < 12 || riffEnd > wave.Length)
                throw new InvalidDataException("Truncated RIFF data");
            // Native 0x36ed0's lookup skips exactly chunkSize+8, without
            // RIFF odd-byte padding. A smpl after odd-sized data is therefore
            // invisible to that lookup. Keep structural validation separate.
            int selectedSampler = FindWaveSampler(wave, riffEnd);
            bool samplerSeen = false;
            for (long cursor = 12; cursor < riffEnd; )
            {
                if (cursor + 8 > riffEnd)
                    throw new InvalidDataException("Truncated RIFF chunk header");
                int at = checked((int)cursor);
                uint size = ReadLE(wave, at + 4);
                long next = cursor + 8 + size;
                if (next > riffEnd)
                    throw new InvalidDataException("Truncated RIFF chunk");
                if (at == selectedSampler)
                {
                    // The native chunk lookup returns the first smpl only.
                    samplerSeen = true;
                    if (size < 36)
                        throw new InvalidDataException("Truncated WAVE sampler header");
                    uint loops = ReadLE(wave, at + 8 + 28);
                    if (loops > 0)
                    {
                        if (size < 60)
                            throw new InvalidDataException("Truncated WAVE loop record");
                        int record = at + 8 + 36;
                        // Native 0x37447..0x37483 ignores type, fraction, count
                        // and later records: all types use the first range as
                        // the existing forward runtime loop, not pingpong playback.
                        uint first = ReadLE(wave, record + 8), last = ReadLE(wave, record + 12);
                        if (first > int.MaxValue || last > int.MaxValue || last < first)
                            throw new NotSupportedException("Invalid WAVE loop points");
                        start = (int)first;
                        end = (int)last;
                    }
                }
                cursor = next + (size & 1u);
                if (cursor > riffEnd)
                    throw new InvalidDataException("Missing RIFF chunk padding");
            }
            if (selectedSampler >= 0 && !samplerSeen)
                throw new NotSupportedException("Misaligned native WAVE sampler lookup has no bounded chunk contract");
            return start >= 0;
        }

        private static int FindWaveSampler(byte[] wave, long riffEnd)
        {
            for (long cursor = 12; cursor + 8 <= riffEnd; )
            {
                int at = checked((int)cursor);
                if (Tag(wave, at, "smpl")) return at;
                cursor += 8L + ReadLE(wave, at + 4);
            }
            return -1;
        }

        internal static PcmEncodingResult EncodeSamples(float[] samples, int channels,
            int sampleRate, bool streamed, int loopStart, int loopEnd, bool xas)
        {
            if (samples == null) throw new ArgumentNullException("samples");
            if ((channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8) || samples.Length % channels != 0)
                throw new ArgumentOutOfRangeException("channels");
            int sourceFrames = samples.Length / channels;
            if (loopStart < 0 || loopEnd < loopStart || loopEnd >= sourceFrames)
                throw new NotSupportedException("Loop points must describe an inclusive range within the audio source");
            if (sourceFrames > 0x1fffffff)
                throw new ArgumentOutOfRangeException("samples");
            if (xas) Xas1Encoder.ValidateSamples(samples, (loopEnd + 1) * channels);
            else if (sampleRate <= 0 || sampleRate > 0x3ffff) throw new ArgumentOutOfRangeException("sampleRate");
            int maximum = streamed ? Pcm16Encoder.NativeBlockFrames(
                sampleRate * channels * (xas ? 0.59375f : 2.0f), sampleRate) : loopEnd + 1;
            int loopFrames = loopEnd - loopStart + 1;
            int loopOffset = loopStart == 0 ? 0 : SegmentLength(loopStart, channels, maximum, streamed, xas);
            int dataLength = checked(loopOffset + SegmentLength(loopFrames, channels, maximum, streamed, xas));

            // Original header writer 0x2cc5c..0x2ccc1:
            // loop flag at bit29, inclusive loop end+1 in low29 bits,
            // loopStart BE32, and streamed intro byte count BE32.
            byte[] header = new byte[streamed ? 16 : 12];
            byte[] prefix = Pcm16Encoder.CreateHeader(xas ? 4 : 2,
                channels, sampleRate, loopEnd + 1, streamed);
            Buffer.BlockCopy(prefix, 0, header, 0, prefix.Length);
            header[4] |= 0x20;
            WriteBE(header, 8, loopStart);
            if (streamed) WriteBE(header, 12, loopOffset);

            int dataOffset = streamed ? 0 : header.Length;
            byte[] custom = new byte[checked(dataOffset + dataLength)];
            if (!streamed) Buffer.BlockCopy(header, 0, custom, 0, header.Length);
            float[] source = xas ? new float[30] : null;
            if (loopStart > 0)
                EncodeSegment(samples, 0, loopStart, channels, maximum, streamed, custom, dataOffset, xas, source);
            EncodeSegment(samples, loopStart * channels, loopFrames, channels, maximum,
                streamed, custom, dataOffset + loopOffset, xas, source);
            // The plugin obtains the source frame count before writing SNR.
            // Trailing samples are absent from SNR but remain in the runtime.
            return new PcmEncodingResult(streamed ? header : new byte[0], custom,
                sampleRate, channels, sourceFrames);
        }

        private static int SegmentLength(int frames, int channels, int maximum, bool streamed, bool xas)
        {
            return xas ? Xas1Encoder.SegmentLength(frames, channels, maximum, streamed) :
                Pcm16Encoder.SegmentLength(frames, channels, maximum, streamed);
        }

        private static void EncodeSegment(float[] samples, int sampleOffset, int frames, int channels,
            int maximum, bool streamed, byte[] output, int outputOffset, bool xas, float[] source)
        {
            if (xas) Xas1Encoder.EncodeSegment(samples, sampleOffset, frames, channels, maximum, streamed, output, outputOffset, source);
            else Pcm16Encoder.EncodeSegment(samples, sampleOffset, frames, channels, maximum, streamed, output, outputOffset);
        }

        private static void WriteBE(byte[] destination, int at, int value)
        {
            destination[at] = (byte)(value >> 24);
            destination[at + 1] = (byte)(value >> 16);
            destination[at + 2] = (byte)(value >> 8);
            destination[at + 3] = (byte)value;
        }

        private static bool Tag(byte[] bytes, int at, string value)
        {
            return bytes[at] == value[0] && bytes[at + 1] == value[1] &&
                bytes[at + 2] == value[2] && bytes[at + 3] == value[3];
        }

        private static uint ReadLE(byte[] bytes, int at)
        {
            return (uint)(bytes[at] | (bytes[at + 1] << 8) |
                (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
        }
    }
}
