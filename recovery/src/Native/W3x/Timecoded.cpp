#include "Timecoded.h"
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <algorithm>
#include <memory>

namespace Recovered { namespace W3x {
namespace {
void Validate(std::uint32_t type, const std::vector<TimecodedFrame>& frames) {
    if (frames.size() > 0xffffffffu)
        throw std::invalid_argument("Timecoded frame count overflow");
    if (frames.empty()) return;
    const std::size_t width = frames.front().values.size();
    if (width == 0 || width > 4 || (type == 3 && width != 4))
        throw std::invalid_argument("Timecoded source dimension");
    for (const TimecodedFrame& frame : frames) {
        if (frame.values.size() != width)
            throw std::invalid_argument("Timecoded inconsistent source dimension");
    }
}
void Validate(const TimecodedChannel& channel) { Validate(channel.type, channel.frames); }
std::array<float,4> Slerp(const std::vector<float>& p,
                        const std::vector<float>& q, float alpha) {
    float dot = float(double(p[0])*q[0]+double(p[1])*q[1]+
                      double(p[2])*q[2]+double(p[3])*q[3]);
    const bool flip = dot < 0.f;
    if (flip) dot = 0.f-dot;
    float weight;
    if (1.0-double(dot) < 9.99999905104687E-09) {
        weight = float(1.0-double(alpha));
    } else {
        const float theta = float(std::acos(double(dot)));
        const float sine = float(std::sin(double(theta)));
        const float inverse = float(1.0/double(sine));
        const double angle = double(alpha)*double(theta);
        weight = float(double(float(std::sin(double(float(double(theta)-angle)))))*double(inverse));
        alpha = float(double(float(std::sin(double(float(angle)))))*double(inverse));
    }
    if (flip) alpha = 0.f-alpha;
    std::array<float,4> result;
    for (unsigned j=0;j<4;++j)
        result[j] = float(double(p[j])*double(weight)+double(q[j])*double(alpha));
    return result;
}
float RotationW(const std::vector<float>& p, const std::array<float,4>& q) {
    // W of conjugate(p)*q, retaining the original double expression grouping.
    return float(double(p[3])*double(q[3])-
        (double(0.f-p[0])*double(q[0])+double(q[1])*double(0.f-p[1])+
         double(0.f-p[2])*double(q[2])));
}
float RemovalError(const TimecodedFrame& first, const TimecodedFrame& middle,
                   const TimecodedFrame& last, bool quaternion) {
    const float alpha=float(double(float(std::uint32_t(middle.index-first.index)))/
                            double(float(std::uint32_t(last.index-first.index))));
    if (!quaternion) {
        float maximum=0.f;
        for (std::size_t j=0;j<first.values.size();++j) {
            // Original FindUselessPacket compares the interpolated endpoints
            // with the LAST endpoint (not the middle sample).
            const double end=last.values[j], begin=first.values[j];
            const float difference=float(std::fabs(double(float((end-begin)*double(alpha)+begin-end))));
            if (double(difference)>double(maximum)) maximum=difference;
        }
        return maximum;
    }
    const std::array<float,4> interpolated=Slerp(first.values,last.values,alpha);
    const float w=float(std::fabs(double(RotationW(middle.values,interpolated))));
    // The original returns the FIRST w>=1 triple even in forced reduction,
    // ahead of any previously observed minimum. All other errors are >=0.
    return w>=1.f ? -1.f : float(std::acos(double(w)))*2.f;
}
bool RemoveLast(const TimecodedFrame& first, const TimecodedFrame& last,
                float error, bool quaternion) {
    if (last.binaryMove) return false;
    if (error>=0.f) {
        if (quaternion) {
            std::array<float,4> value;
            // Keep the pair's double multiply, distinct from triple error.
            for (unsigned j=0;j<4;++j) value[j]=last.values[j];
            const float w=float(std::fabs(double(RotationW(first.values,value))));
            if (w<1.f && double(float(std::acos(double(w))))*2.0>double(error)) return false;
        } else {
            error = float(double(float(std::uint32_t(last.index-first.index)))*double(error));
            for (float value : last.values) {
                const float difference=float(double(value)-double(value));
                if (double(float(std::fabs(double(difference)))) > double(error)) return false;
            }
        }
    }
    return true;
}
// Index links keep source frames immutable. Only the two adjacent triples
// change after a deletion; retained frame values are copied once, at the end.
struct FrameLinks { std::size_t previous, next; };
class ReductionHeap {
    const std::vector<TimecodedFrame>& frames;
    const std::vector<FrameLinks>& links;
    const bool quaternion;
    std::vector<std::size_t> heap, position;
    std::vector<float> errors;
    bool Less(std::size_t a, std::size_t b) const {
        return errors[a]<errors[b] || (errors[a]==errors[b] && a<b);
    }
    void Swap(std::size_t a, std::size_t b) {
        std::swap(heap[a],heap[b]); position[heap[a]]=a; position[heap[b]]=b;
    }
    std::size_t Up(std::size_t at) {
        while (at && Less(heap[at],heap[(at-1)/2])) {
            Swap(at,(at-1)/2); at=(at-1)/2;
        }
        return at;
    }
    void Down(std::size_t at) {
        while (at*2+1<heap.size()) {
            std::size_t child=at*2+1;
            if (child+1<heap.size() && Less(heap[child+1],heap[child])) ++child;
            if (!Less(heap[child],heap[at])) break;
            Swap(at,child); at=child;
        }
    }
    float Error(std::size_t i) const {
        const std::size_t p=links[i].previous, n=links[i].next;
        if (p==frames.size() || n==frames.size() || frames[p].binaryMove || frames[n].binaryMove)
            return std::numeric_limits<float>::max();
        return RemovalError(frames[p],frames[i],frames[n],quaternion);
    }
public:
    ReductionHeap(const std::vector<TimecodedFrame>& f, const std::vector<FrameLinks>& l,
                  bool q) : frames(f), links(l), quaternion(q),
                  position(f.size(),f.size()), errors(f.size()) {
        heap.reserve(f.size());
        for (std::size_t i=links[0].next; i<f.size(); i=links[i].next) {
            errors[i]=Error(i);
            if (errors[i]<std::numeric_limits<float>::max()) {
                position[i]=heap.size(); heap.push_back(i);
            }
        }
        for (std::size_t i=heap.size()/2; i>0; --i) Down(i-1);
    }
    std::size_t Best() const { return heap.empty()?frames.size():heap[0]; }
    void Erase(std::size_t i) {
        const std::size_t at=position[i];
        if (at==frames.size()) return;
        Swap(at,heap.size()-1); heap.pop_back(); position[i]=frames.size();
        if (at<heap.size()) Down(Up(at));
    }
    void Update(std::size_t i) {
        const float value=Error(i);
        if (!(value<std::numeric_limits<float>::max())) { Erase(i); return; }
        errors[i]=value;
        if (position[i]==frames.size()) {
            position[i]=heap.size(); heap.push_back(i);
        }
        Down(Up(position[i]));
    }
};
void Put16(unsigned char* p, std::uint16_t value, bool be) {
    p[be?1:0]=static_cast<unsigned char>(value);
    p[be?0:1]=static_cast<unsigned char>(value>>8);
}
void Put32(unsigned char* p, std::uint32_t value, bool be) {
    for (unsigned i=0;i<4;++i) p[be?3-i:i]=static_cast<unsigned char>(value>>(i*8));
}
void PutFloat(unsigned char* p, std::uint32_t bits, bool be) {
    // The original ldind.r4/call float helper quiets a source sNaN before
    // either the native-endian return or the byte-swap branch.
    if ((bits&0x7f800000u)==0x7f800000u && (bits&0x007fffffu)!=0)
        bits|=0x00400000u;
    Put32(p,bits,be);
    if (be) {
        // WriteOut calls the original float-returning EndianToPlatform helper.
        // Its swapped word passes through x87 as a float, quieting an sNaN
        // created by the byte swap even when the source float was finite.
        const std::uint32_t swapped=(bits>>24)|((bits>>8)&0x0000ff00u)|
            ((bits<<8)&0x00ff0000u)|(bits<<24);
        if ((swapped&0x7f800000u)==0x7f800000u &&
            (swapped&0x007fffffu)!=0 && (swapped&0x00400000u)==0)
            p[2]|=0x40u;
    }
}
}
std::uint32_t TimecodedReductionLimit(std::uint32_t sourceCount,float forceRate) {
    // The original IL first rounds the count to r4, multiplies as r8, then
    // uses CLR4 x86 conv.u4: signed64 truncation followed by low32 bits.
    // Nonfinite/out-of-signed64 values produce integer-indefinite, low32=0.
    // Reuse the established converter oracle model without a C++ UB cast.
    volatile double roundedProduct=double(float(sourceCount))*double(forceRate);
    const double target=roundedProduct;
    std::uint32_t converted=0;
    if (std::isfinite(target) && target>=-9223372036854775808.0 &&
        target<9223372036854775808.0)
        converted=static_cast<std::uint32_t>(static_cast<std::int64_t>(target));
    return converted<1u?1u:converted;
}
TimecodedChannel CompressTimecoded(std::uint32_t type, std::uint16_t pivot,
    const std::vector<TimecodedFrame>& frames, float translationError,
    float rotationError, float visibilityError, float forceRate) {
    Validate(type,frames);
    TimecodedChannel output={type,pivot,{}};
    if (frames.empty()) return output;
    const std::uint32_t minimum=TimecodedReductionLimit(static_cast<std::uint32_t>(frames.size()),forceRate);
    float error= type==3 ? rotationError : type==4 ? visibilityError : translationError;
    if (frames.size()==1 || !(frames.size()>minimum || error>=0.f)) {
        output.frames=frames;
        return output;
    }
    std::vector<FrameLinks> links(frames.size());
    for (std::size_t i=0;i<frames.size();++i) {
        links[i].previous=i?i-1:frames.size(); links[i].next=i+1;
    }
    std::size_t remaining=frames.size(), cursor=links[0].next;
    // Allocate the heap only if forced reduction is actually needed.
    std::unique_ptr<ReductionHeap> heap;
    while (remaining>minimum || error>=0.f) {
        std::size_t index=frames.size();
        if (remaining==2) {
            const std::size_t last=links[0].next;
            if (RemoveLast(frames[0],frames[last],error,type==3)) index=last;
        } else if (remaining>2) {
            if (error<0.f) {
                if (remaining==3 && !heap) {
                    const std::size_t middle=links[0].next, last=links[middle].next;
                    if (!frames[0].binaryMove && !frames[last].binaryMove &&
                        RemovalError(frames[0],frames[middle],frames[last],type==3)<std::numeric_limits<float>::max())
                        index=middle;
                } else {
                    if (!heap) heap.reset(new ReductionHeap(frames,links,type==3));
                    index=heap->Best();
                }
            } else {
                // Earlier rejected triples are unchanged. Resume at the
                // predecessor after each deletion instead of scanning again.
                for (;cursor<frames.size() && links[cursor].next<frames.size();cursor=links[cursor].next) {
                    const TimecodedFrame& first=frames[links[cursor].previous];
                    const TimecodedFrame& last=frames[links[cursor].next];
                    if (first.binaryMove || last.binaryMove) continue;
                    const float value=RemovalError(first,frames[cursor],last,type==3);
                    if (value<0.f || (type==3 ? value<=error : value<error)) { index=cursor; break; }
                }
            }
        }
        if (index==frames.size()) {
            if (error<0.f) break;
            error=-1.f;
        } else {
            const std::size_t p=links[index].previous, n=links[index].next;
            links[p].next=n;
            if (n<frames.size()) links[n].previous=p;
            --remaining;
            if (heap) {
                heap->Erase(index); heap->Update(p);
                if (n<frames.size()) heap->Update(n);
            } else cursor=p? p : n;
        }
    }
    output.frames.reserve(remaining);
    for (std::size_t i=0;i<frames.size();i=links[i].next) output.frames.push_back(frames[i]);
    return output;
}
std::uint32_t EstimateTimecodedSize(const TimecodedChannel& channel) {
    Validate(channel);
    if (channel.frames.empty()) return 0;
    const std::uint64_t size=(channel.frames.front().values.size()*4+2)*std::uint64_t(channel.frames.size());
    return static_cast<std::uint32_t>(size);
}
TimecodedEncoded EncodeTimecoded(const TimecodedChannel& channel, bool bigEndian) {
    Validate(channel);
    if (channel.frames.empty())
        throw std::invalid_argument("Original Timecoded WriteOut dereferences empty source frames");
    TimecodedEncoded encoded={};
    const std::size_t count=channel.frames.size(), width=channel.frames.front().values.size();
    if (count>0xffffffffu/width || count*width>0xffffffffu/4)
        throw std::invalid_argument("Timecoded serialized values overflow");
    Put32(encoded.root.data(),1624821609u,bigEndian);
    Put32(encoded.root.data()+4,channel.type,bigEndian);
    Put32(encoded.root.data()+8,static_cast<std::uint32_t>(count),bigEndian);
    Put32(encoded.root.data()+12,static_cast<std::uint32_t>(width),bigEndian);
    Put16(encoded.root.data()+16,channel.pivot,bigEndian);
    Put32(encoded.root.data()+20,static_cast<std::uint32_t>(count),bigEndian);
    Put32(encoded.root.data()+28,static_cast<std::uint32_t>(count*width),bigEndian);
    encoded.values.resize(count*width*4);
    encoded.codes.resize(count*2);
    for (std::size_t i=0;i<count;++i) {
        std::uint16_t code=static_cast<std::uint16_t>(channel.frames[i].index);
        if (channel.frames[i].binaryMove) code|=0x8000u;
        Put16(encoded.codes.data()+i*2,code,bigEndian);
        for (std::size_t j=0;j<width;++j) {
            std::uint32_t bits;
            std::memcpy(&bits,&channel.frames[i].values[j],4);
            PutFloat(encoded.values.data()+(i*width+j)*4,bits,bigEndian);
        }
    }
    return encoded;
}
}}
