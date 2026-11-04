using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    internal static class GameWorldbuilderDataTests
    {
        static int checks;
        static void Check(bool value,string message) {checks++;if(!value)throw new Exception("Game WorldBuilder data: "+message);}
        static void BigEndian(BinaryWriter output,uint value) {output.Write(new[]{(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value});}
        static void Archive(string file,IDictionary<string,byte[]> members)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            int table=16+members.Sum(pair=>8+Encoding.ASCII.GetByteCount(pair.Key)+1),size=table+members.Sum(pair=>pair.Value.Length);
            using(var output=new BinaryWriter(File.Create(file))) {
                output.Write(Encoding.ASCII.GetBytes("BIG4"));output.Write(size);BigEndian(output,(uint)members.Count);BigEndian(output,(uint)table);
                int offset=table;
                foreach(var member in members) {BigEndian(output,(uint)offset);BigEndian(output,(uint)member.Value.Length);output.Write(Encoding.ASCII.GetBytes(member.Key));output.Write((byte)0);offset+=member.Value.Length;}
                foreach(var member in members)output.Write(member.Value);
            }
        }
        static byte[] RefPack(bool wide,bool compressedSize)
        {
            int width=wide?4:3,header=2+width+(compressedSize?width:0);
            var data=new List<byte>{(byte)((wide?0x90:0x10)|(compressedSize?1:0)),0xfb};
            Action<int> size=value=> {for(int i=width-1;i>=0;i--)data.Add((byte)(value>>(8*i)));};
            if(compressedSize)size(header+15);
            size(143);
            data.AddRange(new byte[]{0xe0,65,66,67,68,0,0,0x80,0,0,0xc0,0,0,127,0xfc});
            return data.ToArray();
        }
        public static int Run(string root)
        {
            checks=0;string sdk=Path.Combine(root,"sdk"),mod=Path.Combine(root,"mod"),game=Path.Combine(root,"game"),archive=Path.Combine(game,"Data","WBData_12.big");
            Directory.CreateDirectory(sdk);Directory.CreateDirectory(mod);
            var request=new BuildRequest {SdkRoot=sdk,ModRoot=mod,GameRoot=game,OutputRoot=Path.Combine(root,"out")};
            var members=new Dictionary<string,byte[]>();
            foreach(string extension in new[]{".manifest",".bin",".imp",".relo"})members.Add("dAtA/WORLDBUILDER"+extension,Encoding.ASCII.GetBytes("original"+extension));
            Archive(archive,members);
            Check(GameWorldbuilderData.FindArchive(game)==archive,"Explicit game root did not resolve Data/WBData_12.big");
            GameWorldbuilderData.Validate(archive);
            string manifest=GameWorldbuilderData.Prepare(request,archive,CancellationToken.None),cache=Path.GetDirectoryName(manifest);
            foreach(var member in members)Check(File.ReadAllBytes(Path.Combine(cache,Path.GetFileName(member.Key.Replace('/',Path.DirectorySeparatorChar)))).SequenceEqual(member.Value),"Game BIG member bytes changed during extraction");
            DateTime sentinel=new DateTime(2005,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(manifest,sentinel);
            Check(GameWorldbuilderData.Prepare(request,archive,CancellationToken.None)==manifest && File.GetLastWriteTimeUtc(manifest)==sentinel,"Unchanged game archive was extracted again");
            string otherMod=Path.Combine(root,"other-mod");Directory.CreateDirectory(otherMod);
            var other=new BuildRequest {SdkRoot=sdk,ModRoot=otherMod,GameRoot=game,OutputRoot=Path.Combine(root,"other-out")};
            Check(cache==Path.Combine(sdk,"SuperCompilerCache","shared","neutral-worldbuilder"),"Reference cache is not SDK-wide");
            Check(GameWorldbuilderData.Prepare(other,archive,CancellationToken.None)==manifest && File.GetLastWriteTimeUtc(manifest)==sentinel,"Another MOD did not reuse the same unmodified reference files");
            string legacy=Path.Combine(CacheLayout.ForRequest(request).CacheRoot,"neutral-worldbuilder");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy));Directory.Move(cache,legacy);
            Check(GameWorldbuilderData.Prepare(request,archive,CancellationToken.None)==manifest && !Directory.Exists(legacy) && File.GetLastWriteTimeUtc(manifest)==sentinel,"A valid old MOD cache was not moved without recopying");
            Directory.CreateDirectory(legacy);
            foreach(string file in Directory.GetFiles(cache))File.Copy(file,Path.Combine(legacy,Path.GetFileName(file)));
            GameWorldbuilderData.Prepare(request,archive,CancellationToken.None);
            Check(!Directory.Exists(legacy) && File.GetLastWriteTimeUtc(manifest)==sentinel,"The current MOD retained a redundant valid legacy reference cache");
            Directory.Delete(cache,true);
            Task<string> first=Task.Run(()=>GameWorldbuilderData.Prepare(request,archive,CancellationToken.None));
            Task<string> second=Task.Run(()=>GameWorldbuilderData.Prepare(other,archive,CancellationToken.None));
            Check(Task.WaitAll(new Task[]{first,second},10000) && first.Result==manifest && second.Result==manifest,"Concurrent MOD first use did not converge on one shared cache");
            using(GameWorldbuilderData.Acquire(request,CancellationToken.None))
            using(var cancelled=new CancellationTokenSource())
            using(var started=new ManualResetEventSlim()) {
                Task<bool> waiting=Task.Run(()=> {
                    started.Set();
                    try {using(GameWorldbuilderData.Acquire(other,cancelled.Token))return false;}
                    catch(OperationCanceledException) {return true;}
                });
                Check(started.Wait(5000) && !waiting.Wait(100),"The shared reference lease did not block a second MOD");
                cancelled.Cancel();
                Check(waiting.Wait(5000) && waiting.Result,"A cancelled MOD could not leave the shared reference lock wait");
            }
            File.Delete(Path.Combine(cache,"Worldbuilder.imp"));
            GameWorldbuilderData.Prepare(request,archive,CancellationToken.None);
            Check(File.Exists(Path.Combine(cache,"Worldbuilder.imp")),"Partial cached extraction was not repaired");
            members["dAtA/WORLDBUILDER.bin"]=Encoding.ASCII.GetBytes("updated.bin");Archive(archive,members);File.SetLastWriteTimeUtc(archive,DateTime.UtcNow.AddSeconds(2));
            GameWorldbuilderData.Prepare(request,archive,CancellationToken.None);
            Check(File.ReadAllBytes(Path.Combine(cache,"Worldbuilder.bin")).SequenceEqual(members["dAtA/WORLDBUILDER.bin"]),"Changed game archive reused stale extracted bytes");
            foreach(bool wide in new[]{false,true})foreach(bool sized in new[]{false,true}) {
                members["dAtA/WORLDBUILDER.bin"]=RefPack(wide,sized);Archive(archive,members);File.SetLastWriteTimeUtc(archive,DateTime.UtcNow.AddSeconds((wide?4:8)+(sized?1:0)));
                GameWorldbuilderData.Prepare(request,archive,CancellationToken.None);
                Check(File.ReadAllBytes(Path.Combine(cache,"Worldbuilder.bin")).SequenceEqual(Encoding.ASCII.GetBytes("ABC"+new string('D',140))),"RefPack literal/three match forms or size-header variant failed");
            }
            members.Remove("dAtA/WORLDBUILDER.imp");Archive(archive,members);
            try {GameWorldbuilderData.Validate(archive);throw new Exception("Missing game BIG stream member accepted");}
            catch(BuildFailure failure) {Check(failure.Code=="PACKAGE_NEUTRAL_REFERENCE_MISSING","Missing game member lost its reference diagnostic");}
            Check(!Directory.GetDirectories(Path.GetDirectoryName(cache),"*.import-*").Any(),"Extraction left a partial staging directory");
            using(var cancellation=new CancellationTokenSource()) {
                cancellation.Cancel();
                try {GameWorldbuilderData.Prepare(request,archive,cancellation.Token);throw new Exception("Cancelled game extraction accepted");}
                catch(OperationCanceledException) {Check(true,"Cancelled extraction");}
            }
            return checks;
        }
    }
}
