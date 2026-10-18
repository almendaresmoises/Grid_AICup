#!/usr/bin/env bash
# Run: bash deploy/run-migrations.sh. All values come from the root .env.
set -euo pipefail
[[ $# -eq 0 ]] || { printf '%s\n' 'Configure the repository .env instead of passing migration arguments.' >&2; exit 1; }
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/common.sh"
grid_load_env
CONNECTION=$(grid_require ConnectionStrings__Default)
grid_cloud_config
API_URL=$(grid_get '' OpenIddict__Applications__Grid_Swagger__RootUrl)
if [[ -z "$API_URL" ]]; then
    API_URL=$(grid_api_url)
fi
WEB_URL=$(grid_web_url)
APP_CLIENT=$(grid_get Grid_App OpenIddict__Applications__Grid_App__ClientId)
SWAGGER_CLIENT=$(grid_get Grid_Swagger OpenIddict__Applications__Grid_Swagger__ClientId)
grid_tool dotnet
cd -- "$GRID_ROOT/backend/Grid/src/Grid.DbMigrator"
printf 'Running database migrations and seeders using the repository .env.\n'
(
    for key in "${!GRID_DEPLOY_ENV[@]}"; do
        export "$key"="${GRID_DEPLOY_ENV[$key]}"
    done
    export ConnectionStrings__Default="$CONNECTION"
    export OpenIddict__Applications__Grid_App__ClientId="$APP_CLIENT"
    export OpenIddict__Applications__Grid_App__RootUrl="$WEB_URL"
    export OpenIddict__Applications__Grid_Swagger__ClientId="$SWAGGER_CLIENT"
    export OpenIddict__Applications__Grid_Swagger__RootUrl="$API_URL"
    dotnet run -c Release
)
printf 'Database migrations and seeders completed.\n'
