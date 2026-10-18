# Run: .\deploy\run-migrations.ps1. The database target comes only from .env.
# Explicit OpenIddict root URLs avoid needing Cloud Run URL discovery.
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-GridDeployment
$connection = Get-GridSetting -Names @('ConnectionStrings__Default') -Required
$cloud = Get-GridCloudConfig
$apiUrl = Get-GridSetting -Names @('OpenIddict__Applications__Grid_Swagger__RootUrl')
if (-not $apiUrl) { $apiUrl = Get-GridApiUrl $cloud }
$webUrl = Get-GridWebUrl $cloud
$null = Resolve-GridTool 'dotnet'
$values = @{}
foreach ($key in $script:GridSettings.Keys) { $values[$key] = $script:GridSettings[$key] }
$values['ConnectionStrings__Default'] = $connection
$values['OpenIddict__Applications__Grid_App__ClientId'] = Get-GridSetting -Names @('OpenIddict__Applications__Grid_App__ClientId') -Default 'Grid_App'
$values['OpenIddict__Applications__Grid_App__RootUrl'] = $webUrl
$values['OpenIddict__Applications__Grid_Swagger__ClientId'] = Get-GridSetting -Names @('OpenIddict__Applications__Grid_Swagger__ClientId') -Default 'Grid_Swagger'
$values['OpenIddict__Applications__Grid_Swagger__RootUrl'] = $apiUrl
$previous = @{}
Push-Location (Join-Path $script:GridRoot 'backend/Grid/src/Grid.DbMigrator')
try {
    foreach ($key in $values.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $values[$key], 'Process')
    }
    Write-Host 'Running database migrations and seeders using the repository .env.'
    Invoke-GridCommand 'dotnet' @('run', '-c', 'Release')
    Write-Host 'Database migrations and seeders completed.'
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    Pop-Location
}
