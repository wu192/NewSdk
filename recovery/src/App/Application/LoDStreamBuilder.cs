using System;
using System.IO;
using System.Text;
using System.Threading;

namespace NewModCompiler.Application
{
    // RA3 PC v6 only. Original: SDK/tools/LoDStreamBuilder.exe, version-six
    // branch 0040f954..0040ff3f. Never modifies the main linked stream.
    public static class LoDStreamBuilder
    {
        public static string[] Create(string manifest, CancellationToken cancellation)
        { return Create(manifest, cancellation, null); }
        internal static string[] Create(string manifest, CancellationToken cancellation, string outputDirectory)
        {
            manifest=Path.GetFullPath(manifest);
            string stem=Path.Combine(outputDirectory ?? Path.GetDirectoryName(manifest),Path.GetFileNameWithoutExtension(manifest));
            if (outputDirectory != null) Directory.CreateDirectory(outputDirectory);
            string leaf=Path.GetFileName(manifest);
            if(!string.Equals(Path.GetExtension(manifest),".manifest",StringComparison.OrdinalIgnoreCase) ||
               leaf.IndexOfAny(new[]{'\\','/',':','\0'})>=0 || Array.Exists(leaf.ToCharArray(),c=>c<32 || c>126))
                throw new InvalidDataException("LoD requires an ASCII .manifest basename.");
            cancellation.ThrowIfCancellationRequested();
            byte[] data=File.ReadAllBytes(manifest);
            if(data.Length<48 || data[0]!=0 || data[1]!=1 || data[2]!=6 || data[3]!=0)
                throw new InvalidDataException("LoD requires a linked little-endian RA3 v6 manifest.");
            uint count=Read(data,12),references=Read(data,32),streams=Read(data,36),names=Read(data,40),sources=Read(data,44);
            long tableEnd=48L+48L*count,refsEnd=tableEnd+references,namesStart=refsEnd+streams;
            if(namesStart+names+sources!=data.LongLength)
                throw new InvalidDataException("LoD manifest tables exceed or do not consume the source file.");
            byte[] dependency=Encoding.ASCII.GetBytes("\x02"+leaf+"\0");
            long length=refsEnd+dependency.Length+names+sources;
            if(length>int.MaxValue) throw new InvalidDataException("LoD manifest exceeds supported length.");
            byte[] output=new byte[(int)length];
            Buffer.BlockCopy(data,0,output,0,(int)refsEnd);
            for(long i=0;i<count;i++) {
                cancellation.ThrowIfCancellationRequested();
                Array.Clear(output,checked((int)(48+48*i+32)),12);
            }
            Write(output,36,(uint)dependency.Length);
            Buffer.BlockCopy(dependency,0,output,(int)refsEnd,dependency.Length);
            // Deliberate original-tool bug fix: copy exactly the logical tables,
            // preserving the final NUL. Never reproduce the Delphi array+1
            // overrun (documented in P7_LOD_ORIGINAL_TAIL.md).
            Buffer.BlockCopy(data,(int)namesStart,output,(int)refsEnd+dependency.Length,checked((int)(names+sources)));
            // v6 emits precisely the original stream checksum, FOUR bytes.
            // v7's additional zero word must not leak into RA3 v6 payloads.
            byte[] empty=new byte[4];Buffer.BlockCopy(data,4,empty,0,4);
            string[] paths=new string[8];int next=0;
            foreach(string suffix in new[]{"_l","_m"}) foreach(string extension in new[]{".manifest",".bin",".imp",".relo"}) {
                string path=stem+suffix+extension;
                if(File.Exists(path) || Directory.Exists(path)) throw new IOException("LoD output already exists: "+path);
                paths[next++]=path;
            }
            // Caller owns an isolated staging workspace and its failure cleanup.
            foreach(string path in paths) {
                cancellation.ThrowIfCancellationRequested();
                byte[] bytes=Path.GetExtension(path)==".manifest" ? output : empty;
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) stream.Write(bytes,0,bytes.Length);
            }
            return paths;
        }
        static uint Read(byte[] data,int offset) {return (uint)data[offset]|((uint)data[offset+1]<<8)|((uint)data[offset+2]<<16)|((uint)data[offset+3]<<24);}
        static void Write(byte[] data,int offset,uint value) {for(int i=0;i<4;i++)data[offset+i]=(byte)(value>>(i*8));}
    }
}
