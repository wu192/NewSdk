// Port of the MFC archive protocol described by JPEXS FLA Convert 3.0.0.
// Copyright (C) 2026 JPEXS. LGPL-2.1-or-later; see ui/licenses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BinaryAssetBuilder.UiCompiler.Fla
{
    internal sealed class MfcArchive
    {
        private readonly BinaryData data;
        private readonly string path;
        private readonly Dictionary<string, FlaRecordDecoder> decoders;
        private readonly List<object> references = new List<object> { null };
        private int position, depth;
        internal MfcArchive(BinaryData data, string path, Dictionary<string, FlaRecordDecoder> decoders) { this.data = data; this.path = path; this.decoders = decoders; }
        internal int getPosition() => position;
        internal int getRemaining() => data.size() - position;
        internal int getContentsRevision() => 0x3f;
        internal Encoding getAnsiCharset() => Encoding.GetEncoding(1252);
        internal InvalidDataException error(string message) => new InvalidDataException(path + " @0x" + position.ToString("x") + ": " + message);
        private void Require(int count) { if (count < 0 || count > getRemaining()) throw error("Truncated archive."); }
        internal int readUI8() { Require(1); return data.unsignedByte(position++); }
        internal int peekUI8() { Require(1); return data.unsignedByte(position); }
        internal int peekUI16() { Require(2); return data.unsignedByte(position) | data.unsignedByte(position + 1) << 8; }
        internal int readUI16() { Require(2); return readUI8() | readUI8() << 8; }
        internal int readI16() => (short)readUI16();
        internal long readUI32() { Require(4); return (long)readUI16() | (long)readUI16() << 16; }
        internal int readI32() => unchecked((int)readUI32());
        internal long readI64() { Require(8); return readUI32() | readUI32() << 32; }
        internal float readFloat() => BitConverter.ToSingle(BitConverter.GetBytes(readI32()), 0);
        internal double readDouble() => BitConverter.Int64BitsToDouble(readI64());
        internal BinaryData readData(int count) { Require(count); BinaryData result = data.slice(position, count); position += count; return result; }
        internal BinaryData readData(long count) { if (count < 0 || count > getRemaining()) throw error("Invalid data length."); return readData((int)count); }
        internal int readCount() { int count = readUI16(); return checkedCount(count == 0xffff ? readUI32() : count, 0); }
        internal int checkedCount(long count, int minBytes)
        { if (count < 0 || count > 1000000 || minBytes > 0 && count > getRemaining() / minBytes) throw error("Invalid collection count."); return (int)count; }
        internal sealed class StringHeader
        {
            private readonly long length; private readonly bool unicode;
            internal StringHeader(long length, bool unicode) { this.length = length; this.unicode = unicode; }
            internal long getLength() => length;
            internal bool isUnicode() => unicode;
            internal long getByteLength() => length * (unicode ? 2 : 1);
        }
        private long StringLength() { int small = readUI8(); if (small < 255) return small; int medium = readUI16(); return medium == 0xfffe ? -1 : medium == 0xffff ? readUI32() : medium; }
        internal StringHeader readStringHeader()
        {
            long length = StringLength(); bool unicode = length == -1; if (unicode) length = StringLength();
            if (length == -1) throw error("Repeated Unicode string marker."); if (length == uint.MaxValue) length = 0;
            return new StringHeader(length, unicode);
        }
        internal string readString()
        { StringHeader h = readStringHeader(); return (h.isUnicode() ? Encoding.Unicode : getAnsiCharset()).GetString(readData(h.getByteLength()).toByteArray()); }
        internal string readStreamName(bool unicode, bool trailingNul)
        {
            int length = readUI8(); if (length < 1 || length > 31) throw error("Invalid stream name length.");
            string name = (unicode ? Encoding.Unicode : getAnsiCharset()).GetString(readData(length * (unicode ? 2 : 1)).toByteArray());
            if (trailingNul) name = name.TrimEnd('\0'); if (name.Length == 0 || name.IndexOf('\0') >= 0) throw error("Invalid stream name."); return name;
        }
        internal string readZeroTerminatedIdentifier(int maxLength)
        {
            var text = new StringBuilder(); for (int i = 0; i <= maxLength; i++) { int c = readUI8(); if (c == 0) return text.ToString(); if (c > 127 || i == maxLength) throw error("Invalid identifier."); text.Append((char)c); } throw error("Unterminated identifier.");
        }
        private sealed class ClassInfo { internal string Name; internal int Schema; }
        private object Reference(long index) { if (index < 0 || index >= references.Count) throw error("Invalid MFC reference."); return references[(int)index]; }
        internal FlaRecord readObject() => ReadObject(unused => { });
        internal FlaRecord readObjectInto(FlaRecord owner, string field) => ReadObject(value => owner.set(field, value));
        private FlaRecord ReadObject(Action<FlaRecord> target)
        {
            int start = position, small = readUI16(); long tag = small == 0x7fff ? readUI32() : (small & 0x8000) != 0 ? 0x80000000L | (small & 0x7fff) : small;
            ClassInfo type;
            if (small == 0xffff)
            {
                int schema = readUI16(), length = readUI16(); if (length < 1 || length > 63) throw error("Invalid class name length.");
                string name = Encoding.ASCII.GetString(readData(length).toByteArray()); type = new ClassInfo { Name = name, Schema = schema }; references.Add(type);
            }
            else if ((tag & 0x80000000L) != 0) { type = Reference(tag & 0x7fffffff) as ClassInfo; if (type == null) throw error("Class reference points to an object."); }
            else { object value = Reference(tag); if (value != null && !(value is FlaRecord)) throw error("Object reference points to a class."); target((FlaRecord)value); return (FlaRecord)value; }
            var record = new FlaRecord(type.Name, type.Schema, start, position, data); references.Add(record); target(record);
            if (++depth > 128 || references.Count > 1000000) throw error("Archive object limit.");
            try { if (!decoders.TryGetValue(type.Name, out FlaRecordDecoder decoder)) throw error("Unsupported class " + type.Name); decoder(this, record); record.finish(position, true); return record; }
            finally { depth--; }
        }
        internal List<FlaRecord> readObjectList(FlaRecord owner, string field)
        {
            var list = new List<FlaRecord>(); owner.set(field, list);
            while (ReadObject(value => { if (value != null) list.Add(value); }) != null) if (list.Count > 1000000) throw error("Object list limit.");
            return list;
        }
        internal List<FlaRecord> readObjectList(FlaRecord owner, string field, int count)
        { checkedCount(count, 0); var list = new List<FlaRecord>(); owner.set(field, list); for (int i = 0; i < count; i++) ReadObject(list.Add); return list; }
        internal FlaRecord readInline(FlaRecord owner, string field, FlaRecordDecoder decoder) => readInline(field, value => owner.set(field, value), decoder);
        internal FlaRecord readInline(string name, Func<FlaRecord, bool> target, FlaRecordDecoder decoder) => readInline(name, value => { target(value); }, decoder);
        internal FlaRecord readInline(string name, Action<FlaRecord> target, FlaRecordDecoder decoder)
        {
            var record = new FlaRecord(name, 0, position, position, data); target(record);
            if (++depth > 128) throw error("Inline record nesting limit.");
            try { decoder(this, record); record.finish(position, true); return record; } finally { depth--; }
        }
    }
}
