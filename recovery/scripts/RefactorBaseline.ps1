param(
 [Parameter(Mandatory=$true)][string]$SourceRoot,
 [Parameter(Mandatory=$true)][string]$ModSourcePath,
 [int]$TimeoutSeconds=1200
)
$ErrorActionPreference='Stop'
$harnessRoot=(Resolve-Path "$PSScriptRoot/../..").Path
$root=(Resolve-Path $SourceRoot).Path
$source=(Resolve-Path $ModSourcePath).Path
$baselineCommit='87cf54e254dfb44a129da087316fd04806ed2b10'
$modCommit='068643219fded09a05fb7b75c6507b769c430316'
if ((git -C $root rev-parse HEAD).Trim() -ne $baselineCommit -or $LASTEXITCODE -ne 0) { throw 'Baseline source must be the fixed pre-refactor commit' }
if ((git -C $source rev-parse HEAD).Trim() -ne $modCommit -or $LASTEXITCODE -ne 0) { throw 'MOD checkout is not pinned' }
if (@(git -C $root status --porcelain --untracked-files=all).Count -or @(git -C $source status --porcelain --untracked-files=all -- .).Count) { throw 'Pinned source checkouts must be clean' }
if (!$env:RUNNER_TEMP -or !$env:GITHUB_RUN_ID) { throw 'An identified Windows Actions runner is required' }
Set-Location $root
$work="$env:RUNNER_TEMP/upheaval-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
$report="$harnessRoot/recovery/evidence/refactor/baseline/$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"
$build="$work/plugins"
if (Test-Path $report) { throw 'Refusing to overwrite immutable evidence' }
New-Item -ItemType Directory -Force $build,$report | Out-Null
$result=[ordered]@{originalBaselinePassed=$false;sourceCommit=$baselineCommit;harnessCommit=$env:GITHUB_SHA;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;succeeded=$false;selectedSteps=@(3,4,5);modSourceCommit=$modCommit;modInputTree=(git -C $source rev-parse HEAD:SDK/Mods/Upheaval).Trim();scope='Pre-refactor baseline: original BAB versus combined recovered XML/Tokenizer/W3X/Core/Remote/EALABuild/ExpressionEval/Verifier/MvpXml/Audio; Utility/Hash/Metrics/Texture/ANTLR remain original';finalExeIntegrated=$false;publicCases='SDK/Mods/Upheaval/Data/Mod/PublicCases/manifest.json';builds=@();invocations=@();outputs=@();comparisons=@()}
$csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
function Check([string]$Label) {
 if($LASTEXITCODE -ne 0){Get-Content "$report/$Label.log";throw "$Label failed: $LASTEXITCODE"}
}
function Build-Plugin([string]$Name,[string[]]$Sources,[string]$Plugin,[string]$PluginOutput) {
 $paths=@($Sources|ForEach-Object{(Resolve-Path $_).Path})
 & cl.exe /nologo /clr /LD /MD /EHa /std:c++17 /O2 /fp:precise "/AI$build" "/Fo$build/" "/Fe$build/$Name.dll" @paths *> "$report/$Name.log"
 Check $Name
 & $csc /nologo /target:library /platform:x86 "/r:$build/BinaryAssetBuilder.Core.dll" "/r:$build/$Name.dll" "/out:$build/$PluginOutput.dll" (Resolve-Path $Plugin).Path *> "$report/$PluginOutput.log"
 Check $PluginOutput
}
try {
 if (Test-Path SDK/Mods/Upheaval) { Remove-Item SDK/Mods/Upheaval -Recurse -Force }
 New-Item SDK/Mods -ItemType Directory -Force | Out-Null
 Copy-Item $source SDK/Mods/Upheaval -Recurse
 $result['sdkToolsTree']=(git rev-parse HEAD:SDK/tools).Trim()
 $result['schemasTree']=(git rev-parse HEAD:SDK/Schemas).Trim()
 $result['sourceTrees']=[ordered]@{managed=(git rev-parse HEAD:recovery/src/Managed).Trim();native=(git rev-parse HEAD:recovery/src/Native).Trim();bridge=(git rev-parse HEAD:recovery/src/Bridge).Trim();audio=(git rev-parse HEAD:src/AudioCompiler).Trim()}
 $result['toolchain']=[ordered]@{cl=((& cl.exe 2>&1 | Out-String).Trim());dotnet=(& dotnet --version);os=[Environment]::OSVersion.ToString();csc=$csc}
 @(Get-ChildItem SDK/tools -File|Sort-Object Name|ForEach-Object{@{path=$_.Name;sha256=(Get-FileHash $_.FullName).Hash;bytes=$_.Length}})|ConvertTo-Json -Depth 5|Set-Content "$report/sdk-tools.json"
 @(Get-ChildItem SDK/Schemas -File -Recurse|Sort-Object FullName|ForEach-Object{@{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName).Hash;bytes=$_.Length}})|ConvertTo-Json -Depth 5|Set-Content "$report/schemas.json"
 @(Get-ChildItem SDK/Mods/Upheaval -File -Recurse|Sort-Object FullName|ForEach-Object{
  @{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName).Hash}
 })|ConvertTo-Json -Depth 5|Set-Content "$report/inputs.json"
 Copy-Item SDK/defaultscript.cs "$report/defaultscript.cs"
 Copy-Item SDK/tools/*.dll $build
 $targets=@(@{step=3;folder='AptUI';filter='*.xml'},@{step=4;folder='AdditionalMaps';filter='mapmetadata_*.xml'},@{step=5;folder='';filter='Mod.xml'})
 foreach($mode in @('Original','Recovered')){
  if($mode -eq 'Recovered'){
   if(!$result.originalBaselinePassed){throw 'Original SDK gate has not passed'}
 $common=@('Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter')|ForEach-Object{"recovery/src/Native/$_.cpp"}
 $xmlSources=@($common)+@(Get-ChildItem recovery/src/Native/Xml*.cpp -File|Where-Object Name -notmatch Draft|Sort-Object Name|ForEach-Object FullName)+@(Get-ChildItem recovery/src/Bridge/Xml*Compiler.cpp -File|Where-Object Name -notmatch Draft|Sort-Object Name|ForEach-Object FullName)
 Build-Plugin 'Recovered.XmlCompilerPriority' $xmlSources 'recovery/src/Plugin/XmlPriorityPlugin.cs' 'Recovered.XmlPriority.Plugin'
 Build-Plugin 'Recovered.GameObject' (@($common)+@('recovery/src/Native/WeaponSlotOrder.cpp','recovery/src/Native/WeaponNugget.cpp','recovery/src/Native/GameObject.cpp','recovery/src/Bridge/GameObjectCompiler.cpp')) 'recovery/src/Plugin/GameObjectPlugin.cs' 'Recovered.GameObjectPlugin'
 Build-Plugin 'Recovered.Weapon' (@($common)+@('recovery/src/Native/WeaponNugget.cpp','recovery/src/Native/WeaponTemplate.cpp','recovery/src/Bridge/WeaponCompiler.cpp')) 'recovery/src/Plugin/WeaponPlugin.cs' 'Recovered.WeaponPlugin'
 Build-Plugin 'Recovered.Armor' @('recovery/src/Native/Relocation.cpp','recovery/src/Native/ScalarParsing.cpp','recovery/src/Native/ArmorTemplate.cpp','recovery/src/Bridge/ArmorCompiler.cpp') 'recovery/src/Plugin/ArmorPlugin.cs' 'Recovered.Compiler'
 & "$root/recovery/scripts/BuildW3xPlugin.ps1" -OutputDirectory "$work/w3x"
 Copy-Item "$work/w3x/Recovered.W3x*.dll" $build
 foreach($name in @('Core','Remote','EALABuild','ExpressionEval','GameDataVerifier')){
  $assembly=if($name -eq 'EALABuild'){$name}else{"BinaryAssetBuilder.$name"}
  $output="$work/managed-$name"
  & dotnet build "recovery/src/Managed/$name/$assembly.csproj" -c Release -p:Platform=AnyCPU -p:PlatformTarget=x86 -o $output --nologo *> "$report/$name-build.log"
  Check "$name-build"
  $result.builds+=@{assembly=$assembly;file="$output/$assembly.dll";sha256=(Get-FileHash "$output/$assembly.dll").Hash}
 }
  }
  if($mode -eq 'Recovered') {
   # Core's source dependency must be deployed explicitly: the historical joint
   # harness accidentally left original Mvp.Xml in place.
   $mvp="$work/managed-Core/Mvp.Xml.dll"
   if(!(Test-Path $mvp)){throw 'Recovered Core MvpXml dependency missing'}
   $result.builds+=@{assembly='Mvp.Xml';file=$mvp;sha256=(Get-FileHash $mvp).Hash}
   & dotnet build src/AudioCompiler/BinaryAssetBuilder.AudioCompiler.csproj -c Release -p:Platform=AnyCPU -p:PlatformTarget=x86 -p:AssemblyName=Recovered.AudioCompiler -o "$work/audio" --nologo *> "$report/audio-build.log"
   Check 'audio-build'
   $audio="$work/audio/Recovered.AudioCompiler.dll"
   $result.builds+=@{assembly='Recovered.AudioCompiler';file=$audio;sha256=(Get-FileHash $audio).Hash}
  }
  # Both modes use identical absolute MOD, SDK and output paths. Recreate the
  # disposable SDK completely to prevent generated input or cache leakage.
  $sdk="$work/SDK"
  if(Test-Path $sdk){Remove-Item $sdk -Recurse -Force}
  New-Item -ItemType Directory -Force $sdk|Out-Null
  Get-ChildItem SDK -Force|Where-Object Name -notin @('Mods','builtmods')|Copy-Item -Destination $sdk -Recurse
  New-Item -ItemType Directory -Force "$sdk/Mods"|Out-Null
  Copy-Item SDK/Mods/Upheaval "$sdk/Mods/Upheaval" -Recurse
  & "$root/recovery/scripts/SetXmlPriorityPlugin.ps1" -SdkRoot $sdk -PluginDirectory $build -Mode $mode
  # Replace all three Tokenizer routes atomically. Standalone setters require a
  # remaining original Tokenizer entry and cannot remove its last type jointly.
  $jointCfg="$sdk/tools/BinaryAssetBuilder.exe.config"
  [xml]$joint=Get-Content $jointCfg -Raw
  $plugins=$joint.configuration.assetbuilder.plugins
  $tokenizer=@($plugins.plugin|Where-Object{$_.targetType -eq 'BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer'})
  if($tokenizer.Count -ne 1 -or (@($tokenizer[0].assetTypes.Split(',')|Sort-Object) -join ',') -ne 'ArmorTemplate,GameObject,WeaponTemplate'){throw 'Unexpected original Tokenizer routes'}
  $plugins.RemoveChild($tokenizer[0])|Out-Null
  $routes=@(
   @{type='GameObject';target='Recovered.GameObject.Plugin, Recovered.GameObjectPlugin';dlls=@('Recovered.GameObjectPlugin.dll','Recovered.GameObject.dll')},
   @{type='WeaponTemplate';target='Recovered.Weapon.Plugin, Recovered.WeaponPlugin';dlls=@('Recovered.WeaponPlugin.dll','Recovered.Weapon.dll')},
   @{type='ArmorTemplate';target='Recovered.Compiler.Plugin, Recovered.Compiler';dlls=@('Recovered.Compiler.dll','Recovered.Armor.dll')}
  )
  foreach($route in $routes){
   $entry=$joint.CreateElement('plugin')
   $entry.SetAttribute('assetTypes',$route.type)
   $entry.SetAttribute('targetType',$(if($mode -eq 'Recovered'){$route.target}else{'BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer'}))
   $entry.SetAttribute('enabled','true');$entry.SetAttribute('useBuildCache','false')
   $plugins.AppendChild($entry)|Out-Null
   if($mode -eq 'Recovered'){foreach($dll in $route.dlls){Copy-Item "$build/$dll" "$sdk/tools/$dll" -Force}}
  }
  $seen=@{}
  foreach($entry in @($plugins.plugin)){
   foreach($type in $entry.assetTypes.Split(',')){
    $type=$type.Trim()
    if($seen.ContainsKey($type)){throw "Duplicate plugin route: $type"}
    $seen[$type]=$entry.targetType
   }
  }
  foreach($route in $routes){
   $expected=if($mode -eq 'Recovered'){$route.target}else{'BinaryAssetBuilder.Tokenizer.Plugin, BinaryAssetBuilder.Tokenizer'}
   if($seen[$route.type] -ne $expected){throw "Incorrect joint route: $($route.type)"}
  }
  $joint.Save($jointCfg)
  $cfg="$sdk/tools/BinaryAssetBuilder.exe.config"
  [xml]$config=Get-Content $cfg -Raw
  $route=@($config.configuration.assetbuilder.plugins.plugin|Where-Object{$_.assetTypes.Split(',') -contains 'W3DAnimation'})
  if($route.Count -ne 1 -or $route[0].assetTypes -ne 'W3DAnimation,W3DMesh'){throw 'Unexpected W3X route'}
  $route[0].SetAttribute('useBuildCache','false')
  if($mode -eq 'Recovered'){
   $route[0].SetAttribute('targetType','Recovered.Compiler.W3x.NativePlugin, Recovered.W3x.Plugin')
   Copy-Item "$build/Recovered.W3x*.dll" "$sdk/tools"
   foreach($row in $result.builds){
    $destination=if($row.assembly -eq 'EALABuild'){"$sdk/EALABuild.dll"}else{"$sdk/tools/$($row.assembly).dll"}
    Copy-Item $row.file $destination -Force
    if((Get-FileHash $destination).Hash -ne $row.sha256){throw 'Recovered installation mismatch'}
   }
  }
  & $csc /nologo /target:library /platform:x86 /r:System.Core.dll /r:System.Xml.dll "/r:$sdk/tools/BinaryAssetBuilder.Core.dll" "/out:$sdk/tools/AudioSdkProbe.dll" (Resolve-Path recovery/tests/AudioSdkTracingPlugin.cs).Path *> "$report/$mode-audio-tracer.log"
  Check "$mode-audio-tracer"
  $audioRoutes=@($config.configuration.assetbuilder.plugins.plugin|Where-Object{$_.assetTypes.Split(',') -contains 'AudioFile'})
  if($audioRoutes.Count -ne 1){throw 'Unexpected audio route'}
  $audioRoutes[0].SetAttribute('targetType','AudioSdkTracingPlugin, AudioSdkProbe')
  $audioRoutes[0].SetAttribute('useBuildCache','false')
  $env:AUDIO_SDK_IMPLEMENTATION=$mode;$env:AUDIO_SDK_TRACE="$report/$mode-audio-calls.tsv"
  if($mode -eq 'Recovered'){Remove-Item "$sdk/tools/BinaryAssetBuilder.AudioCompiler.dll" -Force}
  $startup=$config.configuration.startup
  if(!$startup){$startup=$config.CreateElement('startup');$config.configuration.AppendChild($startup)|Out-Null}
  $startup.RemoveAll();$startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true')
  $runtime=$config.CreateElement('supportedRuntime');$runtime.SetAttribute('version','v4.0');$startup.AppendChild($runtime)|Out-Null
  $config.Save($cfg)
  Copy-Item $cfg "$report/$mode.config"
  Get-ChildItem "$sdk/tools" -Filter *.dll|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash $_.FullName).Hash}}|ConvertTo-Json|Set-Content "$report/$mode-dlls.json"
  $env:RA3_RECOVERED_XML_PRIORITY_TRACE=if($mode -eq 'Recovered'){"$report/trace/xml"}else{$null}
  $env:RA3_RECOVERED_GAMEOBJECT_TRACE=if($mode -eq 'Recovered'){"$report/trace/gameobject"}else{$null}
  $env:RECOVERED_W3X_TRACE_DIRECTORY=if($mode -eq 'Recovered'){"$report/trace/w3x"}else{$null}
  $built="$sdk/builtmods"
  foreach($target in $targets){
   $files=@(Get-ChildItem "$sdk/Mods/Upheaval/Data/$($target.folder)" -Filter $target.filter -File|Sort-Object Name)
   if(!$files.Count){throw "Missing inputs for step $($target.step)"}
   foreach($file in $files){
    $label="$mode-$($target.step)-$($file.BaseName)"
    $arguments=@("$([char]34)$($file.FullName)$([char]34)","/od:$([char]34)$built$([char]34)","/iod:$([char]34)$built$([char]34)",'/csc:false','/ls:true','/osh:false','/pc:true','/res:true','/slowclean:true','/ss:true','/art:".;.\Mods\Upheaval\Art;.\Mods;.\Art"','/audio:".;.\Mods\Upheaval\Audio;.\Mods;.\Audio"','/data:".;.\Mods\Upheaval\Data;.\Mods;.\SageXml"') -join ' '
    $stdout="$report/$label-stdout.txt";$stderr="$report/$label-stderr.txt"
    $p=Start-Process "$sdk/tools/BinaryAssetBuilder.exe" -ArgumentList $arguments -WorkingDirectory $sdk -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $timeout=!$p.WaitForExit($TimeoutSeconds*1000)
    if($timeout){Stop-Process -Id $p.Id -Force}
    $p.WaitForExit();$p.Refresh()
    $log=(Get-Content $stdout,$stderr -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    $row=@{mode=$mode;step=$target.step;input=$file.Name;executable="$sdk/tools/BinaryAssetBuilder.exe";workingDirectory=$sdk;inputAbsolute=$file.FullName;arguments=$arguments;exitCode=$p.ExitCode;timedOut=$timeout;succeeded=(!$timeout -and $p.ExitCode -eq 0 -and $log -notmatch '(?im)^\s*(Critical|Error):')}
    foreach($ext in @('bin','manifest','imp','relo')){
     $path="$built/mods/Upheaval/data/$($target.folder)/$($file.BaseName).$ext"
     if(!(Test-Path $path)){$row.succeeded=$false;continue}
     Copy-Item $path "$report/$label.$ext"
     $result.outputs+=@{mode=$mode;step=$target.step;input=$file.Name;extension=$ext;sha256=(Get-FileHash $path).Hash;bytes=(Get-Item $path).Length}
    }
    if($target.step -eq 3 -and (Test-Path "$built/mods/Upheaval/data/AptUI/$($file.BaseName).manifest")){
     [IO.File]::WriteAllText("$built/mods/Upheaval/data/AptUI/$($file.BaseName).version",[string][char]13+[char]10)
    }
    $result.invocations+=$row
    Write-Host "$label success=$($row.succeeded) exit=$($p.ExitCode)"
    if(!$row.succeeded){throw "$label failed; baseline gate prevents proceeding to recovered SDK"}
   }
  }
  $custom=@(Get-ChildItem $built -Recurse -File -Filter '*.cdata'|Sort-Object FullName|ForEach-Object{@{path=[IO.Path]::GetRelativePath($built,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName).Hash;bytes=$_.Length}})
  ConvertTo-Json -InputObject @($custom) -Depth 5 | Set-Content "$report/$mode-custom.json"
  if($mode -eq 'Original'){$result.originalBaselinePassed=$true;Write-Host 'ORIGINAL SDK BASELINE PASSED; recovered build and execution now permitted'}
 }
 foreach($row in @($result.outputs|Where-Object mode -eq Original)){
  $other=@($result.outputs|Where-Object{$_.mode -eq 'Recovered' -and $_.step -eq $row.step -and $_.input -eq $row.input -and $_.extension -eq $row.extension})
  $result.comparisons+=@{step=$row.step;input=$row.input;extension=$row.extension;identical=($other.Count -eq 1 -and $other[0].sha256 -eq $row.sha256)}
 }
 $originalCustom=@(Get-Content "$report/Original-custom.json" -Raw|ConvertFrom-Json)
 $recoveredCustom=@(Get-Content "$report/Recovered-custom.json" -Raw|ConvertFrom-Json)
 if((ConvertTo-Json -InputObject @($originalCustom) -Compress) -cne (ConvertTo-Json -InputObject @($recoveredCustom) -Compress)){throw 'CustomData path, bytes or content differ'}
 $originalAudio=@(Get-Content "$report/Original-audio-calls.tsv"|Sort-Object)
 $recoveredAudio=@(Get-Content "$report/Recovered-audio-calls.tsv"|Sort-Object)
 if(!$originalAudio.Count -or (ConvertTo-Json -InputObject @($originalAudio) -Compress) -cne (ConvertTo-Json -InputObject @($recoveredAudio) -Compress)){throw 'Actual audio buffers, CustomData or call counts differ'}
 $result['audioCalls']=@{original=$originalAudio.Count;recovered=$recoveredAudio.Count;identical=$true}
 $result['customData'] = @{identical=$true;files=$originalCustom.Count}
 $result['actualRoutes']=@()
 foreach($mode in @('Original','Recovered')){[xml]$recorded=Get-Content "$report/$mode.config" -Raw;$result.actualRoutes+=@{mode=$mode;routes=@($recorded.configuration.assetbuilder.plugins.plugin|ForEach-Object{@{types=$_.assetTypes;target=$_.targetType;enabled=$_.enabled}})}}
 $caseManifest=Get-Content 'SDK/Mods/Upheaval/Data/Mod/PublicCases/manifest.json' -Raw|ConvertFrom-Json
 $xmlCalls=Get-Content "$report/trace/xml/calls.tsv"
 $objectCalls=Get-Content "$report/trace/gameobject/calls.tsv"
 $result['publicCaseCoverage']=@()
 foreach($asset in $caseManifest.registeredAssets){
  if($asset.type -eq 'WeaponTemplate'){continue} # Compared via complete four-output SDK differential; this plugin has no trace sink.
  $matches=if($asset.type -eq 'GameObject'){@($objectCalls|Where-Object{($_ -split "`t")[0] -ceq $asset.id})}else{@($xmlCalls|Where-Object{($_ -split "`t")[1] -ceq $asset.id})}
  $result.publicCaseCoverage+=@{type=$asset.type;id=$asset.id;calls=$matches.Count}
  if(!$matches.Count){throw "Registered public asset never reached recovered plugin: $($asset.id)"}
 }
 $result.succeeded=$result.invocations.Count -eq 10 -and @($result.invocations|Where-Object{!$_.succeeded}).Count -eq 0 -and $result.comparisons.Count -eq 20 -and @($result.comparisons|Where-Object{!$_.identical}).Count -eq 0
 if(!$result.succeeded){throw 'Upheaval SDK invocation or output comparison failed'}
}catch{$result['error']=$_.ToString();throw}
finally{
 $env:AUDIO_SDK_IMPLEMENTATION=$null
 $env:AUDIO_SDK_TRACE=$null
 $env:RA3_RECOVERED_XML_PRIORITY_TRACE=$null
 $env:RA3_RECOVERED_GAMEOBJECT_TRACE=$null
 $env:RECOVERED_W3X_TRACE_DIRECTORY=$null
 $result|ConvertTo-Json -Depth 12|Set-Content "$report/validation.json"
 Set-Location $harnessRoot
}
