# Adopting the mod-API compatibility gate

The contract: compiled SPT 4.1.2 C# mods must keep loading. Any change to the
public surface of the six server contract assemblies (SPTarkov.Common,
SPTarkov.DI, SPTarkov.Reflection, SPTarkov.Server.Assets, SPTarkov.Server.Core,
SPTarkov.Server.Web) or the seven client contract assemblies (spt-common,
spt-core, spt-custom, spt-debugging, spt-prepatch, spt-reflection,
spt-singleplayer) can break that. This gate makes such breaks fail PRs.

`check-api-compat.sh <candidate-dir> <baseline-dir> [refs-dir]` picks the
contract from the baseline directory's contents, so one script covers both.

## Setup in a consuming repo

1. Copy `server/baseline-dlls/` and/or `client/baseline-dlls/` from mpex-api-compat
   into your repo (e.g. as `./baseline-dlls` and `./client-baseline-dlls`) —
   whichever contracts you gate. The DLLs are frozen at 4.1.2 forever — copies
   cannot drift.
2. Copy `ci/check-api-compat.sh`.
3. Add the ApiCompat tool to your tool manifest:
   `dotnet new tool-manifest` (if you have none), then
   `dotnet tool install Microsoft.DotNet.ApiCompat.Tool`.
4. Add a PR workflow modeled on `ci/github-workflow-example.yml`: build your
   C#-facing shim assemblies, then run
   `./ci/check-api-compat.sh <your-shim-output-dir> ./baseline-dlls`.

Point the gate at a directory containing the shims **plus their dependency
closure** (or pass `--right-assembly-references <dir>`). ApiCompat does not warn
when it cannot resolve a candidate's references — an assembly compared in an
otherwise empty directory yields a clean pass with zero diagnostics, which looks
exactly like success.

## Client contract

The client baseline works the same way with one extra requirement: the seven
client assemblies (spt-common, spt-core, spt-custom, spt-debugging,
spt-prepatch, spt-reflection, spt-singleplayer) reference game-derived
assemblies that cannot be vendored. Provide them as a third argument:

    ./ci/check-api-compat.sh <your-client-shim-dir> ./client-baseline-dlls <refs-dir>

Populate <refs-dir> from a live SPT 4.1.2 install exactly as described in
mpex-api-compat's `client/refs/README.md`. The script selects the client
assembly list automatically (it looks for spt-common.dll in the baseline dir)
and refuses to run the client contract unless the refs argument is given and
names an existing, non-empty directory (a typo'd path fails loudly rather than
resolving nothing) — with no refs argument at
all ApiCompat resolves nothing, says nothing about it, and silently skips
whatever it could not resolve, so a clean pass with zero diagnostics carries no
guarantee about actual compatibility.

Once a refs directory is passed, ApiCompat prints a loud
`Could not resolve reference '<name>.dll' ...` line for every reference it still
cannot find. Those lines are warnings — they do **not** fail the run — so treat
any of them as an incomplete refs directory and fix it before trusting a pass.
A correctly populated `client/refs/` produces none.

Client shims must also be stamped `AssemblyVersion 4.1.2.0` — the client
behavioral suite asserts it as the contract's freeze marker.

## Rules

`--enable-rule-cannot-change-parameter-name` is on: mods call by named argument,
so a renamed parameter is a source break even though it is binary-compatible.
`--enable-rule-attributes-must-match` remains available as an opt-in if attribute
drift ever needs to fail the build.

`--strict-mode` is deliberately off. A shim may ADD public API; it may not remove
or change any.

## Reading failures

Each incompatibility prints as a `CP****` diagnostic naming the exact member
(e.g. removed member, changed signature, tightened accessibility). Fix the shim.
If a difference is genuinely intended (rare — it breaks mods), suppress it
explicitly with `--generate-suppression-file` and commit the suppression file
so the decision is reviewable.

## What this does NOT check

Behavior. A shim can be binary-compatible and still behave differently.
Behavioral coverage lives in `tests/SrvBehavioralTests/` — point
`$(MpexAssemblyDir)` at your shim output directory and run the suite
(see the repo README).
