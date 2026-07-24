[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stack = Join-Path $root 'infra\miabi-local'

docker compose `
    --env-file (Join-Path $stack '.env') `
    -f (Join-Path $stack 'compose.yaml') `
    ps
