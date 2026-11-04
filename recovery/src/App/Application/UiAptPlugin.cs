#if NEWSdk_MONOLITHIC
using System;
using BinaryAssetBuilder.Core;
using BinaryAssetBuilder.UiCompiler;

namespace NewModCompiler.Application
{
    // AptAptData retains the existing binary-file route. FLA declarations are
    // expanded by Core before hashing and reach the same output manager.
    internal sealed class UiAptPlugin : IAssetBuilderPlugin, IUiFileCompiler, IDisposable
    {
        private readonly Plugin source = new Plugin();
        private readonly Recovered.XmlPriority.Plugin binary = new Recovered.XmlPriority.Plugin();
        internal IAssetBuilderPlugin BinaryBackend { get { return binary; } }
        public void Initialize(object config, TargetPlatform platform)
        {
            UiEmbeddedDependencies.Initialize();
            source.Initialize(null, platform);
            binary.Initialize(config, platform);
        }
        public void ReInitialize(object config, TargetPlatform platform)
        {
            source.ReInitialize(null, platform);
            binary.ReInitialize(config, platform);
        }
        public uint GetAllTypesHash() { return source.GetAllTypesHash(); }
        public uint GetVersionNumber() { return source.GetVersionNumber(); }
        public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            return typeId == 1816258268u ? binary.GetExtendedTypeInformation(typeId) : null;
        }
        public AssetBuffer ProcessInstance(InstanceDeclaration declaration) { return binary.ProcessInstance(declaration); }
        public UiAssetGroup CompileUiFile(InstanceDeclaration declaration, TargetPlatform platform)
        {
            return source.CompileUiFile(declaration, platform);
        }
        public void Dispose() { source.Dispose(); }
    }
}
#endif
