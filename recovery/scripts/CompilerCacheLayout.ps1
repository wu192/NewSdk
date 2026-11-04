# Test fixtures create ordinary, absolute SDK/MOD directories. Keep their cache
# oracle independent of --out, matching the production normalized-path identity.
function Get-CompilerTestCacheRoot([string]$SdkRoot,[string]$ModRoot) {
    $sdk=(Resolve-Path -LiteralPath $SdkRoot).ProviderPath
    $mod=(Resolve-Path -LiteralPath $ModRoot).ProviderPath.TrimEnd([char[]]'\/')
    $normalized=$mod.Replace([IO.Path]::AltDirectorySeparatorChar,[IO.Path]::DirectorySeparatorChar).ToUpperInvariant()+[IO.Path]::DirectorySeparatorChar
    $hash=[Security.Cryptography.SHA256]::Create()
    try {$identity=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalized)),0,16).Replace('-','').ToLowerInvariant()}
    finally {$hash.Dispose()}
    $name=[IO.Path]::GetFileName($mod).ToLowerInvariant();$label=[Text.StringBuilder]::new()
    foreach($value in $name.ToCharArray()) {
        if($label.Length -eq 12){break}
        [void]$label.Append($(if([char]::IsLetterOrDigit($value) -or $value -eq '-' -or $value -eq '_'){$value}else{'_'}))
    }
    if($label.Length -eq 0){[void]$label.Append('Mod')}
    Join-Path (Join-Path $sdk 'SuperCompilerCache') ($label.ToString()+'_'+$identity)
}
