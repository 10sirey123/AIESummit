$ErrorActionPreference = 'Stop'
$bundleRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../dependencies/blueshift'))
$manifest = Get-Content -LiteralPath "$bundleRoot/manifest.json" -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.artifacts.Count -ne 7) { throw 'Unsupported or incomplete dependency manifest.' }
foreach ($artifact in $manifest.artifacts) {
    $path = [IO.Path]::GetFullPath((Join-Path $bundleRoot $artifact.path))
    if (-not $path.StartsWith($bundleRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid dependency path.' }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $artifact.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $artifact.sha256) {
        throw "Dependency integrity check failed: $($artifact.path)"
    }
}
Write-Host "Verified $($manifest.artifacts.Count) Blueshift artifacts at $($manifest.sourceCommit)."
foreach ($patch in $manifest.sourcePatches) {
    $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot $patch.path))
    if (-not $path.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $patch.sha256) { throw 'Framework source patch integrity check failed.' }
}
