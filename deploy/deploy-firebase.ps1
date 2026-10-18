# Run: .\deploy\deploy-firebase.ps1 (all values come from the root .env).
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
Initialize-GridDeployment
Assert-GridProduction
$cloud = Get-GridCloudConfig
$apiUrl = Get-GridApiUrl $cloud
$null = Resolve-GridTool 'npm'
$null = Resolve-GridTool 'firebase'
$previous = @{}
Push-Location $script:GridRoot
try {
    foreach ($key in $script:GridSettings.Keys) {
        if ($key.StartsWith('VITE_')) {
            $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $script:GridSettings[$key], 'Process')
        }
    }
    if (-not $previous.ContainsKey('VITE_API_URL')) { $previous['VITE_API_URL'] = $env:VITE_API_URL }
    $env:VITE_API_URL = $apiUrl
    Write-Host "Building frontend for $($cloud.FirebaseProject). API URL: $apiUrl"
    Invoke-GridCommand 'npm' @('--prefix', 'apps/web', 'run', 'build')
    Invoke-GridCommand 'firebase' @('deploy', '--only', 'hosting', '--project', $cloud.FirebaseProject, '--non-interactive')
    Write-Host 'Firebase Hosting deployment completed.'
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    Pop-Location
}
