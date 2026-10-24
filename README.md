# Dyson Sphere Program modding workspace

Tools and research for local source review, game-code capture and offline
compatibility checks. Game binaries, reconstructed source and indexes stay
private. No command here installs a mod or starts the game.

The [SphereOpt assessment](docs/SPHEREOPT_ASSESSMENT.md) finds that its rendering
approach remains relevant to DSP 0.10.35.29151. The released 0.9.2 DLL passes
metadata checks; runtime, shader and performance compatibility remain untested.

## Prepare

```powershell
git clone https://github.com/andyoneal/SphereOpt.git external/SphereOpt
dotnet tool restore
py -3 tools/game_snapshot.py capture
```

The clone command is only needed when the independent clone is absent. The
capture discovers Steam libraries, snapshots the installed managed DLLs and
decompiles every managed DLL using the pinned ILSpy tool. Override discovery
with `--game 'D:\SteamLibrary\steamapps\common\Dyson Sphere Program'`.

Captures are local under `.local/snapshots/`. A manifest identifies every input
assembly by SHA-256, the Steam build, decompilation scope and decompiler version.
Unchanged,
verified captures are reused. Failed capture leaves the previous latest pointer
untouched. If a process crashes, an OS-owned file lock is released automatically;
an incomplete staging directory is retained only if cleanup itself fails.

## Validate without launching DSP

```powershell
py -3 tools/game_snapshot.py verify
dotnet build tools/OfflineChecks -c Release
dotnet run --project tools/OfflineChecks -c Release --no-build -- self-test
py -3 -m unittest discover -s tests -v
```

The separate offline build project compiles the unchanged SphereOpt source
against captured installed-game assemblies. It does not edit the upstream
project or deploy its output. Its output is analysis material, not a packaged
release with a validated shader bundle.

Building unchanged SphereOpt against unmodified installed DLLs reports seven
access errors involving six private fields. All six fields still exist. This
differs from the upstream publicized reference setup and does not establish
runtime incompatibility. To inspect the published plugin, run `check-mod` against
its separately downloaded DLL. That released 0.9.2 DLL passed 351 reference and
15 Harmony target checks for the assessed game build.

To reproduce the source-build access errors:

```powershell
dotnet build tools/SphereOpt.OfflineBuild -c Release
```

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

## Index all managed source privately

```powershell
py -3 tools/prepare_code_index.py
py -3 tools/source_coverage.py
```

Use the returned `source_root` with JCodeMunch `index_folder`,
`identity_mode="git"` and `use_ai_summaries=false`. The helper verifies every
copied file against the snapshot and creates an independent local Git identity
with no remote. This avoids merging private source into the public workspace's
index identity. Repeated preparation verifies and reuses the same tree.

Schema 2 stores source per assembly under `source/<assembly-name>/`; older
schema 1 snapshots remain verifiable but contain only the primary game source.
The assessed capture includes all 95 installed managed DLLs, including game
logic, Unity modules and bundled framework libraries. All 14,675 generated C#
files were indexed. The coverage command also checks PE headers of DLLs and EXEs
outside `Managed`; none of those in the assessed installation is managed.

This covers managed implementation and Unity's native-call declarations. It
does not recover Unity's native C++ engine or GPU shader implementations. See
the [coverage and compatibility assessment](docs/SPHEREOPT_ASSESSMENT.md).

## Index SphereOpt source and project text

```powershell
py -3 tools/prepare_sphereopt_index.py --index-code
```

This audits every clone file and prepares a local text projection. Code indexing
uses the configured JCodeMunch Python runtime and public indexing API with a
local copy of its configuration. Global settings and the independent clone stay
unchanged. ShaderLab/HLSL extensions use an approximate C++ grammar: all shader
text is stored, while shader symbol extraction can be incomplete.

Index the returned `text_index_root` with JDocMunch `index_local`, using the
returned `text_index_name`, `use_embeddings=false` and `use_ai_summaries=false`.
This covers every text file, including shader source, Unity settings and scenes,
build configuration, documentation and tracked editor settings. Binary artifacts
have a separate path/size/hash catalog.

Completion requires comparing JCodeMunch's file tree and JDocMunch's document
inventory against `.local/sphereopt-index-coverage.json`. Read back each projected
document's section with hash verification enabled and compression/boilerplate
removal disabled. Record the observed inventories and content-hash results, then
pass that JSON to `--verify-index-inventory`. The verifier rejects missing code,
missing or unexpected text documents, and failed or mismatched content hashes.
The expected observation fields are exercised in
[the coverage tests](tests/test_sphereopt_index.py).

```powershell
py -3 tools/prepare_sphereopt_index.py --verify-index-inventory .local/sphereopt-observed-indexes.json
```

An ordinary default refresh of the clone will omit shader extensions again;
use this workflow for SphereOpt refreshes. See the
[assessed coverage](docs/SPHEREOPT_ASSESSMENT.md#sphereopt-source-index).

## Sources and decisions

- [Clone/fork and licensing evaluation](docs/LICENSING.md)
- [Task scope and workflow](docs/WORKFLOW.md)
- [Pinned upstream source](sources.json)
- [SphereOpt relevance and compatibility assessment](docs/SPHEREOPT_ASSESSMENT.md)
- [SphereOpt upstream](https://github.com/andyoneal/SphereOpt)
