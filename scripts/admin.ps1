param(
    [Parameter(Mandatory, Position=0)]
    [ValidateSet('build','export','validate','plan','apply','schema')][string]$Action,
    [ValidateSet('keep','merge','update')][string]$Policy = 'update',
    [string]$Preview,
    [switch]$IncludeBindings
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$seedRoot = Join-Path $repoRoot 'artifacts/seeding'
if ($IncludeBindings -and $Action -ne 'export') { throw '-IncludeBindings is only valid with export. Review the exported manifest before planning.' }
if ($Preview -and $Action -ne 'apply') { throw '-Preview is only valid with apply.' }
if ($Action -eq 'apply' -and [string]::IsNullOrWhiteSpace($Preview)) { throw 'Apply requires -Preview <token> from a reviewed plan.' }
$compose = @('compose','--project-directory',$repoRoot,'-f',"$repoRoot/compose.yaml",'-f',"$repoRoot/deploy/compose.cli.yaml",'--profile','tools')
New-Item -ItemType Directory -Force -Path $seedRoot | Out-Null
& "$PSScriptRoot/verify-dependencies.ps1"
if ($Action -eq 'build') {
    & docker @compose build configuration
    if ($LASTEXITCODE) { throw 'Configuration image build failed.' }
    return
}
$run = $compose + @('run','--rm','--no-deps','-T')
if ($Action -in @('export','schema')) {
    $command = $run + @('--entrypoint','dotnet','configuration','/configuration/AIESummit.Configuration.dll')
    if ($Action -eq 'export') {
        $command += @('export','/seeds/workflow.json')
        if ($IncludeBindings) { $command += '--include-bindings' }
    } else { $command += 'schema' }
    & docker @command
    if ($LASTEXITCODE) { throw 'Configuration command failed.' }
    return
}
if (!(Test-Path -LiteralPath "$seedRoot/workflow.json")) { throw 'Export the code definitions first: ./scripts/admin.ps1 export' }
$command = $run + @('configuration','seed',$Action,'/seeds/workflow.json','--policy',$Policy)
if ($Action -eq 'apply') { $command += @('--preview',$Preview) }
$output = & docker @command
if ($LASTEXITCODE) { $output | Write-Output; throw 'Seed operation failed; inspect the safe diagnostic above.' }
$output | Write-Output
if ($Action -eq 'plan') {
    $result = $output -join "`n" | ConvertFrom-Json
    if ($result.status -ne 'planned' -or !$result.plan.PreviewToken) { throw 'CLI returned no seed preview token.' }
    $output | Set-Content -LiteralPath "$seedRoot/plan.json" -Encoding utf8
    Write-Host "Plan saved to artifacts/seeding/plan.json. Apply the reviewed plan with -Policy $Policy -Preview $($result.plan.PreviewToken)."
}
