param([Parameter(Mandatory=$true)][string]$FoundationDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$BuildDirectory)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite product validation $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
if(!$BuildDirectory){$BuildDirectory=Join-Path $root ('recovery/artifacts/compiler-tests/'+[Guid]::NewGuid().ToString('N'))}
$buildRoot=[IO.Path]::GetFullPath($BuildDirectory)
$state=@{schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';scope='P4 clean unified production deployment and complete built-in route metadata; integrated backend environment rejection only; not component differential or successful MOD compilation';modes=@()}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $FoundationDirectory=(Resolve-Path $FoundationDirectory).Path
    $work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-test-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work|Out-Null
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $inspector=Join-Path $work 'CompilerDeployment.exe'
    & $csc /nologo /codepage:65001 /platform:x86 /r:System.Web.Extensions.dll "/out:$inspector" (Resolve-Path 'recovery/tests/CompilerDeployment.cs').Path *> "$out/inspector-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/inspector-build.log";throw 'Deployment metadata inspector compilation failed'}
    $routeInspector=Join-Path $work 'CompilerRoutes.exe'
    & $csc /nologo /codepage:65001 /platform:x86 /r:System.Web.Extensions.dll "/out:$routeInspector" (Resolve-Path 'recovery/tests/CompilerRoutes.cs').Path *> "$out/routes-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/routes-build.log";throw 'Built-in route inspector compilation failed'}
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>'|Set-Content "$routeInspector.config"
    foreach($mode in @('Debug','Release')) {
        $modeOut=Join-Path $out $mode;New-Item -ItemType Directory -Force $modeOut|Out-Null
        $build=Join-Path $buildRoot $mode
        try {& "$PSScriptRoot/BuildCompiler.ps1" -Configuration $mode -FoundationDirectory $FoundationDirectory -OutputDirectory $build|Out-Host}
        finally {Get-ChildItem $build -File -ErrorAction SilentlyContinue|Where-Object{$_.Extension -in @('.log','.json')}|Copy-Item -Destination $modeOut}
        & "$PSScriptRoot/BuildCompilerValidationHost.ps1" -ProductBuildDirectory $build -OutputDirectory "$build/validation"|Out-Host
        Copy-Item "$build/validation/build.json" "$modeOut/validation-host-build.json"
        Copy-Item "$build/link-inputs.json" "$modeOut/link-inputs.json"
        $routeClean=Join-Path $work "$mode/route-validation"
        $validationRun=Initialize-CompilerValidationRun $build $routeClean $mode $state.sourceCommit
        $clean=Join-Path $work "$mode/clean"
        New-Item -ItemType Directory -Force $clean|Out-Null
        Copy-Item "$build/bin/*" $clean
        # An isolated copy must not resolve intermediate modules through their
        # original absolute paths; make all native/managed objects unavailable.
        if(!(Test-Path "$build/obj")){throw 'Missing product object directory to quarantine'}
        Move-Item "$build/obj" (Join-Path $work "$mode/quarantined-obj")
        $allowed=@('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')
        if(@(Get-ChildItem $clean -Recurse|Where-Object{$_.PSIsContainer -or $_.Name -notin $allowed}).Count -ne 0){throw 'Unexpected file or subdirectory in clean production directory'}
        foreach($required in $allowed){if(!(Test-Path (Join-Path $clean $required) -PathType Leaf)){throw "Missing required production file: $required"}}
        $exe=Join-Path $clean 'NewModCompiler.exe'
        & $inspector $exe (Join-Path $FoundationDirectory "$mode/Foundation.types.txt") (Join-Path $FoundationDirectory "$mode/resources.json") *> "$modeOut/metadata-runtime.log"
        if($LASTEXITCODE -ne 0){Get-Content "$modeOut/metadata-runtime.log";throw 'Clean product metadata validation failed'}
        Push-Location $routeClean
        try { & $validationRun.exe $routeInspector $validationRun.exe (Join-Path $root 'recovery/catalog/XmlSdkTypes.json') *> "$modeOut/routes-runtime.log"; $routeCode=$LASTEXITCODE }
        finally { Pop-Location }
        if($routeCode -ne 0){Get-Content "$modeOut/routes-runtime.log";throw 'Clean production built-in route validation failed'}
        & $exe --help *> "$modeOut/help.log"
        if($LASTEXITCODE -ne 0 -or !(Select-String -Path "$modeOut/help.log" -SimpleMatch '--mod' -Quiet)){throw 'Clean product help failed'}
        $fixture=Join-Path $work "$mode/MOD 中文 fixture"
        $sdk=Join-Path $work "$mode/SDK fixture"
        New-Item -ItemType Directory -Force "$fixture/Data/AptUI","$fixture/Data/AdditionalMaps",$sdk|Out-Null
        '<Mod/>'|Set-Content "$fixture/Data/Mod.xml"
        '<Apt/>'|Set-Content "$fixture/Data/AptUI/One.xml"
        '<Map/>'|Set-Content "$fixture/Data/AdditionalMaps/MapMetaData_One.xml"
        & $exe doctor --sdk-root $sdk *> "$modeOut/doctor.log"
        if($LASTEXITCODE -ne 3 -or !(Select-String -Path "$modeOut/doctor.log" -SimpleMatch 'CORE_CONFIG_MISSING' -Quiet)){throw 'Doctor must reject missing SDK configuration with exit 3'}
        foreach($command in @('plan','build')) {
            $destination=Join-Path $work "$mode/$command-output"
            & $exe $command --mod $fixture --sdk-root $sdk --out $destination --package-format loose --package-content base,apt,maps *> "$modeOut/$command.log"
            $code=$LASTEXITCODE;$expected=if($command -eq 'plan'){0}else{3}
            if($code -ne $expected){Get-Content "$modeOut/$command.log";throw "$mode $command exit $code differs from $expected"}
            $report=Get-Content "$destination/build-report.json" -Raw|ConvertFrom-Json
            $events=@(Get-Content "$destination/build.events.jsonl"|ForEach-Object{$_|ConvertFrom-Json})
            if(@($report.plan.Tasks).Count -ne 3){throw 'Clean production CLI plan must contain all three selected content groups'}
            if($report.exitCode -ne $code -or @($events|Where-Object{$_.eventType -eq 'BuildFinished'}).Count -ne 1){throw 'Production event/report terminal status differs'}
            if($command -eq 'build' -and @($report.diagnostics|Where-Object{$_.code -eq 'CORE_CONFIG_MISSING'}).Count -ne 1){throw 'Build did not explicitly reject missing SDK configuration'}
            if(@($report.artifacts).Count -ne 0){throw 'Invalid SDK environment claimed compiled assets'}
            Copy-Item "$destination/build-report.json" "$modeOut/$command-report.json"
            Copy-Item "$destination/build.events.jsonl" "$modeOut/$command-events.jsonl"
        }
        Complete-CompilerValidationRun $validationRun
        $state.modes+=@{validationHostSha256=$validationRun.validationHostSha256;componentExecution='test-only host using identical production inputs';configuration=$mode;passed=$true;exeSha256=(Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant();runtimeFiles=$allowed;typeInventoryVerified=$true;exactResourcesVerified=$true;productContainsTests=$false;moduleInputsQuarantined=$true;builtinRouteCount=299;builtinPluginCount=7;routeMetadataVerified=$true;componentDifferentialPassed=$false;planExit=0;doctorExit=3;buildExit=3;backendIntegrated=$true;fakeSdkRejected=$true;modCompilationValidated=$false}
        Write-Host "PASS production $mode clean directory: metadata/types/resources, all built-in routes/rejections, help, plan/events/report, specific missing-SDK-configuration rejection with integrated backend"
    }
    $state.status='passed'
    # Expected invalid-SDK-environment probes returned 3. The harness itself passed.
    $global:LASTEXITCODE=0
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {$state|ConvertTo-Json -Depth 8|Set-Content "$out/validation.json";Pop-Location}


