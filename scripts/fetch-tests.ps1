# Fetches conformance test files into Lite.Conformance\vendor\ at pinned commits.
# Run from anywhere: paths are resolved relative to this script.
#
# Commits and sparse paths come from Lite.Conformance\test-suites.lock.json. To bump a suite,
# update that file; the resolved revisions printed below must match it exactly.
#
# -IncludeCss21Official vendors the official 23 March 2011 CSS 2.1 suite snapshot from the
# Internet Archive (the canonical host test.csswg.org is offline). Every file's SHA-1 is
# verified against the CDX capture digest; the resulting tree SHA-256 must be pinned in the
# lock entry before the vendored copy is trusted. CI passes the switch on every run because
# the profile gates require the directory; a vendored tree that already matches the pinned
# hash short-circuits the import without touching the network.

param(
    [switch]$IncludeCss21Official
)

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
        git clone --filter=blob:none --no-checkout --config core.autocrlf=false $Url $tmp
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

    # Pinned conformance sources must stay byte-identical to upstream. Windows Git's system
    # default core.autocrlf=true rewrites every LF file to CRLF at checkout, and test262
    # asserts verbatim source text (Function.prototype.toString line-terminator tests), so a
    # rewritten tree fails shards only where that default is active. test262's .gitattributes
    # does not forbid the rewrite (wpt's does), so pin the setting inside the vendor repo,
    # where it also survives the CI vendor cache.
    $effectiveAutocrlf = (git -C $Dest config --get core.autocrlf)
    git -C $Dest config core.autocrlf false
    if ($LASTEXITCODE -ne 0) { throw "cannot pin core.autocrlf=false in $Dest" }
    if ($effectiveAutocrlf -eq 'true') {
        # A tree fetched before this pin may already hold CRLF files, and checkout --force
        # skips files whose stat cache is clean. Clear the working tree so the sparse
        # checkout below re-materializes every file byte-exactly. Relocate .git out of the
        # tree first: the wipe can never touch it, and `git -C $Dest` can never fall through
        # to the enclosing project repository mid-operation.
        Write-Host "Re-materializing $Dest (previously fetched with core.autocrlf=$effectiveAutocrlf)"
        $destFull2 = [IO.Path]::GetFullPath($Dest)
        $backup = Join-Path (Split-Path -Parent $destFull2) ([IO.Path]::GetFileName($destFull2) + '._git_backup')
        if (Test-Path -LiteralPath $backup) { Remove-Item -Recurse -Force -LiteralPath $backup }
        Move-Item -LiteralPath (Join-Path $destFull2 '.git') -Destination $backup
        try {
            Remove-Item -Recurse -Force -LiteralPath $destFull2
            New-Item -ItemType Directory -Force $destFull2 | Out-Null
        } finally {
            Move-Item -LiteralPath $backup -Destination (Join-Path $destFull2 '.git')
        }
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

# ---- Official CSS 2.1 suite (23 March 2011) via the Internet Archive ----
# The canonical host (test.csswg.org) now redirects away, so the snapshot is vendored from
# web.archive.org captures. Integrity is verified per file against the CDX capture digest
# (base32 SHA-1 of the response body), and the whole tree is reduced to one SHA-256 that the
# lock entry must pin. Resume-safe: files already on disk with a matching digest are skipped.

function ConvertTo-Base32 {
    param([byte[]]$Bytes)
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $Bytes.Length; $i += 5) {
        $chunk = @($Bytes[$i])
        foreach ($offset in 1..4) { if ($i + $offset -lt $Bytes.Length) { $chunk += $Bytes[$i + $offset] } }
        $b0 = $chunk[0]
        $b1 = if ($chunk.Count -gt 1) { $chunk[1] } else { -1 }
        $b2 = if ($chunk.Count -gt 2) { $chunk[2] } else { -1 }
        $b3 = if ($chunk.Count -gt 3) { $chunk[3] } else { -1 }
        $b4 = if ($chunk.Count -gt 4) { $chunk[4] } else { -1 }
        [void]$sb.Append($alphabet[($b0 -shr 3) -band 31])
        [void]$sb.Append($alphabet[(($b0 -shl 2) -bor $(if ($b1 -ge 0) { $b1 -shr 6 } else { 0 })) -band 31])
        if ($b1 -ge 0) {
            [void]$sb.Append($alphabet[($b1 -shr 1) -band 31])
            [void]$sb.Append($alphabet[(($b1 -shl 4) -bor $(if ($b2 -ge 0) { $b2 -shr 4 } else { 0 })) -band 31])
            if ($b2 -ge 0) {
                [void]$sb.Append($alphabet[(($b2 -shl 1) -bor $(if ($b3 -ge 0) { $b3 -shr 7 } else { 0 })) -band 31])
                if ($b3 -ge 0) {
                    [void]$sb.Append($alphabet[($b3 -shr 2) -band 31])
                    [void]$sb.Append($alphabet[(($b3 -shl 3) -bor $(if ($b4 -ge 0) { $b4 -shr 5 } else { 0 })) -band 31])
                    if ($b4 -ge 0) { [void]$sb.Append($alphabet[$b4 -band 31]) }
                }
            }
        }
    }
    $sb.ToString()
}

