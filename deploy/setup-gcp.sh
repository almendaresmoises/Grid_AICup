#!/usr/bin/env bash
# Run: bash deploy/setup-gcp.sh. All values come from the root .env.
set -euo pipefail
[[ $# -eq 0 ]] || { printf '%s\n' 'Configure the repository .env instead of passing deployment arguments.' >&2; exit 1; }
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
grid_load_env
grid_assert_production
grid_cloud_config
grid_tool gcloud
grid_tool gsutil
cd -- "$GRID_ROOT"
printf 'Configuring infrastructure in %s / %s.\n' "$GRID_PROJECT" "$GRID_REGION"
gcloud services enable run.googleapis.com pubsub.googleapis.com storage.googleapis.com \
    artifactregistry.googleapis.com secretmanager.googleapis.com --project="$GRID_PROJECT" --quiet
if ! gcloud artifacts repositories describe "$GRID_REPOSITORY" --project="$GRID_PROJECT" --location="$GRID_REGION" >/dev/null 2>&1; then
    gcloud artifacts repositories create "$GRID_REPOSITORY" --repository-format=docker \
        --location="$GRID_REGION" --project="$GRID_PROJECT" '--description=Docker repository for Grid services' --quiet
fi
if ! gsutil ls -b "gs://$GRID_BUCKET" >/dev/null 2>&1; then
    gsutil mb -p "$GRID_PROJECT" -c standard -l "$GRID_REGION" "gs://$GRID_BUCKET/"
fi
for topic in "$GRID_PARSER_TOPIC" "$GRID_DATASET_TOPIC"; do
    if ! gcloud pubsub topics describe "$topic" --project="$GRID_PROJECT" >/dev/null 2>&1; then
        gcloud pubsub topics create "$topic" --project="$GRID_PROJECT" --quiet
    fi
done
if ! gcloud pubsub subscriptions describe "$GRID_SUBSCRIPTION" --project="$GRID_PROJECT" >/dev/null 2>&1; then
    gcloud pubsub subscriptions create "$GRID_SUBSCRIPTION" --topic="$GRID_PARSER_TOPIC" \
        --ack-deadline=600 --project="$GRID_PROJECT" --quiet
fi
for account in "$GRID_API_ACCOUNT" "$GRID_WORKER_ACCOUNT"; do
    if ! gcloud iam service-accounts describe "$account" --project="$GRID_PROJECT" >/dev/null 2>&1; then
        gcloud iam service-accounts create "${account%%@*}" --project="$GRID_PROJECT" --quiet
    fi
    gsutil iam ch "serviceAccount:$account:roles/storage.objectAdmin" "gs://$GRID_BUCKET"
    gcloud projects add-iam-policy-binding "$GRID_PROJECT" --member="serviceAccount:$account" \
        --role=roles/pubsub.editor --project="$GRID_PROJECT" --quiet
done
for topic in "$GRID_PARSER_TOPIC" "$GRID_DATASET_TOPIC"; do
    gcloud pubsub topics add-iam-policy-binding "$topic" --member="serviceAccount:$GRID_API_ACCOUNT" \
        --role=roles/pubsub.publisher --project="$GRID_PROJECT" --quiet
done
gcloud pubsub subscriptions add-iam-policy-binding "$GRID_SUBSCRIPTION" --member="serviceAccount:$GRID_WORKER_ACCOUNT" \
    --role=roles/pubsub.subscriber --project="$GRID_PROJECT" --quiet
printf '%s\n' 'GCP infrastructure setup completed.'
