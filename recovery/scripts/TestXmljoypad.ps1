param([string]$EvidenceDirectory='recovery/evidence/xml-joypad')
$ErrorActionPreference='Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
$evidence=(New-Item -ItemType Directory -Force $EvidenceDirectory).FullName
$tempRoot=if($env:RUNNER_TEMP){$env:RUNNER_TEMP}else{[IO.Path]::GetTempPath()}
$out=(New-Item -ItemType Directory (Join-Path $tempRoot ('joypad-component-'+[Guid]::NewGuid().ToString('N')))).FullName
$summary=@{succeeded=$false;sourceCommit=(& git rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;scope='11 independent Joypad roots, all 34 runtime polymorphic Data layouts; original raw three buffers, Od/O2, platforms0/1/2/99';platforms=@(0,1,2,99);modes=@();status='started'}
try{
    python recovery/scripts/PrepareJoypadFixtures.py --output "$out/fixtures"
    if($LASTEXITCODE -ne 0){throw 'Joypad fixture preparation failed'}
    [xml]$mod=[IO.File]::ReadAllText("$out/fixtures/mod/Data/Mod.xml")
    $mod.Schemas.XmlResolver=[Xml.XmlUrlResolver]::new()
    $mod.Schemas.Add('uri:ea.com:eala:asset',(Resolve-Path 'SDK/Schemas/xsd/CnC3Types.xsd').Path)|Out-Null
    $mod.Validate($null)
    Copy-Item "$out/fixtures/fixtures.json" "$evidence/fixtures.json"
    Copy-Item SDK/tools/*.dll $out
    $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
    $sources=@('recovery/src/Native/Relocation.cpp','recovery/src/Native/ScalarParsing.cpp','recovery/src/Native/DataInitializers.cpp','recovery/src/Native/EnumBitset.cpp','recovery/src/Native/ResourceMarshaling.cpp','recovery/src/Native/ObjectFilter.cpp','recovery/src/Native/Xmljoypad.cpp','recovery/src/Bridge/XmljoypadCompiler.cpp')
    foreach($mode in @('Od','O2')){
        $modeOut=(New-Item -ItemType Directory "$out/$mode").FullName
        Copy-Item "$out/*.dll" $modeOut
        & cl /nologo /clr /LD /MD /EHa /std:c++17 "/$mode" /fp:precise "/AI$modeOut" "/Fo$modeOut/" "/Fe$modeOut/Recovered.Xmljoypad.dll" @sources *> "$evidence/$mode-build.txt"
        if($LASTEXITCODE -ne 0){Get-Content "$evidence/$mode-build.txt" -Tail 70;throw "$mode Joypad component build failed"}
        $testSources=@('recovery/tests/PluginSchemaBoundary.cs','recovery/tests/XmljoypadOracle.cs'|ForEach-Object{(Resolve-Path $_).Path})
        & $csc /nologo /codepage:65001 /platform:x86 /r:System.Core.dll /r:System.Web.Extensions.dll "/r:$modeOut/Recovered.Xmljoypad.dll" "/r:$modeOut/BinaryAssetBuilder.Core.dll" "/out:$modeOut/XmljoypadOracle.exe" @testSources *> "$evidence/$mode-host-build.txt"
        if($LASTEXITCODE -ne 0){Get-Content "$evidence/$mode-host-build.txt";throw "$mode Joypad oracle host build failed"}
        '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>'|Set-Content "$modeOut/XmljoypadOracle.exe.config"
        & "$modeOut/XmljoypadOracle.exe" (Resolve-Path 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll').Path "$evidence/$mode-result.json" "$out/fixtures/cases.json" *> "$evidence/$mode-oracle.txt"
        if($LASTEXITCODE -ne 0){Get-Content "$evidence/$mode-oracle.txt" -Tail 45;throw "$mode Joypad original differential failed"}
        Get-Content "$evidence/$mode-oracle.txt"
        $result=Get-Content "$evidence/$mode-result.json" -Raw|ConvertFrom-Json
        if(!$result.succeeded -or $result.rootNames.Count -ne 11 -or $result.platforms.Count -ne 4){throw "$mode Joypad matrix incomplete"}
        foreach($p in @(0,1,2,99)){
            $row=@($result.platforms|Where-Object{$_.platform -eq $p})
            if($row.Count -ne 1 -or $row[0].fixtures -le 34 -or $row[0].arrayComparisons -ne 6*$row[0].fixtures -or $row[0].rejections -lt 33){throw "$mode Joypad platform$p incomplete"}
        }
        $summary.modes+=@{mode="/$mode";result=$result}
    }
    $summary.succeeded=$true;$summary.status='passed'
}catch{$summary.status='failed';$summary.error=$_.ToString();throw}
finally{$summary|ConvertTo-Json -Depth 12|Set-Content "$evidence/validation.json" -Encoding utf8}
