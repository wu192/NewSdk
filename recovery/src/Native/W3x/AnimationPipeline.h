#pragma once
#include <cstdint>
#include <vector>
#include "../Relocation.h"
#include "Timecoded.h"

namespace Recovered { namespace W3x {
// Typed input after XML marshaling; this is not the native VC8 structure ABI.
struct AnimationInputFrame {
    std::vector<float> values;
    bool binaryMove = false;
};
struct AnimationInputChannel {
    std::uint32_t typeId = 1355976437u;
    std::uint32_t type = 0;
    std::uint32_t firstFrame = 0;
    std::uint16_t pivot = 0;
    std::vector<AnimationInputFrame> frames;
};
struct AnimationCompressionSettings {
    float maxTranslationError = .001f;
    float maxRotationError = .01f;
    float maxVisibilityError = .01f;
    float maxAdaptiveDeltaError = .03f;
    float forceReductionRate = 1.f;
    bool allowTimecoded = true;
    bool allowAdaptiveDelta = true;
};
struct AnimationInput {
    std::uint32_t hierarchy = 0;
    std::uint32_t numFrames = 0;
    std::uint32_t frameRate = 0;
    bool hasCompressionSettings = false;
    AnimationCompressionSettings settings;
    std::vector<AnimationInputChannel> channels;
};
using AnimationExpandedFrame = TimecodedFrame;
struct AnimationPreparedChannel {
    std::uint32_t type = 0;
    std::uint16_t pivot = 0;
    std::vector<AnimationExpandedFrame> frames;
};
struct AnimationPrepared {
    std::uint32_t hierarchy = 0, numFrames = 0, frameRate = 0;
    bool bigEndian = false;
    AnimationCompressionSettings settings;
    std::vector<AnimationPreparedChannel> channels;
};
AnimationPrepared PrepareAnimation(const AnimationInput& input, int platform);
// Builds the runtime three buffers using the shared tracker; x86 only.
Relo::Chunk CompileAnimation(const AnimationInput& input, int platform);
// Exact HandleAnimation selection, including delta preference on equal estimates.
bool ChooseAdaptiveDelta(bool deltaExists, bool timecodedExists,
                         std::uint32_t timecodedSize, std::uint32_t deltaSize);
}} // namespace Recovered::W3x
