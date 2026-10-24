# SphereOpt current-game investigation and update

SphereOpt's shell batching is still relevant to DSP 0.10.35.29151. Vanilla's
simulation improvements do not replace the per-shell rendering work visible in
the installed game's current code. The upstream 0.9.2 release should not be
treated as fully compatible merely because it loads or passes metadata checks:
its shipped shaders contain independently confirmed correctness defects.

The unofficial 0.9.3 update is committed in the independent local clone.
It retains vanilla nodes, frames and painting overlays while batching supported
shell layers, including planned and partly constructed shells. It compiles,
passes the offline acceptance checks below, and is expected to operate with the
assessed current-game assemblies on Direct3D 11. No game files have been changed
or game process run; in-game appearance and performance remain unverified.

The subsequent [installed-stack review](OPTIMIZATION_STACK.md) identifies a
paint-transpiler ordering hazard with DSPOptimizations 1.1.19. The acceptance
record below covers the candidate's isolated offline contracts, not compatibility
with that combined stack. Resolve composition before treating the candidate as
validated for those installed mods.

## Scope and evidence

The user requested a complete investigation, including author statements and
online experience, followed by a functional current-game update unless vanilla
made it unnecessary. Research began on 2026-10-23 and continued on October 24.

The installed game is DSP `0.10.35.29151`, Steam build `25955035`, Unity
`2022.3.62f3c1`. Snapshot identity is
`5faaee93e64501df2b2a123096d70317ebf8c2ec0d7c5309fbd912f47a23f8ff`.
All 95 managed DLLs and all 14,675 reconstructed C# files were captured and
indexed, including deeper Unity managed modules and framework libraries.
Native Unity C++ implementations are outside this coverage.

The SphereOpt baseline is commit
`a06ca9a7bd53d907bea6160eec333b8b30336574`. Its complete inventory includes
33 C# files and 15 shader/include/compute files. All implementation and project
text was indexed separately; shader text uses an approximate parsing grammar.
Binary artifacts have hash catalogs. The final candidate index covers all
122 tracked files: 118 text files and four binaries. Its 73 implementation
files comprise 57 C#, 15 shader/include/compute files and one Python tool.
JCodeMunch contains 106 files and 1,456 symbols; full-text indexing contains
119 documents, including the binary catalog. All 238 sections and 119 full
bodies were retrieved, with 947,865 bytes independently hash-verified.

The final indexes are `andyoneal/SphereOpt` at local candidate HEAD
`7a566ddad93ab58529c02fa3778d929eb747aa85`, and
`local/SphereOpt-fulltext-7a566ddad93a`. The code identifier derives from the
retained upstream remote; its indexed content is this local candidate, not a
claim that upstream published it. The inventory verifier passed with no omitted
implementation or text scope. Python is now explicitly part of that contract.

Review covered the complete mod implementation, relevant current game call
paths, author issues/comments/commits/branches/releases, community reports,
official game changes, and actual compiled shader programs. Merely reading
helper classes or compiling against publicized reference DLLs was insufficient.

## Author and release history

