#if NEWSdk_MONOLITHIC
using System;
using System.Collections.Generic;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace NewModCompiler.Application
{
    public sealed class BuiltinCompilerRoute
    {
        public uint TypeId { get; private set; }
        public string TypeName { get; private set; }
        public uint TypeHash { get; private set; }
        public string Family { get; private set; }
        public bool SchemaGlobal { get; private set; }
        public BuiltinCompilerRoute(uint typeId, string typeName, uint typeHash, string family, bool schemaGlobal)
        {
            TypeId = typeId; TypeName = typeName; TypeHash = typeHash;
            Family = family; SchemaGlobal = schemaGlobal;
        }
    }

    // Owns one instance of each implementation, not one instance per asset type.
    // Core execution is deliberately separate: creating this set does not build a MOD.
    public sealed class BuiltinCompilers : IDisposable
    {
        private static readonly IList<BuiltinCompilerRoute> expected = MakeExpectedRoutes();
        private readonly List<IAssetBuilderPlugin> plugins = new List<IAssetBuilderPlugin>();
        private readonly List<PluginDescriptor> descriptors = new List<PluginDescriptor>();
        private readonly List<BuiltinCompilerRoute> routes = new List<BuiltinCompilerRoute>();
        private PluginRegistry registry;
        private bool disposed;
        private bool lazy;
        public static IList<BuiltinCompilerRoute> ExpectedRoutes { get { return expected; } }
        public IList<BuiltinCompilerRoute> Routes { get { return routes.AsReadOnly(); } }
        public IList<IAssetBuilderPlugin> Plugins { get { return plugins.AsReadOnly(); } }
        public PluginRegistry Registry
        {
            get { ThrowIfDisposed(); return registry; }
        }
        private BuiltinCompilers() { }

        public static BuiltinCompilers Create(TargetPlatform platform)
        {
            return CreateCore(platform, false);
        }

        public static BuiltinCompilers CreateWithTexture(TargetPlatform platform)
        {
            return CreateCore(platform, true);
        }

        internal static BuiltinCompilers CreateLazyWithTexture(TargetPlatform platform)
        {
            return CreateCore(platform, true, true);
        }

        private static BuiltinCompilers CreateCore(TargetPlatform platform, bool includeTexture, bool lazy=false)
        {
            if (platform != TargetPlatform.Win32)
                throw new NotSupportedException("The production compiler supports only Win32.");
            BuiltinCompilers result = new BuiltinCompilers { lazy = lazy };
            try
            {
                result.Add("gameobject", false, () => new Recovered.GameObject.Plugin());
                result.Add("weapon", false, () => new Recovered.Weapon.Plugin());
                result.Add("armor", false, () => new Recovered.Compiler.Plugin());
                result.Add("xml", false, () => new Recovered.XmlPriority.Plugin());
                result.Add("ui", false, () => new UiAptPlugin());
                result.Add("w3x", true, () => new Recovered.Compiler.W3x.NativePlugin());
                result.Add("audio", true, () => new BinaryAssetBuilder.AudioCompiler.Plugin());
                if (includeTexture)
                {
                    result.descriptors.Add(PluginDescriptor.CreateBuiltin(
                        "Texture,OnDemandTexture,TerrainTextureAtlas", true, () =>
                        {
                            IAssetBuilderPlugin plugin = lazy
                                ? (IAssetBuilderPlugin)new LazyPlugin(() => new Recovered.Compiler.Texture.Plugin(), Recovered.Compiler.Texture.TextureCatalog.Version(TargetPlatform.Win32))
                                : new Recovered.Compiler.Texture.Plugin();
                            result.plugins.Add(plugin);
                            return plugin;
                        }));
                }
                result.registry = new PluginRegistry(result.descriptors.ToArray(), platform);
                result.registry.DefaultPlugin = new UnsupportedPlugin();
                result.routes.AddRange(expected);
                if (includeTexture)
                {
                    foreach (string name in new[] { "Texture", "OnDemandTexture", "TerrainTextureAtlas" })
                    {
                        uint id = InstanceHandle.GetTypeId(name);
                        var info = lazy ? null : result.registry.GetPlugin(id).GetExtendedTypeInformation(id);
                        if (!lazy && (info == null || info.TypeId != id || info.TypeName != name))
                            throw new InvalidOperationException("Missing texture route: " + name);
                        result.routes.Add(new BuiltinCompilerRoute(id, name, lazy ? 0u : info.TypeHash, "texture", true));
                    }
                }
                return result;
            }
            catch (Exception failure)
            {
                try { result.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Compiler initialization and cleanup failed.", failure, cleanup); }
                throw;
            }
        }

        private void Add(string family, bool useBuildCache, Func<IAssetBuilderPlugin> factory)
        {
            List<string> names = new List<string>();
            foreach (BuiltinCompilerRoute route in expected)
                if (route.Family == family) names.Add(route.TypeName);
            if (names.Count == 0) throw new InvalidOperationException("Missing built-in family: " + family);
            descriptors.Add(PluginDescriptor.CreateBuiltin(String.Join(",", names), useBuildCache, () =>
            {
                uint version = family == "xml" ? 4u : family == "w3x" ? Recovered.W3x.W3xCatalog.Version(TargetPlatform.Win32) :
                    family == "audio" ? BinaryAssetBuilder.AudioCompiler.AudioCatalog.Version(TargetPlatform.Win32) : 4100u;
                // Source expansion happens before backend dispatch, so its UI
                // interface must remain available even on a lazy Core session.
                IAssetBuilderPlugin plugin = lazy && family != "ui" ?
                    (family == "audio" ? (IAssetBuilderPlugin)new LazyAudioPlugin(factory, version) : new LazyPlugin(factory, version)) : factory();
                if (plugin == null) throw new InvalidOperationException("Built-in factory returned null: " + family);
                plugins.Add(plugin);
                return plugin;
            }));
        }

        // Validate both the IDs and names before Core initializes any factories.
        // An omitted, duplicate, renamed or reclassified route is never silently accepted.
        public static void ValidateRoutes(IEnumerable<BuiltinCompilerRoute> candidate)
        {
            if (candidate == null) throw new ArgumentNullException("candidate");
            Dictionary<uint, BuiltinCompilerRoute> required = new Dictionary<uint, BuiltinCompilerRoute>();
            foreach (BuiltinCompilerRoute route in expected) required.Add(route.TypeId, route);
            HashSet<uint> ids = new HashSet<uint>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuiltinCompilerRoute route in candidate)
            {
                if (route == null || String.IsNullOrEmpty(route.TypeName) || String.IsNullOrEmpty(route.Family))
                    throw new InvalidOperationException("Invalid built-in route.");
                if (!ids.Add(route.TypeId) || !names.Add(route.TypeName))
                    throw new InvalidOperationException("Duplicate built-in route: " + route.TypeName);
                BuiltinCompilerRoute original;
                if (!required.TryGetValue(route.TypeId, out original) ||
                    route.TypeName != original.TypeName || route.TypeHash != original.TypeHash ||
                    route.Family != original.Family || route.SchemaGlobal != original.SchemaGlobal ||
                    InstanceHandle.GetTypeId(route.TypeName) != route.TypeId)
                    throw new InvalidOperationException("Unexpected built-in route: " + route.TypeName);
            }
            if (ids.Count != required.Count)
                throw new InvalidOperationException("Missing built-in routes.");
        }

        private static void CheckMetadata(BuiltinCompilerRoute route, ExtendedTypeInformation info)
        {
            if (info == null || info.TypeId != route.TypeId ||
                info.TypeName != route.TypeName || info.TypeHash != route.TypeHash)
                throw new InvalidOperationException("Built-in metadata mismatch: " + route.TypeName);
        }

        public void ReInitialize(TargetPlatform platform)
        {
            ThrowIfDisposed();
            if (platform != TargetPlatform.Win32)
                throw new NotSupportedException("The production compiler supports only Win32.");
            registry.ReInitialize(descriptors.ToArray(), platform);
            if (!lazy) foreach (BuiltinCompilerRoute route in routes)
                CheckMetadata(route, registry.GetPlugin(route.TypeId).GetExtendedTypeInformation(route.TypeId));
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException("BuiltinCompilers");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            List<Exception> failures = new List<Exception>();
            for (int i = plugins.Count - 1; i >= 0; --i)
            {
                IDisposable owned = plugins[i] as IDisposable;
                if (owned == null) continue;
                try { owned.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count != 0) throw new AggregateException("Compiler cleanup failed.", failures);
        }

        private static IList<BuiltinCompilerRoute> MakeExpectedRoutes()
        {
            List<BuiltinCompilerRoute> entries = new List<BuiltinCompilerRoute>
            {
                new BuiltinCompilerRoute(2486173485u, "GameObject", 3415017856u, "gameobject", true),
                new BuiltinCompilerRoute(2496977262u, "WeaponTemplate", 216405646u, "weapon", true),
                new BuiltinCompilerRoute(980180622u, "ArmorTemplate", 1679479598u, "armor", true),
                new BuiltinCompilerRoute(608742960u, "W3DAnimation", 1430609896u, "w3x", true),
                new BuiltinCompilerRoute(3266421346u, "W3DMesh", 3448375452u, "w3x", true),
                new BuiltinCompilerRoute(376113229u, "AudioFile", 1405623879u, "audio", true),
                new BuiltinCompilerRoute(364756738u, "AudioFileMP3Passthrough", 2457571466u, "audio", true),
                new BuiltinCompilerRoute(2516466693u, "PathMusicMap", 3122351307u, "audio", true),
                new BuiltinCompilerRoute(2802565590u, "PathMusicTrack", 2668810035u, "audio", true),
                new BuiltinCompilerRoute(2590317961u, "PathMusicEvent", 1993395951u, "audio", true)
            };
            foreach (var route in BuiltinXmlRoutes.Entries)
                entries.Add(route.TypeName == "AptAptData"
                    ? new BuiltinCompilerRoute(route.TypeId, route.TypeName, route.TypeHash, "ui", route.SchemaGlobal)
                    : route);
            return entries.AsReadOnly();
        }

        // Called by the owner only after Core has established a cache miss.
        // Preserve lazy aggregate metadata on hot builds; workers receive the
        // exact initialized implementation and never touch the lazy wrapper.
        internal static IAssetBuilderPlugin ResolveBackendPlugin(IAssetBuilderPlugin plugin)
        {
            var lazyPlugin = plugin as LazyPlugin;
            plugin = lazyPlugin == null ? plugin : lazyPlugin.Backend;
            var uiPlugin = plugin as UiAptPlugin;
            return uiPlugin == null ? plugin : uiPlugin.BinaryBackend;
        }

        // Version metadata is the original Win32 contract. Reading aggregate cache
        // versions must not initialize native providers for unrelated asset types.
        private class LazyPlugin : IAssetBuilderPlugin, IDisposable
        {
            readonly Func<IAssetBuilderPlugin> factory;
            readonly uint version;
            IAssetBuilderPlugin instance;
            TargetPlatform platform;
            object configuration;
            bool disposed;
            public LazyPlugin(Func<IAssetBuilderPlugin> factory, uint version) { this.factory = factory; this.version = version; }
            IAssetBuilderPlugin Value
            {
                get
                {
                    if (disposed) throw new ObjectDisposedException("LazyPlugin");
                    if (instance == null)
                    {
                        var created = factory();
                        instance = created; // Owner disposes even when Initialize fails.
                        created.Initialize(configuration, platform);
                        if (created.GetVersionNumber() != version) throw new InvalidOperationException("Built-in provider version mismatch.");
                    }
                    return instance;
                }
            }
            internal IAssetBuilderPlugin Backend { get { return Value; } }
            public void Initialize(object config, TargetPlatform target) { configuration = config; platform = target; }
            public void ReInitialize(object config, TargetPlatform target)
            {
                if (disposed) throw new ObjectDisposedException("LazyPlugin");
                configuration = config; platform = target;
                if (instance != null) instance.ReInitialize(config, target);
            }
            public uint GetAllTypesHash() { return 1424942948u; }
            public uint GetVersionNumber() { return version; }
            public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId) { return Value.GetExtendedTypeInformation(typeId); }
            public AssetBuffer ProcessInstance(InstanceDeclaration instance) { return Value.ProcessInstance(instance); }
            public void Dispose() { if (disposed) return; disposed = true; var owned = instance as IDisposable; if (owned != null) owned.Dispose(); }
        }

        // Only audio has per-instance identities. Other lazy routes keep the
        // registry's cached type metadata on cold and hot paths.
        private sealed class LazyAudioPlugin : LazyPlugin, IInstanceProcessingHash
        {
            public LazyAudioPlugin(Func<IAssetBuilderPlugin> factory, uint version) : base(factory, version) { }
            public uint GetProcessingHash(InstanceDeclaration declaration)
            {
                return ((IInstanceProcessingHash)Backend).GetProcessingHash(declaration);
            }
            public bool IsProcessingHashCurrent(uint typeId, uint processingHash, bool compilerVersionChanged)
            {
                return ((IInstanceProcessingHash)Backend).IsProcessingHashCurrent(typeId, processingHash, compilerVersionChanged);
            }
        }

        private sealed class UnsupportedPlugin : IAssetBuilderPlugin
        {
            public void Initialize(object configuration, TargetPlatform platform) { }
            public void ReInitialize(object configuration, TargetPlatform platform) { }
            public uint GetAllTypesHash() { return 0; }
            public uint GetVersionNumber() { return 0; }
            public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
            {
                throw new NotSupportedException("No built-in compiler for type ID " + typeId + ".");
            }
            public AssetBuffer ProcessInstance(InstanceDeclaration instance)
            {
                throw new NotSupportedException("No built-in compiler for the requested asset.");
            }
        }
    }
}
#endif