function Get-FileBase32Sha1 {
    param([string]$Path)
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try { ConvertTo-Base32 ($sha1.ComputeHash([System.IO.File]::ReadAllBytes($Path))) }
    finally { $sha1.Dispose() }
}

function Get-Css21CdxRows {
    param([string]$DirectoryPrefix)
    # collapse=urlkey: one best capture per URL. The index is several megabytes for html4,
    # so allow generous time and retry transient Wayback failures. The last parsed index is
    # kept beside a .new candidate so a failed download never destroys a known-good copy.
    $name = "cdx-css21-" + $DirectoryPrefix.TrimEnd('/')
    $query = "https://web.archive.org/cdx/search/cdx?url=test.csswg.org%2Fsuites%2Fcss2.1%2F20110323%2F$DirectoryPrefix*&output=json&collapse=urlkey"
    $cached = Join-Path $env:TEMP "$name.json"
    $candidate = Join-Path $env:TEMP "$name.json.new"
    $delays = @(0, 10, 20, 40, 60, 90, 120, 180)
    foreach ($delay in $delays) {
        if ($delay -gt 0) { Start-Sleep -Seconds $delay }
        & curl.exe -s --max-time 420 $query -o $candidate
        try {
            $rows = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
            if ($rows.Count -gt 1) {
                # Serialize each row to "timestamp<TAB>original<TAB>digest" here, while the
                # JSON types are intact: nested arrays do not survive Start-Job serialization.
                $lines = @()
                foreach ($row in ($rows | Select-Object -Skip 1)) {
                    if ("$($row[4])" -eq '200' -and -not "$($row[2])".EndsWith('/')) {
                        $lines += "$($row[1])`t$($row[2])`t$($row[5])"
                    }
                }
                Move-Item -LiteralPath $candidate -Destination $cached -Force
                return ,$lines
            }
        } catch { }
    }
    if (Test-Path -LiteralPath $cached) {
        try {
            $rows = Get-Content -LiteralPath $cached -Raw | ConvertFrom-Json
            if ($rows.Count -gt 1) {
                Write-Warning "CDX query for $DirectoryPrefix keeps failing; using the last known good index."
                $lines = @()
                foreach ($row in ($rows | Select-Object -Skip 1)) {
                    if ("$($row[4])" -eq '200' -and -not "$($row[2])".EndsWith('/')) {
                        $lines += "$($row[1])`t$($row[2])`t$($row[5])"
                    }
                }
                return ,$lines
            }
        } catch { }
    }
    throw "Cannot read the Wayback CDX index for $DirectoryPrefix"
}

