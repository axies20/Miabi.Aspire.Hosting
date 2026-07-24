[CmdletBinding()]
param(
    [switch]$Pull
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stack = Join-Path $root 'infra\miabi-local'
$envFile = Join-Path $stack '.env'
$composeFile = Join-Path $stack 'compose.yaml'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI was not found. Start Docker Desktop and ensure docker is on PATH.'
}

docker info | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is not running. Start Docker Desktop and retry.'
}

if (-not (Test-Path -LiteralPath $envFile)) {
    function New-HexSecret([int]$bytes = 32) {
        $buffer = [byte[]]::new($bytes)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
        return [Convert]::ToHexString($buffer).ToLowerInvariant()
    }

    @(
        'MIABI_IMAGE=miabi/miabi:1.6.5'
        'MIABI_ADMIN_EMAIL=admin@example.com'
        'MIABI_ADMIN_PASSWORD=MiabiLocal2026!'
        "MIABI_DB_PASSWORD=$(New-HexSecret)"
        "MIABI_REDIS_PASSWORD=$(New-HexSecret)"
        "MIABI_JWT_SECRET=$(New-HexSecret)"
        "MIABI_ENCRYPTION_KEY=$(New-HexSecret)"
    ) | Set-Content -LiteralPath $envFile -Encoding utf8NoBOM
    Write-Host "Created local secrets at $envFile"
}

$arguments = @('compose', '--env-file', $envFile, '-f', $composeFile)
if ($Pull) {
    & docker @arguments pull
    if ($LASTEXITCODE -ne 0) { throw 'docker compose pull failed.' }
}

& docker @arguments up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

Write-Host ''
Write-Host 'Miabi is running at http://localhost:9000'
Write-Host 'Login: admin@example.com / MiabiLocal2026!'
Write-Host 'Next: create a workspace named "local", then create an API token.'
