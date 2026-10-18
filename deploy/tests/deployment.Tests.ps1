# Offline deployment smoke tests. All external tools are replaced with mocks.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File deploy/tests/deployment.Tests.ps1
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('grid-deploy-tests-' + [Guid]::NewGuid().ToString('N'))
$originalLocation = Get-Location
$global:GridMockEvents = [System.Collections.Generic.List[object]]::new()
$global:GridMockFailure = ''
$global:GridMockMissingResources = $false
$global:LASTEXITCODE = 0
$script:checks = 0

function Assert-GridTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Invoke-GridMock {
    param([string]$Tool, [object[]]$ToolArguments)
    $commandLine = "$Tool $($ToolArguments -join ' ')"
    $global:LASTEXITCODE = 0
    $event = @{ Tool = $Tool; Arguments = $ToolArguments; Directory = (Get-Location).Path }
    foreach ($argument in $ToolArguments) {
        if ([string]$argument -like '--env-vars-file=*') {
            $file = ([string]$argument).Substring(16)
            $event.File = $file
            $event.Environment = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        }
    }
    if ($Tool -eq 'npm') { $event.ApiUrl = $env:VITE_API_URL }
    if ($Tool -eq 'dotnet') { $event.Connection = $env:ConnectionStrings__Default; $event.WebUrl = $env:OpenIddict__Applications__Grid_App__RootUrl }
    $global:GridMockEvents.Add($event)
    if ($global:GridMockFailure -and $commandLine.StartsWith($global:GridMockFailure)) { $global:LASTEXITCODE = 23; return }
    if ($Tool -eq 'gcloud' -and $commandLine -like 'gcloud run services describe*') { return 'https://discovered-api.example.test' }
    if ($global:GridMockMissingResources -and ($commandLine -match ' describe ' -or $commandLine -like 'gsutil ls*')) { $global:LASTEXITCODE = 1 }
}

function global:gcloud.cmd { Invoke-GridMock 'gcloud' $args }
function global:gcloud { Invoke-GridMock 'gcloud' $args }
function global:docker { Invoke-GridMock 'docker' $args }
function global:gsutil.cmd { Invoke-GridMock 'gsutil' $args }
function global:gsutil { Invoke-GridMock 'gsutil' $args }
function global:npm.cmd { Invoke-GridMock 'npm' $args }
function global:npm { Invoke-GridMock 'npm' $args }
function global:firebase.cmd { Invoke-GridMock 'firebase' $args }
function global:firebase { Invoke-GridMock 'firebase' $args }
function global:dotnet { Invoke-GridMock 'dotnet' $args }

$fixture = @'
# Literal configuration including special characters and quoted values.
export GRID_ENV=Production
GoogleCloud__ProjectId=test-project
GOOGLE_CLOUD_PROJECT=test-project
GoogleCloud__Bucket=test-bucket
GCS_BUCKET=test-bucket
GoogleCloud__WorkerSecret='fake,worker;$literal "quote"\slash'
GRID_WORKER_SECRET='fake,worker;$literal "quote"\slash'
ConnectionStrings__Default='Host=db.example;Password=space,comma; $literal "quote"\slash'
AuthServer__CertificatePassPhrase=fake-certificate
GEMINI_API_KEY=fake-provider
GEMINI_MODEL=fake-model
GCP_REGION=asia-southeast1
FIREBASE_PROJECT_ID=test-web
App__CorsOrigins=https://one.example,https://two.example
PUBSUB_PARSER_SUBSCRIPTION=custom-subscription
AiOptimization__Enabled=true
'@

function Set-GridFixture {
    param([string]$Text = $fixture)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot '.env'), $Text, [Text.UTF8Encoding]::new($true))
    $global:GridMockEvents.Clear()
    $global:GridMockFailure = ''
    $global:GridMockMissingResources = $false
}

function Invoke-GridTestScript {
    param([string]$Name)
    & (Join-Path $fixtureRoot "deploy/$Name") 6>&1 | Out-String
}

function Assert-GridScriptFails {
    param([string]$Name, [string]$Expected, [switch]$NoCommands)
    $message = ''
    try { $null = Invoke-GridTestScript $Name } catch { $message = $_.Exception.Message }
    Assert-GridTest ($message -like "*$Expected*") "$Name did not report the expected failure: $Expected"
    if ($NoCommands) { Assert-GridTest ($global:GridMockEvents.Count -eq 0) 'Invalid configuration reached an external tool.' }
}

