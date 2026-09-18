param(
    [string]$Python = 'python',
    [string]$VirtualEnvironment,
    [switch]$SkipEnvironmentSetup
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$wptDirectory = Join-Path $repository 'Lite.Conformance\vendor\wpt'
$expected = (Get-Content -LiteralPath (Join-Path $repository 'Lite.Conformance\test-suites.lock.json') -Raw | ConvertFrom-Json).suites |
    Where-Object { $_.id -eq 'wpt' }
$actual = & git -C $wptDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $expected.revision) { throw 'Fetch the locked WPT checkout first.' }
$changes = & git -C $wptDirectory status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Generate the manifest from a clean locked WPT checkout.' }
$artifacts = Join-Path $repository 'Lite.Conformance\artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$manifest = Join-Path $artifacts 'wpt-manifest.json'
$pythonVersion = & $Python -c 'import sys; print(sys.version_info.major, sys.version_info.minor, sep=chr(46))'
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the Python interpreter version.' }
# A shared _venv3 can silently retain a different interpreter after Python upgrades.
$venv = if ($VirtualEnvironment) {
    (Resolve-Path -LiteralPath $VirtualEnvironment).Path
} else {
    if ($SkipEnvironmentSetup) { throw '-SkipEnvironmentSetup requires an existing -VirtualEnvironment.' }
    Join-Path $repository "Lite.Conformance\vendor\manifest-venv-$pythonVersion"
}
$environmentArgs = @('--venv', $venv)
if ($SkipEnvironmentSetup) { $environmentArgs += '--skip-venv-setup' }
Push-Location -LiteralPath $wptDirectory
try {
    & $Python wpt @environmentArgs manifest --no-download --rebuild --no-parallel --path $manifest
    if ($LASTEXITCODE -ne 0) { throw "WPT manifest generation failed with exit code $LASTEXITCODE." }
    $provenance = @{
        revision = $actual
        suiteLockSha256 = (Get-FileHash -LiteralPath (Join-Path $repository 'Lite.Conformance\test-suites.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        manifestSha256 = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $provenance | ConvertTo-Json | Set-Content -LiteralPath ($manifest + '.meta.json') -Encoding utf8
} finally { Pop-Location }
