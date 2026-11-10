// Uprising 1.0 parser 0x475560, image base 0x400000. The wire token IDs
// are native; public names are descriptive aliases. The parent data layout
// remains the existing RA3 layout (264 bytes), preserving all old assets.
const ModuleField yurikoShieldFields[] = {
    { "ReflectionFX", 264u, ModuleValue::Reference, nullptr, nullptr },
    { "AlternateReflectionFX", 268u, ModuleValue::Reference, nullptr, nullptr },
    { "AlternateReflectionDamageTypes", 272u, ModuleValue::Bits, nullptr, &abilityFinishDamageType },
};
const ModuleFieldSet yurikoShieldFieldSet = { yurikoShieldFields, 3u };
const ModuleToken yurikoShieldTokens[] = {
    { 1u, 0x6c11b6f7u, 0u, &abilityFinishShieldSphereUpdateModuleData, 0u, 0u },
    { 0x945463f7u, 0x0d1c8e83u, 264u, nullptr, 0u, 0u },
    { 0x92285c9fu, 0x0d1c8e83u, 268u, nullptr, 0u, 0u },
    { 0xd9086106u, 0x07faaea4u, 272u, nullptr, 0u, 0u },
};
const ModuleLayout yurikoShieldLayout = { yurikoShieldTokens, 4u };
const ModuleChoice shieldChoices[] = {
    { 0x95d855b6u, &yurikoShieldLayout, 280u, 295u },
};
