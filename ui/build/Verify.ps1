param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repo
try {
    dotnet build ui/BinaryAssetBuilder.UiCompiler.csproj -c $Configuration --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }
    dotnet run --project ui/tests/UiCompiler.Tests.csproj -c $Configuration -f net8.0
    if ($LASTEXITCODE -ne 0) { throw "Component tests failed" }
    if ($env:OS -eq "Windows_NT") {
        dotnet build ui/tests/UiCompiler.Tests.csproj -c $Configuration -f net472 --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw "Framework tests build failed" }
        & "ui/tests/bin/$Configuration/net472/UiCompiler.Tests.exe"
        if ($LASTEXITCODE -ne 0) { throw "Framework plugin tests failed" }
    }
    $stage = Join-Path $repo "ui/artifacts/UiCompiler"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item $stage -ItemType Directory -Force | Out-Null
    Get-ChildItem "ui/bin/$Configuration/net472" -Filter *.dll | Where-Object { $_.Name -in @("BinaryAssetBuilder.UiCompiler.dll", "LibTessDotNet.dll", "Clipper2Lib.dll", "StbImageSharp.dll") } | Copy-Item -Destination $stage
    Copy-Item ui/README.md,ui/VENDOR.md -Destination $stage
    Copy-Item ui/licenses -Destination $stage -Recurse
    $revision = git rev-parse HEAD
    @{ commit = $revision; framework = "net472"; target = "Win32 RA3"; input = "Flash 8 FLA and AS source folder"; output = @("manifest", "bin", "relo", "imp") } | ConvertTo-Json | Set-Content (Join-Path $stage "build.json") -Encoding UTF8
    Compress-Archive -Path "$stage/*" -DestinationPath ui/artifacts/UiCompiler.zip -Force
} finally { Pop-Location }
