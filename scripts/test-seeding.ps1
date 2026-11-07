param([switch]$VerifyCli)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$container = 'aiesummit-seed-test-' + [Guid]::NewGuid().ToString('N')
$oldConnection = $env:AIESUMMIT_SEED_TEST_CONNECTION
try {
    & docker run --detach --name $container --publish '127.0.0.1::5432' --env POSTGRES_DB=aiesummit_seed_test --env POSTGRES_USER=seedtest --env POSTGRES_PASSWORD=synthetic-test-only postgres:16 | Out-Null
    if ($LASTEXITCODE) { throw 'Could not start disposable PostgreSQL.' }
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        & docker exec $container pg_isready -U seedtest -d aiesummit_seed_test *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }; Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'Disposable PostgreSQL did not become ready.' }
    $binding = (& docker port $container 5432/tcp).Trim()
    $port = ($binding -split ':')[-1]
    $env:AIESUMMIT_SEED_TEST_CONNECTION = "Host=127.0.0.1;Port=$port;Database=aiesummit_seed_test;Username=seedtest;Password=synthetic-test-only"
    & dotnet test "$repoRoot/AIESummit.Tests/AIESummit.Tests.csproj" --nologo --filter 'FullyQualifiedName~SeedingTests'
    if ($LASTEXITCODE) { throw 'Seed acceptance tests failed.' }
    if ($VerifyCli) {
        $seedRoot = Join-Path $repoRoot ('artifacts/seed-cli-test-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $seedRoot | Out-Null
        $dockerArgs = @('run','--rm','--network',"container:$container",'--env',
            'ConnectionStrings__AIESummitDb=Host=127.0.0.1;Database=aiesummit_seed_test;Username=seedtest;Password=synthetic-test-only',
            '--volume',"${seedRoot}:/seeds")
        & docker @dockerArgs --entrypoint dotnet aiesummit-configuration /configuration/AIESummit.Configuration.dll export /seeds/workflow.json
        if ($LASTEXITCODE) { throw 'CLI fixture export failed.' }
        $plan = & docker @dockerArgs aiesummit-configuration seed plan /seeds/workflow.json --policy update
        if ($LASTEXITCODE) { throw 'CLI plan failed.' }
        $token = ($plan -join "`n" | ConvertFrom-Json).plan.PreviewToken
        if (!$token) { throw 'CLI plan did not return a preview token.' }
        $applied = & docker @dockerArgs aiesummit-configuration seed apply /seeds/workflow.json --policy update --preview $token
        if ($LASTEXITCODE -or ($applied -join "`n" | ConvertFrom-Json).status -ne 'applied') { throw 'CLI apply failed.' }
        $again = & docker @dockerArgs aiesummit-configuration seed plan /seeds/workflow.json --policy update
        if ($LASTEXITCODE) { throw 'CLI re-plan failed.' }
        $result = $again -join "`n" | ConvertFrom-Json
        if (@($result.plan.Items | Where-Object Action -NE 'keep').Count) { throw 'CLI did not converge after application.' }
        Write-Host 'Verified bundled CLI export, plan, previewed apply and repeat-plan convergence on disposable PostgreSQL.'
    }
} finally {
    $env:AIESUMMIT_SEED_TEST_CONNECTION = $oldConnection
    # Only this invocation's uniquely named disposable container is removed.
    & docker rm --force --volumes $container *> $null
}
