using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    // Source: original Plugin metadata methods; SHA256
    // 4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e.
    public static class TextureCatalog
    {
        public const uint TextureTypeId = 568797146u;
        public const uint OnDemandTextureTypeId = 2467477932u;
        public const uint TerrainTextureAtlasTypeId = 1601459053u;
        public const uint AllTypesHash = 1424942948u;

        // This recovered plugin supports Win32/PC only.
        public static void RequireWin32(TargetPlatform platform)
        {
            if (platform != TargetPlatform.Win32)
                throw new System.NotSupportedException("TextureCompiler supports Win32/PC only.");
        }

        public static uint Version(TargetPlatform platform)
        {
            RequireWin32(platform);
            return 4202u;
        }

        public static ExtendedTypeInformation TypeInformation(uint typeId, TargetPlatform platform)
        {
            RequireWin32(platform);
            ExtendedTypeInformation result = new ExtendedTypeInformation();
            result.TypeId = typeId;
            result.HasCustomData = false;
            switch (typeId)
            {
                case TextureTypeId:
                    result.TypeName = "Texture";
                    result.TypeHash = 2673866970u;
                    result.ProcessingHash = 2673862832u;
                    break;
                case OnDemandTextureTypeId:
                    result.TypeName = "OnDemandTexture";
                    result.TypeHash = 4165504734u;
                    result.ProcessingHash = 4165508788u;
                    result.HasCustomData = true;
                    break;
                case TerrainTextureAtlasTypeId:
                    result.TypeName = "TerrainTextureAtlas";
                    result.TypeHash = 417928170u;
                    result.ProcessingHash = 1182597302u;
                    break;
            }
            return result;
        }
    }
}

