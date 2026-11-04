param([Parameter(Mandatory=$true)][string]$CompilerBuildDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$env:NEWSdk_PLUGIN_TEST_SCHEMA=(Resolve-Path "$PSScriptRoot/../../SDK/Schemas/xsd/CnC3Types.xsd").Path
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerBuildDirectory=(Resolve-Path $CompilerBuildDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite GameObject transcript evidence $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-gameobject-'+[Guid]::NewGuid().ToString('N'))
$oracle=Join-Path $work 'original'
New-Item -ItemType Directory -Force $oracle|Out-Null
$state=@{schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';scope='complete existing GameObject fixture matrix against isolated original transcript; product bridge, not MOD/Core execution';modes=@()}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $fixturePaths=@(
        'recovery/tests/PluginSchemaBoundary.cs',
        'recovery/tests/GameObjectOracle.cs',
        'recovery/tests/GameObjectCases.cs',
        'recovery/tests/GameObjectKindOf.cs',
        'recovery/tests/GameObjectWeaponSetCases.cs',
        'recovery/tests/GameObjectHealingCases.cs',
        'recovery/tests/GameObjectCrateCases.cs',
        'recovery/tests/GameObjectAIUpdateCases.cs',
        'recovery/tests/GameObjectEconomyCases.cs',
        'recovery/tests/GameObjectAssistCases.cs',
        'recovery/tests/GameObjectSpawnUpdateCases.cs',
        'recovery/tests/GameObjectSpawnCrateWatcherCases.cs',
        'recovery/tests/GameObjectQueueParkingCases.cs',
        'recovery/tests/GameObjectBuildingStructureCases.cs',
        'recovery/tests/GameObjectEconomyPowerCases.cs',
        'recovery/tests/GameObjectStructureWallCases.cs',
        'recovery/tests/GameObjectCombatResponseCases.cs',
        'recovery/tests/GameObjectStatusEffectsCases.cs',
        'recovery/tests/GameObjectStealthVisionCases.cs',
        'recovery/tests/GameObjectTiberiumCases.cs',
        'recovery/tests/GameObjectWallEncloseCases.cs',
        'recovery/tests/GameObjectResourceAlliesCases.cs',
        'recovery/tests/GameObjectStateEventCases.cs',
        'recovery/tests/GameObjectModelConditionCases.cs',
        'recovery/tests/GameObjectMotionCleanupCases.cs',
        'recovery/tests/GameObjectSpecialPowerBaseCases.cs',
        'recovery/tests/GameObjectSpecialPowerDerivedCases.cs',
        'recovery/tests/GameObjectPowerRoutingCases.cs',
        'recovery/tests/GameObjectSpecialAbilityCases.cs',
        'recovery/tests/GameObjectAbilityWeaponCases.cs',
        'recovery/tests/GameObjectPowerCreationCases.cs',
        'recovery/tests/GameObjectAbilityMovementCases.cs',
        'recovery/tests/GameObjectAbilityStateCases.cs',
        'recovery/tests/GameObjectAbilityRoutingCases.cs',
        'recovery/tests/GameObjectAbilityResourceCases.cs',
        'recovery/tests/GameObjectAbilityToggleCases.cs',
        'recovery/tests/GameObjectAbilityFinishCases.cs',
        'recovery/tests/GameObjectAttachCases.cs',
        'recovery/tests/GameObjectUtilityCases.cs',
        'recovery/tests/GameObjectAreaCases.cs',
        'recovery/tests/GameObjectSupportCases.cs',
        'recovery/tests/GameObjectObjectControlCases.cs',
        'recovery/tests/GameObjectSlowDeathCases.cs',
        'recovery/tests/GameObjectContainCoreCases.cs',
        'recovery/tests/GameObjectContainRemainingCases.cs',
        'recovery/tests/GameObjectDrawBeamCases.cs',
        'recovery/tests/GameObjectDrawModelCases.cs'
    )
    $existingScript=Get-Content "$PSScriptRoot/TestGameObject.ps1" -Raw
    $existingPaths=@([regex]::Matches($existingScript,"Resolve-Path '(recovery/tests/[^']+\.cs)'")|ForEach-Object{$_.Groups[1].Value})
    if(($existingPaths -join "`n") -cne ($fixturePaths -join "`n")){throw 'Existing GameObject fixture source set changed; update the full product replay manifest explicitly'}
    $sources=@($fixturePaths|ForEach-Object{(Resolve-Path $_).Path})
    $sources+=(Resolve-Path 'recovery/tests/GameObjectTranscript.cs').Path
    $state.fixtureInputs=@($sources|ForEach-Object{@{path=([IO.Path]::GetFullPath($_).Substring($root.Length+1).Replace('\','/'));sha256=(Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $state.pluginSchemaInputs=@(Get-ChildItem "$root/SDK/Schemas/xsd" -Recurse -Filter *.xsd -File|Sort-Object FullName|ForEach-Object{@{path=$_.FullName.Substring($root.Length+1).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    # PointerStringLists reads schema data. Keep this separate from both DLL
    # directories so fixture data cannot introduce a business assembly search path.
    $fixtureData=Join-Path $work 'fixture-data'
    New-Item -ItemType Directory -Force "$fixtureData/SDK/Schemas/xsd/Includes"|Out-Null
    Copy-Item SDK/Schemas/xsd/Includes/Base.xsd "$fixtureData/SDK/Schemas/xsd/Includes/Base.xsd"
    $state.schemaInput=@{path='SDK/Schemas/xsd/Includes/Base.xsd';sha256=(Get-FileHash SDK/Schemas/xsd/Includes/Base.xsd -Algorithm SHA256).Hash.ToLowerInvariant()}
    Copy-Item SDK/tools/*.dll $oracle
    $state.originalInputs=@(Get-ChildItem $oracle -Filter *.dll -File|Sort-Object Name|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $config='<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'
    $originalHost=Join-Path $oracle 'GameObjectTranscriptOracle.exe'
    & $csc /nologo /codepage:65001 /platform:x86 /define:GO_TRANSCRIPT_ORACLE /r:System.Xml.dll "/r:$oracle/BinaryAssetBuilder.Core.dll" "/out:$originalHost" $sources *> "$out/original-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/original-build.log";throw 'Original full GameObject transcript host compilation failed'}
    $config|Set-Content "$originalHost.config"
    $transcript=Join-Path $out 'original-gameobject.bin.gz'
    Push-Location $fixtureData
    try {& $originalHost "$oracle/BinaryAssetBuilder.Tokenizer.dll" $transcript *> "$out/original-runtime.log"; $code=$LASTEXITCODE}
    finally {Pop-Location}
    if($code -ne 0 -or !(Select-String -LiteralPath "$out/original-runtime.log" -SimpleMatch 'PASS GameObject full transcript record:' -Quiet)){Get-Content "$out/original-runtime.log" -Tail 60;throw 'Original complete GameObject matrix failed'}
    $state.originalTranscriptSha256=(Get-FileHash $transcript -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach($mode in @('Debug','Release')) {
        $clean=Join-Path $work $mode
        New-Item -ItemType Directory -Force $clean|Out-Null
        $bin=Join-Path $CompilerBuildDirectory "$mode/bin"
        $buildReceipt=Get-Content (Join-Path $CompilerBuildDirectory "$mode/build.json") -Raw|ConvertFrom-Json
        if($buildReceipt.sourceCommit -cne $state.sourceCommit -or $buildReceipt.configuration -cne $mode){throw "Product $mode receipt source/configuration does not match this validation"}
        $builtExe=Join-Path $bin 'NewModCompiler.exe'
        if((Get-FileHash $builtExe -Algorithm SHA256).Hash.ToLowerInvariant() -cne $buildReceipt.exeSha256){throw "Product $mode executable differs from its build receipt"}
        $validationRun=Initialize-CompilerValidationRun (Join-Path $CompilerBuildDirectory $mode) $clean $mode $state.sourceCommit
        $exe=Join-Path $clean 'NewModCompiler.exe'
        $replayHost=Join-Path $clean 'GameObjectProductReplay.exe'
        & $csc /nologo /codepage:65001 /platform:x86 /define:GO_TRANSCRIPT_PRODUCT /r:System.Xml.dll "/r:$exe" "/out:$replayHost" $sources *> "$out/$mode-build.log"
        if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-build.log";throw "Product GameObject $mode replay host compilation failed"}
        $config|Set-Content "$replayHost.config"
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll','GameObjectProductReplay.exe','GameObjectProductReplay.exe.config')
        if(@(Get-ChildItem $clean -Recurse|Where-Object{$_.PSIsContainer -or $_.Name -notin $allowed}).Count -ne 0){throw 'Unexpected file in isolated GameObject product replay directory'}
        Push-Location $fixtureData
        try {& $validationRun.exe $replayHost $transcript *> "$out/$mode-runtime.log"; $code=$LASTEXITCODE}
        finally {Pop-Location}
        if($code -ne 0 -or !(Select-String -LiteralPath "$out/$mode-runtime.log" -SimpleMatch 'PASS GameObject full transcript replay:' -Quiet)){Get-Content "$out/$mode-runtime.log" -Tail 60;throw "Complete product GameObject $mode matrix failed"}
        Complete-CompilerValidationRun $validationRun
        $state.modes+=@{validationHostSha256=$validationRun.validationHostSha256;configuration=$mode;passed=$true;exeSha256=$validationRun.productExeSha256;platforms=@(0,1,2,99);fullExistingFixtureSet=$true;originalBusinessDllsAbsent=$true;summary=@(Select-String -LiteralPath "$out/$mode-runtime.log" -SimpleMatch 'PASS GameObject full transcript replay:'|ForEach-Object{$_.Line})}
    }
    $state.status='passed';$global:LASTEXITCODE=0
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$state|ConvertTo-Json -Depth 8|Set-Content "$out/validation.json";Pop-Location}

