param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
if (!$env:RUNNER_TEMP) { $env:RUNNER_TEMP = [IO.Path]::GetTempPath() }
if (!$OutputDirectory) {
    $suffix = if ($env:GITHUB_RUN_ID) { "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" } else { 'local-' + [Guid]::NewGuid().ToString('N') }
    $OutputDirectory = "recovery/evidence/audio/metadata-validation/$suffix"
}
if (Test-Path $OutputDirectory) { throw "Refusing to overwrite evidence: $OutputDirectory" }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$out = (Resolve-Path $OutputDirectory).Path
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
$sourceCommit = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
$originalHash = (Get-FileHash SDK/tools/BinaryAssetBuilder.AudioCompiler.dll -Algorithm SHA256).Hash.ToLowerInvariant()
$results = @()
foreach ($mode in @('Debug', 'Release')) {
    $work = Join-Path $env:RUNNER_TEMP ('audio-metadata-' + $mode + '-' + [Guid]::NewGuid().ToString('N'))
    $baseline = Join-Path $work 'original'
    $candidate = Join-Path $work 'recovered'
    New-Item -ItemType Directory $baseline, $candidate -Force | Out-Null
    Copy-Item SDK/tools/*.dll $baseline
    Get-ChildItem SDK/tools -Filter '*.dll' | Where-Object Name -ne 'BinaryAssetBuilder.AudioCompiler.dll' | Copy-Item -Destination $candidate
    $core = Join-Path $candidate 'BinaryAssetBuilder.Core.dll'
    $library = Join-Path $candidate 'Recovered.Audio.Metadata.dll'
    $optimize = if ($mode -eq 'Release') { '/optimize+' } else { '/optimize-' }
    & $csc /nologo /target:library /platform:x86 $optimize /r:"$core" /out:"$library" (Resolve-Path 'recovery/src/Managed/Audio/AudioCatalog.cs').Path *> "$out/$mode-build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-build.log"; throw "$mode catalog build failed" }
    foreach ($side in @('original', 'recovered')) {
        $directory = if ($side -eq 'original') { $baseline } else { $candidate }
        $hostExe = Join-Path $directory 'AudioMetadataOracle.exe'
        & $csc /nologo /platform:x86 $optimize /r:System.Core.dll /r:"$core" /out:"$hostExe" (Resolve-Path 'recovery/tests/AudioMetadataOracle.cs').Path *> "$out/$mode-$side-host-build.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-$side-host-build.log"; throw "$mode $side oracle build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$hostExe.config"
        $assembly = if ($side -eq 'original') { Join-Path $baseline 'BinaryAssetBuilder.AudioCompiler.dll' } else { $library }
        $kind = if ($side -eq 'original') { 'original' } else { 'catalog' }
        & $hostExe $assembly $kind "$out/$mode-$side.txt" *> "$out/$mode-$side.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-$side.log"; throw "$mode $side oracle failed" }
    }
    # Deliberate cache invalidation for the real EALayer3 implementation.
    # Preserve the raw original transcript; adjust only these exact old values.
    $expectedText=[IO.File]::ReadAllText("$out/$mode-original.txt")
    $oldVersion=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('548405252'))
    $newVersion=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('548405253'))
    $oldProcessing=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('2414318214'))
    $newProcessing=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('2414318215'))
    $expectedText=$expectedText.Replace("version:0:VALUE:$oldVersion","version:0:VALUE:$newVersion").Replace("ProcessingHash=$oldProcessing","ProcessingHash=$newProcessing")
    $actualText=[IO.File]::ReadAllText("$out/$mode-recovered.txt")
    if($expectedText -cne $actualText){
        Compare-Object ($expectedText -split "`n") ($actualText -split "`n") | Select-Object -First 20 | Out-String | Write-Host
        throw "$mode metadata transcript differs beyond the EALayer3 cache-version update"
    }
    $actual=(Get-FileHash "$out/$mode-recovered.txt" -Algorithm SHA256).Hash
    $count = @(Get-Content "$out/$mode-original.txt").Count
    $results += @{mode=$mode; records=$count; succeeded=$true; transcriptSha256=$actual.ToLowerInvariant(); recoveredSha256=(Get-FileHash $library).Hash.ToLowerInvariant()}
    Write-Host "PASS Audio metadata $mode`: $count records equal except explicit EALayer3 cache versions; recovered host excludes original target DLL"
}
@{
    scope='AudioCatalog only: original plugin metadata functions versus recovered catalog'
    sourceCommit=$sourceCommit; runId=$env:GITHUB_RUN_ID; runAttempt=$env:GITHUB_RUN_ATTEMPT
    originalSha256=$originalHash; modes=$results; platforms=@(0)
    originalAbsentFromRecoveredHost=$true; nativeHandlersValidated=$false; pluginLifecycleValidated=$false; sdkValidated=$false
} | ConvertTo-Json -Depth 8 | Set-Content "$out/validation.json"
