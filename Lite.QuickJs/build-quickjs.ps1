param([string]$OutputPath = (Join-Path $PSScriptRoot 'runtimes/win-x64/native/litequickjs.dll'))

$ErrorActionPreference = 'Stop'
function Get-Sha256File([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally {
        $hasher.Dispose()
        $stream.Dispose()
    }
}

$version = '2026-06-04'
$expectedHash = 'B376E839B322978313D929FD20663B11BA58B75DF5A46C126DD19EA2FA70AD2A'
$cache = Join-Path $PSScriptRoot 'obj/quickjs'
$archive = Join-Path $cache "quickjs-$version.tar.xz"
$bridge = Join-Path $PSScriptRoot 'native/litequickjs.c'
$patches = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'native/patches') -Filter '*.patch' -File | Sort-Object Name)
$gccCommand = Get-Command gcc.exe -ErrorAction SilentlyContinue
$gcc = if ($gccCommand) { $gccCommand.Source } else { $null }
if (-not $gcc) {
    foreach ($candidate in @('C:\msys64\mingw64\bin\gcc.exe', 'C:\tools\msys64\mingw64\bin\gcc.exe')) {
        if (Test-Path -LiteralPath $candidate) { $gcc = $candidate; break }
    }
}
if (-not $gcc) { throw 'MinGW-w64 gcc is required to build the Windows x64 QuickJS bridge.' }

New-Item -ItemType Directory -Force -Path $cache | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://bellard.org/quickjs/quickjs-$version.tar.xz" -OutFile $archive
}
$actualHash = Get-Sha256File $archive
if ($actualHash -ne $expectedHash) { throw "QuickJS source hash mismatch: $actualHash" }

$compilerDirectory = Split-Path -Parent $gcc
$env:PATH = "$compilerDirectory;$env:PATH"
$compilerVersion = (& $gcc -dumpfullversion).Trim()
$compilerTarget = (& $gcc -dumpmachine).Trim()
if ($LASTEXITCODE -ne 0 -or $compilerTarget -notmatch 'x86_64.*mingw') {
    throw "The native bridge requires a Windows x64 compiler; got $compilerTarget"
}
$flags = '-std=gnu11 -O2 -shared -D_GNU_SOURCE -static -lm'
$inputs = @("archive:$actualHash", "compiler:$compilerVersion/$compilerTarget", "flags:$flags",
    "bridge:$(Get-Sha256File $bridge)",
    "script:$(Get-Sha256File $PSCommandPath)")
foreach ($patch in $patches) {
    $inputs += "patch:$($patch.Name):$(Get-Sha256File $patch.FullName)"
}
$bytes = [Text.Encoding]::UTF8.GetBytes(($inputs -join "`n"))
$hasher = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
finally { $hasher.Dispose() }
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$stamp = "$resolvedOutput.build.json"
if ((Test-Path -LiteralPath $resolvedOutput) -and (Test-Path -LiteralPath $stamp)) {
    $previous = Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json
    if ($previous.inputSha256 -eq $fingerprint -and
        $previous.nativeDllSha256 -eq (Get-Sha256File $resolvedOutput)) {
        Write-Host "QuickJS native bridge is current ($fingerprint)"
        return
    }
}

$patchedRoot = Join-Path $cache "patched-$fingerprint"
$source = Join-Path $patchedRoot "quickjs-$version"
$ready = Join-Path $patchedRoot 'patches-applied.txt'
if (-not (Test-Path -LiteralPath $ready)) {
    if (Test-Path -LiteralPath $patchedRoot) {
        $absoluteCache = [IO.Path]::GetFullPath($cache).TrimEnd('\') + '\'
        $absoluteTarget = [IO.Path]::GetFullPath($patchedRoot)
        if (-not $absoluteTarget.StartsWith($absoluteCache, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear a patch directory outside the QuickJS cache: $absoluteTarget"
        }
        Remove-Item -LiteralPath $absoluteTarget -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $patchedRoot | Out-Null
    & tar.exe -xf $archive -C $patchedRoot
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $source 'quickjs.h'))) {
        throw 'QuickJS source extraction failed.'
    }
    Push-Location $source
    try {
        foreach ($patch in $patches) {
            & git apply --check -- $patch.FullName
            if ($LASTEXITCODE -ne 0) { throw "QuickJS patch does not apply cleanly: $($patch.Name)" }
            & git apply -- $patch.FullName
            if ($LASTEXITCODE -ne 0) { throw "QuickJS patch failed: $($patch.Name)" }
        }
    }
    finally { Pop-Location }
    Set-Content -LiteralPath $ready -Value $fingerprint -Encoding ascii
}

$config = Join-Path $patchedRoot 'litequickjs-config.h'
Set-Content -LiteralPath $config -Value "#define CONFIG_VERSION `"$version`"" -Encoding ascii
$temporaryOutput = Join-Path $patchedRoot 'litequickjs.dll'
$sources = @($bridge) + @('quickjs.c', 'cutils.c', 'dtoa.c', 'libregexp.c', 'libunicode.c' |
    ForEach-Object { Join-Path $source $_ })
& $gcc -std=gnu11 -O2 -shared -include $config -D_GNU_SOURCE -I $source @sources -o $temporaryOutput -static -lm
if ($LASTEXITCODE -ne 0) { throw "QuickJS native build failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
Copy-Item -LiteralPath $temporaryOutput -Destination $resolvedOutput -Force
$metadata = [ordered]@{
    version = $version; archiveSha256 = $actualHash.ToLowerInvariant(); inputSha256 = $fingerprint
    nativeDllSha256 = Get-Sha256File $resolvedOutput
    compilerVersion = $compilerVersion; compilerTarget = $compilerTarget
    patches = @($patches | ForEach-Object Name)
}
$metadata | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stamp -Encoding utf8
Write-Host "Built $resolvedOutput from patched QuickJS $version ($fingerprint)"
