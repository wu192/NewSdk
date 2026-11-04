#!/usr/bin/env python3
"""Derive isolated XML route-smoke hosts without changing fixture definitions.

This adapts the existing 300-route default/completion smoke, NOT the complete
per-group XML differential/layout/rejection matrices or SDK acceptance.
"""
import argparse
import pathlib
import re

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=pathlib.Path, required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
parser.add_argument("--manifest", type=pathlib.Path, required=True)
args = parser.parse_args()
source = args.source.read_text(encoding="utf-8-sig")

def replace_once(old, new):
    global source
    if source.count(old) != 1:
        raise ValueError("XML fixture adapter anchor changed: " + old[:100])
    source = source.replace(old, new)

roots = re.findall(r'new Root\("([^"]+)",\s*(\d+)u,\s*(?:true|false)\)', source)
if len(roots) != 300 or len({name for name, _ in roots}) != 300:
    raise ValueError("Expected exactly 300 distinct XML routes")
args.manifest.write_text("".join(name + "\t" + value + "\n" for name, value in roots), encoding="ascii")

replace_once('    static string current = "metadata preflight";', '''    static string current = "metadata preflight";
    static StreamWriter Transcript;
    static string Data(byte[] value) {
        if (value == null) throw new Exception(current + " null transcript buffer");
        return Convert.ToBase64String(value);
    }''')
replace_once('''        original.Initialize(null, (TargetPlatform)platform);
        AssetBuffer expected = original.ProcessInstance(declaration);
        AssetBuffer actual = (AssetBuffer)Methods[root.Name].Invoke(null, new object[] { declaration, (TargetPlatform)platform });''', '''#if ORACLE
        original.Initialize(null, (TargetPlatform)platform);
        AssetBuffer expected = original.ProcessInstance(declaration);
        AssetBuffer actual = expected;
#else
        AssetBuffer actual = (AssetBuffer)Methods[root.Name].Invoke(null, new object[] { declaration, (TargetPlatform)platform });
        AssetBuffer expected = actual;
#endif
        // Original and recovered processes write independent transcripts. The
        // PowerShell caller must compare complete files, not these self-checks.
        Transcript.WriteLine(current + "\\t" + Data(Encoding.UTF8.GetBytes(xml.OuterXml)) +
            "\\t" + Data(actual.InstanceData) + "\\t" + Data(actual.RelocationData) + "\\t" + Data(actual.ImportsData));''')
replace_once('''            Assembly recovered = Assembly.LoadFrom(args[1]);
            Preflight(recovered);''','''            string transcriptPath = Environment.GetEnvironmentVariable("XML_PRODUCT_TRANSCRIPT");
            if (String.IsNullOrEmpty(transcriptPath)) throw new Exception("XML_PRODUCT_TRANSCRIPT is required");
            Transcript = new StreamWriter(transcriptPath, false, new UTF8Encoding(false));
#if ORACLE
            // Route declaration count only; no claim that the original host
            // inspected the production image.
            foreach (Root root in Roots) Methods.Add(root.Name, null);
#else
            Assembly recovered = Assembly.GetEntryAssembly();
            if (recovered.GetName().Name != "NewModCompiler" || recovered.GetModules().Length != 1 ||
                typeof(AssetBuffer).Assembly != recovered ||
                !String.Equals(Path.GetFullPath(recovered.Location), Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase))
                throw new Exception("XML fixture must execute in the isolated entry EXE with EXE-owned Core types");
            Preflight(recovered);
#endif''')
replace_once('''            if (SelectedRoots.Count != 0) {
                Assembly source = Assembly.LoadFrom(args[0]);
                Type pluginType = source.GetType("BinaryAssetBuilder.XmlCompiler.Plugin", true, false);
                original = (IAssetBuilderPlugin)Activator.CreateInstance(pluginType);
            }''','''#if ORACLE
            if (SelectedRoots.Count != 0) {
                Assembly source = Assembly.LoadFrom(args[0]);
                Type pluginType = source.GetType("BinaryAssetBuilder.XmlCompiler.Plugin", true, false);
                original = (IAssetBuilderPlugin)Activator.CreateInstance(pluginType);
            }
#endif''')
replace_once('''        } finally {
            if (args.Length >= 3) WriteResult''','''        } finally {
            if (Transcript != null) Transcript.Dispose();
            if (args.Length >= 3) WriteResult''')
replace_once('''        text.Append(",\\"scope\\":\\"all-route metadata preflight and selected-root default/completion smoke only\\"");''','''#if ORACLE
        text.Append(",\\"scope\\":\\"original XML default/completion transcript only; production metadata not inspected\\"");
#else
        text.Append(",\\"scope\\":\\"production XML default/completion transcript; external full transcript comparison required; not group full matrices\\"");
#endif''')
replace_once('''        text.Append(",\\"fullMetadataRoutes\\":").Append(Methods.Count);''','''#if ORACLE
        text.Append(",\\"fullMetadataRoutes\\":0");
#else
        text.Append(",\\"fullMetadataRoutes\\":").Append(Methods.Count);
#endif''')
args.output.write_text(source, encoding="utf-8")
print("Prepared 300-route XML smoke binding; complete per-group matrices remain separate")


