[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(30,1800)][int]$GroupTimeoutSeconds=900
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/CompilerValidation.Common.ps1"
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$env:NEWSdk_PLUGIN_TEST_SCHEMA=(Resolve-Path "$root/SDK/Schemas/xsd/CnC3Types.xsd").Path
$CompilerDirectory=(Resolve-Path $CompilerDirectory).Path
if(Test-Path $OutputDirectory){throw "Refusing to overwrite XML matrix evidence: $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$work=Join-Path ([IO.Path]::GetTempPath()) ('compiler-xml-matrices-'+[Guid]::NewGuid().ToString('N'))
$oracle=Join-Path $work 'original'
New-Item -ItemType Directory -Force $work,$oracle|Out-Null
$oldPipe=$env:XML_ORIGINAL_PIPE;$oldPath=$env:PATH;$oldJoypadFixtures=$env:NEWSdk_JOYPAD_FIXTURES
$state=[ordered]@{schemaVersion=1;succeeded=$false;sourceCommit=$null;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';sdkValidated=$false;scope='All 64 existing complete XML group matrices against EXE-owned recovered types, independent original oracle process; not production G3 single-process or SDK acceptance';groups=@();modes=@()}
function Start-XmlProcess([string]$Exe,[string[]]$Arguments,[string]$Directory) {
    $info=[Diagnostics.ProcessStartInfo]::new($Exe)
    $info.WorkingDirectory=$Directory;$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    if(!$process.Start()){throw "Cannot start XML test process: $Exe"}
    return @{process=$process;stdout=$process.StandardOutput.ReadToEndAsync();stderr=$process.StandardError.ReadToEndAsync()}
}
function Finish-XmlProcess($Run,[string]$Log,[int]$Timeout) {
    $timedOut=!$Run.process.WaitForExit($Timeout*1000)
    if($timedOut){$Run.process.Kill($true);$Run.process.WaitForExit()}
    $text=$Run.stdout.GetAwaiter().GetResult()+$Run.stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($Log,$text)
    if($timedOut){Get-Content $Log -Tail 80|Out-Host;throw "XML test timeout after $Timeout seconds: $($Run.process.StartInfo.FileName)"}
    if($Run.process.ExitCode -ne 0){Get-Content $Log -Tail 80|Out-Host;throw "XML test process failed ($($Run.process.ExitCode)): $Log"}
}
function Check-XmlMatrix([string]$Log,[string]$Transcript,[string]$Group) {
    $rows=@();$calls=0
    foreach($line in Get-Content $Log) {
        $row=$null
        if($line -match '^PASS platform=(\d+) fixtures=(\d+) arrayComparisons=(\d+) rejections=(\d+) layoutAssertions=(\d+)$' -or
           $line -match '^PASS platform=(\d+) fixtures=(\d+) comparisons=(\d+) rejections=(\d+) independent=(\d+)$') {
            $row=@{platform=[int]$Matches[1];fixtures=[int]$Matches[2];comparisons=[int]$Matches[3];rejections=[int]$Matches[4];layouts=[int]$Matches[5]}
            if($Group -in @('Xmlpowers','Xmllocomotor','Xmlfxparticle')){if($row.comparisons -ne 3*$row.fixtures){throw 'XML single-path three-buffer matrix incomplete'}}
            elseif($Group -eq 'Xmlplayers'){if($row.comparisons -lt 3*$row.fixtures){throw 'XML players three-buffer matrix incomplete'}}
            elseif($Group -eq 'Xmlai'){if($row.fixtures -lt 1000 -or $row.comparisons -lt 6000 -or $row.rejections -lt 30 -or $row.layouts -lt 300){throw 'XML AI complete coverage missing'}}
            elseif($row.comparisons -ne 6*$row.fixtures){throw 'XML matrix lost direct or ProcessInstance comparisons'}
        } elseif($line -match '^PASS platform=(\d+) fixtures=(\d+) threeArrayComparisons=(\d+) directArrayComparisons=(\d+) rejections=(\d+) independentLayouts=(\d+)$') {
            $row=@{platform=[int]$Matches[1];fixtures=[int]$Matches[2];comparisons=([int]$Matches[3]+[int]$Matches[4]);rejections=[int]$Matches[5];layouts=[int]$Matches[6]}
            if([int]$Matches[3] -ne 3*$row.fixtures -or [int]$Matches[4] -ne 3*$row.fixtures){throw 'XML matrix lost direct or ProcessInstance comparisons'}
        } elseif($line -match '^SUMMARY platform=(\d+) fixtures=(\d+) threeArrayComparisons=(\d+) rejected=(\d+) independentLayouts=(\d+)$') {
            # Attributes' historical full matrix has one three-buffer path.
            $row=@{platform=[int]$Matches[1];fixtures=[int]$Matches[2];comparisons=[int]$Matches[3];rejections=[int]$Matches[4];layouts=[int]$Matches[5]}
            if($row.comparisons -lt 3*$row.fixtures){throw 'XML attributes three-buffer matrix incomplete'}
        } elseif($line -match '^ORACLE-END calls=(\d+)$') {$calls=[int]$Matches[1]}
        if($null -ne $row){if($row.fixtures -le 0 -or $row.rejections -le 0 -or $row.layouts -le 0 -or ($Group -notin @('Xmlplayers','Xmlai','Xmlpowers','Xmlattributes') -and $row.layouts -lt $row.fixtures)){throw 'XML matrix missing fixture/rejection/layout coverage'};if($Group -eq 'Xmlplayers' -and ($row.fixtures -lt 1000 -or $row.comparisons -lt 3000 -or $row.rejections -lt 30 -or $row.layouts -lt 300)){throw 'XML Players complete coverage missing'};$rows+=,$row}
    }
    if($rows.Count -ne 4 -or ($rows.platform -join ',') -cne '0,1,2,99'){throw 'XML complete four-platform matrix missing'}
    $wire=@(Get-Content $Transcript)
    if($wire.Count -ne $calls+1 -or $wire[-1] -cne "END`t$calls" -or $calls -lt ($rows.fixtures|Measure-Object -Sum).Sum){throw 'Original oracle terminal/call coverage missing'}
    for($i=0;$i -lt $calls;$i++) {
        $parts=$wire[$i] -split "`t"
        if($parts.Count -ne 6 -or [int]$parts[0] -ne $i+1 -or $parts[1] -notin @('0','1','2','99') -or @($parts[2..5]|Where-Object{$_ -cnotmatch '^[a-f0-9]{64}$'}).Count -ne 0){throw 'Original oracle sequence or digest malformed'}
    }
    foreach($p in @('0','1','2','99')){if(@($wire|Where-Object{($_ -split "`t")[1] -ceq $p}).Count -eq 0){throw "Original oracle platform $p missing"}}
    return @{platforms=$rows;originalCalls=$calls;transcriptSha256=(Get-FileHash $Transcript -Algorithm SHA256).Hash.ToLowerInvariant()}
}
Push-Location $root
try {
    $state.sourceCommit=(git rev-parse HEAD).Trim()
    if(!$state.runId){$state.runId='local-'+[Guid]::NewGuid().ToString('N')}
    & python recovery/scripts/PrepareCompilerXmlMatrices.py --output "$work/sources"
    if($LASTEXITCODE -ne 0){throw 'XML complete matrix binding adaptation failed'}
    & python recovery/scripts/PrepareJoypadFixtures.py --output "$work/joypad-fixtures"
    if($LASTEXITCODE -ne 0){throw 'Joypad complete matrix fixture generation failed'}
    $env:NEWSdk_JOYPAD_FIXTURES=Join-Path $work 'joypad-fixtures/cases.json'
    $manifest=@(Get-Content "$work/sources/manifest.json" -Raw|ConvertFrom-Json)
    if($manifest.Count -ne 64){throw 'All 64 complete XML groups are required'}
    $state.groups=@($manifest|ForEach-Object{@{group=$_.group;inputs=$_.inputs}})
    $state.adapterSha256=(Get-FileHash recovery/scripts/PrepareCompilerXmlMatrices.py -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.runnerSha256=(Get-FileHash $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $state.transportSha256=(Get-FileHash recovery/tests/XmlProductOracleTransport.cs -Algorithm SHA256).Hash.ToLowerInvariant()
    $transport=(Resolve-Path 'recovery/tests/XmlProductOracleTransport.cs').Path
    Copy-Item SDK/tools/*.dll $oracle
    $original=Join-Path $oracle 'BinaryAssetBuilder.XmlCompiler.dll'
    $state.originalDllSha256=(Get-FileHash $original -Algorithm SHA256).Hash.ToLowerInvariant()
    if($state.originalDllSha256 -cne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6'){throw 'Original XML fingerprint mismatch'}
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    & $csc /nologo /platform:x86 /define:XML_ORIGINAL_SERVER /r:System.Xml.dll /r:System.Core.dll "/r:$oracle/BinaryAssetBuilder.Core.dll" "/out:$oracle/XmlOriginal.exe" $transport *> "$out/original-build.log"
    if($LASTEXITCODE -ne 0){Get-Content "$out/original-build.log" -Tail 80|Out-Host;throw 'XML original server build failed'}
    '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>'|Set-Content "$oracle/XmlOriginal.exe.config"
    $env:PATH=(@($oldPath -split ';'|Where-Object{$_ -notmatch '[\\/]SDK[\\/]tools'}) -join ';')
    $matrixFailures=@()
    foreach($mode in @('Debug','Release')) {
        $clean=Join-Path $work $mode;$modeOut=Join-Path $out $mode
        New-Item -ItemType Directory -Force $clean,$modeOut|Out-Null
        $validationRun=Initialize-CompilerValidationRun (Join-Path $CompilerDirectory $mode) $clean $mode $state.sourceCommit
        $exe=Join-Path $clean 'NewModCompiler.exe'
        $modeState=@{configuration=$mode;productExeSha256=$validationRun.productExeSha256;validationHostSha256=$validationRun.validationHostSha256;groups=@();passed=$false};$state.modes+=,$modeState
        foreach($group in $manifest) {
            $name=$group.group;$groupOut=Join-Path $modeOut $name;New-Item -ItemType Directory $groupOut|Out-Null
            $hostExe=Join-Path $clean ($name+'.exe')
            $defines=@($group.defines|ForEach-Object{'/define:'+$_})
            $server=$null;$product=$null
            try {
            & $csc /nologo /codepage:65001 /platform:x86 /r:System.Xml.dll /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$exe" "/out:$hostExe" @defines @($group.sources) $transport *> "$groupOut/host-build.log"
            if($LASTEXITCODE -ne 0){Get-Content "$groupOut/host-build.log" -Tail 80|Out-Host;throw "$mode $name matrix host build failed"}
            Copy-Item "$exe.config" "$hostExe.config"
            $env:XML_ORIGINAL_PIPE='newsdk-xml-'+[Guid]::NewGuid().ToString('N')
                $server=Start-XmlProcess "$oracle/XmlOriginal.exe" @($env:XML_ORIGINAL_PIPE,$original,"$groupOut/original.tsv") $oracle
                $fixtureArgs=@($hostExe,$original)
                if($group.dynamicAssemblyArgument){$fixtureArgs+=@($exe)}else{$fixtureArgs+=@("$groupOut/result.json")}
                if($group.repositoryArgument){$fixtureArgs+=@($root)}
                $product=Start-XmlProcess $validationRun.exe $fixtureArgs $clean
                Finish-XmlProcess $product "$groupOut/host.log" $GroupTimeoutSeconds
                Finish-XmlProcess $server "$groupOut/original.log" 30
                $receipt=Check-XmlMatrix "$groupOut/host.log" "$groupOut/original.tsv" $name
                $receipt.group=$name;$receipt.passed=$true;$receipt.generatedHostSha256=(Get-FileHash $hostExe -Algorithm SHA256).Hash.ToLowerInvariant()
                $modeState.groups+=,$receipt
                $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
                Write-Host "$mode $name complete existing matrix passed"
            } catch {
                $failure=@{group=$name;configuration=$mode;passed=$false;error=$_.Exception.Message}
                $modeState.groups+=,$failure;$matrixFailures+=,$failure
                $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
                Write-Host "$mode $name failed; collecting the remaining independent matrices"
            } finally {
                foreach($item in @(@{run=$product;log="$groupOut/host.log"},@{run=$server;log="$groupOut/original.log"})){
                    $run=$item.run
                    if($null -ne $run){
                        try {
                            if(!$run.process.HasExited){$run.process.Kill($true);$run.process.WaitForExit()}
                            if(!(Test-Path -LiteralPath $item.log)){[IO.File]::WriteAllText($item.log,($run.stdout.GetAwaiter().GetResult()+$run.stderr.GetAwaiter().GetResult()))}
                        } catch {Write-Warning "Unable to preserve XML process cleanup log: $_"}
                    }
                }
            }
        }
        Complete-CompilerValidationRun $validationRun
        if($modeState.groups.Count -ne 64){throw 'XML groups omitted'}
        $modeState.passed=@($modeState.groups|Where-Object{!$_.passed}).Count -eq 0
    }
    if($matrixFailures.Count){throw "$($matrixFailures.Count) XML matrices failed; see each group log and validation.json"}
    $state.succeeded=$true
    $state.status='passed all 64 complete group matrices in Debug and Release; SDK and G3 acceptance remain separate'
} catch {$state.status='failed';$state.error=$_.Exception.Message;throw}
finally {
    $env:XML_ORIGINAL_PIPE=$oldPipe;$env:PATH=$oldPath;$env:NEWSdk_JOYPAD_FIXTURES=$oldJoypadFixtures
    $state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"
    Pop-Location
}
