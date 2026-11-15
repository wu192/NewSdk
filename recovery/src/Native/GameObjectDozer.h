// Native DozerAIUpdateModuleData contract, from the original Tokenizer
// Tokenize(DozerAIUpdateModuleData) and its polymorphic type (0x8D093323).
// Keep this restoration separate from the generated eight-type AI metadata.
const ModuleField dozerAIUpdateFields[] = {
    { "RepairHealthPercentPerSecond", 76u, ModuleValue::Float, "0", nullptr },
    { "BoredTime", 80u, ModuleValue::Float, "0", nullptr },
    { "BoredRange", 84u, ModuleValue::Float, "0", nullptr },
};
const ModuleFieldSet dozerAIUpdateFieldSet = { dozerAIUpdateFields, 3u };
const ModuleToken dozerAIUpdateTokens[] = {
    { 1u, 0x08DBC00Eu, 0u, &aiUpdateAIUpdateModuleData, 0u, 0u },
    { 0x8229777Eu, 0x645864F3u, 76u, nullptr, 0u, 0u },
    { 0x18C1A7F3u, 0x645864F3u, 80u, nullptr, 0u, 0u },
    { 0xC492A6B5u, 0x645864F3u, 84u, nullptr, 0u, 0u },
};
const ModuleLayout dozerAIUpdateLayout = { dozerAIUpdateTokens, 4u };
const ModuleChoice dozerAIUpdateChoice = { 0x8D093323u, &dozerAIUpdateLayout, 88u, 291u };
