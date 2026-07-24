[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stack = Join-Path $root 'infra\miabi-local'

docker compose `
    --env-file (Join-Path $stack '.env') `
    -f (Join-Path $stack 'compose.yaml') `
    down

if ($LASTEXITCODE -ne 0) {
    throw 'docker compose down failed.'
}

Write-Host 'Miabi stopped. Database and log volumes were preserved.'
