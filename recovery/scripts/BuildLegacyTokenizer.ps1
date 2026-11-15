param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $out | Out-Null
$framework = "$env:WINDIR/Microsoft.NET/Framework/v2.0.50727"
$csc = Join-Path $framework 'csc.exe'
if (!(Test-Path $csc)) { throw 'Enable Windows NetFx3 before building; CLR 2 compiler is required.' }
Push-Location $root
try {
    & $csc /nologo /target:module /platform:x86 /optimize+ /r:System.Xml.dll "/r:$root/SDK/tools/BinaryAssetBuilder.Core.dll" "/out:$out/Plugin.netmodule" (Resolve-Path 'recovery/src/LegacyTokenizer/Plugin.cs').Path 2>&1 | Tee-Object "$out/build.log"
    if ($LASTEXITCODE -ne 0) { throw 'CLR 2 plugin netmodule build failed' }
    $sources = @('Relocation','ScalarParsing','DataInitializers','ResourceMarshaling','EnumBitset','ObjectFilter','WeaponSlotOrder','GameObject','ArmorTemplate','WeaponTemplate','WeaponNugget') | ForEach-Object { "recovery/src/Native/$_.cpp" }
    & cl /nologo /c /MT /EHsc /O2 /fp:precise /std:c++17 "/Fo$out/" @sources recovery/src/LegacyTokenizer/NativeExports.cpp 2>&1 | Tee-Object "$out/build.log" -Append
    if ($LASTEXITCODE -ne 0) { throw 'Native legacy compiler build failed' }
    $objects = Get-ChildItem $out -Filter '*.obj' | ForEach-Object { $_.FullName }
    # Positional netmodule input merges its IL into this PE. /ASSEMBLYMODULE
    # would leave an external .netmodule dependency and is deliberately unused.
    & link /nologo /DLL /LTCG /MACHINE:X86 /CLRIMAGETYPE:IJW /ASSEMBLYVERSION:1.0.3492.22559 "/DEF:$root/recovery/src/LegacyTokenizer/NativeExports.def" "/OUT:$out/BinaryAssetBuilder.Tokenizer.dll" "/LIBPATH:$framework" @objects "$out/Plugin.netmodule" mscoree.lib 2>&1 | Tee-Object "$out/build.log" -Append
    if ($LASTEXITCODE -ne 0) { throw 'Single DLL CLR 2 link failed' }
} finally { Pop-Location }
