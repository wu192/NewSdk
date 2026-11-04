#if NEWSdk_MONOLITHIC
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using BinaryAssetBuilder.Core;
using BinaryAssetBuilder.AudioCompiler;
using Recovered.W3x;

namespace NewModCompiler.Application
{
    // This is a backend input, not a serialized replacement for Core's semantic
    // front end. Prepare runs on the owner after schema/reference processing.
    // Certified backends only read the finalized XML, namespace bindings and
    // type identity. The document version retains the finalized XML through IO;
    // each operation has its own declaration state and CDATA pathname.
    internal sealed partial class PreparedBackendWork : IDisposable
    {
        readonly IAssetBuilderPlugin plugin;
        readonly InstanceDeclaration declaration;
        readonly List<FileStream> inputLeases;
        readonly List<TextureMessage> textureMessages;
        bool disposed;

        public string Family { get; private set; }
        public long PreparedBytes { get; private set; }
        public long ExternalReadBytes { get; private set; }
        public InstanceDeclaration Declaration { get { return declaration; } }

        PreparedBackendWork(string family, IAssetBuilderPlugin plugin, InstanceDeclaration declaration,
            List<FileStream> inputLeases, long preparedBytes)
        {
            Family = family; this.plugin = plugin; this.declaration = declaration;
            this.inputLeases = inputLeases;
            if (family == "texture") textureMessages = new List<TextureMessage>();
            PreparedBytes = preparedBytes;
            if (inputLeases != null)
                foreach (FileStream input in inputLeases) ExternalReadBytes = checked(ExternalReadBytes + input.Length);
        }

        public static bool TryPrepare(InstanceDeclaration original, IAssetBuilderPlugin plugin,
            out PreparedBackendWork prepared)
        {
            return TryPrepareWithinBudget(original, plugin, 128L * 1024L * 1024L, out prepared);
        }

        public static bool TryPrepareWithinBudget(InstanceDeclaration original, IAssetBuilderPlugin plugin,
            long preparationBudget, out PreparedBackendWork prepared)
        {
            string reason;
            return TryPrepareWithReason(original, plugin, preparationBudget, out prepared, out reason);
        }

        // The reason records the first actual gate that refused preparation.
        // It describes neither a cache lookup nor a backend execution failure;
        // exceptions retain their original propagation and compatibility path.
        public static bool TryPrepareWithReason(InstanceDeclaration original, IAssetBuilderPlugin plugin,
            long preparationBudget, out PreparedBackendWork prepared, out string reason)
        {
            long estimatedBytes;
            return TryPrepareForScheduling(original, plugin, preparationBudget, out prepared, out reason, out estimatedBytes);
        }

        // Preserve the estimate when parallel preparation refuses this input so
        // owner compatibility can apply the same memory admission without a
        // second XML walk or another external-input probe.
        internal static bool TryPrepareForScheduling(InstanceDeclaration original, IAssetBuilderPlugin plugin,
            long preparationBudget, out PreparedBackendWork prepared, out string reason, out long estimatedBytes)
        {
            prepared = null;
            reason = null;
            estimatedBytes = 0;
            string family;
            if (!Eligibility(original, plugin, out family, out reason)) return false;

            long nodes = 0;
            long preparationEstimate = EstimatePreparationBytes(original, ref nodes);
            estimatedBytes = preparationEstimate;
            // Bound the bridge's native input, scratch and output allocation.
            // Sharing the DOM does not eliminate that per-call working set.
            if (preparationEstimate > preparationBudget) return Reject("preparation-budget-exceeded", out reason);

            var leases = family == "audio" || family == "texture" || (family == "xml" && IsApt(original.Handle.TypeId)) ? new List<FileStream>() : null;
            try
            {
                // Only the certified backend's actual reopened files require
                // leases. No action identity, serialization or file digest is built.
                if (family == "xml" && !PrepareXmlReads(original, leases, out reason)) return false;
                if (family == "audio" && !PrepareAudio(original, leases, out reason)) return false;
                long retained = preparationEstimate;
                if (family == "texture")
                {
                    bool accepted = PrepareTexture(original, leases, retained, out retained, out reason);
                    estimatedBytes = retained;
                    if (!accepted) return false;
                }
                else if (family == "w3x") retained = W3xBudget(original.Node, nodes, retained);
                else if (family == "xml") retained = ExternalReadBudget(leases, retained, 4L);
                // Audio keeps per-call input, PCM and encoded buffers. Charge
                // four input copies instead of the old 64-copy allowance.
                else if (family == "audio") retained = ExternalReadBudget(leases, retained, 4L);
                estimatedBytes = retained;
                if (retained > preparationBudget) return Reject("preparation-budget-exceeded", out reason);
                // The version is read-only before publication. A private state
                // view captures document metadata and the CDATA pathname while
                // borrowing the original schema-annotated node and references.
                // This avoids a full DOM copy and preserves inherited context.
                var input = original.CreateBackendView();
                prepared = new PreparedBackendWork(family, plugin, input, leases, retained);
                leases = null; // Ownership moves to the envelope through commit.
                return true;
            }
            finally
            {
                if (leases != null) foreach (FileStream lease in leases) lease.Dispose();
            }
        }

