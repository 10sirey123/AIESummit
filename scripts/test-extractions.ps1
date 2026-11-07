param()
$ErrorActionPreference = 'Stop'
$container = 'aiesummit-extraction-test-' + [Guid]::NewGuid().ToString('N')
$previous = $env:AIESUMMIT_EXTRACTION_TEST_CONNECTION
$mongoContainer = 'aiesummit-mongo-test-' + [Guid]::NewGuid().ToString('N')
$previousMongo = $env:AIESUMMIT_MONGO_TEST_URI
try {
    docker run --detach --name $container --publish '127.0.0.1::5432' --env POSTGRES_DB=aiesummit_extraction_test --env POSTGRES_USER=extractiontest --env POSTGRES_PASSWORD=synthetic-test-only postgres:16 | Out-Null
    if ($LASTEXITCODE) { throw 'Could not start disposable PostgreSQL.' }
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        docker exec $container pg_isready -U extractiontest -d aiesummit_extraction_test *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }; Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'Disposable PostgreSQL did not become ready.' }
    $port = ((docker port $container 5432/tcp).Trim() -split ':')[-1]
    $env:AIESUMMIT_EXTRACTION_TEST_CONNECTION = "Host=127.0.0.1;Port=$port;Database=aiesummit_extraction_test;Username=extractiontest;Password=synthetic-test-only"
    docker run --detach --name $mongoContainer --publish '127.0.0.1::27017' mongo:8.0 | Out-Null
    if ($LASTEXITCODE) { throw 'Could not start disposable MongoDB.' }
    $mongoPort = ((docker port $mongoContainer 27017/tcp).Trim() -split ':')[-1]
    $env:AIESUMMIT_MONGO_TEST_URI = "mongodb://127.0.0.1:$mongoPort"
    dotnet test "$PSScriptRoot/../AIESummit.Extraction.Tests/AIESummit.Extraction.Tests.csproj" -c Release --nologo
    if ($LASTEXITCODE) { throw 'Extraction acceptance tests failed.' }
} finally {
    $env:AIESUMMIT_EXTRACTION_TEST_CONNECTION = $previous
    $env:AIESUMMIT_MONGO_TEST_URI = $previousMongo
    docker rm --force --volumes $container *> $null
    docker rm --force --volumes $mongoContainer *> $null
}
