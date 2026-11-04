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
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite input-root evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$short=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('i'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
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
function RunCli([string]$Name,[string]$Mod,[string]$Destination,[bool]$Enabled,[int]$Expected=0,[bool]$Cancel=$false,[string]$HoldReadFile=$null,[string[]]$Extra=@()){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Force -Path $Destination|Out-Null
    $intermediate=Join-Path (Get-CompilerTestCacheRoot $script:sdk $Mod) ('intermediate/'+$Name)
    $arguments=@('build','--sc=false','--iod',$intermediate,'--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-format','loose','--package-content','base',('--bc='+$Enabled.ToString().ToLowerInvariant()))+$Extra
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    $request=@{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=$events;cancel=$Cancel;timeoutSeconds=$TimeoutSeconds}
    if($HoldReadFile){$request.holdReadFile=$HoldReadFile}
    $request|ConvertTo-Json -Depth 6|Set-Content "$caseOut/request.json"
    & $script:control "$caseOut/request.json" "$caseOut/control.json" *> "$caseOut/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$caseOut/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:modeState.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "Input-root CLI failed: $Name"
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
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq 4) 'Cache fixture did not publish exactly one four-file stream'
            $published=Join-Path $Destination 'published'
            foreach($artifact in $body.artifacts){
                Need ((Within $published $artifact.Path) -and (Hash $artifact.Path) -ceq $artifact.TestSha256 -and (Get-Item -LiteralPath $artifact.Path).Length -eq $artifact.Size) 'Cached artifact identity/containment differs'
                $relative=[IO.Path]::GetRelativePath($published,$artifact.Path);$copy=Join-Path $archived $relative
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($copy))|Out-Null
                Copy-Item -LiteralPath $artifact.Path -Destination $copy;$files[$relative]=$copy
            }
        }else{Need (@($body.artifacts).Count -eq 0) 'Failed/cancelled cache build accepted artifacts'}
        Need (!(Test-Path -LiteralPath (Join-Path $Destination ('.build/'+$body.buildId)))) 'Cache build retained owned staging'
        Need (@(Get-ChildItem -LiteralPath $Destination -Directory|Where-Object{$_.Name -like '.cache-candidate-*'}).Count -eq 0) 'Asset-cache transaction retained its candidate directory'
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
function EqualPaths($Actual,[string[]]$Expected,[string]$Label){
    $actualPaths=@($Actual);Need ($actualPaths.Count -eq $Expected.Count) "$Label effective root count differs"
    for($i=0;$i -lt $Expected.Count;$i++){Need ([IO.Path]::GetFullPath([string]$actualPaths[$i]).Equals([IO.Path]::GetFullPath($Expected[$i]),[StringComparison]::OrdinalIgnoreCase)) "$Label effective root order differs at $i"}
}
function OriginalOracle($Case,[string]$Name){
    $outputs=@{};foreach($key in $Case.files.Keys){$outputs[[IO.Path]::GetExtension($key).Substring(1)]=$Case.files[$key]}
    Need ($outputs.Count -eq 4) 'Input-root original oracle requires exactly four linked files'
    $fixture=Join-Path $script:modeOut "$Name.json"
    $case=@{name='mod-a';modRoot=$Case.mod;inputPath=$Case.report.plan.Tasks[0].InputPath;sdkRoot=$script:sdk;originalToolsRoot=(Join-Path $root 'SDK/tools');stagingDirectory=([IO.Path]::GetDirectoryName($outputs.manifest));outputs=$outputs;expectedTextures=@('Texture:InputRootsTexture');effectiveInputRoots=$Case.report.inputRoots;scope='Explicit effective input roots and unchanged source paths; original BAB raw four-output oracle'}
    @{succeeded=$true;sourceCommit=$script:state.sourceCommit;runId=$env:GITHUB_RUN_ID;baselineCases=@($case);scope='One Armor/Texture/PathMusicEvent stream with explicit roots; not full SDK'}|ConvertTo-Json -Depth 10|Set-Content $fixture
    & "$PSScriptRoot/TestCompilerBackendBaseline.ps1" -FixtureResult $fixture -OutputDirectory (Join-Path $script:modeOut $Name) -TimeoutSeconds $TimeoutSeconds -ExpectedCaseCount 1
    Need ($LASTEXITCODE -eq 0) 'Input-root independent original BAB oracle failed'
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Current production explicit /dr /data /art /audio order, CLI-over-SDK and MOD-prepend defaults, root-external legal inputs with cache disabled, failure/retry and cache-boundary refusal; strict current original BAB four-file oracle, not full SDK'}
try{
    Need ($state.sourceCommit -cmatch '^[0-9a-f]{40}$') 'Cannot identify source commit'
    Need (!(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'reuse.json'))) 'Input-root acceptance requires current production EXE bytes'
    $script:control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    $state.scriptSha256=Hash $PSCommandPath;$state.baselineScriptSha256=Hash "$PSScriptRoot/TestCompilerBackendBaseline.ps1";$state.controllerSha256=Hash $control
    foreach($mode in @('Debug','Release')){
        $script:modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $modeWork=(New-Item -ItemType Directory -Path (Join-Path $short $mode.Substring(0,1))).FullName
        $script:runtime=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'r')).FullName
        foreach($file in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){Copy-Item -LiteralPath (Join-Path $CompilerDirectory "$mode/bin/$file") -Destination $runtime}
        $script:exe=Join-Path $runtime 'NewModCompiler.exe'
        $build=Get-Content -LiteralPath (Join-Path $CompilerDirectory "$mode/build.json") -Raw|ConvertFrom-Json
        Need ($build.exeSha256 -ceq (Hash $exe) -and $build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) 'Cache/input-root acceptance requires the current source texture build'
        Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 3) 'Unexpected source runtime deployment files'
        $script:modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 's')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $mod=Join-Path $modeWork 'm';$external=Join-Path $modeWork 'external';$destination=Join-Path $modeWork 'o'
        $probe=[IO.File]::ReadAllText("$root/SDK/Mods/mymod/Data/RecoveryArmorProbe.xml")
        Put "$mod/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><Includes><Include type="all" source="DATA:Armor.xml"/></Includes><Texture id="InputRootsTexture" File="ART:cache.tga" Type="StandardTexture" GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/><PathMusicEvent id="RootsEvent" PathfinderEventHeader="AUDIO:events.h" IsXferable="true"/></AssetDeclaration>'
        Put "$mod/Data/Armor.xml" $probe
        Put "$mod/Audio/events.h" "#define PATH_EVENT_RootsEvent 0x11111111`r`n"
        New-Item -ItemType Directory -Path "$mod/Art/ca","$external/ArtA/ca","$external/ArtB/ca" -Force|Out-Null
        & python "$root/recovery/probes/TextureRedirect/MakeFixture.py" "$mod/Art/ca/cache.tga";Need ($LASTEXITCODE -eq 0) 'Input-root TGA preparation failed'
        foreach($letter in @('A','B')){
            Put "$external/Data$letter/Armor.xml" ($probe.Replace('72.5%',$(if($letter -eq 'A'){'12.5%'}else{'22.5%'})))
            Put "$external/Audio$letter/events.h" $(if($letter -eq 'A'){"#define PATH_EVENT_RootsEvent 0x22222222`r`n"}else{"#define PATH_EVENT_RootsEvent 0x33333333`r`n"})
            Copy-Item -LiteralPath "$mod/Art/ca/cache.tga" -Destination "$external/Art$letter/ca/cache.tga"
        }
        $texture=[IO.File]::ReadAllBytes("$external/ArtB/ca/cache.tga");$texture[18]=77;[IO.File]::WriteAllBytes("$external/ArtB/ca/cache.tga",$texture)
        [xml]$configuration=[IO.File]::ReadAllText("$sdk/tools/BinaryAssetBuilder.exe.config")
        $section=$configuration.SelectSingleNode('/configuration/assetbuilder')
        $section.SetAttribute('defaultDataPaths',"$external/DataB;$mod/Data")
        $section.SetAttribute('defaultArtPaths',"$external/ArtB")
        $section.SetAttribute('defaultAudioPaths',"$external/AudioB")
        $configuration.Save("$sdk/tools/BinaryAssetBuilder.exe.config")
        $defaults=RunCli 'mod-prepend-config-fallback' $mod $destination $false
        $roots=$defaults.report.inputRoots;Need ($null -ne $roots -and $roots.sources.dataPaths -ceq 'mod-prepend+sdk-config') 'Default MOD/SDK root provenance missing'
        EqualPaths $roots.dataPaths @("$mod/Data","$external/DataB","$mod/Data") 'default data'
        EqualPaths $roots.artPaths @("$mod/Art","$external/ArtB") 'default art'
        EqualPaths $roots.audioPaths @("$mod/Audio","$external/AudioB") 'default audio'
        OriginalOracle $defaults 'original-default-roots'
        $first=@('--dr',$external,'--data',"DataA;DataB;$mod/Data",'--art','ArtA;ArtB','--audio','AudioA;AudioB')
        $a=RunCli 'explicit-first-roots' $mod $destination $false -Extra $first
        $roots=$a.report.inputRoots
        Need ($roots.dataRoot -ceq $external -and $roots.sources.dataRoot -ceq 'commandLine' -and $roots.sources.dataPaths -ceq 'commandLine' -and $roots.sources.artPaths -ceq 'commandLine' -and $roots.sources.audioPaths -ceq 'commandLine') 'Explicit effective root values/provenance missing'
        EqualPaths $roots.dataPaths @("$external/DataA","$external/DataB","$mod/Data") 'explicit data'
        EqualPaths $roots.artPaths @("$external/ArtA","$external/ArtB") 'explicit art'
        EqualPaths $roots.audioPaths @("$external/AudioA","$external/AudioB") 'explicit audio'
        DifferentOutputs $defaults $a;OriginalOracle $a 'original-first-roots'
        $second=@(('/dr:'+$external),('/data:DataB;DataA;'+"$mod/Data"),'/art:ArtB;ArtA','/audio:AudioB;AudioA')
        $b=RunCli 'legacy-reversed-roots' $mod $destination $false -Extra $second
        EqualPaths $b.report.inputRoots.dataPaths @("$external/DataB","$external/DataA","$mod/Data") 'reversed data'
        DifferentOutputs $a $b;OriginalOracle $b 'original-reversed-roots'
        $missing=@('--dr',$external,'--data',"DataA;$mod/Data",'--art','ArtA','--audio','AudioA')
        # The original PathMusic handler treats a missing header as a warning.
        # A missing Texture file is an established real compilation failure.
        Move-Item -LiteralPath "$external/ArtA/ca/cache.tga" -Destination "$external/ArtA/ca/cache.saved"
        $failed=RunCli 'missing-external-input' $mod $destination $false 4 -Extra $missing
        Move-Item -LiteralPath "$external/ArtA/ca/cache.saved" -Destination "$external/ArtA/ca/cache.tga"
        $retry=RunCli 'retry-external-input' $mod $destination $false -Extra $missing
        SameOutputs $a $retry
        $cached=RunCli 'cache-external-roots' $mod $destination $true -Extra $first;SameOutputs $a $cached
        $cachedHot=RunCli 'cache-external-roots-hot' $mod $destination $true -Extra $first;SameOutputs $a $cachedHot
        Need ($cachedHot.report.cache.hits -gt 0 -and $cachedHot.report.cache.misses -eq 0) 'Original cache did not reuse root-external Texture and Audio dependencies'
        $modeState.passed=$true
    }
    $state.succeeded=$true;$global:LASTEXITCODE=0
}catch{$state.error=$_.Exception.ToString();throw}
finally{
    $state|ConvertTo-Json -Depth 18|Set-Content "$out/validation.json"
    try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "Input-root work cleanup failed: $_"}
}