        static bool Eligibility(InstanceDeclaration original, IAssetBuilderPlugin plugin, out string family, out string reason)
        {
            family = null;
            reason = null;
            if (original == null || original.Node == null || original.Document == null || plugin == null)
                return Reject("invalid-input", out reason);
            // Exact built-in classes only: a subclass may introduce untracked
            // reads, metadata writes or registrations even for an identical type.
            Type implementation = plugin.GetType();
            if (implementation == typeof(Recovered.XmlPriority.Plugin))
            {
                family = "xml";
                if (!IsParallelXml(original.Handle.TypeId)) return Reject("uncertified-xml-type", out reason);
            }
            else if (implementation == typeof(Recovered.Compiler.W3x.NativePlugin))
            {
                family = "w3x";
                uint type = original.Handle.TypeId;
                if (type != W3xCatalog.AnimationTypeId && type != W3xCatalog.MeshTypeId)
                    return Reject("uncertified-w3x-type", out reason);
            }
            else if (implementation == typeof(BinaryAssetBuilder.AudioCompiler.Plugin))
            {
                family = "audio";
                uint type = original.Handle.TypeId;
                if (type == AudioCatalog.AudioFileTypeId)
                {
                    if (!((BinaryAssetBuilder.AudioCompiler.Plugin)plugin).UsesRecoveredAudioFileCompiler)
                        return Reject("uncertified-audio-compiler", out reason);
                }
                else if (type != AudioCatalog.Mp3PassthroughTypeId)
                    return Reject("uncertified-audio-type", out reason);
            }
            else if (implementation == typeof(Recovered.GameObject.Plugin))
            {
                family = "gameobject";
                if (original.Handle.TypeId != 2486173485u) return Reject("uncertified-tokenizer-type", out reason);
            }
            else if (implementation == typeof(Recovered.Compiler.Texture.Plugin))
            {
                family = "texture";
                uint type = original.Handle.TypeId;
                if (type != Recovered.Compiler.Texture.TextureCatalog.TextureTypeId &&
                    type != Recovered.Compiler.Texture.TextureCatalog.OnDemandTextureTypeId &&
                    type != Recovered.Compiler.Texture.TextureCatalog.TerrainTextureAtlasTypeId)
                    return Reject("uncertified-texture-type", out reason);
            }
            else if (implementation == typeof(Recovered.Weapon.Plugin))
            {
                family = "weapon";
                if (original.Handle.TypeId != 2496977262u) return Reject("uncertified-tokenizer-type", out reason);
            }
            else if (implementation == typeof(Recovered.Compiler.Plugin))
            {
                family = "armor";
                if (original.Handle.TypeId != 980180622u) return Reject("uncertified-tokenizer-type", out reason);
            }
            else return Reject("uncertified-plugin", out reason);

            return true;
        }

        public static long EstimatePreparationBytes(InstanceDeclaration original)
        {
            long nodes = 0;
            return EstimatePreparationBytes(original, ref nodes);
        }

        static long EstimatePreparationBytes(InstanceDeclaration original, ref long nodes)
        {
            if (original == null || original.Node == null) return 0;
            // The bridges still marshal strings, maps, vectors and output buffers.
            // Tokenized modules additionally emit data and procedure trees.
            uint type = original.Handle.TypeId;
            long multiplier = type == 2486173485u || type == 2496977262u || type == 980180622u ? 4L : 1L;
            try { return checked(4096L + EstimateNode(original.Node, ref nodes) * multiplier); }
            catch (OverflowException) { return long.MaxValue; }
        }

        static long EstimateNode(XmlNode node, ref long nodes)
        {
            if (node.NodeType != XmlNodeType.Attribute && node.ParentNode?.NodeType != XmlNodeType.Attribute) nodes++;
            string value = node.Value;
            long characters = (long)node.Name.Length + node.NamespaceURI.Length + (value == null ? 0L : value.Length);
            long bytes = checked(512L + characters * 8L);
            if (node.Attributes != null)
                foreach (XmlAttribute attribute in node.Attributes) bytes = checked(bytes + EstimateNode(attribute, ref nodes));
            foreach (XmlNode child in node.ChildNodes) bytes = checked(bytes + EstimateNode(child, ref nodes));
            return bytes;
        }

