using System;
using System.IO;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    public partial class Plugin
    {
        private static readonly Tracer ImageTracer = Tracer.GetTracer("TextureCompiler", "Provides Texture Processing");
        // This method is called only by the owner for prepared worker messages.
        internal void EmitPreparedTrace(bool warning, string message)
        {
            if (warning) ImageTracer.TraceWarning(message);
            else ImageTracer.TraceInfo(message);
        }
        private AssetBuffer CompileTexture(InstanceDeclaration declaration, bool onDemand,
            Action<string> warning, Action<string> information)
        {
            string file = declaration.XmlNode.Attributes["File"].Value;
            if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return CompileDds(declaration, onDemand);
            if (!File.Exists(file))
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "The texture file referenced from the XML does not exist on disk!", new object[0]);
            string format = "D3DFMT_" + declaration.Node.Attributes["OutputFormat"].Value;
            bool mipmaps = bool.Parse(declaration.Node.Attributes["GenerateMipMaps"].Value);
            string textureType = declaration.Node.Attributes["Type"].Value;
            TexturePacker.Image image;
            try { image = TextureImageLoader.Load(file); }
            catch (InvalidDataException)
            {
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "D3DX cannot load the texture from disk.", new object[0]);
            }
            AssetBuffer result = new AssetBuffer { InstanceData = new byte[onDemand ? 8 : 12],
                RelocationData = new byte[0], ImportsData = new byte[0] };
            bool valid = TextureImageRules.IsValidTexture((uint)image.Width, (uint)image.Height,
                declaration.Handle.InstanceName, warning);
            if (valid)
            {
                byte[] rgba = ImageBytes(image);
                format = TextureImageRules.GuessProperFormat((uint)image.Width, (uint)image.Height,
                    rgba, image.HasAlpha, format, declaration.Handle.InstanceName,
                    information);
                switch (textureType)
                {
                    case "CubeTexture":
                        throw new BinaryAssetBuilderException(null, ErrorCode.InvalidArgument,
                            "CubeTexture is not currently supported.  Create the cube texture as a DDS with the D3D Texture tool in the meantime.", new object[0]);
                    case "VolumeTexture":
                        result = EmbedDds(TextureDdsEncoder.EncodeVolumeStrip(image.Width, image.Height, rgba, format, mipmaps), !onDemand, _Platform);
                        break;
                    case "StandardTexture":
                        result = EmbedDds(TextureDdsEncoder.Encode2D(image.Width, image.Height, rgba, format, mipmaps), !onDemand, _Platform);
                        break;
                }
            }
            return onDemand ? FinishOnDemand(declaration, textureType, result) : result;
        }
        private static byte[] ImageBytes(TexturePacker.Image image)
        {
            byte[] bytes = new byte[checked(image.Pixels.Length * 4)];
            Buffer.BlockCopy(image.Pixels, 0, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
