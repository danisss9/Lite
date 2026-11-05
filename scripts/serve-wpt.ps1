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
# Python 3.15 removed http.server.CGIHTTPRequestHandler, which the pinned checkout's
# vendored pywebsocket3 subclasses at import time; the compatibility shim (kept outside
# the checkout so the pin stays clean) restores it at interpreter startup and is inert
# on interpreters that still ship the class.
$compat = Join-Path $PSScriptRoot 'wptserve-py315'
if (Test-Path -LiteralPath (Join-Path $compat 'sitecustomize.py')) {
    $env:PYTHONPATH = if ($env:PYTHONPATH) { "$compat;$env:PYTHONPATH" } else { $compat }
}
$serveArguments = @('wpt', 'serve', '--no-h2')
# The harness addresses wptserve by loopback (--wpt-base-url http://127.0.0.1:8000), so the
# shipped default config binds the servers to 127.0.0.1 directly and skips the subdomain
# startup probes (which need web-platform.test mapped in the hosts file). A -Config file
# replaces it for a full multi-domain setup.
if ($Config) { $serveArguments += @('--config', (Resolve-Path -LiteralPath $Config).Path) }
else {
    $loopback = Join-Path $PSScriptRoot 'wptserve-loopback.json'
    if (Test-Path -LiteralPath $loopback) { $serveArguments += @('--config', $loopback) }
}
Push-Location -LiteralPath $wptDirectory
try {
    & $Python @serveArguments
    if ($LASTEXITCODE -ne 0) { throw "Upstream wpt serve exited with code $LASTEXITCODE." }
} finally { Pop-Location }
