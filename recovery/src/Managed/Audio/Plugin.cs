using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using BinaryAssetBuilder.Core;

namespace BinaryAssetBuilder.AudioCompiler
{
    public interface IAudioFileCompiler
    {
        AssetBuffer CompileAudioFile(InstanceDeclaration declaration, TargetPlatform platform);
    }

    // PathMusic business handlers and metadata are recovered. The original
    // Initialize's native RenderWare codec registry is deliberately not created:
    // AudioFile uses recovered PCM import/encoding; MPEG passthrough recovers
    // SIMEX identification and header-info behavior without decoding the payload.
    public class Plugin : IAssetBuilderPlugin, IDisposable
#if NEWSdk_MONOLITHIC
        , IInstanceProcessingHash
#endif
    {
        private readonly Tracer m_Tracer = Tracer.GetTracer("AudioCompiler", "Provides Audio processing functionality");
        private uint m_HashCode;
        private readonly IAudioFileCompiler audioCompiler;

        public Plugin() : this(new PcmWaveAudioCompiler()) { }
        public Plugin(IAudioFileCompiler compiler) { audioCompiler = compiler; }

        // The injected encoder may have shared state or external reads that are
        // outside the recovered PCM backend's parallel execution contract.
        public bool UsesRecoveredAudioFileCompiler
        {
            get { return audioCompiler != null && audioCompiler.GetType() == typeof(PcmWaveAudioCompiler); }
        }

        public virtual void Initialize(object configSection, TargetPlatform platform)
        {
            AudioCatalog.RequireWin32(platform);
            m_HashCode = HashProvider.GetTypeHash(GetType());
        }

        public virtual void ReInitialize(object configSection, TargetPlatform platform)
        {
            Initialize(configSection, platform);
        }

        public virtual uint GetAllTypesHash() { return AudioCatalog.AllTypesHash; }
        public virtual uint GetVersionNumber() { return AudioCatalog.Version(TargetPlatform.Win32); }
        public ExtendedTypeInformation GetPathMusicExtendedTypeInformation(uint typeId)
        {
            return AudioCatalog.PathMusicInformation(typeId, TargetPlatform.Win32);
        }
        public ExtendedTypeInformation GetAudioFileExtendedTypeInformation(uint typeId)
        {
            return AudioCatalog.AudioFileInformation(typeId, TargetPlatform.Win32);
        }
        public virtual ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            return AudioCatalog.TypeInformation(typeId, TargetPlatform.Win32);
        }

#if NEWSdk_MONOLITHIC
        public uint GetProcessingHash(InstanceDeclaration declaration)
        {
            if (declaration.Handle.TypeId == AudioCatalog.AudioFileTypeId)
            {
                XmlAttribute compression = declaration.XmlNode.Attributes["PCCompression"];
                if (compression != null && compression.Value == "EALAYER3")
                    return AudioCatalog.EaLayer3ProcessingHash;
            }
            return GetExtendedTypeInformation(declaration.Handle.TypeId).ProcessingHash;
        }

        public bool IsProcessingHashCurrent(uint typeId, uint processingHash, bool compilerVersionChanged)
        {
            // Cached declarations no longer retain their XML. On migration,
            // reload AudioFiles so NONE/XAS from the former type-wide hash and
            // original SDK EALayer3/PCM cannot masquerade as current instances.
            if (typeId == AudioCatalog.AudioFileTypeId && compilerVersionChanged) return false;
            return processingHash == GetExtendedTypeInformation(typeId).ProcessingHash ||
                (typeId == AudioCatalog.AudioFileTypeId && processingHash == AudioCatalog.EaLayer3ProcessingHash);
        }
#endif

