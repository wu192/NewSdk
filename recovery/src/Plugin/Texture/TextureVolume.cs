using System;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    // Pure IL reconstruction of RGBAMipMappedVolumeMap.resize and
    // CreateVolumeRGBAImage. Does not generate filtered mip levels or compress.
    public sealed class TextureVolume
    {
        public sealed class Slice
        {
            public uint Width { get; internal set; }
            public uint Height { get; internal set; }
            public uint PixelCount { get; internal set; }
            public uint PixelCapacity { get; internal set; }
            // Like nvVector, the backing storage can exceed the logical count.
            // Newly allocated bytes are unspecified in the original; managed
            // storage initializes them to zero. Retained bytes are preserved.
            public uint[] Pixels { get; internal set; }
            internal Slice() { Pixels = new uint[0]; }
            internal void Resize(uint width, uint height)
            {
                uint count = unchecked(width * height);
                uint capacity = ResizeCapacity(count, PixelCapacity, 4u);
                if (capacity != PixelCapacity)
                {
                    uint[] replacement = new uint[(int)capacity];
                    Array.Copy(Pixels, replacement, Math.Min(Pixels.Length, replacement.Length));
                    Pixels = replacement;
                    PixelCapacity = capacity;
                }
                PixelCount = count; Width = width; Height = height;
            }
        }
        public sealed class Level
        {
            public uint SliceCapacity { get; internal set; }
            public Slice[] Slices { get; internal set; }
            internal Level() { Slices = new Slice[0]; }
            internal void Resize(uint width, uint height, uint depth)
            {
                uint capacity = ResizeCapacity(depth, SliceCapacity, 24u);
                Slice[] replacement = new Slice[CheckedCount(depth, 24u)];
                int retained = Math.Min(Slices.Length, replacement.Length);
                Array.Copy(Slices, replacement, retained);
                for (int i = retained; i < replacement.Length; ++i) replacement[i] = new Slice();
                Slices = replacement; SliceCapacity = capacity;
                foreach (Slice slice in Slices) slice.Resize(width, height);
            }
        }
        public uint LevelCapacity { get; private set; }
        public Level[] Levels { get; private set; }
        public TextureVolume() { Levels = new Level[0]; }
        private static int CheckedCount(uint count, uint elementSize)
        {
            if ((ulong)count * elementSize > Int32.MaxValue)
                throw new NotSupportedException("Original native vector byte allocation exceeds the managed safe allocation range.");
            return (int)count;
        }
        private static uint ResizeCapacity(uint count, uint capacity, uint elementSize)
        {
            CheckedCount(count, elementSize);
            if (count != 0 && (count > capacity || count <= (capacity >> 1)))
            {
                ulong next = capacity == 0 ? count : (ulong)count + (count >> 2);
                if (next > UInt32.MaxValue)
                    throw new NotSupportedException("Original native vector capacity arithmetic wraps.");
                capacity = (uint)next;
            }
            CheckedCount(capacity, elementSize);
            return capacity;
        }
        public static uint MipCount(uint width, uint height, uint depth)
        {
            uint maximum = Math.Max(Math.Max(width, height), depth), count = 0;
            while (maximum != 0) { ++count; maximum >>= 1; }
            return count;
        }
        public void Resize(uint width, uint height, uint depth, uint mipCount)
        {
            if (mipCount == 0) mipCount = MipCount(width, height, depth);
            uint capacity = ResizeCapacity(mipCount, LevelCapacity, 12u);
            Level[] replacement = new Level[CheckedCount(mipCount, 12u)];
            int retained = Math.Min(Levels.Length, replacement.Length);
            Array.Copy(Levels, replacement, retained);
            for (int i = retained; i < replacement.Length; ++i) replacement[i] = new Level();
            Levels = replacement; LevelCapacity = capacity;
            foreach (Level level in Levels)
            {
                level.Resize(width, height, depth);
                width = Math.Max(width >> 1, 1u);
                height = Math.Max(height >> 1, 1u);
                depth = Math.Max(depth >> 1, 1u);
            }
        }
        public void CreateVolumeRGBAImage(uint width, uint height, uint[] rgba)
        {
            // Preserve the order: modulo precedes division, allocation and all
            // pixel reads. A zero height raises DivideByZeroException.
            if (width % height != 0)
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "The texture's width is not a direct multiple of the height.  Cannot process it as a 3D texture.", new object[0]);
            uint depth = width / height;
            // Native indexing is 32-bit pointer arithmetic. Overflowing source
            // extents have no defined safe copy contract; reject before copying.
            ulong count = (ulong)width * height;
            if (count > Int32.MaxValue / 4 || (depth != 0 && (ulong)height * height > Int32.MaxValue / 4))
                throw new NotSupportedException("Original strip copy would exceed safe 32-bit RGBA storage.");
            if (rgba == null && count != 0) throw new ArgumentNullException("rgba");
            if (rgba != null && (ulong)rgba.LongLength < count)
                throw new ArgumentException("Source RGBA storage is shorter than the strip image.", "rgba");
            Resize(height, height, depth, 1u);
            for (uint z = 0; z < depth; ++z)
            {
                Slice slice = Levels[0].Slices[(int)z];
                for (uint x = 0; x < height; ++x)
                    for (uint y = 0; y < height; ++y)
                        slice.Pixels[(int)(y * height + x)] = rgba[(int)(y * width + z * height + x)];
            }
        }
        public static TextureVolume FromStrip(uint width, uint height, uint[] rgba)
        {
            TextureVolume result = new TextureVolume();
            result.CreateVolumeRGBAImage(width, height, rgba);
            return result;
        }
    }
}
