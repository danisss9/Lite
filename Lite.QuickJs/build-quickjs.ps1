param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'runtimes/win-x64/native/litequickjs.dll')
)

$ErrorActionPreference = 'Stop'
$version = '2026-06-04'
$expectedHash = 'B376E839B322978313D929FD20663B11BA58B75DF5A46C126DD19EA2FA70AD2A'
$cache = Join-Path $PSScriptRoot 'obj/quickjs'
$archive = Join-Path $cache "quickjs-$version.tar.xz"
$source = Join-Path $cache "quickjs-$version"
$config = Join-Path $cache 'litequickjs-config.h'
$bridge = Join-Path $PSScriptRoot 'native/litequickjs.c'
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
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
if ($actualHash -ne $expectedHash) { throw "QuickJS source hash mismatch: $actualHash" }
if (-not (Test-Path -LiteralPath (Join-Path $source 'quickjs.h'))) {
    & tar.exe -xf $archive -C $cache
    if ($LASTEXITCODE -ne 0) { throw 'QuickJS source extraction failed.' }
}
Set-Content -LiteralPath $config -Value '#define CONFIG_VERSION "2026-06-04"' -Encoding ascii

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
$sources = @($bridge) + @('quickjs.c', 'cutils.c', 'dtoa.c', 'libregexp.c', 'libunicode.c' |
    ForEach-Object { Join-Path $source $_ })
$compilerDirectory = Split-Path -Parent $gcc
$env:PATH = "$compilerDirectory;$env:PATH"
& $gcc -std=gnu11 -O2 -shared -include $config -D_GNU_SOURCE -I $source `
    @sources -o $resolvedOutput -static -lm
if ($LASTEXITCODE -ne 0) { throw "QuickJS native build failed with exit code $LASTEXITCODE" }
Write-Host "Built $resolvedOutput from QuickJS $version ($expectedHash)"
