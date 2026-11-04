#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$FixtureResult,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180,
    [ValidateRange(1,3)][int]$ExpectedCaseCount=2,
    [string]$CacheExportDirectory,
    [switch]$CompressedCacheExport,
    [string]$FixtureRunId
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerCliOutputs.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$FixtureResult=(Resolve-Path -LiteralPath $FixtureResult).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite original backend baseline evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Inventory([string]$Directory){
    @(Get-ChildItem -LiteralPath $Directory -File -Recurse|Sort-Object FullName|ForEach-Object{
        @{path=$_.FullName.Substring($Directory.Length).TrimStart([char[]]'\/');bytes=$_.Length;sha256=(Hash $_.FullName)}
    })
}
function SameInventory([string]$Directory,$Expected){
    $actual=@(Inventory $Directory)
    if($actual.Count -ne @($Expected).Count){throw "Input file count changed: $Directory"}
    $map=@{};foreach($item in $actual){$map[$item.path]=$item}
    foreach($item in $Expected){if(!$map.ContainsKey($item.path) -or $map[$item.path].sha256 -cne $item.sha256){throw "Input changed: $Directory/$($item.path)"}}
}
function Contains([string]$Directory,[string]$Path){
    $directoryFull=[IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/')
    $pathFull=[IO.Path]::GetFullPath($Path)
    $pathFull.Equals($directoryFull,[StringComparison]::OrdinalIgnoreCase) -or $pathFull.StartsWith($directoryFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function CompareBytes([string]$Original,[string]$Backend){
    $left=[IO.File]::ReadAllBytes($Original);$right=[IO.File]::ReadAllBytes($Backend)
    if($left.Length -ne $right.Length){throw "Independent original/backend output size differs: $Original vs $Backend"}
    if([Linq.Enumerable]::SequenceEqual[byte]($left,$right)){return}
    for($i=0;$i -lt $left.Length;$i++){if($left[$i] -ne $right[$i]){throw "Independent original/backend bytes differ at $i : $Original vs $Backend"}}
}
function GetOriginalTextureCustomOutputs([string]$Manifest,$Information){
    $bytes=[IO.File]::ReadAllBytes($Manifest)
    if($bytes.Length -lt 96 -or $bytes[0] -ne 0 -or $bytes[1] -ne 1 -or $bytes[2] -ne 6 -or $bytes[3] -ne 0 -or [BitConverter]::ToUInt32($bytes,12) -ne 1){throw 'Original Texture oracle requires one exact linked Win32 v6 asset'}
    $nameSize=[BitConverter]::ToUInt32($bytes,40);$nameStart=96L+[BitConverter]::ToUInt32($bytes,32)+[BitConverter]::ToUInt32($bytes,36)
    if($nameStart+$nameSize+[BitConverter]::ToUInt32($bytes,44) -ne $bytes.Length){throw 'Original Texture oracle manifest tables differ'}
    $offset=[BitConverter]::ToUInt32($bytes,72);if($offset -ge $nameSize){throw 'Original Texture name offset escaped table'}
    $start=$nameStart+$offset;$end=$start;while($end -lt $nameStart+$nameSize -and $bytes[$end] -ne 0){$end++}
    if($end -eq $nameStart+$nameSize){throw 'Original Texture name is unterminated'}
    $name=[Text.Encoding]::ASCII.GetString($bytes,[int]$start,[int]($end-$start))
    $type=[BitConverter]::ToUInt32($bytes,48);$instance=[BitConverter]::ToUInt32($bytes,52);$typeHash=[BitConverter]::ToUInt32($bytes,56);$instanceHash=[BitConverter]::ToUInt32($bytes,60)
    if($name -cne ($Information.TypeName+':SessionTexture') -or $type -ne [uint32]$Information.TypeId -or $typeHash -ne [uint32]$Information.TypeHash){throw 'Texture manifest does not match independently probed original type metadata'}
    if(!$Information.HasCustomData){return @()}
    $file=('{0:x8}.{1:x8}.{2:x8}.{3:x8}.cdata' -f $type,$typeHash,$instance,$instanceHash)
    $relative=[IO.Path]::GetFileNameWithoutExtension($Manifest)+'/cdata/'+$file
    $path=Join-Path ([IO.Path]::GetDirectoryName($Manifest)) $relative
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Original Texture manifest-bound complete CDATA is missing'}
    return @(@{qualifiedName=$name;typeId=$type;relativePath=$relative;path=$path})
}
function InvokeOriginal([string]$Exe,[string[]]$Arguments,[string]$WorkingDirectory,[string]$LogRoot){
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$Exe;$start.WorkingDirectory=$WorkingDirectory
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    # Test instrumentation must not redirect or replace the original DLL calls.
    foreach($name in @($start.Environment.Keys)){
        if($name -like 'RA3_RECOVERED_*'){$start.Environment.Remove($name)|Out-Null}
    }
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    $watch=[Diagnostics.Stopwatch]::StartNew();$started=$false
    try {
        $started=$process.Start()
        if(!$started){throw 'Original BAB process could not start'}
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $finished=$process.WaitForExit($TimeoutSeconds*1000)
        if(!$finished){
            try{$process.Kill($true)}catch{}
            if(!$process.WaitForExit(10000)){throw 'Timed-out original BAB could not be terminated within 10 seconds'}
        }
        if(![Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout,$stderr),10000)){throw 'Original BAB redirected logs did not close within 10 seconds'}
        [IO.File]::WriteAllText((Join-Path $LogRoot 'stdout.log'),$stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $LogRoot 'stderr.log'),$stderr.GetAwaiter().GetResult())
        $watch.Stop()
        $result=@{processId=$process.Id;exitCode=$process.ExitCode;elapsedMs=$watch.ElapsedMilliseconds;timedOut=(!$finished)}
        $result|ConvertTo-Json|Set-Content (Join-Path $LogRoot 'process.json')
        if(!$finished){throw "Original BAB exceeded $TimeoutSeconds seconds"}
        if($process.ExitCode -ne 0){Get-Content (Join-Path $LogRoot 'stdout.log') -Tail 80|Out-Host;Get-Content (Join-Path $LogRoot 'stderr.log') -Tail 80|Out-Host;throw "Original BAB failed with exit code $($process.ExitCode)"}
        $result
    } finally {
        if($started -and !$process.HasExited){try{$process.Kill($true);$process.WaitForExit(5000)|Out-Null}catch{}}
        $process.Dispose()
    }
}
function ArchiveStreams([string]$Directory,[string]$CaseOut,[string]$Prefix){
    $archived=@()
    if(Test-Path -LiteralPath $Directory){
        foreach($extension in @('manifest','bin','imp','relo')){
            $files=@(Get-ChildItem -LiteralPath $Directory -File -Recurse|Where-Object{$_.Extension -ceq ".$extension"}|Sort-Object FullName)
            for($index=0;$index -lt $files.Count;$index++){
                $name=if($files.Count -eq 1){"$Prefix.$extension"}else{"$Prefix-$index.$extension"}
                $target=Join-Path $CaseOut $name
                Copy-Item -LiteralPath $files[$index].FullName -Destination $target -Force
                $archived+=,@{source=$files[$index].FullName;archive=$target;bytes=$files[$index].Length;sha256=(Hash $target)}
            }
        }
    }
    $archived
}
$temporaryRoot=$null
$state=@{schemaVersion=1;succeeded=$false;status='started';sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;fixtureResultSha256=(Hash $FixtureResult);cases=@();scope="Small independent original BAB process oracle for $ExpectedCaseCount selected fixture stream inputs; exact four-stream and manifest-bound Texture CustomData comparison; not full SDK, production CLI or complete G3 acceptance"}
try {
    $source=(& git -C $root rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0){throw 'Cannot determine current source commit'}
    $fixture=Get-Content -LiteralPath $FixtureResult -Raw|ConvertFrom-Json
    $expectedFixtureRun=if($FixtureRunId){$FixtureRunId}else{$state.runId}
    if(!$fixture.succeeded -or $fixture.sourceCommit -cne $source -or $fixture.runId -cne $expectedFixtureRun){throw 'Backend fixture success/source/run identity mismatch'}
    $state.fixtureRunId=$expectedFixtureRun
    if($fixture.transportedFromRunId){
        if($fixture.transportedFromRunId -cne $expectedFixtureRun -or $fixture.originalResultSha256 -cnotmatch '^[0-9a-f]{64}$'){throw 'Transported fixture original identity differs'}
        $state.originalFixtureSha256=$fixture.originalResultSha256
    }
    if(@($fixture.baselineCases).Count -ne $ExpectedCaseCount){throw "Expected $ExpectedCaseCount successful selected fixture stream oracle cases"}
    $state.sourceCommit=$source
    $state.fixtureScope=$fixture.scope;$state.expectedCaseCount=$ExpectedCaseCount
    $state.scriptSha256=Hash $PSCommandPath
    $state.backendSourceSha256=Hash (Join-Path $root 'recovery/src/App/Application/CoreStreamCompiler.cs')
    $state.fixtureSourceSha256=Hash (Join-Path $root 'recovery/tests/CompilerBackendLifecycle.cs')
    $temporaryRoot=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('b'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
    $state.temporaryRoot=$temporaryRoot
    $names=@{};$caseIndex=0
    foreach($case in $fixture.baselineCases){
        if($case.name -notin @('mod-a','mod-b','TacticalHUDBuildQueuePage','TacticalHUDCommandListBox','TacticalHUDMainCommandBar') -or $names.ContainsKey($case.name)){throw 'Duplicate or unexpected independent baseline case'}
        $names[$case.name]=$true
        $caseOut=(New-Item -ItemType Directory -Path (Join-Path $out $case.name)).FullName
        $sdk=(Resolve-Path -LiteralPath $case.sdkRoot).Path
        $mod=(Resolve-Path -LiteralPath $case.modRoot).Path
        $input=(Resolve-Path -LiteralPath $case.inputPath).Path
        if(!(Contains $mod $input) -or (Contains $mod $caseOut) -or (Contains $sdk $caseOut)){throw 'Baseline source/output containment is invalid'}
        $tools=(Resolve-Path -LiteralPath (Join-Path $sdk 'tools')).Path
        if($case.originalToolsRoot){
            $requestedTools=(Resolve-Path -LiteralPath ([string]$case.originalToolsRoot)).Path
            $repositoryTools=(Resolve-Path -LiteralPath (Join-Path $root 'SDK/tools')).Path
            if(![IO.Path]::GetFullPath($requestedTools).TrimEnd([char[]]'\/').Equals([IO.Path]::GetFullPath($repositoryTools).TrimEnd([char[]]'\/'),[StringComparison]::OrdinalIgnoreCase)){
                throw 'Only the repository original SDK/tools may override the independent oracle tool source'
            }
            $tools=$requestedTools
        }
        $schemas=(Resolve-Path -LiteralPath (Join-Path $sdk 'Schemas/xsd')).Path
        $inputInventory=@(Inventory $mod);$toolInventory=@(Inventory $tools);$schemaInventory=@(Inventory $schemas)
        $caseState=@{name=$case.name;scope=$case.scope;passed=$false;inputPath=$input;modRoot=$mod;sdkRoot=$sdk;originalToolsRoot=$tools;inputs=$inputInventory;originalTools=$toolInventory;schemaInputs=$schemaInventory;outputs=@()}
        $state.cases+=,$caseState
        # CLR2/Framework mixed-mode tools still use MAX_PATH internally. Keep all
        # writable native build paths short; retain the exact source MOD paths,
        # including Unicode/spaces, and archive only small outputs and receipts.
        $caseTemp=(New-Item -ItemType Directory -Path (Join-Path $temporaryRoot ([string]$caseIndex))).FullName
        $caseIndex++
        $caseState.temporaryRoot=$caseTemp
        $oracle=(New-Item -ItemType Directory -Path (Join-Path $caseTemp 't')).FullName
        $streams=Join-Path $caseTemp 's';$intermediate=Join-Path $caseTemp 'i';$hashes=Join-Path $caseTemp 'h';$cache=Join-Path $caseTemp 'c'
        if($CacheExportDirectory){
            $export=Join-Path ([IO.Path]::GetFullPath($CacheExportDirectory)) $case.name
            if(Test-Path -LiteralPath $export){throw 'Refusing to overwrite original SDK cache export'}
            $intermediate=Join-Path $export 'intermediate';$hashes=Join-Path $export 'session';$cache=Join-Path $export 'cache'
        }
        try {
            Copy-Item -Path (Join-Path $tools '*') -Destination $oracle -Recurse
            $exe=Join-Path $oracle 'BinaryAssetBuilder.exe';$configPath="$exe.config"
            if((Hash $exe) -cne (Hash (Join-Path $tools 'BinaryAssetBuilder.exe'))){throw 'Original BAB copy differs from fixed SDK input'}
            # Effective SDK settings come from the actual case SDK, even when
            # immutable original EXE/DLL bytes come from repository SDK/tools.
            $caseConfig=Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config'
            $caseState.sdkConfigSha256=Hash $caseConfig
            [xml]$configuration=[IO.File]::ReadAllText($caseConfig)
            $section=$configuration.SelectSingleNode('/configuration/assetbuilder')
            if(!$section){throw 'Original BAB assetbuilder configuration missing'}
            $data=(Join-Path $mod 'Data')+';'+$section.GetAttribute('defaultDataPaths')
            $art=(Join-Path $mod 'Art')+';'+$section.GetAttribute('defaultArtPaths')
            $audio=(Join-Path $mod 'Audio')+';'+$section.GetAttribute('defaultAudioPaths')
            $dataRoot=$sdk
            # Optional explicit input-root acceptance fixture. Existing callers
            # keep the historical MOD-prepended SDK contract above. Use the
            # already resolved production roots verbatim, in their original order.
            if($case.effectiveInputRoots){
                $effective=$case.effectiveInputRoots
                $dataRoot=[string]$effective.dataRoot
                if(![IO.Path]::IsPathFullyQualified($dataRoot) -or !(Test-Path -LiteralPath $dataRoot -PathType Container)){throw 'Original oracle explicit dataRoot must be an existing absolute directory'}
                foreach($name in @('dataPaths','artPaths','audioPaths')){
                    $paths=@($effective.$name)
                    if($paths.Count -eq 0){throw "Original oracle explicit $name cannot be empty"}
                    foreach($path in $paths){if(![IO.Path]::IsPathFullyQualified([string]$path) -or !(Test-Path -LiteralPath $path -PathType Container)){throw "Original oracle explicit $name must contain existing absolute directories"}}
                }
                $data=@($effective.dataPaths) -join ';';$art=@($effective.artPaths) -join ';';$audio=@($effective.audioPaths) -join ';'
            }
            $schema=Join-Path $schemas 'CnC3Types.xsd'
            $section.SetAttribute('schema',$schema);$section.SetAttribute('dataRoot',$dataRoot)
            $section.SetAttribute('defaultDataPaths',$data);$section.SetAttribute('defaultArtPaths',$art);$section.SetAttribute('defaultAudioPaths',$audio)
            $buildConfigurations=$section.SelectSingleNode('buildConfigurations')
            if($buildConfigurations){$buildConfigurations.RemoveAll()}
            $handlers=@($section.SelectNodes('plugins/plugin')|Where-Object{$_.GetAttribute('assetTypes').Split(',') -contains 'ArmorTemplate'})
            $defaults=@($section.SelectNodes('plugins/plugin')|Where-Object{$_.GetAttribute('assetTypes') -ceq '#all'})
            if($handlers.Count -ne 1 -or $handlers[0].GetAttribute('targetType') -cne 'BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer' -or
                $defaults.Count -ne 1 -or $defaults[0].GetAttribute('targetType') -cne 'BinaryAssetBuilder.XmlCompiler.Plugin, BinaryAssetBuilder.XmlCompiler'){
                throw 'Independent oracle does not use the original Tokenizer/XML default routes'
            }
            $startup=$configuration.SelectSingleNode('/configuration/startup')
            if(!$startup){$startup=$configuration.CreateElement('startup');$configuration.configuration.AppendChild($startup)|Out-Null}
            $startup.RemoveAll();$startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true')
            $runtime=$configuration.CreateElement('supportedRuntime');$runtime.SetAttribute('version','v4.0');$startup.AppendChild($runtime)|Out-Null
            $configuration.Save($configPath)
            Copy-Item -LiteralPath $configPath -Destination (Join-Path $caseOut 'original-effective.config')
            New-Item -ItemType Directory -Path $streams,$intermediate,$hashes,$cache|Out-Null
            # Match the current Core defaults: legacy stable sorting, reference policy
            # and slow cleanup; caches and the resident/GUI hosts remain disabled.
            $arguments=@($input,"/sp:$schema","/dr:$dataRoot","/od:$streams","/iod:$intermediate","/scd:$hashes","/bcp:$cache",
                '/tp:Win32','/sc:false','/bc:false','/psc:false','/fsc:false','/csc:false','/atc:false','/sh:false','/pc:true',
                '/res:false','/gui:false','/poe:false','/mr:false','/oof:false','/sf:false','/ls:true','/ss:true','/oix:false','/vf:false',
                '/osh:false','/oar:false','/slowclean:true','/tl:3','/el:0',"/art:$art","/audio:$audio","/data:$data")
            if($CacheExportDirectory){
                $arguments=@($arguments|ForEach-Object{
                    if($_ -eq '/sc:false'){'/sc:true'}elseif($_ -eq '/bc:false'){'/bc:true'}elseif($_ -eq '/psc:false'){'/psc:true'}elseif($_ -eq '/csc:false' -and $CompressedCacheExport){'/csc:true'}else{$_}
                })
                $caseState.originalCacheExport=@{directory=$export;session=$hashes;assets=$cache;intermediate=$intermediate;compressed=[bool]$CompressedCacheExport}
            }
            $caseState.arguments=$arguments;$caseState.workingDirectory=$sdk
            @{exe=$exe;arguments=$arguments;workingDirectory=$sdk}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $caseOut 'invocation.json')
            $caseState.originalExeSha256=Hash $exe;$caseState.originalConfigSha256=Hash (Join-Path $tools 'BinaryAssetBuilder.exe.config')
            $caseState.effectiveConfigSha256=Hash $configPath
            $caseState.runtimeInputs=@(Inventory $oracle)
            $caseState.effectiveSettings=@{schema=$schema;dataRoot=$dataRoot;defaultDataPaths=$data;defaultArtPaths=$art;defaultAudioPaths=$audio;targetPlatform='Win32';sessionCache=[bool]$CacheExportDirectory;buildCache=[bool]$CacheExportDirectory;partialSessionCache=[bool]$CacheExportDirectory;compressedSessionCache=([bool]$CacheExportDirectory -and [bool]$CompressedCacheExport);precompiled=$true;resident=$false;linked=$true;stableSort=$true;outputStringHashes=$false;outputAssetReport=$false;scope='Same normalized Core settings for this selected fixture stream; original built-in plugin routes, codec, layout and metadata retained'}
            $caseState.process=InvokeOriginal $exe $arguments $sdk $caseOut
            $actual=@(Get-ChildItem -LiteralPath $streams -File -Recurse)
            $expectedTextures=@($case.expectedTextures|Where-Object{$null -ne $_})
            $custom=@()
            $originalManifests=@($actual|Where-Object{$_.Extension -ceq '.manifest'})
            if($originalManifests.Count -ne 1){throw 'Expected one unambiguous original manifest'}
            if($case.originalTextureInformation){
                $caseState.originalTextureInformation=$case.originalTextureInformation
                $caseState.originalInlineTextures=@();$caseState.backendInlineTextures=@()
                $custom=@(GetOriginalTextureCustomOutputs $originalManifests[0].FullName $case.originalTextureInformation)
                $backendCustom=@(GetOriginalTextureCustomOutputs ([string]$case.outputs.manifest) $case.originalTextureInformation)
                if($case.originalTextureInformation.TypeName -ceq 'Texture'){
                    $caseState.originalInlineTextures=@(Test-CompilerInlineTexturePayload $originalManifests[0].FullName @('Texture:SessionTexture'))
                    $caseState.backendInlineTextures=@(Test-CompilerInlineTexturePayload ([string]$case.outputs.manifest) @('Texture:SessionTexture'))
                }
            }else{
                $caseState.originalInlineTextures=@(Test-CompilerInlineTexturePayload $originalManifests[0].FullName $expectedTextures)
                $caseState.backendInlineTextures=@(Test-CompilerInlineTexturePayload ([string]$case.outputs.manifest) $expectedTextures)
                $custom=@(Get-CompilerCustomDataOutputs $originalManifests[0].FullName $expectedTextures)
                $backendCustom=@(Get-CompilerCustomDataOutputs ([string]$case.outputs.manifest) $expectedTextures)
            }
            $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($item in $custom){if(!$allowed.Add([IO.Path]::GetFullPath($item.path))){throw 'Duplicate CustomData allowlist path'}}
            foreach($file in $actual){if($file.Extension -cnotin @('.manifest','.bin','.imp','.relo') -and !$allowed.Contains($file.FullName)){throw "Original BAB unexpected output: $($file.FullName)"}}
            if($actual.Count -ne 4+$custom.Count){throw 'Original BAB output count differs from exact four-stream plus Texture CustomData allowlist'}
            if(@($case.customOutputs|Where-Object{$null -ne $_}).Count -ne $custom.Count -or $backendCustom.Count -ne $custom.Count){throw 'Backend Texture CustomData count differs'}
            foreach($extension in @('manifest','bin','imp','relo')){
                $original=@($actual|Where-Object{$_.Extension -ceq ".$extension"})
                if($original.Count -ne 1){throw "Original BAB emitted ambiguous/missing .$extension stream"}
                $backend=[string]$case.outputs.$extension
                if(!$backend -or !(Test-Path -LiteralPath $backend) -or !(Contains $case.stagingDirectory $backend)){throw "Backend oracle output missing/outside staging: $extension"}
                $originalArchive=Join-Path $caseOut ("original.$extension")
                $backendArchive=Join-Path $caseOut ("backend.$extension")
                Copy-Item -LiteralPath $original[0].FullName -Destination $originalArchive
                Copy-Item -LiteralPath $backend -Destination $backendArchive
                $pair=@{extension=$extension;original=$original[0].FullName;backend=$backend;originalArchive=$originalArchive;backendArchive=$backendArchive;bytes=$original[0].Length;originalSha256=(Hash $originalArchive);backendSha256=(Hash $backendArchive);byteIdentical=$false}
                $caseState.outputs+=,$pair
                CompareBytes $originalArchive $backendArchive
                $pair.byteIdentical=$true
            }
            $caseState.customOutputs=@()
            for($index=0;$index -lt $custom.Count;$index++){
                $original=$custom[$index]
                $candidates=@($case.customOutputs|Where-Object{$_.qualifiedName -ceq $original.qualifiedName -and $_.relativePath -ceq $original.relativePath})
                $bound=@($backendCustom|Where-Object{$_.qualifiedName -ceq $original.qualifiedName -and $_.relativePath -ceq $original.relativePath})
                if($candidates.Count -ne 1 -or $bound.Count -ne 1 -or $candidates[0].path -cne $bound[0].path -or !(Contains $case.stagingDirectory $bound[0].path)){throw 'Backend CustomData identity/path differs from manifest allowlist'}
                $originalArchive=Join-Path $caseOut ('original-'+[IO.Path]::GetFileName($original.path))
                $backendArchive=Join-Path $caseOut ('backend-'+[IO.Path]::GetFileName($original.path))
                Copy-Item -LiteralPath $original.path -Destination $originalArchive
                Copy-Item -LiteralPath $bound[0].path -Destination $backendArchive
                $pair=@{qualifiedName=$original.qualifiedName;typeId=$original.typeId;relativePath=$original.relativePath;original=$original.path;backend=$bound[0].path;originalArchive=$originalArchive;backendArchive=$backendArchive;originalSha256=(Hash $originalArchive);backendSha256=(Hash $backendArchive);byteIdentical=$false}
                $caseState.customOutputs+=,$pair
                CompareBytes $originalArchive $backendArchive
                $pair.byteIdentical=$true
            }
            if($CacheExportDirectory){
                foreach($file in @(Get-ChildItem -LiteralPath $streams -File -Filter '*.cdata' -Recurse)){
                    $relative=$file.FullName.Substring($streams.Length).TrimStart([char[]]'\/')
                    $target=Join-Path $hashes $relative;New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target))|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $target
                }
                if(!(Test-Path -LiteralPath (Join-Path $hashes ('BinaryAssetBuilder.SessionCache.xml'+$(if($CompressedCacheExport){'.deflate'}else{''}))))){throw 'Original BAB did not write the requested cache export'}
                $caseState.exportedCacheFiles=@(Inventory $export)
                Copy-Item -LiteralPath $export -Destination (Join-Path $caseOut 'original-cache-export') -Recurse
            }
            $caseState.passed=$true
        } finally {
            # Preserve small final/partial output evidence even when BAB or the
            # comparison failed; original EXE/DLL bytes remain reproducible from
            # the recorded Git inputs and are never copied into the artifact.
            $caseState.archivedOriginalOutputs=@(ArchiveStreams $streams $caseOut 'original')
            $caseState.archivedBackendOutputs=@()
            foreach($extension in @('manifest','bin','imp','relo')){
                $backend=[string]$case.outputs.$extension
                if($backend -and (Test-Path -LiteralPath $backend) -and (Contains $case.stagingDirectory $backend)){
                    $target=Join-Path $caseOut ("backend.$extension")
                    Copy-Item -LiteralPath $backend -Destination $target -Force
                    $caseState.archivedBackendOutputs+=,@{source=$backend;archive=$target;bytes=(Get-Item -LiteralPath $backend).Length;sha256=(Hash $target)}
                }
            }
            foreach($log in @('stdout','stderr')){
                $logPath=Join-Path $caseOut ("$log.log")
                if(Test-Path -LiteralPath $logPath){$caseState[$log+'Sha256']=Hash $logPath}
            }
            try {
                SameInventory $mod $inputInventory;SameInventory $tools $toolInventory;SameInventory $schemas $schemaInventory
                if($caseState.ContainsKey('runtimeInputs')){SameInventory $oracle $caseState.runtimeInputs}
                $caseState.originalInputsUnchanged=$true
            } catch {
                $caseState.originalInputsUnchanged=$false;$caseState.inputVerificationError=$_.Exception.ToString()
                $wasPassed=$caseState.passed;$caseState.passed=$false
                if($wasPassed){throw}
            }
        }
        if($caseState.passed){Write-Host "PASS IndependentBackendBaseline $($case.name): original BAB process, manifest/bin/imp/relo and $(@($caseState.customOutputs).Count) Texture CustomData byte-identical"}
    }
    if((Hash $FixtureResult) -cne $state.fixtureResultSha256){throw 'Backend fixture result changed during independent baseline'}
    $state.succeeded=$true;$state.status="passed $ExpectedCaseCount original BAB independent-process selected fixture baselines; all four stream files and exact allowed Texture CustomData byte-identical in each case; full SDK/G3/production CLI remain separate"
    $global:LASTEXITCODE=0
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {
    if($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)){
        try {Remove-Item -LiteralPath $temporaryRoot -Recurse -Force;$state.temporaryRootRemoved=$true}
        catch {$state.temporaryRootRemoved=$false;$state.temporaryCleanupError=$_.Exception.ToString()}
    }
    $state|ConvertTo-Json -Depth 14|Set-Content (Join-Path $out 'validation.json')
}
