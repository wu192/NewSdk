[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(30,900)][int]$FixtureTimeoutSeconds=180,
    [string[]]$FixtureNames=@()
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite Core lifecycle evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Force $OutputDirectory).FullName
$work=(New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) ('clr-'+[Guid]::NewGuid().ToString('N').Substring(0,12)))).FullName
$oldPath=$env:PATH
$state=[ordered]@{
    schemaVersion=1;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT
    succeeded=$false;status='started';modes=@();sdkValidated=$false;g3Validated=$false;coldProcessValidated=$false;longPathsValidated=$false
    scope='P5 targeted real Core/backend/application pipeline and resource reuse in production-object validation fixtures using small Armor inputs; not production build CLI, APT business coverage, cold-process equivalence, complete MOD/SDK or G3 acceptance'
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Invoke-LifecycleFixture([string]$Executable,[string[]]$Arguments,[string]$Directory,[string]$Log,[int]$Timeout) {
    $info=[Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory=$Directory;$info.UseShellExecute=$false
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    $stdout=$null;$stderr=$null
    try {
        if(!$process.Start()){throw 'Unable to start Core lifecycle fixture'}
        $processId=$process.Id
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $timedOut=!$process.WaitForExit($Timeout*1000)
        if($timedOut){$process.Kill($true);$process.WaitForExit()}
        [IO.File]::WriteAllText($Log,($stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()))
        if($timedOut -or $process.ExitCode -ne 0){
            Get-Content $Log -Tail 100|Out-Host
            throw "Core lifecycle fixture failed: timeout=$timedOut exit=$($process.ExitCode)"
        }
        return $processId
    } finally {
        if($stdout -and !$process.HasExited){$process.Kill($true);$process.WaitForExit()}
        $process.Dispose()
    }
}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    $state.binarySourceCommit=$state.sourceCommit
    $state.testSourceCommit=if($env:COMPILER_TEST_SOURCE_COMMIT){$env:COMPILER_TEST_SOURCE_COMMIT}else{$state.sourceCommit}
    $state.workRoot=$work
    if(!$state.runId){$state.runId='local-'+[Guid]::NewGuid().ToString('N')}
    $state.runnerSha256=Hash $PSCommandPath
    $sdk=(Resolve-Path 'SDK').Path
    $schemas=(Resolve-Path 'SDK/Schemas/xsd').Path
    $state.schemaInputs=@(Get-ChildItem -LiteralPath $schemas -Recurse -File|Sort-Object FullName|ForEach-Object{
        @{path=[IO.Path]::GetRelativePath($sdk,$_.FullName).Replace('\','/');sha256=(Hash $_.FullName)}
    })
    if($state.schemaInputs.Count -eq 0){throw 'Real SDK schema input inventory is empty'}
    $fixtureFailures=@()
    $fixtures=@(
        @{name='CompilerPreparedBackend';marker='PASS PreparedBackendWork:';timeoutSeconds=300},
        @{name='CompilerXmlBackendBatch';marker='PASS XmlBackendBatch:';timeoutSeconds=300},
        @{name='CompilerCacheContent';marker='PASS CacheContent:'},
        @{name='CompilerStableSort';marker='PASS StableSort:'},
        @{name='CompilerInputRoots';marker='PASS InputRoots:'},
        @{name='CompilerCorePaths';marker='PASS CorePaths:'},
        @{name='CompilerUnicodeSettings';marker='PASS UnicodeSettings:'},
        @{name='CompilerCoreLifecycle';marker='PASS CoreLifecycle:'},
        @{name='CompilerResourceLifecycle';marker='PASS resource lifecycle'},
        @{name='CompilerStateLifecycle';marker='PASS Hash/Tracer state lifecycle'},
        @{name='CompilerBackendLifecycle';marker='PASS BackendLifecycle:'},
        @{name='CompilerApplicationBuild';marker='PASS ApplicationBuild:';timeoutSeconds=600}
    )
    $state.allFixtureNames=@($fixtures|ForEach-Object{$_.name})
    $state.fixtureSelection='all'
    if($FixtureNames.Count -gt 0){
        if(@($FixtureNames|Select-Object -Unique).Count -ne $FixtureNames.Count){throw 'Duplicate requested lifecycle fixture'}
        foreach($name in $FixtureNames){if($state.allFixtureNames -cnotcontains $name){throw "Unknown requested lifecycle fixture: $name"}}
        $fixtures=@($fixtures|Where-Object{$_.name -cin $FixtureNames})
        $state.fixtureSelection='selected'
        $state.scope+='; selected fixtures only: '+($FixtureNames -join ',')
    }
    $state.fixtures=@($fixtures|ForEach-Object{
        @{name=$_.name;path=('recovery/tests/'+$_.name+'.cs');sha256=(Hash ('recovery/tests/'+$_.name+'.cs'))}
    })
    $state.sdkFixtureInputs=@('Mods/mymod/Data/RecoveryArmorProbe.xml','tools/BinaryAssetBuilder.exe.config'|ForEach-Object{
        @{path=$_;sha256=(Hash (Join-Path $sdk $_))}
    })
    # Match the App build's Roslyn/NET48 toolchain. The legacy Framework csc
    # silently assumes a much older C# language version and is not this recipe.
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if(!(Test-Path -LiteralPath $vswhere)){throw 'VS2022 Build Tools with Roslyn are required'}
    $vs=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    if($LASTEXITCODE -ne 0 -or !$vs){throw 'Cannot locate VS Roslyn compiler'}
    $csc=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
    $refs=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
    if(!(Test-Path -LiteralPath $csc) -or !(Test-Path -LiteralPath (Join-Path $refs 'mscorlib.dll'))){throw 'Roslyn or .NET Framework 4.8 reference assemblies missing'}
    $manifest=Get-Content recovery/build/Managed.Foundation.json -Raw|ConvertFrom-Json
    $frameworkReferences=@(@($manifest.frameworkReferences)+@('System.Web.Extensions')|Select-Object -Unique)
    $state.compiler=@{path=$csc;sha256=(Hash $csc);version=([Diagnostics.FileVersionInfo]::GetVersionInfo($csc).FileVersion);languageVersion='7.3';target='net48/x86'}
    $state.frameworkReferences=@($frameworkReferences|ForEach-Object{@{name=$_;sha256=(Hash (Join-Path $refs ($_+'.dll')))}})
    $env:PATH=(@($oldPath -split ';'|Where-Object{$_ -notmatch '[\\/]SDK[\\/]tools'}) -join ';')
    foreach($mode in @('Debug','Release')) {
        $modeOut=(New-Item -ItemType Directory (Join-Path $out $mode)).FullName
        $clean=Join-Path $work $mode
        $run=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
        $productBuild=Join-Path $CompilerDirectory $mode
        Copy-Item "$productBuild/build.json" "$modeOut/product-build.json"
        Copy-Item "$productBuild/validation/build.json" "$modeOut/validation-host-build.json"
        Copy-Item "$productBuild/link-inputs.json" "$modeOut/link-inputs.json"
        $runtimeOut=(New-Item -ItemType Directory (Join-Path $modeOut 'runtime')).FullName
        foreach($file in $run.runtimeFiles){Copy-Item (Join-Path $clean $file.name) $runtimeOut}
        $product=Get-Content -LiteralPath "$productBuild/build.json" -Raw|ConvertFrom-Json
        if($product.texturePlugin.implementation -cne 'built-in-source' -or @($product.texturePlugin.externalRuntimeFiles).Count -ne 0){throw 'Core lifecycle requires the built-in source texture plugin'}
        $modeState=@{
            configuration=$mode;passed=$false;productExeSha256=$run.productExeSha256
            validationHostSha256=$run.validationHostSha256;runtimeFiles=$run.runtimeFiles
            linkRecipeSha256=(Hash "$productBuild/link-inputs.json");fixtures=@()
            textureDeployment=@{implementation='built-in-source';assemblySha256=$run.validationHostSha256;externalRuntimeFiles=@();scope='Source-linked registration/lifecycle; texture payload differential remains separate'}
        }
        $state.modes+=,$modeState
        $fixtureIndex=0
        foreach($fixture in $fixtures) {
            $fixtureIndex++
            $fixtureOut=(New-Item -ItemType Directory (Join-Path $modeOut $fixture.name)).FullName
            $fixtureWork=(New-Item -ItemType Directory (Join-Path $work ($mode.Substring(0,1)+'-f'+$fixtureIndex))).FullName
            $source=(Resolve-Path ('recovery/tests/'+$fixture.name+'.cs')).Path
            $fixtureExe=Join-Path $clean ($fixture.name+'.exe')
            $arguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/langversion:7.3','/codepage:65001','/platform:x86',('/out:'+$fixtureExe),('/r:'+$run.exe),('/r:'+(Join-Path $clean 'antlr.runtime.dll')))
            $arguments+=$(if($mode -eq 'Release'){'/optimize+'}else{'/debug:full'})
            foreach($name in $frameworkReferences){$arguments+=('/r:'+(Join-Path $refs ($name+'.dll')))}
            $arguments+=$source
            try {
            & $csc @arguments *> "$fixtureOut/fixture-build.log"
            if($LASTEXITCODE -ne 0){Get-Content "$fixtureOut/fixture-build.log" -Tail 100|Out-Host;throw "$mode $($fixture.name) fixture compilation failed"}
            Copy-Item "$($run.exe).config" "$fixtureExe.config"
            Copy-Item $fixtureExe "$fixtureOut/fixture.exe"
            Copy-Item "$fixtureExe.config" "$fixtureOut/fixture.exe.config"
            $fixtureHash=Hash $fixtureExe
            $timeout=if($fixture.timeoutSeconds){[int]$fixture.timeoutSeconds}else{$FixtureTimeoutSeconds}
            $resultPath=Join-Path $fixtureOut 'result.json'
                $processId=Invoke-LifecycleFixture $run.exe @($fixtureExe,$fixtureWork,$resultPath,$state.sourceCommit,$state.runId,$sdk) $clean "$fixtureOut/runtime.log" $timeout
                if(!(Select-String -LiteralPath "$fixtureOut/runtime.log" -SimpleMatch $fixture.marker -Quiet)){throw 'Core lifecycle success marker missing'}
                $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
                if(!$result.succeeded -or $result.sourceCommit -cne $state.sourceCommit -or $result.runId -cne $state.runId){throw 'Core lifecycle result identity or success mismatch'}
                $result|Add-Member -Force -NotePropertyName binarySourceCommit -NotePropertyValue $state.binarySourceCommit
                $result|Add-Member -Force -NotePropertyName testSourceCommit -NotePropertyValue $state.testSourceCommit
                $result|ConvertTo-Json -Depth 20|Set-Content -LiteralPath $resultPath
                if(@($result.checks).Count -eq 0 -or ($result.checks -is [ValueType] -and $result.checks -le 0)){throw 'Lifecycle fixture assertion coverage missing'}
                if($fixture.name -eq 'CompilerCoreLifecycle' -and @($result.tests).Count -lt 4){throw 'Real Core lifecycle case groups missing'}
                if($fixture.name -in @('CompilerBackendLifecycle','CompilerApplicationBuild') -and @($result.tests).Count -eq 0){throw 'Targeted backend/application case groups missing'}
                if((Hash $fixtureExe) -cne $fixtureHash -or (Hash $source) -cne ($state.fixtures|Where-Object{$_.name -ceq $fixture.name}).sha256){throw 'Lifecycle fixture input changed while running'}
                Complete-CompilerValidationRun $run
                $modeState.fixtures+=,@{
                    name=$fixture.name;passed=$true;processId=$processId;timeoutSeconds=$timeout;workRoot=$fixtureWork
                    sourceSha256=(Hash $source);fixtureExeSha256=(Hash $fixtureExe)
                    resultSha256=(Hash $resultPath);runtimeLogSha256=(Hash "$fixtureOut/runtime.log")
                }
                $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
                Write-Host "PASS $mode $($fixture.name): targeted production-object lifecycle fixture"
            } catch {
                $fixtureError=$_.Exception.ToString()
                $fixtureFailures+=($mode+' '+$fixture.name+': '+$fixtureError)
                $modeState.fixtures+=,@{name=$fixture.name;passed=$false;error=$fixtureError;workRoot=$fixtureWork;sourceSha256=(Hash $source)}
                # Each fixture is its own process. Keep collecting independent
                # failures without converting any failed scope to a success.
                Complete-CompilerValidationRun $run
                $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
                Write-Host "FAIL $mode $($fixture.name): $fixtureError"
            } finally {
                # Preserve the small real XML/cache/output case tree on failures too.
                if(Test-Path -LiteralPath $fixtureWork){Copy-Item -LiteralPath $fixtureWork -Destination (Join-Path $fixtureOut 'work') -Recurse}
            }
        }
        Complete-CompilerValidationRun $run
        foreach($file in $run.runtimeFiles){if((Hash (Join-Path $runtimeOut $file.name)) -cne $file.sha256){throw 'Archived lifecycle runtime input changed'}}
        if($modeState.fixtures.Count -ne $fixtures.Count){throw 'Lifecycle fixture omitted'}
        $modeState.passed=(@($modeState.fixtures|Where-Object{$_.passed -ne $true}).Count -eq 0)
    }
    foreach($input in $state.schemaInputs){if((Hash (Join-Path $sdk $input.path)) -cne $input.sha256){throw 'SDK schema input changed during lifecycle validation'}}
    foreach($input in $state.sdkFixtureInputs){if((Hash (Join-Path $sdk $input.path)) -cne $input.sha256){throw 'SDK fixture/config input changed during lifecycle validation'}}
    if($fixtureFailures.Count -ne 0){throw ($fixtureFailures -join "`n")}
    $state.succeeded=$true;$state.status='passed '+$state.fixtureSelection+' targeted Core/backend/application/resource fixtures in Debug and Release; production CLI, APT business coverage, cold-process equivalence, SDK and G3 acceptance remain separate'
    $global:LASTEXITCODE=0
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {
    $env:PATH=$oldPath
    $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
    Pop-Location
}
