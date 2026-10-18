param (
    [Parameter(Mandatory = $false)]
    [string]$ProjectId,
    [Parameter(Mandatory = $false)]
    [string]$Region = "us-central1",
    [Parameter(Mandatory = $false)]
    [string]$BucketName
)

Write-Host "=== Grid GCP Infrastructure Setup ===" -ForegroundColor Cyan

if (-not $ProjectId) {
    $ProjectId = gcloud config get-value project 2>$null
    if (-not $ProjectId) {
        $ProjectId = Read-Host "Enter your Google Cloud Project ID"
    }
}

if (-not $ProjectId) {
    Write-Error "Google Cloud Project ID is required."
    exit 1
}

if (-not $BucketName) {
    $BucketName = "$ProjectId-grid-storage"
}

Write-Host "Configuring Project: $ProjectId (Region: $Region)" -ForegroundColor Green
Write-Host "Storage Bucket: gs://$BucketName" -ForegroundColor Green

gcloud config set project $ProjectId

Write-Host "`n[1/5] Enabling GCP APIs..." -ForegroundColor Yellow
gcloud services enable `
    run.googleapis.com `
    pubsub.googleapis.com `
    storage.googleapis.com `
    artifactregistry.googleapis.com `
    secretmanager.googleapis.com

Write-Host "`n[2/5] Creating Artifact Registry repository..." -ForegroundColor Yellow
gcloud artifacts repositories create grid-repo `
    --repository-format=docker `
    --location=$Region `
    --description="Docker repository for Grid services" 2>$null

Write-Host "`n[3/5] Creating Cloud Storage Bucket..." -ForegroundColor Yellow
gsutil mb -p $ProjectId -c standard -l $Region "gs://$BucketName/" 2>$null

Write-Host "`n[4/5] Setting up Pub/Sub Topics & Subscription..." -ForegroundColor Yellow
gcloud pubsub topics create grid-parser-runs 2>$null
gcloud pubsub topics create grid-datasets 2>$null
gcloud pubsub subscriptions create grid-parser-runs-etl `
    --topic=grid-parser-runs `
    --ack-deadline=600 2>$null

Write-Host "`n[5/5] Creating Service Accounts..." -ForegroundColor Yellow
gcloud iam service-accounts create grid-api-sa --display-name="Grid API Service Account" 2>$null
gcloud iam service-accounts create grid-worker-sa --display-name="Grid Worker Service Account" 2>$null

$ApiSa = "grid-api-sa@$ProjectId.iam.gserviceaccount.com"
$WorkerSa = "grid-worker-sa@$ProjectId.iam.gserviceaccount.com"

# Bucket permissions
gsutil iam ch "serviceAccount:$ApiSa:roles/storage.objectAdmin" "gs://$BucketName"
gsutil iam ch "serviceAccount:$WorkerSa:roles/storage.objectAdmin" "gs://$BucketName"

# Pub/Sub permissions
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ApiSa" --role="roles/pubsub.editor"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$WorkerSa" --role="roles/pubsub.editor"
gcloud pubsub topics add-iam-policy-binding grid-parser-runs --member="serviceAccount:$ApiSa" --role="roles/pubsub.publisher"
gcloud pubsub topics add-iam-policy-binding grid-datasets --member="serviceAccount:$ApiSa" --role="roles/pubsub.publisher"
gcloud pubsub subscriptions add-iam-policy-binding grid-parser-runs-etl --member="serviceAccount:$WorkerSa" --role="roles/pubsub.subscriber"

Write-Host "`nGCP Infrastructure configured successfully!" -ForegroundColor Green
Write-Host "Service Accounts:"
Write-Host " - API SA: $ApiSa"
Write-Host " - Worker SA: $WorkerSa"
