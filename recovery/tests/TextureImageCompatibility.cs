using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;

// One source, isolated original DLL process and production-object EXE host.
// Raw A8R8G8B8 removes compression search from the image-decoder comparison.
public static class TextureImageCompatibility
{
    public sealed class Case
    {
        public string name, file, source, inputSha256;
        public bool? expectedOriginal;
        public bool expectedCandidate, compareBytes;
    }
    static string Hash(byte[] data)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    static InstanceDeclaration Declare(string file, string custom)
    {
        var xml = new XmlDocument();
        xml.LoadXml("<Texture xmlns='uri:ea.com:eala:asset' id='ImageCompatibility' File='" +
            System.Security.SecurityElement.Escape(file) +
            "' Type='StandardTexture' OutputFormat='A8R8G8B8' GenerateMipMaps='false' AllowAutomaticResize='true'/>");
        var namespaces = new XmlNamespaceManager(xml.NameTable);
        namespaces.AddNamespace("ea", "uri:ea.com:eala:asset");
        var document = new AssetDeclarationDocument();
        Type current = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(current);
        current.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(document, state);
        return new InstanceDeclaration(document) { XmlNode = xml.DocumentElement, CustomDataPath = custom };
    }
    static void Buffer(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes == null ? -1 : bytes.Length);
        if (bytes != null) writer.Write(bytes);
    }
    public static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected fixture-directory output-directory original-texture-dll|built-in");
        if (IntPtr.Size != 4) throw new Exception("Texture input compatibility requires x86");
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Case[] cases = json.Deserialize<Case[]>(File.ReadAllText(Path.Combine(fixtures, "cases.json")));
        var results = new List<object>(); int failures = 0;
#if TEXTURE_SOURCE_PROBE
        if (args[2] != "built-in") throw new Exception("Candidate must use the production plugin objects");
        IAssetBuilderPlugin plugin = new Recovered.Compiler.Texture.Plugin();
#else
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[2]));
        IAssetBuilderPlugin plugin = (IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.TextureCompiler.Plugin", true));
#endif
        try
        {
            plugin.Initialize(null, TargetPlatform.Win32);
            for (int i = 0; i < cases.Length; i++)
            {
                Case item = cases[i];
                string file = Path.Combine(fixtures, item.file);
                if (Hash(File.ReadAllBytes(file)) != item.inputSha256) throw new Exception("Fixture hash differs: " + item.name);
                bool accepted = false; string error = null, bytesHash = null;
                string raw = "buffers-" + i.ToString("D4") + ".bin";
                try
                {
                    plugin.ReInitialize(null, TargetPlatform.Win32);
                    string custom = Path.Combine(output, "unused.custom");
                    if (File.Exists(custom)) File.Delete(custom);
                    AssetBuffer buffer = plugin.ProcessInstance(Declare(file, custom));
                    if (buffer == null || buffer.InstanceData == null) throw new Exception("No texture buffer");
                    if (File.Exists(custom)) throw new Exception("StandardTexture unexpectedly emitted custom data");
                    using (var memory = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(memory))
                        {
                            Buffer(writer, buffer.InstanceData); Buffer(writer, buffer.RelocationData); Buffer(writer, buffer.ImportsData);
                            writer.Flush(); byte[] bytes = memory.ToArray();
                            File.WriteAllBytes(Path.Combine(output, raw), bytes); bytesHash = Hash(bytes);
                        }
                    }
                    accepted = true;
                }
                catch (Exception failure) { error = failure.ToString(); }
#if TEXTURE_SOURCE_PROBE
                bool? expected = item.expectedCandidate;
#else
                bool? expected = item.expectedOriginal;
#endif
                bool passed = !expected.HasValue || expected.Value == accepted;
                if (!passed) { ++failures; Console.Error.WriteLine("FAIL " + item.name + ": " + error); }
                results.Add(new { item.name, item.inputSha256, accepted, expected, passed, error,
                    buffers = accepted ? raw : null, buffersSha256 = bytesHash, item.compareBytes });
            }
            File.WriteAllText(Path.Combine(output, "results.json"), json.Serialize(new {
                status = failures == 0 ? "passed" : "failed", failures, cases = results,
                pluginVersion = plugin.GetVersionNumber(), allTypesHash = plugin.GetAllTypesHash(),
                scope = "Original untouched TNW TGA inputs and bounded metadata cases, raw three-buffer comparison; no compressed-byte or full MOD claim"
            }));
        }
        finally { ((IDisposable)plugin).Dispose(); }
        return failures == 0 ? 0 : 1;
    }
}
