using System;

namespace Recovered.Compiler.Texture
{
    // Original pure image rules. No compression, image decoding, D3D, or original DLL calls.
    public static class TextureImageRules
    {
        public const int CompressionOptionsSize = 1644;

        private static uint PixelCount(uint width, uint height) { return unchecked(width * height); }
        private static uint CheckPixels(uint width, uint height, byte[] rgba)
        {
            uint count = PixelCount(width, height);
            if (rgba == null) throw new ArgumentNullException("rgba");
            if ((ulong)count * 4ul > (ulong)rgba.LongLength)
                throw new ArgumentException("RGBA storage is shorter than the image's wrapped 32-bit pixel count.", "rgba");
            return count;
        }
        public static bool HasConstantAlphaChannel(uint width, uint height, byte[] rgba)
        {
            uint count = CheckPixels(width, height, rgba);
            if (count == 0) return true;
            byte first = rgba[3];
            for (uint i = 0; i < count; i++) if (rgba[(int)(i * 4 + 3)] != first) return false;
            return true;
        }
        public static bool Has1BitAlphaChannel(uint width, uint height, byte[] rgba)
        {
            uint count = CheckPixels(width, height, rgba);
            for (uint i = 0; i < count; i++)
            {
                byte alpha = rgba[(int)(i * 4 + 3)];
                if (alpha != 0 && alpha != 255) return false;
            }
            return true;
        }
        public static bool IsValidTexture(uint width, uint height, string instanceName, Action<string> warning)
        {
            uint count = PixelCount(width, height);
            if (count == 0)
            {
                Emit(warning, "The loaded image contains no pixel data. {0} will be pink.", instanceName);
                return false;
            }
            if (unchecked(count << 2) >= 67108864u)
            {
                Emit(warning, "The loaded image {0} is larger than the compression buffer, which is unsafe. {0} will be pink.", instanceName);
                return false;
            }
            if (width != 0 && (unchecked(~width + 1u) & width) == width &&
                height != 0 && (unchecked(~height + 1u) & height) == height) return true;
            Emit(warning, "Non power of two sized textures not supported. {0} will be pink.", instanceName);
            return false;
        }
        public static string GuessProperFormat(uint width, uint height, byte[] rgba, bool hasAlpha,
            string inFormat, string instanceName, Action<string> info)
        {
            if (inFormat != "D3DFMT_DXT1" || !hasAlpha) return inFormat;
            if (HasConstantAlphaChannel(width, height, rgba))
            {
                // Original reads pixel zero even for an empty image. Its valid calling path
                // first rejects an empty image; expose an explicit adapter error for that case.
                if (rgba.Length < 4) throw new ArgumentException("Format inference requires a first pixel.", "rgba");
                if (rgba[3] == 255) return inFormat;
                Emit(info, "Converting to DXT3 because \"{0}\" contains a constant color alpha channel.  DXT3 should be specified in the XML, or the alpha channel should be removed.", instanceName);
                return "D3DFMT_DXT3";
            }
            if (Has1BitAlphaChannel(width, height, rgba))
            {
                Emit(info, "Converting to DXT3 because \"{0}\" has a 1 bit alpha channel.  DXT3 should be specified in the XML, or the alpha channel should be removed.", instanceName);
                return "D3DFMT_DXT3";
            }
            Emit(info, "Converting to DXT5 because \"{0}\" does not have a 1-bit alpha channel.  DXT5 should be specified in the XML, or the alpha channel should be removed.", instanceName);
            return "D3DFMT_DXT5";
        }
        private static void Emit(Action<string> trace, string format, string instanceName)
        {
            if (trace != null) trace(String.Format(format, instanceName));
        }

