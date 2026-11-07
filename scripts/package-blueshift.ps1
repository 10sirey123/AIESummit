# Maintainer-only: ordinary builds consume the committed bundle and need no source access.
param(
    [Parameter(Mandatory)][string]$SourcePath,
    [string]$ExpectedCommit = 'cc69aa4da64bf313ec7120711624816c7f9d4254',
    [string]$Version = '10.0.0-aiesummit.cc69aa4.gemini3'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = (Resolve-Path -LiteralPath $SourcePath).Path
$sourceCommit = (& git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $sourceCommit -ne $ExpectedCommit) { throw 'Source revision does not match ExpectedCommit.' }
$sourceChanges = & git -C $sourceRoot status --porcelain --untracked-files=normal
if ($LASTEXITCODE -or $sourceChanges) { throw 'Use a clean Blueshift source checkout.' }
$outputRoot = Join-Path $repoRoot 'dependencies/blueshift'
$stagingRoot = Join-Path $repoRoot ('artifacts/blueshift-bundle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stagingRoot | Out-Null
& docker build --file "$repoRoot/deploy/Blueshift.Bundle.Dockerfile" --build-context "aiesummit=$repoRoot" --build-arg "BUNDLE_VERSION=$Version" --build-arg "SOURCE_COMMIT=$sourceCommit" --output "type=local,dest=$stagingRoot" $sourceRoot
if ($LASTEXITCODE) { throw 'Blueshift bundle build failed; existing dependencies were not changed.' }
$expectedFiles = @(
    "packages/10123.Blueshift.Domain.$Version.nupkg",
    "packages/10123.Blueshift.Agents.$Version.nupkg",
    'worker-linux-x64.tar.gz', 'admin-api-linux-x64.tar.gz', 'admin-web.tar.gz',
    'bootstrap-admin-auth.ps1', 'nginx.conf'
)
$inventory = foreach ($relativePath in $expectedFiles) {
    $file = Get-Item -LiteralPath (Join-Path $stagingRoot $relativePath)
    if ($file.Length -ge 100MB) { throw "Artifact exceeds GitHub's file limit: $relativePath" }
    [ordered]@{ path = $relativePath; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
New-Item -ItemType Directory -Force "$outputRoot/packages" | Out-Null
foreach ($relativePath in $expectedFiles) {
    Copy-Item -LiteralPath (Join-Path $stagingRoot $relativePath) -Destination (Join-Path $outputRoot $relativePath) -Force
}
$manifest = [ordered]@{
    schemaVersion = 1
    sourceRepository = 'https://github.com/10123IO/10.Blueshift.123'
    sourceBranch = 'net10'
    sourceCommit = $sourceCommit
    sourcePatches = @(Get-ChildItem "$repoRoot/deploy/patches/*.patch" | Sort-Object Name | ForEach-Object { @{path="deploy/patches/$($_.Name)"; sha256=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()} })
    packageVersion = $Version
    targetFramework = 'net10.0'
    hostRuntime = 'linux-x64'
    selfContained = $false
    sdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0.403'
    frontendImage = 'node:22-alpine'
    builtAtUtc = [DateTime]::UtcNow.ToString('o')
    artifacts = @($inventory)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$outputRoot/manifest.json" -Encoding utf8
Write-Host "Built Blueshift $sourceCommit into dependencies/blueshift."
Write-Host 'When changing versions, update package references and remove obsolete package files deliberately; then verify and test all hosts.'
