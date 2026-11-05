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
    scope = 'Win32 native production serializer and C++/CLI XML bridge; not full SDK/MOD/game acceptance'
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
foreach ($mode in @('Od', 'O2')) {
    $out = Join-Path $OutputDirectory $mode
    $fixtures = Join-Path $out 'fixtures'
    $nativeObjects = Join-Path $out 'native-obj'
    $bridgeObjects = Join-Path $out 'bridge-obj'
    New-Item -ItemType Directory -Force $out, $fixtures, $nativeObjects, $bridgeObjects | Out-Null
    Copy-Item SDK/tools/*.dll $out
    $exe = Join-Path $out 'GameObjectDozerNative.exe'
    & cl /nologo /MD /EHsc /std:c++17 /utf-8 /fp:precise "/$mode" "/Irecovery/src/Native" "/Fo$nativeObjects/" "/Fe$exe" @native recovery/tests/GameObjectDozerNative.cpp *> "$out/native-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/native-build.log"; throw "Native serializer compilation failed: $mode" }
    & $exe $fixtures *> "$out/native-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/native-test.log"; throw "Native serializer contract failed: $mode" }
    $dll = Join-Path $out 'Recovered.GameObject.dll'
    & cl /nologo /clr /LD /MD /EHa /std:c++17 /utf-8 /fp:precise "/$mode" "/AI$out" "/Fo$bridgeObjects/" "/Fe$dll" @native recovery/src/Bridge/GameObjectCompiler.cpp *> "$out/bridge-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/bridge-build.log"; throw "XML bridge compilation failed: $mode" }
    $smoke = Join-Path $out 'GameObjectDozerSmoke.exe'
    & $csc /nologo /platform:x86 "/r:$dll" "/r:$out/BinaryAssetBuilder.Core.dll" /r:System.Xml.dll "/out:$smoke" recovery/tests/PluginSchemaBoundary.cs recovery/tests/GameObjectDozerSmoke.cs *> "$out/smoke-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/smoke-build.log"; throw "Managed smoke compilation failed: $mode" }
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$smoke.config"
    & $smoke $fixtures *> "$out/bridge-test.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/bridge-test.log"; throw "XML bridge contract failed: $mode" }
    $receipt.modes += [ordered]@{
        mode = $mode
        native = (Get-Content "$out/native-test.log" -Raw).Trim()
        bridge = (Get-Content "$out/bridge-test.log" -Raw).Trim()
        fixtures = @(Get-ChildItem $fixtures -File | ForEach-Object {
            @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
}
$receipt.succeeded = $true
$receipt | ConvertTo-Json -Depth 8 | Set-Content $validationPath
Write-Host "PASS: Win32 production serializer and XML bridge in /Od and /O2; $source"
