# Lite.QuickJs

Windows x64 native QuickJS bridge used by Lite. The project pins the QuickJS
2026-06-04 source archive by SHA-256. `build-quickjs.ps1` applies the numbered
patches in `native/patches` to a fresh source extraction and embeds the resulting
`litequickjs.dll` in the NuGet package under `runtimes/win-x64/native`.

The native bridge requires MinGW-w64 GCC. The build creates a `.build.json`
record beside the DLL with the archive, patch and build input fingerprint. The
bridge has no supported public API; use the Lite package for browser scripting.
