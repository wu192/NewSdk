[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [switch]$Tests
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "recovery\artifacts\app\$Configuration" }
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'VS2022 Build Tools with Roslyn are required.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$csc = Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe'
$refs = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path (Join-Path $refs 'mscorlib.dll'))) { throw '.NET Framework 4.8 developer targeting pack is required.' }
$sources = @(Get-ChildItem (Join-Path $repo 'recovery\src\App') -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object FullName)
$entry = 'NewModCompiler.Cli.Program'; $file = 'NewModCompiler.exe'
if ($Tests) {
    $sources += @(Get-ChildItem (Join-Path $repo 'recovery\tests\App') -Filter '*.cs' | Sort-Object FullName | ForEach-Object FullName)
    $entry = 'NewModCompiler.Tests.AppTests'; $file = 'NewModCompiler.AppTests.exe'
}
$arguments = @('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x86','/langversion:7.3',"/main:$entry",("/out:" + (Join-Path $OutputDirectory $file)))
foreach ($reference in @('mscorlib','System','System.Core','System.Configuration','System.Web.Extensions','System.Xml')) { $arguments += ('/reference:' + (Join-Path $refs "$reference.dll")) }
$arguments += $(if ($Configuration -eq 'Release') { '/optimize+' } else { '/debug:full' })
& $csc @arguments @sources
if ($LASTEXITCODE -ne 0) { throw "App compilation failed: $LASTEXITCODE" }
Copy-Item (Join-Path $repo 'recovery\src\App\App.config') (Join-Path $OutputDirectory "$file.config") -Force
Write-Output (Join-Path $OutputDirectory $file)
