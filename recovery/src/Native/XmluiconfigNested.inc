// Complete UIConfigList nested layouts. Module blob 2d2f471c8008b7c2e1f78e39752d0726da395cea.
// Included inside UIConfigDetail::Builder; scalar addresses are corrected once by the parent.
int EnumValue(const char* type,const std::string& text){
 if(std::strcmp(type,"FactionType")==0){const EnumNameValue table[]={{"Allies",0},{"Japan",2},{"Neutral",3},{"RANDOM",-1},{"Soviet",1}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 0;}
 if(std::strcmp(type,"AIDifficultyValue")==0){const EnumNameValue table[]={{"BRUTAL",3},{"EASY",0},{"HARD",2},{"MEDIUM",1}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 3;}
 if(std::strcmp(type,"GameplayType")==0){const EnumNameValue table[]={{"CAH",2},{"CTF",3},{"KOTH",1},{"SIEGE",4},{"VERSUS",0}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 2;}
 if(std::strcmp(type,"SlotStateType")==0){const EnumNameValue table[]={{"AI",3},{"CLOSED",1},{"OPEN",0},{"PRIVATE",2}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 3;}
 if(std::strcmp(type,"CareerStatType")==0){const EnumNameValue table[]={{"Float",1},{"Int",0},{"Time",2}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 1;}
 if(std::strcmp(type,"CareerStatOrder")==0){const EnumNameValue table[]={{"ActivatedCommanderPowers",33},{"ActivatedUnitAbilities",32},{"AvgGameLength",8},{"AvgIncomePerMinute",23},{"BridgesDestroyed",17},{"CaptureRatio",15},{"CareerLosses",5},{"CareerWins",6},{"CurrentLossStreak",1},{"CurrentWinStreak",0},{"EfficiencyRatio",28},{"FormationPreviewUsed",27},{"FriendlyFireIncidents",31},{"LongestLosingStreak",3},{"LongestWinStreak",2},{"ResourcesGathered",25},{"ResourcesSpent",26},{"SecondsInLowPower",24},{"StructuresBuilt",10},{"StructuresCaptured",13},{"StructuresCapturedByEnemy",14},{"StructuresDestroyed",12},{"StructuresInfiltrated",34},{"StructuresLost",11},{"StructuresSold",18},{"SuperWeaponsBuilt",16},{"TotalMatchesPlayed",4},{"TotalTimePlayed",9},{"UnitKillLostRatio",22},{"UnitsBuilt",19},{"UnitsKilled",21},{"UnitsLost",20},{"ValueOfUnitsKilled",30},{"ValueOfUnitsLost",29},{"WinLossRatio",7}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 33;}
 if(std::strcmp(type,"PostGameStatType")==0){const EnumNameValue table[]={{"AvgIncomePerMinute",10},{"AvgIncomePerSecond",11},{"CommanderPowersUsed",16},{"EfficiencyRatio",13},{"EnemyStructuresInfiltrated",14},{"ResourcesGathered",12},{"SessionLength",0},{"StructuresCaptured",4},{"StructuresCreated",1},{"StructuresDestroyed",3},{"StructuresLost",2},{"StructuresSold",5},{"UnitAbilitiesUsed",15},{"UnitKillDeathRatio",9},{"UnitsCreated",6},{"UnitsKilled",8},{"UnitsLost",7}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 10;}
 if(std::strcmp(type,"ShadowType")==0){const EnumNameValue table[]={{"ADDITIVE_DECAL",3},{"ADDITIVE_DECAL_DYNAMIC",5},{"ALPHA_DECAL",2},{"ALPHA_DECAL_DYNAMIC",4},{"DECAL",1},{"MERGE_DECAL",6},{"NONE",0}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 3;}
 if(std::strcmp(type,"JoyPadButtonType")==0){const EnumNameValue table[]={{"A",6},{"B",7},{"BACK",11},{"D_PAD",10},{"GUIDE",13},{"LEFT_BUMPER",2},{"LEFT_STICK",0},{"LEFT_TRIGGER",1},{"RIGHT_BUMPER",5},{"RIGHT_STICK",3},{"RIGHT_TRIGGER",4},{"START",12},{"X",8},{"Y",9}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 6;}
 if(std::strcmp(type,"JoyPadActionType")==0){const EnumNameValue table[]={{"CLICK",2},{"DOUBLE_CLICK",3},{"DOWN",11},{"HOLD",1},{"LEFT",8},{"LEFT_OR_RIGHT",12},{"LINK_WITH_NEXT",14},{"MOVE",7},{"ON_GROUND",4},{"ON_TARGET",5},{"ON_UNIT_TYPE",6},{"PRESS",0},{"RIGHT",9},{"UP",10},{"UP_OR_DOWN",13}};for(const auto& v:table)if(std::strcmp(text.c_str(),v.name)==0)return v.value;return 2;}
 throw std::invalid_argument("Unknown UIConfig nested enum.");
}
void EnumAttr(const Node& n,const char* attr,unsigned char* p,const char* type,const char* fallback=nullptr){auto i=n.attributes.find(attr);if(i!=n.attributes.end()||fallback)Word(p,static_cast<U32>(EnumValue(type,i==n.attributes.end()?fallback:i->second)));}
void NestedScalar(const Node& n,const char* attr,unsigned char* p,const char* kind,const char* fallback=nullptr){auto i=n.attributes.find(attr);if(i==n.attributes.end()&&!fallback)return;std::string v=i==n.attributes.end()?fallback:i->second;if(std::strcmp(kind,"bool")==0)*p=ParseBoolean(v);else if(std::strcmp(kind,"int")==0)Word(p,ParseSigned(v));else if(std::strcmp(kind,"uint")==0)Word(p,ParseUnsigned(v));else if(std::strcmp(kind,"float")==0)Real(p,ParseFloat(v));else if(std::strcmp(kind,"percent")==0)Real(p,ParsePercentage(v));else throw std::invalid_argument("Unknown UIConfig nested scalar.");}
void NullableHash(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),4,1));Word(target,HashAssetId(i->second));t.Pop();}
void NullableRef(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),4,1));MarshalReference(t,target,i->second);t.Pop();}
void KindFlags(const Node& n,const char* attr,unsigned char* p){auto i=n.attributes.find(attr);if(i==n.attributes.end())return;const EnumNameValue table[]={{"ACTIVATE_AFTER_UNPACK",149},{"AIRCRAFT",14},{"AIRCRAFT_PATH_AROUND",66},{"AIRCRAFT_TRANSPORT_ONLY_GARRISONABLE_WHILE_PARKED",251},{"AIRFIELD",36},{"AI_CAN_UNPACK_NEAR_CONYARD",143},{"AI_NEEDS_PLAYER_POWER_UPDATE",137},{"ALLY_TARGETABLE",3},{"ALWAYS_COLLIDE_WITH_OWN_SHIELD_SPHERES",187},{"ALWAYS_SELECTABLE",59},{"ALWAYS_SHOW_HOUSE_COLOR",171},{"ALWAYS_VISIBLE",54},{"ALWAYS_VISIBLE_IN_RADAR",274},{"AMPHIBIOUS",211},{"ANTI_GARRISON",242},{"ARMY_SUMMARY",126},{"ASSAULT_AIRCRAFT",239},{"ATTACH_ATTACK",275},{"ATTACK_FROM_STRUCTURE_ONLY_WHEN_GARRISONED",253},{"AUTO_ACQUIRABLE_BY_AI",230},{"AUTO_RALLYPOINT",69},{"BALLISTIC_MISSILE",75},{"BANNER",157},{"BASE_FOUNDATION",101},{"BASE_SITE",118},{"BEACON",247},{"BEAM_TARGET",111},{"BEAM_TARGET_REFLECTOR",112},{"BLOCKING_GATE",135},{"BOMBER_AIRCRAFT",240},{"BRIDGE",23},{"BRIDGE_ENDCAP",256},{"BRIDGE_GATEHOUSE",257},{"BRIDGE_SEGMENT",219},{"BROADCASTS_INVISIBILITY",144},{"BUFF",139},{"BUILD_FOR_FREE",154},{"CANNOT_BE_DETECTED",229},{"CANNOT_BUILD_NEAR_SUPPLIES",85},{"CANNOT_HIJACK",270},{"CANNOT_RETALIATE",164},{"CANNOT_STOP_ON_WATER",195},{"CANT_TOGGLE_POWER",24},{"CAN_ATTACK",5},{"CAN_ATTACK_STEALTHED",227},{"CAN_ATTACK_WALLS",145},{"CAN_BE_CAPTURED",104},{"CAN_BE_FAVORITE_UNIT",258},{"CAN_BE_REPULSED",46},{"CAN_BUILD_ON_DEEP_WATER",265},{"CAN_BUILD_ON_WATER",264},{"CAN_CAPTURE",70},{"CAN_CAST_REFLECTIONS",7},{"CAN_CLIMB_WALLS",174},{"CAN_HEAL_ALLIES",261},{"CAN_NOT_BUILD_ON_LAND",263},{"CAN_PLACE_CHARGE",57},{"CAN_PLACE_MANIPULATOR",83},{"CAN_RAPPEL",44},{"CAN_SEE_THROUGH_STRUCTURE",74},{"CAN_SHOOT_OVER_WALLS",208},{"CAN_SKIP_SHADOW",190},{"CAN_TOPPLE",188},{"CAPTURABLE",50},{"CAPTUREFLAG",194},{"CARPET_BOMBER",282},{"CASTLE_CENTER",19},{"CASTLE_KEEP",29},{"CAVALRY",11},{"CHUNK_VENDOR",130},{"CIVILIAN_BUILDING",201},{"CIVILIAN_UNIT",127},{"CLASSIFY_ENTIRE_FOOTPRINT",58},{"CLEARED_BY_BUILD",52},{"CLICK_THROUGH",76},{"COMBO_HORDE",107},{"COMMANDCENTER",18},{"COMMANDO",47},{"CONSTRUCTION_YARD",207},{"COVER",217},{"CRANE",136},{"CRATE",49},{"CREEP",165},{"CRUSHABLE_BUILDING",271},{"CRUSHABLE_OBSTACLE",231},{"CRUSH_CRUSHABLE_BUILDINGS",272},{"CYCLE_SELECTION",189},{"DEBRIS",280},{"DEFENSIVE_WALL",61},{"DEFLECT_BY_SPECIAL_POWER",99},{"DEPLOYED_MINE",163},{"DEPRECATED",122},{"DISGUISER",88},{"DONT_AUTO_CRUSH_INFANTRY",91},{"DONT_DESTROY_IF_ON_BRIDGE",281},{"DONT_HIDE_IF_FOGGED",203},{"DONT_SUBMERGE_BY_WAVES",84},{"DONT_UNATTACH_WHEN_HEALING",276},{"DONT_USE_CANCEL_BUILD_BUTTON",200},{"DOZER",16},{"DOZER_FACTORY",184},{"DO_NOT_CLASSIFY",147},{"DO_NOT_PICK_ME_WHEN_BUILDING",168},{"DRAWABLE_ONLY",37},{"DRONE",73},{"ECONOMY_STRUCTURE",186},{"EGG",245},{"ENCLOSURE",268},{"ENGINEER",243},{"ENVIRONMENT",98},{"EXPANSION_PAD",210},{"EXPANSION_UNIT",244},{"FIGHTER_AIRCRAFT",241},{"FORCEATTACKABLE",68},{"FS_AIR_FIELD",223},{"FS_BARRACKS",221},{"FS_BASE_DEFENSE",64},{"FS_CASH_PRODUCER",133},{"FS_FACTORY",63},{"FS_MONEY_STORAGE",213},{"FS_POWER",62},{"FS_RADAR",222},{"FS_TECHNOLOGY",65},{"FS_TECH_CENTER",224},{"FS_WAR_FACTORY",220},{"GARRISON",116},{"GARRISONABLE_UNTIL_DESTROYED",79},{"GRABBABLE",206},{"GRAB_AND_DROP",140},{"GRAB_AND_KILL",96},{"GRANTS_VETERANCY",273},{"HARVESTABLE",205},{"HARVESTER",17},{"HAS_HEALTH_BAR",161},{"HEAL_PAD",33},{"HEAVY_MELEE_HITTER",202},{"HIDE_IF_FOGGED",170},{"HORDE",106},{"HUGE_VEHICLE",15},{"HULK",82},{"HUSK",248},{"IGNORED_IN_FINDPOSITIONAROUND",105},{"IGNORED_IN_GUI",48},{"IGNORES_SELECT_ALL",90},{"IGNORE_CAN_NOT_BUILD",234},{"IGNORE_FORCE_MOVE",249},{"IGNORE_FOR_EVA_SPEECH_POSITION",155},{"IGNORE_FOR_VICTORY",146},{"IGNORE_IN_AI_HUNT_TACTIC",162},{"IMMOBILE",4},{"IMMUNE_TO_CAPTURE",81},{"IMMUNE_TO_CYCLONIC_SHOCKWAVE",228},{"INERT",89},{"INERT_SHROUD_REVEALER",119},{"INFANTRY",10},{"INFILTRATOR",218},{"KEEP_CLASSIFIED_WHEN_DEAD",129},{"LARGE_RECTANGLE_PATHFIND",176},{"LEAVE_PARKING_ON_BUILD_COMPLETE",267},{"LIMITED_PRODUCTION_AIRCRAFT",266},{"LINE_OF_SIGHT_IGNORES_BUILDINGS",250},{"LINKED_TO_FLAG",51},{"LOW_OVERLAPPABLE",67},{"MACHINE",13},{"MCV",260},{"MELEE_HORDE",117},{"MINE",56},{"MINE_TRIGGER",138},{"MONSTER",12},{"MOVE_FOR_NOONE",172},{"MOVE_ONLY",132},{"MP_COUNT_FOR_VICTORY",38},{"NEED_BASE_FOUNDATION",102},{"NEUTRAL_TECH",232},{"NEVER_CULL_FOR_MP",199},{"NONOCCLUDING",108},{"NOTIFY_OF_PREATTACK",115},{"NOT_AUTOACQUIRABLE",128},{"NOT_SELLABLE",151},{"NO_BASE_CAPTURE",125},{"NO_COLLIDE",31},{"NO_FORMATION_MOVEMENT",124},{"NO_FREEWILL_ENTER",109},{"NO_GARRISON",28},{"NO_HEAL_ICON",43},{"NO_SHADOW",175},{"OBSTACLE",0},{"OCL_BIT",120},{"OPTIMIZED_PROP",97},{"OPTIMIZED_SOUND",182},{"ORE_NODE",159},{"ORIENTS_TO_CAMERA",198},{"OUTPOST",246},{"PARACHUTABLE",45},{"PARACHUTE",78},{"PASS_EXPERIENCE_TO_CONTAINED",183},{"PASS_EXPERIENCE_TO_CONTAINER",196},{"PASS_EXPERIENCE_TO_PRODUCER",209},{"PASS_EXPERIENCE_TO_SLAVER",160},{"PATH_THROUGH_EACH_OTHER",114},{"PATH_THROUGH_INFANTRY",123},{"PORT",178},{"POWERED",71},{"POWERED_POWERS_ONLY",166},{"PRELOAD",27},{"PRODUCED_AT_HELIPAD",72},{"PROJECTILE",26},{"PROJECT_BUILDABILITY_FOR_ALLIES",262},{"RADAR_SCRAMBLER",269},{"REACT_WHEN_SELECTED",103},{"REBUILD_HOLE",39},{"REFINERY",131},{"REPAIR_PAD",32},{"RESIST_EMP",197},{"REVEAL_TO_ALL",87},{"ROCK",94},{"ROCK_VENDOR",134},{"SALVAGER",20},{"SCALEABLE_WALL",191},{"SCARY",142},{"SCORE",40},{"SCORE_CREATE",41},{"SCORE_DESTROY",42},{"SELECTABLE",1},{"SHADOW_ULTRA_HIGH_ONLY",30},{"SHIP",181},{"SHOW_BEHIND_OCCLUDERS",212},{"SHRUBBERY",8},{"SIEGE_TOWER",92},{"SIEGE_WEAPON",25},{"SIMPLE_OBJECT_PROP",180},{"SKIP_DEFECT_IF_UNCOMBINED",254},{"SKIP_IDLE_WHEN_CAPTURED",255},{"SKIRMISH_AI_ATTEMPT_TO_DODGE",238},{"SKIRMISH_AI_CAN_BUILD_DURING_EMERGENCY_NO_INCOME",80},{"SKIRMISH_AI_DONT_CONSIDER_THREAT",235},{"SKIRMISH_AI_DONT_GARRISON",236},{"SKIRMISH_AI_DONT_MULTI_TARGET",150},{"SKIRMISH_AI_DONT_REPAIR",237},{"SKYBOX",192},{"SLAVE_OWNER",259},{"SMALL_MISSILE",53},{"SNIPER",252},{"SPELL_BOOK",121},{"STEALTH_GARRISON",34},{"STICK_TO_TERRAIN_SLOPE",6},{"STRUCTURE",9},{"SUBMARINE",177},{"SUMMONED",169},{"SUPER_WEAPON",233},{"SUPPLY_GATHERING_CENTER",35},{"SUPPLY_SOURCE",86},{"SUPPLY_SOURCE_ON_PREVIEW",77},{"SURPRISE_ATTACKER",216},{"T1_UNIT",277},{"T2_UNIT",278},{"T3_UNIT",279},{"TACTICAL_MARKER",113},{"TARGETABLE",2},{"TERRAIN_LIKE_GROUND_OBJECT",283},{"THREAT_FINDER",185},{"THROWN_OBJECT",95},{"TIBERIUM",226},{"TIBERIUM_BASED",225},{"TIBERIUM_FIELD",110},{"TIME_BOMB",93},{"TRANSFORMER",141},{"TRANSPORT",22},{"UNATTACKABLE",55},{"UNIQUE_UNIT",215},{"UNPACKS_INTO_BUILDING",158},{"VALID_TARGET_FOR_C4",214},{"VEHICLE",204},{"VITAL_FOR_BASE_SURVIVAL",167},{"WALK_ON_TOP_OF_WALL",60},{"WALL_GATE",193},{"WALL_HUB",153},{"WALL_PIECE",152},{"WALL_SEGMENT",179},{"WALL_UPGRADE",148},{"WB_DISPLAY_SCRIPT_NAME",173},{"WEAPON_SALVAGER",21},{"WORKING_PASSENGER",100},{"WORLDBUILDER_SNAP_TO_GRID",156}};MarshalEnumBitset(i->second,table,sizeof(table)/sizeof(table[0]),149,284,reinterpret_cast<U32*>(p));for(unsigned j=0;j<9;++j)words.push_back(p+4*j);}
unsigned NestedSize(const char* type){
 if(std::strcmp(type,"AIDifficultySetting")==0)return 12;
 if(std::strcmp(type,"TeamSetting")==0)return 12;
 if(std::strcmp(type,"RankedOptionSetting")==0)return 12;
 if(std::strcmp(type,"GameplayTypeSetting")==0)return 16;
 if(std::strcmp(type,"GameTimeSetting")==0)return 20;
 if(std::strcmp(type,"GameResourcesSetting")==0)return 8;
 if(std::strcmp(type,"GamePointsSetting")==0)return 8;
 if(std::strcmp(type,"GameFlagsSetting")==0)return 8;
 if(std::strcmp(type,"SlotState")==0)return 16;
 if(std::strcmp(type,"CareerStatData")==0)return 24;
 if(std::strcmp(type,"PostGameStatData")==0)return 12;
 if(std::strcmp(type,"UIComponentTickerGroup")==0)return 16;
 if(std::strcmp(type,"FontDesc")==0)return 16;
 if(std::strcmp(type,"RGBColor")==0)return 12;
 if(std::strcmp(type,"Vector2")==0)return 8;
 if(std::strcmp(type,"ArchivableMovie")==0)return 32;
 if(std::strcmp(type,"RadiusDecalComponent")==0)return 64;
 if(std::strcmp(type,"DefaultValues")==0)return 72;
 if(std::strcmp(type,"InputButton")==0)return 8;
 if(std::strcmp(type,"RadiusDecalTemplate")==0)return 8;
 if(std::strcmp(type,"ArchiveMovieList")==0)return 12;
 if(std::strcmp(type,"ObjectFilter")==0)return 112;
 if(std::strcmp(type,"FactionType")==0)return 4;
 if(std::strcmp(type,"MultiplayerLobbyData")==0)return 48;
 if(std::strcmp(type,"SkirmishLobbyData")==0)return 24;
 if(std::strcmp(type,"ColorTag")==0)return 4;
 if(std::strcmp(type,"AssetId")==0)return 8;
 if(std::strcmp(type,"String")==0)return 8;
 if(std::strcmp(type,"TypedAssetId")==0)return 4;
 if(std::strcmp(type,"SingleCommandList")==0)return 16;
 if(std::strcmp(type,"SingleCommandLine")==0)return 24;
 throw std::invalid_argument("Unknown UIConfig nested type size.");
}
void NamedList(const Node& n,const char* name,unsigned char* p,const char* type){List(n,name,p,NestedSize(type),[&](const Node& child,unsigned char* item){Marshal(type,child,item);});}
void Nullable(const Node& n,const char* child,unsigned char* p,const char* type){auto c=Child(n,child);if(!c)return;auto target=static_cast<unsigned char*>(t.Push(reinterpret_cast<void**>(p),NestedSize(type),1));Marshal(type,*c,target);t.Pop();}
void NestedChildString(const Node& n,const char* name,unsigned char* p){auto c=Child(n,name);if(c){Text(c->value,p);}}
void Marshal(const char* type,const Node& n,unsigned char* p){
 if(std::strcmp(type,"AIDifficultySetting")==0){
  String(n,"Label",p+0,nullptr);
  EnumAttr(n,"Value",p+8,"AIDifficultyValue",nullptr);
  return;
 }
 if(std::strcmp(type,"TeamSetting")==0){
  String(n,"Label",p+0,nullptr);
  NestedScalar(n,"Value",p+8,"int",nullptr);
  return;
 }
 if(std::strcmp(type,"RankedOptionSetting")==0){
  String(n,"Label",p+0,nullptr);
  NestedScalar(n,"Value",p+8,"int",nullptr);
  return;
 }
 if(std::strcmp(type,"GameplayTypeSetting")==0){
  String(n,"Label",p+0,nullptr);
  EnumAttr(n,"Value",p+8,"GameplayType",nullptr);
  NestedScalar(n,"AvailableWithAI",p+12,"bool","false");
  return;
 }
 if(std::strcmp(type,"GameTimeSetting")==0){
  String(n,"Label",p+0,nullptr);
  NestedScalar(n,"Value",p+8,"int",nullptr);
  NestedScalar(n,"Default",p+16,"bool","false");
  NestedScalar(n,"MinGameTimeValue",p+12,"int","0");
  return;
 }
 if(std::strcmp(type,"GameResourcesSetting")==0){
  NestedScalar(n,"Value",p+0,"int",nullptr);
  NestedScalar(n,"Default",p+4,"bool","false");
  return;
 }
 if(std::strcmp(type,"GamePointsSetting")==0){
  NestedScalar(n,"Value",p+0,"int",nullptr);
  NestedScalar(n,"Default",p+4,"bool","false");
  return;
 }
 if(std::strcmp(type,"GameFlagsSetting")==0){
  NestedScalar(n,"Value",p+0,"int",nullptr);
  NestedScalar(n,"Default",p+4,"bool","false");
  return;
 }
 if(std::strcmp(type,"SlotState")==0){
  String(n,"Label",p+0,nullptr);
  EnumAttr(n,"Value",p+8,"SlotStateType",nullptr);
  NestedScalar(n,"AvailableInRanked",p+12,"bool","true");
  NestedScalar(n,"AvailableInModerated",p+13,"bool","true");
  return;
 }
 if(std::strcmp(type,"CareerStatData")==0){
  EnumAttr(n,"StatType",p+0,"CareerStatType",nullptr);
  String(n,"StatLabel",p+4,nullptr);
  NestedScalar(n,"IsRatio",p+20,"bool","false");
  NestedScalar(n,"NoSummary",p+21,"bool","false");
  EnumAttr(n,"RatioNumerator",p+12,"CareerStatOrder",nullptr);
  EnumAttr(n,"RatioDenominator",p+16,"CareerStatOrder",nullptr);
  return;
 }
 if(std::strcmp(type,"PostGameStatData")==0){
  EnumAttr(n,"StatType",p+0,"PostGameStatType",nullptr);
  String(n,"StatLabel",p+4,nullptr);
  return;
 }
 if(std::strcmp(type,"UIComponentTickerGroup")==0){
  String(n,"Name",p+0,nullptr);
  String(n,"HTTPAddress",p+8,nullptr);
  return;
 }
 if(std::strcmp(type,"FontDesc")==0){
  String(n,"Name",p+0,nullptr);
  NestedScalar(n,"Size",p+8,"uint",nullptr);
  NestedScalar(n,"Bold",p+12,"bool","false");
  return;
 }
 if(std::strcmp(type,"RGBColor")==0){
  NestedScalar(n,"R",p+0,"float",nullptr);
  NestedScalar(n,"G",p+4,"float",nullptr);
  NestedScalar(n,"B",p+8,"float",nullptr);
  return;
 }
 if(std::strcmp(type,"Vector2")==0){
  NestedScalar(n,"x",p+0,"float","0.0");
  NestedScalar(n,"y",p+4,"float","0.0");
  return;
 }
 if(std::strcmp(type,"ArchivableMovie")==0){
  NestedScalar(n,"ProgressLock",p+0,"uint","0");
  Ref(n,"MovieImage",p+4,nullptr);
  String(n,"FileName",p+8,nullptr);
  String(n,"OnScreenName",p+16,nullptr);
  String(n,"Description",p+24,nullptr);
  return;
 }
 if(std::strcmp(type,"RadiusDecalComponent")==0){
  Ref(n,"Texture",p+0,nullptr);
  Ref(n,"Texture2",p+4,nullptr);
  EnumAttr(n,"Style",p+8,"ShadowType",nullptr);
  NestedScalar(n,"OpacityMin",p+12,"percent","1.0");
  NestedScalar(n,"OpacityMax",p+16,"percent","1.0");
  NestedScalar(n,"OpacityPulsesPerSecond",p+20,"float","0.0");
  NestedScalar(n,"RotationMin",p+24,"float","0.0");
  NestedScalar(n,"RotationMax",p+28,"float","0.0");
  NestedScalar(n,"RotationDegreesPerSecond",p+32,"float","0.0");
  NestedScalar(n,"MinRadius",p+36,"float","1.0");
  NestedScalar(n,"MaxRadius",p+40,"float","1.0");
  NestedScalar(n,"RadiusPulsesPerSecond",p+44,"float","0.0");
  NestedScalar(n,"PositionOffsetMin",p+48,"float","0.0");
  NestedScalar(n,"PositionOffsetMax",p+52,"float","0.0");
  NestedScalar(n,"PositionOffsetPulsesPerSecond",p+56,"float","0.0");
  if(auto c=Child(n,"Color"))Marshal("ColorTag",*c,p+60);
  return;
 }
 if(std::strcmp(type,"DefaultValues")==0){
  NestedScalar(n,"Difficulty",p+0,"int","1");
  NestedScalar(n,"Gamma",p+4,"float","1.30");
  NestedScalar(n,"Brightness",p+8,"float","0.50");
  NestedScalar(n,"Contrast",p+12,"float","1.30");
  NestedScalar(n,"VolumeMusic",p+16,"float","0.45");
  NestedScalar(n,"VolumeFX",p+20,"float","0.70");
  NestedScalar(n,"VolumeVoice",p+24,"float","0.70");
  NestedScalar(n,"VolumeAmbient",p+28,"float","0.50");
  NestedScalar(n,"VolumeMovie",p+32,"float","0.70");
  NestedScalar(n,"ScrollSpeed",p+36,"float","1.00");
  NestedScalar(n,"ScrollMagnetism",p+40,"int","2");
  NestedScalar(n,"ButtonIcon",p+60,"bool","true");
  NestedScalar(n,"ShowCommandList",p+61,"bool","true");
  NestedScalar(n,"HealthBars",p+62,"bool","true");
  NestedScalar(n,"HighlightPads",p+63,"bool","true");
  NestedScalar(n,"ToolTips",p+64,"bool","true");
  NestedScalar(n,"Subtitles",p+65,"bool","false");
  NestedScalar(n,"InvertRotate",p+66,"bool","true");
  NestedScalar(n,"Telemetry",p+67,"bool","true");
  NestedScalar(n,"DefaultFaction",p+44,"int","-1");
  NestedScalar(n,"VisionCamVisible",p+68,"bool","true");
  NestedScalar(n,"VisionCamZoom",p+48,"int","1");
  NestedScalar(n,"ScrollSpeedMin",p+52,"float","0.2");
  NestedScalar(n,"ScrollSpeedMax",p+56,"float","2.0");
  return;
 }
 if(std::strcmp(type,"InputButton")==0){
  EnumAttr(n,"ButtonName",p+0,"JoyPadButtonType",nullptr);
  EnumAttr(n,"ButtonAction",p+4,"JoyPadActionType",nullptr);
  return;
 }
if(std::strcmp(type,"SingleCommandList")==0){String(n,"CategoryName",p);NamedList(n,"CommandLine",p+8,"SingleCommandLine");return;}
 if(std::strcmp(type,"SingleCommandLine")==0){String(n,"Purpose",p);String(n,"Description",p+8);NamedList(n,"Input",p+16,"InputButton");return;}
 if(std::strcmp(type,"ColorTag")==0){U32 red=ParseUnsigned(Value(n,"R","0"))&255u,green=ParseUnsigned(Value(n,"G","0"))&255u,blue=ParseUnsigned(Value(n,"B","0"))&255u,alpha=ParseUnsigned(Value(n,"A","255"))&255u;Word(p,(alpha<<24)|(red<<16)|(green<<8)|blue);return;}
 if(std::strcmp(type,"RadiusDecalTemplate")==0){NamedList(n,"Component",p,"RadiusDecalComponent");return;}
 if(std::strcmp(type,"ArchiveMovieList")==0){EnumAttr(n,"Faction",p,"FactionType","Soviet");NamedList(n,"Movie",p+4,"ArchivableMovie");return;}
 if(std::strcmp(type,"MultiplayerLobbyData")==0){NestedChildString(n,"LobbyTitleLabelSpec",p);NestedChildString(n,"UnmoderatedLobbyTitleTemplateSpec",p+8);NestedChildString(n,"ModeratedLobbyTitleTemplateSpec",p+16);NestedChildString(n,"PlayerNameLabelSpec",p+24);NestedChildString(n,"SlotLabelSpec",p+32);NamedList(n,"SlotState",p+40,"SlotState");return;}
 if(std::strcmp(type,"SkirmishLobbyData")==0){NestedChildString(n,"LoadMusic",p);NestedChildString(n,"SlotLabelSpec",p+8);NamedList(n,"SlotState",p+16,"SlotState");return;}
 if(std::strcmp(type,"FactionType")==0){Word(p,static_cast<U32>(EnumValue(type,n.value)));return;}
 if(std::strcmp(type,"AssetId")==0||std::strcmp(type,"String")==0){Text(n.value,p);return;}
 if(std::strcmp(type,"TypedAssetId")==0){Word(p,HashAssetId(n.value));return;}
 if(std::strcmp(type,"ObjectFilter")==0){
  ObjectFilterInput input;
  // Original scalar enum helpers63778/63789 compare C strings. Scope this adapter
  // normalization to UIConfigList; shared ObjectFilter users retain their accepted input contract.
  for(const auto& a:n.attributes)if(a.first!="TypeId")input.attributes[a.first]=(a.first=="Rule"||a.first=="Alignment")?std::string(a.second.c_str()):a.second;for(const auto& c:n.children){(c.name=="IncludeThing"?input.includeThings:input.excludeThings).push_back(c.value);}auto object=reinterpret_cast<ObjectFilter*>(p);MarshalObjectFilter(input,object,t);
  for(unsigned at=0;at<88;at+=4)words.push_back(p+at);words.push_back(p+96);words.push_back(p+104);
  if(object->status)for(unsigned j=0;j<7;++j)words.push_back(object->status+j);if(object->statusExclude)for(unsigned j=0;j<7;++j)words.push_back(object->statusExclude+j);for(unsigned j=0;j<object->includeCount;++j)words.push_back(object->includeThings+j);for(unsigned j=0;j<object->excludeCount;++j)words.push_back(object->excludeThings+j);return;
 }
 throw std::invalid_argument("Unknown UIConfig nested type.");
}
