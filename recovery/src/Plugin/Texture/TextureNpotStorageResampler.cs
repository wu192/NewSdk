using System;
using System.Runtime.InteropServices;
namespace Recovered.Compiler.Texture
{
    // PC D3DX source-format filtering: preserve source precision until the
    // destination format's row store, then apply LoadImageA's RGBA conversion.
    internal static class TextureNpotStorageResampler
    {
        internal enum Format { L8, A8L8, L16, Rgb555, Argb1555, Rgba16 }
        [StructLayout(LayoutKind.Explicit)]struct Bits { [FieldOffset(0)]public float Value;[FieldOffset(0)]public uint Word; }
        static float Truncate(double value){Bits b=new Bits{Value=(float)value};if(b.Value>value)b.Word--;return b.Value;}
        static readonly float[] Dither={31f/32,15f/32,27f/32,11f/32,7f/32,23f/32,3f/32,19f/32,25f/32,9f/32,29f/32,13f/32,1f/32,17f/32,5f/32,21f/32};
        static int Pot(int n){int p=1;while(p<n)p=checked(p*2);return p;}
        static int Quantize(float value,int max,double dither){return Math.Max(0,Math.Min(max,(int)Truncate((double)Truncate((double)value*max)+dither)));}
        internal static TexturePacker.Image Resize(TexturePacker.Image source,Format format)
        {
            ushort[] samples=new ushort[checked(source.Pixels.Length*4)];
            bool five=format==Format.Rgb555||format==Format.Argb1555;
            for(int i=0;i<source.Pixels.Length;i++)for(int c=0;c<4;c++){
                int value=(int)(source.Pixels[i]>>(8*c)&255);samples[i*4+c]=(ushort)(five?(c==3?value/255:(value*31+127)/255):value);
            }
            return Resize(source.Width,source.Height,samples,source.HasAlpha,format);
        }
        internal static TexturePacker.Image Resize(int width,int height,ushort[] samples,bool alpha,Format format)
        {
            if(width<1||height<1||width>8192||height>8192)throw new NotSupportedException("PC NPOT source dimensions exceed the validated range.");
            int tw=Pot(width),th=Pot(height);if((long)tw*th>=16777216L)throw new NotSupportedException("Rounded image exceeds the original safe RGBA range.");
            if(samples==null||samples.LongLength!=(long)width*height*4)throw new ArgumentException("Source sample storage must match dimensions.");
            bool five=format==Format.Rgb555||format==Format.Argb1555,word=format==Format.L16||format==Format.Rgba16,lum=format==Format.L8||format==Format.A8L8||format==Format.L16;
            int max=five?31:word?65535:255;double inverse=five?0.032258063554763794:word?0.000015259021893143654:0.0039215688593685627;
            var hx=TextureNpotRgba8Resampler.Contributions(width,tw);var vy=TextureNpotRgba8Resampler.Contributions(height,th);
            float[] output=new float[checked(tw*th*4)],pixel=new float[4],joint=new float[1];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                for(int c=0;c<4;c++)pixel[c]=(float)((double)samples[(y*width+x)*4+c]*(five&&c==3?1.0:inverse));
                foreach(var yy in vy[y])foreach(var xx in hx[x]){joint[0]=(float)((double)xx.Weight*yy.Weight);int at=(yy.Target*tw+xx.Target)*4;for(int c=0;c<4;c++)output[at+c]=(float)((double)output[at+c]+(double)pixel[c]*joint[0]);}
            }
            uint[] pixels=new uint[tw*th];
            for(int y=0;y<th;y++)for(int x=0;x<tw;x++){
                int at=(y*tw+x)*4;double d=Dither[(y&3)*4+(((y&1)==0?x:tw-1-x)&3)];
                if(lum)output[at]=Truncate((double)output[at+1]*0.715399980545044+(double)output[at]*0.21250000596046448+(double)output[at+2]*0.072099998593330383);
                uint rgba=0;
                for(int c=0;c<4;c++){
                    int scale=five&&c==3?1:max;int channel=format==Format.Rgba16?(c==0?3:c==1?2:c==2?0:1):lum&&c<3?0:c;float v=output[at+channel];
                    int q=Quantize(v,scale,d);int b=(q*255+scale/2)/scale;
                    if(c==3&&format!=Format.A8L8&&format!=Format.Argb1555&&format!=Format.Rgba16)b=255;
                    rgba|=(uint)b<<(8*c);
                }pixels[y*tw+x]=rgba;
            }
            return new TexturePacker.Image(tw,th,pixels,alpha);
        }
    }
}
