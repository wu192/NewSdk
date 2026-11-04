using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

// Independent original DLL metadata, used to bind each cache test's exact
// CustomData count and manifest type identity. This never enters production.
public static class CompilerLegacyCacheMetadata
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || IntPtr.Size != 4) throw new Exception("Expected original SDK/tools and output.json (x86)");
            var core = Assembly.LoadFrom(Path.Combine(args[0], "BinaryAssetBuilder.Core.dll"));
            var texture = Assembly.LoadFrom(Path.Combine(args[0], "BinaryAssetBuilder.TextureCompiler.dll"));
            var pluginType = texture.GetType("BinaryAssetBuilder.TextureCompiler.Plugin", true);
            var api = core.GetType("BinaryAssetBuilder.Core.IAssetBuilderPlugin", true);
            var lifecycle = core.GetType("BinaryAssetBuilder.IAssetBuilderPluginBase", true);
            object plugin = Activator.CreateInstance(pluginType);
            var result = new Dictionary<string, object>();
            try
            {
                object platform = Enum.Parse(core.GetType("BinaryAssetBuilder.Core.TargetPlatform", true), "Win32");
                lifecycle.GetMethod("Initialize").Invoke(plugin, new object[] {null, platform});
                foreach (string name in new[] {"Texture", "OnDemandTexture", "TerrainTextureAtlas"})
                {
                    object id = core.GetType("BinaryAssetBuilder.Core.InstanceHandle", true).GetMethod("GetTypeId").Invoke(null, new object[] {name});
                    object information = api.GetMethod("GetExtendedTypeInformation").Invoke(plugin, new object[] {id});
                    var fields = new Dictionary<string, object>();
                    foreach (string field in new[] {"TypeName", "TypeId", "TypeHash", "ProcessingHash", "HasCustomData"})
                        fields[field] = information.GetType().GetField(field).GetValue(information);
                    result[name] = fields;
                }
            }
            finally { ((IDisposable)plugin).Dispose(); }
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(result)); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
