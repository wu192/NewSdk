[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerPath,
    [Parameter(Mandatory=$true)][string]$SourceSdkRoot,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [string]$SummaryPath
)
$ErrorActionPreference='Stop'
$compiler=(Resolve-Path -LiteralPath $CompilerPath).Path
$sourceSdk=(Resolve-Path -LiteralPath $SourceSdkRoot).Path
if(Test-Path -LiteralPath $WorkDirectory){throw "Source diagnostic probe requires a fresh directory: $WorkDirectory"}
$work=(New-Item -ItemType Directory -Force $WorkDirectory).FullName
if(!$SummaryPath){$SummaryPath=Join-Path $work 'summary.json'}
$summary=[ordered]@{succeeded=$false;sourceCommit=$env:GITHUB_SHA;scope='real-Core-schema-invalid-ArmorTemplate';cases=@()}
$events=@();$nativeExit=$null
function Require([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function SourceFile([string]$Value){
    if([string]::IsNullOrWhiteSpace($Value)){return $null}
    $uri=$null
    if([Uri]::TryCreate($Value,[UriKind]::Absolute,[ref]$uri) -and $uri.IsFile){$Value=$uri.LocalPath}
    return [IO.Path]::GetFullPath($Value)
}
function SameSource($Expected,$Actual,[string]$Name){
    foreach($field in @('file','line','column','assetType','assetId')){
        Require ($Expected.Contains($field) -eq $Actual.Contains($field)) "$Name optional $field differs"
        if($Expected.Contains($field)){
            Require ([string]$Expected[$field] -ceq [string]$Actual[$field]) "$Name $field differs"
        }
    }
}
try {
    $sdk=Join-Path $work 'sdk'
    New-Item -ItemType Directory -Force (Join-Path $sdk 'tools'),(Join-Path $sdk 'Schemas')|Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceSdk 'Schemas/xsd') -Destination (Join-Path $sdk 'Schemas') -Recurse
    Copy-Item -LiteralPath (Join-Path $sourceSdk 'tools/BinaryAssetBuilder.exe.config') -Destination (Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config')
    $cases=@(
        @{Name='entry';SourceFile='Mod.xml';Xml='<AssetDeclaration xmlns="uri:ea.com:eala:asset">'+"`n"+'  <ArmorTemplate id="BrokenArmor" Default="not-a-number"/>'+"`n"+'</AssetDeclaration>'},
        @{Name='xi-include';SourceFile='BrokenArmor.xml';Xml='<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude">'+"`n"+'  <!-- The include is line 3; the actual invalid child is line 2. -->'+"`n"+'  <xi:include href="BrokenArmor.xml"/>'+"`n"+'</AssetDeclaration>';Child="`n"+'<ArmorTemplate xmlns="uri:ea.com:eala:asset" id="BrokenIncludedArmor" Default="not-a-number"/>'+"`n"}
    )
    foreach($case in $cases){
    $events=@();$mod=Join-Path $work $case.Name;$output=Join-Path $work ($case.Name+'-output')
    New-Item -ItemType Directory -Force (Join-Path $mod 'Data')|Out-Null
    $entryFile=Join-Path $mod 'Data/Mod.xml';$inputFile=Join-Path (Join-Path $mod 'Data') $case.SourceFile
    [IO.File]::WriteAllText($entryFile,$case.Xml,[Text.UTF8Encoding]::new($false))
    if($case.Contains('Child')){[IO.File]::WriteAllText($inputFile,$case.Child,[Text.UTF8Encoding]::new($false))}
    $sourceBefore=[IO.File]::ReadAllText($inputFile)
    $arguments=@('build','--mod',$mod,'--sdk-root',$sdk,'--out',$output,'--package-format','loose','--package-content','base')
    # A nonzero native exit is the expected compilation result. Its actual
    # diagnostics, rather than an alternative mode or disabled validation, gate
    # this probe. Everything writable is inside the disposable SDK/MOD copy.
    $PSNativeCommandUseErrorActionPreference=$false
    & $compiler @arguments *> (Join-Path $work ($case.Name+'-compiler.log'))
    $nativeExit=$LASTEXITCODE
    Require ($nativeExit -ne 0) 'Invalid ArmorTemplate unexpectedly compiled successfully'
    $events=@(Get-Content -LiteralPath (Join-Path $output 'build.events.jsonl')|Where-Object{$_}|ForEach-Object{ConvertFrom-Json -AsHashtable $_})
    $report=Get-Content -LiteralPath (Join-Path $output 'build-report.json') -Raw|ConvertFrom-Json -AsHashtable
    $errors=@($events|Where-Object{$_.eventType -ceq 'Diagnostic' -and $_.level -ceq 'error'})
    $located=@($errors|Where-Object{
        $_.payload -is [Collections.IDictionary] -and
        [string]::Equals((SourceFile $_.payload.file),$inputFile,[StringComparison]::OrdinalIgnoreCase) -and
        [int]$_.payload.line -eq 2 -and [int]$_.payload.column -gt 0
    })
    Require ($located.Count -gt 0) "Actual Core $($case.Name) error has no reliable $($case.SourceFile) line 2 and positive column"
    $diagnosticEvent=$located[0];$location=$diagnosticEvent.payload
    Require ($location.source -is [Collections.IDictionary]) 'Event payload has no structured source metadata'
    SameSource $location $location.source 'Event/source'
    $matching=@($report.diagnostics|Where-Object{
        $_.code -ceq $diagnosticEvent.diagnosticCode -and $_.message -ceq $diagnosticEvent.message -and
        $_.streamId -ceq $diagnosticEvent.streamId -and $_.stageId -ceq $diagnosticEvent.stageId
    })
    Require ($matching.Count -gt 0) 'Final report has no matching contextual diagnostic'
    SameSource $location $matching[0] 'Event/report'
    Require ($matching[0].source -is [Collections.IDictionary]) 'Final report has no structured source metadata'
    SameSource $location $matching[0].source 'Report/source'
    Require ($report.schemaVersion -ceq '1.0' -and $report.status -ceq 'failed' -and [int]$report.exitCode -eq $nativeExit) 'Source metadata changed the report protocol or failure result'
    Require ($report.buildId -ceq $diagnosticEvent.buildId) 'Located event/report build identity differs'
    $finished=@($events|Where-Object{$_.eventType -ceq 'BuildFinished'})
    Require ($finished.Count -eq 1 -and $finished[0].buildId -ceq $diagnosticEvent.buildId -and $finished[0].payload.status -ceq 'failed') 'Expected exactly one matching failed terminal event'
    Require ([IO.File]::ReadAllText($entryFile) -ceq $case.Xml) 'Compiler changed the MOD entry source'
    Require ([IO.File]::ReadAllText($inputFile) -ceq $sourceBefore) 'Compiler changed the invalid source'
    $summary.cases+=@{name=$case.Name;exitCode=$nativeExit;buildId=$diagnosticEvent.buildId;diagnostic=@{code=$diagnosticEvent.diagnosticCode;file=$location.file;line=$location.line;column=$location.column;source=$location.source}}
    Write-Host "PASS actual Core $($case.Name) schema source: $($case.SourceFile) line 2, positive column, event/report/source and build identity agree."
    }
    $summary.succeeded=$true
} catch {
    $summary.failure=$_.Exception.Message;$summary.failedCase=$case.Name;$summary.exitCode=$nativeExit
    foreach($item in @($events|Where-Object{$_.eventType -ceq 'Diagnostic' -and $_.level -ceq 'error'}|Select-Object -First 8)){
        $message=[string]$item.message
        if($message.Length -gt 2000){$message=$message.Substring(0,2000)}
        @{code=$item.diagnosticCode;message=$message;file=$item.payload.file;line=$item.payload.line;column=$item.payload.column;source=$item.payload.source}|ConvertTo-Json -Depth 6 -Compress|Write-Host
    }
    throw
} finally {
    $parent=Split-Path -Parent $SummaryPath
    if($parent){New-Item -ItemType Directory -Force $parent|Out-Null}
    $text=$summary|ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($SummaryPath,$text,[Text.UTF8Encoding]::new($false))
    if($env:GITHUB_STEP_SUMMARY){$text|Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY}
}
if($summary.succeeded){$global:LASTEXITCODE=0}
