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
    if [ -z "$REFS_DIR" ] || [ ! -d "$REFS_DIR" ] || [ -z "$(ls -A "$REFS_DIR")" ]; then
        # ApiCompat reports zero diagnostics when references don't resolve, which
        # looks exactly like success. Refuse to produce a meaningless pass — a
        # missing or typo'd refs path must fail as loudly as no refs path at all.
        echo "FATAL: the client contract requires a populated refs-dir (game-derived references)."
        echo "       got: '${REFS_DIR:-<none>}' — must be an existing, non-empty directory."
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

# Intentional breaks live next to the baseline they are scoped to, so the server and
# client contracts cannot suppress each other's diagnostics. One file per assembly:
# apicompat reports every suppression it did not use, so a file shared across the loop
# would cry "unnecessary suppression" on all the other assemblies and bury a genuinely
# stale entry. An assembly with no accepted breaks simply has no file.
SUPPRESSION_DIR="$(dirname "$BASELINE_DIR")/api-compat-suppressions"

fail=0
for a in "${ASSEMBLIES[@]}"; do
    echo "== apicompat: $a"
    if [ ! -f "$CANDIDATE_DIR/$a.dll" ]; then
        echo "MISSING: $CANDIDATE_DIR/$a.dll"
        fail=1
        continue
    fi
    SUPPRESSION_ARGS=()
    if [ -f "$SUPPRESSION_DIR/$a.xml" ]; then
        echo "   using suppressions: $SUPPRESSION_DIR/$a.xml"
        SUPPRESSION_ARGS=(--suppression-file "$SUPPRESSION_DIR/$a.xml")
    fi

    # left = contract (baseline), right = implementation (candidate).
    # cannot-change-parameter-name: source compatibility — mods call by named argument.
    dotnet apicompat --left "$BASELINE_DIR/$a.dll" --right "$CANDIDATE_DIR/$a.dll" \
        "${REF_ARGS[@]}" "${SUPPRESSION_ARGS[@]}" \
        --enable-rule-cannot-change-parameter-name || fail=1
done

if [ "$fail" -ne 0 ]; then
    echo "API COMPATIBILITY BROKEN: compiled 4.1.2 mods would no longer load. See errors above."
fi
exit $fail
