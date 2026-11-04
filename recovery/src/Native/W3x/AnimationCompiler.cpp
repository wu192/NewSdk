#include "AnimationPipeline.h"
#include "AdaptiveDelta.h"
#include "Timecoded.h"
#include <cstring>
#include <limits>
#include <stdexcept>

namespace Recovered { namespace W3x {
namespace {
void Word(unsigned char* bytes, std::uint32_t value, bool bigEndian) {
    for (unsigned i = 0; i < 4; ++i)
        bytes[i] = static_cast<unsigned char>(value >> (8 * (bigEndian ? 3-i : i)));
}
void CopyAllocation(Relo::Tracker& tracker, unsigned char* parent,
                    std::uint32_t slot, std::uint32_t elementSize,
                    const std::vector<unsigned char>& bytes) {
    if (bytes.size() > std::numeric_limits<std::uint32_t>::max() ||
        bytes.size() % elementSize != 0)
        throw std::length_error("Animation allocation exceeds tracker limits");
    unsigned char* target = static_cast<unsigned char*>(tracker.Push(
        reinterpret_cast<void**>(parent + slot), elementSize,
        static_cast<std::uint32_t>(bytes.size() / elementSize)));
    tracker.Pop();
    if (!bytes.empty()) std::memcpy(target, bytes.data(), bytes.size());
}
void WriteTimecoded(Relo::Tracker& tracker, void** slot,
                    const TimecodedChannel& channel, bool bigEndian) {
    TimecodedEncoded encoded = EncodeTimecoded(channel, bigEndian);
    unsigned char* root = static_cast<unsigned char*>(tracker.Push(slot, 36, 1));
    std::memcpy(root, encoded.root.data(), encoded.root.size());
    CopyAllocation(tracker, root, 32, 4, encoded.values);
    CopyAllocation(tracker, root, 24, 2, encoded.codes);
    tracker.Pop();
}
void WriteDelta(Relo::Tracker& tracker, void** slot,
                const AdaptiveDeltaChannel& channel, bool bigEndian) {
    AdaptiveDeltaEncoded encoded = EncodeAdaptiveDelta(channel, bigEndian);
    unsigned char* root = static_cast<unsigned char*>(tracker.Push(slot, 44, 1));
    std::memcpy(root, encoded.root.data(), encoded.root.size());
    CopyAllocation(tracker, root, 28, 4, encoded.initial);
    CopyAllocation(tracker, root, 40, 1, encoded.data);
    tracker.Pop();
}
}
Relo::Chunk CompileAnimation(const AnimationInput& input, int platform) {
    if (sizeof(void*) != 4)
        throw std::logic_error("W3X animation tracker serialization requires an x86 host");
    AnimationPrepared prepared = PrepareAnimation(input, platform);
    if (prepared.channels.size() > std::numeric_limits<std::uint32_t>::max() / 4u)
        throw std::length_error("Too many animation channels");
    Relo::Tracker tracker;
    unsigned char* root = nullptr;
    tracker.Push(reinterpret_cast<void**>(&root), 24, 1);
    tracker.AddReference(root + 4, prepared.hierarchy);
    Word(root + 8, prepared.numFrames, prepared.bigEndian);
    Word(root + 12, prepared.frameRate, prepared.bigEndian);
    Word(root + 16, static_cast<std::uint32_t>(prepared.channels.size()), prepared.bigEndian);
    unsigned char* slots = static_cast<unsigned char*>(tracker.Push(
        reinterpret_cast<void**>(root + 20), 4,
        static_cast<std::uint32_t>(prepared.channels.size())));
    for (std::size_t i = 0; i < prepared.channels.size(); ++i) {
        const AnimationPreparedChannel& channel = prepared.channels[i];
        if (channel.frames.empty())
            throw std::invalid_argument("Zero-frame channels have no defined original Timecoded WriteOut");
        AdaptiveDeltaChannel delta;
        bool hasDelta = false;
        if (prepared.settings.allowAdaptiveDelta) {
            hasDelta = CompressAdaptiveDeltaFrames(channel.type, channel.pivot, channel.frames,
                prepared.settings.maxAdaptiveDeltaError, delta);
        }
        // Timecoded is evaluated unless delta succeeds with timecoded disabled.
        // The matching WriteOut below deliberately does not use payload length
        // to choose the format; the original estimate formulas are authoritative.
        bool hasTimecoded = !hasDelta || prepared.settings.allowTimecoded;
        TimecodedChannel timecoded;
        if (hasTimecoded) {
            timecoded = CompressTimecoded(channel.type, channel.pivot, channel.frames,
                prepared.settings.maxTranslationError, prepared.settings.maxRotationError,
                prepared.settings.maxVisibilityError, prepared.settings.forceReductionRate);
        }
        void** slot = reinterpret_cast<void**>(slots + i * 4);
        if (ChooseAdaptiveDelta(hasDelta, hasTimecoded,
                hasTimecoded ? EstimateTimecodedSize(timecoded) : 0,
                hasDelta ? EstimateAdaptiveDeltaSize(delta) : 0))
            WriteDelta(tracker, slot, delta, prepared.bigEndian);
        else
            WriteTimecoded(tracker, slot, timecoded, prepared.bigEndian);
    }
    tracker.Pop();
    // Root need not be popped before MakeRelocatable; source FinalizeTracker
    // invokes it with the root stack frame still present.
    return tracker.MakeRelocatable(prepared.bigEndian, 0);
}
}} // namespace Recovered::W3x
