param([string]$OutputDirectory, [switch]$OriginalOnly, [switch]$IncludeMp3, [ValidateSet('business','pcm','xas','defaults','rates','main','pcm8','formats','loops','aiffloops','resample','resample-full','multichannel','multichannel-aiff','multichannel-eight','new-world-eight')][string[]]$Groups=@('business','pcm','xas','defaults','rates','main','pcm8','formats','loops','aiffloops'))
$ErrorActionPreference = 'Stop'
if (!$OriginalOnly -and (@($Groups | Where-Object { $_ -in @('resample') }).Count -gt 0)) { throw 'Unrecovered native groups are original-only evidence, not candidate validation' }
Set-Location (Resolve-Path "$PSScriptRoot/../..")
if (!$env:RUNNER_TEMP) { $env:RUNNER_TEMP = [IO.Path]::GetTempPath() }
if (!$OutputDirectory) {
    $suffix = if ($env:GITHUB_RUN_ID) { "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" } else { 'local-' + [Guid]::NewGuid().ToString('N') }
    $OutputDirectory = "recovery/evidence/audio/plugin-validation/$suffix"
}
if (Test-Path $OutputDirectory) { throw "Refusing to overwrite evidence: $OutputDirectory" }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$out = (Resolve-Path $OutputDirectory).Path
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
$work = Join-Path $env:RUNNER_TEMP ('audio-plugin-' + [Guid]::NewGuid().ToString('N'))
$original = Join-Path $work 'original'; $fixtures = Join-Path $work 'fixtures'
New-Item -ItemType Directory $original, $fixtures -Force | Out-Null
Copy-Item SDK/tools/*.dll $original
$core = Join-Path $original 'BinaryAssetBuilder.Core.dll'
$hostExe = Join-Path $original 'AudioPluginOracle.exe'
& $csc /nologo /unsafe /platform:x86 /r:System.Core.dll /r:System.Xml.dll /r:"$core" /out:"$hostExe" (Resolve-Path 'recovery/tests/AudioPluginOracle.cs').Path (Resolve-Path 'recovery/tests/AudioMp3Fixtures.cs').Path (Resolve-Path 'recovery/tests/AudioInputFormatCases.cs').Path (Resolve-Path 'recovery/tests/AudioLoopProbeCases.cs').Path (Resolve-Path 'recovery/tests/AudioAiffLoopCases.cs').Path (Resolve-Path 'recovery/tests/AudioUnrecoveredProbeCases.cs').Path (Resolve-Path 'recovery/tests/AudioResampleCases.cs').Path (Resolve-Path 'recovery/tests/AudioMultichannelCases.cs').Path (Resolve-Path 'recovery/tests/AudioMultichannelAiffCases.cs').Path (Resolve-Path 'recovery/tests/AudioAiffMarkerRangeCases.cs').Path (Resolve-Path 'recovery/tests/AudioAiffOffsetCases.cs').Path (Resolve-Path 'recovery/tests/AudioAifcInputCases.cs').Path (Resolve-Path 'recovery/tests/AudioAdp4InputCases.cs').Path *> "$out/oracle-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/oracle-build.log"; throw 'Audio plugin oracle build failed' }
& $csc /nologo /platform:x86 /r:"$core" /out:"$original/AudioExcludedPlatformOracle.exe" (Resolve-Path 'recovery/tests/AudioExcludedPlatformOracle.cs').Path *> "$out/excluded-platform-oracle-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/excluded-platform-oracle-build.log"; throw 'Excluded-platform oracle build failed' }
& $csc /nologo /platform:x86 /out:"$original/AudioInputReaderOracle.exe" (Resolve-Path 'recovery/tests/AudioInputReaderOracle.cs').Path (Resolve-Path 'recovery/tests/AudioInputFormatCases.cs').Path *> "$out/reader-oracle-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/reader-oracle-build.log"; throw 'Audio reader boundary oracle build failed' }
& $csc /nologo /platform:x86 /out:"$original/AudioLoopReaderOracle.exe" (Resolve-Path 'recovery/tests/AudioLoopReaderOracle.cs').Path (Resolve-Path 'recovery/tests/AudioLoopProbeCases.cs').Path *> "$out/loop-reader-oracle-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/loop-reader-oracle-build.log"; throw 'Audio loop reader oracle build failed' }
& $csc /nologo /platform:x86 /out:"$original/AudioAiffLoopReaderOracle.exe" (Resolve-Path 'recovery/tests/AudioAiffLoopReaderOracle.cs').Path (Resolve-Path 'recovery/tests/AudioAiffLoopCases.cs').Path (Resolve-Path 'recovery/tests/AudioInputFormatCases.cs').Path *> "$out/aiff-loop-reader-oracle-build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/aiff-loop-reader-oracle-build.log"; throw 'AIFF loop reader oracle build failed' }
'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$hostExe.config"
$sourceCommit = (git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
$receipt = @{sourceCommit=$sourceCommit; runId=$env:GITHUB_RUN_ID; runAttempt=$env:GITHUB_RUN_ATTEMPT; originalSha256=(Get-FileHash SDK/tools/BinaryAssetBuilder.AudioCompiler.dll).Hash.ToLowerInvariant(); status='started'; configurations=@(); runtimeDirectories=@{}; sdkValidated=$false; nativeAudioFileValidated=$false; scope='PathMusic and AudioFile PCM8/16/24 WAVE, AIFF8/16/24 NONE/XAS component differential'}
try {
    $businessScope = if ($OriginalOnly -or $IncludeMp3) { 'business' } else { 'pathmusic' }
    $receipt.mp3Validated = [bool]($IncludeMp3 -and ($Groups -contains 'business'))
    if ($IncludeMp3) { $receipt.scope += '; MP3 passthrough ProcessInstance/direct-handler differential' }
    $env:AUDIO_TEST_REAL_DIRECTORY = (Resolve-Path recovery/fixtures/audio/main).Path
    foreach ($group in $Groups) {
        $scope = if ($group -eq 'business') { $businessScope } else { $group }
        $expectedFile = if ($group -eq 'business') { "$out/original.txt" } else { "$out/$group-original.txt" }
        & $hostExe "$original/BinaryAssetBuilder.AudioCompiler.dll" original $fixtures $expectedFile $scope *> "$out/$group-original.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$group-original.log"; throw "Original $group probe failed" }
    }
    if ($Groups -contains 'multichannel' -and @(Get-Content "$out/multichannel-original.txt").Count -ne 1728) { throw 'Expected all1728 original multichannel success records' }
    if ($Groups -contains 'multichannel-eight' -and @(Get-Content "$out/multichannel-eight-original.txt").Count -ne 864) { throw 'Expected all864 original eight-channel success records' }
    if ($Groups -contains 'new-world-eight' -and @(Get-Content "$out/new-world-eight-original.txt").Count -ne 4) { throw 'Expected all4 real New World eight-channel success records' }
    if ($Groups -contains 'multichannel-aiff' -and @(Get-Content "$out/multichannel-aiff-original.txt").Count -ne 6336) { throw 'Expected all6336 AIFF multichannel success records' }
    $receipt.selectedGroups = @($Groups)
    $receipt.scope = $(if ($OriginalOnly) { 'Original-only evidence: ' } else { 'Component differential: ' }) + ($Groups -join ',')
    $receipt.realMainProvenanceSha256=(Get-FileHash recovery/fixtures/audio/main/provenance.json).Hash.ToLowerInvariant()
    Get-ChildItem $fixtures -File | Where-Object { $_.Extension -in @('.wav','.aiff','.aifc') } | Copy-Item -Destination $out
    if ($OriginalOnly) { $receipt.status='original-evidence-only'; return }
    foreach ($mode in @('Debug', 'Release')) {
        $candidate = Join-Path $work $mode; New-Item -ItemType Directory $candidate | Out-Null
        Get-ChildItem SDK/tools -Filter '*.dll' | Where-Object Name -ne 'BinaryAssetBuilder.AudioCompiler.dll' | Copy-Item -Destination $candidate
        $library = Join-Path $candidate 'Recovered.AudioCompiler.dll'
        $sources = @(Get-ChildItem recovery/src/Managed/Audio -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName)
        if ($sources.Count -lt 2) { throw 'Recovered AudioCompiler implementation is missing' }
        $optimize = if ($mode -eq 'Release') { '/optimize+' } else { '/optimize-' }
        & dotnet build recovery/src/Managed/Audio/BinaryAssetBuilder.AudioCompiler.csproj --configuration $mode --output $candidate -p:AssemblyName=Recovered.AudioCompiler *> "$out/$mode-build.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-build.log"; throw "$mode AudioCompiler build failed" }
        $receipt.runtimeDirectories[$mode]=$candidate
        Copy-Item "$original/AudioPluginOracle.exe*" $candidate
        Copy-Item "$original/AudioExcludedPlatformOracle.exe" $candidate
        & "$candidate/AudioExcludedPlatformOracle.exe" $library *> "$out/$mode-excluded-platform.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-excluded-platform.log"; throw "$mode excluded-platform rejection failed" }
        Get-Content "$out/$mode-excluded-platform.log"
        Copy-Item "$original/AudioInputReaderOracle.exe" $candidate
        if ($Groups -notcontains 'resample-full' -or $Groups.Count -ne 1) {
        & "$candidate/AudioInputReaderOracle.exe" $library $fixtures *> "$out/$mode-reader-boundaries.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-reader-boundaries.log"; throw "$mode input rejection boundaries failed" }
        Get-Content "$out/$mode-reader-boundaries.log" -Tail 1
        Copy-Item "$original/AudioLoopReaderOracle.exe" $candidate
        & "$candidate/AudioLoopReaderOracle.exe" $library *> "$out/$mode-loop-boundaries.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-loop-boundaries.log"; throw "$mode loop rejection boundaries failed" }
        Get-Content "$out/$mode-loop-boundaries.log" -Tail 1
        Copy-Item "$original/AudioAiffLoopReaderOracle.exe" $candidate
        & "$candidate/AudioAiffLoopReaderOracle.exe" $library *> "$out/$mode-aiff-loop-boundaries.log"
        if ($LASTEXITCODE -ne 0) { Get-Content "$out/$mode-aiff-loop-boundaries.log"; throw "$mode AIFF loop boundaries failed" }
        Get-Content "$out/$mode-aiff-loop-boundaries.log" -Tail 1
        }
        foreach ($group in $Groups) {
            $scope = if ($group -eq 'business') { $businessScope } else { $group }
            $expectedFile = if ($group -eq 'business') { "$out/original.txt" } else { "$out/$group-original.txt" }
            $actualFile = "$out/$mode-$group.txt"
            & "$candidate/AudioPluginOracle.exe" $library recovered $fixtures $actualFile $scope *> "$out/$mode-$group.log"
            if ($LASTEXITCODE -ne 0) {
                Get-Content "$out/$mode-$group.log" -Tail 25
                $receipt.configurations += @{configuration=$mode;group=$group;passed=$false;error='oracle failed'}
                continue
            }
            $safetyRejections = 0
            if ($group -eq 'resample-full') {
                # Exact registered undefined-import cases stay in the full transcripts.
                # Their candidate errors are asserted by AudioPluginOracle; original
                # malloc contents cannot be an equivalence target (see native audit).
                $undefinedPattern = '^pcm-resample-400-96000-1-[12]-signal0-(NONE|XAS)-(False|True):0:dispatch:'
                $originalRows = @(Get-Content $expectedFile)
                $candidateRows = @(Get-Content $actualFile)
                if ($originalRows.Count -ne 432 -or $candidateRows.Count -ne 432) { throw 'Expected all 432 registered resampling records' }
                $originalUndefined = @($originalRows | Where-Object { $_ -match $undefinedPattern })
                $candidateUndefined = @($candidateRows | Where-Object { $_ -match $undefinedPattern })
                if ($originalUndefined.Count -ne 8 -or $candidateUndefined.Count -ne 8) { throw 'Expected exactly eight undefined single-frame importer records' }
                if (@($originalUndefined | Where-Object { $_ -notmatch ':BUFFER:' }).Count -ne 0) { throw 'Original single-frame evidence is missing buffers' }
                if (@($candidateUndefined | Where-Object { $_ -notmatch ':ERROR:System.NotSupportedException:' -or $_ -notmatch ':CUSTOM:<absent>$' }).Count -ne 0) { throw 'Candidate single-frame safety records invalid' }
                $originalUndefined | Set-Content "$out/original-single-frame-evidence.txt"
                $candidateUndefined | Set-Content "$out/$mode-single-frame-safety.txt"
                $expectedFile = "$out/original-resample-defined.txt"
                $actualFile = "$out/$mode-resample-defined.txt"
                @($originalRows | Where-Object { $_ -notmatch $undefinedPattern }) | Set-Content $expectedFile
                @($candidateRows | Where-Object { $_ -notmatch $undefinedPattern }) | Set-Content $actualFile
                $safetyRejections = 8
            }
            $actual = (Get-FileHash $actualFile).Hash
            $expectedHash = (Get-FileHash $expectedFile).Hash
            if ($actual -ne $expectedHash) {
                Compare-Object (Get-Content $expectedFile) (Get-Content $actualFile) | Out-String -Width 2000 | Set-Content "$out/$mode-$group-difference.txt"
                Write-Host "FAIL Audio plugin $mode $group`: transcript differs; full buffers saved to $mode-$group-difference.txt"
                $receipt.configurations += @{configuration=$mode;group=$group;passed=$false;error='transcript differs'}
                continue
            }
            $count = @(Get-Content $actualFile).Count
            $receipt.configurations += @{configuration=$mode;group=$group;passed=$true;records=$count;candidateSafetyRejections=$safetyRejections;originalUndefinedEvidenceRecords=$safetyRejections;transcriptSha256=$actual.ToLowerInvariant();recoveredSha256=(Get-FileHash $library).Hash.ToLowerInvariant();originalAbsentFromRecoveredHost=$true}
            Write-Host "PASS Audio plugin $mode $group`: $count identical three-buffer/custom-data records"
        }
    }
    if (@($receipt.configurations | Where-Object { !$_.passed }).Count -gt 0) { $receipt.status='failed'; throw 'Audio plugin groups failed; see individual difference files' }
    $receipt.nativeAudioFileValidated=(@('pcm','xas','defaults','rates','main','pcm8','formats','loops','aiffloops') | Where-Object { $Groups -notcontains $_ }).Count -eq 0
    $receipt.status='passed'
} catch {
    $receipt.status='failed'
    $receipt.error=$_.Exception.Message
    throw
} finally {
    $receipt | ConvertTo-Json -Depth 8 | Set-Content "$out/validation.json"
}
