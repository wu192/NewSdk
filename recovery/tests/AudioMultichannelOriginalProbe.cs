using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// One bounded original ProcessInstance invocation per process. This is evidence
// discovery, never candidate parity or SDK acceptance.
internal static class AudioMultichannelOriginalProbe
{
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
    private static string Data(byte[] bytes) { return bytes == null ? null : Convert.ToBase64String(bytes); }
    private static void Wave(string path, int channels, int frames, int bits, string signal)
    {
        int payloadBytes = channels * frames * (bits / 8);
        using (var file = File.Create(path)) using (var writer = new BinaryWriter(file)) {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + payloadBytes + (payloadBytes & 1));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)channels); writer.Write(22050); writer.Write(22050 * channels * (bits / 8));
            writer.Write((short)(channels * (bits / 8))); writer.Write((short)bits);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(channels * frames * (bits / 8));
            // Distinct channel signatures expose channel permutation, truncation,
            // and accidental mono output; bounded well below clipping.
            for (int frame = 0; frame < frames; ++frame) for (int channel = 0; channel < channels; ++channel)
            {
                short sample = (short)(signal == "constant" ? (channel + 1) * 2048 :
                    (frame == channel * 11 + 1 ? 12000 + channel * 512 : 0));
                if (bits == 8) writer.Write((byte)((sample >> 8) + 128));
                else if (bits == 16) writer.Write(sample);
                else { int value = (sample << 8) | ((frame + channel) & 255); writer.Write((byte)value); writer.Write((byte)(value >> 8)); writer.Write((byte)(value >> 16)); }
            }
            if ((payloadBytes & 1) != 0) writer.Write((byte)0);
        }
    }
    private static InstanceDeclaration Declaration(string path, string custom, string codec, bool streamed)
    {
        var xml = new XmlDocument();
        xml.LoadXml("<AudioFile xmlns='uri:ea.com:eala:asset' id='MultichannelProbe' File='" +
            System.Security.SecurityElement.Escape(path) + "' PCCompression='" + codec +
            "' PCQuality='75' IsStreamedOnPC='" + streamed.ToString().ToLowerInvariant() + "'/>");
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var document = new AssetDeclarationDocument();
        Type current = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(current);
        current.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        return new InstanceDeclaration(document) { XmlNode = xml.DocumentElement, CustomDataPath = custom };
    }
    private static int Main(string[] args)
    {
        SetErrorMode(0x0001 | 0x0002); // Suppress dialogs; parent records crashes/timeouts.
        if (args.Length != 9 || IntPtr.Size != 4) return 2;
        string assembly = Path.GetFullPath(args[0]), directory = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        int channels = Int32.Parse(args[3]), frames = Int32.Parse(args[4]), bits = Int32.Parse(args[7]); string codec = args[5], signal = args[8]; bool streamed = Boolean.Parse(args[6]);
        if ((channels != 1 && channels != 2 && channels != 4 && channels != 6 && channels != 8) || frames < 2 || frames > 4096 || (bits != 8 && bits != 16 && bits != 24) || (signal != "constant" && signal != "impulse") || (codec != "NONE" && codec != "XAS")) return 2;
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        string wave = Path.Combine(directory, "input.wav"), custom = Path.Combine(directory, "output.cdata");
        Wave(wave, channels, frames, bits, signal); if (File.Exists(custom)) File.Delete(custom);
        var result = new Dictionary<string, object> { { "channels", channels }, { "frames", frames }, { "codec", codec }, { "streamed", streamed }, { "platform", "Win32" }, { "bits", bits }, { "signal", signal } };
        IAssetBuilderPlugin plugin = null;
        try {
            plugin = (IAssetBuilderPlugin)Activator.CreateInstance(Assembly.LoadFrom(assembly).GetType("BinaryAssetBuilder.AudioCompiler.Plugin", true));
            plugin.Initialize(null, TargetPlatform.Win32);
            AssetBuffer buffer = plugin.ProcessInstance(Declaration(wave, custom, codec, streamed));
            result["status"] = buffer == null ? "NULL" : "BUFFER";
            if (buffer != null) {
                result["instance"] = Data(buffer.InstanceData); result["relocation"] = Data(buffer.RelocationData); result["imports"] = Data(buffer.ImportsData);
                if (buffer.InstanceData != null && buffer.InstanceData.Length >= 29) {
                    result["outputChannels"] = buffer.InstanceData[28];
                    result["outputFrames"] = BitConverter.ToUInt32(buffer.InstanceData, 12);
                    result["outputSampleRate"] = BitConverter.ToUInt32(buffer.InstanceData, 16);
                }
            }
        } catch (Exception error) {
            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
            result["status"] = "ERROR"; result["errorType"] = error.GetType().FullName; result["error"] = error.Message;
        } finally {
            result["customPresent"] = File.Exists(custom);
            result["custom"] = File.Exists(custom) ? Data(File.ReadAllBytes(custom)) : null;
            // Persist a managed outcome before native Dispose; parent separately
            // records a failing exit so a teardown crash cannot count as success.
            File.WriteAllText(output, new JavaScriptSerializer().Serialize(result));
            if (plugin != null) ((IDisposable)plugin).Dispose();
        }
        return 0;
    }
}
