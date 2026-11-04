#pragma once
#include "Relocation.h"
#include <map>
#include <string>
#include <vector>

namespace Recovered { namespace Native {
struct XmljoypadNode {
    std::string name, value, namespaceUri, assetNamespace;
    std::map<std::string, std::string> attributes;
    std::vector<XmljoypadNode> children;
};
unsigned int XmljoypadTypeId(const std::string& name);
Relo::Chunk CompileXmljoypad(const XmljoypadNode& input, bool bigEndian);
}}
