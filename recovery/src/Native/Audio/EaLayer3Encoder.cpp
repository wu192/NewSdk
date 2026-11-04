/* NewSdk EALayer3 V1 integration, GPL-3.0-or-later.
 * Encoding/granule/startup algorithm adapted from Jonathan Wilson's sagetools
 * encealayer31.cpp (Copyright 2026 Jonathan Wilson). See EaLayer3/NOTICE.md.
 * LAME and MP3toEA instances, reservoir, startup decoder and buffers belong to
 * one channel pair in one segment. No encoder state is shared between jobs.
 */
#include "EaLayer3/system.h"
#include "EaLayer3/lame396/lame.h"
#include "EaLayer3/lame396/lame_global_flags.h"
#include "EaLayer3/mp3toea.h"
#include "EaLayer3/mpegcommon.h"
#include <algorithm>
#include <array>
#include <deque>
#include <memory>
#include <string>
#include <vector>
#include <limits>

DetectCPU staticDetectCPU;
namespace {
using Bytes = std::vector<unsigned char>;
void be32(Bytes& b, unsigned v) { for (int n=24;n>=0;n-=8) b.push_back((unsigned char)(v>>n)); }
void put32(Bytes& b, size_t at, unsigned v) { for (int n=3;n>=0;--n) { b[at+n]=(unsigned char)v; v>>=8; } }
short pcm(float v) { return v>=1.0f ? 32767 : v<=-1.0f ? -32768 : (short)(v*32768.0f); }
void check(int v) { if(v<0) throw std::runtime_error("LAME encoding failed ("+std::to_string(v)+")"); }
struct Pair {
    lame_global_flags* lame = nullptr;
    MP3toEA converter;
    EALayer3Core startup;
    int channels, granules = 0, rawCount;
    std::array<short,4096> left{}, right{};
    std::array<std::array<short,47>,2> raw{};
    std::array<unsigned char,20000> mp3{};
    Bytes pending;
    std::deque<Bytes> ready;
    Pair(const float* src,int totalChannels,int pair,int frames,int rate,int quality)
        : startup(std::min(2,totalChannels-pair*2)), channels(std::min(2,totalChannels-pair*2)), rawCount(std::min(47,frames)) {
        lame=lame_init();
        if(!lame) throw std::bad_alloc();
        try {
            lame->num_channels=channels;
            float q=quality/100.0f;
            lame->VBR_q=(int)((100.0-q*100.0)*9.0/100.0);
            lame->VBR=vbr_mtrh;
            lame->in_samplerate=rate; lame->out_samplerate=rate;
            lame->mode=channels==2 ? JOINT_STEREO : MONO;
            lame->quality=2; lame->bWriteVbrTag=0; lame->lowpassfreq=0;
            check(lame_init_params(lame));
            if(lame->out_samplerate!=rate) throw std::runtime_error("LAME changed sample rate");
            for(int c=0;c<channels;++c) for(int i=0;i<rawCount;++i) raw[c][i]=pcm(src[i*totalChannels+pair*2+c]);
        } catch(...) { lame_close(lame); lame=nullptr; throw; }
    }
    ~Pair(){ if(lame) lame_close(lame); }
    void parse(const unsigned char* data,int bytes) {
        check(bytes);
        pending.insert(pending.end(),data,data+bytes);
        size_t at=0;
        while(pending.size()-at>=4) {
            const unsigned char* h=pending.data()+at;
            static const int rates[3][3]={{11025,12000,8000},{22050,24000,16000},{44100,48000,32000}};
            static const int br[2][15]={{0,32,40,48,56,64,80,96,112,128,160,192,224,256,320},{0,8,16,24,32,40,48,56,64,80,96,112,128,144,160}};
            int id=(h[1]>>3)&3, bi=h[2]>>4, si=(h[2]>>2)&3;
            if(h[0]!=255 || (h[1]&0xe6)!=0xe2 || id==1 || bi==0 || bi==15 || si==3) throw std::runtime_error("Invalid LAME frame");
            int version=id ? id-1:0, lsf=version<2;
            int length=(144000*br[lsf][bi]/rates[version][si] >> lsf)+((h[2]>>1)&1);
            if(pending.size()-at<(size_t)length) break;
            if(converter.parse(pending.data()+at)!=length) throw std::runtime_error("MP3toEA conversion failed");
            for(int g=0;g<converter.getgranulecount();++g) {
                unsigned char* granule=nullptr; int count=0;
                converter.getgranule(g,granule,count);
                Bytes packet; packet.reserve(count+8+rawCount*channels*2+1);
                bool special=granules==1;
                packet.push_back(special ? 0xee:0);
                packet.insert(packet.end(),granule,granule+count);
                if(granules<2) {
                    // Decode the first two granules just as encealayer31 does,
                    // then blend the 47 source samples with the delayed output.
                    std::array<float,576> l{},r{}; float* decoded[2]={l.data(),r.data()};
                    std::array<unsigned char,2056> safe{};
                    std::copy(granule,granule+count,safe.begin());
                    if(granules==0) startup.Open(safe.data(),count); else startup.Seek(safe.data());
                    check(startup.Decode(decoded));
                    if(special) {
                        be32(packet,(unsigned)rawCount); be32(packet,0);
                        for(int c=0;c<channels;++c) for(int i=0;i<rawCount;++i) {
                            float fade=rawCount<=1 ? 0.0f : i/(float)(rawCount-1);
                            short v=(short)(raw[c][i]*(1.0f-fade)+fade*decoded[c][529+i]);
                            packet.push_back((unsigned char)((unsigned short)v>>8));packet.push_back((unsigned char)v);
                        }
                    }
                }
                ready.push_back(std::move(packet)); ++granules;
            }
            at+=length;
        }
        if(at) pending.erase(pending.begin(),pending.begin()+at);
    }
    void feed(const float* src,int totalChannels,int pair,int count) {
        for(int i=0;i<count;++i) {
            left[i]=pcm(src[i*totalChannels+pair*2]);
            right[i]=channels==2 ? pcm(src[i*totalChannels+pair*2+1]) : left[i];
        }
        parse(mp3.data(),lame_encode_buffer(lame,left.data(),right.data(),count,mp3.data(),(int)mp3.size()));
    }
    void flush(const float* last,int totalChannels,int pair,int residual) {
        int padding=1152-(residual%1152);
        left.fill(pcm(last[pair*2]));right.fill(pcm(last[pair*2+(channels==2 ? 1:0)]));
        parse(mp3.data(),lame_encode_buffer(lame,left.data(),right.data(),padding,mp3.data(),(int)mp3.size()));
        parse(mp3.data(),lame_encode_flush(lame,mp3.data(),(int)mp3.size()));
        if(!pending.empty()) throw std::runtime_error("Incomplete LAME frame at flush");
    }
};
void segment(Bytes& output,const float* src,int frames,int channels,int rate,int quality,bool streamed) {
    std::vector<std::unique_ptr<Pair>> pairs;
    for(int p=0;p<(channels+1)/2;++p) pairs.emplace_back(new Pair(src,channels,p,frames,rate,quality));
    int fed=0, emitted=0, granules=0;
    Bytes block; int blockFrames=0;
    auto drain=[&]() {
        while(std::all_of(pairs.begin(),pairs.end(),[](const std::unique_ptr<Pair>& p){return !p->ready.empty();})) {
            for(auto& p:pairs) { auto& data=p->ready.front();block.insert(block.end(),data.begin(),data.end());p->ready.pop_front(); }
            ++granules;
            blockFrames+=granules==1 ? 0 : granules==2 ? 47:576;
        }
    };
    auto write=[&](bool final) {
        if(block.empty() || (!final && blockFrames==0)) return;
        int count=final ? frames-emitted : std::min(blockFrames,frames-emitted);
        size_t start=output.size();be32(output,0);be32(output,(unsigned)count);
        output.insert(output.end(),block.begin(),block.end());
        unsigned size=(unsigned)(output.size()-start);
        if(size>0x00ffffffu) throw std::runtime_error("EALayer3 RAM segment exceeds the SNR 24-bit block size; use streamed audio");
        put32(output,start,size|((final&&streamed)?0x80000000u:0));
        emitted+=count;block.clear();blockFrames=0;
    };
    size_t segmentStart=output.size(),lastBlock=segmentStart;
    int maximum=streamed ? std::max(1,(int)(rate*(2040.0/(48000.0f*channels)))):4096;
    maximum=std::min(maximum,4096);
    while(fed<frames) {
        int count=std::min(maximum,frames-fed);
        for(int p=0;p<(int)pairs.size();++p) pairs[p]->feed(src+fed*channels,channels,p,count);
        fed+=count;drain();
        if(fed<frames && streamed && blockFrames>0) {lastBlock=output.size();write(false);}
    }
    for(int p=0;p<(int)pairs.size();++p) pairs[p]->flush(src+(frames-1)*channels,channels,p,frames-emitted-blockFrames);
    drain();
    for(auto& p:pairs) if(!p->ready.empty()) throw std::runtime_error("Channel pair granules differ");
    if(granules<2) throw std::runtime_error("Insufficient EALayer3 startup granules");
    lastBlock=output.size();write(true);
    if(streamed) {
        size_t pad=64-((output.size()-segmentStart)%64);
        output.insert(output.end(),pad,0);
        put32(output,lastBlock,0x80000000u|(unsigned)(output.size()-lastBlock));
    }
    if(emitted!=frames) throw std::runtime_error("Encoded sample count differs");
}
struct Result { Bytes data; std::string error; };
}
#ifdef _MSC_VER
#define API extern "C" __declspec(dllexport)
#pragma comment(linker,"/export:NewSdkEaEncode=_NewSdkEaEncode")
#pragma comment(linker,"/export:NewSdkEaFree=_NewSdkEaFree")
#else
#define API extern "C"
#endif
API void* NewSdkEaEncode(const float* samples,int frames,int channels,int rate,int quality,int streamed,int loopStart,int loopEnd,const unsigned char** data,int* size,const char** error,int* loopByteOffset) {
    std::unique_ptr<Result> result(new Result());
    try {
        if(!samples || frames<=0 || frames>0x1fffffff || (channels!=1&&channels!=2&&channels!=4&&channels!=6&&channels!=8) || quality<0||quality>100)
            throw std::runtime_error("Invalid EALayer3 input");
        if(rate!=16000&&rate!=22050&&rate!=24000&&rate!=32000&&rate!=44100&&rate!=48000)
            throw std::runtime_error("EALayer3 V1 requires 16000, 22050, 24000, 32000, 44100 or 48000 Hz");
        bool loop=loopStart>=0;
        if(loop && (loopEnd<loopStart||loopEnd>=frames)) throw std::runtime_error("Invalid EALayer3 loop range");
        if(loopStart>0) segment(result->data,samples,loopStart,channels,rate,quality,streamed!=0);
        size_t loopOffset=result->data.size();
        segment(result->data,samples+(loop?loopStart*channels:0),loop ? loopEnd-loopStart+1:frames,channels,rate,quality,streamed!=0);
        // Return the byte offset for the existing managed SNR header builder.
        if(loopOffset>(size_t)std::numeric_limits<int>::max()) throw std::runtime_error("EALayer3 output exceeds asset limit");
        *loopByteOffset=(int)loopOffset;*size=(int)result->data.size();*data=result->data.data();*error=nullptr;
    } catch(const std::exception& e) {result->error=e.what();*size=0;*data=nullptr;*error=result->error.c_str();}
    return result.release();
}
API void NewSdkEaFree(void* result) {delete static_cast<Result*>(result);}
