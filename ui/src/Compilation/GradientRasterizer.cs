using System;
using System.IO;
using System.Linq;
using BinaryAssetBuilder.UiCompiler.Fla;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal static class GradientRasterizer
    {
        // Flash 8 gradients become ordinary bitmap materials, understood by APT.
        internal static int Compile(FlaCompiler compiler, FlaRecord style, double[] bounds, out double[] bitmapMatrix)
        {
            double[] m = style.Matrix(); double determinant = m[0] * m[3] - m[1] * m[2]; if (Math.Abs(determinant) < 1e-12) throw new InvalidDataException("Singular gradient matrix.");
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (double x in new[] { bounds[0], bounds[1] }) foreach (double y in new[] { bounds[2], bounds[3] }) { double dx = x - m[4], dy = y - m[5], gx = (m[3] * dx - m[2] * dy) / determinant, gy = (-m[1] * dx + m[0] * dy) / determinant; left = Math.Min(left, gx); right = Math.Max(right, gx); top = Math.Min(top, gy); bottom = Math.Max(bottom, gy); }
            double sx = Math.Max(1, right - left) / 255, sy = Math.Max(1, bottom - top) / 255; const int size = 256;
            bitmapMatrix = new[] { m[0] * sx, m[1] * sx, m[2] * sy, m[3] * sy, m[4] + m[0] * (left - sx / 2) + m[2] * (top - sy / 2), m[5] + m[1] * (left - sx / 2) + m[3] * (top - sy / 2) };
            var stops = style.Records("stops").OrderBy(s => s.Number("ratio")).ToArray(); if (stops.Length == 0 || stops.Length > 15) throw new InvalidDataException("Invalid gradient stops.");
            int flags = (int)style.Number("flags"), gradientFlags = (int)style.Number("gradientFlags"); if (flags != 0x10 && flags != 0x12 && flags != 0x13) throw new NotSupportedException("Unknown gradient type.");
            double focal = (short)style.Number("focalPoint") / 255.0; if (Math.Abs(focal) > 1) throw new InvalidDataException("Focal ratio outside [-1,1]."); focal = Math.Max(-0.9999, Math.Min(0.9999, focal));
            byte[] pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                double gx = (left + x * sx) / 16384, gy = (top + y * sy) / 16384, ratio;
                if (flags == 0x10) ratio = (gx + 1) / 2;
                else { double dx = gx - focal, length = dx * dx + gy * gy, dot = focal * dx; ratio = length < 1e-20 ? 0 : length / (-dot + Math.Sqrt(dot * dot + (1 - focal * focal) * length)); }
                if ((gradientFlags & 4) != 0) { ratio %= 2; if (ratio < 0) ratio += 2; if (ratio > 1) ratio = 2 - ratio; }
                else if ((gradientFlags & 8) != 0) ratio -= Math.Floor(ratio); else ratio = Math.Max(0, Math.Min(1, ratio));
                double position = ratio * 255; int stop = 0; while (stop + 1 < stops.Length && stops[stop + 1].Number("ratio") <= position) stop++;
                var a = stops[stop]; var b = stops[Math.Min(stop + 1, stops.Length - 1)]; double delta = b.Real("ratio") - a.Real("ratio"), t = delta <= 0 ? 0 : Math.Max(0, Math.Min(1, (position - a.Real("ratio")) / delta));
                long ca = a.Number("colorRgba"), cb = b.Number("colorRgba"); int alpha = CoordinateMath.Round(TweenCompiler.Lerp(ca >> 24 & 255, cb >> 24 & 255, t)), at = (y * size + x) * 4; pixels[at] = (byte)alpha;
                for (int c = 0; c < 3; c++) { double aa = (ca >> (8 * c) & 255) / 255.0, bb = (cb >> (8 * c) & 255) / 255.0; if ((gradientFlags & 1) != 0) { aa = Linear(aa); bb = Linear(bb); } double channel = TweenCompiler.Lerp(aa, bb, t); if ((gradientFlags & 1) != 0) channel = Srgb(channel); pixels[at + c + 1] = (byte)Math.Max(0, Math.Min(alpha, CoordinateMath.Round(channel * alpha))); }
            }
            return compiler.DefineBitmap(pixels, size, size);
        }
        private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        private static double Srgb(double c) => c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    }
}
