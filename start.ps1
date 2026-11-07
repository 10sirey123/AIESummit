$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./scripts/verify-dependencies.ps1
    & ./dependencies/blueshift/bootstrap-admin-auth.ps1 -SettingsPath "$PSScriptRoot/deploy/admin-auth.local.json" -TokenPath "$PSScriptRoot/artifacts/admin.token.local.txt" -Role Admin -Subject aiesummit-operator -Minutes 240
    docker compose up --build --detach --wait --wait-timeout 180
    if ($LASTEXITCODE) { throw 'The AIESummit stack did not start.' }
    Write-Host 'AIESummit  http://localhost:18080'
    Write-Host 'Admin      http://localhost:13088'
    Write-Host 'Admin sign-in token: artifacts/admin.token.local.txt'
} finally {
    Pop-Location
}
