param (
    [Parameter(Mandatory = $false)]
    [string]$ProjectId,
    [Parameter(Mandatory = $false)]
    [string]$Region = "us-central1",
    [Parameter(Mandatory = $false)]
    [string]$SupabaseConnectionString,
    [Parameter(Mandatory = $false)]
    [string]$WorkerSecret,
    [Parameter(Mandatory = $false)]
    [string]$GeminiApiKey,
    [Parameter(Mandatory = $false)]
    [string]$FirebaseDomain = "*"
)

Write-Host "=== Grid Cloud Run Deployment Runner ===" -ForegroundColor Cyan

if (-not $ProjectId) {
    $ProjectId = gcloud config get-value project 2>$null
    if (-not $ProjectId) {
        $ProjectId = Read-Host "Enter your Google Cloud Project ID"
    }
}

if (-not $WorkerSecret) {
    $WorkerSecret = [System.Guid]::NewGuid().ToString("N") + [System.Guid]::NewGuid().ToString("N")
    Write-Host "Generated random GRID_WORKER_SECRET: $WorkerSecret" -ForegroundColor Yellow
}

$RepoPrefix = "$Region-docker.pkg.dev/$ProjectId/grid-repo"
$BucketName = "$ProjectId-grid-storage"
$ApiImage = "$RepoPrefix/grid-api:latest"
$WorkerImage = "$RepoPrefix/grid-worker:latest"
$ApiSa = "grid-api-sa@$ProjectId.iam.gserviceaccount.com"
$WorkerSa = "grid-worker-sa@$ProjectId.iam.gserviceaccount.com"

# Authenticate Docker to Artifact Registry
Write-Host "`nConfiguring Docker authentication..." -ForegroundColor Cyan
gcloud auth configure-docker "$Region-docker.pkg.dev" --quiet

# --- 1. BUILD & PUSH API ---
Write-Host "`n[1/4] Building Grid API container..." -ForegroundColor Cyan
docker build -f deploy/api.Dockerfile -t $ApiImage .
if ($LASTEXITCODE -ne 0) { Write-Error "API build failed"; exit $LASTEXITCODE }

Write-Host "Pushing API container to Artifact Registry..." -ForegroundColor Cyan
docker push $ApiImage
if ($LASTEXITCODE -ne 0) { Write-Error "API push failed"; exit $LASTEXITCODE }

# --- 2. DEPLOY API TO CLOUD RUN ---
Write-Host "`n[2/4] Deploying Grid API to Cloud Run..." -ForegroundColor Cyan
$ApiEnvVars = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "ASPNETCORE_HTTP_PORTS=8080",
    "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true",
    "GoogleCloud__ProjectId=$ProjectId",
    "GoogleCloud__Bucket=$BucketName",
    "GoogleCloud__ParserTopicId=grid-parser-runs",
    "GoogleCloud__DatasetTopicId=grid-datasets",
    "GoogleCloud__WorkerSecret=$WorkerSecret",
    "App__CorsOrigins=$FirebaseDomain",
    "AuthServer__RequireHttpsMetadata=true",
    "AuthServer__CertificatePassPhrase=145683a4-f6fa-4ed2-833e-44b8bc9736d1"
)
if ($SupabaseConnectionString) {
    $ApiEnvVars += "ConnectionStrings__Default=$SupabaseConnectionString"
}
if ($GeminiApiKey) {
    $ApiEnvVars += "Gemini__ApiKey=$GeminiApiKey"
}

$ApiEnvString = $ApiEnvVars -join ","

gcloud run deploy grid-api `
    --image=$ApiImage `
    --platform=managed `
    --region=$Region `
    --port=8080 `
    --allow-unauthenticated `
    --service-account=$ApiSa `
    --memory=1Gi `
    --cpu=1 `
    --set-env-vars=$ApiEnvString

$ApiUrl = gcloud run services describe grid-api --platform=managed --region=$Region --format="value(status.url)"
Write-Host "Grid API deployed at: $ApiUrl" -ForegroundColor Green

# Update AuthServer:Authority to match Cloud Run URL
Write-Host "Updating API Authority & SelfUrl..." -ForegroundColor Cyan
gcloud run services update grid-api `
    --platform=managed `
    --region=$Region `
    --update-env-vars="AuthServer__Authority=$ApiUrl,App__SelfUrl=$ApiUrl"

# --- 3. BUILD & PUSH WORKER ---
Write-Host "`n[3/4] Building Grid ETL Worker container..." -ForegroundColor Cyan
docker build -f deploy/worker.Dockerfile -t $WorkerImage .
if ($LASTEXITCODE -ne 0) { Write-Error "Worker build failed"; exit $LASTEXITCODE }

Write-Host "Pushing Worker container to Artifact Registry..." -ForegroundColor Cyan
docker push $WorkerImage
if ($LASTEXITCODE -ne 0) { Write-Error "Worker push failed"; exit $LASTEXITCODE }

# --- 4. DEPLOY WORKER TO CLOUD RUN ---
Write-Host "`n[4/4] Deploying Grid Worker to Cloud Run..." -ForegroundColor Cyan
$WorkerEnvVars = @(
    "GOOGLE_CLOUD_PROJECT=$ProjectId",
    "GCS_BUCKET=$BucketName",
    "PUBSUB_PARSER_TOPIC=grid-parser-runs",
    "PUBSUB_PARSER_SUBSCRIPTION=grid-parser-runs-etl",
    "BACKEND_API_URL=$ApiUrl",
    "GRID_API_URL=$ApiUrl",
    "GRID_WORKER_SECRET=$WorkerSecret",
    "PUBSUB_MODE=auto"
) -join ","

gcloud run deploy grid-worker `
    --image=$WorkerImage `
    --platform=managed `
    --region=$Region `
    --port=8080 `
    --no-allow-unauthenticated `
    --service-account=$WorkerSa `
    --memory=2Gi `
    --cpu=2 `
    --no-cpu-throttling `
    --min-instances=1 `
    --timeout=600 `
    --set-env-vars=$WorkerEnvVars

Write-Host "`nAll Cloud Run services deployed successfully!" -ForegroundColor Green
Write-Host "API URL: $ApiUrl"
Write-Host "Use this URL as VITE_API_URL when deploying the frontend to Firebase."
