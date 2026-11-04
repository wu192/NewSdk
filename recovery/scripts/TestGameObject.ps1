$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$out = Join-Path $env:RUNNER_TEMP 'ra3-recovered-gameobject'
$report = 'recovery/evidence/gameobject-root'
New-Item -ItemType Directory -Force $out, $report | Out-Null
@{ succeeded = $false; sourceCommit = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID; sdkStatus = 'not run; original route retained' } | ConvertTo-Json | Set-Content "$report/validation.json"
python recovery/scripts/GenerateGameObjectFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'GameObject source metadata mismatch' }
python recovery/scripts/GenerateSoundSelectorFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Sound selector source metadata mismatch' }
python recovery/scripts/GenerateLifecycleFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle source metadata mismatch' }
python recovery/scripts/GenerateDieFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Die source metadata mismatch' }
python recovery/scripts/GenerateUpgradeFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Upgrade source metadata mismatch' }
python recovery/scripts/GenerateUpgradeRemaining.py --check
if ($LASTEXITCODE -ne 0) { throw 'Remaining Upgrade source metadata mismatch' }
python recovery/scripts/GenerateProductionFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Production source metadata mismatch' }
python recovery/scripts/GenerateCreateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Create source metadata mismatch' }
python recovery/scripts/GenerateSlavedFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Slaved source metadata mismatch' }
python recovery/scripts/GenerateTechReceiverFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'TechUpgradeReceiver source metadata mismatch' }
python recovery/scripts/GenerateVisionFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Vision source metadata mismatch' }
python recovery/scripts/GenerateWeaponBehaviorFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Weapon behavior source metadata mismatch' }
python recovery/scripts/GenerateBeamStateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Beam state source metadata mismatch' }
python recovery/scripts/GenerateMotionFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Motion source metadata mismatch' }
python recovery/scripts/GenerateFlightFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Flight source metadata mismatch' }
python recovery/scripts/GenerateReactiveFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Reactive source metadata mismatch' }
python recovery/scripts/GenerateWeaponSetFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'WeaponSet source metadata mismatch' }
python recovery/scripts/GenerateHealingFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Healing/repair source metadata mismatch' }
python recovery/scripts/GenerateCrateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Crate source metadata mismatch' }
python recovery/scripts/GenerateAIUpdateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'AI Update source metadata mismatch' }
python recovery/scripts/GenerateEconomyFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Economy source metadata mismatch' }
python recovery/scripts/GenerateAssistFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Assistance source metadata mismatch' }
python recovery/scripts/GenerateSpawnUpdateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Spawn/OCL Update source metadata mismatch' }
python recovery/scripts/GenerateSlaveWatcherFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'SlaveWatcher source metadata mismatch' }
python recovery/scripts/GenerateQueueBuildingFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Queue/Parking/Building source metadata mismatch' }
python recovery/scripts/InspectSpawnUpdateEnums.py | Tee-Object -FilePath "$report/spawn-update-enums.txt"
if ($LASTEXITCODE -ne 0) { throw 'Spawn/OCL Update PE enum inspection failed' }
python recovery/scripts/InspectEconomyPowerEnums.py | Tee-Object -FilePath "$report/economy-power-enums.txt"
if ($LASTEXITCODE -ne 0) { throw 'Economy/Power PE enum inspection failed' }
python recovery/scripts/GenerateEconomyPowerFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Economy/Power source metadata mismatch' }
python recovery/scripts/GenerateStructureWallFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Wall/Bridge/Placement source metadata mismatch' }
python recovery/scripts/GenerateCombatResponseFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Combat response source metadata mismatch' }
python recovery/scripts/GenerateStatusEffectsFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'EMP/Suppression source metadata mismatch' }
python recovery/scripts/GenerateStealthVisionFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Stealth/Visibility source metadata mismatch' }
python recovery/scripts/GenerateTiberiumFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Tiberium source metadata mismatch' }
python recovery/scripts/GenerateWallEncloseFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'WallHub/Enclose source metadata mismatch' }
python recovery/scripts/GenerateResourceAlliesFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Resource/Allies source metadata mismatch' }
python recovery/scripts/GenerateStateEventFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'State/Event source metadata mismatch' }
python recovery/scripts/InspectModelConditionEnums.py | Tee-Object -FilePath "$report/model-condition-enums.txt"
if ($LASTEXITCODE -ne 0) { throw 'Model condition PE enum inspection failed' }
python recovery/scripts/GenerateModelConditionFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Model Condition source metadata mismatch' }
python recovery/scripts/GenerateMotionCleanupFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Motion/Cleanup source metadata mismatch' }
python recovery/scripts/InspectSpecialPowerEnums.py | Tee-Object -FilePath "$report/special-power-enums.txt"
if ($LASTEXITCODE -ne 0) { throw 'SpecialPower PE enum inspection failed' }
python recovery/scripts/GenerateSpecialPowerBaseFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'SpecialPower/Radar source metadata mismatch' }
python recovery/scripts/GenerateSpecialPowerDerivedFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'SpecialPower derivations source metadata mismatch' }
python recovery/scripts/GeneratePowerRoutingFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Power routing source metadata mismatch' }
python recovery/scripts/GenerateSpecialAbilityFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'SpecialAbility source metadata mismatch' }
python recovery/scripts/ExportContainMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Contain original method evidence mismatch' }
python recovery/scripts/ExportW3DDrawMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'W3D original closure mismatch' }
python recovery/scripts/GenerateDrawBeamFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'W3D beam source metadata mismatch' }
python recovery/scripts/GenerateDrawModelConditionFields.py --check
if ($LASTEXITCODE -ne 0) { throw "Model condition contract check failed" }
python recovery/scripts/GenerateDrawAnimationFields.py --check
if ($LASTEXITCODE -ne 0) { throw "Animation contract check failed" }
python recovery/scripts/GenerateDrawDynamicsFields.py --check
if ($LASTEXITCODE -ne 0) { throw "Dynamics contract check failed" }
python recovery/scripts/GenerateDrawModelFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'W3D model source metadata mismatch' }
python recovery/scripts/GenerateContainRemainingFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Remaining Contain source metadata mismatch' }
python recovery/scripts/GenerateContainCoreFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Contain source metadata mismatch' }
python recovery/scripts/ExportSlowDeathMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'SlowDeath original method evidence mismatch' }
python recovery/scripts/GenerateSlowDeathFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'SlowDeath source metadata mismatch' }
python recovery/scripts/ExportObjectControlMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Object-control original method evidence mismatch' }
python recovery/scripts/GenerateObjectControlFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Object-control source metadata mismatch' }
python recovery/scripts/ExportSupportMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Support original method evidence mismatch' }
python recovery/scripts/GenerateSupportFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Support source metadata mismatch' }
python recovery/scripts/ExportAreaMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Area original method evidence mismatch' }
python recovery/scripts/GenerateAreaFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Area source metadata mismatch' }
python recovery/scripts/GenerateUtilityFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Utility source metadata mismatch' }
python recovery/scripts/ExportAttachNextMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Attach original method evidence mismatch' }
python recovery/scripts/GenerateAttachFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Attach source metadata mismatch' }
python recovery/scripts/ExportAbilityFinishMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability finish original evidence mismatch' }
python recovery/scripts/GenerateAbilityFinishFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability finish source metadata mismatch' }
python recovery/scripts/ExportAbilityToggleMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability toggle original method mismatch' }
python recovery/scripts/GenerateAbilityToggleFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability toggle source metadata mismatch' }
python recovery/scripts/ExportAbilityResourceMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability resource original method mismatch' }
python recovery/scripts/GenerateAbilityResourceFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability resource source metadata mismatch' }
python recovery/scripts/ExportAbilityRoutingMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability routing original method mismatch' }
python recovery/scripts/GenerateAbilityRoutingFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability routing source metadata mismatch' }
python recovery/scripts/ExportAbilityStateMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability state original method mismatch' }
python recovery/scripts/GenerateAbilityStateFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability state source metadata mismatch' }
python recovery/scripts/ExportAbilityMovementMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability movement original method mismatch' }
python recovery/scripts/GenerateAbilityMovementFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability movement source metadata mismatch' }
python recovery/scripts/ExportAbilityWeaponMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability weapon original method mismatch' }
python recovery/scripts/GenerateAbilityWeaponFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Ability weapon source metadata mismatch' }
python recovery/scripts/ExportPowerCreationMethods.py --check
if ($LASTEXITCODE -ne 0) { throw 'Power creation original method mismatch' }
python recovery/scripts/GeneratePowerCreationFields.py --check
if ($LASTEXITCODE -ne 0) { throw 'Power creation source metadata mismatch' }
Copy-Item SDK/tools/*.dll $out
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
$summaries = @()
foreach ($mode in @('Od','O2')) {
    cl /nologo /clr /LD /MD /EHa /$mode /fp:precise /AI"$out" /Fo"$out/" /Fe"$out/Recovered.GameObject.dll" recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/WeaponSlotOrder.cpp recovery/src/Native/GameObject.cpp recovery/src/Bridge/GameObjectCompiler.cpp *> "$report/build-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$report/build-$mode.txt"; throw "GameObject build failed: $mode" }
    & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:"$out/Recovered.GameObject.dll" /r:"$out/BinaryAssetBuilder.Core.dll" /out:"$out/GameObjectOracle.exe" (Resolve-Path 'recovery/tests/GameObjectOracle.cs') (Resolve-Path 'recovery/tests/GameObjectCases.cs') (Resolve-Path 'recovery/tests/GameObjectKindOf.cs') (Resolve-Path 'recovery/tests/GameObjectWeaponSetCases.cs') (Resolve-Path 'recovery/tests/GameObjectHealingCases.cs') (Resolve-Path 'recovery/tests/GameObjectCrateCases.cs') (Resolve-Path 'recovery/tests/GameObjectAIUpdateCases.cs') (Resolve-Path 'recovery/tests/GameObjectEconomyCases.cs') (Resolve-Path 'recovery/tests/GameObjectAssistCases.cs') (Resolve-Path 'recovery/tests/GameObjectSpawnUpdateCases.cs') (Resolve-Path 'recovery/tests/GameObjectSpawnCrateWatcherCases.cs') (Resolve-Path 'recovery/tests/GameObjectQueueParkingCases.cs') (Resolve-Path 'recovery/tests/GameObjectBuildingStructureCases.cs') (Resolve-Path 'recovery/tests/GameObjectEconomyPowerCases.cs') (Resolve-Path 'recovery/tests/GameObjectStructureWallCases.cs') (Resolve-Path 'recovery/tests/GameObjectCombatResponseCases.cs') (Resolve-Path 'recovery/tests/GameObjectStatusEffectsCases.cs') (Resolve-Path 'recovery/tests/GameObjectStealthVisionCases.cs') (Resolve-Path 'recovery/tests/GameObjectTiberiumCases.cs') (Resolve-Path 'recovery/tests/GameObjectWallEncloseCases.cs') (Resolve-Path 'recovery/tests/GameObjectResourceAlliesCases.cs') (Resolve-Path 'recovery/tests/GameObjectStateEventCases.cs') (Resolve-Path 'recovery/tests/GameObjectModelConditionCases.cs') (Resolve-Path 'recovery/tests/GameObjectMotionCleanupCases.cs') (Resolve-Path 'recovery/tests/GameObjectSpecialPowerBaseCases.cs') (Resolve-Path 'recovery/tests/GameObjectSpecialPowerDerivedCases.cs') (Resolve-Path 'recovery/tests/GameObjectPowerRoutingCases.cs') (Resolve-Path 'recovery/tests/GameObjectSpecialAbilityCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityWeaponCases.cs') (Resolve-Path 'recovery/tests/GameObjectPowerCreationCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityMovementCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityStateCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityRoutingCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityResourceCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityToggleCases.cs') (Resolve-Path 'recovery/tests/GameObjectAbilityFinishCases.cs') (Resolve-Path 'recovery/tests/GameObjectAttachCases.cs') (Resolve-Path 'recovery/tests/GameObjectUtilityCases.cs') (Resolve-Path 'recovery/tests/GameObjectAreaCases.cs') (Resolve-Path 'recovery/tests/GameObjectSupportCases.cs') (Resolve-Path 'recovery/tests/GameObjectObjectControlCases.cs') (Resolve-Path 'recovery/tests/GameObjectSlowDeathCases.cs') (Resolve-Path 'recovery/tests/GameObjectContainCoreCases.cs') (Resolve-Path 'recovery/tests/GameObjectContainRemainingCases.cs') (Resolve-Path 'recovery/tests/GameObjectDrawBeamCases.cs') (Resolve-Path 'recovery/tests/GameObjectDrawModelCases.cs') *> "$report/host-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$report/host-$mode.txt"; throw 'GameObject oracle build failed' }
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/GameObjectOracle.exe.config"
    & "$out/GameObjectOracle.exe" "$out/BinaryAssetBuilder.Tokenizer.dll" *> "$report/oracle-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$report/oracle-$mode.txt" -Tail 20; throw "GameObject differential failed: $mode" }
    $summary = (Get-Content "$report/oracle-$mode.txt" -Tail 1)
    $summaries += @{ mode = $mode; summary = $summary }
    Write-Host $summary
}
@{ succeeded = $true; sourceCommit = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID; scope = 'complete three-type Laser/Tracer/Stream Draw with Shader28 and four polymorphic shader variants, Color4f16 and nullable ParticleSystemList8; complete five remaining Engineer/Infiltrator/Transport/HordeTransport/Horde Contain classes; complete eight-type Open/Garrison/HordeGarrison/Contest/Tunnel/TunnelTeleport/Heal/Slaughter Contain core, passenger128/roster8/member8 and all inherited dependencies; complete five-type SlowDeath/destruction group with nested phase strong/string lists, nullable model/status masks, Geometry48/Shape32/Contact20 and full DieMux44; complete thirteen-type remaining object-control group with full Capture20/Laser44, tuple120/strong Upgrade list, Shadow60, Particle20 and UpgradeMap8; complete eight-type effect/locomotor support group with full Upgrade36/Die52, strong modifier/emotion lists, Query12 filters and Event24; complete six-type area damage/targeting group with inline ranges, full Filter112/validation56 and full FireWeapon32 parent; complete thirteen-type utility group including PropDraw/Naval/ReplaceDraw/Stances/Squish/AreaRestrict/Minefield/LiftObject/ClusterBomb; complete Attach336/Steal336/Leech336, MoneyGain348/validation56 and BoobyTrap404 (nullable 8-byte damage/death masks, full filters and strong modifier list); complete ToggleStatus252/nugget280/inline AI148/nullable weapon28/ModuleInfo12, GiveUpgrade256/ReplaceObject260-Nugget12/ShieldSphere264/SpecialDisguise264-TerrainData8; 88 root attributes; thirty-eight child nodes including module containers with 31 complete base-only types plus all seven remaining Body types (all nine XSD Body choices), full DamageRangeModifier and nullable ShieldArmor; all six ClientUpdate types including exact-case Key hash lists and ushort UnitWeight; complete TiberiumThief344/CreateAndEnter504 (inline223/456 and nullable vector with full OCL500 parent) and complete ActivateAbility248/RadarScanMap248 (full structured ModuleInfo12 and intersection128 lists) and complete seven-type Give/Reconstitute/Dominate/Level/Clone/Replace/Assemble SpecialAbility derivations (nullable filters, typed GameObject lists, ReplaceOptions7 and full parent) and complete four-type SwitchLocomotors/EjectPassengers/Teleport/TeleportToCaster SpecialAbility derivations (nullable Vector3 and full parent) and complete OCL500/Movement488 SpecialPower derivations and complete four-type WeaponFire/DelayedWeaponFire/Beam/ObjectStatus SpecialAbility derivations and complete SpecialAbility240/Capture/OCLMonitor (45 root fields, Options31, Conditions3, full inline223 masks and nullable Anim12) and complete SpecialPowerUpdate24/ActivateModule/SpecialPowerDispatch routing (structured lists and exact per-item Token inheritance) and complete twenty-three-type SpecialPower derivation family (full inherited parent, StringLists, nullable Filters, strong Upgrade lists, status masks and percent fields) and complete SpecialPower452/RadarHack464/RadarJam460 family (all parent attributes, full inline filters, StringList8, nullable SoundOrEvaEvent8 and Sound reference4, complete inheritance) and complete PowerTimer/RandomModel/MonitorCondition family (fifteen-word model flags, literal StringList8, OCL/time/action flags) and complete RunOffMap/DestroyObjects/GrapplingHook family (String8 and Behavior parent, inline Filter112, full nullable status/filter and weapons) and complete StoreCurrentState/LUAEventList family (Time floats, exact-case String8, correct Update/Behavior parents) and complete TerrainResource/DistributeEnergyToAllies family (full Capture20, percentage, uint interval, StringList8 and nullable filter) and complete WallHub/Enclose family (six typed template IDs, typed SegmentPattern list, numeric Options, inline Filter112) and complete Tiberium Growth/Crystal/Field family (full inherited DieMux44, packed ARGB RadarColor, typed ID and two nullable filters) and complete EMP/Suppression family (nullable Color3f12 with full nested Tokens, plain suppressability and status mask) and complete StealthDetector/Invisibility family (full Upgrade, KindOf masks, exact nullable voice hashes and inline Filter112) and complete economy/power family (PowerUpdate, PlayerPowerManager, AutoDeposit with three MoneyTransaction8 lists, DistributedMoney) and complete Wall/Bridge/Placement family (full inline masks/Filter112, nullable resources/float, inline Shape48) and complete FindCover/Notify/HitReaction family (full references, nullable Filter/status56 and DeathBits) and complete BuildingBehavior/StructureUnpack/StructureSell family (four inline strings, nullable strong audio reference, time and single model enum) and complete QueueDock/ParkingPlace family (five default-empty strings, nullable Filter112 and Vector3, single model enum, parking times and packed booleans) and complete SlaveWatcher family (strong Upgrade references and packed booleans), with strengthened existing SpawnCrate strong-list/nullable Filter112 regression and complete three-type Spawn/OCL Update family (full Upgrade and nullable DieMux44, typed GameObject list, Damage/Death/monitor masks, signed Amount and frame delays) and complete four-type assistance/placement family (full HintObjectMapping list/nullable PlayerTemplate/PlacementRestriction/nested Filter/Vector3, placement hints and attack/special-power assist responses) and complete seven-type Dock/Ore/gathering family (full Dock, SupplyCenter/SupplyWarehouse dock, OreNode/OreMiner, GatherSlaves/GatherEject with nullable Filter/status and inline vectors/Filter) and complete eight-type AI Update family (full AI/Jet/Horde/SupplyTruck/Transport/Animal/Wander/DeployStyle, UnitChooser40/BaseChooser/VisionRange/Filter) and complete eight-type Crate collision/spawn family (base/Money/Unit/Veterancy/Attribute/Random/Salvage/Spawn, strong reference list and nullable BlockingUnits) and complete AutoHealBehavior/RepairUpdate/InitiateRepairDie/HealCrateCollide (full Upgrade/Crate/DieMux, nullable validation/filter, inline percent/audio) and complete WeaponSetUpdate (four slots, sorted WeaponData, full TurretAI/Chooser/VisionRange/ObjectFilter) and complete DamageState and three reactive fire modules and complete Physics/Bezier/Missile group and complete four-type Projectile/Dynamics/Freefall/Collide group (four Movement choices, full Volume/five shapes/Quaternion) and complete three-type beam state group and seven-type firing tracking group and eight-type radar visibility Capture group and TechUpgradeReceiver and SlavedUpdate and four remaining Create modules and three-type Production Update/exit family and twenty-two-type named Upgrade group and seven-type shared Die family and six lifecycle Updates and four sound/EVA selectors and AnimationSound/ModelConditionAudioLoop playback groups (nullable masks/range, signed Frame list and inline masks); unknown fallback (all 322 known Marshal module types implemented; 58 Token-only fallback types retained); complete empty ReplaceModule/InheritableModule and complete SkirmishAIInformation dependency group (64 fields, contextual power lists, base criteria, nullable references and filters); AudioArrayVoice (all three lists), AudioArraySound and EvaEventArray; complete raw Geometry with PlacementShape/Shape/ContactPoint lists and Vector2/Vector3; CrusherInfo/ExtraCrushLevels and repeated ProjectedBuildabilityInfo with nullable RadiusY, all masks and AllowedObjectFilter; GameDependency masks/typed lists/ObjectFilter; ObjectResourceInfo/BuildCost (pointer StringLists on XSD-valid text surface); AutoResolve family with nested upgrade lists including visual metadata, leaf structures, repeated lists, ArmorSet and LocomotorSet; other children rejected'; platforms = @(0,1,2,99); modes = $summaries; sdkStatus = 'not run; original route retained' } | ConvertTo-Json -Depth 5 | Set-Content "$report/validation.json"











