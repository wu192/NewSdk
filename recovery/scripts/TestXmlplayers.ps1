$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { [guid]::NewGuid().ToString() }
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git rev-parse HEAD).Trim() }
$evidence = 'recovery/evidence/xml-players'
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$out = Join-Path $tempRoot ("recovered-xml-players-" + $runId)
New-Item -ItemType Directory -Force $out, $evidence | Out-Null
$result = [ordered]@{
    succeeded = $false; sourceCommit = $sourceCommit; runId = $runId; originalDllSha256 = ''
    scope = 'Fourteen XmlCompiler player/formation/skirmish roots: complete fields/lists/filters, inherited AIState heuristics; components only, SDK integration pending'
    modes = @(); status = 'started'; sources = @()
}
function Save-Validation { $result | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 "$evidence/validation.json" }
Save-Validation
try {
    Copy-Item SDK/tools/*.dll $out
    $original = (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path
    $result.originalDllSha256 = (Get-FileHash $original -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($result.originalDllSha256 -ne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6') { throw 'Original XmlCompiler DLL fingerprint mismatch' }
    $sources = @(
        'recovery/src/Native/Relocation.cpp', 'recovery/src/Native/ScalarParsing.cpp',
        'recovery/src/Native/DataInitializers.cpp', 'recovery/src/Native/ResourceMarshaling.cpp',
        'recovery/src/Native/EnumBitset.cpp', 'recovery/src/Native/ObjectFilter.cpp',
        'recovery/src/Native/Xmlai.cpp', 'recovery/src/Native/Xmlplayers.cpp',
        'recovery/src/Bridge/XmlplayersCompiler.cpp'
    )
    $inputs = @($sources)
    $inputs += @(Get-ChildItem 'recovery/src/Native/Xmlplayers*.inc','recovery/src/Native/Xmlai*.inc','recovery/src/Native/Xmlplayers.h','recovery/src/Native/Xmlai.h','recovery/src/Bridge/XmlNodeAdapter.h' -File | ForEach-Object { $_.FullName })
    $oracleSources = @((Resolve-Path 'recovery/tests/XmlplayersOracle.cs').Path)
    $oracleSources += @(Get-ChildItem 'recovery/tests/Xmlplayers*Cases.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
    $inputs += $oracleSources
    foreach ($inputFile in $inputs) { $result.sources += [ordered]@{ path = $inputFile; sha256 = (Get-FileHash $inputFile -Algorithm SHA256).Hash.ToLowerInvariant() } }
    $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    foreach ($mode in @('Od', 'O2')) {
        & cl /nologo /std:c++17 /clr /LD /MD /EHa "/$mode" /fp:precise /AI"$out" /Fo"$out/" /Fe"$out/Recovered.Xmlplayers.dll" @sources *> "$evidence/$mode-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-build.txt"; throw "$mode Xmlplayers x86 C++/CLI build failed" }
        & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /codepage:65001 /platform:x86 /r:System.Core.dll /r:System.Web.Extensions.dll /r:"$out/Recovered.Xmlplayers.dll" /r:"$out/BinaryAssetBuilder.Core.dll" /out:"$out/XmlplayersOracle.exe" @oracleSources *> "$evidence/$mode-host-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-host-build.txt"; throw "$mode Xmlplayers oracle build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/XmlplayersOracle.exe.config"
        & "$out/XmlplayersOracle.exe" $original "$evidence/$mode-result.json" *> "$evidence/$mode-oracle.txt"
        $code = $LASTEXITCODE
        $platforms = @()
        foreach ($line in (Get-Content "$evidence/$mode-oracle.txt")) {
            if ($line -match '^PASS platform=(\d+) fixtures=(\d+) arrayComparisons=(\d+) rejections=(\d+) layoutAssertions=(\d+)$') {
                $platforms += [ordered]@{ platform = [int]$Matches[1]; fixtures = [int]$Matches[2]; arrayComparisons = [int]$Matches[3]; rejections = [int]$Matches[4]; layoutAssertions = [int]$Matches[5] }
            }
        }
        $result.modes += [ordered]@{ mode = "/$mode"; exitCode = $code; platforms = $platforms; log = "$mode-oracle.txt" }
        Save-Validation
        Get-Content "$evidence/$mode-oracle.txt"
        if ($code -ne 0) { throw "$mode Xmlplayers differential failed" }
        $oracleResult = Get-Content "$evidence/$mode-result.json" -Raw | ConvertFrom-Json
        if (!$oracleResult.succeeded) { throw "$mode Oracle did not record success" }
        if ($platforms.Count -ne 4 -or (($platforms | ForEach-Object { $_.platform }) -join ',') -ne '0,1,2,99') { throw "$mode missing complete platform summaries" }
        foreach ($p in $platforms) { if ($p.fixtures -lt 1000 -or $p.arrayComparisons -lt 3000 -or $p.rejections -lt 30 -or $p.layoutAssertions -lt 300) { throw "$mode incomplete player coverage" } }
    }
    $result.succeeded = $true
    $result.status = 'Od/O2 four-platform original DLL three-buffer differential completed; SDK integration not claimed'
} catch { $result.status = $_.Exception.Message; throw }
finally { Save-Validation }
