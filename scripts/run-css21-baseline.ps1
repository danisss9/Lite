# Runs the complete css21-full baseline for one medium in parallel shards and aggregates
# the failure-cluster report. Long-running (hours): launch detached, e.g.
#   Start-Process powershell -ArgumentList '-NoProfile','-File','scripts\run-css21-baseline.ps1' -RedirectStandardOutput <log> -WindowStyle Hidden
#
# Shards share one build identity; every shard writes its own evidence report and the
# aggregator merges them, refusing to report if identities differ or runs are incomplete.

param(
    [int]$ShardCount = 0,
    [string]$Media = 'screen',
    # Serve the WPT css/CSS2 cases through an upstream `wpt serve` instance so their
    # evidence is stamped upstream-wpt (the only WPT environment the readiness gate accepts).
    # The runner probes the server against the pinned checkout and refuses a mismatch.
    [string]$WptBaseUrl = '',
    # Start scripts/serve-wpt.ps1 for the run (on http://127.0.0.1:8000) and stop it after.
    [switch]$StartWptServe
)

$ErrorActionPreference = 'Stop'
# 0 asks for the adaptive default: cores minus headroom for the shared server and OS,
# clamped to the reviewed 1..16 range. An explicit value must still be within range.
if ($ShardCount -eq 0) { $ShardCount = [Math]::Min(16, [Math]::Max(4, [Environment]::ProcessorCount - 2)) }
if ($ShardCount -lt 1 -or $ShardCount -gt 16) { throw 'ShardCount must be 0 (adaptive default) or 1..16.' }
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'Lite.Conformance\Lite.Conformance.csproj'
$artifacts = Join-Path $repo 'Lite.Conformance\artifacts'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $env:TEMP "css21-baseline-$Media-$stamp"
New-Item -ItemType Directory -Force $logDir | Out-Null

# One build for every shard so the evidence identities match.
dotnet build $project -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Release build failed; not collecting evidence.' }
try { dotnet build (Join-Path $repo 'Lite.Tests\Lite.Tests.csproj') -c Release | Out-Null } catch { }

# Build the official catalog once before the shards start: when the vendored tree changes (a
# restore re-pins its hash), every shard otherwise races to rebuild css21-official-catalog.json
# and all but one die on the locked file. A filter that selects no case runs only the catalog step.
dotnet run --project $project -c Release --no-build -- --suite css21-full --media $Media --catalog official --filter '__catalog-warmup__' | Out-Null
if (-not (Test-Path (Join-Path $artifacts 'css21-official-catalog.json'))) { throw 'Official catalog warm-up failed; not collecting evidence.' }

$wptServe = $null
if ($StartWptServe) {
    if (-not $WptBaseUrl) { $WptBaseUrl = 'http://127.0.0.1:8000' }
    $wptServe = Start-Process powershell -ArgumentList '-NoProfile', '-File', (Join-Path $PSScriptRoot 'serve-wpt.ps1') `
        -RedirectStandardOutput (Join-Path $logDir 'wptserve.out.log') -RedirectStandardError (Join-Path $logDir 'wptserve.err.log') `
        -PassThru -WindowStyle Hidden
    $ready = $false
    for ($t = 0; $t -lt 120 -and -not $ready; $t++) {
        Start-Sleep -Seconds 1
        try { $ready = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 "$WptBaseUrl/resources/testharness.js").StatusCode -eq 200 } catch { }
        if ($wptServe.HasExited) { throw "wpt serve exited early; see $logDir\wptserve.err.log" }
    }
    if (-not $ready) { throw "wpt serve did not answer at $WptBaseUrl within two minutes." }
    Write-Host "wpt serve ready at $WptBaseUrl (pid $($wptServe.Id))"
}

$procs = @()
for ($i = 0; $i -lt $ShardCount; $i++) {
    $out = Join-Path $logDir "shard-$i.out.log"
    $err = Join-Path $logDir "shard-$i.err.log"
    $arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--',
        '--suite', 'css21-full', '--media', $Media, '--shard', "$i/$ShardCount")
    if ($WptBaseUrl) { $arguments += @('--wpt-base-url', $WptBaseUrl) }
    $procs += Start-Process dotnet -ArgumentList $arguments -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
    Write-Host "shard $i/$ShardCount launched (pid $($procs[-1].Id))"
}

$failedShards = @()
try {
    foreach ($proc in $procs) {
        Wait-Process -Id $proc.Id -ErrorAction SilentlyContinue
        $proc.Refresh()
        if ($proc.ExitCode -ne 0) { $failedShards += $proc.Id }
        Write-Host "pid $($proc.Id) exited with $($proc.ExitCode) (nonzero is expected while the suite has failures)"
    }
} finally {
    # wpt serve runs python child processes; take the whole tree down.
    if ($wptServe -and -not $wptServe.HasExited) { & taskkill /PID $wptServe.Id /T /F | Out-Null }
}

$reports = @(Get-ChildItem $artifacts -Filter "css21-full-$Media-*-of-$ShardCount.json" |
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-12) } |
    Sort-Object Name)
if ($reports.Count -ne $ShardCount) {
    throw "Expected $ShardCount fresh shard reports, found $($reports.Count); not aggregating."
}
$reportPaths = @($reports | ForEach-Object { $_.FullName })
$aggregated = Join-Path $artifacts "css21-full-$Media-baseline.md"
& python (Join-Path $PSScriptRoot 'aggregate-css21-baseline.py') @reportPaths --output $aggregated
Write-Host "aggregation exit: $LASTEXITCODE"
Write-Host "report: $aggregated"
Write-Host "shard logs: $logDir"
