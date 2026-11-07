param([switch]$ImportBaseline)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repoRoot
try {
    & "$PSScriptRoot/verify-dependencies.ps1"
    if ($LASTEXITCODE) { throw 'Dependency verification failed.' }
    $stateRoot = Join-Path $repoRoot 'artifacts/improvement'
    $runnerRoot = Join-Path $stateRoot 'runner'
    $webRoot = Join-Path $stateRoot 'web-host'
    New-Item -ItemType Directory -Force $runnerRoot,$webRoot | Out-Null
    dotnet publish AIESummit.WorkflowRunner/AIESummit.WorkflowRunner.csproj -c Release --nologo -o $runnerRoot
    if ($LASTEXITCODE) { throw 'Workflow runner build failed.' }
    if (!(Test-Path improvement/workflow.json)) {
        $capture = Join-Path $stateRoot 'bootstrap'
        New-Item -ItemType Directory -Force $capture | Out-Null
        $savedConnection = $env:ConnectionStrings__AIESummitDb
        try {
            $env:ConnectionStrings__AIESummitDb = docker compose exec -T api printenv ConnectionStrings__AIESummitDb
            if ($LASTEXITCODE) { throw 'Start the local application stack first with ./start.ps1.' }
            docker run --rm --network aiesummit_default --mount "type=bind,source=$runnerRoot,target=/app,readonly" --mount "type=bind,source=$capture,target=/results" -e ConnectionStrings__AIESummitDb mcr.microsoft.com/dotnet/aspnet:10.0 dotnet /app/AIESummit.WorkflowRunner.dll snapshot --output /results/workflow.json
            if ($LASTEXITCODE) { throw 'Could not capture the persisted extraction workflow.' }
        } finally { $env:ConnectionStrings__AIESummitDb = $savedConnection }
        New-Item -ItemType Directory -Force improvement | Out-Null
        Copy-Item -LiteralPath (Join-Path $capture 'workflow.json') -Destination improvement/workflow.json
    }
    if ($ImportBaseline) {
        dotnet run --project AIESummit.Evaluation -- enrich-results artifacts/extraction-evaluation/manifest-final.json artifacts/extraction-evaluation/final --run ead9272caae140fcaf21df00b749b74c
        if ($LASTEXITCODE) { throw 'Baseline instrumentation enrichment failed.' }
    }
    dotnet publish AIESummit.Improvement/AIESummit.Improvement.csproj -c Release --nologo -o $webRoot
    if ($LASTEXITCODE) { throw 'Improvement workbench build failed.' }
    Write-Host 'Improvement workbench: http://127.0.0.1:18110'
    dotnet (Join-Path $webRoot 'AIESummit.Improvement.dll') --repo $repoRoot --contentRoot $webRoot
} finally { Pop-Location }
