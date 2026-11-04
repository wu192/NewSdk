[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$ModDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
$ModDirectory=(Resolve-Path -LiteralPath $ModDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to replace texture codec evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$work=Join-Path ([IO.Path]::GetTempPath()) ('new-world-texture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work|Out-Null
$state=@{status='started';sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;configurations=@();failures=@();scope='Original 7.1 source images, isolated original DLL and production-object image decoding; exact raw three buffers; malformed metadata observations kept separately; not full MOD or compressed-byte validation'}
function Invoke-CodecTool([string]$Command,[string[]]$Arguments,[string]$Directory,[string]$Log){
    Push-Location $Directory
    try{& $Command @Arguments *> $Log;$code=$LASTEXITCODE}finally{Pop-Location}
    if($code -ne 0){Get-Content -LiteralPath $Log -Tail 45|Out-Host;throw "Texture codec tool failed: $Command (exit $code)"}
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
Push-Location $root
try{
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $source=(Resolve-Path -LiteralPath 'recovery/tests/TextureImageCompatibility.cs').Path
    $generator=(Resolve-Path -LiteralPath 'recovery/scripts/PrepareNewWorldTextureFixtures.py').Path
    $state.testSources=@(@($source,$generator)|ForEach-Object{@{path=[IO.Path]::GetRelativePath($root,$_).Replace('\','/');sha256=Hash $_}})
    $fixtures=Join-Path $work 'fixtures'
    Invoke-CodecTool 'python' @($generator,'--mod',$ModDirectory,'--output',$fixtures) $root "$out/fixtures.log"
    Copy-Item -LiteralPath "$fixtures/cases.json" -Destination "$out/cases.json"
    $cases=@(Get-Content -LiteralPath "$fixtures/cases.json" -Raw|ConvertFrom-Json)
    if($cases.Count -ne 255){throw 'Incomplete actual 7.1 image and metadata-boundary matrix'}
    $oracle=(New-Item -ItemType Directory "$work/original").FullName
    Copy-Item SDK/tools/*.dll $oracle
    $originalDll=Join-Path $oracle 'BinaryAssetBuilder.TextureCompiler.dll'
    $state.originalSha256=Hash $originalDll
    if($state.originalSha256 -cne '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e'){throw 'Original Texture DLL identity differs'}
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    Invoke-CodecTool $csc @('/nologo','/codepage:65001','/platform:x86','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Web.Extensions.dll',"/r:$oracle/BinaryAssetBuilder.Core.dll","/out:$oracle/Original.exe",$source) $root "$out/original-build.log"
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'|Set-Content "$oracle/Original.exe.config"
    Invoke-CodecTool "$oracle/Original.exe" @($fixtures,"$out/original",$originalDll) $oracle "$out/original-runtime.log"
    $original=Get-Content -LiteralPath "$out/original/results.json" -Raw|ConvertFrom-Json
    if($original.status -cne 'passed' -or @($original.cases).Count -ne $cases.Count){throw 'Original image fixture run failed or incomplete'}
    foreach($mode in @('Debug','Release')){
        $entry=@{configuration=$mode;passed=$false;differentBuffers=@()};$state.configurations+=,$entry
        $run=$null
        try{
            $evidence=(New-Item -ItemType Directory "$out/$mode").FullName
            $clean=Join-Path $work "$mode/clean"
            $run=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
            $fixture=Join-Path $clean 'TextureImageCompatibility.exe'
            Invoke-CodecTool $csc @('/nologo','/codepage:65001','/platform:x86','/define:TEXTURE_SOURCE_PROBE','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Web.Extensions.dll',"/r:$($run.exe)","/out:$fixture",$source) $root "$evidence/fixture-build.log"
            Invoke-CodecTool $run.exe @($fixture,$fixtures,"$evidence/source",'built-in') $clean "$evidence/runtime.log"
            $candidate=Get-Content -LiteralPath "$evidence/source/results.json" -Raw|ConvertFrom-Json
            if($candidate.status -cne 'passed' -or @($candidate.cases).Count -ne $cases.Count){throw 'Candidate input and retained-boundary matrix failed or incomplete'}
            if($candidate.pluginVersion -ne $original.pluginVersion -or $candidate.allTypesHash -ne $original.allTypesHash){throw 'Original and candidate Texture metadata differ'}
            for($i=0;$i -lt $cases.Count;$i++){
                $left=$original.cases[$i];$right=$candidate.cases[$i]
                if($left.name -cne $cases[$i].name -or $right.name -cne $cases[$i].name -or $left.inputSha256 -cne $right.inputSha256){throw 'Image case membership/order/input identity differs'}
                if(!$cases[$i].compareBytes){continue}
                if(!$left.accepted -or !$right.accepted){throw "A valid actual image was rejected: $($cases[$i].name)"}
                $a=[IO.File]::ReadAllBytes((Join-Path "$out/original" $left.buffers));$b=[IO.File]::ReadAllBytes((Join-Path "$evidence/source" $right.buffers))
                if(![Linq.Enumerable]::SequenceEqual[byte]($a,$b)){
                    $at=0;while($at -lt [Math]::Min($a.Length,$b.Length) -and $a[$at] -eq $b[$at]){$at++}
                    $entry.differentBuffers+=@{name=$cases[$i].name;originalBytes=$a.Length;candidateBytes=$b.Length;firstDifference=$at;originalSha256=$left.buffersSha256;candidateSha256=$right.buffersSha256}
                }
            }
            $entry.caseCount=$cases.Count;$entry.exactThreeBufferCases=@($cases|Where-Object{$_.compareBytes}).Count
            $entry.exeSha256=$run.productExeSha256;$entry.validationHostSha256=$run.validationHostSha256
            if($entry.differentBuffers.Count){throw 'Raw original image-decoding three-buffer differences remain; see validation.json'}
            $entry.passed=$true
        }catch{
            $entry.error=$_.Exception.ToString();$state.failures+=@{configuration=$mode;error=$entry.error}
            Write-Host "FAIL Texture image compatibility $mode`: $($entry.error)"
        }finally{
            if($run){try{Complete-CompilerValidationRun $run}catch{$entry.passed=$false;$state.failures+=@{configuration=$mode;error=$_.Exception.ToString()}}}
        }
    }
    if($state.failures.Count){throw 'Texture image compatibility failed; all independent configuration results retained'}
    $state.status='passed';$global:LASTEXITCODE=0
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$state|ConvertTo-Json -Depth 16|Set-Content "$out/validation.json" -Encoding utf8;Pop-Location}
