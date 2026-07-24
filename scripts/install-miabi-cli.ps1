[CmdletBinding()]
param(
    [string]$Version = '0.7.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root 'tools\miabi'
$executable = Join-Path $destination 'miabi.exe'
$architecture = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
    'arm64'
} else {
    'amd64'
}
$asset = "miabi_${Version}_windows_${architecture}.zip"
$releaseBase = "https://github.com/miabi-io/miabi-cli/releases/download/v$Version"
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporary = [System.IO.Path]::GetFullPath(
    (Join-Path $temporaryRoot "miabi-cli-$([Guid]::NewGuid().ToString('N'))"))
if (-not $temporary.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use temporary path outside $temporaryRoot."
}

New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary $asset
    $checksums = Join-Path $temporary 'checksums.txt'
    Invoke-WebRequest "$releaseBase/$asset" -OutFile $archive
    Invoke-WebRequest "$releaseBase/checksums.txt" -OutFile $checksums

    $expectedLine = Get-Content -LiteralPath $checksums |
        Where-Object { $_ -match [regex]::Escape($asset) } |
        Select-Object -First 1
    if (-not $expectedLine) {
        throw "Checksum for $asset was not found."
    }
    $expected = ($expectedLine -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "Checksum verification failed for $asset."
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Miabi CLI executable was not found after extracting $asset."
}

Write-Host "Installed Miabi CLI v$Version at $executable"
Write-Host "For direct commands: `$env:MIABI_CLI_PATH = '$executable'"
