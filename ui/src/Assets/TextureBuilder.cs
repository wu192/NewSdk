using System;
using System.IO;
using System.Text;
using BinaryAssetBuilder.UiCompiler.Compilation;
namespace BinaryAssetBuilder.UiCompiler
{
    internal static class TextureBuilder
    {
        // Write the game's texture payload in its final AssetBuffer. The source
        // pixels and one row are working data, never an encoded image buffer.
        internal static void Write(Stream destination, ImageSymbol image)
        {
            int Pot(int n) { int result = 1; while (result <= n) result = checked(result * 2); return result; }
            int offset = image.NoPack ? 0 : 1;
            int width = Pot(checked(image.Width + offset * 2)), height = Pot(checked(image.Height + offset * 2));
            using (var writer = new BinaryWriter(destination, Encoding.UTF8, true))
            {
                writer.Write(0x20534444u); writer.Write(124); writer.Write(0x100f); writer.Write(height); writer.Write(width); writer.Write(checked(width * 4)); writer.Write(0); writer.Write(1);
                for (int i = 0; i < 11; i++) writer.Write(0);
                writer.Write(32); writer.Write(0x41); writer.Write(0); writer.Write(32); writer.Write(0xff0000); writer.Write(0xff00); writer.Write(0xff); writer.Write(0xff000000u); writer.Write(0x1000);
                for (int i = 0; i < 4; i++) writer.Write(0);
                var row = new byte[checked(width * 4)];
                for (int y = 0; y < height; y++)
                {
                    Array.Clear(row, 0, row.Length);
                    if (image.NoPack || (y >= offset && y < image.Height + offset))
                    {
                        int sy = Math.Min(image.Height - 1, Math.Max(0, y - offset));
                        for (int x = 0; x < width; x++)
                        {
                            if (!image.NoPack && (x < offset || x >= image.Width + offset)) continue;
                            int sx = Math.Min(image.Width - 1, Math.Max(0, x - offset));
                            Buffer.BlockCopy(image.Pixels, (sy * image.Width + sx) * 4, row, x * 4, 4);
                        }
                    }
                    writer.Write(row);
                }
            }
        }
    }
}
