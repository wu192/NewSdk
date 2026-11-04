// SAGE FastHash arithmetic recovered on recovery/legacy-sdk-cache, src/Managed/Hash.
using System;
using System.Text;
namespace BinaryAssetBuilder.UiCompiler
{
    internal static class SageHash
    {
        internal static uint Symbol(string text)
        {
            // Game asset identifiers in this plugin use ASCII, independently of OS code page.
            foreach (char c in text) if (c > 127 || c == 0) throw new ArgumentException("SAGE asset identifiers must be ASCII.");
            return Data(Encoding.ASCII.GetBytes(text));
        }
        internal static uint Data(byte[] bytes) => Combine((uint)bytes.Length, bytes);
        internal static uint Combine(uint hash, byte[] bytes)
        {
            if (bytes.Length == 0) return 0; int at = 0; uint Read16(int p) => (uint)(bytes[p] | bytes[p + 1] << 8);
            unchecked
            {
                for (int blocks = bytes.Length >> 2; blocks != 0; blocks--) { hash += Read16(at); hash ^= (Read16(at + 2) ^ hash << 5) << 11; at += 4; hash += hash >> 11; }
                switch (bytes.Length & 3) { case 3: hash += Read16(at); hash ^= (((uint)(sbyte)bytes[at + 2] << 2) ^ hash) << 16; hash += hash >> 11; break; case 2: hash += Read16(at); hash ^= hash << 11; hash += hash >> 17; break; case 1: hash += (uint)(sbyte)bytes[at]; hash ^= hash << 10; hash += hash >> 1; break; }
                hash ^= hash << 3; hash += hash >> 5; hash ^= hash << 2; hash += hash >> 15; return hash ^ hash << 10;
            }
        }
    }
}
