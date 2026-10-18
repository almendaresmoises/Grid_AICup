# All deployment values come from the repository .env, regardless of the current
# directory or inherited shell values. Values are literal; matching quotes and
# export KEY=value are supported, but shell expansion is never evaluated.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:GridRoot = Split-Path -Parent $PSScriptRoot
$script:GridSettings = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)

function Initialize-GridDeployment {
    $envPath = Join-Path $script:GridRoot '.env'
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        throw "Create $envPath and configure deployment values first."
    }
    $script:GridSettings.Clear()
    $lineNumber = 0
    foreach ($raw in [IO.File]::ReadAllLines($envPath)) {
        $lineNumber++
        $line = $raw.Trim()
        if (-not $line -or $line.StartsWith('#')) { continue }
        if ($line -notmatch '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') {
            throw "Invalid .env entry at line $lineNumber. Use KEY=value."
        }
        $key = $Matches[1]
        $value = $Matches[2].Trim()
        if ($value.StartsWith('"') -or $value.StartsWith("'")) {
            if ($value.Length -lt 2 -or $value[$value.Length - 1] -ne $value[0]) {
                throw "Unclosed quote for $key in .env."
            }
            $value = $value.Substring(1, $value.Length - 2)
        }
        if ($script:GridSettings.ContainsKey($key) -and $script:GridSettings[$key] -cne $value) {
            throw "Conflicting duplicate entry for $key in .env."
        }
        $script:GridSettings[$key] = $value
    }
}

function Get-GridSetting {
    param([string[]]$Names, [string]$Default = '', [switch]$Required)
    $result = ''
    foreach ($key in $Names) {
        if ($script:GridSettings.ContainsKey($key) -and $script:GridSettings[$key] -ne '') {
            if ($result -ne '' -and $result -cne $script:GridSettings[$key]) {
                throw "Conflicting .env aliases: $($Names -join ', '). Use the same value."
            }
            $result = $script:GridSettings[$key]
        }
    }
    if ($result -eq '') { $result = $Default }
    if ($Required -and $result -eq '') { throw "Set $($Names -join ' or ') in the repository .env." }
    return $result
}

function Assert-GridProduction {
    $mode = Get-GridSetting -Names @('GRID_ENV', 'ASPNETCORE_ENVIRONMENT') -Required
    if ($mode -ine 'Production') { throw 'Cloud deployment requires GRID_ENV=Production in .env.' }
    foreach ($key in @('PUBSUB_EMULATOR_HOST', 'STORAGE_EMULATOR_HOST')) {
        if (Get-GridSetting -Names @($key)) { throw "Remove or empty $key in .env before cloud deployment." }
    }
}

function Get-GridCloudConfig {
    $project = Get-GridSetting -Names @('GoogleCloud__ProjectId', 'GOOGLE_CLOUD_PROJECT') -Required
    return @{
        Project = $project
        Region = Get-GridSetting -Names @('GCP_REGION') -Default 'us-central1'
        Bucket = Get-GridSetting -Names @('GoogleCloud__Bucket', 'GCS_BUCKET') -Required
        Repository = Get-GridSetting -Names @('ARTIFACT_REGISTRY_REPOSITORY') -Default 'grid-repo'
        ApiService = Get-GridSetting -Names @('CLOUD_RUN_API_SERVICE') -Default 'grid-api'
        WorkerService = Get-GridSetting -Names @('CLOUD_RUN_WORKER_SERVICE') -Default 'grid-worker'
        ApiAccount = Get-GridSetting -Names @('GRID_API_SERVICE_ACCOUNT') -Default "grid-api-sa@$project.iam.gserviceaccount.com"
        WorkerAccount = Get-GridSetting -Names @('GRID_WORKER_SERVICE_ACCOUNT') -Default "grid-worker-sa@$project.iam.gserviceaccount.com"
        ParserTopic = Get-GridSetting -Names @('GoogleCloud__ParserTopicId', 'PUBSUB_PARSER_TOPIC') -Default 'grid-parser-runs'
        DatasetTopic = Get-GridSetting -Names @('GoogleCloud__DatasetTopicId') -Default 'grid-datasets'
        Subscription = Get-GridSetting -Names @('PUBSUB_PARSER_SUBSCRIPTION') -Default 'grid-parser-runs-etl'
        FirebaseProject = Get-GridSetting -Names @('FIREBASE_PROJECT_ID') -Default $project
    }
}

function Resolve-GridTool {
    param([string]$Name)
    if ($env:OS -eq 'Windows_NT' -and (Get-Command "$Name.cmd" -ErrorAction SilentlyContinue)) { return "$Name.cmd" }
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Install $Name and sign in where required before deployment." }
    return $Name
}

function Invoke-GridCommand {
    param([string]$Command, [string[]]$Arguments)
    $tool = Resolve-GridTool $Command
    & $tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE. Deployment stopped." }
}

function Test-GridResource {
    param([string]$Command = 'gcloud', [string[]]$Arguments)
    $tool = Resolve-GridTool $Command
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $tool @Arguments *> $null
        return $LASTEXITCODE -eq 0
    } finally { $ErrorActionPreference = $previousPreference }
}

function New-GridEnvironmentFile {
    param([System.Collections.IDictionary]$Values)
    $file = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($file, (ConvertTo-Json -InputObject $Values -Compress), [Text.UTF8Encoding]::new($false))
        return $file
    } catch {
        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Get-GridApiUrl {
    param([System.Collections.IDictionary]$Cloud)
    $configured = Get-GridSetting -Names @('App__SelfUrl', 'VITE_API_URL', 'BACKEND_API_URL', 'GRID_API_URL')
    if ($configured) {
        if (-not $configured.StartsWith('https://')) { throw 'The production API URL in .env must start with https://.' }
        return $configured.TrimEnd('/')
    }
    $url = Invoke-GridCommand 'gcloud' @('run', 'services', 'describe', $Cloud.ApiService,
        "--project=$($Cloud.Project)", "--region=$($Cloud.Region)", '--platform=managed', '--format=value(status.url)')
    if (-not $url -or -not ([string]$url).StartsWith('https://')) {
        throw 'Set App__SelfUrl or VITE_API_URL in .env, or deploy the API first for URL discovery.'
    }
    return ([string]$url).Trim().TrimEnd('/')
}

function Get-GridWebUrl {
    param([System.Collections.IDictionary]$Cloud)
    $explicit = Get-GridSetting -Names @('OpenIddict__Applications__Grid_App__RootUrl')
    if ($explicit) { return $explicit.TrimEnd('/') }
    $origins = Get-GridSetting -Names @('App__CorsOrigins')
    if ($origins) {
        $first = ($origins -split ',')[0].Trim().TrimEnd('/')
        if ($first.StartsWith('https://') -or $first.StartsWith('http://')) { return $first }
    }
    return "https://$($Cloud.FirebaseProject).web.app"
}
