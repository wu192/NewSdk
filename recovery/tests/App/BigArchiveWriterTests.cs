using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NewModCompiler.Application;

namespace NewModCompiler.Tests
{
    public static class BigArchiveWriterTests
    {
        static int checks;
        static void Check(bool value,string message) { checks++;if(!value)throw new Exception("BIG writer: "+message); }
        static void Reject<T>(Action action,string message) where T:Exception { try { action(); } catch(T) {checks++;return;}throw new Exception("BIG writer: "+message); }
        static string Put(string root,string name,byte[] bytes) {string path=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);return path;}
        static BigArchiveEntry Entry(string path,string name) {return new BigArchiveEntry {SourcePath=path,Name=name};}
        static uint Be(byte[] data,int offset) {return ((uint)data[offset]<<24)|((uint)data[offset+1]<<16)|((uint)data[offset+2]<<8)|data[offset+3];}
        static IEnumerable<BigArchiveEntry> ChangedAfterInventory(BigArchiveEntry entry) {yield return entry;File.WriteAllText(entry.SourcePath,"changed");}
        public static int Run(string root)
        {
            checks=0;Directory.CreateDirectory(root);
            string source=Put(root,"input/three.bin",new byte[]{0,1,2,255});
            string text=Put(root,"input/four.imp",Encoding.ASCII.GetBytes("some raw imports\0"));
            string empty=Put(root,"input/empty.relo",new byte[0]);
            Put(root,"input/unlisted.asset",new byte[]{9,8,7});
            var entries=new[]{Entry(source,"Data/AptUI/Thing.BIN"),Entry(text,"Data/AptUI/Thing.IMP"),Entry(empty,"Data/AptUI/Thing.RELO")};
            string output=Path.Combine(root,"package/Example_1.0.big");BigArchiveWriter.Write(output,entries);
            byte[] data=File.ReadAllBytes(output);
            Check(Encoding.ASCII.GetString(data,0,4)=="BIG4","Magic differs from original MakeBig BIG4");
            Check(BitConverter.ToUInt32(data,4)==data.Length,"Total length is not little endian");
            Check(Be(data,8)==3,"Entry count is not big endian");
            uint start=Be(data,12);int cursor=16;long expectedOffset=start;
            var expected=new Dictionary<string,byte[]>(StringComparer.Ordinal) {{"data\\aptui\\thing.bin",File.ReadAllBytes(source)},{"data\\aptui\\thing.imp",File.ReadAllBytes(text)},{"data\\aptui\\thing.relo",new byte[0]}};
            string previous="";
            for(int i=0;i<3;i++)
            {
                uint offset=Be(data,cursor),size=Be(data,cursor+4);cursor+=8;
                int end=Array.IndexOf(data,(byte)0,cursor);Check(end>=cursor && end<start,"Entry name did not terminate inside index");
                string name=Encoding.ASCII.GetString(data,cursor,end-cursor);cursor=end+1;
                Check(expected.ContainsKey(name) && StringComparer.Ordinal.Compare(previous,name)<0,"Entry normalization, exact list or deterministic order differs");previous=name;
                Check(offset==expectedOffset && size==expected[name].Length,"Entry offsets or sizes differ");
                Check(data.Skip((int)offset).Take((int)size).SequenceEqual(expected[name]),"Raw payload changed or alignment bytes were inserted");
                expectedOffset+=size;
            }
            Check(cursor+8==start && Encoding.ASCII.GetString(data,cursor,4)=="L219" && data.Skip(cursor+4).Take(4).All(value=>value==0),"Original MakeBig L219+zero directory footer differs");
            Check(expectedOffset==data.Length,"Archive contains unlisted/trailing bytes");
            string skudef=Path.Combine(root,"package/Example_1.0.skudef");BigArchiveWriter.WriteSkudef(skudef,"Example_1.0.big");
            Check(File.ReadAllBytes(skudef).SequenceEqual(Encoding.ASCII.GetBytes("mod-game 1.12\r\nadd-big Example_1.0.big\r\n")),"SKUDEF differs from legacy script commands or CRLF/ASCII format");
            BigArchiveWriter.Write(output,entries);
            Check(File.ReadAllBytes(output).SequenceEqual(data),"Direct replacement changed package bytes");
            Reject<InvalidDataException>(()=>BigArchiveWriter.Write(Path.Combine(root,"duplicate.big"),new[]{Entry(source,"data/a.bin"),Entry(text,"DATA\\A.BIN")}),"Case-insensitive normalized duplicate was accepted");
            foreach(string invalid in new[]{"","../a.bin","data/../a.bin","/root.bin","C:\\a.bin","data//a.bin","data\\.\\a.bin","data/中文.bin","data/a\nb.bin","data/*.bin","data/ a.bin"})
                Reject<ArgumentException>(()=>BigArchiveWriter.Write(Path.Combine(root,"bad.big"),new[]{Entry(source,invalid)}),"Unsafe/non-ASCII entry name accepted: "+invalid);
            Reject<InvalidDataException>(()=>BigArchiveWriter.Write(Path.Combine(root,"empty.big"),new BigArchiveEntry[0]),"Empty package was published");
            Reject<FileNotFoundException>(()=>BigArchiveWriter.Write(Path.Combine(root,"missing.big"),new[]{Entry(Path.Combine(root,"missing"),"data/a.bin")}),"Missing exact input was accepted");
            Reject<ArgumentException>(()=>BigArchiveWriter.WriteSkudef(Path.Combine(root,"bad.skudef"),"../a.big"),"SKUDEF allowed a BIG path escape");
            using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();Reject<OperationCanceledException>(()=>BigArchiveWriter.Write(Path.Combine(root,"cancelled.big"),entries,cancelled.Token),"Cancelled package was published");}
            Check(!File.Exists(Path.Combine(root,"cancelled.big")),"Cancellation exposed a package");
            string changing=Put(root,"changing.bin",new byte[100]);
            Reject<IOException>(()=>BigArchiveWriter.Write(Path.Combine(root,"changed.big"),ChangedAfterInventory(Entry(changing,"data/changed.bin"))),"Input length change after inventory was accepted");
            Check(!File.Exists(Path.Combine(root,"changed.big")),"Failed input copy exposed a final package");
            Check(!Directory.GetFiles(root,"*.tmp-*",SearchOption.AllDirectories).Any(),"Cancellation/failure left package temporary files");
            Check(BigArchiveWriter.CheckedArchiveSize(24,uint.MaxValue-24L)==uint.MaxValue,"Maximum representable BIG size was rejected");
            Reject<IOException>(()=>BigArchiveWriter.CheckedArchiveSize(24,uint.MaxValue),"Total-size overflow was accepted");
            Reject<IOException>(()=>BigArchiveWriter.CheckedArchiveSize(uint.MaxValue+1L,0),"Index offset overflow was accepted");
            Reject<IOException>(()=>BigArchiveWriter.CheckedArchiveSize(24,long.MaxValue),"Payload overflow was accepted");
            return checks;
        }
    }
}
