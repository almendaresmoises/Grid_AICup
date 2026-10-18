#!/usr/bin/env bash
# Run: bash deploy/deploy-cloudrun.sh. All values come from the root .env.
set -euo pipefail
[[ $# -eq 0 ]] || { printf '%s\n' 'Configure the repository .env instead of passing deployment arguments.' >&2; exit 1; }
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
grid_load_env
grid_assert_production
grid_cloud_config
CONNECTION=$(grid_require ConnectionStrings__Default)
WORKER_SECRET=$(grid_require GoogleCloud__WorkerSecret GRID_WORKER_SECRET)
CERTIFICATE=$(grid_require AuthServer__CertificatePassPhrase)
GEMINI_KEY=$(grid_get '' Gemini__ApiKey GEMINI_API_KEY)
MODEL=$(grid_get '' Gemini__Model GEMINI_MODEL)
WEB_URL=$(grid_web_url)
CORS=$(grid_get "$WEB_URL" App__CorsOrigins)
TAG=$(grid_get latest GRID_IMAGE_TAG)
REGISTRY="$GRID_REGION-docker.pkg.dev"
PREFIX="$REGISTRY/$GRID_PROJECT/$GRID_REPOSITORY"
API_IMAGE="$PREFIX/grid-api:$TAG"
WORKER_IMAGE="$PREFIX/grid-worker:$TAG"
API_MEMORY=$(grid_get 1Gi GRID_API_MEMORY)
API_CPU=$(grid_get 1 GRID_API_CPU)
WORKER_MEMORY=$(grid_get 2Gi GRID_WORKER_MEMORY)
WORKER_CPU=$(grid_get 2 GRID_WORKER_CPU)
WORKER_MIN=$(grid_get 1 GRID_WORKER_MIN_INSTANCES)
WORKER_TIMEOUT=$(grid_get 600 GRID_WORKER_TIMEOUT)
CONFIGURED_URL=$(grid_get '' App__SelfUrl VITE_API_URL BACKEND_API_URL GRID_API_URL)
AUTHORITY=$(grid_get '' AuthServer__Authority)
APP_CLIENT=$(grid_get Grid_App OpenIddict__Applications__Grid_App__ClientId)
SWAGGER_CLIENT=$(grid_get Grid_Swagger OpenIddict__Applications__Grid_Swagger__ClientId)
PUBSUB_MODE_VALUE=$(grid_get auto PUBSUB_MODE)
SWAGGER_URL=$(grid_get '' OpenIddict__Applications__Grid_Swagger__RootUrl)
grid_tool gcloud
grid_tool docker
API_ENV_FILE=''
WORKER_ENV_FILE=''
cleanup() {
    [[ -z "$API_ENV_FILE" ]] || rm -f -- "$API_ENV_FILE"
    [[ -z "$WORKER_ENV_FILE" ]] || rm -f -- "$WORKER_ENV_FILE"
}
trap cleanup EXIT
cd -- "$GRID_ROOT"
printf 'Deploying Cloud Run services in %s / %s.\n' "$GRID_PROJECT" "$GRID_REGION"
gcloud auth configure-docker "$REGISTRY" --quiet
docker build -f deploy/api.Dockerfile -t "$API_IMAGE" .
docker push "$API_IMAGE"
API_BASE=(
    ASPNETCORE_ENVIRONMENT Production GRID_ENV Production
    ASPNETCORE_HTTP_PORTS 8080 ASPNETCORE_FORWARDEDHEADERS_ENABLED true
    GoogleCloud__ProjectId "$GRID_PROJECT" GoogleCloud__Bucket "$GRID_BUCKET"
    GoogleCloud__ParserTopicId "$GRID_PARSER_TOPIC" GoogleCloud__DatasetTopicId "$GRID_DATASET_TOPIC"
    GoogleCloud__WorkerSecret "$WORKER_SECRET" ConnectionStrings__Default "$CONNECTION"
    App__CorsOrigins "$CORS" AuthServer__RequireHttpsMetadata true AuthServer__CertificatePassPhrase "$CERTIFICATE"
    OpenIddict__Applications__Grid_App__ClientId "$APP_CLIENT"
    OpenIddict__Applications__Grid_App__RootUrl "$WEB_URL"
    OpenIddict__Applications__Grid_Swagger__ClientId "$SWAGGER_CLIENT"
)
for key in "${!GRID_DEPLOY_ENV[@]}"; do
    case "$key" in
        App__*|AuthServer__*|OpenIddict__*|AiOptimization__*)
            case "$key" in
                App__CorsOrigins|App__SelfUrl|AuthServer__Authority|AuthServer__RequireHttpsMetadata|AuthServer__CertificatePassPhrase|OpenIddict__Applications__Grid_App__ClientId|OpenIddict__Applications__Grid_App__RootUrl|OpenIddict__Applications__Grid_Swagger__ClientId|OpenIddict__Applications__Grid_Swagger__RootUrl) continue ;;
            esac
            if [[ -n "${GRID_DEPLOY_ENV[$key]}" ]]; then API_BASE+=("$key" "${GRID_DEPLOY_ENV[$key]}"); fi
            ;;
    esac
