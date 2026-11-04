using System;
using System.IO;
using System.Globalization;
using System.Xml;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder.AudioCompiler
{
    // AudioFile backend for the recovered integral WAVE/AIFF input
    // subset, including supported forward loops and filter 120 rate conversion. Unsupported codec/filter/input combinations are explicit.
    public sealed class PcmWaveAudioCompiler : IAudioFileCompiler
    {
        public AssetBuffer CompileAudioFile(InstanceDeclaration declaration, TargetPlatform platform)
        {
            AudioCatalog.RequireWin32(platform);
            XmlAttributeCollection attrs = declaration.XmlNode.Attributes;
            string file = attrs["File"].Value;
            const string prefix = "PC";
            int? compression = Compression(attrs[prefix + "Compression"]);
            string streamedKey = "IsStreamedOn" + prefix;
            bool? streamed = attrs[streamedKey] == null ? (bool?)null : Bool(attrs[streamedKey].Value);
            AudioEncodingPolicy policy = AudioEncodingPolicy.Select((int)platform, file, compression, streamed);
            byte[] wave;
            try { wave = File.ReadAllBytes(file); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            int loopStart, loopEnd;
            bool hasLoop = LoopPcmEncoder.TryReadWaveLoop(wave, out loopStart, out loopEnd);
            if (!hasLoop) hasLoop = AudioAiffLoopReader.TryRead(wave, out loopStart, out loopEnd);
            // Both container-specific readers validated loop metadata, including
            // accepted metadata that deliberately installs no forward loop.
            Pcm16Encoder.PcmWaveInput input = AudioPcmInputReader.Read(wave, true);
            XmlAttribute requested = attrs[prefix + "SampleRate"];
            if (requested != null)
            {
                int rate = ParseCInt(requested.Value);
                if (rate != input.SampleRate)
                {
                    if (rate < 400 || rate > 96000)
                        throw new BinaryAssetBuilderException(ErrorCode.InvalidArgument,
                            "Audio file {0}: Sample rate must be between 400 and 96000", declaration);
                    AudioResampling.Apply(ref input, rate, hasLoop, ref loopStart, ref loopEnd);
                }
            }
            // Quality is ignored for representations PCM(1) and XAS(29) by the
            // original. In particular, do not add a new [0,100] rejection here.
            int quality = policy.SampleRepresentation == 31 ? Quality(attrs["PCQuality"]) : 75;
            policy.ValidateQuality(quality);
            PcmEncodingResult encoded = policy.SampleRepresentation == 31 ?
                EaLayer3Encoder.EncodeSamples(input.Samples, input.Channels, input.SampleRate,
                    policy.Streamed, quality, hasLoop, loopStart, loopEnd) : hasLoop ?
                LoopPcmEncoder.EncodeSamples(input.Samples, input.Channels, input.SampleRate, policy.Streamed, loopStart, loopEnd, policy.SampleRepresentation == 29) :
                policy.SampleRepresentation == 1 ?
                Pcm16Encoder.EncodeSamples(input.Samples, input.Channels, input.SampleRate, policy.Streamed) :
                Xas1Encoder.EncodeSamples(input.Samples, input.Channels, input.SampleRate, policy.Streamed);
            PathMusicBuilder builder = new PathMusicBuilder(32);
            string subtitle = attrs["SubtitleStringName"] == null ?
                "DIALOGEVENT:" + Path.GetFileNameWithoutExtension(file) + "SubTitle" : attrs["SubtitleStringName"].Value;
            builder.String(4, subtitle);
            builder.Word(12, (uint)encoded.Frames);
            builder.Word(16, (uint)encoded.SampleRate);
            builder.Byte(28, (byte)encoded.Channels);
            if (policy.Streamed)
            {
                builder.Allocate(20, encoded.HeaderData);
                builder.Word(24, (uint)encoded.HeaderData.Length);
            }
            if (File.Exists(declaration.CustomDataPath)) File.Delete(declaration.CustomDataPath);
            File.WriteAllBytes(declaration.CustomDataPath, encoded.CustomData);
            return builder.Finish();
        }

        private static int Quality(XmlAttribute attr)
        {
            if (attr == null || attr.Value.Length == 0) return 75;
            string value = attr.Value;
            // jonwil's RA3 AudioFile marshaler uses decimal (including leading
            // zeroes), or the full 0xXXXXXXXX SageInt form, not C octal parsing.
            bool hex = value.Length == 10 && value[0] == '0' && value[1] == 'x';
            return Int32.Parse(hex ? value.Substring(2) : value,
                hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static int? Compression(XmlAttribute attr)
        {
            if (attr == null) return null;
            switch (attr.Value)
            {
                case "NONE": return 0;
                case "XAS": return 1;
                case "EALAYER3": return 2;
                default: throw new NotSupportedException("Unknown audio compression setting: " + attr.Value);
            }
        }

        private static bool Bool(string text)
        {
            return text.Length > 0 && (text[0] == '1' || text[0] == 't' || text[0] == 'T');
        }

        private static int ParseCInt(string text)
        {
            int at = 0;
            while (at < text.Length && (text[at] == ' ' || (text[at] >= '\t' && text[at] <= '\r'))) ++at;
            bool negative = at < text.Length && text[at] == '-';
            if (at < text.Length && (text[at] == '-' || text[at] == '+')) ++at;
            int radix = 10;
            if (at < text.Length && text[at] == '0')
            {
                radix = 8;
                if (at + 1 < text.Length && (text[at + 1] == 'x' || text[at + 1] == 'X')) { radix = 16; at += 2; }
            }
            uint value = 0, limit = negative ? 0x80000000u : 0x7fffffffu;
            for (; at < text.Length; ++at)
            {
                char c = text[at];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0 || digit >= radix) break;
                value = value > (limit - (uint)digit) / (uint)radix ? limit : value * (uint)radix + (uint)digit;
            }
            return unchecked((int)(negative ? 0u - value : value));
        }
    }
}
