// Paired with EnhancerCorona's PassiveMindControlUpdateModuleData contract.
// This descriptor uses the existing module and inline ObjectFilter machinery.
constexpr U32 passiveMindControlType = 0xEE78914Bu;
constexpr unsigned passiveMindControlKind = 292u; // 291 is native DozerAIUpdate.
struct PassiveMindControlModuleData {
    U32 typeId, moduleId, capacity;
    float acquireRadius;
    U32 captureIntervalFrames;
    ObjectFilter targetFilter;
    void* specialPowerTemplate;
    U32 disableWhenStatus[7];
};
static_assert(sizeof(PassiveMindControlModuleData) == 0xA4, "PassiveMindControl data ABI");
static_assert(offsetof(PassiveMindControlModuleData, targetFilter) == 0x14, "Inline ObjectFilter ABI");
static_assert(offsetof(PassiveMindControlModuleData, specialPowerTemplate) == 0x84, "SpecialPower reference ABI");
static_assert(offsetof(PassiveMindControlModuleData, disableWhenStatus) == 0x88, "ObjectStatus mask ABI");
const ModuleField passiveMindControlFields[] = {
    { "Capacity", 0x08u, ModuleValue::Unsigned, "3", nullptr },
    { "AcquireRadius", 0x0Cu, ModuleValue::Float, nullptr, nullptr },
    { "CaptureIntervalFrames", 0x10u, ModuleValue::Unsigned, "15", nullptr },
    { "SpecialPowerTemplate", 0x84u, ModuleValue::Reference, nullptr, nullptr },
    { "DisableWhenStatus", 0x88u, ModuleValue::ObjectStatus, "", nullptr },
};
const ModuleFieldSet passiveMindControlFieldSet = { passiveMindControlFields, 5u };
const ModuleToken passiveMindControlTokens[] = {
    { 1u, 0xD75D93A2u, 0u, &moduleTable0, 0u, 0u },
    { 0xC890C0E1u, 0x1E0413C5u, 0x08u, nullptr, 0u, 0u },
    { 0xB4BD3599u, 0x4292E499u, 0x0Cu, nullptr, 0u, 0u },
    { 0x7EF7A1A1u, 0x1E0413C5u, 0x10u, nullptr, 0u, 0u },
    { 0xB73A2D5Fu, 0xE46EA3B2u, 0x14u, nullptr, 8u, 0u },
    { 0x81D85EFAu, 0xAEE83888u, 0x84u, nullptr, 0u, 0u },
    { 0x7E6EE20Bu, 0xA7BD811Eu, 0x88u, nullptr, 0u, 0u },
};
const ModuleLayout passiveMindControlLayout = { passiveMindControlTokens, 7u };
const ModuleChoice passiveMindControlChoice = {
    passiveMindControlType, &passiveMindControlLayout, 0xA4u, passiveMindControlKind
};
