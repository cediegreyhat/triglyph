#!/usr/bin/env bash
# Builds TriGlyph.exe: self-contained, single file, no runtime install required.
set -euo pipefail
cd "$(dirname "$0")"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME/.dotnet}"

echo "== tests =="
dotnet test tests/TriGlyph.Tests/TriGlyph.Tests.csproj -c Release

echo "== publish win-x64 self-contained single file =="
dotnet publish src/TriGlyph.App/TriGlyph.App.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o dist

echo "== force GUI subsystem =="
# MSBuild applies this during a Windows build; off Windows it only emits NETSDK1074,
# leaving a console-subsystem apphost that would flash a console window on launch.
python3 tools/patch-subsystem.py dist/TriGlyph.exe

echo
echo "Output:"
ls -la dist/TriGlyph.exe
file dist/TriGlyph.exe
