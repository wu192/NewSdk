// Complete queue-parking contracts; original blob 6e40a9ed6c9f961d294d49768d911e6a6f354619.
const ModuleToken queueParkingCreateModuleDataTokens[] = {
    { 1u, 2769121946u, 0u, &dieBehaviorModuleData, 0u, 0u },
};
const ModuleLayout queueParkingCreateModuleData = { queueParkingCreateModuleDataTokens, 1u };
const ModuleField queueParkingQueueDockModuleDataFields[] = {
    { "DockStartBoneName", 8u, ModuleValue::String, "", nullptr },
    { "DockActionBoneName", 16u, ModuleValue::String, "", nullptr },
    { "DockEndBoneName", 24u, ModuleValue::String, "", nullptr },
    { "QueueBonePrefix", 32u, ModuleValue::String, "", nullptr },
    { "TurnTableBoneName", 40u, ModuleValue::String, "", nullptr },
    { "DockingAnimation", 48u, ModuleValue::Enum, nullptr, &aiModelConditionFlagType },
    { "MaxConcurrentMiners", 52u, ModuleValue::Unsigned, "1", nullptr },
};
const ModuleField queueParkingParkingPlaceBehaviorModuleDataFields[] = {
    { "NumRows", 8u, ModuleValue::Signed, nullptr, nullptr },
    { "NumCols", 12u, ModuleValue::Signed, nullptr, nullptr },
    { "ApproachHeight", 16u, ModuleValue::Float, nullptr, nullptr },
    { "ParkInHangars", 52u, ModuleValue::Boolean, nullptr, nullptr },
    { "DoorOpenTime", 20u, ModuleValue::Time, "0s", nullptr },
    { "DoorCloseTime", 24u, ModuleValue::Time, "0s", nullptr },
    { "LowersOntoParkingPlaceOnProduction", 53u, ModuleValue::Boolean, "false", nullptr },
    { "CanSetRallyPoint", 54u, ModuleValue::Boolean, "true", nullptr },
    { "DoorsUnpackOpen", 55u, ModuleValue::Boolean, "true", nullptr },
    { "QueueDistance", 28u, ModuleValue::Float, "100", nullptr },
    { "LimitedProductionAircraftSpots", 32u, ModuleValue::Signed, "0", nullptr },
    { "HelipadBoneName", 36u, ModuleValue::String, "HeliPark01", nullptr },
};
const ModuleFieldSet queueParkingFieldSets[] = {
    { queueParkingQueueDockModuleDataFields, 7u },
    { queueParkingParkingPlaceBehaviorModuleDataFields, 12u },
};
const ModuleToken queueParkingQueueDockModuleDataTokens[] = {
    { 1u, 816016269u, 0u, &queueParkingCreateModuleData, 0u, 0u },
    { 3160487442u, 438907818u, 8u, nullptr, 0u, 0u },
    { 1056961469u, 438907818u, 16u, nullptr, 0u, 0u },
    { 3246219667u, 438907818u, 24u, nullptr, 0u, 0u },
    { 2098610190u, 438907818u, 32u, nullptr, 0u, 0u },
    { 3567440301u, 438907818u, 40u, nullptr, 0u, 0u },
    { 1270412640u, 359808523u, 48u, nullptr, 0u, 0u },
    { 983236950u, 503583685u, 52u, nullptr, 0u, 0u },
};
const ModuleLayout queueParkingQueueDockModuleData = { queueParkingQueueDockModuleDataTokens, 8u };
const ModuleToken queueParkingParkingPlaceBehaviorModuleDataTokens[] = {
    { 1u, 4043318533u, 0u, &spawnUpdateModuleData, 0u, 0u },
    { 3047136039u, 602296504u, 8u, nullptr, 0u, 0u },
    { 942460295u, 602296504u, 12u, nullptr, 0u, 0u },
    { 719946706u, 1683514611u, 16u, nullptr, 0u, 0u },
    { 1437207067u, 2011428087u, 52u, nullptr, 0u, 0u },
    { 1271591122u, 2575722984u, 20u, nullptr, 0u, 0u },
    { 1105908716u, 2575722984u, 24u, nullptr, 0u, 0u },
    { 2939451305u, 2011428087u, 53u, nullptr, 0u, 0u },
    { 2889851600u, 2011428087u, 54u, nullptr, 0u, 0u },
    { 378112203u, 2011428087u, 55u, nullptr, 0u, 0u },
    { 1934486978u, 1683514611u, 28u, nullptr, 0u, 0u },
    { 4294017903u, 602296504u, 32u, nullptr, 0u, 0u },
    { 623834041u, 438907818u, 36u, nullptr, 0u, 0u },
    { 2545738018u, 3832456114u, 44u, nullptr, 7u, 0u },
    { 1514901642u, 691437687u, 48u, nullptr, 2u, 0u },
};
const ModuleLayout queueParkingParkingPlaceBehaviorModuleData = { queueParkingParkingPlaceBehaviorModuleDataTokens, 15u };
const ModuleChoice queueParkingChoices[] = {
    { 2941001505u, &queueParkingQueueDockModuleData, 56u, 128u },
    { 3921409083u, &queueParkingParkingPlaceBehaviorModuleData, 56u, 129u },
};
