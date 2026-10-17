param (
    [Parameter(Mandatory = $false)]
    [string]$ConnectionString,
    [Parameter(Mandatory = $false)]
    [string]$WebAppUrl = "https://grid.web.app",
    [Parameter(Mandatory = $false)]
    [string]$ApiUrl = "https://grid-api.a.run.app"
)

Write-Host "=== Grid Supabase Database Migration Runner ===" -ForegroundColor Cyan

if (-not $ConnectionString) {
    $ConnectionString = Read-Host "Enter Supabase PostgreSQL Connection String (e.g. Host=db.[REF].supabase.co;Port=5432;Database=postgres;Username=postgres;Password=[PWD];SSL Mode=Require;Trust Server Certificate=true)"
}

if (-not $ConnectionString) {
    Write-Error "Connection string is required to run migrations."
    exit 1
}

Write-Host "Target Connection: $ConnectionString" -ForegroundColor Yellow
Write-Host "Web Root URL: $WebAppUrl" -ForegroundColor Yellow
Write-Host "API Root URL: $ApiUrl" -ForegroundColor Yellow

$env:ConnectionStrings__Default = $ConnectionString
$env:OpenIddict__Applications__Grid_App__RootUrl = $WebAppUrl
$env:OpenIddict__Applications__Grid_Swagger__RootUrl = $ApiUrl

Write-Host "`nRunning Grid.DbMigrator against Supabase..." -ForegroundColor Cyan
dotnet run --project backend/Grid/src/Grid.DbMigrator/Grid.DbMigrator.csproj -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDatabase migrations & initial seeders completed successfully!" -ForegroundColor Green
} else {
    Write-Error "`nMigration failed. Review log output above."
    exit $LASTEXITCODE
}
