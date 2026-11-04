using System;
using System.IO;

namespace BinaryAssetBuilder.AudioCompiler
{
    // Source: original Plugin.ProcessAudioFileInstance, RVA 0x5974.
    // These are SIMEX sample representation IDs, not SNR codec nibble values.
    public sealed class AudioEncodingPolicy
    {
        public readonly int SampleRepresentation;
        public readonly bool Streamed;
        public readonly bool CompressionWarning;

        private AudioEncodingPolicy(int representation, bool streamed, bool warning)
        {
            SampleRepresentation = representation;
            Streamed = streamed;
            CompressionWarning = warning;
        }

        public static AudioEncodingPolicy Select(int platform, string file,
            int? compression, bool? streamed)
        {
            if (platform != 0) throw new ArgumentOutOfRangeException("platform", "Audio compilation supports only Win32.");
            bool sounds = Path.GetDirectoryName(file).ToLower().EndsWith("\\sounds");
            if (compression.HasValue && compression.Value < 0)
                throw new ArgumentOutOfRangeException("compression");
            int representation;
            bool warning = false;
            int value = compression.HasValue ? compression.Value : -1;
            switch (value)
            {
                case -1: representation = sounds ? 29 : 1; break;
                case 0: representation = 1; break;
                case 1: representation = 29; break;
                case 2: representation = 31; break;
                default: throw new ArgumentOutOfRangeException("compression");
            }
            return new AudioEncodingPolicy(representation, streamed.HasValue ? streamed.Value : !sounds, warning);
        }

        // Supported PCM/XAS representations ignore quality, as in the original.
        public void ValidateQuality(int quality)
        {
            if (SampleRepresentation == 31 && (quality < 0 || quality > 100))
                throw new ArgumentOutOfRangeException("quality", "Quality parameter must be between 0 and 100");
        }

        // A redundant explicit rate bypasses the original resampling filter and
        // its range validation. Do not reject it earlier than the original.
        public static bool NeedsResampling(int originalRate, int? requestedRate)
        {
            if (!requestedRate.HasValue || requestedRate.Value == originalRate) return false;
            if (requestedRate.Value < 400 || requestedRate.Value > 96000)
                throw new ArgumentOutOfRangeException("requestedRate", "Sample rate must be between 400 and 96000");
            return true;
        }
    }
}
