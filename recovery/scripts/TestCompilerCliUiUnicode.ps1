#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerCliOutputs.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw 'Refusing to overwrite Unicode UI evidence'}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$work=(New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ('u'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
$oldPath=$env:PATH
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Need([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Inventory([string]$Directory){
    $map=@{};Get-ChildItem -LiteralPath $Directory -File -Recurse|ForEach-Object{$map[[IO.Path]::GetRelativePath($Directory,$_.FullName)]=Hash $_.FullName};return $map
}
function Unchanged([string]$Directory,$Expected){
    $actual=Inventory $Directory;Need ($actual.Count -eq $Expected.Count) 'Unicode probe input inventory count changed'
    foreach($key in $Expected.Keys){Need ($actual.ContainsKey($key) -and $actual[$key] -ceq $Expected[$key]) "Unicode probe input changed: $Directory/$key"}
}
function CompareBytes([string]$Left,[string]$Right){
    $a=[IO.File]::ReadAllBytes($Left);$b=[IO.File]::ReadAllBytes($Right)
    Need ($a.Length -eq $b.Length) 'ASCII/Unicode output lengths differ'
    for($i=0;$i -lt $a.Length;$i++){Need ($a[$i] -eq $b[$i]) "ASCII/Unicode original output byte differs at $i"}
}
$state=@{schemaVersion=1;succeeded=$false;testSourceCommit=(git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;modes=@();scope='Optional real production EXE probe: complete fixed original 106-file UI inputs, both SDK/MOD/runtime/output roots with Unicode spaces # %, single process and exact ASCII/Unicode output bytes; no original BAB Unicode or full SDK/G3 acceptance claim'}
try {
    $state.scriptSha256=Hash $PSCommandPath
    $state.outputOracleSha256=Hash "$PSScriptRoot/CompilerCliOutputs.ps1"
    $reusePath=Join-Path $CompilerDirectory 'reuse.json';$reuse=$null
    if(Test-Path -LiteralPath $reusePath){
        $reuse=Get-Content -LiteralPath $reusePath -Raw|ConvertFrom-Json
        Need ($reuse.prepared -eq $true -and $reuse.candidateValidated -eq $false -and $reuse.candidateCommit -ceq $state.testSourceCommit) 'Historical runtime reuse identity invalid'
        $state.runtimeReuse=$reuse
    }
    $control=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')
    Need ($LASTEXITCODE -eq 0) 'Could not build real single-process CLI controller'
    $controlBuild=Get-Content (Join-Path $out 'controller/build.json') -Raw|ConvertFrom-Json
    Need ($controlBuild.succeeded -and $controlBuild.sourceCommit -ceq $state.testSourceCommit -and $controlBuild.controllerSha256 -ceq (Hash $control)) 'CLI controller provenance invalid'
    $state.controller=$controlBuild
    $fixture=Join-Path $work 'original-ui'
    & python "$PSScriptRoot/PrepareCompilerCliUi.py" $fixture --entry all *> (Join-Path $out 'prepare.log')
    Need ($LASTEXITCODE -eq 0) 'Original complete UI preparation failed'
    $manifest=Get-Content (Join-Path $fixture 'fixture-manifest.json') -Raw|ConvertFrom-Json
    Need ($manifest.prepared -and !$manifest.compiled -and $manifest.xsdValidated -and @($manifest.files).Count -eq 106 -and @($manifest.textures).Count -eq 2) 'Complete original UI fixture receipt invalid'
    Copy-Item -LiteralPath (Join-Path $fixture 'fixture-manifest.json') -Destination (Join-Path $out 'fixture.json')
    $fixtureBefore=Inventory $fixture
    $env:PATH=(@($oldPath -split ';'|Where-Object{$_ -notmatch '[\\/]SDK[\\/]tools'}) -join ';')
    foreach($mode in @('Debug','Release')){
        $modeOut=(New-Item -ItemType Directory -Path (Join-Path $out $mode)).FullName
        $modeWork=(New-Item -ItemType Directory -Path (Join-Path $work $mode.Substring(0,1))).FullName
        $modeState=@{configuration=$mode;succeeded=$false;cases=@();comparisons=@()};$state.modes+=,$modeState
        $originalBuildPath=Join-Path $CompilerDirectory ($mode+'/build.json')
        if($reuse){$modeState.binarySourceCommit=$reuse.sourceCommit}
        elseif(Test-Path -LiteralPath $originalBuildPath){
            $build=Get-Content -LiteralPath $originalBuildPath -Raw|ConvertFrom-Json
            Need ($build.schemaVersion -eq 2 -and $build.backendIntegrated -eq $true -and $build.containsProbeOrTestSources -eq $false -and $build.exeSha256 -cmatch '^[0-9a-f]{64}$' -and $build.configuration -ceq $mode -and $build.sourceCommit -cmatch '^[0-9a-f]{40}$') 'Candidate build provenance invalid'
            $modeState.binarySourceCommit=$build.sourceCommit;$modeState.originalBuild=$build
        } else {throw 'Compiler input must include build.json or a strict reuse.json provenance receipt'}
        $previousRuntime=$null;$accepted=@{}
        foreach($variant in @('ascii','unicode')){
            $caseOut=(New-Item -ItemType Directory -Path (Join-Path $modeOut $variant)).FullName
            $caseRoot=(New-Item -ItemType Directory -Path (Join-Path $modeWork $(if($variant -ceq 'ascii'){'ascii'}else{'中文 空格 # %'}))).FullName
            $sdk=(New-Item -ItemType Directory -Path (Join-Path $caseRoot $(if($variant -ceq 'ascii'){'sdk'}else{'SDK 中文 # %'}))).FullName
            $mod=Join-Path $caseRoot $(if($variant -ceq 'ascii'){'mod'}else{'MOD 中文 # %'})
            $runtime=(New-Item -ItemType Directory -Path (Join-Path $caseRoot $(if($variant -ceq 'ascii'){'runtime'}else{'运行 中文 # %'}))).FullName
            $destination=(New-Item -ItemType Directory -Path (Join-Path $caseRoot $(if($variant -ceq 'ascii'){'out'}else{'产物 中文 # %'}))).FullName
            New-Item -ItemType Directory -Path (Join-Path $sdk 'tools'),(Join-Path $sdk 'Schemas')|Out-Null
            Copy-Item -LiteralPath (Join-Path $root 'SDK/Schemas/xsd') -Destination (Join-Path $sdk 'Schemas') -Recurse
            Copy-Item -LiteralPath (Join-Path $root 'SDK/tools/BinaryAssetBuilder.exe.config') -Destination (Join-Path $sdk 'tools')
            Copy-Item -LiteralPath $fixture -Destination $mod -Recurse
            if($previousRuntime){Copy-Item -Path (Join-Path $previousRuntime '*') -Destination $runtime}
            else {
                foreach($file in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){
                    $source=Join-Path $CompilerDirectory ($mode+'/bin/'+$file)
                    if(!$reuse -and $file -ceq 'NewModCompiler.exe'){Need ((Hash $source) -ceq $build.exeSha256) 'Compiler EXE hash differs from original build receipt'}
                    if($reuse){$expected=@($reuse.files|Where-Object{$_.path -ceq ($mode+'/bin/'+$file)});Need ($expected.Count -eq 1 -and $expected[0].sha256 -ceq (Hash $source)) 'Historical runtime hash differs'}
                    Copy-Item -LiteralPath $source -Destination $runtime
                }
                if($reuse){
                    & "$PSScriptRoot/PrepareLegacyTexture.ps1" -TargetExe (Join-Path $runtime 'NewModCompiler.exe') *> (Join-Path $caseOut 'texture-prepare.log')
                    Need ($LASTEXITCODE -eq 0) 'Historical EXE-bound Texture preparation failed'
                }
            }
            $exe=Join-Path $runtime 'NewModCompiler.exe'
            if($reuse){
                $textureDeployment=Get-Content (Join-Path $runtime 'BinaryAssetBuilder.TextureCompiler.dll.patch.json') -Raw|ConvertFrom-Json
                Need ($textureDeployment.targetExeSha256 -ceq (Hash $exe) -and $textureDeployment.patchedSha256 -ceq (Hash (Join-Path $runtime 'BinaryAssetBuilder.TextureCompiler.dll')) -and $textureDeployment.protectedPeAudit.status -ceq 'passed') 'Historical Unicode runtime Texture deployment does not bind its EXE'
                Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 5) 'Unexpected historical production runtime files'
            }else{
                Need ($build.texturePlugin.implementation -ceq 'built-in-source' -and @($build.texturePlugin.externalRuntimeFiles).Count -eq 0) 'Unicode build requires the source texture plugin'
                Need (@(Get-ChildItem -LiteralPath $runtime -Force).Count -eq 3) 'Unexpected production runtime files'
                $textureDeployment=@{implementation='built-in-source';assemblySha256=(Hash $exe);externalRuntimeFiles=@()}
            }
            $modBefore=Inventory $mod;$sdkBefore=Inventory $sdk;$runtimeBefore=Inventory $runtime
            $request=@{exe=$exe;cwd=$runtime;arguments=@('build','--sdk-root',$sdk,'--mod',$mod,'--out',$destination,'--package-format','loose','--package-content','apt');stdout=(Join-Path $caseOut 'stdout.log');stderr=(Join-Path $caseOut 'stderr.log');events=(Join-Path $destination 'build.events.jsonl');cancel=$false;timeoutSeconds=$TimeoutSeconds}
            $requestPath=Join-Path $caseOut 'request.json';$controlPath=Join-Path $caseOut 'control.json'
            $caseState=@{variant=$variant;succeeded=$false;sdkRoot=$sdk;modRoot=$mod;runtimeRoot=$runtime;outputRoot=$destination;exeSha256=(Hash $exe);textureDeployment=$textureDeployment;modInputs=$modBefore;sdkInputs=$sdkBefore;runtimeInputs=$runtimeBefore;artifacts=@();inlineTextures=@()};$modeState.cases+=,$caseState
            try {
                $request|ConvertTo-Json -Depth 5|Set-Content $requestPath
                & $control $requestPath $controlPath *> (Join-Path $caseOut 'controller.log')
                $controllerExit=$LASTEXITCODE;$audit=Get-Content -LiteralPath $controlPath -Raw|ConvertFrom-Json;$caseState.process=$audit
                Need ($controllerExit -eq 0 -and $audit.succeeded -and $audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0 -and $audit.processExitCode -eq 0) 'Actual UI CLI process/job/exit failed'
                $report=Get-Content (Join-Path $destination 'build-report.json') -Raw|ConvertFrom-Json
                $events=@(Get-Content (Join-Path $destination 'build.events.jsonl')|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
                Need ($report.exitCode -eq 0 -and $report.executionMode -ceq 'InProcess' -and $report.publicationStatus -ceq 'IsolatedStagingOnly' -and @($report.externalTools).Count -eq 0 -and @($report.plan.Tasks).Count -eq 3 -and @($report.artifacts).Count -eq 12) 'UI report plan/output contract differs'
                Need (@($events|Where-Object{$_.eventType -ceq 'BuildStarted'}).Count -eq 1 -and @($events|Where-Object{$_.eventType -ceq 'BuildFinished'}).Count -eq 1 -and $events[-1].eventType -ceq 'BuildFinished' -and $events[-1].payload.exitCode -eq 0) 'UI terminal events differ'
                $started=@($events|Where-Object{$_.eventType -ceq 'BuildStarted'})[0];Need ($started.payload.processId -eq $audit.processId) 'UI build PID differs from actual process'
                Need (@($events|Where-Object{$_.eventType -ceq 'StreamStarted'}).Count -eq 3 -and @($events|Where-Object{$_.eventType -ceq 'StreamCompleted'}).Count -eq 3) 'UI actual stream event counts differ'
                $sequence=0;foreach($event in $events){$sequence++;Need ($event.sequence -eq $sequence -and $event.buildId -ceq $report.buildId) 'UI event identity/sequence differs'}
                $map=@{};$buildPrefix=Join-Path $destination 'published'
                foreach($artifact in $report.artifacts){
                    $relative=[IO.Path]::GetRelativePath($buildPrefix,$artifact.Path)
                    Need (!$relative.StartsWith('..') -and ![IO.Path]::IsPathRooted($relative) -and !$map.ContainsKey($relative) -and (Get-Item -LiteralPath $artifact.Path).Length -eq $artifact.Size) 'UI artifact containment/identity differs'
                    $map[$relative]=$artifact.Path;$caseState.artifacts+=,@{relativePath=$relative;path=$artifact.Path;bytes=$artifact.Size;sha256=(Hash $artifact.Path);archive=(Join-Path $caseOut ('output/published/'+$relative))}
                }
                $customCount=0;$inlineCount=0
                foreach($task in $report.plan.Tasks){
                    $name=[IO.Path]::GetFileNameWithoutExtension($task.InputPath);Need ($name -cin @('TacticalHUDBuildQueuePage','TacticalHUDCommandListBox','TacticalHUDMainCommandBar')) 'Unexpected Unicode UI task identity';Need ($task.Content -ceq 'apt') 'Unexpected Unicode task group'
                    $textures=@($manifest.textures|Where-Object{$_.entry -ceq $name}|ForEach-Object{$_.qualifiedName})
                    $prefix='apt'+[IO.Path]::DirectorySeparatorChar+$name+[IO.Path]::DirectorySeparatorChar
                    $taskEntries=@($map.Keys|Where-Object{$_.StartsWith($prefix,[StringComparison]::Ordinal)})
                    $manifests=@($taskEntries|Where-Object{[IO.Path]::GetExtension($_) -ceq '.manifest'});Need ($manifests.Count -eq 1) 'UI task manifest missing'
                    foreach($extension in @('.manifest','.bin','.imp','.relo')){Need (@($taskEntries|Where-Object{[IO.Path]::GetExtension($_) -ceq $extension}).Count -eq 1) 'UI task must own each of four unique stream outputs'}
                    $inline=@(Test-CompilerInlineTexturePayload $map[$manifests[0]] $textures);$inlineCount+=$inline.Count;$caseState.inlineTextures+=@($inline)
                    $custom=@(Get-CompilerCustomDataOutputs $map[$manifests[0]] $textures);$customCount+=$custom.Count
                    Need ($taskEntries.Count -eq 4+$custom.Count) 'UI task output count differs from manifest allowlist'
                    foreach($item in $custom){Need (@($report.artifacts|Where-Object{$_.Path -ceq $item.path -and $_.Kind -ceq 'custom-data'}).Count -eq 1) 'UI Texture CustomData missing or mistyped in reported outputs'}
                }
                Need ($customCount -eq 0 -and $inlineCount -eq 2) 'UI must contain two verified inline Texture DDS payloads and no CustomData'
                $accepted[$variant]=$map;$caseState.succeeded=$true
            } catch {
                $caseState.error=$_.Exception.ToString()
                foreach($log in @('stdout.log','stderr.log','controller.log')){if(Test-Path -LiteralPath (Join-Path $caseOut $log)){Get-Content -LiteralPath (Join-Path $caseOut $log) -Tail 30|Out-Host}}
                throw
            } finally {
                Copy-Item -LiteralPath $destination -Destination (Join-Path $caseOut 'output') -Recurse
                try {Unchanged $mod $modBefore;Unchanged $sdk $sdkBefore;Unchanged $runtime $runtimeBefore;$caseState.inputsUnchanged=$true}
                catch {$caseState.succeeded=$false;$caseState.inputsUnchanged=$false;$caseState.inputVerificationError=$_.Exception.ToString();throw}
            }
            $previousRuntime=$runtime
        }
        Need ($accepted.ascii.Count -eq $accepted.unicode.Count) 'ASCII/Unicode artifact count differs'
        foreach($relative in $accepted.ascii.Keys){
            Need ($accepted.unicode.ContainsKey($relative)) 'Unicode artifact relative path differs'
            CompareBytes $accepted.ascii[$relative] $accepted.unicode[$relative]
            $modeState.comparisons+=,@{relativePath=$relative;asciiSha256=(Hash $accepted.ascii[$relative]);unicodeSha256=(Hash $accepted.unicode[$relative]);rawBytesIdentical=$true}
        }
        $modeState.succeeded=$true
    }
    Unchanged $fixture $fixtureBefore
    $state.succeeded=$true;$global:LASTEXITCODE=0
} catch {$state.error=$_.Exception.ToString();throw}
finally {
    $env:PATH=$oldPath
    $state|ConvertTo-Json -Depth 20|Set-Content (Join-Path $out 'validation.json')
    try {Remove-Item -LiteralPath $work -Recurse -Force}catch{Write-Warning "Unicode probe workspace cleanup failed: $_"}
}
