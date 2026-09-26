<#
.SYNOPSIS
    Runs the RoadOps test suites (unit, integration, stress) and prints a summary.

.DESCRIPTION
    - Unit tests run anywhere (no database).
    - Integration and stress tests host the API in-process against a throwaway PostgreSQL
      container (Testcontainers), so Docker must be running. Alternatively set
      ROADOPS_TEST_CONNECTION to use an existing database (e.g. the docker-compose one).
    - Results (TRX files + stress report) are written to TestResults/<timestamp>/.

.EXAMPLE
    ./scripts/run-tests.ps1
    ./scripts/run-tests.ps1 -Suite unit
    ./scripts/run-tests.ps1 -Suite stress -StressDuration 60 -StressConcurrency 50
    ./scripts/run-tests.ps1 -Suite stress -StressBaseUrl http://localhost:5277
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'unit', 'integration', 'stress')]
    [string]$Suite = 'all',
    [int]$StressDuration = 20,
    [int]$StressConcurrency = 25,
    [double]$StressMaxP95Ms = 500,
    [int]$StressBurstSize = 500,
    [string]$StressBaseUrl = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultsDir = Join-Path $root "TestResults\$stamp"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$suites = [ordered]@{
    unit        = 'tests\RoadOps.UnitTests\RoadOps.UnitTests.csproj'
    integration = 'tests\RoadOps.IntegrationTests\RoadOps.IntegrationTests.csproj'
    stress      = 'tests\RoadOps.StressTests\RoadOps.StressTests.csproj'
}
$selected = if ($Suite -eq 'all') { @($suites.Keys) } else { @($Suite) }

function Write-Section([string]$text) {
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
}

# --- Pre-flight -------------------------------------------------------------
Write-Section 'Pre-flight checks'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK (dotnet) is not on PATH.' }
Write-Host "dotnet $(dotnet --version)"

$needsDocker = ($selected -contains 'integration' -and -not $env:ROADOPS_TEST_CONNECTION) -or
               ($selected -contains 'stress' -and -not $StressBaseUrl -and -not $env:ROADOPS_TEST_CONNECTION)
if ($needsDocker) {
    docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop (integration/stress tests use a PostgreSQL container), or set ROADOPS_TEST_CONNECTION.'
    }
    Write-Host 'Docker engine is running.'
}

# --- Build once -------------------------------------------------------------
Write-Section "Building solution ($Configuration)"
dotnet build (Join-Path $root 'RoadOps.sln') -c $Configuration --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# --- Stress settings (read by the stress tests) -----------------------------
$env:STRESS_DURATION_SECONDS = "$StressDuration"
$env:STRESS_CONCURRENCY = "$StressConcurrency"
$env:STRESS_MAX_P95_MS = "$StressMaxP95Ms"
$env:STRESS_BURST_SIZE = "$StressBurstSize"
$env:STRESS_REPORT_PATH = Join-Path $resultsDir 'stress-report.md'
if ($StressBaseUrl) { $env:STRESS_BASE_URL = $StressBaseUrl } else { Remove-Item Env:STRESS_BASE_URL -ErrorAction SilentlyContinue }

# --- Run suites -------------------------------------------------------------
$summary = @()
foreach ($name in $selected) {
    Write-Section "Running $name tests"
    $trx = "$name.trx"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    dotnet test (Join-Path $root $suites[$name]) -c $Configuration --no-build --nologo `
        --logger "trx;LogFileName=$trx" --logger 'console;verbosity=normal' --results-directory $resultsDir
    $exit = $LASTEXITCODE
    $watch.Stop()

    $total = 0; $passed = 0; $failed = 0
    $trxPath = Join-Path $resultsDir $trx
    if (Test-Path $trxPath) {
        [xml]$xml = Get-Content $trxPath
        $counters = $xml.TestRun.ResultSummary.Counters
        $total = [int]$counters.total; $passed = [int]$counters.passed; $failed = [int]$counters.failed
    }
    $status = if ($exit -eq 0) { 'PASS' } else { 'FAIL' }
    $summary += [pscustomobject]@{
        Suite    = $name
        Status   = $status
        Total    = $total
        Passed   = $passed
        Failed   = $failed
        Duration = '{0:N1}s' -f $watch.Elapsed.TotalSeconds
    }
}

# --- Summary ----------------------------------------------------------------
Write-Section 'Summary'
$summary | Format-Table -AutoSize | Out-String | Write-Host

if (Test-Path $env:STRESS_REPORT_PATH) {
    Write-Section 'Stress test report'
    Get-Content $env:STRESS_REPORT_PATH -Encoding UTF8 | Write-Host
}
Write-Host "Results: $resultsDir"

if ($summary | Where-Object Status -eq 'FAIL') {
    Write-Host 'One or more suites FAILED.' -ForegroundColor Red
    exit 1
}
Write-Host 'All suites passed.' -ForegroundColor Green
exit 0
