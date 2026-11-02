# Dyson Sphere Program modding workspace

Tools for local source review, game-code capture and offline compatibility
checks. Game binaries, reconstructed source and indexes stay private. No command
here installs a mod or starts the game.

The local SphereOpt 0.9.3 update builds against captured current-game DLLs and
retains vanilla node/frame rendering. Build and offline contracts do not
establish in-game visuals or a performance gain.

[sources.json](sources.json) pins the reviewed upstream source.
[docs/LICENSING.md](docs/LICENSING.md) covers clone, fork and licence questions.

## Prepare

```powershell
git clone https://github.com/andyoneal/SphereOpt.git external/SphereOpt
dotnet tool restore
py -3 tools/game_snapshot.py capture
```

The clone command is only needed when the independent clone is absent. The
upstream checkout is the old baseline. For a fresh clone, apply the reviewed
update before running the current mod checks:

```powershell
git -C external/SphereOpt switch -c rk/current-game-compat a06ca9a7bd53d907bea6160eec333b8b30336574
git -C external/SphereOpt am ../../patches/SphereOpt-current-game.patch
```

Override game discovery with
`--game 'D:\SteamLibrary\steamapps\common\Dyson Sphere Program'`. Captures are
local under `.local/snapshots/`.

## Validate without launching DSP

```powershell
py -3 tools/game_snapshot.py verify
dotnet build tools/OfflineChecks -c Release
dotnet run --project tools/OfflineChecks -c Release --no-build -- self-test
py -3 -m unittest discover -s tests -v
```

Build and test the updated clone against verified game DLLs. The build never
deploys its output.

```powershell
dotnet build tools/SphereOpt.OfflineBuild -c Release
dotnet run --project external/SphereOpt/tests/SphereOpt.Tests -c Release
dotnet run --project external/SphereOpt/tests/SphereOpt.LayerTests -c Release
dotnet run --project external/SphereOpt/tests/SphereOpt.RenderTests -c Release
py -3 tools/check_sphereopt_shaders.py
py -3 tools/game_snapshot.py check-mod .local/build/SphereOpt/SphereOpt.dll
```

If capture uses an explicit game path, pass the same selection to the build:

```powershell
dotnet build tools/SphereOpt.OfflineBuild -c Release '-p:DSPGameDir=D:\SteamLibrary\steamapps\common\Dyson Sphere Program'
```

These checks do not execute plugin initializers, apply patches to the live game,
validate shader rendering or benchmark FPS.

Run the instruction checker over the captured game IL. It loads neither the game
nor any plugin.

```powershell
$project = (Resolve-Path 'tools/SphereOpt.InstructionChecks/SphereOpt.InstructionChecks.csproj').Path
$assets = Join-Path (Split-Path -Parent $project) 'obj/project.assets.json'
$buildRoot = Join-Path (Get-Location).Path '.local/build/InstructionChecks'
dotnet build $project --no-restore --configuration Release --nologo --verbosity minimal `
  "-p:BaseOutputPath=$buildRoot/bin/" `
  "-p:BaseIntermediateOutputPath=$buildRoot/obj/" `
  "-p:ProjectAssetsFile=$assets" -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Instruction checker build failed' }
$checker = Join-Path $buildRoot 'bin/Release/net8.0/SphereOpt.InstructionChecks.dll'
$snapshotProps = [xml](Get-Content -Raw -LiteralPath '.local/GameSnapshot.props')
$capturedDll = Join-Path $snapshotProps.Project.PropertyGroup.DSPManagedDir 'Assembly-CSharp.dll'
dotnet $checker --assembly $capturedDll --report .local/build/InstructionChecks/stack-proof.json
```

To check a candidate, add `--plugin <candidate DLL>` and
`--dsp-optimizations <installed DLL>`, then rerun the process boundary checks:

```powershell
py -3 tools/SphereOpt.InstructionChecks/run-regressions.py --checker $checker `
  --assembly $capturedDll --plugin $candidateDll --dsp-optimizations $dspoDll
py -3 tools/SphereOpt.InstructionChecks/test_regression_runner.py
```

Check the published patch against the pinned baseline. The command leaves the
clone's working tree and normal index untouched.

```powershell
py -3 tools/verify_sphereopt_patch.py
```

Package a reviewed, committed, clean clone. Output stays private under `.local/`.

```powershell
py -3 tools/package_sphereopt.py
```

## Index all managed source privately

```powershell
py -3 tools/prepare_code_index.py
py -3 tools/source_coverage.py
```

Use the returned `source_root` with JCodeMunch `index_folder`,
`identity_mode="git"` and `use_ai_summaries=false`. The helper creates an
independent local Git identity with no remote, which keeps private source out of
the public workspace's index identity.

This covers managed implementation and Unity's native-call declarations. It does
not recover Unity's native C++ engine or GPU shader implementations.

## Index SphereOpt source and project text

```powershell
py -3 tools/prepare_sphereopt_index.py --index-code
```

Index the returned `text_index_root` with JDocMunch `index_local`, using the
returned `text_index_name`, `use_embeddings=false` and `use_ai_summaries=false`.

Completion requires comparing JCodeMunch's file tree and JDocMunch's document
inventory against `.local/sphereopt-index-coverage.json`. Read back each projected
document's section with hash verification enabled and compression/boilerplate
removal disabled. Record the observed inventories and content-hash results, then
pass that JSON to `--verify-index-inventory`. The expected observation fields are
exercised in [the coverage tests](tests/test_sphereopt_index.py).

```powershell
py -3 tools/prepare_sphereopt_index.py --verify-index-inventory .local/sphereopt-observed-indexes.json
```

An ordinary default refresh of the clone omits shader extensions, so use this
workflow for SphereOpt refreshes.
