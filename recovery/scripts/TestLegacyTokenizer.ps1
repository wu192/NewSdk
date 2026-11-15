param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$build = [IO.Path]::GetFullPath($BuildDirectory)
$work = Join-Path $env:RUNNER_TEMP 'legacy-tokenizer-validation'
New-Item -ItemType Directory -Force $work | Out-Null
$csc = "$env:WINDIR/Microsoft.NET/Framework/v2.0.50727/csc.exe"
function Checked([string]$Message) { if ($LASTEXITCODE -ne 0) { throw $Message } }
function Summary([string]$Message) {
    Write-Host $Message
    if ($env:GITHUB_STEP_SUMMARY) { $Message | Add-Content $env:GITHUB_STEP_SUMMARY }
}
Push-Location $root
try {
    & $csc /nologo /platform:x86 /r:System.Xml.dll "/r:$root/SDK/tools/BinaryAssetBuilder.Core.dll" "/out:$build/LegacyTokenizerOracle.exe" (Resolve-Path 'recovery/tests/LegacyTokenizerOracle.cs').Path
    Checked 'CLR 2 oracle build failed'
    foreach ($mode in @('original','candidate')) {
        $dir = Join-Path $work $mode
        New-Item -ItemType Directory -Force $dir | Out-Null
        Copy-Item SDK/tools/*.dll $dir
        Copy-Item "$build/LegacyTokenizerOracle.exe" $dir
        if ($mode -eq 'candidate') { Copy-Item "$build/BinaryAssetBuilder.Tokenizer.dll" $dir -Force }
        & "$dir/LegacyTokenizerOracle.exe" dump "$dir/BinaryAssetBuilder.Tokenizer.dll" "$dir/buffers"
        Checked "$mode CLR 2 plugin regression failed"
    }
    & "$work/candidate/LegacyTokenizerOracle.exe" compare "$work/original/buffers" "$work/candidate/buffers"
    Checked 'Original/candidate three-buffer comparison failed'
    & "$work/candidate/LegacyTokenizerOracle.exe" dozer "$work/candidate/BinaryAssetBuilder.Tokenizer.dll" "$work/candidate/dozer"
    Checked 'Dozer managed/native contract failed'
    Summary 'PASS: CLR 2/x86 identity, plugin metadata, ordinary GameObject/WeaponTemplate/ArmorTemplate three-buffer byte comparison, Dozer native layout/token/relocation contract.'

    # An untouched tools/config directory is copied for actual BAB execution.
    # The required schema delta is tested separately from the DLL-only run.
    $sdk = Join-Path $work 'sdk'
    Copy-Item SDK $sdk -Recurse
    Copy-Item "$build/BinaryAssetBuilder.Tokenizer.dll" "$sdk/tools/BinaryAssetBuilder.Tokenizer.dll" -Force
    $schema = Join-Path $sdk 'Schemas/xsd/AssetTypeGameObject.xsd'
    $schemaWithDozer = [IO.File]::ReadAllBytes($schema)
    $originalSchema = New-Object Diagnostics.Process
    $originalSchema.StartInfo.FileName = 'git'
    $originalSchema.StartInfo.Arguments = 'show 103f8ea14e6687884518047a861fc42e76abec0f:SDK/Schemas/xsd/AssetTypeGameObject.xsd'
    $originalSchema.StartInfo.UseShellExecute = $false
    $originalSchema.StartInfo.RedirectStandardOutput = $true
    $originalSchema.Start() | Out-Null
    $stream = [IO.File]::Create($schema)
    try { $originalSchema.StandardOutput.BaseStream.CopyTo($stream) } finally { $stream.Dispose() }
    $originalSchema.WaitForExit()
    if ($originalSchema.ExitCode -ne 0) { throw 'Cannot restore original SDK schema' }
    $data = Join-Path $sdk 'Mods/LegacyDozer/Data'
    New-Item -ItemType Directory -Force $data | Out-Null
    Copy-Item recovery/examples/legacy-dozer/*.xml $data
    $configHash = (Get-FileHash "$sdk/tools/BinaryAssetBuilder.exe.config").Hash
    $arguments = @("`"$data/Mod.xml`"", '/csc:false', '/ls:true', '/osh:false', '/pc:true', '/res:true', '/slowclean:true', '/ss:true', "/data:`".;.\Mods\LegacyDozer\Data;.\Mods;.\SageXml`"", "/art:`".;.\Art`"", "/audio:`".;.\Audio`"")
    function InvokeSdk([string]$Label) {
        $output = Join-Path $work "sdk-$Label-output"
        New-Item -ItemType Directory -Force $output | Out-Null
        $allArguments = $arguments + @("/od:`"$output`"", "/iod:`"$output`"")
        $process = Start-Process "$sdk/tools/BinaryAssetBuilder.exe" -ArgumentList ($allArguments -join ' ') -WorkingDirectory $sdk -PassThru -RedirectStandardOutput "$work/sdk-$Label.log" -RedirectStandardError "$work/sdk-$Label.err"
        if (!$process.WaitForExit(180000)) { $process.Kill(); throw "SDK $Label timed out" }
        $process.Refresh()
        Get-Content "$work/sdk-$Label.log", "$work/sdk-$Label.err" | Select-Object -Last 35 | ForEach-Object { Summary $_ }
        $errors = @(Select-String -Path "$work/sdk-$Label.log", "$work/sdk-$Label.err" -Pattern '\] (Error|Critical):')
        $products = @(foreach ($extension in @('bin','manifest','imp','relo')) { Get-ChildItem $output -Recurse -Filter "mod.$extension" })
        return @{ ExitCode = $process.ExitCode; Output = $output; Errors = $errors; Succeeded = ($process.ExitCode -eq 0 -and $errors.Count -eq 0 -and $products.Count -eq 4) }
    }
    $strict = InvokeSdk 'dll-only'
    Summary "DLL-only SDK compile exit: $($strict.ExitCode). Original GameObject XSD has no Dozer declaration."
    [IO.File]::WriteAllBytes($schema, $schemaWithDozer)
    $integrated = InvokeSdk 'required-schema'
    if (!$integrated.Succeeded) { $integrated.Errors | ForEach-Object { Summary $_.Line }; throw 'Real SDK Dozer example compile failed or logged input errors with required schema' }
    $manifest = Get-ChildItem $integrated.Output -Recurse -Filter 'mod.manifest'
    & "$work/candidate/LegacyTokenizerOracle.exe" sdk "$work/candidate/BinaryAssetBuilder.Tokenizer.dll" $manifest.FullName
    Checked 'Final SDK NativeDozer buffers/contract verification failed'
    if ((Get-FileHash "$sdk/tools/BinaryAssetBuilder.exe.config").Hash -ne $configHash) { throw 'SDK config changed' }
    Summary 'PASS: real SDK example compiled with required XSD; original EXE/config/other DLLs retained.'
    if ($env:GITHUB_ENV) {
        'LEGACY_CANDIDATE_READY=true' | Add-Content $env:GITHUB_ENV
        "LEGACY_STRICT_EXIT=$($strict.ExitCode)" | Add-Content $env:GITHUB_ENV
    }
    if (!$strict.Succeeded) { throw 'Incomplete strict plan gate: original SDK schema rejects Dozer; required one-line XSD change cannot be omitted.' }
    Summary 'PASS: strict DLL-only SDK compile.'
} finally { Pop-Location }
