#!/usr/bin/env bash
# Regenerates the entire MPEX baseline from the published 4.1.2 NuGet packages.
# Rerunning must produce a zero git diff.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# Local tools (dotnet genapi) are discovered from the CWD, not from $ROOT.
cd "$ROOT"

echo "== [1/4] Restoring 4.1.2 package closure"
dotnet publish "$ROOT/tools/SrvBaselineClosure/SrvBaselineClosure.csproj" -c Release \
    -o "$ROOT/tools/SrvBaselineClosure/publish"

echo "== [2/4] Copying assemblies into server/baseline-dlls/"
mkdir -p "$ROOT/server/baseline-dlls"
rm -f "$ROOT/server/baseline-dlls"/*.dll
# Top-level DLLs only: skips SPT_Data content dirs and runtimes/ natives.
for dll in "$ROOT/tools/SrvBaselineClosure/publish"/*.dll; do
    base="$(basename "$dll")"
    [ "$base" = "SrvBaselineClosure.dll" ] && continue
    cp "$dll" "$ROOT/server/baseline-dlls/$base"
done
# Normalize mode: the publish output is 755 on some machines, 644 on others.
chmod 644 "$ROOT/server/baseline-dlls"/*.dll

echo "== [3/4] Generating API surface listings"
# --configfile: tool restore resolves NuGet.config from the CWD, not from $ROOT.
dotnet tool restore --tool-manifest "$ROOT/.config/dotnet-tools.json" \
    --configfile "$ROOT/NuGet.config" >/dev/null

# GenAPI needs reference assemblies to resolve framework types.
NETCORE_REF=$(ls -d /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.*/ref/net10.0 | sort -V | tail -1)
ASPNET_REF=$(ls -d /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.*/ref/net10.0 | sort -V | tail -1)

mkdir -p "$ROOT/server/api-surface"
rm -f "$ROOT/server/api-surface"/*.cs
for a in SPTarkov.Common SPTarkov.DI SPTarkov.Reflection SPTarkov.Server.Assets SPTarkov.Server.Core SPTarkov.Server.Web; do
    echo "   genapi $a"
    dotnet genapi \
        --assembly "$ROOT/server/baseline-dlls/$a.dll" \
        --assembly-reference "$ROOT/server/baseline-dlls,$NETCORE_REF,$ASPNET_REF" \
        --output-path "$ROOT/server/api-surface"
done

echo "== [4/4] Dumping semantic inventory + README"
SPT_SRC="${SPT_SRC:-$HOME/git/TEMP/spt-412-src}"
dotnet run --project "$ROOT/tools/SrvInventoryDumper" -c Release -- \
    --output "$ROOT/server/inventory" \
    --routes-source "$SPT_SRC" \
    --readme "$ROOT/README.md"

echo "OK: baseline regenerated"
