using System;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    // The image loader is an explicit boundary. This class does not forward to
    // the original DLL or substitute an approximate codec for LoadImageA.
    public sealed class TexturePacker
    {
        public sealed class Image
        {
            public readonly int Width, Height;
            public readonly uint[] Pixels;
            public readonly bool HasAlpha;
            public Image(int width, int height, uint[] pixels, bool hasAlpha)
            {
                if (width <= 0 || height <= 0 || pixels == null || pixels.Length != checked(width * height))
                    throw new ArgumentException("Image requires a complete positive-sized RGBA pixel array.");
                Width = width; Height = height; Pixels = pixels; HasAlpha = hasAlpha;
            }
        }
        public sealed class Tile
        {
            public readonly uint Id;
            public ushort UpperLeftX, UpperLeftY, BottomRightX, BottomRightY;
            internal Image Base, Normal;
            internal Tile(uint id, Image image, Image normal) { Id = id; Base = image; Normal = normal; }
        }
        public delegate Image ImageLoader(string path);
        private readonly List<Tile> tiles = new List<Tile>();
        private readonly ImageLoader loader;
        private bool packed, hasNormals;
        public Image BaseAtlas { get; private set; }
        public Image NormalAtlas { get; private set; }
        public Tile[] Tiles { get { return tiles.ToArray(); } }
        public bool HasNormals { get { return hasNormals; } }
        public bool IsPacked { get { return packed; } }
        public TexturePacker() { }
        public TexturePacker(ImageLoader loader) { this.loader = loader; }
        private static BinaryAssetBuilderException Bad(string message)
        {
            return new BinaryAssetBuilderException(ErrorCode.BadTextureFile, message, new object[0]);
        }
        public void AddTexture(string basePath, string normalPath, uint id)
        {
            if (packed) return;
            if (!File.Exists(basePath)) return;
            // Original checks substring rather than extension and checks both
            // paths before testing whether the normal image exists.
            if (!basePath.ToLower().Contains("tga") || !normalPath.ToLower().Contains("tga"))
                throw Bad("Terrain textures must be targas!");
            if (loader == null) throw new NotSupportedException("Original LoadImageA file decoding has not been recovered; provide an image loader.");
            Image image = loader(basePath);
            if (image.Width != 256 || image.Height != 256)
                throw Bad("The terrain texture must be 256x256 in size!");
            Image normal = File.Exists(normalPath) ? loader(normalPath) : null;
            AddImage(id, image, normal);
        }
        // In-memory equivalent of a successfully loaded AddTexture. RGBA values
        // use little-endian byte order: R in bits0..7 and A in bits24..31.
        public void AddImage(uint id, Image image, Image normal)
        {
            if (packed) return;
            if (image == null || image.Width != 256 || image.Height != 256)
                throw Bad("The terrain texture must be 256x256 in size!");
            uint[] pixels = (uint[])image.Pixels.Clone();
            if (!image.HasAlpha)
                for (int i = 0; i < pixels.Length; ++i) pixels[i] &= 0x00FFFFFFu;
            Image baseImage = new Image(image.Width, image.Height, pixels, image.HasAlpha);
            Image normalImage = null;
            if (normal != null)
            {
                uint[] normals = (uint[])normal.Pixels.Clone();
                for (int i = 0; i < normals.Length; ++i) normals[i] |= 0xFF000000u;
                normalImage = new Image(normal.Width, normal.Height, normals, true);
                hasNormals = true;
            }
            tiles.Add(new Tile(id, baseImage, normalImage));
        }
        public bool PackTextures()
        {
            // Native marks packed even when it subsequently rejects >49 tiles.
            if (packed) throw new InvalidOperationException("Repeated packing uses freed source images in the original and is not supported.");
            packed = true;
            int count = tiles.Count, width, height;
            if (count <= 1) { width = 512; height = 512; }
            else if (count <= 3) { width = 1024; height = 512; }
            else if (count <= 7) { width = 2048; height = 512; }
            else if (count <= 9) { width = 1024; height = 1024; }
            else if (count <= 21) { width = 2048; height = 1024; }
            else if (count <= 49) { width = 2048; height = 2048; }
            else throw Bad(String.Format("Maximum number of textures exceeded ({0}). Limit is 49.", count));
            MergeTextures(width, height);
            Tile[] ordered = tiles.ToArray();
            QuickSort(ordered);
            tiles.Clear(); tiles.AddRange(ordered);
            foreach (Tile tile in tiles) { tile.Base = null; tile.Normal = null; }
            return true;
        }
        private void MergeTextures(int width, int height)
        {
            BaseAtlas = new Image(width, height, new uint[width * height], true);
            if (hasNormals) NormalAtlas = new Image(width, height, new uint[width * height], true);
            int x = 16, y = 16;
            foreach (Tile tile in tiles)
            {
                tile.UpperLeftX = (ushort)x; tile.UpperLeftY = (ushort)y;
                tile.BottomRightX = (ushort)(x + tile.Base.Width);
                tile.BottomRightY = (ushort)(y + tile.Base.Height);
                CopyTexture(BaseAtlas, tile.Base, x, y);
                if (hasNormals && tile.Normal != null) CopyTexture(NormalAtlas, tile.Normal, x, y);
                x += 288;
                if (x + 288 > width)
                {
                    x = 16; y += 288;
                    if (y + 288 > height) break;
                }
            }
        }
        public static void CopyTexture(Image destination, Image source, int offsetX, int offsetY)
        {
            // Periodic edge padding, not clamping/extrusion. Preserve original
            // double arithmetic and traversal (x outer, y inner).
            for (int x = 0; x < source.Width + 32; ++x)
            {
                double u = (double)(x - 16) / source.Width;
                for (int y = 0; y < source.Height + 32; ++y)
                {
                    int targetX = offsetX - 16 + x, targetY = offsetY - 16 + y;
                    if (targetX < 0 || targetX >= destination.Width || targetY < 0 || targetY >= destination.Height)
                        throw new NotSupportedException("Original CopyTexture would write outside the destination image.");
                    destination.Pixels[targetY * destination.Width + targetX] = GetPixel(source, u, (double)(y - 16) / source.Height);
                }
            }
        }
        private static uint GetPixel(Image image, double u, double v)
        {
            u = u < 0 ? u - Math.Ceiling(u) + 1 : u - Math.Floor(u);
            v = v < 0 ? v - Math.Ceiling(v) + 1 : v - Math.Floor(v);
            int x = (int)(image.Width * u), y = (int)(image.Height * v);
            if (x < 0 || x >= image.Width || y < 0 || y >= image.Height)
                throw new NotSupportedException("Original wrapped sample is outside the source image.");
            return image.Pixels[y * image.Width + x];
        }
        private static bool Less(Tile a, Tile b) { return a.Id < b.Id; }
        private static void QuickSort(Tile[] values)
        {
            int count = values.Length;
            if (count == 0) return;
            int bits = 0;
            for (int n = count; n != 0; n >>= 1) ++bits;
            QuickSortImpl(values, 0, count, bits * 2 - 2);
            if (count > 28)
            {
                InsertionSort(values, 0, 28);
                InsertionSortSimple(values, 28, count);
            }
            else InsertionSort(values, 0, count);
        }
        private static void QuickSortImpl(Tile[] a, int first, int last, int depth)
        {
            if (last - first > 28)
            {
                while (depth > 0)
                {
                    int cut = Partition(a, first, last, Median(a, first, first + (last - first) / 2, last - 1));
                    --depth;
                    QuickSortImpl(a, cut, last, depth);
                    last = cut;
                    if (last - first <= 28) break;
                }
            }
            if (depth == 0) HeapSort(a, first, last);
        }
        private static int Median(Tile[] a, int x, int y, int z)
        {
            if (Less(a[x], a[y]))
            {
                if (Less(a[y], a[z])) return y;
                return Less(a[x], a[z]) ? z : x;
            }
            if (Less(a[x], a[z])) return x;
            return Less(a[y], a[z]) ? z : y;
        }
        private static int Partition(Tile[] a, int first, int last, int pivotIndex)
        {
            Tile pivot = a[pivotIndex];
            while (true)
            {
                if (Less(a[first], pivot)) { ++first; continue; }
                --last;
                while (Less(pivot, a[last])) --last;
                if (first >= last) return first;
                Tile saved = a[first]; a[first] = a[last]; a[last] = saved; ++first;
            }
        }
        private static void InsertionSort(Tile[] a, int first, int last)
        {
            for (int i = first + 1; i < last; ++i)
            {
                Tile value = a[i]; int hole = i;
                while (hole != first && Less(value, a[hole - 1])) { a[hole] = a[hole - 1]; --hole; }
                a[hole] = value;
            }
        }
        private static void InsertionSortSimple(Tile[] a, int first, int last)
        {
            for (int i = first; i < last; ++i)
            {
                Tile value = a[i]; int hole = i;
                while (Less(value, a[hole - 1])) { a[hole] = a[hole - 1]; --hole; }
                a[hole] = value;
            }
        }
        private static void HeapSort(Tile[] a, int first, int last)
        {
            int count = last - first;
            if (count >= 2)
                for (int p = (count - 2) / 2; p >= 0; --p) AdjustHeap(a, first, p, count, p, a[first + p]);
            while (count > 1)
            {
                Tile value = a[first + count - 1];
                a[first + count - 1] = a[first];
                AdjustHeap(a, first, 0, count - 1, 0, value);
                --count;
            }
        }
        private static void AdjustHeap(Tile[] a, int first, int top, int size, int position, Tile value)
        {
            int child = position * 2 + 2;
            while (child < size)
            {
                if (Less(a[first + child], a[first + child - 1])) --child;
                a[first + position] = a[first + child];
                position = child; child = child * 2 + 2;
            }
            if (child == size) { a[first + position] = a[first + child - 1]; position = child - 1; }
            int parent = (position - 1) >> 1;
            while (position > top && Less(a[first + parent], value))
            {
                a[first + position] = a[first + parent]; position = parent; parent = (parent - 1) >> 1;
            }
            a[first + position] = value;
        }
    }
}
