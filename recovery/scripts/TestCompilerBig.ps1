#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180,
    [switch]$Preflight,
    [switch]$UsePackagedRuntime
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if(!$Preflight){$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path}
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite BIG evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$short=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('b'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
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
    foreach($key in $Left.files.Keys){if(!$Right.files.ContainsKey($key)){$changed=$true}elseif((Hash $Left.files[$key]) -cne (Hash $Right.files[$key])){$changed=$true}}
    Need $changed 'Input mutation did not exercise any observable compiled output change'
}
function RuntimeSnapshot([string]$Directory){
    $result=@{};foreach($file in @(Get-ChildItem -LiteralPath $Directory -File)){$result[$file.Name]=Hash $file.FullName};return $result
}
function RuntimeUnchanged([string]$Directory,$Before){
    $after=RuntimeSnapshot $Directory;Need ($after.Count -eq $Before.Count) 'BIG test changed packaged runtime file count'
    foreach($name in $Before.Keys){Need ($after.ContainsKey($name) -and $after[$name] -ceq $Before[$name]) "BIG test changed packaged runtime bytes: $name"}
}
function TreeSnapshot([string]$Directory){
    $result=@{};foreach($file in @(Get-ChildItem -LiteralPath $Directory -File -Recurse)){$result[[IO.Path]::GetRelativePath($Directory,$file.FullName)]=Hash $file.FullName};return $result
}
function TreeUnchanged([string]$Directory,$Before){
    $after=TreeSnapshot $Directory;Need ($after.Count -eq $Before.Count) 'Neutral build changed source/reference file count'
    foreach($name in $Before.Keys){Need ($after.ContainsKey($name) -and $after[$name] -ceq $Before[$name]) "Neutral build changed source/reference bytes: $name"}
}
function RunCli([string]$Name,[string]$Mod,[string]$Destination,[string]$Selection,[string]$Format,[int]$Expected=0,[bool]$Cancel=$false,[string]$HoldReadFile=$null,[string[]]$Extra=@(),[switch]$Defaults){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    New-Item -ItemType Directory -Force -Path $Destination|Out-Null
    $arguments=@('build','--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination)
    if(!$Defaults){
        $arguments+=@('--package-content',$Selection,'--package-format',$Format)
        # Keep the original no-overlay differential; default overlays are verified separately below.
        if($Format -eq 'big' -and !@($Extra|Where-Object{$_ -like '--include-additional*'}).Count){$arguments+='--include-additional=false'}
    }
    if(($Format -eq 'big' -or $Defaults) -and !@($Extra|Where-Object{$_ -like '--mod-version*'}).Count){$arguments+=@('--mod-version','1.0')}
    $arguments+=$Extra
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    $request=@{exe=$script:exe;cwd=$script:runtime;arguments=$arguments;stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=$events;cancel=$Cancel;timeoutSeconds=$TimeoutSeconds}
    if($HoldReadFile){$request.holdReadFile=$HoldReadFile}
    $request|ConvertTo-Json -Depth 6|Set-Content "$caseOut/request.json"
    & $script:control "$caseOut/request.json" "$caseOut/control.json" *> "$caseOut/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$caseOut/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:modeState.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "BIG CLI failed: $Name"
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
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq $(if($Format -ceq 'big'){2}else{$script:expectedArtifacts})) 'Session fixture did not publish exact stream/CDATAs count'
            if($Format -ceq 'big'){
                $packaged=@($rows|Where-Object{$_.eventType -ceq 'ArtifactProduced' -and $_.stageId -ceq 'package'})
                Need ($packaged.Count -eq 2) 'BIG build did not produce a current BIG/SKUDEF pair'
                $publishedRoot=Join-Path $Destination 'published'
                foreach($row in $packaged){
                    Need ([IO.Path]::GetDirectoryName([string]$row.payload.Path) -ceq $publishedRoot) 'BIG/SKUDEF was not generated in the final directory'
                    Need (@($body.artifacts|Where-Object{$_.Path -ceq $row.payload.Path -and $_.Size -eq $row.payload.Size}).Count -eq 1) 'Package event and current report differ'
                }
                Need (!(Test-Path -LiteralPath (Join-Path $Destination '.file-identities.json'))) 'BIG build recreated the removed file-hash sidecar'
            }
            $published=Join-Path $Destination 'published'
            foreach($artifact in $body.artifacts){
                Need ((Within $published $artifact.Path) -and (Hash $artifact.Path) -ceq $artifact.TestSha256 -and (Get-Item -LiteralPath $artifact.Path).Length -eq $artifact.Size) 'Cached artifact identity/containment differs'
                $relative=[IO.Path]::GetRelativePath($published,$artifact.Path);$copy=Join-Path $archived $relative
                New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($copy))|Out-Null
                Copy-Item -LiteralPath $artifact.Path -Destination $copy;$files[$relative]=$copy
            }
        }else{Need (@($body.artifacts).Count -eq 0) 'Failed/cancelled cache build accepted artifacts'}
        Need (!(Test-Path -LiteralPath (Join-Path $Destination '.build'))) 'Direct build created the removed .build staging root'

        $entry.passed=$true;$entry.buildId=$body.buildId
        return @{name=$Name;report=$body;files=$files;mod=$Mod;caseOut=$caseOut;destination=$Destination}
    }catch{
        foreach($name in @('stdout.log','stderr.log','controller.log')){Get-Content -LiteralPath (Join-Path $caseOut $name) -Tail 30 -ErrorAction SilentlyContinue|Out-Host}
        throw
    }finally{
        if(Test-Path -LiteralPath $report){Copy-Item -LiteralPath $report -Destination "$caseOut/build-report.json"}
        if(Test-Path -LiteralPath $events){Copy-Item -LiteralPath $events -Destination "$caseOut/build.events.jsonl"}
    }
}

