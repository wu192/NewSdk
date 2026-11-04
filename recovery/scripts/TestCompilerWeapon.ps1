param([Parameter(Mandatory=$true)][string]$CompilerBuildDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$env:NEWSdk_PLUGIN_TEST_SCHEMA=(Resolve-Path "$PSScriptRoot/../../SDK/Schemas/xsd/CnC3Types.xsd").Path
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerBuildDirectory=(Resolve-Path $CompilerBuildDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite Weapon transcript evidence $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-weapon-'+[Guid]::NewGuid().ToString('N'))
$oracle=Join-Path $work 'original'
New-Item -ItemType Directory -Force $oracle|Out-Null
$state=@{schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';scope='complete existing Weapon fixture matrix against isolated original transcript; product bridge and wrapper, not MOD/Core execution';modes=@()}
$oldPath=$env:PATH
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $fixturePaths=@(
        'recovery/tests/PluginSchemaBoundary.cs',
        'recovery/tests/WeaponOracle.cs',
        'recovery/tests/WeaponScatterOracle.cs',
        'recovery/tests/WeaponNuggetOracle.cs',
        'recovery/tests/WeaponProjectileOracle.cs',
        'recovery/tests/WeaponActivationOracle.cs',
        'recovery/tests/WeaponScalarNuggetOracle.cs',
        'recovery/tests/WeaponEffectOracle.cs',
        'recovery/tests/WeaponFinalNuggetOracle.cs'
    )
    $existingScript=Get-Content "$PSScriptRoot/TestWeapon.ps1" -Raw
    $existingPaths=@([regex]::Matches($existingScript,"Resolve-Path '(recovery/tests/[^']+\.cs)'")|ForEach-Object{$_.Groups[1].Value})
    if(($existingPaths -join "`n") -cne ($fixturePaths -join "`n")){throw 'Existing Weapon fixture source set changed; update the full product replay manifest explicitly'}
    $sources=@($fixturePaths|ForEach-Object{(Resolve-Path $_).Path})
    $sources+=(Resolve-Path 'recovery/tests/WeaponTranscript.cs').Path
    $state.fixtureInputs=@($sources|ForEach-Object{@{path=([IO.Path]::GetFullPath($_).Substring($root.Length+1).Replace('\','/'));sha256=(Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $state.pluginSchemaInputs=@(Get-ChildItem "$root/SDK/Schemas/xsd" -Recurse -Filter *.xsd -File|Sort-Object FullName|ForEach-Object{@{path=$_.FullName.Substring($root.Length+1).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    Copy-Item SDK/tools/*.dll $oracle
    $state.originalInputs=@(Get-ChildItem $oracle -Filter *.dll -File|Sort-Object Name|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $config='<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'
    $originalHost=Join-Path $oracle 'WeaponTranscriptOracle.exe'
    & $csc /nologo /unsafe /codepage:65001 /platform:x86 /define:WEAPON_TRANSCRIPT_ORACLE /r:System.Xml.dll "/r:$oracle/BinaryAssetBuilder.Core.dll" "/out:$originalHost" $sources *> "$out/original-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/original-build.log";throw 'Original full Weapon transcript host compilation failed'}
    $config|Set-Content "$originalHost.config"
    $transcript=Join-Path $out 'original-weapon.bin.gz'
    Push-Location $oracle
    try {& $originalHost "$oracle/BinaryAssetBuilder.Tokenizer.dll" $transcript *> "$out/original-runtime.log"; $code=$LASTEXITCODE}
    finally {Pop-Location}
    if($code -ne 0 -or !(Select-String -LiteralPath "$out/original-runtime.log" -SimpleMatch 'PASS Weapon full transcript record:' -Quiet)){Get-Content "$out/original-runtime.log" -Tail 60;throw 'Original complete Weapon matrix failed'}
    $state.originalTranscriptSha256=(Get-FileHash $transcript -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach($mode in @('Debug','Release')) {
        $receipt=Get-Content "$CompilerBuildDirectory/$mode/build.json" -Raw|ConvertFrom-Json
        if($receipt.configuration -cne $mode -or $receipt.sourceCommit -cne $state.sourceCommit){throw 'Product receipt configuration/source does not match current validation'}
        $clean=Join-Path $work $mode
        New-Item -ItemType Directory -Force $clean|Out-Null
        $bin=Join-Path $CompilerBuildDirectory "$mode/bin"
        $validationRun=Initialize-CompilerValidationRun (Join-Path $CompilerBuildDirectory $mode) $clean $mode $state.sourceCommit
        $exe=Join-Path $clean 'NewModCompiler.exe'
        # The helper verifies the distinct production and validation executable hashes.
        $replayHost=Join-Path $clean 'WeaponProductReplay.exe'
        & $csc /nologo /unsafe /codepage:65001 /platform:x86 /define:WEAPON_TRANSCRIPT_PRODUCT /r:System.Xml.dll "/r:$exe" "/out:$replayHost" $sources *> "$out/$mode-build.log"
        if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-build.log";throw "Product Weapon $mode replay host compilation failed"}
        $config|Set-Content "$replayHost.config"
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll','WeaponProductReplay.exe','WeaponProductReplay.exe.config')
        if(@(Get-ChildItem $clean -Recurse|Where-Object{$_.PSIsContainer -or $_.Name -notin $allowed}).Count -ne 0){throw 'Unexpected file in isolated Weapon product replay directory'}
        $env:PATH="$env:WINDIR/System32;$env:WINDIR;$env:WINDIR/Microsoft.NET/Framework/v4.0.30319"
        Push-Location $clean
        try {& $validationRun.exe $replayHost $transcript *> "$out/$mode-runtime.log"; $code=$LASTEXITCODE}
        finally {Pop-Location; $env:PATH=$oldPath}
        if($code -ne 0 -or !(Select-String -LiteralPath "$out/$mode-runtime.log" -SimpleMatch 'PASS Weapon full transcript replay:' -Quiet)){Get-Content "$out/$mode-runtime.log" -Tail 60;throw "Complete product Weapon $mode matrix failed"}
        Complete-CompilerValidationRun $validationRun
        $state.modes+=@{validationHostSha256=$validationRun.validationHostSha256;configuration=$mode;passed=$true;exeSha256=$receipt.exeSha256;platforms=@(0,1,2,99);fullExistingFixtureSet=$true;originalBusinessDllsAbsent=$true;summary=@(Select-String -LiteralPath "$out/$mode-runtime.log" -SimpleMatch 'PASS Weapon full transcript replay:'|ForEach-Object{$_.Line})}
    }
    $state.status='passed';$global:LASTEXITCODE=0
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$env:PATH=$oldPath;$state|ConvertTo-Json -Depth 8|Set-Content "$out/validation.json";Pop-Location}