done
if [[ -n "$GEMINI_KEY" ]]; then API_BASE+=(GEMINI_API_KEY "$GEMINI_KEY" Gemini__ApiKey "$GEMINI_KEY"); fi
if [[ -n "$MODEL" ]]; then API_BASE+=(GEMINI_MODEL "$MODEL" Gemini__Model "$MODEL"); fi
INITIAL=("${API_BASE[@]}")
if [[ -n "$CONFIGURED_URL" ]]; then INITIAL+=(App__SelfUrl "$CONFIGURED_URL"); fi
if [[ -n "${AUTHORITY:-$CONFIGURED_URL}" ]]; then INITIAL+=(AuthServer__Authority "${AUTHORITY:-$CONFIGURED_URL}"); fi
API_ENV_FILE=$(grid_env_file "${INITIAL[@]}")
gcloud run deploy "$GRID_API_SERVICE" --project="$GRID_PROJECT" --image="$API_IMAGE" \
    --platform=managed --region="$GRID_REGION" --port=8080 --allow-unauthenticated \
    --service-account="$GRID_API_ACCOUNT" --memory="$API_MEMORY" --cpu="$API_CPU" \
    --env-vars-file="$API_ENV_FILE" --quiet
API_URL=$(grid_api_url)
rm -f -- "$API_ENV_FILE"
API_ENV_FILE=$(grid_env_file "${API_BASE[@]}" App__SelfUrl "$API_URL" \
    AuthServer__Authority "${AUTHORITY:-$API_URL}" OpenIddict__Applications__Grid_Swagger__RootUrl "${SWAGGER_URL:-$API_URL}")
gcloud run services update "$GRID_API_SERVICE" --project="$GRID_PROJECT" --platform=managed \
    --region="$GRID_REGION" --env-vars-file="$API_ENV_FILE" --quiet
docker build -f deploy/worker.Dockerfile -t "$WORKER_IMAGE" .
docker push "$WORKER_IMAGE"
WORKER_ENV_FILE=$(grid_env_file GRID_ENV Production GOOGLE_CLOUD_PROJECT "$GRID_PROJECT" GCS_BUCKET "$GRID_BUCKET" \
    PUBSUB_PARSER_TOPIC "$GRID_PARSER_TOPIC" PUBSUB_PARSER_SUBSCRIPTION "$GRID_SUBSCRIPTION" \
    BACKEND_API_URL "$API_URL" GRID_API_URL "$API_URL" GRID_WORKER_SECRET "$WORKER_SECRET" PUBSUB_MODE "$PUBSUB_MODE_VALUE")
gcloud run deploy "$GRID_WORKER_SERVICE" --project="$GRID_PROJECT" --image="$WORKER_IMAGE" \
    --platform=managed --region="$GRID_REGION" --port=8080 --no-allow-unauthenticated \
    --service-account="$GRID_WORKER_ACCOUNT" --memory="$WORKER_MEMORY" --cpu="$WORKER_CPU" \
    --no-cpu-throttling --min-instances="$WORKER_MIN" --timeout="$WORKER_TIMEOUT" \
    --env-vars-file="$WORKER_ENV_FILE" --quiet
printf 'Cloud Run deployment completed. API URL: %s\n' "$API_URL"