$Css21Worker = {
    param([int]$Bucket, [int]$WorkerCount, [string]$LinesFile, [string]$DestRoot, [string]$LogPath)
    # Reads its own slice from the shared TSV file: array parameters do not survive the
    # Start-Job boundary intact (they arrive stringified), so only scalars are passed.
    function ConvertTo-Base32Worker {
        param([byte[]]$Bytes)
        $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
        $sb = New-Object System.Text.StringBuilder
        for ($i = 0; $i -lt $Bytes.Length; $i += 5) {
            $chunk = @($Bytes[$i])
            foreach ($offset in 1..4) { if ($i + $offset -lt $Bytes.Length) { $chunk += $Bytes[$i + $offset] } }
            $b0 = $chunk[0]
            $b1 = if ($chunk.Count -gt 1) { $chunk[1] } else { -1 }
            $b2 = if ($chunk.Count -gt 2) { $chunk[2] } else { -1 }
            $b3 = if ($chunk.Count -gt 3) { $chunk[3] } else { -1 }
            $b4 = if ($chunk.Count -gt 4) { $chunk[4] } else { -1 }
            [void]$sb.Append($alphabet[($b0 -shr 3) -band 31])
            [void]$sb.Append($alphabet[(($b0 -shl 2) -bor $(if ($b1 -ge 0) { $b1 -shr 6 } else { 0 })) -band 31])
            if ($b1 -ge 0) {
                [void]$sb.Append($alphabet[($b1 -shr 1) -band 31])
                [void]$sb.Append($alphabet[(($b1 -shl 4) -bor $(if ($b2 -ge 0) { $b2 -shr 4 } else { 0 })) -band 31])
                if ($b2 -ge 0) {
                    [void]$sb.Append($alphabet[(($b2 -shl 1) -bor $(if ($b3 -ge 0) { $b3 -shr 7 } else { 0 })) -band 31])
                    if ($b3 -ge 0) {
                        [void]$sb.Append($alphabet[($b3 -shr 2) -band 31])
                        [void]$sb.Append($alphabet[(($b3 -shl 3) -bor $(if ($b4 -ge 0) { $b4 -shr 5 } else { 0 })) -band 31])
                        if ($b4 -ge 0) { [void]$sb.Append($alphabet[$b4 -band 31]) }
                    }
                }
            }
        }
        $sb.ToString()
    }
    function Get-FileBase32Sha1Worker {
        param([string]$Path)
        $sha1 = [System.Security.Cryptography.SHA1]::Create()
        try { ConvertTo-Base32Worker ($sha1.ComputeHash([System.IO.File]::ReadAllBytes($Path))) }
        finally { $sha1.Dispose() }
    }
    $allLines = [System.IO.File]::ReadAllLines($LinesFile)
    $failures = 0
    # One persistent HttpClient per worker: opening a fresh TLS connection per file (the
    # curl.exe-per-invocation approach) triggers connection-reset storms on web.archive.org.
    # Auto-redirect stays off so a dead capture's redirect fails fast instead of downloading
    # the wrong page and burning the digest comparison.
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    # GZip (1) + Deflate (2): assigned numerically because the enum type literal does not
    # resolve reliably in every job/session state; PowerShell coerces the integer.
    $handler.AutomaticDecompression = 3
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    for ($index = $Bucket; $index -lt $allLines.Length; $index += $WorkerCount) {
        $parts = "$($allLines[$index])" -split "`t"
        if ($parts.Count -ne 3) { $failures++; Add-Content -LiteralPath $LogPath "BADROW $($allLines[$index])"; continue }
        $timestamp = "$($parts[0])"
        $original = "$($parts[1])"
        $digest = "$($parts[2])"
        $path = [uri]::UnescapeDataString(([uri]$original).AbsolutePath)
        $marker = '/suites/css2.1/20110323/'
        $markIndex = $path.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase)
        if ($markIndex -lt 0) { $failures++; Add-Content -LiteralPath $LogPath "NOSUITEPATH $original"; continue }
        $relative = $path.Substring($markIndex + $marker.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
        if ($relative -eq '' -or $relative.Split([IO.Path]::DirectorySeparatorChar) -contains '..') {
            $failures++
            Add-Content -LiteralPath $LogPath "UNSAFEPATH $original"
            continue
        }
        $dest = Join-Path $DestRoot $relative
        # The Wayback index also captures directory URLs (e.g. ".../20110323/other" without
        # a trailing slash, status 200). They are not files to vendor: skip them without
        # counting a failure, and never let one be mistaken for a verified file.
        if (Test-Path -LiteralPath $dest -PathType Container) { Add-Content -LiteralPath $LogPath "SKIP-DIRECTORY $original"; continue }
        if ((Test-Path -LiteralPath $dest -PathType Leaf) -and ((Get-FileBase32Sha1Worker $dest) -eq $digest)) { continue }
        $directory = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
        $replay = "https://web.archive.org/web/${timestamp}id_/$original"
        $done = $false
        $tmp = "$dest.download"
        foreach ($delay in @(0, 3, 8, 20, 50)) {
            if ($delay -gt 0) { Start-Sleep -Seconds $delay }
            $code = 'ERR'
            try {
                $response = $client.GetAsync($replay).GetAwaiter().GetResult()
                $code = [int]$response.StatusCode
                if ($code -eq 200) {
                    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    [System.IO.File]::WriteAllBytes($tmp, $bytes)
                }
                $response.Dispose()
            } catch { }
            if ($code -eq 200 -and (Test-Path -LiteralPath $tmp) -and ((Get-FileBase32Sha1Worker $tmp) -eq $digest)) {
                Move-Item -LiteralPath $tmp -Destination $dest -Force
                $done = $true
                break
            }
            if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
            # Redirects and permanent errors mean the exact capture is unavailable; retrying
            # with long backoff only stalls the crawl (the nearest-snapshot redirect can
            # never match this capture's digest anyway).
            if ("$code" -in @('301', '302', '303', '307', '308', '404', '410')) { break }
        }
        if (-not $done) {
            $failures++
            Add-Content -LiteralPath $LogPath "FETCH $code $original"
        }
        # Pace each worker so the aggregate request rate stays below the archive's
        # connection limit; skipped (already verified) files impose no load. Sustained
        # higher rates trigger per-IP throttling that lasts far longer than the slowdown.
        Start-Sleep -Milliseconds (800 + (Get-Random -Minimum 0 -Maximum 400))
    }
    $client.Dispose()
    "bucket $Bucket failures $failures"
}

