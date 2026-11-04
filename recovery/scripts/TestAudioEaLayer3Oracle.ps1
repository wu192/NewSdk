param([string]$OutputDirectory='recovery/evidence/audio/ealayer3', [Parameter(Mandatory=$true)][string]$UpstreamDirectory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $root
$out=[IO.Path]::GetFullPath($OutputDirectory)
$upstream=(Resolve-Path $UpstreamDirectory).Path
$bin=Join-Path $out 'oracle/bin';New-Item -ItemType Directory -Force $bin|Out-Null
$source=Join-Path $upstream 'BinaryAssetBuilder.EALayer3AudioCompiler'
$project=Join-Path $out 'oracle/Oracle.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net472</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><GenerateAssemblyInfo>false</GenerateAssemblyInfo><EnableDefaultCompileItems>false</EnableDefaultCompileItems><AssemblyName>BinaryAssetBuilder.EALayer3AudioCompiler</AssemblyName><LangVersion>7.3</LangVersion><PlatformTarget>x86</PlatformTarget></PropertyGroup>
  <ItemGroup><Compile Include="$source/**/*.cs" Exclude="$source/obj/**;$source/bin/**" />
  <Reference Include="BinaryAssetBuilder.Core"><HintPath>$root/SDK/tools/BinaryAssetBuilder.Core.dll</HintPath></Reference>
  <Reference Include="EALA.Hash"><HintPath>$root/SDK/tools/EALA.Hash.dll</HintPath></Reference>
  <Reference Include="EALA.Metrics"><HintPath>$root/SDK/tools/EALA.Metrics.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"@|Set-Content $project
& dotnet build $project -c Release -o $bin *> "$out/oracle/build.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/oracle/build.log" -Tail 50;throw 'Jonwil RA3 plugin oracle build failed'}
Copy-Item "$upstream/audio.dll" $bin
Copy-Item "$out/Release/bin/AudioEaLayer3Host.exe*" $bin
Copy-Item SDK/tools/*.dll $bin -Exclude BinaryAssetBuilder.AudioCompiler.dll
[xml]$xml=Get-Content "$out/fixtures/assets.xml" -Raw
foreach($node in @($xml.DocumentElement.ChildNodes)) {
    if($node.id -like 'short*' -or $node.File -like '*.aiff'){$xml.DocumentElement.RemoveChild($node)|Out-Null}
}
$xml.Save("$out/oracle/assets.xml")
& "$bin/AudioEaLayer3Host.exe" "$bin/BinaryAssetBuilder.EALayer3AudioCompiler.dll" BinaryAssetBuilder.EALayer3AudioCompiler.Plugin "$out/oracle/assets.xml" "$out/oracle/assets" 1 *> "$out/oracle/encode.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/oracle/encode.log" -Tail 70;throw 'Jonwil RA3 reference encode failed'}
$decoder=(Get-ChildItem (Join-Path $out 'vgmstream') -Recurse -Filter vgmstream-cli.exe|Select-Object -First 1).FullName
python recovery/scripts/CompareAudioEaLayer3Oracle.py --root $out --decoder $decoder --reference oracle --reference-codec 6
if($LASTEXITCODE -ne 0){throw 'Jonwil RA3 format/decoded behavior comparison failed'}
# The current bundled audio.dll uses snrtool's newer enum: 30=V1, 31=V2 PCM.
# Preserve the unmodified RA3 plugin output above. Build a separately identified
# V1 oracle by changing only codec selection and its matching quality condition.
$v1bin=Join-Path $out 'oracle-v1/bin';New-Item -ItemType Directory -Force $v1bin|Out-Null
$v1source=Join-Path $out 'oracle-v1/Plugin.cs'
$originalPlugin=Join-Path $source 'BinaryAssetBuilder/EALayer3AudioCompiler/Plugin.cs'
$text=[IO.File]::ReadAllText($originalPlugin)
if(([regex]::Matches($text,'codec = 31;')).Count -ne 1){throw 'Reference codec choice changed'}
$text=$text.Replace('codec = 31;','codec = 30;').Replace('codec == 31 || codec == 38','codec == 30 || codec == 38')
[IO.File]::WriteAllText($v1source,$text)
$v1project=Join-Path $out 'oracle-v1/OracleV1.csproj'
$projectText=[IO.File]::ReadAllText($project).Replace("<Compile Include=`"$source/**/*.cs`" Exclude=`"$source/obj/**;$source/bin/**`" />", "<Compile Include=`"$source/**/*.cs`" Exclude=`"$source/obj/**;$source/bin/**;$originalPlugin`" /><Compile Include=`"$v1source`" />")
[IO.File]::WriteAllText($v1project,$projectText)
& dotnet build $v1project -c Release -o $v1bin *> "$out/oracle-v1/build.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/oracle-v1/build.log" -Tail 50;throw 'V1 reference adapter build failed'}
Copy-Item "$upstream/audio.dll","$bin/AudioEaLayer3Host.exe","$bin/AudioEaLayer3Host.exe.config" $v1bin
Copy-Item SDK/tools/*.dll $v1bin -Exclude BinaryAssetBuilder.AudioCompiler.dll
& "$v1bin/AudioEaLayer3Host.exe" "$v1bin/BinaryAssetBuilder.EALayer3AudioCompiler.dll" BinaryAssetBuilder.EALayer3AudioCompiler.Plugin "$out/oracle/assets.xml" "$out/oracle-v1/assets" 1 *> "$out/oracle-v1/encode.log"
if($LASTEXITCODE -ne 0){Get-Content "$out/oracle-v1/encode.log" -Tail 60;throw 'V1 reference adapter encode failed'}
python recovery/scripts/CompareAudioEaLayer3Oracle.py --root $out --decoder $decoder --reference oracle-v1 --reference-codec 5
if($LASTEXITCODE -ne 0){throw 'V1 reference format/signal comparison failed'}
