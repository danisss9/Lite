param([string]$Python = 'python')
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
Push-Location -LiteralPath $wptDirectory
try {
    & $Python wpt manifest --no-download --rebuild --path $manifest
    if ($LASTEXITCODE -ne 0) { throw "WPT manifest generation failed with exit code $LASTEXITCODE." }
    $provenance = @{
        revision = $actual
        suiteLockSha256 = (Get-FileHash -LiteralPath (Join-Path $repository 'Lite.Conformance\test-suites.lock.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        manifestSha256 = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $provenance | ConvertTo-Json | Set-Content -LiteralPath ($manifest + '.meta.json') -Encoding utf8
} finally { Pop-Location }
