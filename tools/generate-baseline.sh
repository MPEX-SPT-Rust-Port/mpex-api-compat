#!/usr/bin/env bash
# Regenerates the entire MPEX baseline from the published 4.1.2 NuGet packages.
# Rerunning must produce a zero git diff.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# Local tools (dotnet genapi) are discovered from the CWD, not from $ROOT.
cd "$ROOT"

echo "== [1/4] Restoring 4.1.2 package closure"
dotnet publish "$ROOT/tools/BaselineClosure/BaselineClosure.csproj" -c Release \
    -o "$ROOT/tools/BaselineClosure/publish"

echo "== [2/4] Copying assemblies into baseline-dlls/"
mkdir -p "$ROOT/baseline-dlls"
rm -f "$ROOT/baseline-dlls"/*.dll
# Top-level DLLs only: skips SPT_Data content dirs and runtimes/ natives.
for dll in "$ROOT/tools/BaselineClosure/publish"/*.dll; do
    base="$(basename "$dll")"
    [ "$base" = "BaselineClosure.dll" ] && continue
    cp "$dll" "$ROOT/baseline-dlls/$base"
done

echo "== [3/4] Generating API surface listings"
# --configfile: tool restore resolves NuGet.config from the CWD, not from $ROOT.
dotnet tool restore --tool-manifest "$ROOT/.config/dotnet-tools.json" \
    --configfile "$ROOT/NuGet.config" >/dev/null

# GenAPI needs reference assemblies to resolve framework types.
NETCORE_REF=$(ls -d /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.*/ref/net10.0 | sort | tail -1)
ASPNET_REF=$(ls -d /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.*/ref/net10.0 | sort | tail -1)

mkdir -p "$ROOT/api-surface"
rm -f "$ROOT/api-surface"/*.cs
for a in SPTarkov.Common SPTarkov.DI SPTarkov.Reflection SPTarkov.Server.Assets SPTarkov.Server.Core SPTarkov.Server.Web; do
    echo "   genapi $a"
    dotnet genapi \
        --assembly "$ROOT/baseline-dlls/$a.dll" \
        --assembly-reference "$ROOT/baseline-dlls,$NETCORE_REF,$ASPNET_REF" \
        --output-path "$ROOT/api-surface"
done

echo "== [4/4] Dumping semantic inventory + README"
SPT_SRC="${SPT_SRC:-$HOME/git/TEMP/spt-412-src}"
dotnet run --project "$ROOT/tools/InventoryDumper" -c Release -- \
    --output "$ROOT/inventory" \
    --routes-source "$SPT_SRC" \
    --readme "$ROOT/README.md"

echo "OK: baseline regenerated"
