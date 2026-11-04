using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NewModCompiler.Application
{
    internal static class FileTransfer
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

        // Only private, immutable cache snapshots use links. Published files and
        // restored CDATA have independent storage because callers may edit them.
        internal static void Snapshot(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target)) File.Delete(target);
            if (!CreateHardLink(target, source, IntPtr.Zero)) File.Copy(source, target);
        }

    }
}
