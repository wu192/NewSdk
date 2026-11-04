using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using BinaryAssetBuilder.Core;

// Component probe, not a substitute for real Core XSD/preprocessing integration.
// The same source may be compiled into the final EXE for G2 C (call Run).
public static class TextureProbe
{
    static string Escape(string text) { return System.Security.SecurityElement.Escape(text); }
    static string Bytes(byte[] bytes) { return bytes == null ? "null" : Convert.ToBase64String(bytes); }
    static InstanceDeclaration Declare(string text, string output)
    {
        var xml = new XmlDocument(); xml.LoadXml(text);
        var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("ea", "uri:ea.com:eala:asset");
        var doc = new AssetDeclarationDocument();
        var current = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        var state = FormatterServices.GetUninitializedObject(current);
        current.GetField("NamespaceManager").SetValue(state, ns);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(doc, state);
        return new InstanceDeclaration(doc) { XmlNode = xml.DocumentElement, CustomDataPath = output };
    }
    static string Xml(string type, string file, bool missing)
    {
        string source = Escape(missing ? file + (type == "TerrainTextureAtlas" ? ".wrong-size.tga" : ".missing") : file);
        if (type == "TerrainTextureAtlas")
            return "<TerrainTextureAtlas xmlns='uri:ea.com:eala:asset' id='Probe' AtlasSize='2048' AllowLossyCompression='false'><Tile TextureID='1' BaseTexture='" + source + "' NormalTexture='" + source + "'><Cell IndexX='0' IndexY='0'/></Tile></TerrainTextureAtlas>";
        return "<" + type + " xmlns='uri:ea.com:eala:asset' id='Probe' File='" + source + "' Type='StandardTexture' GenerateMipMaps='false' OutputFormat='A8R8G8B8' AllowAutomaticResize='false'/>";
    }
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("texture.dll fixture.tga output.txt original|exe");
            if (IntPtr.Size != 4) throw new Exception("Texture requires x86");
            bool exe = args[3] == "exe";
            var owner = typeof(IAssetBuilderPlugin).Assembly;
            if (exe && owner != Assembly.GetEntryAssembly()) throw new Exception("Core types must belong to the current entry EXE");
            if (exe && owner.GetModules().Length != 1) throw new Exception("EXE-bound Core still depends on build-time modules");
            if (exe)
                foreach (string name in new[] { "BinaryAssetBuilder.Core", "BinaryAssetBuilder.Utility", "EALA.Hash", "EALA.Metrics", "BinaryAssetBuilder.TextureCompiler" })
                    if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll"))) throw new Exception("Forbidden compatibility DLL present: " + name);
#if TEXTURE_SOURCE_PROBE
            object instance = new Recovered.Compiler.Texture.Plugin();
#else
            var pluginAssembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            object instance = Activator.CreateInstance(pluginAssembly.GetType("BinaryAssetBuilder.TextureCompiler.Plugin", true));
#endif
            var plugin = (IAssetBuilderPlugin)instance;
            string custom = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "texture-probe.custom");
            int successes = 0, errors = 0;
            try
            {
                plugin.Initialize(null, TargetPlatform.Win32);
                using (var writer = new StreamWriter(args[2], false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("metadata:" + plugin.GetAllTypesHash() + ":" + plugin.GetVersionNumber());
                    foreach (string type in new[] { "Texture", "OnDemandTexture", "TerrainTextureAtlas" })
                    {
                        // Success -> error -> success in one plugin and process checks reuse after failure.
                        foreach (bool missing in new[] { false, true, false })
                        {
                            plugin.ReInitialize(null, TargetPlatform.Win32);
                            if (File.Exists(custom)) File.Delete(custom);
                            var declaration = Declare(Xml(type, Path.GetFullPath(args[1]), missing), custom);
                            var information = plugin.GetExtendedTypeInformation(declaration.Handle.TypeId);
                            if (information == null || information.TypeName != type) throw new Exception("Metadata route absent: " + type);
                            string result;
                            try
                            {
                                AssetBuffer buffer = plugin.ProcessInstance(declaration);
                                if (missing) throw new Exception("Expected invalid-input error for " + type);
                                if (buffer == null || buffer.InstanceData == null) throw new Exception("Missing output buffer: " + type);
                                result = "BUFFER:" + Bytes(buffer.InstanceData) + ":" + Bytes(buffer.RelocationData) + ":" + Bytes(buffer.ImportsData);
                                successes++;
                            }
                            catch (Exception ex)
                            {
                                if (!missing) throw;
                                // A normal return is not an accepted negative test.
                                if (ex.Message.StartsWith("Expected invalid-input error", StringComparison.Ordinal)) throw;
                                result = "ERROR:" + ex.GetType().FullName + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(ex.Message)); errors++;
                            }
                            result += ":CUSTOM:" + (File.Exists(custom) ? Bytes(File.ReadAllBytes(custom)) : "absent");
                            writer.WriteLine(type + ":" + missing + ":" + result);
                        }
                    }
                }
            }
            finally { ((IDisposable)instance).Dispose(); }
#if TEXTURE_SOURCE_PROBE
            ((IDisposable)instance).Dispose(); // single-close contract: a second close is harmless
            bool closed = false;
            try { plugin.GetVersionNumber(); } catch (ObjectDisposedException) { closed = true; }
            if (!closed) throw new Exception("Texture plugin remains callable after Dispose");
#endif
            string trace = args[2] + ".assemblies.tsv";
            File.WriteAllLines(trace, AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName).Select(a => a.FullName + "\t" + (a.IsDynamic ? "dynamic" : a.Location)).ToArray());
            if (exe && AppDomain.CurrentDomain.GetAssemblies().Any(a => new[] { "BinaryAssetBuilder.Core", "BinaryAssetBuilder.Utility", "EALA.Hash", "EALA.Metrics", "BinaryAssetBuilder.TextureCompiler" }.Contains(a.GetName().Name))) throw new Exception("Old business assembly loaded");
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                File.WriteAllLines(args[2] + ".native.tsv", process.Modules.Cast<System.Diagnostics.ProcessModule>().Select(m => m.ModuleName + "\t" + m.FileName).OrderBy(s => s).ToArray());
            if (successes != 6 || errors != 3) throw new Exception("Incomplete three-route matrix");
            Console.WriteLine("PASS Texture ProcessInstance: 6 successes, 3 invalid-input errors, reuse after error; owner=" + owner.FullName);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static int Main(string[] args) { return Run(args); }
}