        public virtual AssetBuffer ProcessInstance(InstanceDeclaration declaration)
        {
            switch (declaration.Handle.TypeId)
            {
                case AudioCatalog.AudioFileTypeId: return ProcessAudioFileInstance(declaration);
                case AudioCatalog.Mp3PassthroughTypeId: return ProcessAudioFileMP3PassthroughInstance(declaration);
                case AudioCatalog.PathMusicMapTypeId: return ProcessPathMusicMapInstance(declaration);
                case AudioCatalog.PathMusicTrackTypeId: return ProcessPathMusicTrackInstance(declaration);
                case AudioCatalog.PathMusicEventTypeId: return ProcessPathMusicEventInstance(declaration);
                default:
                    m_Tracer.TraceWarning("Couldn't process {0}. No matching handler found.", declaration);
                    return null;
            }
        }

        public AssetBuffer ProcessAudioFileInstance(InstanceDeclaration declaration)
        {
            if (audioCompiler == null)
                throw new NotSupportedException("AudioFile requires a configured encoding backend.");
            return audioCompiler.CompileAudioFile(declaration, TargetPlatform.Win32);
        }

        public AssetBuffer ProcessAudioFileMP3PassthroughInstance(InstanceDeclaration declaration)
        {
            XmlAttributeCollection attrs = declaration.XmlNode.Attributes;
            string file = attrs["File"].Value;
            byte[] data;
            try { data = File.ReadAllBytes(file); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            int first = MpegPassthrough.IsContainer(data) ? -1 : MpegPassthrough.FindFirstFrame(data);
            if (first < 0)
            {
                Console.WriteLine("Warning: Passthrough is for MP3 format only.  Cannot process \"{0}\"", file);
                return null;
            }
            byte channels;
            try { channels = MpegPassthrough.Validate(data, first); }
            catch (Exception exception)
            {
                if (!(exception is InvalidDataException) && !(exception is EndOfStreamException) &&
                    !(exception is IndexOutOfRangeException) && !(exception is ArgumentException)) throw;
                throw new BinaryAssetBuilderException((ErrorCode)1,
                    "Internal error reading element {0} of \"{1}\" (SIMEX_info(): {2}).", 0, file, "Error decoding MPEG Layer 123 data.");
            }
            PathMusicBuilder builder = new PathMusicBuilder(16);
            string subtitle = attrs["SubtitleStringName"] == null ?
                "DIALOGEVENT:" + Path.GetFileNameWithoutExtension(file) + "SubTitle" : attrs["SubtitleStringName"].Value;
            builder.String(4, subtitle);
            builder.Byte(12, channels);
            string streamed = attrs["IsStreamed"] == null ? "true" : attrs["IsStreamed"].Value;
            builder.Byte(13, (byte)(streamed.Length != 0 && (streamed[0] == '1' || streamed[0] == 't' || streamed[0] == 'T') ? 1 : 0));
            if (File.Exists(declaration.CustomDataPath)) File.Delete(declaration.CustomDataPath);
            File.Copy(file, declaration.CustomDataPath);
            return builder.Finish();
        }

        public AssetBuffer ProcessPathMusicMapInstance(InstanceDeclaration declaration)
        {
            PathMusicBuilder builder = new PathMusicBuilder(12);
            string file = declaration.XmlNode.Attributes["File"].Value;
            try
            {
                byte[] content = File.ReadAllBytes(file);
                builder.Word(8, (uint)content.Length);
                builder.Allocate(4, content);
            }
            catch (FileNotFoundException)
            {
                m_Tracer.TraceWarning("ProcessPathMusicMapInstance could not find the Map Data file: {0}", file);
            }
            catch (DirectoryNotFoundException)
            {
                m_Tracer.TraceWarning("ProcessPathMusicMapInstance could not find the Map Data file: {0}", file);
            }
            catch (UnauthorizedAccessException)
            {
                m_Tracer.TraceWarning("ProcessPathMusicMapInstance could not find the Map Data file: {0}", file);
            }
            catch (IOException)
            {
                m_Tracer.TraceWarning("ProcessPathMusicMapInstance could not find the Map Data file: {0}", file);
            }
            return builder.Finish();
        }

        public AssetBuffer ProcessPathMusicTrackInstance(InstanceDeclaration declaration)
        {
            XmlAttributeCollection attrs = declaration.XmlNode.Attributes;
            PathMusicBuilder builder = new PathMusicBuilder(28);
            if (attrs["File"] != null) builder.String(4, attrs["File"].Value);
            if (attrs["Type"] != null) builder.Word(12, attrs["Type"].Value == "OVERLAY_0" ? 1u : 0u);
            if (attrs["PathfinderTrackHeader"] != null) builder.String(16, attrs["PathfinderTrackHeader"].Value);
            string file = attrs["File"].Value;
            string output = declaration.CustomDataPath;
            if (File.Exists(output)) File.Delete(output);
            if (!File.Exists(file))
            {
                m_Tracer.TraceWarning("ProcessPathMusicTrackInstance could not find the Track Data file: {0}.  Creating an empty dummy file for CDATA.", file);
                // Dispose the stream promptly; the original leaks this handle,
                // which is not a deterministic serialization behavior.
                using (File.Create(output)) { }
            }
            else
            {
                m_Tracer.TraceNote("Creating output file {0}\n", output);
                File.Copy(file, output);
                File.SetAttributes(output, File.GetAttributes(output) & ~FileAttributes.ReadOnly);
            }
            string header = attrs["PathfinderTrackHeader"].Value;
            string name = attrs["id"].Value;
            bool opened;
            int handle = PathMusicHeader.Find(header, "PATH_TRACK_", name, out opened);
            if (!opened)
                m_Tracer.TraceWarning("Warning: ProcessPathMusicTrackInstance could not open Event Header file for verfication: {0}", header);
            if (!opened || handle == 0)
                m_Tracer.TraceWarning("ProcessPathMusicEventInstance could not find a handle for track: {0} in the header file: {1}", name, header);
            builder.Word(24, (uint)handle);
            return builder.Finish();
        }

        public AssetBuffer ProcessPathMusicEventInstance(InstanceDeclaration declaration)
        {
            XmlAttributeCollection attrs = declaration.XmlNode.Attributes;
            PathMusicBuilder builder = new PathMusicBuilder(16);
            string header = attrs["PathfinderEventHeader"].Value;
            string name = attrs["id"].Value;
            bool opened;
            int eventId = PathMusicHeader.Find(header, "PATH_EVENT_", name, out opened);
            if (!opened)
                m_Tracer.TraceWarning("Warning: ProcessPathMusicEventInstance could not open Event Header file for verfication: {0}", header);
            if (!opened || eventId == 0)
                m_Tracer.TraceWarning("ProcessPathMusicEventInstance could not find an ID for event: {0} in the header file: {1}", name, header);
            builder.Word(4, (uint)eventId);
            if (attrs["XferAlternateEvent"] != null)
            {
                int at = builder.Allocate(8, new byte[4]);
                builder.Word(at, PathMusicHeader.WeakHash(attrs["XferAlternateEvent"].Value));
            }
            string flag = attrs["IsXferable"] == null ? "true" : attrs["IsXferable"].Value;
            builder.Byte(12, (byte)(flag.Length != 0 && (flag[0] == '1' || flag[0] == 't' || flag[0] == 'T') ? 1 : 0));
            return builder.Finish();
        }

        public unsafe bool ParsePathMusicHeaderLine(sbyte* defineType, sbyte* line, sbyte* name, int* foundID)
        {
            int value = 0;
            bool found = PathMusicHeader.Parse(Marshal.PtrToStringAnsi((IntPtr)defineType),
                Marshal.PtrToStringAnsi((IntPtr)line), Marshal.PtrToStringAnsi((IntPtr)name), ref value);
            if (found) *foundID = value;
            return found;
        }

        protected virtual void Dispose(bool disposing) { }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        ~Plugin() { Dispose(false); }
    }
}

