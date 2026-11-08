param([string]$OutputDirectory = (Join-Path $env:RUNNER_TEMP 'enhancer-dozer-xml'))
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$validationPath = Join-Path $OutputDirectory 'validation.json'
$source = (git rev-parse HEAD).Trim()
$receipt = [ordered]@{
    succeeded = $false
    sourceCommit = $source
    runId = $env:GITHUB_RUN_ID
    scope = 'Win32 native Dozer, passive mind control and RepairDock production serializer/C++/CLI XML bridge; source-built Core repair expressions/schema/TypeId injection; focused legacy original-DLL three-buffer compatibility; not full SDK/MOD/game acceptance'
    core = $null
    modes = @()
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content $validationPath
$native = @(
    'recovery/src/Native/Relocation.cpp',
    'recovery/src/Native/ScalarParsing.cpp',
    'recovery/src/Native/DataInitializers.cpp',
    'recovery/src/Native/ResourceMarshaling.cpp',
    'recovery/src/Native/EnumBitset.cpp',
    'recovery/src/Native/ObjectFilter.cpp',
    'recovery/src/Native/WeaponSlotOrder.cpp',
    'recovery/src/Native/GameObject.cpp'
)
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
# Build the changed frontend once, separately from the original-DLL component
# oracle directories. Do not substitute the stock Core for semantic coverage.
$coreOut = Join-Path $OutputDirectory 'source-core'
New-Item -ItemType Directory -Force $coreOut | Out-Null
Copy-Item SDK/tools/*.dll $coreOut
& dotnet build recovery/src/Managed/Core/BinaryAssetBuilder.Core.csproj -c Release -p:PlatformTarget=x86 -o "$coreOut/build" --nologo *> "$coreOut/build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$coreOut/build.log" -Tail 70; throw 'Source Core compilation failed' }
Copy-Item "$coreOut/build/BinaryAssetBuilder.Core.dll", "$coreOut/build/Mvp.Xml.dll" $coreOut -Force
$coreSmoke = Join-Path $coreOut 'GameObjectRepairDockCore.exe'
& $csc /nologo /platform:x86 "/r:$coreOut/BinaryAssetBuilder.Core.dll" /r:System.Xml.dll "/out:$coreSmoke" (Resolve-Path 'recovery/tests/GameObjectRepairDockCore.cs').Path *> "$coreOut/smoke-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$coreOut/smoke-build.log"; throw 'RepairDock Core smoke compilation failed' }
'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$coreSmoke.config"
& $coreSmoke *> "$coreOut/test.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$coreOut/test.log"; throw 'RepairDock source Core contract failed' }
$receipt.core = [ordered]@{
    configuration = 'Release/net20/x86'
    result = (Get-Content "$coreOut/test.log" -Raw).Trim()
    sha256 = (Get-FileHash "$coreOut/BinaryAssetBuilder.Core.dll" -Algorithm SHA256).Hash.ToLowerInvariant()
}
foreach ($mode in @('Od', 'O2')) {
    $modeTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $out = Join-Path $OutputDirectory $mode
    $fixtures = Join-Path $out 'fixtures'
    $nativeObjects = Join-Path $out 'native-obj'
    $bridgeObjects = Join-Path $out 'bridge-obj'
    $weaponObjects = Join-Path $out 'weapon-obj'
    New-Item -ItemType Directory -Force $out, $fixtures, $nativeObjects, $bridgeObjects, $weaponObjects | Out-Null
    Copy-Item SDK/tools/*.dll $out
    $exe = Join-Path $out 'GameObjectDozerNative.exe'
    & cl /nologo /MD /EHsc /std:c++17 /utf-8 /fp:precise "/$mode" "/Irecovery/src/Native" "/Fo$nativeObjects/" "/Fe$exe" @native recovery/tests/GameObjectDozerNative.cpp *> "$out/native-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/native-build.log"; throw "Native serializer compilation failed: $mode" }
    & $exe $fixtures *> "$out/native-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/native-test.log"; throw "Native serializer contract failed: $mode" }
    # Reuse the production native objects built above; only the new test main
    # differs. Keep the existing /Od and /O2 matrix rather than adding jobs.
    $sharedNativeObjects = @(Get-ChildItem "$nativeObjects/*.obj" | Where-Object {
        $_.Name -notin @('GameObjectDozerNative.obj', 'GameObjectPassiveMindControlNative.obj', 'GameObjectRepairDockNative.obj')
    } | ForEach-Object { $_.FullName })
    $mindControlNative = Join-Path $out 'GameObjectPassiveMindControlNative.exe'
    & cl /nologo /MD /EHsc /std:c++17 /utf-8 /fp:precise "/$mode" "/Irecovery/src/Native" "/Fo$nativeObjects/" "/Fe$mindControlNative" recovery/tests/GameObjectPassiveMindControlNative.cpp @sharedNativeObjects *> "$out/mind-control-native-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/mind-control-native-build.log"; throw "Passive mind control native compilation failed: $mode" }
    & $mindControlNative $fixtures *> "$out/mind-control-native-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/mind-control-native-test.log"; throw "Passive mind control native contract failed: $mode" }
    $repairDockNative = Join-Path $out 'GameObjectRepairDockNative.exe'
    & cl /nologo /MD /EHsc /std:c++17 /utf-8 /fp:precise "/$mode" "/Irecovery/src/Native" "/Fo$nativeObjects/" "/Fe$repairDockNative" recovery/tests/GameObjectRepairDockNative.cpp @sharedNativeObjects *> "$out/repair-dock-native-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/repair-dock-native-build.log"; throw "RepairDock native compilation failed: $mode" }
    & $repairDockNative $fixtures *> "$out/repair-dock-native-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/repair-dock-native-test.log"; throw "RepairDock native contract failed: $mode" }
    $dll = Join-Path $out 'Recovered.GameObject.dll'
    & cl /nologo /clr /LD /MD /EHa /std:c++17 /utf-8 /fp:precise "/$mode" "/AI$out" "/Fo$bridgeObjects/" "/Fe$dll" @native recovery/src/Bridge/GameObjectCompiler.cpp *> "$out/bridge-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/bridge-build.log"; throw "XML bridge compilation failed: $mode" }
    $smoke = Join-Path $out 'GameObjectDozerSmoke.exe'
    & $csc /nologo /platform:x86 "/r:$dll" "/r:$out/BinaryAssetBuilder.Core.dll" /r:System.Xml.dll "/out:$smoke" (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs').Path (Resolve-Path 'recovery/tests/GameObjectDozerSmoke.cs').Path *> "$out/smoke-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/smoke-build.log"; throw "Managed smoke compilation failed: $mode" }
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$smoke.config"
    & $smoke $fixtures *> "$out/bridge-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/bridge-test.log"; throw "XML bridge contract failed: $mode" }
    $repairDockSmoke = Join-Path $out 'GameObjectRepairDockSmoke.exe'
    & $csc /nologo /platform:x86 "/r:$dll" "/r:$out/BinaryAssetBuilder.Core.dll" /r:System.Xml.dll "/out:$repairDockSmoke" (Resolve-Path 'recovery/tests/GameObjectRepairDockSmoke.cs').Path *> "$out/repair-dock-smoke-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/repair-dock-smoke-build.log"; throw "RepairDock managed smoke compilation failed: $mode" }
    Copy-Item "$smoke.config" "$repairDockSmoke.config"
    & $repairDockSmoke $fixtures (Resolve-Path "$out/BinaryAssetBuilder.Tokenizer.dll").Path *> "$out/repair-dock-bridge-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/repair-dock-bridge-test.log"; throw "RepairDock XML bridge or legacy Dock compatibility failed: $mode" }
    # The complete ordinary-attack example also compiles its harmless weapon and
    # the estimate-only template through the existing production weapon bridge.
    $weaponDll = Join-Path $out 'Recovered.Weapon.dll'
    $weaponNative = @($native | Where-Object { $_ -notin @('recovery/src/Native/GameObject.cpp', 'recovery/src/Native/WeaponSlotOrder.cpp') }) + @(
        'recovery/src/Native/WeaponNugget.cpp', 'recovery/src/Native/WeaponTemplate.cpp'
    )
    & cl /nologo /clr /LD /MD /EHa /std:c++17 /utf-8 /fp:precise "/$mode" "/AI$out" "/Fo$weaponObjects/" "/Fe$weaponDll" @weaponNative recovery/src/Bridge/WeaponCompiler.cpp *> "$out/mind-control-weapon-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/mind-control-weapon-build.log"; throw "Passive mind control weapon bridge compilation failed: $mode" }
    $mindControlSmoke = Join-Path $out 'GameObjectPassiveMindControlSmoke.exe'
    & $csc /nologo /platform:x86 "/r:$dll" "/r:$weaponDll" "/r:$out/BinaryAssetBuilder.Core.dll" /r:System.Xml.dll "/out:$mindControlSmoke" (Resolve-Path 'recovery/src/Plugin/GameObjectPlugin.cs').Path (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs').Path (Resolve-Path 'recovery/tests/GameObjectPassiveMindControlSmoke.cs').Path *> "$out/mind-control-smoke-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/mind-control-smoke-build.log"; throw "Passive mind control managed smoke compilation failed: $mode" }
    Copy-Item "$smoke.config" "$mindControlSmoke.config"
    & $mindControlSmoke $fixtures (Resolve-Path "$out/BinaryAssetBuilder.Tokenizer.dll").Path (Resolve-Path 'examples/passive-mind-control/PassiveMindControlTower.xml').Path *> "$out/mind-control-bridge-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/mind-control-bridge-test.log"; throw "Passive mind control XML bridge or legacy compatibility failed: $mode" }
    $receipt.modes += [ordered]@{
        mode = $mode
        elapsedSeconds = [Math]::Round($modeTimer.Elapsed.TotalSeconds, 3)
        native = (Get-Content "$out/native-test.log" -Raw).Trim()
        bridge = (Get-Content "$out/bridge-test.log" -Raw).Trim()
        mindControlNative = (Get-Content "$out/mind-control-native-test.log" -Raw).Trim()
        mindControlBridge = (Get-Content "$out/mind-control-bridge-test.log" -Raw).Trim()
        repairDockNative = (Get-Content "$out/repair-dock-native-test.log" -Raw).Trim()
        repairDockBridge = (Get-Content "$out/repair-dock-bridge-test.log" -Raw).Trim()
        fixtures = @(Get-ChildItem $fixtures -File | ForEach-Object {
            @{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
}
$receipt.succeeded = $true
$receipt | ConvertTo-Json -Depth 8 | Set-Content $validationPath
Write-Host "PASS: Win32 Dozer/passive mind control/RepairDock serializer and XML bridge, legacy three-buffer compatibility in /Od and /O2; $source"
