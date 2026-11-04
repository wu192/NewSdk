#pragma once
#include <cstdint>
#include <memory>
#include <vector>

namespace Recovered { namespace Relo {
using U32 = std::uint32_t;
struct Chunk {
    std::vector<unsigned char> instance, relocations, imports;
};

// Reconstructed semantics, not the VC8 EASTL container ABI. Serialized pointers
// remain 32 bits; callers of Push supply a native pointer location on x86.
class Tracker {
    struct Block {
        std::unique_ptr<unsigned char[]> data;
        U32 size, elementSize;
    };
    struct Bookmark { U32 block, offset, value; };
    struct MemberBookmark { U32 block, offset; const void* member; };
    U32 size_ = 0;
    std::vector<U32> stack_, blockOffsets_;
    std::vector<Block> blocks_;
    std::vector<Bookmark> pointers_, references_;
    std::vector<MemberBookmark> members_;
    Bookmark At(const void* location, U32 value) const;
    void Flatten(Chunk& output,bool bigEndian,U32 absoluteInstanceOffset,U32 destinationOffset,bool recordTables);
    void FixupMembers(const Tracker& owner,Chunk& output,bool bigEndian,U32 dataStart) const;
    static bool Less(const Bookmark& a, const Bookmark& b);
    static void Sort(std::vector<Bookmark>& values);
    static void Quick(Bookmark* first, Bookmark* last, int depth);
    static void AdjustHeap(Bookmark* first, int top, int size, int position, Bookmark value);
public:
    U32 Allocate(U32 count, U32 elementSize);
    void* Push(void** pointerLocation, U32 objectSize, U32 objectCount);
    void Pop();
    void AddReference(void* location, U32 value);
    bool GetMemberOffset(const void* member, U32& offset) const;
    void BookmarkMemberOffset(const void* member, void** offset);
    bool ExchangePointers(void* a, void* b);
    Chunk MakeRelocatable(bool bigEndian, U32 absoluteInstanceOffset);
    // Token rows are twelve bytes. Assemble token/data directly into one final
    // instance buffer; relocation/import tables still belong to data only.
    Chunk MakeTokenized(Tracker& data,bool bigEndian);
    void FixupMemberOffsets(const Tracker& owner, Chunk& chunk, bool bigEndian) const;
};
}}
