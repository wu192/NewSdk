#include "../src/Native/W3x/Timecoded.h"
#include "../src/Native/W3x/AdaptiveDelta.h"
#include <chrono>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>
using namespace Recovered::W3x;
namespace {
std::uint32_t Read(std::istream& in) {
    unsigned char p[4]; in.read(reinterpret_cast<char*>(p),4);
    return std::uint32_t(p[0])|(std::uint32_t(p[1])<<8)|(std::uint32_t(p[2])<<16)|(std::uint32_t(p[3])<<24);
}
float Float(std::istream& in) {
    const std::uint32_t bits=Read(in); float value; std::memcpy(&value,&bits,4); return value;
}
void Word(std::ostream& out,std::uint32_t value) {
    unsigned char p[4]; for (unsigned i=0;i<4;++i) p[i]=static_cast<unsigned char>(value>>(8*i));
    out.write(reinterpret_cast<const char*>(p),4);
}
template<class T> void Buffer(std::ostream& out,const T& bytes) {
    Word(out,static_cast<std::uint32_t>(bytes.size()));
    if (!bytes.empty()) out.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());
}
}
int main(int argc,char** argv) {
    try {
        if (argc!=4) throw std::runtime_error("Usage: historical input.bin output.bin timing.csv");
        std::ifstream input(argv[1],std::ios::binary);
        std::ofstream output(argv[2],std::ios::binary), timing(argv[3]);
        input.exceptions(std::ios::badbit|std::ios::failbit);
        output.exceptions(std::ios::badbit|std::ios::failbit);
        const unsigned channels=Read(input);
        unsigned deltas=0,timecodes=0; double milliseconds=0;
        for (unsigned c=0;c<channels;++c) {
            const unsigned type=Read(input), pivot=Read(input), count=Read(input), width=Read(input), allow=Read(input);
            const float translation=Float(input),rotation=Float(input),visibility=Float(input),deltaError=Float(input),rate=Float(input);
            std::vector<TimecodedFrame> frames(count);
            for (unsigned i=0;i<count;++i) {
                frames[i].index=i; frames[i].binaryMove=Read(input)!=0;
                frames[i].values.resize(width);
                for (unsigned j=0;j<width;++j) frames[i].values[j]=Float(input);
            }
            const auto start=std::chrono::steady_clock::now();
            AdaptiveDeltaChannel delta; TimecodedChannel timecoded;
            bool hasDelta=false;
            if (allow&2u) {
#ifdef W3X_BASELINE
                std::vector<std::vector<float> > samples; samples.reserve(count);
                for (const TimecodedFrame& f:frames) samples.push_back(f.values);
                hasDelta=CompressAdaptiveDelta(type,static_cast<std::uint16_t>(pivot),samples,deltaError,delta);
#else
                hasDelta=CompressAdaptiveDeltaFrames(type,static_cast<std::uint16_t>(pivot),frames,deltaError,delta);
#endif
            }
            const bool hasTimecoded=!hasDelta || (allow&1u)!=0;
            if (hasTimecoded) {
#ifdef W3X_BASELINE
                const std::vector<TimecodedFrame> converted=frames;
                timecoded=CompressTimecoded(type,static_cast<std::uint16_t>(pivot),converted,translation,rotation,visibility,rate);
#else
                timecoded=CompressTimecoded(type,static_cast<std::uint16_t>(pivot),frames,translation,rotation,visibility,rate);
#endif
            }
            const bool useDelta=hasDelta && (!hasTimecoded || EstimateTimecodedSize(timecoded)>=EstimateAdaptiveDeltaSize(delta));
            milliseconds+=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
            Word(output,useDelta?1:0);
            if (useDelta) {
                ++deltas;
                for (unsigned be=0;be<2;++be) {
                    const AdaptiveDeltaEncoded b=EncodeAdaptiveDelta(delta,be!=0);
                    Buffer(output,b.root); Buffer(output,b.initial); Buffer(output,b.data);
                }
            } else {
                ++timecodes;
                for (unsigned be=0;be<2;++be) {
                    const TimecodedEncoded b=EncodeTimecoded(timecoded,be!=0);
                    Buffer(output,b.root); Buffer(output,b.values); Buffer(output,b.codes);
                }
            }
        }
        output.flush();
        timing<<"milliseconds,channels,timecoded,delta\n"<<milliseconds<<','<<channels<<','<<timecodes<<','<<deltas<<'\n';
        std::cout<<"PASS historical channels="<<channels<<" timecoded="<<timecodes<<" delta="<<deltas<<'\n';
    } catch (const std::exception& e) { std::cerr<<"FAIL "<<e.what()<<'\n'; return 1; }
}
