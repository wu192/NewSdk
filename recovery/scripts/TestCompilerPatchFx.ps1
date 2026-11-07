#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CompilerDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$FxcExecutable,
    [ValidateRange(15,600)][int]$TimeoutSeconds=120,
    [ValidateSet('Debug','Release')][string[]]$Configurations=@('Debug','Release')
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$CompilerDirectory=(Resolve-Path -LiteralPath $CompilerDirectory).Path
if(Test-Path -LiteralPath $OutputDirectory){throw "Refusing to overwrite patch evidence: $OutputDirectory"}
$out=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$temporary=if($env:RUNNER_TEMP){$env:RUNNER_TEMP}else{[IO.Path]::GetTempPath()}
$work=(New-Item -ItemType Directory -Path (Join-Path $temporary ('pf'+[Guid]::NewGuid().ToString('N').Substring(0,8)))).FullName
function Need([bool]$Value,[string]$Message){if(!$Value){throw $Message}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function RunProcess([string]$Executable,[string[]]$Arguments,[string]$Cwd,[string]$Log,[bool]$ExpectFailure=$false){
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Executable;$start.WorkingDirectory=$Cwd
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    try{
        Need ($process.Start()) "Process did not start: $Executable"
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(!$process.WaitForExit($TimeoutSeconds*1000)){$process.Kill($true);throw "Process timed out: $Executable"}
        [IO.File]::WriteAllText(($Log+'.stdout.log'),$stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText(($Log+'.stderr.log'),$stderr.GetAwaiter().GetResult())
        if($ExpectFailure){Need ($process.ExitCode -ne 0) 'Malformed base manifest silently succeeded as a full stream'}
        else{Need ($process.ExitCode -eq 0) "Process failed ($($process.ExitCode)): $Executable; see $Log.stderr.log"}
    }finally{$process.Dispose()}
}
function PutMod([string]$Mod,[string]$Shared,[switch]$Patch){
    New-Item -ItemType Directory -Force -Path "$Mod/Data"|Out-Null
    $changed=if($Patch){'70%'}else{'80%'}
    $added=if($Patch){'<ArmorTemplate id="PatchAdded" Default="50%"/>'}else{''}
    [IO.File]::WriteAllText("$Mod/Data/Mod.xml",('<AssetDeclaration xmlns="uri:ea.com:eala:asset"><ArmorTemplate id="PatchShared" Default="'+$Shared+'"/><ArmorTemplate id="PatchChanged" Default="'+$changed+'"/>'+$added+'</AssetDeclaration>'))
}
function Build([string]$Name,[string]$Mod,[string]$Destination,[string[]]$Extra){
    $case=(New-Item -ItemType Directory -Path "$script:modeOut/$Name").FullName
    $arguments=@('build','--mod',$Mod,'--sdk-root',$script:sdk,'--out',$Destination,'--package-content','base')+$Extra
    @{exe=$script:exe;arguments=$arguments}|ConvertTo-Json -Depth 4|Set-Content "$case/request.json"
    RunProcess $script:exe $arguments ([IO.Path]::GetDirectoryName($script:exe)) "$case/process"
    $report=Get-Content -LiteralPath "$Destination/build-report.json" -Raw|ConvertFrom-Json
    Need ($report.exitCode -eq 0 -and $report.publicationStatus -ceq 'PublishedDirectory') "Build/report failed: $Name"
    Copy-Item -LiteralPath "$Destination/build-report.json","$Destination/build.events.jsonl" -Destination $case
    $script:modeState.cases+=,@{name=$Name;arguments=$arguments;buildId=$report.buildId}
    return @{report=$report;directory=$Destination;case=$case}
}
function Manifest($Build){
    $files=@($Build.report.artifacts|Where-Object{[IO.Path]::GetExtension($_.Path) -ieq '.manifest'})
    Need ($files.Count -eq 1) 'Expected one loose base manifest';return [string]$files[0].Path
}
function Package($Build){
    $files=@($Build.report.artifacts|Where-Object{$_.Kind -ceq 'package.big'})
    Need ($files.Count -eq 1) 'Expected one BIG artifact';return [string]$files[0].Path
}
function CheckPatch($Build,[string]$Normal,[string]$Base,[bool]$Reused,[string]$Oracle=''){
    & python $script:validator $PSScriptRoot (Package $Build) $Normal $Base $Build.case $Reused.ToString().ToLowerInvariant() $Oracle
    Need ($LASTEXITCODE -eq 0) 'Production patch manifest/BIG/original oracle comparison failed'
    $sku=@($Build.report.artifacts|Where-Object{$_.Kind -ceq 'package.skudef'})
    Need ($sku.Count -eq 1) 'Patch SKUDEF missing'
    $expected="mod-game 1.12`r`nadd-big Patch_2.7.big`r`nadd-config Base_1.0.skudef`r`n"
    Need ([IO.File]::ReadAllText($sku[0].Path) -ceq $expected) 'Patch SKUDEF add-config/CRLF order differs'
    Need ($Build.report.effectiveOptions.'mod-version'.Source -ceq 'project') 'Patch version was not supplied solely by MOD JSON'
}
$validator=Join-Path $out 'validate-patch.py'
@'
import hashlib,json,struct,sys
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from ValidateCompilerBig import read_archive
archive,normal,base,destination=map(Path,sys.argv[2:6])
reused=sys.argv[6]=='true';oracle=Path(sys.argv[7]) if sys.argv[7] else None
def need(value,message):
    if not value: raise ValueError(message)
def manifest(data):
    need(len(data)>=48 and data[:4]==b'\0\1\6\0','Expected complete linked little-endian RA3 v6 header')
    count=struct.unpack_from('<I',data,12)[0];sizes=struct.unpack_from('<4I',data,32)
    need(len(data)==48+48*count+sum(sizes),'Manifest table lengths do not consume output')
    start=48+48*count+sizes[0];end=start+sizes[1];refs=[]
    while start<end:
        flag=data[start];stop=data.index(0,start+1,end);refs.append((flag,data[start+1:stop].decode('ascii')));start=stop+1
    names=end;assets={}
    for index in range(count):
        fields=struct.unpack_from('<12I',data,48+index*48);start=names+fields[6]
        name=data[start:data.index(0,start,names+sizes[2])].decode('ascii')
        need(name not in assets,'Duplicate manifest asset');assets[name]=fields[8:11]
    return refs,assets
entries={row['name']:row['data'] for row in read_archive(archive)['members']}
stems=['mod_1.01','mod_m_1.01','mod_l_1.01'];extensions=['manifest','bin','imp','relo']
expected={f'data\\{stem}.{extension}' for stem in stems for extension in extensions}|{f'data\\{stem}.version' for stem in ['mod','mod_m','mod_l']}
need(set(entries)==expected,'Patch BIG must contain the exact versioned main/LoD streams and three version markers')
for stem in ['mod','mod_m','mod_l']: need(entries[f'data\\{stem}.version']==b'_1.01','Patch version marker bytes differ')
refs,assets=manifest(entries['data\\mod_1.01.manifest']);normalrefs,normalassets=manifest(normal.read_bytes())
need(refs==[(2,base.name)] and not normalrefs,'Patch must carry exactly one flag-2 base reference; normal cache output must carry none')
need(set(assets)=={'ArmorTemplate:PatchShared','ArmorTemplate:PatchChanged','ArmorTemplate:PatchAdded'},'Patch asset inventory differs')
need((assets['ArmorTemplate:PatchShared'][0]==0)==reused,'Base edit/normal-to-patch cache invalidation did not change Shared payload reuse')
need(assets['ArmorTemplate:PatchChanged'][0]>0 and assets['ArmorTemplate:PatchAdded'][0]>0,'Changed/new assets were omitted from patch payload')
need(all(row[0]>0 for row in normalassets.values()),'Normal loose fixture did not contain full payloads')
for stem in stems: manifest(entries[f'data\\{stem}.manifest'])
comparisons=[]
if oracle:
    for name,data in entries.items():
        original=(oracle/name.split('\\')[-1]).read_bytes()
        comparisons.append({'name':name,'byteIdentical':original==data,'originalSha256':hashlib.sha256(original).hexdigest(),'candidateSha256':hashlib.sha256(data).hexdigest(),'originalBytes':len(original),'candidateBytes':len(data),'firstDifferentOffsets':[i for i,(a,b) in enumerate(zip(original,data)) if a!=b][:16]})
export=destination/'members';export.mkdir(exist_ok=True)
for name,data in entries.items(): (export/name.split('\\')[-1]).write_bytes(data)
passed=all(row['byteIdentical'] for row in comparisons)
(destination/'comparison.json').write_text(json.dumps({'succeeded':passed,'sharedReused':reused,'originalCompared':bool(oracle),'rawComparisons':comparisons,'references':refs,'assets':assets,'members':{name:hashlib.sha256(data).hexdigest() for name,data in entries.items()}},indent=2)+'\n')
need(passed,'Original BAB /bps + LoD raw bytes differ; raw comparisons retained')
'@|Set-Content -LiteralPath $validator
$state=@{schemaVersion=1;succeeded=$false;sourceCommit=(& git -C $root rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;scriptSha256=(Hash $PSCommandPath);modes=@();scope='Small real Core patch, original BAB/LoD byte oracle, normal/patch/hot/base-edit cache transitions; optional actual FX compilation; no full MOD/game acceptance'}
try{
    foreach($mode in $Configurations){
        $modeOut=(New-Item -ItemType Directory -Path "$out/$mode").FullName
        $modeWork=(New-Item -ItemType Directory -Path "$work/$mode").FullName
        $exe=(Resolve-Path -LiteralPath "$CompilerDirectory/$mode/bin/NewModCompiler.exe").Path
        $sdk=(New-Item -ItemType Directory -Path "$modeWork/s").FullName
        New-Item -ItemType Directory -Path "$sdk/tools","$sdk/Schemas"|Out-Null
        Copy-Item -LiteralPath "$root/SDK/Schemas/xsd" -Destination "$sdk/Schemas" -Recurse
        Copy-Item -LiteralPath "$root/SDK/tools/BinaryAssetBuilder.exe.config" -Destination "$sdk/tools"
        $modeState=@{configuration=$mode;passed=$false;exeSha256=(Hash $exe);cases=@();fx=@{status='not-available'}};$state.modes+=,$modeState
        $baseMod=Join-Path $modeWork 'Base';$patchMod=Join-Path $modeWork 'Patch'
        PutMod $baseMod '100%';PutMod $patchMod '100%' -Patch
        $baseOutput=Join-Path $modeWork 'base-out'
        $base=Build 'base' $baseMod $baseOutput @('--package-format','loose')
        $baseManifest=Manifest $base
        $normal=Build 'normal-before-patch' $patchMod (Join-Path $modeWork 'normal') @('--package-format','loose')
        [IO.File]::WriteAllText("$patchMod/newmodcompiler.json",'{"mod-version":"2.7"}')
        $patchArguments=@('--base-patch-stream',$baseManifest,'--patch-config','Base_1.0.skudef','--include-additional=false')
        $patch=Build 'patch' $patchMod (Join-Path $modeWork 'patch-out') $patchArguments
        CheckPatch $patch (Manifest $normal) $baseManifest $true
        $hot=Build 'patch-hot' $patchMod (Join-Path $modeWork 'hot') $patchArguments
        CheckPatch $hot (Manifest $normal) $baseManifest $true
        Need ((Hash (Package $patch)) -ceq (Hash (Package $hot))) 'Unchanged hot patch package raw bytes differ'

        # Independent original tools use the same XML and resolved input roots.
        $oracle=(New-Item -ItemType Directory -Path "$modeWork/original").FullName
        $tools=(New-Item -ItemType Directory -Path "$oracle/tools").FullName
        Get-ChildItem -LiteralPath "$root/SDK/tools" -File|Copy-Item -Destination $tools
        [xml]$config=[IO.File]::ReadAllText("$tools/BinaryAssetBuilder.exe.config")
        $section=$config.SelectSingleNode('/configuration/assetbuilder');$roots=$patch.report.inputRoots
        $section.SetAttribute('schema',[string]$roots.schemaPath);$section.SetAttribute('dataRoot',[string]$roots.dataRoot)
        foreach($pair in @(@('defaultDataPaths','dataPaths'),@('defaultArtPaths','artPaths'),@('defaultAudioPaths','audioPaths'))){$section.SetAttribute($pair[0],(@($roots.($pair[1])) -join ';'))}
        $buildConfigurations=$section.SelectSingleNode('buildConfigurations');if($buildConfigurations){$buildConfigurations.RemoveAll()}
        $startup=$config.SelectSingleNode('/configuration/startup');if(!$startup){$startup=$config.CreateElement('startup');$config.configuration.AppendChild($startup)|Out-Null}
        $startup.RemoveAll();$startup.SetAttribute('useLegacyV2RuntimeActivationPolicy','true')
        $runtime=$config.CreateElement('supportedRuntime');$runtime.SetAttribute('version','v4.0');$startup.AppendChild($runtime)|Out-Null
        $config.Save("$tools/BinaryAssetBuilder.exe.config")
        New-Item -ItemType Directory -Path "$oracle/streams","$oracle/intermediate","$oracle/session","$oracle/cache"|Out-Null
        $originalArguments=@("$patchMod/Data/Mod.xml","/bps:$baseManifest","/sp:$($roots.schemaPath)","/dr:$sdk","/od:$oracle/streams","/iod:$oracle/intermediate","/scd:$oracle/session","/bcp:$oracle/cache",'/tp:Win32','/sc:false','/bc:false','/psc:false','/fsc:false','/csc:false','/atc:false','/sh:false','/pc:true','/res:false','/gui:false','/poe:false','/mr:false','/oof:false','/sf:false','/ls:true','/ss:true','/oix:false','/vf:false','/osh:false','/oar:false','/slowclean:true','/tl:3','/el:0')
        RunProcess "$tools/BinaryAssetBuilder.exe" $originalArguments $sdk "$modeOut/original-bab"
        $manifests=@(Get-ChildItem -LiteralPath "$oracle/streams" -Filter Mod.manifest -Recurse -File)
        Need ($manifests.Count -eq 1) 'Original BAB did not emit one fresh patch manifest'
        $streams=$manifests[0].DirectoryName
        foreach($extension in @('manifest','bin','imp','relo')){
            Move-Item -LiteralPath "$streams/Mod.$extension" -Destination "$streams/mod_1.01.$extension"
            Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($baseManifest,$extension)) -Destination "$streams/Mod.$extension"
        }
        RunProcess "$tools/LoDStreamBuilder.exe" @("$streams/mod_1.01.manifest") $tools "$modeOut/original-lod"
        foreach($suffix in @('m','l')){foreach($extension in @('manifest','bin','imp','relo')){Move-Item -LiteralPath "$streams/mod_1.01_$suffix.$extension" -Destination "$streams/mod_${suffix}_1.01.$extension"}}
        foreach($stem in @('mod','mod_m','mod_l')){[IO.File]::WriteAllBytes("$streams/$stem.version",[Text.Encoding]::ASCII.GetBytes('_1.01'))}
        $originalArchive=(New-Item -ItemType Directory -Path "$modeOut/original-streams").FullName
        Get-ChildItem -LiteralPath $streams -File|Where-Object{$_.BaseName -in @('mod_1.01','mod_m_1.01','mod_l_1.01','mod','mod_m','mod_l')}|Copy-Item -Destination $originalArchive
        CheckPatch $patch (Manifest $normal) $baseManifest $true $streams
        $modeState.originalBabSha256=Hash "$tools/BinaryAssetBuilder.exe";$modeState.originalLodSha256=Hash "$tools/LoDStreamBuilder.exe"

        PutMod $baseMod '90%'
        $changedBase=Build 'base-edited' $baseMod $baseOutput @('--package-format','loose')
        Need ((Manifest $changedBase) -ceq $baseManifest) 'Base-edit fixture changed the reference path'
        $changedPatch=Build 'patch-after-base-edit' $patchMod (Join-Path $modeWork 'changed') $patchArguments
        CheckPatch $changedPatch (Manifest $normal) $baseManifest $false
        Need ((Hash (Package $patch)) -cne (Hash (Package $changedPatch))) 'Same-path base edit left a stale patch package'

        # Keep the CLI's valid v6 header, but make the body unreadable in Core.
        # Signed -1 asset count throws before any unsafe asset-table access.
        $badBase=Join-Path $modeWork 'broken.manifest'
        $header=[byte[]]::new(48);[Array]::Copy([IO.File]::ReadAllBytes($baseManifest),$header,48)
        [Array]::Copy([BitConverter]::GetBytes([uint32]::MaxValue),0,$header,12,4)
        [IO.File]::WriteAllBytes($badBase,$header)
        foreach($extension in @('bin','imp','relo')){Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($baseManifest,$extension)) -Destination ([IO.Path]::ChangeExtension($badBase,$extension))}
        $badOutput=Join-Path $modeWork 'invalid';$badCase=(New-Item -ItemType Directory -Path "$modeOut/invalid-base-body").FullName
        $badArguments=@('build','--mod',$patchMod,'--sdk-root',$sdk,'--out',$badOutput,'--base-patch-stream',$badBase,'--include-additional=false')
        RunProcess $exe $badArguments ([IO.Path]::GetDirectoryName($exe)) "$badCase/process" $true
        Need (@(Get-ChildItem -LiteralPath "$badOutput/published" -Filter '*.big' -Recurse -File -ErrorAction SilentlyContinue).Count -eq 0) 'Invalid base body published a BIG'
        if(Test-Path -LiteralPath "$badOutput/build-report.json"){Copy-Item -LiteralPath "$badOutput/build-report.json" -Destination $badCase}
        $modeState.cases+=,@{name='invalid-base-body';expectedFailure=$true;noPublishedBig=$true}

        $fxc=$FxcExecutable
        if(!$fxc){$fxc=@("$root/SDK/ARModExtraTools/fxc.exe","$root/SDK/tools/fxc.exe")|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf}|Select-Object -First 1}
        if(!$fxc){$fxc=Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/*/bin/*/x86/fxc.exe" -ErrorAction SilentlyContinue|Sort-Object FullName -Descending|Select-Object -First 1 -ExpandProperty FullName}
        if($fxc){
            New-Item -ItemType Directory -Path "$patchMod/Additional/Shaders"|Out-Null
            [IO.File]::WriteAllText("$patchMod/Additional/Shaders/Probe.fx",'float4 main():COLOR0{return float4(1,0,0,1);} technique Main {pass P {PixelShader=compile ps_2_0 main();}}')
            $fx=Build 'patch-with-fx' $patchMod (Join-Path $modeWork 'fx') @('--base-patch-stream',$baseManifest,'--fxc',$fxc)
            $check='import sys;sys.path.insert(0,sys.argv[1]);from ValidateCompilerBig import read_archive;d={x["name"]:x["data"] for x in read_archive(sys.argv[2])["members"]};assert len(d["shaders\\compiled\\probe.fxo"])>4;assert not any(n.endswith((".fx",".fxh")) for n in d)'
            & python -c $check $PSScriptRoot (Package $fx);Need ($LASTEXITCODE -eq 0) 'Actual FX bytecode missing or shader source leaked into BIG'
            $modeState.fx=@{status='passed';compiler=$fxc;compilerSha256=(Hash $fxc)}
        }
        $modeState.passed=$true
    }
    $state.succeeded=$true
}finally{$state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json"}
