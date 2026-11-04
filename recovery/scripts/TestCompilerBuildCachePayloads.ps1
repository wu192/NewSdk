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
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite build-cache payload evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$short=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('p'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
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
    # CDATA names carry the instance hash and may legitimately change with the
    # input. The unchanged-versus-cache comparison still requires exact names.
    foreach($key in $Left.files.Keys){if(!$Right.files.ContainsKey($key)){$changed=$true}elseif((Hash $Left.files[$key]) -cne (Hash $Right.files[$key])){$changed=$true}}
    Need $changed 'Input mutation did not exercise any observable compiled output change'
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
. "$PSScriptRoot/CompilerCacheLayout.ps1"
function ModCacheRoot([string]$Mod){return Get-CompilerTestCacheRoot $script:sdk $Mod}
function BuildCacheDirectory([string]$Mod,[string]$Destination){
    return Join-Path (ModCacheRoot $Mod) ('validation/'+[IO.Path]::GetFileName($Destination)+'/assets')
}
function RunCli([string]$Name,[string]$Mod,[string]$Destination,[bool]$Enabled,[int]$Expected=0,[bool]$Cancel=$false,[string]$HoldReadFile=$null,[string[]]$Extra=@()){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Force -Path $Destination|Out-Null
    $arguments=@('build','--sc=false','--iod',(Join-Path (ModCacheRoot $Mod) ('validation/'+$Name+'/intermediate')),'--bcd',(BuildCacheDirectory $Mod $Destination),'--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-format','loose','--package-content','base',('--bc='+$Enabled.ToString().ToLowerInvariant()))+$Extra
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    $request=@{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=$events;cancel=$Cancel;timeoutSeconds=$TimeoutSeconds}
    if($HoldReadFile){$request.holdReadFile=$HoldReadFile}
    $request|ConvertTo-Json -Depth 6|Set-Content "$caseOut/request.json"
    & $script:control "$caseOut/request.json" "$caseOut/control.json" *> "$caseOut/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$caseOut/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:modeState.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "Build-cache payload CLI failed: $Name"
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
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq 4+$script:expectedCustomData) 'Cache fixture did not publish the exact four-file stream plus allowed CustomData'
            Need (@($body.artifacts|Where-Object{$_.Kind -ceq 'custom-data' -and [IO.Path]::GetExtension($_.Path) -ceq '.cdata'}).Count -eq $script:expectedCustomData) 'Payload fixture CustomData count differs'
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
function Put([string]$Path,[string]$Text){New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($Path))|Out-Null;[IO.File]::WriteAllText($Path,$Text)}
function MakeWave([string]$Path){
    # Same known integral PCM source shape as AudioSdkSupport.Wave; no codec,
    # compression library, external encoder or imprecise floating conversion.
    $stream=[IO.File]::Create($Path);$writer=[IO.BinaryWriter]::new($stream)
    try{
        $frames=4096;$channels=2;$dataBytes=$frames*$channels*2
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int](36+$dataBytes));$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
        $writer.Write([int]16);$writer.Write([int16]1);$writer.Write([int16]$channels);$writer.Write([int]22050);$writer.Write([int](22050*$channels*2));$writer.Write([int16]($channels*2));$writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]$dataBytes)
        $values=[int16[]](0,1,-1,32767,-32768,0x1234)
        for($i=0;$i -lt $frames*$channels;$i++){$writer.Write([int16]$values[$i%$values.Length])}
    }finally{$writer.Dispose();$stream.Dispose()}
    Need ((Get-Item -LiteralPath $Path).Length -eq 44+4096*2*2) 'PCM fixture length differs from its RIFF data size'
}
function AudioMod([string]$Path){
    Put "$Path/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><AudioFile id="BuildCachePcmWave" File="../Audio/probe.wav" PCCompression="NONE" IsStreamedOnPC="false"/></AssetDeclaration>'
    New-Item -ItemType Directory -Path "$Path/Audio"|Out-Null;MakeWave "$Path/Audio/probe.wav"
}
function W3xMod([string]$Path){
    # Reuse the existing source-XSD-valid recovered W3X integration fixtures,
    # retaining its hierarchy and two cacheable animation instances. The XML
    # provider's hierarchy itself is not eligible for the asset cache.
    [xml]$animations=[IO.File]::ReadAllText("$root/SDK/Mods/W3xRecovery/Data/Animations.xml")
    [xml]$dependencies=[IO.File]::ReadAllText("$root/SDK/Mods/W3xRecovery/Data/Dependencies.xml")
    $ns=[Xml.XmlNamespaceManager]::new($animations.NameTable);$ns.AddNamespace('ea','uri:ea.com:eala:asset')
    $animation=$animations.SelectSingleNode('/ea:AssetDeclaration/ea:W3DAnimation[1]',$ns)
    $dns=[Xml.XmlNamespaceManager]::new($dependencies.NameTable);$dns.AddNamespace('ea','uri:ea.com:eala:asset')
    $hierarchy=$dependencies.SelectSingleNode('/ea:AssetDeclaration/ea:W3DHierarchy[1]',$dns)
    Need ($null -ne $animation -and $null -ne $hierarchy -and $animation.GetAttribute('Hierarchy') -ceq $hierarchy.GetAttribute('id')) 'W3X source fixture hierarchy closure changed'
    $unchanged=$animation.CloneNode($true);$unchanged.SetAttribute('id',$animation.GetAttribute('id')+'_Unchanged')
    Put "$Path/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><Includes><Include type="all" source="W3x.xml"/></Includes></AssetDeclaration>'
    Put "$Path/Data/W3x.xml" ('<AssetDeclaration xmlns="uri:ea.com:eala:asset">'+$hierarchy.OuterXml+$animation.OuterXml+$unchanged.OuterXml+'</AssetDeclaration>')
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Production /bc asset-cache replay for AudioFile PCM WAVE including complete CDATA and two W3DAnimation instances with hierarchy closure; disabled/cold/hot and partial reuse after same-length same-mtime input changes, exact four-stream and CDATA equality against independent no-cache processes; not a new original-DLL/SDK gate'}
try{
    Need ($state.sourceCommit -cmatch '^[0-9a-f]{40}$') 'Cannot identify source commit'
    Need (!(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'reuse.json'))) 'Payload cache acceptance requires current production EXE bytes'

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
    $script:control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    $state.scriptSha256=Hash $PSCommandPath;$state.controllerSha256=Hash $control
    $state.sourceInputs=@('recovery/tests/AudioSdkSupport.cs','SDK/Mods/W3xRecovery/Data/Animations.xml','SDK/Mods/W3xRecovery/Data/Dependencies.xml')|ForEach-Object{@{path=$_;sha256=(Hash (Join-Path $root $_))}}
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
            & "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe $exe *> "$modeOut/prepare-texture.log";Need ($LASTEXITCODE -eq 0) 'Payload runtime Texture deployment failed'
        }else{
            $build=Get-Content -LiteralPath (Join-Path $CompilerDirectory "$mode/build.json") -Raw|ConvertFrom-Json
            Need ($build.exeSha256 -ceq (Hash $exe) -and $build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) 'Cache/input-root acceptance requires the current source texture build'
            Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 3) 'Unexpected source runtime deployment files'
        }
        $script:modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@();fixtures=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 's')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        foreach($family in @('audio','w3x')){
            $mod=Join-Path $modeWork $family;$cachedOut=Join-Path $modeWork ($family+'-cache');$plainOut=Join-Path $modeWork ($family+'-plain')
            if($family -eq 'audio'){AudioMod $mod;$script:expectedCustomData=1}else{W3xMod $mod;$script:expectedCustomData=0}
            $before=RunCli ($family+'-disabled') $mod $plainOut $false;Cache $before 'disabled'
            $cold=RunCli ($family+'-cold') $mod $cachedOut $true;Cache $cold 'cold';SameOutputs $before $cold
            $hot=RunCli ($family+'-hot') $mod $cachedOut $true;Cache $hot 'hot';SameOutputs $before $hot
            if($family -eq 'audio'){
                $input="$mod/Audio/probe.wav";$stamp=[IO.File]::GetLastWriteTimeUtc($input);$bytes=[IO.File]::ReadAllBytes($input);$length=$bytes.Length
                # Change one PCM sample, not RIFF metadata; force payload change.
                $bytes[44]=0x34;$bytes[45]=0x12
                [IO.File]::WriteAllBytes($input,$bytes);[IO.File]::SetLastWriteTimeUtc($input,$stamp)
                Need ((Get-Item -LiteralPath $input).Length -eq $length -and [IO.File]::GetLastWriteTimeUtc($input) -eq $stamp) 'Audio mutation did not preserve input length/mtime'
            }else{
                $input="$mod/Data/W3x.xml";$stamp=[IO.File]::GetLastWriteTimeUtc($input)
                SameLengthMutation $input 'FrameRate="30"' 'FrameRate="31"'
            }
            $changed=RunCli ($family+'-content-invalidated') $mod $cachedOut $true;Cache $changed 'invalidated'
            if($family -eq 'w3x'){Need ($changed.report.cache.hits -eq 1 -and $changed.report.cache.misses -eq 1) 'W3X animation edit must miss the changed animation and retain exactly one unchanged animation'}
            DifferentOutputs $hot $changed
            $changedPlain=RunCli ($family+'-changed-disabled') $mod $plainOut $false;Cache $changedPlain 'disabled';SameOutputs $changedPlain $changed
            $changedHot=RunCli ($family+'-changed-hot') $mod $cachedOut $true;Cache $changedHot 'hot';SameOutputs $changedPlain $changedHot
            if($family -eq 'audio'){
                $customKeys=@($hot.files.Keys|Where-Object{[IO.Path]::GetExtension($_) -ceq '.cdata'})
                $changedKeys=@($changed.files.Keys|Where-Object{[IO.Path]::GetExtension($_) -ceq '.cdata'})
                Need ($customKeys.Count -eq 1 -and $changedKeys.Count -eq 1 -and (Hash $hot.files[$customKeys[0]]) -cne (Hash $changed.files[$changedKeys[0]])) 'Audio sample mutation did not change the actual complete CDATA payload'
            }
            $modeState.fixtures+=,@{family=$family;input=$input;mutatedInputSha256=(Hash $input);sameLengthSameTimestamp=$true;hotAssetHits=$hot.report.cache.hits;changedHotAssetHits=$changedHot.report.cache.hits;completeCustomDataFiles=$script:expectedCustomData;allRawOutputsEqualToIndependentNoCache=$true}
        }
        $modeState.passed=$true
    }
    foreach($file in $state.sourceInputs){Need ((Hash (Join-Path $root $file.path)) -ceq $file.sha256) 'Original payload fixture source changed'}
    $state.succeeded=$true;$global:LASTEXITCODE=0
}catch{$state.error=$_.Exception.ToString();throw}
finally{
    $state|ConvertTo-Json -Depth 18|Set-Content "$out/validation.json"
    try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "Payload cache work cleanup failed: $_"}
}
