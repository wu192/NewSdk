using System;
using System.Collections.Generic;

namespace Recovered.Compiler.Texture
{
    // Original PC gold 63dcfd2b / 40793184986: all 12480 complete images
    // ignore TGA 2.0 rendering metadata, tables, developer data and coordinates.
    // Validate the declared tail regions without applying gamma, alpha or color
    // correction. Pixel/RLE validation and the legacy DevIL dialect stay separate.
    internal static class TextureTgaMetadata
    {
        private struct Region { internal int Start, End; internal Region(int start,int end){Start=start;End=end;} }
        private static int Word(byte[] data,int at){return data[at]|data[at+1]<<8;}
        private static uint Dword(byte[] data,int at){return (uint)data[at]|(uint)data[at+1]<<8|(uint)data[at+2]<<16|(uint)data[at+3]<<24;}
        private static bool Text(byte[] data,int at,string value){if(at<0||at>data.Length-value.Length)return false;for(int i=0;i<value.Length;i++)if(data[at+i]!=(byte)value[i])return false;return true;}
        private static bool Add(List<Region> regions,uint at,long size,int start,int limit)
        {
            if(size==0&&at==0)return true;
            if(size<0||at<(uint)start||(long)at+size>limit)return false;
            regions.Add(new Region((int)at,(int)((long)at+size)));return true;
        }
        private static bool Footer(byte[] data,int at){return Text(data,at+8,"TRUEVISION-XFILE.\0");}
        private static bool Frame(byte[] data,int start,int footer,List<Region> regions)
        {
            uint extension=Dword(data,footer),developer=Dword(data,footer+4);
            if(extension!=0){
                if(!Add(regions,extension,495,start,footer))return false;int ext=(int)extension;
                // Some D3DX-compatible writers leave the extension size word
                // zero (for example TNW 7.1 VIPEMP.tga). The footer still owns
                // a complete 495-byte extension; do not weaken its region bounds.
                int size=Word(data,ext);if((size!=0&&size!=495)||data[ext+494]>4)return false;
                // The already accepted DevIL writer declares 495 but emits 507
                // bytes with shifted fields. Its bounded legacy validator owns it.
                if(Text(data,ext+438,"Developer's Image Library (DevIL)"))return false;
                uint correction=Dword(data,ext+482),stamp=Dword(data,ext+486),scanlines=Dword(data,ext+490);
                if(correction!=0&&!Add(regions,correction,2048,start,footer))return false;
                if(scanlines!=0){
                    int height=Word(data,14);if(!Add(regions,scanlines,(long)height*4,start,footer))return false;
                    int pixelStart=18+data[0]+(data[1]==1?Word(data,5)*((data[7]+7)/8):0);
                    for(int row=0;row<height;row++){uint offset=Dword(data,(int)scanlines+row*4);if(offset<(uint)pixelStart||offset>=(uint)start)return false;}
                }
                if(stamp!=0){
                    if(!Add(regions,stamp,2,start,footer))return false;
                    int width=data[(int)stamp],height=data[(int)stamp+1];
                    if(width<1||height<1||width>64||height>64||!Add(regions,stamp,2L+(long)width*height*((data[16]+7)/8),start,footer))return false;
                }
            }
            if(developer!=0){
                if(!Add(regions,developer,2,start,footer))return false;int count=Word(data,(int)developer);
                if(!Add(regions,developer,2L+count*10L,start,footer))return false;
                for(int i=0;i<count;i++){int at=(int)developer+2+i*10;if(!Add(regions,Dword(data,at+2),Dword(data,at+6),start,footer))return false;}
            }
            return Add(regions,(uint)footer,26,start,data.Length);
        }
        internal static bool TryValidateTail(byte[] data,int start)
        {
            if(data==null||data.Length<18||start<18||start>data.Length)return false;
            if(start==data.Length)return true;
            if(data.Length-start<26||!Footer(data,data.Length-26))return false;
            var regions=new List<Region>();bool final=false;
            // Chained extension/footer records exist in real accepted inputs.
            // Auxiliary regions can precede or follow their owning extension.
            for(int at=start;at<=data.Length-26;at++)if(Footer(data,at)){
                var frame=new List<Region>();bool valid=Frame(data,start,at,frame);
                if(at==data.Length-26){if(!valid)return false;final=true;}
                if(valid)regions.AddRange(frame);
            }
            if(!final)return false;
            regions.Sort(delegate(Region a,Region b){return a.Start.CompareTo(b.Start);});
            int covered=start;foreach(Region region in regions){if(region.Start>covered)return false;if(region.End>covered)covered=region.End;}
            return covered==data.Length;
        }
    }
}
