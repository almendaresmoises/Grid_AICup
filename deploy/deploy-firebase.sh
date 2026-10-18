#!/usr/bin/env bash
# Run: bash deploy/deploy-firebase.sh. All values come from the root .env.
set -euo pipefail
[[ $# -eq 0 ]] || { printf '%s\n' 'Configure the repository .env instead of passing deployment arguments.' >&2; exit 1; }
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
grid_load_env
grid_assert_production
grid_cloud_config
API_URL=$(grid_api_url)
grid_tool npm
grid_tool firebase
cd -- "$GRID_ROOT"
printf 'Building frontend for %s. API URL: %s\n' "$GRID_FIREBASE_PROJECT" "$API_URL"
(
    for key in "${!GRID_DEPLOY_ENV[@]}"; do
        if [[ "$key" == VITE_* ]]; then
            export "$key"="${GRID_DEPLOY_ENV[$key]}"
        fi
    done
    export VITE_API_URL="$API_URL"
    npm --prefix apps/web run build
    firebase deploy --only hosting --project "$GRID_FIREBASE_PROJECT" --non-interactive
)
printf 'Firebase Hosting deployment completed.\n'
