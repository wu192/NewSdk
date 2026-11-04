#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite build-cache evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$short=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('c'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
function Need([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Within([string]$Directory,[string]$Path){
    $directory=[IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/');$path=[IO.Path]::GetFullPath($Path)
    $path.StartsWith($directory+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function EqualBytes([string]$Left,[string]$Right){
    $leftBytes=[IO.File]::ReadAllBytes($Left);$rightBytes=[IO.File]::ReadAllBytes($Right)
    Need ($leftBytes.Length -eq $rightBytes.Length) 'No-cache/cache artifact lengths differ'
    for($i=0;$i -lt $leftBytes.Length;$i++){Need ($leftBytes[$i] -eq $rightBytes[$i]) "No-cache/cache raw artifact byte differs at $i"}
}
function SameOutputs($Left,$Right){
    Need ($Left.files.Count -eq $Right.files.Count) 'No-cache/cache artifact counts differ'
    foreach($key in $Left.files.Keys){Need ($Right.files.ContainsKey($key)) "Cache output omitted $key";EqualBytes $Left.files[$key] $Right.files[$key]}
}
function DifferentOutputs($Left,$Right){
    Need ($Left.files.Count -eq $Right.files.Count) 'Changed fixture output layout differs'
    $changed=$false
    foreach($key in $Left.files.Keys){Need ($Right.files.ContainsKey($key)) "Changed fixture omitted $key";if((Hash $Left.files[$key]) -cne (Hash $Right.files[$key])){$changed=$true}}
    Need $changed 'Input mutation did not exercise any observable compiled output change'
}
function CacheFiles([string]$Mod,[string]$Destination){
    $files=@{};$cache=BuildCacheDirectory $Mod $Destination
    if(Test-Path -LiteralPath $cache){Get-ChildItem -LiteralPath $cache -File -Recurse|ForEach-Object{$files[$_.FullName.Substring($cache.Length)]=Hash $_.FullName}}
    return $files
}
function CacheUnchanged([string]$Mod,[string]$Destination,$Before){
    $after=CacheFiles $Mod $Destination;Need ($after.Count -eq $Before.Count) 'Failed/cancelled build persisted additional asset-cache files'
    foreach($key in $Before.Keys){Need ($after.ContainsKey($key) -and $after[$key] -ceq $Before[$key]) 'Failed/cancelled build changed previously confirmed asset-cache bytes'}
}
function Cache($Case,[string]$Expected){
    $statistics=$Case.report.cache
    Need ($null -ne $statistics) 'Durable report omitted actual asset-cache statistics'
    if($Expected -eq 'disabled'){Need ($statistics.hits -eq 0 -and $statistics.misses -eq 0) 'Disabled cache recorded reuse';return}
    Need ($statistics.status -ceq 'BuildAssetCache') 'Build cache was accepted without the actual asset-cache implementation'
    if($Expected -eq 'cold'){Need ($statistics.hits -eq 0 -and $statistics.misses -gt 0) 'Cold asset cache did not record real misses'}
    elseif($Expected -eq 'hot'){Need ($statistics.hits -gt 0 -and $statistics.misses -eq 0) 'Hot asset cache did not reuse all fixture assets'}
    elseif($Expected -eq 'invalidated'){Need ($statistics.misses -gt 0) 'Changed dependency did not invalidate cached assets'}
    else{throw "Unknown cache expectation: $Expected"}
}
function SameLengthMutation([string]$Path,[string]$From,[string]$To){
    $before=[IO.File]::ReadAllBytes($Path);$stamp=[IO.File]::GetLastWriteTimeUtc($Path)
    # Match raw UTF-8 bytes to retain BOM, line endings and all other bytes.
    $find=[Text.Encoding]::UTF8.GetBytes($From);$replace=[Text.Encoding]::UTF8.GetBytes($To)
    Need ($find.Length -eq $replace.Length -and $find.Length -gt 0) 'Mutation tokens must have identical UTF-8 lengths'
    $offset=-1
    for($i=0;$i -le $before.Length-$find.Length;$i++){
        $match=$true;for($j=0;$j -lt $find.Length;$j++){if($before[$i+$j] -ne $find[$j]){$match=$false;break}}
        if($match){$offset=$i;break}
    }
    Need ($offset -ge 0) "Mutation token absent: $Path"
    [Array]::Copy($replace,0,$before,$offset,$replace.Length)
    [IO.File]::WriteAllBytes($Path,$before)
    [IO.File]::SetLastWriteTimeUtc($Path,$stamp)
    Need (([IO.File]::ReadAllBytes($Path)).Length -eq $before.Length -and [IO.File]::GetLastWriteTimeUtc($Path) -eq $stamp) 'Mutation did not preserve exact input length and timestamp'
}
function MakeMod([string]$Path,[string]$Probe){
    New-Item -ItemType Directory -Path "$Path/Data/AptUI" -Force|Out-Null
    [IO.File]::WriteAllText("$Path/Data/Armor.xml",$Probe,[Text.UTF8Encoding]::new($false))
    # Original Texture and Audio-family PathMusicEvent routes both use /bc;
    # XML/Armor does not. Dependencies must invalidate only the affected asset.
    [IO.File]::WriteAllText("$Path/Data/Mod.xml",'<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude"><Includes><Include type="all" source="Armor.xml"/></Includes><xi:include href="XArmor.xml"/><Texture id="BuildCacheTexture" File="cache.tga" Type="StandardTexture" GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/><PathMusicEvent id="CacheEvent" PathfinderEventHeader="events.h" IsXferable="true"/></AssetDeclaration>')
    [IO.File]::WriteAllText("$Path/Data/XArmor.xml",'<ArmorTemplate xmlns="uri:ea.com:eala:asset" id="XIncludeArmor" Default="22.5%"/>')
    [IO.File]::WriteAllText("$Path/Data/events.h","#define PATH_EVENT_CacheEvent 0x12345678`r`n")
    & python "$root/recovery/probes/TextureRedirect/MakeFixture.py" "$Path/Data/cache.tga"
    Need ($LASTEXITCODE -eq 0) 'Build-cache deterministic Texture fixture preparation failed'
    [IO.File]::WriteAllText("$Path/Data/AptUI/Payload.xml",'<AssetDeclaration xmlns="uri:ea.com:eala:asset"><AptAptData id="BuildCachePayload" File="payload.bin"/></AssetDeclaration>')
    [IO.File]::WriteAllBytes("$Path/Data/AptUI/payload.bin",[byte[]](1,2,3,4,5,6,7))
}
. "$PSScriptRoot/CompilerCacheLayout.ps1"
function ModCacheRoot([string]$Mod){return Get-CompilerTestCacheRoot $script:sdk $Mod}
function BuildCacheDirectory([string]$Mod,[string]$Destination){
    return Join-Path (ModCacheRoot $Mod) ('validation/'+[IO.Path]::GetFileName($Destination)+'/assets')
}
function RunCli([string]$Name,[string]$Mod,[string]$Destination,[bool]$Enabled,[int]$Expected=0,[bool]$Cancel=$false,[string]$HoldReadFile=$null,[string[]]$Extra=@()){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Force -Path $Destination|Out-Null
    $arguments=@('build','--sc=false','--iod',(Join-Path (ModCacheRoot $Mod) ('validation/'+$Name+'/intermediate')),'--bcd',(BuildCacheDirectory $Mod $Destination),'--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-format','loose','--package-content','base,apt',('--bc='+$Enabled.ToString().ToLowerInvariant()))+$Extra
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    $request=@{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=$events;cancel=$Cancel;timeoutSeconds=$TimeoutSeconds}
    if($HoldReadFile){$request.holdReadFile=$HoldReadFile}
    $request|ConvertTo-Json -Depth 6|Set-Content "$caseOut/request.json"
    & $script:control "$caseOut/request.json" "$caseOut/control.json" *> "$caseOut/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$caseOut/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:modeState.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "Build-cache CLI failed: $Name"
        Need ($audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0) 'Asset cache introduced a product child process'
        if($Cancel){Need ($audit.signalSent -and $audit.freshEventsPathObserved -and $audit.streamStartedObserved -and $Expected -eq 6) 'Cache cancellation did not receive actual CtrlBreak'}
        $body=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        foreach($artifact in $body.artifacts){$artifact|Add-Member -NotePropertyName TestSha256 -NotePropertyValue (Hash $artifact.Path)}
        Need ($body.exitCode -eq $Expected) 'Cache process/report exit differs'
        $rows=@(Get-Content -LiteralPath $events|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
        Need ($rows[-1].eventType -ceq 'BuildFinished' -and @($rows|Where-Object{$_.eventType -ceq 'BuildStarted'}).Count -eq 1 -and @($rows|Where-Object{$_.eventType -ceq 'BuildFinished'}).Count -eq 1) 'Cache CLI terminal protocol differs'
        $sequence=0;foreach($row in $rows){$sequence++;Need ($row.buildId -ceq $body.buildId -and $row.sequence -eq $sequence) 'Cache CLI build identity/sequence differs'}
        Need ($rows[0].payload.processId -eq $audit.processId -and @($body.externalTools).Count -eq 0) 'Cache CLI production identity/external tools differs'
        if($Cancel){Need ($audit.observedBuildStarted.buildId -ceq $body.buildId -and $audit.observedBuildStarted.payload.processId -eq $audit.processId -and $audit.observedStreamStarted.buildId -ceq $body.buildId) 'CtrlBreak observed a stale cached build generation'}
        $files=@{};$archived=Join-Path $caseOut 'artifacts';New-Item -ItemType Directory -Path $archived|Out-Null
        if($Expected -eq 0){
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq 8) 'Cache fixture did not publish exactly two four-file streams'
            $published=Join-Path $Destination 'published'
            foreach($artifact in $body.artifacts){
                Need ((Within $published $artifact.Path) -and (Hash $artifact.Path) -ceq $artifact.TestSha256 -and (Get-Item -LiteralPath $artifact.Path).Length -eq $artifact.Size) 'Cached artifact identity/containment differs'
                $relative=[IO.Path]::GetRelativePath($published,$artifact.Path);$copy=Join-Path $archived $relative
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($copy))|Out-Null
                Copy-Item -LiteralPath $artifact.Path -Destination $copy;$files[$relative]=$copy
            }
        }else{Need (@($body.artifacts).Count -eq 0) 'Failed/cancelled cache build accepted artifacts'}
        Need (!(Test-Path -LiteralPath (Join-Path $Destination '.build'))) 'Direct build created the removed .build staging root'

        $entry.passed=$true;$entry.cache=$body.cache;$entry.buildId=$body.buildId
        return @{name=$Name;report=$body;files=$files;mod=$Mod;caseOut=$caseOut;destination=$Destination}
    }catch{
        foreach($name in @('stdout.log','stderr.log','controller.log')){Get-Content -LiteralPath (Join-Path $caseOut $name) -Tail 30 -ErrorAction SilentlyContinue|Out-Host}
        throw
    }finally{
        if(Test-Path -LiteralPath $report){Copy-Item -LiteralPath $report -Destination "$caseOut/build-report.json"}
        if(Test-Path -LiteralPath $events){Copy-Item -LiteralPath $events -Destination "$caseOut/build.events.jsonl"}
    }
}
function OriginalOracle($Case,[string]$Name){
    $baseline=@();$index=0
    foreach($task in $Case.report.plan.Tasks){
        $outputs=@{};$prefix=$task.Content+'/'+[IO.Path]::GetFileNameWithoutExtension($task.InputPath)
        foreach($key in $Case.files.Keys){if($key.Replace('\','/').StartsWith($prefix+'/',[StringComparison]::Ordinal)){$outputs[[IO.Path]::GetExtension($key).Substring(1)]=$Case.files[$key]}}
        Need ($outputs.Count -eq 4) 'Cache original oracle requires exact four files per task'
        $baseline+=,@{name=$(if($index++ -eq 0){'mod-a'}else{'mod-b'});modRoot=$Case.mod;inputPath=$task.InputPath;sdkRoot=$script:sdk;originalToolsRoot=(Join-Path $root 'SDK/tools');stagingDirectory=([IO.Path]::GetDirectoryName($outputs.manifest));outputs=$outputs;expectedTextures=$(if($task.Content -ceq 'base'){@('Texture:BuildCacheTexture')}else{@()});scope='Current build-cache fixture, strict original BAB raw four-output comparison; no normalization'}
    }
    $fixture=Join-Path $script:modeOut "$Name.json"
    @{succeeded=$true;sourceCommit=$script:state.sourceCommit;runId=$env:GITHUB_RUN_ID;baselineCases=$baseline;scope='One Armor/include stream and one file-backed APT stream, not full SDK'}|ConvertTo-Json -Depth 9|Set-Content $fixture
    & "$PSScriptRoot/TestCompilerBackendBaseline.ps1" -FixtureResult $fixture -OutputDirectory (Join-Path $script:modeOut $Name) -TimeoutSeconds $TimeoutSeconds
    Need ($LASTEXITCODE -eq 0) 'Build-cache independent original BAB oracle failed'
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Actual production /bc asset-cache CLI acceptance using cacheable Audio-family PathMusicEvent plus noncached Texture, Armor Include/XInclude and file-backed APT streams; raw four outputs, actual asset hits/misses, same-length/same-mtime dependency invalidation, MOD isolation, actual cancellation and failure/retry; AudioFile codec/CustomData, /sc and complete SDK remain separate'}
try{
    Need ($state.sourceCommit -cmatch '^[0-9a-f]{40}$') 'Cannot identify source commit'
    Need (!(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'reuse.json'))) 'Build-cache acceptance requires current production EXE bytes'

    $state.testSourceCommit=$state.sourceCommit;$state.binarySourceCommit=$state.sourceCommit
    $replayFiles=@{};$replayPath=Join-Path $CompilerDirectory 'candidate-replay.json'
    if(Test-Path -LiteralPath $replayPath){
        $replay=Get-Content -LiteralPath $replayPath -Raw|ConvertFrom-Json
        $fixedSource='fb6f67a0aaf5889bcd950485c41a883bc5807523'
        Need ($replay.prepared -eq $true -and $replay.candidateValidated -eq $false -and $replay.productionSourcesUnchanged -eq $true -and $replay.sourceCommit -ceq $fixedSource -and $replay.binarySourceCommit -ceq $fixedSource -and $replay.candidateCommit -ceq $state.sourceCommit -and $replay.testSourceCommit -ceq $state.sourceCommit -and [string]$replay.runId -ceq '40360344360' -and [string]$replay.runAttempt -ceq '1' -and $replay.archiveSha256 -ceq 'd1308d63759466789eb657ac3389a78f2274c52d261b0e078144923af435d8de') 'Candidate replay identity or preparation contract differs'
        # The fixed archive checker verifies API status, successful scopes and
        # raw receipts. Independently re-evaluate unchanged production inputs.
        $closureCode='import sys;sys.path.insert(0,sys.argv[1]);from PrepareCompilerReplay import closure;closure(sys.argv[2],sys.argv[3],sys.argv[4])'
        & python -c $closureCode $PSScriptRoot $root $fixedSource $state.sourceCommit *> "$out/replay-closure.log"
        Need ($LASTEXITCODE -eq 0) 'Candidate replay production-source closure rejected'
        $expectedFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($configuration in @('Debug','Release')){foreach($fileName in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){$null=$expectedFiles.Add($configuration+'/bin/'+$fileName)}}
        Need (@($replay.files).Count -eq 6) 'Candidate replay requires exactly six runtime identities'
        foreach($identity in $replay.files){
            Need ($expectedFiles.Remove([string]$identity.path) -and $identity.sha256 -cmatch '^[0-9a-f]{64}$') 'Duplicate or unexpected candidate replay runtime identity'
            Need ((Hash (Join-Path $CompilerDirectory $identity.path)) -ceq $identity.sha256) 'Candidate replay runtime bytes differ from validated archive receipt'
            $replayFiles[[string]$identity.path]=[string]$identity.sha256
        }
        Need ($expectedFiles.Count -eq 0) 'Candidate replay runtime set is incomplete'
        $state.binarySourceCommit=$fixedSource;$state.candidateReplay=$replay
        $state.scope+='; fixed binarySourceCommit replay with separately recorded current testSourceCommit and unchanged production inputs; this is not a current production rebuild'
    }
    $control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    $state.scriptSha256=Hash $PSCommandPath;$state.controllerSha256=Hash $control
    foreach($mode in @('Debug','Release')){
        $script:modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $modeWork=(New-Item -ItemType Directory -Path (Join-Path $short $mode.Substring(0,1))).FullName
        $script:runtime=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'r')).FullName
        foreach($file in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){
            Copy-Item -LiteralPath (Join-Path $CompilerDirectory "$mode/bin/$file") -Destination $runtime
            if($replayFiles.Count -gt 0){Need ((Hash (Join-Path $runtime $file)) -ceq $replayFiles[$mode+'/bin/'+$file]) 'Copied candidate replay runtime differs from validated identity'}
        }
        $script:exe=Join-Path $runtime 'NewModCompiler.exe'
        if($replayFiles.Count -gt 0){
            & "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe $exe *> "$modeOut/prepare-texture.log";Need ($LASTEXITCODE -eq 0) 'Build-cache Texture deployment failed'
        }else{
            $build=Get-Content -LiteralPath (Join-Path $CompilerDirectory "$mode/build.json") -Raw|ConvertFrom-Json
            Need ($build.exeSha256 -ceq (Hash $exe) -and $build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) 'Cache/input-root acceptance requires the current source texture build'
            Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 3) 'Unexpected source runtime deployment files'
        }
        $script:modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 's')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $probe=[IO.File]::ReadAllText("$root/SDK/Mods/mymod/Data/RecoveryArmorProbe.xml")
        $mod=Join-Path $modeWork 'm';MakeMod $mod $probe
        $cachedOut=Join-Path $modeWork 'cache';$plainOut=Join-Path $modeWork 'plain'
        $plain=RunCli 'disabled' $mod $plainOut $false;Cache $plain 'disabled'
        $cold=RunCli 'cold' $mod $cachedOut $true;Cache $cold 'cold';SameOutputs $plain $cold
        $hot=RunCli 'hot' $mod $cachedOut $true;Cache $hot 'hot';SameOutputs $plain $hot
        OriginalOracle $hot 'original-initial'
        # Armor/APT edits keep both cacheable Texture and PathMusicEvent hot.
        # Texture payload and events.h edits each require one actual asset miss.
        SameLengthMutation "$mod/Data/Armor.xml" '72.5%' '12.5%'
        $include=RunCli 'include-content-invalidated' $mod $cachedOut $true;Cache $include 'hot';DifferentOutputs $hot $include
        $includePlain=RunCli 'include-content-no-cache' $mod $plainOut $false;SameOutputs $includePlain $include
        $includeHot=RunCli 'include-content-hot' $mod $cachedOut $true;Cache $includeHot 'hot';SameOutputs $includePlain $includeHot
        SameLengthMutation "$mod/Data/XArmor.xml" '22.5%' '32.5%'
        $xinclude=RunCli 'xinclude-content-invalidated' $mod $cachedOut $true;Cache $xinclude 'hot';DifferentOutputs $includeHot $xinclude
        $includePlain=RunCli 'xinclude-content-no-cache' $mod $plainOut $false;SameOutputs $includePlain $xinclude
        $includeHot=RunCli 'xinclude-content-hot' $mod $cachedOut $true;Cache $includeHot 'hot';SameOutputs $includePlain $includeHot
        $payload="$mod/Data/AptUI/payload.bin";$stamp=[IO.File]::GetLastWriteTimeUtc($payload);$bytes=[IO.File]::ReadAllBytes($payload);$bytes[0]=9
        [IO.File]::WriteAllBytes($payload,$bytes);[IO.File]::SetLastWriteTimeUtc($payload,$stamp)
        Need ((Get-Item -LiteralPath $payload).Length -eq 7 -and [IO.File]::GetLastWriteTimeUtc($payload) -eq $stamp) 'Payload mutation failed to preserve length/mtime'
        $changed=RunCli 'payload-content-invalidated' $mod $cachedOut $true;Cache $changed 'hot';DifferentOutputs $includeHot $changed
        $changedPlain=RunCli 'payload-content-no-cache' $mod $plainOut $false;SameOutputs $changedPlain $changed
        $changedHot=RunCli 'payload-content-hot' $mod $cachedOut $true;Cache $changedHot 'hot';SameOutputs $changedPlain $changedHot
        $texture="$mod/Data/cache.tga";$stamp=[IO.File]::GetLastWriteTimeUtc($texture);$bytes=[IO.File]::ReadAllBytes($texture);$originalLength=$bytes.Length;$bytes[18]=253
        [IO.File]::WriteAllBytes($texture,$bytes);[IO.File]::SetLastWriteTimeUtc($texture,$stamp)
        Need ((Get-Item -LiteralPath $texture).Length -eq $originalLength -and [IO.File]::GetLastWriteTimeUtc($texture) -eq $stamp) 'Texture mutation failed to preserve length/mtime'
        $textureChanged=RunCli 'texture-content-invalidated' $mod $cachedOut $true;Cache $textureChanged 'invalidated';Need ($textureChanged.report.cache.hits -gt 0) 'Texture edit discarded unrelated Audio cache';DifferentOutputs $changedHot $textureChanged
        $changedPlain=RunCli 'texture-content-no-cache' $mod $plainOut $false;SameOutputs $changedPlain $textureChanged
        $changedHot=RunCli 'texture-content-hot' $mod $cachedOut $true;Cache $changedHot 'hot';SameOutputs $changedPlain $changedHot
        SameLengthMutation "$mod/Data/events.h" '0x12345678' '0x23456789'
        $audioChanged=RunCli 'audio-header-content-invalidated' $mod $cachedOut $true;Cache $audioChanged 'invalidated';Need ($audioChanged.report.cache.hits -gt 0) 'Audio dependency edit discarded unrelated Texture cache';DifferentOutputs $changedHot $audioChanged
        $changedPlain=RunCli 'audio-header-content-no-cache' $mod $plainOut $false;SameOutputs $changedPlain $audioChanged
        $changedHot=RunCli 'audio-header-content-hot' $mod $cachedOut $true;Cache $changedHot 'hot';SameOutputs $changedPlain $changedHot
        OriginalOracle $changedHot 'original-mutated'
        # Semantically neutral edits still require content invalidation. Keep
        # original BOM/length/mtime, and prove resulting bytes stay unchanged.
        SameLengthMutation "$sdk/Schemas/xsd/AssetTypeTexture.xsd" 'elementFormDefault="qualified"' "elementFormDefault='qualified'"
        $schema=RunCli 'schema-content-invalidated' $mod $cachedOut $true;Cache $schema 'hot';SameOutputs $changedPlain $schema
        SameLengthMutation "$sdk/tools/BinaryAssetBuilder.exe.config" 'monitorPaths="..\"' "monitorPaths='..\'"
        $config=RunCli 'configuration-content-invalidated' $mod $cachedOut $true;Cache $config 'hot';SameOutputs $changedPlain $config
        SameLengthMutation "$runtime/NewModCompiler.exe.config" 'version="v4.0"' "version='v4.0'"
        $runtimeChange=RunCli 'runtime-content-invalidated' $mod $cachedOut $true;Cache $runtimeChange 'hot';SameOutputs $changedPlain $runtimeChange
        $runtimeHot=RunCli 'runtime-content-hot' $mod $cachedOut $true;Cache $runtimeHot 'hot';SameOutputs $changedPlain $runtimeHot
        $other=Join-Path $modeWork 'b';MakeMod $other ($probe.Replace('72.5%','32.5%'))
        $otherCold=RunCli 'second-mod' $other $cachedOut $true;Cache $otherCold 'cold'
        $otherPlain=RunCli 'second-mod-no-cache' $other $plainOut $false;SameOutputs $otherPlain $otherCold
        $returnA=RunCli 'first-mod-return' $mod $cachedOut $true;Cache $returnA 'hot';SameOutputs $changedPlain $returnA
        $armorPath="$mod/Data/Armor.xml";$valid=[IO.File]::ReadAllBytes($armorPath)
        $confirmed=CacheFiles $mod $cachedOut
        [IO.File]::WriteAllText($armorPath,'<AssetDeclaration>broken')
        $failed=RunCli 'invalid-include' $mod $cachedOut $true 4
        CacheUnchanged $mod $cachedOut $confirmed
        [IO.File]::WriteAllBytes($armorPath,$valid)
        $retry=RunCli 'retry-after-invalid-include' $mod $cachedOut $true;SameOutputs $changedPlain $retry
        $confirmed=CacheFiles $mod $cachedOut
        $cancelled=RunCli 'actual-cancel' $mod $cachedOut $true 6 $true
        CacheUnchanged $mod $cachedOut $confirmed
        $afterCancel=RunCli 'retry-after-actual-cancel' $mod $cachedOut $true;SameOutputs $changedPlain $afterCancel
        $held=Join-Path (Join-Path (ModCacheRoot $mod) 'streams') ([IO.Path]::GetRelativePath((Join-Path $cachedOut 'published'),@($afterCancel.report.artifacts)[0].Path))
        $lockedHot=RunCli 'unchanged-locked-output' $mod $cachedOut $true 0 $false $held;SameOutputs $changedPlain $lockedHot

        $unrelated=Join-Path $cachedOut 'published/unrelated.keep'
        [IO.File]::WriteAllText($unrelated,'preserve unrelated output')
        $repeat=RunCli 'repeat-with-unrelated-output' $mod $cachedOut $true
        SameOutputs $changedPlain $repeat
        Need ([IO.File]::ReadAllText($unrelated) -ceq 'preserve unrelated output') 'Repeated compilation removed unrelated output'
        $modeState.passed=$true
    }
    $state.succeeded=$true;$global:LASTEXITCODE=0
}catch{$state.error=$_.Exception.ToString();throw}
finally{
    $state|ConvertTo-Json -Depth 18|Set-Content "$out/validation.json"
    try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "Build-cache work cleanup failed: $_"}
}
