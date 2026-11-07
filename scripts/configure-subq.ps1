param(
    [string]$KeyFile,
    [switch]$ImportLocalKey,
    [switch]$SelectPageStep,
    [string]$AdminOrigin = 'http://localhost:13088'
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$key = if ($KeyFile) { (Get-Content -LiteralPath $KeyFile -Raw).Trim() } elseif ($ImportLocalKey) {
    $line = if (Test-Path -LiteralPath "$repoRoot/.env") {
        Get-Content -LiteralPath "$repoRoot/.env" | Where-Object { $_ -match '^SUBQ_API_KEY=' } | Select-Object -First 1
    }
    if ($line) { ($line -split '=',2)[1].Trim().Trim('"').Trim("'") } else { $env:SUBQ_API_KEY }
} else { throw 'Provide -KeyFile <private-file> or explicitly use -ImportLocalKey for one-time import.' }
if ([string]::IsNullOrWhiteSpace($key)) { throw 'No SubQ API key was found.' }
$token = (Get-Content -LiteralPath "$repoRoot/artifacts/admin.token.local.txt" -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$base = "$AdminOrigin/admin/v1/entities"
$api = (Invoke-RestMethod "$base/APIInfo?take=100" -Headers $headers).items | Where-Object Identifier -EQ 'subq'
$model = (Invoke-RestMethod "$base/LLMModel?take=100" -Headers $headers).items | Where-Object Tag -EQ 'subq-preview'
if (@($api).Count -ne 1 -or @($model).Count -ne 1) { throw 'Start the current application first to seed SubQ APIInfo and model.' }
try {
    # Credentials travel only in the authenticated request body, never a URL or console output.
    $body = @{ AppKey=$key; Enabled=$true; BaseURI='https://api.subq.ai/v1' } | ConvertTo-Json -Compress
    $null = Invoke-RestMethod "$base/APIInfo/$($api.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body $body
    $null = Invoke-RestMethod "$base/LLMModel/$($model.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body (@{Name='subq-preview'; Provider='SubQ'} | ConvertTo-Json -Compress)
    if ($SelectPageStep) {
        $step = (Invoke-RestMethod "$base/LLMStep?take=100" -Headers $headers).items | Where-Object Tag -EQ 'classify-page-sections'
        if (@($step).Count -ne 1) { throw 'Expected exactly one native page classification step.' }
        $null = Invoke-RestMethod "$base/LLMStep/$($step.ID)" -Method Put -Headers $headers -ContentType 'application/json' -Body (@{Model_ID=$model.ID; Enabled=$true; ReasoningEffort='low'} | ConvertTo-Json -Compress)
        $configured = Invoke-RestMethod "$base/LLMStep/$($step.ID)" -Headers $headers
        if ($configured.Model.id -ne $model.ID -or $configured.ReasoningEffort -ne 'low') { throw 'Page step selection was not persisted.' }
    }
} finally { $key=$null; $body=$null; $token=$null; $headers=$null }
Write-Host "Persisted SubQ APIInfo $($api.ID) and model $($model.ID) (subq-preview)."
if ($SelectPageStep) { Write-Host "Selected SubQ with low reasoning on page LLMStep $($step.ID)." }
Write-Host 'SubQ must grant Preview access to this key. Restart the worker after rotating an existing key to clear its provider configuration cache.'
