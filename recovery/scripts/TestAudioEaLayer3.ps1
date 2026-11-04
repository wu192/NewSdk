param([string]$OutputDirectory='recovery/evidence/audio/ealayer3')
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $root
$out=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force $out|Out-Null
$fixtures=Join-Path $out 'fixtures'
python recovery/scripts/AudioEaLayer3Fixtures.py $fixtures $root
if($LASTEXITCODE -ne 0){throw 'EALayer3 fixtures failed'}
foreach($mode in @('Debug','Release')) {
    $bin=Join-Path $out "$mode/bin";New-Item -ItemType Directory -Force $bin|Out-Null
    & dotnet build recovery/src/Managed/Audio/BinaryAssetBuilder.AudioCompiler.csproj -c $mode -o $bin *> "$out/$mode-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-build.log" -Tail 70;throw 'EALayer3 AudioCompiler build failed'}
    Copy-Item SDK/tools/*.dll $bin -Exclude BinaryAssetBuilder.AudioCompiler.dll
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    & $csc /nologo /platform:x86 /r:System.Core.dll /r:"$bin/BinaryAssetBuilder.Core.dll" /out:"$bin/AudioEaLayer3Host.exe" (Resolve-Path recovery/tests/AudioEaLayer3Host.cs).Path
    if($LASTEXITCODE -ne 0){throw 'EALayer3 host build failed'}
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$bin/AudioEaLayer3Host.exe.config"
    foreach($jobs in @(1,4)) {
        & "$bin/AudioEaLayer3Host.exe" "$bin/BinaryAssetBuilder.AudioCompiler.dll" BinaryAssetBuilder.AudioCompiler.Plugin "$fixtures/assets.xml" "$out/$mode/jobs$jobs" $jobs *> "$out/$mode-jobs$jobs.log"
        if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-jobs$jobs.log" -Tail 80;throw 'EALayer3 real asset compile failed'}
    }
}
# A pinned public decoder is test-only, never a compiler runtime dependency.
$archive=Join-Path $out 'vgmstream-win64.zip'
Invoke-WebRequest https://github.com/vgmstream/vgmstream/releases/download/r2168/vgmstream-win64.zip -OutFile $archive
if((Get-FileHash $archive).Hash.ToLowerInvariant() -ne '692d45070852a35c1fa42726aa3c9de528e44eec925d37d99b1074256b99a229'){throw 'vgmstream archive identity differs'}
Expand-Archive $archive (Join-Path $out 'vgmstream')
$decoder=(Get-ChildItem (Join-Path $out 'vgmstream') -Recurse -Filter vgmstream-cli.exe|Select-Object -First 1).FullName
python recovery/scripts/ValidateAudioEaLayer3.py --root $out --decoder $decoder
if($LASTEXITCODE -ne 0){throw 'EALayer3 independent decoding failed'}
