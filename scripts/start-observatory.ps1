param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet build AIESummit.Observatory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Observatory build failed.' }
    Push-Location AIESummit.Observatory/client
    try { npm ci --prefer-offline --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'Client setup failed.' } }
    finally { Pop-Location }
    Start-Process dotnet -ArgumentList @('run','--project','AIESummit.Observatory','--no-build') -WorkingDirectory $repo -WindowStyle Hidden
    Start-Process npm.cmd -ArgumentList @('run','dev','--','--port','18115','--strictPort') -WorkingDirectory (Join-Path $repo 'AIESummit.Observatory/client') -WindowStyle Hidden
    Write-Host 'Observatory: http://127.0.0.1:18115 (read-only API: port 18114)'
} finally { Pop-Location }
