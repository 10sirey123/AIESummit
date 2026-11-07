param([switch]$CacheOnly)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = [Guid]::NewGuid().ToString('N')
$outputRoot = Join-Path $repoRoot "artifacts/improvement/baseline-$runId"
$runnerRoot = Join-Path $repoRoot 'artifacts/improvement/runner'
Push-Location $repoRoot
try {
    if (!(Test-Path improvement/workflow.json)) { throw 'Start the improvement workbench once to capture the current workflow.' }
    dotnet publish AIESummit.WorkflowRunner/AIESummit.WorkflowRunner.csproj -c Release --nologo -o $runnerRoot
    if ($LASTEXITCODE) { throw 'Workflow runner build failed.' }
    New-Item -ItemType Directory -Force $outputRoot | Out-Null
    $savedConnection = $env:ConnectionStrings__AIESummitDb
    try {
        $env:ConnectionStrings__AIESummitDb = docker compose exec -T api printenv ConnectionStrings__AIESummitDb
        if ($LASTEXITCODE) { throw 'Start the local application stack first.' }
        $runnerArgs = @('run','--workflow','/repo/improvement/workflow.json','--manifest','/repo/artifacts/extraction-evaluation/manifest-final.json','--fixtures','/repo/artifacts/extraction-evaluation/final','--output','/results','--env-file','/repo/.env','--attempt-id','muse-baseline')
        if ($CacheOnly) { $runnerArgs += '--cache-only' }
        docker run --rm --network aiesummit_default --mount "type=bind,source=$repoRoot,target=/repo,readonly" --mount "type=bind,source=$runnerRoot,target=/app,readonly" --mount "type=bind,source=$outputRoot,target=/results" -e ConnectionStrings__AIESummitDb mcr.microsoft.com/dotnet/aspnet:10.0 dotnet /app/AIESummit.WorkflowRunner.dll @runnerArgs
        if ($LASTEXITCODE) { throw 'Baseline evaluation stopped. Retained results and telemetry remain in the output directory.' }
    } finally { $env:ConnectionStrings__AIESummitDb = $savedConnection }
    $identity = Get-Content -LiteralPath (Join-Path $outputRoot 'run-identity.json') -Raw | ConvertFrom-Json
    $summary = Get-Content -LiteralPath (Join-Path $outputRoot 'summary.json') -Raw | ConvertFrom-Json
    if ($summary.documentsAttempted -ne 20) { throw 'A complete baseline must include all 20 frozen benchmark documents.' }
    if ($CacheOnly) {
        $cacheMisses = 0
        foreach ($resultFile in Get-ChildItem -LiteralPath $outputRoot -Filter '*.json') {
            $result = Get-Content -LiteralPath $resultFile.FullName -Raw | ConvertFrom-Json
            if ($result.graph.nodes) { $cacheMisses += @($result.graph.nodes | Where-Object state -eq 'cache_miss').Count }
        }
        if ($cacheMisses) { throw 'Cache verification found missing node results. The existing baseline was preserved; inspect the retained report.' }
    }
    @{runId=$identity.runId;workflowHash=$identity.workflowHash;summary=$summary} | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath artifacts/improvement/baseline.json
    Write-Host "Baseline retained at $outputRoot. Refresh the improvement workbench to compare it."
} finally { Pop-Location }