function Get-VendoredTreeSha256($DestRoot) {
    $entries = @(Get-ChildItem -LiteralPath $DestRoot -Recurse -File | Where-Object {
            $_.Name -ne 'fetch-failures.log' -and $_.Extension -ne '.download' } | ForEach-Object {
        $rel = $_.FullName.Substring($DestRoot.Length).TrimStart('\', '/').Replace('\', '/').ToLowerInvariant()
        [pscustomobject]@{ Rel = $rel; Full = $_.FullName; Length = $_.Length }
    })
    # Ordinal sort over the lowered forward-slash relative path, matching OfficialCatalog's
    # canonical order: PowerShell's default culture sort orders hyphens differently and
    # produced a different tree hash for the same bytes.
    $keys = @($entries | ForEach-Object { "$($_.Rel)|$($_.Full)" })
    [System.Array]::Sort($keys, [System.StringComparer]::Ordinal)
    $tree = New-Object System.Text.StringBuilder
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    foreach ($key in $keys) {
        $bar = $key.IndexOf('|')
        $relative = $key.Substring(0, $bar)
        $full = $key.Substring($bar + 1)
        [void]$tree.Append($relative).Append([char]0)
        [void]$tree.Append(([System.BitConverter]::ToString($sha256.ComputeHash([System.IO.File]::ReadAllBytes($full))) -replace '-', '').ToLowerInvariant()).Append([char]10)
    }
    $hash = ([System.BitConverter]::ToString($sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($tree.ToString()))) -replace '-', '').ToLowerInvariant()
    $sha256.Dispose()
    [pscustomobject]@{ TreeSha256 = $hash; FileCount = $entries.Count; Bytes = ($entries | Measure-Object -Property Length -Sum).Sum }
}

