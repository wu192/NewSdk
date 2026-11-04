using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Candidate = BinaryAssetBuilder.AudioCompiler;
using BeforeCodec = Baseline.AudioCompiler;

// Compile both revisions into the same host (Windows validation uses net472 x86). PCM creation, output
// comparison and explicit GC are outside the measured encoding interval.
internal static class AudioXasPerformance
{
    private sealed class Measurement
    {
        public string Case { get; set; }
        public string Revision { get; set; }
        public int Round { get; set; }
        public double Milliseconds { get; set; }
        public long AllocatedBytes { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
    }

    private static float[] Samples(int frames, int channels)
    {
        var samples = new float[checked(frames * channels)];
        uint state = 0x12345678;
        for (int i = 0; i < samples.Length; ++i) {
            state = unchecked(state * 1664525 + 1013904223);
            samples[i] = unchecked((short)(state >> 16)) / 32768.0f;
        }
        return samples;
    }

    private static object Encode(bool baseline, float[] samples, int channels, int rate,
        bool streamed, int loopStart, int loopEnd, bool xas)
    {
        if (baseline) {
            if (loopStart >= 0) return BeforeCodec.LoopPcmEncoder.EncodeSamples(samples, channels, rate, streamed, loopStart, loopEnd, xas);
            return xas ? BeforeCodec.Xas1Encoder.EncodeSamples(samples, channels, rate, streamed) :
                BeforeCodec.Pcm16Encoder.EncodeSamples(samples, channels, rate, streamed);
        }
        if (loopStart >= 0) return Candidate.LoopPcmEncoder.EncodeSamples(samples, channels, rate, streamed, loopStart, loopEnd, xas);
        return xas ? Candidate.Xas1Encoder.EncodeSamples(samples, channels, rate, streamed) :
            Candidate.Pcm16Encoder.EncodeSamples(samples, channels, rate, streamed);
    }

    private static void Equal(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length) throw new Exception("Encoded length differs");
        for (int i = 0; i < expected.Length; ++i)
            if (expected[i] != actual[i]) throw new Exception("Encoded byte differs at " + i);
    }

    private static void Equal(object oldResult, object newResult)
    {
        var before = (BeforeCodec.PcmEncodingResult)oldResult;
        var after = (Candidate.PcmEncodingResult)newResult;
        Equal(before.HeaderData, after.HeaderData); Equal(before.CustomData, after.CustomData);
        if (before.Frames != after.Frames || before.SampleRate != after.SampleRate || before.Channels != after.Channels)
            throw new Exception("Encoded metadata differs");
    }

    private static int CheckBoundaries()
    {
        int checks = 0;
        foreach (int channels in new[] { 1, 2, 4, 6 })
        foreach (int rate in new[] { 400, 431, 22050, 48000, 96000 }) {
            int maximum = Candidate.Pcm16Encoder.NativeBlockFrames(rate * channels * 0.59375f, rate);
            foreach (int frames in new[] { 1, 127, 128, 129, maximum - 1, maximum, maximum + 1, maximum * 2 + 129 }) {
                float[] samples = Samples(frames, channels);
                foreach (bool streamed in new[] { false, true })
                foreach (bool xas in new[] { false, true }) {
                    Equal(Encode(true, samples, channels, rate, streamed, -1, -1, xas),
                        Encode(false, samples, channels, rate, streamed, -1, -1, xas)); ++checks;
                    foreach (int start in new[] { 0, 1, 127, 128, 129, maximum - 1 }) {
                        if (start >= frames) continue;
                        int end = Math.Max(start, frames - 2);
                        Equal(Encode(true, samples, channels, rate, streamed, start, end, xas),
                            Encode(false, samples, channels, rate, streamed, start, end, xas)); ++checks;
                    }
                }
            }
        }
        return checks;
    }

    private static long AllocatedBytes()
    {
#if NETFRAMEWORK
        return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
#else
        return GC.GetAllocatedBytesForCurrentThread();
#endif
    }

    private sealed class BenchCase
    {
        internal string Name;
        internal int Seconds, Channels;
        internal bool Streamed, Loop;
        internal BenchCase(string name, int seconds, int channels, bool streamed, bool loop)
        { Name = name; Seconds = seconds; Channels = channels; Streamed = streamed; Loop = loop; }
    }

