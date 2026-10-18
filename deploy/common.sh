#!/usr/bin/env bash
# Shared .env parser. Do not source .env: unquoted connection strings, dollar
# signs, commas and semicolons must remain literal values, not executable shell.
# Requires Bash 4+ (Google Cloud Shell and current Git Bash are supported).
GRID_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
declare -A GRID_DEPLOY_ENV=()

grid_fail() { printf '%s\n' "$*" >&2; return 1; }
grid_trim() {
    local value=$1
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    printf '%s' "$value"
}
grid_load_env() {
    local line key value number=0
    [[ -f "$GRID_ROOT/.env" ]] || { grid_fail "Create $GRID_ROOT/.env and configure deployment values first."; return 1; }
    GRID_DEPLOY_ENV=()
    while IFS= read -r line || [[ -n "$line" ]]; do
        number=$((number + 1))
        line=${line#$'\xef\xbb\xbf'}
        line=$(grid_trim "$line")
        [[ -n "$line" && "$line" != \#* ]] || continue
        if [[ "$line" =~ ^(export[[:space:]]+)?([a-zA-Z_][a-zA-Z0-9_]*)[[:space:]]*=(.*)$ ]]; then
            key=${BASH_REMATCH[2]}
            value=$(grid_trim "${BASH_REMATCH[3]}")
        else
            grid_fail "Invalid .env entry at line $number. Use KEY=value."; return 1
        fi
        if [[ "${value:0:1}" == \" || "${value:0:1}" == "'" ]]; then
            [[ ${#value} -ge 2 && "${value: -1}" == "${value:0:1}" ]] || { grid_fail "Unclosed quote for $key in .env."; return 1; }
            value=${value:1:${#value}-2}
        fi
        if [[ -n "${GRID_DEPLOY_ENV[$key]+present}" && "${GRID_DEPLOY_ENV[$key]}" != "$value" ]]; then
            grid_fail "Conflicting duplicate entry for $key in .env."; return 1
        fi
        GRID_DEPLOY_ENV[$key]=$value
    done < "$GRID_ROOT/.env"
}
grid_get() {
    local fallback=$1 key found='' value
    shift
    for key in "$@"; do
        value=${GRID_DEPLOY_ENV[$key]-}
        if [[ -n "$value" ]]; then
            [[ -z "$found" || "$found" == "$value" ]] || { grid_fail "Conflicting .env aliases: $*. Use the same value."; return 1; }
            found=$value
        fi
    done
    printf '%s' "${found:-$fallback}"
}
grid_require() {
    local value
    value=$(grid_get '' "$@") || return 1
    [[ -n "$value" ]] || { grid_fail "Set $* in the repository .env."; return 1; }
    printf '%s' "$value"
}
grid_assert_production() {
    local mode key
    mode=$(grid_require GRID_ENV ASPNETCORE_ENVIRONMENT) || return 1
    [[ "${mode,,}" == production ]] || { grid_fail 'Cloud deployment requires GRID_ENV=Production in .env.'; return 1; }
    for key in PUBSUB_EMULATOR_HOST STORAGE_EMULATOR_HOST; do
        [[ -z "$(grid_get '' "$key")" ]] || { grid_fail "Remove or empty $key in .env before cloud deployment."; return 1; }
    done
}
grid_cloud_config() {
    GRID_PROJECT=$(grid_require GoogleCloud__ProjectId GOOGLE_CLOUD_PROJECT) || return 1
    GRID_REGION=$(grid_get us-central1 GCP_REGION) || return 1
    GRID_BUCKET=$(grid_require GoogleCloud__Bucket GCS_BUCKET) || return 1
    GRID_REPOSITORY=$(grid_get grid-repo ARTIFACT_REGISTRY_REPOSITORY) || return 1
    GRID_API_SERVICE=$(grid_get grid-api CLOUD_RUN_API_SERVICE) || return 1
    GRID_WORKER_SERVICE=$(grid_get grid-worker CLOUD_RUN_WORKER_SERVICE) || return 1
    GRID_API_ACCOUNT=$(grid_get "grid-api-sa@$GRID_PROJECT.iam.gserviceaccount.com" GRID_API_SERVICE_ACCOUNT) || return 1
    GRID_WORKER_ACCOUNT=$(grid_get "grid-worker-sa@$GRID_PROJECT.iam.gserviceaccount.com" GRID_WORKER_SERVICE_ACCOUNT) || return 1
    GRID_PARSER_TOPIC=$(grid_get grid-parser-runs GoogleCloud__ParserTopicId PUBSUB_PARSER_TOPIC) || return 1
    GRID_DATASET_TOPIC=$(grid_get grid-datasets GoogleCloud__DatasetTopicId) || return 1
    GRID_SUBSCRIPTION=$(grid_get grid-parser-runs-etl PUBSUB_PARSER_SUBSCRIPTION) || return 1
    GRID_FIREBASE_PROJECT=$(grid_get "$GRID_PROJECT" FIREBASE_PROJECT_ID) || return 1
}
grid_tool() { command -v "$1" >/dev/null 2>&1 || grid_fail "Install $1 and sign in where required before deployment."; }
grid_json_string() {
    local value=$1
    value=${value//\\/\\\\}
    value=${value//\"/\\\"}
    value=${value//$'\r'/\\r}
    value=${value//$'\n'/\\n}
    value=${value//$'\t'/\\t}
    printf '"%s"' "$value"
}
grid_env_file() {
    local file separator='' key value
    [[ $(($# % 2)) -eq 0 ]] || { grid_fail 'Environment file needs key/value pairs.'; return 1; }
    file=$(umask 077; mktemp "${TMPDIR:-/tmp}/grid-deploy-env.XXXXXX") || return 1
    if ! {
        printf '{'
        while [[ $# -gt 0 ]]; do
            key=$1; value=$2; shift 2
            printf '%s' "$separator"
            grid_json_string "$key"
            printf ':'
            grid_json_string "$value"
            separator=','
        done
        printf '}\n'
    } > "$file"; then
        rm -f -- "$file"
        return 1
    fi
    printf '%s\n' "$file"
}
grid_api_url() {
    local url
    url=$(grid_get '' App__SelfUrl VITE_API_URL BACKEND_API_URL GRID_API_URL) || return 1
    if [[ -z "$url" ]]; then
        url=$(gcloud run services describe "$GRID_API_SERVICE" --project="$GRID_PROJECT" --region="$GRID_REGION" --platform=managed '--format=value(status.url)') || return 1
    fi
    [[ "$url" == https://* ]] || { grid_fail 'Set App__SelfUrl or VITE_API_URL in .env, or deploy the API first for URL discovery.'; return 1; }
    printf '%s' "${url%/}"
}
grid_web_url() {
    local url origins
    url=$(grid_get '' OpenIddict__Applications__Grid_App__RootUrl) || return 1
    if [[ -z "$url" ]]; then
        origins=$(grid_get '' App__CorsOrigins) || return 1
        url=$(grid_trim "${origins%%,*}")
        if [[ "$url" != https://* && "$url" != http://* ]]; then url="https://$GRID_FIREBASE_PROJECT.web.app"; fi
    fi
    printf '%s' "${url%/}"
}
