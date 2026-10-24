# SphereOpt assessment for DSP 0.10.35.29151

Assessed 2026-10-23. SphereOpt's rendering approach remains relevant to the
installed game. Its released 0.9.2 DLL passes the workspace's metadata checks.
This supports further runtime testing; it does not establish that the mod is
fully compatible or faster on the current game.

## Assessed inputs

- Installed DSP: `0.10.35.29151`, Steam build `25955035`. The installed
  `Updates/Versions.txt` ends with the 2026-10-15 release, matching the
  [developer's current announcement](https://steamcommunity.com/app/1366540).
- Installed Unity player: `2022.3.62f3c1 (1623fc0bbb97)`, read from
  `UnityPlayer.dll` version metadata.
- SphereOpt source: upstream commit
  [`a06ca9a7bd53d907bea6160eec333b8b30336574`](https://github.com/andyoneal/SphereOpt/tree/a06ca9a7bd53d907bea6160eec333b8b30336574),
  also the upstream `master` tip when checked. The independent clone is clean.
- Released plugin: Andy/SphereOpt `0.9.2` from Thunderstore. The
  [version listing](https://thunderstore.io/c/dyson-sphere-program/p/Andy/SphereOpt/versions/)
  still lists 0.9.2 as latest; its declared game support is 0.10.33.26941.
- Input hashes and the exact private snapshot ID are recorded in
  [sources.json](../sources.json). Game inputs, reconstructed source, the release
  package and generated inspection reports remain under ignored `.local/`.

## What was decompiled and indexed

The capture includes **95 of 95 managed DLLs** in the installed `Managed`
directory. ILSpy generated a separate project for every assembly, including
`Assembly-CSharp`, Unity's engine modules, Unity.Mathematics, Unity.Timeline,
the bundled .NET/Mono libraries and XGamingRuntime. It reconstructs game
simulation and rendering implementation, rather than just helper APIs.

The coverage scan also inspected PE headers of all DLLs and EXEs outside
`Managed`. Those 12 binaries were native, including `DSPGAME.exe`,
`UnityPlayer.dll`, the Mono runtime and platform integration libraries. No
additional managed binary was found in that scan.

JCodeMunch indexed all **14,675 generated C# files** and extracted **258,085
symbols**. The index handle is `local/DSPManaged-5faaee93e645`. Its 14,773 indexed
files also include 98 XML/project files. No C# files were excluded for size,
readability, ignore rules or file limits. There are 97 indexed files without
extracted symbols, mostly assembly attributes; these remain available as text.
Eighteen non-code resource files were outside the index's supported extensions.
XML import extraction is unavailable, so cross-file relationship analysis is
less complete than C# source retrieval. Spot checks retrieved both the game's
renderer and Unity's deeper `Graphics` and `Matrix4x4` implementation.

This is complete coverage of the installed managed DLL set, not the complete
engine implementation. Unity's managed modules contain both C# bodies and
declarations for native calls. ILSpy cannot recover the C++ behind those calls,
the native platform libraries, or GPU shader implementations as C#. That
additional reverse engineering is unnecessary for this initial assessment;
native behavior and shader compatibility remain runtime questions.
Decompiled C# is a reconstruction. Captured assembly bytes are authoritative.

## Why SphereOpt still has work to do

In the current game's `DysonSphereSegmentRenderer.DrawModels`, shells are still
walked individually and submitted through `Graphics.DrawMesh`. SphereOpt
[replaces this method](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/Patch_DysonSphereSegmentRenderer.cs#L35)
and [draws shell hexagons by layer](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/InstDysonShellRenderer.cs#L172).
The current game has therefore not subsumed that batching approach.

The current game already uses indirect instancing for nodes and frames.
SphereOpt adds its own mesh LOD and compute-shader culling to that path.
The game's segment record remains 44 bytes with the fields consumed by
SphereOpt's
[compute shader](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/unity/sphereopt-bundle/Assets/sphereopt-bundle/shaders/Frame%20LOD.compute).
These source observations establish continuing relevance, not a measured
performance benefit on this version or machine.

## Compatibility evidence

The published DLL returned `PASS_WITH_LIMITATIONS`: **351 game/Unity reference
checks, 15 Harmony patch targets, zero reported errors**. The checker resolves
types and members and inspects supported patch declarations and injected
arguments. It does not enforce C# visibility or execute the plugin.

An unchanged source build against the raw installed assemblies fails with
seven diagnostics involving six private fields: `DysonShell.polyn`,
`clockwise`, `center`, `radius`, `DysonSphereSegmentRenderer.instMats` and
`Matrix4x4.identityMatrix`. All six still exist with the expected types. These
errors do not demonstrate API removal. Inspection of the exact
[packaged game and Unity references used upstream](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/SphereOpt.csproj#L26)
confirmed that all six fields are public there: the five game fields in
`DysonSphereProgram.GameLibs` 0.10.33.26513-r.0, and `identityMatrix` in
`UnityEngine.Modules` 2022.3.53. The raw installed DLLs keep those fields private.
Restoring equivalent publicized references for the current game, or changing
field access, would be necessary for a local rebuild. No reference assembly or
SphereOpt source was modified here, and a rebuild using those alternatives
was not attempted.

The paint transpiler was inspected separately. Current
`UIDysonBrush_Paint._OnUpdate` contains one `DysonShell.color` store at IL
`0x077b`. Twelve instructions earlier is `ldloc.s V_40`, whose local type is
`DysonShell`, matching the
[transpiler's fixed-offset assumption](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/Patch_DysonShell.cs#L134).
This verifies the static anchor and local type, not Harmony application or
interactions with other transpilers.

## Remaining limitations

The [upstream README](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/README.md)
documents shell gaps and an unsupported painting grid. The current game's
renderer still draws a painting overlay inside the method SphereOpt replaces;
that limitation remains relevant. The replacement also bypasses GPU-work
profiling calls inside the original method, so built-in GPU-work counters
cannot be assumed equivalent while the replacement runs.

The released asset bundle's UnityFS header identifies Unity `2018.4.12f1`,
matching the upstream Unity project's version file. The installed player is
Unity 2022.3. This version difference is a test requirement, not evidence that
the bundle necessarily fails to load. No asset-bundle loading, shader
compilation, rendering, save round-trip or performance test was performed.

Keep SphereOpt as a candidate for large-sphere rendering optimization. Do not
label it obsolete on the evidence reviewed here, or label it runtime-verified.
A separately authorized runtime trial should cover all rendering views,
shell construction/removal and colors, layer masks, painting behavior,
save/reload, and comparisons with identical saves and graphics settings.

## Reproduce the offline checks

Run the preparation and validation commands in [README.md](../README.md).
Download the exact released package linked in [sources.json](../sources.json)
into `.local/assessment/`, verify its hash, and extract it there. Then run:

```powershell
py -3 tools/game_snapshot.py check-mod .local/assessment/SphereOpt-0.9.2/SphereOpt.dll
py -3 tools/source_coverage.py
py -3 tools/prepare_code_index.py
```

For individual field metadata or IL, the offline inspector supports:

```text
dotnet run --project tools/OfflineChecks -c Release --no-build -- describe ASSEMBLY_PATH TYPE_NAME
dotnet run --project tools/OfflineChecks -c Release --no-build -- instructions ASSEMBLY_PATH TYPE_NAME METHOD_NAME
```

Use the verified snapshot's `Managed/Assembly-CSharp.dll` with type
`UIDysonBrush_Paint` and method `_OnUpdate` for the transpiler inspection.
The published workspace contains no game source excerpts or game binaries.