function ExpectedInventory($Loose){
    $expected=@();$published=Join-Path $Loose.destination 'published'
    foreach($artifact in $Loose.report.artifacts){
        $relative=[IO.Path]::GetRelativePath($published,$artifact.Path).Replace('\','/')
        $task=@($Loose.report.plan.Tasks|Where-Object{$relative.StartsWith($_.Content+'/'+[IO.Path]::GetFileNameWithoutExtension($_.InputPath)+'/',[StringComparison]::Ordinal)})
        Need ($task.Count -eq 1) 'Loose BIG oracle artifact has ambiguous task owner'
        $stem=[IO.Path]::GetFileNameWithoutExtension($task[0].InputPath)
        $prefix=if($task[0].Content -ceq 'apt'){'Data/AptUI/'}elseif($task[0].Content -ceq 'maps'){'Data/AdditionalMaps/'}elseif($task[0].Content -ceq 'base'){'Data/'}else{throw 'Unknown artifact reached BIG oracle'}
        $tail=[IO.Path]::GetFileName($artifact.Path)
        if($task[0].Content -ceq 'base'){$stem='mod';if($artifact.Kind -cne 'custom-data'){$tail=$stem+[IO.Path]::GetExtension($artifact.Path)}}
        # Strip private Core streams/category directories. Game archive names
        # use four linked basenames and manifest-bound stem/cdata children.
        if($artifact.Kind -ceq 'custom-data'){$tail=$stem+'/cdata/'+$tail}
        $name=($prefix+$tail).Replace('/','\').ToLowerInvariant()
        $source=$Loose.files[[IO.Path]::GetRelativePath($published,$artifact.Path)]
        Need ($null -ne $source -and (Hash $source) -ceq $artifact.TestSha256) 'Archived loose oracle source changed'
        $expected+=,@{name=$name;source=$source;bytes=$artifact.Size;sha256=$artifact.TestSha256}
    }
    # The old SDK writes echo. after each successful APT stream, producing
    # exactly space + CRLF (Windows probe 200d0a) in <stream>.version. Derive only from this current plan.
    foreach($task in @($Loose.report.plan.Tasks|Where-Object{$_.Content -ceq 'apt'})){
        $stem=[IO.Path]::GetFileNameWithoutExtension($task.InputPath)
        $directory=Join-Path $Loose.caseOut 'versions';New-Item -ItemType Directory -Force -Path $directory|Out-Null
        $source=Join-Path $directory ($stem+'.version');[IO.File]::WriteAllBytes($source,[byte[]](32,13,10))
        $expected+=,@{name=('data\aptui\'+$stem.ToLowerInvariant()+'.version');source=$source;bytes=3;sha256=(Hash $source)}
    }
    # Default SDK step 5 derives low/medium streams from fixed mod.manifest.
    # Run the original tool against a private copy of current loose bytes.
    $baseTasks=@($Loose.report.plan.Tasks|Where-Object{$_.Content -ceq 'base'})
    foreach($task in $baseTasks){
        $manifest=@($Loose.report.artifacts|Where-Object{$_.Kind -cne 'custom-data' -and [IO.Path]::GetExtension($_.Path) -ieq '.manifest' -and (Within (Join-Path $published ('base/'+[IO.Path]::GetFileNameWithoutExtension($task.InputPath))) $_.Path)})
        Need ($manifest.Count -eq 1) 'Base LoD oracle requires exactly one current manifest'
        $source=$Loose.files[[IO.Path]::GetRelativePath($published,$manifest[0].Path)]
        $lodOut=Join-Path $Loose.caseOut ('original-lod-'+[Guid]::NewGuid().ToString('N'))
        & python "$PSScriptRoot/ValidateCompilerLoD.py" --manifest $source --original "$root/SDK/tools/LoDStreamBuilder.exe" --output $lodOut *> ($lodOut+'.log')
        Need ($LASTEXITCODE -eq 0) 'Original LoD failed for current production base stream'
        $lod=Get-Content -LiteralPath (Join-Path $lodOut 'inventory.json') -Raw|ConvertFrom-Json
        Need ($lod.succeeded -and @($lod.files).Count -eq 8) 'Original LoD did not produce exact eight-file inventory'
        foreach($item in $lod.files){$expected+=,@{name=('data\'+$item.name.ToLowerInvariant());source=$item.source;bytes=$item.bytes;sha256=$item.sha256}}
    }
    return $expected
}
function CompareBig($Big,$Loose,[string]$Name,[bool]$Additional=$false,[string]$NeutralOracle=$null,[string]$ModVersion="1.0",[string]$GameVersion="1.12",[string]$SkudefName="business"){
    $caseOut=(New-Item -ItemType Directory -Path (Join-Path $script:modeOut $Name)).FullName
    if($NeutralOracle){
        $neutral=Get-Content -LiteralPath (Join-Path $NeutralOracle 'inventory.json') -Raw|ConvertFrom-Json
        Need ($neutral.succeeded -and $neutral.hashFixChanged -and $neutral.hashFixChangedCount -gt 0 -and $neutral.resolverInsertedAssets -eq 0) 'Original neutral oracle did not exercise actual hash repair'
        $neutralArchive=Join-Path $caseOut 'neutral-original';Copy-Item -LiteralPath $NeutralOracle -Destination $neutralArchive -Recurse
        $expected=@($neutral.files)
        foreach($item in $expected){$item.source=Join-Path $neutralArchive ([IO.Path]::GetRelativePath($NeutralOracle,$item.source))}
    }else{$expected=@(ExpectedInventory $Loose)}
    Need ($expected.Count -eq ($Loose.files.Count+@($Loose.report.plan.Tasks|Where-Object{$_.Content -ceq 'apt'}).Count+8*@($Loose.report.plan.Tasks|Where-Object{$_.Content -ceq 'base'}).Count) -and @($expected.name|Select-Object -Unique).Count -eq $expected.Count) 'BIG oracle inventory duplicates or omits current artifacts'
    $expected|ConvertTo-Json -Depth 6 -AsArray|Set-Content "$caseOut/expected.json"
    if($Additional){
        $additionalOut=Join-Path $short ('a'+[Guid]::NewGuid().ToString('N').Substring(0,8))
        & python "$PSScriptRoot/PrepareCompilerAdditional.py" oracle --sdk-root $script:sdk --mod-root $Loose.mod --expected "$caseOut/expected.json" --control $script:additionalControl --output $additionalOut
        Need ($LASTEXITCODE -eq 0) 'Original SDK Additional copy oracle failed'
        $additionalInventory=Get-Content -LiteralPath (Join-Path $additionalOut 'inventory.json') -Raw|ConvertFrom-Json
        Need ($additionalInventory.succeeded -eq $true) 'Original Additional overlay did not finish'
        $expected=@($additionalInventory.files)
        Need (@($expected.name|Select-Object -Unique).Count -eq $expected.Count -and @($expected|Where-Object{$_.name -imatch '\.asset$'}).Count -eq 0) 'Additional oracle retained asset/duplicate members'
        $additionalArchive=Join-Path $caseOut 'additional-original';Copy-Item -LiteralPath $additionalOut -Destination $additionalArchive -Recurse
        foreach($item in $expected){$item.source=Join-Path $additionalArchive ([IO.Path]::GetRelativePath($additionalOut,$item.source))}
        $expected|ConvertTo-Json -Depth 6 -AsArray|Set-Content "$caseOut/expected.json"
    }
    $oracleWork=(New-Item -ItemType Directory -Path (Join-Path $short ('o'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
    $tools=(New-Item -ItemType Directory -Path (Join-Path $oracleWork 't')).FullName
    $inputs=(New-Item -ItemType Directory -Path (Join-Path $oracleWork 'i')).FullName
    foreach($item in $expected){$path=Join-Path $inputs $item.name;New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($path))|Out-Null;Copy-Item -LiteralPath $item.source -Destination $path}
    [IO.File]::WriteAllText((Join-Path $inputs 'excluded.asset'),'Original -x:*.asset must exclude this stale fixture')
    $original=Join-Path $tools 'MakeBig.exe';Copy-Item -LiteralPath "$root/SDK/tools/MakeBig.exe" -Destination $original
    Need ((Hash $original) -ceq (Hash "$root/SDK/tools/MakeBig.exe")) 'MakeBig oracle EXE differs from original SDK bytes'
    [IO.File]::WriteAllText(($original+'.config'),'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>')
    $archive=Join-Path $oracleWork 'original.big'
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$original;$start.WorkingDirectory=$tools;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('-f',$inputs,'-x:*.asset',('-o:'+$archive))){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try{
        Need ($process.Start()) 'Original MakeBig did not start'
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);throw 'Original MakeBig timed out'}
        [IO.File]::WriteAllText("$caseOut/original.stdout.log",$stdout.GetAwaiter().GetResult());[IO.File]::WriteAllText("$caseOut/original.stderr.log",$stderr.GetAwaiter().GetResult())
        Need ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $archive)) 'Original MakeBig rejected exact artifact inventory'
    }finally{$process.Dispose()}
    $bigFiles=@($Big.report.artifacts|Where-Object{$_.Kind -ceq 'package.big'});$skuFiles=@($Big.report.artifacts|Where-Object{$_.Kind -ceq 'package.skudef'})
    Need ($bigFiles.Count -eq 1 -and $skuFiles.Count -eq 1) 'BIG report omitted exact package identities'
    Need ([IO.Path]::GetFileName($bigFiles[0].Path) -ceq ("business_"+$ModVersion+".big") -and [IO.Path]::GetFileName($skuFiles[0].Path) -ceq ($SkudefName+"_"+$ModVersion+".skudef")) 'BIG/skudef filenames differ from production contract'
    $skudef=[IO.File]::ReadAllBytes($skuFiles[0].Path)
    Need ([Convert]::ToBase64String($skudef) -ceq [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(("mod-game "+$GameVersion+"`r`nadd-big business_"+$ModVersion+".big`r`n")))) 'Game skudef raw ASCII/CRLF commands/version differ from old SDK script'
    & python "$PSScriptRoot/ValidateCompilerBig.py" --archive $bigFiles[0].Path --oracle $archive --expected "$caseOut/expected.json" --output "$caseOut/comparison.json"
    Need ($LASTEXITCODE -eq 0) 'BIG/original MakeBig format, member inventory or raw contents differ'
    Copy-Item -LiteralPath $archive -Destination (Join-Path $caseOut 'original.big')
    $script:modeState.comparisons+=,@{name=$Name;originalExeSha256=(Hash $original);expected=$expected;comparison=(Get-Content "$caseOut/comparison.json" -Raw|ConvertFrom-Json)}
}

function PrepareFixture([string]$Path,[string]$Log){
    & python "$PSScriptRoot/PrepareCompilerCliBusiness.py" $Path *> $Log;Need ($LASTEXITCODE -eq 0) 'BIG business fixture generation failed'
    [IO.File]::WriteAllText("$Path/Data/AptUI/OnDemand.xml",'<AssetDeclaration xmlns="uri:ea.com:eala:asset"><OnDemandTexture id="BigCdata" File="probe.tga" Type="StandardTexture" GenerateMipMaps="false" OutputFormat="A8R8G8B8" AllowAutomaticResize="false"/></AssetDeclaration>')
    & python "$root/recovery/probes/TextureRedirect/MakeFixture.py" "$Path/Data/AptUI/probe.tga";Need ($LASTEXITCODE -eq 0) 'BIG CDATA fixture generation failed'
    Copy-Item -LiteralPath "$Path/Data/AptUI/probe.tga" -Destination "$Path/Data/probe.tga"
    $check='import pathlib,runpy,sys;from lxml import etree as E;r=pathlib.Path(sys.argv[1]);p=pathlib.Path(sys.argv[2]);s=runpy.run_path(str(r/"recovery/scripts/GenerateXmlSdkModA.py"),run_name="big_fixture_schema")["aggregate_schema"]();s.assertValid(E.parse(str(p/"Data/AptUI/OnDemand.xml")));d=E.parse(str(p/"Data/Mod.xml"));n=E.parse(str(p/"Data/AptUI/OnDemand.xml")).getroot()[0];n.set("id","BigBaseCdata");d.getroot().append(n);s.assertValid(d);d.write(str(p/"Data/Mod.xml"),encoding="utf-8",xml_declaration=True);assert (p/"Data/probe.tga").read_bytes()==(p/"Data/AptUI/probe.tga").read_bytes();assert len((p/"Data/AptUI/probe.tga").read_bytes())==18+256*256*4;assert b"mod-game" in (r/"SDK/defaultscript.cs").read_bytes();assert (r/"SDK/tools/MakeBig.exe").read_bytes()[:2]==b"MZ"'
    & python -c $check $root $Path *>> $Log;Need ($LASTEXITCODE -eq 0) 'BIG OnDemand/CDATAs fixture source-XSD preflight failed'
    [IO.File]::WriteAllText("$Path/Data/AptUI/not-listed.asset",'Must never be included by directory globbing')
}
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Actual production P7 BIG apt/maps selected and combined, exact current loose member bytes and independent original MakeBig format/content oracle, skudef, CDATA, cancellation/failure, atomic single-file replacement and retry without MOD rollback; default base LoD, explicit SDK-then-MOD Additional overlays, and optional neutral actual hash repair with no-insertion original oracle; component insertion evidence remains separate; final distribution and game runtime acceptance excluded'}
try{
    & python "$PSScriptRoot/ValidateCompilerBig.py" --self-test *> "$out/parser-preflight.json";Need ($LASTEXITCODE -eq 0) 'Independent BIG parser preflight failed'
    $fixture=Join-Path $short 'preflight-business'
    PrepareFixture $fixture "$out/fixture-preflight.log"
    if($Preflight){$state.succeeded=$true;$state.preflight=$true;$state.scope='Source-XSD business fixture and independent BIG parser preflight only';$global:LASTEXITCODE=0;return}
    Need (!(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'reuse.json')) -and !(Test-Path -LiteralPath (Join-Path $CompilerDirectory 'session-replay.json'))) 'New BIG production inputs require a current production build'
    $p7Replay=Join-Path $CompilerDirectory 'p7-replay.json'
    if(Test-Path -LiteralPath $p7Replay){
        $replay=Get-Content -LiteralPath $p7Replay -Raw|ConvertFrom-Json
        Need ($UsePackagedRuntime -and $replay.prepared -eq $true -and $replay.candidateValidated -eq $false -and $replay.requiresBigDeployment -eq $true -and $replay.originalRunConclusion -ceq 'failure' -and $replay.binarySourceCommit -ceq '5f81f664f4970176d104b1aaa725501beffac410' -and $replay.testSourceCommit -ceq $state.sourceCommit -and $replay.testScriptSha256 -ceq (Hash $PSCommandPath)) 'P7 partial replay identity/current test mismatch'
    }
    $script:control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    $script:additionalControl=Join-Path $out 'AdditionalControl.exe'
    # Reuse the proven NET48 controller toolchain. Framework csc misparses
    # mixed-separator source paths (CS1504); always pass canonical Windows paths.
    $controllerBuild=Get-Content -LiteralPath (Join-Path $out 'controller/build.json') -Raw|ConvertFrom-Json
    $csc=$controllerBuild.controllerCompiler.path
    $additionalSource=(Resolve-Path -LiteralPath (Join-Path $root 'recovery/preparation/additional/AdditionalControl.cs')).Path
    $refs=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
    $additionalArguments=@('/nologo','/noconfig','/nostdlib+','/langversion:7.3','/target:exe','/platform:x86',('/out:'+$script:additionalControl))
    foreach($name in @('mscorlib','System','System.Core')){$additionalArguments+=('/reference:'+(Resolve-Path -LiteralPath (Join-Path $refs ($name+'.dll'))).Path)}
    $additionalArguments+=$additionalSource
    & $csc @additionalArguments *> "$out/build-additional-control.log"
    Need ($LASTEXITCODE -eq 0) 'Original SDK Additional copy helper compilation failed'
    $state.usePackagedRuntime=[bool]$UsePackagedRuntime;$state.scriptSha256=Hash $PSCommandPath;$state.parserSha256=Hash "$PSScriptRoot/ValidateCompilerBig.py"
    foreach($mode in @('Debug','Release')){
        $script:modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $modeWork=(New-Item -ItemType Directory -Path (Join-Path $short $mode.Substring(0,1))).FullName
        $script:runtime=(New-Item -ItemType Directory -Path (Join-Path $modeWork 'r')).FullName
        $sourceBin=Join-Path $CompilerDirectory "$mode/bin"
        if(Test-Path -LiteralPath $p7Replay){Need ($replay.modeExeSha256.$mode -ceq (Hash (Join-Path $sourceBin 'NewModCompiler.exe'))) 'P7 replay mode EXE differs from independently accepted original bytes'}
        $runtimeFiles=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')
        if(Test-Path -LiteralPath $p7Replay){$runtimeFiles+=@('BinaryAssetBuilder.TextureCompiler.dll','BinaryAssetBuilder.TextureCompiler.dll.patch.json')}
        if($UsePackagedRuntime){
            $sourceBefore=RuntimeSnapshot $sourceBin
            Need ($sourceBefore.Count -eq $runtimeFiles.Count -and @($sourceBefore.Keys|Where-Object{$_ -cnotin $runtimeFiles}).Count -eq 0) 'Packaged BIG runtime file inventory differs'
        }
        foreach($file in $runtimeFiles){Copy-Item -LiteralPath (Join-Path $sourceBin $file) -Destination $runtime}
        if($UsePackagedRuntime){$runtimeBefore=RuntimeSnapshot $runtime;RuntimeUnchanged $runtime $sourceBefore}
        $script:exe=Join-Path $runtime 'NewModCompiler.exe'
        if(!$UsePackagedRuntime -and (Test-Path -LiteralPath $p7Replay)){& "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe $exe *> "$modeOut/prepare-texture.log";Need ($LASTEXITCODE -eq 0) 'BIG runtime Texture deployment failed'}
        $script:modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@();comparisons=@()};$state.modes+=,$modeState
        $script:sdk=(New-Item -ItemType Directory -Path (Join-Path $modeWork 's')).FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse;Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $mod=Join-Path $modeWork 'business'
        PrepareFixture $mod "$modeOut/fixture.log"
        & python "$PSScriptRoot/PrepareCompilerAdditional.py" prepare --sdk-root $script:sdk --mod-root $mod --output "$modeOut/additional-fixture"
        Need ($LASTEXITCODE -eq 0) 'Additional SDK/MOD fixture preparation failed'
        $combined=$null;$plain=$null;$baseLoose=$null
        foreach($selection in @('apt','maps','apt,maps','base','base,apt,maps')){
            $name=$selection.Replace(',','-');$script:expectedArtifacts=if($selection -ceq 'apt'){9}elseif($selection -ceq 'maps'){4}elseif($selection -ceq 'base'){5}elseif($selection -ceq 'apt,maps'){13}else{18}
            $loose=RunCli ($name+'-loose') $mod (Join-Path $modeWork ($name+'-loose')) $selection 'loose'
            $big=RunCli ($name+'-big') $mod (Join-Path $modeWork ($name+'-big')) $selection 'big'
            CompareBig $big $loose ($name+'-original-makebig')
            if($selection -ceq 'base,apt,maps'){$combined=$big;$plain=$loose}
            if($selection -ceq 'base'){$baseLoose=$loose}
        }
        $versioned=RunCli 'custom-package-identity' $mod (Join-Path $modeWork 'versioned') 'base,apt,maps' 'big' 0 $false $null @('--mod-version','2.7-beta','--game-version','1.12','--skudef-name','ExampleLauncher')
        CompareBig $versioned $plain 'versioned-original-makebig' $false $null '2.7-beta' '1.12' 'ExampleLauncher'
        Need (@($plain.report.artifacts|Where-Object{$_.Kind -ceq 'custom-data'}).Count -eq 2) 'BIG fixture did not exercise actual complete base and APT CDATAs'
        $destination=$combined.destination;$published=Join-Path $destination 'published'
        [IO.File]::WriteAllText((Join-Path $published 'stale.asset'),'Unrelated output must remain outside the BIG')
        $rebuilt=RunCli 'rebuild-with-unrelated-output' $mod $destination 'base,apt,maps' 'big'
        Need ([IO.File]::ReadAllText((Join-Path $published 'stale.asset')) -ceq 'Unrelated output must remain outside the BIG') 'BIG packaging removed unrelated output'
        CompareBig $rebuilt $plain 'replacement-original-makebig'
        $publishedBefore=@{};Get-ChildItem -LiteralPath $published -File|ForEach-Object{$publishedBefore[$_.Name]=Hash $_.FullName}
        $held=@($rebuilt.report.artifacts|Where-Object{$_.Kind -ceq 'package.big'})[0].Path
        $lockedHot=RunCli 'unchanged-locked-output' $mod $destination 'base,apt,maps' 'big' 7 $false $held

        $replacementMarker=Join-Path $published 'obsolete-generation.tmp'
        [IO.File]::WriteAllText($replacementMarker,'Unrelated marker remains while the BIG is replaced')
        $failed=RunCli 'locked-big-fault' $mod $destination 'base,apt,maps' 'big' 7 $false $held
        foreach($name in $publishedBefore.Keys){Need ((Hash (Join-Path $published $name)) -ceq $publishedBefore[$name]) 'A failed atomic BIG replacement changed completed package bytes'}
        $retry=RunCli 'retry-after-locked-big' $mod $destination 'base,apt,maps' 'big';CompareBig $retry $plain 'locked-big-retry-original'
        Need (Test-Path -LiteralPath $replacementMarker) 'BIG replacement removed unrelated output'
        $cancelled=RunCli 'actual-cancel' $mod $destination 'base,apt,maps' 'big' 6 $true
        foreach($name in $publishedBefore.Keys){Need ((Hash (Join-Path $published $name)) -ceq $publishedBefore[$name]) 'Cancellation before packaging changed completed package bytes'}
        $retry=RunCli 'retry-after-cancel' $mod $destination 'base,apt,maps' 'big';CompareBig $retry $plain 'cancel-retry-original'
        $additionalBig=RunCli 'all-with-additional' $mod (Join-Path $modeWork 'all-additional') 'base,apt,maps' 'big' 0 $false $null @('--include-additional=true')
        CompareBig $additionalBig $plain 'additional-original-copy-makebig' $true
        $automatic=RunCli 'standard-defaults' $mod (Join-Path $modeWork 'standard-defaults') 'base,apt,maps' 'big' -Defaults
        Need ($automatic.report.effectiveOptions.'package-content'.Value -ceq 'auto' -and $automatic.report.effectiveOptions.'include-additional'.Value -ceq 'true') 'Standard build did not discover content and Additional automatically'
        Need (@($automatic.report.diagnostics|Where-Object{$_.code -ceq 'ADDITIONAL_OVERWRITE' -and $_.level -ceq 'warning'}).Count -ge 3) 'Default Additional overwrites were not reported as warnings'
        CompareBig $automatic $plain 'standard-defaults-original-copy-makebig' $true
        $script:expectedArtifacts=18
        $development=RunCli 'development-defaults' $mod (Join-Path $modeWork 'development-defaults') 'base,apt,maps' 'loose' -Extra @('--profile','development','--mod-version','2.7-beta') -Defaults
        Need ($development.report.effectiveOptions.'package-content'.Value -ceq 'auto' -and $development.report.effectiveOptions.'include-additional'.Value -ceq 'false') 'Development did not default to automatic loose streams'
        Need ($development.files.Count -eq $plain.files.Count) 'Development loose inventory differs'
        foreach($name in $plain.files.Keys){Need ($development.files.ContainsKey($name) -and (Hash $development.files[$name]) -ceq (Hash $plain.files[$name])) "Development default stream bytes differ: $name"}


        $baseManifest=@($baseLoose.report.artifacts|Where-Object{[IO.Path]::GetExtension($_.Path) -ieq '.manifest'})
        Need ($baseManifest.Count -eq 1) 'Neutral fixture requires one current base manifest'
        $basePublished=Join-Path $baseLoose.destination 'published'
        $baseSource=$baseLoose.files[[IO.Path]::GetRelativePath($basePublished,$baseManifest[0].Path)]
        & python "$PSScriptRoot/PrepareCompilerNeutral.py" prepare --manifest $baseSource --sdk-root $script:sdk --output "$modeOut/neutral-fixture" *> "$modeOut/neutral-fixture.log"
        Need ($LASTEXITCODE -eq 0) 'Neutral real-stream reference fixture failed'
        $neutralOracle=Join-Path $short ('n'+[Guid]::NewGuid().ToString('N').Substring(0,8))
        & python "$PSScriptRoot/PrepareCompilerNeutral.py" oracle --manifest $baseSource --sdk-root $script:sdk --output $neutralOracle *> "$modeOut/neutral-oracle.log"
        Need ($LASTEXITCODE -eq 0) 'Original HashFix resolver LoD real-stream oracle failed'
        $referenceRoot=Join-Path $script:sdk 'BuiltMods';$referenceBefore=TreeSnapshot $referenceRoot;$modBefore=TreeSnapshot $mod
        $neutralDestination=Join-Path $modeWork 'neutral-base'
        $neutralBig=RunCli 'base-with-neutral-repair' $mod $neutralDestination 'base' 'big' 0 $false $null @('--fix-neutral-assets=true')
        CompareBig $neutralBig $baseLoose 'neutral-original-hashfix-resolver-lod-makebig' $false $neutralOracle
        TreeUnchanged $referenceRoot $referenceBefore;TreeUnchanged $mod $modBefore
        $neutralPublished=Join-Path $neutralDestination 'published';$neutralBefore=TreeSnapshot $neutralPublished
        $missing=Join-Path $referenceRoot 'Worldbuilder.manifest';$saved=$missing+'.saved';Move-Item -LiteralPath $missing -Destination $saved
        try{
            $missingResult=RunCli 'neutral-missing-reference' $mod $neutralDestination 'base' 'big' 3 $false $null @('--fix-neutral-assets=true')
            Need (@($missingResult.report.diagnostics|Where-Object{$_.code -ceq 'PACKAGE_NEUTRAL_REFERENCE_MISSING'}).Count -eq 1) 'Missing neutral reference did not emit its explicit diagnostic'
            TreeUnchanged $neutralPublished $neutralBefore
        }finally{Move-Item -LiteralPath $saved -Destination $missing}
        TreeUnchanged $referenceRoot $referenceBefore;TreeUnchanged $mod $modBefore
        $neutralRetry=RunCli 'neutral-retry-after-reference-restored' $mod $neutralDestination 'base' 'big' 0 $false $null @('--fix-neutral-assets=true')
        CompareBig $neutralRetry $baseLoose 'neutral-retry-original-makebig' $false $neutralOracle
        TreeUnchanged $referenceRoot $referenceBefore;TreeUnchanged $mod $modBefore
        if($UsePackagedRuntime){RuntimeUnchanged $sourceBin $sourceBefore;RuntimeUnchanged $runtime $runtimeBefore;$modeState.packagedRuntimeUnchanged=$true;$modeState.runtimeFiles=$runtimeBefore}
        $modeState.passed=$true
    }
    $state.succeeded=$true;$global:LASTEXITCODE=0
}catch{$state.error=$_.Exception.ToString();throw}
finally{$state|ConvertTo-Json -Depth 22|Set-Content "$out/validation.json";try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "BIG test work cleanup failed: $_"}}