    private static object Measure(string name, bool baseline, int round, Func<object> encode, List<Measurement> rows)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var timer = new Stopwatch();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        long allocated = AllocatedBytes();
        timer.Start();
        object result = encode();
        timer.Stop();
        long allocatedBytes = AllocatedBytes() - allocated;
        var row = new Measurement { Case = name, Revision = baseline ? "before" : "after", Round = round,
            Milliseconds = timer.Elapsed.TotalMilliseconds,
            AllocatedBytes = allocatedBytes,
            Gen0 = GC.CollectionCount(0) - g0, Gen1 = GC.CollectionCount(1) - g1, Gen2 = GC.CollectionCount(2) - g2 };
        rows.Add(row);
        Console.WriteLine(string.Join(",", name, row.Revision, round,
            row.Milliseconds.ToString("F3", CultureInfo.InvariantCulture), row.AllocatedBytes, row.Gen0, row.Gen1, row.Gen2));
        return result;
    }

    private static int Main(string[] args)
    {
        try {
            if (args.Length != 2) throw new Exception("Usage: AudioXasPerformance output.csv baseline-commit");
#if NETFRAMEWORK
            AppDomain.MonitoringIsEnabled = true;
            Console.WriteLine("Allocation metric: AppDomain.MonitoringTotalAllocatedMemorySize");
#else
            Console.WriteLine("Allocation metric: GC.GetAllocatedBytesForCurrentThread");
#endif
            Console.WriteLine("Runtime: " + Environment.Version + "; pointer bits: " + (IntPtr.Size * 8));
            int boundaryComparisons = CheckBoundaries();
            Console.WriteLine("PASS before/after boundary comparisons: " + boundaryComparisons);
            var rows = new List<Measurement>();
            var cases = new[] {
                new BenchCase("stereo-30s-ram", 30, 2, false, false),
                new BenchCase("stereo-120s-ram", 120, 2, false, false),
                new BenchCase("stereo-120s-streamed", 120, 2, true, false),
                new BenchCase("stereo-120s-loop-ram", 120, 2, false, true),
                new BenchCase("stereo-120s-loop-streamed", 120, 2, true, true),
                new BenchCase("stereo-600s-streamed", 600, 2, true, false),
                new BenchCase("six-channel-30s-streamed", 30, 6, true, false)
            };
            Console.WriteLine("case,revision,round,milliseconds,allocatedBytes,gen0,gen1,gen2");
            foreach (var item in cases) {
                // Odd lengths exercise final padding; the loop discards seven
                // source frames and flushes an unaligned intro independently.
                int frames = item.Seconds * 48000 + 17;
                float[] samples = Samples(frames, item.Channels);
                int first = item.Loop ? 48000 + 129 : -1, last = item.Loop ? frames - 8 : -1;
                for (int round = 0; round < 3; ++round) {
                    object before, after;
                    if ((round & 1) == 0) {
                        before = Measure(item.Name, true, round, () => Encode(true, samples, item.Channels, 48000, item.Streamed, first, last, true), rows);
                        after = Measure(item.Name, false, round, () => Encode(false, samples, item.Channels, 48000, item.Streamed, first, last, true), rows);
                    } else {
                        after = Measure(item.Name, false, round, () => Encode(false, samples, item.Channels, 48000, item.Streamed, first, last, true), rows);
                        before = Measure(item.Name, true, round, () => Encode(true, samples, item.Channels, 48000, item.Streamed, first, last, true), rows);
                    }
                    Equal(before, after);
                }
            }
            using (var writer = new StreamWriter(args[0])) {
                writer.WriteLine("case,revision,round,milliseconds,allocatedBytes,gen0,gen1,gen2");
                foreach (var row in rows)
                    writer.WriteLine(string.Join(",", row.Case, row.Revision, row.Round,
                        row.Milliseconds.ToString("F3", CultureInfo.InvariantCulture), row.AllocatedBytes, row.Gen0, row.Gen1, row.Gen2));
            }
            Console.WriteLine("PASS long audio byte comparisons: " + (cases.Length * 3));
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
