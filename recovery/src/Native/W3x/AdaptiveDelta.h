#pragma once
#include <array>
#include <cstdint>
#include <vector>

namespace Recovered { namespace W3x {
struct TimecodedFrame;
// Recovered value model, not the VC8 EASTL object ABI. Source: W3X original
// method tokens 0x06000011-0x06000015, extraction 40162934653-1.
struct AdaptiveDeltaFrame {
    std::uint8_t filter;
    std::array<std::int8_t,16> delta;
};
struct AdaptiveDeltaChannel {
    std::uint32_t type;
    std::uint16_t pivot;
    float scale;
    std::uint32_t bits;
    std::vector<float> initial;
    // Block-major then component-major, with 16 deltas in each frame.
    std::vector<AdaptiveDeltaFrame> frames;
};
struct AdaptiveDeltaEncoded {
    // Pointer slots 28 and 40 are zero. The caller allocates initial then data
    // and bookmarks those slots with the shared relocation tracker.
    std::array<unsigned char,44> root;
    std::vector<unsigned char> initial;
    std::vector<unsigned char> data;
};
// Returns false for the original null result (<2 source samples or failure at
// both 4 and 8 bits); output remains unchanged. Dimensions outside 1..4,
// inconsistent sample widths, nonfinite samples, and oversized input throw.
// maxError uses original IEEE comparisons, including NaN and +/-infinity.
bool CompressAdaptiveDelta(std::uint32_t type, std::uint16_t pivot,
    const std::vector<std::vector<float> >& samples, float maxError,
    AdaptiveDeltaChannel& output);
// Production animation input can be read directly without copying each vector.
bool CompressAdaptiveDeltaFrames(std::uint32_t type, std::uint16_t pivot,
    const std::vector<TimecodedFrame>& samples, float maxError,
    AdaptiveDeltaChannel& output);
// The original estimate deliberately differs from WriteOut payload length.
std::uint32_t EstimateAdaptiveDeltaSize(const AdaptiveDeltaChannel& channel);
AdaptiveDeltaEncoded EncodeAdaptiveDelta(const AdaptiveDeltaChannel& channel, bool bigEndian);
// Exposed for original-method differential fixtures; filter range 0..255,
// bits 4 or 8; the 17 values contain the previous reconstructed sample first.
float CompressAdaptiveDeltaFrame(AdaptiveDeltaFrame& dst, unsigned filter,
    const std::array<float,17>& values, float scale, unsigned bits,
    float* currentPrevious, bool* excessError, float maxError);
float AdaptiveDeltaFilter(unsigned filter);
}}
