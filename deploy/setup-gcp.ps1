# Run: .\deploy\setup-gcp.ps1 (project and resource settings come from .env).
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-GridDeployment
Assert-GridProduction
$cloud = Get-GridCloudConfig
$null = Resolve-GridTool 'gcloud'
$null = Resolve-GridTool 'gsutil'
Push-Location $script:GridRoot
try {
    Write-Host "Configuring infrastructure in $($cloud.Project) / $($cloud.Region)."
    Invoke-GridCommand 'gcloud' @('services', 'enable', 'run.googleapis.com', 'pubsub.googleapis.com',
        'storage.googleapis.com', 'artifactregistry.googleapis.com', 'secretmanager.googleapis.com', "--project=$($cloud.Project)", '--quiet')
    if (-not (Test-GridResource -Arguments @('artifacts', 'repositories', 'describe', $cloud.Repository, "--project=$($cloud.Project)", "--location=$($cloud.Region)"))) {
        Invoke-GridCommand 'gcloud' @('artifacts', 'repositories', 'create', $cloud.Repository, '--repository-format=docker',
            "--location=$($cloud.Region)", "--project=$($cloud.Project)", '--description=Docker repository for Grid services', '--quiet')
    }
    if (-not (Test-GridResource -Command 'gsutil' -Arguments @('ls', '-b', "gs://$($cloud.Bucket)"))) {
        Invoke-GridCommand 'gsutil' @('mb', '-p', $cloud.Project, '-c', 'standard', '-l', $cloud.Region, "gs://$($cloud.Bucket)/")
    }
    foreach ($topic in @($cloud.ParserTopic, $cloud.DatasetTopic)) {
        if (-not (Test-GridResource -Arguments @('pubsub', 'topics', 'describe', $topic, "--project=$($cloud.Project)"))) {
            Invoke-GridCommand 'gcloud' @('pubsub', 'topics', 'create', $topic, "--project=$($cloud.Project)", '--quiet')
        }
    }
    if (-not (Test-GridResource -Arguments @('pubsub', 'subscriptions', 'describe', $cloud.Subscription, "--project=$($cloud.Project)"))) {
        Invoke-GridCommand 'gcloud' @('pubsub', 'subscriptions', 'create', $cloud.Subscription, "--topic=$($cloud.ParserTopic)",
            '--ack-deadline=600', "--project=$($cloud.Project)", '--quiet')
    }
    foreach ($account in @($cloud.ApiAccount, $cloud.WorkerAccount)) {
        if (-not (Test-GridResource -Arguments @('iam', 'service-accounts', 'describe', $account, "--project=$($cloud.Project)"))) {
            $accountId = ($account -split '@')[0]
            Invoke-GridCommand 'gcloud' @('iam', 'service-accounts', 'create', $accountId, "--project=$($cloud.Project)", '--quiet')
        }
        Invoke-GridCommand 'gsutil' @('iam', 'ch', "serviceAccount:${account}:roles/storage.objectAdmin", "gs://$($cloud.Bucket)")
        Invoke-GridCommand 'gcloud' @('projects', 'add-iam-policy-binding', $cloud.Project,
            "--member=serviceAccount:$account", '--role=roles/pubsub.editor', "--project=$($cloud.Project)", '--quiet')
    }
    foreach ($topic in @($cloud.ParserTopic, $cloud.DatasetTopic)) {
        Invoke-GridCommand 'gcloud' @('pubsub', 'topics', 'add-iam-policy-binding', $topic,
            "--member=serviceAccount:$($cloud.ApiAccount)", '--role=roles/pubsub.publisher', "--project=$($cloud.Project)", '--quiet')
    }
    Invoke-GridCommand 'gcloud' @('pubsub', 'subscriptions', 'add-iam-policy-binding', $cloud.Subscription,
        "--member=serviceAccount:$($cloud.WorkerAccount)", '--role=roles/pubsub.subscriber', "--project=$($cloud.Project)", '--quiet')
    Write-Host 'GCP infrastructure setup completed.'
} finally { Pop-Location }
