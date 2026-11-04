param([Parameter(Mandatory=$true)][string]$AudioBinDirectory,[string]$OutputDirectory='recovery/evidence/audio/ealayer3-sdk')
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path;Set-Location $root
$bin=(Resolve-Path $AudioBinDirectory).Path
$out=[IO.Path]::GetFullPath($OutputDirectory);New-Item -ItemType Directory -Force $out|Out-Null
$sdk=Join-Path $env:RUNNER_TEMP ('ea-sdk-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$sdk/tools","$sdk/Mods/AudioRecoveryProbe/Data"|Out-Null
Copy-Item SDK/tools/* "$sdk/tools" -Recurse
Copy-Item SDK/Schemas $sdk -Recurse
Copy-Item SDK/SageXml $sdk -Recurse
python recovery/scripts/AudioEaLayer3Fixtures.py "$sdk/Mods/AudioRecoveryProbe/Audio" $root
if($LASTEXITCODE -ne 0){throw 'SDK fixtures failed'}
Copy-Item "$sdk/Mods/AudioRecoveryProbe/Audio/assets.xml" "$sdk/Mods/AudioRecoveryProbe/Data/Mod.xml"
Copy-Item "$bin/BinaryAssetBuilder.AudioCompiler.dll","$bin/NewSdk.EaLayer3.dll" "$sdk/tools" -Force
$csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
& $csc /nologo /target:library /platform:x86 /r:"$sdk/tools/BinaryAssetBuilder.Core.dll" /r:"$sdk/tools/BinaryAssetBuilder.AudioCompiler.dll" /out:"$sdk/tools/AudioEaLayer3SdkTrace.dll" (Resolve-Path recovery/tests/AudioEaLayer3SdkTrace.cs).Path
if($LASTEXITCODE -ne 0){throw 'SDK tracing plugin build failed'}
[xml]$config=Get-Content "$sdk/tools/BinaryAssetBuilder.exe.config" -Raw
$route=@($config.configuration.assetbuilder.plugins.plugin|Where-Object{$_.assetTypes.Split(',') -contains 'AudioFile'})
$route[0].SetAttribute('targetType','AudioEaLayer3SdkTrace, AudioEaLayer3SdkTrace');$route[0].SetAttribute('useBuildCache','false')
if($config.configuration.startup){$config.configuration.RemoveChild($config.configuration.startup)|Out-Null}
$startup=$config.CreateElement('startup');$startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true');$runtime=$config.CreateElement('supportedRuntime');$runtime.SetAttribute('version','v4.0');$startup.AppendChild($runtime)|Out-Null;$config.configuration.AppendChild($startup)|Out-Null;$config.Save("$sdk/tools/BinaryAssetBuilder.exe.config")
$trace=Join-Path $out 'assets';New-Item -ItemType Directory -Force $trace|Out-Null
$previous=$env:EALAYER3_SDK_TRACE;$env:EALAYER3_SDK_TRACE=$trace
try {
    $output=Join-Path $sdk 'builtmods';$mod=Join-Path $sdk 'Mods/AudioRecoveryProbe/Data/Mod.xml'
    $arguments=@("`"$mod`"","/od:`"$output`"","/iod:`"$output`"",'/csc:false','/ls:true','/osh:false','/pc:true','/res:true','/slowclean:true','/ss:true','/art:".;.\Mods\AudioRecoveryProbe\Art;.\Mods;.\Art"','/audio:".;.\Mods\AudioRecoveryProbe\Audio;.\Mods;.\Audio"','/data:".;.\Mods\AudioRecoveryProbe\Data;.\Mods;.\SageXml"')
    $process=Start-Process "$sdk/tools/BinaryAssetBuilder.exe" -ArgumentList ($arguments -join ' ') -WorkingDirectory $sdk -PassThru -RedirectStandardOutput "$out/stdout.log" -RedirectStandardError "$out/stderr.log"
    if(!$process.WaitForExit(180000)){Stop-Process -Id $process.Id -Force;throw 'Official BAB timed out'};$process.Refresh()
    if($process.ExitCode -ne 0){Get-Content "$out/stdout.log" -Tail 60;Get-Content "$out/stderr.log" -Tail 30;throw 'Official BAB failed'}
    $cases=Get-Content "$sdk/Mods/AudioRecoveryProbe/Audio/cases.json" -Raw|ConvertFrom-Json
    if(@(Get-ChildItem $trace -Filter '*.cdata').Count -ne $cases.Count){throw 'Official BAB omitted actual AudioFile callbacks'}
    foreach($case in $cases) {
        foreach($ext in @('runtime','relo','imp','cdata')){
            $component=Join-Path (Split-Path $bin) "jobs1/$($case.id).$ext"
            if((Get-FileHash $component).Hash -cne (Get-FileHash "$trace/$($case.id).$ext").Hash){throw "Official BAB $($case.id).$ext differs from component output"}
        }
    }
    foreach($ext in @('manifest','bin','imp','relo')){
        $file=Join-Path $output "Mods/AudioRecoveryProbe/Data/Mod.$ext"
        if(!(Test-Path $file)){throw "Official BAB omitted mod.$ext"};Copy-Item $file $out
    }
    @{passed=$true;sourceCommit=$env:GITHUB_SHA;runId=$env:GITHUB_RUN_ID;assets=$cases.Count;audioDllSha256=(Get-FileHash "$sdk/tools/BinaryAssetBuilder.AudioCompiler.dll").Hash;nativeDllSha256=(Get-FileHash "$sdk/tools/NewSdk.EaLayer3.dll").Hash;officialBabSha256=(Get-FileHash "$sdk/tools/BinaryAssetBuilder.exe").Hash}|ConvertTo-Json|Set-Content "$out/validation.json"
    Write-Host "PASS official BAB loaded new AudioCompiler and native DLL: $($cases.Count) assets, complete runtime/relocation/imports/CustomData identical"
} finally {$env:EALAYER3_SDK_TRACE=$previous}
