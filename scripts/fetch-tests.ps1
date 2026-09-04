# Fetches conformance test files into Lite.Conformance\vendor\ at pinned commits.
# Run from anywhere: paths are resolved relative to this script.
#
# Commits and sparse paths come from Lite.Conformance\test-suites.lock.json. To bump a suite,
# update that file; the resolved revisions printed below must match it exactly.

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Vendor = Join-Path $RepoRoot 'Lite.Conformance\vendor'
$SuiteLockPath = Join-Path $RepoRoot 'Lite.Conformance\test-suites.lock.json'
$SuiteLock = Get-Content -LiteralPath $SuiteLockPath -Raw | ConvertFrom-Json
$WptPin = $SuiteLock.suites | Where-Object { $_.id -eq 'wpt' } | Select-Object -First 1
$Test262Pin = $SuiteLock.suites | Where-Object { $_.id -eq 'test262' } | Select-Object -First 1
if (-not $WptPin -or -not $Test262Pin) {
    throw "The suite lock must contain wpt and test262 entries: $SuiteLockPath"
}
$WptSha = $WptPin.revision
$Test262Sha = $Test262Pin.revision
$WptDirs = @($WptPin.sparseCheckout)
$Test262Dirs = @($Test262Pin.sparseCheckout)
New-Item -ItemType Directory -Force $Vendor | Out-Null

function Resolve-VendorDestination($RelativePath) {
    $candidate = [IO.Path]::GetFullPath((Join-Path (Join-Path $RepoRoot 'Lite.Conformance') $RelativePath))
    $vendorRoot = [IO.Path]::GetFullPath($Vendor).TrimEnd('\') + '\'
    if (-not $candidate.StartsWith($vendorRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing suite destination outside the vendor directory: $candidate"
    }
    return $candidate
}

$WptDestination = Resolve-VendorDestination $WptPin.destination
$Test262Destination = Resolve-VendorDestination $Test262Pin.destination

function Fetch-SparseRepo($Url, $Dest, $Sha, $Dirs) {
    if (-not (Test-Path (Join-Path $Dest '.git'))) {
        # Clone needs an empty target. If the dir exists without a .git (e.g. a stray
        # manually-downloaded file), clone into a temp dir and move it in, so we never
        # leave a non-repo dir that a later `git -C` would resolve against the PARENT repo.
        Write-Host "Cloning $Url (blobless, no checkout)..."
        $destinationFull = [IO.Path]::GetFullPath($Dest)
        $tmp = [IO.Path]::GetFullPath("$destinationFull._clone_tmp")
        if ($tmp -ne "$destinationFull._clone_tmp") {
            throw "Refusing unexpected clone temporary directory: $tmp"
        }
        if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
        git clone --filter=blob:none --no-checkout $Url $tmp
        if ($LASTEXITCODE -ne 0) { throw "git clone failed for $Url" }
        New-Item -ItemType Directory -Force $Dest | Out-Null
        Move-Item (Join-Path $tmp '.git') (Join-Path $Dest '.git')
        Remove-Item -Recurse -Force $tmp
    }

    # SAFETY: never operate unless the repo top-level really is $Dest. Otherwise `git -C`
    # would silently walk up to the enclosing project repo and mangle the working tree.
    $top = (git -C $Dest rev-parse --show-toplevel 2>$null)
    $destFull = (Resolve-Path $Dest).Path.Replace('\', '/')
    if (-not $top -or $top.Replace('\', '/').TrimEnd('/') -ne $destFull.TrimEnd('/')) {
        throw "Refusing to run git in '$Dest': resolved top-level is '$top', not the vendor dir. Aborting to protect the parent repo."
    }

    git -C $Dest sparse-checkout set --cone @Dirs
    if ($LASTEXITCODE -ne 0) { throw "sparse-checkout failed for $Dest" }
    if ($Sha -eq 'latest') {
        git -C $Dest fetch --depth 1 origin HEAD
        git -C $Dest checkout --force FETCH_HEAD
    } else {
        git -C $Dest fetch origin $Sha
        git -C $Dest checkout --force $Sha
    }
    if ($LASTEXITCODE -ne 0) { throw "checkout failed for $Dest" }
    $resolved = git -C $Dest rev-parse HEAD
    Write-Host "$Dest @ $resolved"
    return $resolved
}

function Fetch-File($Urls, $Dest) {
    if (Test-Path $Dest) { Write-Host "exists: $Dest"; return }
    New-Item -ItemType Directory -Force (Split-Path -Parent $Dest) | Out-Null
    foreach ($u in $Urls) {
        try {
            Write-Host "GET $u"
            Invoke-WebRequest -Uri $u -OutFile $Dest -UseBasicParsing
            return
        } catch {
            Write-Warning "failed: $u ($($_.Exception.Message))"
        }
    }
    Write-Warning "Could not fetch $Dest from any source"
}

# ---- WPT ----
$wptResolved = Fetch-SparseRepo $WptPin.repository $WptDestination $WptSha $WptDirs

# ---- test262 ----
$t262Resolved = Fetch-SparseRepo $Test262Pin.repository $Test262Destination $Test262Sha $Test262Dirs

# ---- Acid1 (W3C CSS1 test 5526c) ----
$acid1Dir = Join-Path $Vendor 'acid\acid1'
Fetch-File @(
    'https://www.w3.org/Style/CSS/Test/CSS1/current/test5526c.htm',
    'https://web.archive.org/web/2020id_/https://www.w3.org/Style/CSS/Test/CSS1/current/test5526c.htm'
) (Join-Path $acid1Dir 'test5526c.htm')

# ---- Acid2 (Web Standards Project) ----
$acid2Dir = Join-Path $Vendor 'acid\acid2'
Fetch-File @(
    'https://www.webstandards.org/files/acid2/test.html',
    'https://web.archive.org/web/2013id_/http://www.webstandards.org/files/acid2/test.html'
) (Join-Path $acid2Dir 'test.html')
Fetch-File @(
    'https://www.webstandards.org/files/acid2/reference.html',
    'https://web.archive.org/web/2013id_/http://www.webstandards.org/files/acid2/reference.html'
) (Join-Path $acid2Dir 'reference.html')

Write-Host ''
Write-Host 'Done. Resolved commits (must match Lite.Conformance\test-suites.lock.json):'
Write-Host "  WPT:     $wptResolved"
Write-Host "  test262: $t262Resolved"