The latest upstream release is [v0.9.2, tag v0.9.2_2](https://github.com/andyoneal/SphereOpt/releases/tag/v0.9.2_2),
published October 3, 2025. Its stated target is DSP `0.10.33.26941`.
The [October 3 compatibility commit](https://github.com/andyoneal/SphereOpt/commit/c3983669e516a2a280c9bc6e4030446f5387fb52)
changes project references rather than the rendering implementation.

[Issue #9](https://github.com/andyoneal/SphereOpt/issues/9) reported breakage after
the September 2025 multithreading update. It closed as completed on October 3,
coinciding with the release, and has no discussion comments. It is evidence of
an addressed historical break, not an unresolved report against today's build.

The [author's README](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/README.md)
describes shell-hexagon instancing and custom node/frame LOD/culling. Its old
5,240-shell benchmark and FPS figures are historical measurements, not results
for the installed build. The same README acknowledges shell gaps and missing
painting-grid support. Ordinary shell colors and style textures are distinct
features and were implemented earlier.

The baseline [renderer TODO](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/InstDysonShellRenderer.cs#L247)
questions resetting properties every frame, while the
[layer deletion code](https://github.com/andyoneal/SphereOpt/blob/a06ca9a7bd53d907bea6160eec333b8b30336574/InstDysonShellLayer.cs)
contains a TODO around clearing removed shell records. These are leads rather
than measurements or compatibility assurances. The update gives allocation
ownership an explicit contract and avoids dirtying unchanged progress/color/state
data; it does not claim a speedup from a comment alone.

Author discussion adds useful regression targets:

| Source | What the author established |
| --- | --- |
| [Issue #2](https://github.com/andyoneal/SphereOpt/issues/2) | Reproduced layer-10 shell loss and announced a 0.8.1 fix. Keep layer 10 in coverage. |
| [Issue #3](https://github.com/andyoneal/SphereOpt/issues/3) | Shell style support was initially missing and later fixed in 0.9.0. This does not establish painting-grid support. |
| [Issue #5](https://github.com/andyoneal/SphereOpt/issues/5) | Acknowledged node orientation trouble; no later resolution appeared in the reviewed discussion. |
| [Issue #8](https://github.com/andyoneal/SphereOpt/issues/8) | Requested saves, mod lists and measurements for a rocket optimization request. Did not claim an implemented rocket optimization. |

The online inventory covered 47 master-history commits, 10 branches, all five
non-PR issues and nine comments, four merged author PRs and their discussion
timelines, and 11 releases. The older experimental branches do not establish a
newer completed fix. No newer released successor, public exact-name fork, author
obsolescence statement or exact-build certification was found. This is a bounded
negative search, not proof that none exists elsewhere.

## What vanilla changed

The [September 29, 2025 multithreading announcement](https://steamcommunity.com/games/1366540/announcements/detail/534362428708750273)
describes substantial simulation scheduling changes, including rockets and
solar-sail projectiles. It does touch sphere-related work. Calling it unrelated
to Dyson spheres would be inaccurate.

The [January 21, 2026 developer log](https://steamcommunity.com/games/1366540/announcements/detail/529869613052199064)
describes reducing completed-sphere per-frame calculations as part of CPU/data
layout improvements. The [January 22 update](https://steamcommunity.com/games/1366540/announcements/detail/529869613052200383)
and [October 15 current-version notes](https://steamcommunity.com/games/1366540/announcements/detail/693149194510139852)
do not establish replacement of SphereOpt's shell batching.

Patch-note silence alone would be weak evidence. The stronger evidence is the
captured current `DysonSphereSegmentRenderer.DrawModels`: nodes and frames are
already instanced, but live shells still receive individual dynamic material
updates and `Graphics.DrawMesh` submissions. Painting overlays are drawn after
each layer. `DysonShell.Construct` increments progress inside the original
method. These current contracts determine the update's integration points.

The resulting conclusion is narrow: simulation optimizations can make other
mods or individual features redundant while the shell draw-submission cost
persists. There is no sound basis for the requested obsolete-mod outcome.

## Community experience and integrations

[TactlessTortoise's March 5, 2026 report](https://www.reddit.com/r/Dyson_Sphere_Program/comments/1rl348p/comment/o8qzt4v/)
describes recovery from 49 to over 100 UPS after adding SphereOpt and reloading
at the same position. A [March 19 follow-up](https://www.reddit.com/r/Dyson_Sphere_Program/comments/1rxyylc/comment/obaoxm5/)
comes from the same person, not an independent replication. Preserve their UPS
label; these are not controlled FPS benchmarks or exact-build tests.

[September 2026 vanilla-player reports](https://www.reddit.com/r/Dyson_Sphere_Program/comments/1wf0nrf/comment/p9iebdg/)
describe severe large-sphere rendering costs. An
[October 14 thread endorsement](https://www.reddit.com/r/Dyson_Sphere_Program/comments/1x5kfyi/comment/pgn70ok/)
still recommends SphereOpt, without a build number or controlled measurements.
These support further development, not a claim that the new candidate is faster.

[DSPOptimizations' changelog](https://thunderstore.io/c/dyson-sphere-program/p/Selsion/DSPOptimizations/)
explicitly removes some optimizations absorbed by vanilla. That is evidence
about those features, not a verdict on SphereOpt's renderer.
[Weaver's compatibility list](https://thunderstore.io/c/dyson-sphere-program/p/Loom/Weaver/)
includes SphereOpt, but a companion author's compatibility entry is not a test
of this fork or the exact installed build.

[NebulaCompatibilityAssist's pinned SphereOpt patch](https://github.com/starfi5h/DSP_Mod_Support/blob/2789a05caf26b4b9a3b026030e6eabea6e0789a7/NebulaCompatibilityAssist/src/Patches/SphereOpt_Patch.cs)
documents stale sphere references and null-player client-join paths. It rebinds
the sphere, skips null-player rendering and suppresses exceptions. Those are
concrete lifecycle test targets. Exception suppression alone does not prove
correct multiplayer drawing. No multiplayer certification is claimed here.

The Thunderstore dependant census remains incomplete: its package header and
fetched listing disagreed, and an attempted bulk census was rejected by command
review for unbounded output capture. Targeted source reads continued. Private
Discord, unpublished saves, non-English discussion and a controlled current-build
A/B benchmark were not available in this investigation.

## Source and binary findings

The original bundle has SHA-256
`da49089c0a6cab76b224685a6c2ee9188928f2579c5a9efd84ddb6eb4621c696`.
It contains three D3D11 graphics shaders and one compute shader, serialized by
Unity 2018.4.12f1. That version header alone does not prove a load failure in
Unity 2022. The reason to repair or avoid programs is their confirmed behavior.

Seven DXBC programs were extracted and disassembled without running them.
Embedded compiler inputs matched eight canonical function bodies after comment
and whitespace normalization. The delivered shell layout is the correct
40-byte structure with texture arrays; the obsolete root shader copy is not
the delivered program's input.

| Finding | Update disposition |
| --- | --- |
| Private fields in raw current-game assemblies | Cached typed Harmony field access; metadata checker now validates field names, types and instance ownership. |
| Non-Append initial LOD buffers; compute over-dispatch | Legacy path improved in source, but custom node/frame/compute rendering is not enabled. Vanilla owns the active path. |
| Nonconservative compute culling | Avoided by retaining vanilla node/frame rendering. |
| Shell/node pole basis | Node replacement is disabled. The delivered shell vertex program uses the vanilla construction basis, including the pole fallback. |
| Frame neighbor out-of-range/stale reads | Custom frame shader is not used. |
| Gamma conversion of node alpha | Custom node shader is not used. Vanilla keeps its current color behavior. |
| Missing painting grid and stencil isolation | Original overlay block remains; owned layer materials set material-level stencil references. |
| Existing-shell style changes | Style postfix synchronizes cached style with bounded effective texture-array indices. |
| Construction progress one update behind | Postfix reads the completed original mutation; capacity arithmetic is bounded and validated. |
| Shell construction-noise cube rounding | Corrected the nonzero-sum repair condition in both canonical source and delivered fragment instructions. Partial construction remains supported. |
| Destructive partial initialization | Validate assets/device first; install no global shader replacements; initialization failure leaves original rendering available. |
| Repeated generation and historical pool growth | Replace before regeneration; compact/rebase progress and polygon ranges on deletion. |
| Cleanup and sphere identity | Actual sphere/layer ownership, lookup-only teardown, idempotent layer release and owned shared-resource cleanup. |
| Brittle paint transpiler | Replace the semantic shell-color field store with a stack-equivalent setter; preserve labels and exception metadata. |
| Missing profiler accounting | Complete vanilla fallback while the game's GPU profiler is enabled. No fabricated visible-instance counters. |

The narrow render seam changes two exact `shellCursor` field loads. Returning 1
for a successfully submitted layer skips only its original shell loop and keeps
the periodic modulo denominator valid. Original node/frame draws, masks,
painting overlays and fallback shell draws remain in the game's method.
Per-call context restores across nested calls and exceptions. Successful earlier
layer submissions remain suppressed if a later layer fails.

## Repaired shader artifact

The delivered bundle is 128,644 bytes, SHA-256
`ea9719ae0307a1bbf07fae22f4c6dff485091a6ab4610065a2ba1883564ba75b`.
Only the shell vertex and fragment programs change. All other five programs
remain byte-exact and are not activated by the node/frame path. The canonical
source owner is the Unity project's `Assets/sphereopt-bundle/shaders/` tree;
the older root shader copies are retained legacy material.

No usable Unity Editor was available. The verified alternative recovers the
public mod bundle's original compiler inputs, invokes the Windows SDK's
[D3DCompile](https://learn.microsoft.com/en-us/windows/win32/api/d3dcompiler/nf-d3dcompiler-d3dcompile),
and removes nondeterministic debug data with
[D3DStripShader](https://learn.microsoft.com/en-us/windows/win32/api/d3dcompiler/nf-d3dcompiler-d3dstripshader).
Unchanged compiler inputs reproduce the original instruction and interface
chunks. Fresh fixed compiles reproduce identical stripped program bytes.

A narrow serializer preserves the existing Unity wrapper, binding tails,
program order, asset pointers and metadata, updating calculated sizes/offsets
and the two program blobs. UnityPy 1.25.4 supplies serialization support. A
separate UnityFS/serialized-v17/raw-LZ4/DXBC reader independently extracts the
result; all seven reflection contracts remain equal. Repeated complete builds
produce the bundle hash above. Canonical function bodies match the actual
compiled input bodies after comment/whitespace normalization. No machine
instructions were patched manually, and no private game source is published.

Startup verifies the byte array's exact identity before passing it to
`AssetBundle.LoadFromMemory`. Missing, old, changed or unsupported assets leave
vanilla rendering available. This prevents using unrestricted construction and
pole geometry with the old program. The recorded source/program identities are
in the clone's `SHADER_PROVENANCE.json`. The workspace shader checker verifies
both delivered copies, the runtime pin and canonical function bodies.

The Unity Editor exporter now uses an explicit four-asset build map, following
the [documented build API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html).
The previous label-only export depended on untracked `.meta` files. Stale
generated Editor manifests were removed. That Editor route was not executed;
an Editor rebuild requires new artifact verification and an updated identity.
Recovered compiler inputs/includes and detailed native build evidence remain
local. Existing shell normal/tangent lighting approximations remain unchanged.

## Validation record

The reviewed mod commit is `7a566ddad93ab58529c02fa3778d929eb747aa85`, tree
`0312adaa128aae37021789978e32a9534b13ffa9`, on `rk/current-game-compat`.
Its owning clone is clean. The workspace publishes its complete source delta in
[SphereOpt-current-game.patch](../patches/SphereOpt-current-game.patch), including
tests and repaired mod assets. Applying it to the pinned baseline reconstructs
that exact Git tree. No hosted SphereOpt fork or public binary release was made.

| Check | Result |
| --- | --- |
| Verified current-game bridge build | Zero warnings, zero errors; raw captured game DLLs. |
| Standalone SphereOpt project against the same DLLs | Zero errors; ten existing vendored analyzer warnings. |
| Game/Unity metadata | 368 references, 16 Harmony targets and two selected instruction contracts, zero errors. |
| Helper/policy suite | 231 passed; repeated unchanged run passed. Includes 13 real bundle identity/corruption cases. |
| Actual layer code through API/lifecycle fakes | 36 cases passed; repeated cleanup, failure injection, compaction and regeneration covered. |
| Actual render prefix/getter/finalizer through fakes | 77 passed with captured-IL case; one case covers 59,049 mask combinations. |
| Captured IL through both real transformers | 586 DrawModels and 789 Paint instructions; exactly two cursor and one color substitutions. All other instructions, operands, 99 labels, locals and six finally-boundary markers preserved. Twelve negative controls pass. |
| Workspace regression tests | 19 passed, including capture/reuse/publication boundaries and complete source/text inventory rejection cases. |
| Metadata checker's fixtures | 14 passed, including missing/reflected fields and supported static Harmony injection. |
| Shader mathematics | Five tests pass, including 3,969 independently checked cube-rounding points and exact candidate-source linkage. |
| Shader/container proof | Both fresh compiled stages and full bundle repeat identically; all seven interfaces and the five untouched programs verified. |
| Independent final review | No open confirmed P1/P2 blockers. Two earlier P2 findings were fixed and regression-checked. |

The mod suites total 344 passing cases, with no failures or skips. The IL tool's
fixture `FieldInfo` adaptation tests the real transformations over captured
metadata; it does not load game types or apply a live Harmony detour. Readiness
and native submission remain source-reviewed rather than end-to-end executed.

The local package is `.local/packages/SphereOpt-0.9.3-7a566ddad93a.zip`,
317,996 bytes, SHA-256
`73510af0718548deab02e1a8a54d44805a4ff4802cecfcaa6ded640bba1f36cd`.
Its DLL SHA-256 is
`12c8aef7a6fb270219b5378235aaeb41f288f4109f0a8629403c2ce38f186593`.
Plugin GUID is `SphereOpt`, version `0.9.3`. It includes the matching bundle,
manifest, update notes, provenance and retained Unlicense/MIT notices. Two
package generations from the same verified workspace inputs produced identical
bytes; ZIP contents were read back and compared. Cross-machine build identity
was not tested.

Use the README's validation commands and `py -3 tools/package_sphereopt.py` to
reproduce the local package. `py -3 tools/verify_sphereopt_patch.py` checks the
published patch against the pinned baseline using an isolated Git index.

No offline check executes the game or establishes native Unity bundle loading,
visual parity, save behavior, multiplayer compatibility or a measured speedup.
Those limits apply even when all build and contract checks pass.

## Next optimization priorities

The [stack review](OPTIMIZATION_STACK.md#combined-patch-risks-to-resolve-first)
adds combined paint/draw patch validation ahead of this proposed CPU work.
Weaver already owns simulation scheduling and Dyson power optimizations;
DSPOptimizations owns vanilla node/frame buffer synchronization. Keep the next
SphereOpt changes within its own shell rendering and buffer state.

On 2026-10-24, the user prioritized CPU overhead reduction and requested that
visibility culling be logged for a later, more ambitious project. These are
follow-up proposals; the assessed mod and its validation record above are
unchanged. The target is rendering CPU time. This can also improve factory
simulation throughput when rendering competes for CPU time, cache or memory
bandwidth, even without changing simulation algorithms. Whether UPS improves
depends on the actual bottleneck and needs measurement.

The first CPU changes should reduce repeated allocation lookups and progress
calculations in shell reconciliation while preserving the current eligibility
decisions and vanilla fallback. Broader structural caching needs demonstrated
invalidation coverage, including changes made by other mods. Diagnostics must
confirm that batching is active: the current render guard bypasses it when
`PerformanceMonitor.GpuProfilerOn` is true.

Changed-range uploads are the next bounded proposal. Start with progress and
shell appearance buffers, whose individual writes currently dirty whole arrays.
Unity 2022.3 provides an
[offset/count SetData overload](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ComputeBuffer.SetData.html).
Track and combine dirty intervals, with full uploads after buffer replacement
or pool compaction and when fragmented changes make small submissions costly.
Audit direct array writes and public dirty flags so no update can be omitted.
Keep public dirty flags as independent force-full-upload signals; a precise
pending range must not hide a simultaneous untracked write marked by that flag.
Retain pending changes after a failed upload. Tests should compare the resulting
buffer contents against full uploads through edits, growth, removals and retries.
This proposal needs no shader or allocation-policy change; actual driver cost
and frame-time benefit still require native measurements.

GPU capacity reuse is deferred from this first pass. It is feasible, but its
safety depends on keeping live draw counts separate from capacity, initializing
every referenced record, preserving buffer bindings and respecting queued-draw
lifetimes during replacement and teardown. Spare capacity also retains more
memory. Source review does not establish freedom from native crashes.

### Deferred: conservative shell visibility culling

Do not implement or activate culling in this first optimization pass. The
existence of a shell on an outer layer does not prove that an inner hex is
occluded. Any future occlusion design must account for actual polygon coverage,
holes, construction progress and rendered opacity, as well as layer visibility
masks and camera position. Shell rotation, inside/outside views, editor and
starmap scaling, and the current shader's distance compression also affect the
result. Frustum rejection and occlusion are separate questions.

The conservative rule is to keep drawing a hex when invisibility is uncertain.
Future acceptance needs image comparisons against the unculled path for
overlapping partial shells, openings, differing shapes, moving layers and all
supported views. A higher layer ID or a complete-shell flag alone is insufficient
evidence of coverage. Preserve this work for a separate design and validation
effort after the CPU and upload changes have been assessed.
