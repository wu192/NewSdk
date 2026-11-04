#include "ObjectFilter.h"
#include "EnumBitset.h"
#include "ResourceMarshaling.h"
#include <algorithm>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push, off)
#endif
namespace Recovered { namespace Native {
namespace {
using U32 = std::uint32_t;
struct Procedure { U32 name, type; void* offset; };
static_assert(sizeof(ObjectFilter) == 112, "ObjectFilter requires x86 layout");
const EnumNameValue ObjectFilterRule[] = {
    { "ALL", 1 },
    { "ANY", 2 },
    { "NONE", 3 },
    { "UNPARSED", 0 },
};
const EnumNameValue ObjectFilterAlignment[] = {
    { "EVIL", 1 },
    { "GOOD", 2 },
    { "NONE", 0 },
};
const EnumNameValue ObjectFilterRelationship[] = {
    { "ALLIES", 0 },
    { "ENEMIES", 1 },
    { "NEUTRAL", 2 },
    { "SAME_PLAYER", 3 },
};
const EnumNameValue KindOfType[] = {
    { "ACTIVATE_AFTER_UNPACK", 149 },
    { "AIRCRAFT", 14 },
    { "AIRCRAFT_PATH_AROUND", 66 },
    { "AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED", 251 },
    { "AIRFIELD", 36 },
    { "AI_CAN_UNPACK_NEAR_CONYARD", 143 },
    { "AI_NEEDS_PLAYER_POWER_UPDATE", 137 },
    { "ALLY_TARGETABLE", 3 },
    { "ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES", 187 },
    { "ALWAYS_SELECTABLE", 59 },
    { "ALWAYS_SHOW_HOUSE_COLOR", 171 },
    { "ALWAYS_VISIBLE", 54 },
    { "ALWAYS_VISIBLE_IN_RADAR", 274 },
    { "AMPHIBIOUS", 211 },
    { "ANTI_GARRISON", 242 },
    { "ARMY_SUMMARY", 126 },
    { "ASSAULT_AIRCRAFT", 239 },
    { "ATTACH_ATTACK", 275 },
    { "ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED", 253 },
    { "AUTO_ACQUIRABLE_BY_AI", 230 },
    { "AUTO_RALLYPOINT", 69 },
    { "BALLISTIC_MISSILE", 75 },
    { "BANNER", 157 },
    { "BASE_FOUNDATION", 101 },
    { "BASE_SITE", 118 },
    { "BEACON", 247 },
    { "BEAM_TARGET", 111 },
    { "BEAM_TARGET_REFLECTOR", 112 },
    { "BLOCKING_GATE", 135 },
    { "BOMBER_AIRCRAFT", 240 },
    { "BRIDGE", 23 },
    { "BRIDGE_ENDCAP", 256 },
    { "BRIDGE_GATEHOUSE", 257 },
    { "BRIDGE_SEGMENT", 219 },
    { "BROADCASTS_INVISIBILITY", 144 },
    { "BUFF", 139 },
    { "BUILD_FOR_FREE", 154 },
    { "CANNOT_BE_DETECTED", 229 },
    { "CANNOT_BUILD_NEAR_SUPPLIES", 85 },
    { "CANNOT_HIJACK", 270 },
    { "CANNOT_RETALIATE", 164 },
    { "CANNOT_STOP_ON_WATER", 195 },
    { "CANT_TOGGLE_POWER", 24 },
    { "CAN_ATTACK", 5 },
    { "CAN_ATTACK_STEALTHED", 227 },
    { "CAN_ATTACK_WALLS", 145 },
    { "CAN_BE_CAPTURED", 104 },
    { "CAN_BE_FAVORITE_UNIT", 258 },
    { "CAN_BE_REPULSED", 46 },
    { "CAN_BUILD_ON_DEEP_WATER", 265 },
    { "CAN_BUILD_ON_WATER", 264 },
    { "CAN_CAPTURE", 70 },
    { "CAN_CAST_REFLECTIONS", 7 },
    { "CAN_CLIMB_WALLS", 174 },
    { "CAN_HEAL_ALLIES", 261 },
    { "CAN_NOT_BUILD_ON_LAND", 263 },
    { "CAN_PLACE_CHARGE", 57 },
    { "CAN_PLACE_MANIPULATOR", 83 },
    { "CAN_RAPPEL", 44 },
    { "CAN_SEE_THROUGH_STRUCTURE", 74 },
    { "CAN_SHOOT_OVER_WALLS", 208 },
    { "CAN_SKIP_SHADOW", 190 },
    { "CAN_TOPPLE", 188 },
    { "CAPTURABLE", 50 },
    { "CAPTUREFLAG", 194 },
    { "CARPET_BOMBER", 282 },
    { "CASTLE_CENTER", 19 },
    { "CASTLE_KEEP", 29 },
    { "CAVALRY", 11 },
    { "CHUNK_VENDOR", 130 },
    { "CIVILIAN_BUILDING", 201 },
    { "CIVILIAN_UNIT", 127 },
    { "CLASSIFY_ENTIRE_FOOTPRINT", 58 },
    { "CLEARED_BY_BUILD", 52 },
    { "CLICK_THROUGH", 76 },
    { "COMBO_HORDE", 107 },
    { "COMMANDCENTER", 18 },
    { "COMMANDO", 47 },
    { "CONSTRUCTION_YARD", 207 },
    { "COVER", 217 },
    { "CRANE", 136 },
    { "CRATE", 49 },
    { "CREEP", 165 },
    { "CRUSHABLE_BUILDING", 271 },
    { "CRUSHABLE_OBSTACLE", 231 },
    { "CRUSH_CRUSHABLE_BUILDINGS", 272 },
    { "CYCLE_SELECTION", 189 },
    { "DEBRIS", 280 },
    { "DEFENSIVE_WALL", 61 },
    { "DEFLECT_BY_SPECIAL_POWER", 99 },
    { "DEPLOYED_MINE", 163 },
    { "DEPRECATED", 122 },
    { "DISGUISER", 88 },
    { "DONT_AUTO_CRUSH_INFANTRY", 91 },
    { "DONT_DESTROY_IF_ON_BRIDGE", 281 },
    { "DONT_HIDE_IF_FOGGED", 203 },
    { "DONT_SUBMERGE_BY_WAVES", 84 },
    { "DONT_UNATTACH_WHEN_HEALING", 276 },
    { "DONT_USE_CANCEL_BUILD_BUTTON", 200 },
    { "DOZER", 16 },
    { "DOZER_FACTORY", 184 },
    { "DO_NOT_CLASSIFY", 147 },
    { "DO_NOT_PICK_ME_WHEN_BUILDING", 168 },
    { "DRAWABLE_ONLY", 37 },
    { "DRONE", 73 },
    { "ECONOMY_STRUCTURE", 186 },
    { "EGG", 245 },
    { "ENCLOSURE", 268 },
    { "ENGINEER", 243 },
    { "ENVIRONMENT", 98 },
    { "EXPANSION_PAD", 210 },
    { "EXPANSION_UNIT", 244 },
    { "FIGHTER_AIRCRAFT", 241 },
    { "FORCEATTACKABLE", 68 },
    { "FS_AIR_FIELD", 223 },
    { "FS_BARRACKS", 221 },
    { "FS_BASE_DEFENSE", 64 },
    { "FS_CASH_PRODUCER", 133 },
    { "FS_FACTORY", 63 },
    { "FS_MONEY_STORAGE", 213 },
    { "FS_POWER", 62 },
    { "FS_RADAR", 222 },
    { "FS_TECHNOLOGY", 65 },
    { "FS_TECH_CENTER", 224 },
    { "FS_WAR_FACTORY", 220 },
    { "GARRISON", 116 },
    { "GARRISONABLE_UNTIL_DESTROYED", 79 },
    { "GRABBABLE", 206 },
    { "GRAB_AND_DROP", 140 },
    { "GRAB_AND_KILL", 96 },
    { "GRANTS_VETERANCY", 273 },
    { "HARVESTABLE", 205 },
    { "HARVESTER", 17 },
    { "HAS_HEALTH_BAR", 161 },
    { "HEAL_PAD", 33 },
    { "HEAVY_MELEE_HITTER", 202 },
    { "HIDE_IF_FOGGED", 170 },
    { "HORDE", 106 },
    { "HUGE_VEHICLE", 15 },
    { "HULK", 82 },
    { "HUSK", 248 },
    { "IGNORED_IN_FINDPOSITIONAROUND", 105 },
    { "IGNORED_IN_GUI", 48 },
    { "IGNORES_SELECT_ALL", 90 },
    { "IGNORE_CAN_NOT_BUILD", 234 },
    { "IGNORE_FORCE_MOVE", 249 },
    { "IGNORE_FOR_EVA_SPEECH_POSITION", 155 },
    { "IGNORE_FOR_VICTORY", 146 },
    { "IGNORE_IN_AI_HUNT_TACTIC", 162 },
    { "IMMOBILE", 4 },
    { "IMMUNE_TO_CAPTURE", 81 },
    { "IMMUNE_TO_CYCLONIC_SHOCKWAVE", 228 },
    { "INERT", 89 },
    { "INERT_SHROUD_REVEALER", 119 },
    { "INFANTRY", 10 },
    { "INFILTRATOR", 218 },
    { "KEEP_CLASSIFIED_WHEN_DEAD", 129 },
    { "LARGE_RECTANGLE_PATHFIND", 176 },
    { "LEAVE_PARKING_ON_BUILD_COMPLETE", 267 },
    { "LIMITED_PRODUCTION_AIRCRAFT", 266 },
    { "LINE_OF_SIGHT_IGNORES_BUILDINGS", 250 },
    { "LINKED_TO_FLAG", 51 },
    { "LOW_OVERLAPPABLE", 67 },
    { "MACHINE", 13 },
    { "MCV", 260 },
    { "MELEE_HORDE", 117 },
    { "MINE", 56 },
    { "MINE_TRIGGER", 138 },
    { "MONSTER", 12 },
    { "MOVE_FOR_NOONE", 172 },
    { "MOVE_ONLY", 132 },
    { "MP_COUNT_FOR_VICTORY", 38 },
    { "NEED_BASE_FOUNDATION", 102 },
    { "NEUTRAL_TECH", 232 },
    { "NEVER_CULL_FOR_MP", 199 },
    { "NONOCCLUDING", 108 },
    { "NOTIFY_OF_PREATTACK", 115 },
    { "NOT_AUTOACQUIRABLE", 128 },
    { "NOT_SELLABLE", 151 },
    { "NO_BASE_CAPTURE", 125 },
    { "NO_COLLIDE", 31 },
    { "NO_FORMATION_MOVEMENT", 124 },
    { "NO_FREEWILL_ENTER", 109 },
    { "NO_GARRISON", 28 },
    { "NO_HEAL_ICON", 43 },
    { "NO_SHADOW", 175 },
    { "OBSTACLE", 0 },
    { "OCL_BIT", 120 },
    { "OPTIMIZED_PROP", 97 },
    { "OPTIMIZED_SOUND", 182 },
    { "ORE_NODE", 159 },
    { "ORIENTS_TO_CAMERA", 198 },
    { "OUTPOST", 246 },
    { "PARACHUTABLE", 45 },
    { "PARACHUTE", 78 },
    { "PASS_EXPERIENCE_TO_CONTAINED", 183 },
    { "PASS_EXPERIENCE_TO_CONTAINER", 196 },
    { "PASS_EXPERIENCE_TO_PRODUCER", 209 },
    { "PASS_EXPERIENCE_TO_SLAVER", 160 },
    { "PATH_THROUGH_EACH_OTHER", 114 },
    { "PATH_THROUGH_INFANTRY", 123 },
    { "PORT", 178 },
    { "POWERED", 71 },
    { "POWERED_POWERS_ONLY", 166 },
    { "PRELOAD", 27 },
    { "PRODUCED_AT_HELIPAD", 72 },
    { "PROJECTILE", 26 },
    { "PROJECT_BUILDABILITY_FOR_ALLIES", 262 },
    { "RADAR_SCRAMBLER", 269 },
    { "REACT_WHEN_SELECTED", 103 },
    { "REBUILD_HOLE", 39 },
    { "REFINERY", 131 },
    { "REPAIR_PAD", 32 },
    { "RESIST_EMP", 197 },
    { "REVEAL_TO_ALL", 87 },
    { "ROCK", 94 },
    { "ROCK_VENDOR", 134 },
    { "SALVAGER", 20 },
    { "SCALEABLE_WALL", 191 },
    { "SCARY", 142 },
    { "SCORE", 40 },
    { "SCORE_CREATE", 41 },
    { "SCORE_DESTROY", 42 },
    { "SELECTABLE", 1 },
    { "SHADOW_ULTRA_HIGH_ONLY", 30 },
    { "SHIP", 181 },
    { "SHOW_BEHIND_OCCLUDERS", 212 },
    { "SHRUBBERY", 8 },
    { "SIEGE_TOWER", 92 },
    { "SIEGE_WEAPON", 25 },
    { "SIMPLE_OBJECT_PROP", 180 },
    { "SKIP_DEFECT_IF_UNCOMBINED", 254 },
    { "SKIP_IDLE_WHEN_CAPTURED", 255 },
    { "SKIRMISH_AI_ATTEMPT_TO_DODGE", 238 },
    { "SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME", 80 },
    { "SKIRMISH_AI_DONT_CONSIDER_THREAT", 235 },
    { "SKIRMISH_AI_DONT_GARRISON", 236 },
    { "SKIRMISH_AI_DONT_MULTI_TARGET", 150 },
    { "SKIRMISH_AI_DONT_REPAIR", 237 },
    { "SKYBOX", 192 },
    { "SLAVE_OWNER", 259 },
    { "SMALL_MISSILE", 53 },
    { "SNIPER", 252 },
    { "SPELL_BOOK", 121 },
    { "STEALTH_GARRISON", 34 },
    { "STICK_TO_TERRAIN_SLOPE", 6 },
    { "STRUCTURE", 9 },
    { "SUBMARINE", 177 },
    { "SUMMONED", 169 },
    { "SUPER_WEAPON", 233 },
    { "SUPPLY_GATHERING_CENTER", 35 },
    { "SUPPLY_SOURCE", 86 },
    { "SUPPLY_SOURCE_ON_PREVIEW", 77 },
    { "SURPRISE_ATTACKER", 216 },
    { "T1_UNIT", 277 },
    { "T2_UNIT", 278 },
    { "T3_UNIT", 279 },
    { "TACTICAL_MARKER", 113 },
    { "TARGETABLE", 2 },
    { "TERRAIN_LIKE_GROUND_OBJECT", 283 },
    { "THREAT_FINDER", 185 },
    { "THROWN_OBJECT", 95 },
    { "TIBERIUM", 226 },
    { "TIBERIUM_BASED", 225 },
    { "TIBERIUM_FIELD", 110 },
    { "TIME_BOMB", 93 },
    { "TRANSFORMER", 141 },
    { "TRANSPORT", 22 },
    { "UNATTACKABLE", 55 },
    { "UNIQUE_UNIT", 215 },
    { "UNPACKS_INTO_BUILDING", 158 },
    { "VALID_TARGET_FOR_C4", 214 },
    { "VEHICLE", 204 },
    { "VITAL_FOR_BASE_SURVIVAL", 167 },
    { "WALK_ON_TOP_OF_WALL", 60 },
    { "WALL_GATE", 193 },
    { "WALL_HUB", 153 },
    { "WALL_PIECE", 152 },
    { "WALL_SEGMENT", 179 },
    { "WALL_UPGRADE", 148 },
    { "WB_DISPLAY_SCRIPT_NAME", 173 },
    { "WEAPON_SALVAGER", 21 },
    { "WORKING_PASSENGER", 100 },
    { "WORLDBUILDER_SNAP_TO_GRID", 156 },
};
U32 Enum(const std::string& text, const EnumNameValue* names, unsigned count, U32 fallback) {
    for (unsigned i = 0; i < count; ++i) if (text == names[i].name) return static_cast<U32>(names[i].value);
    return fallback;
}
void Swap(void* at) { auto p = static_cast<unsigned char*>(at); std::swap(p[0],p[3]); std::swap(p[1],p[2]); }
void ThingList(const std::vector<std::string>& names, U32& count, U32*& words, Relo::Tracker& data) {
    count = static_cast<U32>(names.size());
    if (!count) return;
    data.Push(reinterpret_cast<void**>(&words), 4, count);
    for (unsigned i = 0; i < count; ++i) words[i] = HashAssetId(names[i]);
    data.Pop();
}
void TokenThings(U32& count, U32* words, void** table, Relo::Tracker& tokens) {
    auto entries = static_cast<Procedure*>(tokens.Push(table, 12, count+1));
    entries[0].name = 1820487795u; entries[0].type = 1564774360u;
    tokens.BookmarkMemberOffset(&count, &entries[0].offset);
    for (unsigned i = 0; i < count; ++i) {
        entries[i+1].name = 2974916843u;
        tokens.BookmarkMemberOffset(words+i, &entries[i+1].offset);
    }
    tokens.Pop();
}
}
void MarshalObjectFilter(const ObjectFilterInput& input, ObjectFilter* object, Relo::Tracker& data) {
    const auto& attrs = input.attributes;
    for (const auto& item : attrs) {
        const auto& name = item.first;

    }
    auto text = [&](const char* name, const char* fallback) { auto found = attrs.find(name); return found == attrs.end() ? std::string(fallback) : found->second; };
    if (attrs.count("id")) object->id = HashAssetId(text("id", ""));
    object->rule = Enum(text("Rule", "NONE"), ObjectFilterRule, 4, 1);
    object->alignment = Enum(text("Alignment", "NONE"), ObjectFilterAlignment, 3, 1);
    MarshalEnumBitset(text("Relationship", ""), ObjectFilterRelationship, 4, 0, 4, &object->relationship);
    if (attrs.count("Include")) MarshalEnumBitset(text("Include", ""), KindOfType, 284, 149, 284, object->include);
    if (attrs.count("Exclude")) MarshalEnumBitset(text("Exclude", ""), KindOfType, 284, 149, 284, object->exclude);
    if (attrs.count("StatusBitFlags")) { data.Push(reinterpret_cast<void**>(&object->status), 28, 1); MarshalObjectStatus(text("StatusBitFlags", ""), object->status); data.Pop(); }
    if (attrs.count("StatusBitFlagsExclude")) { data.Push(reinterpret_cast<void**>(&object->statusExclude), 28, 1); MarshalObjectStatus(text("StatusBitFlagsExclude", ""), object->statusExclude); data.Pop(); }
    ThingList(input.includeThings, object->includeCount, object->includeThings, data);
    ThingList(input.excludeThings, object->excludeCount, object->excludeThings, data);
}
void TokenizeObjectFilter(ObjectFilter* object, void** table, Relo::Tracker& tokens) {
    auto entries = static_cast<Procedure*>(tokens.Push(table, 12, 11));
    const U32 names[] = {2363267224u,3384945314u,2688015380u,665784578u,452487489u,2278711377u,4076609409u,1549709877u,3363839358u,3600960647u};
    const U32 types[] = {1295728409u,2603864u,1552515497u,2684175339u,2600243603u,2600243603u,2814214430u,2814214430u,2602108063u,2602108063u};
    const unsigned offsets[] = {0,4,8,12,16,52};
    for (unsigned i = 0; i < 10; ++i) { entries[i].name = names[i]; entries[i].type = types[i]; }
    for (unsigned i = 0; i < 6; ++i) tokens.BookmarkMemberOffset(reinterpret_cast<unsigned char*>(object)+offsets[i], &entries[i].offset);
    if (object->status) tokens.BookmarkMemberOffset(object->status, &entries[6].offset);
    if (object->statusExclude) tokens.BookmarkMemberOffset(object->statusExclude, &entries[7].offset);
    TokenThings(object->includeCount, object->includeThings, &entries[8].offset, tokens);
    TokenThings(object->excludeCount, object->excludeThings, &entries[9].offset, tokens);
    tokens.Pop();
}
void EndianObjectFilter(ObjectFilter* object) {
    if (object->status) for (unsigned i = 0; i < 7; ++i) Swap(object->status+i);
    if (object->statusExclude) for (unsigned i = 0; i < 7; ++i) Swap(object->statusExclude+i);
    for (unsigned i = 0; i < object->includeCount; ++i) Swap(object->includeThings+i);
    for (unsigned i = 0; i < object->excludeCount; ++i) Swap(object->excludeThings+i);
    for (unsigned offset = 0; offset < 88; offset += 4) Swap(reinterpret_cast<unsigned char*>(object)+offset);
    Swap(&object->includeCount); Swap(&object->excludeCount);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