try {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'deploy') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'backend/Grid/src/Grid.DbMigrator') -Force | Out-Null
    Copy-Item -Path (Join-Path (Split-Path -Parent $PSScriptRoot) '*.ps1') -Destination (Join-Path $fixtureRoot 'deploy')
    Set-Location ([IO.Path]::GetTempPath())
    Set-GridFixture
    $output = Invoke-GridTestScript 'deploy-cloudrun.ps1'
    $envEvents = @($global:GridMockEvents | Where-Object { $_.ContainsKey('Environment') })
    Assert-GridTest ($envEvents.Count -eq 3) 'Cloud Run did not deploy API, update its URL, and deploy worker.'
    Assert-GridTest ($envEvents[0].Environment.App__CorsOrigins -ceq 'https://one.example,https://two.example') 'Comma-separated CORS was corrupted.'
    Assert-GridTest ($envEvents[0].Environment.ConnectionStrings__Default -ceq 'Host=db.example;Password=space,comma; $literal "quote"\slash') 'Connection string was corrupted.'
    Assert-GridTest ($envEvents[0].Environment.GoogleCloud__WorkerSecret -ceq 'fake,worker;$literal "quote"\slash') 'Worker secret was corrupted.'
    Assert-GridTest ($envEvents[0].Environment.GEMINI_MODEL -eq 'fake-model') 'Configured model was lost.'
    Assert-GridTest ($envEvents[0].Environment.AiOptimization__Enabled -eq 'true') 'Additional API settings were lost.'
    Assert-GridTest ($envEvents[2].Environment.BACKEND_API_URL -eq 'https://discovered-api.example.test') 'Discovered URL was not propagated.'
    Assert-GridTest ($envEvents[2].Environment.PUBSUB_PARSER_SUBSCRIPTION -eq 'custom-subscription') 'Subscription override was lost.'
    foreach ($event in $envEvents) {
        Assert-GridTest (-not (Test-Path -LiteralPath $event.File)) 'Temporary environment file was not deleted.'
        Assert-GridTest ($event.Arguments -contains '--project=test-project') 'Deployment used an ambient gcloud project.'
        Assert-GridTest ($event.Directory -eq $fixtureRoot) 'Deployment depended on the caller directory.'
    }
    Assert-GridTest (-not $output.Contains('fake,worker') -and -not $output.Contains('Password=space') -and -not $output.Contains('fake-provider')) 'Credentials appeared in script output.'
    Assert-GridTest ((Get-Location).Path -eq ([IO.Path]::GetTempPath()).TrimEnd('\')) 'Caller directory was not restored.'

    Set-GridFixture ($fixture + "`nApp__SelfUrl=https://configured.example.test`nVITE_API_URL=https://configured.example.test")
    $null = Invoke-GridTestScript 'deploy-cloudrun.ps1'
    Assert-GridTest (@($global:GridMockEvents | Where-Object { ($_.Arguments -join ' ') -like 'run services describe*' }).Count -eq 0) 'Configured URL was ignored.'

    foreach ($invalid in @(
        @{ Text = $fixture.Replace('GOOGLE_CLOUD_PROJECT=test-project', 'GOOGLE_CLOUD_PROJECT=other-project'); Expected = 'Conflicting .env aliases' },
        @{ Text = $fixture.Replace('GRID_ENV=Production', 'GRID_ENV=Development'); Expected = 'GRID_ENV=Production' },
        @{ Text = $fixture + "`nPUBSUB_EMULATOR_HOST=localhost:8085"; Expected = 'PUBSUB_EMULATOR_HOST' },
        @{ Text = $fixture + "`nGoogleCloud__Bucket=different"; Expected = 'duplicate' },
        @{ Text = $fixture + "`nBROKEN='unterminated"; Expected = 'Unclosed quote' },
        @{ Text = $fixture + "`nnot a valid entry"; Expected = 'Invalid .env entry' },
        @{ Text = $fixture.Replace('AuthServer__CertificatePassPhrase=fake-certificate', 'AuthServer__CertificatePassPhrase='); Expected = 'AuthServer__CertificatePassPhrase' }
    )) {
        Set-GridFixture $invalid.Text
        Assert-GridScriptFails 'deploy-cloudrun.ps1' $invalid.Expected -NoCommands
    }

    Set-GridFixture
    $global:GridMockFailure = 'gcloud run deploy'
    Assert-GridScriptFails 'deploy-cloudrun.ps1' 'exit code 23'
    foreach ($event in @($global:GridMockEvents | Where-Object { $_.ContainsKey('File') })) {
        Assert-GridTest (-not (Test-Path -LiteralPath $event.File)) 'Failed deploy leaked an environment file.'
    }
    Assert-GridTest (@($global:GridMockEvents | Where-Object { ($_.Arguments -join ' ') -like '*grid-worker*' }).Count -eq 0) 'Failed API deployment continued to worker.'

    Set-GridFixture
    $global:GridMockMissingResources = $true
    $null = Invoke-GridTestScript 'setup-gcp.ps1'
    Assert-GridTest (@($global:GridMockEvents | Where-Object { ($_.Arguments -join ' ') -match ' create |^mb ' }).Count -eq 7) 'Missing infrastructure was not created.'
    Set-GridFixture
    $null = Invoke-GridTestScript 'setup-gcp.ps1'
    Assert-GridTest (@($global:GridMockEvents | Where-Object { ($_.Arguments -join ' ') -match ' create |^mb ' }).Count -eq 0) 'Existing resources were recreated.'
    Set-GridFixture
    $global:GridMockMissingResources = $true
    $global:GridMockFailure = 'gcloud artifacts repositories create'
    Assert-GridScriptFails 'setup-gcp.ps1' 'exit code 23'

    Set-GridFixture
    $env:VITE_API_URL = 'https://old.example.test'
    $null = Invoke-GridTestScript 'deploy-firebase.ps1'
    $npmEvent = @($global:GridMockEvents | Where-Object { $_.Tool -eq 'npm' })[0]
    $firebaseEvent = @($global:GridMockEvents | Where-Object { $_.Tool -eq 'firebase' })[0]
    Assert-GridTest ($npmEvent.ApiUrl -eq 'https://discovered-api.example.test') 'Frontend build used an ambient URL.'
    Assert-GridTest ($firebaseEvent.Arguments -contains 'test-web') 'Firebase used an ambient project.'
    Assert-GridTest ($env:VITE_API_URL -eq 'https://old.example.test') 'Frontend build did not restore process settings.'
    Set-GridFixture
    $global:GridMockFailure = 'npm'
    Assert-GridScriptFails 'deploy-firebase.ps1' 'exit code 23'
    Assert-GridTest (@($global:GridMockEvents | Where-Object { $_.Tool -eq 'firebase' }).Count -eq 0) 'Failed frontend build reached Firebase.'

    Set-GridFixture
    $env:ConnectionStrings__Default = 'old-connection'
    $output = Invoke-GridTestScript 'run-migrations.ps1'
    $migrationEvent = @($global:GridMockEvents | Where-Object { $_.Tool -eq 'dotnet' })[0]
    Assert-GridTest ($migrationEvent.Connection -ceq 'Host=db.example;Password=space,comma; $literal "quote"\slash') 'Migrator did not load the .env connection.'
    Assert-GridTest ($migrationEvent.WebUrl -eq 'https://one.example') 'Migrator web root was not derived from .env.'
    Assert-GridTest ($env:ConnectionStrings__Default -eq 'old-connection') 'Migrator did not restore inherited values.'
    Assert-GridTest (-not $output.Contains('Password=space')) 'Migrator printed the database connection.'
    Set-GridFixture
    $global:GridMockFailure = 'dotnet'
    Assert-GridScriptFails 'run-migrations.ps1' 'exit code 23'

    Set-GridFixture
    Remove-Item -LiteralPath (Join-Path $fixtureRoot '.env')
    Assert-GridScriptFails 'deploy-cloudrun.ps1' 'configure deployment values' -NoCommands
    Write-Output "PowerShell offline deployment tests passed: $script:checks assertions. No external tools were executed."
} finally {
    Set-Location $originalLocation
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedFixture)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
