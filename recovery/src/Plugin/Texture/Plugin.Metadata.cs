using System;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    public partial class Plugin : IAssetBuilderPlugin, IDisposable
    {
        private TargetPlatform _Platform;
        private bool initialized;
        private bool disposed;

        public virtual void Initialize(object configSection, TargetPlatform platform)
        {
            Alive(); TextureCatalog.RequireWin32(platform);
            if (initialized) throw new InvalidOperationException("Texture plugin is already initialized; use ReInitialize.");
            // Registration uses Core's owner-only tracer dictionary. Complete
            // it before prepared texture work can begin on background threads.
            GC.KeepAlive(ImageTracer);
            _Platform = platform;
            initialized = true;
        }
        public virtual void ReInitialize(object configSection, TargetPlatform platform)
        {
            Alive(); TextureCatalog.RequireWin32(platform);
            if (!initialized) throw new InvalidOperationException("Texture plugin must first be initialized.");
            _Platform = platform;
        }
        public virtual uint GetAllTypesHash() { Alive(); return TextureCatalog.AllTypesHash; }
        public virtual uint GetVersionNumber() { Alive(); return TextureCatalog.Version(_Platform); }
        public virtual ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            Alive();
            if (typeId != TextureCatalog.TextureTypeId && typeId != TextureCatalog.OnDemandTextureTypeId && typeId != TextureCatalog.TerrainTextureAtlasTypeId)
                return null;
            return TextureCatalog.TypeInformation(typeId, _Platform);
        }
        private void Alive()
        {
            if (disposed) throw new ObjectDisposedException("TextureCompiler.Plugin");
        }
        public void Dispose() { disposed = true; }
    }
}
