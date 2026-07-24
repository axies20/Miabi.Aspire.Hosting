[CmdletBinding()]
param(
    [string]$Workspace = 'local'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $root 'tools\miabi\miabi.exe'
$appHost = Join-Path $root 'examples\Miabi.Aspire.Hosting.AppHost\Miabi.Aspire.Hosting.AppHost.csproj'
$localCompose = Join-Path $root 'infra\miabi-local\compose.yaml'
$tokenParameter = 'Parameters__miabi-token'

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

    $headers = @{ Authorization = "Bearer $token" }
    $domains = Invoke-RestMethod `
        -Uri "$($env:Miabi__Server)/api/v1/workspaces/$Workspace/domains" `
        -Headers $headers
    $localDomain = @($domains.data) |
        Where-Object { $_.name -eq 'blazor.localhost' } |
        Select-Object -First 1
    if (-not $localDomain) {
        throw "Miabi domain 'blazor.localhost' was not found after deployment."
    }
    if (-not $localDomain.verified) {
        Invoke-RestMethod `
            -Method Post `
            -Uri "$($env:Miabi__Server)/api/v1/admin/domains/$($localDomain.id)/force-verify" `
            -Headers $headers | Out-Null
        Write-Host "Force-verified local development domain 'blazor.localhost'."
    }

    Invoke-RestMethod `
        -Method Post `
        -Uri "$($env:Miabi__Server)/api/v1/admin/routes/resync" `
        -Headers $headers | Out-Null
    Write-Host 'Resynchronized Miabi gateway routes.'

    docker compose -f $localCompose restart gateway
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to restart the local Miabi gateway.'
    }

    $routeReady = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -Uri 'http://blazor.localhost/' `
                -TimeoutSec 5 `
                -SkipHttpErrorCheck
            if ($response.StatusCode -ne 404) {
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
        throw "Miabi deployment completed, but 'http://blazor.localhost/' is still not routed."
    }
    Write-Host "Application is available at http://blazor.localhost/"
}
finally {
    Remove-Item Env:\MIABI_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:\MIABI_SERVER -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable($tokenParameter, $null, 'Process')
}
