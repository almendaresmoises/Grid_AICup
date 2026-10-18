#!/usr/bin/env bash
set -e

echo "=== Grid Cloud Run Deployment Runner (Bash / Cloud Shell) ==="

PROJECT_ID="${1:-$(gcloud config get-value project 2>/dev/null)}"
REGION="${2:-us-central1}"
SUPABASE_CONNECTION_STRING="${3}"
WORKER_SECRET="${4:-$(openssl rand -base64 32)}"
GEMINI_API_KEY="${5}"
FIREBASE_DOMAIN="${6:-*}"

if [ -z "$PROJECT_ID" ]; then
    read -p "Enter your Google Cloud Project ID: " PROJECT_ID
fi

REPO_PREFIX="${REGION}-docker.pkg.dev/${PROJECT_ID}/grid-repo"
BUCKET_NAME="${PROJECT_ID}-grid-storage"
API_IMAGE="${REPO_PREFIX}/grid-api:latest"
WORKER_IMAGE="${REPO_PREFIX}/grid-worker:latest"
API_SA="grid-api-sa@${PROJECT_ID}.iam.gserviceaccount.com"
WORKER_SA="grid-worker-sa@${PROJECT_ID}.iam.gserviceaccount.com"

echo "Configuring Docker authentication..."
gcloud auth configure-docker "${REGION}-docker.pkg.dev" --quiet

# --- 1. BUILD & PUSH API ---
echo -e "\n[1/4] Building Grid API container..."
docker build -f deploy/api.Dockerfile -t "$API_IMAGE" .
echo "Pushing API container..."
docker push "$API_IMAGE"

# --- 2. DEPLOY API TO CLOUD RUN ---
echo -e "\n[2/4] Deploying Grid API to Cloud Run..."
ENV_VARS="ASPNETCORE_ENVIRONMENT=Production,ASPNETCORE_HTTP_PORTS=8080,ASPNETCORE_FORWARDEDHEADERS_ENABLED=true,GoogleCloud__ProjectId=${PROJECT_ID},GoogleCloud__Bucket=${BUCKET_NAME},GoogleCloud__ParserTopicId=grid-parser-runs,GoogleCloud__DatasetTopicId=grid-datasets,GoogleCloud__WorkerSecret=${WORKER_SECRET},App__CorsOrigins=${FIREBASE_DOMAIN},AuthServer__RequireHttpsMetadata=true,AuthServer__CertificatePassPhrase=145683a4-f6fa-4ed2-833e-44b8bc9736d1"

if [ -n "$SUPABASE_CONNECTION_STRING" ]; then
    ENV_VARS="${ENV_VARS},ConnectionStrings__Default=${SUPABASE_CONNECTION_STRING}"
fi
if [ -n "$GEMINI_API_KEY" ]; then
    ENV_VARS="${ENV_VARS},Gemini__ApiKey=${GEMINI_API_KEY}"
fi

gcloud run deploy grid-api \
    --image="$API_IMAGE" \
    --platform=managed \
    --region="$REGION" \
    --port=8080 \
    --allow-unauthenticated \
    --service-account="$API_SA" \
    --memory=1Gi \
    --cpu=1 \
    --set-env-vars="$ENV_VARS"

API_URL=$(gcloud run services describe grid-api --platform=managed --region="$REGION" --format="value(status.url)")
echo "Grid API deployed at: $API_URL"

gcloud run services update grid-api \
    --platform=managed \
    --region="$REGION" \
    --update-env-vars="AuthServer__Authority=${API_URL},App__SelfUrl=${API_URL}"

# --- 3. BUILD & PUSH WORKER ---
echo -e "\n[3/4] Building Grid ETL Worker container..."
docker build -f deploy/worker.Dockerfile -t "$WORKER_IMAGE" .
docker push "$WORKER_IMAGE"

# --- 4. DEPLOY WORKER TO CLOUD RUN ---
echo -e "\n[4/4] Deploying Grid Worker to Cloud Run..."
WORKER_ENV_VARS="GOOGLE_CLOUD_PROJECT=${PROJECT_ID},GCS_BUCKET=${BUCKET_NAME},PUBSUB_PARSER_TOPIC=grid-parser-runs,PUBSUB_PARSER_SUBSCRIPTION=grid-parser-runs-etl,BACKEND_API_URL=${API_URL},GRID_API_URL=${API_URL},GRID_WORKER_SECRET=${WORKER_SECRET},PUBSUB_MODE=auto"

gcloud run deploy grid-worker \
    --image="$WORKER_IMAGE" \
    --platform=managed \
    --region="$REGION" \
    --port=8080 \
    --no-allow-unauthenticated \
    --service-account="$WORKER_SA" \
    --memory=2Gi \
    --cpu=2 \
    --no-cpu-throttling \
    --min-instances=1 \
    --timeout=600 \
    --set-env-vars="$WORKER_ENV_VARS"

echo -e "\nAll Cloud Run services deployed successfully!"
echo "API URL: $API_URL"
