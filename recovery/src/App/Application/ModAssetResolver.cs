using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NewModCompiler.Application
{
    // Output semantics follow SDK/tools/Source/ModAssetResolver.dpr (Bibber).
    // Reference streams are explicit inputs; no registry, extraction or SDK writes.
    public static class ModAssetResolver
    {
        public static int Apply(string manifest,string builtMods,CancellationToken cancellation)
        {
            return Apply(manifest,builtMods,Path.Combine(builtMods,"Worldbuilder.manifest"),cancellation);
        }
        public static int Apply(string manifest,string builtMods,string worldbuilderManifest,CancellationToken cancellation)
        {
            return Apply(manifest,builtMods,worldbuilderManifest,null,cancellation);
        }
        internal static int Apply(string manifest,string builtMods,string worldbuilderManifest,WorldbuilderPayload payload,CancellationToken cancellation)
        {
            return new Resolver(manifest,builtMods,worldbuilderManifest,payload,cancellation).Run();
        }
        sealed class Resolver
        {
            readonly string path;
            readonly CancellationToken cancellation;
            readonly Manifest mod, world;
            readonly Manifest[] external;
            readonly List<byte[]> inserted=new List<byte[]>(), refs=new List<byte[]>();
            readonly MemoryStream names=new MemoryStream(),sources=new MemoryStream();
            readonly List<int> insertedIndices=new List<int>();
            long insertedBinLength;
            readonly HashSet<ulong> complete=new HashSet<ulong>(),active=new HashSet<ulong>();
            readonly WorldbuilderPayload worldBin;
            readonly WorldbuilderPayload worldImp,worldRelo;
            public Resolver(string path,string builtMods,string worldbuilderManifest,WorldbuilderPayload payload,CancellationToken cancellation)
            {
                this.path=path;this.cancellation=cancellation;cancellation.ThrowIfCancellationRequested();
                mod=new Manifest(path);
                external=new[]{new Manifest(Path.Combine(builtMods,"Static.manifest")),new Manifest(Path.Combine(builtMods,"Global.manifest")),new Manifest(Path.Combine(builtMods,"Audio.manifest"))};
                world=new Manifest(worldbuilderManifest);
                worldBin=payload ?? new WorldbuilderPayload(Path.ChangeExtension(worldbuilderManifest,".bin"));
                worldBin.ValidateLength(PayloadLength(world,8));
                worldImp=new WorldbuilderPayload(ValidatePayload(world,".imp",10));worldRelo=new WorldbuilderPayload(ValidatePayload(world,".relo",9));
            }
            public int Run()
            {
                byte[] modRefs=(byte[])mod.References.Clone();
                for(int offset=0;offset<modRefs.Length;offset+=8) {
                    cancellation.ThrowIfCancellationRequested();uint type=Read(modRefs,offset),id=Read(modRefs,offset+4);
                    // The original leaves local polymorphic references unchanged.
                    if(mod.Find(type,id)>=0) continue;
                    uint resolved=Resolve(type,id);
                    if(resolved!=0) Write(modRefs,offset,resolved);
                }
                byte[] header=(byte[])mod.Header.Clone();
                Write(header,12,checked((uint)(mod.Assets.Count+inserted.Count)));
                Write(header,16,checked(Read(header,16)+(uint)insertedBinLength));
                Write(header,32,checked((uint)(refs.Count*8+modRefs.Length)));
                Write(header,40,checked((uint)(names.Length+mod.Names.Length)));
                Write(header,44,checked((uint)(sources.Length+mod.Sources.Length)));
                foreach(byte[] asset in inserted) {
                    Write(header,20,Math.Max(Read(header,20),Read(asset,32)));
                    Write(header,24,Math.Max(Read(header,24),Read(asset,36)));
                    Write(header,28,Math.Max(Read(header,28),Read(asset,40)));
                }
                using(var output=new MemoryStream()) {
                    Append(output,header);foreach(byte[] asset in inserted)Append(output,asset);
                    foreach(byte[] original in mod.Assets) {
                        byte[] asset=(byte[])original.Clone();
                        Write(asset,16,checked(Read(asset,16)+(uint)(refs.Count*8)));
                        Write(asset,24,checked(Read(asset,24)+(uint)names.Length));
                        Write(asset,28,checked(Read(asset,28)+(uint)sources.Length));Append(output,asset);
                    }
                    foreach(byte[] reference in refs)Append(output,reference);
                    Append(output,modRefs);Append(output,mod.Streams);Append(output,names.ToArray());Append(output,mod.Names);Append(output,sources.ToArray());Append(output,mod.Sources);
                    // Validate all payloads before changing any staging file.
                    string oldBin=ValidatePayload(mod,".bin",8),oldImp=ValidatePayload(mod,".imp",10),oldRelo=ValidatePayload(mod,".relo",9);
                    cancellation.ThrowIfCancellationRequested();
                    File.WriteAllBytes(path,output.ToArray());
                    if(inserted.Count!=0) {
                        SavePayload(oldBin,worldBin,8);SavePayload(oldImp,worldImp,10);SavePayload(oldRelo,worldRelo,9);
                    }
                }
                return inserted.Count;
            }
            uint Resolve(uint type,uint id)
            {
                cancellation.ThrowIfCancellationRequested();
                foreach(Manifest source in external) {int found=source.Find(type,id);if(found>=0)return Read(source.Assets[found],0);}
                int index=world.Find(type,id);if(index<0)return 0;
                byte[] original=world.Assets[index];uint actual=Read(original,0);ulong identity=((ulong)id<<32)|actual;
                if(complete.Contains(identity))return actual;
                if(!active.Add(identity))throw new InvalidDataException("Worldbuilder dependency cycle cannot be resolved by the original asset insertion algorithm.");
                int referenceOffset=checked((int)Read(original,16)),count=checked((int)Read(original,20));
                for(int i=0;i<count;i++)Resolve(Read(world.References,referenceOffset+i*8),Read(world.References,referenceOffset+i*8+4));
                byte[] asset=(byte[])original.Clone();
                Write(asset,16,checked((uint)(refs.Count*8)));Write(asset,24,checked((uint)names.Length));Write(asset,28,checked((uint)sources.Length));
                // Recursive CheckAsset does not rewrite the inserted references.
                for(int i=0;i<count;i++)refs.Add(Slice(world.References,referenceOffset+i*8,8));
                Append(names,Terminated(world.Names,Read(original,24)));Append(sources,Terminated(world.Sources,Read(original,28)));
                insertedIndices.Add(index);insertedBinLength=checked(insertedBinLength+Read(original,32));
                inserted.Add(asset);active.Remove(identity);complete.Add(identity);return actual;
            }
            void SavePayload(string destination,WorldbuilderPayload source,int sizeWord)
            {
                string temporary=destination+".tmp-"+Guid.NewGuid().ToString("N");
                try {
                    using(var original=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        byte[] buffer=new byte[64*1024];
                        Copy(original,output,4,buffer);
                        foreach(int index in insertedIndices)
                            source.CopyTo(output,world.Offset(index,sizeWord),Read(world.Assets[index],sizeWord*4),cancellation);
                        Copy(original,output,original.Length-4,buffer);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    File.Replace(temporary,destination,null);
                }
                finally {if(File.Exists(temporary))File.Delete(temporary);}
            }
            void Copy(Stream input,Stream output,long count,byte[] buffer)
            {
                while(count>0) {
                    cancellation.ThrowIfCancellationRequested();
                    int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,count));
                    if(read==0)throw new EndOfStreamException("MOD payload changed during asset resolution.");
                    output.Write(buffer,0,read);count-=read;
                }
            }
        }
        sealed class Manifest
        {
            public readonly string PathName;
            public readonly byte[] Header,References,Streams,Names,Sources;
            public readonly List<byte[]> Assets=new List<byte[]>();
            public Manifest(string path)
            {
                PathName=path;byte[] bytes=File.ReadAllBytes(path);
                if(bytes.Length<48 || bytes[0]!=0 || bytes[1]!=1 || bytes[2]!=6 || bytes[3]!=0)throw new InvalidDataException("Asset resolver requires linked little-endian RA3 v6 manifests.");
                Header=Slice(bytes,0,48);int count=checked((int)Read(bytes,12));long end=48L+48L*count+Read(bytes,32)+Read(bytes,36)+Read(bytes,40)+Read(bytes,44);
                if(end!=bytes.LongLength || Read(bytes,32)%8!=0)throw new InvalidDataException("Asset resolver manifest table length differs.");
                for(int i=0;i<count;i++)Assets.Add(Slice(bytes,48+i*48,48));
                int offset=checked(48+count*48);References=Take(bytes,ref offset,Read(bytes,32));Streams=Take(bytes,ref offset,Read(bytes,36));Names=Take(bytes,ref offset,Read(bytes,40));Sources=Take(bytes,ref offset,Read(bytes,44));
                foreach(byte[] asset in Assets) {
                    if(Read(asset,16)%8!=0 || (ulong)Read(asset,16)+8UL*Read(asset,20)>(ulong)References.Length)throw new InvalidDataException("Asset resolver reference range differs.");
                    Terminated(Names,Read(asset,24));Terminated(Sources,Read(asset,28));
                }
            }
            public int Find(uint type,uint id) {for(int i=0;i<Assets.Count;i++)if(Read(Assets[i],4)==id && Matches(type,Read(Assets[i],0)))return i;return -1;}
            public long Offset(int index,int word) {long result=4;for(int i=0;i<index;i++)result+=Read(Assets[i],word*4);return result;}
        }
        static bool Matches(uint requested,uint actual) {return requested==actual || requested==0xb22f87c9U && (actual==0xe3181c04U || actual==0xf0f08712U || actual==0xc2b1a262U);}
        static long PayloadLength(Manifest manifest,int sizeWord) {
            long result=4;foreach(byte[] asset in manifest.Assets)result+=Read(asset,sizeWord*4);return result;
        }
        static string ValidatePayload(Manifest manifest,string extension,int sizeWord) {
            string path=Path.ChangeExtension(manifest.PathName,extension);long expected=PayloadLength(manifest,sizeWord);
            if(new FileInfo(path).Length!=expected)throw new InvalidDataException("Asset resolver linked payload length differs.");return path;
        }
        static byte[] Terminated(byte[] buffer,uint offset) {int start=checked((int)offset),end=start;while(end<buffer.Length && buffer[end]!=0)end++;if(end>=buffer.Length)throw new InvalidDataException("Asset resolver string is not terminated.");return Slice(buffer,start,end-start+1);}
        static byte[] Take(byte[] data,ref int offset,uint count) {byte[] result=Slice(data,offset,checked((int)count));offset=checked(offset+result.Length);return result;}
        static byte[] Slice(byte[] data,int start,int count) {byte[] result=new byte[count];Buffer.BlockCopy(data,start,result,0,count);return result;}
        static void Append(Stream output,byte[] data) {output.Write(data,0,data.Length);}
        static uint Read(byte[] data,int offset) {return (uint)data[offset]|((uint)data[offset+1]<<8)|((uint)data[offset+2]<<16)|((uint)data[offset+3]<<24);}
        static void Write(byte[] data,int offset,uint value) {for(int i=0;i<4;i++)data[offset+i]=(byte)(value>>(8*i));}
    }
}
