$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$out = Join-Path $(if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }) 'ra3-recovered-xml-attributes'
$evidence = 'recovery/evidence/xml-attributes'
New-Item -ItemType Directory -Force $out, $evidence | Out-Null
$sourceCommit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git rev-parse HEAD).Trim() }
$runId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { 'local-' + [Guid]::NewGuid().ToString('N') }
$result = @{ succeeded=$false; sourceCommit=$sourceCommit; runId=$runId; platforms=@(0,1,2,99); modes=@(); scope='AttributeModifier, UpgradeTemplate and LogicCommandSet components including dependency ObjectFilter; SDK not integrated' }
$result | ConvertTo-Json -Depth 8 | Set-Content "$evidence/validation.json"
try {
    $dll = (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path
    $result.sourceBlob = '2d2f471c8008b7c2e1f78e39752d0726da395cea'
    $result.originalDllSha256 = (Get-FileHash -Algorithm SHA256 $dll).Hash.ToLowerInvariant()
    if ($result.originalDllSha256 -ne 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6') { throw 'Original XmlCompiler DLL fingerprint differs from recovered evidence' }
    Copy-Item SDK/tools/*.dll $out
    $csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    foreach ($mode in @('Od','O2')) {
        cl /nologo /clr /LD /MD /EHa "/$mode" /fp:precise /AI"$out" /Fo"$out/" /Fe"$out/Recovered.Xmlattributes.dll" recovery/src/Native/Relocation.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/Xmlattributes.cpp recovery/src/Bridge/XmlattributesCompiler.cpp *> "$evidence/$mode-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-build.txt"; throw "$mode C++/CLI build failed" }
        & $csc (Resolve-Path 'recovery/tests/PluginSchemaBoundary.cs') /nologo /platform:x86 /r:"$out/Recovered.Xmlattributes.dll" /r:"$out/BinaryAssetBuilder.Core.dll" /out:"$out/XmlattributesOracle.exe" (Resolve-Path 'recovery/tests/XmlattributesOracle.cs') *> "$evidence/$mode-host-build.txt"
        if ($LASTEXITCODE -ne 0) { Get-Content "$evidence/$mode-host-build.txt"; throw "$mode oracle build failed" }
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>' | Set-Content "$out/XmlattributesOracle.exe.config"
        & "$out/XmlattributesOracle.exe" (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path *> "$evidence/$mode-oracle.txt"
        $code = $LASTEXITCODE
        Get-Content "$evidence/$mode-oracle.txt"
        $summaries = @(Get-Content "$evidence/$mode-oracle.txt" | Where-Object { $_ -match '^SUMMARY platform=' })
        $result.modes += @{ mode=$mode; succeeded=($code -eq 0); exitCode=$code; log="$mode-oracle.txt"; summaries=$summaries }
        if ($code -ne 0 -or $summaries.Count -ne 4) { throw "$mode ProcessInstance differential/layout/rejection test failed" }
    }
    $result.succeeded = $true
} catch {
    $result.error = $_.ToString()
    throw
} finally {
    $result | ConvertTo-Json -Depth 8 | Set-Content "$evidence/validation.json"
}
