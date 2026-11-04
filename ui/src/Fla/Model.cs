// C# binary-FLA model. Serialization names follow JPEXS FLA Convert 3.0.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BinaryAssetBuilder.UiCompiler.Fla
{
    internal sealed class BinaryData
    {
        private readonly byte[] bytes;
        private readonly int offset, count;
        internal BinaryData(byte[] bytes) : this(bytes, 0, bytes.Length) { }
        private BinaryData(byte[] bytes, int offset, int count) { this.bytes = bytes; this.offset = offset; this.count = count; }
        internal int size() => count;
        internal int unsignedByte(int index) { if ((uint)index >= count) throw new EndOfStreamException(); return bytes[offset + index]; }
        internal BinaryData slice(int at, int length)
        {
            if (at < 0 || length < 0 || at > count - length) throw new EndOfStreamException();
            return new BinaryData(bytes, offset + at, length);
        }
        internal byte[] toByteArray() { byte[] result = new byte[count]; Buffer.BlockCopy(bytes, offset, result, 0, count); return result; }
    }
    internal sealed class FlaRecord
    {
        internal readonly string ClassName;
        internal readonly int Schema, Offset, PayloadOffset;
        private readonly BinaryData source;
        private int end;
        internal readonly Dictionary<string, object> Fields = new Dictionary<string, object>(StringComparer.Ordinal);
        internal FlaRecord(string name, int schema = 1, int offset = 0, int payload = 0, BinaryData data = null)
        { ClassName = name; Schema = schema; Offset = offset; PayloadOffset = payload; source = data; end = payload; }
        internal string getClassName() => ClassName;
        internal int getSchema() => Schema;
        internal int getOffset() => Offset;
        internal int getPayloadOffset() => PayloadOffset;
        internal void set(string key, object value) => Fields[key] = value;
        internal object get(string key) => Fields.TryGetValue(key, out object value) ? value : null;
        internal void finish(int position, bool complete) { end = position; Complete = complete; }
        internal bool Complete = true;
        internal bool isComplete() => Complete;
        internal BinaryData getConsumedData() => source == null ? new BinaryData(Array.Empty<byte>()) : source.slice(PayloadOffset, end - PayloadOffset);
        internal List<FlaRecord> getChildren() => get("children") as List<FlaRecord> ?? new List<FlaRecord>();
        internal int getLayerType()
        {
            int type = (int)Number("type", 0);
            if (Number("revision", 13) < 9) { if (Number("legacyMask", 0) != 0) return 4; if (type == 2) return 0; }
            return type;
        }
        internal FlaRecord getLayerParent()
        {
            long revision = Number("revision", 13);
            if (revision < 9 && (Number("legacyMask", 0) != 0 || Number("type", 0) == 1)) return null;
            return revision == 7 || revision == 8 ? get("legacyRelatedLayer") as FlaRecord : get("relatedLayer") as FlaRecord;
        }
        internal long Number(string key, long fallback = 0) => PortSupport.IsNumber(get(key)) ? get(key).longValue() : fallback;
        internal double Real(string key, double fallback = 0) => PortSupport.IsNumber(get(key)) ? get(key).doubleValue() : fallback;
        internal string Text(string key, string fallback = "") => get(key) as string ?? fallback;
        internal FlaRecord Record(string key) => get(key) as FlaRecord;
        internal List<FlaRecord> Records(string key) => get(key) as List<FlaRecord> ?? new List<FlaRecord>();
        internal int[] Words(string key, int count)
        {
            var values = get(key) as System.Collections.IList;
            if (values == null || values.Count != count) throw new InvalidDataException("Missing " + key + " on " + ClassName);
            int[] result = new int[count]; for (int i = 0; i < count; i++) result[i] = values[i].intValue(); return result;
        }
        internal double[] Matrix(string key = "matrixWords")
        {
            if (get(key) == null) return new double[] { 1, 0, 0, 1, 0, 0 };
            int[] w = Words(key, 6); return new double[] { w[0] / 65536.0, w[1] / 65536.0, w[2] / 65536.0, w[3] / 65536.0, w[4], w[5] };
        }
        internal FlaRecord Copy() { var copy = new FlaRecord(ClassName); foreach (var e in Fields) copy.Fields.Add(e.Key, e.Value); return copy; }
    }
}
