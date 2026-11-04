param([string]$BaselineCommit='45d485d829170dc4fda3b3ce63b6af2759fc7ba0', [string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
if (!$OutputDirectory) { $OutputDirectory="recovery/evidence/audio/xas-performance/$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" }
if (Test-Path $OutputDirectory) { throw "Refusing to overwrite evidence: $OutputDirectory" }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$work=Join-Path $env:RUNNER_TEMP ('audio-xas-performance-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$baseline=(git rev-parse $BaselineCommit).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Missing baseline commit' }
foreach ($name in @('Pcm16Encoder','Xas1Encoder','LoopPcmEncoder')) {
    $path="recovery/src/Managed/Audio/Native/$name.cs"
    $source=git show "${baseline}:$path"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $name" }
    ($source -join "`n").Replace('namespace BinaryAssetBuilder.AudioCompiler','namespace Baseline.AudioCompiler') | Set-Content "$work/Baseline.$name.cs"
    Copy-Item $path "$work/$name.cs"
}
Copy-Item recovery/tests/AudioXasPerformance.cs $work
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net472</TargetFramework>
    <LangVersion>10.0</LangVersion><PlatformTarget>x86</PlatformTarget>
    <TieredCompilation>false</TieredCompilation><ServerGarbageCollection>false</ServerGarbageCollection>
  </PropertyGroup>
</Project>
'@ | Set-Content "$work/AudioXasPerformance.csproj"
dotnet build "$work/AudioXasPerformance.csproj" -c Release -o "$work/bin" --nologo *> "$out/build.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/build.log"; throw 'XAS benchmark build failed' }
& "$work/bin/AudioXasPerformance.exe" "$out/measurements.csv" $baseline *> "$out/measurements.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$out/measurements.log" -Tail 40; throw 'XAS before/after comparison failed' }
Get-Content "$out/measurements.log"
@{baselineCommit=$baseline;runtime='Windows .NET Framework 4.8 / net472 x86 Release';sampleRate=48000;signal='LCG signed16 normalized PCM';repeats=3;allocationMetric='AppDomain.MonitoringTotalAllocatedMemorySize';allocationScope='encoding only; input/compare/explicit GC excluded';measurements=@(Import-Csv "$out/measurements.csv")} | ConvertTo-Json -Depth 8 | Set-Content "$out/measurements.json"
@{sourceCommit=(git rev-parse HEAD).Trim();baselineCommit=$baseline;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='passed';sdkValidated=$false;runtimeScope='Windows net472 x86 Release same-process before/after; original byte oracles run separately'} | ConvertTo-Json | Set-Content "$out/validation.json"
