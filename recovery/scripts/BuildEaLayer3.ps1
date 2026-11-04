[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory,
      [ValidateSet('Debug','Release')][string]$Configuration='Release',
      [switch]$CompilerReady)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if (!(Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    if ($CompilerReady) { throw 'MSVC x86 compiler was not initialized' }
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$vs) { throw 'Building EALayer3 requires Visual Studio C++ x86 build tools' }
    $vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars32.bat'
    & cmd.exe /d /s /c "`"`"$vcvars`" >nul && pwsh -NoProfile -File `"$PSCommandPath`" -OutputDirectory `"$OutputDirectory`" -Configuration $Configuration -CompilerReady`""
    if ($LASTEXITCODE -ne 0) { throw 'EALayer3 native build failed' }
    return
}
if ($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x86') { throw 'EALayer3 requires the x86 compiler' }
$out=[IO.Path]::GetFullPath($OutputDirectory)
$obj=Join-Path $out 'ealayer3-obj'
New-Item -ItemType Directory -Force $out,$obj|Out-Null
$source=Join-Path $root 'recovery/src/Native/Audio'
$files=@(Get-ChildItem (Join-Path $source 'EaLayer3') -Filter '*.cpp' -Recurse|Sort-Object FullName)
$files+=Get-Item (Join-Path $source 'EaLayer3Encoder.cpp')
$objects=@()
foreach($file in $files) {
    $object=Join-Path $obj ($file.BaseName+'.obj');$log=Join-Path $obj ($file.BaseName+'.log')
    $opt=if($Configuration -eq 'Release'){'/O2'}else{'/Od'}
    & cl.exe /nologo /c /MD /EHsc /std:c++17 /fp:precise $opt "/Fo$object" $file.FullName *> $log
    if($LASTEXITCODE -ne 0){Get-Content $log -Tail 60|Out-Host;throw "EALayer3 compile failed: $($file.Name)"}
    $objects+=$object
}
& link.exe /nologo /DLL /MACHINE:X86 "/OUT:$out/NewSdk.EaLayer3.dll" "/IMPLIB:$obj/EaLayer3Exports.lib" @objects *> (Join-Path $obj 'link.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $obj 'link.log')|Out-Host;throw 'EALayer3 link failed'}
