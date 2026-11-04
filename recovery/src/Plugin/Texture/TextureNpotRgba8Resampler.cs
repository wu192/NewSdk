using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Recovered.Compiler.Texture
{
    // PC D3DX RGBA8 resampling for true-color TGA24/32, PNG RGB/RGBA8
    // and BI_RGB BMP24/32. Other source quantization paths remain rejected.
    public static class TextureNpotRgba8Resampler
    {
        internal sealed class Tap { public int Target; public float Weight; }
        // Original numeric probe 678db012 / 40529918922: exact constants.
        private const double InverseByte = 0.0039215688593685627;
        private const float MinimumWeight = 0.00001f;
        private static readonly float[] Dither = {
            31f/32,15f/32,27f/32,11f/32,
            7f/32,23f/32,3f/32,19f/32,
            25f/32,9f/32,29f/32,13f/32,
            1f/32,17f/32,5f/32,21f/32
        };
        internal static Tap[][] Contributions(int source,int target)
        {
            // Literal float stores and contribution order from RVA 0x18ad8.
            // The default filter wraps each axis and integrates a triangular
            // reconstruction kernel over the output pixel's unit footprint.
            float[] f=new float[9];f[0]=(float)((double)target/source);f[1]=(float)(0.5/(double)f[0]);
            Tap[][] result=new Tap[source][];
            for(int input=0;input<source;input++){
                var taps=new List<Tap>();int previous=-1;f[8]=0;
                for(int half=0;half<2;half++){
                    f[2]=(float)((double)input+half-0.5);
                    f[3]=(float)((double)f[2]*f[0]);f[4]=(float)((double)f[3]+f[0]);
                    for(int cell=(int)Math.Floor((double)f[3]);(double)cell<f[4];cell++){
                        int mapped=cell<0?cell+target:cell>=target?cell-target:cell;
                        if(mapped!=previous){if(f[8]>MinimumWeight)taps.Add(new Tap{Target=previous,Weight=f[8]});f[8]=0;previous=mapped;}
                        f[5]=Math.Max((float)cell,f[3]);f[6]=Math.Min((float)(cell+1),f[4]);
                        f[7]=(float)(((double)f[6]+f[5])*f[1]-f[2]);
                        double average=half==0?(double)f[7]:1.0-(double)f[7];
                        f[8]=(float)((double)f[8]+average*((double)f[6]-f[5]));
                    }
                }
                if(f[8]>MinimumWeight)taps.Add(new Tap{Target=previous,Weight=f[8]});
                result[input]=taps.ToArray();
            }
            return result;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits { [FieldOffset(0)]public float Value; [FieldOffset(0)]public uint Bits; }
        private static float TruncateFloat(double value)
        {
            // Original RGBA8 row encoder @0x1cfd0 sets x87 RC=toward-zero,
            // stores the scaled sample and then its dithered value as float32.
            // For finite non-negative values, one predecessor corrects a
            // nearest/upward float conversion without changing caller FPU state.
            FloatBits bits=new FloatBits{Value=(float)value};
            if((double)bits.Value>value)bits.Bits--;
            return bits.Value;
        }
        private static int PowerOfTwo(int value){int result=1;while(result<value)result=checked(result*2);return result;}
        public static TexturePacker.Image Resize(TexturePacker.Image source)
        {
            if(source==null)throw new ArgumentNullException("source");
            int width=source.Width,height=source.Height;
            if(width<1||height<1||width>8192||height>8192)throw new NotSupportedException("The isolated RGBA8 resampler accepts positive dimensions up to 8192.");
            if(source.Pixels==null||(long)width*height!=source.Pixels.LongLength)throw new ArgumentException("Pixel storage must match image dimensions.","source");
            int targetWidth=PowerOfTwo(width),targetHeight=PowerOfTwo(height);
            if((long)targetWidth*targetHeight>=16777216L)throw new NotSupportedException("Rounded image exceeds the original safe RGBA range.");
            if(targetWidth==width&&targetHeight==height)return source;
            Tap[][] horizontal=Contributions(width,targetWidth),vertical=Contributions(height,targetHeight);
            float[] output=new float[checked(targetWidth*targetHeight*4)],pixel=new float[4],joint=new float[1];
            // RVA 0x194fc scatters in source y/x order. X/Y multiplication is
            // rounded once to float32 before each channel's extended multiply-add.
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                uint rgba=source.Pixels[y*width+x];for(int c=0;c<4;c++)pixel[c]=(float)(((rgba>>(c*8))&255)*InverseByte);
                foreach(Tap vy in vertical[y])foreach(Tap vx in horizontal[x]){
                    joint[0]=(float)((double)vx.Weight*vy.Weight);int offset=(vy.Target*targetWidth+vx.Target)*4;
                    for(int c=0;c<4;c++)output[offset+c]=(float)((double)output[offset+c]+(double)pixel[c]*joint[0]);
                }
            }
            uint[] pixels=new uint[targetWidth*targetHeight];
            for(int y=0;y<targetHeight;y++)for(int x=0;x<targetWidth;x++){
                int at=(y*targetWidth+x)*4;uint rgba=0;double dither=Dither[(y&3)*4+(((y&1)==0?x:targetWidth-1-x)&3)];
                for(int c=0;c<4;c++){
                    double sample=Math.Max(0.0,Math.Min(1.0,(double)output[at+c]));
                    float scaled=TruncateFloat(sample*255.0);
                    int value=(int)TruncateFloat((double)scaled+dither);value=Math.Max(0,Math.Min(255,value));rgba|=(uint)value<<(c*8);
                }pixels[y*targetWidth+x]=rgba;
            }
            return new TexturePacker.Image(targetWidth,targetHeight,pixels,source.HasAlpha);
        }
    }
}
