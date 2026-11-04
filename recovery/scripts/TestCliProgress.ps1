#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ProductBuildDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(3,15)][int]$Pairs=3,
    [ValidateRange(30,600)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
if(!$IsWindows){throw 'Progress integration and real-console measurements require Windows'}
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$product=(Resolve-Path -LiteralPath $ProductBuildDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite progress evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$work=Join-Path ([IO.Path]::GetPathRoot($env:RUNNER_TEMP)) ('cpg-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$null=New-Item -ItemType Directory -Path $work
$oldProgress=$env:NEWSDK_PROGRESS
$state=@{schemaVersion=1;sourceCommit=(git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';samples=@();coreFixtures=@();scope='One current Release product; existing real Core/application lifecycle fixtures; actual redirected CLI error/CtrlBreak/retry; complete unmodified pinned Upheaval base stream, warm legacy cache, three or more alternating progress-on/off pairs in newly allocated real Windows consoles. No Debug rebuild, original SDK differential or game runtime acceptance.';timingBoundary='External process wall including equal console/process startup; setup, output comparisons, evidence copies and cache warm-up excluded. Same executable, SDK/MOD/cache, output path, jobs=4 and performance=off. OS page cache is uncontrolled.'}
function Need([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function SaveState{$state|ConvertTo-Json -Depth 16|Set-Content -LiteralPath "$out/validation.json"}
function Median($Values){$sorted=@($Values|Sort-Object);$n=$sorted.Count;$middle=[int][Math]::Floor($n/2);if($n%2){return $sorted[$middle]};return ($sorted[$middle-1]+$sorted[$middle])/2}
function RunRedirected([string]$Exe,[string[]]$Arguments,[string]$CaseDirectory,[int]$Expected=0){
    $null=New-Item -ItemType Directory -Force -Path $CaseDirectory
    $info=[Diagnostics.ProcessStartInfo]::new($Exe);$info.WorkingDirectory=Split-Path $Exe;$info.UseShellExecute=$false
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.CreateNoWindow=$true
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$info
    try {
        Need ($process.Start()) 'Cannot start redirected process'
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);$process.WaitForExit();throw 'Redirected process timed out'}
        $text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText("$CaseDirectory/stdout.log",$text);[IO.File]::WriteAllText("$CaseDirectory/stderr.log",$errors)
        Need ($process.ExitCode -eq $Expected) "Redirected process failed: exit=$($process.ExitCode), expected=$Expected; $CaseDirectory"
        # CRLF line endings are normal. A remaining CR would be a dynamic redraw.
        Need (!$errors.Replace("`r`n","`n").Contains("`r")) 'Redirected stderr contains a progress redraw'
    } finally {$process.Dispose()}
}
try {
    Need ((Median @(1,2,100)) -eq 2 -and (Median @(1,3,7,100)) -eq 5) 'Measurement median precheck failed'
    $receipt=Get-Content -LiteralPath "$product/build.json" -Raw|ConvertFrom-Json
    $exe=(Resolve-Path -LiteralPath "$product/bin/NewModCompiler.exe").Path
    Need ($receipt.sourceCommit -ceq $state.sourceCommit -and $receipt.configuration -ceq 'Release' -and (Hash $exe) -ceq $receipt.exeSha256) 'Product identity differs from current Release source'
    $state.binarySourceCommit=$receipt.sourceCommit;$state.binarySha256=$receipt.exeSha256
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
    Need ($LASTEXITCODE -eq 0 -and !!$vs) 'VS Roslyn compiler unavailable'
    $csc=Join-Path $vs 'MSBuild/Current/Bin/Roslyn/csc.exe'
    $refs=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'

    # Only this test launcher allocates a console. Production remains a single
    # compiler process with its default terminal detection and no forced mode.
    $consoleSource=@'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class CliProgressConsole {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup {
        public int Size; public string Reserved,Desktop,Title;
        public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        public short Show,ReservedSize; public IntPtr ReservedBytes,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct Child { public IntPtr Process,Thread; public uint Pid,Tid; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder command,IntPtr ps,IntPtr ts,bool inherit,uint flags,IntPtr environment,string cwd,ref Startup startup,out Child child);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static string Quote(string value) {
        var result=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\'){slashes++;continue;}
            result.Append('\\',c=='"'?slashes*2+1:slashes);result.Append(c);slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
    public sealed class Result { public uint ExitCode,ProcessId; public double WallMs; }
    public static Result Run(string exe,string[] arguments,string cwd,int timeoutSeconds) {
        var command=new StringBuilder(Quote(exe));foreach(string argument in arguments)command.Append(' ').Append(Quote(argument));
        // CREATE_NEW_CONSOLE and no STARTF_USESTDHANDLES: Windows supplies real
        // console handles, even when the Actions runner itself uses pipes.
        var startup=new Startup{Size=Marshal.SizeOf(typeof(Startup)),Flags=1,Show=0};Child child;
        var timer=Stopwatch.StartNew();
        if(!CreateProcess(exe,command,IntPtr.Zero,IntPtr.Zero,false,0x10,IntPtr.Zero,cwd,ref startup,out child))throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            uint waited=WaitForSingleObject(child.Process,checked((uint)timeoutSeconds*1000));
            if(waited!=0){TerminateProcess(child.Process,252);WaitForSingleObject(child.Process,5000);throw new TimeoutException("Console child wait failed: "+waited);}
            timer.Stop();uint code;if(!GetExitCodeProcess(child.Process,out code))throw new Win32Exception(Marshal.GetLastWin32Error());
            return new Result{ExitCode=code,ProcessId=child.Pid,WallMs=timer.Elapsed.TotalMilliseconds};
        } finally {CloseHandle(child.Thread);CloseHandle(child.Process);}
    }
    public static bool EqualFiles(string first,string second) {
        using(var a=File.OpenRead(first))using(var b=File.OpenRead(second)) {
            if(a.Length!=b.Length)return false;var x=new byte[65536];var y=new byte[65536];int n;
            while((n=a.Read(x,0,x.Length))!=0){int done=0;while(done<n){int read=b.Read(y,done,n-done);if(read==0)return false;done+=read;}for(int i=0;i<n;i++)if(x[i]!=y[i])return false;}
            return true;
        }
    }
    public static int Main(string[] args) {
        File.WriteAllText(args[0],"{\"outputRedirected\":"+Console.IsOutputRedirected.ToString().ToLowerInvariant()+",\"errorRedirected\":"+Console.IsErrorRedirected.ToString().ToLowerInvariant()+"}");return 0;
    }
}
'@
    $consolePath=Join-Path $out 'ConsoleLauncher.cs';[IO.File]::WriteAllText($consolePath,$consoleSource)
    Add-Type -TypeDefinition $consoleSource
    $probe=Join-Path $work 'console-probe.exe'
    & $csc /nologo /target:exe /platform:x86 "/out:$probe" $consolePath *> "$out/console-probe-build.log"
    Need ($LASTEXITCODE -eq 0) 'Console handle probe compilation failed'
    $probeResult=[CliProgressConsole]::Run($probe,[string[]]@("$out/console-probe.json"),$work,30)
    $probeBody=Get-Content "$out/console-probe.json" -Raw|ConvertFrom-Json
    Need ($probeResult.ExitCode -eq 0 -and !$probeBody.outputRedirected -and !$probeBody.errorRedirected) 'Real console child has redirected standard handles'
    $state.consoleProbe=$probeBody

    # Reuse the exact Release production objects with the existing fixture entry,
    # and existing assertions; no second production build or copied Debug label.
    . "$PSScriptRoot/CompilerValidation.Common.ps1"
    $runtime=Join-Path $work 'core-runtime'
    $run=Initialize-CompilerValidationRun $product $runtime 'Release' $state.sourceCommit
    $manifest=Get-Content "$root/recovery/build/Managed.Foundation.json" -Raw|ConvertFrom-Json
    foreach($fixture in @('CompilerCoreLifecycle','CompilerApplicationBuild')){
        $case=Join-Path $out $fixture;$null=New-Item -ItemType Directory -Path $case
        $fixtureExe=Join-Path $runtime ($fixture+'.exe')
        $compilerArguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/langversion:7.3','/codepage:65001','/platform:x86','/optimize+',('/out:'+$fixtureExe),('/r:'+$run.exe),('/r:'+(Join-Path $runtime 'antlr.runtime.dll')))
        foreach($name in @(@($manifest.frameworkReferences)+@('System.Web.Extensions')|Select-Object -Unique)){$compilerArguments+=('/r:'+(Join-Path $refs ($name+'.dll')))}
        $compilerArguments+=(Join-Path $root ('recovery/tests/'+$fixture+'.cs'))
        & $csc @compilerArguments *> "$case/fixture-build.log"
        Need ($LASTEXITCODE -eq 0) "$fixture compilation failed"
        RunRedirected $run.exe @($fixtureExe,(Join-Path $work $fixture),"$case/result.json",$state.sourceCommit,[string]$state.runId,(Join-Path $root 'SDK')) $case
        $body=Get-Content "$case/result.json" -Raw|ConvertFrom-Json
        Need ($body.succeeded -and $body.checks -gt 0 -and $body.sourceCommit -ceq $state.sourceCommit) "$fixture assertions failed"
        Complete-CompilerValidationRun $run
        $state.coreFixtures+=@{name=$fixture;configuration='Release';checks=$body.checks;sourceSha256=(Hash (Join-Path $root ('recovery/tests/'+$fixture+'.cs')));validationHostSha256=$run.validationHostSha256}
        SaveState
    }

    $sdk=Join-Path $work 's';$null=New-Item -ItemType Directory -Path $sdk
    Get-ChildItem (Join-Path $root 'SDK') -Force|Where-Object Name -notin @('Mods','builtmods','SuperCompilerCache')|Copy-Item -Destination $sdk -Recurse
    $historical='bacfc69fc3e71c8116d1f22b933b8b6173170ecf';$tree='5551fc26a6f96b62613443e0d0fa2958c14cbce0'
    Need ((git -C $root rev-parse ($historical+':SDK/Mods/Upheaval')).Trim() -ceq $tree) 'Historical complete Upheaval identity differs'
    $archive=Join-Path $work 'upheaval.tar'
    & git -C $root archive --output=$archive $historical 'SDK/Mods/Upheaval';Need ($LASTEXITCODE -eq 0) 'Upheaval archive failed'
    $external=Join-Path $work 'external';$null=New-Item -ItemType Directory -Path $external
    & tar -xf $archive -C $external;Need ($LASTEXITCODE -eq 0) 'Upheaval extraction failed';Remove-Item $archive
    $mod=Join-Path $external 'SDK/Mods/Upheaval'
    $state.workload=@{sourceCommit=$historical;sourceTree=$tree;mod='complete unmodified Upheaval';selection='base';sdk='current checkout copied without Mods or caches';files=@(Get-ChildItem -LiteralPath $mod -Recurse -File).Count}
    $destination=Join-Path $work 'o'
    $buildArguments=@('build','--sdk-root',$sdk,'--mod',$mod,'--out',$destination,'--package-content','base','--package-format','loose','--jobs','4','--performance','off')
    $reference=Join-Path $work 'reference'
    function CheckBuild([string]$Label,[bool]$Hot){
        $case=Join-Path $out $Label;$null=New-Item -ItemType Directory -Force -Path $case
        Copy-Item "$destination/build-report.json","$destination/build.events.jsonl" -Destination $case
        $body=Get-Content "$case/build-report.json" -Raw|ConvertFrom-Json
        Need ($body.status -ceq 'succeeded' -and $body.exitCode -eq 0 -and $body.diagnosticCounts.error -eq 0) "$Label build failed"
        Need ($body.effectiveOptions.performance.Value -ceq 'off' -and $null -eq $body.performance) 'Progress enabled performance sampling'
        if($Hot){Need ($body.cache.xmlParse -eq 0 -and $body.cache.instanceCompile -eq 0 -and $body.cache.documentReuse -gt 0) "$Label cache is not fully warm"}
        $progress=@(Get-Content "$case/build.events.jsonl"|ForEach-Object{$_|ConvertFrom-Json}|Where-Object eventType -CEQ 'Progress')
        Need ($progress.Count -eq @($body.plan.Tasks).Count) 'Persistent progress changed from one event per completed stream'
        for($i=0;$i -lt $progress.Count;$i++){Need ($progress[$i].payload.completed -eq $i+1 -and $progress[$i].payload.total -eq $progress.Count -and $progress[$i].payload.unit -ceq 'streams') 'Persistent stream progress semantics changed'}
        $published=Join-Path $destination 'published'
        if(!(Test-Path $reference)){Copy-Item $published $reference -Recurse}
        $files=@(Get-ChildItem -LiteralPath $published -Recurse -File)
        Need ($files.Count -gt 0 -and $files.Count -eq @(Get-ChildItem -LiteralPath $reference -Recurse -File).Count) 'Published output inventory differs'
        foreach($file in $files){$relative=[IO.Path]::GetRelativePath($published,$file.FullName);Need ([CliProgressConsole]::EqualFiles($file.FullName,(Join-Path $reference $relative))) "Published bytes differ: $relative"}
        return $body
    }
    function CheckProgressCallbacks([string]$Label,$Body,[long]$ExpectedAssets){
        $text=[IO.File]::ReadAllText((Join-Path (Join-Path $out $Label) 'stderr.log'))
        $summaries=[regex]::Matches($text,'(?m)^\[完成\]\s+\d+/\d+\s+[^\r\n]*?\|\s*完成\s+([0-9,]+)\s*\|\s*XML\s+([0-9,]+)\s*\|\s*复用文档\s+([0-9,]+)')
        Need ($summaries.Count -eq @($Body.plan.Tasks).Count) "$Label omitted completed stream work counts"
        [long]$assets=0;[long]$xml=0;[long]$reused=0
        foreach($summary in $summaries){
            $assets+=[long]$summary.Groups[1].Value.Replace(',','')
            $xml+=[long]$summary.Groups[2].Value.Replace(',','')
            $reused+=[long]$summary.Groups[3].Value.Replace(',','')
        }
        Need ($assets -eq $ExpectedAssets -and $assets -gt 0 -and $xml -eq $Body.cache.xmlParse -and $reused -eq $Body.cache.documentReuse) "$Label live callbacks differ from actual compiled/reused work"
        return @{assetsCompleted=$assets;xmlParsed=$xml;documentsReused=$reused;matchedReportCache=$true}
    }
    $env:NEWSDK_PROGRESS=$null
    RunRedirected $exe $buildArguments "$out/redirected-cold"
    $coldReport=CheckBuild 'redirected-cold' $false
    $state.realCallbacks=@{cold=(CheckProgressCallbacks 'redirected-cold' $coldReport $coldReport.cache.instanceCompile)}
    $redirected=[IO.File]::ReadAllText("$out/redirected-cold/stderr.log")
    Need ($redirected.Contains('[开始]') -and $redirected.Contains('[完成]')) 'Default redirected CLI omitted phase boundaries or result'
    foreach($enabled in @($true,$false)){
        $env:NEWSDK_PROGRESS=if($enabled){$null}else{'off'}
        $warm=[CliProgressConsole]::Run($exe,$buildArguments,(Split-Path $exe),$TimeoutSeconds)
        Need ($warm.ExitCode -eq 0) 'Console warm-up failed'
        $null=CheckBuild ('warm-'+$enabled.ToString().ToLowerInvariant()) $true
    }
    for($pair=1;$pair -le $Pairs;$pair++){
        $order=if($pair%2){@($true,$false)}else{@($false,$true)}
        foreach($enabled in $order){
            $label='pair-'+$pair+'-'+$(if($enabled){'on'}else{'off'})
            $env:NEWSDK_PROGRESS=if($enabled){$null}else{'off'}
            $sample=[CliProgressConsole]::Run($exe,$buildArguments,(Split-Path $exe),$TimeoutSeconds)
            $row=@{label=$label;pair=$pair;progressEnabled=$enabled;wallMs=$sample.WallMs;processId=$sample.ProcessId;exitCode=$sample.ExitCode;terminal='real hidden Windows console';status='started'}
            $state.samples+=,$row;SaveState
            Need ($sample.ExitCode -eq 0) "$label console build failed"
            $body=CheckBuild $label $true
            $row.cache=$body.cache;$row.status='passed';SaveState
        }
    }
    $on=Median @($state.samples|Where-Object progressEnabled -EQ $true|ForEach-Object wallMs)
    $off=Median @($state.samples|Where-Object progressEnabled -EQ $false|ForEach-Object wallMs)
    $overhead=100*($on/$off-1)
    $state.measurement=@{pairs=$Pairs;onMedianMs=$on;offMedianMs=$off;overheadPercent=$overhead;targetMaxPercent=1;targetMet=$overhead -le 1}
    SaveState

    # Existing controller sends a real CTRL_BREAK only after this process's
    # StreamStarted event; timeout/termination cannot be mistaken for cancel.
    $env:NEWSDK_PROGRESS=$null
    $controller=& "$PSScriptRoot/BuildCompilerCliControl.ps1" -OutputDirectory "$out/controller"
    $cancelOut=Join-Path $work 'cancel'
    $cancelArguments=@('build','--sdk-root',$sdk,'--mod',$mod,'--out',$cancelOut,'--package-content','base','--package-format','loose','--jobs','4','--performance','off','--sc=false','--bc=false')
    $null=New-Item -ItemType Directory -Path "$out/cancel"
    @{exe=$exe;cwd=(Split-Path $exe);arguments=$cancelArguments;stdout="$out/cancel/stdout.log";stderr="$out/cancel/stderr.log";events=(Join-Path $cancelOut 'build.events.jsonl');cancel=$true;timeoutSeconds=$TimeoutSeconds}|ConvertTo-Json -Depth 5|Set-Content "$out/cancel/request.json"
    RunRedirected $controller @("$out/cancel/request.json","$out/cancel/control.json") "$out/cancel/controller"
    $audit=Get-Content "$out/cancel/control.json" -Raw|ConvertFrom-Json
    $cancelReport=Get-Content "$cancelOut/build-report.json" -Raw|ConvertFrom-Json
    Copy-Item "$cancelOut/build-report.json","$cancelOut/build.events.jsonl" -Destination "$out/cancel"
    Need ($audit.succeeded -and $audit.signalSent -and $audit.streamStartedObserved -and $audit.processExitCode -eq 6 -and $cancelReport.status -ceq 'cancelled' -and @($cancelReport.artifacts).Count -eq 0) 'Actual CtrlBreak cancellation was not verified'
    $state.actualCancellation=@{passed=$true;exitCode=6;terminal='redirected via existing real CtrlBreak controller'}
    RunRedirected $exe $buildArguments "$out/retry-after-cancel"
    $retryReport=CheckBuild 'retry-after-cancel' $true
    $state.realCallbacks.hot=CheckProgressCallbacks 'retry-after-cancel' $retryReport $state.realCallbacks.cold.assetsCompleted
    $bad=Join-Path $work 'bad';$null=New-Item -ItemType Directory -Path "$bad/Data"
    [IO.File]::WriteAllText("$bad/Data/Mod.xml","<AssetDeclaration xmlns='uri:ea.com:eala:asset'><ArmorTemplate id='broken'></AssetDeclaration>")
    $badOut=Join-Path $work 'failed'
    RunRedirected $exe @('build','--sdk-root',$sdk,'--mod',$bad,'--out',$badOut,'--package-content','base','--package-format','loose','--performance','off') "$out/failure" 4
    $failed=Get-Content "$badOut/build-report.json" -Raw|ConvertFrom-Json
    Copy-Item "$badOut/build-report.json","$badOut/build.events.jsonl" -Destination "$out/failure"
    Need ($failed.status -ceq 'failed' -and @($failed.artifacts).Count -eq 0) 'Malformed XML failure accepted output'
    $state.actualFailure=@{passed=$true;exitCode=4}
    Need ((Hash $exe) -ceq $state.binarySha256) 'Production EXE changed during validation'
    $summary=@"
Complete historical Upheaval base stream, same Release executable and hot cache; $Pairs alternating real-console pairs.
Progress on median: $([Math]::Round($on,3)) ms; off: $([Math]::Round($off,3)) ms; overhead: $([Math]::Round($overhead,3))%; target <= 1%: $($overhead -le 1).
Output bytes and stream-level JSONL semantics matched; performance sampling stayed off. External process wall includes console startup; OS page cache was uncontrolled.
Release Core/application fixtures, actual redirected CtrlBreak exit 6/retry, malformed-XML exit 4 and redirected boundary output passed. Cold/hot stream summaries matched actual committed asset totals and XML/document reuse report counters. Managed renderer contracts are recorded separately in contracts.log.
"@
    $summary|Set-Content "$out/measurement.md"
    if($env:GITHUB_STEP_SUMMARY){$summary|Add-Content $env:GITHUB_STEP_SUMMARY}
    Need ($overhead -le 1) "Progress overhead target exceeded: $overhead%"
    $state.status='passed'
} catch {$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally {$env:NEWSDK_PROGRESS=$oldProgress;SaveState}
