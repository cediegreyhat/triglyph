# Building

## Requirements

- .NET 6 SDK
- Python 3 (only for the PE subsystem patch, see below)
- On Linux: `EnableWindowsTargeting` is already set in the project files

## Build

```bash
./build.sh
```

Runs the tests, publishes `win-x64` self-contained single-file, and patches the PE
subsystem. Output: `dist/TriGlyph.exe` (~146 MB) plus `dist/themes/`.

## Building on Linux

This repository is developed on Linux, which works, with one wrinkle worth knowing.

MSBuild rewrites the apphost's subsystem, icon, manifest and version resource during a
**Windows** build. Off Windows it only emits:

```
warning NETSDK1074: The application host executable will not be customized
```

and skips the step entirely. The raw apphost from the runtime pack is **console**
subsystem, so a GUI app built this way would flash a console window on launch.

`tools/patch-subsystem.py` fixes that by flipping two bytes in the PE optional header
after publish (subsystem lives at `e_lfanew + 24 + 68`). The build script runs it
automatically. Verify with `file dist/TriGlyph.exe` - it should say `(GUI)`.

What this cannot fix: **no icon or version resource** can be embedded off Windows.
If those matter, build on Windows.

## Building on Windows

Just:

```bash
dotnet publish src/TriGlyph.App/TriGlyph.App.csproj -c Release -r win-x64 ^
    --self-contained true -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

No subsystem patch is needed - MSBuild handles it, and the icon and version resource
are applied too.

## Tests

```bash
dotnet test tests/TriGlyph.Tests/TriGlyph.Tests.csproj
```

102 tests, all runnable on Linux. See `docs/ARCHITECTURE.md` for why they all live in
`TriGlyph.Core`.

## CI

`.github/workflows/ci.yml` runs the test suite on Ubuntu and a full build plus publish
on Windows, uploading the artifact. The Windows job is the only automated check that
the GUI project actually compiles against the real Windows desktop SDK.

## Disk space

A self-contained WPF publish is ~150 MB and the NuGet cache is several hundred MB. On
a machine where `/tmp` is a small tmpfs this fails with `No space left on device` from
`NuGet.targets`. Build under `$HOME` and set `DOTNET_CLI_HOME` there too.
