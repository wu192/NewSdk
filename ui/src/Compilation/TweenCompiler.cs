using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BinaryAssetBuilder.UiCompiler.Fla;
namespace BinaryAssetBuilder.UiCompiler.Compilation
{
    internal static class TweenCompiler
    {
        internal static double Lerp(double a, double b, double t) => a + (b - a) * t;
        internal static long ColorLerp(long a, long b, double t) { long color = 0; for (int i = 0; i < 4; i++) color |= (long)Math.Max(0, Math.Min(255, CoordinateMath.Round(Lerp(a >> (8 * i) & 255, b >> (8 * i) & 255, t)))) << (8 * i); return color; }
        internal static void Apply(List<FlaCompiler.Placement> start, List<FlaCompiler.Placement> end, FlaRecord frame, double t)
        {
            if (start.Count != end.Count) throw new NotSupportedException("Motion tween changes object count.");
            for (int i = 0; i < start.Count; i++)
            {
                var a = start[i]; var b = end[i]; if (a.Id != b.Id && a.Record.Number("libraryId", -1) != b.Record.Number("libraryId", -2)) throw new NotSupportedException("Motion tween changes symbol identity.");
                double position = Ease(frame, t, 0), rotation = Ease(frame, t, 1), scale = Ease(frame, t, 2); var da = Decompose(a.Matrix); var db = Decompose(b.Matrix);
                double angle = db[0] - da[0]; while (angle > Math.PI) angle -= 2 * Math.PI; while (angle < -Math.PI) angle += 2 * Math.PI;
                int mode = (int)frame.Number("rotationMode"), turns = (int)frame.Number("rotationCount"); if (mode == 1) { if (angle < 0) angle += 2 * Math.PI; angle += turns * 2 * Math.PI; } else if (mode == 2) { if (angle > 0) angle -= 2 * Math.PI; angle -= turns * 2 * Math.PI; } else if ((frame.Number("keyMode") & 0x400) == 0) angle = 0;
                double theta = da[0] + angle * rotation, sx = Lerp(da[1], db[1], scale), sy = Lerp(da[2], db[2], scale), skew = Lerp(da[3], db[3], scale);
                if ((frame.Number("keyMode") & 0x200) == 0) { sx = da[1]; sy = da[2]; skew = da[3]; }
                a.Matrix = new[] { Math.Cos(theta) * sx, Math.Sin(theta) * sx, Math.Cos(theta) * skew - Math.Sin(theta) * sy, Math.Sin(theta) * skew + Math.Cos(theta) * sy, Lerp(a.Matrix[4], b.Matrix[4], position), Lerp(a.Matrix[5], b.Matrix[5], position) };
                var owner = a.Record.Copy(); var color = new FlaRecord("colorTransform"); color.set("effectType", 3); string[] channels = { "red", "green", "blue", "alpha" };
                int[] ac = ColorValues(a.Record), bc = ColorValues(b.Record);
                for (int c = 0; c < 4; c++) { color.set(channels[c] + "Multiplier", CoordinateMath.Round(Lerp(ac[c], bc[c], Ease(frame, t, 3)))); color.set(channels[c] + "Offset", CoordinateMath.Round(Lerp(ac[c + 4], bc[c + 4], Ease(frame, t, 3)))); } owner.set("colorTransform", color); a.Record = owner;
            }
        }
        private static int[] ColorValues(FlaRecord r)
        { var c = r.Record("colorTransform"); int[] v = { 256, 256, 256, 256, 0, 0, 0, 0 }; if (c == null) return v; int type = (int)c.Number("effectType", r.Number("colorEffectType", 3)); string[] names = { "red", "green", "blue", "alpha" }; for (int i = 0; i < 4; i++) { v[i] = (int)c.Number(names[i] + "Multiplier", 256); v[i + 4] = (int)c.Number(names[i] + "Offset"); } if (type == 1 || type == 2) { double amount = c.Real("effectPercent", r.Real("colorEffectPercent")) / 100; long tint = c.Number("effectColorRgba", r.Number("colorEffectRgba")); for (int i = 0; i < 3; i++) { v[i] = CoordinateMath.Round((1 - Math.Abs(amount)) * 256); v[i + 4] = CoordinateMath.Round(type == 1 ? Math.Max(0, amount) * 255 : (tint >> (8 * i) & 255) * amount); } } else if (type == 4) for (int i = 0; i < 3; i++) { v[i] = 256; v[i + 4] = 0; } return v; }
        private static double[] Decompose(double[] m) { double sx = Math.Sqrt(m[0] * m[0] + m[1] * m[1]); if (sx < 1e-9) throw new InvalidDataException("Singular tween matrix."); return new[] { Math.Atan2(m[1], m[0]), sx, (m[0] * m[3] - m[1] * m[2]) / sx, (m[0] * m[2] + m[1] * m[3]) / sx }; }
        internal static double Ease(FlaRecord frame, double t, int target)
        {
            if (frame.Number("customEasePresent") == 0) { double amount = (short)frame.Number("acceleration") / 100.0; return t + Math.Abs(amount) * ((amount >= 0 ? t * (2 - t) : t * t) - t); }
            var curves = frame.get("customEaseCurves") as IList ?? throw new InvalidDataException("Missing custom easing."); int index = frame.Number("useSingleEaseCurve", 1) != 0 ? 5 : target; if (index >= curves.Count) return t; var points = (IList)curves[index]; if (points.Count == 0) return t; if (points.Count < 2) throw new InvalidDataException("Invalid easing curve.");
            double P(int p, int c) => Convert.ToDouble(((IList)points[p])[c]); double x0 = P(0, 2), x1 = P(points.Count - 1, 2), y0 = P(0, 3), y1 = P(points.Count - 1, 3), x = Lerp(x0, x1, t);
            if (x1 <= x0 || y1 == y0) throw new InvalidDataException("Degenerate easing curve.");
            for (int i = 0; i + 1 < points.Count; i++) if (x <= P(i + 1, 2))
            { double a = P(i, 2), b = P(i, 4), c = P(i + 1, 0), d = P(i + 1, 2); double qa = -a + 3 * b - 3 * c + d, qb = 2 * (a - 2 * b + c), qc = b - a, minimum = Math.Min(qc, qa + qb + qc), vertex = qa == 0 ? -1 : -qb / (2 * qa); if (qa > 0 && vertex > 0 && vertex < 1) minimum = Math.Min(minimum, qa * vertex * vertex + qb * vertex + qc); if (minimum < -1e-12) throw new NotSupportedException("Nonmonotonic easing curve."); double lo = 0, hi = 1; for (int j = 0; j < 36; j++) { double mid = (lo + hi) / 2; if (Cubic(a, b, c, d, mid) < x) lo = mid; else hi = mid; } return (Cubic(P(i, 3), P(i, 5), P(i + 1, 1), P(i + 1, 3), (lo + hi) / 2) - y0) / (y1 - y0); }
            return 1;
        }
        private static double Cubic(double a, double b, double c, double d, double t) { double u = 1 - t; return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d; }
    }
}
