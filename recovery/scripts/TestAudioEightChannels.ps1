param([Parameter(Mandatory=$true)][string]$NewWorldWave,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-Location (Resolve-Path "$PSScriptRoot/../..")
if(!$OutputDirectory){$OutputDirectory="recovery/evidence/audio/new-world-eight/$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT"}
if(Test-Path $OutputDirectory){throw 'Refusing to overwrite eight-channel evidence'}
New-Item -ItemType Directory -Force $OutputDirectory|Out-Null
$out=(Resolve-Path $OutputDirectory).Path
$wave=(Resolve-Path -LiteralPath $NewWorldWave).Path
if((Get-FileHash $wave).Hash.ToLowerInvariant() -ne '153891dd0a249385207c22a5bdf0962d706ad8bbc59d226d03d9de08a69f703e'){throw 'Original New World WAVE identity differs'}
$state=@{sourceCommit=(git rev-parse HEAD).Trim();runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;status='started';inputSha256='153891dd0a249385207c22a5bdf0962d706ad8bbc59d226d03d9de08a69f703e';scope='WAVE8 PCM8/16/24 NONE/XAS, RAM/streamed, standard/extensible, loops and resampling; original three buffers and CDATA; existing supported channels and refusal boundaries retained';configurations=@()}
try{
 $env:AUDIO_NEW_WORLD_FILE=$wave
 ./recovery/scripts/TestAudioPlugin.ps1 -OutputDirectory "$out/plugin" -Groups @('multichannel-eight','new-world-eight','multichannel','business','pcm','xas','defaults','rates','main','pcm8','formats','loops','aiffloops') -IncludeMp3
 $plugin=Get-Content "$out/plugin/validation.json" -Raw|ConvertFrom-Json
 if($plugin.status -ne 'passed'){throw 'Eight-channel and existing audio byte comparison failed'}
 $csc="$env:WINDIR/Microsoft.NET/Framework/v4.0.30319/csc.exe"
 $boundary="$out/AudioMultichannelBoundaryOracle.exe"
 & $csc /nologo /platform:x86 "/out:$boundary" (Resolve-Path recovery/tests/AudioMultichannelBoundaryOracle.cs).Path (Resolve-Path recovery/tests/AudioMultichannelCases.cs).Path (Resolve-Path recovery/tests/AudioInputFormatCases.cs).Path (Resolve-Path recovery/tests/AudioAifcInputCases.cs).Path (Resolve-Path recovery/tests/AudioAdp4InputCases.cs).Path
 if($LASTEXITCODE -ne 0){throw 'Boundary oracle compilation failed'}
 $direct="$out/AudioMultichannelDirectOracle.exe"
 & $csc /nologo /platform:x86 /r:System.Core.dll "/out:$direct" (Resolve-Path recovery/tests/AudioMultichannelDirectOracle.cs).Path
 if($LASTEXITCODE -ne 0){throw 'Direct-entry oracle compilation failed'}
 foreach($mode in @('Debug','Release')){
  $library=Join-Path $plugin.runtimeDirectories.$mode 'Recovered.AudioCompiler.dll'
  & $boundary $library "$out/boundary-inputs" *> "$out/$mode-boundaries.log"
  if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-boundaries.log";throw "$mode channel boundaries failed"}
  & $direct $library "$out/plugin/multichannel-eight-original.txt" "$out/plugin" 32 *> "$out/$mode-direct-eight.log"
  if($LASTEXITCODE -ne 0){Get-Content "$out/$mode-direct-eight.log";throw "$mode direct eight-channel byte comparison failed"}
  $state.configurations+=@{configuration=$mode;boundaries=42;directCases=32;directEncoderComparisons=64;passed=$true}
 }
 ./recovery/scripts/TestAudioEaLayer3.ps1 -OutputDirectory "$out/ealayer3"
 $ea=Get-Content "$out/ealayer3/validation.json" -Raw|ConvertFrom-Json
 if(!$ea.passed){throw 'Independent EALayer3 decoding failed'}
 $state.ealayer3DecodedCases=$ea.cases.Count
 $state.status='passed'
}catch{$state.status='failed';$state.error=$_.Exception.ToString();throw}
finally{$state|ConvertTo-Json -Depth 12|Set-Content "$out/validation.json";Remove-Item Env:AUDIO_NEW_WORLD_FILE -ErrorAction SilentlyContinue}
