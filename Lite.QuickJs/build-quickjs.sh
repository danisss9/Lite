#!/usr/bin/env bash
# Linux counterpart of build-quickjs.ps1: builds the patched QuickJS bridge as
# runtimes/linux-<arch>/native/liblitequickjs.so with the same pinned archive, patches
# and fingerprinted .build.json record.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
case "$(uname -m)" in
    x86_64|amd64) arch=x64 ;;
    aarch64|arm64) arch=arm64 ;;
    *) arch="$(uname -m)" ;;
esac
output_path="$script_dir/runtimes/linux-$arch/native/liblitequickjs.so"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --output-path|-OutputPath) output_path="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

sha256_file() { sha256sum "$1" | cut -d' ' -f1; }

version='2026-06-04'
expected_hash='b376e839b322978313d929fd20663b11ba58b75df5a46c126dd19ea2fa70ad2a'
cache="$script_dir/obj/quickjs"
archive="$cache/quickjs-$version.tar.xz"
bridge="$script_dir/native/litequickjs.c"
cc="${CC:-gcc}"

command -v "$cc" >/dev/null || { echo "A C compiler ($cc) is required to build the Linux QuickJS bridge." >&2; exit 1; }
command -v git >/dev/null || { echo "git is required to apply the QuickJS patches." >&2; exit 1; }

# git apply matches context lines byte-for-byte and the QuickJS sources use LF endings;
# normalize the patches so a CRLF checkout hashes and applies identically.
mkdir -p "$cache/patches-lf"
patches=()
for patch in "$script_dir"/native/patches/*.patch; do
    normalized="$cache/patches-lf/$(basename "$patch")"
    tr -d '\r' < "$patch" > "$normalized"
    patches+=("$normalized")
done

if [[ ! -f "$archive" ]]; then
    curl -fsSL "https://bellard.org/quickjs/quickjs-$version.tar.xz" -o "$archive.tmp"
    mv "$archive.tmp" "$archive"
fi
actual_hash="$(sha256_file "$archive")"
if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "QuickJS source hash mismatch: $actual_hash" >&2
    exit 1
fi

compiler_version="$("$cc" -dumpfullversion -dumpversion | head -n1)"
compiler_target="$("$cc" -dumpmachine)"
if [[ "$compiler_target" != *linux* ]]; then
    echo "The native bridge requires a Linux compiler; got $compiler_target" >&2
    exit 1
fi
flags='-std=gnu11 -O2 -shared -fPIC -D_GNU_SOURCE -lm -lpthread -static-libgcc'
inputs="archive:$actual_hash"$'\n'"compiler:$compiler_version/$compiler_target"$'\n'"flags:$flags"
inputs+=$'\n'"bridge:$(sha256_file "$bridge")"$'\n'"script:$(sha256_file "${BASH_SOURCE[0]}")"
for patch in "${patches[@]}"; do
    inputs+=$'\n'"patch:$(basename "$patch"):$(sha256_file "$patch")"
done
fingerprint="$(printf '%s' "$inputs" | sha256sum | cut -d' ' -f1)"

mkdir -p "$(dirname "$output_path")"
resolved_output="$(cd "$(dirname "$output_path")" && pwd)/$(basename "$output_path")"
stamp="$resolved_output.build.json"
if [[ -f "$resolved_output" && -f "$stamp" ]] &&
   grep -q "\"inputSha256\": *\"$fingerprint\"" "$stamp" &&
   grep -qi "\"nativeDllSha256\": *\"$(sha256_file "$resolved_output")\"" "$stamp"; then
    echo "QuickJS native bridge is current ($fingerprint)"
    exit 0
fi

patched_root="$cache/patched-$fingerprint"
source_dir="$patched_root/quickjs-$version"
ready="$patched_root/patches-applied.txt"
if [[ ! -f "$ready" ]]; then
    rm -rf -- "$patched_root"
    mkdir -p "$patched_root"
    tar -xf "$archive" -C "$patched_root"
    [[ -f "$source_dir/quickjs.h" ]] || { echo "QuickJS source extraction failed." >&2; exit 1; }
    for patch in "${patches[@]}"; do
        git -C "$source_dir" apply --check -- "$patch" ||
            { echo "QuickJS patch does not apply cleanly: $(basename "$patch")" >&2; exit 1; }
        git -C "$source_dir" apply -- "$patch"
    done
    printf '%s\n' "$fingerprint" > "$ready"
fi

config="$patched_root/litequickjs-config.h"
printf '#define CONFIG_VERSION "%s"\n' "$version" > "$config"
temporary_output="$patched_root/liblitequickjs.so"
# shellcheck disable=SC2086 # $flags is a deliberate word list
"$cc" -include "$config" -I "$source_dir" "$bridge" \
    "$source_dir/quickjs.c" "$source_dir/cutils.c" "$source_dir/dtoa.c" \
    "$source_dir/libregexp.c" "$source_dir/libunicode.c" \
    -o "$temporary_output" $flags

cp -f "$temporary_output" "$resolved_output"
patch_names=""
for patch in "${patches[@]}"; do
    patch_names+="${patch_names:+,}"$'\n'"        \"$(basename "$patch")\""
done
cat > "$stamp" <<JSON
{
    "version": "$version",
    "archiveSha256": "$actual_hash",
    "inputSha256": "$fingerprint",
    "nativeDllSha256": "$(sha256_file "$resolved_output")",
    "compilerVersion": "$compiler_version",
    "compilerTarget": "$compiler_target",
    "patches": [$patch_names
    ]
}
JSON
echo "Built $resolved_output from patched QuickJS $version ($fingerprint)"
