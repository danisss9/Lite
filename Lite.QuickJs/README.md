# Lite.QuickJs

Native QuickJS bridge used by Lite, for Windows x64 and Linux. The project pins
the QuickJS 2026-06-04 source archive by SHA-256 and applies the numbered patches
in `native/patches` to a fresh source extraction:

- `build-quickjs.ps1` (Windows, MinGW-w64 GCC) builds `litequickjs.dll` into
  `runtimes/win-x64/native`.
- `build-quickjs.sh` (Linux, `gcc`) builds `liblitequickjs.so` into
  `runtimes/linux-<arch>/native`.

The project runs the script for the host OS before compiling. A NuGet package
built on one OS also carries the other OS's library when it is already present in
`runtimes/` (the release workflow builds the Linux library first). Each build
creates a `.build.json` record beside the library with the archive, patch and
build input fingerprint. The bridge has no supported public API; use the Lite
package for browser scripting.
