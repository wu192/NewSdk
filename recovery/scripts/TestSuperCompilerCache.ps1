#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(30,600)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$Compiler=(Resolve-Path -LiteralPath $Compiler).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to replace evidence: $OutputDirectory"}
$evidence=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
# Short paths preserve the existing Win32/MAX_PATH contract, particularly the
# SDK cache prefix and legacy asset hash filenames.
$work=Join-Path ([IO.Path]::GetPathRoot($env:RUNNER_TEMP)) ('scc-'+$env:GITHUB_RUN_ID+'-'+$env:GITHUB_RUN_ATTEMPT)
if(Test-Path -LiteralPath $work){throw "Refusing to replace work: $work"}
$null=New-Item -ItemType Directory -Path $work
$sdk=Join-Path $work 's';$cacheBase=Join-Path $sdk 'SuperCompilerCache'
$state=@{schemaVersion=1;sourceCommit=(git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';samples=@();inputs=@();scope='One Release executable; complete current mymod inside SDK/Mods and complete pinned historical Upheaval outside SDK; base loose cold/hot with changed output and BIG from warm cache, plus tiny flash8-empty direct-FLA APT cold/hot source-identity check. No Debug build, original SDK byte differential, full APT/maps or full cache flag matrix.'}
$state.binarySourceCommit=(Get-Content -LiteralPath (Join-Path (Split-Path (Split-Path $Compiler)) 'build.json') -Raw|ConvertFrom-Json).sourceCommit
$state.binarySha256=(Get-FileHash -LiteralPath $Compiler -Algorithm SHA256).Hash.ToLowerInvariant()
$watchers=@()
function Need([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Within([string]$Directory,[string]$Path){
    $base=[IO.Path]::GetFullPath($Directory).TrimEnd([char[]]'\/');$full=[IO.Path]::GetFullPath($Path)
    $full.Equals($base,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function Snapshot([string]$Directory,[string]$Exclude){
    $result=@{};Get-ChildItem -LiteralPath $Directory -Recurse -Force|ForEach-Object{
        if($Exclude -and (Within $Exclude $_.FullName)){return}
        $path=$_.FullName;$key=[IO.Path]::GetRelativePath($Directory,$path).Replace('\','/')
        # Provider DirectoryInfo objects can retain stale enumeration metadata.
        # Query each path directly at snapshot time instead of reusing it.
        $attributes=[int][IO.File]::GetAttributes($path)
        $result[$key]=if([IO.Directory]::Exists($path)){'directory:'+$attributes+':'+[IO.Directory]::GetLastWriteTimeUtc($path).Ticks}else{[string]([IO.FileInfo]::new($path).Length)+':'+[IO.File]::GetLastWriteTimeUtc($path).Ticks+':'+$attributes+':'+(Hash $path)}
    };$result
}
function Unchanged([string]$Directory,$Before){
    $after=Snapshot $Directory;Need ($after.Count -eq $Before.Count) "Source entry count changed: $Directory"
    foreach($key in $Before.Keys){Need ($after.ContainsKey($key) -and $after[$key] -ceq $Before[$key]) "Source changed: $Directory/$key"}
}
function SnapshotDifferences($Before,$After){
    foreach($key in @((@($Before.Keys)+@($After.Keys))|Sort-Object -Unique)){
        if($Before[$key] -cne $After[$key]){@{path=$key;before=$Before[$key];after=$After[$key]}}
    }
}
function InvokeBuild([string]$Label,[string]$Command,[string]$Mod,[string]$Format,[bool]$Version=$true,[string]$Content='base'){
    $out=Join-Path $work ('out/'+$Label);$case=Join-Path $evidence $Label;$null=New-Item -ItemType Directory -Path $case
    $arguments=@($Command,'--sdk-root',$sdk,'--mod',$Mod,'--out',$out,'--package-content',$Content,'--package-format',$Format,'--jobs','4','--performance','summary')
    if($Format -eq 'big' -and $Version){$arguments+=@('--mod-version','1.0')}
    $row=@{label=$Label;command=$arguments;status='started'};$state.samples+=,$row
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Compiler;$start.WorkingDirectory=Split-Path $Compiler;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $arguments){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try {
        Need ($process.Start()) "Cannot launch $Label"
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);throw "$Label timed out"}
        $text=$stdout.GetAwaiter().GetResult();$errors=$stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $case 'stdout.log'),$text);[IO.File]::WriteAllText((Join-Path $case 'stderr.log'),$errors)
        $row.exitCode=$process.ExitCode
        if(!$Version){Need ($process.ExitCode -eq 2 -and $errors.Contains('CLI_MOD_VERSION')) 'BIG still requires an explicit CLI MOD version';$row.status='passed';return $null}
        if($process.ExitCode -ne 0){Write-Host $text;Write-Host $errors;throw "$Label failed with exit $($process.ExitCode)"}
        $reportPath=Join-Path $out 'build-report.json';Copy-Item -LiteralPath $reportPath -Destination $case
        Copy-Item -LiteralPath (Join-Path $out 'build.events.jsonl') -Destination $case
        $report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
        Need ($report.status -ceq 'succeeded' -and $report.diagnosticCounts.error -eq 0) "$Label reported failure/errors"
        foreach($entry in @(Get-ChildItem -LiteralPath $out -Force)){Need ($entry.Name -in @('published','build.log','build.events.jsonl','build-report.json')) "$Label left non-delivery files in output: $($entry.Name)"}
        $row.cache=$report.cache;$row.artifactCount=@($report.artifacts).Count;$row.status='passed'
        return @{report=$report;output=$out}
    } finally {$process.Dispose();$state|ConvertTo-Json -Depth 16|Set-Content (Join-Path $evidence 'validation.json')}
}

# Event callbacks run in CLR handlers, so even transient create/delete writes
# are recorded while PowerShell waits for the child. Buffer errors reject the
# audit instead of allowing a source snapshot to hide transient writes.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.IO;
public sealed class SuperCompilerCacheWriteAudit : IDisposable {
    public sealed class Event {
        public string Path, ChangeType;
        public long LastWriteTimeUtcTicks;
        public int Attributes;
    }
    readonly FileSystemWatcher watcher;
    public readonly ConcurrentQueue<string> Paths = new ConcurrentQueue<string>();
    public readonly ConcurrentQueue<Event> Events = new ConcurrentQueue<Event>();
    public readonly ConcurrentQueue<string> Errors = new ConcurrentQueue<string>();
    public SuperCompilerCacheWriteAudit(string root,bool recursive = true) {
        watcher = new FileSystemWatcher(root) { IncludeSubdirectories = recursive, InternalBufferSize = 65536,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.Security };
        watcher.Created += Changed; watcher.Changed += Changed; watcher.Deleted += Changed;
        watcher.Renamed += (s,e) => { Record(e.OldFullPath,"RenamedFrom"); Record(e.FullPath,"RenamedTo"); };
        watcher.Error += (s,e) => Errors.Enqueue(e.GetException().ToString());
        watcher.EnableRaisingEvents = true;
    }
    void Changed(object sender, FileSystemEventArgs args) { Record(args.FullPath,args.ChangeType.ToString()); }
    void Record(string path,string type) {
        var record = new Event { Path = path, ChangeType = type };
        try { record.Attributes=(int)File.GetAttributes(path); record.LastWriteTimeUtcTicks=File.GetLastWriteTimeUtc(path).Ticks; } catch { }
        Paths.Enqueue(path); Events.Enqueue(record);
    }
    public void Dispose() { watcher.EnableRaisingEvents = false; watcher.Dispose(); }
}
'@
try {
    $null=New-Item -ItemType Directory -Path $sdk
    Get-ChildItem (Join-Path $root 'SDK') -Force|Where-Object Name -notin @('Mods','builtmods','SuperCompilerCache')|Copy-Item -Destination $sdk -Recurse
    $modInside=Join-Path $sdk 'Mods/mymod';$null=New-Item -ItemType Directory -Path (Split-Path $modInside)
    Copy-Item (Join-Path $root 'SDK/Mods/mymod') -Destination $modInside -Recurse
    $flaMod=Join-Path $sdk 'Mods/Flash8'
    $null=New-Item -ItemType Directory -Path (Join-Path $flaMod 'Data/AptUI')
    Copy-Item (Join-Path $root 'ui/tests/fixtures/flash8-empty.fla') -Destination (Join-Path $flaMod 'Data/AptUI/flash8-empty.fla')
    $historical='bacfc69fc3e71c8116d1f22b933b8b6173170ecf';$tree='5551fc26a6f96b62613443e0d0fa2958c14cbce0'
    Need ((git -C $root rev-parse ($historical+':SDK/Mods/Upheaval')).Trim() -ceq $tree) 'Pinned historical Upheaval tree differs'
    $archive=Join-Path $work 'upheaval.tar'
    & git -C $root archive --output=$archive $historical 'SDK/Mods/Upheaval';Need ($LASTEXITCODE -eq 0) 'Cannot archive pinned Upheaval'
    $external=Join-Path $work 'external';$null=New-Item -ItemType Directory -Path $external
    & tar -xf $archive -C $external;Need ($LASTEXITCODE -eq 0) 'Cannot extract pinned Upheaval';Remove-Item $archive
    $modOutside=Join-Path $external 'SDK/Mods/Upheaval'
    # Finish fixture copy finalizers and delayed directory-close notifications
    # before defining the compiler's audit interval.
    [GC]::Collect();[GC]::WaitForPendingFinalizers();Start-Sleep -Milliseconds 200
    $sdkBefore=Snapshot $sdk $cacheBase
    $state.sdkRootMetadata=@{beforeLastWriteTimeUtcTicks=[IO.Directory]::GetLastWriteTimeUtc($sdk).Ticks;beforeAttributes=[int][IO.File]::GetAttributes($sdk);allowedChange='Creating the authorized SuperCompilerCache child changes the SDK root directory timestamp.'}
    $sdkBefore|ConvertTo-Json -Depth 4|Set-Content (Join-Path $evidence 'inputs-sdk.json')
    $mods=@(@{name='mymod';path=$modInside;source='current repository';snapshot=(Snapshot $modInside)},@{name='upheaval';path=$modOutside;source=$historical;sourceTree=$tree;snapshot=(Snapshot $modOutside)})
    $sourceMods=@($mods)+@(@{name='flash8-empty';path=$flaMod;source='ui/tests/fixtures/flash8-empty.fla';snapshot=(Snapshot $flaMod)})
    foreach($mod in $sourceMods){
        $state.inputs+=@{name=$mod.name;path=$mod.path;source=$mod.source;sourceTree=$mod.sourceTree;entries=$mod.snapshot.Count}
        $mod.snapshot|ConvertTo-Json -Depth 4|Set-Content (Join-Path $evidence ('inputs-'+$mod.name+'.json'))
        $audit=[SuperCompilerCacheWriteAudit]::new($mod.path);$watchers+=,$audit;$mod.audit=$audit
    }
    # Cover every SDK input subtree, plus direct children of the SDK root. The
    # authorized cache traffic must not flood the input audit's native buffer.
    # A new non-cache directory/file is still observed at the root boundary.
    $sdkAudits=@([SuperCompilerCacheWriteAudit]::new($sdk,$false))
    foreach($directory in @(Get-ChildItem -LiteralPath $sdk -Directory -Force|Where-Object Name -ne 'SuperCompilerCache')){
        $sdkAudits+=,[SuperCompilerCacheWriteAudit]::new($directory.FullName)
    }
    $watchers+=@($sdkAudits)
    foreach($mod in $mods){$null=InvokeBuild ($mod.name+'-plan') 'plan' $mod.path 'big';Need (!(Test-Path $cacheBase)) 'Plan created a cache directory'}
    $null=InvokeBuild 'big-version-required' 'plan' $modInside 'big' $false
    foreach($mod in $mods){
        $cold=InvokeBuild ($mod.name+'-loose-cold') 'build' $mod.path 'loose'
        $session=$cold.report.cache.sessionCacheDirectory;Need ([bool]$session -and (Within $cacheBase $session)) 'Session cache escaped the supplied SDK'
        $partition=Split-Path $session;Need ((Split-Path $partition) -ieq $cacheBase) 'Cache did not use a MOD partition'
        Need ((Split-Path $partition -Leaf) -match '^.{1,12}_[0-9a-f]{32}$') 'MOD partition identity format differs'
        $mod.partition=$partition
        Need (@($cold.report.artifacts).Count -gt 0) 'Cold loose build produced no delivery'
        foreach($artifact in $cold.report.artifacts){
            Need (Within (Join-Path $cold.output 'published') $artifact.Path) 'A private cache path entered final artifacts'
            $relative=[IO.Path]::GetRelativePath((Join-Path $cold.output 'published'),$artifact.Path)
            $cached=Join-Path (Join-Path $partition 'streams') $relative
            Need ((Test-Path -LiteralPath $cached -PathType Leaf) -and (Hash $cached) -ceq (Hash $artifact.Path)) 'Loose export differs from the current cache stream'
        }
        $hot=InvokeBuild ($mod.name+'-loose-hot-newout') 'build' $mod.path 'loose'
        Need ($hot.report.cache.sessionCacheDirectory -ieq $session) 'Changing --out changed the MOD partition'
        Need ($hot.report.cache.xmlParse -eq 0 -and $hot.report.cache.instanceCompile -eq 0) 'Unmodified hot build parsed or compiled assets'
        Need (@($cold.report.artifacts).Count -eq @($hot.report.artifacts).Count) 'Hot delivery count differs'
        foreach($artifact in $cold.report.artifacts){
            $relative=[IO.Path]::GetRelativePath((Join-Path $cold.output 'published'),$artifact.Path)
            Need ((Hash $artifact.Path) -ceq (Hash (Join-Path (Join-Path $hot.output 'published') $relative))) 'Cold/hot loose delivery bytes differ'
        }
        $big=InvokeBuild ($mod.name+'-big-hot-newout') 'build' $mod.path 'big'
        Need ($big.report.cache.sessionCacheDirectory -ieq $session -and $big.report.cache.xmlParse -eq 0 -and $big.report.cache.instanceCompile -eq 0) 'BIG did not reuse the same warm stream cache'
        $published=@(Get-ChildItem -LiteralPath (Join-Path $big.output 'published') -Recurse -File)
        Need ($published.Count -eq 2 -and @($published|Where-Object Extension -notin @('.big','.skudef')).Count -eq 0) 'BIG output contains private cache/intermediate streams'
        Need (@(Get-ChildItem -LiteralPath (Join-Path $partition 'temp') -Force).Count -eq 0) 'Package temporary inputs were retained'
        Need (@(Get-ChildItem -LiteralPath $partition -Filter '*.asset' -Recurse -File).Count -gt 0) 'No reusable/intermediate asset was written to the partition'
        $mod.cacheInventory=@(Get-ChildItem -LiteralPath $partition -Recurse -File|ForEach-Object{@{path=[IO.Path]::GetRelativePath($partition,$_.FullName).Replace('\','/');bytes=$_.Length}})
        $mod.cacheInventory|ConvertTo-Json -Depth 4|Set-Content (Join-Path $evidence ('cache-'+$mod.name+'.json'))
    }
    Need ($mods[0].partition -ine $mods[1].partition) 'Different MODs shared a partition'
    $flaCold=InvokeBuild 'fla-direct-cold' 'build' $flaMod 'loose' $true 'apt'
    $flaHot=InvokeBuild 'fla-direct-hot-newout' 'build' $flaMod 'loose' $true 'apt'
    $flaPartition=Split-Path $flaCold.report.cache.sessionCacheDirectory
    Need ($flaHot.report.cache.sessionCacheDirectory -ieq $flaCold.report.cache.sessionCacheDirectory -and $flaHot.report.cache.xmlParse -eq 0 -and $flaHot.report.cache.instanceCompile -eq 0) 'Direct FLA hot build did not reuse the same partition'
    Need (Test-Path -LiteralPath (Join-Path $flaPartition 'generated/fla/flash8-empty.xml') -PathType Leaf) 'Direct FLA wrapper escaped generated/fla'
    foreach($artifact in $flaCold.report.artifacts){
        $relative=[IO.Path]::GetRelativePath((Join-Path $flaCold.output 'published'),$artifact.Path)
        Need ((Hash $artifact.Path) -ceq (Hash (Join-Path (Join-Path $flaHot.output 'published') $relative))) 'Direct FLA cold/hot stream bytes differ'
        if([IO.Path]::GetExtension($artifact.Path) -ieq '.manifest'){
            $manifestText=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($artifact.Path))
            Need ($manifestText.IndexOf('SuperCompilerCache',[StringComparison]::OrdinalIgnoreCase) -lt 0 -and $manifestText.IndexOf((Split-Path $flaPartition -Leaf),[StringComparison]::OrdinalIgnoreCase) -lt 0) 'Direct FLA manifest leaked the private cache source path'
        }
    }
    Start-Sleep -Milliseconds 200
    foreach($mod in $sourceMods){Need ($mod.audit.Errors.IsEmpty) "Source watcher failed: $($mod.name)";Need ($mod.audit.Paths.IsEmpty) "Source write event observed: $($mod.name) $($mod.audit.Paths.ToArray() -join ', ')";Unchanged $mod.path $mod.snapshot}
    foreach($audit in $sdkAudits){
        Need ($audit.Errors.IsEmpty) 'SDK input watcher overflowed or failed'
        foreach($path in $audit.Paths.ToArray()){Need (Within $cacheBase $path) "SDK write escaped SuperCompilerCache: $path"}
    }
    $sdkAfter=Snapshot $sdk $cacheBase
    $state.sdkInputDifferences=@(SnapshotDifferences $sdkBefore $sdkAfter)
    Need ($state.sdkInputDifferences.Count -eq 0) 'SDK input bytes or metadata changed'
    $state.sourceWriteEvents=0;$state.sdkInputWriteEvents=0;$state.sourceSnapshotsUnchanged=$true;$state.status='passed'
} catch {$state.status='failed';$state.error=$_.ToString();throw}
finally {
    $state.sourceAudits=@(foreach($mod in $sourceMods){@{name=$mod.name;writeEvents=$mod.audit.Paths.Count;events=@($mod.audit.Events.ToArray()|Select-Object -First 100);errors=@($mod.audit.Errors.ToArray())}})
    if($sdkAudits){
        if(!$sdkAfter){$sdkAfter=Snapshot $sdk $cacheBase}
        $state.sdkInputDifferences=@(SnapshotDifferences $sdkBefore $sdkAfter)
        $state.sdkRootMetadata.afterLastWriteTimeUtcTicks=[IO.Directory]::GetLastWriteTimeUtc($sdk).Ticks
        $state.sdkRootMetadata.afterAttributes=[int][IO.File]::GetAttributes($sdk)
        $state.sdkAudit=@{watchedInputTrees=$sdkAudits.Count;outsideCache=@($sdkAudits|ForEach-Object{$_.Events.ToArray()}|Where-Object{!(Within $cacheBase $_.Path)}|Select-Object -First 100);errors=@($sdkAudits|ForEach-Object{$_.Errors.ToArray()})}
    }
    foreach($watcher in $watchers){$watcher.Dispose()}
    $state|ConvertTo-Json -Depth 16|Set-Content (Join-Path $evidence 'validation.json')
    @{status=$state.status;sourceCommit=$state.sourceCommit;binarySourceCommit=$state.binarySourceCommit;samples=@($state.samples|ForEach-Object{@{label=$_.label;status=$_.status;exitCode=$_.exitCode;artifactCount=$_.artifactCount;xmlParse=$_.cache.xmlParse;instanceCompile=$_.cache.instanceCompile}});sourceAudits=$state.sourceAudits;sdkAudit=$state.sdkAudit;sdkInputDifferences=$state.sdkInputDifferences;error=$state.error}|ConvertTo-Json -Depth 10|Write-Host
}
