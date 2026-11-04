#include "AdaptiveDelta.h"
#include "Timecoded.h"
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace Recovered { namespace W3x {
namespace {
// IL conv.r4 stores are observable, as are separate double multiply/add steps.
float R4(double x) { volatile float f = static_cast<float>(x); return f; }
double Product(double a, double b) { volatile double p = a*b; return p; }
float FromBits(std::uint32_t bits) { float f; std::memcpy(&f,&bits,4); return f; }
std::uint32_t Bits(float f) { std::uint32_t bits; std::memcpy(&bits,&f,4); return bits; }
std::int32_t ConvI4(float value) {
    // CLR x86 conv.i4 uses the integer-indefinite result for unordered or
    // overflowing input. Avoid C++'s undefined floating/integer conversion.
    if (!std::isfinite(value) || static_cast<double>(value) < -2147483648.0 ||
        static_cast<double>(value) >= 2147483648.0)
        return std::numeric_limits<std::int32_t>::min();
    return static_cast<std::int32_t>(value);
}
std::int32_t NegateI4(std::int32_t x) {
    return x == std::numeric_limits<std::int32_t>::min() ? x : -x;
}
std::int8_t LowByte(std::int32_t x) {
    const unsigned low = static_cast<std::uint32_t>(x)&255u;
    return static_cast<std::int8_t>(low < 128u ? static_cast<int>(low) : static_cast<int>(low)-256);
}
struct Filters {
    std::array<float,256> values;
    Filters() {
        // Original PE data, first sixteen floats, not decimal approximations.
        const std::uint32_t bits[16] = {0x322bcc77u,0x33d6bf95u,0x358637bdu,0x3727c5acu,
            0x38d1b717u,0x3a83126fu,0x3c23d70au,0x3dcccccdu,0x3f800000u,0x41200000u,
            0x42c80000u,0x447a0000u,0x461c4000u,0x47c35000u,0x49742400u,0x4b189680u};
        for (unsigned i=0;i<16;++i) values[i]=FromBits(bits[i]);
        for (unsigned i=0;i<240;++i) {
            const float fraction=R4(static_cast<double>(R4(i))/240.0);
            const double radians=Product(Product(static_cast<double>(fraction),90.0),3.1415927410125732)/180.0;
            values[16+i]=R4(1.0-static_cast<double>(R4(std::sin(radians))));
        }
    }
};
const Filters& Table() { static const Filters filters; return filters; }
void ValidateChannel(const AdaptiveDeltaChannel& c) {
    if (c.initial.empty() || c.initial.size()>4 || (c.bits!=4 && c.bits!=8) ||
        c.frames.size()%c.initial.size()!=0 || c.frames.empty())
        throw std::invalid_argument("Invalid adaptive delta channel");
    if (c.frames.size()>std::numeric_limits<std::uint32_t>::max()/17u)
        throw std::length_error("Adaptive delta payload exceeds 32 bits");
}
void Put(unsigned char* out,std::uint32_t value,unsigned bytes,bool big) {
    for (unsigned i=0;i<bytes;++i) out[i]=static_cast<unsigned char>(value>>((big?bytes-i-1:i)*8));
}
void PutFloat(unsigned char* out,float value,bool big) {
    std::uint32_t bits=Bits(value);
    if (big) {
        const std::uint32_t swapped=((bits&0x000000ffu)<<24)|((bits&0x0000ff00u)<<8)|
            ((bits&0x00ff0000u)>>8)|((bits&0xff000000u)>>24);
        // WriteOut reloads its swapped float local (IL_015b / IL_0266)
        // before stind.r4. CLR x86 x87 loads quiet even an sNaN manufactured
        // by swapping a finite source. Swapped bit22 is source bit14.
        if ((swapped&0x7f800000u)==0x7f800000u && (swapped&0x007fffffu)!=0)
            bits|=0x00004000u;
    }
    Put(out,bits,4,big);
}
// Search only needs the error. Once its nonnegative partial sum cannot beat
// the current winner, subsequent samples cannot change the selection. The
// public frame encoder always executes all sixteen samples and writes state.
template<bool Encode>
float Frame(AdaptiveDeltaFrame* dst,const std::array<float,17>& values,
    float step,float inverse,int bound,float cutoff,
    float* currentPrevious,bool* excessError,float maxError) {
    float previous=values[0];
    float error=0.0f;
    for (unsigned i=1;i<=16;++i) {
        const float difference=R4(std::fabs(static_cast<double>(R4(static_cast<double>(values[i])-previous))));
        const float rounded=R4(std::floor(static_cast<double>(R4(Product(difference,inverse)+0.5))));
        std::int32_t quantized=ConvI4(rounded);
        if (static_cast<double>(values[i])<static_cast<double>(previous)) {
            quantized=NegateI4(quantized);
            if (quantized < -bound) quantized=-bound;
        } else if (quantized>=bound) quantized=bound-1;
        const double reconstructed=Product(static_cast<double>(R4(quantized)),step)+static_cast<double>(previous);
        const float deviation=R4(std::fabs(static_cast<double>(R4(reconstructed-static_cast<double>(values[i])))));
        if (Encode) dst->delta[i-1]=LowByte(quantized);
        previous=R4(reconstructed);
        error=R4(static_cast<double>(deviation)+static_cast<double>(error));
        if (!Encode && !(error<cutoff)) return error;
        if (excessError && static_cast<double>(deviation)>static_cast<double>(maxError)) *excessError=true;
    }
    if (currentPrevious) *currentPrevious=previous;
    return error;
}
float Step(unsigned filter,float scale,unsigned bits) {
    float step=R4(static_cast<double>(Table().values[filter])*static_cast<double>(scale));
    if (bits==8) step=R4(static_cast<double>(step)*0.0625);
    return step;
}
}
float AdaptiveDeltaFilter(unsigned filter) {
    if (filter>255) throw std::invalid_argument("Adaptive delta filter out of range");
    return Table().values[filter];
}
float CompressAdaptiveDeltaFrame(AdaptiveDeltaFrame& dst,unsigned filter,
    const std::array<float,17>& values,float scale,unsigned bits,
    float* currentPrevious,bool* excessError,float maxError) {
    if (filter>255 || (bits!=4 && bits!=8))
        throw std::invalid_argument("Invalid adaptive delta frame parameters");
    dst.filter=static_cast<std::uint8_t>(filter);
    const float step=Step(filter,scale,bits);
    return Frame<true>(&dst,values,step,R4(1.0/static_cast<double>(step)),bits==4?8:128,
        0.0f,currentPrevious,excessError,maxError);
}
namespace {
const std::vector<float>& Values(const std::vector<float>& sample) { return sample; }
const std::vector<float>& Values(const TimecodedFrame& sample) { return sample.values; }
template<class Sample>
bool Compress(std::uint32_t type,std::uint16_t pivot,
    const std::vector<Sample>& samples,float maxError,AdaptiveDeltaChannel& output) {
    if (samples.size()<2) return false;
    const std::size_t width=Values(samples[0]).size();
    if (width==0 || width>4) throw std::invalid_argument("Adaptive delta supports one to four components");
    if (samples.size()>static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max()/24)-15u)
        throw std::length_error("Adaptive delta source exceeds original 32-bit vector range");
    float greatest=0.0f;
    for (std::size_t i=0;i<samples.size();++i) {
        const std::vector<float>& values=Values(samples[i]);
        if (values.size()!=width) throw std::invalid_argument("Inconsistent adaptive delta sample widths");
        for (std::size_t j=0;j<width;++j) {
            if (!std::isfinite(values[j])) throw std::invalid_argument("Nonfinite adaptive delta sample is unverified");
            if (i) {
                const float diff=R4(std::fabs(static_cast<double>(R4(static_cast<double>(values[j])-Values(samples[i-1])[j]))));
                if (!std::isfinite(diff)) throw std::invalid_argument("Overflowing adaptive delta source difference is unverified");
                if (diff>greatest) greatest=diff;
            }
        }
    }
    AdaptiveDeltaChannel candidate;
    candidate.type=type; candidate.pivot=pivot; candidate.scale=R4(static_cast<double>(greatest)/7.0);
    candidate.initial=Values(samples[0]);
    const std::size_t blocks=(samples.size()+15u)>>4;
    if (greatest==0.0f) {
        // Every candidate reconstructs a constant finite channel exactly at
        // scale zero. Strict ties select filter 0, whose low delta bytes are
        // all zero even for CLR's integer-indefinite quantization result.
        if (maxError<0.0f) return false;
        candidate.bits=4;
        candidate.frames.assign(blocks*width,AdaptiveDeltaFrame{});
        output=std::move(candidate);
        return true;
    }
    candidate.frames.reserve(blocks*width);
    for (unsigned bits=4;bits<12;bits+=4) {
        candidate.bits=bits; candidate.frames.clear();
        std::array<float,256> steps,inverses;
        unsigned cached=0;
        std::vector<float> previous=candidate.initial;
        bool excess=false;
        for (std::size_t block=0;block<blocks;++block) {
            for (std::size_t component=0;component<width;++component) {
                std::array<float,17> values;
                values[0]=previous[component];
                for (unsigned i=0;i<16;++i) {
                    const std::size_t source=1u+block*16u+i;
                    values[i+1]=source<samples.size()?Values(samples[source])[component]:values[i];
                }
                float smallest=std::numeric_limits<float>::max();
                unsigned selected=0;
                bool found=false;
                for (unsigned filter=0;filter<256;++filter) {
                    if (filter==cached) {
                        steps[filter]=Step(filter,candidate.scale,bits);
                        inverses[filter]=R4(1.0/static_cast<double>(steps[filter]));
                        ++cached;
                    }
                    const float error=Frame<false>(0,values,steps[filter],inverses[filter],
                        bits==4?8:128,smallest,0,0,0.0f);
                    if (static_cast<double>(error)<static_cast<double>(smallest)) {
                        smallest=error; selected=filter; found=true;
                        if (smallest==0.0f) break;
                    }
                }
                // Original passes -1 to CompressFrame if every candidate has
                // unordered/overflow error. Do not perform that invalid read.
                if (!found) throw std::runtime_error("Adaptive delta has no finite filter candidate");
                AdaptiveDeltaFrame encoded;
                encoded.filter=static_cast<std::uint8_t>(selected);
                Frame<true>(&encoded,values,steps[selected],inverses[selected],bits==4?8:128,
                    0.0f,&previous[component],&excess,maxError);
                candidate.frames.push_back(encoded);
            }
        }
        if (!excess) { output=std::move(candidate); return true; }
    }
    return false;
}
}
bool CompressAdaptiveDelta(std::uint32_t type,std::uint16_t pivot,
    const std::vector<std::vector<float> >& samples,float maxError,AdaptiveDeltaChannel& output) {
    return Compress(type,pivot,samples,maxError,output);
}
bool CompressAdaptiveDeltaFrames(std::uint32_t type,std::uint16_t pivot,
    const std::vector<TimecodedFrame>& samples,float maxError,AdaptiveDeltaChannel& output) {
    return Compress(type,pivot,samples,maxError,output);
}
std::uint32_t EstimateAdaptiveDeltaSize(const AdaptiveDeltaChannel& channel) {
    ValidateChannel(channel);
    const std::uint32_t width=static_cast<std::uint32_t>(channel.initial.size());
    return width*4u+((width*channel.bits+7u)>>3)*static_cast<std::uint32_t>(channel.frames.size());
}
AdaptiveDeltaEncoded EncodeAdaptiveDelta(const AdaptiveDeltaChannel& c,bool bigEndian) {
    ValidateChannel(c);
    AdaptiveDeltaEncoded out;
    out.root.fill(0);
    const std::uint32_t width=static_cast<std::uint32_t>(c.initial.size());
    const std::uint32_t frameCount=static_cast<std::uint32_t>(c.frames.size());
    Put(out.root.data(),2012836613u,4,bigEndian);
    Put(out.root.data()+4,c.type,4,bigEndian);
    Put(out.root.data()+8,(frameCount/width)*16u,4,bigEndian);
    Put(out.root.data()+12,width,4,bigEndian);
    Put(out.root.data()+16,c.pivot,2,bigEndian);
    PutFloat(out.root.data()+20,c.scale,bigEndian);
    Put(out.root.data()+24,width,4,bigEndian);
    Put(out.root.data()+32,c.bits,4,bigEndian);
    out.initial.resize(width*4u);
    for (std::uint32_t i=0;i<width;++i) PutFloat(out.initial.data()+i*4u,c.initial[i],bigEndian);
    out.data.reserve(frameCount*(c.bits==4?9u:17u));
    for (std::size_t i=0;i<c.frames.size();++i) {
        out.data.push_back(c.frames[i].filter);
        if (c.bits==4) {
            for (unsigned j=0;j<16;j+=2) {
                const unsigned lo=static_cast<unsigned>(static_cast<std::uint8_t>(c.frames[i].delta[j]))&15u;
                const unsigned hi=static_cast<unsigned>(static_cast<std::uint8_t>(c.frames[i].delta[j+1]))<<4;
                out.data.push_back(static_cast<unsigned char>(lo|hi));
            }
        } else for (unsigned j=0;j<16;++j) out.data.push_back(static_cast<unsigned char>(c.frames[i].delta[j]));
    }
    Put(out.root.data()+36,static_cast<std::uint32_t>(out.data.size()),4,bigEndian);
    return out;
}
}}
