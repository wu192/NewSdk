param(
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if(!$IsWindows -and $env:OS -ne 'Windows_NT'){throw 'Foundation requires Windows, .NET Framework 4.8 reference assemblies and Roslyn 4.0.1'}
$manifest=Get-Content "$root/recovery/build/Managed.Foundation.json" -Raw | ConvertFrom-Json
if(!$OutputDirectory){$OutputDirectory="$root/recovery/artifacts/managed-foundation/$Configuration"}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$ui=& "$PSScriptRoot/RestoreUiDependencies.ps1"
$tools="$root/recovery/build/ManagedMetadata/ManagedMetadata.csproj"
& dotnet build $tools -c Release -p:Platform=AnyCPU --nologo *> "$out/metadata-tool-build.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/metadata-tool-build.log";throw 'Metadata build tools failed'}
$packageLine=(& dotnet nuget locals global-packages --list)
if($LASTEXITCODE -ne 0){throw 'Cannot locate fixed Roslyn package'}
$packageRoot=($packageLine -replace '^global-packages:\s*','').Trim()
$csc=Join-Path $packageRoot 'microsoft.net.compilers.toolset/4.0.1/tasks/net472/csc.exe'
if(!(Test-Path $csc)){throw "Pinned Roslyn 4.0.1 compiler missing: $csc"}
$framework=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
if(!(Test-Path "$framework/mscorlib.dll")){throw '.NET Framework 4.8 reference assemblies unavailable'}
$files=@(foreach($sourceRoot in $manifest.sourceRoots){
    $path=Join-Path $root $sourceRoot
    if(!(Test-Path $path)){throw "Missing Foundation source root $sourceRoot"}
    Get-ChildItem $path -Recurse -Filter *.cs -File | Where-Object {
        $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.Name -notin $manifest.excludedFileNames
    }
}) | Sort-Object FullName
if($files.Count -lt 200){throw 'Foundation source inventory is unexpectedly incomplete'}
$module=Join-Path $out $manifest.moduleName
$opt=if($Configuration -eq 'Release'){'/optimize+'}else{'/optimize-'}
$arguments=@('/nologo','/codepage:65001','/noconfig','/nostdlib+','/target:module','/platform:anycpu','/langversion:10.0','/unsafe+','/deterministic+', $opt, ('/define:'+$manifest.define), ('/out:"'+$module+'"'))
$arguments+=@($manifest.frameworkReferences|ForEach-Object {'/reference:"'+(Join-Path $framework ($_+'.dll'))+'"'})
$arguments+='/reference:"'+(Join-Path $root 'SDK/tools/antlr.runtime.dll')+'"'
$arguments+='/reference:"'+(Join-Path $framework 'Facades/netstandard.dll')+'"'
$arguments+=@($ui.references|ForEach-Object {'/reference:"'+$_+'"'})
$arguments+=@($files|ForEach-Object {'"'+$_.FullName+'"'})
$rsp=Join-Path $out 'Foundation.rsp'
[IO.File]::WriteAllLines($rsp,[string[]]$arguments,[Text.UTF8Encoding]::new($false))
& $csc ('@'+$rsp) *> "$out/compile.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/compile.log";throw 'Foundation compilation failed'}
$tool="$root/recovery/build/ManagedMetadata/bin/Release/net8.0/ManagedMetadata.dll"
& dotnet $tool repair $module "$root/SDK/tools/BinaryAssetBuilder.Utility.dll" *> "$out/metadata.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/metadata.log";throw 'Foundation contract repair failed'}
$resourceDirectory=Join-Path $out 'resources'
New-Item -ItemType Directory -Force $resourceDirectory|Out-Null
$resources=@($manifest.resources|ForEach-Object {
    $inputPath=Join-Path $root $_.path
    $path=Join-Path $resourceDirectory $_.logicalName
    if($_.lineEndings -eq 'CRLF') {
        # Canonicalize this text resource independently of checkout core.autocrlf.
        # The committed LF source is semantically identical to original CRLF.
        $text=[IO.File]::ReadAllText($inputPath).Replace("`r`n","`n").Replace("`n","`r`n")
        [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))
    } else {Copy-Item $inputPath $path -Force}
    $sha=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if($sha -cne $_.expectedSha256){throw "Resource bytes differ from fixed original SHA-256: $($_.logicalName)"}
    @{path=$path;logicalName=$_.logicalName;sha256=$sha}
})
$resources+=@($ui.resources|ForEach-Object {
    $path=Join-Path $resourceDirectory (Split-Path $_.path -Leaf)
    Copy-Item -LiteralPath $_.path -Destination $path
    @{path=$path;logicalName=$_.logicalName;sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
ConvertTo-Json -InputObject $ui.packages -Depth 5|Set-Content "$out/ui-dependencies.json" -Encoding utf8
ConvertTo-Json -InputObject $resources -Depth 5|Set-Content "$out/resources.json" -Encoding utf8
$sources=@($files|ForEach-Object {@{path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
@{formatVersion=1;configuration=$Configuration;sourceCommit=(git -C $root rev-parse HEAD).Trim();compiler='Microsoft.Net.Compilers.Toolset/4.0.1';target='net48/Win32';intermediateModulePlatform='AnyCPU';module=(Split-Path $module -Leaf);moduleSha256=(Get-FileHash $module -Algorithm SHA256).Hash.ToLowerInvariant();sourceFiles=$sources;resources=$resources;allowedBusinessReferences=@($manifest.allowedBusinessReferences);singleExeValidated=$false}|ConvertTo-Json -Depth 8|Set-Content "$out/build.json" -Encoding utf8
Write-Host "PASS Foundation $Configuration module, $($files.Count) source files; resources deferred to final link"
# Build artifact path only: other scripts may dot-source this script and use the result.
Write-Output $out
