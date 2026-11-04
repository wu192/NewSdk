#pragma once
#include <map>
#include <string>
#include <functional>

namespace Recovered { namespace Native {
// Schema field names are const char*. Transparent lookup avoids constructing
// an owning string for every absent or present attribute lookup.
struct AttributeMap : std::map<std::string,std::string,std::less<>> {
    using Base=std::map<std::string,std::string,std::less<>>;
    using Base::Base;
    using Base::operator=;
    AttributeMap()=default;
    AttributeMap(const AttributeMap&)=default;
    AttributeMap(AttributeMap&&)=default;
    AttributeMap& operator=(const AttributeMap&)=default;
    AttributeMap& operator=(AttributeMap&&)=default;
    // Shared XML adapters still supply their existing map type. This is the
    // same owning input copy as before, performed only at the adapter boundary.
    template<class Compare> AttributeMap(const std::map<std::string,std::string,Compare>& source)
        :Base(source.begin(),source.end()) {}
    template<class Compare> AttributeMap& operator=(const std::map<std::string,std::string,Compare>& source) {
        Base::clear();Base::insert(source.begin(),source.end());return *this;
    }
};
}}
