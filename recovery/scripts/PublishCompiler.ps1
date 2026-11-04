#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$AcceptanceReceipt,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180,
    [switch]$VerifyBigDeployment
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
$AcceptanceReceipt=(Resolve-Path -LiteralPath $AcceptanceReceipt).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite release output: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$short=$null;$oldPath=$env:PATH
$allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')
$state=[ordered]@{schemaVersion=1;succeeded=$false;status='started';sourceCommit=$null;binarySourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;configurations=@();scope='Exact three-file Win32/net48 runtime ZIP deployment, production doctor and small Armor MOD with source-runtime raw output equivalence; not complete P5/G3/SDK or Unicode Texture acceptance'}
function Need([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Within([string]$Directory,[string]$Path){
    $base=[IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/');$full=[IO.Path]::GetFullPath($Path)
    $full.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function Inventory([string]$Directory){
    $items=@(Get-ChildItem -LiteralPath $Directory -Force)
    Need ($items.Count -eq $allowed.Count) "Runtime does not contain exactly three files: $Directory"
    foreach($item in $items){Need (!$item.PSIsContainer -and $item.Name -cin $allowed -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) "Unexpected runtime entry: $($item.Name)"}
    return @($allowed|ForEach-Object{$file=Get-Item -LiteralPath (Join-Path $Directory $_);@{name=$_;bytes=$file.Length;sha256=(Hash $file.FullName)}})
}
function AssertInventory([string]$Directory,$Expected){
    $actual=Inventory $Directory
    for($i=0;$i -lt $Expected.Count;$i++){Need ($actual[$i].name -ceq $Expected[$i].name -and $actual[$i].bytes -eq $Expected[$i].bytes -and $actual[$i].sha256 -ceq $Expected[$i].sha256) "Runtime bytes changed: $Directory/$($Expected[$i].name)"}
}
function Controlled([string]$Name,[string]$Exe,[string]$Cwd,[string[]]$Arguments,[string]$Destination,[string]$Evidence){
    New-Item -ItemType Directory -Path $Evidence|Out-Null
    $request=@{exe=$Exe;cwd=$Cwd;arguments=$Arguments;stdout=(Join-Path $Evidence 'stdout.log');stderr=(Join-Path $Evidence 'stderr.log');events=(Join-Path $Destination 'build.events.jsonl');cancel=$false;timeoutSeconds=$TimeoutSeconds}
    $requestPath=Join-Path $Evidence 'request.json';$controlPath=Join-Path $Evidence 'control.json'
    $request|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $requestPath
    & $script:control $requestPath $controlPath *> (Join-Path $Evidence 'controller.log')
    $controlExit=$LASTEXITCODE;$audit=Get-Content -LiteralPath $controlPath -Raw|ConvertFrom-Json
    if($controlExit -ne 0 -or !$audit.succeeded -or $audit.processExitCode -ne 0){
        foreach($log in @('stdout.log','stderr.log','controller.log')){Get-Content -LiteralPath (Join-Path $Evidence $log) -Tail 25|Out-Host}
        throw "Release validation process failed: $Name"
    }
    Need ($audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0) "$Name launched a worker or descendant"
    return $audit
}
function BuildOutputs([string]$Runtime,[string]$Sdk,[string]$Mod,[string]$Destination,[string]$Evidence){
    New-Item -ItemType Directory -Path $Destination|Out-Null
    $audit=Controlled 'build' (Join-Path $Runtime 'NewModCompiler.exe') $Runtime @('build','--mod',$Mod,'--sdk-root',$Sdk,'--out',$Destination,'--package-format','loose','--package-content','base','/sc:false','/bc:false') $Destination $Evidence
    $reportPath=Join-Path $Destination 'build-report.json';$eventPath=Join-Path $Destination 'build.events.jsonl'
    $report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
    $events=@(Get-Content -LiteralPath $eventPath|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
    Need ($report.exitCode -eq 0 -and $report.status -ceq 'succeeded' -and $report.executionMode -ceq 'InProcess' -and $report.publicationStatus -ceq 'PublishedDirectory' -and @($report.externalTools).Count -eq 0) 'Packaged build did not confirm in-process successful publication'
    Need (@($report.plan.Tasks).Count -eq 1 -and $report.plan.Tasks[0].Id -ceq 'base/Mod') 'Release probe selected an unexpected task'
    Need ($events.Count -gt 0 -and @($events|Where-Object{$_.eventType -ceq 'BuildStarted'}).Count -eq 1 -and @($events|Where-Object{$_.eventType -ceq 'BuildFinished'}).Count -eq 1 -and @($events|Where-Object{$_.eventType -ceq 'StreamStarted'}).Count -eq 1 -and @($events|Where-Object{$_.eventType -ceq 'StreamCompleted'}).Count -eq 1 -and $events[-1].eventType -ceq 'BuildFinished') 'Release protocol lifecycle differs'
    Need ($events[0].payload.processId -eq $audit.processId -and $events[-1].payload.exitCode -eq 0 -and $events[-1].payload.status -ceq $report.status) 'Release terminal protocol/process identity differs'
    for($i=0;$i -lt $events.Count;$i++){Need ($events[$i].buildId -ceq $report.buildId -and $events[$i].sequence -eq $i+1) 'Release event sequence/build correlation differs'}
    $published=Join-Path $Destination 'published';Need ($report.publicationDirectory -ceq $published) 'Release publication root differs'
    $files=@(Get-ChildItem -LiteralPath $published -File -Recurse -Force)
    Need ($files.Count -eq 4 -and @($report.artifacts).Count -eq 4) 'Release probe must produce exactly four linked outputs'
    $outputs=@{}
    foreach($artifact in $report.artifacts){
        Need ((Within $published $artifact.Path) -and (Test-Path -LiteralPath $artifact.Path -PathType Leaf)) 'Release artifact escaped its publication'
        $file=Get-Item -LiteralPath $artifact.Path
        Need ($file.Length -eq $artifact.Size) 'Release artifact differs from report'
        $relative=[IO.Path]::GetRelativePath($published,$file.FullName).Replace('\','/')
        Need ([IO.Path]::GetExtension($relative) -cin @('.manifest','.bin','.imp','.relo') -and !$outputs.ContainsKey($relative)) 'Release probe returned an unexpected linked output'
        $outputs[$relative]=@{path=$file.FullName;bytes=$file.Length;sha256=(Hash $file.FullName)}
    }
    Need (@($outputs.Keys|ForEach-Object{[IO.Path]::GetExtension($_)}|Sort-Object -Unique).Count -eq 4 -and @($outputs.Keys|ForEach-Object{$_.Substring(0,$_.Length-[IO.Path]::GetExtension($_).Length)}|Sort-Object -Unique).Count -eq 1) 'Release probe did not return one complete four-file stream'
    Need (@(Get-ChildItem -LiteralPath $Destination -File -Recurse -Filter '*.tmp').Count -eq 0 -and !(Test-Path -LiteralPath (Join-Path $Destination ('.build/'+$report.buildId)))) 'Release left uncommitted build staging or temporary reports'
    Copy-Item -LiteralPath $Destination -Destination (Join-Path $Evidence 'output') -Recurse
    foreach($relative in @($outputs.Keys)){
        $file=$outputs[$relative];$file.sourcePath=$file.path
        $file.path=Join-Path (Join-Path $Evidence 'output/published') $relative
        Need ((Test-Path -LiteralPath $file.path -PathType Leaf) -and (Get-Item -LiteralPath $file.path).Length -eq $file.bytes -and (Hash $file.path) -ceq $file.sha256) 'Archived release output differs from its original bytes'
    }
    return @{audit=$audit;outputs=$outputs;report=$report;events=$events.Count;evidenceDirectory=$Evidence}
}
try {
    $state.sourceCommit=(git -C $root rev-parse HEAD).Trim();Need ($LASTEXITCODE -eq 0) 'Cannot identify packaging source'
    $state.scriptSha256=Hash $PSCommandPath
    $accept=Get-Content -LiteralPath $AcceptanceReceipt -Raw|ConvertFrom-Json
    Need ($accept.succeeded -eq $true -and @($accept.modes).Count -eq 2) 'A successful complete Debug/Release production CLI receipt is required'
    $binarySource=if($accept.binarySourceCommit){$accept.binarySourceCommit}else{$accept.sourceCommit}
    Need ($binarySource -cmatch '^[0-9a-f]{40}$') 'CLI acceptance has no exact binary source commit'
    $state.binarySourceCommit=$binarySource
    $state.acceptance=@{path=$AcceptanceReceipt;sha256=(Hash $AcceptanceReceipt);sourceCommit=$accept.sourceCommit;binarySourceCommit=$binarySource;runId=$accept.runId;runAttempt=$accept.runAttempt;scope=$accept.scope}
    $p7Replay=Join-Path $CompilerDirectory 'p7-replay.json'
    if(Test-Path -LiteralPath $p7Replay){
        $replay=Get-Content -LiteralPath $p7Replay -Raw|ConvertFrom-Json
        Need ($replay.prepared -eq $true -and $replay.candidateValidated -eq $false -and $replay.requiresBigDeployment -eq $true -and $replay.binarySourceCommit -ceq $binarySource -and $replay.testSourceCommit -ceq $state.sourceCommit -and $replay.testScriptSha256 -ceq (Hash (Join-Path $PSScriptRoot 'TestCompilerBig.ps1'))) 'P7 replay preparation identity or current test script differs'
        Need ([bool]$VerifyBigDeployment) 'P7 partial replay requires -VerifyBigDeployment before publication'
        $state.partialAcceptance=@{path=$p7Replay;sha256=(Hash $p7Replay);binarySourceCommit=$replay.binarySourceCommit;testSourceCommit=$replay.testSourceCommit;originalRunConclusion=$replay.originalRunConclusion;scope=$replay.scope}
    }
    $preparation=Join-Path $CompilerDirectory 'release-preparation.json'
    if(Test-Path -LiteralPath $preparation){
        $prepared=Get-Content -LiteralPath $preparation -Raw|ConvertFrom-Json
        if($prepared.baseBigAcceptance.succeeded -eq $true){Need ([bool]$VerifyBigDeployment) 'Accepted base BIG preparation requires -VerifyBigDeployment before publication'}
    }
    $temporaryRoot=if($env:RUNNER_TEMP){$env:RUNNER_TEMP}else{[IO.Path]::GetTempPath()}
    $short=(New-Item -ItemType Directory -Path (Join-Path $temporaryRoot ('p'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
    $deployedProduct=(New-Item -ItemType Directory -Path (Join-Path $short 'deployed-product')).FullName
    $sdk=(New-Item -ItemType Directory -Path (Join-Path $short 's')).FullName
    New-Item -ItemType Directory -Path (Join-Path $sdk 'Schemas'),(Join-Path $sdk 'tools')|Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'SDK/Schemas/xsd') -Destination (Join-Path $sdk 'Schemas') -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'SDK/tools/BinaryAssetBuilder.exe.config') -Destination (Join-Path $sdk 'tools')
    $mod=Join-Path $short 'm';New-Item -ItemType Directory -Path (Join-Path $mod 'Data')|Out-Null
    $probe=Join-Path $root 'SDK/Mods/mymod/Data/RecoveryArmorProbe.xml'
    Copy-Item -LiteralPath $probe -Destination (Join-Path $mod 'Data/Mod.xml')
    $state.probe=@{source='SDK/Mods/mymod/Data/RecoveryArmorProbe.xml';sha256=(Hash $probe);sdkConfigSha256=(Hash (Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config'));schemaInputs=@(Get-ChildItem -LiteralPath (Join-Path $sdk 'Schemas/xsd') -File -Recurse|Sort-Object FullName|ForEach-Object{@{path=[IO.Path]::GetRelativePath($sdk,$_.FullName).Replace('\','/');sha256=(Hash $_.FullName)}})}
    $script:control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'evidence/controller')
    $state.controller=Get-Content -LiteralPath (Join-Path $out 'evidence/controller/build.json') -Raw|ConvertFrom-Json
    Need ($state.controller.succeeded -and $state.controller.controllerSha256 -ceq (Hash $script:control)) 'Release controller identity differs'
    $env:PATH=(@($oldPath -split ';'|Where-Object{$_ -notmatch '[\\/]SDK[\\/]tools'}) -join ';')
    foreach($mode in @('Debug','Release')){
        $buildRoot=Join-Path $CompilerDirectory $mode;$sourceBin=Join-Path $buildRoot 'bin'
        $build=Get-Content -LiteralPath (Join-Path $buildRoot 'build.json') -Raw|ConvertFrom-Json
        $accepted=@($accept.modes|Where-Object{$_.configuration -ceq $mode})
        Need ($accepted.Count -eq 1 -and $accepted[0].passed -eq $true -and $accepted[0].exeSha256 -ceq $build.exeSha256) "$mode lacks matching production CLI acceptance"
        Need ($build.schemaVersion -eq 2 -and $build.configuration -ceq $mode -and $build.sourceCommit -ceq $binarySource -and $build.runtime -ceq 'net48/Win32' -and $build.entryPoint -ceq 'NewModCompiler.Hosting.CompilerEntry.Main' -and $build.containsProbeOrTestSources -eq $false -and $build.backendIntegrated -eq $true -and ($build.runtimeFiles -join "`n") -ceq ($allowed -join "`n")) "$mode production build receipt differs from release contract"
        $inventory=Inventory $sourceBin
        Need ($inventory[0].sha256 -ceq $build.exeSha256 -and $inventory[2].sha256 -ceq 'febdc86d6299a550c6ec87050b0cbf5205e821851e75494c699c17ce8ba2c62a') "$mode EXE or pinned ANTLR differs"
        $acceptedFiles=@($accepted[0].runtimeInputs.PSObject.Properties)
        Need ($acceptedFiles.Count -eq $allowed.Count) "$mode CLI receipt has an incomplete runtime inventory"
        foreach($file in $inventory){
            $match=@($acceptedFiles|Where-Object{$_.Name.TrimStart([char[]]'\/') -ceq $file.name})
            Need ($match.Count -eq 1 -and $match[0].Value -ceq $file.sha256) "$mode runtime file does not match accepted production bytes: $($file.name)"
        }
        Need ($build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) "$mode requires the built-in source texture plugin"
        $package=Join-Path $out ('NewModCompiler-Win32-'+$mode+'.candidate.zip')
        Compress-Archive -LiteralPath @($allowed|ForEach-Object{Join-Path $sourceBin $_}) -DestinationPath $package
        $runtime=Join-Path $deployedProduct ($mode+'/bin')
        Expand-Archive -LiteralPath $package -DestinationPath $runtime
        AssertInventory $runtime $inventory
        $evidence=Join-Path $out ('evidence/'+$mode);New-Item -ItemType Directory -Path $evidence|Out-Null
        $doctor=Controlled 'doctor' (Join-Path $runtime 'NewModCompiler.exe') $runtime @('doctor','--sdk-root',$sdk) (Join-Path $short 'doctor-unused') (Join-Path $evidence 'doctor')
        Need ((Get-Content -LiteralPath (Join-Path $evidence 'doctor/stdout.log') -Raw).Contains('CORE_ENVIRONMENT_OK')) 'Packaged doctor did not verify the actual runtime'
        $baselineDoctor=Controlled 'source-doctor' (Join-Path $sourceBin 'NewModCompiler.exe') $sourceBin @('doctor','--sdk-root',$sdk) (Join-Path $short 'doctor-unused') (Join-Path $evidence 'baseline-doctor')
        Need ((Get-Content -LiteralPath (Join-Path $evidence 'baseline-doctor/stdout.log') -Raw).Contains('CORE_ENVIRONMENT_OK')) 'Source doctor did not verify the actual runtime'
        $baseline=BuildOutputs $sourceBin $sdk $mod (Join-Path $short ($mode.Substring(0,1)+'b')) (Join-Path $evidence 'baseline')
        $deployed=BuildOutputs $runtime $sdk $mod (Join-Path $short ($mode.Substring(0,1)+'p')) (Join-Path $evidence 'deployed')
        Need ($baseline.outputs.Count -eq $deployed.outputs.Count) 'ZIP-deployed/source output counts differ'
        foreach($key in $baseline.outputs.Keys){
            Need ($deployed.outputs.ContainsKey($key) -and $baseline.outputs[$key].sha256 -ceq $deployed.outputs[$key].sha256 -and $baseline.outputs[$key].bytes -eq $deployed.outputs[$key].bytes) "ZIP-deployed/source output identity differs: $key"
            $left=[IO.File]::ReadAllBytes($baseline.outputs[$key].path);$right=[IO.File]::ReadAllBytes($deployed.outputs[$key].path)
            for($offset=0;$offset -lt $left.Length;$offset++){Need ($left[$offset] -eq $right[$offset]) "ZIP-deployed/source raw bytes differ: $key at $offset"}
        }
        AssertInventory $sourceBin $inventory;AssertInventory $runtime $inventory
        $state.configurations+=,@{configuration=$mode;succeeded=$true;candidateZip=$package;zipSha256=(Hash $package);runtimeFiles=$inventory;buildReceiptSha256=(Hash (Join-Path $buildRoot 'build.json'));linkRecipeSha256=(Hash (Join-Path $buildRoot 'link-inputs.json'));toolchain=@{compiler=$build.compiler;vcToolsVersion=$build.vcToolsVersion;windowsSdkVersion=$build.windowsSdkVersion};texturePlugin=$build.texturePlugin;doctor=$doctor;sourceDoctor=$baselineDoctor;baseline=$baseline;deployed=$deployed;rawOutputEquality=$true;runtimeUnchanged=$true}
    }
    if($VerifyBigDeployment){
        if(Test-Path -LiteralPath $p7Replay){Copy-Item -LiteralPath $p7Replay -Destination (Join-Path $deployedProduct 'p7-replay.json')}
        $bigEvidence=Join-Path $out 'evidence/big-deployment'
        & "$PSScriptRoot/TestCompilerBig.ps1" -CompilerDirectory $deployedProduct -OutputDirectory $bigEvidence -TimeoutSeconds $TimeoutSeconds -UsePackagedRuntime
        Need ($LASTEXITCODE -eq 0) 'ZIP-deployed base/APT/maps and original LoD/MakeBig acceptance failed'
        $big=Get-Content -LiteralPath (Join-Path $bigEvidence 'validation.json') -Raw|ConvertFrom-Json
        Need ($big.succeeded -eq $true -and $big.usePackagedRuntime -eq $true -and @($big.modes).Count -eq 2) 'ZIP BIG gate lacks complete packaged-runtime acceptance'
        foreach($configuration in $state.configurations){
            $rows=@($big.modes|Where-Object{$_.configuration -ceq $configuration.configuration})
            Need ($rows.Count -eq 1 -and $rows[0].passed -eq $true -and $rows[0].packagedRuntimeUnchanged -eq $true -and $rows[0].exeSha256 -ceq $configuration.runtimeFiles[0].sha256) 'ZIP BIG acceptance used a different production EXE'
            foreach($name in @('base-big','base-loose','base-apt-maps-big','base-apt-maps-loose','all-with-additional','base-with-neutral-repair','neutral-missing-reference','neutral-retry-after-reference-restored')){Need (@($rows[0].cases|Where-Object{$_.name -ceq $name -and $_.passed -eq $true}).Count -eq 1) "ZIP BIG acceptance omitted $name"}
            $additionalCase=@($rows[0].cases|Where-Object{$_.name -ceq 'all-with-additional'})[0]
            $additionalArguments=@($additionalCase.arguments)
            Need ($additionalArguments -ccontains '--include-additional=true') 'ZIP Additional acceptance did not explicitly enable Additional'
            $additionalComparison=@($rows[0].comparisons|Where-Object{$_.name -ceq 'additional-original-copy-makebig'})
            Need ($additionalComparison.Count -eq 1 -and $additionalComparison[0].comparison.succeeded -eq $true -and $additionalComparison[0].comparison.exactMemberNamesAndRawContents -eq $true) 'ZIP Additional lacks original CopyFolders/MakeBig raw comparison'
            $neutralCase=@($rows[0].cases|Where-Object{$_.name -ceq 'base-with-neutral-repair'})[0]
            $neutralArguments=@($neutralCase.arguments)
            Need ($neutralArguments -ccontains '--fix-neutral-assets=true') 'ZIP neutral repair acceptance did not explicitly enable repair'
            $neutralComparison=@($rows[0].comparisons|Where-Object{$_.name -ceq 'neutral-original-hashfix-resolver-lod-makebig'})
            Need ($neutralComparison.Count -eq 1 -and $neutralComparison[0].comparison.succeeded -eq $true -and $neutralComparison[0].comparison.exactMemberNamesAndRawContents -eq $true) 'ZIP neutral repair lacks original HashFix/Resolver/LoD/MakeBig raw comparison'
            AssertInventory (Join-Path $deployedProduct ($configuration.configuration+'/bin')) $configuration.runtimeFiles
            AssertInventory (Join-Path $CompilerDirectory ($configuration.configuration+'/bin')) $configuration.runtimeFiles
        }
        $state.bigDeployment=@{succeeded=$true;receiptSha256=(Hash (Join-Path $bigEvidence 'validation.json'));sourceCommit=$big.sourceCommit;scope=$big.scope;packagedRuntime=$true}
        $state.scope+='; independently extracted exact runtime additionally passed real base/APT/maps BIG builds, original LoD and MakeBig raw member differential, explicit SDK/MOD Additional against original CopyFolders and MakeBig, opt-in neutral hash repair against original HashFix/Resolver/LoD/MakeBig (no-insertion scope), CDATA and rollback/cancellation matrices'
    }
    Need ((Hash (Join-Path $mod 'Data/Mod.xml')) -ceq $state.probe.sha256 -and (Hash (Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config')) -ceq $state.probe.sdkConfigSha256) 'Release validation modified its MOD or SDK config inputs'
    foreach($schema in $state.probe.schemaInputs){Need ((Hash (Join-Path $sdk $schema.path)) -ceq $schema.sha256) 'Release validation modified an SDK schema input'}
    & python "$PSScriptRoot/ValidateAppProtocol.py" (Join-Path $out 'evidence') --source-commit $state.sourceCommit --output (Join-Path $out 'evidence/protocol.json')
    Need ($LASTEXITCODE -eq 0) 'Actual packaged build protocol schema validation failed'
    $state.protocolSha256=Hash (Join-Path $out 'evidence/protocol.json')
    foreach($configuration in $state.configurations){
        $stem='NewModCompiler-Win32-'+$configuration.configuration
        $zip=Join-Path $out ($stem+'.zip');Move-Item -LiteralPath $configuration.candidateZip -Destination $zip
        $manifest=[ordered]@{schemaVersion=1;status='verified-small-mod-deployment';platform='Win32';runtime='.NET Framework 4.8';configuration=$configuration.configuration;binarySourceCommit=$state.binarySourceCommit;packagingSourceCommit=$state.sourceCommit;runId=$state.runId;runAttempt=$state.runAttempt;acceptance=$state.acceptance;toolchain=$configuration.toolchain;buildReceiptSha256=$configuration.buildReceiptSha256;linkRecipeSha256=$configuration.linkRecipeSha256;files=$configuration.runtimeFiles;archive=@{name=($stem+'.zip');bytes=(Get-Item -LiteralPath $zip).Length;sha256=$configuration.zipSha256};texturePlugin=$configuration.texturePlugin;dependencies=@{textureImplementation='built-in-source';systemRequirements=@('Windows x86-compatible process','NET Framework 4.8','x86 VC runtime matching recorded production VCToolsVersion');limitations='Texture is compiled into the EXE; no legacy Texture DLL, VC80 or DirectX runtime is deployed; system runtime libraries are not redistributed'};validation=@{productionDoctor=$true;sourceRuntimeRawOutputEquality=$true;bigDeployment=$(if($VerifyBigDeployment){$state.bigDeployment}else{$null});schemaProtocolSha256=$state.protocolSha256;scope=$state.scope}}
        $manifest|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $out ($stem+'.manifest.json'))
        $configuration.zip=$zip;$null=$configuration.Remove('candidateZip')
    }
    $state.succeeded=$true;$state.status='verified-small-mod-deployment';$global:LASTEXITCODE=0
} catch {$state.error=$_.Exception.ToString();$state.status='failed';throw}
finally {
    $env:PATH=$oldPath
    $state|ConvertTo-Json -Depth 28|Set-Content -LiteralPath (Join-Path $out 'release.json')
    if($short){try{Remove-Item -LiteralPath $short -Recurse -Force}catch{Write-Warning "Release validation workspace retained: $short"}}
}
