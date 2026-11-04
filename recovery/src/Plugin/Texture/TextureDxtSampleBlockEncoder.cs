using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Recovered.Compiler.Texture
{
    // PC quality71 float/partial DXT3/5 and transparent-block DXT1A. Preserves the original wrapper's index
    // map, including holes in 1x1/2x2; alpha holes are zero indices, not edges.
    public static class TextureDxtSampleBlockEncoder
    {
        private static int Colour(byte[] state,int bytes,int mode) { return TextureNativeCodec.Colour(state,bytes,mode); }
        private static int PublicAlpha(float[] rgba,int count,int[] endpoints,int[] indices,out int levels) { return TextureNativeCodec.Alpha(rgba,count,endpoints,indices,out levels); }
        public static byte[] EncodeBlock(byte[] rgba,int width,int height,string format)
        {
            if(rgba==null)throw new ArgumentNullException("rgba");
            Validate(width,height,rgba.Length,format);
            float[] samples=new float[rgba.Length];for(int i=0;i<samples.Length;i++)samples[i]=(float)(rgba[i]/255.0);
            return EncodeFloatBlock(samples,width,height,format);
        }
        private static void Validate(int width,int height,int length,string format)
        {
            if((width!=1&&width!=2&&width!=4)||(height!=1&&height!=2&&height!=4))throw new NotSupportedException("A power-of-two block with dimensions 1, 2 or 4 is required.");
            if(length!=width*height*4)throw new ArgumentException("RGBA storage must match block dimensions.");
            if(format!="D3DFMT_DXT3"&&format!="D3DFMT_DXT5")throw new NotSupportedException("Only PC Production DXT3/DXT5 is accepted.");
        }
        public static byte[] EncodeFloatBlock(float[] rgba,int width,int height,string format)
        {
            if(rgba==null)throw new ArgumentNullException("rgba");Validate(width,height,rgba.Length,format);
            foreach(float value in rgba)if(Single.IsNaN(value)||value<0||value>1)throw new ArgumentException("Finite normalized RGBA samples are required.","rgba");
            int count=width*height;byte[] state=new byte[0x3850];PutInt(state,0,format=="D3DFMT_DXT3"?2:3);PutInt(state,4,1);PutInt(state,8,count);
            for(int i=0;i<count;i++)for(int c=0;c<3;c++)PutDouble(state,0x10+(i*3+c)*8,rgba[i*4+c]);
            // Original default block area stays 16 even for partial blocks.
            PutDouble(state,0x3010,16);PutDouble(state,0x3018,(double)0.3086f);PutDouble(state,0x3020,(double)0.6094f);PutDouble(state,0x3028,(double)0.082f);PutInt(state,0x3030,71);
            int status=Colour(state,state.Length,0);if(status!=0)throw new InvalidOperationException("Colour state rejected: "+status);
            if(BitConverter.ToInt32(state,0x3034)!=4)throw new InvalidOperationException("DXT3/5 requires four colour levels.");
            ushort first=Pack565(state,0x3038),second=Pack565(state,0x3044);int[] indices=new int[count];
            for(int i=0;i<count;i++)indices[i]=BitConverter.ToInt32(state,0x3050+i*4);
            if(first==second){first=unchecked((ushort)(first+1));for(int i=0;i<count;i++)indices[i]=1;}
            bool swap=second>first;if(swap){ushort tmp=first;first=second;second=tmp;}
            byte[] result=format=="D3DFMT_DXT3"?ExplicitAlpha(rgba,width,height):InterpolatedAlpha(rgba,width,height);
            result[8]=(byte)first;result[9]=(byte)(first>>8);result[10]=(byte)second;result[11]=(byte)(second>>8);
            int[] map=ColourMap(width,height);uint bits=0;
            for(int i=0;i<16;i++){
                int index=map[i]<0?3:indices[map[i]];
                if(index<0||index>3)throw new InvalidOperationException("Invalid colour index.");
                if(map[i]>=0&&swap)index^=1;bits|=(uint)index<<(i*2);
            }
            for(int i=0;i<4;i++)result[12+i]=(byte)(bits>>(8*i));return result;
        }
        private static int[] ColourMap(int width,int height)
        {
            int[] m=new int[16];for(int i=0;i<16;i++)m[i]=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)m[y*4+x]=y*width+x;
            // Literal default PC dispatch mapping at original RVA 0x5d3c5..0x5d4de.
            switch((height<<4)|width){
                case 0x11:m[1]=m[0];goto L3f7;
                case 0x12:m[12]=m[8]=m[4]=m[0];goto L3f7;
                case 0x14:for(int y=1;y<4;y++)for(int x=0;x<4;x++)m[y*4+x]=m[x];return m;
                case 0x21:m[1]=m[2]=m[3]=m[0];m[8]=m[12]=m[4];m[1]=m[4];goto L405;
                case 0x22:m[2]=m[3]=m[1];m[6]=m[7]=m[10]=m[11]=m[15]=m[14]=m[5];m[8]=m[12]=m[4];return m;
                case 0x24:for(int y=2;y<4;y++)for(int x=0;x<4;x++)m[y*4+x]=m[4+x];return m;
                case 0x41:m[1]=m[2]=m[3]=m[0];m[5]=m[6]=m[7]=m[4];m[9]=m[10]=m[11]=m[8];m[1]=m[12];goto L42f;
                case 0x42:m[2]=m[3]=m[1];m[6]=m[7]=m[5];m[10]=m[11]=m[9];goto L436;
                default:return m;
            }
        L3f7:m[3]=m[2]=m[1];
        L405:m[5]=m[6]=m[7]=m[9]=m[10]=m[11]=m[1];
        L42f:m[13]=m[1];
        L436:m[14]=m[15]=m[13];m[1]=m[2]=m[3];m[5]=m[6]=m[7];m[9]=m[10]=m[11];return m;
        }
        private static byte[] ExplicitAlpha(float[] rgba,int width,int height)
        {
            byte[] output=new byte[16];for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                int index=y*4+x;int value=(int)((double)rgba[(y*width+x)*4+3]*15.0+0.5);
                output[index/2]|=(byte)(value<<((index&1)*4));
            }return output;
        }
        private static byte[] InterpolatedAlpha(float[] rgba,int width,int height)
        {
            int count=width*height,numLevels;int[] endpoints=new int[2],resultIndices=new int[count];
            if(PublicAlpha(rgba,count,endpoints,resultIndices,out numLevels)!=0)throw new InvalidOperationException("Public S3 alpha input rejected.");
            int low=endpoints[0],high=endpoints[1];
            if(low==high){high++;numLevels=6;Array.Clear(resultIndices,0,count);}
            bool swap=(low>high)==(numLevels==6);if(swap){int tmp=low;low=high;high=tmp;}
            byte[] output=new byte[16];output[0]=(byte)Math.Max(0,Math.Min(255,low));output[1]=(byte)Math.Max(0,Math.Min(255,high));ulong bits=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                int index=resultIndices[y*width+x];if(swap){if(numLevels==8)index=index>1?9-index:1-index;else if(index<6)index=index>1?7-index:1-index;}
                bits|=(ulong)index<<((y*4+x)*3);
            }
            for(int i=0;i<6;i++)output[2+i]=(byte)(bits>>(i*8));return output;
        }
        private static ushort Pack565(byte[] state,int offset){int r=BitConverter.ToInt32(state,offset),g=BitConverter.ToInt32(state,offset+4),b=BitConverter.ToInt32(state,offset+8);if(r<0||r>31||g<0||g>63||b<0||b>31)throw new InvalidOperationException("Invalid endpoint.");return(ushort)((r<<11)|(g<<5)|b);}
        private static void PutInt(byte[] b,int o,int v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,4);}
        private static void PutDouble(byte[] b,int o,double v){Buffer.BlockCopy(BitConverter.GetBytes(v),0,b,o,8);}
    }
}
