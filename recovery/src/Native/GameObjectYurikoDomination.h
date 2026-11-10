// Uprising 1.0 LureObjectsUpdate parser 0x42eb80; see YURIKO_DOMINATION.md.
// Public aliases retain the verified original input field IDs. RA3 uses seven
// ObjectStatus words; later offsets are adjusted by four bytes from Uprising.
const ModuleField lureFields[] = {
    { "LureObjectLinkID", 8u, ModuleValue::Signed, "0", nullptr },
    { "MaxRangeSquared", 12u, ModuleValue::Float, "1000000", nullptr },
    { "GuardRange", 16u, ModuleValue::Float, "99999", nullptr },
    { "GuardStatus", 20u, ModuleValue::ObjectStatus, nullptr, nullptr },
    { "UpdateRate", 48u, ModuleValue::Signed, "10", nullptr },
    { "TargetMarkerObjectRef", 52u, ModuleValue::AssetId, nullptr, nullptr },
    { "DisabledTypesToProcess", 56u, ModuleValue::Bits, nullptr, &specialPowerDisabled },
};
const ModuleFieldSet lureFieldSet = { lureFields, 7u };
const ModuleToken lureOffsetTokens[] = {
    { 0x043c9363u, 0x645864f3u, 0u, nullptr, 0u, 0u },
    { 0x1e8fda1au, 0x645864f3u, 4u, nullptr, 0u, 0u },
    { 0x30be6097u, 0x645864f3u, 8u, nullptr, 0u, 0u },
};
const ModuleLayout lureOffsetLayout = { lureOffsetTokens, 3u };
const ModuleToken lureTokens[] = {
    { 1u, 0xf1002505u, 0u, &spawnUpdateModuleData, 0u, 0u },
    { 0x16e57839u, 0x23e650b8u, 8u, nullptr, 0u, 0u },
    { 0x3283a5d7u, 0x645864f3u, 12u, nullptr, 0u, 0u },
    { 0x5052f256u, 0x645864f3u, 16u, nullptr, 0u, 0u },
    { 0x38b23c4fu, 0xa7bd811eu, 20u, nullptr, 0u, 0u },
    { 0xfd25ec9eu, 0x23e650b8u, 48u, nullptr, 0u, 0u },
    { 0xf8234249u, 0x148ed75du, 52u, nullptr, 0u, 0u },
    { 0x383e6b6du, 0x196570c9u, 56u, nullptr, 0u, 0u },
    { 0x13b3d27cu, 0x29368077u, 60u, &lureOffsetLayout, 1u, 12u },
};
const ModuleLayout lureLayout = { lureTokens, 9u };
// The RA3 port uses StoreObjectsSpecialPower's existing TeleportLinkID to
// select LureObjectsUpdate.LureObjectLinkID. It does not add a second storage identifier.
const ModuleToken addLureTokens[] = {
    { 1u, 0xa74a234cu, 0u, &specialDerivedStoreObjectsSpecialPowerModuleData, 0u, 0u },
};
const ModuleLayout addLureLayout = { addLureTokens, 1u };
const ModuleChoice dominationChoices[] = {
    { 0xb65ab72au, &lureLayout, 68u, 291u },
    { 0xa7deffccu, &addLureLayout, 468u, 292u },
};
