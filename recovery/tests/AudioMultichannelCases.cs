using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// WAVE-only 4/6-channel matrix and explicit eight-channel extension. Source channels have distinct DC, impulses,
// ramps, and alternating signs, so every encoder/loop/resampler sees ordering.
internal static class AudioMultichannelCases
{
    internal const int AssetCount = 1728;
    private sealed class Case { internal string Name, Xml; }
    internal static void Write(string path, int bits, int channels, int frames, bool extensible, int loopStart)
    {
        int width = bits / 8, payload = frames * channels * width, fmtSize = extensible ? 40 : 16;
        int size = 12 + 8 + fmtSize + 8 + payload + (payload & 1) + (loopStart >= 0 ? 68 : 0);
        using (var file = File.Create(path)) using (var b = new BinaryWriter(file)) {
            b.Write(Encoding.ASCII.GetBytes("RIFF")); b.Write(size - 8); b.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            b.Write(fmtSize); b.Write((ushort)(extensible ? 0xfffe : 1)); b.Write((ushort)channels); b.Write(22050);
            b.Write(22050 * channels * width); b.Write((ushort)(channels * width)); b.Write((ushort)bits);
            if (extensible) {
                b.Write((ushort)22); b.Write((ushort)bits); b.Write(channels == 8 ? 0x63fu : channels == 4 ? 0x33u : 0x3fu);
                b.Write(new byte[] { 1,0,0,0,0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71 });
            }
            b.Write(Encoding.ASCII.GetBytes("data")); b.Write(payload);
            for (int frame = 0; frame < frames; ++frame) for (int channel = 0; channel < channels; ++channel) {
                int sample = (channel + 1) * 1700 + ((frame * (channel * 7 + 13)) % 8191) - 4096;
                if (frame % (29 + channel * 5) == channel) sample = -14000 + channel * 1100;
                if ((frame / 31) % 2 != 0) sample = -sample;
                if (bits == 8) b.Write((byte)((sample >> 8) + 128));
                else if (bits == 16) b.Write((short)sample);
                else { int value = (sample << 8) | ((frame * 17 + channel * 37) & 255); b.Write((byte)value); b.Write((byte)(value >> 8)); b.Write((byte)(value >> 16)); }
            }
            if ((payload & 1) != 0) b.Write((byte)0);
            if (loopStart >= 0) {
                b.Write(Encoding.ASCII.GetBytes("smpl")); b.Write(60);
                b.Write(0); b.Write(0); b.Write(1000000000 / 22050); b.Write(60); b.Write(0); b.Write(0); b.Write(0);
                b.Write(1); b.Write(0); b.Write(0); b.Write(0); b.Write(loopStart); b.Write(frames - 1); b.Write(0); b.Write(0);
            }
        }
    }
    private static IEnumerable<Case> Cases(string root, int[] channelCounts)
    {
        foreach (int channels in channelCounts) foreach (int bits in new[] { 8, 16, 24 })
        foreach (bool extensible in new[] { false, true }) foreach (int frames in new[] { 127, 128, 129, 4096 })
        foreach (int start in frames == 4096 ? new[] { -1, 0, 129 } : new[] { -1, 0 }) {
            string stem = "C" + channels + "B" + bits + (extensible ? "Ext" : "Wave") + "F" + frames + "L" + (start < 0 ? "None" : start.ToString());
            string file = Path.Combine(root, "multichannel-" + stem + ".wav"); Write(file, bits, channels, frames, extensible, start);
            foreach (int target in new[] { 22050, 11025, 44100, 8000 })
            foreach (string codec in new[] { "NONE", "XAS" }) foreach (bool streamed in new[] { false, true }) {
                string name = "Multichannel" + stem + "R" + target + codec + streamed;
                string xml = "<AudioFile xmlns='uri:ea.com:eala:asset' id='" + name + "' File='" +
                    System.Security.SecurityElement.Escape(file) + "' PCCompression='" + codec +
                    "' PCQuality='75' PCSampleRate='" + target + "' IsStreamedOnPC='" + streamed.ToString().ToLowerInvariant() + "'/>";
                yield return new Case { Name = name, Xml = xml };
            }
        }
    }
    internal static void Run(string root, Action<string,int,string,bool> run)
    { Run(root, run, new[] { 4, 6 }, AssetCount); }
    internal static void RunEight(string root, Action<string,int,string,bool> run)
    { Run(root, run, new[] { 8 }, 864); }
    internal static void RunNewWorld(string root, Action<string,int,string,bool> run)
    {
        string file = Environment.GetEnvironmentVariable("AUDIO_NEW_WORLD_FILE");
        if (String.IsNullOrEmpty(file) || !File.Exists(file)) throw new Exception("Original New World WAVE fixture is required");
        foreach (string codec in new[] { "NONE", "XAS" }) foreach (bool streamed in new[] { false, true }) {
            string name = "NewWorldC8B16" + codec + streamed;
            string xml = "<AudioFile xmlns='uri:ea.com:eala:asset' id='FX_kanade_spell' File='" +
                System.Security.SecurityElement.Escape(file) + "' GUIPreset='Default' PCCompression='" + codec +
                "' IsStreamedOnPC='" + streamed.ToString().ToLowerInvariant() + "'/>";
            run("multichannel-" + name, 0, xml, false);
        }
    }
    private static void Run(string root, Action<string,int,string,bool> run, int[] channelCounts, int expected)
    {
        int count = 0;
        foreach (Case item in Cases(root, channelCounts)) { run("multichannel-" + item.Name, 0, item.Xml, false); ++count; }
        if (count != expected) throw new Exception("Incomplete multichannel matrix: " + count);
    }
    internal static IEnumerable<string> WriteSdkCases(string root)
    {
        int count = 0;
        foreach (Case item in Cases(root, new[] { 4, 6 })) { ++count; yield return item.Xml; }
        if (count != AssetCount) throw new Exception("Incomplete multichannel SDK matrix: " + count);
    }
}
