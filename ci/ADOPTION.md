# Adopting the mod-API compatibility gate

The contract: compiled SPT 4.1.2 C# mods must keep loading. Any change to the
public surface of the six contract assemblies (SPTarkov.Common, SPTarkov.DI,
SPTarkov.Reflection, SPTarkov.Server.Assets, SPTarkov.Server.Core,
SPTarkov.Server.Web) can break that. This gate makes such breaks fail PRs.

## Setup in a consuming repo

1. Copy `baseline-dlls/` from mpex-api-compat into your repo. The DLLs are
   frozen at 4.1.2 forever — copies cannot drift.
2. Copy `ci/check-api-compat.sh`.
3. Add the ApiCompat tool to your tool manifest:
   `dotnet new tool-manifest` (if you have none), then
   `dotnet tool install Microsoft.DotNet.ApiCompat.Tool`.
4. Add a PR workflow modeled on `ci/github-workflow-example.yml`: build your
   C#-facing shim assemblies, then run
   `./ci/check-api-compat.sh <your-shim-output-dir> ./baseline-dlls`.

## Reading failures

Each incompatibility prints as a `CP****` diagnostic naming the exact member
(e.g. removed member, changed signature, tightened accessibility). Fix the shim.
If a difference is genuinely intended (rare — it breaks mods), suppress it
explicitly with `--generate-suppression-file` and commit the suppression file
so the decision is reviewable.

## What this does NOT check

Behavior. A shim can be binary-compatible and still behave differently.
Behavioral coverage lives in `tests/BehavioralTests/` — point
`$(MpexAssemblyDir)` at your shim output directory and run the suite
(see the repo README).
