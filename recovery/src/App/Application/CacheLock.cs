using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NewModCompiler.Application
{
    internal sealed class CacheLock : IDisposable
    {
        Semaphore semaphore;
        CacheLock(Semaphore value) { semaphore = value; }
        internal static CacheLock Acquire(string path)
        {
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).TrimEnd('\\','/').ToUpperInvariant()))).Replace("-", "");
            var value = new Semaphore(1, 1, "Local\\NewModCompiler-Cache-" + key);
            try {
                if (!value.WaitOne(0)) throw new IOException("Another compiler owns this cache.");
                return new CacheLock(value);
            } catch { value.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (semaphore == null) return;
            try { semaphore.Release(); } finally { semaphore.Dispose(); semaphore = null; }
        }
    }
}
