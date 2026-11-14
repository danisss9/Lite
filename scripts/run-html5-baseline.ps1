# Runs one phase of the --suite html5 baseline in parallel shards. The reviewed gate is
# 7,837 expanded paths and the encoding legacy-mb pages hold ~4 GB each while they run, so
# the whole queue does not fit in one 8-way pass on a 32 GB machine: phases bound the
# concurrency per family and the aggregator merges the phase reports into one evidence set.
# Pass -Aggregate -Gate after the last phase to enforce the reviewed no-regression baseline
# (Wpt\html5-baseline-passes.txt); without -Gate the merge only checks the paths that ran.
# Long-running (hours): launch detached, e.g.
#   Start-Process powershell -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',
#     'scripts\run-html5-baseline.ps1','-Filter','html/','-Label','html' -RedirectStandardOutput <log> -WindowStyle Hidden
#
# Shards share one build identity; every shard writes its own evidence report and
# aggregate-html5-baseline.py refuses completed evidence when identities differ or a shard
# died mid-queue. Keep the working tree unchanged while shards run: the identity hashes
# every tracked and untracked source file, so editing a file mid-run marks the evidence
# incomplete.

param(
    [int]$ShardCount = 0,
    [string]$Filter = '',
    [string]$Label = 'all',
    [string]$Dotnet = 'dotnet',
    [switch]$SkipBuild,
    [switch]$Aggregate,
    [switch]$Gate
)

$ErrorActionPreference = 'Stop'
# 0 asks for the adaptive default: four cores held back for the shared server, the OS and
# the probe gates, clamped to the reviewed 1..16 range. An explicit value must be in range.
if ($ShardCount -eq 0) { $ShardCount = [Math]::Min(16, [Math]::Max(4, [Environment]::ProcessorCount - 4)) }
if ($ShardCount -lt 1 -or $ShardCount -gt 16) { throw 'ShardCount must be 0 (adaptive default) or 1..16.' }
if ($Label -notmatch '^[A-Za-z0-9._-]+$') { throw 'Label must be a simple file-name token (letters, digits, dot, dash, underscore).' }
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'Lite.Conformance\Lite.Conformance.csproj'
$artifacts = Join-Path $repo 'Lite.Conformance\artifacts'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $env:TEMP "html5-baseline-$Label-$stamp"
New-Item -ItemType Directory -Force $logDir | Out-Null

# One build for every shard so the evidence identities match. Phases that run while another
# phase holds the build output pass -SkipBuild; the build must then have happened once already.
if (-not $SkipBuild) {
    & $Dotnet build $project -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed; not collecting evidence.' }
}

# Stale reports from an earlier run of the same phase would be counted as this run's evidence.
Get-ChildItem $artifacts -Filter "html5-results-$Label-*-of-$ShardCount.json" -ErrorAction SilentlyContinue |
    Remove-Item -Force

$procs = @()
for ($i = 0; $i -lt $ShardCount; $i++) {
    $out = Join-Path $logDir "shard-$i.out.log"
    $err = Join-Path $logDir "shard-$i.err.log"
    # The phase names its own reports so the aggregator can merge phases and the documented
    # evidence path stays unambiguous.
    $report = Join-Path $artifacts "html5-results-$Label-$i-of-$ShardCount.json"
    $arguments = @('run', '--project', $project, '-c', 'Release', '--no-build', '--',
        '--suite', 'html5', '--shard', "$i/$ShardCount", '--report', $report)
    if ($Filter) { $arguments += @('--filter', $Filter) }
    $procs += Start-Process $Dotnet -ArgumentList $arguments -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
    Write-Host "shard $i/$ShardCount launched (pid $($procs[-1].Id))"
}

foreach ($proc in $procs) {
    Wait-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $proc.Refresh()
    Write-Host "pid $($proc.Id) exited with $($proc.ExitCode) (nonzero is expected while the suite has failures)"
}

$reports = @()
for ($i = 0; $i -lt $ShardCount; $i++) {
    $candidate = Join-Path $artifacts "html5-results-$Label-$i-of-$ShardCount.json"
    if (Test-Path $candidate) { $reports += Get-Item $candidate }
}
if ($reports.Count -ne $ShardCount) {
    throw "Expected $ShardCount shard reports for phase '$Label', found $($reports.Count). Shard logs: $logDir"
}
Write-Host "phase '$Label' reports:"
$reports | ForEach-Object { Write-Host "  $($_.FullName)" }
# A report can exist and still be a checkpoint: a shard that died mid-queue, or a run whose
# final identity verification failed because another track edited the checkout. Surface it
# here instead of letting a truncated phase look like a finished one.
$incomplete = @($reports | Where-Object { -not ((Get-Content $_.FullName -Raw | ConvertFrom-Json).completed) })
if ($incomplete.Count -gt 0) {
    Write-Warning "$($incomplete.Count) of $ShardCount '$Label' shard reports are not completed evidence:"
    $incomplete | ForEach-Object { Write-Warning "  $($_.Name)" }
}

if ($Aggregate) {
    $all = @(Get-ChildItem $artifacts -Filter 'html5-results-*-of-*.json' |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-24) } | Sort-Object Name)
    $aggregated = Join-Path $artifacts 'html5-baseline.md'
    # A full-queue aggregation can enforce the reviewed no-regression baseline; a phase run
    # checks only the baseline paths it actually executed.
    $aggregateArguments = @($all | ForEach-Object { $_.FullName }) + @('--output', $aggregated)
    if ($Gate) { $aggregateArguments += '--gate' } else { $aggregateArguments += '--allow-missing-baseline' }
    & python (Join-Path $PSScriptRoot 'aggregate-html5-baseline.py') @aggregateArguments
    Write-Host "aggregation exit: $LASTEXITCODE"
    Write-Host "report: $aggregated"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "shard logs: $logDir"
