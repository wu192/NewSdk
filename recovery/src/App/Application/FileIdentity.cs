using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NewModCompiler.Application
{
    // NTFS directory records include ChangeTime and FileId. Unlike length/mtime,
    // these detect replacement and writes followed by restoring LastWriteTime.
    // One metadata query returns a batch; schema identity never reads file bodies.
    internal sealed class FileIdentity
    {
        internal string Path;
        internal long VolumeSerial, FileId, ChangeTime, LastWriteTime, Size;

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)]
        static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
            IntPtr information, uint size);

        internal static IList<FileIdentity> ReadDirectory(string directory)
        {
            var result=new List<FileIdentity>();
            const int bufferSize=65536;
            IntPtr buffer=Marshal.AllocHGlobal(bufferSize);
            try
            {
                using(var handle=CreateFile(directory,0x81,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero))
                {
                    if(handle.IsInvalid)
                    {
                        int error=Marshal.GetLastWin32Error();
                        if(error==2 || error==3)throw new DirectoryNotFoundException(directory);
                        throw new Win32Exception(error,directory);
                    }
                    if(!GetFileInformationByHandleEx(handle,18,buffer,bufferSize))throw new Win32Exception(Marshal.GetLastWin32Error(),directory);
                    long volume=Marshal.ReadInt64(buffer,0);
                    while(true)
                    {
                        // FILE_ID_BOTH_DIR_INFO (10), directory cursor advances.
                        if(!GetFileInformationByHandleEx(handle,10,buffer,bufferSize))
                        {
                            int error=Marshal.GetLastWin32Error();
                            if(error==18)break; // ERROR_NO_MORE_FILES
                            throw new Win32Exception(error,directory);
                        }
                        int offset=0;
                        while(true)
                        {
                            IntPtr entry=IntPtr.Add(buffer,offset);
                            string name=Marshal.PtrToStringUni(IntPtr.Add(entry,104),Marshal.ReadInt32(entry,60)/2);
                            if(name!="." && name!="..")
                            {
                                string path=System.IO.Path.Combine(directory,name);
                                var attributes=(FileAttributes)Marshal.ReadInt32(entry,56);
                                if((attributes&FileAttributes.Directory)==0) result.Add(new FileIdentity {Path=path,VolumeSerial=volume,FileId=Marshal.ReadInt64(entry,96),
                                    ChangeTime=Marshal.ReadInt64(entry,32),LastWriteTime=Marshal.ReadInt64(entry,24),Size=Marshal.ReadInt64(entry,40)});
                            }
                            int next=Marshal.ReadInt32(entry,0);
                            if(next==0)break;
                            offset+=next;
                        }
                    }
                }
                return result;
            }
            finally {Marshal.FreeHGlobal(buffer);}
        }
    }
}
