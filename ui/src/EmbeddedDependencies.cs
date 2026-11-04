using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace BinaryAssetBuilder.UiCompiler
{
    /// <summary>Loads the three pinned UI libraries embedded by the unified compiler.</summary>
    public static class UiEmbeddedDependencies
    {
        private static int initialized;
        private static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        // This method intentionally mentions no types from the optional libraries:
        // the host calls it before the JIT reaches the UI compilation path.
        public static void Initialize()
        {
            if (Interlocked.Exchange(ref initialized, 1) == 0)
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name != "LibTessDotNet" && name != "Clipper2Lib" && name != "StbImageSharp") return null;
            lock (loaded)
            {
                Assembly assembly;
                if (loaded.TryGetValue(name, out assembly)) return assembly;
                using (Stream source = typeof(UiEmbeddedDependencies).Assembly.GetManifestResourceStream("BinaryAssetBuilder.UiCompiler.Dependencies." + name + ".dll"))
                {
                    if (source == null) return null; // Standalone plugin deployments use the normal loader.
                    using (var bytes = new MemoryStream())
                    {
                        source.CopyTo(bytes);
                        assembly = Assembly.Load(bytes.ToArray());
                    }
                }
                loaded.Add(name, assembly);
                return assembly;
            }
        }
    }
}
