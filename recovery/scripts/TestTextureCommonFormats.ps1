[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(15,180)][int]$TimeoutSeconds=90
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
. "$PSScriptRoot/CompilerCacheLayout.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite common texture evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$work=Join-Path ([IO.Path]::GetTempPath()) ('tc'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$state=@{schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';configurations=@();productionCli=@();failures=@();scope='Common formats through production plugin objects and real MOD CLI; independent DDS/BC decode, Core schema/file preprocessing, cache invalidation, missing-input retry; no game runtime claim'}
function Invoke-CommonTool([string]$Command,[string[]]$Arguments,[string]$Directory,[string]$Log,[bool]$CollectFailure=$false){
    Push-Location $Directory
    try{& $Command @Arguments *> $Log;$code=$LASTEXITCODE}finally{Pop-Location}
    if($code -ne 0){Get-Content $Log -Tail 80|Out-Host;if(!$CollectFailure){throw "Texture common-format tool failed: $Command (exit $code)"}}
    if($CollectFailure){return $code}
}
function Record-TextureFailure([string]$Configuration,[string]$Scope,[string]$ErrorText){
    $script:state.failures+=@{configuration=$Configuration;scope=$Scope;error=$ErrorText}
    Write-Host "FAIL common texture $Configuration/$Scope`: $ErrorText"
}
function Need([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Equal-TextureOutputs($Left,$Right){
    Need ($Left.Count -eq $Right.Count) 'Common-format CLI output file counts differ'
    foreach($name in $Left.Keys){Need ($Right.ContainsKey($name) -and $Left[$name] -ceq $Right[$name]) "Common-format CLI raw output bytes differ: $name"}
}
function Run-TextureCli([string]$Name,[string]$Destination,[bool]$Cache=$true,[int]$Expected=0,[string]$Jobs='1'){
    $case=(New-Item -ItemType Directory -Path (Join-Path $script:cliEvidence $Name)).FullName
    New-Item -ItemType Directory -Path $Destination -Force|Out-Null
    # Distinct explicit namespaces retain the cold jobs1/jobs4/auto oracles;
    # changing --out alone now intentionally shares a MOD's SDK cache.
    $namespace=Join-Path (Get-CompilerTestCacheRoot $script:cliSdk $script:cliMod) ('tests/'+[IO.Path]::GetFileName($Destination))
    $arguments=@('build','--mod',$script:cliMod,'--sdk-root',$script:cliSdk,'--out',$Destination,'--jobs',$Jobs,'--package-format','loose','--package-content','base','--performance','detailed',('--sc='+$Cache.ToString().ToLowerInvariant()),('--bc='+$Cache.ToString().ToLowerInvariant()),'--scd',(Join-Path $namespace 'session'),'--bcd',(Join-Path $namespace 'build-cache'),'--iod',(Join-Path $namespace 'intermediate'))
    $events=Join-Path $Destination 'build.events.jsonl';$report=Join-Path $Destination 'build-report.json'
    @{exe=$script:cliExe;cwd=$script:cliRuntime;arguments=$arguments;stdout=(Join-Path $case 'stdout.log');stderr=(Join-Path $case 'stderr.log');events=$events;cancel=$false;timeoutSeconds=$TimeoutSeconds}|ConvertTo-Json -Depth 6|Set-Content "$case/request.json"
    & $script:cliControl "$case/request.json" "$case/control.json" *> "$case/controller.log"
    $exit=$LASTEXITCODE;$audit=Get-Content "$case/control.json" -Raw|ConvertFrom-Json
    $entry=@{name=$Name;arguments=$arguments;expectedExit=$Expected;passed=$false;controller=$audit};$script:cliMode.cases+=,$entry
    try{
        Need ($exit -eq 0 -and $audit.succeeded -and $audit.processExitCode -eq $Expected) "Common-format production CLI failed: $Name"
        Need ($audit.singleProcessJobEnforced -and $audit.singleProcessJobLimit -eq 1 -and @($audit.descendants).Count -eq 0) 'Common-format CLI created a child process'
        $body=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        Need ($body.exitCode -eq $Expected) 'Common-format CLI report/process exit differs'
        $rows=@(Get-Content -LiteralPath $events|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
        Need (@($rows|Where-Object{$_.eventType -ceq 'BuildFinished'}).Count -eq 1 -and $rows[-1].eventType -ceq 'BuildFinished') 'Common-format CLI terminal event differs'
        $files=@{};$published=Join-Path $Destination 'published'
        if($Expected -eq 0){
            Need ($body.publicationStatus -ceq 'PublishedDirectory' -and @($body.artifacts).Count -eq 5 -and @($body.artifacts|Where-Object{$_.Kind -ceq 'custom-data'}).Count -eq 1) 'Common-format CLI must publish one four-file stream and one OnDemand CDATA'
            foreach($file in Get-ChildItem -LiteralPath $published -File -Recurse){$files[[IO.Path]::GetRelativePath($published,$file.FullName)]=Hash $file.FullName}
            Need ($files.Count -eq 5) 'Unexpected common-format CLI published file'
            $manifest=@(Get-ChildItem -LiteralPath $published -Filter *.manifest -File -Recurse)
            Need ($manifest.Count -eq 1 -and [BitConverter]::ToUInt32([IO.File]::ReadAllBytes($manifest[0].FullName),12) -eq 7) 'Common-format CLI omitted declared texture assets'
        }else{Need (@($body.artifacts).Count -eq 0) 'Missing common-format source accepted artifacts'}
        $entry.passed=$true;$entry.cache=$body.cache;$entry.outputSha256=$files;$entry.buildId=$body.buildId
        return @{files=$files;report=$body;published=$published}
    }catch{
        foreach($log in @('stdout.log','stderr.log','controller.log')){Get-Content -LiteralPath (Join-Path $case $log) -Tail 35 -ErrorAction SilentlyContinue|Out-Host};throw
    }finally{
        if(Test-Path -LiteralPath $report){Copy-Item -LiteralPath $report -Destination "$case/build-report.json"}
        if(Test-Path -LiteralPath $events){Copy-Item -LiteralPath $events -Destination "$case/build.events.jsonl"}
    }
}
Push-Location $root
try{
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $source=(Resolve-Path 'recovery/tests/TextureCommonFormats.cs').Path
    $generator=(Resolve-Path 'recovery/scripts/GenerateTextureFixtures.py').Path
    $state.testSources=@(@($source,$generator)|ForEach-Object{@{path=[IO.Path]::GetRelativePath($root,$_).Replace('\','/');sha256=(Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
    New-Item -ItemType Directory -Force $work|Out-Null
    $fixtures=Join-Path $work '中文纹理 fixtures'
    Invoke-CommonTool 'python' @($generator,$fixtures) $root "$out/generate.log"
    $state.fixtureInputs=@(Get-ChildItem $fixtures -File|Sort-Object Name|ForEach-Object{@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
    try{$script:cliControl=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory (Join-Path $out 'controller')}
    catch{Record-TextureFailure 'shared' 'cli-controller' $_.Exception.ToString()}
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    foreach($mode in @('Debug','Release')){
        $evidence=(New-Item -ItemType Directory "$out/$mode").FullName
        $component=@{configuration=$mode;passed=$false};$state.configurations+=,$component
        $run=$null
        try{
        $clean=Join-Path $work "$mode/clean"
        $run=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
        $fixture=Join-Path $clean 'TextureCommonFormats.exe'
        Invoke-CommonTool $csc @('/nologo','/codepage:65001','/platform:x86','/r:System.Core.dll','/r:System.Xml.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',"/r:$($run.exe)","/out:$fixture",$source) $root "$evidence/fixture-build.log"
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll','TextureCommonFormats.exe')
        if(@(Get-ChildItem -LiteralPath $clean -Force|Where-Object{$_.PSIsContainer -or $_.Name -notin $allowed}).Count -ne 0){throw 'Unexpected common-format validation deployment entry'}
        $componentExit=Invoke-CommonTool $run.exe @($fixture,$fixtures,"$evidence/results.json") $clean "$evidence/runtime.log" $true
        $results=Get-Content "$evidence/results.json" -Raw|ConvertFrom-Json
        $component.caseCount=@($results.cases).Count;$component.assertions=$results.assertions;$component.resultSha256=Hash "$evidence/results.json"
        $component.failures=@($results.failures);$component.processExit=$componentExit
        $component.exeSha256=$run.productExeSha256;$component.validationHostSha256=$run.validationHostSha256;$component.sameProductionObjects=$true;$component.legacyTextureDllPresent=$false
        if($componentExit -ne 0 -or $results.status -cne 'passed' -or @($results.cases).Count -ne 31){throw 'Common-format component matrix failed or incomplete; see results.json failures array'}
        $component.passed=$true
        }catch{$component.error=$_.Exception.ToString();Record-TextureFailure $mode 'component' $component.error}
        finally{
            if($run){try{Complete-CompilerValidationRun $run}catch{$component.passed=$false;Record-TextureFailure $mode 'component-runtime-identity' $_.Exception.ToString()}}
        }
        $script:cliMode=@{configuration=$mode;passed=$false;cases=@()};$state.productionCli+=,$cliMode
        try{
        $script:cliEvidence=(New-Item -ItemType Directory "$evidence/cli").FullName
        $modeWork=(New-Item -ItemType Directory "$work/$mode/production").FullName
        $script:cliRuntime=(New-Item -ItemType Directory "$modeWork/runtime").FullName
        foreach($name in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){Copy-Item -LiteralPath (Join-Path $CompilerDirectory "$mode/bin/$name") -Destination $cliRuntime}
        $script:cliExe=Join-Path $cliRuntime 'NewModCompiler.exe'
        $product=Get-Content -LiteralPath (Join-Path $CompilerDirectory "$mode/build.json") -Raw|ConvertFrom-Json
        Need ($product.sourceCommit -ceq $state.sourceCommit -and $product.configuration -ceq $mode -and (Hash $cliExe) -ceq $product.exeSha256 -and @(Get-ChildItem -LiteralPath $cliRuntime -Force).Count -eq 3) 'Common-format production runtime differs from built product'
        $cliMode.exeSha256=Hash $cliExe
        $script:cliSdk=(New-Item -ItemType Directory "$modeWork/sdk").FullName
        New-Item -ItemType Directory "$cliSdk/tools","$cliSdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$cliSdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$cliSdk/tools"
        $script:cliMod=(New-Item -ItemType Directory "$modeWork/MOD 中文纹理").FullName
        New-Item -ItemType Directory "$cliMod/Data"|Out-Null
        foreach($name in @('opaque.png','opaque.tga','opaque.bmp','alpha.png','solid.jpg','preserve.dds')){Copy-Item -LiteralPath (Join-Path $fixtures $name) -Destination "$cliMod/Data/$name"}
        Copy-Item -LiteralPath "$fixtures/opaque.png" -Destination "$cliMod/Data/on-demand.png"
        $xml=@'
<AssetDeclaration xmlns="uri:ea.com:eala:asset">
  <Texture id="PngDefault" File="opaque.png"/>
  <Texture id="TgaRaw" File="opaque.tga" OutputFormat="A8R8G8B8" GenerateMipMaps="false"/>
  <Texture id="BmpDxt3" File="opaque.bmp" OutputFormat="DXT3"/>
  <Texture id="JpegDxt5" File="solid.jpg" OutputFormat="DXT5"/>
  <Texture id="AlphaDxt5" File="alpha.png" OutputFormat="DXT5"/>
  <Texture id="DdsPreserve" File="preserve.dds"/>
  <OnDemandTexture id="OnDemandPng" File="on-demand.png" OutputFormat="A8R8G8B8" GenerateMipMaps="false"/>
</AssetDeclaration>
'@
        [IO.File]::WriteAllText("$cliMod/Data/Mod.xml",$xml,[Text.UTF8Encoding]::new($false))
        $cached=Join-Path $modeWork 'cached';$fresh=Join-Path $modeWork 'fresh'
        $cold=Run-TextureCli 'cold' $cached
        Need ($cold.report.cache.instanceCompile -eq 7) 'Cold common-format build did not compile all seven declared assets'
        $ownerThreads=@($cold.report.cache.parallel.backendIntervals.threadId|Select-Object -Unique)
        Need ($ownerThreads.Count -eq 1) 'Common-format jobs1 owner-thread baseline differs'
        foreach($jobs in @('4','auto')){
            $parallelCold=Run-TextureCli ('cold-jobs'+$jobs) (Join-Path $modeWork ('cold-jobs'+$jobs)) $true 0 $jobs
            Equal-TextureOutputs $cold.files $parallelCold.files
            $metrics=$parallelCold.report.cache.parallel
            Need ($parallelCold.report.cache.instanceCompile -eq 7) 'Parallel cold common-format build omitted a texture'
            if($metrics.minimumEffectiveJobs -gt 1){
                $intervals=@($metrics.backendIntervals|Where-Object{$_.typeName -in @('Texture','OnDemandTexture')})
                Need ($intervals.Count -eq 7 -and @($intervals|Where-Object{$_.threadId -in $ownerThreads}).Count -eq 0) 'Admitted common-format texture calls did not execute on workers'
            }
        }
        $warm=Run-TextureCli 'warm-jobs-switch' $cached $true 0 '4';Equal-TextureOutputs $cold.files $warm.files
        Need ($warm.report.cache.instanceCompile -eq 0 -and $warm.report.cache.documentReuse -gt 0) 'Warm common-format build did not reuse compiled assets'
        $image="$cliMod/Data/opaque.tga";$stamp=[IO.File]::GetLastWriteTimeUtc($image);$before=[IO.File]::ReadAllBytes($image);$before[18]=$before[18] -bxor 127
        [IO.File]::WriteAllBytes($image,$before);[IO.File]::SetLastWriteTimeUtc($image,$stamp)
        $changed=Run-TextureCli 'one-image-mutated' $cached $true 0 '4'
        Need ($changed.report.cache.instanceCompile -eq 1) 'Single image mutation must compile only its dependent texture'
        $changedMetrics=$changed.report.cache.parallel
        if($changedMetrics.minimumEffectiveJobs -gt 1){
            $intervals=@($changedMetrics.backendIntervals)
            Need ($intervals.Count -eq 1 -and $intervals[0].typeName -ceq 'Texture' -and $intervals[0].threadId -notin $ownerThreads) 'Single changed texture did not compile on a worker'
        }
        Need (@($warm.files.Keys|Where-Object{$warm.files[$_] -cne $changed.files[$_]}).Count -gt 0) 'Image mutation did not change any final output'
        $plain=Run-TextureCli 'fresh-no-cache' $fresh $false;Equal-TextureOutputs $changed.files $plain.files
        Need ($plain.report.cache.instanceCompile -eq 7) 'Independent no-cache build did not compile every texture'
        Remove-Item -LiteralPath $image
        $null=Run-TextureCli 'missing-image' $cached $true 4 '4'
        [IO.File]::WriteAllBytes($image,$before)
        $retry=Run-TextureCli 'restored-retry' $cached $true 0 'auto';Equal-TextureOutputs $changed.files $retry.files
        $cliMode.passed=$true;$cliMode.schemaDefaultsExercised=$true;$cliMode.nonAsciiModPath=$true;$cliMode.onlyDependentTextureRecompiled=$true;$cliMode.failureRetryPassed=$true
        }catch{$cliMode.error=$_.Exception.ToString();Record-TextureFailure $mode 'production-cli' $cliMode.error}
    }
    if(@($state.configurations|Where-Object{$_.passed}).Count -eq 2 -and (Hash "$out/Debug/results.json") -cne (Hash "$out/Release/results.json")){Record-TextureFailure 'Debug/Release' 'component-output-equality' 'Debug/Release common-format outputs differ'}
    Need ($state.failures.Count -eq 0) "Common texture validation failed in $($state.failures.Count) scopes; see failures and per-mode results"
    $state.status='passed';$global:LASTEXITCODE=0
    Write-Host 'PASS common texture formats Debug/Release: independent DDS decode plus real seven-asset CLI defaults/cold/warm/single-image invalidation/no-cache equality/missing-image retry; no original texture DLL.'
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json" -Encoding utf8;Pop-Location}
