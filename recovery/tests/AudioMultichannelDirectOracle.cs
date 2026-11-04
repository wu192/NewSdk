using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

// Reuse the full plugin matrix's ORIGINAL transcript and WAVE fixtures.
// Independently exercise public EncodeWave -> narrow ReadWave and EncodeSamples.
// No new native oracle invocation; this is component evidence, not SDK acceptance.
internal static class AudioMultichannelDirectOracle
{
    private static void Equal(string label, byte[] expected, byte[] actual)
    {
        if (actual == null || expected.Length != actual.Length)
            throw new Exception(label + " length mismatch");
        for (int i = 0; i < expected.Length; ++i)
            if (expected[i] != actual[i]) throw new Exception(label + " byte " + i);
    }
    private static object Field(object target, string name)
    { return target.GetType().GetField(name).GetValue(target); }
    private static void Result(string name, object result, byte[] header, byte[] custom, int channels, int frames)
    {
        Equal(name + " header", header, (byte[])Field(result, "HeaderData"));
        Equal(name + " custom", custom, (byte[])Field(result, "CustomData"));
        if ((int)Field(result, "Channels") != channels || (int)Field(result, "Frames") != frames ||
            (int)Field(result, "SampleRate") != 22050) throw new Exception(name + " metadata differs");
    }
    private static void Rejected(MethodInfo method, object[] arguments)
    {
        try { method.Invoke(null, arguments); }
        catch (TargetInvocationException error) {
            if (error.InnerException is ArgumentException || error.InnerException is NotSupportedException) return;
            throw;
        }
        throw new Exception("Invalid channels accepted by " + method.Name);
    }
    private static int Main(string[] args)
    {
        try {
            if (args.Length != 3 && args.Length != 4) throw new Exception("Usage: AudioMultichannelDirectOracle candidate.dll multichannel-original.txt fixture-directory [expected-cases]");
            int expectedCases = args.Length == 4 ? Int32.Parse(args[3]) : 64;
            Assembly library = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type pcm = library.GetType("BinaryAssetBuilder.AudioCompiler.Pcm16Encoder", true);
            MethodInfo read = pcm.GetMethod("ReadWave", BindingFlags.Static | BindingFlags.NonPublic);
            var pattern = new Regex(@"^multichannel-MultichannelC(4|6|8)B(8|16)WaveF(127|128|129|4096)LNoneR22050(NONE|XAS)(False|True)$");
            int records = 0, readerSamples = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (string line in File.ReadAllLines(args[1])) {
                string[] f = line.Split(':');
                Match match = pattern.Match(f[0]); if (!match.Success) continue;
                if (!seen.Add(f[0])) throw new Exception("Duplicate original record " + f[0]);
                if (f.Length != 9 || f[1] != "0" || f[2] != "dispatch" || f[3] != "BUFFER" || f[7] != "CUSTOM")
                    throw new Exception("Invalid original success record " + f[0]);
                int channels = Int32.Parse(match.Groups[1].Value), bits = Int32.Parse(match.Groups[2].Value);
                int frames = Int32.Parse(match.Groups[3].Value); bool streamed = Boolean.Parse(match.Groups[5].Value);
                string file = "multichannel-C" + channels + "B" + bits + "WaveF" + frames + "LNone.wav";
                byte[] wave = File.ReadAllBytes(Path.Combine(args[2], file));
                object input = read.Invoke(null, new object[] { wave });
                Type inputType = input.GetType();
                var hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                float[] samples = (float[])inputType.GetField("Samples", hidden).GetValue(input);
                if ((int)inputType.GetField("Channels", hidden).GetValue(input) != channels ||
                    (int)inputType.GetField("SampleRate", hidden).GetValue(input) != 22050 || samples.Length != frames * channels)
                    throw new Exception("ReadWave metadata differs");
                // The fixture has a standard16-byte fmt, so data starts at44.
                // Decode source bytes independently and check the confirmed inverse map.
                int[] map = channels == 8 ? new[] { 0,1,2,3,4,5,6,7 } : channels == 4 ? new[] { 0,1,3,2 } : new[] { 0,2,1,4,5,3 };
                for (int frame = 0; frame < frames; ++frame) for (int c = 0; c < channels; ++c) {
                    int at = 44 + (frame * channels + map[c]) * (bits / 8);
                    float expected = bits == 8 ? (wave[at] - 128) / 128.0f :
                        unchecked((short)(wave[at] | (wave[at + 1] << 8))) / 32768.0f;
                    if (samples[frame * channels + c] != expected) throw new Exception("ReadWave mapping " + f[0] + ":" + frame + ":" + c);
                    ++readerSamples;
                }
                byte[] original = Convert.FromBase64String(f[4]), custom = Convert.FromBase64String(f[8]);
                if (original.Length < 32) throw new Exception("Original AudioFile buffer too short");
                if (original[28] != channels || BitConverter.ToInt32(original,12) != frames || BitConverter.ToInt32(original,16) != 22050)
                    throw new Exception("Original runtime metadata differs");
                int pointer = BitConverter.ToInt32(original,20), size = BitConverter.ToInt32(original,24);
                if (pointer < 0 || size < 0 || (long)pointer + size > original.Length) throw new Exception("Original header bounds");
                byte[] header = new byte[size]; Buffer.BlockCopy(original,pointer,header,0,size);
                Type codec = library.GetType("BinaryAssetBuilder.AudioCompiler." + (match.Groups[4].Value == "NONE" ? "Pcm16Encoder" : "Xas1Encoder"),true);
                Result(f[0] + " EncodeWave",codec.GetMethod("EncodeWave").Invoke(null,new object[] {wave,streamed}),header,custom,channels,frames);
                Result(f[0] + " EncodeSamples",codec.GetMethod("EncodeSamples").Invoke(null,new object[] {samples,channels,22050,streamed}),header,custom,channels,frames);
                ++records;
            }
            if (records != expectedCases) throw new Exception("Expected " + expectedCases + " distinct original direct-entry cases; found " + records);
            int refusals = 0;
            foreach (int channels in new[] { 0,3,5,7,Int32.MaxValue }) {
                foreach (string name in new[] { "Pcm16Encoder", "Xas1Encoder" }) {
                    Type codec = library.GetType("BinaryAssetBuilder.AudioCompiler." + name,true);
                    Rejected(codec.GetMethod("EncodeSamples"),new object[] {new float[0],channels,22050,false}); ++refusals;
                }
                Rejected(library.GetType("BinaryAssetBuilder.AudioCompiler.Xas1Encoder",true).GetMethod("EncodeBlock128"),new object[] {new float[0],channels}); ++refusals;
            }
            Console.WriteLine("PASS " + records + " original transcript cases; " + (records * 2) + " direct encoder comparisons; " + readerSamples + " exact mapped reader samples;" + refusals + " invalid-channel refusals");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
