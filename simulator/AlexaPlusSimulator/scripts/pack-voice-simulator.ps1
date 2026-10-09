<#
.SYNOPSIS
    Rebuilds vendor/mcp-voice-simulator-<version>-<sha>.tgz from a pinned commit of the mcp-voice-simulator fork.

.DESCRIPTION
    This app depends on the fork through that tarball, so `npm install` needs neither git nor a build step and the
    version can't move. Run this only to change the pinned commit: it clones the public fork into a temporary folder,
    checks out the commit, installs, builds and packs it, then copies the tarball into vendor/. Afterwards update the
    `mcp-voice-simulator` path in package.json if the file name changed, and run `npm install`.

.EXAMPLE
    ./scripts/pack-voice-simulator.ps1
    ./scripts/pack-voice-simulator.ps1 -Commit <full commit sha>
#>
[CmdletBinding()]
param(
    [string]$Repository = 'https://github.com/musawenkos/mcp-voice-simulator.git',
    [string]$Commit = 'aa2b0421ed0839ae18fc7e6eaf027d064f5fb2fb'
)

$ErrorActionPreference = 'Stop'
$app = Split-Path -Parent $PSScriptRoot
$vendor = Join-Path $app 'vendor'
$work = Join-Path ([IO.Path]::GetTempPath()) ('mcp-voice-simulator-' + [guid]::NewGuid().ToString('N'))

function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

try {
    Invoke-Checked 'git clone' { git clone --quiet $Repository $work }
    Push-Location $work
    Invoke-Checked 'git checkout' { git -c advice.detachedHead=false checkout --quiet $Commit }
    $short = (git rev-parse --short=7 HEAD).Trim()
    # The fork's optional peer dependencies (Anthropic, Cursor and Bedrock SDKs) conflict on install without this.
    Invoke-Checked 'npm ci' { npm ci --legacy-peer-deps --no-audit --no-fund }
    Invoke-Checked 'npm run build' { npm run build }
    $packed = (npm pack --silent | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'npm pack failed.' }
    Pop-Location

    New-Item -ItemType Directory -Force -Path $vendor | Out-Null
    $target = Join-Path $vendor ($packed -replace '\.tgz$', "-$short.tgz")
    Copy-Item (Join-Path $work $packed) $target -Force
    Write-Host "Packed $Commit to $target"
}
finally {
    if ((Get-Location).Path -eq $work) { Pop-Location }
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}
