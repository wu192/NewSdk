using System;
using System.IO;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class LegacyCacheStoreTests
    {
        static int count;
        static void Check(bool value, string message) { count++; if (!value) throw new Exception("Legacy cache: " + message); }
        static void Put(string file, string value) { Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, value); }
        public static int Run(string root)
        {
            count=0;
            string shared=Path.Combine(root,"sessions"), streams=Path.Combine(root,"streams");
            string session=Path.Combine(shared,LegacyCacheStore.SessionName), data=Path.Combine(streams,"mod","cdata","one.cdata");
            using(var store=new LegacyCacheStore(shared)) {
                Check(!store.CanLoad(),"Missing BAB session was a hit");
                Put(session,"<sc d=\"first\"/>"); Put(data,"payload");
                Check(store.CanLoad(),"Original BAB session without a sidecar was rejected");
                store.SaveData(streams,new[]{data},CancellationToken.None); store.Saved(false);
                Check(!File.Exists(Path.Combine(shared,".NewModCompiler","session.identity")),"Session identity sidecar was created");
                string restored=Path.Combine(root,"restored","one.cdata");
                Check(store.RestoreData(Path.Combine("mod","cdata","one.cdata"),restored,CancellationToken.None)==restored && File.ReadAllText(restored)=="payload","CDATA was not restored on demand");
                File.WriteAllText(restored,"modified");
                Check(File.ReadAllText(data)=="payload","Restoring CDATA shared mutable output storage");
            }
            using(var incompatible=new LegacyCacheStore(shared)) Check(incompatible.CanLoad(),"Existing BAB session was rejected");
            Put(session+".deflate","compressed");
            using(var store=new LegacyCacheStore(shared)) store.Saved(true);
            Check(!File.Exists(session) && File.ReadAllText(session+".deflate")=="compressed","Compression switch kept stale active XML");
            string original=Path.Combine(root,"original"); Put(Path.Combine(original,LegacyCacheStore.SessionName),"<sc d=\"old\"/>"); Put(Path.Combine(original,"mod","cdata","old.cdata"),"old payload");
            using(var store=new LegacyCacheStore(original)) {
                Check(store.CanLoad(),"Existing BAB cache was rejected");
                string restored=Path.Combine(root,"old-import.cdata");
                Check(store.RestoreData(Path.Combine("mod","cdata","old.cdata"),restored,CancellationToken.None)==restored && File.ReadAllText(restored)=="old payload","Original BAB CDATA was not imported");
            }
            return count;
        }
    }
}
