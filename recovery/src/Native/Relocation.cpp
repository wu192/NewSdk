#include "Relocation.h"
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace Recovered { namespace Relo {
namespace {
void Write(std::vector<unsigned char>& bytes, U32 at, U32 value, bool big) {
    for (U32 i = 0; i < 4; ++i) bytes.at(at + i) = static_cast<unsigned char>(value >> ((big ? 3-i : i)*8));
}
void Append(std::vector<unsigned char>& bytes, U32 value, bool big) {
    U32 at = static_cast<U32>(bytes.size()); bytes.resize(at + 4); Write(bytes, at, value, big);
}
}
U32 Tracker::Allocate(U32 count, U32 elementSize) {
    U32 size = (count * elementSize + 3u) & ~3u;
    Block b; b.data.reset(new unsigned char[size]()); b.size = size; b.elementSize = elementSize;
    blocks_.push_back(std::move(b)); size_ += size;
    return static_cast<U32>(blocks_.size()-1);
}
Tracker::Bookmark Tracker::At(const void* location, U32 value) const {
    U32 block = stack_.back();
    return {block, static_cast<U32>(static_cast<const unsigned char*>(location)-blocks_[block].data.get()), value};
}
void* Tracker::Push(void** location, U32 objectSize, U32 count) {
    bool root = stack_.empty();
    U32 parent = root ? 0 : stack_.back();
    U32 target = Allocate(count, objectSize); stack_.push_back(target);
    if (root) return *location = blocks_[target].data.get();
    if (!location) return nullptr; // allocation and stack push still occurred
    pointers_.push_back({parent, static_cast<U32>(reinterpret_cast<unsigned char*>(location)-blocks_[parent].data.get()), target});
    return *location = blocks_[target].data.get();
}
void Tracker::Pop() { stack_.pop_back(); }
void Tracker::AddReference(void* location, U32 value) {
    references_.push_back(At(location, value)); std::memcpy(location, &value, 4);
}
bool Tracker::GetMemberOffset(const void* member, U32& offset) const {
    U32 base = 0; auto address = reinterpret_cast<std::uintptr_t>(member);
    for (const auto& b : blocks_) {
        auto start = reinterpret_cast<std::uintptr_t>(b.data.get());
        if (address >= start && address-start < b.size) { offset = base + static_cast<U32>(address-start); return true; }
        base += b.size;
    }
    return false; // preserve the caller's output on failure
}
void Tracker::BookmarkMemberOffset(const void* member, void** offset) {
    auto b = At(offset, 0); members_.push_back({b.block, b.offset, member});
}
bool Tracker::ExchangePointers(void* a, void* b) {
    U32 current = stack_.back(); auto& block = blocks_[current];
    auto start = reinterpret_cast<std::uintptr_t>(block.data.get());
    auto aa = reinterpret_cast<std::uintptr_t>(a), bb = reinterpret_cast<std::uintptr_t>(b);
    if (aa < start || aa-start >= block.size || bb < start || bb-start >= block.size) return false;
    U32 x = static_cast<U32>(aa-start), y = static_cast<U32>(bb-start);
    auto alter = [&](U32& offset) {
        if (offset >= x && offset < x + block.elementSize) offset = offset-x+y;
        else if (offset >= y && offset < y + block.elementSize) offset = offset-y+x;
    };
    for (auto& p : pointers_) if (p.block == current) alter(p.offset);
    for (auto& p : references_) if (p.block == current) alter(p.offset);
    for (auto& p : members_) if (p.block == current) alter(p.offset);
    return true; // only bookmarks move; caller exchanges object bytes
}
bool Tracker::Less(const Bookmark& a, const Bookmark& b) {
    return a.block < b.block || (a.block == b.block && a.value < b.value);
}
void Tracker::AdjustHeap(Bookmark* first, int top, int size, int position, Bookmark value) {
    int child = position*2+2;
    while (child < size) {
        if (Less(first[child],first[child-1])) --child;
        first[position] = first[child]; position = child; child = child*2+2;
    }
    if (child == size) { first[position] = first[child-1]; position = child-1; }
    int parent = (position-1)/2;
    while (position > top && Less(first[parent],value)) {
        first[position] = first[parent]; position = parent; parent = (position-1)/2;
    }
    first[position] = value;
}
void Tracker::Quick(Bookmark* first, Bookmark* last, int depth) {
    while (last-first > 28 && depth > 0) {
        auto a = first, b = first+(last-first)/2, c = last-1;
        auto median = Less(*a,*b) ? (Less(*b,*c) ? b : (Less(*a,*c) ? c : a))
                                  : (Less(*a,*c) ? a : (Less(*b,*c) ? c : b));
        Bookmark pivot = *median;
        auto lo = first, hi = last;
        for (;;) {
            while (Less(*lo,pivot)) ++lo;
            do { --hi; } while (Less(pivot,*hi));
            if (lo >= hi) break;
            std::swap(*lo,*hi); ++lo;
        }
        --depth; Quick(lo,last,depth); last = lo;
    }
    if (depth == 0) {
        // Preserve the original heap's right-child preference when keys tie.
        // A library sort is allowed to produce a different relocation order.
        int size = static_cast<int>(last-first);
        if (size >= 2) for (int i=(size-2)/2; i>=0; --i) AdjustHeap(first,i,size,i,first[i]);
        while (size > 1) {
            Bookmark value = first[size-1]; first[size-1] = first[0];
            --size; AdjustHeap(first,0,size,0,value);
        }
    }
}
void Tracker::Sort(std::vector<Bookmark>& values) {
    if (values.empty()) return;
    int bits = 0; for (auto n = values.size(); n; n >>= 1) ++bits;
    Quick(values.data(),values.data()+values.size(),bits*2-2);
    // Equivalent to the original guarded first-28 insertion pass followed by
    // its sentinel-based unguarded pass, without reading before the range.
    for (size_t i = 1; i < values.size(); ++i) {
        auto v = values[i]; size_t j = i;
        while (j && Less(v,values[j-1])) { values[j] = values[j-1]; --j; }
        values[j] = v;
    }
}
void Tracker::Flatten(Chunk& out,bool big,U32 absolute,U32 destination,bool recordTables) {
    blockOffsets_.clear();blockOffsets_.reserve(blocks_.size());
    U32 base=0;
    for(const auto& block:blocks_) {
        blockOffsets_.push_back(base);
        if(block.size) std::memcpy(out.instance.data()+destination+base,block.data.get(),block.size);
        base+=block.size;
    }
    Sort(pointers_);Sort(references_);
    if(recordTables) {
        out.relocations.reserve((pointers_.size()+(pointers_.empty()?0:1))*4);
        out.imports.reserve((references_.size()+(references_.empty()?0:1))*4);
    }
    for(const auto& p:pointers_) {
        U32 at=blockOffsets_[p.block]+p.offset;
        if(recordTables) Append(out.relocations,at+absolute,big);
        Write(out.instance,destination+at,blockOffsets_[p.value]+absolute,big);
    }
    if(recordTables && !pointers_.empty()) Append(out.relocations,~0u,big);
    for(const auto& p:references_) {
        U32 at=blockOffsets_[p.block]+p.offset;
        if(recordTables) Append(out.imports,at+absolute,big);
        Write(out.instance,destination+at,p.value+1,big);
    }
    if(recordTables && !references_.empty()) Append(out.imports,~0u,big);
}
Chunk Tracker::MakeRelocatable(bool big,U32 absolute) {
    Chunk out;out.instance.resize(size_);
    Flatten(out,big,absolute,0,true);
    return out;
}
void Tracker::FixupMembers(const Tracker& owner,Chunk& chunk,bool big,U32 dataStart) const {
    struct Range {std::uintptr_t start;U32 size,base;};
    std::vector<Range> ranges;
    // Tiny owners/short query sets are cheaper to scan. Large token trees
    // amortize one address index; adjacent fields reuse their current range.
    if(owner.blocks_.size()>16 && members_.size()>=owner.blocks_.size()/2) {
        ranges.reserve(owner.blocks_.size());U32 base=0;
        for(const auto& block:owner.blocks_) {
            if(block.size) ranges.push_back({reinterpret_cast<std::uintptr_t>(block.data.get()),block.size,base});
            base+=block.size;
        }
        std::sort(ranges.begin(),ranges.end(),[](const Range& a,const Range& b){return a.start<b.start;});
    }
    const Range* previous=nullptr;
    for(const auto& m:members_) {
        U32 offset=0;bool found=false;auto address=reinterpret_cast<std::uintptr_t>(m.member);
        if(previous && address>=previous->start && address-previous->start<previous->size) {
            offset=previous->base+static_cast<U32>(address-previous->start);found=true;
        } else if(!ranges.empty()) {
            auto at=std::upper_bound(ranges.begin(),ranges.end(),address,[](std::uintptr_t value,const Range& range){return value<range.start;});
            if(at!=ranges.begin()) {
                --at;
                if(address-at->start<at->size) {previous=&*at;offset=at->base+static_cast<U32>(address-at->start);found=true;}
            }
        } else found=owner.GetMemberOffset(m.member,offset);
        if(!found) throw std::invalid_argument("Member outside data owner");
        Write(chunk.instance,blockOffsets_.at(m.block)+m.offset,dataStart+offset,big);
    }
}
void Tracker::FixupMemberOffsets(const Tracker& owner,Chunk& chunk,bool big) const {
    FixupMembers(owner,chunk,big,static_cast<U32>(chunk.instance.size()));
}
Chunk Tracker::MakeTokenized(Tracker& data,bool big) {
    Chunk out;out.instance.resize(static_cast<std::size_t>(size_)+data.size_);
    Flatten(out,big,0,0,false);
    FixupMembers(data,out,big,size_);
    data.Flatten(out,big,size_,size_,true);
    if(big) for(U32 at=0;at<size_;at+=12) {
        auto row=out.instance.data()+at;
        std::swap(row[0],row[3]);std::swap(row[1],row[2]);
        std::swap(row[4],row[7]);std::swap(row[5],row[6]);
    }
    return out;
}
}}
