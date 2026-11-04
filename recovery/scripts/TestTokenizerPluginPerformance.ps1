[CmdletBinding()]
param([string]$BaselineCommit='7cfdd4314d8cafa2f2c7313e64f5ffbacf94eaf9')
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$out=Join-Path $root "recovery/evidence/refactor/tokenizer-plugin-performance/$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
$work=Join-Path $env:RUNNER_TEMP 'tokenizer-plugin-performance'
if((Test-Path $out) -or (Test-Path $work)){throw 'Refusing to overwrite performance evidence'}
New-Item -ItemType Directory -Force $out,$work|Out-Null
$state=@{status='started';sourceCommit=$env:GITHUB_SHA;baselineCommit=$BaselineCommit;runId=$env:GITHUB_RUN_ID;scope='Isolated original and before/after C++/CLI plugin components, Release /O2 x86. Seven prepared-XML fixtures, including materialized SDK schema root defaults; both byte orders compared; five alternating Win32 measurement pairs. XML parsing, Core preparation, caches and full MOD compilation excluded. managedAllocatedBytes excludes native heap allocations.'}
Push-Location $root
try {
    $archive=Join-Path $work 'baseline.tar'
    git archive --format=tar "--output=$archive" $BaselineCommit recovery/src/Native recovery/src/Bridge
    if($LASTEXITCODE -ne 0){throw 'Cannot export exact baseline source'}
    $baseline=Join-Path $work 'baseline'
    New-Item -ItemType Directory -Force $baseline|Out-Null
    tar -xf $archive -C $baseline
    if($LASTEXITCODE -ne 0){throw 'Cannot unpack baseline source'}
    Copy-Item SDK/tools/*.dll $work
    $common=@('Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter')
    foreach($revision in @('Before','After')) {
        $source=if($revision -eq 'Before'){$baseline}else{$root}
        foreach($component in @('GameObject','Weapon')) {
            $obj=Join-Path $work "$revision-$component-objects"
            New-Item -ItemType Directory -Force $obj|Out-Null
            $names=$common+$(if($component -eq 'GameObject'){@('WeaponSlotOrder','GameObject')}else{@('WeaponNugget','WeaponTemplate')})
            $files=@($names|ForEach-Object{Join-Path $source "recovery/src/Native/$_.cpp"})
            $files+=Join-Path $source "recovery/src/Bridge/${component}Compiler.cpp"
            $arguments=@('/nologo','/clr','/LD','/MD','/EHa','/O2','/std:c++17','/fp:precise',"/AI$work","/Fo$obj/","/Fe$work/$revision.$component.dll")+$files
            & cl.exe @arguments *> "$out/$revision-$component-build.log"
            if($LASTEXITCODE -ne 0){Get-Content "$out/$revision-$component-build.log" -Tail 60;throw "Component build failed: $revision $component"}
        }
    }
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    & $csc /nologo /platform:x86 "/r:$work/BinaryAssetBuilder.Core.dll" "/out:$work/TokenizerPluginPerformance.exe" (Resolve-Path recovery/tests/TokenizerPluginPerformance.cs) *> "$out/host-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/host-build.log";throw 'Performance host build failed'}
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>'|Set-Content "$work/TokenizerPluginPerformance.exe.config"
    & "$work/TokenizerPluginPerformance.exe" $work "$out/measurements.csv" *> "$out/measurement.log"
    $state.hostExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){Get-Content "$out/measurement.log" -Tail 60;throw "Component host failed with exit code $LASTEXITCODE"}
    Get-Content "$out/measurement.log" -Tail 5
    $state.inputs=@(Get-ChildItem $work -File|Where-Object{$_.Extension -in @('.dll','.exe','.config')}|Sort-Object Name|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $state.compiler=$env:VCToolsVersion
    $state.status='passed'
    $global:LASTEXITCODE=0
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {$state|ConvertTo-Json -Depth 8|Set-Content "$out/validation.json";Pop-Location}
