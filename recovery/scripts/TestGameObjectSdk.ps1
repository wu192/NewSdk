param([int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $root
$work = Join-Path $env:RUNNER_TEMP 'ra3-gameobject-sdk'
$build = Join-Path $work 'build'
$report = Join-Path $root 'recovery/evidence/gameobject-sdk'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
if (Test-Path $report) { Remove-Item $report -Recurse -Force }
New-Item -ItemType Directory -Force $build, $report | Out-Null
@{ succeeded = $false; sourceCommit = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID; status = 'started' } | ConvertTo-Json | Set-Content "$report/validation.json"
# SDK invocation is extracted from the GUI's actual static-data build, not an invented entry.
$script = Get-Content SDK/defaultscript.cs
$start = 0; $end = 0
for ($i=0; $i -lt $script.Count; ++$i) {
    if ($script[$i] -match '^\s*case 5:') { $start=$i }
    if ($start -gt 0 -and $script[$i] -match '^\s*case 6:') { $end=$i; break }
}
if ($start -eq 0 -or $end -eq 0) { throw 'EALAModStudio case 5 launch evidence missing' }
$script[$start..($end-1)] | Set-Content "$report/EALAModStudio-case5.txt"
Copy-Item SDK/tools/*.dll $build
cl /nologo /clr /LD /MD /EHa /O2 /fp:precise /AI"$build" /Fo"$build/" /Fe"$build/Recovered.GameObject.dll" recovery/src/Native/Relocation.cpp recovery/src/Native/ScalarParsing.cpp recovery/src/Native/DataInitializers.cpp recovery/src/Native/ResourceMarshaling.cpp recovery/src/Native/EnumBitset.cpp recovery/src/Native/ObjectFilter.cpp recovery/src/Native/WeaponSlotOrder.cpp recovery/src/Native/WeaponNugget.cpp recovery/src/Native/GameObject.cpp recovery/src/Bridge/GameObjectCompiler.cpp *> "$report/build.txt"
if ($LASTEXITCODE -ne 0) { Get-Content "$report/build.txt"; throw 'GameObject SDK component build failed' }
$csc = "$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
& $csc /nologo /target:library /platform:x86 /r:"$build/Recovered.GameObject.dll" /r:"$build/BinaryAssetBuilder.Core.dll" /out:"$build/Recovered.GameObjectPlugin.dll" (Resolve-Path 'recovery/src/Plugin/GameObjectPlugin.cs') *> "$report/plugin-build.txt"
if ($LASTEXITCODE -ne 0) { Get-Content "$report/plugin-build.txt"; throw 'GameObject plugin build failed' }
& $csc /nologo /platform:x86 /r:System.Core.dll /r:"$build/BinaryAssetBuilder.Core.dll" /out:"$build/GameObjectSdkOracle.exe" (Resolve-Path 'recovery/tests/GameObjectSdkOracle.cs') *> "$report/host-build.txt"
if ($LASTEXITCODE -ne 0) { Get-Content "$report/host-build.txt"; throw 'GameObject SDK oracle build failed' }
'<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0"/></startup></configuration>' | Set-Content "$build/GameObjectSdkOracle.exe.config"
& "$build/GameObjectSdkOracle.exe" schema (Join-Path $root 'SDK') *> "$report/schema.txt"
if ($LASTEXITCODE -ne 0) { Get-Content "$report/schema.txt"; throw 'Complete XSD validation failed' }
$inputs = @{}
foreach ($file in @(Get-ChildItem SDK/Mods/recoverydraw -Recurse -File) + @(Get-ChildItem SDK/Mods/mymod -Recurse -File)) {
    $inputs[$file.FullName.Substring($root.Length + 1).Replace('\','/')] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
}
$inputs | ConvertTo-Json -Depth 5 | Set-Content "$report/input-sha256.json"
Copy-Item SDK/Mods/recoverydraw "$report/recoverydraw-source" -Recurse -Force
$results = @()
# Full mymod also covers the xml:base/xml:lang annotations injected by its XIncludes.
foreach ($modName in @('recoverydraw', 'mymod')) {
    $modReport = Join-Path $report $modName
    New-Item -ItemType Directory -Force $modReport | Out-Null
    $trace = Join-Path $modReport 'trace'
    if (Test-Path $trace) { Remove-Item $trace -Recurse -Force }
    foreach ($mode in @('Original', 'Recovered')) {
        $sdk = Join-Path $work "$modName-$mode"
        Copy-Item (Join-Path $root 'SDK') $sdk -Recurse
        Remove-Item (Join-Path $sdk 'builtmods') -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem $sdk -Directory -Recurse | Where-Object { $_.Name -in @('cache','buildcache','sessioncache') } | Remove-Item -Recurse -Force
        $output = Join-Path $sdk 'builtmods'
        New-Item -ItemType Directory -Force $output | Out-Null
        & "$PSScriptRoot/SetGameObjectPlugin.ps1" -SdkRoot $sdk -PluginDirectory $build -Mode Recovered
        & "$PSScriptRoot/SetGameObjectPlugin.ps1" -SdkRoot $sdk -PluginDirectory $build -Mode Original
        & "$PSScriptRoot/SetGameObjectPlugin.ps1" -SdkRoot $sdk -PluginDirectory $build -Mode $mode
        & "$PSScriptRoot/SetGameObjectPlugin.ps1" -SdkRoot $sdk -PluginDirectory $build -Mode $mode
        $cfg = Join-Path $sdk 'tools/BinaryAssetBuilder.exe.config'
        [xml]$xml = Get-Content -Raw $cfg
        $handlers = @($xml.configuration.assetbuilder.plugins.plugin | Where-Object { $_.assetTypes.Split(',') -contains 'GameObject' })
        $expected = if ($mode -eq 'Recovered') { 'Recovered.GameObject.Plugin, Recovered.GameObjectPlugin' } else { 'BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer' }
        if ($handlers.Count -ne 1 -or $handlers[0].targetType -ne $expected) { throw 'Ambiguous GameObject route' }
        if ($mode -eq 'Recovered' -and $handlers[0].useBuildCache -ne 'false') { throw 'Recovered cache enabled' }
        $startup=$xml.CreateElement('startup');$startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true')
        $runtime=$xml.CreateElement('supportedRuntime');$runtime.SetAttribute('version','v4.0');$startup.AppendChild($runtime)|Out-Null;$xml.configuration.AppendChild($startup)|Out-Null;$xml.Save($cfg)
        "PASS reversible/idempotent $mode route: $expected" | Set-Content "$modReport/$mode-routing.txt"
        Copy-Item $cfg "$modReport/$mode.config"
        $env:RA3_RECOVERED_GAMEOBJECT_TRACE = if ($mode -eq 'Recovered') { $trace } else { $null }
        $mod = Join-Path $sdk "Mods/$modName/Data/Mod.xml"
        $arguments = @("`"$mod`"", "/od:`"$output`"", "/iod:`"$output`"", '/csc:false', '/ls:true', '/osh:false', '/pc:true', '/res:true', '/slowclean:true', '/ss:true', "/art:`".;.\Mods\$modName\Art;.\Mods;.\Art`"", "/audio:`".;.\Mods\$modName\Audio;.\Mods;.\Audio`"", "/data:`".;.\Mods\$modName\Data;.\Mods;.\SageXml`"")
        $arguments -join ' ' | Set-Content "$modReport/$mode-arguments.txt"
        $p=Start-Process (Join-Path $sdk 'tools/BinaryAssetBuilder.exe') -ArgumentList ($arguments -join ' ') -WorkingDirectory $sdk -PassThru -RedirectStandardOutput "$modReport/$mode-stdout.txt" -RedirectStandardError "$modReport/$mode-stderr.txt"
        if (!$p.WaitForExit($TimeoutSeconds*1000)) { Stop-Process -Id $p.Id -Force;throw "$modName/$mode timed out" }
        $p.Refresh()
        if ($p.ExitCode -ne 0) { Get-Content "$modReport/$mode-stdout.txt";Get-Content "$modReport/$mode-stderr.txt";throw "$modName/$mode SDK failed $($p.ExitCode)" }
        foreach($ext in @('bin','manifest','imp','relo')) {
            $file=Join-Path $output "mods/$modName/data/mod.$ext"
            if(!(Test-Path $file)){throw "Missing $file"}
            Copy-Item $file "$modReport/$mode-mod.$ext"
            $results+=@{mod=$modName;mode=$mode;file="mod.$ext";bytes=(Get-Item $file).Length;sha256=(Get-FileHash $file -Algorithm SHA256).Hash}
        }
    }
    $env:RA3_RECOVERED_GAMEOBJECT_TRACE=$null
    foreach($ext in @('bin','manifest','imp','relo')) {
        if((Get-FileHash "$modReport/Original-mod.$ext").Hash -ne (Get-FileHash "$modReport/Recovered-mod.$ext").Hash){throw "$modName mod.$ext differs"}
    }
    & "$build/GameObjectSdkOracle.exe" buffers "$build/BinaryAssetBuilder.Tokenizer.dll" $trace "$modReport/Recovered-mod.bin" $modName *> "$modReport/buffers.txt"
    if($LASTEXITCODE -ne 0){Get-Content "$modReport/buffers.txt";throw 'SDK buffer replay failed'}
    $isolated=Join-Path $work "isolated-$modName"
    New-Item -ItemType Directory -Force $isolated|Out-Null
    Get-ChildItem $build -File|Where-Object{$_.Extension -in @('.dll','.exe','.config') -and $_.Name -notin @('BinaryAssetBuilder.Tokenizer.dll','BinaryAssetBuilder.XmlCompiler.dll')}|Copy-Item -Destination $isolated
    & "$isolated/GameObjectSdkOracle.exe" isolated "$isolated/Recovered.GameObjectPlugin.dll" $trace "$modReport/Recovered-mod.bin" $modName *> "$modReport/isolated.txt"
    if($LASTEXITCODE -ne 0){Get-Content "$modReport/isolated.txt";throw 'Original-free replay failed'}
    Get-Content "$modReport/buffers.txt", "$modReport/isolated.txt"
}
$results|ConvertTo-Json -Depth 5|Set-Content "$report/files.json"
@{succeeded=$true;sourceCommit=$env:GITHUB_SHA;runId=$env:GITHUB_RUN_ID;scope='GameObject formal route, seven recoverydraw probes including five new Draw modules/default/inheritance and full mymod; complete XSD; clean original/recovered builds; all four outputs byte-identical; actual three buffers replay; original-free replay; GUI defaultscript case5 launch'}|ConvertTo-Json|Set-Content "$report/validation.json"
