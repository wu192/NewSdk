[CmdletBinding()]
param([string]$PackageDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
if(!$PackageDirectory) {
    $packageLine=(& dotnet nuget locals global-packages --list)
    if($LASTEXITCODE -ne 0){throw 'Cannot locate the NuGet package directory'}
    $PackageDirectory=($packageLine -replace '^global-packages:\s*','').Trim()
}
$PackageDirectory=[IO.Path]::GetFullPath($PackageDirectory)
# Restore only the Framework target used by the production compiler. The UI
# project also has a net8 target for portable component tests.
& dotnet restore (Join-Path $root 'ui/BinaryAssetBuilder.UiCompiler.csproj') '-p:TargetFrameworks=net472' --packages $PackageDirectory --nologo -v:q | Out-Host
if($LASTEXITCODE -ne 0){throw 'UI dependency restore failed'}
$dependencies=Get-Content (Join-Path $root 'ui/dependencies.json') -Raw|ConvertFrom-Json
$assemblies=@(
    @{id='libtessdotnet';file='lib/netstandard2.0/LibTessDotNet.dll';name='LibTessDotNet'},
    @{id='clipper2';file='lib/netstandard2.0/Clipper2Lib.dll';name='Clipper2Lib'},
    @{id='stbimagesharp';file='lib/net471/StbImageSharp.dll';name='StbImageSharp'}
)
$references=@();$resources=@();$packages=@()
foreach($assembly in $assemblies) {
    $pin=@($dependencies.nugetPackages|Where-Object{$_.id -ceq $assembly.id})
    if($pin.Count -ne 1){throw "Missing UI dependency pin: $($assembly.id)"}
    $pin=$pin[0]
    $packagePath=Join-Path $PackageDirectory "$($pin.id)/$($pin.version)"
    $archive=Join-Path $packagePath "$($pin.id).$($pin.version).nupkg"
    $hash=(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if($hash -cne $pin.sha256){throw "UI NuGet package does not match its source pin: $($pin.id)/$($pin.version)"}
    $path=(Resolve-Path (Join-Path $packagePath $assembly.file)).Path
    $references+=$path
    $resources+=@{path=$path;logicalName="BinaryAssetBuilder.UiCompiler.Dependencies.$($assembly.name).dll"}
    $packages+=@{id=$pin.id;version=$pin.version;sha256=$hash;assembly=$assembly.file;assemblySha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[pscustomobject]@{references=$references;resources=$resources;packages=$packages}
