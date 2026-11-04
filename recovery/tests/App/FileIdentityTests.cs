using System;
using System.IO;
using System.Linq;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class FileIdentityTests
    {
        static int count;
        static void Check(bool value,string message) {count++;if(!value)throw new Exception("File identity: "+message);}
        public static int Run(string root)
        {
            count=0; Directory.CreateDirectory(Path.Combine(root,"child"));
            string path=Path.Combine(root,"schema.xsd"), nested=Path.Combine(root,"child","nested.xsd");
            File.WriteAllText(path,"before"); File.WriteAllText(nested,"nested");
            var before=FileIdentity.ReadDirectory(root).Concat(FileIdentity.ReadDirectory(Path.Combine(root,"child"))).ToList();
            Check(before.Count==2 && before.Any(file=>file.Path==nested),"Recursive metadata omitted an input");
            var first=before.Single(file=>file.Path==path);
            Check(first.Size==6 && first.FileId!=0 && first.ChangeTime!=0,"Directory record layout differs from Win32 file identity");
            var unchanged=FileIdentity.ReadDirectory(root).Single();
            Check(unchanged.FileId==first.FileId && unchanged.ChangeTime==first.ChangeTime && unchanged.LastWriteTime==first.LastWriteTime,"Read-only enumeration changed identity");
            DateTime stamp=File.GetLastWriteTimeUtc(path); Thread.Sleep(30);
            File.WriteAllText(path,"after!"); File.SetLastWriteTimeUtc(path,stamp);
            var changed=FileIdentity.ReadDirectory(root).Single();
            Check(changed.Size==first.Size && changed.LastWriteTime==first.LastWriteTime && changed.ChangeTime!=first.ChangeTime,"Same-length/restored-mtime write was not invalidated");
            using(var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Check(FileIdentity.ReadDirectory(root).Single().Size==6,"Identity attempted to open a locked file body");
            string replacement=Path.Combine(root,"replacement"); File.WriteAllText(replacement,"after!");
            File.SetLastWriteTimeUtc(replacement,stamp); File.Delete(path); File.Move(replacement,path);
            var replaced=FileIdentity.ReadDirectory(root).Single();
            Check(replaced.FileId!=changed.FileId,"Replacing an input retained its old identity");
            File.Delete(nested);
            Check(FileIdentity.ReadDirectory(root).Concat(FileIdentity.ReadDirectory(Path.Combine(root,"child"))).ToList().Count==1,"Removed input remained in metadata closure");
            return count;
        }
    }
}
