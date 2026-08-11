#!/usr/bin/env bash
# Regenerates the entire MPEX baseline from the published 4.1.2 NuGet packages.
# Rerunning must produce a zero git diff.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "== [1/2] Restoring 4.1.2 package closure"
dotnet publish "$ROOT/tools/BaselineClosure/BaselineClosure.csproj" -c Release \
    -o "$ROOT/tools/BaselineClosure/publish"

echo "== [2/2] Copying assemblies into baseline-dlls/"
mkdir -p "$ROOT/baseline-dlls"
rm -f "$ROOT/baseline-dlls"/*.dll
# Top-level DLLs only: skips SPT_Data content dirs and runtimes/ natives.
for dll in "$ROOT/tools/BaselineClosure/publish"/*.dll; do
    base="$(basename "$dll")"
    [ "$base" = "BaselineClosure.dll" ] && continue
    cp "$dll" "$ROOT/baseline-dlls/$base"
done

echo "OK: $(ls "$ROOT/baseline-dlls" | wc -l) assemblies in baseline-dlls/"
