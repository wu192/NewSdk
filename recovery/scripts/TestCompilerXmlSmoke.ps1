[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite XML smoke evidence: $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-xml-smoke-'+[Guid]::NewGuid().ToString('N'))
$oracle=Join-Path $work 'original'
New-Item -ItemType Directory -Force $work,$oracle|Out-Null
$oldTranscript=$env:XML_PRODUCT_TRANSCRIPT
$oldPath=$env:PATH
$state=@{
    schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT
    scope='Actual production EXE 300 XML routes: complete existing joint default/completion smoke; NOT 64 per-group full differential/layout/rejection matrices or SDK acceptance'
    fullGroupMatricesPassed=$false;sdkValidated=$false;status='started';modes=@()
}
function Invoke-XmlSmoke([string]$Executable,[string[]]$Arguments,[string]$Directory,[string]$Log) {
    Push-Location $Directory
    try {& $Executable @Arguments *> $Log;$code=$LASTEXITCODE}
    finally {Pop-Location}
    if($code -ne 0){Get-Content $Log -Tail 80|Out-Host;throw "XML route smoke host failed: $Executable ($code)"}
}
function Check-XmlSmoke([string]$ResultPath,[string]$Transcript,[bool]$Production) {
    $result=Get-Content $ResultPath -Raw|ConvertFrom-Json
    $metadata=if($Production){300}else{0}
    if(!$result.succeeded -or $result.fullMetadataRoutes -ne $metadata -or $result.types -ne 300 -or $result.testedTypes -ne 300 -or
        $result.fixtures -ne 1200 -or $result.comparisons -ne 3600 -or $result.completionFixtures -ne 208 -or $result.completionComparisons -ne 624 -or
        ($result.platforms -join ',') -cne '0,1,2,99' -or $result.sourceCommit -cne $state.sourceCommit -or $result.runId -cne $state.runId){throw 'Incomplete or mismatched XML smoke report'}
    if(@($result.typeCoverage.PSObject.Properties).Count -ne 300 -or @($result.typeCoverage.PSObject.Properties|Where-Object{$_.Value -ne 4}).Count -ne 0){throw 'Incomplete XML per-route platform coverage'}
    $rows=@(Get-Content $Transcript)
    if($rows.Count -ne 1408 -or @($rows|Where-Object{($_ -split "\t").Count -ne 5}).Count -ne 0){throw 'Incomplete XML fixture transcript'}
}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    if(!$state.runId){$state.runId='local-'+[Guid]::NewGuid().ToString('N')}
    $source=Join-Path $work 'XmlProductSmoke.cs'
    $manifest=Join-Path $work 'expected-xml-roots.tsv'
    & python recovery/scripts/PrepareCompilerXmlSmoke.py --source recovery/tests/XmlPriorityLinkOracle.cs --output $source --manifest $manifest
    if($LASTEXITCODE -ne 0){throw 'XML fixture binding adaptation failed'}
    $state.fixtureSha256=(Get-FileHash recovery/tests/XmlPriorityLinkOracle.cs -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.adapterSha256=(Get-FileHash recovery/scripts/PrepareCompilerXmlSmoke.py -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.generatedHostSha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Copy-Item SDK/tools/*.dll $oracle
    $original=Join-Path $oracle 'BinaryAssetBuilder.XmlCompiler.dll'
    $state.originalDllSha256=(Get-FileHash $original -Algorithm SHA256).Hash.ToLowerInvariant()
    if($state.originalDllSha256 -cne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6'){throw 'Original XML compiler fingerprint mismatch'}
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    & $csc /nologo /codepage:65001 /platform:x86 /define:ORACLE /r:System.Xml.dll /r:System.Web.Extensions.dll "/r:$oracle/BinaryAssetBuilder.Core.dll" "/out:$oracle/XmlSmoke.exe" $source *> "$out/original-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/original-build.log" -Tail 80|Out-Host;throw 'XML original smoke host build failed'}
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>'|Set-Content "$oracle/XmlSmoke.exe.config"
    $env:XML_PRODUCT_TRANSCRIPT=Join-Path $out 'original.tsv'
    Invoke-XmlSmoke "$oracle/XmlSmoke.exe" @($original,'unused',"$out/original.json",$manifest,$state.sourceCommit,$state.runId) $oracle "$out/original.log"
    Check-XmlSmoke "$out/original.json" $env:XML_PRODUCT_TRANSCRIPT $false
    $baseline=(Get-FileHash $env:XML_PRODUCT_TRANSCRIPT -Algorithm SHA256).Hash
    # Product hosts cannot find original SDK DLLs through their working folder
    # or PATH. They reference only the actual product EXE.
    $env:PATH=(@($oldPath -split ';'|Where-Object{$_ -notmatch '[\\/]SDK[\\/]tools'}) -join ';')
    foreach($mode in @('Debug','Release')) {
        $modeOut=Join-Path $out $mode
        $clean=Join-Path $work $mode
        New-Item -ItemType Directory -Force $modeOut,$clean|Out-Null
        $validationRun=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
        $exe=Join-Path $clean 'NewModCompiler.exe'
        & $csc /nologo /codepage:65001 /platform:x86 /r:System.Xml.dll /r:System.Web.Extensions.dll "/r:$exe" "/out:$clean/XmlSmoke.exe" $source *> "$modeOut/host-build.log"
        if($LASTEXITCODE -ne 0){Get-Content "$modeOut/host-build.log" -Tail 80|Out-Host;throw "$mode production XML smoke host build failed"}
        Copy-Item "$exe.config" "$clean/XmlSmoke.exe.config"
        foreach($forbidden in @('BinaryAssetBuilder.Core.dll','BinaryAssetBuilder.XmlCompiler.dll','BinaryAssetBuilder.Tokenizer.dll','Recovered.XmlCompilerPriority.dll')){
            if(Test-Path (Join-Path $clean $forbidden)){throw "Forbidden runtime oracle/bridge DLL in product host: $forbidden"}
        }
        $env:XML_PRODUCT_TRANSCRIPT=Join-Path $modeOut 'recovered.tsv'
        Invoke-XmlSmoke $validationRun.exe @("$clean/XmlSmoke.exe",'unused',$exe,"$modeOut/result.json",$manifest,$state.sourceCommit,$state.runId) $clean "$modeOut/host.log"
        Check-XmlSmoke "$modeOut/result.json" $env:XML_PRODUCT_TRANSCRIPT $true
        if((Get-FileHash $env:XML_PRODUCT_TRANSCRIPT -Algorithm SHA256).Hash -cne $baseline){throw "$mode XML original/product complete transcript mismatch"}
        Complete-CompilerValidationRun $validationRun
        $state.modes+=@{validationHostSha256=$validationRun.validationHostSha256;configuration=$mode;passed=$true;routes=300;platforms=@(0,1,2,99);defaultFixtures=1200;completionFixtures=208;bufferComparisons=4224;transcriptSha256=$baseline.ToLowerInvariant();exeSha256=$validationRun.productExeSha256}
    }
    $state.status='passed joint route smoke only; full per-group matrices and SDK acceptance remain separate'
} catch {$state.status='failed';$state.error=$_.Exception.Message;throw}
finally {
    $env:PATH=$oldPath
    $env:XML_PRODUCT_TRANSCRIPT=$oldTranscript
    $state|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'validation.json')
    Pop-Location
}


