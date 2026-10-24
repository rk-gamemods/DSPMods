# Dyson Sphere Program modding workspace

Tools and research for local source review, game-code capture and offline
compatibility checks. Game binaries, reconstructed source and indexes stay
private. No command here installs a mod or starts the game.

The [current SphereOpt investigation and update](docs/SPHEREOPT_UPDATE.md)
establishes continued value for shell batching in DSP 0.10.35.29151. The local
0.9.3 update builds against captured current-game DLLs and includes repaired
shell programs. It retains vanilla node/frame rendering. Build and offline
contracts do not establish in-game visuals or a performance gain. The
[initial 0.9.2 assessment](docs/SPHEREOPT_ASSESSMENT.md) records the baseline.

The [installed optimization-stack review](docs/OPTIMIZATION_STACK.md) covers
Weaver, DSPOptimizations and BuildToolOpt, their overlap with SphereOpt, and
the combined patch checks needed before further optimization.

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

An already-updated local clone does not need those two commands. The game
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

The separate build project compiles the updated independent clone against
verified, unmodified installed-game DLLs. It never deploys its output. Current
private-field access uses typed cached Harmony accessors rather than publicized
reference assemblies. The upstream baseline's original access failures are
recorded in the initial assessment.

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

The build checks that its `DSPManagedDir` is the latest verified snapshot's
managed directory. Overrides pointing at different inputs are rejected.

The checker finds missing game/Unity members, typed reflected-field mismatches,
Harmony target/argument mismatches and the selected current-game instruction
contracts. Executable tests link real production helpers and layer/context code
through isolated fixtures. These do not execute plugin initializers, apply
patches to the live game, validate shader rendering or benchmark FPS. See
[workflow and contracts](docs/WORKFLOW.md).

The [instruction checker](tools/SphereOpt.InstructionChecks/README.md) runs both
real transformers over the captured game IL without loading the game. It checks
the exact substitutions, surrounding loop/overlay semantics and preserved
labels, locals and finally boundaries.

```powershell
py -3 tools/verify_sphereopt_patch.py
```

That command reconstructs the reviewed tree using an isolated Git index and
leaves the clone's working tree and normal index untouched.

The local package command requires a reviewed, committed, clean clone. It
rebuilds, verifies game inputs, checks plugin identity and shader provenance,
and verifies deterministic ZIP contents. Output remains private under `.local/`.

```powershell
py -3 tools/package_sphereopt.py
```

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
- [Current-game research, fixes and validation](docs/SPHEREOPT_UPDATE.md)
- [SphereOpt upstream](https://github.com/andyoneal/SphereOpt)
