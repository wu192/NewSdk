#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$SdkRoot,
    [Parameter(Mandatory=$true)][string]$ModRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$sdk=(Resolve-Path -LiteralPath $SdkRoot).Path
$mod=(Resolve-Path -LiteralPath $ModRoot).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Refusing to replace identity diagnostic evidence'}
foreach($inputRoot in @($runtime,$sdk,$mod)){
    if($out.Equals($inputRoot,[StringComparison]::OrdinalIgnoreCase) -or $out.StartsWith($inputRoot.TrimEnd([char[]]'\/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Diagnostic output must remain outside SDK and accepted runtime'
    }
}
$expected=@{
    'NewModCompiler.exe'='3f714d9da359540f56d5d7d3211cdf6829f2a5d59978f29835da9b4b52c01de3'
    'NewModCompiler.exe.config'='94eedb0cd0150b90c8115aaee53f64ab0523b39bd67887991b1b62679a752e68'
    'antlr.runtime.dll'='febdc86d6299a550c6ec87050b0cbf5205e821851e75494c699c17ce8ba2c62a'
    'BinaryAssetBuilder.TextureCompiler.dll'='076509e81077fcd916ef884949caa4d9c1817366ffd5291e49bab44dfd7eae48'
    'BinaryAssetBuilder.TextureCompiler.dll.patch.json'='2f5b343670ace5ed109b1f6308cb669e254860475e7433f12635e5172cd80cd1'
}
function VerifyRuntime {
    $files=@(Get-ChildItem -LiteralPath $runtime -File -Force)
    if($files.Count -ne 5){throw 'Identity diagnostic requires the exact accepted Release five-file runtime'}
    foreach($file in $files){
        if(!$expected.ContainsKey($file.Name) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected[$file.Name]){
            throw "Accepted Release file differs: $($file.Name)"
        }
    }
}
VerifyRuntime
$out=(New-Item -ItemType Directory -Path $out).FullName
$helperDirectory=(New-Item -ItemType Directory -Path (Join-Path $out 'helper')).FullName
$exe=Join-Path $helperDirectory 'IdentityProfile.exe'
$source=Join-Path $root 'recovery/tests/CompilerCacheIdentityProfile.cs'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $csc /nologo /platform:x86 /optimize+ /langversion:5 /r:System.Web.Extensions.dll ("/out:"+$exe) $source *> (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0){throw 'x86 CLR4 identity helper compilation failed; see compile.log'}
'<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>'|Set-Content -LiteralPath "$exe.config" -Encoding utf8
$profilePath=Join-Path $out 'profile.json'
$info=[Diagnostics.ProcessStartInfo]::new($exe)
$info.WorkingDirectory=$sdk
$info.UseShellExecute=$false
$info.RedirectStandardOutput=$true
$info.RedirectStandardError=$true
foreach($argument in @($runtime,$sdk,$profilePath)){$info.ArgumentList.Add($argument)}
$process=[Diagnostics.Process]::new();$process.StartInfo=$info
$stdout=$null;$stderr=$null;$started=$false;$timedOut=$false;$exitCode=$null
try{
    $started=$process.Start()
    if(!$started){throw 'Identity helper process could not start'}
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(!$process.WaitForExit(20000)){
        $timedOut=$true
        $process.Kill($true)
        if(!$process.WaitForExit(5000)){throw 'Identity helper did not stop after bounded termination'}
    }
    $exitCode=$process.ExitCode
}finally{
    if($started -and !$process.HasExited){try{$process.Kill($true);$null=$process.WaitForExit(5000)}catch{}}
    foreach($entry in @(@($stdout,'stdout.log'),@($stderr,'stderr.log'))){
        $text=if($entry[0] -and $entry[0].Wait(3000)){$entry[0].GetAwaiter().GetResult()}else{'Helper output incomplete after bounded cleanup.'}
        [IO.File]::WriteAllText((Join-Path $out $entry[1]),$text)
    }
    $process.Dispose()
}
if(Test-Path -LiteralPath $profilePath){$profile=Get-Content -LiteralPath $profilePath -Raw|ConvertFrom-Json -AsHashtable}
else{$profile=@{schemaVersion=1;status=$(if($timedOut){'timed-out'}else{'failed'});error='Helper did not produce a complete profile'}}
$profile.binarySourceCommit='39039961847cd1ca9cb93743fd51741a4e5c9f49'
$profile.sourceRunId='40814401237'
$profile.productionMethodDirectlyMeasured=$false
$profile.preflightMeasured=$false
$profile.method='source-equivalent filesystem microbenchmark, NOT production method invocation'
$profile.diagnosticRunId=$env:GITHUB_RUN_ID
$profile.diagnosticRunAttempt=$env:GITHUB_RUN_ATTEMPT
$profile.diagnosticSourceCommit=(& git -C $root rev-parse HEAD).Trim()
$profile.helperSourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
$profile.helperExeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$profile.helperExitCode=$exitCode;$profile.timedOut=$timedOut
$profile.runtimeDirectory=$runtime;$profile.sdkRoot=$sdk;$profile.modRoot=$mod
$profile|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $profilePath -Encoding utf8
VerifyRuntime
if($timedOut -or $exitCode -ne 0 -or $profile.status -cne 'passed'){throw 'Identity diagnostic failed or exceeded its 20-second process budget'}
Get-Content -LiteralPath $profilePath -Raw|Write-Output
