[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite product texture evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-texture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work|Out-Null
$state=@{schemaVersion=2;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';configurations=@();scope='Built-in texture payload in test-only host using identical production objects; raw original metadata/buffers/CDATA/errors, with identical plugin version and processing hashes; not production CLI MOD compilation'}
$oldPath=$env:PATH
function Invoke-Fixture([string]$Command,[string[]]$Arguments,[string]$Directory,[string]$Log){
    Push-Location $Directory
    try{ & $Command @Arguments *> $Log; $code=$LASTEXITCODE }
    finally{Pop-Location}
    if($code -ne 0){Get-Content $Log -Tail 80|Out-Host;throw "Product texture fixture failed: $Command (exit $code)"}
}
function Assert-TextureTranscript([string]$Path){
    $lines=@(Get-Content -LiteralPath $Path)
    if($lines.Count -ne 10 -or @($lines|Where-Object{$_ -cmatch '^(Texture|OnDemandTexture|TerrainTextureAtlas):False:BUFFER:'}).Count -ne 6 -or
        @($lines|Where-Object{$_ -cmatch '^(Texture|OnDemandTexture|TerrainTextureAtlas):True:ERROR:'}).Count -ne 3){throw 'Texture transcript does not contain the complete three-route 6-success/3-error matrix'}
}
Push-Location $root
try{
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $source=(Resolve-Path 'recovery/probes/TextureRedirect/TextureProbe.cs').Path
    $state.fixtureSha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.originalInputs=@(@('BinaryAssetBuilder.TextureCompiler.dll','BinaryAssetBuilder.Core.dll')|ForEach-Object{@{path="SDK/tools/$_";sha256=(Get-FileHash "SDK/tools/$_" -Algorithm SHA256).Hash.ToLowerInvariant()}})
    if($state.originalInputs[0].sha256 -cne '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e'){throw 'Texture original is not the pinned audited input'}
    $oracle=(New-Item -ItemType Directory "$work/original").FullName
    Copy-Item SDK/tools/*.dll $oracle
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    Invoke-Fixture $csc @('/nologo','/codepage:65001','/platform:x86','/r:System.Core.dll','/r:System.Xml.dll',"/r:$oracle/BinaryAssetBuilder.Core.dll","/out:$oracle/Original.exe",$source) $root "$out/original-build.log"
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'|Set-Content "$oracle/Original.exe.config"
    Invoke-Fixture 'python' @('recovery/probes/TextureRedirect/MakeFixture.py',"$work/fixture.tga") $root "$out/fixture.log"
    Invoke-Fixture 'python' @('recovery/probes/TextureRedirect/MakeFixture.py',"$work/fixture.tga.wrong-size.tga",'512') $root "$out/fixture-invalid.log"
    Invoke-Fixture "$oracle/Original.exe" @("$oracle/BinaryAssetBuilder.TextureCompiler.dll","$work/fixture.tga","$out/original.txt",'original') $oracle "$out/original-runtime.log"
    Assert-TextureTranscript "$out/original.txt"
    $baseline=(Get-FileHash "$out/original.txt" -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.fixtureInputs=@(@('fixture.tga','fixture.tga.wrong-size.tga')|ForEach-Object{@{name=$_;sha256=(Get-FileHash (Join-Path $work $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
    foreach($mode in @('Debug','Release')){
        $evidence=(New-Item -ItemType Directory "$out/$mode").FullName
        $clean=Join-Path $work "$mode/clean"
        $run=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
        $fixture=Join-Path $clean 'TextureFixture.exe'
        Invoke-Fixture $csc @('/nologo','/codepage:65001','/platform:x86','/define:TEXTURE_SOURCE_PROBE','/r:System.Core.dll','/r:System.Xml.dll',"/r:$($run.exe)","/out:$fixture",$source) $root "$evidence/fixture-build.log"
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll','TextureFixture.exe')
        if(@(Get-ChildItem -LiteralPath $clean -Force|Where-Object{$_.PSIsContainer -or $_.Name -notin $allowed}).Count -ne 0){throw 'Unexpected product texture validation deployment entry'}
        $arguments=@($fixture,'built-in',"$work/fixture.tga","$evidence/source.txt",'exe')
        $env:PATH="$env:WINDIR/System32;$env:WINDIR;$env:WINDIR/Microsoft.NET/Framework/v4.0.30319"
        try{
            Invoke-Fixture $run.exe $arguments $clean "$evidence/runtime.log"
            Assert-TextureTranscript "$evidence/source.txt"
            $originalBytes=[IO.File]::ReadAllBytes("$out/original.txt")
            $sourceBytes=[IO.File]::ReadAllBytes("$evidence/source.txt")
            if((Get-Content -LiteralPath "$evidence/source.txt" -TotalCount 1) -cne 'metadata:1424942948:4202'){throw 'Unexpected source Texture catalog version'}
            if(![Linq.Enumerable]::SequenceEqual[byte]($originalBytes,$sourceBytes)){
                throw "$mode complete Texture transcript metadata, raw buffers, CustomData or input error differs from original"
            }
        }finally{$env:PATH=$oldPath}
        Complete-CompilerValidationRun $run
        $state.configurations+=@{configuration=$mode;passed=$true;successes=6;invalidInputErrors=3;externalTextureDll=$false;singleDispose=$true;separateOracleProcess=$true;exeSha256=$run.productExeSha256;validationHostSha256=$run.validationHostSha256;originalVersion=4202;sourceVersion=4202;originalTranscriptSha256=$baseline;sourceTranscriptSha256=(Get-FileHash "$evidence/source.txt" -Algorithm SHA256).Hash.ToLowerInvariant();componentExecution='test-only host with identical production objects; source-linked texture';textureDeployment=@{implementation='built-in-source';externalRuntimeFiles=@()}}
    }
    $state.status='passed'
    $global:LASTEXITCODE=0
    Write-Host 'PASS production-object source Texture Debug/Release: original raw payload/CustomData/errors, reuse and single-close, no external texture runtime; original cache version and processing hashes preserved; not CLI MOD compilation.'
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$env:PATH=$oldPath;$state|ConvertTo-Json -Depth 14|Set-Content "$out/validation.json" -Encoding utf8;Pop-Location}
