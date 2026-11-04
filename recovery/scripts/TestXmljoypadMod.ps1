[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
$out=(New-Item -ItemType Directory $OutputDirectory).FullName
$work=(New-Item -ItemType Directory (Join-Path $env:RUNNER_TEMP ('joypad-mod-'+[Guid]::NewGuid().ToString('N')))).FullName
$state=@{succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;scope='Small self-contained Joypad MOD only: all11 independent roots and all29 schema Data choices; original BAB four-stream bytes; no performance or full external MOD';configurations=@();status='started'}
function Hash([string]$Path){(Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
Push-Location $root
try{
    python recovery/scripts/PrepareJoypadFixtures.py --output "$work/fixtures"
    if($LASTEXITCODE -ne 0){throw 'Joypad MOD fixture preparation failed'}
    Copy-Item "$work/fixtures/fixtures.json" "$out/fixtures.json"
    Copy-Item "$work/fixtures/mod/Data/Mod.xml" "$out/Mod.xml"
    $mod=(Resolve-Path "$work/fixtures/mod").Path
    $sdk=(Resolve-Path SDK).Path
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $routeInspector=Join-Path $work 'CompilerRoutes.exe'
    & $csc /nologo /codepage:65001 /platform:x86 /r:System.Web.Extensions.dll "/out:$routeInspector" (Resolve-Path 'recovery/tests/CompilerRoutes.cs').Path *> "$out/routes-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/routes-build.log";throw 'Production route inspector compilation failed'}
    foreach($mode in @('Debug','Release')){
        $evidence=(New-Item -ItemType Directory "$out/$mode").FullName
        $product=Join-Path $CompilerDirectory $mode
        & "$PSScriptRoot/BuildCompilerValidationHost.ps1" -ProductBuildDirectory $product -OutputDirectory "$product/validation"|Out-Host
        $routeClean=Join-Path $work "$mode-routes"
        $validationRun=Initialize-CompilerValidationRun $product $routeClean $mode $state.sourceCommit
        Push-Location $routeClean
        try{& $validationRun.exe $routeInspector $validationRun.exe (Join-Path $root 'recovery/catalog/XmlSdkTypes.json') *> "$evidence/routes-runtime.log";$routeCode=$LASTEXITCODE}
        finally{Pop-Location}
        if($routeCode -ne 0){Get-Content "$evidence/routes-runtime.log";throw "$mode production built-in route validation failed"}
        Complete-CompilerValidationRun $validationRun
        $runtime=(New-Item -ItemType Directory "$work/$mode-runtime").FullName
        foreach($file in @('NewModCompiler.exe','NewModCompiler.exe.config','antlr.runtime.dll')){Copy-Item "$CompilerDirectory/$mode/bin/$file" $runtime}
        $exe=Join-Path $runtime 'NewModCompiler.exe'
        $destination=Join-Path $work "$mode-output"
        $arguments=@('build','--sdk-root',$sdk,'--mod',$mod,'--out',$destination,'--package-format','loose','--package-content','base','--include-additional=false','--jobs','1','/bc:false','/sc:false')
        & $exe @arguments *> "$evidence/cli.log"
        if($LASTEXITCODE -ne 0){Get-Content "$evidence/cli.log" -Tail 65;throw "$mode small Joypad MOD compilation failed"}
        $report=Get-Content "$destination/build-report.json" -Raw|ConvertFrom-Json
        if($report.status -cne 'success' -and $report.status -cne 'succeeded'){throw "$mode CLI report failed: $($report.status)"}
        if(@($report.plan.Tasks).Count -ne 1 -or @($report.artifacts).Count -ne 4){throw "$mode small MOD must emit one exact four-file stream"}
        $outputs=@{}
        foreach($artifact in $report.artifacts){$outputs[[IO.Path]::GetExtension($artifact.Path).Substring(1)]=$artifact.Path}
        foreach($extension in @('manifest','bin','imp','relo')){if(!$outputs[$extension]){throw "$mode missing .$extension"}}
        $manifest=[IO.File]::ReadAllBytes($outputs.manifest)
        $metadata=Get-Content "$work/fixtures/fixtures.json" -Raw|ConvertFrom-Json
        if([BitConverter]::ToUInt32($manifest,12) -ne $metadata.sdkAssets){throw "$mode linked asset count differs from complete small fixture"}
        $case=@{name='mod-a';modRoot=$mod;inputPath=(Join-Path $mod 'Data/Mod.xml');sdkRoot=$sdk;originalToolsRoot=(Join-Path $sdk 'tools');stagingDirectory=[IO.Path]::GetDirectoryName($outputs.manifest);outputs=$outputs;scope='All 11 Joypad roots and 29 schema Data choices with four support assets; exact original tool four streams'}
        @{succeeded=$true;sourceCommit=$state.sourceCommit;runId=$state.runId;baselineCases=@($case);scope=$state.scope}|ConvertTo-Json -Depth 8|Set-Content "$evidence/fixture-result.json"
        & "$PSScriptRoot/TestCompilerBackendBaseline.ps1" -FixtureResult "$evidence/fixture-result.json" -OutputDirectory "$evidence/original-baseline" -ExpectedCaseCount 1
        $baseline=Get-Content "$evidence/original-baseline/validation.json" -Raw|ConvertFrom-Json
        if(!$baseline.succeeded){throw "$mode small Joypad original four-stream comparison failed"}
        Copy-Item "$destination/build-report.json" "$evidence/build-report.json"
        Copy-Item "$destination/build.events.jsonl" "$evidence/build.events.jsonl"
        $state.configurations+=@{configuration=$mode;passed=$true;exeSha256=(Hash $exe);validationHostSha256=$validationRun.validationHostSha256;productionRoutesVerified=$true;assets=$metadata.sdkAssets;rootNames=$metadata.rootNames;schemaDataChoices=$metadata.sdkChoices;originalFourStreamsByteIdentical=$true}
    }
    $state.succeeded=$true;$state.status='passed'
}catch{$state.status='failed';$state.error=$_.ToString();throw}
finally{$state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json" -Encoding utf8;Pop-Location}
