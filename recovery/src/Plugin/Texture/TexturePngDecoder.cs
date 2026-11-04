using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Recovered.Compiler.Texture
{
    // PNG scanline reconstruction follows the W3C PNG specification. The final
    // sample conversion/HasAlpha policy must match the legacy D3DX LoadImageA,
    // not a color-managed viewer. Unsupported profiles never call the old DLL.
    public static class TexturePngDecoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        public static bool IsPng(byte[] data)
        {
            if (data == null || data.Length < 8) return false;
            for (int i = 0; i < 8; i++) if (data[i] != Signature[i]) return false;
            return true;
        }
        private static InvalidDataException Invalid() { return new InvalidDataException("Invalid PNG image."); }
        private static uint Big(byte[] data, int at)
        { return (uint)data[at] << 24 | (uint)data[at + 1] << 16 | (uint)data[at + 2] << 8 | data[at + 3]; }
        private static uint Crc(byte[] data, int at, int count)
        {
            uint crc = 0xffffffff;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[at + i];
                for (int b = 0; b < 8; b++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            return ~crc;
        }
        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
        private static int Sample(byte[] row, int index, int depth)
        {
            if (depth == 8) return row[index];
            if (depth == 16) return row[index * 2] << 8 | row[index * 2 + 1];
            int bit = index * depth;
            return (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
        }
        public static TexturePacker.Image Decode(byte[] data)
        {
            if (!IsPng(data)) throw Invalid();
            int width = 0, height = 0, depth = 0, color = -1, interlace = 0;
            uint gamma=0;
            byte[] palette = null, transparency = null;
            bool header = false, ended = false, idatSeen = false, idatEnded = false, srgb = false;
            using (MemoryStream compressed = new MemoryStream())
            {
                int at = 8;
                while (at <= data.Length - 12)
                {
                    uint length = Big(data, at);
                    if (length > int.MaxValue || length > data.Length - at - 12) throw Invalid();
                    int n = (int)length, payload = at + 8;
                    string name = Encoding.ASCII.GetString(data, at + 4, 4);
                    if (Crc(data, at + 4, n + 4) != Big(data, payload + n)) throw Invalid();
                    if (!header && name != "IHDR") throw Invalid();
                    if (name != "IDAT" && idatSeen) idatEnded = true;
                    switch (name)
                    {
                        case "IHDR":
                            if (header || n != 13) throw Invalid();
                            uint w = Big(data, payload), h = Big(data, payload + 4);
                            if (w == 0 || h == 0 || w > 16384 || h > 16384 || (long)w * h > 16777216) throw Invalid();
                            width = (int)w; height = (int)h; depth = data[payload + 8]; color = data[payload + 9];
                            interlace = data[payload + 12];
                            if (data[payload + 10] != 0 || data[payload + 11] != 0 || interlace > 1) throw Invalid();
                            bool valid = color == 0 ? depth == 1 || depth == 2 || depth == 4 || depth == 8 || depth == 16 :
                                color == 3 ? depth == 1 || depth == 2 || depth == 4 || depth == 8 :
                                (color == 2 || color == 4 || color == 6) && (depth == 8 || depth == 16);
                            if (!valid) throw Invalid();
                            header = true; break;
                        case "PLTE":
                            if (idatSeen || palette != null || n == 0 || n % 3 != 0 || n > 768) throw Invalid();
                            palette = new byte[n]; Buffer.BlockCopy(data, payload, palette, 0, n); break;
                        case "tRNS":
                            if (idatSeen || transparency != null) throw Invalid();
                            if (color == 0 ? n != 2 : color == 2 ? n != 6 : color == 3 ? palette == null || n == 0 || n > palette.Length / 3 : true) throw Invalid();
                            transparency = new byte[n]; Buffer.BlockCopy(data, payload, transparency, 0, n); break;
                        case "gAMA":
                            if(idatSeen||n!=4||Big(data,payload)==0)throw Invalid();
                            gamma=Big(data,payload);break;
                        case "sRGB":
                            // Legacy libpng accepts intents 0..3 before IDAT. A
                            // valid sRGB declaration takes precedence over gAMA,
                            // including a gAMA chunk that follows it. Late or
                            // malformed declarations do not change the samples.
                            if(!idatSeen&&n==1&&data[payload]<=3)srgb=true;
                            break;
                        case "IDAT":
                            if (idatEnded || (color == 3 && palette == null)) throw Invalid();
                            compressed.Write(data, payload, n); idatSeen = true; break;
                        case "IEND":
                            if (n != 0 || !idatSeen) throw Invalid(); ended = true; break;
                        case "acTL": case "fcTL": case "fdAT":
                        case "iCCP":
                            // D3DX consumes the default PNG image in IDAT. It
                            // neither color-manages ICC profiles nor composites
                            // APNG frames; ancillary payload validity does not
                            // alter that image. Chunk framing/CRC remain checked.
                            break;
                        default:
                            if ((data[at + 4] & 32) == 0) throw new NotSupportedException("Unknown critical PNG chunk.");
                            break;
                    }
                    at += n + 12;
                    if (ended) break;
                }
                if (!ended) throw Invalid();
                if(srgb)gamma=45455;
                // NPOT decoding is coupled to D3DX texture creation and resampling.
                bool npot = (width & (width - 1)) != 0 || (height & (height - 1)) != 0;
                if(color==3||(color==4&&depth==16)||(color==0&&depth==16&&transparency!=null))
                    throw new BinaryAssetBuilder.BinaryAssetBuilderException(null,BinaryAssetBuilder.ErrorCode.BadTextureFile,"D3DX cannot load the texture from disk.",new object[0]);
                byte[] z = compressed.ToArray();
                if (z.Length < 6 || (z[0] & 15) != 8 || (z[0] >> 4) > 7 || ((z[0] << 8) + z[1]) % 31 != 0 || (z[1] & 32) != 0) throw Invalid();
                int channels = color == 0 || color == 3 ? 1 : color == 2 ? 3 : color == 4 ? 2 : 4;
                int bpp = Math.Max(1, (channels * depth + 7) / 8);
                uint[] pixels = new uint[checked(width * height)];
                ushort[] precise = npot && depth==16 && color!=2 ? new ushort[checked(width*height*4)] : null;
                uint adlerA = 1, adlerB = 0;
                using (MemoryStream packed = new MemoryStream(z, 2, z.Length - 6))
                using (DeflateStream inflate = new DeflateStream(packed, CompressionMode.Decompress))
                {
                    int[] sx = interlace == 0 ? new int[] { 0 } : new int[] { 0, 4, 0, 2, 0, 1, 0 };
                    int[] sy = interlace == 0 ? new int[] { 0 } : new int[] { 0, 0, 4, 0, 2, 0, 1 };
                    int[] dx = interlace == 0 ? new int[] { 1 } : new int[] { 8, 8, 4, 4, 2, 2, 1 };
                    int[] dy = interlace == 0 ? new int[] { 1 } : new int[] { 8, 8, 8, 4, 4, 2, 2 };
                    for (int pass = 0; pass < sx.Length; pass++)
                    {
                        int pw = width <= sx[pass] ? 0 : (width - sx[pass] + dx[pass] - 1) / dx[pass];
                        int ph = height <= sy[pass] ? 0 : (height - sy[pass] + dy[pass] - 1) / dy[pass];
                        if (pw == 0 || ph == 0) continue;
                        int rowLength = checked((pw * channels * depth + 7) / 8);
                        byte[] previous = new byte[rowLength], row = new byte[rowLength];
                        for (int y = 0; y < ph; y++)
                        {
                            int filter = Read(inflate, ref adlerA, ref adlerB);
                            if (filter > 4) throw Invalid();
                            for (int i = 0; i < rowLength; i++)
                            {
                                int value = Read(inflate, ref adlerA, ref adlerB);
                                int left = i >= bpp ? row[i - bpp] : 0, up = previous[i], corner = i >= bpp ? previous[i - bpp] : 0;
                                int predictor = filter == 0 ? 0 : filter == 1 ? left : filter == 2 ? up : filter == 3 ? (left + up) / 2 : Paeth(left, up, corner);
                                row[i] = unchecked((byte)(value + predictor));
                            }
                            for (int x = 0; x < pw; x++)
                            {
                                int index = x * channels;
                                int r, g, b, a = 255;
                                if (color == 3)
                                {
                                    int entry = Sample(row, index, depth);
                                    if (entry >= palette.Length / 3) throw Invalid();
                                    r = palette[entry * 3]; g = palette[entry * 3 + 1]; b = palette[entry * 3 + 2];
                                    if (transparency != null && entry < transparency.Length) a = transparency[entry];
                                }
                                else
                                {
                                    r = Sample(row, index, depth); g = r; b = r;
                                    if (color == 2 || color == 6) { g = Sample(row, index + 1, depth); b = Sample(row, index + 2, depth); }
                                    if (transparency != null && color==0)
                                    {
                                        int tr = transparency[0] << 8 | transparency[1];
                                        if (r == tr && (color == 0 || (g == (transparency[2] << 8 | transparency[3]) && b == (transparency[4] << 8 | transparency[5])))) a = 0;
                                    }
                                    if (color == 4 || color == 6) a = Sample(row, index + channels - 1, depth);
                                    if(color==2&&depth==16)
                                    {
                                        // Original creates X8R8G8B8 but copies width*4
                                        // bytes from little-endian B16 G16 R16 FF00 rows.
                                        // Consequently only the first half of source
                                        // pixels survive, split across two output pixels.
                                        int sourceX=sx[pass]+x*dx[pass],sourceY=sy[pass]+y*dy[pass],outputX=sourceX*2;
                                        if(outputX<width)
                                        {
                                            int alphaLow=transparency!=null&&!(r==(transparency[0]<<8|transparency[1])&&g==(transparency[2]<<8|transparency[3])&&b==(transparency[4]<<8|transparency[5]))?255:0;
                                            r=Gamma16(r,gamma);g=Gamma16(g,gamma);b=Gamma16(b,gamma);
                                            pixels[sourceY*width+outputX]=0xff000000u|(uint)(g&255)|(uint)(b>>8)<<8|(uint)(b&255)<<16;
                                            if(outputX+1<width)pixels[sourceY*width+outputX+1]=0xff000000u|(uint)alphaLow|(uint)(r>>8)<<8|(uint)(r&255)<<16;
                                        }
                                        continue;
                                    }
                                    if(precise!=null)
                                    {
                                        int target=((sy[pass]+y*dy[pass])*width+sx[pass]+x*dx[pass])*4;
                                        precise[target]=(ushort)Gamma16(r,gamma);precise[target+1]=(ushort)Gamma16(g,gamma);precise[target+2]=(ushort)Gamma16(b,gamma);precise[target+3]=(ushort)(color==6?a:65535);
                                    }
                                    if(depth==16)
                                    {
                                        r=ConvertSample(r,65535,gamma);g=ConvertSample(g,65535,gamma);b=ConvertSample(b,65535,gamma);
                                        if(color==4||color==6)a=(a*255+32767)/65535;
                                    }
                                    else
                                    {
                                        int max=(1<<depth)-1;
                                        r=ConvertSample(r,max,gamma);g=ConvertSample(g,max,gamma);b=ConvertSample(b,max,gamma);
                                    }
                                }
                                pixels[(sy[pass] + y * dy[pass]) * width + sx[pass] + x * dx[pass]] = (uint)(r | g << 8 | b << 16) | (uint)a << 24;
                            }
                            byte[] swap = previous; previous = row; row = swap;
                        }
                    }
                    if (inflate.ReadByte() != -1 || (adlerB << 16 | adlerA) != Big(z, z.Length - 4)) throw Invalid();
                }
                TexturePacker.Image image = new TexturePacker.Image(width, height, pixels, color == 6);
                if(!npot)return image;
                if(precise!=null)return TextureNpotStorageResampler.Resize(width,height,precise,color==6,color==0?TextureNpotStorageResampler.Format.L16:TextureNpotStorageResampler.Format.Rgba16);
                if(color==0||color==4)return TextureNpotStorageResampler.Resize(image,color==4||transparency!=null?TextureNpotStorageResampler.Format.A8L8:TextureNpotStorageResampler.Format.L8);
                return TextureNpotRgba8Resampler.Resize(image);
            }
        }
        private static double GammaProduct(uint gamma)
        {
            // D3DX passes single-precision file/screen gamma values to the
            // double-precision gamma table builder. Keep both float roundings,
            // then promote before multiplication/pow; all-double is observably
            // different at rare 16-bit quantization boundaries.
            float fileGamma=(float)(gamma/100000.0);
            return (double)fileGamma*(double)2.2f;
        }
        private static int Gamma16(int sample,uint gamma)
        {
            double product=gamma==0?1.0:GammaProduct(gamma);
            if(Math.Abs(product-1.0)<=0.05)return sample;
            return (int)(65535.0*Math.Pow(sample/65535.0,1.0/product)+0.5);
        }
        private static int ConvertSample(int sample,int max,uint gamma)
        {
            double product=gamma==0?1.0:GammaProduct(gamma);
            if(Math.Abs(product-1.0)<=0.05)return (sample*255+max/2)/max;
            return (int)(255.0*Math.Pow(sample/(double)max,1.0/product)+0.5);
        }
        private static int Read(Stream input, ref uint a, ref uint b)
        {
            int value = input.ReadByte(); if (value < 0) throw Invalid();
            a = (a + (uint)value) % 65521; b = (b + a) % 65521; return value;
        }
    }
}

