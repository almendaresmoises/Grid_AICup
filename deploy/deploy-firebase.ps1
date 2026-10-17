param (
    [Parameter(Mandatory = $false)]
    [string]$ApiUrl,
    [Parameter(Mandatory = $false)]
    [string]$FirebaseProjectId
)

Write-Host "=== Grid Firebase Web Deployment Runner ===" -ForegroundColor Cyan

if (-not $ApiUrl) {
    $ApiUrl = Read-Host "Enter the deployed Cloud Run API URL (e.g. https://grid-api-xyz.a.run.app)"
}

if (-not $ApiUrl) {
    Write-Error "API URL is required for frontend production build."
    exit 1
}

Write-Host "Target API URL: $ApiUrl" -ForegroundColor Green

$env:VITE_API_URL = $ApiUrl

Write-Host "`n[1/2] Building Vite React Web App for Production..." -ForegroundColor Cyan
npm --prefix apps/web run build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Frontend build failed."
    exit $LASTEXITCODE
}

Write-Host "`n[2/2] Deploying to Firebase Hosting..." -ForegroundColor Cyan
if ($FirebaseProjectId) {
    firebase deploy --only hosting --project $FirebaseProjectId
} else {
    firebase deploy --only hosting
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nFrontend successfully deployed to Firebase Hosting!" -ForegroundColor Green
} else {
    Write-Error "`nFirebase deployment failed."
    exit $LASTEXITCODE
}
