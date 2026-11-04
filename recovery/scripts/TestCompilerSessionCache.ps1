#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180,
    [switch]$Preflight
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
. "$PSScriptRoot/CompilerCacheLayout.ps1"
if(!$Preflight){$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path}
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite session-cache evidence: $OutputDirectory"}
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
    if([Linq.Enumerable]::SequenceEqual[byte]($leftBytes,$rightBytes)){return}
    for($i=0;$i -lt $leftBytes.Length;$i++){Need ($leftBytes[$i] -eq $rightBytes[$i]) "No-cache/cache raw artifact byte differs at $i"}
}
function SameOutputs($Left,$Right){
    Need ($Left.files.Count -eq $Right.files.Count) 'No-cache/cache artifact counts differ'
    foreach($key in $Left.files.Keys){Need ($Right.files.ContainsKey($key)) "Cache output omitted $key";EqualBytes $Left.files[$key] $Right.files[$key]}
}
function DifferentOutputs($Left,$Right){
    Need ($Left.files.Count -eq $Right.files.Count) 'Changed fixture output layout differs'
    $changed=$false
    foreach($key in $Left.files.Keys){if(!$Right.files.ContainsKey($key)){$changed=$true}elseif((Hash $Left.files[$key]) -cne (Hash $Right.files[$key])){$changed=$true}}
    Need $changed 'Input mutation did not exercise any observable compiled output change'
}
# The matrix deliberately keeps independent cache experiments separate. They
# are explicit subdirectories of the one physical SDK/MOD partition, never out.
$script:cacheDestinations=@{}
function CacheRoots([string]$Mod,[string]$Destination){
    $partition=Get-CompilerTestCacheRoot $script:sdk $Mod
    $sha=[Security.Cryptography.SHA256]::Create()
    try {$group=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($Destination).ToUpperInvariant())),0,6).Replace('-','').ToLowerInvariant()}
    finally {$sha.Dispose()}
    $namespace=Join-Path (Join-Path $partition 'tests') $group
    return @{partition=$partition;session=(Join-Path $namespace 's');build=(Join-Path $namespace 'b');intermediate=(Join-Path $namespace 'i');streams=(Join-Path $partition 'streams')}
}
function CacheOverride([string[]]$Extra,[string[]]$Names,[string]$Fallback){
    for($i=0;$i -lt $Extra.Count;$i++){
        $value=$Extra[$i];$key=$value.TrimStart([char[]]'-/');$split=$key.IndexOfAny([char[]]'=:')
        if($split -ge 0){$name=$key.Substring(0,$split);if($Names -icontains $name){return [IO.Path]::GetFullPath($key.Substring($split+1))}}
        elseif($Names -icontains $key){Need ($i+1 -lt $Extra.Count) 'Cache override has no value';return [IO.Path]::GetFullPath($Extra[$i+1])}
    }
    return $Fallback
}
function CacheAt([string]$Destination){
    Need ($script:cacheDestinations.ContainsKey($Destination)) "No cache layout recorded for $Destination"
    return $script:cacheDestinations[$Destination]
}
function CacheFiles([string]$Destination){
    $files=@{}
    $session=(CacheAt $Destination).session
    foreach($file in @(Get-ChildItem -LiteralPath $session -File -Recurse -ErrorAction SilentlyContinue)){
        $relative=$file.FullName.Substring($session.Length).TrimStart([char[]]'\/')
        if($relative -like 'BinaryAssetBuilder.SessionCache.xml*' -or $relative.StartsWith('.NewModCompiler'+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){$files[$relative]=Hash $file.FullName}
    }
    return $files
}
function CacheUnchanged([string]$Destination,$Before){
    $after=CacheFiles $Destination;Need ($after.Count -eq $Before.Count) 'Failed/cancelled build persisted additional session-cache files'
    foreach($key in $Before.Keys){Need ($after.ContainsKey($key) -and $after[$key] -ceq $Before[$key]) 'Failed/cancelled build changed previously confirmed session-cache bytes'}
}
function Cache($Case,[string]$Expected){
    $statistics=$Case.report.cache
    Need ($null -ne $statistics) 'Durable report omitted cache statistics'
    foreach($name in @('documentReuse','xmlParse','instanceCompile','sessionPackageHits','sessionPackageMisses')){
        Need ($null -ne $statistics.$name -and $statistics.$name -ge 0) "Actual session counter missing: $name"
    }
    Need ($statistics.hits -eq 0 -and $statistics.misses -eq 0) '/sc-only build used /bc statistics'
    if($Expected -eq 'disabled'){
        Need (!$statistics.sessionCache -and $statistics.sessionPackageHits -eq 0 -and $statistics.sessionPackageMisses -eq 0 -and $statistics.documentReuse -eq 0) 'Disabled session cache recorded reuse';return
    }
    Need ($statistics.sessionCache -eq $true -and $statistics.status -ceq 'SessionCache') 'Session cache was accepted without production enablement'
    if($Expected -eq 'cold'){
        Need ($statistics.sessionPackageHits -eq 0 -and $statistics.sessionPackageMisses -gt 0 -and $statistics.documentReuse -eq 0 -and $statistics.xmlParse -gt 0 -and $statistics.instanceCompile -gt 0) 'Cold session cache did not parse and compile actual assets'
    }elseif($Expected -eq 'hot'){
        Need ($statistics.sessionPackageHits -gt 0 -and $statistics.sessionPackageMisses -eq 0 -and $statistics.documentReuse -gt 0 -and $statistics.xmlParse -lt $script:hotParseCeiling -and $statistics.instanceCompile -eq 0) 'Hot session package did not reduce XML parsing and reuse compiled instances'
    }elseif($Expected -eq 'incremental'){
        Need ($statistics.sessionPackageHits -gt 0 -and $statistics.sessionPackageMisses -eq 0 -and $statistics.xmlParse -gt 0 -and $statistics.instanceCompile -gt 0) 'Changed MOD did not restore its session and rebuild the affected assets'
    }elseif($Expected -eq 'revalidated'){
        Need ($statistics.sessionPackageMisses -gt 0 -and $statistics.documentReuse -eq 0 -and $statistics.xmlParse -gt 0) 'Changed session contract was not revalidated from source XML'
    }elseif($Expected -eq 'restored'){
        Need ($statistics.sessionPackageHits -gt 0 -and $statistics.sessionPackageMisses -eq 0) 'Original session did not retain the previous MOD document records'
    }elseif($Expected -eq 'repaired'){
        Need ($statistics.sessionPackageHits -gt 0 -and $statistics.xmlParse -gt 0 -and $statistics.instanceCompile -gt 0) 'Missing original intermediate or CDATA was not repaired by actual recompilation'
    }else{throw "Unknown session expectation: $Expected"}
}
function StreamTimes([string]$Destination){
    $times=@{}
    # Published loose files are formal exports. Link reuse is measured at the
    # reusable stream root, independently of their export timestamps.
    foreach($file in Get-ChildItem -LiteralPath (CacheAt $Destination).streams -File -Recurse){$times[$file.FullName]=$file.LastWriteTimeUtc.Ticks}
    return $times
}
function LinkedHot($Case,$Before){
    Need ($Case.report.cache.linkedStreamsRestored -gt 0) 'Hot build did not restore linked output'

    if(@($Case.report.artifacts|Where-Object{$_.Kind -ceq 'custom-data'}).Count -eq 0){
        if($Case.report.cache.sessionCache){
            Need ($Case.report.cache.fileHashReads -eq 0) 'Unchanged BAB session reread file contents'
        } else {
            # Without SessionCache every FileHashItem is new. The original game
            # FastHash is required; the old receipt counter hid these real reads.
            Need ($Case.report.cache.fileHashReads -gt 0) 'Disabled session did not compute required input FastHashes'
        }
    }
    $after=StreamTimes $Case.destination
    Need ($after.Count -eq $Before.Count) 'Unchanged reusable stream file set differs'
    foreach($path in $Before.Keys){Need ($after[$path] -eq $Before[$path]) 'Unchanged reusable stream rewrote a file'}
    $log=[IO.File]::ReadAllText($Case.report.logPaths.text)
    foreach($message in @('Old manifest is up to date.','Linked binary data up to date','Linked import data up to date','Linked relocation data up to date')){
        Need ($log.Contains($message)) "Original linker did not skip unchanged output: $message"
    }
}
function DamagePackage([string]$Destination,[string]$Kind){
    $layout=CacheAt $Destination
    if($Kind -eq 'xml'){$paths=@(Join-Path $layout.session 'BinaryAssetBuilder.SessionCache.xml')}
    elseif($Kind -eq 'intermediate'){$paths=@(Get-ChildItem -LiteralPath $layout.intermediate -Filter '*.asset' -File -Recurse|ForEach-Object FullName)}
    elseif($Kind -eq 'cdata'){$paths=@(@($layout.session,$layout.streams)|ForEach-Object{Get-ChildItem -LiteralPath $_ -Filter '*.cdata' -File -Recurse -ErrorAction SilentlyContinue|ForEach-Object FullName})}
    else{throw 'Unknown original cache corruption kind'}
    Need ($paths.Count -gt 0) "Original session omitted required $Kind data"
    $selected=@()
    foreach($path in $paths){
        Need (Test-Path -LiteralPath $path) 'Original cache corruption target is absent'
        $selected+=,@{path=$path;sha256=(Hash $path);length=(Get-Item -LiteralPath $path).Length;kind=$Kind}
        if($Kind -eq 'xml'){[IO.File]::WriteAllText($path,'<SessionCache>broken')}else{Remove-Item -LiteralPath $path}
    }
    return $selected
}
function MakeMod([string]$Path,[string]$Probe){
    Put "$Path/unused.txt" 'unused A'
    Put "$Path/Data/Armor.xml" $Probe
    Put "$Path/Data/Defs.xml" '<Defines xmlns="uri:ea.com:eala:asset"><Define name="SESSION_SCALE" value="83.25"/></Defines>'
    Put "$Path/Data/XArmor.xml" '<ArmorTemplate xmlns="uri:ea.com:eala:asset" id="SessionXIncludeArmor" Default="22.5%"/>'
    Put "$Path/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude"><Includes><Include type="all" source="Armor.xml"/></Includes><xi:include href="Defs.xml"/><xi:include href="XArmor.xml"/><ArmorTemplate id="SessionExpressionArmor" DamageScalar="=$SESSION_SCALE"/><PathMusicEvent id="CacheEvent" PathfinderEventHeader="events.h" IsXferable="true"/></AssetDeclaration>'
    Put "$Path/Data/events.h" "#define PATH_EVENT_CacheEvent 0x12345678`r`n"
    Put "$Path/Data/AptUI/Payload.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><AptAptData id="SessionPayload" File="payload.bin"/></AssetDeclaration>'
    [IO.File]::WriteAllBytes("$Path/Data/AptUI/payload.bin",[byte[]](1,2,3,4,5,6,7))
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
function RunCli([string]$Name,[string]$Mod,[string]$Destination,[bool]$Enabled,[int]$Expected=0,[bool]$Cancel=$false,[string]$HoldReadFile=$null,[string[]]$Extra=@(),[bool]$BuildEnabled=$false,[switch]$OriginalDefaults){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Force -Path $Destination|Out-Null
    $arguments=@('build','--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-format','loose','--package-content',$script:contents)
    if(!$OriginalDefaults){
        $arguments+=@('--csc=false','--psc=false',('--sc='+$Enabled.ToString().ToLowerInvariant()),('--bc='+$BuildEnabled.ToString().ToLowerInvariant()))
    }
    $layout=CacheRoots $Mod $Destination
    if(!$Enabled -and !$OriginalDefaults){
        # Force the independently compiled reference to have fresh intermediates.
        $layout.intermediate=Join-Path $layout.intermediate $Name
    }
    $layout.session=CacheOverride $Extra @('scd','scp','session-cache-directory') $layout.session
    $layout.build=CacheOverride $Extra @('bcd','bcp','build-cache-directory') $layout.build
    $layout.intermediate=CacheOverride $Extra @('iod') $layout.intermediate
    foreach($cachePath in @($layout.session,$layout.build,$layout.intermediate)){Need (Within $layout.partition $cachePath) 'Fixture cache override escaped its MOD partition'}
    $script:cacheDestinations[$Destination]=$layout
    if((CacheOverride $Extra @('scd','scp','session-cache-directory') '') -eq ''){$arguments+=('/scd:'+$layout.session)}
    if((CacheOverride $Extra @('bcd','bcp','build-cache-directory') '') -eq ''){$arguments+=('/bcd:'+$layout.build)}
    if((CacheOverride $Extra @('iod') '') -eq ''){$arguments+=('/iod:'+$layout.intermediate)}
    $arguments+=@($Extra)
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    $request=@{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=$events;cancel=$Cancel;timeoutSeconds=$TimeoutSeconds}
    if($HoldReadFile){$request.holdReadFile=$HoldReadFile}
    $request|ConvertTo-Json -Depth 6|Set-Content "$caseOut/request.json"
    & $script:control "$caseOut/request.json" "$caseOut/control.json" *> "$caseOut/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$caseOut/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit;cacheLayout=$layout};$script:modeState.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "Session-cache CLI failed: $Name"
        Need ($audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0) 'Asset cache introduced a product child process'
        if($Cancel){Need ($audit.signalSent -and $audit.freshEventsPathObserved -and $audit.streamStartedObserved -and $Expected -eq 6) 'Cache cancellation did not receive actual CtrlBreak'}
        $body=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        foreach($artifact in $body.artifacts){$artifact|Add-Member -NotePropertyName TestSha256 -NotePropertyValue (Hash $artifact.Path)}
        Need ($body.exitCode -eq $Expected) 'Cache process/report exit differs'
        if($body.cache.sessionCacheDirectory){Need ($body.cache.sessionCacheDirectory -ieq $layout.session) 'Actual session cache escaped its requested partition namespace'}
        Need (!(Test-Path (Join-Path $Destination 'BinaryAssetBuilder.SessionCache.xml')) -and !(Test-Path (Join-Path $Destination 'BinaryAssetBuilder.SessionCache.xml.deflate')) -and !(Test-Path (Join-Path $Destination '.NewModCompiler'))) 'Compiler cache leaked into final output'
        $rows=@(Get-Content -LiteralPath $events|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
        Need ($rows[-1].eventType -ceq 'BuildFinished' -and @($rows|Where-Object{$_.eventType -ceq 'BuildStarted'}).Count -eq 1 -and @($rows|Where-Object{$_.eventType -ceq 'BuildFinished'}).Count -eq 1) 'Cache CLI terminal protocol differs'
        $sequence=0;foreach($row in $rows){$sequence++;Need ($row.buildId -ceq $body.buildId -and $row.sequence -eq $sequence) 'Cache CLI build identity/sequence differs'}
        Need ($rows[0].payload.processId -eq $audit.processId -and @($body.externalTools).Count -eq 0) 'Cache CLI production identity/external tools differs'
        if($Cancel){Need ($audit.observedBuildStarted.buildId -ceq $body.buildId -and $audit.observedBuildStarted.payload.processId -eq $audit.processId -and $audit.observedStreamStarted.buildId -ceq $body.buildId) 'CtrlBreak observed a stale cached build generation'}
        $files=@{};$archived=Join-Path $caseOut 'artifacts';New-Item -ItemType Directory -Path $archived|Out-Null
        if($Expected -eq 0){
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq $script:expectedArtifacts) 'Session fixture did not publish exact stream/CDATAs count'
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
function OriginalOracle($Case,[string]$Name,$OriginalTextureInformation=$null,[string]$CacheExportDirectory=$null){
    $baseline=@();$index=0
    foreach($task in $Case.report.plan.Tasks){
        $outputs=@{};$prefix=$task.Content+'/'+[IO.Path]::GetFileNameWithoutExtension($task.InputPath)
        foreach($key in $Case.files.Keys){if($key.Replace('\','/').StartsWith($prefix+'/',[StringComparison]::Ordinal) -and [IO.Path]::GetExtension($key) -in @('.manifest','.bin','.imp','.relo')){$outputs[[IO.Path]::GetExtension($key).Substring(1)]=$Case.files[$key]}}
        Need ($outputs.Count -eq 4) 'Cache original oracle requires exact four files per task'
        $baseline+=,@{name=$(if($index++ -eq 0){'mod-a'}else{'mod-b'});modRoot=$Case.mod;inputPath=$task.InputPath;sdkRoot=$script:sdk;originalToolsRoot=(Join-Path $root 'SDK/tools');stagingDirectory=([IO.Path]::GetDirectoryName($outputs.manifest));outputs=$outputs;expectedTextures=@();scope='Current session-cache fixture, strict original BAB raw four-output comparison; no normalization'}
    }
    if($OriginalTextureInformation){
        Need ($baseline.Count -eq 1) 'Texture oracle requires one task'
        $baseline[0].originalTextureInformation=$OriginalTextureInformation
        $baseline[0].expectedTextures=@()
        $baseline[0].customOutputs=@()
        foreach($file in @($Case.files.Values|Where-Object{[IO.Path]::GetExtension($_) -ceq '.cdata'})){
            $baseline[0].customOutputs+=,@{qualifiedName=($OriginalTextureInformation.TypeName+':SessionTexture');relativePath=([IO.Path]::GetFileNameWithoutExtension($baseline[0].outputs.manifest)+'/cdata/'+[IO.Path]::GetFileName($file));path=$file}
        }
    }
    $fixture=Join-Path $script:modeOut "$Name.json"
    @{succeeded=$true;sourceCommit=$script:state.sourceCommit;runId=$env:GITHUB_RUN_ID;baselineCases=$baseline;scope='Session-cache Armor/include/define/inheritance and file-backed APT stream; not full SDK'}|ConvertTo-Json -Depth 9|Set-Content $fixture
    & "$PSScriptRoot/TestCompilerBackendBaseline.ps1" -FixtureResult $fixture -OutputDirectory (Join-Path $script:modeOut $Name) -TimeoutSeconds $TimeoutSeconds -ExpectedCaseCount $baseline.Count -CacheExportDirectory $CacheExportDirectory
    Need ($LASTEXITCODE -eq 0) 'Build-cache independent original BAB oracle failed'
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
    # retaining the exact animation node and its one hierarchy dependency.
    [xml]$animations=[IO.File]::ReadAllText("$root/SDK/Mods/W3xRecovery/Data/Animations.xml")
    [xml]$dependencies=[IO.File]::ReadAllText("$root/SDK/Mods/W3xRecovery/Data/Dependencies.xml")
    $ns=[Xml.XmlNamespaceManager]::new($animations.NameTable);$ns.AddNamespace('ea','uri:ea.com:eala:asset')
    $animation=$animations.SelectSingleNode('/ea:AssetDeclaration/ea:W3DAnimation[1]',$ns)
    $dns=[Xml.XmlNamespaceManager]::new($dependencies.NameTable);$dns.AddNamespace('ea','uri:ea.com:eala:asset')
    $hierarchy=$dependencies.SelectSingleNode('/ea:AssetDeclaration/ea:W3DHierarchy[1]',$dns)
    Need ($null -ne $animation -and $null -ne $hierarchy -and $animation.GetAttribute('Hierarchy') -ceq $hierarchy.GetAttribute('id')) 'W3X source fixture hierarchy closure changed'
    Put "$Path/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><Includes><Include type="all" source="W3x.xml"/></Includes></AssetDeclaration>'
    Put "$Path/Data/W3x.xml" ('<AssetDeclaration xmlns="uri:ea.com:eala:asset">'+$hierarchy.OuterXml+$animation.OuterXml+'</AssetDeclaration>')
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Actual production /sc session-package disabled/cold/hot reuse, Include/XInclude defines/expressions/inheritance, complete intermediate and Audio CDATA/W3X, content invalidation, MOD isolation, corruption, cancellation/failure retries; exact raw streams, selected original BAB oracle; not complete P5/G3/SDK'}
try{
    Need ($state.sourceCommit -cmatch '^[0-9a-f]{40}$') 'Cannot identify source commit'
    if($Preflight){
        $probe=[IO.File]::ReadAllText("$root/SDK/Mods/mymod/Data/RecoveryArmorProbe.xml")
        $preflightMod=Join-Path $short 'm';MakeMod $preflightMod $probe
        AudioMod (Join-Path $short 'a');W3xMod (Join-Path $short 'w')
        $check=@'
import importlib.util, pathlib, sys
from lxml import etree as E
scripts=pathlib.Path(sys.argv[1]); work=pathlib.Path(sys.argv[2])
spec=importlib.util.spec_from_file_location('session_schema', scripts/'GenerateXmlSdkModA.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
schema=module.aggregate_schema(); ns={'ea':'uri:ea.com:eala:asset'}
root=E.parse(str(work/'m/Data/Mod.xml')); root.xinclude()
defines=root.xpath('/ea:AssetDeclaration/ea:Defines/ea:Define', namespaces=ns)
assert len(defines)==1 and defines[0].get('name')=='SESSION_SCALE' and defines[0].get('value')=='83.25'
expression=root.xpath('/ea:AssetDeclaration/ea:ArmorTemplate[@id="SessionExpressionArmor"]', namespaces=ns)[0]
assert expression.get('DamageScalar')=='=$SESSION_SCALE'
# Core evaluates expressions before XSD validation. Check that exact known
# expression result, then source-XSD validation including declaration order.
expression.set('DamageScalar',defines[0].get('value')); schema.assertValid(root)
for name in ('m/Data/Armor.xml','m/Data/AptUI/Payload.xml','a/Data/Mod.xml','w/Data/Mod.xml','w/Data/W3x.xml'):
    schema.assertValid(E.parse(str(work/name)))
armor=E.parse(str(work/'m/Data/Armor.xml'))
assert len(armor.xpath('/ea:AssetDeclaration/ea:ArmorTemplate[@inheritFrom="RecoveryArmorProbe_NonDefault"]',namespaces=ns))==1
assert b'72.5%' in (work/'m/Data/Armor.xml').read_bytes()
assert b'22.5%' in (work/'m/Data/XArmor.xml').read_bytes()
assert b'0x12345678' in (work/'m/Data/events.h').read_bytes()
assert b'FrameRate="30"' in (work/'w/Data/W3x.xml').read_bytes()
assert len((work/'a/Audio/probe.wav').read_bytes())==44+4096*2*2
config=(module.ROOT/'SDK/tools/BinaryAssetBuilder.exe.config').read_bytes()
assert b'monitorPaths="..\\"' in config
assert b'elementFormDefault="qualified"' in (module.S/'AssetTypeTexture.xsd').read_bytes()
print('PASS session fixtures: XInclude declaration order, exact defines/expression, inheritance, source XSD, payload closure and mutation tokens')
'@
        & python -c $check $PSScriptRoot $short *> "$out/fixture-preflight.log"
        Need ($LASTEXITCODE -eq 0) 'Session fixture preflight failed; inspect fixture-preflight.log'
        $state.preflight=$true;$state.scope='Fixture source-XSD/known expression/closure and mutation-token preflight only; no production cache execution';$state.succeeded=$true;$global:LASTEXITCODE=0
        return
    }
    Need (!(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'reuse.json')) -and !(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'candidate-replay.json'))) 'New session-cache acceptance requires current production EXE bytes'
    $state.binarySourceCommit=$state.sourceCommit;$state.testSourceCommit=$state.sourceCommit
    $replayFiles=@{};$replayPath=Join-Path $CompilerDirectory 'session-replay.json'
    if(Test-Path -LiteralPath $replayPath){
        $replay=Get-Content -LiteralPath $replayPath -Raw|ConvertFrom-Json
        $guard=@'
import json, re, sys
sys.path.insert(0,sys.argv[1])
import PrepareCompilerSessionReplay as p
assert p.SOURCE=='f2ba141284469411d604d756f7916b0cf6aecf0a'
assert str(p.RUN)=='40406049617' and str(p.ATTEMPT)=='1'
assert re.fullmatch('[0-9a-f]{64}',p.SHA256) and int(p.ARTIFACT)>0
p.closure(sys.argv[2],p.SOURCE,sys.argv[3])
print(json.dumps({'source':p.SOURCE,'runId':str(p.RUN),'runAttempt':str(p.ATTEMPT),'sha256':p.SHA256,'artifactId':str(p.ARTIFACT)}))
'@
        & python -c $guard $PSScriptRoot $root $state.sourceCommit > "$out/replay-source-closure.json"
        Need ($LASTEXITCODE -eq 0) 'Session replay actual production-source closure rejected'
        $identity=Get-Content "$out/replay-source-closure.json" -Raw|ConvertFrom-Json
        Need ($replay.prepared -eq $true -and $replay.candidateValidated -eq $false -and $replay.productionSourcesUnchanged -eq $true -and $replay.binarySourceCommit -ceq $identity.source -and $replay.testSourceCommit -ceq $state.sourceCommit -and [string]$replay.runId -ceq $identity.runId -and [string]$replay.runAttempt -ceq $identity.runAttempt -and $replay.archiveSha256 -ceq $identity.sha256 -and [string]$replay.artifactId -ceq $identity.artifactId) 'Session replay fixed identity or preparation contract differs'
        $expectedFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($configuration in @('Debug','Release')){foreach($fileName in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){$null=$expectedFiles.Add($configuration+'/bin/'+$fileName)}}
        Need (@($replay.files).Count -eq 6) 'Session replay requires exactly six runtime identities'
        foreach($file in $replay.files){
            Need ($expectedFiles.Remove([string]$file.path) -and $file.sha256 -cmatch '^[0-9a-f]{64}$') 'Unexpected or duplicate session replay runtime identity'
            Need ((Hash (Join-Path $CompilerDirectory $file.path)) -ceq $file.sha256) 'Session replay production bytes differ from fixed archive'
            $replayFiles[[string]$file.path]=[string]$file.sha256
        }
        Need ($expectedFiles.Count -eq 0) 'Session replay runtime set is incomplete'
        $state.binarySourceCommit=$identity.source;$state.sessionReplay=$replay
        $state.scope+='; fixed binarySourceCommit with separately recorded current testSourceCommit and checked actual source closure; not a current production rebuild or a change to the original run conclusion'
    }
    $script:control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    $state.scriptSha256=Hash $PSCommandPath;$state.controllerSha256=Hash $control
    foreach($mode in @('Debug','Release')){
        $script:modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $modeWork=(New-Item -ItemType Directory -Path (Join-Path $short $mode.Substring(0,1))).FullName
        $script:runtime=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'r')).FullName
        foreach($file in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){
            Copy-Item -LiteralPath (Join-Path $CompilerDirectory "$mode/bin/$file") -Destination $runtime
            if($replayFiles.Count -gt 0){Need ((Hash (Join-Path $runtime $file)) -ceq $replayFiles[$mode+'/bin/'+$file]) 'Copied session replay runtime identity differs'}
        }
        $script:exe=Join-Path $runtime 'NewModCompiler.exe'
        if($replayFiles.Count -gt 0){
            & "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe $exe *> "$modeOut/prepare-texture.log";Need ($LASTEXITCODE -eq 0) 'Session runtime Texture deployment failed'
        }else{
            $build=Get-Content -LiteralPath (Join-Path $CompilerDirectory "$mode/build.json") -Raw|ConvertFrom-Json
            Need ($build.exeSha256 -ceq (Hash $exe) -and $build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) 'Cache/input-root acceptance requires the current source texture build'
            Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 3) 'Unexpected source runtime deployment files'
        }
        $script:modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@();corruptions=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 's')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $probe=[IO.File]::ReadAllText("$root/SDK/Mods/mymod/Data/RecoveryArmorProbe.xml")
        $mod=Join-Path $modeWork 'm';MakeMod $mod $probe
        $cachedOut=Join-Path $modeWork 'cache';$plainOut=Join-Path $modeWork 'plain'
        $script:contents='base,apt';$script:expectedArtifacts=8
        $plain=RunCli 'disabled' $mod $plainOut $false;Cache $plain 'disabled'
        $cold=RunCli 'cold' $mod $cachedOut $true -Extra @('--performance','summary');Cache $cold 'cold';SameOutputs $plain $cold;$script:hotParseCeiling=$cold.report.cache.xmlParse
        Need ((@($cold.report.performance.counters|Where-Object name -eq 'cache.session.memoryReuse'|Measure-Object value -Sum).Sum) -eq 1) 'Second cold stream did not reuse the saved in-memory session'
        $publishedBefore=StreamTimes $cachedOut
        $cacheTimes=@{}
        $cachedSession=(CacheAt $cachedOut).session
        foreach($file in Get-ChildItem -LiteralPath $cachedSession -File -Recurse){
            if($file.Name -like 'BinaryAssetBuilder.SessionCache.xml*' -or (Within (Join-Path $cachedSession '.NewModCompiler') $file.FullName)){$cacheTimes[$file.FullName]=$file.LastWriteTimeUtc.Ticks}
        }
        $hot=RunCli 'hot' $mod $cachedOut $true -Extra @('--performance','summary');Cache $hot 'hot';SameOutputs $plain $hot
        Need ((@($hot.report.performance.counters|Where-Object name -eq 'cache.session.memoryReuse'|Measure-Object value -Sum).Sum) -eq 1) 'Second hot stream reparsed the shared saved session'
        LinkedHot $hot $publishedBefore
        foreach($path in $cacheTimes.Keys){Need ([IO.File]::GetLastWriteTimeUtc($path).Ticks -eq $cacheTimes[$path]) 'Hot build rewrote unchanged SessionCache or CDATA'}
        $plainBefore=StreamTimes $plainOut
        $plainHot=RunCli 'disabled-hot-linked' $mod $plainOut $false;Cache $plainHot 'disabled';SameOutputs $plain $plainHot;LinkedHot $plainHot $plainBefore
        $damagedLinked=Get-ChildItem -LiteralPath (CacheAt $cachedOut).streams -Filter '*.imp' -Recurse -File|Select-Object -First 1
        Remove-Item -LiteralPath $damagedLinked.FullName
        $repairedLinked=RunCli 'missing-linked-group' $mod $cachedOut $true;Cache $repairedLinked 'hot';SameOutputs $plain $repairedLinked

        OriginalOracle $hot 'original-initial'
        $combinedOut=Join-Path $modeWork 'both'
        $combinedCold=RunCli 'combined-sc-bc-cold' $mod $combinedOut $true -BuildEnabled $true;SameOutputs $plain $combinedCold
        Need ($combinedCold.report.cache.sessionCache -and $combinedCold.report.cache.sessionPackageMisses -gt 0 -and $combinedCold.report.cache.documentReuse -eq 0 -and $combinedCold.report.cache.misses -gt 0 -and $combinedCold.report.cache.hits -eq 0) 'Combined cold session/build cache did not record independent actual misses'
        $combinedHot=RunCli 'combined-sc-bc-hot' $mod $combinedOut $true -BuildEnabled $true;SameOutputs $plain $combinedHot
        Need ($combinedHot.report.cache.sessionPackageHits -gt 0 -and $combinedHot.report.cache.sessionPackageMisses -eq 0 -and $combinedHot.report.cache.documentReuse -gt 0 -and $combinedHot.report.cache.instanceCompile -eq 0 -and $combinedHot.report.cache.hits -eq 0 -and $combinedHot.report.cache.misses -eq 0) 'Combined hot cache conflated session reuse with asset-cache hits'
        SameLengthMutation "$mod/unused.txt" 'unused A' 'unused B'
        $unrelated=RunCli 'unreferenced-content-edit' $mod $cachedOut $true;Cache $unrelated 'hot';SameOutputs $plain $unrelated
        foreach($change in @(
            @{name='include-inheritance';path="$mod/Data/Armor.xml";from='72.5%';to='12.5%'},
            @{name='xinclude';path="$mod/Data/XArmor.xml";from='22.5%';to='32.5%'},
            @{name='define-expression';path="$mod/Data/Defs.xml";from='83.25';to='93.25'},
            @{name='file-reference';path="$mod/Data/events.h";from='0x12345678';to='0x23456789'})){
            SameLengthMutation $change.path $change.from $change.to
            $changed=RunCli ($change.name+'-invalidated') $mod $cachedOut $true;Cache $changed 'incremental';DifferentOutputs $hot $changed
            Need ($changed.report.cache.documentReuse -gt 0 -and $changed.report.cache.instanceCompile -lt $cold.report.cache.instanceCompile) 'MOD edit recompiled the unaffected stream instead of retaining partial reuse'
            $plain=RunCli ($change.name+'-disabled') $mod $plainOut $false;SameOutputs $plain $changed
            $hot=RunCli ($change.name+'-hot') $mod $cachedOut $true;Cache $hot 'hot';SameOutputs $plain $hot
        }
        $combinedChanged=RunCli 'combined-sc-bc-incremental' $mod $combinedOut $true -BuildEnabled $true;SameOutputs $plain $combinedChanged
        Need ($combinedChanged.report.cache.sessionPackageHits -gt 0 -and $combinedChanged.report.cache.sessionPackageMisses -eq 0 -and $combinedChanged.report.cache.documentReuse -gt 0 -and $combinedChanged.report.cache.instanceCompile -gt 0 -and $combinedChanged.report.cache.misses -gt 0) 'Combined incremental caches did not retain unaffected documents and invalidate affected asset entries independently'
        $combinedChangedHot=RunCli 'combined-sc-bc-incremental-hot' $mod $combinedOut $true -BuildEnabled $true;SameOutputs $plain $combinedChangedHot
        Need ($combinedChangedHot.report.cache.sessionPackageHits -gt 0 -and $combinedChangedHot.report.cache.sessionPackageMisses -eq 0 -and $combinedChangedHot.report.cache.instanceCompile -eq 0 -and $combinedChangedHot.report.cache.hits -eq 0 -and $combinedChangedHot.report.cache.misses -eq 0) 'Combined updated session failed to advance to its latest hot generation'
        OriginalOracle $hot 'original-mutated'
        # Schema content changes revalidate cached defaulted XML. Unrelated SDK
        # and runtime configuration quoting edits leave the session hot.
        foreach($change in @(
            @{name='schema';path="$sdk/Schemas/xsd/AssetTypeTexture.xsd";from='elementFormDefault="qualified"';to="elementFormDefault='qualified'"},
            @{name='sdk-config';path="$sdk/tools/BinaryAssetBuilder.exe.config";from='monitorPaths="..\"';to="monitorPaths='..\'"},
            @{name='runtime-config';path="$runtime/NewModCompiler.exe.config";from='version="v4.0"';to="version='v4.0'"})){
            SameLengthMutation $change.path $change.from $change.to
            $changed=RunCli ($change.name+'-invalidated') $mod $cachedOut $true;Cache $changed $(if($change.name -eq 'schema'){'revalidated'}else{'hot'});SameOutputs $plain $changed;if($change.name -eq 'schema'){$script:hotParseCeiling=$changed.report.cache.xmlParse}
            $hot=RunCli ($change.name+'-hot') $mod $cachedOut $true;Cache $hot 'hot';SameOutputs $plain $hot
        }
        Need (!(Test-Path (Join-Path (CacheAt $cachedOut).partition '.file-identities.json')) -and !(Test-Path (Join-Path (CacheAt $cachedOut).session '.NewModCompiler/session.identity'))) 'Removed hash/session sidecar was recreated'
        Put "$runtime/unrelated.dll" 'Unloaded runtime neighbor A'
        Put "$runtime/unrelated.config" 'Unloaded runtime configuration'
        $neighbor=RunCli 'unrelated-runtime-files' $mod $cachedOut $true;Cache $neighbor 'hot';SameOutputs $plain $neighbor
        SameLengthMutation "$runtime/unrelated.dll" 'neighbor A' 'neighbor B'
        $neighborEdited=RunCli 'unrelated-runtime-edit' $mod $cachedOut $true;Cache $neighborEdited 'hot';SameOutputs $plain $neighborEdited
        $armorSchema=Join-Path $sdk 'Schemas/xsd/AssetTypeArmorTemplate.xsd'
        $schemaOptional='name="DamageScalar" type="Percentage" default="100" balance:snapshot="true"'
        $schemaRequired=('name="DamageScalar" type="Percentage" use="required"').PadRight($schemaOptional.Length)
        SameLengthMutation $armorSchema $schemaOptional $schemaRequired
        $schemaPlain=RunCli 'schema-required-disabled' $mod $plainOut $false 4
        $schemaChanged=RunCli 'schema-required-invalidated' $mod $cachedOut $true 4
        Need ($schemaChanged.report.cache.xmlParse -gt 0 -and $schemaChanged.report.cache.sessionPackageMisses -gt 0) 'Changed XSD constraint reused a completed document without validation'
        foreach($case in @($schemaPlain,$schemaChanged)){
            Need (($case.report.diagnostics|ConvertTo-Json -Depth 12).Contains('DamageScalar')) 'Schema failure did not identify the newly required attribute'
        }
        SameLengthMutation $armorSchema $schemaRequired $schemaOptional
        $hot=RunCli 'schema-required-restored' $mod $cachedOut $true;Cache $hot 'hot';SameOutputs $plain $hot
        $other=Join-Path $modeWork 'b';MakeMod $other ($probe.Replace('72.5%','32.5%'))
        $otherCold=RunCli 'second-mod' $other $cachedOut $true;Cache $otherCold 'cold'
        $otherPlain=RunCli 'second-mod-disabled' $other $plainOut $false;SameOutputs $otherPlain $otherCold
        $returnA=RunCli 'first-mod-return' $mod $cachedOut $true;Cache $returnA 'restored';SameOutputs $plain $returnA
        $returnAHot=RunCli 'first-mod-return-hot' $mod $cachedOut $true;Cache $returnAHot 'hot';SameOutputs $plain $returnAHot
        # Damage actual original XML/intermediate/CDATA; preserve valid documents
        # and require real repair plus exact independent output equality.
        foreach($kind in @('xml','intermediate')){
            $damagedOut=Join-Path $modeWork ('damage-'+$kind)
            $beforeDamage=RunCli ($kind+'-warm') $mod $damagedOut $true;Cache $beforeDamage 'cold';SameOutputs $plain $beforeDamage
            $modeState.corruptions+=,@{kind=$kind;entries=@(DamagePackage $damagedOut $kind)}
            $afterDamage=RunCli ($kind+'-damaged-cold') $mod $damagedOut $true;Cache $afterDamage $(if($kind -eq 'xml'){'revalidated'}else{'repaired'});SameOutputs $plain $afterDamage
            $repairedHot=RunCli ($kind+'-repaired-hot') $mod $damagedOut $true;Cache $repairedHot 'hot';SameOutputs $plain $repairedHot
        }
        $armorPath="$mod/Data/Armor.xml";$valid=[IO.File]::ReadAllBytes($armorPath);$confirmed=CacheFiles $cachedOut
        [IO.File]::WriteAllText($armorPath,'<AssetDeclaration>broken')
        $failed=RunCli 'invalid-include' $mod $cachedOut $true 4;CacheUnchanged $cachedOut $confirmed
        [IO.File]::WriteAllBytes($armorPath,$valid)
        $retry=RunCli 'retry-after-invalid-include' $mod $cachedOut $true;SameOutputs $plain $retry
        $confirmed=CacheFiles $cachedOut
        $cancelled=RunCli 'actual-cancel' $mod $cachedOut $true 6 $true;CacheUnchanged $cachedOut $confirmed
        $afterCancel=RunCli 'retry-after-actual-cancel' $mod $cachedOut $true;SameOutputs $plain $afterCancel
        $publishedRoot=Join-Path $cachedOut 'published';$artifact=@($afterCancel.report.artifacts)[0]
        $held=Join-Path (CacheAt $cachedOut).streams ([IO.Path]::GetRelativePath($publishedRoot,$artifact.Path))
        $lockedHot=RunCli 'unchanged-locked-output' $mod $cachedOut $true 0 $false $held;SameOutputs $plain $lockedHot

        $unrelated=Join-Path $cachedOut 'published/unrelated.keep'
        [IO.File]::WriteAllText($unrelated,'preserve unrelated output')
        $repeat=RunCli 'repeat-with-unrelated-output' $mod $cachedOut $true
        SameOutputs $plain $repeat
        Need ([IO.File]::ReadAllText($unrelated) -ceq 'preserve unrelated output') 'Repeated compilation removed unrelated output'
        $script:contents='base'
        foreach($family in @('audio','w3x')){
            $payloadMod=Join-Path $modeWork $family;$payloadCache=Join-Path $modeWork ($family+'-cache');$payloadPlain=Join-Path $modeWork ($family+'-plain')
            if($family -eq 'audio'){AudioMod $payloadMod;$script:expectedArtifacts=5}else{W3xMod $payloadMod;$script:expectedArtifacts=4}
            $before=RunCli ($family+'-disabled') $payloadMod $payloadPlain $false;Cache $before 'disabled'
            $payloadCold=RunCli ($family+'-cold') $payloadMod $payloadCache $true;Cache $payloadCold 'cold';SameOutputs $before $payloadCold;$script:hotParseCeiling=$payloadCold.report.cache.xmlParse
            $payloadHot=RunCli ($family+'-hot') $payloadMod $payloadCache $true;Cache $payloadHot 'hot';SameOutputs $before $payloadHot;Need ($payloadHot.report.cache.xmlParse -eq 0) 'Hot Audio/W3X unexpectedly parsed XML'
            if($family -eq 'audio'){
                $modeState.corruptions+=,@{kind='cdata';entries=@(DamagePackage $payloadCache 'cdata')}
                $damaged=RunCli 'audio-missing-cdata-cold' $payloadMod $payloadCache $true;Cache $damaged 'repaired';SameOutputs $before $damaged
                $repairedHot=RunCli 'audio-repaired-cdata-hot' $payloadMod $payloadCache $true;Cache $repairedHot 'hot';SameOutputs $before $repairedHot
                $input="$payloadMod/Audio/probe.wav";$stamp=[IO.File]::GetLastWriteTimeUtc($input);$bytes=[IO.File]::ReadAllBytes($input);$length=$bytes.Length
                $bytes[44]=0x34;$bytes[45]=0x12;[IO.File]::WriteAllBytes($input,$bytes);[IO.File]::SetLastWriteTimeUtc($input,$stamp)
                Need ((Get-Item -LiteralPath $input).Length -eq $length -and [IO.File]::GetLastWriteTimeUtc($input) -eq $stamp) 'Audio mutation failed to preserve length/mtime'
            }else{SameLengthMutation "$payloadMod/Data/W3x.xml" 'FrameRate="30"' 'FrameRate="31"'}
            $changed=RunCli ($family+'-invalidated') $payloadMod $payloadCache $true;Cache $changed 'incremental';DifferentOutputs $payloadHot $changed
            $changedPlain=RunCli ($family+'-changed-disabled') $payloadMod $payloadPlain $false;SameOutputs $changedPlain $changed
            $changedHot=RunCli ($family+'-changed-hot') $payloadMod $payloadCache $true;Cache $changedHot 'hot';SameOutputs $changedPlain $changedHot;Need ($changedHot.report.cache.xmlParse -eq 0) 'Changed-hot Audio/W3X unexpectedly parsed XML'
            if($family -eq 'w3x'){OriginalOracle $changedHot 'original-w3x'}
        }
        $metadataExe=Join-Path $modeOut 'original-texture-metadata.exe'
        $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
        $metadataSource=(Resolve-Path -LiteralPath (Join-Path $root 'recovery/tests/CompilerLegacyCacheMetadata.cs')).Path
        & $csc /nologo /platform:x86 /r:System.Web.Extensions.dll ("/out:"+$metadataExe) $metadataSource *> "$modeOut/texture-metadata-build.log"
        if($LASTEXITCODE -ne 0){Get-Content "$modeOut/texture-metadata-build.log" -Tail 40|Out-Host}
        Need ($LASTEXITCODE -eq 0) 'Original texture metadata probe did not compile'
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>'|Set-Content "$metadataExe.config"
        Need ((Hash "$root/SDK/tools/BinaryAssetBuilder.TextureCompiler.dll") -ceq '4f559428e11a5cf8b914792e0eccf3b94397b8851233ec658d4c02c4633e9d9e') 'Original Texture metadata DLL identity changed'
        & $metadataExe "$root/SDK/tools" "$modeOut/original-texture-metadata.json" *> "$modeOut/texture-metadata.log"
        if($LASTEXITCODE -ne 0){Get-Content "$modeOut/texture-metadata.log" -Tail 40|Out-Host}
        Need ($LASTEXITCODE -eq 0) 'Original texture metadata probe failed'
        $textureMetadata=Get-Content "$modeOut/original-texture-metadata.json" -Raw|ConvertFrom-Json
        # All original texture providers now participate in session reuse.
        # Keep complete raw output comparisons for cold/hot/payload mutation.
        foreach($type in @('Texture','OnDemandTexture','TerrainTextureAtlas')){
            $textureMod=Join-Path $modeWork ('t'+[array]::IndexOf(@('Texture','OnDemandTexture','TerrainTextureAtlas'),$type));$textureOut=Join-Path $modeWork ('o'+[array]::IndexOf(@('Texture','OnDemandTexture','TerrainTextureAtlas'),$type))
            New-Item -ItemType Directory -Force -Path "$textureMod/Data"|Out-Null
            & python "$root/recovery/probes/TextureRedirect/MakeFixture.py" "$textureMod/Data/probe.tga"
            Need ($LASTEXITCODE -eq 0) 'Session Texture fixture preparation failed'
            if($type -ceq 'TerrainTextureAtlas'){$asset='<TerrainTextureAtlas id="SessionTexture" AtlasSize="2048" AllowLossyCompression="false"><Tile TextureID="1" BaseTexture="probe.tga" NormalTexture="probe.tga"><Cell IndexX="0" IndexY="0"/></Tile></TerrainTextureAtlas>'}
            else{$asset='<'+$type+' id="SessionTexture" File="probe.tga" Type="StandardTexture" GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/>'}
            Put "$textureMod/Data/Mod.xml" ('<AssetDeclaration xmlns="uri:ea.com:eala:asset">'+$asset+'</AssetDeclaration>')
            $script:expectedArtifacts=4+[int][bool]$textureMetadata.$type.HasCustomData
            $texturePlainOut=$textureOut+'-plain'
            $texturePlain=RunCli ($type+'-disabled') $textureMod $texturePlainOut $false
            $textureCold=RunCli ($type+'-session-cold') $textureMod $textureOut $true;Cache $textureCold 'cold';SameOutputs $texturePlain $textureCold;$script:hotParseCeiling=$textureCold.report.cache.xmlParse
            $textureBefore=StreamTimes $textureOut
            $textureHot=RunCli ($type+'-session-hot') $textureMod $textureOut $true;Cache $textureHot 'hot';SameOutputs $texturePlain $textureHot
            LinkedHot $textureHot $textureBefore
            $image="$textureMod/Data/probe.tga";$stamp=[IO.File]::GetLastWriteTimeUtc($image);$bytes=[IO.File]::ReadAllBytes($image);$bytes[18]=$bytes[18] -bxor 127
            [IO.File]::WriteAllBytes($image,$bytes);[IO.File]::SetLastWriteTimeUtc($image,$stamp)
            $textureChanged=RunCli ($type+'-session-invalidated') $textureMod $textureOut $true;Cache $textureChanged 'incremental';DifferentOutputs $textureHot $textureChanged
            $textureChangedPlain=RunCli ($type+'-changed-disabled') $textureMod $texturePlainOut $false;SameOutputs $textureChangedPlain $textureChanged
            $textureChangedHot=RunCli ($type+'-changed-hot') $textureMod $textureOut $true;Cache $textureChangedHot 'hot';SameOutputs $textureChangedPlain $textureChangedHot
            OriginalOracle $textureChangedHot ($type+'-original-mutated') $textureMetadata.$type
            $textureBuildOut=$textureOut+'-build'
            $textureBuildCold=RunCli ($type+'-build-cold') $textureMod $textureBuildOut $false -BuildEnabled $true;SameOutputs $textureChangedPlain $textureBuildCold
            Need ($textureBuildCold.report.cache.hits -eq 0 -and $textureBuildCold.report.cache.misses -gt 0) 'Texture build cache did not record actual cold misses'
            $textureBuildHot=RunCli ($type+'-build-hot') $textureMod $textureBuildOut $false -BuildEnabled $true;SameOutputs $textureChangedPlain $textureBuildHot
            Need ($textureBuildHot.report.cache.hits -gt 0 -and $textureBuildHot.report.cache.misses -eq 0 -and $textureBuildHot.report.cache.instanceCompile -eq 0) 'Texture build cache did not reuse original asset and complete CDATA'
        }
        # Original defaults and all session controls use the same raw cache.
        $script:contents='base';$script:expectedArtifacts=4
        $optionsMod=Join-Path $modeWork 'options';MakeMod $optionsMod $probe
        $optionsOut=Join-Path $modeWork 'options-cache';$optionsPlainOut=Join-Path $modeWork 'options-plain'
        $optionsPlain=RunCli 'options-disabled' $optionsMod $optionsPlainOut $false
        $defaults=RunCli 'original-defaults-cold' $optionsMod $optionsOut $true -OriginalDefaults;SameOutputs $optionsPlain $defaults
        Need ($defaults.report.cache.sessionCache -and $defaults.report.cache.compressedSessionCache -and $defaults.report.cache.partialSessionCache -and !$defaults.report.cache.freezeSessionCache -and !$defaults.report.cache.alwaysTouchCache -and $defaults.report.cache.misses -gt 0) 'Original SDK cache defaults are not active'
        Need (Test-Path -LiteralPath (Join-Path (CacheAt $optionsOut).session 'BinaryAssetBuilder.SessionCache.xml.deflate')) 'Default original Deflate file is absent'
        $defaultsHot=RunCli 'original-defaults-hot' $optionsMod $optionsOut $true -OriginalDefaults;SameOutputs $optionsPlain $defaultsHot
        Need ($defaultsHot.report.cache.documentReuse -gt 0 -and $defaultsHot.report.cache.instanceCompile -eq 0) 'Original default session did not reuse compiled documents'
        $xml=RunCli 'original-xml-switch' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--csc=false');SameOutputs $optionsPlain $xml
        Need ((Test-Path -LiteralPath (Join-Path (CacheAt $optionsOut).session 'BinaryAssetBuilder.SessionCache.xml')) -and !(Test-Path -LiteralPath (Join-Path (CacheAt $optionsOut).session 'BinaryAssetBuilder.SessionCache.xml.deflate'))) 'XML/Deflate switch left an ambiguous active representation'
        $frozenBytes=CacheFiles $optionsOut
        SameLengthMutation "$optionsMod/Data/Armor.xml" '72.5%' '42.5%'
        $frozen=RunCli 'original-freeze-edit' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--fsc=true','--csc=false');CacheUnchanged $optionsOut $frozenBytes
        Need ($frozen.report.cache.freezeSessionCache -and $frozen.report.cache.xmlParse -gt 0) 'Frozen old session did not revalidate the modified document'
        $editedPlain=RunCli 'original-freeze-edit-disabled' $optionsMod $optionsPlainOut $false;SameOutputs $editedPlain $frozen;DifferentOutputs $optionsPlain $frozen
        $frozenAgain=RunCli 'original-freeze-repeat' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--fsc=true','--csc=false');CacheUnchanged $optionsOut $frozenBytes;SameOutputs $editedPlain $frozenAgain
        Need ($frozenAgain.report.cache.xmlParse -gt 0) 'Freeze updated the document state instead of reading the old session'
        $thawed=RunCli 'original-unfreeze' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--csc=false');SameOutputs $editedPlain $thawed
        $thawedHot=RunCli 'original-unfreeze-hot' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--csc=false');SameOutputs $editedPlain $thawedHot
        Need ($thawedHot.report.cache.documentReuse -gt 0 -and $thawedHot.report.cache.instanceCompile -eq 0) 'Unfrozen session did not persist the edited state'
        $assetFiles=@(Get-ChildItem -LiteralPath (CacheAt $optionsOut).build -Filter '*.asset' -File -Recurse);Need ($assetFiles.Count -gt 0) 'Original network asset layout is absent'
        $oldStamp=[DateTime]::new(2001,1,1,0,0,0,[DateTimeKind]::Utc)
        foreach($assetFile in $assetFiles){[IO.File]::SetLastWriteTimeUtc($assetFile.FullName,$oldStamp)}
        $untouched=RunCli 'original-touch-disabled' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--csc=false');SameOutputs $editedPlain $untouched
        foreach($assetFile in $assetFiles){Need ([IO.File]::GetLastWriteTimeUtc($assetFile.FullName) -eq $oldStamp) 'Default atc:false touched a build cache asset reused from intermediate'}
        $touched=RunCli 'original-touch-enabled' $optionsMod $optionsOut $true -OriginalDefaults -Extra @('--atc=true','--csc=false');SameOutputs $editedPlain $touched
        Need ($touched.report.cache.alwaysTouchCache -and @($assetFiles|Where-Object{[IO.File]::GetLastWriteTimeUtc($_.FullName) -gt $oldStamp}).Count -eq $assetFiles.Count) 'atc:true did not reach original native cache timestamp updates'
        $external=Join-Path (CacheAt $optionsOut).partition 'custom';$sessionDirectory=Join-Path $external 'sessions';$buildDirectory=Join-Path $external 'assets';$intermediateDirectory=Join-Path $external 'intermediate'
        $custom=RunCli 'original-cache-directories' $optionsMod ($optionsOut+'-custom') $true -OriginalDefaults -Extra @(('/scd:'+$sessionDirectory),('/bcd:'+$buildDirectory),('/iod:'+$intermediateDirectory));SameOutputs $editedPlain $custom
        Need ((Test-Path -LiteralPath (Join-Path $sessionDirectory 'BinaryAssetBuilder.SessionCache.xml.deflate')) -and @(Get-ChildItem -LiteralPath $buildDirectory -Filter '*.asset' -File -Recurse).Count -gt 0 -and @(Get-ChildItem -LiteralPath $intermediateDirectory -Filter '*.asset' -File -Recurse).Count -gt 0) 'scd/bcd/iod did not use independent original directories'
        $aliases=RunCli 'original-cache-directory-aliases' $optionsMod ($optionsOut+'-aliases') $true -OriginalDefaults -Extra @(('/scp:'+$sessionDirectory),('/bcp:'+$buildDirectory),('/iod:'+$intermediateDirectory));SameOutputs $editedPlain $aliases
        Need ($aliases.report.cache.documentReuse -gt 0 -and $aliases.report.cache.instanceCompile -eq 0) 'scp/bcp did not reuse the same original directories'
        $configPath=Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config';$configBytes=[IO.File]::ReadAllBytes($configPath)
        try{
            [xml]$cacheConfig=[IO.File]::ReadAllText($configPath);$section=$cacheConfig.SelectSingleNode('/configuration/assetbuilder')
            $configuredAssets=Join-Path $modeWork 'configured-assets';$section.SetAttribute('buildCache','false');$section.SetAttribute('buildCacheRoot',$configuredAssets);$cacheConfig.Save($configPath)
            $configured=RunCli 'original-sdk-cache-config' $optionsMod ($optionsOut+'-config') $true -OriginalDefaults;SameOutputs $editedPlain $configured
            Need ($configured.report.effectiveOptions.bc.Value -ceq 'false' -and $configured.report.effectiveOptions.bc.Source -ceq 'sdkConfig' -and $configured.report.cache.hits -eq 0 -and $configured.report.cache.misses -eq 0) 'Original SDK buildCache setting did not override its factory default'
            $configuredOverride=RunCli 'original-sdk-cache-config-cli-override' $optionsMod ($optionsOut+'-config-cli') $true -OriginalDefaults -Extra @('--bc=true');SameOutputs $editedPlain $configuredOverride
            Need ($configuredOverride.report.effectiveOptions.bc.Value -ceq 'true' -and $configuredOverride.report.effectiveOptions.bc.Source -ceq 'commandLine' -and !(Test-Path -LiteralPath $configuredAssets) -and @(Get-ChildItem -LiteralPath (CacheAt ($optionsOut+'-config-cli')).build -Filter '*.asset' -File -Recurse).Count -gt 0) 'CLI buildCache override did not stay in its SDK/MOD partition'
        }finally{[IO.File]::WriteAllBytes($configPath,$configBytes)}
        $rawExport=Join-Path $modeWork 'original-bab-cache'
        OriginalOracle $editedPlain 'original-cache-export' -CacheExportDirectory $rawExport
        $raw=Join-Path $rawExport 'mod-a'
        # The original format is unchanged; explicitly stage this test's raw BAB
        # state inside the allowed partition instead of permitting outside roots.
        $importRoots=CacheRoots $optionsMod ($optionsOut+'-original')
        foreach($pair in @(@{source='session';target=$importRoots.session},@{source='cache';target=$importRoots.build},@{source='intermediate';target=$importRoots.intermediate})){
            New-Item -ItemType Directory -Force -Path $pair.target|Out-Null
            Copy-Item -Path (Join-Path (Join-Path $raw $pair.source) '*') -Destination $pair.target -Recurse
        }
        $imported=RunCli 'original-bab-cache-import' $optionsMod ($optionsOut+'-original') $true -OriginalDefaults;SameOutputs $editedPlain $imported
        Need ($imported.report.cache.documentReuse -gt 0 -and $imported.report.cache.instanceCompile -eq 0) 'Production did not reuse the actual original BAB session and intermediate assets'
        # Complete one included document before a guaranteed later XML failure.
        $partialMod=Join-Path $modeWork 'partial';MakeMod $partialMod $probe
        Put "$partialMod/Data/Mod.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"><Includes><Include type="all" source="Armor.xml"/><Include type="all" source="Broken.xml"/></Includes></AssetDeclaration>'
        $partialArmor=[IO.File]::ReadAllText("$partialMod/Data/Armor.xml")
        Put "$partialMod/Data/Armor.xml" $partialArmor.Replace('</AssetDeclaration>','<PathMusicEvent id="CacheEvent" PathfinderEventHeader="events.h" IsXferable="true"/></AssetDeclaration>')
        Put "$partialMod/Data/Broken.xml" '<AssetDeclaration>broken'
        $partialOut=Join-Path $modeWork 'partial-cache';$noPartialOut=Join-Path $modeWork 'no-partial-cache'
        $partialFailure=RunCli 'original-partial-failure' $partialMod $partialOut $true 4 -OriginalDefaults
        Need ($partialFailure.report.cache.partialSessionsSaved -gt 0 -and (Test-Path -LiteralPath (Join-Path (CacheAt $partialOut).session 'BinaryAssetBuilder.SessionCache.xml.deflate'))) 'psc:true did not save completed document work after failure'
        $noPartialFailure=RunCli 'original-no-partial-failure' $partialMod $noPartialOut $true 4 -OriginalDefaults -Extra @('--psc=false')
        Need (!(Test-Path -LiteralPath (Join-Path (CacheAt $noPartialOut).session 'BinaryAssetBuilder.SessionCache.xml.deflate'))) 'psc:false saved an aborted session'
        Need (@(Get-ChildItem -LiteralPath (CacheAt $noPartialOut).build -Filter '*.asset' -File -Recurse).Count -gt 0) 'Original build cache did not retain successfully compiled assets from an aborted build'
        Put "$partialMod/Data/Broken.xml" '<AssetDeclaration xmlns="uri:ea.com:eala:asset"/>'
        $partialRetry=RunCli 'original-partial-retry' $partialMod $partialOut $true -OriginalDefaults
        $partialPlain=RunCli 'original-partial-retry-disabled' $partialMod ($partialOut+'-plain') $false;SameOutputs $partialPlain $partialRetry
        Need ($partialRetry.report.cache.documentReuse -gt 0) 'Partial retry did not reuse its completed included document'
        $noPartialRetry=RunCli 'original-no-partial-retry' $partialMod $noPartialOut $true -OriginalDefaults -Extra @('--psc=false','--iod',(Join-Path (CacheAt $noPartialOut).intermediate 'retry'));SameOutputs $partialPlain $noPartialRetry
        Need ($noPartialRetry.report.cache.documentReuse -eq 0 -and $noPartialRetry.report.cache.hits -gt 0) 'psc:false retry must parse source but reuse independently completed native assets'
        $modeState.passed=$true
    }
    $state.succeeded=$true;$global:LASTEXITCODE=0
}catch{$state.error=$_.Exception.ToString();throw}
finally{
    $state|ConvertTo-Json -Depth 18|Set-Content "$out/validation.json"
    try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "Session-cache work cleanup failed: $_"}
}