        public AssetBuffer Execute()
        {
            if (disposed) throw new ObjectDisposedException("PreparedBackendWork");
            if (textureMessages != null)
                return ((Recovered.Compiler.Texture.Plugin)plugin).ProcessInstance(declaration,
                    message => textureMessages.Add(new TextureMessage(true, message)),
                    message => textureMessages.Add(new TextureMessage(false, message)));
            // W3X's initialized plugin only reads its immutable platform field;
            // its backend reads the finalized node/namespace bindings. Native
            // scratch and outputs are per-call; the only lazy native table is
            // const and built with C++17's thread-safe static initialization.
            // Certified XML routes use a per-call bridge tree and Relo::Tracker;
            // XmlPriority's optional trace writes already hold TraceGate. Audio
            // uses a stateless exact encoder, readonly tables and private CDATA.
            // Tokenizers also use per-call native inputs/trackers/buffers and
            // serialize only their optional shared trace index. Other routes
            // remain owner barriers, including PathMusic's Tracer paths.
            return plugin.ProcessInstance(declaration);
        }

        sealed class TextureMessage
        {
            internal readonly bool Warning;
            internal readonly string Text;
            internal TextureMessage(bool warning, string text) { Warning = warning; Text = text; }
        }

        // Called only by the owner after this worker joins.
        // Workers never mutate BuildSession diagnostics or the event writer.
        internal void FlushDiagnostics()
        {
            if (textureMessages == null) return;
            var texture = (Recovered.Compiler.Texture.Plugin)plugin;
            foreach (var message in textureMessages) texture.EmitPreparedTrace(message.Warning, message.Text);
            textureMessages.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            if (inputLeases != null) foreach (FileStream lease in inputLeases) lease.Dispose();
            disposed = true;
        }

        static bool Reject(string value, out string reason)
        {
            reason = value;
            return false;
        }

        static bool PrepareXmlReads(InstanceDeclaration original, List<FileStream> leases, out string reason)
        {
            reason = null;
            uint type = original.Handle.TypeId;
            if (!IsApt(type)) return true;
            XmlAttribute file = original.Node.Attributes["File"];
            if (file == null) return Reject("external-input-path-missing", out reason);
            // Native APT uses narrow fopen. ASCII preserves the exact pathname
            // shared by the managed lease; other paths use owner compatibility.
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return Reject("unsupported-platform", out reason);
            foreach (char character in file.Value)
                if (character > 127) return Reject("native-path-non-ascii", out reason);
            return LeaseInputPath(file.Value, leases, out reason);
        }

        static bool IsApt(uint type)
        {
            return type == 1816258268u || type == 983359664u || type == 1110676961u || type == 1034587168u;
        }

        static bool IsParallelXml(uint type)
        {
            // APT retains its separately certified read-lease envelope. All
            // metadata routes use the closed module audit, never an open-ended
            // "all XML except" rule. Unknown/new routes remain owner-only.
            return IsApt(type) || XmlBackendCertification.IsCertified(type);
        }

        static long ExternalReadBudget(List<FileStream> leases, long minimum, long multiplier)
        {
            if (leases == null) return minimum;
            try
            {
                long bytes = minimum;
                foreach (FileStream lease in leases) bytes = checked(bytes + checked(lease.Length * multiplier));
                return bytes;
            }
            catch (OverflowException) { return long.MaxValue; }
        }

        static bool PrepareAudio(InstanceDeclaration original, List<FileStream> leases, out string reason)
        {
            // This exact Windows share mode excludes replacement, deletion and
            // writing while the backend reopens its original pathname.
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return Reject("unsupported-platform", out reason);
            XmlAttribute file = original.Node.Attributes["File"];
            return LeaseInputPath(file == null ? null : file.Value, leases, out reason);
        }

        static bool LeaseInputPath(string value, List<FileStream> leases, out string reason, HashSet<string> checkedDirectories = null)
        {
            reason = null;
            if (string.IsNullOrEmpty(value)) return Reject("external-input-path-missing", out reason);
            try
            {
                string path = Path.GetFullPath(value);
                for (var parent = new FileInfo(path).Directory; parent != null; parent = parent.Parent)
                {
                    if (checkedDirectories != null && !checkedDirectories.Add(parent.FullName)) break;
                    if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                        return Reject("external-input-reparse-point", out reason);
                }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return Reject("external-input-reparse-point", out reason);
                leases.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                return true;
            }
            catch (IOException) { return Reject("external-input-unavailable", out reason); }
            catch (UnauthorizedAccessException) { return Reject("external-input-unavailable", out reason); }
            catch (ArgumentException) { return Reject("external-input-path-invalid", out reason); }
            catch (NotSupportedException) { return Reject("external-input-path-invalid", out reason); }
        }

        static long W3xBudget(XmlNode root, long nodes, long minimum)
        {
            // Worst-case channel/frame expansion is much larger than its XML.
            // Count every element and apply a conservative per-node allowance;
            // declared-frame animation expansion is covered independently.
            long frames = 0;
            XmlAttribute numFrames = root.Attributes["NumFrames"];
            uint value = 0;
            if (numFrames != null && !uint.TryParse(numFrames.Value, out value)) return long.MaxValue;
            frames = value;
            long channels = root.SelectNodes("*[local-name()='Channels']/*").Count;
            try { return checked(minimum + nodes * 1024L + frames * Math.Max(1L, channels) * 256L); }
            catch (OverflowException) { return long.MaxValue; }
        }

    }
}
#endif
