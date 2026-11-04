[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$FoundationModule,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('Debug','Release')][string]$Configuration='Release'
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
$foundation=(Resolve-Path $FoundationModule).Path
if($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x86'){throw 'Compiler Native/Bridge build requires the MSVC x86 target'}
if(Test-Path $OutputDirectory){throw "Refusing to reuse Native/Bridge output: $OutputDirectory"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$generated=Join-Path $out 'generated'
$objectsDirectory=Join-Path $out 'objects'
$logs=Join-Path $out 'logs'
New-Item -ItemType Directory -Force $generated,$objectsDirectory,$logs|Out-Null
$manifestPath=Join-Path $root 'recovery/build/Compiler.Native.json'
$manifest=Get-Content $manifestPath -Raw|ConvertFrom-Json
if($manifest.schemaVersion -ne 1){throw 'Unsupported compiler source manifest'}
$all=@($manifest.nativeSources)+@($manifest.unmanagedSources)+@($manifest.bridgeSources)+@($manifest.applicationSources)
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($path in $all){
    if($path -isnot [string] -or $path -notmatch '^recovery/src/(Native|Bridge|Plugin|Managed/W3x)/' -or $path -match '(^|/)(\.\.|Draft|tests?|probes?)(/|\.)' -or $path -match 'Draft' -or !$seen.Add($path)){throw "Invalid or duplicate production source: $path"}
    if(!(Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)){throw "Missing production source: $path"}
}
$optimization=if($Configuration -eq 'Release'){'/O2'}else{'/Od'}
$objects=@()
$bridgeObjects=@()
$bridgeGenerated=@()
$bridgeUnityObject=Join-Path $objectsDirectory 'CompilerBridges.obj'
$sourceRecords=@()
$dependencyPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-LocalIncludes([string]$Path) {
    $resolved=[IO.Path]::GetFullPath($Path)
    if(!$resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Source include escapes repository: $resolved"}
    if(!$dependencyPaths.Add($resolved)){return}
    $text=[IO.File]::ReadAllText($resolved)
    foreach($match in [regex]::Matches($text,'(?m)^\s*#\s*include\s*"([^"]+)"')) {
        $include=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($resolved)) $match.Groups[1].Value))
        if(!(Test-Path -LiteralPath $include -PathType Leaf)){throw "Missing local include: $include"}
        Add-LocalIncludes $include
    }
}
# Preserve the established compilation modes: XML/Tokenizer shared TUs use
# /clr, W3X native TUs use /EHsc, and all public bridges use /clr /EHa.
foreach($kind in @('nativeSources','unmanagedSources','bridgeSources')) {
    foreach($relative in @($manifest.$kind)) {
        $source=(Resolve-Path (Join-Path $root $relative)).Path
        Add-LocalIncludes $source
        $id=$relative.Replace('/','_').Replace('.cpp','')
        $object=Join-Path $objectsDirectory ($id+'.obj')
        $compileSource=$source
        $generatedHash=$null
        if($kind -eq 'bridgeSources') {
            $text=[IO.File]::ReadAllText($source)
            $corePattern='(?m)^#using <BinaryAssetBuilder\.Core\.dll>\r?$'
            if([regex]::Matches($text,$corePattern).Count -ne 1){throw "Expected exactly one Core import: $relative"}
            $text=[regex]::Replace($text,$corePattern,'#using "Managed.Foundation.netmodule"')
            # Absolute original include paths preserve nested headers and .inc
            # semantics without modifying any recovered source file.
            $sourceDirectory=[IO.Path]::GetDirectoryName($source)
            $text=[regex]::Replace($text,'(?m)^(\s*#\s*include\s*)"([^"]+)"',[Text.RegularExpressions.MatchEvaluator]{
                param($match)
                $include=[IO.Path]::GetFullPath((Join-Path $sourceDirectory $match.Groups[2].Value)).Replace('\','/')
                return $match.Groups[1].Value+'"'+$include+'"'
            })
            foreach($import in [regex]::Matches($text,'(?m)^\s*#using\s*[<"]([^>"]+)[>"]')) {
                if($import.Groups[1].Value -notin @('Managed.Foundation.netmodule','System.Core.dll','System.Xml.dll','System.dll','mscorlib.dll')){throw "Unexpected bridge assembly import: $relative / $($import.Groups[1].Value)"}
            }
            $compileSource=Join-Path $generated ($id+'.cpp')
            [IO.File]::WriteAllText($compileSource,$text,[Text.UTF8Encoding]::new($false))
            $generatedHash=(Get-FileHash $compileSource -Algorithm SHA256).Hash.ToLowerInvariant()
            $object=$bridgeUnityObject
            $bridgeGenerated+=$compileSource
        }
        if($kind -eq 'bridgeSources') {
            $sourceRecords+=@{path=$relative;sha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant();generatedSha256=$generatedHash;object=$object;kind=$kind}
            continue
        }
        # PublicS3's endpoint search depends on the original x86 rounding and
        # operation order. Keep its established strict floating point recipe.
        $floatingPoint=if($relative -like 'recovery/src/Native/Texture/*'){@('/arch:SSE2','/fp:strict','/fp:except-')}else{@('/fp:precise')}
        $arguments=@('/nologo','/c','/MD','/std:c++17')+$floatingPoint+@($optimization,('/Fo'+$object))
        if($kind -eq 'unmanagedSources'){$arguments+='/EHsc'}
        else{$arguments+=@('/clr','/EHa',('/AI'+[IO.Path]::GetDirectoryName($foundation)))}
        $arguments+=$compileSource
        $log=Join-Path $logs ($id+'.log')
        & cl.exe @arguments *> $log
        if($LASTEXITCODE -ne 0){Get-Content $log -Tail 100|ForEach-Object{Write-Host $_};throw "Native/Bridge compile failed: $relative"}
        $objects+=$object
        $sourceRecords+=@{path=$relative;sha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant();generatedSha256=$generatedHash;object=$object;kind=$kind}
    }
}
# One C++/CLI translation unit is essential here. C# /addmodule cannot
# merge std and vc.cppcli.modopts types emitted independently by many /clr
# objects (CS0101). Native code remains separately compiled, and this unity
# object contains the real bridge implementations, not a facade DLL.
$bridgeUnity=Join-Path $generated 'CompilerBridges.cpp'
$unityLines=@('// Explicit production Bridge unity translation unit.')
foreach($path in $bridgeGenerated){$unityLines+=('#include "'+$path.Replace('\','/')+'"')}
[IO.File]::WriteAllLines($bridgeUnity,[string[]]$unityLines,[Text.UTF8Encoding]::new($false))
$unityArguments=@('/nologo','/c','/clr','/MD','/EHa','/std:c++17','/fp:precise','/DNEWSdk_MONOLITHIC',$optimization,('/AI'+[IO.Path]::GetDirectoryName($foundation)),('/Fo'+$bridgeUnityObject),$bridgeUnity)
$unityLog=Join-Path $logs 'CompilerBridges.log'
& cl.exe @unityArguments *> $unityLog
if($LASTEXITCODE -ne 0){Get-Content $unityLog -Tail 100|ForEach-Object{Write-Host $_};throw 'Unified Bridge compilation failed'}
$objects+=$bridgeUnityObject
$bridgeObjects=@($bridgeUnityObject)
$receipt=@{
    schemaVersion=1;configuration=$Configuration;manifestSha256=(Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    foundationSha256=(Get-FileHash $foundation -Algorithm SHA256).Hash.ToLowerInvariant()
    bridgeTranslationUnit=@{path=$bridgeUnity;sha256=(Get-FileHash $bridgeUnity -Algorithm SHA256).Hash.ToLowerInvariant();sourceCount=$bridgeGenerated.Count}
    objects=$objects;bridgeObjects=$bridgeObjects;sources=$sourceRecords
    dependencies=@($dependencyPaths|Sort-Object|ForEach-Object{@{path=[IO.Path]::GetRelativePath($root,$_).Replace('\','/');sha256=(Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant()}})
    applicationSources=@($manifest.applicationSources);runtimeBackendLibraries=@();status='objects built; unified link and behavior validation required'
}
$receipt|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'build.json')
Write-Output (Join-Path $out 'build.json')
