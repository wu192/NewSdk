$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$out = Join-Path $env:RUNNER_TEMP 'ra3-recovered-weapon'
$evidence = 'recovery/evidence/weapon'
New-Item -ItemType Directory -Force $out, $evidence | Out-Null
@{ succeeded = $false; sourceCommit = $env:GITHUB_SHA; scope = 'WeaponTemplate scalar/duration/resource subset, not full type or DLL' } | ConvertTo-Json | Set-Content "$evidence/validation.json"
Copy-Item SDK/tools/*.dll $out
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
foreach ($mode in @('Od', 'O2')) {
    cl /nologo /clr /LD /MD /EHa /$mode /fp:precise /AI"$out" /Fo"$out/" /Fe"$out/Recovered.Weapon.dll" recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/WeaponNugget.cpp recovery/src/Native/WeaponTemplate.cpp recovery/src/Bridge/WeaponCompiler.cpp *> "$evidence/build-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/build-$mode.txt"; throw "Weapon C++/CLI build failed: $mode" }
    & $csc /nologo /target:library /platform:x86 /r:"$out/BinaryAssetBuilder.Core.dll" /r:"$out/Recovered.Weapon.dll" /out:"$out/Recovered.WeaponPlugin.dll" (Resolve-Path 'recovery/src/Plugin/WeaponPlugin.cs') *> "$evidence/plugin-build-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/plugin-build-$mode.txt"; throw 'Weapon plugin build failed' }
    & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /unsafe /platform:x86 /r:"$out/Recovered.Weapon.dll" /r:"$out/BinaryAssetBuilder.Core.dll" /out:"$out/WeaponOracle.exe" (Resolve-Path 'recovery/tests/WeaponOracle.cs') (Resolve-Path 'recovery/tests/WeaponScatterOracle.cs') (Resolve-Path 'recovery/tests/WeaponNuggetOracle.cs') (Resolve-Path 'recovery/tests/WeaponProjectileOracle.cs') (Resolve-Path 'recovery/tests/WeaponActivationOracle.cs') (Resolve-Path 'recovery/tests/WeaponScalarNuggetOracle.cs') (Resolve-Path 'recovery/tests/WeaponEffectOracle.cs') (Resolve-Path 'recovery/tests/WeaponFinalNuggetOracle.cs') *> "$evidence/host-build-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/host-build-$mode.txt"; throw 'Weapon oracle build failed' }
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/WeaponOracle.exe.config"
    & "$out/WeaponOracle.exe" (Resolve-Path 'SDK/tools/BinaryAssetBuilder.Tokenizer.dll') "$out/Recovered.WeaponPlugin.dll" *> "$evidence/oracle-$mode.txt"
    if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/oracle-$mode.txt" -Tail 20; throw "Weapon differential failed: $mode" }
    Get-Content "$evidence/oracle-$mode.txt" -Tail 1
}
@{ succeeded = $true; sourceCommit = $env:GITHUB_SHA; scope = '91 root attributes including two ObjectStatus masks, four RangeDuration blocks, two SoundOrEvaEvent blocks and WeaponAiHintInfo plus IncompatibleAttributeModifier reference list and three complete ObjectFilter blocks; complete ScatterRadiusVsType list with inline ObjectFilter and ModelCondition masks; Nuggets supports DamageNugget, DOTNugget, GrapplingHookNugget, LeechPercentMaxHealthDamageNugget, LeechHealthDamageNugget LineDamageNugget, DamageAndSpawnNugget FireOnObjectsNugget and DamageContainedNugget with common base and ScalarInfo; ProjectileNugget, ScatterProjectileNugget WeaponOCLNugget and SeedTiberiumNugget including VeteranProjectile list; SpecialPowerNugget, InformTargetNugget, ContainedObjectAttackNugget, SlavesAttackNugget and ReportWeaponFiredStatNugget; ActivateCircularDamage, ActivateLiftObject, ActivateLinearDamage, ActivateStream, ActivateLaser and TerrainCrater; Attach, SpendStolenTiberium, SecondaryDamage, StripMaxHealthPercent, Suppression and LuaEvent; Paralyze, InformationWarfare, MetaImpact, TintObjects and AttributeModifier; all 35 explicit nugget branches; unknown/missing/zero TypeId emits the 40-byte common base with zero output tag and null polymorphic token offset; unsupported fields remain rejected; full matrix also exercises BAB-style annotations and formal Weapon plugin entry/metadata'; componentSucceeded = $true; sdkProbeSucceeded = $null; sdkProbeStatus = 'not run for this source; batched separately'; modes = @('Od','O2') } | ConvertTo-Json | Set-Content "$evidence/validation.json"




