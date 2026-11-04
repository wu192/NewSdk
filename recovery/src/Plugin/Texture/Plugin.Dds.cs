using System;
using System.IO;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    public partial class Plugin
    {
        // Original ProcessInstance switches on Handle.TypeId, never XML element name.
        public virtual AssetBuffer ProcessInstance(InstanceDeclaration declaration)
        {
            return ProcessInstance(declaration,
                delegate(string message) { EmitPreparedTrace(true, message); },
                delegate(string message) { EmitPreparedTrace(false, message); });
        }

        // Worker callers collect diagnostics locally; the owner emits them after
        // joining this call, before committing its asset or reporting its failure.
        internal AssetBuffer ProcessInstance(InstanceDeclaration declaration,
            Action<string> warning, Action<string> information)
        {
            Alive();
            if (!initialized) throw new InvalidOperationException("Texture plugin must first be initialized.");
            if (declaration == null) throw new ArgumentNullException("declaration");
            TextureCatalog.RequireWin32(_Platform);
            switch (declaration.Handle.TypeId)
            {
                case TextureCatalog.TextureTypeId: return CompileTexture(declaration, false, warning, information);
                case TextureCatalog.OnDemandTextureTypeId: return CompileTexture(declaration, true, warning, information);
                case TextureCatalog.TerrainTextureAtlasTypeId:
                    return CompileAtlas(declaration);
                default:
                    throw new NotSupportedException("Unsupported texture type: " + declaration.Handle.TypeName);
            }
        }

        private static void Write32(byte[] data, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) data[offset + i] = (byte)(value >> (8 * i));
        }

        private static AssetBuffer EmbedDds(byte[] bytes, bool rootTexture, TargetPlatform platform)
        {
            TextureCatalog.RequireWin32(platform);
            int root = rootTexture ? 12 : 8;
            int pointer = rootTexture ? 4 : 0;
            // Tracker::Allocate rounds EACH block to four bytes and zero-fills padding.
            byte[] data = new byte[checked(root + ((bytes.Length + 3) & ~3))];
            Write32(data, pointer, (uint)root);
            Write32(data, pointer + 4, (uint)bytes.Length);
            Buffer.BlockCopy(bytes, 0, data, root, bytes.Length);
            byte[] relocations = new byte[8];
            Write32(relocations, 0, (uint)pointer);
            Write32(relocations, 4, uint.MaxValue);
            return new AssetBuffer { InstanceData = data, RelocationData = relocations, ImportsData = new byte[0] };
        }

        private AssetBuffer CompileDds(InstanceDeclaration declaration, bool onDemand)
        {
            string file = declaration.XmlNode.Attributes["File"].Value;
            if (!File.Exists(file))
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "The texture file referenced from the XML does not exist on disk!", new object[0]);
            // XSD validation/default expansion and FileReference preprocessing happen
            // in Core before this entry, just as they do for the original plugin.
            string textureType = declaration.Node.Attributes["Type"].Value;
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length == 0)
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "Texture file has length 0", new object[0]);
            AssetBuffer result = EmbedDds(bytes, !onDemand, _Platform);
            return onDemand ? FinishOnDemand(declaration, textureType, result) : result;
        }

        private AssetBuffer FinishOnDemand(InstanceDeclaration declaration, string textureType, AssetBuffer result)
        {
            // OnDemandTexture root has only a zero runtime pointer and an enum.
            // The original writes the enum directly without an endian conversion.
            uint type = textureType == "StandardTexture" ? 0u : textureType == "VolumeTexture" ? 1u : 2u;
            byte[] outer = new byte[8];
            Write32(outer, 4, type);
            byte[] header = new byte[32];
            Write32(header, 0, declaration.Handle.TypeId);
            Write32(header, 4, declaration.Handle.InstanceId);
            Write32(header, 8, declaration.Handle.TypeHash);
            Write32(header, 12, declaration.Handle.InstanceHash);
            Write32(header, 16, (uint)result.InstanceData.Length);
            Write32(header, 20, (uint)result.RelocationData.Length);
            Write32(header, 24, (uint)result.ImportsData.Length);
            // File.OpenWrite intentionally retains any previous trailing bytes.
            using (FileStream stream = File.OpenWrite(declaration.CustomDataPath))
            {
                stream.Write(header, 0, header.Length);
                stream.Write(result.InstanceData, 0, result.InstanceData.Length);
                stream.Write(result.RelocationData, 0, result.RelocationData.Length);
            }
            return new AssetBuffer { InstanceData = outer, RelocationData = new byte[0], ImportsData = new byte[0] };
        }
    }
}

