$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$sourceCommit = $env:GITHUB_SHA
if (-not $sourceCommit) { $sourceCommit = (& git rev-parse HEAD).Trim() }
$runId = $env:GITHUB_RUN_ID
if (-not $runId) { $runId = 'local-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') }
$tempRoot = $env:RUNNER_TEMP
if (-not $tempRoot) { $tempRoot = [IO.Path]::GetTempPath() }
$out = Join-Path $tempRoot ('recovered-xml-singlesound-' + $runId)
$evidence = 'recovery/evidence/xml-singlesound'
New-Item -ItemType Directory -Force $out, $evidence | Out-Null
$result = [ordered]@{ succeeded=$false; sourceCommit=$sourceCommit; runId=$runId; platforms=@(0,1,2,99); modes=@(); scope='Ten complete single-sound family roots; component only; no real SDK compilation or routing changes'; status='started' }
$result | ConvertTo-Json -Depth 8 | Set-Content "$evidence/validation.json"
try {
    if ($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x86') { throw 'Run from the VS2022 x86 developer shell' }
    Copy-Item SDK/tools/*.dll $out
    $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    foreach ($mode in @('Od','O2')) {
        $buildLog = "$evidence/$mode-build.txt"
        & cl /nologo /clr /LD /MD /EHa "/$mode" /fp:precise "/AI$out" "/Fo$out/" "/Fe$out/Recovered.Xmlsinglesound.dll" recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/Xmlsinglesound.cpp recovery/src/Bridge/XmlsinglesoundCompiler.cpp *> $buildLog
        if ($LASTEXITCODE -ne 0) { Get-Content $buildLog; throw "$mode C++/CLI build failed" }
        & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:System.Core.dll "/r:$out/Recovered.Xmlsinglesound.dll" "/r:$out/BinaryAssetBuilder.Core.dll" "/out:$out/XmlsinglesoundOracle.exe" (Resolve-Path recovery/tests/XmlsinglesoundOracle.cs) (Resolve-Path recovery/tests/XmlsinglesoundCases.cs) (Resolve-Path recovery/tests/XmlsinglesoundBoundaryCases.cs) *> "$evidence/$mode-host-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-host-build.txt"; throw "$mode C# oracle build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/XmlsinglesoundOracle.exe.config"
        & "$out/XmlsinglesoundOracle.exe" (Resolve-Path SDK/tools/BinaryAssetBuilder.XmlCompiler.dll).Path "$out/Recovered.Xmlsinglesound.dll" *> "$evidence/$mode-oracle.txt"
        $code = $LASTEXITCODE
        Get-Content "$evidence/$mode-oracle.txt"
        if ($code -ne 0) { throw "$mode original XmlCompiler differential failed ($code)" }
        $platformResults = @()
        foreach ($line in Get-Content "$evidence/$mode-oracle.txt") {
            if ($line -match '^PASS platform=(\d+) fixtures=(\d+) threeArrayComparisons=(\d+) directArrayComparisons=(\d+) rejections=(\d+) independentLayouts=(\d+)$') {
                $platformResults += [ordered]@{ platform=[int]$Matches[1]; fixtures=[int]$Matches[2]; threeArrayComparisons=[int]$Matches[3]; directArrayComparisons=[int]$Matches[4]; rejections=[int]$Matches[5]; independentLayouts=[int]$Matches[6] }
            }
        }
        if ($platformResults.Count -ne 4 -or ($platformResults.platform -join ',') -ne '0,1,2,99') { throw 'Incomplete platform summary' }
        foreach ($p in $platformResults) {
            if ($p.fixtures -le 0 -or $p.threeArrayComparisons -ne 3*$p.fixtures -or $p.directArrayComparisons -ne 3*$p.fixtures -or $p.rejections -le 0 -or $p.independentLayouts -lt $p.fixtures) { throw 'Invalid matrix counts' }
        }
        $result.modes += [ordered]@{ mode=$mode; succeeded=$true; platforms=$platformResults; log="$mode-oracle.txt" }
    }
    $result.succeeded = $true
    $result.status = 'Od and O2 four-platform three-buffer differential passed for documented support boundary'
} catch {
    $result.status = $_.Exception.Message
    throw
} finally {
    $result | ConvertTo-Json -Depth 8 | Set-Content "$evidence/validation.json"
}



