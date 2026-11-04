$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { [guid]::NewGuid().ToString() }
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git rev-parse HEAD).Trim() }
$evidence = 'recovery/evidence/xml-ai'
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$out = Join-Path $tempRoot ("recovered-xml-ai-" + $runId)
New-Item -ItemType Directory -Force $out, $evidence | Out-Null
$result = [ordered]@{
    succeeded = $false
    sourceCommit = $sourceCommit
    runId = $runId
    originalDllSha256 = ''
    scope = 'XmlCompiler AI component; six root attributes, MicroManager, State heuristics, Budget; explicit remaining child boundaries'
    modes = @()
    status = 'started'
}
function Save-Validation { $result | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 "$evidence/validation.json" }
Save-Validation
try {
    Copy-Item SDK/tools/*.dll $out
    $original = (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path
    $result.originalDllSha256 = (Get-FileHash $original -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($result.originalDllSha256 -ne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6') { throw 'Original XmlCompiler DLL fingerprint mismatch' }
    $sources = @(
        'recovery/src/Native/Relocation.cpp'
        'recovery/src/Native/ScalarParsing.cpp'
        'recovery/src/Native/DataInitializers.cpp'
        'recovery/src/Native/ResourceMarshaling.cpp'
        'recovery/src/Native/EnumBitset.cpp'
        'recovery/src/Native/ObjectFilter.cpp'
        'recovery/src/Native/Xmlai.cpp'
        'recovery/src/Bridge/XmlaiCompiler.cpp'
    )
    $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $oracleSources = @((Resolve-Path 'recovery/tests/XmlaiOracle.cs').Path)
    $oracleDefines = @()
    if (Test-Path 'recovery/tests/XmlaiPersonalityCases.cs') {
        $oracleSources += (Resolve-Path 'recovery/tests/XmlaiPersonalityCases.cs').Path
        $oracleDefines += '/define:XMLAI_PERSONALITY_EXTENSION'
    }
    foreach ($mode in @('Od', 'O2')) {
        $build = "$evidence/$mode-build.txt"
        & cl /nologo /clr /LD /MD /EHa "/$mode" /fp:precise /AI"$out" /Fo"$out/" /Fe"$out/Recovered.Xmlai.dll" @sources *> $build
        if ($LASTEXITCODE -ne 0) { Get-Content $build; throw "$mode Xmlai x86 C++/CLI build failed" }
        & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:"$out/Recovered.Xmlai.dll" /r:"$out/BinaryAssetBuilder.Core.dll" /out:"$out/XmlaiOracle.exe" @oracleDefines @oracleSources *> "$evidence/$mode-host-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-host-build.txt"; throw "$mode Xmlai oracle build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/XmlaiOracle.exe.config"
        & "$out/XmlaiOracle.exe" $original *> "$evidence/$mode-oracle.txt"
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
        if ($code -ne 0) { throw "$mode Xmlai differential failed" }
        if ($platforms.Count -ne 4 -or (($platforms | ForEach-Object { $_.platform }) -join ',') -ne '0,1,2,99') { throw "$mode missing complete platform summaries" }
        foreach ($p in $platforms) { if ($p.fixtures -lt 1000 -or $p.arrayComparisons -lt 6000 -or $p.rejections -lt 30 -or $p.layoutAssertions -lt 300) { throw "$mode incomplete test coverage" } }
    }
    $result.succeeded = $true
    $result.status = 'Od/O2 four-platform component differential completed; SDK integration not claimed'
} catch {
    $result.status = $_.Exception.Message
    throw
} finally {
    Save-Validation
}
