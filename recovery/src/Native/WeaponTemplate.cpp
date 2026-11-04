#include "WeaponTemplate.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#include <sstream>
#include <algorithm>
#include <cctype>
#include <type_traits>
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native {
using U32 = std::uint32_t;
namespace {
// Original WeaponTemplate Marshal and Tokenize; offsets are bytes in its 328-byte root.
const WeaponField fields[] = {
    { "AttackRange", "0", 4u, 0xA8A3BBFEu, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "MinimumAttackRange", "0", 8u, 0x9797D067u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "RangeBonusMinHeight", "0", 12u, 0x03646BDEu, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "RangeBonus", "0", 16u, 0x02E3525Eu, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "RangeBonusPerFoot", "0", 20u, 0x9FCEE103u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "RequestAssistRange", "0", 24u, 0x95FF8A6Cu, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "AcceptableAimDelta", "0d", 28u, 0x1066C317u, 0x4E16EA4Du, WeaponValue::Angle, WeaponToken::Member, "" },
    { "AimDirection", "0d", 32u, 0x7EF694B1u, 0x4E16EA4Du, WeaponValue::Angle, WeaponToken::Member, "" },
    { "ScatterRadius", "0", 36u, 0x7A72CBE9u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "ScatterLength", "0", 40u, 0xCB2EC959u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "ScatterIndependently", "false", 292u, 0x0FEB4E73u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "WeaponSpeed", "99999.0", 44u, 0xE16286CBu, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "MinWeaponSpeed", "99999.0", 48u, 0x388A03C9u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "MaxWeaponSpeed", "99999.0", 52u, 0x490E4588u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "ScaleWeaponSpeed", "false", 293u, 0x3BA0478Fu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "IgnoresContactPoints", "false", 294u, 0x61128F5Cu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ScaleAttackRangeByAmmoRemaining", "false", 295u, 0xB6E40781u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "CanBeDodged", "false", 296u, 0xF1DF59ACu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "IdleAfterFiringDelaySeconds", "-1s", 56u, 0x5D5C6183u, 0x998669E8u, WeaponValue::Time, WeaponToken::Member, "" },
    { "HoldAfterFiringDelaySeconds", "0s", 60u, 0x6B1FF4E9u, 0x998669E8u, WeaponValue::Time, WeaponToken::Member, "" },
    { "HoldDuringReload", "false", 297u, 0x00A9BE33u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "CanFireWhileMoving", "false", 298u, 0xBBCBE27Eu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "WeaponRecoil", "0d", 64u, 0x1319E3B1u, 0x4E16EA4Du, WeaponValue::Angle, WeaponToken::Member, "" },
    { "MinTargetPitch", "-180d", 68u, 0x553B6C95u, 0x4E16EA4Du, WeaponValue::Angle, WeaponToken::Member, "" },
    { "MaxTargetPitch", "180d", 72u, 0xB7737023u, 0x4E16EA4Du, WeaponValue::Angle, WeaponToken::Member, "" },
    { "PreferredTargetBone", nullptr, 76u, 0x4C932F32u, 0x1A2933AAu, WeaponValue::String, WeaponToken::Member, "" },
    { "FireSound", nullptr, 84u, 0x120A78E8u, 0x63D7B468u, WeaponValue::ReferencePointer, WeaponToken::Pointer, "" },
    { "FireSoundPerClip", nullptr, 88u, 0x3819F796u, 0x63D7B468u, WeaponValue::ReferencePointer, WeaponToken::Pointer, "" },
    { "FiringLoopSound", nullptr, 92u, 0x86CE6D08u, 0x63D7B468u, WeaponValue::ReferencePointer, WeaponToken::Pointer, "" },
    { "FiringLoopSoundContinuesDuringReload", "true", 299u, 0x8E9F06FBu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "FireFX", nullptr, 96u, 0x9F1BCA25u, 0x0D1C8E83u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "FireVeteranFX", nullptr, 100u, 0x6DBBA63Eu, 0x0D1C8E83u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "FireFlankFX", nullptr, 104u, 0x1459E5BDu, 0x0D1C8E83u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "PreAttackFX", nullptr, 108u, 0x17F17220u, 0x0D1C8E83u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "ClipSize", "0", 112u, 0xB68DBC28u, 0x23E650B8u, WeaponValue::Integer, WeaponToken::Member, "" },
    { "ContinuousFireOne", "99999", 116u, 0x9B8145E9u, 0x23E650B8u, WeaponValue::Integer, WeaponToken::Member, "" },
    { "ContinuousFireTwo", "99999", 120u, 0xCC9D6D26u, 0x23E650B8u, WeaponValue::Integer, WeaponToken::Member, "" },
    { "ContinuousFireCoastSeconds", "0s", 124u, 0x97FDA959u, 0x998669E8u, WeaponValue::Time, WeaponToken::Member, "" },
    { "AutoReloadWhenIdleSeconds", "0s", 128u, 0x88099007u, 0x998669E8u, WeaponValue::Time, WeaponToken::Member, "" },
    { "ShotsPerBarrel", "0", 132u, 0x95C56669u, 0x23E650B8u, WeaponValue::Integer, WeaponToken::Member, "" },
    { "DamageDealtAtSelfPosition", "false", 300u, 0x104CDFAAu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "RequiredFiringObjectStatus", nullptr, 136u, 0x4D73E039u, 0xA7BD811Eu, WeaponValue::StatusPointer, WeaponToken::Pointer, "" },
    { "ForbiddenFiringObjectStatus", nullptr, 140u, 0x600D69FBu, 0xA7BD811Eu, WeaponValue::StatusPointer, WeaponToken::Pointer, "" },
    { "CheckStatusFlagsInRangeChecks", "true", 301u, 0x6A4BDF67u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ProjectileSelf", "false", 302u, 0x9BDB3BC4u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "MeleeWeapon", "false", 303u, 0x3F081EA7u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ChaseWeapon", "false", 304u, 0x0A8779F5u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "LeechRangeWeapon", "false", 305u, 0x844F87FAu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "HitStoredTarget", "false", 306u, 0x8DF650A3u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "CapableOfFollowingWaypoints", "false", 307u, 0x252225FAu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ShowsAmmoPips", "false", 308u, 0xB1598108u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "AllowAttackGarrisonedBldgs", "false", 309u, 0x1F46C311u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "PlayFXWhenStealthed", "false", 310u, 0x5B25DE0Du, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ContinueAttackRange", "0", 144u, 0x4131B666u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "SuspendFXDelaySeconds", "0s", 148u, 0x1C66E1FCu, 0x998669E8u, WeaponValue::Time, WeaponToken::Member, "" },
    { "IsAimingWeapon", "false", 311u, 0x8566AEF8u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "NoVictimNeeded", "false", 312u, 0x4013983Bu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "HitPercentage", "100%", 152u, 0xFFB6DE81u, 0x645864F3u, WeaponValue::Percentage, WeaponToken::Member, "" },
    { "HitPassengerPercentage", "100%", 156u, 0xA09C5D99u, 0x645864F3u, WeaponValue::Percentage, WeaponToken::Member, "" },
    { "PassengerProportionalAttack", "false", 313u, 0xFBB273E4u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "HealthProportionalResolution", "0", 160u, 0x87D285A7u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "MaxAttackPassengers", nullptr, 164u, 0xAF845003u, 0x23E650B8u, WeaponValue::Integer, WeaponToken::Member, "" },
    { "FinishAttackOnceStarted", "false", 314u, 0x447F0E34u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "RestrictedHeightRange", "0.0", 168u, 0xCA7889B7u, 0x645864F3u, WeaponValue::Float, WeaponToken::Member, "" },
    { "CannotTargetCastleVictims", "false", 315u, 0x7D34A061u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "RequireFollowThru", "false", 316u, 0x5031D40Bu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ShareTimers", "false", 317u, 0x5BE81F7Cu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ShouldPlayUnderAttackEvaEvent", "true", 318u, 0x68DD1107u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "InstantLoadClipOnActivate", "false", 319u, 0x35890A12u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "Flags", "NONE", 172u, 0xF345058Au, 0x47FC1D9Au, WeaponValue::Bits, WeaponToken::Member, "WeaponFlagsType" },
    { "LockWhenUsing", "false", 320u, 0x62679E18u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "BombardType", "false", 321u, 0xD51F15E0u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "UseInnateAttributes", "false", 322u, 0xAFB534D7u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "PreAttackType", "PER_SHOT", 176u, 0x15B123C5u, 0x826ACC69u, WeaponValue::Enum, WeaponToken::Member, "WeaponPrefireType" },
    { "ReAcquireDetailType", "PER_SHOT", 180u, 0xEF029607u, 0x3061BA53u, WeaponValue::Enum, WeaponToken::Member, "WeaponReAcquireDetailType" },
    { "AutoReloadsClip", "AUTO", 184u, 0x2CF21F66u, 0x266EF25Au, WeaponValue::Enum, WeaponToken::Member, "WeaponReloadType" },
    { "SingleAmmoReloadedNotFullSound", "", 188u, 0xD9D3C0EEu, 0x63D7B468u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "ClipReloadedSound", "", 192u, 0xB1C14901u, 0x63D7B468u, WeaponValue::Reference, WeaponToken::Member, "" },
    { "RadiusDamageAffects", "ENEMIES", 196u, 0xE3731062u, 0x5208E1B9u, WeaponValue::Bits, WeaponToken::Member, "WeaponAffectsType" },
    { "FXTrigger", "NONE", 200u, 0xC4274C12u, 0x808B85BEu, WeaponValue::Enum, WeaponToken::Member, "FXTriggerType" },
    { "ProjectileCollidesWith", "STRUCTURES", 204u, 0x6E2FE0B6u, 0x11E28116u, WeaponValue::Bits, WeaponToken::Member, "WeaponCollideType" },
    { "RequiredAntiMask", "ANTI_GROUND ANTI_WATER ANTI_STRUCTURE", 208u, 0x475B437Du, 0xC8C69390u, WeaponValue::Bits, WeaponToken::Member, "WpnAntiT" },
    { "ForbiddenAntiMask", "", 212u, 0x4DD6D922u, 0xC8C69390u, WeaponValue::Bits, WeaponToken::Member, "WpnAntiT" },
    { "StopFiringOnCanBeInvisible", "false", 323u, 0xE1BD34FEu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ProjectileStreamName", nullptr, 216u, 0x8A3BDF92u, 0x148ED75Du, WeaponValue::AssetId, WeaponToken::Member, "" },
    { "ContactWeapon", "false", 324u, 0xF40F232Du, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "UseCenterForRangeCheck", "false", 325u, 0x4C86C5A2u, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "VirtualDamage", "NONE", 220u, 0xD297044Fu, 0x2AFA9122u, WeaponValue::Enum, WeaponToken::Member, "VirtualDamageType" },
    { "PreAttackWeapon", nullptr, 224u, 0x3BD61BC1u, 0xD046DD22u, WeaponValue::ReferencePointer, WeaponToken::Pointer, "" },
    { "RevealShroudOnFire", "false", 326u, 0x1BC662CCu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "ShouldPlayTargetDeadEvaEvent", "true", 327u, 0xD9EEFC1Fu, 0x77E3F4F7u, WeaponValue::Boolean, WeaponToken::Member, "" },
    { "OverrideVoiceAttackSound", nullptr, 228u, 0x7E74DFCCu, 0x1AE6B6B7u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "OverrideVoiceEnterStateAttackSound", nullptr, 232u, 0xAE8F7512u, 0x1AE6B6B7u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "PreAttackDelay", nullptr, 236u, 0xCF9D76E0u, 0x9CED3BFAu, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "FiringDuration", nullptr, 240u, 0x508A6678u, 0x9CED3BFAu, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "CoolDownDelayBetweenShots", nullptr, 244u, 0x51A6820Du, 0x9CED3BFAu, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "ClipReloadTime", nullptr, 248u, 0x0C7FF035u, 0x9CED3BFAu, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "WeaponAiHintInfo", nullptr, 252u, 0xD3A7C780u, 0xD3A7C780u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "Nuggets", nullptr, 256u, 0x5752BD3Cu, 0xA5C43252u, WeaponValue::Absent, WeaponToken::List, "" },
    { "SurpriseAttackObjectFilter", nullptr, 264u, 0xA64A0E8Fu, 0xE46EA3B2u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "CombinedAttackObjectFilter", nullptr, 268u, 0xAB96B610u, 0xE46EA3B2u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "HitStoredObjectFilter", nullptr, 272u, 0xC0F5DA4Cu, 0xE46EA3B2u, WeaponValue::Absent, WeaponToken::Pointer, "" },
    { "ScatterRadiusVsType", nullptr, 276u, 0xDBEA8EFDu, 0xE1EBA7C4u, WeaponValue::Absent, WeaponToken::List, "" },
    { "IncompatibleAttributeModifier", nullptr, 284u, 0xDF062E0Du, 0x09DDCFF3u, WeaponValue::Absent, WeaponToken::List, "" },
};
struct EnumEntry { const char* group; const char* name; U32 value; };
const EnumEntry enumEntries[] = {
    { "WeaponPrefireType", "PER_BURST", 2u },
    { "WeaponPrefireType", "PER_CLIP", 1u },
    { "WeaponPrefireType", "PER_POSITION", 4u },
    { "WeaponPrefireType", "PER_SHOT", 0u },
    { "WeaponPrefireType", "PER_TARGET", 3u },
    { "WeaponReAcquireDetailType", "PER_ATTACK", 3u },
    { "WeaponReAcquireDetailType", "PER_CLIP", 2u },
    { "WeaponReAcquireDetailType", "PER_SHOT", 1u },
    { "WeaponReAcquireDetailType", "POST_FIRE", 5u },
    { "WeaponReAcquireDetailType", "PRE_FIRE", 4u },
    { "WeaponReAcquireDetailType", "PRE_SHOT", 0u },
    { "WeaponReloadType", "AUTO", 0u },
    { "WeaponReloadType", "NONE", 1u },
    { "WeaponReloadType", "RETURN_TO_BASE", 2u },
    { "WeaponAffectsType", "ALLIES", 1u },
    { "WeaponAffectsType", "ENEMIES", 2u },
    { "WeaponAffectsType", "MINES", 9u },
    { "WeaponAffectsType", "NEUTRALS", 3u },
    { "WeaponAffectsType", "NOT_AIRBORNE", 6u },
    { "WeaponAffectsType", "NOT_SIMILAR", 5u },
    { "WeaponAffectsType", "PROJECTILES", 7u },
    { "WeaponAffectsType", "SAME_HEIGHT_ONLY", 8u },
    { "WeaponAffectsType", "SELF", 0u },
    { "WeaponAffectsType", "SUICIDE", 4u },
    { "WeaponFlagsType", "ATTACK_NEEDS_LINE_OF_SIGHT", 4u },
    { "WeaponFlagsType", "CRUSH_VEHICLE", 8u },
    { "WeaponFlagsType", "FORCE_EMPTY_ENTIRE_CLIP", 9u },
    { "WeaponFlagsType", "IGNORE_ENCLOSURE_CHECK", 11u },
    { "WeaponFlagsType", "IGNORE_TARGET_AS_OBSTACLE", 6u },
    { "WeaponFlagsType", "IGNORE_WALL_RELATIONSHIP", 10u },
    { "WeaponFlagsType", "LENGTH_SCATTER", 3u },
    { "WeaponFlagsType", "NONE", 0u },
    { "WeaponFlagsType", "NOT_ATTRACTED_BY_MAGNETS", 7u },
    { "WeaponFlagsType", "ORTHOGONAL_SCATTER", 2u },
    { "WeaponFlagsType", "RELOAD_WHEN_ATTACK_STOPS", 5u },
    { "WeaponFlagsType", "SYNC_AMMO_ON_ACTIVATE", 1u },
    { "WeaponCollideType", "ALLIES", 0u },
    { "WeaponCollideType", "BALLISTIC_MISSILES", 8u },
    { "WeaponCollideType", "CONTROLLED_STRUCTURES", 9u },
    { "WeaponCollideType", "ENEMIES", 1u },
    { "WeaponCollideType", "MONSTERS", 10u },
    { "WeaponCollideType", "NEUTRAL", 2u },
    { "WeaponCollideType", "PROJECTILES", 5u },
    { "WeaponCollideType", "SHRUBBERY", 4u },
    { "WeaponCollideType", "SMALL_MISSILES", 7u },
    { "WeaponCollideType", "STRUCTURES", 3u },
    { "WeaponCollideType", "WALLS", 6u },
    { "WpnAntiT", "ANTI_AIRBORNE_INFANTRY", 7u },
    { "WpnAntiT", "ANTI_AIRBORNE_VEHICLE", 6u },
    { "WpnAntiT", "ANTI_BALLISTIC_MISSILE", 8u },
    { "WpnAntiT", "ANTI_GROUND", 0u },
    { "WpnAntiT", "ANTI_INFANTRY", 12u },
    { "WpnAntiT", "ANTI_LIFTED_GROUND_UNIT", 11u },
    { "WpnAntiT", "ANTI_MINE", 9u },
    { "WpnAntiT", "ANTI_PARACHUTE", 10u },
    { "WpnAntiT", "ANTI_PROJECTILE", 4u },
    { "WpnAntiT", "ANTI_SMALL_MISSILE", 5u },
    { "WpnAntiT", "ANTI_STRUCTURE", 3u },
    { "WpnAntiT", "ANTI_SUBMERGED", 2u },
    { "WpnAntiT", "ANTI_VEHICLE", 13u },
    { "WpnAntiT", "ANTI_WATER", 1u },
    { "VirtualDamageType", "NONE", 0u },
    { "VirtualDamageType", "SHARE", 2u },
    { "VirtualDamageType", "SOLO", 1u },
    { "FXTriggerType", "CATAPULT_ROCK", 1u },
    { "FXTriggerType", "NONE", 0u },
    { "FXTriggerType", "TREBUCHET_ROCK", 2u },
};
U32 Lookup(const std::string& group, const std::string& text) {
    for (const auto& entry : enumEntries)
        if (group == entry.group && text == entry.name) return entry.value;
    if (group == "WeaponPrefireType") return 2u;
    if (group == "WeaponReAcquireDetailType") return 3u;
    if (group == "WeaponReloadType") return 0u;
    if (group == "WeaponAffectsType") return 1u;
    if (group == "WeaponFlagsType") return 4u;
    if (group == "WeaponCollideType") return 0u;
    if (group == "WpnAntiT") return 7u;
    if (group == "VirtualDamageType") return 0u;
    if (group == "FXTriggerType") return 1u;
    throw std::logic_error("Unknown recovered enum group");
}
struct BitsetGroup { std::vector<EnumNameValue> names; int unknown; };
using BitsetGroups=std::map<std::string,BitsetGroup>;
INIT_ONCE BitsetGroupsOnce=INIT_ONCE_STATIC_INIT;
const BitsetGroups* bitsetGroups=nullptr;
BOOL CALLBACK InitializeBitsetGroups(PINIT_ONCE,PVOID,PVOID*) {
    try {
        std::unique_ptr<BitsetGroups> groups(new BitsetGroups);
        for(const auto& entry:enumEntries)
            (*groups)[entry.group].names.push_back({entry.name,static_cast<int>(entry.value)});
        for(auto& entry:*groups) entry.second.unknown=static_cast<int>(Lookup(entry.first,""));
        // Native process-lifetime tables must not register CLR shutdown destructors.
        bitsetGroups=groups.release();
        return TRUE;
    } catch(...) {
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return FALSE;
    }
}
U32 Bits(const std::string& group, const std::string& text) {
    if(!InitOnceExecuteOnce(&BitsetGroupsOnce,InitializeBitsetGroups,nullptr,nullptr))
        throw std::runtime_error("Weapon bitset table initialization failed.");
    const auto& values=bitsetGroups->at(group);
    U32 result=0;
    MarshalEnumBitset(text,values.names.data(),values.names.size(),values.unknown,32,&result,true);
    return result;
}

void Swap(void* at) {
    auto p = static_cast<unsigned char*>(at);
    std::swap(p[0], p[3]); std::swap(p[1], p[2]);
}
bool WeaponScalarDefault(WeaponValue value) {
    return value==WeaponValue::AssetId || value==WeaponValue::Boolean || value==WeaponValue::Integer ||
        value==WeaponValue::Enum || value==WeaponValue::Bits || value==WeaponValue::Float ||
        value==WeaponValue::Angle || value==WeaponValue::Time || value==WeaponValue::Percentage;
}
U32 DecodeWeaponScalar(const WeaponField& field,const std::string& text) {
    float number=0;U32 word=0;
    switch(field.value) {
    case WeaponValue::AssetId:return HashAssetId(text);
    case WeaponValue::Boolean:return ParseBoolean(text)?1:0;
    case WeaponValue::Integer:return static_cast<U32>(ParseSigned(text));
    case WeaponValue::Enum:return Lookup(field.enumGroup,text);
    case WeaponValue::Bits:return Bits(field.enumGroup,text);
    case WeaponValue::Float:number=ParseFloat(text);break;
    case WeaponValue::Angle:number=InitializeAngle(text.c_str());break;
    case WeaponValue::Time:number=InitializeTime(text.c_str());break;
    case WeaponValue::Percentage:number=ParsePercentage(text);break;
    default:throw std::logic_error("Expected scalar Weapon field");
    }
    std::memcpy(&word,&number,4);return word;
}
struct WeaponRootDefaults {
    unsigned char bytes[328]{};
    WeaponRootDefaults() {
        for(const auto& field:fields)if(field.fallback && WeaponScalarDefault(field.value)) {
            U32 word=DecodeWeaponScalar(field,std::string(field.fallback));
            std::memcpy(bytes+field.offset,&word,field.value==WeaponValue::Boolean?1:4);
        }
    }
};
static_assert(std::is_trivially_destructible<WeaponRootDefaults>::value,"Root defaults must not own exit-time state");
const WeaponRootDefaults rootDefaults;
struct Procedure { U32 name, type; void* offset; };
static_assert(sizeof(Procedure) == 12, "Weapon serializer requires x86");
struct Root { unsigned char bytes[328]; };
}
const WeaponField* WeaponFields() { return fields; }
unsigned WeaponFieldCount() { return sizeof(fields) / sizeof(fields[0]); }
Relo::Chunk CompileWeapon(const AttributeMap& attributes,
    const std::map<std::string, WeaponDurationInput>& durations,
    const std::map<std::string, WeaponOptionalInput>& optionalBlocks,
    const std::vector<std::string>& incompatibleModifiers,
    const std::map<std::string, ObjectFilterInput>& filters,
    const std::vector<WeaponScatterInput>& scatter,
    const std::vector<WeaponNuggetInput>& nuggets, bool bigEndian) {
    const char* durationNames[] = { "PreAttackDelay", "FiringDuration", "CoolDownDelayBetweenShots", "ClipReloadTime" };

    Relo::Tracker data, tokens;
    Root* root = nullptr;
    data.Push(reinterpret_cast<void**>(&root), sizeof(Root), 1);
    std::memcpy(root->bytes,rootDefaults.bytes,sizeof(Root));
    std::map<U32, void*> referenceBlocks;
    for(const auto& field:fields) {
        if(field.value==WeaponValue::Absent)continue;
        auto found=attributes.find(field.name);
        if(found==attributes.end() && (!field.fallback || WeaponScalarDefault(field.value)))continue;
        if(found!=attributes.end() && field.fallback && WeaponScalarDefault(field.value) && found->second==field.fallback)continue;
        std::string fallback;if(found==attributes.end())fallback=field.fallback;
        const std::string& text=found==attributes.end()?fallback:found->second;
        auto at=root->bytes+field.offset;
        if(WeaponScalarDefault(field.value)) {
            U32 word=DecodeWeaponScalar(field,text);
            std::memcpy(at,&word,field.value==WeaponValue::Boolean?1:4);continue;
        }
        switch(field.value) {
        case WeaponValue::String:MarshalString(data,at,text);break;
        case WeaponValue::Reference:MarshalReference(data,at,text);break;
        case WeaponValue::ReferencePointer: {
            auto block=data.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(data,block,text);
            referenceBlocks[field.offset]=block;data.Pop();break;
        }
        case WeaponValue::StatusPointer: {
            auto block=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),28,1));MarshalObjectStatus(text,block);
            referenceBlocks[field.offset]=block;data.Pop();break;
        }
        default:throw std::logic_error("Unimplemented Weapon value");
        }
    }
    struct SoundOrEvaEvent { void* sound; U32 eva; };
    SoundOrEvaEvent* soundBlocks[2] = {};
    void* soundReferences[2] = {};
    const char* soundNames[] = { "OverrideVoiceAttackSound", "OverrideVoiceEnterStateAttackSound" };
    for (unsigned i = 0; i < 2; ++i) {
        auto found = optionalBlocks.find(soundNames[i]);
        if (found == optionalBlocks.end()) continue;
        auto block = static_cast<SoundOrEvaEvent*>(data.Push(reinterpret_cast<void**>(root->bytes + 228 + 4*i), 8, 1));
        auto sound = found->second.find("Sound");
        if (sound != found->second.end()) {
            soundReferences[i] = data.Push(&block->sound, 4, 1);
            MarshalReference(data, soundReferences[i], sound->second); data.Pop();
        }
        auto eva = found->second.find("EvaEvent");
        if (eva != found->second.end()) MarshalReference(data, &block->eva, eva->second);
        soundBlocks[i] = block; data.Pop();
    }
    struct RangeDuration { float minSeconds, maxSeconds; };
    RangeDuration* durationBlocks[4] = {};
    for (unsigned i = 0; i < 4; ++i) {
        auto found = durations.find(durationNames[i]);
        if (found == durations.end()) continue;
        auto block = static_cast<RangeDuration*>(data.Push(
            reinterpret_cast<void**>(root->bytes + 236 + 4*i), sizeof(RangeDuration), 1));
        block->minSeconds = InitializeTime(found->second.minSeconds.c_str());
        block->maxSeconds = InitializeTime(found->second.maxSeconds.c_str());
        durationBlocks[i] = block;
        data.Pop();
    }
    struct AiHint { void* maxSpeed; U32 warhead; unsigned char antiGarrison, longLock, padding[2]; };
    AiHint* ai = nullptr;
    float* maxSpeed = nullptr;
    auto hint = optionalBlocks.find("WeaponAiHintInfo");
    if (hint != optionalBlocks.end()) {
        ai = static_cast<AiHint*>(data.Push(reinterpret_cast<void**>(root->bytes+252), 12, 1));
        const auto& attrs = hint->second;
        auto anti = attrs.find("IsAntiGarrisonWeapon");
        ai->antiGarrison = anti != attrs.end() && ParseBoolean(anti->second);
        auto lock = attrs.find("UseLongLockOnTimeCode");
        ai->longLock = lock != attrs.end() && ParseBoolean(lock->second);
        auto speed = attrs.find("MaxSpeedOfTarget");
        if (speed != attrs.end()) { maxSpeed = static_cast<float*>(data.Push(&ai->maxSpeed, 4, 1)); *maxSpeed = ParseFloat(speed->second); data.Pop(); }
        auto warhead = attrs.find("UseAsWarheadForDamageCalculations");
        if (warhead != attrs.end()) MarshalReference(data, &ai->warhead, warhead->second);
        data.Pop();
    }
    MarshalWeaponNuggets(nuggets,reinterpret_cast<WeaponNuggetList*>(root->bytes+256),data);
    ObjectFilter* filterBlocks[3] = {};
    const char* filterNames[] = { "SurpriseAttackObjectFilter", "CombinedAttackObjectFilter", "HitStoredObjectFilter" };
    for (unsigned i = 0; i < 3; ++i) {
        auto found = filters.find(filterNames[i]);
        if (found == filters.end()) continue;
        auto block = static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(root->bytes+264+4*i), 112, 1));
        MarshalObjectFilter(found->second, block, data); filterBlocks[i] = block; data.Pop();
    }
    ;
    // Scatter elements are inline 176-byte structures; absent Filter stays zero.
    struct ScatterRadius { float radius; U32 conditions[15]; ObjectFilter filter; };
    static_assert(sizeof(ScatterRadius) == 176, "Original ScatterRadiusType layout");
    ScatterRadius* scatterBlocks = nullptr;
    *reinterpret_cast<U32*>(root->bytes+276) = static_cast<U32>(scatter.size());
    if (!scatter.empty()) {
        scatterBlocks = static_cast<ScatterRadius*>(data.Push(reinterpret_cast<void**>(root->bytes+280), 176, static_cast<U32>(scatter.size())));
        for (unsigned i = 0; i < scatter.size(); ++i) {
            scatterBlocks[i].radius = ParseFloat(scatter[i].radius);
            MarshalModelCondition(scatter[i].modelConditions, scatterBlocks[i].conditions);
            if (scatter[i].hasFilter) MarshalObjectFilter(scatter[i].filter, &scatterBlocks[i].filter, data);
        }
        data.Pop();
    }
    // Final Marshal allocation in the original: repeated prepared references.
    U32* modifiers = nullptr;
    *reinterpret_cast<U32*>(root->bytes+284) = static_cast<U32>(incompatibleModifiers.size());
    if (!incompatibleModifiers.empty()) {
        modifiers = static_cast<U32*>(data.Push(reinterpret_cast<void**>(root->bytes+288), 4, static_cast<U32>(incompatibleModifiers.size())));
        for (unsigned i = 0; i < incompatibleModifiers.size(); ++i) MarshalReference(data, modifiers+i, incompatibleModifiers[i]);
        data.Pop();
    }
    Procedure* table = nullptr;
    tokens.Push(reinterpret_cast<void**>(&table), sizeof(Procedure), 105);
    for (unsigned i = 0; i < WeaponFieldCount(); ++i) {
        const auto& field = fields[i];
        table[i].name = field.nameHash; table[i].type = field.typeHash;
        if (field.offset >= 236 && field.offset <= 248 && durationBlocks[(field.offset - 236)/4]) {
            auto block = durationBlocks[(field.offset - 236)/4];
            auto nested = static_cast<Procedure*>(tokens.Push(&table[i].offset, sizeof(Procedure), 3));
            nested[0].name = 947845537u; nested[0].type = 2575722984u;
            nested[1].name = 895757295u; nested[1].type = 2575722984u;
            tokens.BookmarkMemberOffset(&block->minSeconds, &nested[0].offset);
            tokens.BookmarkMemberOffset(&block->maxSeconds, &nested[1].offset);
            tokens.Pop();
        } else if (field.offset >= 228 && field.offset <= 232 && soundBlocks[(field.offset-228)/4]) {
            unsigned index = (field.offset-228)/4;
            auto nested = static_cast<Procedure*>(tokens.Push(&table[i].offset, 12, 3));
            nested[0].name = 805758285u; nested[0].type = 1675080808u;
            nested[1].name = 357806494u; nested[1].type = 3303818161u;
            if (soundReferences[index]) tokens.BookmarkMemberOffset(soundReferences[index], &nested[0].offset);
            tokens.BookmarkMemberOffset(&soundBlocks[index]->eva, &nested[1].offset);
            tokens.Pop();
        } else if (field.offset == 252 && ai) {
            auto nested = static_cast<Procedure*>(tokens.Push(&table[i].offset, 12, 5));
            nested[0].name = 486840776u; nested[0].type = 2011428087u;
            nested[1].name = 4291171833u; nested[1].type = 1683514611u;
            nested[2].name = 1432037361u; nested[2].type = 2011428087u;
            nested[3].name = 3539729073u; nested[3].type = 3494305058u;
            tokens.BookmarkMemberOffset(&ai->antiGarrison, &nested[0].offset);
            if (maxSpeed) tokens.BookmarkMemberOffset(maxSpeed, &nested[1].offset);
            tokens.BookmarkMemberOffset(&ai->longLock, &nested[2].offset);
            tokens.BookmarkMemberOffset(&ai->warhead, &nested[3].offset);
            tokens.Pop();
        } else if (field.offset >= 264 && field.offset <= 272 && filterBlocks[(field.offset-264)/4]) {
            TokenizeObjectFilter(filterBlocks[(field.offset-264)/4], &table[i].offset, tokens);
        } else if ((field.value == WeaponValue::ReferencePointer || field.value == WeaponValue::StatusPointer)) {
            auto found = referenceBlocks.find(field.offset);
            if (found != referenceBlocks.end()) tokens.BookmarkMemberOffset(found->second, &table[i].offset);
        } else if (field.token == WeaponToken::Member) {
            tokens.BookmarkMemberOffset(root->bytes + field.offset, &table[i].offset);
        } else if (field.offset == 256) {
            TokenizeWeaponNuggets(reinterpret_cast<WeaponNuggetList*>(root->bytes+256),&table[i].offset,tokens);
        } else if (field.token == WeaponToken::List) {
            unsigned count = field.offset == 284 ? static_cast<unsigned>(incompatibleModifiers.size()) :
                field.offset == 276 ? static_cast<unsigned>(scatter.size()) : 0;
            Procedure* list = static_cast<Procedure*>(tokens.Push(&table[i].offset, sizeof(Procedure), count+1));
            list->name = 1820487795u; list->type = 1564774360u;
            tokens.BookmarkMemberOffset(root->bytes + field.offset, &list->offset);
            for (unsigned index = 0; index < count; ++index) {
                list[index+1].name = 2974916843u; list[index+1].type = 0;
                if (field.offset == 284) tokens.BookmarkMemberOffset(modifiers+index, &list[index+1].offset);
                else {
                    auto nested = static_cast<Procedure*>(tokens.Push(&list[index+1].offset, 12, 4));
                    nested[0].name = 1328662213u; nested[0].type = 1683514611u;
                    nested[1].name = 1022003432u; nested[1].type = 1742454729u;
                    nested[2].name = static_cast<U32>(-2055623642); nested[2].type = static_cast<U32>(-462511182);
                    tokens.BookmarkMemberOffset(&scatterBlocks[index].radius, &nested[0].offset);
                    tokens.BookmarkMemberOffset(scatterBlocks[index].conditions, &nested[1].offset);
                    // Filter tokenization is unconditional, even if Filter was absent.
                    TokenizeObjectFilter(&scatterBlocks[index].filter, &nested[2].offset, tokens);
                    tokens.Pop();
                }
            }
            tokens.Pop();
        }
        // Absent optional pointers have zero token offsets, as in the original.
    }
    tokens.Pop();
    if (bigEndian) {
        EndianWeaponNuggets(reinterpret_cast<WeaponNuggetList*>(root->bytes+256));
        // The nugget helper swaps its count; other root fields follow the original layout.
        for (unsigned offset = 4; offset < 292; offset += 4)
            if (offset != 256 && (offset < 236 || offset > 248)) Swap(root->bytes + offset);
        for (const auto& block : referenceBlocks) if (block.first == 136 || block.first == 140)
            for (unsigned i = 0; i < 7; ++i) Swap(static_cast<U32*>(block.second)+i);
        for (auto block : filterBlocks) if (block) EndianObjectFilter(block);
        for (unsigned i = 0; i < scatter.size(); ++i) {
            Swap(&scatterBlocks[i].radius);
            for (unsigned word = 0; word < 15; ++word) Swap(scatterBlocks[i].conditions+word);
            EndianObjectFilter(&scatterBlocks[i].filter);
        }
        if (maxSpeed) Swap(maxSpeed);
        for (auto block : durationBlocks) if (block) { Swap(&block->minSeconds); Swap(&block->maxSeconds); }
    }
    auto result=tokens.MakeTokenized(data,bigEndian);
    result.imports.clear();return result;
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
