param(
    [string]$EvidenceDirectory = 'recovery/evidence/xml-dynamics'
)
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$evidence = $EvidenceDirectory
New-Item -ItemType Directory -Force $evidence | Out-Null
$evidence = (Resolve-Path $evidence).Path
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$out = Join-Path $tempRoot ('ra3-xml-dynamics-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $out | Out-Null
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git rev-parse HEAD).Trim() }
$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') }
$summary = [ordered]@{
    succeeded = $false
    sourceCommit = $sourceCommit
    runId = $runId
    scope = 'XmlCompiler dynamics single-root component; original ProcessInstance three buffers; SDK integration pending'
    platforms = @(0,1,2,99)
    modes = @()
    status = 'started'
}
$validation = Join-Path $evidence 'validation.json'
$summary | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $validation
try {
    if ($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x86') { throw 'Use a VS2022 x86 developer shell' }
    Copy-Item SDK/tools/*.dll $out
    $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $sources = @(
        'recovery/src/Native/Relocation.cpp',
        'recovery/src/Native/ScalarParsing.cpp',
        'recovery/src/Native/DataInitializers.cpp',
        'recovery/src/Native/EnumBitset.cpp',
                'recovery/src/Native/ResourceMarshaling.cpp',
        'recovery/src/Bridge/XmldynamicsCompiler.cpp'
    )
    # Keep the component and combined-link builds on the same formal dynamics sources.
    $sources += @(Get-ChildItem 'recovery/src/Native/Xmldynamics*.cpp' -File | Sort-Object Name | ForEach-Object { $_.FullName })
    $oracleSources = @((Resolve-Path 'recovery/tests/XmldynamicsOracle.cs').Path)

    foreach ($mode in @('Od','O2')) {
        $modeOut = Join-Path $out $mode
        New-Item -ItemType Directory -Force $modeOut | Out-Null
        Copy-Item "$out/*.dll" $modeOut
        $buildLog = Join-Path $evidence "$mode-build.txt"
        & cl /nologo /clr /LD /MD /EHa /std:c++17 "/$mode" /fp:precise "/AI$modeOut" "/Fo$modeOut/" "/Fe$modeOut/Recovered.Xmldynamics.dll" @sources *> $buildLog
        if ($LASTEXITCODE -ne 0) { Get-Content $buildLog; throw "$mode C++/CLI build failed" }
        $hostLog = Join-Path $evidence "$mode-host-build.txt"
        & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$modeOut/Recovered.Xmldynamics.dll" "/r:$modeOut/BinaryAssetBuilder.Core.dll" "/out:$modeOut/XmldynamicsOracle.exe" @oracleSources *> $hostLog
        if ($LASTEXITCODE -ne 0) { Get-Content $hostLog; throw "$mode oracle host build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$modeOut/XmldynamicsOracle.exe.config"
        $oracleLog = Join-Path $evidence "$mode-oracle.txt"
        $resultPath = Join-Path $evidence "$mode-result.json"
        & "$modeOut/XmldynamicsOracle.exe" (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path $resultPath *> $oracleLog
        $code = $LASTEXITCODE
        Get-Content $oracleLog
        if ($code -ne 0) { throw "$mode original ProcessInstance differential failed (exit $code)" }
        $result = Get-Content -Raw $resultPath | ConvertFrom-Json
        if (-not $result.succeeded -or $result.platforms.Count -ne 4) { throw "$mode incomplete matrix" }
        foreach ($platform in @(0,1,2,99)) {
            $row = @($result.platforms | Where-Object { $_.platform -eq $platform })
            if ($row.Count -ne 1 -or $row[0].fixtures -le 0 -or $row[0].arrayComparisons -ne 6*$row[0].fixtures -or $row[0].rejections -le 0 -or $row[0].layoutAssertions -lt $row[0].fixtures) { throw "$mode platform $platform missing comparisons/rejections/layout" }
        }
        $summary.modes += [ordered]@{ mode = "/$mode"; result = $result; log = "$mode-oracle.txt" }
        $summary.status = "$mode passed; next mode or finalization pending"
        $summary | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $validation
    }
    $summary.succeeded = $true
    $summary.status = 'Od and O2 passed; four platforms, all three buffers, independent ABI, explicit rejection boundaries'
} catch {
    $summary.status = 'failed'
    $summary.error = $_.ToString()
    throw
} finally {
    $summary | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 $validation
}


