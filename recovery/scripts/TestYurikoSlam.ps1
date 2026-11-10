param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$build=if($env:RUNNER_TEMP){Join-Path $env:RUNNER_TEMP "yuriko-slam-$Configuration"}else{Join-Path $PSScriptRoot '../../work/yuriko-slam'}
New-Item -ItemType Directory -Path $build -Force | Out-Null
$mode=if($Configuration -eq 'Debug'){'/Od'}else{'/O2'}
$native='recovery/src/Native'
$sources=@('Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter','WeaponSlotOrder','WeaponNugget','GameObject') | ForEach-Object {"$native/$_.cpp"}
& cl /nologo /EHsc /std:c++17 /MD /W3 $mode "/I$native" /Irecovery/tests @sources recovery/tests/YurikoSlamTest.cpp "/Fe:$build/test.exe" "/Fo:$build/"
if($LASTEXITCODE -ne 0){throw 'Native module test build failed'}
& "$build/test.exe"
if($LASTEXITCODE -ne 0){throw 'Native module test failed'}
& csc /nologo /unsafe /target:exe "/out:$build/schema-routes.exe" recovery/src/Managed/Hash/FastHash.cs recovery/tests/YurikoSchemaRoutes.cs
if($LASTEXITCODE -ne 0){throw 'Schema route test build failed'}
& "$build/schema-routes.exe" SDK/Schemas/xsd/CnC3Types.xsd slam
if($LASTEXITCODE -ne 0){throw 'Schema route validation failed'}
