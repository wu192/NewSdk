using System;
using System.Runtime.CompilerServices;

namespace BinaryAssetBuilder.AudioCompiler
{
    // Filter 120 integration for the integral PCM importer. The original
    // SSOUND stores signed16 planar channels; normalized interleaved samples
    // are only the surrounding managed representation.
    internal static class AudioResampling
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float F(float value) { return value; }

        internal static void Apply(ref Pcm16Encoder.PcmWaveInput input, int destinationRate,
            bool hasLoop, ref int loopStart, ref int loopEnd)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (input.Samples == null) throw new ArgumentException("Missing PCM samples", "input");
            if ((input.Channels != 1 && input.Channels != 2 && input.Channels != 4 && input.Channels != 6 && input.Channels != 8) || input.Samples.Length % input.Channels != 0)
                throw new ArgumentException("1/2/4/6/8-channel interleaved PCM is required", "input");
            int sourceFrames = input.Samples.Length / input.Channels;
            // Original 0x404e0 succeeds without reading/writing any samples
            // for start0,count1. The caller's malloc payload is uninitialized.
            // Do not emulate its non-deterministic heap contents.
            if (sourceFrames == 1)
                throw new NotSupportedException("Single-frame SIMEX source import leaves samples uninitialized; rate conversion requires at least two source frames");
            if (sourceFrames == 0 || sourceFrames > 0x1fffffff)
                throw new ArgumentOutOfRangeException("input", "Unsupported PCM frame count");
            if (input.SampleRate <= 0 || input.SampleRate > 0x3ffff)
                throw new ArgumentOutOfRangeException("input", "Invalid source sample rate");
            // Keep the established importer/encoder loop range boundary.
            // In particular, do not allow the resampler's end clamp to repair
            // an out-of-range loop from a previously rejected source file.
            if (hasLoop && (loopStart < 0 || loopEnd < loopStart || loopEnd >= sourceFrames))
                throw new NotSupportedException("Loop points must describe an inclusive range within the audio source");

            // The managed original bypasses filter120 for an unchanged rate,
            // even when that source rate lies outside its changed-rate limits.
            if (destinationRate == input.SampleRate) return;
            ResamplerScheduler.Profile profile = ResamplerScheduler.CreateProfile(input.SampleRate, destinationRate);
            if (ResamplerScheduler.IsRateOnly(sourceFrames, profile))
            {
                input = new Pcm16Encoder.PcmWaveInput
                { Samples = input.Samples, Channels = input.Channels, SampleRate = destinationRate };
                return;
            }

            int destinationFrames = ResamplerScheduler.OutputFrameCount(sourceFrames, profile.Ratio);
            if (destinationFrames == 0 || destinationFrames > 0x1fffffff)
                throw new NotSupportedException("Unsupported resampled PCM frame count");
            float[] interleaved = new float[checked(destinationFrames * input.Channels)];
            for (int channel = 0; channel < input.Channels; ++channel)
            {
                short[] source = new short[sourceFrames];
                for (int frame = 0; frame < sourceFrames; ++frame)
                {
                    float sample = input.Samples[frame * input.Channels + channel];
                    // PCM8/16 and the recovered PCM24 importer produce exact
                    // signed16 / 32768 values. Never silently truncate another
                    // decoder's float samples into this established subset.
                    double scaled = (double)sample * 32768.0;
                    if (Single.IsNaN(sample) || Single.IsInfinity(sample) ||
                        scaled < -32768.0 || scaled > 32767.0 || scaled != Math.Truncate(scaled))
                        throw new NotSupportedException("Resampling requires exact signed16 PCM importer samples");
                    source[frame] = (short)scaled;
                }
                short[] result = ResamplerScheduler.Resample(source, input.SampleRate, destinationRate);
                if (result.Length != destinationFrames)
                    throw new InvalidOperationException("Resampler kernel and filter callback frame counts differ");
                for (int frame = 0; frame < destinationFrames; ++frame)
                    interleaved[frame * input.Channels + channel] = F(result[frame] * (1.0f / 32768.0f));
            }
            int start = loopStart, end = loopEnd;
            if (hasLoop)
            {
                // End zero is intentionally not rescaled. Only the end is
                // clamped; an invalid resulting start is never repaired.
                ResamplerScheduler.RescaleLoop(destinationFrames, profile.Ratio, ref start, ref end);
                if (start < 0 || end < start || end >= destinationFrames)
                    throw new NotSupportedException("Resampled loop points must describe an inclusive range within the audio source");
            }
            input = new Pcm16Encoder.PcmWaveInput
            { Samples = interleaved, Channels = input.Channels, SampleRate = destinationRate };
            if (hasLoop) { loopStart = start; loopEnd = end; }
        }
    }
}
