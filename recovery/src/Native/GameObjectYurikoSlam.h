// Uprising 1.0 AddLift input parser 0x26c0000 and Fling parser 0x4ae440.
// StoreObjectsLinkID and LiftObjectLinkID are hash-verified original names.
// FlingSpeedMax/Min are descriptive aliases retaining original input token IDs.
const ModuleField addLiftFields[] = {
    { "LiftObjectLinkID", 468u, ModuleValue::Signed, "0", nullptr },
};
const ModuleFieldSet addLiftFieldSet = { addLiftFields, 1u };
const ModuleToken addLiftTokens[] = {
    { 1u, 0xa74a234cu, 0u, &specialDerivedStoreObjectsSpecialPowerModuleData, 0u, 0u },
    { 0xddccddf3u, 0x23e650b8u, 468u, nullptr, 0u, 0u },
};
const ModuleLayout addLiftLayout = { addLiftTokens, 2u };
const ModuleField flingFields[] = {
    { "StoreObjectsLinkID", 452u, ModuleValue::Signed, "0", nullptr },
    { "LiftObjectLinkID", 456u, ModuleValue::Signed, "0", nullptr },
    { "FlingSpeedMax", 460u, ModuleValue::Float, "300", nullptr },
    { "FlingSpeedMin", 464u, ModuleValue::Float, "100", nullptr },
};
const ModuleFieldSet flingFieldSet = { flingFields, 4u };
// Native outer list token and 8-byte source/physical-proxy pairs. Inner
// Source/Replacement names and hashes are an explicit RA3-port contract.
const ModuleField flingReplacementFields[] = {
    { "Source", 0u, ModuleValue::AssetId, nullptr, nullptr },
    { "Replacement", 4u, ModuleValue::AssetId, nullptr, nullptr },
};
const ModuleFieldSet flingReplacementFieldSet = { flingReplacementFields, 2u };
const ModuleToken flingReplacementTokens[] = {
    { 0xd4edc301u, 0x148ed75du, 0u, nullptr, 0u, 0u },
    { 0xb955f215u, 0x148ed75du, 4u, nullptr, 0u, 0u },
};
const ModuleLayout flingReplacementLayout = { flingReplacementTokens, 2u };
const ModuleToken flingTokens[] = {
    { 1u, 0xcefe1113u, 0u, &specialPowerSpecialPowerModuleData, 0u, 0u },
    { 0xfee927fcu, 0x23e650b8u, 452u, nullptr, 0u, 0u },
    { 0xddccddf3u, 0x23e650b8u, 456u, nullptr, 0u, 0u },
    { 0x7b773fb9u, 0x645864f3u, 460u, nullptr, 0u, 0u },
    { 0x80503b2du, 0x645864f3u, 464u, nullptr, 0u, 0u },
    { 0x891c564bu, 0xee9c04c0u, 468u, &flingReplacementLayout, 1u, 8u },
};
const ModuleLayout flingLayout = { flingTokens, 6u };
// A new derived name keeps old LiftObjectUpdate's 40-byte data and tokens
// unchanged. This port adds the missing Uprising link field after that prefix.
const ModuleField yurikoLiftFields[] = {
    { "LiftObjectLinkID", 40u, ModuleValue::Signed, "0", nullptr },
};
const ModuleFieldSet yurikoLiftFieldSet = { yurikoLiftFields, 1u };
const ModuleToken yurikoLiftTokens[] = {
    { 1u, 0xbf91bfcdu, 0u, &utilityLiftObjectUpdateModuleData, 0u, 0u },
    { 0xddccddf3u, 0x23e650b8u, 40u, nullptr, 0u, 0u },
};
const ModuleLayout yurikoLiftLayout = { yurikoLiftTokens, 2u };
const ModuleChoice slamChoices[] = {
    { 0xd9b247dfu, &addLiftLayout, 472u, 293u },
    { 0x12d1fad9u, &flingLayout, 476u, 294u },
    { 0xaa9e10d6u, &yurikoLiftLayout, 44u, 296u },
};

// Uprising parser 0x499D60, type 19BF3331. DynamicsCollideModuleData's
// parser 0x432EA0 directly inherits BehaviorModuleData, independently of Collide.
const ModuleField damageDynamicsFields[] = {
    { "FullDamageSpeed", 8u, ModuleValue::Float, "10", nullptr },
};
const ModuleFieldSet damageDynamicsFieldSet = { damageDynamicsFields, 1u };
const ModuleToken dynamicsCollideTokens[] = {
    { 1u, 0xa50d729au, 0u, &dieBehaviorModuleData, 0u, 0u },
};
const ModuleLayout dynamicsCollideLayout = { dynamicsCollideTokens, 1u };
const ModuleToken damageDynamicsTokens[] = {
    { 1u, 0xf1a07b36u, 0u, &dynamicsCollideLayout, 0u, 0u },
    { 0x00f63ca4u, 0x645864f3u, 8u, nullptr, 0u, 0u },
    { 0xf4948415u, 0xc0f53e99u, 12u, nullptr, 11u, DamageNuggetDataSize },
};
const ModuleLayout damageDynamicsLayout = { damageDynamicsTokens, 3u };
const ModuleChoice damageDynamicsChoice = { 0x19bf3331u, &damageDynamicsLayout, 20u, 297u };
