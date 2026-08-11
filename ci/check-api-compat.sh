#!/usr/bin/env bash
# Compares candidate (shim) assemblies against the frozen 4.1.2 baseline.
# Usage: check-api-compat.sh <candidate-dir> <baseline-dir>
# Exits non-zero if any contract assembly is missing or API-incompatible.
set -uo pipefail

CANDIDATE_DIR="${1:?usage: check-api-compat.sh <candidate-dir> <baseline-dir>}"
BASELINE_DIR="${2:?usage: check-api-compat.sh <candidate-dir> <baseline-dir>}"

ASSEMBLIES=(SPTarkov.Common SPTarkov.DI SPTarkov.Reflection SPTarkov.Server.Assets SPTarkov.Server.Core SPTarkov.Server.Web)
fail=0
for a in "${ASSEMBLIES[@]}"; do
    echo "== apicompat: $a"
    if [ ! -f "$CANDIDATE_DIR/$a.dll" ]; then
        echo "MISSING: $CANDIDATE_DIR/$a.dll"
        fail=1
        continue
    fi
    # left = contract (baseline), right = implementation (candidate).
    dotnet apicompat --left "$BASELINE_DIR/$a.dll" --right "$CANDIDATE_DIR/$a.dll" || fail=1
done

if [ "$fail" -ne 0 ]; then
    echo "API COMPATIBILITY BROKEN: compiled 4.1.2 mods would no longer load. See errors above."
fi
exit $fail
