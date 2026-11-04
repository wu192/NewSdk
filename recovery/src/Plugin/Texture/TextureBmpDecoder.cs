using System;
using System.IO;
namespace Recovered.Compiler.Texture
{
    // PC D3DX BI_RGB storage. Each supported DIB has its own header layout;
    // bitfields are not interchangeable with the corresponding BI_RGB pixels.
    public static class TextureBmpDecoder
    {
        public static bool IsBmp(byte[] data) { return data != null && data.Length >= 2 && data[0] == 66 && data[1] == 77; }
        private static int U16(byte[] b,int p){return b[p]|b[p+1]<<8;}
        private static uint U32(byte[] b,int p){return (uint)b[p]|(uint)b[p+1]<<8|(uint)b[p+2]<<16|(uint)b[p+3]<<24;}
        private static InvalidDataException Invalid(){return new InvalidDataException("Invalid BMP image.");}
        private static BinaryAssetBuilder.BinaryAssetBuilderException CannotLoad(){return new BinaryAssetBuilder.BinaryAssetBuilderException(null,BinaryAssetBuilder.ErrorCode.BadTextureFile,"D3DX cannot load the texture from disk.",new object[0]);}
        public static TexturePacker.Image Decode(byte[] data)
        {
            if(!IsBmp(data)||data.Length<18)throw Invalid();
            uint dib=U32(data,14);
            if(dib!=12&&dib!=40&&dib!=52&&dib!=56&&dib!=64&&dib!=108&&dib!=124)throw new NotSupportedException("This BMP DIB revision has not been recovered.");
            int headerEnd=14+(int)dib;
            if(data.Length<headerEnd)throw Invalid();
            bool core=dib==12;
            int width=core?U16(data,18):unchecked((int)U32(data,18));
            int signedHeight=core?U16(data,20):unchecked((int)U32(data,22));
            int planes=U16(data,core?22:26),depth=U16(data,core?24:28);
            uint compression=core?0:U32(data,30),offset=U32(data,10),colors=core?0:U32(data,46);
            // The original core12 adapter constructs the same BI_RGB parser
            // header used by INFO40/OS2-64. All three direct-color depths use
            // the shared RGB555/BGR24/BGRA32 rules; OS2-64 also has signed height.
            // Keep compressed OS/2 storage outside that BI_RGB contract.
            if((core||dib==64)&&compression!=0)throw new NotSupportedException("Compressed OS/2 BMP storage has not been recovered.");
            if(compression==3)
            {
                if(dib==40)throw CannotLoad();
                // Extended-DIB 24/32-bit masks have repeat-varying original
                // output; 16-bit masks have a separate, unresolved descriptor
                // interpretation. Neither is a deterministic BI_RGB fallback.
                throw new NotSupportedException("Extended-DIB BMP bitfields have not been recovered.");
            }
            if(compression==6)throw CannotLoad();
            if(compression!=0)throw new NotSupportedException("Compressed BMP has not been recovered.");
            if(planes!=1||width<=0||signedHeight==0||signedHeight==int.MinValue||width>16384||Math.Abs(signedHeight)>16384)throw Invalid();
            int height=Math.Abs(signedHeight);
            if((long)width*height>16777216)throw Invalid();
            if(depth!=1&&depth!=4&&depth!=8&&depth!=16&&depth!=24&&depth!=32)throw Invalid();
            bool npot=(width&(width-1))!=0||(height&(height-1))!=0;
            if(depth<=8)throw CannotLoad();
            int stride=checked(((width*depth+31)/32)*4);
            // Native DIB parser 0x13FE8..0x1400D skips biClrUsed RGBQUADs
            // even for direct-colour BI_RGB. They do not remap the raster.
            long pixelStart=headerEnd+(long)colors*4;
            long rasterBytes=(long)stride*height;
            if(pixelStart>data.Length||offset<pixelStart||offset>data.Length||rasterBytes>data.Length-offset||rasterBytes>data.Length-pixelStart)throw Invalid();
            if(dib==124)
            {
                uint profileOffset=U32(data,126),profileBytes=U32(data,130);
                if((profileOffset==0)!=(profileBytes==0))throw Invalid();
                if(profileBytes!=0)
                {
                    // bV5ProfileData is relative to the DIB rather than the
                    // file. Preserve the observed post-raster bounded profile;
                    // metadata does not transform the decoded PC colors.
                    long profileStart=14L+profileOffset;
                    if(profileStart<offset+rasterBytes||profileStart+profileBytes>data.Length)throw Invalid();
                }
            }
            // Legacy D3DX BI_RGB consumes pixels immediately after the actual
            // DIB, ignoring bfOffBits (including any extra file gap). V4/V5
            // gamma/color-space fields and bounded V5 ICC have no render effect.
            offset=(uint)pixelStart;
            bool hasAlpha=depth==24;
            if(depth==32)for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(data[(int)offset+y*stride+x*4+3]!=0)hasAlpha=true;
            uint[] pixels=new uint[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int row=checked((int)offset+(signedHeight<0?y:height-1-y)*stride);
                uint r,g,b,a=255;
                if(depth==16)
                {
                    int v=U16(data,row+x*2),rv=(v>>10)&31,gv=(v>>5)&31,bv=v&31;
                    r=(uint)((rv*255+15)/31);g=(uint)((gv*255+15)/31);b=(uint)((bv*255+15)/31);
                }
                else
                {
                    int p=row+x*(depth/8);b=data[p];g=data[p+1];r=data[p+2];if(depth==32&&hasAlpha)a=data[p+3];
                }
                pixels[y*width+x]=r|g<<8|b<<16|a<<24;
            }
            TexturePacker.Image image=new TexturePacker.Image(width,height,pixels,hasAlpha);
            return !npot?image:depth==16?TextureNpotStorageResampler.Resize(image,TextureNpotStorageResampler.Format.Rgb555):TextureNpotRgba8Resampler.Resize(image);
        }
    }
}


