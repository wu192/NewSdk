param()
$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$repo = (Get-Location).Path
$evidence = Join-Path $repo 'recovery/evidence/xml-powers'
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$out = Join-Path $tempRoot 'ra3-recovered-xmlpowers'
New-Item -ItemType Directory -Force $evidence, $out | Out-Null
$sourceCommit = $env:GITHUB_SHA
if (-not $sourceCommit) { $sourceCommit = (& git rev-parse HEAD).Trim(); if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve sourceCommit' } }
$runId = $env:GITHUB_RUN_ID
if (-not $runId) { $runId = 'local-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') }
$validation = [ordered]@{ succeeded = $false; sourceCommit = $sourceCommit; runId = $runId; scope = 'XmlCompiler powers components; SDK integration pending'; platforms = @(0,1,2,99); modes = @(); status = 'started' }
$validation | ConvertTo-Json -Depth 10 | Set-Content "$evidence/validation.json"
try {
 if ($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x86') { throw 'Run in an x86 VS2022 devshell' }
 $dllHash = (Get-FileHash 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll' -Algorithm SHA256).Hash.ToLowerInvariant()
 $validation['originalDllSha256'] = $dllHash
 if ($dllHash -ne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6') { throw 'Unexpected original XmlCompiler DLL fingerprint' }
 $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
 $sources = @('recovery/src/Native/Relocation.cpp','recovery/src/Native/ScalarParsing.cpp','recovery/src/Native/DataInitializers.cpp','recovery/src/Native/ResourceMarshaling.cpp','recovery/src/Native/EnumBitset.cpp','recovery/src/Native/ObjectFilter.cpp','recovery/src/Native/Xmlpowers.cpp','recovery/src/Bridge/XmlpowersCompiler.cpp')
 foreach ($mode in @('Od','O2')) {
  $modeOut = Join-Path $out $mode
  New-Item -ItemType Directory -Force $modeOut | Out-Null
  Copy-Item SDK/tools/*.dll $modeOut -Force
  & cl /nologo /clr /LD /MD /EHa "/$mode" /fp:precise "/AI$modeOut" "/Fo$modeOut/" "/Fe$modeOut/Recovered.Xmlpowers.dll" @sources *> "$evidence/$mode-build.txt"
  if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-build.txt"; throw "$mode Xmlpowers C++/CLI build failed" }
  & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:System.Core.dll "/r:$modeOut/Recovered.Xmlpowers.dll" "/r:$modeOut/BinaryAssetBuilder.Core.dll" "/out:$modeOut/XmlpowersOracle.exe" (Resolve-Path 'recovery/tests/XmlpowersOracle.cs').Path *> "$evidence/$mode-host-build.txt"
  if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-host-build.txt"; throw "$mode Xmlpowers oracle build failed" }
  '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$modeOut/XmlpowersOracle.exe.config"
  & "$modeOut/XmlpowersOracle.exe" (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path "$evidence/$mode-result.json" $repo *> "$evidence/$mode-oracle.txt"
  $code = $LASTEXITCODE
  Get-Content "$evidence/$mode-oracle.txt"
  if ($code -ne 0) { throw "$mode XmlCompiler original-DLL differential failed" }
  $result = Get-Content "$evidence/$mode-result.json" -Raw | ConvertFrom-Json
  if (-not $result.succeeded -or $result.comparisons -ne 3*$result.fixtures -or $result.rejections -le 0 -or $result.platforms.Count -ne 4) { throw "$mode incomplete oracle result" }
  $validation.modes += [ordered]@{ mode = $mode; succeeded = $true; fixtures = $result.fixtures; comparisons = $result.comparisons; rejections = $result.rejections; independent = $result.independent; platforms = $result.platforms; log = "$mode-oracle.txt" }
  $validation | ConvertTo-Json -Depth 10 | Set-Content "$evidence/validation.json"
 }
 $validation.succeeded = $true
 $validation.status = 'Od and O2 passed original XmlCompiler ProcessInstance differential, independent layout and explicit rejection tests'
} catch {
 $validation.status = $_.Exception.Message
 throw
} finally {
 $validation | ConvertTo-Json -Depth 10 | Set-Content "$evidence/validation.json"
}
