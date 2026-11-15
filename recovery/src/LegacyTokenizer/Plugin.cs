using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using BinaryAssetBuilder.Core;

[assembly: AssemblyVersion("1.0.3492.22559")]
[assembly: AssemblyTitle("BinaryAssetBuilder.Tokenizer")]

namespace BinaryAssetBuilder.Tokenizer
{
    // CLR 2 netmodule linked into the same PE as the recovered native compiler.
    // The P/Invoke targets this DLL; no second compiler DLL is deployed.
    public class Plugin : IAssetBuilderPlugin
    {
        private TargetPlatform platform;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeResult
        {
            public IntPtr Instance;
            public uint InstanceLength;
            public IntPtr Relocations;
            public uint RelocationLength;
            public IntPtr Imports;
            public uint ImportsLength;
            public IntPtr Error;
            public IntPtr Owner;
        }

        [DllImport("BinaryAssetBuilder.Tokenizer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LegacyTokenizerCompile(uint typeId, byte[] input,
            uint length, int platform, out NativeResult result);
        [DllImport("BinaryAssetBuilder.Tokenizer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LegacyTokenizerFree(IntPtr owner);

        public virtual void Initialize(object configSection, TargetPlatform target) { platform = target; }
        public virtual void ReInitialize(object configSection, TargetPlatform target) { Initialize(configSection, target); }
        public virtual uint GetAllTypesHash() { return 1424942948u; }
        public virtual uint GetVersionNumber()
        {
            return platform == TargetPlatform.PlayStation3 ? 4102u :
                platform == TargetPlatform.Xbox360 ? 4101u : 4100u;
        }
        public virtual ExtendedTypeInformation GetExtendedTypeInformation(uint typeId)
        {
            ExtendedTypeInformation info = new ExtendedTypeInformation();
            info.TypeId = typeId;
            info.Tokenized = true;
            switch (typeId)
            {
                case 2486173485u: info.TypeName = "GameObject"; info.TypeHash = 3415017856u; break;
                case 2496977262u: info.TypeName = "WeaponTemplate"; info.TypeHash = 216405646u; break;
                case 980180622u: info.TypeName = "ArmorTemplate"; info.TypeHash = 1679479598u; break;
            }
            if (info.TypeName != null) info.ProcessingHash = unchecked(info.TypeHash + GetVersionNumber());
            return info;
        }

        private static bool Context(XmlAttribute attribute)
        {
            return attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/" ||
                (attribute.NamespaceURI == "http://www.w3.org/XML/1998/namespace" &&
                 (attribute.LocalName == "base" || attribute.LocalName == "lang"));
        }
        private static void WriteString(BinaryWriter writer, string value)
        {
            // Match msclr::marshal_as<std::string> in the restored bridges.
            byte[] bytes = Encoding.Default.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        private static void WriteNode(BinaryWriter writer, XmlNode node)
        {
            WriteString(writer, node.LocalName);
            WriteString(writer, node.InnerText);
            int attributes = 0;
            foreach (XmlAttribute attribute in node.Attributes) if (!Context(attribute)) ++attributes;
            writer.Write(attributes);
            foreach (XmlAttribute attribute in node.Attributes)
            {
                if (Context(attribute)) continue;
                if (attribute.NamespaceURI.Length != 0) throw new NotSupportedException("Namespaced compiler attribute: " + attribute.Name);
                WriteString(writer, attribute.LocalName);
                WriteString(writer, attribute.Value);
            }
            int children = 0;
            foreach (XmlNode child in node.ChildNodes) if (child.NodeType == XmlNodeType.Element) ++children;
            writer.Write(children);
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.NamespaceURI != node.NamespaceURI) throw new NotSupportedException("Foreign compiler element: " + child.Name);
                WriteNode(writer, child);
            }
        }
        private static byte[] Copy(IntPtr source, uint length)
        {
            byte[] bytes = new byte[checked((int)length)];
            if (bytes.Length != 0) Marshal.Copy(source, bytes, 0, bytes.Length);
            return bytes;
        }
        public virtual AssetBuffer ProcessInstance(InstanceDeclaration declaration)
        {
            uint typeId = declaration.Handle.TypeId;
            if (typeId != 2486173485u && typeId != 2496977262u && typeId != 980180622u) return null;
            byte[] input;
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                WriteNode(writer, declaration.Node);
                writer.Flush();
                input = stream.ToArray();
            }
            NativeResult native;
            int status = LegacyTokenizerCompile(typeId, input, (uint)input.Length, (int)platform, out native);
            try
            {
                if (status == 2) throw new BinaryAssetBuilderException(ErrorCode.XmlFormattingError,
                    Marshal.PtrToStringAnsi(native.Error), new object[0]);
                if (status != 0) throw new NotSupportedException(Marshal.PtrToStringAnsi(native.Error));
                AssetBuffer buffer = new AssetBuffer();
                buffer.InstanceData = Copy(native.Instance, native.InstanceLength);
                buffer.RelocationData = Copy(native.Relocations, native.RelocationLength);
                buffer.ImportsData = Copy(native.Imports, native.ImportsLength);
                return buffer;
            }
            finally { LegacyTokenizerFree(native.Owner); }
        }
    }
}