function Import-Css21OfficialSnapshot {
    $pin = $SuiteLock.suites | Where-Object { $_.id -eq 'css21-official-20110323' } | Select-Object -First 1
    if (-not $pin) { throw 'The suite lock has no css21-official-20110323 entry.' }
    $destRoot = Resolve-VendorDestination $pin.destination
    if (-not (Test-Path -LiteralPath $destRoot)) { New-Item -ItemType Directory -Force -Path $destRoot | Out-Null }

    # A vendored tree that still matches the pinned hash needs no network at all: skip the
    # Wayback CDX index and the crawl instead of re-verifying thousands of captures on
    # every CI run. Anything else falls through to a resumable refetch.
    if ($pin.PSObject.Properties['treeSha256']) {
        $existing = Get-VendoredTreeSha256 $destRoot
        if ($existing.TreeSha256 -eq $pin.treeSha256) {
            Write-Host "Official CSS 2.1 snapshot already matches the pinned treeSha256; nothing to fetch."
            return
        }
        Write-Host "Vendored official suite tree hash $($existing.TreeSha256) differs from the pinned $($pin.treeSha256); refetching."
    }

    # Variant availability: xhtml1-screen (and the noncanonical printer build) were captured
    # only sparsely by the Internet Archive and cannot be vendored from it. The lock records
    # that gap; the readiness gates keep the variant blocked until a source exists.
    $variantDirectories = [ordered]@{ 'html4-screen' = 'html4'; 'other-formats-screen' = 'other' }
    $allLines = @()
    foreach ($entry in $variantDirectories.GetEnumerator()) {
        Write-Host "Indexing archived $($entry.Value)/ tests for $($entry.Key)..."
        $lines = Get-Css21CdxRows $entry.Value
        Write-Host "  $($lines.Count) archived files for $($entry.Key)"
        $allLines += $lines
    }

    $workerCount = 2
    $linesFile = Join-Path $env:TEMP 'css21-official-cdx.tsv'
    [System.IO.File]::WriteAllLines($linesFile, [string[]]$allLines)

    $logPath = Join-Path $destRoot 'fetch-failures.log'
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
    $jobs = @()
    for ($w = 0; $w -lt $workerCount; $w++) {
        $jobs += Start-Job -ScriptBlock $Css21Worker -ArgumentList $w, $workerCount, $linesFile, $destRoot, $logPath
    }
    $target = $allLines.Count
    try {
        while ($jobs | Where-Object { $_.State -eq 'Running' }) {
            Start-Sleep -Seconds 30
            $done = @(Get-ChildItem -LiteralPath $destRoot -Recurse -File | Where-Object { $_.Name -ne 'fetch-failures.log' -and $_.Name -ne 'fetch-failures.log.download' }).Count
            Write-Host ("  progress: {0}/{1} files" -f $done, $target)
        }
    } finally {
        $jobs | Where-Object { $_.State -eq 'Running' } | Stop-Job
    }
    foreach ($job in $jobs) { Receive-Job -Job $job | Write-Host; $job | Remove-Job }
    # SKIP-DIRECTORY rows are index artifacts, not fetch failures; everything else logged
    # (FETCH, BADROW, NOSUITEPATH, UNSAFEPATH) must be retried before pinning.
    $failures = @(if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath | Where-Object { $_ -and $_ -notlike 'SKIP-DIRECTORY *' } })
    if ($failures.Count -gt 0) {
        Write-Warning "$($failures.Count) files could not be fetched or verified; re-run to retry. Samples:"
        $failures | Select-Object -First 10 | ForEach-Object { Write-Warning "  $_" }
    }

    $verified = Get-VendoredTreeSha256 $destRoot
    $treeSha256 = $verified.TreeSha256
    $bytes = $verified.Bytes
    Write-Host ''
    Write-Host "Official CSS 2.1 snapshot: $($verified.FileCount) files, $bytes bytes"
    Write-Host "  treeSha256 = $treeSha256"
    if ($pin.PSObject.Properties['treeSha256']) {
        if ($pin.treeSha256 -ne $treeSha256) {
            throw "Vendored official suite tree hash $treeSha256 differs from the pinned $($pin.treeSha256)."
        }
        Write-Host '  matches the pinned treeSha256.'
    } else {
        Write-Host '  NOT yet pinned: record treeSha256 in Lite.Conformance\test-suites.lock.json before trusting the tree.'
    }
    if ($failures.Count -gt 0) { throw 'The official suite fetch had failures; fix or re-run before pinning.' }
}

if ($IncludeCss21Official) { Import-Css21OfficialSnapshot }
