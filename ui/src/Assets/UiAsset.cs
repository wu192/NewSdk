using System;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder.Core;
namespace BinaryAssetBuilder.UiCompiler
{
    public sealed class CompiledUiAsset
    {
        public string TypeName { get; }
        public string Name { get; }
        public string Source { get; }
        public uint TypeId { get; }
        public uint TypeHash { get; }
        public uint InstanceId { get; }
        public uint InstanceHash { get; }
        public AssetBuffer Buffer { get; }
        internal CompiledUiAsset(string type, string name, string source, Action<Stream> writePayload, int aptId = 0)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0) throw new ArgumentException("An asset name is required.", nameof(name));
            TypeName = type; Name = name; Source = source; var metadata = UiCatalog.Find(type); TypeId = metadata.TypeId; TypeHash = metadata.TypeHash; InstanceId = SageHash.Symbol(name.ToLowerInvariant());
            int rootSize = type == "AptGeometryData" ? 16 : 12;
            byte[] instance;
            using (var finalAsset = new MemoryStream())
            {
                finalAsset.SetLength(rootSize); finalAsset.Position = rootSize;
                var payload = new AssetPayloadStream(finalAsset, rootSize);
                writePayload(payload); int payloadSize = checked((int)payload.Length);
                finalAsset.Position = finalAsset.Length;
                while ((finalAsset.Length & 3) != 0) finalAsset.WriteByte(0);
                instance = finalAsset.ToArray();
                Word(instance, 4, (uint)rootSize); Word(instance, 8, (uint)payloadSize);
                if (rootSize == 16) Word(instance, 12, unchecked((uint)aptId));
            }
            Buffer = new AssetBuffer { InstanceData = instance, RelocationData = new byte[] { 4, 0, 0, 0, 255, 255, 255, 255 }, ImportsData = Array.Empty<byte>() };
            InstanceHash = SageHash.Data(instance);
        }
        internal static void Word(byte[] bytes, int offset, uint value) { for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i)); }
    }
    internal static class UiCatalog
    {
        internal const uint ProcessingHash = 0x55493804;
        internal static readonly Tuple<string, uint, uint>[] Types = {
            Tuple.Create("AptAptData", 1816258268u, 3795385027u), Tuple.Create("AptConstData", 983359664u, 3876251416u),
            Tuple.Create("AptDatData", 1110676961u, 230038576u), Tuple.Create("AptGeometryData", 1034587168u, 1390234602u),
            Tuple.Create("Texture", SageHash.Symbol("Texture"), 2673866970u)
        };
        internal static ExtendedTypeInformation Find(string name)
        { foreach (var t in Types) if (t.Item1 == name) return new ExtendedTypeInformation { TypeName = name, TypeId = t.Item2, TypeHash = t.Item3, ProcessingHash = ProcessingHash, UseBuildCache = false }; throw new NotSupportedException(name); }
        internal static ExtendedTypeInformation Find(uint id) { foreach (var t in Types) if (t.Item2 == id) return Find(t.Item1); return null; }
        internal static uint AllTypesHash { get { uint hash = ProcessingHash; foreach (var t in Types) hash = SageHash.Combine(hash, BitConverter.GetBytes(t.Item3)); return hash; } }
    }
}
