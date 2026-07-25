[CmdletBinding()]
param(
    [string]$Workspace = 'local'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'tools\miabi\miabi.exe'
$appHost = Join-Path $root 'examples\Miabi.Aspire.Hosting.AppHost\Miabi.Aspire.Hosting.AppHost.csproj'
$tokenParameter = 'Parameters__miabi-token'
$applicationUrl = 'http://blazor.apps.localhost/'

if (-not (Test-Path -LiteralPath $cli)) {
    & (Join-Path $PSScriptRoot 'install-miabi-cli.ps1')
}

$token = [Environment]::GetEnvironmentVariable($tokenParameter, 'Process')
if (-not $token) {
    $secureToken = Read-Host 'Paste the Miabi API token' -AsSecureString
    $token = [System.Net.NetworkCredential]::new('', $secureToken).Password.Trim()
    [Environment]::SetEnvironmentVariable(
        $tokenParameter,
        $token,
        'Process')
}
$env:MIABI_CLI_PATH = $cli
$env:Miabi__Server = 'http://localhost:9000'
$env:Miabi__Workspace = $Workspace

try {
    $env:MIABI_SERVER = $env:Miabi__Server
    $env:MIABI_TOKEN = $token
    & $cli whoami --workspace $Workspace --no-color
    if ($LASTEXITCODE -ne 0) {
        throw "Miabi rejected the API token before Aspire deployment. Create a new API key for workspace '$Workspace' and copy the full one-time value beginning with 'mb_'."
    }
    Remove-Item Env:\MIABI_TOKEN

    aspire deploy --project $appHost --non-interactive --clear-cache
    if ($LASTEXITCODE -ne 0) {
        throw 'aspire deploy failed.'
    }

    $routeReady = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -Uri $applicationUrl `
                -TimeoutSec 5 `
                -SkipHttpErrorCheck
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                $routeReady = $true
                break
            }
        }
        catch {
            # The gateway and application may still be converging.
        }
        Start-Sleep -Seconds 1
    }
    if (-not $routeReady) {
        throw "Miabi deployment completed, but '$applicationUrl' is still not routed."
    }
    Write-Host "Application is available at $applicationUrl"
}
finally {
    Remove-Item Env:\MIABI_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:\MIABI_SERVER -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable($tokenParameter, $null, 'Process')
}
