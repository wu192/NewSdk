#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerCacheLayout.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite UI acceptance evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$work=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('ui'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
function Need([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Snapshot([string]$Directory){
    $files=@{};Get-ChildItem -LiteralPath $Directory -Recurse -File|ForEach-Object{$files[[IO.Path]::GetRelativePath($Directory,$_.FullName)]=Hash $_.FullName};return $files
}
function Equal($Left,$Right){
    Need ($Left.Count -eq $Right.Count) 'Four-stream file counts differ'
    foreach($key in $Left.Keys){Need ($Right.ContainsKey($key) -and $Left[$key] -ceq $Right[$key]) "Final stream bytes differ: $key"}
}
function FailedStreamsCleaned($Previous,[string]$Published,[string]$CacheRoot){
    Need ($Previous.Count -eq 4) 'Failed-stream cleanup requires the previous complete four-file set'
    foreach($key in $Previous.Keys){Need (!(Test-Path -LiteralPath (Join-Path (Join-Path $CacheRoot 'streams') $key))) "Failed cache stream file was retained: $key"}
    Equal $Previous (Snapshot $Published)
}
function Changed($Left,$Right){
    Need ($Left.Count -eq $Right.Count) 'A source edit changed the published file layout'
    Need (@($Left.Keys|Where-Object{$Left[$_] -cne $Right[$_]}).Count -gt 0) 'Source edit did not change any final stream bytes'
}
function InspectStreams([string]$Published){
    $manifest=@(Get-ChildItem -LiteralPath $Published -Recurse -Filter *.manifest -File)
    Need ($manifest.Count -eq 1) 'Expected one mixed XML/UI stream'
    $path=$manifest[0].FullName;$base=$path.Substring(0,$path.Length-9)
    $bytes=[IO.File]::ReadAllBytes($path)
    Need ($bytes.Length -ge 48 -and $bytes[0] -eq 0 -and $bytes[1] -eq 1 -and $bytes[2] -eq 6 -and $bytes[3] -eq 0) 'Expected linked Win32 manifest v6'
    foreach($extension in @('.bin','.relo','.imp')){
        $data=[IO.File]::ReadAllBytes($base+$extension)
        Need ($data.Length -ge 4 -and [BitConverter]::ToUInt32($data,0) -eq [BitConverter]::ToUInt32($bytes,4)) "Linked checksum differs: $extension"
    }
    $count=[BitConverter]::ToUInt32($bytes,12);$nameSize=[BitConverter]::ToUInt32($bytes,40)
    $nameStart=48L+48L*$count+[BitConverter]::ToUInt32($bytes,32)+[BitConverter]::ToUInt32($bytes,36)
    Need ($nameStart+$nameSize+[BitConverter]::ToUInt32($bytes,44) -eq $bytes.Length) 'Manifest table bounds differ'
    $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$sizes=0L
    for($i=0L;$i -lt $count;$i++){
        $entry=48L+48L*$i;$offset=[BitConverter]::ToUInt32($bytes,[int]($entry+24));Need ($offset -lt $nameSize) 'Asset name escaped name table'
        $start=$nameStart+$offset;$end=$start;while($end -lt $nameStart+$nameSize -and $bytes[$end] -ne 0){$end++}
        Need ($end -lt $nameStart+$nameSize) 'Unterminated asset name'
        $name=[Text.Encoding]::ASCII.GetString($bytes,[int]$start,[int]($end-$start));Need ($names.Add($name)) "Duplicate generated asset: $name"
        $sizes+=[BitConverter]::ToUInt32($bytes,[int]($entry+32))
    }
    foreach($movie in $script:expectedMovies){foreach($part in @(@('AptAptData','apt'),@('AptConstData','const'),@('AptDatData','dat'))){Need ($names.Contains($part[0]+':'+$movie+'_'+$part[1])) "Missing generated $movie $($part[0])"}}
    if($script:expectArmor){Need ($names.Contains('ArmorTemplate:UiMixedArmor')) 'Ordinary XML asset disappeared from UI stream'}
    if($script:expectedMovies -contains 'enhancerApt'){Need (@($names|Where-Object{$_.StartsWith('AptGeometryData:')}).Count -gt 0 -and @($names|Where-Object{$_.StartsWith('Texture:')}).Count -gt 0) 'Real Enhancer geometry/texture siblings are missing'}
    Need ((Get-Item -LiteralPath ($base+'.bin')).Length -eq 4+$sizes) 'Linked binary size differs from manifest entries'
    return @{assetCount=$count;names=@($names|Sort-Object);checksum=[BitConverter]::ToUInt32($bytes,4)}
}
function Run([string]$Name,[string]$Destination,[bool]$SessionCache=$true,[int]$Expected=0){
    $case=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Path $Destination -Force|Out-Null
    $arguments=@('build','--mod',$script:mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-format','loose','--package-content',$script:content,'--performance','detailed',('--sc='+$SessionCache.ToString().ToLowerInvariant()),'--bc=true')
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    @{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $case 'stdout.log');stderr=(Join-Path $case 'stderr.log');events=$events;cancel=$false;timeoutSeconds=$TimeoutSeconds}|ConvertTo-Json -Depth 5|Set-Content "$case/request.json"
    & $script:control "$case/request.json" "$case/control.json" *> "$case/controller.log"
    $controlExit=$LASTEXITCODE;$audit=Get-Content "$case/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:modeState.cases+=,$entry
    try{
        Need ($controlExit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "UI CLI case failed: $Name"
        Need ($audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0) 'UI conversion spawned a product child process'
        $body=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;Need ($body.exitCode -eq $Expected) 'UI process/report exits differ'
        $published=Join-Path $Destination 'published'
        if($Expected -eq 0){
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq 4) 'UI build must publish exactly the final four streams'
            $entry.layout=InspectStreams $published;$files=Snapshot $published
            Need ($files.Count -eq 4) 'UI build published an intermediate or unexpected file'
        }else{Need (@($body.artifacts).Count -eq 0) 'Failed UI source accepted artifacts';$files=$null}
        $entry.passed=$true;$entry.cache=$body.cache;$entry.buildId=$body.buildId
        return @{files=$files;report=$body;published=$published}
    }catch{
        foreach($log in @('stdout.log','stderr.log','controller.log')){Get-Content -LiteralPath (Join-Path $case $log) -Tail 40 -ErrorAction SilentlyContinue|Out-Host};throw
    }finally{
        if(Test-Path -LiteralPath $report){Copy-Item -LiteralPath $report -Destination "$case/build-report.json"}
        if(Test-Path -LiteralPath $events){Copy-Item -LiteralPath $events -Destination "$case/build.events.jsonl"}
    }
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Production CLI direct binary MX2004/Flash8 FLA and AS to Core mixed MOD four streams; both unchanged user sources, file/folder routes, siblings, same-length/mtime AS edit, AS addition/deletion, duplicate generated identities and failed-stream cleanup/retry. No SWF input, Flash process or in-game playback oracle.'}
try{
    $control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    foreach($mode in @('Debug','Release')){
        $script:modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $script:modeWork=(New-Item -ItemType Directory -Path (Join-Path $work $mode)).FullName
        $script:runtime=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'runtime')).FullName
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $CompilerDirectory "$mode/bin") -File){Copy-Item -LiteralPath $file.FullName -Destination $runtime}
        $script:exe=Join-Path $runtime 'NewModCompiler.exe'
        $script:modeState=@{configuration=$mode;exeSha256=(Hash $exe);passed=$false;cases=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'sdk')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $script:mod=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'mod')).FullName
        New-Item -ItemType Directory -Path "$mod/Data/Source"|Out-Null
        foreach($movie in @('CoronaUIMain','EnhancerApt')){Copy-Item -LiteralPath "$root/recovery/fixtures/ui/$movie" -Destination "$mod/Data/Source" -Recurse}
        $xml='<AssetDeclaration xmlns="uri:ea.com:eala:asset"><ArmorTemplate id="UiMixedArmor" Default="72.5%"/><AptAptData id="CoronaUIMain_apt" File="Source/CoronaUIMain/CoronaUIMain.fla"/><AptAptData id="enhancerApt_apt" File="Source/EnhancerApt"/></AssetDeclaration>'
        [IO.File]::WriteAllText("$mod/Data/Mod.xml",$xml)
        $script:content='base';$script:expectedMovies=@('CoronaUIMain','enhancerApt');$script:expectArmor=$true
        $destination=Join-Path $modeWork 'cached';$plain=Join-Path $modeWork 'plain'
        $cold=Run 'cold' $destination;$hot=Run 'hot' $destination;Equal $cold.files $hot.files
        Need ($cold.report.cache.instanceCompile -gt 0 -and $hot.report.cache.instanceCompile -eq 0 -and $hot.report.cache.documentReuse -gt 0) 'UI hot session did not reuse all compiled siblings'
        $independent=Run 'independent-no-session' $plain $false;Equal $cold.files $independent.files
        $as="$mod/Data/Source/CoronaUIMain/CoronaUIMain.as";$stamp=[IO.File]::GetLastWriteTimeUtc($as);$before=[IO.File]::ReadAllBytes($as)
        $text=[IO.File]::ReadAllText($as);Need ($text.Contains('s_IsCorona: Boolean = true;')) 'AS mutation token absent'
        # Preserve UTF-8 BOM, source byte length and mtime to exercise real content invalidation.
        $needle=[Text.Encoding]::UTF8.GetBytes('s_IsCorona: Boolean = true;');$replacement=[Text.Encoding]::UTF8.GetBytes('s_IsCorona: Boolean = null;');$index=-1
        for($i=0;$i -le $before.Length-$needle.Length;$i++){$found=$true;for($j=0;$j -lt $needle.Length;$j++){if($before[$i+$j] -ne $needle[$j]){$found=$false;break}};if($found){$index=$i;break}}
        Need ($index -ge 0 -and $needle.Length -eq $replacement.Length) 'Invalid equal-length AS mutation'
        [Array]::Copy($replacement,0,$before,$index,$replacement.Length);[IO.File]::WriteAllBytes($as,$before);[IO.File]::SetLastWriteTimeUtc($as,$stamp)
        $changed=Run 'as-same-size-mtime' $destination;Changed $hot.files $changed.files
        Need ($changed.report.cache.instanceCompile -gt 0) 'Changed AS did not compile the generated group'
        $independent=Run 'as-no-session' $plain $false;Equal $changed.files $independent.files
        $timeline="$mod/Data/Source/EnhancerApt/enhancerApt.as";[IO.File]::WriteAllText($timeline,'trace("added-source-file");')
        $added=Run 'as-added' $destination;Changed $changed.files $added.files
        $independent=Run 'added-no-session' $plain $false;Equal $added.files $independent.files
        Remove-Item -LiteralPath $timeline
        $deleted=Run 'as-deleted' $destination;Equal $changed.files $deleted.files
        [IO.File]::WriteAllText($timeline,'#include "does-not-exist.as"')
        # Failure cleans the touched cache stream while retaining delivered files.
        # Retry must reconstruct the bytes from the preceding successful build.
        $cacheRoot=Get-CompilerTestCacheRoot $script:sdk $script:mod
        $null=Run 'missing-include' $destination $true 4;FailedStreamsCleaned $deleted.files $deleted.published $cacheRoot
        Remove-Item -LiteralPath $timeline
        $retry=Run 'retry' $destination;Equal $deleted.files $retry.files
        [IO.File]::WriteAllBytes("$mod/Data/collision.bin",[byte[]](1,2,3,4))
        [IO.File]::WriteAllText("$mod/Data/Mod.xml",$xml.Replace('</AssetDeclaration>','<AptConstData id="CoronaUIMain_const" File="collision.bin"/></AssetDeclaration>'))
        $null=Run 'generated-identity-collision' $destination $true 4;FailedStreamsCleaned $retry.files $retry.published $cacheRoot
        [IO.File]::WriteAllText("$mod/Data/Mod.xml",$xml)
        $recovered=Run 'collision-retry' $destination;Equal $retry.files $recovered.files
        $mixedMod=$script:mod;$script:mod=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'bare')).FullName
        New-Item -ItemType Directory -Path "$mod/Data/AptUI" -Force|Out-Null
        Copy-Item -LiteralPath "$root/recovery/fixtures/ui/CoronaUIMain" -Destination "$mod/Data/AptUI" -Recurse
        $script:content='auto';$script:expectedMovies=@('CoronaUIMain');$script:expectArmor=$false
        $bare=Run 'bare-fla-auto' (Join-Path $modeWork 'bare-output')
        Need (@($bare.report.plan.Tasks).Count -eq 1 -and $bare.report.plan.Tasks[0].Content -ceq 'apt') 'Bare FLA was not planned as one APT source task'
        $bareHot=Run 'bare-fla-hot' (Join-Path $modeWork 'bare-output');Equal $bare.files $bareHot.files
        Need ($bareHot.report.cache.instanceCompile -eq 0) 'Bare FLA hot build recompiled unchanged source'
        $barePlain=Run 'bare-fla-independent-output' (Join-Path $modeWork 'bare-plain') $false
        Equal $bare.files $barePlain.files
        $script:mod=$mixedMod
        $modeState.passed=$true
    }
    $state.succeeded=$true;Write-Host 'PASS direct UI production CLI, both attachments, mixed four streams, source invalidation and failed-stream cleanup/retry'
}finally{
    $state|ConvertTo-Json -Depth 14|Set-Content (Join-Path $out 'summary.json')
    if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}
}
