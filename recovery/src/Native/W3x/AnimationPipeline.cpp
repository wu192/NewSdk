#include "AnimationPipeline.h"
#include <stdexcept>
#include <limits>
#include <utility>

namespace Recovered { namespace W3x {
namespace {
AnimationCompressionSettings Settings(const AnimationInput& input, int platform) {
    AnimationCompressionSettings settings = input.settings;
    if (input.hasCompressionSettings) {
        if (!settings.allowAdaptiveDelta && !settings.allowTimecoded) {
            settings.allowTimecoded = true;
            settings.maxTranslationError = 0.f;
            settings.maxRotationError = 0.f;
            settings.maxVisibilityError = 0.f;
            settings.forceReductionRate = 1.f;
            // The original deliberately preserves maxAdaptiveDeltaError.
        }
    } else {
        if (platform != 0 && platform != 1 && platform != 2)
            throw std::invalid_argument("Internal error: unknown platform");
        settings.maxTranslationError = .002f;
        settings.maxRotationError = .003f;
        settings.maxVisibilityError = .01f;
        settings.maxAdaptiveDeltaError = .001f;
        settings.forceReductionRate = 1.f;
        settings.allowTimecoded = true;
        settings.allowAdaptiveDelta = true;
    }
    return settings;
}
}
AnimationPrepared PrepareAnimation(const AnimationInput& input, int platform) {
    AnimationPrepared result;
    result.hierarchy = input.hierarchy;
    result.numFrames = input.numFrames;
    result.frameRate = input.frameRate;
    result.bigEndian = platform != 0;
    result.settings = Settings(input, platform);
    result.channels.reserve(input.channels.size());
    for (const AnimationInputChannel& source : input.channels) {
        AnimationPreparedChannel channel;
        channel.type = source.type;
        channel.pivot = source.pivot;
        std::vector<float> defaults;
        if (source.typeId == 1355976437u) {
            switch (source.type) {
            case 0: case 1: case 2: defaults.push_back(0.f); break;
            case 4: defaults.push_back(1.f); break;
            default: throw std::invalid_argument("Internal error: unknown animation channel usage");
            }
        } else if (source.typeId == 4131577120u) {
            // Quaternion channels are not restricted to Orientation by HandleAnimation.
            defaults = {0.f, 0.f, 0.f, 1.f};
        } else {
            throw std::invalid_argument("Internal error: unknown animation channel type");
        }
        // The original writes unchecked at FirstFrame+i. Reject the undefined
        // memory-access cases rather than emulating heap corruption.
        if (!source.frames.empty() && (source.firstFrame > input.numFrames ||
            source.frames.size() > static_cast<std::uint64_t>(input.numFrames) - source.firstFrame))
            throw std::invalid_argument("Animation frame range exceeds NumFrames");
        channel.frames.resize(input.numFrames);
        for (std::uint32_t i = 0; i < input.numFrames; ++i) {
            channel.frames[i].index = i;
            channel.frames[i].values = defaults;
            channel.frames[i].binaryMove = false;
        }
        for (std::size_t i = 0; i < source.frames.size(); ++i) {
            if (source.frames[i].values.size() != defaults.size())
                throw std::invalid_argument("Animation frame vector dimension mismatch");
            AnimationExpandedFrame& frame = channel.frames[source.firstFrame + i];
            frame.values = source.frames[i].values;
            frame.binaryMove = source.frames[i].binaryMove;
        }
        result.channels.push_back(std::move(channel));
    }
    return result;
}
bool ChooseAdaptiveDelta(bool deltaExists, bool timecodedExists,
                         std::uint32_t timecodedSize, std::uint32_t deltaSize) {
    return deltaExists && (!timecodedExists || timecodedSize >= deltaSize);
}
}} // namespace Recovered::W3x
