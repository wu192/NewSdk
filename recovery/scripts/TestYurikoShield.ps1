param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$build=Join-Path $env:RUNNER_TEMP "yuriko-shield-$Configuration"
if (-not $env:RUNNER_TEMP) {$build=Join-Path $PSScriptRoot '../../work/yuriko-shield'}
New-Item -ItemType Directory -Path $build -Force | Out-Null
$mode=if($Configuration -eq 'Debug'){'/Od'}else{'/O2'}
$native='recovery/src/Native'
$sources=@('Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter','WeaponSlotOrder','GameObject') | ForEach-Object {"$native/$_.cpp"}
& cl /nologo /EHsc /std:c++17 /MD /W3 $mode "/I$native" /Irecovery/tests @sources recovery/tests/YurikoShieldTest.cpp "/Fe:$build/test.exe" "/Fo:$build/"
if($LASTEXITCODE -ne 0){throw 'Native module test build failed'}
& "$build/test.exe"
if($LASTEXITCODE -ne 0){throw 'Native module test failed'}
& csc /nologo /unsafe /target:exe "/out:$build/schema-routes.exe" recovery/src/Managed/Hash/FastHash.cs recovery/tests/YurikoSchemaRoutes.cs
if($LASTEXITCODE -ne 0){throw 'Schema route test build failed'}
& "$build/schema-routes.exe" SDK/Schemas/xsd/CnC3Types.xsd shield
if($LASTEXITCODE -ne 0){throw 'Schema route validation failed'}
