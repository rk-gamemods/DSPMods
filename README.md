# Dyson Sphere Program modding workspace

Local source review, game-code capture and offline compatibility checks. No
command here installs a mod or starts the game.

## Prepare

```powershell
git clone https://github.com/andyoneal/SphereOpt.git external/SphereOpt
dotnet tool restore
py -3 tools/game_snapshot.py capture
```

The clone command is only needed when the independent clone is absent. The
capture discovers Steam libraries, snapshots the installed managed DLLs and
decompiles `Assembly-CSharp.dll` using the pinned ILSpy tool. Override discovery
with `--game 'D:\SteamLibrary\steamapps\common\Dyson Sphere Program'`.

Captures are local under `.local/snapshots/`. A manifest identifies every input
assembly by SHA-256, the Steam build and the decompiler version. Unchanged,
verified captures are reused. Failed capture leaves the previous latest pointer
untouched. If a process crashes, an OS-owned file lock is released automatically;
an incomplete staging directory is retained only if cleanup itself fails.

## Validate without launching DSP

```powershell
py -3 tools/game_snapshot.py verify
dotnet build tools/OfflineChecks -c Release
dotnet run --project tools/OfflineChecks -c Release --no-build -- self-test
dotnet build tools/SphereOpt.OfflineBuild -c Release
py -3 tools/game_snapshot.py check-mod .local/build/SphereOpt/SphereOpt.dll
py -3 -m unittest discover -s tests -v
```

The separate offline build project compiles the unchanged SphereOpt source
against captured installed-game assemblies. It does not edit the upstream
project or deploy its output. Its output is analysis material, not a packaged
release with a validated shader bundle.

The initial build attempt reported seven member-access or availability errors.
Whether these require publicized reference assemblies or source changes remains
unresolved; they do not establish runtime incompatibility. Run `check-mod` only
after a successful build produces a current plugin DLL.

If capture uses an explicit game path, pass the same selection to the build:

```powershell
dotnet build tools/SphereOpt.OfflineBuild -c Release '-p:DSPGameDir=D:\SteamLibrary\steamapps\common\Dyson Sphere Program'
```

The build checks that its `DSPManagedDir` is the latest verified snapshot's
managed directory. Overrides pointing at different inputs are rejected.

The metadata checker finds missing game/Unity member references and common
Harmony target/argument mismatches. It does not execute plugin initializers,
apply Harmony patches, validate shader rendering or benchmark FPS. Transpiler
instruction matching requires additional review. See
[workflow and contracts](docs/WORKFLOW.md).

## Sources and decisions

- [Clone/fork and licensing evaluation](docs/LICENSING.md)
- [Task scope and workflow](docs/WORKFLOW.md)
- [Pinned upstream source](sources.json)
- [SphereOpt upstream](https://github.com/andyoneal/SphereOpt)
