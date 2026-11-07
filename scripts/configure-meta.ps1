param(
    [string]$KeyFile,
    [switch]$ImportLocalKey,
    [string]$Model = 'muse-wonder-1.0',
    [string]$AdminOrigin = 'http://localhost:13088'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$key = if ($KeyFile) { (Get-Content -LiteralPath $KeyFile -Raw).Trim() } elseif ($ImportLocalKey) {
    if (Test-Path -LiteralPath "$repoRoot/.env") {
        $line = Get-Content -LiteralPath "$repoRoot/.env" | Where-Object { $_ -match '^META_API_KEY=' } | Select-Object -First 1
        if ($line) { ($line -split '=',2)[1].Trim().Trim('"').Trim("'") } else { $env:META_API_KEY }
    } else { $env:META_API_KEY }
} else { throw 'Provide -KeyFile <private-file> or explicitly use -ImportLocalKey for one-time import.' }
if ([string]::IsNullOrWhiteSpace($key)) { throw 'No Meta API key was found.' }
$token = (Get-Content -LiteralPath "$repoRoot/artifacts/admin.token.local.txt" -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$base = "$AdminOrigin/admin/v1/entities"
$api = (Invoke-RestMethod "$base/APIInfo?take=100" -Headers $headers).items | Where-Object Identifier -EQ 'meta'
$modelRow = (Invoke-RestMethod "$base/LLMModel?take=100" -Headers $headers).items | Where-Object Tag -EQ 'meta-muse-wonder'
$step = (Invoke-RestMethod "$base/LLMStep?take=100" -Headers $headers).items | Where-Object Tag -EQ 'classify-page-sections'
if (@($api).Count -ne 1 -or @($modelRow).Count -ne 1 -or @($step).Count -ne 1) { throw 'Start the current application first to seed Meta APIInfo, model and step.' }
# Secret travels only in the authenticated request body, never a URL or console output.
$body = @{ AppKey=$key; Enabled=$true; BaseURI='https://api.meta.ai/v1' } | ConvertTo-Json -Compress
$null = Invoke-RestMethod "$base/APIInfo/$($api.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body $body
$null = Invoke-RestMethod "$base/LLMModel/$($modelRow.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body (@{Name=$Model; Provider='Meta'} | ConvertTo-Json -Compress)
$null = Invoke-RestMethod "$base/LLMStep/$($step.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body (@{Enabled=$true; Model_ID=$modelRow.ID; ReasoningEffort=''} | ConvertTo-Json -Compress)
$configured = Invoke-RestMethod "$base/LLMStep/$($step.ID)" -Headers $headers
if ($configured.Model.id -ne $modelRow.ID) { throw 'The page LLMStep model link was not updated.' }
$key = $null; $body = $null
Write-Host "Persisted Meta APIInfo $($api.ID), configured model $Model and enabled LLMStep $($step.ID)."
Write-Host 'Restart the worker after rotating an existing key to clear its provider configuration cache.'
