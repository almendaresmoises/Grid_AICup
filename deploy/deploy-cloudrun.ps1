# Run: .\deploy\deploy-cloudrun.ps1 (all values come from the root .env).
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-GridDeployment
Assert-GridProduction
$cloud = Get-GridCloudConfig
$connection = Get-GridSetting -Names @('ConnectionStrings__Default') -Required
$secret = Get-GridSetting -Names @('GoogleCloud__WorkerSecret', 'GRID_WORKER_SECRET') -Required
$certificate = Get-GridSetting -Names @('AuthServer__CertificatePassPhrase') -Required
$apiKey = Get-GridSetting -Names @('Gemini__ApiKey', 'GEMINI_API_KEY')
$model = Get-GridSetting -Names @('Gemini__Model', 'GEMINI_MODEL')
$webUrl = Get-GridWebUrl $cloud
$cors = Get-GridSetting -Names @('App__CorsOrigins') -Default $webUrl
$tag = Get-GridSetting -Names @('GRID_IMAGE_TAG') -Default 'latest'
$registry = "$($cloud.Region)-docker.pkg.dev"
$prefix = "$registry/$($cloud.Project)/$($cloud.Repository)"
$apiImage = "$prefix/grid-api:$tag"
$workerImage = "$prefix/grid-worker:$tag"
$apiMemory = Get-GridSetting -Names @('GRID_API_MEMORY') -Default '1Gi'
$apiCpu = Get-GridSetting -Names @('GRID_API_CPU') -Default '1'
$workerMemory = Get-GridSetting -Names @('GRID_WORKER_MEMORY') -Default '2Gi'
$workerCpu = Get-GridSetting -Names @('GRID_WORKER_CPU') -Default '2'
$workerMin = Get-GridSetting -Names @('GRID_WORKER_MIN_INSTANCES') -Default '1'
$workerTimeout = Get-GridSetting -Names @('GRID_WORKER_TIMEOUT') -Default '600'
$configuredApiUrl = Get-GridSetting -Names @('App__SelfUrl', 'VITE_API_URL', 'BACKEND_API_URL', 'GRID_API_URL')
$authority = Get-GridSetting -Names @('AuthServer__Authority')
$appClient = Get-GridSetting -Names @('OpenIddict__Applications__Grid_App__ClientId') -Default 'Grid_App'
$swaggerClient = Get-GridSetting -Names @('OpenIddict__Applications__Grid_Swagger__ClientId') -Default 'Grid_Swagger'
$null = Resolve-GridTool 'gcloud'
$null = Resolve-GridTool 'docker'
$apiFile = $null
$workerFile = $null
Push-Location $script:GridRoot
try {
    Write-Host "Deploying Cloud Run services in $($cloud.Project) / $($cloud.Region)."
    Invoke-GridCommand 'gcloud' @('auth', 'configure-docker', $registry, '--quiet')
    Invoke-GridCommand 'docker' @('build', '-f', 'deploy/api.Dockerfile', '-t', $apiImage, '.')
    Invoke-GridCommand 'docker' @('push', $apiImage)
    $apiEnv = @{
        ASPNETCORE_ENVIRONMENT = 'Production'; GRID_ENV = 'Production'
        ASPNETCORE_HTTP_PORTS = '8080'; ASPNETCORE_FORWARDEDHEADERS_ENABLED = 'true'
        GoogleCloud__ProjectId = $cloud.Project; GoogleCloud__Bucket = $cloud.Bucket
        GoogleCloud__ParserTopicId = $cloud.ParserTopic; GoogleCloud__DatasetTopicId = $cloud.DatasetTopic
        GoogleCloud__WorkerSecret = $secret; ConnectionStrings__Default = $connection
        App__CorsOrigins = $cors; AuthServer__RequireHttpsMetadata = 'true'
        AuthServer__CertificatePassPhrase = $certificate
        OpenIddict__Applications__Grid_App__ClientId = $appClient
        OpenIddict__Applications__Grid_App__RootUrl = $webUrl
        OpenIddict__Applications__Grid_Swagger__ClientId = $swaggerClient
    }
    foreach ($key in $script:GridSettings.Keys) {
        if ($key -cmatch '^(App__|AuthServer__|OpenIddict__|AiOptimization__)' -and
            $script:GridSettings[$key] -ne '' -and -not $apiEnv.ContainsKey($key)) {
            $apiEnv[$key] = $script:GridSettings[$key]
        }
    }
    if ($apiKey) { $apiEnv['GEMINI_API_KEY'] = $apiKey; $apiEnv['Gemini__ApiKey'] = $apiKey }
    if ($model) { $apiEnv['GEMINI_MODEL'] = $model; $apiEnv['Gemini__Model'] = $model }
    if ($configuredApiUrl) {
        $apiEnv['App__SelfUrl'] = $configuredApiUrl
        $apiEnv['AuthServer__Authority'] = $(if ($authority) { $authority } else { $configuredApiUrl })
        $apiEnv['OpenIddict__Applications__Grid_Swagger__RootUrl'] = Get-GridSetting -Names @('OpenIddict__Applications__Grid_Swagger__RootUrl') -Default $configuredApiUrl
    } elseif ($authority) {
        $apiEnv['AuthServer__Authority'] = $authority
    }
    $apiFile = New-GridEnvironmentFile $apiEnv
    Invoke-GridCommand 'gcloud' @('run', 'deploy', $cloud.ApiService, "--project=$($cloud.Project)",
        "--image=$apiImage", '--platform=managed', "--region=$($cloud.Region)", '--port=8080',
        '--allow-unauthenticated', "--service-account=$($cloud.ApiAccount)", "--memory=$apiMemory", "--cpu=$apiCpu",
        "--env-vars-file=$apiFile", '--quiet')
    if (-not $configuredApiUrl) {
        $apiUrl = Get-GridApiUrl $cloud
        $apiEnv['App__SelfUrl'] = $apiUrl
        $apiEnv['AuthServer__Authority'] = $(if ($authority) { $authority } else { $apiUrl })
        $apiEnv['OpenIddict__Applications__Grid_Swagger__RootUrl'] = Get-GridSetting -Names @('OpenIddict__Applications__Grid_Swagger__RootUrl') -Default $apiUrl
        Remove-Item -LiteralPath $apiFile -Force
        $apiFile = New-GridEnvironmentFile $apiEnv
        Invoke-GridCommand 'gcloud' @('run', 'services', 'update', $cloud.ApiService, "--project=$($cloud.Project)",
            '--platform=managed', "--region=$($cloud.Region)", "--env-vars-file=$apiFile", '--quiet')
    } else {
        $apiUrl = $configuredApiUrl
    }
    Invoke-GridCommand 'docker' @('build', '-f', 'deploy/worker.Dockerfile', '-t', $workerImage, '.')
    Invoke-GridCommand 'docker' @('push', $workerImage)
    $workerFile = New-GridEnvironmentFile @{
        GRID_ENV = 'Production'; GOOGLE_CLOUD_PROJECT = $cloud.Project; GCS_BUCKET = $cloud.Bucket
        PUBSUB_PARSER_TOPIC = $cloud.ParserTopic; PUBSUB_PARSER_SUBSCRIPTION = $cloud.Subscription
        BACKEND_API_URL = $apiUrl; GRID_API_URL = $apiUrl; GRID_WORKER_SECRET = $secret
        PUBSUB_MODE = Get-GridSetting -Names @('PUBSUB_MODE') -Default 'auto'
    }
    Invoke-GridCommand 'gcloud' @('run', 'deploy', $cloud.WorkerService, "--project=$($cloud.Project)",
        "--image=$workerImage", '--platform=managed', "--region=$($cloud.Region)", '--port=8080',
        '--no-allow-unauthenticated', "--service-account=$($cloud.WorkerAccount)", "--memory=$workerMemory", "--cpu=$workerCpu",
        '--no-cpu-throttling', "--min-instances=$workerMin", "--timeout=$workerTimeout", "--env-vars-file=$workerFile", '--quiet')
    Write-Host "Cloud Run deployment completed. API URL: $apiUrl"
} finally {
    foreach ($file in @($apiFile, $workerFile)) {
        if ($file -and (Test-Path -LiteralPath $file)) { Remove-Item -LiteralPath $file -Force }
    }
    Pop-Location
}
