# Runs the complete css21-full baseline for one medium in parallel shards and aggregates
# the failure-cluster report. Long-running (hours): launch detached, e.g.
#   Start-Process powershell -ArgumentList '-NoProfile','-File','scripts\run-css21-baseline.ps1' -RedirectStandardOutput <log> -WindowStyle Hidden
#
# Shards share one build identity; every shard writes its own evidence report and the
# aggregator merges them, refusing to report if identities differ or runs are incomplete.

param(
    [int]$ShardCount = 4,
    [string]$Media = 'screen'
)

$ErrorActionPreference = 'Stop'
if ($ShardCount -lt 1 -or $ShardCount -gt 16) { throw 'ShardCount must be 1..16.' }
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

$procs = @()
for ($i = 0; $i -lt $ShardCount; $i++) {
    $out = Join-Path $logDir "shard-$i.out.log"
    $err = Join-Path $logDir "shard-$i.err.log"
    $arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--',
        '--suite', 'css21-full', '--media', $Media, '--shard', "$i/$ShardCount")
    $procs += Start-Process dotnet -ArgumentList $arguments -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
    Write-Host "shard $i/$ShardCount launched (pid $($procs[-1].Id))"
}

$failedShards = @()
foreach ($proc in $procs) {
    Wait-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $proc.Refresh()
    if ($proc.ExitCode -ne 0) { $failedShards += $proc.Id }
    Write-Host "pid $($proc.Id) exited with $($proc.ExitCode) (nonzero is expected while the suite has failures)"
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
