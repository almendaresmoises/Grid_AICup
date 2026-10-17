#!/usr/bin/env bash
set -e

echo "=== Grid GCP Infrastructure Setup (Bash / Cloud Shell) ==="

PROJECT_ID="${1:-$(gcloud config get-value project 2>/dev/null)}"
REGION="${2:-asia-southeast1}"

if [ -z "$PROJECT_ID" ]; then
    read -p "Enter your Google Cloud Project ID: " PROJECT_ID
fi

BUCKET_NAME="${PROJECT_ID}-grid-storage"

echo "Configuring Project: ${PROJECT_ID} (Region: ${REGION})"
echo "Storage Bucket: gs://${BUCKET_NAME}"

gcloud config set project "$PROJECT_ID"

echo -e "\n[1/5] Enabling GCP APIs..."
gcloud services enable \
    run.googleapis.com \
    pubsub.googleapis.com \
    storage.googleapis.com \
    artifactregistry.googleapis.com \
    secretmanager.googleapis.com

echo -e "\n[2/5] Creating Artifact Registry repository..."
gcloud artifacts repositories create grid-repo \
    --repository-format=docker \
    --location="$REGION" \
    --description="Docker repository for Grid services" || true

echo -e "\n[3/5] Creating Cloud Storage Bucket..."
gsutil mb -p "$PROJECT_ID" -c standard -l "$REGION" "gs://${BUCKET_NAME}/" || true

echo -e "\n[4/5] Setting up Pub/Sub Topics & Subscription..."
gcloud pubsub topics create grid-parser-runs || true
gcloud pubsub topics create grid-datasets || true
gcloud pubsub subscriptions create grid-parser-runs-etl \
    --topic=grid-parser-runs \
    --ack-deadline=600 || true

echo -e "\n[5/5] Creating Service Accounts..."
gcloud iam service-accounts create grid-api-sa --display-name="Grid API Service Account" || true
gcloud iam service-accounts create grid-worker-sa --display-name="Grid Worker Service Account" || true

API_SA="grid-api-sa@${PROJECT_ID}.iam.gserviceaccount.com"
WORKER_SA="grid-worker-sa@${PROJECT_ID}.iam.gserviceaccount.com"

gsutil iam ch "serviceAccount:${API_SA}:roles/storage.objectAdmin" "gs://${BUCKET_NAME}"
gsutil iam ch "serviceAccount:${WORKER_SA}:roles/storage.objectAdmin" "gs://${BUCKET_NAME}"

gcloud pubsub topics add-iam-policy-binding grid-parser-runs --member="serviceAccount:${API_SA}" --role="roles/pubsub.publisher"
gcloud pubsub topics add-iam-policy-binding grid-datasets --member="serviceAccount:${API_SA}" --role="roles/pubsub.publisher"
gcloud pubsub subscriptions add-iam-policy-binding grid-parser-runs-etl --member="serviceAccount:${WORKER_SA}" --role="roles/pubsub.subscriber"

echo -e "\nGCP Infrastructure configured successfully!"
echo "API SA: $API_SA"
echo "Worker SA: $WORKER_SA"
