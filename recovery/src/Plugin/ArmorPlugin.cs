using System;
using System.IO;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler
{
    // Explicitly registered for ArmorTemplate only. No original compiler calls.
    public sealed class Plugin : IAssetBuilderPlugin
    {
        private TargetPlatform platform;
        private string trace;
        private static readonly object TraceGate = new object();
        public void Initialize(object configObject, TargetPlatform target) { platform = target; trace = Environment.GetEnvironmentVariable("RA3_RECOVERED_ARMOR_TRACE"); }
        public void ReInitialize(object configObject, TargetPlatform target) { Initialize(configObject, target); }
        // Retain original compiler metadata so serialized manifests remain comparable.
        public uint GetAllTypesHash() { return 1424942948u; }
        public uint GetVersionNumber()
        {
            return platform == TargetPlatform.PlayStation3 ? 4102u :
                platform == TargetPlatform.Xbox360 ? 4101u : 4100u;
        }
        public ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            var info = new ExtendedTypeInformation();
            info.TypeId = typeId;
            info.Tokenized = true;
            if (typeId == 980180622u)
            {
                info.TypeName = "ArmorTemplate";
                info.TypeHash = 1679479598u;
                info.ProcessingHash = unchecked(info.TypeHash + GetVersionNumber());
            }
            return info;
        }
        public AssetBuffer ProcessInstance(InstanceDeclaration instance)
        {
            // The component checks type IDs and fails explicitly on other types.
            AssetBuffer result = ArmorCompiler.ProcessInstance(instance, platform);
            if (!String.IsNullOrEmpty(trace))
            {
                Directory.CreateDirectory(trace);
                // A unique stem preserves repeated invocations rather than overwriting them.
                string stem = Path.Combine(trace, Guid.NewGuid().ToString("N"));
                File.WriteAllText(stem + ".xml", instance.Node.OuterXml);
                File.WriteAllBytes(stem + ".bin", result.InstanceData);
                File.WriteAllBytes(stem + ".relo", result.RelocationData);
                File.WriteAllBytes(stem + ".imp", result.ImportsData);
                lock (TraceGate) File.AppendAllText(Path.Combine(trace, "calls.tsv"),
                    instance.Node.Attributes["id"].Value + "\t" + Path.GetFileName(stem) +
                    "\t" + (int)platform + Environment.NewLine);
            }
            return result;
        }
    }
}
