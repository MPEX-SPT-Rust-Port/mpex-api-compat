# client/refs — game-derived reference closure (NOT in git)

The seven frozen client assemblies reference game-derived DLLs that cannot be
committed to this repo. Everything in this directory except this README is
git-ignored. Populate it from a live SPT 4.1.2 game install before running
`tools/generate-baseline.sh` or the client ApiCompat gate:

```sh
GAME_DIR=/path/to/your/SPT-4.1.2-install   # contains EscapeFromTarkov_Data/ and BepInEx/
cp "$GAME_DIR"/EscapeFromTarkov_Data/Managed/*.dll client/refs/
cp "$GAME_DIR"/BepInEx/core/*.dll client/refs/
```

Copy the **whole** Managed directory, not a subset — the tooling needs
`mscorlib.dll`, `netstandard.dll`, `Assembly-CSharp.dll`, every
`UnityEngine.*` module, `Comfort.dll`, `FilesChecker.dll`, `bsg.*.dll`, and
their transitive references, and it hard-fails when resolution is incomplete.
`BepInEx/core/` supplies `BepInEx.dll`, `0Harmony.dll`, and `Mono.Cecil.dll`.

Any `spt-*.dll` that lands here (a live install ships them) is ignored by the
tooling: the frozen copies in `client/baseline-dlls/` always win.
