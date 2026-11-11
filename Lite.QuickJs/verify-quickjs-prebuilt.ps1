<#
Verifies a prebuilt Windows x64 QuickJS bridge against the committed build record, for
machines without MinGW-w64 gcc. Opt-in only: Lite.QuickJs.csproj runs this script instead of
build-quickjs.ps1 when the build sets LiteQuickJsPrebuilt=true.

The script recomputes build-quickjs.ps1's input fingerprint from the checked-out sources
(bridge, LF-normalized patches, build script, pinned archive hash, flags) and the compiler
recorded in the stamp, then requires that

  - the fingerprint equals the stamp's inputSha256 (the dll was built from exactly these
    sources, patches, flags and build script), and
  - the dll's SHA-256 equals the stamp's nativeDllSha256 (it is that exact binary).

Any mismatch fails the build. The compiler identity is the one recorded by the build that
produced the stamp; it is taken from the stamp, not re-derived.

The dll itself is not tracked by git. -PrebuiltSource copies a dll (for example the main
checkout's runtimes/win-x64/native/litequickjs.dll into a fresh worktree) to the output path;
the source is verified first and copied only when every check passes.

Keep the fingerprint formula in lockstep with build-quickjs.ps1. A drift can only make the
verification fail, never pass a dll built from different inputs. Do not edit
build-quickjs.ps1 to share code with this script: its own hash is a fingerprint input, so
any edit invalidates every existing stamp and requires a rebuild with gcc.
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'runtimes/win-x64/native/litequickjs.dll'),
    [string]$PrebuiltSource = ''
)

$ErrorActionPreference = 'Stop'
function Get-Sha256Bytes([byte[]]$Bytes) {
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($Bytes)).Replace('-', '') }
    finally { $hasher.Dispose() }
}
function Get-Sha256File([string]$Path) { return Get-Sha256Bytes ([IO.File]::ReadAllBytes($Path)) }
function Fail([string]$Message) {
    throw "QuickJS prebuilt verification failed: $Message Install MinGW-w64 gcc and build without LiteQuickJsPrebuilt to produce a fresh bridge (docs/es2020-conformance.md, Reproduce)."
}

$buildScript = Join-Path $PSScriptRoot 'build-quickjs.ps1'
$bridge = Join-Path $PSScriptRoot 'native/litequickjs.c'
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$stamp = "$resolvedOutput.build.json"

# Single source of truth for the pinned constants: read them from the build script.
$buildText = [IO.File]::ReadAllText($buildScript)
$version = [regex]::Match($buildText, "(?m)^\`$version = '([^']+)'").Groups[1].Value
$archiveHash = [regex]::Match($buildText, "(?m)^\`$expectedHash = '([0-9A-Fa-f]{64})'").Groups[1].Value
$flags = [regex]::Match($buildText, "(?m)^\`$flags = '([^']+)'").Groups[1].Value
if (-not $version -or -not $archiveHash -or -not $flags) {
    Fail 'could not read $version, $expectedHash and $flags from build-quickjs.ps1; update this script to match it.'
}

# The candidate is verified where it is and copied into place only after every check passes.
$candidate = if ($PrebuiltSource) { [IO.Path]::GetFullPath($PrebuiltSource) } else { $resolvedOutput }
if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
    if ($PrebuiltSource) { Fail "prebuilt source $candidate does not exist." }
    Fail "no prebuilt bridge at $resolvedOutput (the dll is not tracked by git; pass LiteQuickJsPrebuiltSource=<path to a litequickjs.dll>)."
}
if (-not (Test-Path -LiteralPath $stamp -PathType Leaf)) { Fail "no build record at $stamp." }
$record = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json
if (-not $record.compilerVersion -or -not $record.compilerTarget -or -not $record.inputSha256 -or -not $record.nativeDllSha256) {
    Fail "the build record $stamp lacks compiler, input or dll hashes."
}
if ($record.version -ne $version) { Fail "the build record is for QuickJS $($record.version), the sources pin $version." }
if ($record.archiveSha256 -ne $archiveHash.ToLowerInvariant()) { Fail 'the build record names a different source archive.' }

# Same patch selection, order and LF normalization as build-quickjs.ps1.
$patches = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'native/patches') -Filter '*.patch' -File | Sort-Object Name)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$inputs = @("archive:$archiveHash", "compiler:$($record.compilerVersion)/$($record.compilerTarget)", "flags:$flags",
    "bridge:$(Get-Sha256File $bridge)",
    "script:$(Get-Sha256File $buildScript)")
foreach ($patch in $patches) {
    $normalized = [IO.File]::ReadAllText($patch.FullName) -replace "`r`n", "`n"
    $inputs += "patch:$($patch.Name):$(Get-Sha256Bytes $utf8.GetBytes($normalized))"
}
$fingerprint = (Get-Sha256Bytes ([Text.Encoding]::UTF8.GetBytes(($inputs -join "`n")))).ToLowerInvariant()
if ($record.inputSha256 -ne $fingerprint) {
    Fail "the sources fingerprint to $fingerprint but the build record says $($record.inputSha256): litequickjs.c, a patch or build-quickjs.ps1 changed since the bridge was built (or this checkout's line endings differ from the building checkout's)."
}
$dllHash = Get-Sha256File $candidate
if ($dllHash -ne $record.nativeDllSha256.ToUpperInvariant()) {
    Fail "$candidate hashes to $dllHash but the build record says $($record.nativeDllSha256)."
}
if ($candidate -ne $resolvedOutput) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
    Copy-Item -LiteralPath $candidate -Destination $resolvedOutput -Force
    if ((Get-Sha256File $resolvedOutput) -ne $dllHash) { Fail "copying $candidate to $resolvedOutput changed its bytes." }
}
Write-Host "QuickJS native bridge: verified prebuilt $resolvedOutput ($fingerprint, $($record.compilerVersion)/$($record.compilerTarget))"
