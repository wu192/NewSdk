using System;
using System.IO;

namespace BinaryAssetBuilder.UiCompiler
{
    // A logical view into the payload region of the final AssetBuffer. Runtime
    // offsets are relative to this view; bytes are owned by the asset from byte 0.
    internal sealed class AssetPayloadStream : Stream
    {
        private readonly Stream asset;
        private readonly long start;
        internal AssetPayloadStream(Stream asset, int start) { this.asset = asset; this.start = start; }
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => asset.Length - start;
        public override long Position { get => asset.Position - start; set => asset.Position = checked(start + value); }
        public override long Seek(long offset, SeekOrigin origin)
        { long position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? Position + offset : Length + offset; if (position < 0) throw new IOException("Negative payload position."); Position = position; return position; }
        public override void SetLength(long value) => asset.SetLength(checked(start + value));
        public override void Write(byte[] buffer, int offset, int count) => asset.Write(buffer, offset, count);
        public override void WriteByte(byte value) => asset.WriteByte(value);
        public override void Flush() => asset.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
