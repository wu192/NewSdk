using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Web.Script.Serialization;

namespace NewModCompiler.Application
{
    // The official Worldbuilder.bin exceeds GitHub's per-file limit. Independent
    // raw-Deflate blocks preserve its logical offsets without extracting 1.3 GB
    // for a small MOD. Existing ordinary payload files retain precedence.
    public sealed class WorldbuilderPayload
    {
        public sealed class Block
        {
            public string file { get; set; }
            public int uncompressedBytes { get; set; }
            public long compressedBytes { get; set; }
        }
        public sealed class Index
        {
            public int schemaVersion { get; set; }
            public long uncompressedLength { get; set; }
            public int blockSize { get; set; }
            public Block[] blocks { get; set; }
        }
        readonly string path, directory;
        readonly Index index;
        readonly string[] files;
        int cachedBlock = -1;
        byte[] cachedData;
        public long Length { get; private set; }
        public IEnumerable<string> Files { get { return files; } }

        public WorldbuilderPayload(string path)
        {
            this.path = path;
            if (File.Exists(path)) {
                Length = new FileInfo(path).Length;
                files = new[] {path};
                return;
            }
            directory = path + ".parts";
            string metadata = Path.Combine(directory, "index.json");
            if (!File.Exists(metadata)) throw Missing(path);
            index = new JavaScriptSerializer().Deserialize<Index>(File.ReadAllText(metadata));
            if (index == null || index.schemaVersion != 1 || index.uncompressedLength < 4 ||
                index.blockSize <= 0 || index.blockSize > 64 * 1024 * 1024 || index.blocks == null ||
                index.blocks.LongLength != (index.uncompressedLength - 1) / index.blockSize + 1)
                throw new InvalidDataException("Worldbuilder payload block index is invalid: " + metadata);
            Length = index.uncompressedLength;
            files = new string[index.blocks.Length + 1];
            files[0] = metadata;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < index.blocks.Length; i++) {
                Block block = index.blocks[i];
                int expected = (int)Math.Min(index.blockSize, Length - (long)i * index.blockSize);
                if (block == null || string.IsNullOrEmpty(block.file) || block.file.IndexOfAny(new[] {'/', '\\', ':'}) >= 0 ||
                    block.file == "." || block.file == ".." || !names.Add(block.file) ||
                    block.uncompressedBytes != expected || block.compressedBytes <= 0)
                    throw new InvalidDataException("Worldbuilder payload block metadata is invalid: " + metadata);
                string file = files[i + 1] = Path.Combine(directory, block.file);
                if (!File.Exists(file)) throw Missing(file);
                if (new FileInfo(file).Length != block.compressedBytes)
                    throw new InvalidDataException("Worldbuilder compressed payload length differs: " + file);
            }
        }
        static FileNotFoundException Missing(string file)
        {
            return new FileNotFoundException("Neutral asset repair requires original Worldbuilder payload data: " + file,file);
        }
        public void ValidateLength(long expected)
        {
            if (Length != expected) throw new InvalidDataException("Asset resolver linked payload length differs: " + path);
        }
        public byte[] Read(long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > Length - count)
                throw new InvalidDataException("Worldbuilder payload range differs: " + path);
            byte[] result = new byte[count];
            using(var output=new MemoryStream(result))CopyTo(output,offset,count,CancellationToken.None);
            return result;
        }
        public void CopyTo(Stream output,long offset,long count,CancellationToken cancellation)
        {
            if (offset < 0 || count < 0 || offset > Length - count)
                throw new InvalidDataException("Worldbuilder payload range differs: " + path);
            cancellation.ThrowIfCancellationRequested();
            if (index == null) {
                using (var input = File.OpenRead(path)) {
                    input.Position = offset;
                    byte[] buffer=new byte[64*1024];
                    while(count>0) {
                        cancellation.ThrowIfCancellationRequested();
                        int read=input.Read(buffer,0,(int)Math.Min(buffer.Length,count));
                        if(read==0)throw new EndOfStreamException("Worldbuilder payload ended before its declared length.");
                        output.Write(buffer,0,read);count-=read;
                    }
                }
                return;
            }
            while (count > 0) {
                cancellation.ThrowIfCancellationRequested();
                int number = (int)(offset / index.blockSize), within = (int)(offset % index.blockSize);
                if (cachedBlock != number) {
                    Block block = index.blocks[number];
                    byte[] data = new byte[block.uncompressedBytes];
                    using (var input = File.OpenRead(files[number + 1]))
                    using (var decoder = new DeflateStream(input, CompressionMode.Decompress)) {
                        ReadExactly(decoder, data, 0, data.Length);
                        if (decoder.ReadByte() != -1) throw new InvalidDataException("Worldbuilder decoded block length differs.");
                    }
                    cachedData = data;
                    cachedBlock = number;
                }
                int copy = (int)Math.Min(Math.Min(count,64*1024), cachedData.Length - within);
                output.Write(cachedData,within,copy);
                count -= copy;
                offset += copy;
            }
        }
        static void ReadExactly(Stream input, byte[] data, int offset, int count)
        {
            while (count > 0) {
                int read = input.Read(data, offset, count);
                if (read == 0) throw new EndOfStreamException("Worldbuilder payload ended before its declared length.");
                offset += read;
                count -= read;
            }
        }
    }
}
