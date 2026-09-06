param(
    [string]$Config = '',
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$wptDirectory = Join-Path $repository 'Lite.Conformance\vendor\wpt'
if (-not (Test-Path -LiteralPath (Join-Path $wptDirectory 'tools\serve\serve.py'))) {
    throw 'Fetch the pinned suites with scripts/fetch-tests.ps1 first.'
}
$expected = (Get-Content -LiteralPath (Join-Path $repository 'Lite.Conformance\test-suites.lock.json') -Raw | ConvertFrom-Json).suites |
    Where-Object { $_.id -eq 'wpt' }
$actual = & git -C $wptDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $expected.revision) { throw 'WPT checkout does not match the suite lock.' }
$serveArguments = @('wpt', 'serve', '--no-h2')
if ($Config) { $serveArguments += @('--config', (Resolve-Path -LiteralPath $Config).Path) }
Push-Location -LiteralPath $wptDirectory
try {
    & $Python @serveArguments
    if ($LASTEXITCODE -ne 0) { throw "Upstream wpt serve exited with code $LASTEXITCODE." }
} finally { Pop-Location }
