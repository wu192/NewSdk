// FLA raster layout derived from JPEXS FLA Convert 3.0.0, LGPL-2.1-or-later.
using System;
using System.Collections;
using System.IO;
using BinaryAssetBuilder.UiCompiler.Fla;
using StbImageSharp;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal static class BitmapDecoder
    {
        // Decode FLA's top-down, premultiplied ARGB source pixels.
        internal static byte[] Decode(FlaRecord payload, out int width, out int height)
        {
            if (payload.get("encodedData") is BinaryData encoded)
            {
                var image = ImageResult.FromMemory(encoded.toByteArray(), ColorComponents.RedGreenBlueAlpha); width = image.Width; height = image.Height;
                var argb = new byte[image.Data.Length]; for (int i = 0; i < argb.Length; i += 4) { int a = image.Data[i + 3]; argb[i] = (byte)a; for (int c = 0; c < 3; c++) argb[i + 1 + c] = (byte)((image.Data[i + c] * a + 127) / 255); } return argb;
            }
            var r = payload.Record("raster") ?? throw new InvalidDataException("Missing raster."); width = (int)r.Number("width"); height = (int)r.Number("height");
            int stride = (int)r.Number("rowStride"), format = (int)r.Number("formatCode"), revision = (int)r.Number("revision");
            if (width <= 0 || height <= 0 || width > 65535 || height > 65535 || (long)width * height > 16777216) throw new InvalidDataException("Invalid bitmap dimensions.");
            byte[] pixels;
            if (r.get("pixelData") is BinaryData raw) pixels = raw.toByteArray();
            else { using (var m = new MemoryStream()) { foreach (var chunk in r.Records("chunks")) { byte[] bytes = ((BinaryData)chunk.get("data")).toByteArray(); m.Write(bytes, 0, bytes.Length); } pixels = FlaCompression.Inflate(m.ToArray()); } }
            if (pixels.Length != stride * height) throw new InvalidDataException("Raster size mismatch.");
            int depth = format == 1 ? 1 : format == 2 ? 4 : format == 3 ? 8 : format == 4 ? 16 : format == 5 ? 32 : 0;
            if (depth == 0 || stride < (width * depth + 7) / 8) throw new InvalidDataException("Invalid raster format/stride.");
            bool alpha = r.Number("hasAlpha") != 0; var palette = r.Records("palette"); byte[] output = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = y * stride, at = (y * width + x) * 4; int a = 255, red, green, blue;
                if (format == 5) { p += x * 4; a = alpha ? pixels[p] : 255; red = pixels[p + 1]; green = pixels[p + 2]; blue = pixels[p + 3]; }
                else if (format == 4) { p += x * 2; int c = pixels[p] << 8 | pixels[p + 1]; red = ((c >> 10) & 31) * 255 / 31; green = ((c >> 5) & 31) * 255 / 31; blue = (c & 31) * 255 / 31; }
                else { int bit = x * depth, index = pixels[p + bit / 8] >> (8 - depth - bit % 8) & ((1 << depth) - 1); if (index >= palette.Count) throw new InvalidDataException("Missing palette entry."); var components = (IList)palette[index].get("components"); int Component(int n) => revision >= 3 ? Convert.ToInt32(components[n]) & 255 : Convert.ToInt32(components[n]) >> 8 & 255; int start = revision >= 3 ? 1 : 0; a = alpha && revision >= 3 ? Component(0) : 255; red = Component(start); green = Component(start + 1); blue = Component(start + 2); }
                if (red > a || green > a || blue > a) throw new InvalidDataException("Invalid premultiplied FLA raster."); output[at] = (byte)a; output[at + 1] = (byte)red; output[at + 2] = (byte)green; output[at + 3] = (byte)blue;
            }
            return output;
        }
    }
}
