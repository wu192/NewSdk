// Included by GameObject.cpp; definitions remain in its namespace and original order.
unsigned GameObjectSoundSelectorKind(const std::string& typeId) {
    auto choice=FindModule(ParseUnsigned(typeId));return choice && choice->kind>=13 && choice->kind<=16?choice->kind:0;
}

bool GameObjectModuleNestedSupported(const std::string& typeId,const std::string& name) {
    auto choice=FindModule(ParseUnsigned(typeId));return choice && ((choice->kind>=1 && choice->kind<=2 && name=="DamageRangeModifier") || (choice->kind==8 && name=="Key") || (choice->kind==9 && name=="Sound") || (choice->kind==10 && name=="ModelConditionSound") || (choice->kind==21 && name=="ObjectStatusValidation") || (choice->kind>=23 && choice->kind<=29 && name=="DieMuxData") || (choice->kind==27 && (name=="UpgradeRequired" || name=="UpgradeForbidden")) || (choice->kind==29 && name=="ObjectFilter") || (choice->kind>=30 && choice->kind<=51 && (name=="CustomAnimAndDuration" || name=="TriggeredBy" || name=="ConflictsWith")));
}
unsigned GameObjectRecursiveModuleKind(const std::string& typeId) {auto c=FindModule(ParseUnsigned(typeId));return c && c->kind>=41?c->kind:0;}
unsigned GameObjectModuleNodeKind(unsigned kind,const std::string& path) {
    if(kind>=286 && kind<=290)return DrawModelNodeKind(kind,path);
    if(kind>=283 && kind<=285){
        if(kind!=284 && (path=="FXShader" || path=="FXShader/Constants"))return 1;
        if(kind!=285 && path=="ObjectStatusValidation")return 1;
        if(kind==284 && (path=="HeadColor" || path=="TailColor"))return 1;
        if(kind==285){if(path=="ParticleSystems")return 1;if(path=="ParticleSystems/ParticleSys")return 3;}
        return 0;
    }
    if(kind>=278 && kind<=282) {
        if(kind==278) {if(path=="CanEnterFilter") return 1;if(path=="CanEnterFilter/IncludeThing" || path=="CanEnterFilter/ExcludeThing") return 3;return 0;}
        if(kind==279)return 0;
        if(path=="InitialPayload" || path=="UpgradeCreationTrigger")return 2;
        if(path=="ThrowOutPassengersVelocity" || path=="FadeFilter")return 1;
        if(path=="FadeFilter/IncludeThing" || path=="FadeFilter/ExcludeThing")return 3;
        if(kind==282) {
            if(path=="RandomOffset" || path=="LeaderPosition" || path=="BannerCarrierPosition/Pos")return 1;
            if(path=="RankInfo" || path=="RankInfo/Position" || path=="BannerCarrierPosition")return 2;
            if(path=="RankThatStopsAdvance" || path=="RankToReleaseWhenAttacking" || path=="BannerCarriersAllowed" || path=="LeadersAllowed" || path=="AttributeModifier")return 3;
            if(path=="BannerCarrierPosition/UnitType")return 4;
        }
        return GameObjectModuleNodeKind(270,path);
    }
    if(kind>=270 && kind<=277) {
        if(path=="PassengerFilter" || path=="DieMuxData" || path=="PassengerData/Filter") return 1;
        if(path=="PassengerData" || path=="MemberTemplateStatusInfo") return 2;
        if(path=="ModifierToGiveOnExit" || path=="PassengerFilter/IncludeThing" || path=="PassengerFilter/ExcludeThing" || path=="PassengerData/Filter/IncludeThing" || path=="PassengerData/Filter/ExcludeThing") return 3;
        if(kind!=270 && path=="InitialRoster") return 1;
        if((kind==272 || kind>=274) && (path=="EntryOffset" || path=="EntryPosition" || path=="ExitOffset")) return 1;
        if(kind==277) {if(path=="CanAlwaysEnterObjectFilter") return 1;if(path=="CanAlwaysEnterObjectFilter/IncludeThing" || path=="CanAlwaysEnterObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=265 && kind<=269) {
        if(kind==269) {if(path=="Item") return 2;if(path=="DieMuxData") return 1;return 0;}
        if(path=="DieMuxData") return 1;
        for(const char* n:{"FX","OCL","Weapon","Sound"}) {if(path==n) return 2;if(std::string(n)!="Sound" && path==std::string(n)+"/"+n) return 3;}
        if(kind==266) {
            if(path=="ClearanceGeometry" || path=="ClearanceGeometryOffset") return 1;
            if(path=="ClearanceGeometry/Shapes") return 2;
            for(const char* n:{"RotationAnchorOffset","Center","HighestContactPoint","InnermostContactPoint","ContactPoints"}) if(path==std::string("ClearanceGeometry/")+n) return 1;
            if(path=="ClearanceGeometry/Shapes/Offset" || path=="ClearanceGeometry/ContactPoints/Pos") return 1;
        }
        return 0;
    }
    if(kind>=252 && kind<=264) {
        if(kind==255 && path=="UpgradeMap") return 2;
        if(kind==256 && path=="ScatterSettings") return 1;
        if(kind==259 && path=="ParticleData") return 2;
        if(kind==260 && (path=="LeftOffset" || path=="RightOffset")) return 1;
        if(kind==261 && path=="ObjectStatusValidation") return 1;
        if(kind==262) {if(path=="SelfValidation") return 1;if(path=="ObjectEffectTuple") return 2;if(path=="ObjectEffectTuple/UpgradeSelf") return 3;}
        if(kind==263 && path=="ConnectionShadowInfo") return 1;
        const char* filters[2]={nullptr,nullptr};
        if(kind==253 || kind==261 || kind==264) filters[0]="ObjectFilter";
        if(kind==254) {filters[0]="InstaKillObjectFilter";filters[1]="CollidesWithOnTeleport";}
        if(kind==256 || kind==259) filters[0]="Filter";if(kind==259) filters[1]="KillFilter";
        if(kind==262) filters[0]="ObjectEffectTuple/ObjectFilter";
        for(const char* f:filters) if(f) {if(path==f) return 1;if(path==std::string(f)+"/IncludeThing" || path==std::string(f)+"/ExcludeThing") return 3;}
        return kind==258?GameObjectModuleNodeKind(77,path):0;
    }
    if(kind>=244 && kind<=251) {
        if(kind==244 || kind==248) {if(path=="CustomAnimAndDuration") return 1;if(path=="TriggeredBy" || path=="ConflictsWith") return 3;}
        if((kind==245 || kind==248) && path=="ObjectFilter") return 1;
        if((kind==245 || kind==248) && (path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing")) return 3;
        if(kind==246 && path=="DependentObjectRadius") return 3;
        if(kind==247) {if(path=="Modifier") return 3;if(path=="AllowFilter" || path=="ContainerFilter" || path=="ObjectStatusValidation") return 1;if(path=="AllowFilter/IncludeThing" || path=="AllowFilter/ExcludeThing" || path=="ContainerFilter/IncludeThing" || path=="ContainerFilter/ExcludeThing") return 3;}
        if(kind==249) {if(path=="Queries") return 2;if(path=="Queries/ObjectFilter") return 1;if(path=="Queries/ObjectFilter/IncludeThing" || path=="Queries/ObjectFilter/ExcludeThing") return 3;}
        if(kind==250) {if(path=="AddEmotion") return 3;for(const char* f:{"TauntAndPointExcluded","AfraidOf","AlwaysAfraidOf","PointAt"}) {if(path==f) return 1;if(path==std::string(f)+"/IncludeThing" || path==std::string(f)+"/ExcludeThing") return 3;}}
        if(kind==251) {if(path=="Event") return 2;if(path=="DieMuxData") return 1;}
        return 0;
    }
    if(kind>=238 && kind<=243) {
        if(kind==238 && path=="ObjectStatusValidation") return 1;
        if(kind==240 && (path=="ObjectStatusValidation" || path=="TimeDelayRange" || path=="RadiusSpreadRange")) return 1;
        if(kind!=239 && kind!=240) {if(path=="ObjectFilter") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;}
        return kind==243?GameObjectModuleNodeKind(70,path):0;
    }
    if(kind>=225 && kind<=237) return 0;
    if(kind>=220 && kind<=224) {
        if(path=="ObjectFilter" || (kind==224 && path=="PreventEnteringFilter") || (kind==223 && path=="MoneyGainObjectStatusValidation")) return 1;
        if(path=="ModifierToLeechFromParent" || path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing" || (kind==224 && (path=="PreventEnteringFilter/IncludeThing" || path=="PreventEnteringFilter/ExcludeThing"))) return 3;
        return 0;
    }
    if(kind>=216 && kind<=219) {
        if(kind==217) {if(path=="ReplaceObjectNugget") return 2;if(path=="ReplaceObjectNugget/TargetObjectFilter") return 1;if(path=="ReplaceObjectNugget/ReplacementObject" || path=="ReplaceObjectNugget/TargetObjectFilter/IncludeThing" || path=="ReplaceObjectNugget/TargetObjectFilter/ExcludeThing") return 3;}
        if(kind==219 && path=="TerrainData") return 2;
        if(kind==218) {if(path=="PositionOffset" || path=="ObjectFilter" || path=="IgnoreInsideToInsideCheck") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing" || path=="IgnoreInsideToInsideCheck/IncludeThing" || path=="IgnoreInsideToInsideCheck/ExcludeThing") return 3;return 0;}
        return GameObjectModuleNodeKind(191,path);
    }
    if(kind==215) {if(path=="ToggleState" || path=="ToggleState/Module") return 2;if(path=="ToggleState/SkirmishAiInfo" || path=="ToggleState/SkirmishAiInfo/StateWeapon") return 1;return GameObjectModuleNodeKind(191,path);}
    if(kind==213) return GameObjectModuleNodeKind(191,path);
    if(kind==214) {if(path=="FXOffset") return 1;return GameObjectModuleNodeKind(198,path);}
    if(kind>=211 && kind<=212) {if((kind==211 && path=="SpecialPowerModules") || (kind==212 && path=="IntersectionFlagsFilter")) return 2;return GameObjectModuleNodeKind(191,path);}
    if(kind>=204 && kind<=210) {
        if((kind==206 && path=="AttributeModifierAffects") || (kind==207 && path=="AcceptanceFilter")) return 1;
        if((kind==206 && (path=="AttributeModifierAffects/IncludeThing" || path=="AttributeModifierAffects/ExcludeThing")) || (kind==207 && (path=="AcceptanceFilter/IncludeThing" || path=="AcceptanceFilter/ExcludeThing")) || (kind==209 && path=="ReplacementTemplate") || (kind==210 && path=="StructureTemplate")) return 3;
        return GameObjectModuleNodeKind(191,path);
    }
    if(kind>=200 && kind<=203) {if(kind==201 && path=="EjectionBoneOffset") return 1;return GameObjectModuleNodeKind(191,path);}
    if(kind>=198 && kind<=199) {
        if(kind==198) {
            if(path=="UpgradeOCL") return 2;
            if(path=="NearestSecondaryObjectFilter") return 1;
            if(path=="Upgrade" || path=="NearestSecondaryObjectFilter/IncludeThing" || path=="NearestSecondaryObjectFilter/ExcludeThing") return 3;
        }
        return GameObjectModuleNodeKind(162,path);
    }
    if(kind>=194 && kind<=197) return GameObjectModuleNodeKind(191,path);
    if(kind>=191 && kind<=193) return path=="CustomAnimAndDuration"?1:0;
    if(kind>=188 && kind<=190) {
        if(kind==188) return path=="InitiateSound"?1:0;
        if(path==(kind==189?"Modules":"SpecialPower")) return 2;
        return GameObjectModuleNodeKind(162,path);
    }
    if(kind>=165 && kind<=187) {
        const auto& children=specialDerivedChildren[kind-165];
        if(children.filter) {
            if(path==children.filter) return 1;
            if(path==std::string(children.filter)+"/IncludeThing" || path==std::string(children.filter)+"/ExcludeThing") return 3;
        }
        if(children.strongList && path==children.strongList) return 3;
        return GameObjectModuleNodeKind(162,path);
    }
    if(kind>=162 && kind<=164) {
        if(path=="InitiateSound" || path=="AttributeModifierAffects" || path=="RequirementsFilterMP" || path=="RequirementsFilterStrategic") return 1;
        for(const char* filter:{"AttributeModifierAffects","RequirementsFilterMP","RequirementsFilterStrategic"})
            if(path==std::string(filter)+"/IncludeThing" || path==std::string(filter)+"/ExcludeThing") return 3;
        return 0;
    }
    if(kind>=159 && kind<=161) {
        if(kind==160) {if(path=="ObjectFilter") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;}
        if(kind==161) {if(path=="ObjectStatusValidation" || path=="KeepAliveObjectFilter") return 1;if(path=="KeepAliveObjectFilter/IncludeThing" || path=="KeepAliveObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=156 && kind<=158) return 0;
    if(kind>=154 && kind<=155) return 0;
    if(kind>=152 && kind<=153) {
        if(kind==153) {if(path=="ObjectFilter") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=150 && kind<=151) {
        if(kind==150 && path=="SegmentPattern") return 3;
        if(kind==151) {if(path=="Filter") return 1;if(path=="Filter/IncludeThing" || path=="Filter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=147 && kind<=149) {
        if(kind==147 && path=="DieMuxData") return 1;
        if(kind==148 && path=="RadarColor") return 1;
        if(kind==149) {if(path=="DamageFilter" || path=="HealFilter") return 1;if(path=="DamageFilter/IncludeThing" || path=="DamageFilter/ExcludeThing" || path=="HealFilter/IncludeThing" || path=="HealFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=145 && kind<=146) {
        if(kind==145) {if(path=="CustomAnimAndDuration") return 1;if(path=="TriggeredBy" || path=="ConflictsWith") return 3;}
        if(kind==146) {if(path=="RequiresNearbyObjectFilter") return 1;if(path=="RequiresNearbyObjectFilter/IncludeThing" || path=="RequiresNearbyObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=143 && kind<=144) return kind==143 && (path=="StartColor" || path=="EndColor")?1:0;
    if(kind>=140 && kind<=142) {
        if(kind==141) {if(path=="AcceptableFriends") return 1;if(path=="AcceptableFriends/IncludeThing" || path=="AcceptableFriends/ExcludeThing") return 3;}
        if(kind==142 && path=="ObjectStatusValidation") return 1;
        return 0;
    }
    if(kind>=137 && kind<=139) {
        if(kind==137) {if(path=="ConnectingObjectFilter") return 1;if(path=="ConnectingObjectFilter/IncludeThing" || path=="ConnectingObjectFilter/ExcludeThing") return 3;}
        if(kind==139 && (path=="RestrictedAreaShape" || path=="RestrictedAreaShape/Offset")) return 1;
        return 0;
    }
    if(kind>=133 && kind<=136) return kind==135 && (path=="InitialCaptureBonus" || path=="Deposit" || path=="ResourceCap")?2:0;
    if(kind>=128 && kind<=132) {
        if(kind==129) {if(path=="CanParkHereFilter" || path=="DefaultRallyPointOffset") return 1;if(path=="CanParkHereFilter/IncludeThing" || path=="CanParkHereFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind==127) return 0;
    if(kind>=123 && kind<=125) {
        if(kind==123) {if(path=="Die" || path=="CustomAnimAndDuration") return 1;if(path=="SpawnTemplate" || path=="TriggeredBy" || path=="ConflictsWith") return 3;}
        return 0;
    }
    if(kind>=119 && kind<=122) {
        if(kind==119) {
            if(path=="HintObject") return 2;
            if(path=="PlacementRestriction" || path=="SuggestedOffset" || path=="HintObject/ObjectFilter" || path=="PlacementRestriction/RestrictedObjectFilter") return 1;
            if(path=="HintObject/ObjectFilter/IncludeThing" || path=="HintObject/ObjectFilter/ExcludeThing" || path=="PlacementRestriction/RestrictedObjectFilter/IncludeThing" || path=="PlacementRestriction/RestrictedObjectFilter/ExcludeThing") return 3;
        }
        if(kind==120) {if(path=="RequirementObjectFilter") return 1;if(path=="RequirementObjectFilter/IncludeThing" || path=="RequirementObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=112 && kind<=118) {
        if(kind==112 || kind==115 || kind==116) {
            if(path=="ForVoiceRetreatThisIsASafeHarborToObjectFilter") return 1;
            if(path=="ForVoiceRetreatThisIsASafeHarborToObjectFilter/IncludeThing" || path=="ForVoiceRetreatThisIsASafeHarborToObjectFilter/ExcludeThing") return 3;
        }
        if(kind==114 && path=="ObjectStatusValidation") return 1;
        if(kind==118) {if(path=="RotationSpeed" || path=="UnitSpacing" || path=="Filter") return 1;if(path=="Filter/IncludeThing" || path=="Filter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind>=104 && kind<=111) {
        if(path=="UnitAITargetChooserData" || path=="UnitAITargetChooserData/ObjectVisionRange/Filter") return 1;
        if(path=="UnitAITargetChooserData/ObjectVisionRange") return 2;
        if(path=="UnitAITargetChooserData/ObjectVisionRange/Filter/IncludeThing" || path=="UnitAITargetChooserData/ObjectVisionRange/Filter/ExcludeThing") return 3;
        return 0;
    }
    if(kind>=96 && kind<=103) {
        if(kind==103) {if(path=="CrateList" || path=="BlockingUnits/IncludeThing" || path=="BlockingUnits/ExcludeThing") return 3;if(path=="BlockingUnits") return 1;}return 0;
    }
    if(kind>=92 && kind<=95) {
        if(kind==92) {if(path=="ObjectStatusValidation" || path=="CustomAnimAndDuration") return 1;if(path=="TriggeredBy" || path=="ConflictsWith") return 3;}
        if(kind==93 && (path=="PercentOfBuildCostToRebuildInfo" || path=="SelfAudio")) return 1;
        if(kind==94 && path=="Die") return 1;
        if(kind==95) {if(path=="ObjectFilter") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;}
        return 0;
    }
    if(kind==91) {
        for(const char* slot:{"WeaponSlotHardpoint","WeaponSlotTurret","WeaponSlotHierarchicalTurret","WeaponSlotContained"}) {
            std::string base=slot;if(path==base || path==base+"/Weapon") return 2;
            if(base=="WeaponSlotTurret" || base=="WeaponSlotHierarchicalTurret") {
                std::string settings=base+"/TurretSettings",chooser=settings+"/TurretAITargetChooserData",vision=chooser+"/ObjectVisionRange";
                if(path==settings || path==chooser || path==vision+"/Filter") return 1;
                if(path==vision) return 2;
                if(path==vision+"/Filter/IncludeThing" || path==vision+"/Filter/ExcludeThing") return 3;
            }
        }return 0;
    }
    if(kind>=87) {if(kind==87) return path=="Data"?2:0;if(kind==88 || kind==90) {if(path=="CustomAnimAndDuration") return 1;if(path=="TriggeredBy" || path=="ConflictsWith") return 3;}if(kind==90) {if(path=="WeaponOffset" || path=="DieMuxData") return 1;if(path=="WeaponFireProbability") return 2;}return 0;}
    if(kind>=84) return 0;
    if(kind>=80) {
        if(kind==80) return path=="Movement"?1:0;
        if(kind==81) {if(path=="Volume") return 1;for(const auto& shape:motionShapeNames) {std::string base=std::string("Volume/")+shape;if(path==base) return 2;if(path==base+"/Translation" || path==base+"/Rotation") return 1;}}
        return 0;
    }
    if(kind>=77) {if(kind==79) return 0;if(path=="EndOffset" || path=="ObjectStatusValidation") return 1;if(path=="LaserEndParticleSystem" || path=="LaserStartParticleSystem") return 3;return 0;}
    if(kind>=69) {if(WeaponValidationOffset(kind) && path=="ObjectStatusValidation") return 1;if(kind==70) {if(path=="FireWeaponNugget") return 2;if(path=="FireWeaponNugget/Offset") return 1;}if(kind==75) {if(path=="Color") return 1;if(path=="EffectInfo") return 2;}return 0;}
    if(kind>=61) {if(kind==67) {if(path=="BroadcastObjectFilter" || path=="ObjectStatusValidation") return 1;if(path=="BroadcastObjectFilter/IncludeThing" || path=="BroadcastObjectFilter/ExcludeThing") return 3;}return 0;}
    if(kind==60) {if(path=="ObjectFilter" || path=="CustomAnimAndDuration") return 1;if(path=="TriggeredBy" || path=="ConflictsWith" || path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;return 0;}
    if(kind==59) return path=="GuardPosOffset"?1:0;
    if(kind>=55) {if(kind==56 && path=="ObjectFilter") return 1;if((kind==56 && path=="Upgrade") || (kind==58 && path=="SpecialPowerTemplate")) return 3;return 0;}
    if(kind>=52) {
        if(kind==52) {if(path=="QuantityModifier" || path=="ProductionModifier" || path=="GiveUpgradeToProduced/UpgradeTranslate") return 2;if(path=="GiveUpgradeToProduced" || path=="ProductionModifier/ModifierFilter") return 1;}
        if((kind==53 || kind==54) && (path=="UnitCreatePoint" || path=="NaturalRallyPoint")) return 1;
        return 0;
    }
    if(path=="CustomAnimAndDuration") return 1;
    if(path=="TriggeredBy" || path=="ConflictsWith") return 3;
    if(kind==41) {if(path=="AvailableSubStructure" || path=="StructureOffset") return 2;if(path=="AvailableSubStructure/UpgradeTrigger" || path=="AvailableSubStructure/ObjectTemplate") return 4;if(path=="StructureOffset/Offset") return 1;}
    if(kind==43 && path=="ReplacementTemplate") return 3;
    if(kind==44) {if(path=="Nugget" || path=="Nugget/UpgradeTexture") return 2;if(path=="Nugget/CustomAnimAndDuration") return 1;}
    if(kind==45 && path=="Offset") return 1;
    if(kind==46 && path=="UpgradeToRemove") return 3;
    if(kind==49 || kind==50) {if(path=="ObjectFilter") return 1;if(path=="ObjectFilter/IncludeThing" || path=="ObjectFilter/ExcludeThing") return 3;}
    if(kind==49 && path=="ApplyToUpgrade") return 3;
    return 0;
}

bool GameObjectMotionMovePaths(const std::string& typeId) {return ParseUnsigned(typeId)==3012512132u;}

