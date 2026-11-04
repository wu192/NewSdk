using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder.AudioCompiler
{
    // Recovered from original Plugin methods 0x0600011D-0x06000121.
    // Hashes are explicit original metadata; they are not recomputed from a new assembly.
    public static class AudioCatalog
    {
        public const uint AudioFileTypeId = 376113229u;
        public const uint Mp3PassthroughTypeId = 364756738u;
        public const uint PathMusicMapTypeId = 2516466693u;
        public const uint PathMusicEventTypeId = 2590317961u;
        public const uint PathMusicTrackTypeId = 2802565590u;
        public const uint AllTypesHash = 1424942948u;
        public const uint LegacyAudioFileProcessingHash = 2414318214u;
        public const uint EaLayer3ProcessingHash = 2414318215u;

        internal static void RequireWin32(TargetPlatform platform)
        {
            if (platform != TargetPlatform.Win32)
                throw new System.NotSupportedException("Audio compilation supports only Win32.");
        }

        public static uint Version(TargetPlatform platform)
        {
            RequireWin32(platform);
#if NEWSdk_MONOLITHIC
            // Sessions from the former type-wide hash cannot identify which
            // stored AudioFiles used EALayer3. Reload them once before accepting
            // either of the two per-instance hashes below.
            return 548405254u;
#else
            return 548405253u;
#endif
        }

        public static ExtendedTypeInformation PathMusicInformation(uint typeId, TargetPlatform platform)
        {
            RequireWin32(platform);
            ExtendedTypeInformation info = new ExtendedTypeInformation();
            info.TypeId = typeId;
            switch (typeId)
            {
                case PathMusicTrackTypeId:
                    info.TypeName = "PathMusicTrack";
                    info.TypeHash = 2668810035u;
                    info.ProcessingHash = 2668810042u;
                    info.HasCustomData = true;
                    break;
                case PathMusicEventTypeId:
                    info.TypeName = "PathMusicEvent";
                    info.TypeHash = 1993395951u;
                    info.ProcessingHash = 1993395942u;
                    break;
                case PathMusicMapTypeId:
                    info.TypeName = "PathMusicMap";
                    info.TypeHash = 3122351307u;
                    info.ProcessingHash = 290548117u;
                    break;
            }
            return info;
        }

        public static ExtendedTypeInformation AudioFileInformation(uint typeId, TargetPlatform platform)
        {
            RequireWin32(platform);
            ExtendedTypeInformation info = new ExtendedTypeInformation();
            info.TypeId = typeId;
            switch (typeId)
            {
                case AudioFileTypeId:
                    info.TypeName = "AudioFile";
                    info.TypeHash = 1405623879u;
                    info.HasCustomData = true;
#if NEWSdk_MONOLITHIC
                    info.ProcessingHash = LegacyAudioFileProcessingHash;
#else
                    // Original BAB has no per-instance processing-hash hook.
                    info.ProcessingHash = EaLayer3ProcessingHash;
#endif
                    break;
                case Mp3PassthroughTypeId:
                    info.TypeName = "AudioFileMP3Passthrough";
                    info.TypeHash = 2457571466u;
                    info.HasCustomData = true;
                    info.ProcessingHash = 2822637465u;
                    break;
            }
            return info;
        }

        public static ExtendedTypeInformation TypeInformation(uint typeId, TargetPlatform platform)
        {
            RequireWin32(platform);
            switch (typeId)
            {
                case AudioFileTypeId:
                case Mp3PassthroughTypeId: return AudioFileInformation(typeId, platform);
                case PathMusicMapTypeId:
                case PathMusicTrackTypeId:
                case PathMusicEventTypeId: return PathMusicInformation(typeId, platform);
                // Original dispatcher returns a zero-filled object, including TypeId=0.
                default: return new ExtendedTypeInformation();
            }
        }
    }
}