        // Some bytes are padding/unwritten storage in the original native constructor.
        // Pass the same initial storage to compare complete byte arrays, including padding.
        public static byte[] GetNVCompressionOptions(string format, bool generateMipMaps, int platform, byte[] initialStorage)
        {
            if (platform != 0) throw new NotSupportedException("TextureCompiler supports Win32/PC only.");
            if (initialStorage != null && initialStorage.Length != CompressionOptionsSize)
                throw new ArgumentException("Expected 1644 bytes of initial native storage.", "initialStorage");
            byte[] result = initialStorage == null ? new byte[CompressionOptionsSize] : (byte[])initialStorage.Clone();
            SetNativeDefaults(result);
            int textureFormat;
            switch (format)
            {
                case "D3DFMT_A8R8G8B8": textureFormat = 7; break;
                case "D3DFMT_DXT5": textureFormat = 3; break;
                case "D3DFMT_DXT3": textureFormat = 2; break;
                case "D3DFMT_DXT1A": textureFormat = 1; break;
                case "D3DFMT_X8R8G8B8": textureFormat = 25; break;
                case "D3DFMT_R5G6B5": textureFormat = 6; break;
                default: textureFormat = 0; break;
            }
            I(result, 476, textureFormat);
            if (generateMipMaps)
            {
                I(result, 100, 30);
                I(result, 104, 0);
            }
            else I(result, 100, 32);
            return result;
        }
        private static void I(byte[] b, int offset, int value)
        {
            unchecked { b[offset] = (byte)value; b[offset + 1] = (byte)(value >> 8); b[offset + 2] = (byte)(value >> 16); b[offset + 3] = (byte)(value >> 24); }
        }
        private static void F(byte[] b, int offset, float value)
        {
            byte[] encoded = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(encoded);
            Buffer.BlockCopy(encoded, 0, b, offset, 4);
        }
        private static void SetNativeDefaults(byte[] b)
        {
            b[24] = 0; I(b, 28, 0); F(b, 32, 1f);
            I(b, 36, 1041); I(b, 40, 1010); I(b, 44, 1033);
            for (int i = 48; i <= 53; i++) b[i] = 0;
            I(b, 0, 71);
            F(b, 4, 400f);
            I(b, 60, 0);
            I(b, 64, 4);
            F(b, 68, 1f);
            F(b, 72, 1f);
            b[76] = 0;
            F(b, 80, 4096f);
            F(b, 84, 4096f);
            b[88] = 0;
            F(b, 92, 4096f);
            F(b, 96, 4096f);
            I(b, 100, 30);
            I(b, 104, 0);
            I(b, 108, 2);
            b[112] = 0;
            b[1633] = 0;
            b[120] = 0;
            b[121] = 0;
            b[122] = 0;
            b[123] = 0;
            b[124] = 0;
            b[125] = 0;
            F(b, 128, 0f);
            F(b, 132, 0f);
            F(b, 136, 0f);
            F(b, 140, 0f);
            b[144] = 0;
            b[145] = 0;
            F(b, 148, 0f);
            F(b, 152, 0f);
            F(b, 156, 0f);
            F(b, 160, 0f);
            I(b, 164, 0);
            F(b, 168, 0.15f);
            F(b, 116, 0.5f);
            b[1635] = 0;
            b[1636] = 0;
            b[1632] = 0;
            I(b, 440, 0);
            b[444] = 0;
            I(b, 448, 1);
            I(b, 8, 0);
            b[56] = 0;
            F(b, 12, 0.3086f);
            F(b, 16, 0.6094f);
            F(b, 20, 0.082f);
            b[452] = 0;
            F(b, 456, 2.2f);
            F(b, 460, 1f);
            F(b, 468, 10f);
            b[464] = 0;
            I(b, 472, 0);
            I(b, 476, 0);
            b[1631] = 0;
            b[1630] = 0;
            I(b, 1640, 0);
            F(b, 344, 3f);
            F(b, 348, 0f);
            F(b, 352, 5f);
            F(b, 356, 0.5f);
            F(b, 360, 0f);
            F(b, 364, 1f);
            F(b, 368, 1f);
            I(b, 372, 0);
            b[1638] = 0;
            b[1637] = 0;
            b[172] = 0;
            I(b, 480, 0);
            b[1508] = 0;
            F(b, 1512, 1f);
            F(b, 1516, 1f);
            F(b, 1520, 1f);
            F(b, 1524, 1f);
            F(b, 1528, 0f);
            F(b, 1532, 0f);
            F(b, 1536, 0f);
            F(b, 1540, 0f);
            F(b, 1544, 1f);
            F(b, 1548, 1f);
            F(b, 1552, 1f);
            F(b, 1556, 1f);
            F(b, 1560, 0f);
            F(b, 1564, 0f);
            F(b, 1568, 0f);
            F(b, 1572, 0f);
            b[1576] = 0;
            F(b, 1580, 0.3086f);
            F(b, 1584, 0.6094f);
            F(b, 1588, 0.082f);
            F(b, 1592, 0f);
            F(b, 1596, 0f);
            F(b, 1600, 0f);
            F(b, 1604, 0f);
            F(b, 1608, 0f);
            F(b, 1612, 1f);
            F(b, 1616, 1f);
            F(b, 1620, 1f);
            F(b, 1624, 1f);
            b[1629] = 0;
            b[1628] = 0;
            b[1634] = 0;
            float[] kernel = new float[] { 0f,0f,0f,0f,0f, 0f,0f,-2f,0f,0f, 0f,-2f,11f,-2f,0f, 0f,0f,-2f,0f,0f, 0f,0f,0f,0f,0f };
            for (int i = 0; i < kernel.Length; i++) F(b, 244 + i * 4, kernel[i]);
            for (int i = 0; i < 16; i++) I(b, 376 + i * 4, 1);
            for (int i = 484; i < 1508; i++) b[i] = 0;
        }
    }
}

