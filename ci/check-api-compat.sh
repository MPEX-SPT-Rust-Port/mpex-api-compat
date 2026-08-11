#!/usr/bin/env bash
# Compares candidate (shim) assemblies against a frozen 4.1.2 baseline.
# Usage: check-api-compat.sh <candidate-dir> <baseline-dir> [refs-dir]
# The baseline directory determines which contract is checked (server or client).
# refs-dir supplies reference assemblies that ship in neither directory — the
# client contract requires it (game-derived references cannot be vendored).
# Exits non-zero if any contract assembly is missing or API-incompatible.
set -uo pipefail

CANDIDATE_DIR="${1:?usage: check-api-compat.sh <candidate-dir> <baseline-dir> [refs-dir]}"
BASELINE_DIR="${2:?usage: check-api-compat.sh <candidate-dir> <baseline-dir> [refs-dir]}"
REFS_DIR="${3:-}"

if [ -f "$BASELINE_DIR/spt-common.dll" ]; then
    ASSEMBLIES=(spt-common spt-core spt-custom spt-debugging spt-prepatch spt-reflection spt-singleplayer)
    if [ -z "$REFS_DIR" ]; then
        # ApiCompat reports zero diagnostics when references don't resolve, which
        # looks exactly like success. Refuse to produce a meaningless pass.
        echo "FATAL: the client contract requires a refs-dir (game-derived references)."
        echo "usage: check-api-compat.sh <candidate-dir> <baseline-dir> <refs-dir>"
        exit 1
    fi
else
    ASSEMBLIES=(SPTarkov.Common SPTarkov.DI SPTarkov.Reflection SPTarkov.Server.Assets SPTarkov.Server.Core SPTarkov.Server.Web)
fi

REF_ARGS=()
if [ -n "$REFS_DIR" ]; then
    REF_ARGS=(
        --left-assembly-references "$BASELINE_DIR,$REFS_DIR"
        --right-assembly-references "$CANDIDATE_DIR,$REFS_DIR"
    )
fi

fail=0
for a in "${ASSEMBLIES[@]}"; do
    echo "== apicompat: $a"
    if [ ! -f "$CANDIDATE_DIR/$a.dll" ]; then
        echo "MISSING: $CANDIDATE_DIR/$a.dll"
        fail=1
        continue
    fi
    # left = contract (baseline), right = implementation (candidate).
    # cannot-change-parameter-name: source compatibility — mods call by named argument.
    dotnet apicompat --left "$BASELINE_DIR/$a.dll" --right "$CANDIDATE_DIR/$a.dll" \
        "${REF_ARGS[@]}" \
        --enable-rule-cannot-change-parameter-name || fail=1
done

if [ "$fail" -ne 0 ]; then
    echo "API COMPATIBILITY BROKEN: compiled 4.1.2 mods would no longer load. See errors above."
fi
exit $fail
