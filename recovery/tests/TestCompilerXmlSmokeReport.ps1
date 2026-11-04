param([string]$Repo=(Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
# Load only the report validator, without executing the Windows build harness.
$tokens=$null;$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $Repo 'recovery/scripts/TestCompilerXmlSmoke.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw 'Smoke harness syntax error'}
$function=@($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Check-XmlSmoke'},$false))
if($function.Count -ne 1){throw 'Expected exactly one smoke report validator'}
. ([scriptblock]::Create($function[0].Extent.Text))
$work=Join-Path ([IO.Path]::GetTempPath()) ('xml-smoke-report-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work|Out-Null
try {
    $state=@{sourceCommit=('a'*40);runId='42'}
    $coverage=@{};1..300|ForEach-Object{$coverage['root'+$_]=4}
    $report=@{succeeded=$true;sourceCommit=$state.sourceCommit;runId=$state.runId;fullMetadataRoutes=0;testedTypes=300;types=300;fixtures=1200;comparisons=3600;completionFixtures=208;completionComparisons=624;platforms=@(0,1,2,99);typeCoverage=$coverage}
    $path=Join-Path $work 'report.json';$transcript=Join-Path $work 'transcript.tsv'
    1..1408|ForEach-Object{"$_`t0`ta`tb`tc"}|Set-Content $transcript
    $report|ConvertTo-Json -Depth 8|Set-Content $path
    Check-XmlSmoke $path $transcript $false
    $report.fullMetadataRoutes=300;$report|ConvertTo-Json -Depth 8|Set-Content $path
    Check-XmlSmoke $path $transcript $true
    foreach($mutation in @('source','coverage','comparisons','transcript')) {
        $bad=$report.Clone();$inputTranscript=$transcript
        switch($mutation) {
            source {$bad.sourceCommit='b'*40}
            coverage {$bad.typeCoverage=$coverage.Clone();$bad.typeCoverage.root1=3}
            comparisons {$bad.comparisons=3599}
            transcript {$inputTranscript=Join-Path $work 'truncated.tsv';Get-Content $transcript -TotalCount 1407|Set-Content $inputTranscript}
        }
        $bad|ConvertTo-Json -Depth 8|Set-Content $path
        $rejected=$false
        try {Check-XmlSmoke $path $inputTranscript $true}catch{$rejected=$true}
        if(!$rejected){throw "Incomplete smoke evidence accepted: $mutation"}
    }
    Write-Host 'PASS XML smoke report: original/product success and four incomplete-evidence rejections'
} finally {Remove-Item -LiteralPath $work -Recurse -Force}
