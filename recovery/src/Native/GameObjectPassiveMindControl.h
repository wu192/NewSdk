// Paired with EnhancerCorona's PassiveMindControlUpdateModuleData contract.
// This descriptor uses the existing module and inline ObjectFilter machinery.
constexpr U32 passiveMindControlType = 0xEE78914Bu;
constexpr unsigned passiveMindControlKind = 292u; // 291 is native DozerAIUpdate.
struct PassiveMindControlModuleData {
    U32 typeId, moduleId, capacity;
    float acquireRadius;
    U32 captureIntervalFrames;
    ObjectFilter targetFilter;
};
static_assert(sizeof(PassiveMindControlModuleData) == 0x84, "PassiveMindControl data ABI");
static_assert(offsetof(PassiveMindControlModuleData, targetFilter) == 0x14, "Inline ObjectFilter ABI");
const ModuleField passiveMindControlFields[] = {
    { "Capacity", 0x08u, ModuleValue::Unsigned, "3", nullptr },
    { "AcquireRadius", 0x0Cu, ModuleValue::Float, nullptr, nullptr },
    { "CaptureIntervalFrames", 0x10u, ModuleValue::Unsigned, "15", nullptr },
};
const ModuleFieldSet passiveMindControlFieldSet = { passiveMindControlFields, 3u };
const ModuleToken passiveMindControlTokens[] = {
    { 1u, 0xD75D93A2u, 0u, &moduleTable0, 0u, 0u },
    { 0xC890C0E1u, 0x1E0413C5u, 0x08u, nullptr, 0u, 0u },
    { 0xB4BD3599u, 0x4292E499u, 0x0Cu, nullptr, 0u, 0u },
    { 0x7EF7A1A1u, 0x1E0413C5u, 0x10u, nullptr, 0u, 0u },
    { 0xB73A2D5Fu, 0xE46EA3B2u, 0x14u, nullptr, 8u, 0u },
};
const ModuleLayout passiveMindControlLayout = { passiveMindControlTokens, 5u };
const ModuleChoice passiveMindControlChoice = {
    passiveMindControlType, &passiveMindControlLayout, 0x84u, passiveMindControlKind
};
