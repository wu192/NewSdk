using System;
namespace Recovered.Compiler.Texture
{
    // PC atlas prerequisite only: original format3 (DXT5) / format9 (RGB555),
    // option +104=5 emits exactly five levels, including repeated 1x1 tails.
    // Full TerrainTextureAtlas ProcessInstance integration is separate.
    public static class TextureAtlasDdsEncoder
    {
        public static byte[] Encode(uint width,uint height,byte[] rgba,bool normal)
        {
            if(rgba==null)throw new ArgumentNullException("rgba");
            if(width==0||height==0||(width&(width-1))!=0||(height&(height-1))!=0)throw new ArgumentException("PC atlas DDS requires positive power-of-two dimensions.");
            ulong pixels=(ulong)width*height;if(pixels>=16777216ul)throw new ArgumentOutOfRangeException("width");
            if((ulong)rgba.LongLength!=pixels*4)throw new ArgumentException("RGBA storage must match dimensions.","rgba");
            int w=(int)width,h=(int)height,natural=1;while(w!=1||h!=1){w=Math.Max(1,w/2);h=Math.Max(1,h/2);natural++;}
            long payload=0;w=(int)width;h=(int)height;
            for(int level=0;level<5;level++){payload=checked(payload+Size(w,h,normal));w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
            byte[] result=new byte[checked((int)(128+payload))];
            Put(result,0,0x20534444);Put(result,4,124);Put(result,8,0xa1007);Put(result,12,height);Put(result,16,width);Put(result,20,(uint)Size((int)width,(int)height,normal));Put(result,28,(uint)Math.Min(5,natural));
            Put(result,76,32);Put(result,80,normal?0x40u:4u);Put(result,108,0x401008);
            if(normal){Put(result,88,16);Put(result,92,0x7c00);Put(result,96,0x3e0);Put(result,100,0x1f);}else Put(result,84,0x35545844);
            float[] original=TextureTriangleMipEncoder.Normalize(rgba);w=(int)width;h=(int)height;int offset=128;
            for(int level=0;level<5;level++){
                float[] samples=TextureTriangleMipEncoder.ResizeLevel(original,(int)width,(int)height,w,h);
                if(normal){for(int at=0;at<samples.Length;at+=4){int value=Quantize(samples[at])<<10|Quantize(samples[at+1])<<5|Quantize(samples[at+2]);result[offset++]=(byte)value;result[offset++]=(byte)(value>>8);}}
                else{
                    byte[] encoded=TextureDdsEncoder.EncodeLevel(samples,w,h,"DXT5");
                    Buffer.BlockCopy(encoded,0,result,offset,encoded.Length);offset+=encoded.Length;
                }
                w=Math.Max(1,w/2);h=Math.Max(1,h/2);
            }
            if(offset!=result.Length)throw new InvalidOperationException("Fixed atlas mip layout mismatch.");return result;
        }
        static int Quantize(float value){return Math.Max(0,Math.Min(31,(int)((double)value*31+0.5)));}
        static long Size(int w,int h,bool normal){return normal?(long)w*h*2:(long)((w+3)/4)*((h+3)/4)*16;}
        static void Put(byte[] b,int at,uint n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(i*8));}
    }
}
