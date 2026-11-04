[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$FoundationDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$foundationRoot=(Resolve-Path $FoundationDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite texture adapter evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$work=Join-Path ([IO.Path]::GetTempPath()) ('texture-adapter-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work|Out-Null
$state=@{schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';configurations=@();scope='test-only unified EXE adapter differential; not production MOD/Core integration'}
function Invoke-Checked([string]$Command,[string[]]$Arguments,[string]$Log) {
    & $Command @Arguments *> $Log
    if($LASTEXITCODE -ne 0){Get-Content $Log -Tail 80|Write-Host;throw "Texture adapter test command failed: $Command"}
}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $oracle=(New-Item -ItemType Directory "$work/original").FullName
    Copy-Item SDK/tools/*.dll $oracle
    $frameworkCsc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $config='<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'
    Invoke-Checked $frameworkCsc @('/nologo','/codepage:65001','/platform:x86','/r:System.Core.dll','/r:System.Xml.dll',"/r:$oracle/BinaryAssetBuilder.Core.dll","/out:$oracle/Original.exe",(Join-Path $root 'recovery/probes/TextureRedirect/TextureProbe.cs')) "$out/original-build.log"
    $config|Set-Content "$oracle/Original.exe.config"
    Invoke-Checked 'python' @('recovery/probes/TextureRedirect/MakeFixture.py',"$work/fixture.tga") "$out/fixture.log"
    Invoke-Checked 'python' @('recovery/probes/TextureRedirect/MakeFixture.py',"$work/fixture.tga.wrong-size.tga",'512') "$out/fixture-invalid.log"
    Invoke-Checked "$oracle/Original.exe" @("$oracle/BinaryAssetBuilder.TextureCompiler.dll","$work/fixture.tga","$out/original.txt",'original') "$out/original.log"
    $baseline=(Get-FileHash "$out/original.txt").Hash
    $packageLine=(& dotnet nuget locals global-packages --list)
    if($LASTEXITCODE -ne 0){throw 'Cannot locate pinned compiler'}
    $packages=($packageLine -replace '^global-packages:\s*','').Trim()
    $csc=(Resolve-Path (Join-Path $packages 'microsoft.net.compilers.toolset/4.0.1/tasks/net472/csc.exe')).Path
    $framework=(Resolve-Path (Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8')).Path
    foreach($mode in @('Debug','Release')){
        $evidence=(New-Item -ItemType Directory "$out/$mode").FullName
        $obj=(New-Item -ItemType Directory "$work/$mode/obj" -Force).FullName
        $bin=(New-Item -ItemType Directory "$work/$mode/bin" -Force).FullName
        $foundationDir=$foundationRoot
        if(Test-Path "$foundationRoot/$mode/Managed.Foundation.netmodule"){$foundationDir=Join-Path $foundationRoot $mode}
        $foundation=(Resolve-Path "$foundationDir/Managed.Foundation.netmodule").Path
        $foundationReceipt=Get-Content "$foundationDir/build.json" -Raw|ConvertFrom-Json
        if($foundationReceipt.configuration -cne $mode -or $foundationReceipt.moduleSha256 -cne (Get-FileHash $foundation).Hash.ToLowerInvariant()){throw 'Foundation configuration or hash mismatch'}
        $module=Join-Path $obj 'Managed.TextureAdapterProbe.netmodule'
        $compilerArgs=@('/nologo','/noconfig','/nostdlib+','/codepage:65001','/target:module','/platform:anycpu','/langversion:10.0','/define:NEWSdk_MONOLITHIC;TEXTURE_ADAPTER_PROBE',"/addmodule:$foundation","/out:$module")
        $compilerArgs+=if($mode -eq 'Release'){'/optimize+'}else{'/optimize-'}
        foreach($reference in @('mscorlib','System','System.Core','System.Xml','System.Configuration','System.Data','System.Web','System.Configuration.Install','System.Web.Extensions')){$compilerArgs+=('/r:'+(Join-Path $framework ($reference+'.dll')))}
        $compilerArgs+=('/r:'+(Join-Path $root 'SDK/tools/antlr.runtime.dll'))
        $compilerArgs+=@((Join-Path $root 'recovery/src/App/Compatibility/LegacyTextureAdapter.cs'),(Join-Path $root 'recovery/probes/TextureRedirect/TextureProbe.cs'))
        Invoke-Checked $csc $compilerArgs "$evidence/application-build.log"
        $object=Join-Path $obj 'TextureAdapterEntry.obj'
        $opt=if($mode -eq 'Release'){'/O2'}else{'/Od'}
        Invoke-Checked 'cl.exe' @('/nologo','/c','/clr','/MD','/EHa','/fp:precise',$opt,("/AI"+$obj),("/AI"+$foundationDir),("/Fo"+$object),(Join-Path $root 'recovery/probes/TextureRedirect/TextureAdapterEntry.cpp')) "$evidence/host-build.log"
        $exe=Join-Path $bin 'NewModCompiler.exe'
        $link=@('/nologo','/LTCG','/MACHINE:X86','/SUBSYSTEM:CONSOLE','/ENTRY:TextureAdapterEntry.Main',("/OUT:"+$exe),$object,$foundation,$module)
        foreach($resource in @(Get-Content "$foundationDir/resources.json" -Raw|ConvertFrom-Json)){$link+=('/ASSEMBLYRESOURCE:'+$resource.path+','+$resource.logicalName)}
        Invoke-Checked 'link.exe' $link "$evidence/link.log"
        $config|Set-Content "$exe.config"
        Copy-Item SDK/tools/antlr.runtime.dll $bin
        & "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe $exe|Out-Host
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll','BinaryAssetBuilder.TextureCompiler.dll','BinaryAssetBuilder.TextureCompiler.dll.patch.json')
        if(@(Get-ChildItem $bin -File|Where-Object{$_.Name -notin $allowed}).Count){throw 'Unexpected texture test deployment file'}
        Move-Item $obj "$work/$mode/quarantined-obj"
        Invoke-Checked $exe @("$bin/BinaryAssetBuilder.TextureCompiler.dll","$work/fixture.tga","$evidence/adapter.txt",'exe') "$evidence/adapter.log"
        if((Get-FileHash "$evidence/adapter.txt").Hash -cne $baseline){throw 'Texture adapter output, custom data or errors differ from original'}
        $manifest="$bin/BinaryAssetBuilder.TextureCompiler.dll.patch.json"
        $saved=[IO.File]::ReadAllText($manifest)
        try {
            Remove-Item -LiteralPath $manifest
            Invoke-Checked $exe @("$bin/BinaryAssetBuilder.TextureCompiler.dll","$work/fixture.tga","$evidence/no-manifest.txt",'exe') "$evidence/no-manifest.log"
            if((Get-FileHash "$evidence/no-manifest.txt").Hash -cne $baseline){throw 'Texture adapter without deployment manifest differs from original'}
        } finally {[IO.File]::WriteAllText($manifest,$saved,[Text.UTF8Encoding]::new($false))}
        $state.configurations+=@{configuration=$mode;passed=$true;successes=6;invalidInputErrors=3;runtimeDeploymentAuditRemoved=$true;worksWithoutManifest=$true;singleDispose=$true;moduleInputsQuarantined=$true;transcriptSha256=$baseline.ToLowerInvariant();exeSha256=(Get-FileHash $exe).Hash.ToLowerInvariant()}
    }
    $state.status='passed'
    $global:LASTEXITCODE=0
    Write-Host 'PASS Texture adapter Debug/Release: original outputs/errors, reuse, single-close and operation without a deployment manifest.'
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {$state|ConvertTo-Json -Depth 8|Set-Content "$out/validation.json" -Encoding utf8;Pop-Location}

