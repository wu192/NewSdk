#pragma once
#include "GameObject.h"
#include <array>
#include <limits>

namespace Recovered { namespace Native {
// Large child sets are classified once into stable linked groups backed by one
// contiguous allocation. Small nodes retain allocation-free linear traversal.
template<std::size_t N> class GameObjectChildGroups {
    static constexpr std::size_t none=static_cast<std::size_t>(-1);
    struct Entry {const GameObjectChild* child;std::size_t next;};
    struct Group {std::size_t first=none,last=none,count=0;};
    const std::vector<GameObjectChild>& input_;
    const char* const* names_;
    std::array<Group,N> groups_;
    std::vector<Entry> entries_;
    bool indexed_;
public:
    GameObjectChildGroups(const std::vector<GameObjectChild>& input,const char* const (&names)[N])
        :input_(input),names_(names),indexed_(input.size()>8) {
        if(!indexed_) return;
        entries_.reserve(input.size());
        for(const auto& child:input) for(std::size_t i=0;i<N;++i) if(child.name==names_[i]) {
            auto& group=groups_[i];auto index=entries_.size();entries_.push_back({&child,none});
            if(group.count) entries_[group.last].next=index;else group.first=index;
            group.last=index;++group.count;break;
        }
    }
    std::size_t Count(std::size_t group) const {
        if(indexed_) return groups_[group].count;
        std::size_t count=0;for(const auto& child:input_) if(child.name==names_[group])++count;return count;
    }
    const GameObjectChild* First(std::size_t group) const {
        if(indexed_) return groups_[group].count?entries_[groups_[group].first].child:nullptr;
        for(const auto& child:input_) if(child.name==names_[group]) return &child;return nullptr;
    }
    template<class F> void ForEach(std::size_t group,F visit) const {
        if(indexed_) {for(auto at=groups_[group].first;at!=none;at=entries_[at].next)visit(*entries_[at].child);}
        else for(const auto& child:input_) if(child.name==names_[group])visit(child);
    }
};
}}
