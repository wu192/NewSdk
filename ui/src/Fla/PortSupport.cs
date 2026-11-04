// Serialization helpers for the C# port of JPEXS FLA Convert 3.0.0 (LGPL-2.1-or-later).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace BinaryAssetBuilder.UiCompiler.Fla
{
    internal static class PortSupport
    {
        internal static bool IsNumber(object v) => v is byte || v is sbyte || v is short || v is ushort || v is int || v is uint || v is long || v is ulong || v is float || v is double;
        internal static int intValue(this object v) => v is double || v is float ? unchecked((int)Convert.ToDouble(v)) : unchecked((int)Convert.ToInt64(v));
        internal static long longValue(this object v) => Convert.ToInt64(v);
        internal static double doubleValue(this object v) => Convert.ToDouble(v);
        internal static float floatValue(this object v) => Convert.ToSingle(v);
        internal static bool equals(this object a, object b) => object.Equals(a, b);
        internal static bool isEmpty<T>(this ICollection<T> list) => list.Count == 0;
        internal static bool isEmpty(this string s) => s.Length == 0;
        internal static int size<T>(this ICollection<T> list) => list.Count;
        internal static int length(this string s) => s.Length;
        internal static int indexOf(this string s, char c) => s.IndexOf(c);
        internal static char charAt(this string s, int i) => s[i];
        internal static bool startsWith(this string s, string prefix) => s.StartsWith(prefix, StringComparison.Ordinal);
        internal static bool endsWith(this string s, string suffix) => s.EndsWith(suffix, StringComparison.Ordinal);
        internal static string substring(this string s, int start) => s.Substring(start);
        internal static string substring(this string s, int start, int end) => s.Substring(start, end - start);
        internal static string toString(this object s) => Convert.ToString(s, System.Globalization.CultureInfo.InvariantCulture);
        internal static bool add<T>(this List<T> list, T item) { list.Add(item); return true; }
        internal static void addAll<T>(this List<T> list, IEnumerable<T> items) => list.AddRange(items);
        internal static T get<T>(this IList<T> list, int index) => list[index];
        internal static V get<K,V>(this IDictionary<K,V> map, K key) => map.TryGetValue(key, out V result) ? result : default(V);
        internal static void put<K,V>(this IDictionary<K,V> map, K key, V value) => map[key] = value;
        internal static void replaceAll<K,V>(this IDictionary<K,V> map, Func<K,V,V> f) { foreach (K key in map.Keys.ToArray()) map[key] = f(key, map[key]); }
        internal static StringBuilder append(this StringBuilder b, object v) => b.Append(v);
        internal static string Decode(byte[] bytes, Encoding encoding) => encoding.GetString(bytes);
    }
    internal static class Collections
    {
        internal static List<T> unmodifiableList<T>(List<T> list) => list;
        internal static Dictionary<K,V> unmodifiableMap<K,V>(Dictionary<K,V> map) => map;
    }
    internal static class StandardCharsets
    {
        internal static readonly Encoding UTF_16LE = Encoding.Unicode, US_ASCII = Encoding.ASCII, UTF_8 = new UTF8Encoding(false, true);
    }
    internal sealed class UnsupportedFlaRecordException : IOException { internal UnsupportedFlaRecordException(string message) : base(message) { } }
    internal delegate void FlaRecordDecoder(MfcArchive archive, FlaRecord record);
}
