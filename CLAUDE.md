# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A frozen SPT **4.1.2** modding-API baseline for the MPEX C#-to-Rust port. Compiled 4.1.2 C# mods must keep loading against Rust-backed shim assemblies; this repo holds the contract (frozen DLLs, generated API listings, semantic inventory, a CI gate, and behavioral characterization tests). The six contract assemblies are SPTarkov.Common, SPTarkov.DI, SPTarkov.Reflection, SPTarkov.Server.Assets, SPTarkov.Server.Core, SPTarkov.Server.Web. The repo also freezes a seven-assembly client-side contract under `client/` (spt-common, spt-core, spt-custom, spt-debugging, spt-prepatch, spt-reflection, spt-singleplayer).

## Key invariants

- **The baseline is frozen at 4.1.2 forever.** `server/baseline-dlls/` is immutable. SrvInventoryDumper hard-fails if any contract package resolves to a version other than 4.1.2 (checked via its deps.json, because SPT ships AssemblyVersion 4.1.0.0 for every 4.1.x patch — the NuGet package version is the only 4.1.2 marker).
- **`README.md`, `server/api-surface/`, `server/inventory/`, `server/baseline-dlls/`, `client/api-surface/`, and `client/inventory/` are generated** — never edit by hand. Regenerate with `tools/generate-baseline.sh`; rerunning it must produce a zero git diff.
- **`client/baseline-dlls/` is immutable and was seeded once** — never re-copied, because client builds aren't byte-reproducible. The client DLLs carry a real `AssemblyVersion 4.1.2.0`, and ClientInventoryDumper hard-fails if any of them doesn't.
- **`client/refs/` is git-ignored** and must be populated from a live game install before the client stages can run — see `client/refs/README.md` (`CLIENT_REFS` overrides the location).
- **Compat rule stance:** a shim may ADD public API but never remove or change any (`--strict-mode` off). `--enable-rule-cannot-change-parameter-name` is on because mods call by named argument (source compat, not just binary).

## Commands

```sh
# Behavioral test suite against the frozen baseline
dotnet test tests/SrvBehavioralTests

# Same suite against a Rust-backed shim build
dotnet test tests/SrvBehavioralTests -p:MpexAssemblyDir=/path/to/shims

# Single test
dotnet test tests/SrvBehavioralTests --filter "FullyQualifiedName~DependencyInjectionHandlerTests"

# Client behavioral suite (Unity-free slices) against the frozen baseline / a shim build
dotnet test tests/ClientBehavioralTests
dotnet test tests/ClientBehavioralTests -p:MpexAssemblyDir=/path/to/shims

# API-compat gate (as consuming repos run it)
./ci/check-api-compat.sh <shim-output-dir> ./server/baseline-dlls

# Client API-compat gate (refs dir is mandatory — silent pass otherwise)
./ci/check-api-compat.sh <client-shim-dir> ./client/baseline-dlls ./client/refs

# Full regeneration (needs a 4.1.2 source worktree for route-table scanning;
# SPT_SRC defaults to ~/git/TEMP/spt-412-src)
# Also needs client/refs populated (CLIENT_REFS overrides the location)
SPT_SRC=/path/to/spt-412-src tools/generate-baseline.sh
```

**Gotcha:** wipe `tests/SrvBehavioralTests/{bin,obj}` — and `tests/ClientBehavioralTests/{bin,obj}`, the same applies there — when switching `MpexAssemblyDir` between directories: the csproj copies the assembly closure with `PreserveNewest` and the frozen DLLs have old mtimes, so stale DLLs survive the switch.

**Gotcha:** ApiCompat silently passes (zero diagnostics) if a candidate's references don't resolve — always point it at a directory containing the shims plus their dependency closure.

## Architecture / regeneration pipeline

`tools/generate-baseline.sh` runs six stages:

1. **`tools/SrvBaselineClosure`** — a stub library project pinning the six contract packages to `[4.1.2]`; `dotnet publish` materializes the full DLL closure.
2. Top-level publish DLLs are copied into `server/baseline-dlls/` (skips SPT_Data content and native runtimes).
3. **`dotnet genapi`** (local tool from `.config/dotnet-tools.json`) emits per-assembly C# public-surface listings into `server/api-surface/`. It needs the .NET 10 ref packs under `/usr/share/dotnet/packs/`.
4. **`tools/ClientInventoryDumper`** — dumps `client/inventory/` from the seven frozen client DLLs and runs the `AssemblyVersion 4.1.2.0` freeze check. It uses `MetadataLoadContext` rather than plain reflection loading, because the client assemblies reference Unity and can't be loaded for execution. Needs `client/refs/`.
5. **`dotnet genapi`** over `client/baseline-dlls/` into `client/api-surface/`, resolving references against `client/refs/`.
6. **`tools/SrvInventoryDumper`** — reflection-loads the six server assemblies and dumps `server/inventory/` (assembly identities, `[Injectable]` DI registry, lifecycle interface implementors, HTTP route table scanned from the SPT source worktree) plus the generated two-sided `README.md` index (server + client).

`NuGet.config` clears machine feeds for reproducibility; the `dotnet10-transport` feed exists only because `Microsoft.DotNet.GenAPI.Tool` isn't on nuget.org, and the `BepInEx` feed exists only for the client test suite's `BepInEx.*` packages (both source-mapped).

`tests/SrvBehavioralTests/` and `tests/ClientBehavioralTests/` are xunit **characterization tests** — they pin observed 4.1.2 behavior (server: DI container semantics, MongoId, item extensions; client: the Unity-free slices only), not desired behavior. Either suite runs against the baseline or a shim directory via `MpexAssemblyDir`.

`ci/` is meant to be copied into consuming repos, not run here — see `ci/ADOPTION.md`.

Design and plan docs for this repo live in `docs/superpowers/`.
