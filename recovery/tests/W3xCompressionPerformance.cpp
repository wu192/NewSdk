#include "../src/Native/W3x/Timecoded.h"
#include "../src/Native/W3x/AdaptiveDelta.h"
#include <chrono>
#include <cmath>
#include <cstring>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
using namespace Recovered::W3x;
namespace {
std::ofstream bytes, timings;
unsigned fixtures=0;
std::uint32_t randomState=0x57ab31u;
float Random() {
    randomState=randomState*1664525u+1013904223u;
    return float(int(randomState>>16)-32768)/8192.f;
}
void Word(std::uint32_t value) {
    unsigned char p[4];
    for (unsigned i=0;i<4;++i) p[i]=static_cast<unsigned char>(value>>(8*i));
    bytes.write(reinterpret_cast<const char*>(p),4);
}
template<class T> void Buffer(const T& data) {
    Word(static_cast<std::uint32_t>(data.size()));
    if (!data.empty()) bytes.write(reinterpret_cast<const char*>(data.data()),data.size());
}
typedef std::chrono::steady_clock Clock;
void Record(const std::string& name,Clock::time_point start) {
    const double ms=std::chrono::duration<double,std::milli>(Clock::now()-start).count();
    timings<<name<<','<<ms<<'\n';
}
std::vector<TimecodedFrame> Frames(unsigned count,unsigned type,unsigned pattern) {
    std::vector<TimecodedFrame> result(count);
    for (unsigned i=0;i<count;++i) {
        TimecodedFrame& f=result[i];
        f.index=pattern==5 ? i%7 : pattern==6 ? 0xffffffffu-i*11u : i*3u;
        f.binaryMove=pattern==3 && i%5==2;
        if (type==3) {
            const float angle=pattern==0 ? 0.f : pattern==2 ? Random() : float(i)*.037f;
            const float sign=pattern==4 && i%2 ? -1.f : 1.f;
            f.values={0.f,float(std::sin(angle))*sign,0.f,float(std::cos(angle))*sign};
            if (pattern==5 && i%7==1) f.values[3]=2.f; // unconditional w>=1 in force phase
        } else {
            const unsigned width=pattern==4?4:1;
            for (unsigned j=0;j<width;++j)
                f.values.push_back(pattern==0?2.f:pattern==2?Random():float(i+j)*.125f);
        }
        if (pattern==7 && i%11==3) f.values[0]=std::numeric_limits<float>::quiet_NaN();
        if (pattern==7 && i%11==7) f.values.back()=std::numeric_limits<float>::infinity();
    }
    return result;
}
void Timecoded(const std::string& name,unsigned type,const std::vector<TimecodedFrame>& source,
               float error,float rate) {
    const Clock::time_point start=Clock::now();
    const TimecodedChannel c=CompressTimecoded(type,259,source,error,error,error,rate);
    Record(name,start);
    Word(EstimateTimecodedSize(c)); Word(static_cast<std::uint32_t>(c.frames.size()));
    // Compare full retained source indices and flags as well as ushort wire codes.
    for (const TimecodedFrame& f:c.frames) { Word(f.index); Word(f.binaryMove?1:0); }
    if (!c.frames.empty()) for (unsigned be=0;be<2;++be) {
        const TimecodedEncoded b=EncodeTimecoded(c,be!=0);
        Buffer(b.root); Buffer(b.values); Buffer(b.codes);
    }
    ++fixtures;
}
void Delta(const std::string& name,const std::vector<std::vector<float> >& source,float error) {
    AdaptiveDeltaChannel c;
    const Clock::time_point start=Clock::now();
    const bool ok=CompressAdaptiveDelta(3,259,source,error,c);
    Record(name,start); Word(ok?1:0);
    if (ok) {
        Word(EstimateAdaptiveDeltaSize(c));
        for (unsigned be=0;be<2;++be) {
            const AdaptiveDeltaEncoded b=EncodeAdaptiveDelta(c,be!=0);
            Buffer(b.root); Buffer(b.initial); Buffer(b.data);
        }
    }
    ++fixtures;
}
std::vector<std::vector<float> > Samples(unsigned count,unsigned width,unsigned pattern) {
    std::vector<std::vector<float> > source(count,std::vector<float>(width));
    for (unsigned i=0;i<count;++i) for (unsigned j=0;j<width;++j)
        source[i][j]=pattern==0?2.f:pattern==1?float(std::sin(double(i)*.037+j)):
            pattern==2?Random():pattern==3?float(i+j)*.125f:pattern==4?-2.f:
            pattern==5?((i+j)%2?-0.f:0.f):pattern==6?std::numeric_limits<float>::denorm_min():
            (j%2?-std::numeric_limits<float>::max():std::numeric_limits<float>::max());
    return source;
}
}
int main(int argc,char** argv) {
    try {
        if (argc!=4) throw std::runtime_error("Usage: benchmark matrix|long output.bin timings.csv");
        bytes.open(argv[2],std::ios::binary); timings.open(argv[3]);
        bytes.exceptions(std::ios::failbit|std::ios::badbit);
        timings.exceptions(std::ios::failbit|std::ios::badbit);
        timings<<"scenario,milliseconds\n";
        const float nan=std::numeric_limits<float>::quiet_NaN(), inf=std::numeric_limits<float>::infinity();
        if (std::string(argv[1])=="matrix") {
            const float errors[]={-1.f,-0.f,.0001f,.03f,1.f,inf,-inf,nan};
            const float rates[]={1.f,.67f,.05f,-1.f,nan};
            const unsigned counts[]={0,1,2,3,4,7,16,17,32,65,129};
            for (unsigned type:{0u,3u,4u}) for (unsigned count:counts) for (unsigned pattern=0;pattern<8;++pattern) {
                const std::vector<TimecodedFrame> source=Frames(count,type,pattern);
                for (float error:errors) for (float rate:rates) Timecoded("matrix-timecoded",type,source,error,rate);
            }
            for (unsigned count:{0u,1u,2u,15u,16u,17u,31u,32u,33u,65u,129u})
                for (unsigned width=1;width<=4;++width) for (unsigned pattern=0;pattern<8;++pattern) {
                    const auto source=Samples(count,width,pattern);
                    for (float error:{0.f,-0.f,.03f,1.f,-1.f,nan,inf,-inf}) Delta("matrix-delta",source,error);
                }
        } else if (std::string(argv[1])=="long") {
            Timecoded("timecoded-scalar-threshold-16384",0,Frames(16384,0,0),.001f,1.f);
            Timecoded("timecoded-scalar-forced-8192",0,Frames(8192,0,1),-1.f,.05f);
            Timecoded("timecoded-quaternion-threshold-8192",3,Frames(8192,3,0),.003f,1.f);
            Timecoded("timecoded-quaternion-forced-2048",3,Frames(2048,3,1),-1.f,.25f);
            Delta("delta-constant-16384x4",Samples(16384,4,0),.001f);
            Delta("delta-smooth-8192x4",Samples(8192,4,1),.03f);
            Delta("delta-random-4096x4",Samples(4096,4,2),.03f);
        } else throw std::runtime_error("Unknown benchmark mode");
        bytes.flush(); timings.flush();
        std::cout<<"PASS complete output capture, fixtures="<<fixtures<<'\n';
    } catch (const std::exception& e) { std::cerr<<"FAIL "<<e.what()<<'\n'; return 1; }
}
